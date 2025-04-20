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
using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class Turn : Activity
	{
		readonly Mobile mobile;
		readonly IFacing facing;
		readonly WAngle desiredFacing;
		readonly IEnumerable<int> speedModifiers; // Using speed modifier for turn as well

		public Turn(Actor self, WAngle desiredFacing)
		{
			mobile = self.TraitOrDefault<Mobile>();
			facing = self.Trait<IFacing>();
			speedModifiers = self.TraitsImplementing<ISpeedModifier>().ToArray().Select(m => m.GetSpeedModifier());
			this.desiredFacing = desiredFacing;
		}

		public override bool Tick(Actor self)
		{
			if (IsCanceling)
				return true;

			if ((mobile != null && (mobile.IsTraitDisabled || mobile.IsTraitPaused)) || speedModifiers.Any(v => v == 0))
				return false;

			if (desiredFacing == facing.Facing)
				return true;

			var turnSpeed = new WAngle(Util.ApplyPercentageModifiers(facing.TurnSpeed.Angle, speedModifiers.ToArray()));
			facing.Facing = Util.TickFacing(facing.Facing, desiredFacing, turnSpeed);

			return false;
		}
	}
}
