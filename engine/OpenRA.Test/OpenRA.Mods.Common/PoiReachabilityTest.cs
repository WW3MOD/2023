#region Copyright & License Information
/*
 * WW3MOD reachability-scoring math test — frontline-influence Phase 1.
 *
 * Pins the score multiplier + amphibious axis-typing decision for every GroundReach class, and the
 * through-crossing distance approximation. Gate-off byte-identity is the "reachable ⇒ 100" cases.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class PoiReachabilityTest
	{
		// Representative tuning: repairable 60 ≥ amphibious-uncrewed 30 ≥ unreachable 10, all < 100.
		const int Repairable = 60;
		const int Amphib = 30;
		const int Unreach = 10;

		static int Factor(GroundReach reach, bool amphibReachable, bool hasPool)
			=> PoiReachabilityMath.ReachabilityFactor(reach, amphibReachable, hasPool, Repairable, Amphib, Unreach);

		[Test]
		public void GroundReachableIsInert()
		{
			Assert.Multiple(() =>
			{
				Assert.That(Factor(GroundReach.Same, false, false), Is.EqualTo(100));
				Assert.That(Factor(GroundReach.IntactCrossing, false, false), Is.EqualTo(100));
				// Ground-reachable is unaffected by whether we own amphibious units.
				Assert.That(Factor(GroundReach.Same, true, true), Is.EqualTo(100));
			});
		}

		[Test]
		public void AmphibiousRescueKeepsFullValueOnlyWhenCrewable()
		{
			Assert.Multiple(() =>
			{
				// Amphibious route + amphibious units ⇒ full value (the axis will be amphibious-typed).
				Assert.That(Factor(GroundReach.AmphibiousOnly, amphibReachable: true, hasPool: true), Is.EqualTo(100));
				// Same water route but NO amphibious units ⇒ damped to the amphibious penalty.
				Assert.That(Factor(GroundReach.AmphibiousOnly, amphibReachable: true, hasPool: false), Is.EqualTo(Amphib));

				// A repairable-crossing POI that is ALSO amphibious-reachable is rescued when crewable
				// (the rescue branch wins over the repairable penalty).
				Assert.That(Factor(GroundReach.RepairableCrossing, amphibReachable: true, hasPool: true), Is.EqualTo(100));
			});
		}

		[Test]
		public void RepairableIsReducedNotEliminated()
		{
			// Destroyed-bridge-only POI, no amphibious rescue: reduced but stays on the radar.
			var f = Factor(GroundReach.RepairableCrossing, amphibReachable: false, hasPool: false);
			Assert.That(f, Is.EqualTo(Repairable));
			Assert.That(f, Is.GreaterThan(Factor(GroundReach.Unreachable, false, false)), "repairable beats unreachable");
			Assert.That(f, Is.LessThan(100), "still penalised vs a walkable POI");
		}

		[Test]
		public void UnreachableIsHeavilyDamped()
		{
			Assert.That(Factor(GroundReach.Unreachable, amphibReachable: false, hasPool: true), Is.EqualTo(Unreach));
			// Even owning amphibious units cannot rescue a genuinely unreachable POI.
			Assert.That(Factor(GroundReach.Unreachable, amphibReachable: false, hasPool: true), Is.LessThan(Repairable));
		}

		[Test]
		public void AmphibiousTypingMatchesRescueBranch()
		{
			Assert.Multiple(() =>
			{
				Assert.That(PoiReachabilityMath.ShouldTypeAmphibious(GroundReach.Same, true, true), Is.False,
					"a ground-reachable POI is never amphibious-typed");
				Assert.That(PoiReachabilityMath.ShouldTypeAmphibious(GroundReach.IntactCrossing, true, true), Is.False);
				Assert.That(PoiReachabilityMath.ShouldTypeAmphibious(GroundReach.AmphibiousOnly, true, true), Is.True);
				Assert.That(PoiReachabilityMath.ShouldTypeAmphibious(GroundReach.AmphibiousOnly, true, false), Is.False,
					"no amphibious units ⇒ not typed");
				Assert.That(PoiReachabilityMath.ShouldTypeAmphibious(GroundReach.RepairableCrossing, true, true), Is.True,
					"repairable POI also amphibious-reachable ⇒ send amphibious now");
				Assert.That(PoiReachabilityMath.ShouldTypeAmphibious(GroundReach.RepairableCrossing, false, true), Is.False,
					"repairable but NOT amphibious-reachable ⇒ ground axis (engineer later)");
			});
		}

		[Test]
		public void ThroughCrossingDistanceNeverShorterThanDirect()
		{
			Assert.Multiple(() =>
			{
				// Routing through a bridge is longer than the crow-flies line.
				Assert.That(PoiReachabilityMath.ThroughCrossingDistanceCells(10, 8, 9), Is.EqualTo(17));
				// Never reads closer than the direct distance.
				Assert.That(PoiReachabilityMath.ThroughCrossingDistanceCells(30, 5, 5), Is.EqualTo(30));
				Assert.That(PoiReachabilityMath.ThroughCrossingDistanceCells(0, 0, 0), Is.EqualTo(0));
			});
		}

		[Test]
		public void EffectiveDistanceKeepsCrowFliesUnlessBarrierAndCrossing()
		{
			Assert.Multiple(() =>
			{
				// Same bank (no barrier crossed): crow-flies is kept EXACTLY — this is the byte-identity case
				// for same-bank POIs (and, with the gate off, every POI, since no provider is passed).
				Assert.That(PoiReachabilityMath.EffectiveDistanceCells(12, crossesBarrier: false, hasCrossing: true, 5, 6),
					Is.EqualTo(12));

				// Barrier crossed but no crossing exists to route through: crow-flies kept (the reachability
				// FACTOR damps it separately; distance is not fabricated).
				Assert.That(PoiReachabilityMath.EffectiveDistanceCells(12, crossesBarrier: true, hasCrossing: false, 5, 6),
					Is.EqualTo(12));

				// Far bank across a barrier WITH a crossing: honest through-crossing detour (SR→crossing→POI),
				// which is longer than the crow-flies line ⇒ the central-crossing advantage becomes honest.
				Assert.That(PoiReachabilityMath.EffectiveDistanceCells(12, crossesBarrier: true, hasCrossing: true, 9, 10),
					Is.EqualTo(19));

				// The detour is clamped ≥ crow-flies (a crossing never reads CLOSER than straight-line).
				Assert.That(PoiReachabilityMath.EffectiveDistanceCells(30, crossesBarrier: true, hasCrossing: true, 5, 5),
					Is.EqualTo(30));
			});
		}
	}
}
