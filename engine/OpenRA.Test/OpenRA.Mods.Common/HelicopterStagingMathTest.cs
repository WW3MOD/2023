#region Copyright & License Information
/*
 * WW3MOD — HelicopterSquadBotModule forward-staging (Option A) pure-math pin.
 *
 * Pins HeliStagingMath.StagePos, the deterministic WPos interpolation the experimental,
 * default-OFF forward-staging pass uses to place an idle attack heli a fraction of the way
 * from its Supply Route toward the top PoiMap offensive target. Mirrors
 * MountedTransportBotModule.PreContactStagingCell. World-free, zero RNG — this pins the
 * ON-path determinism the byte-identity argument relies on (see influence-stack.md Invariants).
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class HelicopterStagingMathTest
	{
		[Test]
		public void ZeroPctIsTheSupplyRoute()
		{
			var sr = new WPos(1024, 2048, 0);
			var tgt = new WPos(11264, 2048, 0);

			Assert.That(HeliStagingMath.StagePos(sr, tgt, 0), Is.EqualTo(sr),
				"0% stages exactly at the SR");
		}

		[Test]
		public void HundredPctIsTheTarget()
		{
			var sr = new WPos(1024, 2048, 0);
			var tgt = new WPos(11264, 2048, 0);

			Assert.That(HeliStagingMath.StagePos(sr, tgt, 100), Is.EqualTo(tgt),
				"100% stages exactly at the target");
		}

		[Test]
		public void FiftyPctIsTheMidpoint()
		{
			var sr = new WPos(1024, 2048, 0);
			var tgt = new WPos(11264, 6144, 0);

			// (11264-1024)/2 = 5120 -> X = 6144 ; (6144-2048)/2 = 2048 -> Y = 4096.
			var expected = new WPos(6144, 4096, 0);
			Assert.That(HeliStagingMath.StagePos(sr, tgt, 50), Is.EqualTo(expected),
				"50% stages at the geometric midpoint");
		}

		[Test]
		public void DeterministicAcrossRepeatedCalls()
		{
			var sr = new WPos(1500, 9000, 0);
			var tgt = new WPos(23000, 512, 0);

			var first = HeliStagingMath.StagePos(sr, tgt, 40);
			for (var i = 0; i < 64; i++)
				Assert.That(HeliStagingMath.StagePos(sr, tgt, 40), Is.EqualTo(first),
					"same inputs -> identical output (no hidden state / RNG)");
		}
	}
}
