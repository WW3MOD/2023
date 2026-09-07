#region Copyright & License Information
/*
 * WW3MOD ballistic-missile visual-pitch tests.
 *
 * WHAT IS BEING PINNED, in one sentence: at every point of the flight the DRAWN nose direction
 * equals the missile's screen-velocity direction — the line its own smoke trail leaves behind it.
 *
 * THIS IS THE SECOND VERSION OF THIS FILE AND THE FIRST ONE IS THE REASON IT EXISTS. The version
 * that shipped on 2026-09-06 pinned a weaker invariant — "at zero slope the facing is exactly the
 * ground heading" — which is TRUE of the correct model only on the four cardinals. It passed
 * against a model that left the diagonal 15.5 degrees out, the user saw a missile "tilting
 * strangely for much of the flight path" on a north-west shot, and the test that should have caught
 * it was asserting the defect. A test that passes both before and after is the failure mode here,
 * so `TheDrawnNoseAlwaysPointsAlongTheScreenVelocity` and
 * `OnADiagonalTheFacingIsNotTheGroundHeadingEvenInLevelFlight` are both written to FAIL against that
 * earlier model, and their comments say by how much.
 *
 * TWO PROJECTIONS ARE INVOLVED and the tests re-derive both from first principles in doubles, so
 * agreement with BallisticMissileFly is evidence rather than tautology:
 *
 *   POSITION, which is what the trail draws. WorldRenderer.ScreenPosition is (X, Y - Z) scaled by
 *   TileSize / TileScale, and mod.yaml ships TileSize 24,24 with Type: Rectangular, so both screen
 *   axes take the same scale and screen velocity is proportional to (vx, vy - vz).
 *
 *   THE ARTWORK, which is what the nose draws. iskander-missile.shp is not a 1:1 ground rotation —
 *   measured over all 32 frames it is a ground rotation with its screen y multiplied by 0.566.
 *
 * The model is what falls out of setting those equal: squash(g(F)) parallel to (vx, vy - vz), i.e.
 * F = Yaw(vx, (vy - vz) / squash). Every assertion below is a statement about DRAWN angles, because
 * the drawn angle is the only thing the player can see; a facing on its own means nothing until it
 * has been through the artwork.
 *
 * These tests take no World; ScreenAlignedFacing is public and static precisely so they need none.
 */
#endregion

using System;
using NUnit.Framework;
using OpenRA.Mods.Common.Activities;

namespace OpenRA.Test
{
	[TestFixture]
	public class MissilePitchTest
	{
		// mods/ww3mod/rules/ingame/vehicles*.yaml — SpriteFacingSquash on every ballistic missile.
		const int Squash = 566;

		// A WAngle is 1024 steps of 0.352 degrees, and the drawn angle can amplify one step by up to
		// 1/0.566 near due north/south. Measured worst case over the sweeps below is 0.62 degrees.
		// That is a bound on the ANGLE SYSTEM, not on the model, and it sits far below what the art
		// can show: the sequence has 32 facings, so one sprite frame is 11.25 degrees of facing.
		const double ToleranceDegrees = 1.0;
		const double MaxMeanBiasDegrees = 0.25;

		/// <summary>The world-XY heading unit vector for a facing, as WVec.Yaw defines it.</summary>
		static (double X, double Y) Heading(WAngle facing)
		{
			var phi = facing.Angle * 2 * Math.PI / 1024;
			return (-Math.Sin(phi), -Math.Cos(phi));
		}

		/// <summary>
		/// Screen angle the ARTWORK draws a facing at: the ground direction with its screen y
		/// multiplied by the measured squash. This is the nose the player actually sees, and it is
		/// what every assertion in this file is written against.
		/// </summary>
		static double DrawnDegrees(WAngle facing)
		{
			var (x, y) = Heading(facing);
			return Math.Atan2(y * Squash / 1000.0, x) * 180 / Math.PI;
		}

		/// <summary>
		/// Screen angle of the missile's velocity for a path climbing at <paramref name="slope"/>
		/// along <paramref name="facing"/>: the position projection, (vx, vy - vz), unsquashed. This
		/// is the direction the smoke trail runs in.
		/// </summary>
		static double ScreenVelocityDegrees(WAngle facing, double slope)
		{
			var (x, y) = Heading(facing);
			return Math.Atan2(y - slope, x) * 180 / Math.PI;
		}

		static double Normalise(double degrees)
		{
			while (degrees > 180) degrees -= 360;
			while (degrees < -180) degrees += 360;
			return degrees;
		}

		/// <summary>Every facing the 32-frame art can actually distinguish.</summary>
		static WAngle[] AllArtFacings()
		{
			var facings = new WAngle[32];
			for (var i = 0; i < 32; i++)
				facings[i] = new WAngle(i * 32);

			return facings;
		}

		// Slopes covering climb and dive symmetrically, plus the steep descents the support-power
		// missiles actually fly (SpawnAltitude 31c0 over a 60-cell shot is -0.52). Zero is in the
		// list deliberately: level flight is a case of the invariant, not an exception to it.
		static readonly double[] Slopes = { 0.0, 0.1, -0.1, 0.2, -0.2, 0.4, -0.4, 0.8, -0.8, 0.33, -0.33, 0.52, -0.52 };

		[Test]
		public void TheDrawnNoseAlwaysPointsAlongTheScreenVelocity()
		{
			// THE invariant, and the only one that describes what the player sees. It replaces the
			// 2026-09-06 file's "level flight is left exactly alone", which was the same claim
			// restricted to the four headings where it happens to coincide with this one.
			//
			// This FAILS against that earlier model: at facing 128 with slope 0 it returned the ground
			// heading 128, which the art draws at 209.51 degrees against a screen velocity of 225.00.
			var bias = 0.0;
			var samples = 0;
			var worst = 0.0;
			var worstCase = "";

			foreach (var facing in AllArtFacings())
			{
				foreach (var slope in Slopes)
				{
					var result = BallisticMissileFly.ScreenAlignedFacing(facing, (float)slope, 1f, Squash);
					var error = Normalise(DrawnDegrees(result) - ScreenVelocityDegrees(facing, slope));

					bias += error;
					samples++;

					if (Math.Abs(error) > Math.Abs(worst))
					{
						worst = error;
						worstCase = $"facing {facing.Angle}, slope {slope}";
					}
				}
			}

			// Reported as the worst case over the whole sweep rather than aborting on the first one,
			// so a regression says how far off it got and where, not merely that it happened.
			Assert.That(Math.Abs(worst), Is.LessThan(ToleranceDegrees),
				$"drawn nose must lie along the screen velocity; worst was {worst:F3} deg at {worstCase}");

			// Rounding scatters either way and largely cancels; a model that LEANS is what both
			// reports were about, and is what this catches.
			Assert.That(Math.Abs(bias / samples), Is.LessThan(MaxMeanBiasDegrees),
				$"the correction must not be systematically over- or under-done ({bias / samples:F4} deg)");
		}

		[Test]
		public void OnADiagonalTheFacingIsNotTheGroundHeadingEvenInLevelFlight()
		{
			// The specific regression, pinned as a number rather than as prose. The foreshortening is
			// NOT a tilt: it does not vanish as the arc flattens, so a level missile on a diagonal
			// still has to be drawn off its ground heading to sit on its own trail.
			//
			// This is the assertion the 2026-09-06 model fails outright — it returned 128 here.
			var diagonal = new WAngle(128);
			var level = BallisticMissileFly.ScreenAlignedFacing(diagonal, 0f, 1f, Squash);

			Assert.That(level, Is.Not.EqualTo(diagonal),
				"a level missile on a 45-degree heading must NOT be drawn at its ground heading — the "
				+ "art would put its nose 15.5 degrees off the trail");

			// 84 rather than 128: a 44-unit swing, nearly a cell and a half of sprite frame.
			Assert.That(level.Angle, Is.EqualTo(84).Within(2),
				"the facing that makes the art point along a level 45-degree ground track is 84");

			var groundTrack = ScreenVelocityDegrees(diagonal, 0);
			Assert.That(Normalise(DrawnDegrees(level) - groundTrack), Is.EqualTo(0).Within(ToleranceDegrees),
				"and it must draw the nose exactly along the ground track's screen projection");

			// The size of what was wrong, stated so it cannot quietly come back.
			Assert.That(Math.Abs(Normalise(DrawnDegrees(diagonal) - groundTrack)), Is.EqualTo(15.5).Within(0.5),
				"the ground heading itself draws 15.5 degrees off the track — that is the defect size");
		}

		[Test]
		public void TheFourCardinalsAreUnchangedInLevelFlightWhichIsWhySpotCheckingThemProvesNothing()
		{
			// Due east/west: h.Y is zero, so the squash has nothing to scale.
			// Due north/south: h.X is zero, so the direction is straight up or down the screen
			// whatever happens to the y component.
			// A viewer who checks only these sees a correct picture under a wrong model. That is
			// exactly how the 2026-09-06 version reached the user, so it is pinned as a property of
			// the geometry rather than left as a footnote.
			foreach (var cardinal in new[] { new WAngle(0), new WAngle(256), new WAngle(512), new WAngle(768) })
				Assert.That(BallisticMissileFly.ScreenAlignedFacing(cardinal, 0f, 1f, Squash), Is.EqualTo(cardinal),
					$"a level missile on a cardinal heading is drawn at its ground heading (facing {cardinal.Angle})");

			// Every OTHER art facing must move. If any of them does not, the squash is not being
			// applied and the diagonal defect is back.
			foreach (var facing in AllArtFacings())
			{
				if (facing.Angle % 256 == 0)
					continue;

				Assert.That(BallisticMissileFly.ScreenAlignedFacing(facing, 0f, 1f, Squash), Is.Not.EqualTo(facing),
					$"a level missile off the cardinals must be drawn off its ground heading (facing {facing.Angle})");
			}
		}

		[Test]
		public void NorthAndSouthNeverChangeAtAnySlope()
		{
			// The degenerate axis: a missile travelling straight up or down the screen keeps doing so
			// however fast it climbs, because gaining altitude only moves it along the same screen
			// axis it is already travelling on.
			foreach (var facing in new[] { new WAngle(0), new WAngle(512) })
				foreach (var slope in Slopes)
					Assert.That(BallisticMissileFly.ScreenAlignedFacing(facing, (float)slope, 1f, Squash), Is.EqualTo(facing),
						$"facing {facing.Angle} must be invariant to slope {slope}");

			// Including the true singularity, where the climb exactly cancels the southward travel and
			// the missile has no apparent motion at all. There is no direction to point along; the
			// ground heading is the only non-arbitrary answer.
			Assert.That(BallisticMissileFly.ScreenAlignedFacing(new WAngle(512), 1f, 1f, Squash), Is.EqualTo(new WAngle(512)),
				"a due-south missile climbing at slope 1 is motionless on screen and must not snap to north");
		}

		[Test]
		public void ClimbAndDiveMoveTheNoseOppositeWaysAboutTheLevelPose()
		{
			// Measured about the LEVEL DRAWN POSE, not about the ground heading — on a diagonal those
			// are 15.5 degrees apart, and measuring about the wrong one is what made the previous
			// version of this test agree with a broken model.
			foreach (var facing in new[] { new WAngle(256), new WAngle(128), new WAngle(192) })
			{
				var level = BallisticMissileFly.ScreenAlignedFacing(facing, 0f, 1f, Squash);
				var climbing = BallisticMissileFly.ScreenAlignedFacing(facing, 0.4f, 1f, Squash);
				var diving = BallisticMissileFly.ScreenAlignedFacing(facing, -0.4f, 1f, Squash);

				var up = Normalise(DrawnDegrees(climbing) - DrawnDegrees(level));
				var down = Normalise(DrawnDegrees(diving) - DrawnDegrees(level));

				Assert.That(Math.Sign(up), Is.Not.EqualTo(Math.Sign(down)),
					$"climb and dive must move the nose opposite ways (facing {facing.Angle})");
				Assert.That(Math.Abs(up), Is.GreaterThan(1.0),
					$"and by a visible amount (facing {facing.Angle})");
			}
		}

		[Test]
		public void SteepSlopesDoNotWrapTheAngleTable()
		{
			// WAngle.ArcTan evaluates `1024 * ay` in int arithmetic (WAngle.cs:167), so it wraps and
			// returns a garbage angle once a component exceeds 2^31/1024 = 2,097,151. Until
			// 2026-09-06 ScreenAlignedFacing built its vector at a magnitude that crossed that line at
			// |slope| > 1.0 and the nose snapped to due east.
			//
			// A slope past 1 is not hypothetical: DoomsdayStrike spawns each warhead 38c0 up and only
			// 5c0 back from its aim point, which is a constant slope of 7.6 on every map by
			// construction (DoomsdayStrike.cs:580-590).
			//
			// It USED to be reachable through MissileStrikePower too — an edge-cell spawn aimed near
			// your own edge gave a 10-cell hDist under a 31c0 drop, which is -3.1. As of 2026-09-07
			// that power flies a constant standoff of the map diagonal plus a margin, so its steepest
			// shipped slope is -0.54 (Tsar Bomba's 49c0 over the 90-cell standoff of the smallest
			// shipped map) and it no longer exercises this guard on its own. The guard stays
			// because the doomsday case does, and because the failure it prevents is silent.
			foreach (var facing in AllArtFacings())
			{
				foreach (var slope in new[] { 1.5f, -1.5f, 3.1f, -3.1f, 6f, -6f, 8f, -8f })
				{
					var result = BallisticMissileFly.ScreenAlignedFacing(facing, slope, 1f, Squash);
					var error = Normalise(DrawnDegrees(result) - ScreenVelocityDegrees(facing, slope));

					Assert.That(Math.Abs(error), Is.LessThan(ToleranceDegrees),
						$"steep slopes must stay exact, not wrap (facing {facing.Angle}, slope {slope}, off by {error:F2} deg)");
				}
			}
		}

		[Test]
		public void SquashOneThousandReducesToThePlainScreenVelocityYaw()
		{
			// With true 1:1 artwork the un-squash is the identity and the model collapses to
			// Yaw(vx, vy - vz) — the answer for a mod whose sprites are not foreshortened. Adopting
			// the trait therefore costs nothing until SpriteFacingSquash is set.
			foreach (var facing in AllArtFacings())
			{
				foreach (var slope in new[] { 0f, 0.4f, -0.52f })
				{
					var (x, y) = Heading(facing);
					var expected = new WVec((int)(x * 1000000), (int)((y - slope) * 1000000), 0).Yaw;

					Assert.That(BallisticMissileFly.ScreenAlignedFacing(facing, slope, 1f, 1000).Angle,
						Is.EqualTo(expected.Angle).Within(1),
						$"squash 1000 must reduce to WVec(vx, vy - vz, 0).Yaw (facing {facing.Angle}, slope {slope})");
				}
			}
		}

		[Test]
		public void VisualPitchMultiplierBlendsFromTheGroundHeadingToTheScreenAlignedOne()
		{
			// 0 is the ground-actor convention, which is also what the missile must look like while it
			// is still lying flat on its launcher — BallisticMissileFly sweeps the multiplier, not the
			// slope, across the erection so both ends of that animation are right.
			var facing = new WAngle(192);
			const float Slope = 0.5f;

			Assert.That(BallisticMissileFly.ScreenAlignedFacing(facing, Slope, 0f, Squash), Is.EqualTo(facing),
				"VisualPitchMultiplier 0 must mean the raw ground heading");

			var full = BallisticMissileFly.ScreenAlignedFacing(facing, Slope, 1f, Squash);
			var half = BallisticMissileFly.ScreenAlignedFacing(facing, Slope, 0.5f, Squash);

			var fullDelta = Normalise((full.Angle - facing.Angle) * 360.0 / 1024);
			var halfDelta = Normalise((half.Angle - facing.Angle) * 360.0 / 1024);

			Assert.That(Math.Sign(halfDelta), Is.EqualTo(Math.Sign(fullDelta)), "halving must not flip the correction");
			Assert.That(halfDelta, Is.EqualTo(fullDelta / 2).Within(0.5),
				"the multiplier is a percentage of the full correction, so 50 must land near half of it");
		}

		[Test]
		public void TheDescentRampCountsAsMuchAsTheArc()
		{
			// The support-power missiles spawn at SpawnAltitude off the map edge (8c0 to 31c0,
			// mods/ww3mod/rules/player.yaml) and descend that whole altitude on the way in. On the
			// high-yield nuke that ramp is several times the arc it is superimposed on, so a model
			// reading only the arc points the nose UP through a flight that is going DOWN.
			const int HDist = 61440;          // 60 cells
			const int SpawnAltitude = 31744;  // 31c0
			const int ArcPeak = (int)(HDist * 191L / 4096);   // LaunchAngle 30 -> Tan() 191

			var arcOnly = 4f * ArcPeak / HDist;
			var withRamp = (4f * ArcPeak - SpawnAltitude) / HDist;

			Assert.That(arcOnly, Is.GreaterThan(0), "the arc alone claims the missile is climbing at launch");
			Assert.That(withRamp, Is.LessThan(0), "including the ramp, it is descending from the first tick");
			Assert.That(Math.Abs(withRamp), Is.GreaterThan(Math.Abs(arcOnly)),
				"and the ramp is the larger term, so it cannot be treated as a correction");

			// Which flips which side of the level pose the nose sits on, not merely how far.
			var west = new WAngle(256);
			var level = DrawnDegrees(BallisticMissileFly.ScreenAlignedFacing(west, 0f, 1f, Squash));
			var climbing = DrawnDegrees(BallisticMissileFly.ScreenAlignedFacing(west, arcOnly, 1f, Squash));
			var descending = DrawnDegrees(BallisticMissileFly.ScreenAlignedFacing(west, withRamp, 1f, Squash));

			Assert.That(Math.Sign(Normalise(climbing - level)), Is.Not.EqualTo(Math.Sign(Normalise(descending - level))),
				"arc-only and arc-plus-ramp must put the nose on opposite sides of level");
		}
	}
}
