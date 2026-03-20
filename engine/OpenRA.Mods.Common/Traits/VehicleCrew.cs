#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Manages virtual crew slots on a vehicle. Crew eject on critical damage and their absence disables vehicle systems via conditions.")]
	public class VehicleCrewInfo : TraitInfo, Requires<IHealthInfo>
	{
		[FieldLoader.Require]
		[Desc("Crew slot names, e.g. Driver, Gunner, Commander.")]
		public readonly string[] CrewSlots = Array.Empty<string>();

		[FieldLoader.Require]
		[Desc("Map of slot name to actor type to spawn on ejection.")]
		public readonly Dictionary<string, string> CrewActors = new Dictionary<string, string>();

		[FieldLoader.Require]
		[GrantedConditionReference]
		[Desc("Map of slot name to condition granted while that slot is occupied.")]
		public readonly Dictionary<string, string> SlotConditions = new Dictionary<string, string>();

		[Desc("Order in which crew eject. First entry ejects first.")]
		public readonly string[] EjectionOrder = null;

		[Desc("Base ticks between each crew ejection.")]
		public readonly int EjectionDelay = 25;

		[Desc("Random ± variance added to ejection delay.")]
		public readonly int EjectionDelayVariance = 10;

		[Desc("Damage state that triggers crew ejection.")]
		public readonly DamageState EjectionDamageState = DamageState.Critical;

		[Desc("Percent chance each crew member survives ejection on vehicle death.")]
		public readonly int EjectionSurvivalRate = 90;

		[Desc("Whether ejected crew inherit the vehicle's veterancy rank.")]
		public readonly bool TransferVeterancy = true;

		public override object Create(ActorInitializer init) { return new VehicleCrew(init.Self, this); }
	}

	public class VehicleCrew : INotifyCreated, INotifyDamageStateChanged, ITick, INotifyKilled
	{
		readonly Actor self;
		readonly VehicleCrewInfo info;
		readonly string[] ejectionOrder;

		readonly bool[] slotOccupied;
		readonly int[] conditionTokens;
		readonly Dictionary<string, int> slotIndexByName = new Dictionary<string, int>();

		IHealth health;

		[Sync]
		int ejectionCountdown;

		[Sync]
		int nextEjectionIndex;

		[Sync]
		bool ejecting;

		public VehicleCrew(Actor self, VehicleCrewInfo info)
		{
			this.self = self;
			this.info = info;

			slotOccupied = new bool[info.CrewSlots.Length];
			conditionTokens = new int[info.CrewSlots.Length];

			for (var i = 0; i < info.CrewSlots.Length; i++)
			{
				slotIndexByName[info.CrewSlots[i]] = i;
				conditionTokens[i] = Actor.InvalidConditionToken;
			}

			// Use custom ejection order or default to reverse of CrewSlots
			ejectionOrder = info.EjectionOrder ?? info.CrewSlots.Reverse().ToArray();
		}

		void INotifyCreated.Created(Actor self)
		{
			health = self.Trait<IHealth>();

			// All crew start present — grant all conditions
			for (var i = 0; i < info.CrewSlots.Length; i++)
			{
				slotOccupied[i] = true;
				var slotName = info.CrewSlots[i];
				if (info.SlotConditions.TryGetValue(slotName, out var condition))
					conditionTokens[i] = self.GrantCondition(condition);
			}
		}

		void INotifyDamageStateChanged.DamageStateChanged(Actor self, AttackInfo e)
		{
			if (e.DamageState >= info.EjectionDamageState && e.PreviousDamageState < info.EjectionDamageState)
			{
				// Entered critical — start ejecting
				if (!ejecting && HasOccupiedCrewToEject())
				{
					ejecting = true;
					nextEjectionIndex = 0;
					AdvanceToNextOccupiedSlot();
					if (ejecting)
						ejectionCountdown = info.EjectionDelay + self.World.SharedRandom.Next(-info.EjectionDelayVariance, info.EjectionDelayVariance + 1);
				}
			}
			else if (e.DamageState < info.EjectionDamageState && e.PreviousDamageState >= info.EjectionDamageState)
			{
				// Repaired out of critical — stop ejecting
				ejecting = false;
			}
		}

		void ITick.Tick(Actor self)
		{
			if (!ejecting || self.IsDead)
				return;

			if (--ejectionCountdown > 0)
				return;

			// Eject current crew member
			var slotName = ejectionOrder[nextEjectionIndex];
			EjectCrewMember(slotName, false);

			// Advance to next
			nextEjectionIndex++;
			AdvanceToNextOccupiedSlot();

			if (!ejecting)
				return;

			ejectionCountdown = info.EjectionDelay + self.World.SharedRandom.Next(-info.EjectionDelayVariance, info.EjectionDelayVariance + 1);
		}

		void INotifyKilled.Killed(Actor self, AttackInfo e)
		{
			// Eject all remaining crew instantly on death
			foreach (var slotName in ejectionOrder)
			{
				if (!slotIndexByName.TryGetValue(slotName, out var idx))
					continue;

				if (slotOccupied[idx])
					EjectCrewMember(slotName, true);
			}

			ejecting = false;
		}

		void EjectCrewMember(string slotName, bool onDeath)
		{
			if (!slotIndexByName.TryGetValue(slotName, out var idx))
				return;

			if (!slotOccupied[idx])
				return;

			// Revoke condition
			slotOccupied[idx] = false;
			if (conditionTokens[idx] != Actor.InvalidConditionToken)
				conditionTokens[idx] = self.RevokeCondition(conditionTokens[idx]);

			// Check survival on death
			if (onDeath && self.World.SharedRandom.Next(100) >= info.EjectionSurvivalRate)
				return;

			if (!info.CrewActors.TryGetValue(slotName, out var actorType))
				return;

			var td = new TypeDictionary
			{
				new OwnerInit(self.Owner),
				new LocationInit(self.Location),
			};

			// Transfer veterancy — pass level-mapped XP so crew gets same rank
			// Crew actors use ExperienceModifier: 1 with thresholds 100/200/400/800
			if (info.TransferVeterancy)
			{
				var ge = self.TraitOrDefault<GainsExperience>();
				if (ge != null && ge.Level > 0)
				{
					// Map vehicle level to crew XP thresholds: level 1→100, 2→200, 3→400, 4→800
					var levelXpMap = new[] { 0, 100, 200, 400, 800 };
					var xpToGrant = ge.Level < levelXpMap.Length ? levelXpMap[ge.Level] : levelXpMap[levelXpMap.Length - 1];
					var geInfo = self.Info.TraitInfoOrDefault<GainsExperienceInfo>();
					if (geInfo != null)
						td.Add(new ExperienceInit(geInfo, xpToGrant));
				}
			}

			self.World.AddFrameEndTask(w =>
			{
				var crew = w.CreateActor(actorType, td);
				var positionable = crew.TraitOrDefault<IPositionable>();
				if (positionable != null)
				{
					positionable.SetPosition(crew, self.Location);

					if (!positionable.CanEnterCell(self.Location, crew, BlockedByActor.None))
					{
						// Try adjacent cells
						var placed = false;
						foreach (var cell in w.Map.FindTilesInAnnulus(self.Location, 1, 2))
						{
							if (positionable.CanEnterCell(cell, crew, BlockedByActor.None))
							{
								positionable.SetPosition(crew, cell);
								placed = true;
								break;
							}
						}

						if (!placed)
							crew.Kill(crew);
					}
				}

				// Nudge out of the way
				var nbms = crew.TraitsImplementing<INotifyBlockingMove>();
				foreach (var nbm in nbms)
					nbm.OnNotifyBlockingMove(crew, crew);
			});
		}

		void AdvanceToNextOccupiedSlot()
		{
			while (nextEjectionIndex < ejectionOrder.Length)
			{
				var slotName = ejectionOrder[nextEjectionIndex];
				if (slotIndexByName.TryGetValue(slotName, out var idx) && slotOccupied[idx])
					return;

				nextEjectionIndex++;
			}

			// No more crew to eject
			ejecting = false;
		}

		bool HasOccupiedCrewToEject()
		{
			foreach (var slotName in ejectionOrder)
			{
				if (slotIndexByName.TryGetValue(slotName, out var idx) && slotOccupied[idx])
					return true;
			}

			return false;
		}

		// Public API for Phase 2 (crew re-entry)
		public bool HasEmptySlot(string role)
		{
			if (!slotIndexByName.TryGetValue(role, out var idx))
				return false;

			return !slotOccupied[idx];
		}

		public bool CanAcceptRole(string role)
		{
			return HasEmptySlot(role);
		}

		public void FillSlot(string role)
		{
			if (!slotIndexByName.TryGetValue(role, out var idx))
				return;

			if (slotOccupied[idx])
				return;

			slotOccupied[idx] = true;
			var slotName = info.CrewSlots[idx];
			if (info.SlotConditions.TryGetValue(slotName, out var condition))
				conditionTokens[idx] = self.GrantCondition(condition);
		}

		public bool IsSlotOccupied(string role)
		{
			if (!slotIndexByName.TryGetValue(role, out var idx))
				return false;

			return slotOccupied[idx];
		}

		public IEnumerable<string> EmptySlots
		{
			get
			{
				for (var i = 0; i < info.CrewSlots.Length; i++)
				{
					if (!slotOccupied[i])
						yield return info.CrewSlots[i];
				}
			}
		}
	}
}
