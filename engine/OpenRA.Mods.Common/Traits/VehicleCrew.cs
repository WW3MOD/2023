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
		public readonly int EjectionDelay = 15;

		[Desc("Random ± variance added to ejection delay.")]
		public readonly int EjectionDelayVariance = 5;

		[Desc("Additional delay (ticks) after the vehicle comes to a full stop before the first crew member ejects.")]
		public readonly int PostStopDelay = 20;

		[Desc("Maximum ticks to wait for the vehicle to stop after entering critical. Eject anyway after this.")]
		public readonly int StopTimeout = 25;

		[Desc("Damage state that triggers crew ejection. Heavy = HP <50% (crew bails when severely " +
			"damaged); Critical = HP <25% (only when nearly dead). Heavy gets crew out fast enough that " +
			"the ChangesHealth bleed during Heavy isn't a long staring window.")]
		public readonly DamageState EjectionDamageState = DamageState.Heavy;

		// EjectionSurvivalRate removed — vehicle death is now total loss for
		// anyone still inside (crew has to evac during the bleed-out window
		// or stay with the wreck). Existing per-actor YAML overrides can be
		// kept harmlessly; loader ignores unknown fields under TraitInfo.

		[Desc("Whether ejected crew inherit the vehicle's veterancy rank.")]
		public readonly bool TransferVeterancy = true;

		[Desc("Fraction of vehicle MaxHP the finishing shot must exceed before crew start taking damage. E.g. 25 = 25%.")]
		public readonly int CrewDamageThresholdPercent = 25;

		[Desc("Random variance on crew damage, as a fraction of crew MaxHP (1/N). Set to 0 to disable.")]
		public readonly int CrewDamageVarianceDivisor = 5;

		[Desc("Offset applied to the wreck's onfire stack count when crew inherits its burn intensity. " +
			"Negative values mean the crew burns less than the wreck (typical: a tank at stack 8 with " +
			"offset -3 spawns crew at stack 5). Clamped to 0 minimum.")]
		public readonly int CrewFireStackOffset = -3;

		public override object Create(ActorInitializer init) { return new VehicleCrew(init.Self, this); }
	}

	public class VehicleCrew : INotifyCreated, INotifyDamageStateChanged, ITick, INotifyKilled
	{
		readonly Actor self;
		readonly VehicleCrewInfo info;
		readonly string[] ejectionOrder;

		readonly bool[] slotOccupied;
		readonly bool[] slotReserved;
		readonly int[] conditionTokens;
		readonly Dictionary<string, int> slotIndexByName = new Dictionary<string, int>();

		IHealth health;
		Mobile mobile;

		// Damage value of the hit that pushed the vehicle into critical state.
		// Locked in on the critical transition and used to scale crew damage.
		int finishingDamage;

		// Between entering critical and beginning the eject countdown, we wait
		// for the vehicle to stop (or StopTimeout to expire).
		[Sync]
		bool waitingForStop;

		[Sync]
		int stopWaitCounter;

		/// <summary>Whether non-allied crew can currently enter this vehicle (e.g., crash-disabled helicopter).
		/// Set by HeliEmergencyLanding on safe landing to allow capture-by-pilot.</summary>
		public bool AllowForeignCrew { get; set; }

		/// <summary>When true, crew ejection is suppressed (e.g., critical crash — everyone dies).
		/// Set by HeliEmergencyLanding when suppress-eject condition is active.</summary>
		public bool SuppressEjection { get; set; }

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
			slotReserved = new bool[info.CrewSlots.Length];
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
			mobile = self.TraitOrDefault<Mobile>();

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
					finishingDamage = e.Damage?.Value ?? 0;
					nextEjectionIndex = 0;
					AdvanceToNextOccupiedSlot();
					if (ejecting)
					{
						if (mobile != null)
						{
							// Ground vehicle: wait until it rolls to a stop before ejecting.
							waitingForStop = true;
							stopWaitCounter = 0;
						}
						else
						{
							// No Mobile (e.g. aircraft routed here somehow): preserve legacy timing.
							waitingForStop = false;
							ejectionCountdown = info.EjectionDelay + self.World.SharedRandom.Next(-info.EjectionDelayVariance, info.EjectionDelayVariance + 1);
						}
					}
				}
			}
			else if (e.DamageState < info.EjectionDamageState && e.PreviousDamageState >= info.EjectionDamageState)
			{
				// Repaired out of critical — stop ejecting
				ejecting = false;
				waitingForStop = false;
				finishingDamage = 0;
			}
		}

		void ITick.Tick(Actor self)
		{
			if (!ejecting || self.IsDead || SuppressEjection)
				return;

			if (waitingForStop)
			{
				stopWaitCounter++;

				// Vehicle is stopped when it has no horizontal or vertical velocity.
				// If there's no Mobile trait we treat it as stopped (defensive; should have been
				// routed through the legacy path in DamageStateChanged already).
				var stopped = mobile == null
					|| (mobile.CurrentMovementTypes & (MovementType.Horizontal | MovementType.Vertical)) == MovementType.None;

				if (stopped || stopWaitCounter >= info.StopTimeout)
				{
					waitingForStop = false;
					ejectionCountdown = info.PostStopDelay
						+ self.World.SharedRandom.Next(-info.EjectionDelayVariance, info.EjectionDelayVariance + 1);
				}

				return;
			}

			if (--ejectionCountdown > 0)
				return;

			// Eject current crew member
			var slotName = ejectionOrder[nextEjectionIndex];
			EjectCrewMember(slotName);

			// Advance to next
			nextEjectionIndex++;
			AdvanceToNextOccupiedSlot();

			if (!ejecting)
				return;

			ejectionCountdown = info.EjectionDelay + self.World.SharedRandom.Next(-info.EjectionDelayVariance, info.EjectionDelayVariance + 1);
		}

		void INotifyKilled.Killed(Actor self, AttackInfo e)
		{
			// Death = total loss for anyone still inside. The eject window is
			// the bleed-out time between Critical state and HP=0; crew that
			// didn't make it out by then is consumed by the wreck. This trait
			// runs the staged ejection during Critical state — once Killed
			// fires, we just clean up the remaining slot bookkeeping. No
			// post-death actor spawn, no second-chance survival roll.
			ejecting = false;
			for (var i = 0; i < info.CrewSlots.Length; i++)
			{
				slotOccupied[i] = false;
				slotReserved[i] = false;
				if (conditionTokens[i] != Actor.InvalidConditionToken)
					conditionTokens[i] = self.RevokeCondition(conditionTokens[i]);
			}
		}

		void EjectCrewMember(string slotName)
		{
			if (!slotIndexByName.TryGetValue(slotName, out var idx))
				return;

			if (!slotOccupied[idx])
				return;

			// Revoke condition
			slotOccupied[idx] = false;
			if (conditionTokens[idx] != Actor.InvalidConditionToken)
				conditionTokens[idx] = self.RevokeCondition(conditionTokens[idx]);

			if (!info.CrewActors.TryGetValue(slotName, out var actorType))
				return;

			// Damage inherited from the finishing shot. The crew that
			// triggered Critical state takes a fraction of that damage on
			// the way out — big hits leave them either dead inside (skip
			// spawn) or stumbling out at low HP (prone-slow afterwards).
			var crewDamage = 0;
			if (finishingDamage > 0)
			{
				var vehicleMaxHP = health.MaxHP;
				var threshold = vehicleMaxHP * info.CrewDamageThresholdPercent / 100;
				if (finishingDamage > threshold && vehicleMaxHP > 0)
				{
					var crewMaxHP = CrewMaxHPFromRules(actorType);
					crewDamage = crewMaxHP * (finishingDamage - threshold) / vehicleMaxHP;
					if (info.CrewDamageVarianceDivisor > 0 && crewMaxHP / info.CrewDamageVarianceDivisor > 0)
						crewDamage += self.World.SharedRandom.Next(crewMaxHP / info.CrewDamageVarianceDivisor);

					// Would-be-lethal: crew dies inside the vehicle, no actor spawned.
					if (crewDamage >= crewMaxHP)
						return;
				}
			}

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

			// Compute the vehicle's current fire intensity (onfire stacks). Crew
			// emerging from a wreck inherits the wreck's stack count plus
			// CrewFireStackOffset (default -3 — crew burns less than the
			// vehicle they came out of). Pure HP-fraction math; no need to
			// poke the trait's internal state.
			var fireStacks = 0;
			string fireCondition = null;
			var fireInfo = self.Info.TraitInfoOrDefault<GrantStackingConditionOnHealthFractionInfo>();
			if (fireInfo != null && fireInfo.MaxStacks > 0 && health.MaxHP > 0)
			{
				var percent = (health.HP * 100) / health.MaxHP;
				var raw = GrantStackingConditionOnHealthFraction.CalculateStacks(
					percent, fireInfo.StartFraction, fireInfo.EndFraction, fireInfo.MaxStacks);
				fireStacks = Math.Max(0, raw + info.CrewFireStackOffset);
				fireCondition = fireInfo.Condition;
			}

			var damageToApply = crewDamage;
			var spawnLocation = self.Location;
			self.World.AddFrameEndTask(w =>
			{
				// Spawn at the husk's centre cell — visually the crew emerges
				// from the vehicle's hatch — then queue a Nudge so they
				// immediately walk to an adjacent cell. SetPosition bypasses
				// CanEnterCell so the cell-occupancy check in the husk doesn't
				// block placement. If there's truly no adjacent free cell the
				// Nudge no-ops and the crew stays on the husk; that's the
				// realistic "couldn't get clear" outcome for a wrecked vehicle
				// boxed in by other husks.
				var crew = w.CreateActor(false, actorType, td);
				var positionable = crew.TraitOrDefault<IPositionable>();
				if (positionable == null)
				{
					crew.Dispose();
					return;
				}

				positionable.SetPosition(crew, spawnLocation);
				w.Add(crew);

				// Inherit the wreck's burn intensity. ^Infantry already includes
				// ^InfantryAffectedByFire which registers the `onfire` external
				// condition (TotalCap 10) plus visual overlays + per-stack bleed,
				// so granting N tokens here lights up the crew at the same fire
				// stage the vehicle was emitting and starts them bleeding at the
				// matching rate.
				if (fireStacks > 0 && fireCondition != null)
				{
					for (var i = 0; i < fireStacks; i++)
						crew.GrantCondition(fireCondition);
				}

				// Apply finishing-shot damage BEFORE we queue movement so the
				// crew's HP fraction reflects the kill blast — if damage is
				// lethal, Killed fires now and we skip queuing a move on a
				// corpse. If they survive but go critical, InfantryStates.
				// ProneCondition kicks in and the move resolves at prone-speed
				// (≈60%) — naturally fulfils the user's "damaged crew evacuates
				// slower" request without bespoke wiring.
				if (damageToApply > 0)
					crew.InflictDamage(self, new Damage(damageToApply));

				// Walk 2–3 cells in a random direction so crew clears the
				// husk's cookoff radius (0c512 = ½ cell). evaluateNearestMovableCell
				// falls back if the chosen path is blocked by other husks.
				var mobile = crew.TraitOrDefault<Mobile>();
				if (mobile != null && !crew.IsDead)
				{
					var dir = w.SharedRandom.Next(8);
					var dx = new[] { 0, 1, 1, 1, 0, -1, -1, -1 }[dir];
					var dy = new[] { -1, -1, 0, 1, 1, 1, 0, -1 }[dir];
					var dist = 2 + w.SharedRandom.Next(2);
					var target = spawnLocation + new CVec(dx * dist, dy * dist);
					crew.QueueActivity(false, mobile.MoveTo(target, 0, null, true));
				}

				var nbms = crew.TraitsImplementing<INotifyBlockingMove>();
				foreach (var nbm in nbms)
					nbm.OnNotifyBlockingMove(crew, crew);
			});
		}

		int CrewMaxHPFromRules(string actorType)
		{
			if (!self.World.Map.Rules.Actors.TryGetValue(actorType, out var crewInfo))
				return 1;

			var hp = crewInfo.TraitInfoOrDefault<HealthInfo>();
			return hp != null ? hp.HP : 1;
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
			if (!slotIndexByName.TryGetValue(role, out var idx))
				return false;

			return !slotOccupied[idx] && !slotReserved[idx];
		}

		public bool ReserveSlot(string role)
		{
			if (!CanAcceptRole(role))
				return false;

			slotReserved[slotIndexByName[role]] = true;
			return true;
		}

		public void UnreserveSlot(string role)
		{
			if (slotIndexByName.TryGetValue(role, out var idx))
				slotReserved[idx] = false;
		}

		public void FillSlot(string role)
		{
			if (!slotIndexByName.TryGetValue(role, out var idx))
				return;

			if (slotOccupied[idx])
				return;

			slotOccupied[idx] = true;
			slotReserved[idx] = false;
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

		/// <summary>Eject all occupied crew immediately with guaranteed survival.
		/// Used by HeliEmergencyLanding on safe landing — crew walked away alive.</summary>
		public void EjectAllCrew()
		{
			ejecting = false;
			foreach (var slotName in ejectionOrder)
			{
				if (!slotIndexByName.TryGetValue(slotName, out var idx))
					continue;

				if (!slotOccupied[idx])
					continue;

				// Vacate slot, clear reservation, and revoke condition
				slotOccupied[idx] = false;
				slotReserved[idx] = false;
				if (conditionTokens[idx] != Actor.InvalidConditionToken)
					conditionTokens[idx] = self.RevokeCondition(conditionTokens[idx]);

				// Spawn crew actor — guaranteed survival (no random check)
				if (!info.CrewActors.TryGetValue(slotName, out var actorType))
					continue;

				var td = new TypeDictionary
				{
					new OwnerInit(self.Owner),
					new LocationInit(self.Location),
				};

				if (info.TransferVeterancy)
				{
					var ge = self.TraitOrDefault<GainsExperience>();
					if (ge != null && ge.Level > 0)
					{
						var levelXpMap = new[] { 0, 100, 200, 400, 800 };
						var xpToGrant = ge.Level < levelXpMap.Length ? levelXpMap[ge.Level] : levelXpMap[levelXpMap.Length - 1];
						var geInfo = self.Info.TraitInfoOrDefault<GainsExperienceInfo>();
						if (geInfo != null)
							td.Add(new ExperienceInit(geInfo, xpToGrant));
					}
				}

				var spawnLocation = self.Location;
				self.World.AddFrameEndTask(w =>
				{
					// Same hatch-emerge model as EjectCrewMember: spawn at the
					// airframe's cell, then Nudge to an adjacent cell.
					var crew = w.CreateActor(false, actorType, td);
					var positionable = crew.TraitOrDefault<IPositionable>();
					if (positionable == null)
					{
						crew.Dispose();
						return;
					}

					positionable.SetPosition(crew, spawnLocation);
					w.Add(crew);

					// Same evacuation as EjectCrewMember — clear the airframe
					// by 2-3 cells. (Heli-safe-landing doesn't have a cookoff,
					// but the visual still reads better when crew walks away
					// rather than huddling on top of the husk.)
					var mobile = crew.TraitOrDefault<Mobile>();
					if (mobile != null)
					{
						var dir = w.SharedRandom.Next(8);
						var dx = new[] { 0, 1, 1, 1, 0, -1, -1, -1 }[dir];
						var dy = new[] { -1, -1, 0, 1, 1, 1, 0, -1 }[dir];
						var dist = 2 + w.SharedRandom.Next(2);
						var target = spawnLocation + new CVec(dx * dist, dy * dist);
						crew.QueueActivity(false, mobile.MoveTo(target, 0, null, true));
					}

					var nbms = crew.TraitsImplementing<INotifyBlockingMove>();
					foreach (var nbm in nbms)
						nbm.OnNotifyBlockingMove(crew, crew);
				});
			}
		}

		/// <summary>Vacate a slot without spawning a crew actor. Revokes the slot condition.</summary>
		public void VacateSlot(string role)
		{
			if (!slotIndexByName.TryGetValue(role, out var idx))
				return;

			if (!slotOccupied[idx])
				return;

			slotOccupied[idx] = false;
			slotReserved[idx] = false;
			if (conditionTokens[idx] != Actor.InvalidConditionToken)
				conditionTokens[idx] = self.RevokeCondition(conditionTokens[idx]);
		}
	}
}
