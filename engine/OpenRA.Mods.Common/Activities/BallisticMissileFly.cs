#region Copyright & License Information
/*
 * Copyright 2007-2018 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class BallisticMissileFly : Activity
	{
		readonly BallisticMissile sbm;
		readonly WPos initPos;
		readonly WPos targetPos;

		// Arc shape parameter passed to LerpQuadratic. Kept small (distance/speed)
		// to avoid integer overflow in WPos arithmetic.
		readonly int length;

		// Actual game ticks for the flight.
		readonly int totalFlightTicks;

		int ticks;
		readonly WAngle facing;
		readonly bool useAcceleration;

		public BallisticMissileFly(Actor self, Target t, BallisticMissile sbm = null)
		{
			this.sbm = sbm ?? self.Trait<BallisticMissile>();

			initPos = self.CenterPosition;
			targetPos = t.CenterPosition;
			length = Math.Max((targetPos - initPos).Length / this.sbm.Info.Speed, 1);
			facing = (targetPos - initPos).Yaw;

			useAcceleration = this.sbm.Info.Acceleration > 0;

			// No flight time extension needed — the blended acceleration curve
			// (0.4*t + 0.6*t²) has an average progress rate of 1.0, so the missile
			// reaches the target in exactly 'length' ticks at its stated average speed.
			totalFlightTicks = length;
		}

		// Maps real ticks to arc progress (0.0 to 1.0).
		// With acceleration, blends linear + quadratic so the missile starts
		// at 40% of average speed and ends at 160%, instead of the old t²
		// which started at zero speed (causing missiles to appear frozen at launch).
		float GetArcProgress()
		{
			var t = Math.Clamp((float)ticks / Math.Max(totalFlightTicks, 1), 0f, 1f);
			return useAcceleration ? 0.4f * t + 0.6f * t * t : t;
		}

		// Compute the facing with pitch-tilt visual effect.
		// Uses the original analytical formula with a clamp to prevent
		// extreme offsets at steep launch angles.
		WAngle GetEffectiveFacing(float arcProgress)
		{
			var at = Math.Clamp(arcProgress, 0f, 1f);
			var attitude = sbm.Info.LaunchAngle.Tan() * (1 - 2 * at) / (4 * 1024);

			// Clamp attitude to prevent sprite flipping at steep angles
			attitude = Math.Clamp(attitude, -0.5f, 0.5f);

			var u = (facing.Angle % 512) / 512f;
			var scale = 2048 * u * (1 - u);

			var effective = (int)(facing.Angle < 512
				? facing.Angle - scale * attitude
				: facing.Angle + scale * attitude);

			return new WAngle(effective);
		}

		public override bool Tick(Actor self)
		{
			if (ticks >= totalFlightTicks)
			{
				sbm.SetPosition(self, targetPos);
				Queue(new CallFunc(() => self.Kill(self)));
				return true;
			}

			var progress = GetArcProgress();

			// Map progress to a float position along the arc, then interpolate
			// between two adjacent integer arc points for smooth sub-tick movement.
			// We use the original small 'length' for LerpQuadratic to avoid overflow.
			var arcTickF = progress * length;
			var arcTick0 = Math.Min((int)arcTickF, length);
			var arcTick1 = Math.Min(arcTick0 + 1, length);
			var frac = arcTickF - arcTick0;

			var pos0 = WPos.LerpQuadratic(initPos, targetPos, sbm.Info.LaunchAngle, arcTick0, length);
			var pos1 = WPos.LerpQuadratic(initPos, targetPos, sbm.Info.LaunchAngle, arcTick1, length);

			// Lerp between the two points using the fractional part (scaled to 0-1024)
			var pos = WPos.Lerp(pos0, pos1, (int)(frac * 1024), 1024);

			sbm.SetPosition(self, pos);
			sbm.Facing = GetEffectiveFacing(progress);
			ticks++;
			return false;
		}

		public override IEnumerable<Target> GetTargets(Actor self)
		{
			yield return Target.FromPos(targetPos);
		}
	}
}
