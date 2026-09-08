#region Copyright & License Information
/*
 * WW3MOD sub-tick motion smoothing (2026-09-06).
 *
 * Two claims, and only the first can be tested without launching:
 *
 *   1. The offset is zero at a tick boundary, never longer than one tick of travel, zero when there
 *      is no previous position to have moved from, and -- since 2026-09-08 -- never longer than the
 *      distance the mover still has to travel. That is this fixture.
 *   2. Nothing synchronised can read the wall-clock fraction the offset is scaled by. That is
 *      SubTickClockIsNotSimulationStateTest, which IL-scans for it rather than asserting it.
 *
 * What NEITHER covers is whether the result looks right, which needs a window — see
 * tools/autotest/scenarios/demo-subtick-smoothing.
 */
#endregion

using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits.Render;

namespace OpenRA.Test
{
	[TestFixture]
	public class SubTickMotionSmoothingTest
	{
		// One tick of a Kinzhal at cruise: 2000 WDist, just under two cells, ~47 px at 100% zoom.
		// The number the user is actually watching step.
		static readonly WVec KinzhalStep = new WVec(2000, 0, 0);

		[Test]
		public void OffsetIsZeroAtATickBoundary()
		{
			Assert.That(SubTickMotionSmoothingMath.Offset(KinzhalStep, 0, 100, null), Is.EqualTo(WVec.Zero),
				"On the frame drawn immediately after a logic tick the sprite must sit exactly on the " +
				"simulated position. Anything else is a permanent offset between where the missile is " +
				"drawn and where its trail, its explosion and its collisions happen.");
		}

		[Test]
		public void OffsetIsOneFullStepAtTheEndOfATick()
		{
			Assert.That(SubTickMotionSmoothingMath.Offset(KinzhalStep, SubTickClock.One, 100, null),
				Is.EqualTo(KinzhalStep));
		}

		[Test]
		public void OffsetIsZeroWithNoPreviousPosition()
		{
			// The spawn frame. SubTickMotionSmoothing.Tick leaves velocity at WVec.Zero until it has
			// seen two positions, so this is the value the math is handed on an actor's first tick.
			for (var f = 0; f <= SubTickClock.One; f += 64)
				Assert.That(SubTickMotionSmoothingMath.Offset(WVec.Zero, f, 100, null), Is.EqualTo(WVec.Zero));
		}

		[Test]
		public void OffsetIsNeverLongerThanOneTickOfTravel()
		{
			// The bound the whole feature rests on: prediction adds no latency, but it may not draw
			// the sprite further ahead than the simulation is about to move it, or a direction change
			// would snap back by more than one step.
			var velocities = new[]
			{
				KinzhalStep,
				new WVec(-2400, 0, 0),
				new WVec(1414, 1414, 0),
				new WVec(600, -900, 350),
				new WVec(0, 0, -2000),
				new WVec(1, 1, 1)
			};

			foreach (var v in velocities)
			{
				for (var f = 0; f <= SubTickClock.One; f++)
				{
					var offset = SubTickMotionSmoothingMath.Offset(v, f, 100, null);
					Assert.That(offset.LengthSquared, Is.LessThanOrEqualTo(v.LengthSquared),
						$"velocity {v} at fraction {f} predicted {offset}, which is further than one tick of travel.");
				}
			}
		}

		[Test]
		public void FractionsOutsideTheUnitRangeAreClamped()
		{
			// Defence in depth. SubTickClock.Measure already clamps, but the trait multiplies a
			// displacement by whatever it is handed, and an unclamped fraction here would fling the
			// sprite an unbounded distance rather than produce a visible glitch bounded by one step.
			Assert.That(SubTickMotionSmoothingMath.Offset(KinzhalStep, -5000, 100, null), Is.EqualTo(WVec.Zero));
			Assert.That(SubTickMotionSmoothingMath.Offset(KinzhalStep, 10 * SubTickClock.One, 100, null),
				Is.EqualTo(KinzhalStep));
		}

		[Test]
		public void OffsetGrowsMonotonicallyThroughTheTick()
		{
			// A missile that went forwards then backwards inside one tick would look worse than the
			// stepping this replaces.
			var previous = 0L;
			for (var f = 0; f <= SubTickClock.One; f++)
			{
				var length = SubTickMotionSmoothingMath.Offset(KinzhalStep, f, 100, null).LengthSquared;
				Assert.That(length, Is.GreaterThanOrEqualTo(previous), $"went backwards at fraction {f}");
				previous = length;
			}
		}

		[Test]
		public void StrengthScalesThePrediction()
		{
			Assert.That(SubTickMotionSmoothingMath.Offset(KinzhalStep, SubTickClock.One, 50, null),
				Is.EqualTo(new WVec(1000, 0, 0)));

			// Zero and negative are the "off" switch the Info field documents, and must not simply
			// fall through to a divide.
			Assert.That(SubTickMotionSmoothingMath.Offset(KinzhalStep, SubTickClock.One, 0, null), Is.EqualTo(WVec.Zero));
			Assert.That(SubTickMotionSmoothingMath.Offset(KinzhalStep, SubTickClock.One, -20, null), Is.EqualTo(WVec.Zero));
		}

		[Test]
		public void LongDisplacementsDoNotOverflow()
		{
			// The MaxStep guard in the trait means the math should never see a step this long, but
			// the guard is a YAML-tunable field: raise MaxStep and this is what arrives. A single
			// velocity * fraction * strength product would already have wrapped negative here.
			var huge = new WVec(1 << 20, 1 << 20, 1 << 20);
			var offset = SubTickMotionSmoothingMath.Offset(huge, SubTickClock.One, 100, null);
			Assert.That(offset, Is.EqualTo(huge));

			var half = SubTickMotionSmoothingMath.Offset(huge, SubTickClock.One / 2, 100, null);
			Assert.That(half.X, Is.EqualTo(huge.X / 2));
			Assert.That(half.LengthSquared, Is.LessThan(huge.LengthSquared));
		}

		// ============================================================================================
		// THE IMPACT CLAMP (2026-09-08). Reported as "it makes the missile look like it flies through
		// the target a bit before the explosion registers".
		// ============================================================================================

		[Test]
		public void OffsetIsZeroOnceTheMoverHasArrived()
		{
			// THE REPORTED BUG, in one assertion. BallisticMissileFly places the missile ON the target
			// and then spends one more tick finishing before the Kill lands, so for one whole
			// inter-tick interval the trait still holds the arrival step while the simulation is not
			// going to move the actor again. Unclamped, the sprite swept a full step past the crater
			// point and snapped back to explode behind itself.
			for (var f = 0; f <= SubTickClock.One; f += 16)
				Assert.That(SubTickMotionSmoothingMath.Offset(KinzhalStep, f, 100, WDist.Zero),
					Is.EqualTo(WVec.Zero),
					$"at fraction {f}: a mover with nothing left to travel must be drawn exactly where " +
					"the simulation put it.");
		}

		[Test]
		public void OffsetNeverReachesPastTheImpactPoint()
		{
			// The tick BEFORE arrival, which is why the clamp is a distance and not a test for the
			// final tick: the missile is closer to the target than its last step was long, so an
			// unclamped prediction crosses the impact point partway through the tick and comes back.
			var velocities = new[]
			{
				KinzhalStep,
				new WVec(-2400, 0, 0),
				new WVec(1414, 1414, 0),
				new WVec(600, -900, 350),
				new WVec(0, 0, -2000)
			};

			foreach (var v in velocities)
			{
				for (var left = 0; left <= 2600; left += 13)
				{
					var remaining = new WDist(left);
					for (var f = 0; f <= SubTickClock.One; f += 8)
					{
						var offset = SubTickMotionSmoothingMath.Offset(v, f, 100, remaining);
						Assert.That(offset.Length, Is.LessThanOrEqualTo(left),
							$"velocity {v} with {left} left at fraction {f} predicted {offset}, which is " +
							"past the point the mover stops at.");
					}
				}
			}
		}

		[Test]
		public void TheLastTicksOfARealFlightNeverOvershoot()
		{
			// The arithmetic BallisticMissileFly actually produces at the end of a Kinzhal's flight, run
			// through the trait's own model of it: velocity is the step INTO the current position and
			// the remaining distance is measured FROM that position, both recomputed in ITick.
			//
			// Terminal dive at 2400 WDist/tick along +X, target at X = 0. horizontalProgress clamps to
			// 1, so the arrival step is short (900, not 2400) and the tick after it is stationary
			// because the activity spends it finishing.
			var flight = new[]
			{
				(Step: 2400, Left: 3300),
				(Step: 2400, Left: 900),   // less left than the last step was long
				(Step: 900, Left: 0),      // arrival: SetPosition onto the target
				(Step: 0, Left: 0)         // the finishing tick, after Tick recomputed the velocity
			};

			// The interval that actually showed the bug is the third: velocity is the 900-long arrival
			// step and there is nothing left to travel.
			var previousLeft = int.MaxValue;
			foreach (var (step, left) in flight)
			{
				Assert.That(left, Is.LessThanOrEqualTo(previousLeft), "the model must close on the target");
				previousLeft = left;

				for (var f = 0; f <= SubTickClock.One; f++)
				{
					var offset = SubTickMotionSmoothingMath.Offset(new WVec(step, 0, 0), f, 100, new WDist(left));
					Assert.That(offset.X, Is.LessThanOrEqualTo(left),
						$"step {step} with {left} left at fraction {f} drew the missile {offset.X} ahead, " +
						"which is past the impact point.");
					Assert.That(offset.X, Is.GreaterThanOrEqualTo(0), "and never behind it either");
				}
			}
		}

		[Test]
		public void ClampingKeepsTheDirectionOfTravel()
		{
			// A clamp that scaled the components unequally would bend the missile off its own smoke
			// trail on the last tick, which is the one place a player is looking closely.
			var velocity = new WVec(1200, -1600, 0);   // length 2000
			var clamped = SubTickMotionSmoothingMath.Offset(velocity, SubTickClock.One, 100, new WDist(1000));

			Assert.That(clamped.Length, Is.EqualTo(1000).Within(2));
			Assert.That(clamped.X, Is.EqualTo(600).Within(2));
			Assert.That(clamped.Y, Is.EqualTo(-800).Within(2));
		}

		[Test]
		public void AnUnboundedMoverIsSmoothedExactlyAsBefore()
		{
			// The null case is not a stub: an actor with no IMotionEndpoint trait has no endpoint to be
			// clamped against, and must keep the behaviour that shipped on 2026-09-06.
			Assert.That(SubTickMotionSmoothingMath.Offset(KinzhalStep, SubTickClock.One, 100, null),
				Is.EqualTo(KinzhalStep));
			Assert.That(SubTickMotionSmoothingMath.Offset(KinzhalStep, SubTickClock.One / 2, 100, null),
				Is.EqualTo(new WVec(1000, 0, 0)));
		}

		[Test]
		public void ANegativeRemainingDistanceIsTreatedAsArrival()
		{
			// Not reachable from the trait, which measures a length, but the parameter is public and a
			// negative limit must not fall through to the unclamped offset.
			Assert.That(SubTickMotionSmoothingMath.Offset(KinzhalStep, SubTickClock.One, 100, new WDist(-500)),
				Is.EqualTo(WVec.Zero));
		}

		[Test]
		public void NoOffsetIsComputedWithoutAnImpactLimit()
		{
			// ASSERTS AN ABSENCE, following the post-detonation guard (327710b0): the unclamped
			// extrapolation is what drew a missile through its own target, so the fix is that it has no
			// reachable caller, not that the one caller remembers to clamp. Offset WRAPS it and the
			// wrapped form is private, so the compiler confines it to that one file -- this test keeps
			// it that way as the API grows.
			var producers = typeof(SubTickMotionSmoothingMath)
				.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
					BindingFlags.Instance | BindingFlags.DeclaredOnly)
				.Where(m => m.ReturnType == typeof(WVec))
				.ToArray();

			Assert.That(producers, Is.Not.Empty,
				"nothing in this type returns a WVec any more -- the scan below is vacuous, so point " +
				"this test at whatever replaced it rather than leaving a green that checks nothing.");

			var reachable = producers.Where(m => m.IsPublic).ToArray();
			Assert.That(reachable, Is.Not.Empty,
				"every prediction is now private, so nothing outside this type can smooth anything. " +
				"That is either a rename or a deletion, and both need this test pointed somewhere new.");

			var unbounded = reachable
				.Where(m => !m.GetParameters().Any(p => p.ParameterType == typeof(WDist?)))
				.Select(m => m.Name)
				.ToArray();

			Assert.That(unbounded, Is.Empty,
				"every publicly reachable way to compute a prediction must take the distance left to " +
				"travel. One that does not is the overshoot back, reachable from any future caller: " +
				string.Join(", ", unbounded));
		}
	}
}
