#region Copyright & License Information
/*
 * WW3MOD sub-tick motion smoothing (2026-09-06).
 *
 * Two claims, and only the first can be tested without launching:
 *
 *   1. The offset is zero at a tick boundary, never longer than one tick of travel, and zero when
 *      there is no previous position to have moved from. That is this fixture.
 *   2. Nothing synchronised can read the wall-clock fraction the offset is scaled by. That is
 *      SubTickClockIsNotSimulationStateTest, which IL-scans for it rather than asserting it.
 *
 * What NEITHER covers is whether the result looks right, which needs a window — see
 * tools/autotest/scenarios/demo-subtick-smoothing.
 */
#endregion

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
			Assert.That(SubTickMotionSmoothingMath.Offset(KinzhalStep, 0, 100), Is.EqualTo(WVec.Zero),
				"On the frame drawn immediately after a logic tick the sprite must sit exactly on the " +
				"simulated position. Anything else is a permanent offset between where the missile is " +
				"drawn and where its trail, its explosion and its collisions happen.");
		}

		[Test]
		public void OffsetIsOneFullStepAtTheEndOfATick()
		{
			Assert.That(SubTickMotionSmoothingMath.Offset(KinzhalStep, SubTickClock.One, 100),
				Is.EqualTo(KinzhalStep));
		}

		[Test]
		public void OffsetIsZeroWithNoPreviousPosition()
		{
			// The spawn frame. SubTickMotionSmoothing.Tick leaves velocity at WVec.Zero until it has
			// seen two positions, so this is the value the math is handed on an actor's first tick.
			for (var f = 0; f <= SubTickClock.One; f += 64)
				Assert.That(SubTickMotionSmoothingMath.Offset(WVec.Zero, f, 100), Is.EqualTo(WVec.Zero));
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
					var offset = SubTickMotionSmoothingMath.Offset(v, f, 100);
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
			Assert.That(SubTickMotionSmoothingMath.Offset(KinzhalStep, -5000, 100), Is.EqualTo(WVec.Zero));
			Assert.That(SubTickMotionSmoothingMath.Offset(KinzhalStep, 10 * SubTickClock.One, 100),
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
				var length = SubTickMotionSmoothingMath.Offset(KinzhalStep, f, 100).LengthSquared;
				Assert.That(length, Is.GreaterThanOrEqualTo(previous), $"went backwards at fraction {f}");
				previous = length;
			}
		}

		[Test]
		public void StrengthScalesThePrediction()
		{
			Assert.That(SubTickMotionSmoothingMath.Offset(KinzhalStep, SubTickClock.One, 50),
				Is.EqualTo(new WVec(1000, 0, 0)));

			// Zero and negative are the "off" switch the Info field documents, and must not simply
			// fall through to a divide.
			Assert.That(SubTickMotionSmoothingMath.Offset(KinzhalStep, SubTickClock.One, 0), Is.EqualTo(WVec.Zero));
			Assert.That(SubTickMotionSmoothingMath.Offset(KinzhalStep, SubTickClock.One, -20), Is.EqualTo(WVec.Zero));
		}

		[Test]
		public void LongDisplacementsDoNotOverflow()
		{
			// The MaxStep guard in the trait means the math should never see a step this long, but
			// the guard is a YAML-tunable field: raise MaxStep and this is what arrives. A single
			// velocity * fraction * strength product would already have wrapped negative here.
			var huge = new WVec(1 << 20, 1 << 20, 1 << 20);
			var offset = SubTickMotionSmoothingMath.Offset(huge, SubTickClock.One, 100);
			Assert.That(offset, Is.EqualTo(huge));

			var half = SubTickMotionSmoothingMath.Offset(huge, SubTickClock.One / 2, 100);
			Assert.That(half.X, Is.EqualTo(huge.X / 2));
			Assert.That(half.LengthSquared, Is.LessThan(huge.LengthSquared));
		}
	}
}
