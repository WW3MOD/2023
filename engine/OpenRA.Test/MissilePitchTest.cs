#region Copyright & License Information
/*
 * WW3MOD ballistic-missile visual-pitch tests.
 *
 * WHAT IS BEING PINNED: that a ballistic missile's nose is drawn along the line its own smoke trail
 * leaves behind it, and not a few degrees off it.
 *
 * The user's report was "the missile is flying belly first a little bit — track the line represented
 * by the smoke/exhaust". The defect was a sign-and-magnitude error, which is exactly the kind of
 * thing prose cannot hold still and a test can, so the contract is asserted here as an angle in
 * degrees rather than as agreement with the implementation's own arithmetic.
 *
 * TWO PROJECTIONS ARE INVOLVED and the tests re-derive both from first principles in doubles, so
 * agreement with BallisticMissileFly is evidence rather than tautology:
 *
 *   POSITION, which is what the trail draws. WorldRenderer.ScreenPosition is (X, Y - Z) scaled by
 *   TileSize / TileScale, and mod.yaml ships TileSize 24,24 with Type: Rectangular, so both screen
 *   axes take the same scale and screen velocity is proportional to (vx, vy - vz).
 *
 *   THE ARTWORK, which is what the nose draws. iskander-missile.shp is not a 1:1 ground rotation —
 *   measured over all 32 frames it is a ground rotation with its screen y multiplied by 0.566. So
 *   the facing you ask for is NOT the angle you get, and by a factor that runs from 0.566 to 1.767
 *   depending on heading.
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

		// A WAngle is 1024 steps of 0.352 degrees, and the model crosses that grid twice: once
		// quantising the tilt, once quantising the facing it comes back as. Over the sweep below the
		// worst drawn-angle residual is 0.929 degrees (facing 544, slope 0.33) and the mean signed
		// residual is 0.180 degrees — about half a WAngle unit, and unchanged across every
		// implementation variant tried, so it is the floor of the integer angle system rather than a
		// lean in the model.
		//
		// Both sit far below what the art can show: the sequence has 32 facings, so one sprite frame
		// is 11.25 degrees of facing, and the worst residual here is under a tenth of that.
		//
		// Deliberately NOT asserted in facing units. That metric looks tighter and is worse: where the
		// artwork compresses hardest, near due east/west, four units of facing are under a degree of
		// drawn nose — so a facing-unit bound flags cases the player cannot see and stays quiet on the
		// ones they can. The bias check is what guards against systematic drift, which is the shape
		// the original defect had.
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
		/// multiplied by the measured squash. This is the nose the player actually sees.
		/// </summary>
		static double DrawnDegrees(WAngle facing)
		{
			var (x, y) = Heading(facing);
			return Math.Atan2(y * Squash / 1000.0, x) * 180 / Math.PI;
		}

		/// <summary>
		/// Screen angle the TRAIL draws for a path climbing at <paramref name="slope"/> along
		/// <paramref name="facing"/>: the position projection, (vx, vy - vz), unsquashed.
		/// </summary>
		static double TrailDegrees(WAngle facing, double slope)
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

		/// <summary>Every facing the 32-frame art can actually distinguish, plus the cardinals.</summary>
		static WAngle[] AllArtFacings()
		{
			var facings = new WAngle[32];
			for (var i = 0; i < 32; i++)
				facings[i] = new WAngle(i * 32);

			return facings;
		}

		[Test]
		public void LevelFlightIsLeftExactlyAlone()
		{
			// The invariant the whole model hangs off: with no climb there is no tilt, to the bit.
			// It matters because every other actor in the mod uses its plain ground facing, so a
			// missile that drifted here would look rotated against the launcher that fired it.
			foreach (var facing in AllArtFacings())
			{
				Assert.That(BallisticMissileFly.ScreenAlignedFacing(facing, 0f, 1f, Squash), Is.EqualTo(facing),
					$"a level path must return its ground heading unchanged (facing {facing.Angle})");

				// And at the apex of a symmetric arc, which is where slope 0 comes from in flight.
				const int ArcPeak = 4000;
				const int HDist = 60000;
				var apexSlope = 4f * ArcPeak * (1f - 2f * 0.5f) / HDist;
				Assert.That(apexSlope, Is.Zero, "the arc derivative must vanish at progress 0.5");
				Assert.That(BallisticMissileFly.ScreenAlignedFacing(facing, apexSlope, 1f, Squash), Is.EqualTo(facing));
			}
		}

		[Test]
		public void TheNoseIsDrawnAlongTheSmokeTrail()
		{
			// The user's actual criterion, asserted directly: however much the trail's screen
			// direction rotates away from the ground track, the DRAWN nose rotates by the same.
			var bias = 0.0;
			var samples = 0;
			var worst = 0.0;
			var worstCase = "";

			foreach (var facing in AllArtFacings())
			{
				foreach (var slope in new[] { 0.1, -0.1, 0.2, -0.2, 0.4, -0.4, 0.8, -0.8, 0.33, -0.33, 0.52, -0.52 })
				{
					var result = BallisticMissileFly.ScreenAlignedFacing(facing, (float)slope, 1f, Squash);

					var drawnTilt = Normalise(DrawnDegrees(result) - DrawnDegrees(facing));
					var trailTilt = Normalise(TrailDegrees(facing, slope) - TrailDegrees(facing, 0));

					var error = drawnTilt - trailTilt;
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
				$"drawn nose must follow the trail; worst was {worst:F3} deg at {worstCase}");

			// Rounding scatters either way and largely cancels; a model that LEANS is what the report
			// was about, and is what this catches.
			Assert.That(Math.Abs(bias / samples), Is.LessThan(MaxMeanBiasDegrees),
				$"the tilt must not be systematically over- or under-done across headings ({bias / samples:F4} deg)");
		}

		[Test]
		public void ClimbAndDiveTiltOppositeWaysAndDueNorthTiltsNotAtAll()
		{
			// Due west: climbing swings the apparent heading toward the top of the screen, which is
			// toward facing 0 — so the facing must DECREASE. Diving must do the mirror image.
			var west = new WAngle(256);
			var climbing = BallisticMissileFly.ScreenAlignedFacing(west, 0.4f, 1f, Squash);
			var diving = BallisticMissileFly.ScreenAlignedFacing(west, -0.4f, 1f, Squash);

			Assert.That(climbing.Angle, Is.LessThan(west.Angle), "climbing due west must tilt the nose up-screen");
			Assert.That(diving.Angle, Is.GreaterThan(west.Angle), "diving due west must tilt the nose down-screen");
			Assert.That(west.Angle - climbing.Angle, Is.EqualTo(diving.Angle - west.Angle).Within(1),
				"equal climb and dive must tilt by equal and opposite amounts");

			// Due east is the mirror of due west: the same climb rotates the facing the other way.
			var east = new WAngle(768);
			Assert.That(BallisticMissileFly.ScreenAlignedFacing(east, 0.4f, 1f, Squash).Angle,
				Is.GreaterThan(east.Angle), "climbing due east must tilt the nose up-screen too");

			// Due north and due south are the degenerate case and must not move at all: the missile
			// is already travelling straight up or down the screen, so gaining altitude cannot change
			// the direction it appears to be going.
			foreach (var facing in new[] { new WAngle(0), new WAngle(512) })
				foreach (var slope in new[] { 0.4f, -0.4f, 0.9f })
					Assert.That(BallisticMissileFly.ScreenAlignedFacing(facing, slope, 1f, Squash), Is.EqualTo(facing),
						$"a missile flying straight up/down the screen has no apparent tilt (facing {facing.Angle})");
		}

		[Test]
		public void TheArtworkSquashIsWhatMakesTheTiltCorrect()
		{
			// The point of SpriteFacingSquash, stated as a number rather than as prose: ignoring it
			// leaves the drawn tilt wrong by up to a factor of 1/0.566 even though the geometry that
			// produced it is exact. This is the term the old hand-picked constants were standing in
			// for, and the reason a third hand-picked constant could not have fixed the report.
			const float Slope = 0.4f;

			// At SpriteFacingSquash 1000 the model degenerates to the pure screen-velocity yaw.
			foreach (var facing in AllArtFacings())
			{
				var (x, y) = Heading(facing);
				var exact = new WVec((int)(x * 100000), (int)(y * 100000 - Slope * 100000), 0).Yaw;
				Assert.That(BallisticMissileFly.ScreenAlignedFacing(facing, Slope, 1f, 1000).Angle,
					Is.EqualTo(exact.Angle).Within(1),
					$"squash 1000 must reduce to WVec(vx, vy - vz, 0).Yaw (facing {facing.Angle})");
			}

			// And that pure-geometry answer, drawn by art that IS squashed, misses the trail badly.
			var west = new WAngle(256);
			var naive = BallisticMissileFly.ScreenAlignedFacing(west, Slope, 1f, 1000);
			var corrected = BallisticMissileFly.ScreenAlignedFacing(west, Slope, 1f, Squash);

			var trailTilt = Normalise(TrailDegrees(west, Slope) - TrailDegrees(west, 0));
			var naiveDrawn = Normalise(DrawnDegrees(naive) - DrawnDegrees(west));
			var correctedDrawn = Normalise(DrawnDegrees(corrected) - DrawnDegrees(west));

			Assert.That(correctedDrawn, Is.EqualTo(trailTilt).Within(ToleranceDegrees));
			Assert.That(Math.Abs(naiveDrawn), Is.LessThan(Math.Abs(trailTilt) * 0.7),
				"ignoring the artwork squash must visibly under-tilt at due west — that is why the field exists");
		}

		[Test]
		public void VisualPitchMultiplierScalesTheTiltAndZeroDisablesIt()
		{
			var facing = new WAngle(192);
			const float Slope = 0.5f;

			Assert.That(BallisticMissileFly.ScreenAlignedFacing(facing, Slope, 0f, Squash), Is.EqualTo(facing),
				"VisualPitchMultiplier 0 must mean no tilt at all");

			var full = BallisticMissileFly.ScreenAlignedFacing(facing, Slope, 1f, Squash);
			var half = BallisticMissileFly.ScreenAlignedFacing(facing, Slope, 0.5f, Squash);

			var fullTilt = Normalise(DrawnDegrees(full) - DrawnDegrees(facing));
			var halfTilt = Normalise(DrawnDegrees(half) - DrawnDegrees(facing));

			Assert.That(Math.Sign(halfTilt), Is.EqualTo(Math.Sign(fullTilt)), "halving must not flip the tilt");
			Assert.That(Math.Abs(halfTilt), Is.LessThan(Math.Abs(fullTilt)), "halving must reduce the tilt");
			Assert.That(halfTilt, Is.EqualTo(fullTilt / 2).Within(ToleranceDegrees * 2),
				"the multiplier is a percentage of the true tilt, so 50 must land near half of it");
		}

		[Test]
		public void TheDescentRampCountsAsMuchAsTheArc()
		{
			// The support-power missiles spawn at SpawnAltitude off the map edge (8c0 to 31c0,
			// mods/ww3mod/rules/player.yaml) and descend that whole altitude on the way in. On the
			// high-yield nuke that ramp is several times the arc it is superimposed on, so a model
			// that reads only the arc points the nose UP through a flight that is going DOWN — which
			// is the "flying belly first" the report described, at its largest.
			const int HDist = 61440;          // 60 cells
			const int SpawnAltitude = 31744;  // 31c0
			const int ArcPeak = (int)(HDist * 191L / 4096);   // LaunchAngle 30 -> Tan() 191

			var arcOnly = 4f * ArcPeak * (1f - 2f * 0f) / HDist;
			var withRamp = (4f * ArcPeak * (1f - 2f * 0f) - SpawnAltitude) / HDist;

			Assert.That(arcOnly, Is.GreaterThan(0), "the arc alone claims the missile is climbing at launch");
			Assert.That(withRamp, Is.LessThan(0), "including the ramp, it is descending from the first tick");
			Assert.That(Math.Abs(withRamp), Is.GreaterThan(Math.Abs(arcOnly)),
				"and the ramp is the larger term, so it cannot be treated as a correction");

			// Which flips the sign of the tilt, not merely its size.
			var west = new WAngle(256);
			Assert.That(BallisticMissileFly.ScreenAlignedFacing(west, arcOnly, 1f, Squash).Angle,
				Is.LessThan(west.Angle));
			Assert.That(BallisticMissileFly.ScreenAlignedFacing(west, withRamp, 1f, Squash).Angle,
				Is.GreaterThan(west.Angle));
		}
	}
}
