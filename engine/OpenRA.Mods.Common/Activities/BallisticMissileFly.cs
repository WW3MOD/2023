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

		// Arc shape parameter: how many arc-steps the LerpQuadratic curve has.
		// This determines the shape of the parabola, independent of flight time.
		readonly int arcLength;

		// Total flight duration in game ticks.
		// With acceleration this is longer than arcLength because the missile starts slow.
		readonly int totalFlightTicks;

		int ticks;
		readonly WAngle facing;
		readonly bool useAcceleration;

		public BallisticMissileFly(Actor self, Target t, BallisticMissile sbm = null)
		{
			if (sbm == null)
				this.sbm = self.Trait<BallisticMissile>();
			else
				this.sbm = sbm;

			initPos = self.CenterPosition;
			targetPos = t.CenterPosition; // fixed position == no homing
			arcLength = Math.Max((targetPos - initPos).Length / this.sbm.Info.Speed, 1);
			facing = (targetPos - initPos).Yaw;

			useAcceleration = this.sbm.Info.Acceleration > 0;
			if (useAcceleration)
			{
				// With acceleration from rest, average speed is lower so flight takes longer.
				// Acceleration value scales how much longer: higher accel = closer to constant-speed time.
				// At Acceleration=1, flight takes ~2x longer. At Acceleration=100, nearly instant accel.
				// Formula: totalTicks = arcLength * (1 + 1024 / (1024 + Acceleration * 64))
				// This gives a smooth range from ~2x (Accel=1) to ~1.05x (Accel=100+)
				var accelFactor = 1024 + this.sbm.Info.Acceleration * 64;
				totalFlightTicks = arcLength + arcLength * 1024 / accelFactor;
			}
			else
			{
				totalFlightTicks = arcLength;
			}
		}

		// Maps real ticks (0..totalFlightTicks) to arc progress (0..arcLength).
		// With acceleration, uses ease-in (quadratic) so missile starts slow and accelerates.
		int GetArcTick()
		{
			if (!useAcceleration || totalFlightTicks <= 1)
				return ticks;

			// Quadratic ease-in: t^2 curve. Missile starts at 0 speed, accelerates smoothly.
			// progress = (ticks / totalFlightTicks)^2 mapped to arcLength range
			var t = (long)ticks * ticks;
			var total = (long)totalFlightTicks * totalFlightTicks;
			return (int)(t * arcLength / total);
		}

		// Get the arc progress as a 0..1 float for facing calculation
		float GetArcProgress()
		{
			var arcTick = GetArcTick();
			return arcLength > 1 ? (float)arcTick / (arcLength - 1) : 0f;
		}

		WAngle GetEffectiveFacing()
		{
			var at = GetArcProgress();
			var attitude = sbm.Info.LaunchAngle.Tan() * (1 - 2 * at) / (4 * 1024);

			// HACK HACK HACK
			// BodyOrientation does a 90° rotation on isometric worlds.
			// This calculation needs to be updated to accomodate that.
			var u = (facing.Angle % 512) / 512f;
			var scale = 2048 * u * (1 - u);

			var effective = (int)(facing.Angle < 512
				? facing.Angle - scale * attitude
				: facing.Angle + scale * attitude);

			return new WAngle(effective);
		}

		public void FlyToward(Actor self, BallisticMissile sbm)
		{
			var arcTick = GetArcTick();
			var pos = WPos.LerpQuadratic(initPos, targetPos, sbm.Info.LaunchAngle, arcTick, arcLength);
			sbm.SetPosition(self, pos);
			sbm.Facing = GetEffectiveFacing();
		}

		public override bool Tick(Actor self)
		{
			// Terminate when we've completed the flight
			if (ticks >= totalFlightTicks)
			{
				// Snap to target and detonate
				sbm.SetPosition(self, targetPos);
				Queue(new CallFunc(() => self.Kill(self)));
				return true;
			}

			FlyToward(self, sbm);
			ticks++;
			return false;
		}

		public override IEnumerable<Target> GetTargets(Actor self)
		{
			yield return Target.FromPos(targetPos);
		}
	}
}
