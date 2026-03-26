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
		readonly WPos spawnPos;
		readonly WPos targetPos;
		readonly WAngle horizontalFacing;
		readonly bool useAcceleration;
		readonly int launchRiseTicks;
		readonly int launchRiseHeight;
		readonly float visualPitchMul;
		readonly int totalArcTicks;

		int ticks;

		// High-precision denominator for LerpQuadratic to avoid integer truncation
		const int ArcPrecision = 10000;

		public BallisticMissileFly(Actor self, Target t, BallisticMissile sbm = null)
		{
			this.sbm = sbm ?? self.Trait<BallisticMissile>();

			spawnPos = self.CenterPosition;
			targetPos = t.CenterPosition;
			horizontalFacing = (targetPos - spawnPos).Yaw;

			useAcceleration = this.sbm.Info.Acceleration > 0;
			launchRiseTicks = this.sbm.Info.LaunchRiseTicks;
			launchRiseHeight = this.sbm.Info.LaunchRiseHeight.Length;
			visualPitchMul = this.sbm.Info.VisualPitchMultiplier / 100f;

			// Arc is always computed from spawnPos to targetPos (ground-to-ground).
			// The rise height is added as a decaying offset on top of the arc,
			// so the endpoint is always correct and the arc shape is unchanged.
			totalArcTicks = ComputeArcTicks(spawnPos, targetPos);
		}

		int ComputeArcTicks(WPos from, WPos to)
		{
			var distance = (to - from).Length;
			var baseFlightTicks = Math.Max(distance / sbm.Info.Speed, 1);

			if (useAcceleration)
			{
				var accelFactor = 1024 + sbm.Info.Acceleration * 64;
				return baseFlightTicks + baseFlightTicks * 1024 / accelFactor;
			}

			return baseFlightTicks;
		}

		// Maps arc ticks to arc progress (0.0 to 1.0).
		// With acceleration, uses quadratic ease-in so missile starts slow.
		float GetArcProgress(int arcTick)
		{
			if (totalArcTicks <= 1)
				return 1f;

			var t = Math.Clamp((float)arcTick / totalArcTicks, 0f, 1f);
			if (useAcceleration)
				return t * t;

			return t;
		}

		// Get a position on the parabolic arc at a given progress (0.0 to 1.0)
		WPos GetArcPosition(float progress)
		{
			progress = Math.Clamp(progress, 0f, 1f);
			var mul = (int)(progress * ArcPrecision);
			return WPos.LerpQuadratic(spawnPos, targetPos, sbm.Info.LaunchAngle, mul, ArcPrecision);
		}

		// Compute facing from the actual arc tangent direction.
		// VisualPitchMultiplier controls how much vertical movement tilts the sprite.
		WAngle GetEffectiveFacing(float progress)
		{
			if (visualPitchMul <= 0f)
				return horizontalFacing;

			// Sample two points slightly apart to compute tangent
			var eps = 1f / ArcPrecision;
			var p1 = GetArcPosition(Math.Max(0f, progress - eps));
			var p2 = GetArcPosition(Math.Min(1f, progress + eps));
			var delta = p2 - p1;

			var hDist = delta.HorizontalLength;
			if (hDist < 1)
				return horizontalFacing;

			// Compute pitch influence, scaled by VisualPitchMultiplier.
			var maxPitch = 0.4f * visualPitchMul;
			var pitchFactor = Math.Clamp((float)delta.Z / (hDist * 4), -maxPitch, maxPitch);

			// Apply pitch as a facing offset using isometric projection.
			var u = (horizontalFacing.Angle % 512) / 512f;
			var scale = 2048 * u * (1 - u);

			var effective = (int)(horizontalFacing.Angle < 512
				? horizontalFacing.Angle - scale * pitchFactor
				: horizontalFacing.Angle + scale * pitchFactor);

			return new WAngle(effective);
		}

		public override bool Tick(Actor self)
		{
			// Phase 1: Vertical launch rise (no horizontal movement)
			if (launchRiseTicks > 0 && ticks < launchRiseTicks)
			{
				var riseT = Math.Clamp((float)ticks / launchRiseTicks, 0f, 1f);

				// Cubic ease-in: very slow ignition, accelerating upward
				var riseProgress = riseT * riseT * riseT;
				var riseZ = (int)(launchRiseHeight * riseProgress);

				var pos = spawnPos + new WVec(0, 0, riseZ);
				sbm.SetPosition(self, pos);

				// During rise, keep horizontal facing (facing toward target)
				sbm.Facing = horizontalFacing;
				ticks++;
				return false;
			}

			// Phase 2: Parabolic arc with decaying rise offset
			var arcTick = ticks - launchRiseTicks;
			if (arcTick >= totalArcTicks)
			{
				sbm.SetPosition(self, targetPos);
				Queue(new CallFunc(() => self.Kill(self)));
				return true;
			}

			var arcProgress = GetArcProgress(arcTick);
			var arcPos = GetArcPosition(arcProgress);

			// Add the rise height as a decaying offset on top of the arc.
			// At arcProgress=0: full rise height (smooth transition from rise phase).
			// At arcProgress=1: zero offset (missile arrives at exact target).
			if (launchRiseHeight > 0)
			{
				var decayFactor = 1.0f - arcProgress;
				var riseOffset = (int)(launchRiseHeight * decayFactor);
				arcPos += new WVec(0, 0, riseOffset);
			}

			sbm.SetPosition(self, arcPos);
			sbm.Facing = GetEffectiveFacing(arcProgress);
			ticks++;
			return false;
		}

		public override IEnumerable<Target> GetTargets(Actor self)
		{
			yield return Target.FromPos(targetPos);
		}
	}
}
