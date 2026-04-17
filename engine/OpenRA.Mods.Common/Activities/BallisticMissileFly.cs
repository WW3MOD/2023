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
		readonly WPos targetPos;
		readonly WAngle horizontalFacing;
		readonly int launchRiseTicks;
		readonly float visualPitchMul;
		readonly int totalArcTicks;

		// Updated at Phase 2 entry to capture the actual erected position so the
		// parabolic arc flies from where the missile is, not from the original spawn.
		WPos spawnPos;
		int arcPeakHeight;
		int hDist;

		float currentSpeed;
		float horizontalProgress;
		int ticks;
		bool phase2Initialized;

		public BallisticMissileFly(Actor self, Target t, BallisticMissile sbm = null)
		{
			this.sbm = sbm ?? self.Trait<BallisticMissile>();

			spawnPos = self.CenterPosition;
			targetPos = t.CenterPosition;
			horizontalFacing = (targetPos - spawnPos).Yaw;

			launchRiseTicks = this.sbm.Info.LaunchRiseTicks;
			visualPitchMul = this.sbm.Info.VisualPitchMultiplier / 100f;

			hDist = (targetPos - spawnPos).HorizontalLength;
			var speed = this.sbm.Info.Speed;

			if (this.sbm.Info.Acceleration > 0)
			{
				// Simulate velocity profile to compute total flight ticks.
				// Missile starts at InitialSpeedPercent of Speed and accelerates by Acceleration/tick.
				var initSpeed = speed * this.sbm.Info.InitialSpeedPercent / 100f;
				var accel = this.sbm.Info.Acceleration;
				var termSpeed = this.sbm.Info.TerminalSpeed;
				var termAccel = this.sbm.Info.TerminalAcceleration > 0
					? this.sbm.Info.TerminalAcceleration : accel;

				float simDist = 0f;
				float simSpeed = initSpeed;
				int simTicks = 0;
				while (simDist < hDist && simTicks < 10000)
				{
					var simProgress = simDist / hDist;
					if (simProgress >= 0.5f && termSpeed > 0)
						simSpeed = Math.Min(simSpeed + termAccel, termSpeed);
					else
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

		// Compute pitch factor from the parabolic arc derivative at a given progress.
		// The derivative of GetArcHeight = 4 * peak * (1 - 2*progress), so
		// slope = dh/dx = 4 * arcPeakHeight * (1 - 2*progress) / hDist.
		// We convert that slope to a pitch factor scaled by visualPitchMul.
		float GetPitchFactor(float progress)
		{
			if (hDist < 1)
				return 0f;

			// Analytical derivative: slope of arc relative to horizontal
			var slope = 4f * arcPeakHeight * (1f - 2f * progress) / hDist;

			// Scale by user multiplier and clamp to avoid extreme angles
			// Tilt scaled to 75% of the analytical-derivative jump (was over-tilting)
			var maxPitch = 0.775f * visualPitchMul;
			return Math.Clamp(slope * visualPitchMul * 0.8125f, -maxPitch, maxPitch);
		}

		// Compute facing with optional pitch tilt for isometric visual.
		WAngle GetFacing(float progress)
		{
			if (visualPitchMul <= 0f)
				return horizontalFacing;

			return ApplyIsometricPitch(GetPitchFactor(progress));
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
			// Phase 1: Pre-launch (stationary, optional erection + post-erect wait)
			// The missile stays at spawnPos for launchRiseTicks + PostErectionWaitTicks ticks total.
			// During the rise: tilts from horizontal toward the arc's initial pitch angle.
			// After the rise: holds the erected pose until the wait period ends, then ignites.
			var totalPrelaunchTicks = launchRiseTicks + sbm.Info.PostErectionWaitTicks;
			if (launchRiseTicks > 0 && ticks < totalPrelaunchTicks)
			{
				// Erection clamps to 1.0 once we're past launchRiseTicks (holding the erected pose).
				var riseT = launchRiseTicks > 0
					? Math.Clamp((float)ticks / launchRiseTicks, 0f, 1f)
					: 1f;

				if (sbm.Info.LaunchRiseErect && visualPitchMul > 0f)
				{
					// Cubic ease-in for a smooth, accelerating tilt.
					var erectT = riseT * riseT * riseT;
					var targetPitch = GetPitchFactor(0f);
					sbm.Facing = ApplyIsometricPitch(targetPitch * erectT);

					// Direct visual offset: at full erection sprite is at spawnPos + LaunchRiseErectVisualOffset
					// (rotated so X=forward aligns with horizontalFacing). Linear in erectT so the offset
					// tracks the rotation curve exactly.
					var visualOffset = sbm.Info.LaunchRiseErectVisualOffset;
					if (visualOffset != WVec.Zero)
					{
						// X is forward (along facing), Y is lateral (right of facing), Z is up.
						// Rotate the local (X,Y) into world space by horizontalFacing; Z is already world-up.
						var localXY = new WVec(visualOffset.Y, -visualOffset.X, 0).Rotate(WRot.FromYaw(horizontalFacing));
						var rotated = new WVec(localXY.X, localXY.Y, visualOffset.Z);
						var scaled = new WVec(
							(int)(rotated.X * erectT),
							(int)(rotated.Y * erectT),
							(int)(rotated.Z * erectT));
						sbm.SetPosition(self, spawnPos + scaled);
					}
					else
						sbm.SetPosition(self, spawnPos);
				}
				else
				{
					sbm.SetPosition(self, spawnPos);
					sbm.Facing = horizontalFacing;
				}

				ticks++;
				return false;
			}

			// Phase 1 complete — capture the erected position as the launch origin
			// so the parabolic arc starts from where the missile actually is
			// (spawnPos + LaunchRiseErectVisualOffset), not the original spawn.
			if (!phase2Initialized)
			{
				phase2Initialized = true;
				spawnPos = self.CenterPosition;
				hDist = (targetPos - spawnPos).HorizontalLength;
				var tan = sbm.Info.LaunchAngle.Tan();
				arcPeakHeight = (int)((long)hDist * tan / (4 * 1024));
			}

			// Ignite the rocket motor (grants IgnitionCondition).
			sbm.Ignite();

			// Phase 2: Parabolic arc flight — one smooth trajectory from spawn to target
			if (horizontalProgress >= 1f)
			{
				sbm.SetPosition(self, targetPos);
				Queue(new CallFunc(() => self.Kill(self)));
				return true;
			}

			// Update velocity: accelerate toward max speed, with optional terminal boost past apex
			var pastApex = horizontalProgress >= 0.5f;
			if (pastApex && sbm.Info.TerminalSpeed > 0)
			{
				var termAccel = sbm.Info.TerminalAcceleration > 0
					? sbm.Info.TerminalAcceleration : sbm.Info.Acceleration;
				if (termAccel > 0)
					currentSpeed = Math.Min(currentSpeed + termAccel, sbm.Info.TerminalSpeed);
			}
			else if (sbm.Info.Acceleration > 0)
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

			// Vertical position: base terrain Z interpolated + parabolic arc
			var baseZ = spawnPos.Z + (int)((long)(targetPos.Z - spawnPos.Z) * (int)(progress * 1024) / 1024);
			var arcHeight = GetArcHeight(progress);

			var pos = new WPos(hx, hy, baseZ + arcHeight);

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
