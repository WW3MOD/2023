#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class CaptureActor : Enter
	{
		readonly CaptureManager manager;

		Actor enterActor;
		CaptureManager enterCaptureManager;

		public CaptureActor(Actor self, in Target target, Color? targetLineColor)
			: base(self, target, targetLineColor)
		{
			manager = self.Trait<CaptureManager>();
		}

		protected override void TickInner(Actor self, in Target target, bool targetIsDeadOrHiddenActor)
		{
			if (target.Type == TargetType.Actor && enterActor != target.Actor)
			{
				enterActor = target.Actor;
				enterCaptureManager = target.Actor.TraitOrDefault<CaptureManager>();
			}

			if (!targetIsDeadOrHiddenActor && target.Type != TargetType.FrozenActor &&
				(enterCaptureManager == null || !manager.CanTarget(enterCaptureManager)))
				Cancel(self, true);
		}

		protected override bool TryStartEnter(Actor self, Actor targetActor)
		{
			if (enterActor != targetActor)
			{
				enterActor = targetActor;
				enterCaptureManager = targetActor.TraitOrDefault<CaptureManager>();
			}

			// Make sure we can still capture the target before entering
			// (but not before, because this may stop the actor in the middle of nowhere)
			if (enterCaptureManager == null || !manager.CanTarget(enterCaptureManager))
			{
				Cancel(self, true);
				return false;
			}

			// StartCapture returns false when a capture delay is enabled
			// We wait until it returns true before allowing entering the target
			if (!manager.StartCapture(enterCaptureManager, out var captures))
				return false;

			if (!captures.Info.ConsumedByCapture)
			{
				// Immediately capture without entering or disposing the actor
				DoCapture(self, captures);
				Cancel(self, true);
				return false;
			}

			return true;
		}

		protected override void OnEnterComplete(Actor self, Actor targetActor)
		{
			// Make sure the target hasn't changed while entering
			// OnEnterComplete is only called if targetActor is alive
			if (enterActor != targetActor)
				return;

			if (enterCaptureManager.BeingCaptured || !manager.CanTarget(enterCaptureManager))
				return;

			// Prioritize capturing over sabotaging
			var captures = manager.ValidCapturesWithLowestSabotageThreshold(enterCaptureManager);
			if (captures == null)
				return;

			DoCapture(self, captures);
		}

		void DoCapture(Actor self, Captures captures)
		{
			var oldOwner = enterActor.Owner;
			self.World.AddFrameEndTask(w =>
			{
				// The target died or was already captured during this tick
				if (enterActor.IsDead || oldOwner != enterActor.Owner)
					return;

				// Sabotage instead of capture
				if (captures.Info.SabotageThreshold > 0 && !enterActor.Owner.NonCombatant)
				{
					var health = enterActor.Trait<IHealth>();

					// Cast to long to avoid overflow when multiplying by the health
					if (100 * (long)health.HP > captures.Info.SabotageThreshold * (long)health.MaxHP)
					{
						var damage = (int)((long)health.MaxHP * captures.Info.SabotageHPRemoval / 100);
						enterActor.InflictDamage(self, new Damage(damage, captures.Info.SabotageDamageTypes));

						ApplyEnterBehaviour(self, captures);

						return;
					}
				}

				var newOwner = self.Owner;
				if (captures.Info.CaptureToNeutral)
				{
					// Maps are not required to define a Neutral player. Abort rather than falling
					// back to self.Owner: the point of CaptureToNeutral is that this actor must
					// not end up owning the target, so handing it over would invert the rule.
					newOwner = w.Players.FirstOrDefault(p => p.InternalName == "Neutral");
					if (newOwner == null)
					{
						Log.Write("debug", $"CaptureToNeutral: {self.Info.Name} cannot neutralise {enterActor.Info.Name} - this map defines no Neutral player. Capture aborted.");
						return;
					}
				}

				// Do the capture.
				// Buildings are stationary — use the in-place path so we don't
				// trigger World.Remove/Add and the expensive shroud/vision recalc
				// cascade on every player (causes a ~0.5s freeze on capture).
				if (enterActor.Info.HasTraitInfo<BuildingInfo>())
					enterActor.ChangeOwnerInPlaceSync(newOwner);
				else
					enterActor.ChangeOwnerSync(newOwner);

				foreach (var t in enterActor.TraitsImplementing<INotifyCapture>())
					t.OnCapture(enterActor, self, oldOwner, newOwner, captures.Info.CaptureTypes);

				if (self.Owner.RelationshipWith(oldOwner).HasRelationship(captures.Info.PlayerExperienceRelationships))
					self.Owner.PlayerActor.TraitOrDefault<PlayerExperience>()?.GiveExperience(captures.Info.PlayerExperience);

				ApplyEnterBehaviour(self, captures);
			});
		}

		// ConsumedByCapture still means "enters the target" (CaptureManager.cs keys its progress-bar
		// duration estimate off it); EnterBehaviour decides what happens to the actor once inside.
		static void ApplyEnterBehaviour(Actor self, Captures captures)
		{
			if (!captures.Info.ConsumedByCapture)
				return;

			switch (captures.Info.EnterBehaviour)
			{
				case EnterBehaviour.Dispose:
					self.Dispose();
					break;
				case EnterBehaviour.Suicide:
					self.Kill(self);
					break;
			}
		}

		protected override void OnLastRun(Actor self)
		{
			CancelCapture();
			base.OnLastRun(self);
		}

		protected override void OnActorDispose(Actor self)
		{
			CancelCapture();
			base.OnActorDispose(self);
		}

		public override void Cancel(Actor self, bool keepQueue = false)
		{
			CancelCapture();
			base.Cancel(self, keepQueue);
		}

		void CancelCapture()
		{
			manager.CancelCapture(enterActor, enterCaptureManager);
		}
	}
}
