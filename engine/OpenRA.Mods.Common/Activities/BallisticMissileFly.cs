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

		// Steepest path slope the facing maths is evaluated at; see the Scale note in
		// ScreenAlignedFacing for why this is bounded at all.
		const float MaxSlope = 8f;

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
			// would otherwise push ScreenAlignedFacing past the range WAngle.ArcTan can evaluate
			// without wrapping (see the Scale note there). MaxSlope 8 is 82.9 degrees, steeper than
			// anything the mod can produce short of firing across a single cell.
			return Math.Clamp(slope, -MaxSlope, MaxSlope);
		}

		WAngle GetFacing(float progress)
		{
			return ScreenAlignedFacing(horizontalFacing, GetSlope(progress), visualPitchMul, spriteFacingSquash);
		}

		// WHY THIS IS A PROJECTION AND NOT A FUDGE FACTOR.
		//
		// This replaced two hand-picked constants -- a 0.8125 scale and a 0.775 clamp -- sitting on
		// top of a 2048 * u * (1-u) facing weight. All three were compensating for the same thing,
		// and a third hand-picked constant would not have fixed it.
		//
		// There are TWO projections in play, and they are not the same one.
		//
		// 1. POSITION -- what the smoke trail draws. WorldRenderer.ScreenPosition is
		//        screen = (X, Y - Z) * TileSize / TileScale
		//    and mods/ww3mod/mod.yaml sets TileSize 24,24 with Type: Rectangular, so screen x and
		//    screen y take the SAME scale and the ground plane maps to the screen 1:1. Altitude and
		//    depth share the screen y axis, which is the whole reason a climbing missile appears to
		//    travel in a different direction from its ground heading. LeavesTrailsCA places its puffs
		//    at CenterPosition, so the line the player compares the nose against is exactly this.
		//
		//    Screen velocity is therefore proportional to (vx, vy - vz). The horizontal speed is a
		//    common factor of both components and cancels, so only the SLOPE survives.
		//
		// 2. THE ARTWORK -- what the nose draws. The facing set every ballistic missile in the mod
		//    aliases (iskander-missile.shp, 32 facings, sequences.yaml) is NOT a 1:1 ground rotation;
		//    it was drawn from a camera above the horizon. Measured as the principal axis of the
		//    opaque pixels of all 32 frames, frame F is drawn at the screen angle of F's ground
		//    vector with its y component multiplied by 0.566 -- a least-squares fit over the whole
		//    set with 0.47 degrees RMS residual, i.e. a camera 34.5 degrees above the ground plane.
		//    SpriteFacingSquash carries that number; 1000 means "true 1:1 art" and costs nothing.
		//
		// PUT THOSE TOGETHER AND THE ANSWER IS ONE LINE. The art draws facing F at squash(g(F)),
		// where g is the ground unit vector. We want that drawn direction to BE the screen velocity:
		//
		//     squash(g(F)) parallel to (vx, vy - vz)
		//       =>  g(F) parallel to (vx, (vy - vz) / squash)
		//       =>  F = WVec(vx, (vy - vz) / squash, 0).Yaw
		//
		// One Yaw. No rotation composition, no tilt applied on top of a ground heading, no clamp --
		// a Yaw cannot run away the way a raw slope can.
		//
		// THE CORRECTION IS NOT ONLY A PITCH CORRECTION, and getting that wrong is what shipped a
		// visibly wrong diagonal on 2026-09-06. The previous model measured the tilt correctly in
		// projection 1 but then applied it as a ROTATION IN DRAWN SPACE on top of an unmodified
		// ground heading, which preserved "level flight returns horizontalFacing exactly". Rotating a
		// squashed vector and unsquashing the result is NOT the same operation as unsquashing the
		// true screen velocity. The two agree only when h.Y is zero -- due east and due west -- which
		// is precisely why an east/west spot check looked clean and confirmed nothing.
		//
		// On a 45-degree heading the ground vector is (-1,-1) at 225 degrees and the art draws it at
		// (-1,-0.566), which is 209.5 degrees. So a LEVEL missile on a diagonal is drawn 15.5 degrees
		// off its own ground track. That offset is present at every point of the flight, it does not
		// vanish as the arc flattens, and it is larger than most of the pitch corrections it was
		// sitting underneath.
		//
		// SO A LEVEL MISSILE ON A DIAGONAL IS NOW DRAWN AT A FACING THAT IS NOT ITS GROUND HEADING --
		// 84 instead of 128, a 44-unit swing at zero slope -- and that is correct rather than a
		// regression. The earlier objection to it was that it singles missiles out from every other
		// actor on screen. That objection is sound for a ground unit, whose apparent direction the
		// player judges against the terrain it drives over. It does not hold for a missile, which is
		// judged against its own smoke trail, and the trail is drawn in the position projection. Nose
		// and trail have to agree; nose and tank-facing convention do not.
		//
		// The four cardinals are the exception and are unchanged in level flight: at due east/west
		// h.Y is zero so the squash has nothing to scale, and at due north/south h.X is zero so the
		// direction is straight up or down the screen whatever the y component becomes. Which is also
		// why north and south never tilt at all, at any slope.

		/// <summary>
		/// The facing to ask the sprite for so its nose is DRAWN along the missile's apparent
		/// direction of travel -- the line its own smoke trail leaves behind it. Public and static
		/// for the same reason <see cref="EstimateArcTicks"/> is: it is pure arithmetic, and a test
		/// that pins it should exercise this code rather than keep a second copy in step by hand.
		/// </summary>
		/// <param name="horizontalFacing">Ground heading from launch point to target.</param>
		/// <param name="slope">dz per unit of horizontal distance along the flight path.</param>
		/// <param name="visualPitchMul">
		/// <see cref="Traits.BallisticMissileInfo.VisualPitchMultiplier"/> over 100. 1 puts the nose
		/// exactly on the trail; less leans it back toward the raw ground heading, which is how a
		/// ground actor is drawn and is what the missile looks like lying flat on its launcher.
		/// </param>
		/// <param name="spriteFacingSquash">
		/// <see cref="Traits.BallisticMissileInfo.SpriteFacingSquash"/>.
		/// </param>
		public static WAngle ScreenAlignedFacing(WAngle horizontalFacing, float slope, float visualPitchMul, int spriteFacingSquash)
		{
			if (visualPitchMul <= 0f || spriteFacingSquash <= 0)
				return horizontalFacing;

			// Horizontal velocity at an arbitrary fixed magnitude. The real speed is a common factor
			// of both components below and cancels out of the angle taken from them, so it never has
			// to be known here.
			//
			// THE SIZE OF Scale IS A CORRECTNESS CONSTRAINT, not a precision preference.
			// WAngle.ArcTan evaluates `1024 * ay` in INT arithmetic while promoting only the other
			// operand (WAngle.cs:167, whose own comment concedes it "may still fail for unrealistically
			// large ax and ay"), so it silently wraps once either component exceeds 2^31/1024 =
			// 2,097,151 and returns a garbage angle. The worst case here is
			// (1 + MaxSlope) * Scale * 1000 / SpriteFacingSquash; at Scale 1<<16 and MaxSlope 8 that is
			// 1.04 million, half the ceiling.
			//
			// 1<<20 was used until 2026-09-06 and was over that line at |slope| > 1.0 -- reachable by
			// firing a support-power missile near your own map edge, where a 31c0 spawn altitude over
			// a short hDist gives a slope of -3 and the nose snapped to due east. Precision is not the
			// tradeoff it looks like: 1<<16 still resolves direction to 0.0009 degrees, against a
			// WAngle step of 0.352.
			const int Scale = 1 << 16;
			var h = new WVec(0, -Scale, 0).Rotate(WRot.FromYaw(horizontalFacing));
			var rise = (int)(slope * Scale);

			// Un-squash the screen velocity's y to get the GROUND direction whose artwork lands on
			// that screen direction, and read off its yaw.
			var screenY = (int)((long)(h.Y - rise) * 1000 / spriteFacingSquash);
			if (h.X == 0 && screenY == 0)
			{
				// Genuinely degenerate rather than a rounding accident: a missile flying due north or
				// south while climbing at exactly the rate that cancels it has NO apparent motion at
				// all, because screen y is (world Y - world Z). There is no direction to point along,
				// so keep the ground heading rather than letting Yaw return an arbitrary north.
				return horizontalFacing;
			}

			var exact = new WVec(h.X, screenY, 0).Yaw;
			if (visualPitchMul >= 1f)
				return exact;

			var delta = (exact - horizontalFacing).Angle;
			if (delta > 512)
				delta -= 1024;

			return horizontalFacing + new WAngle((int)(delta * visualPitchMul));
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
					//
					// erectT SCALES THE MULTIPLIER, not the slope, and that is what makes the two ends of
					// this animation both correct. At erectT 0 the multiplier is 0, so the missile is drawn
					// at its plain ground heading -- which is right, because a missile lying flat on its
					// launcher is a ground object and has to agree with the TEL sprite underneath it. At
					// erectT 1 the multiplier is the shipped value and the missile is drawn along the
					// direction it is about to fly, which is where the first tick of flight picks it up.
					// The erection is exactly the transition between those two conventions, so sweeping
					// the blend across it is the animation rather than an approximation of one.
					var launchSlope = sbm.Info.LaunchAngle.Tan() / 1024f;
					sbm.Facing = ScreenAlignedFacing(horizontalFacing, launchSlope, visualPitchMul * erectT, spriteFacingSquash);

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
