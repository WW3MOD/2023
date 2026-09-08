#region Copyright & License Information
/*
 * WW3MOD hypersonic plasma / streak (2026-09-07).
 *
 * Three claims, and only the first two can be tested without launching:
 *
 *	 1. A weapon that has not opted in renders byte-identically to before. The trait's defaults are
 *		the whole of that guarantee, so they are asserted here rather than left to a reading of the
 *		Info class.
 *	 2. A sample lands where the YAML says and fades the way the YAML says, independently of how
 *		fast the missile happens to be going, and -- since 2026-09-08 -- never past the point the
 *		missile stops at, at ANY point in the tick rather than only at the moment of arrival. That is
 *		the rest of this fixture.
 *	 3. Whether it LOOKS like plasma, which needs a window. See the capture request in the branch
 *		report; no autotest scenario is shipped for it.
 */
#endregion

using System;
using NUnit.Framework;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits.Render;

namespace OpenRA.Test
{
	[TestFixture]
	public class WithHypersonicPlasmaTest
	{
		// One tick of a Kinzhal at cruise (2000 WDist) and of a Sarmat RV (1600), the two weapons
		// that opt in. Different speeds on purpose: the placement must not depend on them.
		static readonly WVec KinzhalStep = new WVec(2000, 0, 0);
		static readonly WVec SarmatStep = new WVec(0, -1600, 0);

		[Test]
		public void TheDefaultsDrawNothing()
		{
			var info = new WithHypersonicPlasmaInfo();

			Assert.That(info.LeadingSamples, Is.Zero,
				"A weapon that adds this trait without setting a count must render exactly as it did " +
				"before. Default-off is the constraint the whole feature was asked for under.");
			Assert.That(info.TrailingSamples, Is.Zero);
			Assert.That(info.BodyColor.A, Is.Zero,
				"The body overlay is the third way this trait can change a pixel, and it is off by a " +
				"transparent colour rather than by a count.");
		}

		[Test]
		public void ASampleSitsOneSpacingAlongTheVelocity()
		{
			var step = WithHypersonicPlasmaMath.Step(KinzhalStep, new WDist(384));

			Assert.That(step, Is.EqualTo(new WVec(384, 0, 0)),
				"The offset between consecutive copies is the spacing the author wrote, projected " +
				"onto the direction of travel.");
		}

		[Test]
		public void SpacingDoesNotDependOnHowFastTheMissileIs()
		{
			var spacing = new WDist(384);
			var fast = WithHypersonicPlasmaMath.Step(KinzhalStep, spacing).Length;
			var slower = WithHypersonicPlasmaMath.Step(SarmatStep, spacing).Length;

			Assert.That(fast, Is.EqualTo(384));
			Assert.That(slower, Is.EqualTo(384),
				"Two missiles 400 WDist/tick apart in speed must lay their plasma out identically. " +
				"If this drifts, the streak length has become a function of Speed and every weapon " +
				"needs re-tuning whenever a balance pass touches one.");
		}

		[Test]
		public void ASampleFollowsTheSignOfTheVelocity()
		{
			var step = WithHypersonicPlasmaMath.Step(SarmatStep, new WDist(512));

			Assert.That(step, Is.EqualTo(new WVec(0, -512, 0)),
				"Nothing here goes through WAngle, so the counterclockwise convention cannot be got " +
				"wrong — but the sign still has to survive the integer division.");
		}

		[Test]
		public void ADescendingMissileLaysPlasmaDownItsDive()
		{
			// The terminal dive: horizontal and vertical components together. The Z component is what
			// puts the sheath on the missile's real screen path rather than flat on the ground plane.
			var step = WithHypersonicPlasmaMath.Step(new WVec(3000, 0, -4000), new WDist(500));

			Assert.That(step, Is.EqualTo(new WVec(300, 0, -400)));
			Assert.That(step.Length, Is.EqualTo(500));
		}

		[Test]
		public void AStationaryActorGetsNoPlasma()
		{
			Assert.That(WithHypersonicPlasmaMath.Step(WVec.Zero, new WDist(384)), Is.EqualTo(WVec.Zero),
				"Divide-by-zero guard. A missile waiting on its launch rail has no direction to form " +
				"a leading edge along.");
		}

		[Test]
		public void ZeroSpacingGetsNoPlasma()
		{
			Assert.That(WithHypersonicPlasmaMath.Step(KinzhalStep, WDist.Zero), Is.EqualTo(WVec.Zero));
		}

		[Test]
		public void AnAbsurdSpacingDoesNotOverflow()
		{
			// A YAML author writing a spacing in the millions gets a long streak, not a wrapped
			// vector pointing backwards. int arithmetic would have overflowed at 2000 * 1,500,000.
			var step = WithHypersonicPlasmaMath.Step(KinzhalStep, new WDist(1500000));

			Assert.That(step, Is.EqualTo(new WVec(1500000, 0, 0)));
		}

		[Test]
		public void TheFirstSampleRendersAtExactlyTheAuthoredAlpha()
		{
			Assert.That(WithHypersonicPlasmaMath.Alpha(0.5f, 1, 8), Is.EqualTo(0.5f).Within(0.0001f),
				"The alpha in the YAML colour must mean something a reader can check against a frame, " +
				"rather than being silently divided by a sample count they also chose.");
		}

		[Test]
		public void AlphaFallsToTheAuthoredValueOverTheSampleCount()
		{
			Assert.That(WithHypersonicPlasmaMath.Alpha(0.8f, 4, 4), Is.EqualTo(0.2f).Within(0.0001f));
		}

		[Test]
		public void AlphaFallsMonotonically()
		{
			var previous = float.MaxValue;
			for (var i = 1; i <= 8; i++)
			{
				var alpha = WithHypersonicPlasmaMath.Alpha(0.6f, i, 8);
				Assert.That(alpha, Is.LessThan(previous),
					$"Sample {i} is not dimmer than the one before it — the streak would read as a " +
					"row of separate sprites rather than a fading wake.");
				Assert.That(alpha, Is.GreaterThan(0f));
				previous = alpha;
			}
		}

		[Test]
		public void ASingleSampleDoesNotFade()
		{
			Assert.That(WithHypersonicPlasmaMath.Alpha(0.9f, 1, 1), Is.EqualTo(0.9f).Within(0.0001f));
		}

		[Test]
		public void SamplesOutsideTheRangeAreInvisible()
		{
			Assert.That(WithHypersonicPlasmaMath.Alpha(0.9f, 0, 4), Is.Zero);
			Assert.That(WithHypersonicPlasmaMath.Alpha(0.9f, 5, 4), Is.Zero);
			Assert.That(WithHypersonicPlasmaMath.Alpha(0.9f, 1, 0), Is.Zero);
		}

		// ============================================================================================
		// THE IMPACT CLAMP (2026-09-08). The sibling of SubTickMotionSmoothing's, from the same source
		// (IMotionEndpoint) and for the same reason: nothing this feature draws may sit past the point
		// the missile detonates at.
		//
		// THE CLAMP RESERVES A TICK OF TRAVEL, and the first version of it did not. The copies are
		// offset from renderables the smoothing trait has already moved, so the tip is at
		// pos + offset + n * spacing with BOTH terms measured from the same simulated pos -- they add.
		// Spending the whole remaining distance on samples therefore left the bloom hanging past the
		// crater for most of the tick BEFORE arrival, which is the tick these tests exist for: the
		// arrival tick was correct all along.
		// ============================================================================================

		// The Kinzhal's shipped leading configuration, which is what the numbers below are about.
		const int KinzhalSpacing = 96;
		const int KinzhalSamples = 5;

		[Test]
		public void TheSheathIsGoneAtContact()
		{
			// A missile placed ON its target still renders for the tick the activity spends finishing
			// before the Kill lands. With no clamp the whole bloom was drawn past the crater point --
			// 480 wdist on the Kinzhal, about 11 px at 100% zoom.
			Assert.That(
				WithHypersonicPlasmaMath.LeadingSamplesWithin(
					KinzhalSamples, new WDist(KinzhalSpacing), WDist.Zero, new WDist(2400)),
				Is.Zero,
				"a missile with nothing left to travel must draw no leading sheath at all.");

			// The Sarmat's configuration, which is shorter and denser, and must behave the same way.
			Assert.That(
				WithHypersonicPlasmaMath.LeadingSamplesWithin(3, new WDist(128), WDist.Zero, new WDist(1600)),
				Is.Zero);
		}

		[Test]
		public void AWholeTickOfTravelIsReservedForThePredictedBody()
		{
			// THE CONCRETE CASE THE UNRESERVED CLAMP GOT WRONG: 900 wdist left against a 2400 step,
			// the last row but one of SubTickMotionSmoothingTest.TheLastTicksOfARealFlightNeverOvershoot.
			// All five samples fit inside 900, so the clamp did nothing -- while the body was
			// simultaneously being drawn up to 900 ahead, pinned to the endpoint from fraction 0.375
			// on. The bloom hung its full 480 past the crater for ~62% of that interval.
			Assert.That(
				WithHypersonicPlasmaMath.LeadingSamplesWithin(
					KinzhalSamples, new WDist(KinzhalSpacing), new WDist(900), new WDist(2400)),
				Is.Zero,
				"with less left to travel than one step, the body can already be drawn at the endpoint, " +
				"so there is no room in front of it for any sheath at all.");

			// What it used to answer, kept as the statement of the defect rather than as a second way
			// of asking the same question.
			Assert.That(
				WithHypersonicPlasmaMath.LeadingSamplesWithin(
					KinzhalSamples, new WDist(KinzhalSpacing), new WDist(900), WDist.Zero),
				Is.EqualTo(5));

			// And the tick before it, where 3300 - 2400 = 900 of budget is room for all five.
			Assert.That(
				WithHypersonicPlasmaMath.LeadingSamplesWithin(
					KinzhalSamples, new WDist(KinzhalSpacing), new WDist(3300), new WDist(2400)),
				Is.EqualTo(5));
		}

		[Test]
		public void TheSheathIsClearOfTheEndpointBeforeArrivalNotOnlyAtIt()
		{
			// THE TEST THE FIRST VERSION OF THIS CLAMP DID NOT HAVE, and the only one here that
			// composes the two traits the way the renderer does. Everything else in this fixture
			// asks the plasma arithmetic about itself; this asks what the viewer actually sees, which
			// is the body at its predicted position with the sheath hung off it.
			//
			// Exhaustive over fraction rather than sampled at the ends: the failure it is guarding
			// against appears partway through a tick, at whatever fraction the body pins to the
			// endpoint, and both ends of the interval looked fine while it was live.
			foreach (var speed in new[] { 500, 1600, 2000, 2400 })
			{
				var velocity = new WVec(speed, 0, 0);
				var tickTravel = new WDist(velocity.Length);
				var step = WithHypersonicPlasmaMath.Step(velocity, new WDist(KinzhalSpacing));

				for (var left = 0; left <= 3 * speed; left += 11)
				{
					var remaining = new WDist(left);
					var n = WithHypersonicPlasmaMath.LeadingSamplesWithin(
						KinzhalSamples, new WDist(KinzhalSpacing), remaining, tickTravel);

					for (var f = 0; f <= SubTickClock.One; f += 8)
					{
						var offset = SubTickMotionSmoothingMath.Offset(velocity, f, 100, remaining);
						var tip = offset + (step * n);

						Assert.That(tip.Length, Is.LessThanOrEqualTo(left),
							$"speed {speed} with {left} left at fraction {f}: the body is drawn " +
							$"{offset.Length} ahead and carries {n} samples of sheath, putting the tip " +
							$"{tip.Length} out -- past the point the missile detonates at.");
					}
				}
			}
		}

		[Test]
		public void NoLeadingSampleIsPlacedPastTheBudget()
		{
			// The arithmetic on its own, over the whole neighbourhood rather than at hand-picked
			// values: whatever is left and whatever is reserved, n * spacing fits in what is over.
			foreach (var spacing in new[] { 96, 128, 384, 1000 })
			{
				foreach (var reserve in new[] { 0, 900, 2400 })
				{
					for (var left = 0; left <= 4000; left += 7)
					{
						var samples = WithHypersonicPlasmaMath.LeadingSamplesWithin(
							10, new WDist(spacing), new WDist(left), new WDist(reserve));

						Assert.That(samples * spacing, Is.LessThanOrEqualTo(Math.Max(left - reserve, 0)),
							$"spacing {spacing}, {left} left, {reserve} reserved: kept {samples} samples, " +
							"the last of which is drawn past what the body has left in front of it.");
						Assert.That(samples, Is.InRange(0, 10));
					}
				}
			}
		}

		[Test]
		public void TheClampShortensTheSheathRatherThanSwitchingItOff()
		{
			// With the reserve already accounted for, the remaining budget shortens the bloom sample
			// by sample instead of blinking it out -- which is what a slower missile shows, because
			// its sheath is a larger fraction of its step.
			//
			// A KINZHAL DOES NOT SHOW THIS, and that is the accepted cost of the reserve: 2400 of
			// travel against 480 of sheath means the budget goes from "room for all five" to negative
			// in one tick. See AWholeTickOfTravelIsReservedForThePredictedBody.
			Assert.That(WithHypersonicPlasmaMath.LeadingSamplesWithin(5, new WDist(96), new WDist(980), new WDist(500)), Is.EqualTo(5));
			Assert.That(WithHypersonicPlasmaMath.LeadingSamplesWithin(5, new WDist(96), new WDist(800), new WDist(500)), Is.EqualTo(3));
			Assert.That(WithHypersonicPlasmaMath.LeadingSamplesWithin(5, new WDist(96), new WDist(596), new WDist(500)), Is.EqualTo(1));
			Assert.That(WithHypersonicPlasmaMath.LeadingSamplesWithin(5, new WDist(96), new WDist(595), new WDist(500)), Is.Zero);
		}

		[Test]
		public void ThereIsNoClampWithoutAnEndpoint()
		{
			// An actor that does not publish where it stops draws the full sheath, exactly as it did
			// before this clamp existed. Null is unbounded, NOT zero -- reading it as zero would switch
			// the whole feature off on anything but a ballistic missile. The reserve must not leak into
			// that case either: it is a correction to a known distance, and there is no known distance.
			Assert.That(WithHypersonicPlasmaMath.LeadingSamplesWithin(5, new WDist(96), null, WDist.Zero), Is.EqualTo(5));
			Assert.That(WithHypersonicPlasmaMath.LeadingSamplesWithin(5, new WDist(96), null, new WDist(2400)), Is.EqualTo(5));
			Assert.That(WithHypersonicPlasmaMath.LeadingSamplesWithin(3, new WDist(128), null, new WDist(1600)), Is.EqualTo(3));
		}

		[Test]
		public void ADegenerateConfigurationKeepsTheTwoHalvesAgreeing()
		{
			// Step returns WVec.Zero at spacing 0, so there is nowhere to put a sample; reporting a
			// count above zero here would be a count of copies nobody can see, and the division below
			// it would be by zero.
			Assert.That(WithHypersonicPlasmaMath.LeadingSamplesWithin(5, WDist.Zero, new WDist(5000), WDist.Zero), Is.Zero);
			Assert.That(WithHypersonicPlasmaMath.LeadingSamplesWithin(5, WDist.Zero, null, WDist.Zero), Is.Zero);
			Assert.That(WithHypersonicPlasmaMath.LeadingSamplesWithin(0, new WDist(96), null, WDist.Zero), Is.Zero);
			Assert.That(WithHypersonicPlasmaMath.LeadingSamplesWithin(5, new WDist(96), new WDist(-500), WDist.Zero), Is.Zero);

			// A negative reserve must not ADD budget. Not reachable from the trait, which passes a
			// measured length, but the parameter is public.
			Assert.That(
				WithHypersonicPlasmaMath.LeadingSamplesWithin(5, new WDist(96), new WDist(480), new WDist(-5000)),
				Is.EqualTo(5));
		}
	}
}
