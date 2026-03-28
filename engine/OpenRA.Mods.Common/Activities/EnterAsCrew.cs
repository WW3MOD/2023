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
				Cancel(self, true);
				return false;
			}

			return true;
		}

		protected override void OnEnterComplete(Actor self, Actor targetActor)
		{
			self.World.AddFrameEndTask(w =>
			{
				if (self.IsDead)
					return;

				if (targetActor != enterActor)
					return;

				var vc = targetActor.TraitOrDefault<VehicleCrew>();
				if (vc == null || !vc.CanAcceptRole(role))
					return;

				// Capture: if entering a non-allied vehicle (neutral crashed helicopter),
				// change its ownership to the crew member's player
				if (!self.Owner.IsAlliedWith(targetActor.Owner))
					targetActor.ChangeOwner(self.Owner);

				vc.FillSlot(role);
				w.Remove(self);
			});
		}
	}
}
