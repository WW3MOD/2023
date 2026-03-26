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
		readonly int launchRiseTicks;
		readonly int launchRiseHeight;
		readonly float visualPitchMul;
		readonly int totalArcTicks;
		readonly int arcPeakHeight;

		int ticks;

		public BallisticMissileFly(Actor self, Target t, BallisticMissile sbm = null)
		{
			this.sbm = sbm ?? self.Trait<BallisticMissile>();

			spawnPos = self.CenterPosition;
			targetPos = t.CenterPosition;
			horizontalFacing = (targetPos - spawnPos).Yaw;

			launchRiseTicks = this.sbm.Info.LaunchRiseTicks;
			launchRiseHeight = this.sbm.Info.LaunchRiseHeight.Length;
			visualPitchMul = this.sbm.Info.VisualPitchMultiplier / 100f;

			// Compute arc duration from horizontal distance and speed
			var hDist = (targetPos - spawnPos).HorizontalLength;
			var baseFlightTicks = Math.Max(hDist / this.sbm.Info.Speed, 1);

			if (this.sbm.Info.Acceleration > 0)
			{
				// With ease-in-out, average speed is lower — extend flight time
				var accelFactor = 1024 + this.sbm.Info.Acceleration * 64;
				totalArcTicks = baseFlightTicks + baseFlightTicks * 512 / accelFactor;
			}
			else
				totalArcTicks = baseFlightTicks;

			// Peak height of the arc, derived from LaunchAngle and horizontal distance.
			// For a parabolic arc: peak = range * tan(angle) / 4
			var tan = this.sbm.Info.LaunchAngle.Tan();
			arcPeakHeight = (int)((long)hDist * tan / (4 * 1024));
		}

		// Ease-in-out: slow launch, fast cruise, decelerating approach.
		// Much smoother than pure t² which makes the descent look too fast.
		static float EaseInOut(float t)
		{
			t = Math.Clamp(t, 0f, 1f);
			if (t < 0.5f)
				return 2f * t * t;
			return 1f - (-2f * t + 2f) * (-2f * t + 2f) / 2f;
		}

		// Sine-curve arc height. Always >= 0, peaks at progress=0.5.
		int GetArcHeight(float progress)
		{
			return (int)(arcPeakHeight * Math.Sin(Math.PI * progress));
		}

		// Compute facing with optional pitch tilt for isometric visual.
		WAngle GetFacing(float progress)
		{
			if (visualPitchMul <= 0f)
				return horizontalFacing;

			// Sample height at two nearby points to get vertical velocity direction
			var eps = 0.005f;
			var h1 = GetArcHeight(Math.Max(0f, progress - eps));
			var h2 = GetArcHeight(Math.Min(1f, progress + eps));
			var dh = h2 - h1;

			// Horizontal distance covered in the same eps range
			var hDist = (targetPos - spawnPos).HorizontalLength;
			var hStep = (int)(hDist * eps * 2);
			if (hStep < 1)
				return horizontalFacing;

			// Pitch factor: how much the missile is climbing or diving
			var maxPitch = 0.4f * visualPitchMul;
			var pitchFactor = Math.Clamp((float)dh / (hStep * 4), -maxPitch, maxPitch);

			// Apply pitch as a facing offset using isometric projection
			var u = (horizontalFacing.Angle % 512) / 512f;
			var scale = 2048 * u * (1 - u);

			var effective = (int)(horizontalFacing.Angle < 512
				? horizontalFacing.Angle - scale * pitchFactor
				: horizontalFacing.Angle + scale * pitchFactor);

			return new WAngle(effective);
		}

		public override bool Tick(Actor self)
		{
			// Phase 1: Vertical launch rise (missile lifts off launcher)
			if (launchRiseTicks > 0 && ticks < launchRiseTicks)
			{
				var riseT = Math.Clamp((float)ticks / launchRiseTicks, 0f, 1f);

				// Cubic ease-in: slow ignition, accelerating upward
				var riseProgress = riseT * riseT * riseT;
				var riseZ = (int)(launchRiseHeight * riseProgress);

				sbm.SetPosition(self, spawnPos + new WVec(0, 0, riseZ));
				sbm.Facing = horizontalFacing;
				ticks++;
				return false;
			}

			// Phase 2: Parabolic arc flight
			var arcTick = ticks - launchRiseTicks;
			if (arcTick >= totalArcTicks)
			{
				sbm.SetPosition(self, targetPos);
				Queue(new CallFunc(() => self.Kill(self)));
				return true;
			}

			// Time progress through the arc (0 to 1)
			var timeT = Math.Clamp((float)arcTick / totalArcTicks, 0f, 1f);

			// Apply ease-in-out if acceleration is configured
			var progress = sbm.Info.Acceleration > 0 ? EaseInOut(timeT) : timeT;

			// Horizontal position: linear interpolation along ground path
			var hx = spawnPos.X + (int)((long)(targetPos.X - spawnPos.X) * arcTick / totalArcTicks);
			var hy = spawnPos.Y + (int)((long)(targetPos.Y - spawnPos.Y) * arcTick / totalArcTicks);

			// Vertical position: base terrain Z interpolated + sine arc + decaying rise offset
			var baseZ = spawnPos.Z + (int)((long)(targetPos.Z - spawnPos.Z) * arcTick / totalArcTicks);
			var arcHeight = GetArcHeight(progress);
			var riseDecay = launchRiseHeight > 0 ? (int)(launchRiseHeight * (1f - progress)) : 0;

			var pos = new WPos(hx, hy, baseZ + arcHeight + riseDecay);

			sbm.SetPosition(self, pos);
			sbm.Facing = GetFacing(progress);
			ticks++;
			return false;
		}

		public override IEnumerable<Target> GetTargets(Actor self)
		{
			yield return Target.FromPos(targetPos);
		}
	}
}
