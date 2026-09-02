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

		/// <summary>
		/// The actor this capture was ORDERED at, recorded at construction. enterActor below is only
		/// populated once the activity ticks, so it is null for a capture that is still queued behind
		/// something else — which is exactly the case a dispatcher has to see in order not to steal a
		/// technician that already has a job.
		/// </summary>
		public Actor OrderedTarget { get; }

		public CaptureActor(Actor self, in Target target, Color? targetLineColor)
			: base(self, target, targetLineColor)
		{
			manager = self.Trait<CaptureManager>();
			OrderedTarget = target.Type == TargetType.Actor ? target.Actor : null;
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

				// The world owner is the map's OwnsWorld player — conventionally Neutral, but
				// resolved structurally rather than by name: CreateMapPlayers.cs:105-106 throws at
				// world creation unless some player claims the world, so this is guaranteed
				// non-null on every loadable map, whereas matching InternalName == "Neutral"
				// would fail silently on a map that names its world owner anything else.
				var newOwner = captures.Info.CaptureToNeutral ? w.WorldActor.Owner : self.Owner;

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
