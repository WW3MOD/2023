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

using System.Collections.Generic;
using System.Linq;
using OpenRA;
using OpenRA.Mods.Common.Effects;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Actor can be captured by units in a specified proximity.")]
	public class ProximityCapturableInfo : TraitInfo, IRulesetLoaded
	{
		[Desc("Maximum range at which a ProximityCaptor actor can initiate the capture.")]
		public readonly WDist Range = WDist.FromCells(5);

		[Desc("Allowed ProximityCaptor actors to capture this actor.")]
		public readonly BitSet<CaptureType> CaptorTypes = new BitSet<CaptureType>("Player", "Vehicle", "Tank", "Infantry");

		[Desc("If set, the capturing process stops immediately after another player comes into Range.")]
		public readonly bool MustBeClear = false;

		[Desc("If set, the ownership will not revert back when the captor leaves the area.")]
		public readonly bool Sticky = false;

		[Desc("If set, the actor can only be captured via this logic once.",
			"This option implies the `Sticky` behaviour as well.")]
		public readonly bool Permanent = false;

		[Desc("If set, capture requires dominance of force (higher unit value).")]
		public readonly int Dominance = 0;

		[Desc("When captured, Actor turns neutral.")]
		public readonly bool TurnNeutral = false;

		[Desc("If capturing player moves away ownership is reverted to original owner.")]
		public readonly bool ReturnToOriginalOwner = false;

		public void RulesetLoaded(Ruleset rules, ActorInfo info)
		{
			var pci = rules.Actors[SystemActors.Player].TraitInfoOrDefault<ProximityCaptorInfo>();
			if (pci == null)
				throw new YamlException("ProximityCapturable requires the `Player` actor to have the ProximityCaptor trait.");
		}

		public override object Create(ActorInitializer init) { return new ProximityCapturable(init.Self, this); }
	}

	public class ProximityCapturable : ITick, INotifyAddedToWorld, INotifyRemovedFromWorld, INotifyOwnerChanged
	{
		public readonly Player OriginalOwner;
		public bool Captured => Self.Owner != OriginalOwner;

		public ProximityCapturableInfo Info;
		public Actor Self;

		readonly List<Actor> friendlyActorsInRange = new List<Actor>();
		readonly List<Actor> enemyActorsInRange = new List<Actor>();
		int proximityTrigger;
		WPos prevPosition;
		bool skipTriggerUpdate;

		public ProximityCapturable(Actor self, ProximityCapturableInfo info)
		{
			Info = info;
			Self = self;
			OriginalOwner = self.Owner;
		}

		void INotifyAddedToWorld.AddedToWorld(Actor self)
		{
			if (skipTriggerUpdate)
				return;

			// TODO: Eventually support CellTriggers as well
			proximityTrigger = self.World.ActorMap.AddProximityTrigger(self.CenterPosition, Info.Range, WDist.Zero, ActorEntered, ActorLeft);
		}

		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self)
		{
			if (skipTriggerUpdate)
				return;

			self.World.ActorMap.RemoveProximityTrigger(proximityTrigger);
			enemyActorsInRange.Clear();
		}

		void ITick.Tick(Actor self)
		{
			if (!self.IsInWorld || self.CenterPosition == prevPosition)
				return;

			self.World.ActorMap.UpdateProximityTrigger(proximityTrigger, self.CenterPosition, Info.Range, WDist.Zero);
			prevPosition = self.CenterPosition;
		}

		void ActorEntered(Actor other)
		{
			if (skipTriggerUpdate || !CanBeCapturedBy(other))
				return;

			if (other.Owner.RelationshipWith(OriginalOwner) == PlayerRelationship.Ally) {
				friendlyActorsInRange.Add(other);
			}
			else {
				enemyActorsInRange.Add(other);
			}

			UpdateOwnership();
		}

		void ActorLeft(Actor other)
		{
			if (skipTriggerUpdate || !CanBeCapturedBy(other))
				return;

			if (other.Owner.RelationshipWith(OriginalOwner) == PlayerRelationship.Ally) {
				friendlyActorsInRange.Remove(other);
			}
			else {
				enemyActorsInRange.Remove(other);
			}

			UpdateOwnership();
		}

		bool CanBeCapturedBy(Actor a)
		{
			if (a == Self)
				return false;

			var pc = a.Info.TraitInfoOrDefault<ProximityCaptorInfo>();
			return pc != null && pc.Types.Overlaps(Info.CaptorTypes);
		}

		void UpdateOwnership()
		{
			if (Captured && (Info.Permanent || OriginalOwner.WinState == OpenRA.WinState.Lost))
			{
				// This area has been captured and cannot ever be re-captured, so we get rid of the
				// ProximityTrigger and ensure that it won't be recreated in AddedToWorld.
				skipTriggerUpdate = true;
				Self.World.ActorMap.RemoveProximityTrigger(proximityTrigger);
				return;
			}

			// The actor that has been in the area the longest will be the captor.
			// The previous implementation used the closest one, but that doesn't work with
			// ProximityTriggers since they only generate events when actors enter or leave.
			var enemyCaptor = enemyActorsInRange.FirstOrDefault();

			// The last unit left the area
			if (enemyCaptor == null)
			{
				// Unless the Sticky option is set, we revert to the original owner.
				if (Captured && !Info.Sticky)
					ChangeOwnership(Self, OriginalOwner.PlayerActor);
			}
			else
			{
				if (Info.MustBeClear)
				{
					var isClear = enemyActorsInRange.All(a => enemyCaptor.Owner.RelationshipWith(a.Owner) == PlayerRelationship.Ally);

					// An enemy unit has wandered into the area, so we've lost control of it.
					if (Captured && !isClear)
						ChangeOwnership(Self, OriginalOwner.PlayerActor);

					// We don't own the area yet, but it is clear from enemy units, so we take possession of it.
					else if (Self.Owner != enemyCaptor.Owner && isClear)
						ChangeOwnership(Self, enemyCaptor);
				}
				else // if (Self.Owner != enemyCaptor.Owner)
				{
					if (Info.Dominance > 0)
					{
						var allyValue = 0;
						var enemyValue = 0;

						foreach (var actor in enemyActorsInRange)
						{
							enemyValue += actor.Info.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 0;
						}

						foreach (var actor in friendlyActorsInRange)
						{
							allyValue += actor.Info.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 0;
						}

						if (enemyValue > allyValue * Info.Dominance / 100)
							ChangeOwnership(Self, enemyCaptor);
						else
							ChangeOwnership(Self, OriginalOwner.PlayerActor);
					}
					else
						ChangeOwnership(Self, enemyCaptor);
				}
			}
		}

		void ChangeOwnership(Actor self, Actor captor)
		{
			self.World.AddFrameEndTask(w =>
			{
				if (self.Disposed || captor.Disposed)
					return;

				var changeTo = Info.TurnNeutral & captor.Owner.RelationshipWith(OriginalOwner) == PlayerRelationship.Enemy ? self.World.Players.First(p => p.PlayerName == "Neutral") : captor.Owner;

				// prevent (Added|Removed)FromWorld from firing during Actor.ChangeOwner
				skipTriggerUpdate = true;
				var previousOwner = self.Owner;
				self.ChangeOwner(changeTo);

				if (self.Owner == self.World.LocalPlayer)
					w.Add(new FlashTarget(self, Color.White));

				var pc = captor.Info.TraitInfoOrDefault<ProximityCaptorInfo>();
				foreach (var t in self.TraitsImplementing<INotifyCapture>())
					t.OnCapture(self, captor, previousOwner, changeTo, pc.Types);
			});
		}

		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner)
		{
			Game.RunAfterTick(() => skipTriggerUpdate = false);
		}
	}
}
