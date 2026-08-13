#region Copyright & License Information
/*
 * Verifies Cargo.NextUnloadDelay, the single definition of the dismount rhythm.
 * Both the ordered unload (UnloadCargo) and the emergency bail out of a burning
 * transport drive off it, so this is what keeps the two cadences identical.
 * Pure-math test; no Actor / World.
 *
 * Note what is NOT this function's job: the delay before the FIRST man. Both
 * callers unload a passenger and only then ask for a delay, so the first man
 * always leaves immediately — which is what "no delay, even while the vehicle
 * is still moving" depends on.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common.Test
{
	[TestFixture]
	public class CargoUnloadCadenceTest
	{
		// Shipped CargoInfo defaults: pairs leave 4 ticks apart, 12 ticks between pairs.
		const int GroupSize = 2;
		const int IntraDelay = 4;
		const int InterMultiplier = 3;

		static int Delay(int unloaded, int groupSize = GroupSize, int intraDelay = IntraDelay,
			int interMultiplier = InterMultiplier) =>
			Cargo.NextUnloadDelay(unloaded, groupSize, intraDelay, interMultiplier);

		[Test]
		public void ShippedDefaultsComeOutInPairs()
		{
			// A stick of six reads as three pairs: short gap inside a pair, long gap
			// between them. This is the pattern a player sees streaming out of a
			// burning APC, and it must match an ordered dismount exactly.
			Assert.AreEqual(4, Delay(1), "the second man follows the first closely");
			Assert.AreEqual(12, Delay(2), "then the longer pause before the next pair");
			Assert.AreEqual(4, Delay(3));
			Assert.AreEqual(12, Delay(4));
			Assert.AreEqual(4, Delay(5));
			Assert.AreEqual(12, Delay(6));
		}

		[Test]
		public void TheGapBetweenPairsIsVisiblyLongerThanTheGapInsideOne()
		{
			// The whole point of the pacing is that the groups are separable by eye.
			// If these two ever collapse to the same number the squad reads as one
			// undifferentiated spill again.
			Assert.Greater(Delay(2), Delay(1));
			Assert.AreEqual(Delay(1) * InterMultiplier, Delay(2));
		}

		[Test]
		public void PacingDisabledPutsOneManPerTick()
		{
			// Zero means "no wait": the callers then step once per tick, which is the
			// pre-pacing behaviour the Info descriptions promise for these values.
			Assert.AreEqual(0, Delay(1, intraDelay: 0));
			Assert.AreEqual(0, Delay(2, intraDelay: 0));
			Assert.AreEqual(0, Delay(1, groupSize: 0));
			Assert.AreEqual(0, Delay(1, groupSize: -1));
			Assert.AreEqual(0, Delay(1, intraDelay: -1));
		}

		[Test]
		public void GroupSizeOnePutsTheLongPauseBetweenEveryMan()
		{
			// Documented meaning of UnloadGroupSize: 1. Every man is his own group, so
			// every gap is the inter-group one.
			for (var unloaded = 1; unloaded <= 4; unloaded++)
				Assert.AreEqual(12, Delay(unloaded, groupSize: 1), $"gap after man {unloaded}");
		}

		[Test]
		public void AnEvenCadenceIsReachableAndNeverInverts()
		{
			// InterGroupUnloadDelayMultiplier: 1 is documented as "an even cadence".
			for (var unloaded = 1; unloaded <= 4; unloaded++)
				Assert.AreEqual(IntraDelay, Delay(unloaded, interMultiplier: 1));

			// A zero or negative multiplier clamps to 1 rather than collapsing the
			// inter-group gap to nothing — otherwise the pause between pairs would be
			// SHORTER than the pause inside one, and the pairing would read backwards.
			foreach (var multiplier in new[] { 0, -1, -5 })
			{
				Assert.AreEqual(IntraDelay, Delay(2, interMultiplier: multiplier));
				Assert.GreaterOrEqual(Delay(2, interMultiplier: multiplier), Delay(1, interMultiplier: multiplier));
			}
		}

		[Test]
		public void ABailIsNeverSlowerThanTheHullSurvives()
		{
			// Sanity bound on the emergency path: the cadence must empty a full stick
			// in a time a burning vehicle plausibly still exists for. Five men at the
			// shipped defaults is 4+12+4+12 = 32 ticks, under two seconds at 40ms.
			var total = 0;
			for (var unloaded = 1; unloaded < 5; unloaded++)
				total += Delay(unloaded);

			Assert.AreEqual(32, total);
			Assert.Less(total, 60, "a stick should be clear of the hull in well under a hull-fire lifetime");
		}
	}
}
