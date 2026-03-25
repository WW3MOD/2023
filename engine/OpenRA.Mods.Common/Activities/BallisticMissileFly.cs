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
		readonly int totalFlightTicks;
		readonly WAngle horizontalFacing;
		readonly bool useAcceleration;
		int ticks;

		// High-precision denominator for LerpQuadratic to avoid integer truncation
		const int ArcPrecision = 10000;

		public BallisticMissileFly(Actor self, Target t, BallisticMissile sbm = null)
		{
			this.sbm = sbm ?? self.Trait<BallisticMissile>();

			initPos = self.CenterPosition;
			targetPos = t.CenterPosition;
			horizontalFacing = (targetPos - initPos).Yaw;

			var distance = (targetPos - initPos).Length;
			var baseFlightTicks = Math.Max(distance / this.sbm.Info.Speed, 1);

			useAcceleration = this.sbm.Info.Acceleration > 0;
			if (useAcceleration)
			{
				// With acceleration from rest, average speed is lower so flight takes longer.
				// Higher Acceleration value = faster ramp-up = shorter extension.
				// Accel=8: ~1.66x base time. Accel=16: ~1.5x. Accel=100+: ~1.05x.
				var accelFactor = 1024 + this.sbm.Info.Acceleration * 64;
				totalFlightTicks = baseFlightTicks + baseFlightTicks * 1024 / accelFactor;
			}
			else
			{
				totalFlightTicks = baseFlightTicks;
			}
		}

		// Maps game ticks to arc progress (0.0 to 1.0).
		// With acceleration, uses quadratic ease-in so missile starts slow.
		float GetArcProgress()
		{
			if (totalFlightTicks <= 1)
				return 1f;

			var t = (float)ticks / totalFlightTicks;
			if (useAcceleration)
				return t * t;

			return t;
		}

		// Get a position on the parabolic arc at a given progress (0.0 to 1.0)
		WPos GetArcPosition(float progress)
		{
			progress = Math.Clamp(progress, 0f, 1f);
			var mul = (int)(progress * ArcPrecision);
			return WPos.LerpQuadratic(initPos, targetPos, sbm.Info.LaunchAngle, mul, ArcPrecision);
		}

		// Compute facing from the actual arc tangent direction.
		// Samples two nearby points on the arc to get the movement vector,
		// then applies a clamped pitch influence to the horizontal facing.
		// This replaces the old analytical formula that broke at steep angles.
		WAngle GetEffectiveFacing()
		{
			var progress = GetArcProgress();

			// Sample two points slightly apart to compute tangent
			var eps = 1f / ArcPrecision;
			var p1 = GetArcPosition(Math.Max(0f, progress - eps));
			var p2 = GetArcPosition(Math.Min(1f, progress + eps));
			var delta = p2 - p1;

			var hDist = delta.HorizontalLength;
			if (hDist < 1)
				return horizontalFacing;

			// Compute pitch influence: vertical movement relative to horizontal.
			// Clamp to ±0.4 to prevent extreme facing shifts at steep launch angles.
			// Positive = climbing, negative = diving.
			var pitchFactor = Math.Clamp((float)delta.Z / (hDist * 4), -0.4f, 0.4f);

			// Apply pitch as a facing offset using the isometric projection hack.
			// This makes the sprite visually tilt when climbing/diving.
			var u = (horizontalFacing.Angle % 512) / 512f;
			var scale = 2048 * u * (1 - u);

			var effective = (int)(horizontalFacing.Angle < 512
				? horizontalFacing.Angle - scale * pitchFactor
				: horizontalFacing.Angle + scale * pitchFactor);

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

			var pos = GetArcPosition(GetArcProgress());
			sbm.SetPosition(self, pos);
			sbm.Facing = GetEffectiveFacing();
			ticks++;
			return false;
		}

		public override IEnumerable<Target> GetTargets(Actor self)
		{
			yield return Target.FromPos(targetPos);
		}
	}
}
