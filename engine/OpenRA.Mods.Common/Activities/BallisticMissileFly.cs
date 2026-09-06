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
		readonly int spriteFacingSquash;
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
			spriteFacingSquash = this.sbm.Info.SpriteFacingSquash;

			hDist = (targetPos - spawnPos).HorizontalLength;
			var speed = this.sbm.Info.Speed;

			totalArcTicks = EstimateArcTicks(this.sbm.Info, hDist);
			currentSpeed = this.sbm.Info.Acceleration > 0
				? speed * this.sbm.Info.InitialSpeedPercent / 100f
				: speed;

			// Peak height of the arc, derived from LaunchAngle and horizontal distance.
			// For a parabolic arc: peak = range * tan(angle) / 4
			var tan = this.sbm.Info.LaunchAngle.Tan();
			arcPeakHeight = (int)((long)hDist * tan / (4 * 1024));
		}

		/// <summary>
		/// Ticks the arc flight takes to cover <paramref name="hDist"/>, NOT counting the pre-launch
		/// phase (<see cref="Traits.BallisticMissileInfo.PreLaunchTicks"/>). Extracted from the
		/// constructor so that a caller needing the flight time BEFORE the activity exists (see
		/// MissileStrikePower, for its camera and beacon timings) reads the activity's own
		/// arithmetic instead of keeping a second copy of it in step by hand.
		/// </summary>
		public static int EstimateArcTicks(BallisticMissileInfo info, int hDist)
		{
			var speed = info.Speed;
			if (info.Acceleration <= 0)
				return Math.Max(hDist / speed, 1);

			// Simulate velocity profile to compute total flight ticks.
			// Missile starts at InitialSpeedPercent of Speed and accelerates by Acceleration/tick.
			var initSpeed = speed * info.InitialSpeedPercent / 100f;
			var accel = info.Acceleration;
			var termSpeed = info.TerminalSpeed;
			var termAccel = info.TerminalAcceleration > 0
				? info.TerminalAcceleration : accel;

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

			return Math.Max(simTicks, 1);
		}

		// Parabolic arc height: peaks at progress=0.5, zero at endpoints.
		int GetArcHeight(float progress)
		{
			return (int)(4f * arcPeakHeight * progress * (1f - progress));
		}

		// Slope (dz per unit of horizontal distance) of the flight path at a given progress.
		// TWO terms, and the second one was missing from the old model:
		//  * the parabolic arc's analytical derivative -- d/dprogress of GetArcHeight is
		//    4 * arcPeakHeight * (1 - 2*progress), divided by hDist to get dz/dx;
		//  * the constant ramp from the launch altitude down to the target, which Tick() applies to
		//    every position it sets, through baseZ.
		// For a launcher firing off its own TEL that ramp is ~0 and the arc is the whole story. But
		// MissileStrikePower spawns its missile at SpawnAltitude off the map edge -- 8c0 to 31c0 in
		// mods/ww3mod/rules/player.yaml -- and the missile descends that entire altitude on the way
		// in. On the high-yield nuke, 31 cells of drop across a ~60-cell shot is a slope of -0.52
		// against an arc peak of under 3 cells: the path descends from the first tick to the last,
		// and the old arc-only model pitched the nose UP through the first half of it.
		float GetSlope(float progress)
		{
			if (hDist < 1)
				return 0f;

			var slope = (4f * arcPeakHeight * (1f - 2f * progress) + (targetPos.Z - spawnPos.Z)) / hDist;

			// Overflow guard, NOT tuning: a degenerate near-zero hDist under a large altitude drop
			// would otherwise scale past int range inside ScreenAlignedFacing. +-16 is 86.4 degrees;
			// nothing the mod ships comes within a factor of ten of it.
			return Math.Clamp(slope, -16f, 16f);
		}

		WAngle GetFacing(float progress)
		{
			return ScreenAlignedFacing(horizontalFacing, GetSlope(progress), visualPitchMul, spriteFacingSquash);
		}

		// WHY THIS IS A PROJECTION AND NOT A FUDGE FACTOR.
		//
		// This replaced two hand-picked constants -- a 0.8125 scale and a 0.775 clamp -- sitting on
		// top of a 2048 * u * (1-u) facing weight. All three were compensating for the same two
		// errors at once, and a third hand-picked constant would not have fixed either of them.
		//
		// There are TWO projections in play, and they are not the same one.
		//
		// 1. POSITION -- what the smoke trail draws. WorldRenderer.ScreenPosition is
		//        screen = (X, Y - Z) * TileSize / TileScale
		//    and mods/ww3mod/mod.yaml sets TileSize 24,24 with Type: Rectangular, so screen x and
		//    screen y take the SAME scale and the ground plane maps to the screen 1:1. Altitude and
		//    depth share the screen y axis, which is the whole reason a climbing missile appears to
		//    travel in a different direction from its ground heading. Trail sprites are placed at
		//    CenterPosition, so the line the player compares the nose against is exactly this.
		//
		//    Hence screen velocity is proportional to (vx, vy - vz), and the facing that projects
		//    1:1 onto it is just WVec(vx, vy - vz, 0).Yaw. Exact. The horizontal speed is a common
		//    factor of both components and cancels, so only the SLOPE survives -- and no clamp is
		//    needed, because a Yaw cannot run away the way a raw slope can.
		//
		// 2. THE ARTWORK -- what the nose draws. The facing set every ballistic missile in the mod
		//    aliases (iskander-missile.shp, 32 facings, sequences.yaml) is NOT a 1:1 ground rotation;
		//    it was drawn from a camera above the horizon. Measured as the principal axis of the
		//    opaque pixels of all 32 frames, frame F is drawn at the screen angle of F's ground
		//    vector with its y component multiplied by 0.566 -- a least-squares fit over the whole
		//    set with 0.47 degrees RMS residual, i.e. a camera 34.5 degrees above the ground plane.
		//    SpriteFacingSquash carries that number; 1000 means "true 1:1 art" and costs nothing.
		//
		//    What matters is not the constant but its derivative. Near due north/south one unit of
		//    facing swings the drawn nose by 1/0.566 = 1.77x what the facing says; near due east/west
		//    by 0.566x. That 3.1x span is larger than any error in the geometry, which is why the old
		//    model read as over-tilted on some headings and under-tilted on others.
		//
		// So the tilt is MEASURED in projection 1 and APPLIED in projection 2:
		//
		//     tilt   = WVec(vx, vy - vz, 0).Yaw - horizontalFacing        (what the trail shows)
		//     facing = unsquash( squash(horizontalFacing) rotated by tilt )
		//
		// where squash multiplies the y component by SpriteFacingSquash and unsquash divides by it.
		//
		// Level flight is left exactly alone: at slope 0 the tilt is 0, squash and unsquash cancel,
		// and the result is horizontalFacing to the bit. That invariant is load-bearing -- every
		// other actor in the mod uses its plain ground facing, so a missile that did not would look
		// rotated against the launcher that fired it.
		//
		// What this deliberately does NOT do is put the nose on the trail in LEVEL flight. It cannot:
		// the art's 0.566 squash already draws a level missile on a diagonal up to 16 degrees off its
		// ground track, and that is true of every unit in the mod rather than of missiles. Correcting
		// it here would rotate all six missiles in level flight and single them out from everything
		// else on screen. Only the tilt is corrected, because only the tilt was wrong.

		/// <summary>
		/// The facing to ask the sprite for so its nose is drawn along the missile's apparent
		/// direction of travel. Public and static for the same reason <see cref="EstimateArcTicks"/>
		/// is: it is pure arithmetic, and a test that pins it should exercise this code rather than
		/// keep a second copy of it in step by hand.
		/// </summary>
		/// <param name="horizontalFacing">Ground heading from launch point to target.</param>
		/// <param name="slope">dz per unit of horizontal distance along the flight path.</param>
		/// <param name="visualPitchMul">
		/// <see cref="Traits.BallisticMissileInfo.VisualPitchMultiplier"/> over 100. 1 puts the nose
		/// exactly on the trail; less leans it back toward the flat ground heading.
		/// </param>
		/// <param name="spriteFacingSquash">
		/// <see cref="Traits.BallisticMissileInfo.SpriteFacingSquash"/>.
		/// </param>
		public static WAngle ScreenAlignedFacing(WAngle horizontalFacing, float slope, float visualPitchMul, int spriteFacingSquash)
		{
			if (visualPitchMul <= 0f || spriteFacingSquash <= 0)
				return horizontalFacing;

			// Horizontal velocity at an arbitrary fixed magnitude. The real speed is a common factor
			// of both components of every vector below and cancels out of the angles taken from them,
			// so it never has to be known here. Large enough that the integer divisions below stay
			// well under half a WAngle unit of error.
			const int Scale = 1 << 20;
			var h = new WVec(0, -Scale, 0).Rotate(WRot.FromYaw(horizontalFacing));
			var rise = (int)(slope * Scale);

			// (1) The tilt the trail actually shows, taken in the position projection: screen velocity
			// is (vx, vy - vz), so the yaw that projects 1:1 onto it is that vector's own Yaw.
			//
			// Differenced against h.Yaw rather than against horizontalFacing, and every other angle
			// below is likewise a difference against another angle off the same vector, with the final
			// answer applied as an offset to horizontalFacing rather than rebuilt from scratch. Rotate
			// and Yaw both go through 1024-step integer tables, so h.Yaw is not always horizontalFacing
			// to the unit; differencing cancels that bias, which is what makes level flight exact
			// rather than merely close.
			var tilt = (new WVec(h.X, h.Y - rise, 0).Yaw - h.Yaw).Angle;
			if (tilt > 512)
				tilt -= 1024;

			tilt = (int)(tilt * visualPitchMul);
			if (tilt == 0)
				return horizontalFacing;

			// (2) Apply it in the artwork's angle space and convert back to a facing. At
			// SpriteFacingSquash 1000 both scalings are the identity and this is a plain rotation.
			var squashed = new WVec(h.X, (int)((long)h.Y * spriteFacingSquash / 1000), 0);
			var drawn = squashed.Rotate(WRot.FromYaw(new WAngle(tilt)));

			// Both yaws are read back through the SAME squash-and-unsquash round trip, and the answer
			// is the difference between them applied to horizontalFacing. Symmetric by construction:
			// whatever the conversion rounds away it rounds away from both sides.
			var baseYaw = new WVec(squashed.X, (int)((long)squashed.Y * 1000 / spriteFacingSquash), 0).Yaw;
			var tiltedYaw = new WVec(drawn.X, (int)((long)drawn.Y * 1000 / spriteFacingSquash), 0).Yaw;

			return horizontalFacing + (tiltedYaw - baseYaw);
		}

		public override bool Tick(Actor self)
		{
			// Phase 1: Pre-launch (stationary, optional erection + post-erect wait)
			// The missile stays at spawnPos for launchRiseTicks + PostErectionWaitTicks ticks total.
			// During the rise: tilts from horizontal toward the arc's initial pitch angle.
			// After the rise: holds the erected pose until the wait period ends, then ignites.
			var totalPrelaunchTicks = sbm.Info.PreLaunchTicks;
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

					// The erected pose is the arc's own initial tangent, so the rail attitude flows
					// straight into the first tick of flight instead of snapping. Taken from LaunchAngle
					// rather than from GetSlope(0) so that a launcher which ever did spawn at altitude
					// would still erect to the angle its rail is at, not to its net glide.
					var launchSlope = sbm.Info.LaunchAngle.Tan() / 1024f;
					sbm.Facing = ScreenAlignedFacing(horizontalFacing, launchSlope * erectT, visualPitchMul, spriteFacingSquash);

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
