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
		readonly int hDist;

		float currentSpeed;
		float horizontalProgress;
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

			hDist = (targetPos - spawnPos).HorizontalLength;
			var speed = this.sbm.Info.Speed;

			if (this.sbm.Info.Acceleration > 0)
			{
				// Simulate velocity profile to compute total flight ticks.
				// Missile starts at InitialSpeedPercent of Speed and accelerates by Acceleration/tick.
				var initSpeed = speed * this.sbm.Info.InitialSpeedPercent / 100f;
				var accel = this.sbm.Info.Acceleration;

				float simDist = 0f;
				float simSpeed = initSpeed;
				int simTicks = 0;
				while (simDist < hDist && simTicks < 10000)
				{
					simSpeed = Math.Min(simSpeed + accel, speed);
					simDist += simSpeed;
					simTicks++;
				}

				totalArcTicks = Math.Max(simTicks, 1);
				currentSpeed = initSpeed;
			}
			else
			{
				totalArcTicks = Math.Max(hDist / speed, 1);
				currentSpeed = speed;
			}

			// Peak height of the arc, derived from LaunchAngle and horizontal distance.
			// For a parabolic arc: peak = range * tan(angle) / 4
			var tan = this.sbm.Info.LaunchAngle.Tan();
			arcPeakHeight = (int)((long)hDist * tan / (4 * 1024));
		}

		// Parabolic arc height: peaks at progress=0.5, zero at endpoints.
		int GetArcHeight(float progress)
		{
			return (int)(4f * arcPeakHeight * progress * (1f - progress));
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
			var hStep = (int)(hDist * eps * 2);
			if (hStep < 1)
				return horizontalFacing;

			// Pitch factor: how much the missile is climbing or diving
			var maxPitch = 0.4f * visualPitchMul;
			var pitchFactor = Math.Clamp((float)dh / (hStep * 4), -maxPitch, maxPitch);

			return ApplyIsometricPitch(pitchFactor);
		}

		// Apply pitch as a facing offset using isometric projection.
		WAngle ApplyIsometricPitch(float pitchFactor)
		{
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

				// Erection animation: tilt from horizontal to near-vertical during rise
				if (sbm.Info.LaunchRiseErect && visualPitchMul > 0f)
				{
					var erectPitch = riseT * 0.4f * visualPitchMul;
					sbm.Facing = ApplyIsometricPitch(erectPitch);
				}
				else
					sbm.Facing = horizontalFacing;

				ticks++;
				return false;
			}

			// Phase 2: Parabolic arc flight
			if (horizontalProgress >= 1f)
			{
				sbm.SetPosition(self, targetPos);
				Queue(new CallFunc(() => self.Kill(self)));
				return true;
			}

			// Update velocity: accelerate toward max speed each tick
			if (sbm.Info.Acceleration > 0)
				currentSpeed = Math.Min(currentSpeed + sbm.Info.Acceleration, sbm.Info.Speed);

			// Accumulate horizontal progress based on current speed
			if (hDist > 0)
				horizontalProgress += currentSpeed / hDist;
			else
				horizontalProgress = 1f;

			horizontalProgress = Math.Clamp(horizontalProgress, 0f, 1f);

			// Use horizontalProgress for both position and arc height —
			// this keeps the parabolic shape correct regardless of speed variation.
			var progress = horizontalProgress;

			// Horizontal position
			var hx = spawnPos.X + (int)((long)(targetPos.X - spawnPos.X) * (int)(progress * 1024) / 1024);
			var hy = spawnPos.Y + (int)((long)(targetPos.Y - spawnPos.Y) * (int)(progress * 1024) / 1024);

			// Vertical position: base terrain Z interpolated + parabolic arc + decaying rise offset
			var baseZ = spawnPos.Z + (int)((long)(targetPos.Z - spawnPos.Z) * (int)(progress * 1024) / 1024);
			var arcHeight = GetArcHeight(progress);
			var riseDecay = launchRiseHeight > 0 ? (int)(launchRiseHeight * (1f - progress)) : 0;

			var pos = new WPos(hx, hy, baseZ + arcHeight + riseDecay);

			// Ensure missile never goes below terrain
			var terrainAlt = self.World.Map.DistanceAboveTerrain(pos);
			if (terrainAlt.Length < 0)
				pos = new WPos(pos.X, pos.Y, pos.Z - terrainAlt.Length + 1);

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
