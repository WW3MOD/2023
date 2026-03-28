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
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	class EnterAsCrew : Enter
	{
		readonly string role;
		Actor enterActor;

		public EnterAsCrew(Actor self, in Target target, string role)
			: base(self, target, Color.Green)
		{
			this.role = role;
		}

		protected override bool TryStartEnter(Actor self, Actor targetActor)
		{
			enterActor = targetActor;

			var vc = targetActor.TraitOrDefault<VehicleCrew>();
			if (vc == null || !vc.CanAcceptRole(role))
			{
				Log.Write("debug", $"[EnterAsCrew] {self.Info.Name} TryStartEnter REJECTED: vc={vc != null}, canAccept={vc?.CanAcceptRole(role)}, role={role}, target={targetActor.Info.Name}");
				Cancel(self, true);
				return false;
			}

			Log.Write("debug", $"[EnterAsCrew] {self.Info.Name} TryStartEnter ACCEPTED: role={role}, target={targetActor.Info.Name} (Owner={targetActor.Owner.PlayerName})");
			return true;
		}

		protected override void OnEnterComplete(Actor self, Actor targetActor)
		{
			Log.Write("debug", $"[EnterAsCrew] {self.Info.Name} OnEnterComplete called: role={role}, target={targetActor.Info.Name} (Owner={targetActor.Owner.PlayerName})");
			self.World.AddFrameEndTask(w =>
			{
				if (self.IsDead)
				{
					Log.Write("debug", $"[EnterAsCrew] {self.Info.Name} AddFrameEndTask: self is dead, aborting");
					return;
				}

				if (targetActor != enterActor)
				{
					Log.Write("debug", $"[EnterAsCrew] {self.Info.Name} AddFrameEndTask: targetActor changed, aborting");
					return;
				}

				var vc = targetActor.TraitOrDefault<VehicleCrew>();
				if (vc == null || !vc.CanAcceptRole(role))
				{
					Log.Write("debug", $"[EnterAsCrew] {self.Info.Name} AddFrameEndTask: CanAcceptRole({role}) FAILED, vc={vc != null}, emptySlots=[{string.Join(",", vc?.EmptySlots ?? System.Array.Empty<string>())}]");
					return;
				}

				// Capture: if entering a non-allied vehicle (neutral crashed helicopter),
				// change its ownership to the crew member's player
				if (!self.Owner.IsAlliedWith(targetActor.Owner))
				{
					Log.Write("debug", $"[EnterAsCrew] {self.Info.Name} Capturing {targetActor.Info.Name} from {targetActor.Owner.PlayerName} to {self.Owner.PlayerName}");
					targetActor.ChangeOwner(self.Owner);
				}

				Log.Write("debug", $"[EnterAsCrew] {self.Info.Name} FillSlot({role}) SUCCESS, removing crew from world");
				vc.FillSlot(role);
				w.Remove(self);
			});
		}
	}
}
