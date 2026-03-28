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

using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	class EnterAsCrew : Enter
	{
		readonly string role;
		Actor enterActor;
		bool tryStartEnterCalled;

		public EnterAsCrew(Actor self, in Target target, string role)
			: base(self, target, Color.Green)
		{
			this.role = role;
		}

		protected override bool TryStartEnter(Actor self, Actor targetActor)
		{
			enterActor = targetActor;
			tryStartEnterCalled = true;

			var vc = targetActor.TraitOrDefault<VehicleCrew>();
			if (vc == null || !vc.CanAcceptRole(role))
			{
				Game.Debug($"[EnterAsCrew] TryStartEnter FAILED: vc={vc != null}, canAccept={vc?.CanAcceptRole(role)}, role={role}, target={targetActor.Info.Name} owner={targetActor.Owner.PlayerName}");
				Cancel(self, true);
				return false;
			}

			Game.Debug($"[EnterAsCrew] TryStartEnter OK: {self.Info.Name}({self.Owner.PlayerName}) → {targetActor.Info.Name}({targetActor.Owner.PlayerName}), role={role}, allowForeign={vc.AllowForeignCrew}");
			return true;
		}

		protected override void TickInner(Actor self, in Target target, bool targetIsDeadOrHiddenActor)
		{
			// Log once after TryStartEnter to diagnose position check issues
			if (tryStartEnterCalled && targetIsDeadOrHiddenActor)
				Game.Debug($"[EnterAsCrew] TickInner: target hidden/dead for {self.Owner.PlayerName}, targetType={target.Type}");
		}

		protected override void OnEnterComplete(Actor self, Actor targetActor)
		{
			Game.Debug($"[EnterAsCrew] OnEnterComplete: {self.Info.Name} → {targetActor.Info.Name}({targetActor.Owner.PlayerName}), allied={self.Owner.IsAlliedWith(targetActor.Owner)}");

			self.World.AddFrameEndTask(w =>
			{
				if (self.IsDead)
					return;

				if (targetActor != enterActor)
					return;

				var vc = targetActor.TraitOrDefault<VehicleCrew>();
				if (vc == null || !vc.CanAcceptRole(role))
				{
					Game.Debug($"[EnterAsCrew] OnEnterComplete frame-end: CanAcceptRole FAILED");
					return;
				}

				// Capture: if entering a non-allied vehicle (neutral crashed helicopter),
				// change its ownership to the crew member's player
				if (!self.Owner.IsAlliedWith(targetActor.Owner))
				{
					Game.Debug($"[EnterAsCrew] CAPTURE: changing owner from {targetActor.Owner.PlayerName} to {self.Owner.PlayerName}");
					targetActor.ChangeOwner(self.Owner);
				}
				else
				{
					Game.Debug($"[EnterAsCrew] NOT capturing: already allied");
				}

				vc.FillSlot(role);
				w.Remove(self);
				Game.Debug($"[EnterAsCrew] Slot filled: {role}, crew removed from world");
			});
		}
	}
}
