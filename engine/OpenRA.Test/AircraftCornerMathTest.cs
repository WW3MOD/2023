#region Copyright & License Information
/*
 * Tests for AircraftCornerMath — the release distance that lets a helicopter arc through an intermediate
 * waypoint at speed instead of braking to a stop on it.
 *
 * The interesting assertions here are the RELATIONS, not the magnitudes. A magnitude test pins whatever the
 * implementation happened to produce and passes forever; the relations (sharper turn releases earlier, the
 * result scales as speed squared, a terminal waypoint never releases at all) are the properties the feature
 * actually promises and the ones a wrong rewrite would break.
 *
 * The live helicopter configuration is used throughout so the numbers mean something: HELI is Speed 245 in
 * aircraft-america.yaml and MaxAcceleration is the Aircraft default of 10, unoverridden anywhere in mods/.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Activities;

namespace OpenRA.Test
{
	[TestFixture]
	public class AircraftCornerMathTest
	{
		const int HeliSpeed = 245;
		const int MaxAcceleration = 10;
		const int FullAggression = 100;

		// Long enough that the half-leg cap never binds in tests that are not about the cap.
		const int LongLeg = 1024 * 64;

		static readonly WAngle Straight = new(0);
		static readonly WAngle Shallow = new(64);          // 22.5 degrees
		static readonly WAngle RightAngle = new(256);      // 90 degrees
		static readonly WAngle Reversal = new(480);        // 168.75 degrees
		static readonly WAngle DefaultMaxDeflection = new(384);

		static int Release(WAngle deflection, int speed = HeliSpeed, int aggression = FullAggression, int legLength = LongLeg)
		{
			return AircraftCornerMath.ReleaseDistance(speed, MaxAcceleration, deflection, aggression, legLength);
		}

		[TestCase(0, 0, 0)]
		[TestCase(0, 256, 256)]
		[TestCase(256, 0, 256)]
		[TestCase(0, 768, 256)]
		[TestCase(100, 900, 224)]
		[TestCase(0, 512, 512)]
		public void DeflectionIsAnUnsignedMagnitudeAndWrapsTheShortWay(int inbound, int outbound, int expected)
		{
			// Wrapping matters: a leg heading 0 followed by one heading 768 is a quarter turn, not a
			// three-quarter one. And the result must be symmetric, because a signed value here would make the
			// release distance depend on which way the airframe rotates — which the counterclockwise WAngle
			// convention would then be free to invert.
			Assert.That(AircraftCornerMath.Deflection(new WAngle(inbound), new WAngle(outbound)).Angle, Is.EqualTo(expected));
			Assert.That(AircraftCornerMath.Deflection(new WAngle(outbound), new WAngle(inbound)).Angle, Is.EqualTo(expected));
		}

		[Test]
		public void TerminalWaypointNeverReleasesEarly()
		{
			// THE REGRESSION GUARD. The user's report is explicit that stopping exactly on the LAST waypoint
			// already works and must keep working. No deflection, aggression or cap combination may make a leg
			// with nothing queued after it eligible.
			foreach (var deflection in new[] { Straight, Shallow, RightAngle, Reversal })
				Assert.That(AircraftCornerMath.ShouldReleaseEarly(false, FullAggression, deflection, DefaultMaxDeflection),
					Is.False, $"a terminal waypoint became eligible at deflection {deflection.Angle}");
		}

		[Test]
		public void RightAngleTurnReleasesAndDoesSoWellBeforeBrakingWouldStart()
		{
			// Braking under CalculateAccelerationToWaypoint starts once sqrt(2*a*d) drops to the current speed,
			// i.e. at d = v^2/2a. The whole point of the feature is to be gone before that, so the release must
			// sit outside it. Geometry says the ratio is exactly 2*sin(45 deg) = 1.414.
			var brakingStart = HeliSpeed * HeliSpeed / (2 * MaxAcceleration);
			var release = Release(RightAngle);

			Assert.That(AircraftCornerMath.ShouldReleaseEarly(true, FullAggression, RightAngle, DefaultMaxDeflection), Is.True);
			Assert.That(release, Is.GreaterThan(brakingStart));

			// (v^2/a) * sin(45 deg) with sin in 1024ths: 6002 * 724 / 1024.
			Assert.That(release, Is.EqualTo(HeliSpeed * HeliSpeed / MaxAcceleration * new WAngle(128).Sin() / 1024).Within(2));
		}

		[Test]
		public void SharperTurnsReleaseEarlier()
		{
			// The user asked for exactly this: "a 90 degree turn needs an earlier release than a 10 degree one".
			// Monotonicity across the whole eligible band is the general statement of it.
			var previous = -1;
			for (var angle = 0; angle <= 512; angle += 8)
			{
				var release = Release(new WAngle(angle));
				Assert.That(release, Is.GreaterThanOrEqualTo(previous), $"release fell going from {angle - 8} to {angle}");
				previous = release;
			}
		}

		[Test]
		public void ShallowTurnFallsBackToTheOneTickFloor()
		{
			// A nearly-straight waypoint wants almost no lead, so the floor takes over. That floor is not a
			// rounding convenience: it is what stops the airframe reaching an intermediate waypoint at full
			// speed, which is the arrival the pre-02006314 code resolved by snapping to a dead stop.
			Assert.That(Release(Straight), Is.EqualTo(HeliSpeed + MaxAcceleration));
			Assert.That(Release(new WAngle(4)), Is.EqualTo(HeliSpeed + MaxAcceleration));
			Assert.That(Release(Shallow), Is.LessThan(Release(RightAngle)));
		}

		[Test]
		public void NearReversalIsRefusedByTheDeflectionCap()
		{
			// A hairpin cannot be arced. Past MaxCorneringDeflection the airframe is handed back to the
			// unchanged decelerate-and-stop path rather than being made to turn around short of the point the
			// player clicked.
			Assert.That(Reversal.Angle, Is.GreaterThan(DefaultMaxDeflection.Angle));
			Assert.That(AircraftCornerMath.ShouldReleaseEarly(true, FullAggression, Reversal, DefaultMaxDeflection), Is.False);

			// Exactly at the cap is still eligible; one unit past it is not.
			Assert.That(AircraftCornerMath.ShouldReleaseEarly(true, FullAggression, DefaultMaxDeflection, DefaultMaxDeflection), Is.True);
			Assert.That(AircraftCornerMath.ShouldReleaseEarly(true, FullAggression, new WAngle(385), DefaultMaxDeflection), Is.False);
		}

		[Test]
		public void ZeroAggressionRestoresTheBaselinePathExactly()
		{
			// The off-switch, and the control arm for measuring the feature in game. Nothing may be eligible
			// when the field is 0 or negative.
			foreach (var deflection in new[] { Straight, Shallow, RightAngle })
			{
				Assert.That(AircraftCornerMath.ShouldReleaseEarly(true, 0, deflection, DefaultMaxDeflection), Is.False);
				Assert.That(AircraftCornerMath.ShouldReleaseEarly(true, -1, deflection, DefaultMaxDeflection), Is.False);
			}
		}

		[Test]
		public void ReleaseScalesWithTheSquareOfSpeed()
		{
			// d = (v^2/a) * sin(theta/2): doubling the speed must quadruple the lead. A linear-in-v mistake is
			// the single most likely way to get this wrong and it looks correct at one speed.
			var slow = Release(RightAngle, speed: 100);
			var fast = Release(RightAngle, speed: 200);
			Assert.That(fast, Is.EqualTo(slow * 4).Within(slow / 50));
		}

		[Test]
		public void AStandingAirframeNeverReleases()
		{
			// Zero speed has no arc and no next-tick travel to be early for. Returning 0 rather than the floor
			// keeps "0 means no release" true for the caller's test.
			Assert.That(Release(RightAngle, speed: 0), Is.Zero);
			Assert.That(Release(RightAngle, speed: -1), Is.Zero);
		}

		[Test]
		public void AggressionScalesTheLeadAndIsMonotonic()
		{
			var half = Release(RightAngle, aggression: 50);
			var full = Release(RightAngle, aggression: 100);
			var over = Release(RightAngle, aggression: 200);

			Assert.That(half, Is.LessThan(full));
			Assert.That(over, Is.GreaterThan(full));
			Assert.That(full, Is.EqualTo(half * 2).Within(2));
		}

		[Test]
		public void TheHalfLegCapStopsAShortLegBeingDroppedOnItsFirstTick()
		{
			// Without the cap, a leg shorter than the geometric lead releases on tick one and a chain of close
			// waypoints collapses into a straight run at the last of them — the path silently not being flown.
			const int ShortLeg = 2048;
			var release = Release(RightAngle, legLength: ShortLeg);

			Assert.That(Release(RightAngle), Is.GreaterThan(ShortLeg), "test is vacuous unless the uncapped lead exceeds the leg");
			Assert.That(release, Is.LessThanOrEqualTo(ShortLeg / 2));
			Assert.That(release, Is.LessThan(ShortLeg), "the airframe must fly some of the leg before releasing it");
		}

		[Test]
		public void TheOneTickFloorSurvivesTheCapOnAVeryShortLeg()
		{
			// Cap then floor, in that order. The cap is about flying the path; the floor is the correctness
			// guarantee that no intermediate waypoint is ever arrived at, and it must win.
			Assert.That(Release(RightAngle, legLength: 10), Is.EqualTo(HeliSpeed + MaxAcceleration));
			Assert.That(Release(Straight, legLength: 0), Is.EqualTo(HeliSpeed + MaxAcceleration));
		}

		[Test]
		public void NoOverflowAtTheExtremesOfTheReachableDomain()
		{
			// speed^2 * sin * aggression is ~7.2e9 at the fastest shipped airframe, which is why the
			// intermediate is long. Sweep well past the shipped range and assert the result stays sane rather
			// than wrapping negative.
			for (var speed = 1; speed <= 1400; speed += 7)
				for (var angle = 0; angle <= 512; angle += 32)
				{
					var release = AircraftCornerMath.ReleaseDistance(speed, 1, new WAngle(angle), 500, LongLeg);
					Assert.That(release, Is.GreaterThanOrEqualTo(speed + 1), $"speed {speed} angle {angle} fell through the floor");
				}
		}
	}
}
