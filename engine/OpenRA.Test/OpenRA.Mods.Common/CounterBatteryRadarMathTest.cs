#region Copyright & License Information
/*
 * WW3MOD — pins the counter-battery radar's siting arithmetic.
 *
 * The whole siting decision reduces to ONE number: how far forward of its own Supply Route the radar
 * should stand. Everything else in CounterBatteryRadarBotModule is plumbing around it — a bearing, a
 * terrain clamp and a deploy order — so this is where the behaviour can actually be pinned without
 * mounting a world.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class CounterBatteryRadarMathTest
	{
		const int Rear = 33;
		const int Coverage = 42;

		[Test]
		public void AlreadyInCoverage_StaysAtTheAnchor()
		{
			// The front is nearer than the radar's own disc, so advancing buys nothing and every cell
			// forward is a cell closer to being shot. 0 means "deploy where you stand".
			Assert.That(CounterBatteryRadarMath.ForwardOffsetCells(30, Rear, Coverage), Is.EqualTo(0));
			Assert.That(CounterBatteryRadarMath.ForwardOffsetCells(42, Rear, Coverage), Is.EqualTo(0));
		}

		[Test]
		public void MidRange_CoverageTermBindsUnderTheRearCap()
		{
			// 60 out: reaching the front needs 18, and the rear cap allows 19. The radar advances exactly
			// far enough to cover and no further — the case where both constraints are satisfiable at once.
			Assert.That(CounterBatteryRadarMath.ForwardOffsetCells(60, Rear, Coverage), Is.EqualTo(18));
		}

		[Test]
		public void LongRange_RearCapBindsAndCoverageIsDeliberatelyPartial()
		{
			// 120 out: full coverage would need 78 forward, which is two thirds of the way to the enemy —
			// i.e. standing in the contested band, where an unarmoured immobile vehicle is a free kill. The
			// cap wins at 39 and the radar covers the near half of the band instead. THIS ASYMMETRY IS THE
			// DESIGN: the rear rule is a safety bound and the coverage rule is an ambition, so the safety
			// bound must be the one that survives a conflict.
			Assert.That(CounterBatteryRadarMath.ForwardOffsetCells(120, Rear, Coverage), Is.EqualTo(39));
		}

		[Test]
		public void NeverNegativeAndDegenerateInputsAreSafe()
		{
			// A zero or negative distance is a radar standing on its own bearing target (a degenerate map,
			// or the map centre falling on the SR). It must not produce a negative offset, which
			// ShiftToward would happily turn into a step in the WRONG direction — backwards off the map.
			Assert.That(CounterBatteryRadarMath.ForwardOffsetCells(0, Rear, Coverage), Is.EqualTo(0));
			Assert.That(CounterBatteryRadarMath.ForwardOffsetCells(-5, Rear, Coverage), Is.EqualTo(0));

			// A misconfigured negative percentage or coverage must clamp rather than invert the rule.
			Assert.That(CounterBatteryRadarMath.ForwardOffsetCells(100, -10, Coverage), Is.EqualTo(0));
			Assert.That(CounterBatteryRadarMath.ForwardOffsetCells(100, Rear, -10), Is.EqualTo(33));
		}

		[Test]
		public void ZeroRearFraction_PinsTheRadarToItsAnchor()
		{
			// The off switch: 0% rear band means the radar never leaves the SR, whatever the coverage term
			// wants. Worth pinning because it is the fallback a tuner would reach for if forward siting ever
			// turned out to be a mistake, and it must not be reachable by accident from a rounding path.
			Assert.That(CounterBatteryRadarMath.ForwardOffsetCells(200, 0, Coverage), Is.EqualTo(0));
		}

		[Test]
		public void OffsetIsMonotonicAndNeverLeavesTheRearBand()
		{
			// The two properties the module relies on across every map size, checked over the whole
			// plausible range rather than at the three worked examples above.
			var previous = 0;
			for (var d = 0; d <= 400; d++)
			{
				var offset = CounterBatteryRadarMath.ForwardOffsetCells(d, Rear, Coverage);

				Assert.That(offset, Is.GreaterThanOrEqualTo(0), $"distance {d}");
				Assert.That(offset, Is.LessThanOrEqualTo(d * Rear / 100),
					$"distance {d}: the rear band is a hard cap, not a preference");
				Assert.That(offset, Is.GreaterThanOrEqualTo(previous),
					$"distance {d}: a more distant front must never pull the radar BACKWARDS");

				previous = offset;
			}
		}
	}
}
