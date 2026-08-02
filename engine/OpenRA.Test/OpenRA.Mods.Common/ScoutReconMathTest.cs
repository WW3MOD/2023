#region Copyright & License Information
/*
 * WW3MOD scout-heli (littlebird) rotating-recon scoring (Item A) pure-math pin.
 *
 * Pins ScoutReconMath.Score, the deterministic recon-desirability score the scout-heli picker uses to
 * choose a rotating destination (believed POI or stalest far area) instead of the old degenerate
 * first-in-bounds corner. The key properties: the int.MaxValue "never explored" sentinel is clamped
 * (no overflow when bonuses are added), POIs outrank edges outrank plain staleness, and distance is a
 * far-first tie-break. Pure integer math; no world mounted; deterministic.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class ScoutReconMathTest
	{
		[Test]
		public void NeverExploredSentinelIsClampedNoOverflow()
		{
			// int.MaxValue (GetExplorationAge "never visited") must clamp to MaxTrackedAge so adding the POI
			// and edge bonuses cannot overflow into a negative score.
			var score = ScoutReconMath.Score(int.MaxValue, isEdge: true, isPoi: true, distToScoutCells: 5);
			Assert.That(score, Is.EqualTo(ScoutReconMath.MaxTrackedAge + ScoutReconMath.EdgeBonus + ScoutReconMath.PoiBonus + 5));
			Assert.That(score, Is.GreaterThan(0), "clamped staleness + bonuses stays positive (no overflow)");
		}

		[Test]
		public void PoiOutranksEdgeOutranksPlainStaleness()
		{
			// Same staleness/distance: a believed POI beats an edge cell beats a plain interior cell.
			var plain = ScoutReconMath.Score(1000, isEdge: false, isPoi: false, distToScoutCells: 0);
			var edge = ScoutReconMath.Score(1000, isEdge: true, isPoi: false, distToScoutCells: 0);
			var poi = ScoutReconMath.Score(1000, isEdge: false, isPoi: true, distToScoutCells: 0);

			Assert.That(edge, Is.GreaterThan(plain));
			Assert.That(poi, Is.GreaterThan(edge));
		}

		[Test]
		public void DistanceIsAFarFirstTieBreak()
		{
			// Two otherwise-identical never-explored cells: the farther one from the scout wins, so the sweep
			// spreads out over the map from the opening instead of camping the nearest (corner) cell.
			var near = ScoutReconMath.Score(int.MaxValue, isEdge: false, isPoi: false, distToScoutCells: 3);
			var far = ScoutReconMath.Score(int.MaxValue, isEdge: false, isPoi: false, distToScoutCells: 40);
			Assert.That(far, Is.GreaterThan(near));
		}

		[Test]
		public void StalenessMattersBelowTheClamp()
		{
			// Below the clamp, a staler cell scores higher (all else equal) — recon rotates back to areas
			// that have gone stale.
			var fresher = ScoutReconMath.Score(100, isEdge: false, isPoi: false, distToScoutCells: 0);
			var staler = ScoutReconMath.Score(5000, isEdge: false, isPoi: false, distToScoutCells: 0);
			Assert.That(staler, Is.GreaterThan(fresher));
		}

		[Test]
		public void Deterministic()
		{
			Assert.That(
				ScoutReconMath.Score(1234, true, false, 7),
				Is.EqualTo(ScoutReconMath.Score(1234, true, false, 7)));
		}
	}
}
