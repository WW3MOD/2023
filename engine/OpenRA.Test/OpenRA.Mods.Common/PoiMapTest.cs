#region Copyright & License Information
/*
 * WW3MOD PoiMap scoring tests — POI-strategy Phase 2.
 *
 * Pure-logic tests of PoiScoring, the engine-free math the PoiMap world trait
 * uses to rank strategic points of interest (money capturables + neutral/enemy
 * Supply Routes) by value x distance x threat. Actor/World construction is heavy,
 * so — exactly like GoalGuardLedger / InfluenceMapMath — the scoring is a pure
 * static class validated here without a World.
 *
 * Coverage: the four factor helpers (distance, threat bucket, ownership, combine)
 * plus the deterministic ordering + tie-break. These encode the plan's Phase 2
 * ranking invariants: closer/higher-value/safer POIs sort first; enemy SR deny
 * outranks a neutral SR; equal scores break by distance then id.
 */
#endregion

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class PoiMapTest
	{
		// ---------- DistanceFactor ----------

		[Test]
		public void DistanceFactor_Is100AtZeroAnd50AtHalfLife()
		{
			Assert.That(PoiScoring.DistanceFactor(0, 20), Is.EqualTo(100), "full at distance 0");
			Assert.That(PoiScoring.DistanceFactor(20, 20), Is.EqualTo(50), "half at the half-life distance");
		}

		[Test]
		public void DistanceFactor_DecreasesMonotonicallyWithDistance()
		{
			var near = PoiScoring.DistanceFactor(5, 20);
			var mid = PoiScoring.DistanceFactor(20, 20);
			var far = PoiScoring.DistanceFactor(60, 20);
			Assert.That(near, Is.GreaterThan(mid));
			Assert.That(mid, Is.GreaterThan(far));
		}

		[Test]
		public void DistanceFactor_NegativeInputsClamp()
		{
			Assert.That(PoiScoring.DistanceFactor(-10, 20), Is.EqualTo(100), "negative distance clamps to 0 → full");
			Assert.That(PoiScoring.DistanceFactor(20, 0), Is.EqualTo(PoiScoring.DistanceFactor(20, 1)),
				"non-positive half-life clamps to 1");
		}

		// ---------- ThreatFactor ----------

		[Test]
		public void ThreatFactor_BucketsOnEnemyInfluence()
		{
			// mildThreshold=20, safe=100, mild=40, hostile=10.
			Assert.That(PoiScoring.ThreatFactor(0, 20, 100, 40, 10), Is.EqualTo(100), "no influence → safe");
			Assert.That(PoiScoring.ThreatFactor(20, 20, 100, 40, 10), Is.EqualTo(40), "at threshold → mild");
			Assert.That(PoiScoring.ThreatFactor(21, 20, 100, 40, 10), Is.EqualTo(10), "above threshold → hostile");
		}

		[Test]
		public void ThreatFactor_NegativeInfluenceIsSafe()
		{
			Assert.That(PoiScoring.ThreatFactor(-5, 20, 100, 40, 10), Is.EqualTo(100));
		}

		// ---------- OwnershipMultiplier ----------

		[Test]
		public void OwnershipMultiplier_NeutralIncomePreferredOverEnemyIncome()
		{
			var neutral = PoiScoring.OwnershipMultiplier(PoiKind.IncomeStructure, PlayerRelationship.Neutral,
				100, 70, 70, 100);
			var enemy = PoiScoring.OwnershipMultiplier(PoiKind.IncomeStructure, PlayerRelationship.Enemy,
				100, 70, 70, 100);
			Assert.That(neutral, Is.EqualTo(100));
			Assert.That(enemy, Is.EqualTo(70), "enemy-owned income is defended → lower");
			Assert.That(neutral, Is.GreaterThan(enemy));
		}

		[Test]
		public void OwnershipMultiplier_EnemySupplyRoutePreferredOverNeutralSupplyRoute()
		{
			var enemySr = PoiScoring.OwnershipMultiplier(PoiKind.SupplyRoute, PlayerRelationship.Enemy,
				100, 70, 70, 100);
			var neutralSr = PoiScoring.OwnershipMultiplier(PoiKind.SupplyRoute, PlayerRelationship.Neutral,
				100, 70, 70, 100);
			Assert.That(enemySr, Is.EqualTo(100), "cutting the enemy's reinforcement lane is the prize");
			Assert.That(neutralSr, Is.EqualTo(70), "neutral SR is a lower-urgency forward hold");
			Assert.That(enemySr, Is.GreaterThan(neutralSr));
		}

		// ---------- Score: ranking invariants ----------

		[Test]
		public void Score_HigherValueRanksHigher_AllElseEqual()
		{
			var bio = PoiScoring.Score(150, 100, 100, 100);
			var oilb = PoiScoring.Score(50, 100, 100, 100);
			Assert.That(bio, Is.GreaterThan(oilb));
		}

		[Test]
		public void Score_CloserRanksHigher_SameValue()
		{
			var near = PoiScoring.Score(100, PoiScoring.DistanceFactor(5, 20), 100, 100);
			var far = PoiScoring.Score(100, PoiScoring.DistanceFactor(40, 20), 100, 100);
			Assert.That(near, Is.GreaterThan(far));
		}

		[Test]
		public void Score_SaferRanksHigher_SameValueAndDistance()
		{
			var safe = PoiScoring.Score(100, 100, PoiScoring.ThreatFactor(0, 20, 100, 40, 10), 100);
			var hostile = PoiScoring.Score(100, 100, PoiScoring.ThreatFactor(50, 20, 100, 40, 10), 100);
			Assert.That(safe, Is.GreaterThan(hostile));
		}

		[Test]
		public void Score_ClosePricyDerrickCanOutrankDistantHostileSupplyRoute()
		{
			// A near, safe, neutral BIO ($150) vs a far, hostile, enemy SR (deny 120).
			var bio = PoiScoring.Score(150,
				PoiScoring.DistanceFactor(6, 20),
				PoiScoring.ThreatFactor(0, 20, 100, 40, 10),
				PoiScoring.OwnershipMultiplier(PoiKind.IncomeStructure, PlayerRelationship.Neutral, 100, 70, 70, 100));

			var sr = PoiScoring.Score(120,
				PoiScoring.DistanceFactor(40, 20),
				PoiScoring.ThreatFactor(60, 20, 100, 40, 10),
				PoiScoring.OwnershipMultiplier(PoiKind.SupplyRoute, PlayerRelationship.Enemy, 100, 70, 70, 100));

			Assert.That(bio, Is.GreaterThan(sr),
				"threat + distance decay should let a safe near derrick outscore a distant contested SR");
		}

		// ---------- ApplyBias + opening income-secure priority ----------

		[Test]
		public void ApplyBias_BoostsAndDamps()
		{
			Assert.That(PoiScoring.ApplyBias(1000, 150), Is.EqualTo(1500), "150% boosts");
			Assert.That(PoiScoring.ApplyBias(1000, 80), Is.EqualTo(800), "80% damps");
			Assert.That(PoiScoring.ApplyBias(1000, 100), Is.EqualTo(1000), "100% is identity");
			Assert.That(PoiScoring.ApplyBias(1000, -5), Is.EqualTo(0), "negative bias clamps to 0");
		}

		[Test]
		public void OpeningPriority_NeutralIncomeSecureOutranksDistantEnemyBase()
		{
			// The opening default: a mid-distance NEUTRAL derrick (Secure, income-biased
			// 150%) must outrank a farther ENEMY Supply Route (Pressure, attack-damped 80%),
			// so the army spreads to secure income before pushing the base (decision #3).
			var neutralOilb = PoiScoring.ApplyBias(
				PoiScoring.Score(50,
					PoiScoring.DistanceFactor(16, 20),
					PoiScoring.ThreatFactor(0, 20, 100, 40, 10),
					PoiScoring.OwnershipMultiplier(PoiKind.IncomeStructure, PlayerRelationship.Neutral, 100, 70, 70, 100)),
				150);

			var enemySr = PoiScoring.ApplyBias(
				PoiScoring.Score(120,
					PoiScoring.DistanceFactor(52, 20),
					PoiScoring.ThreatFactor(0, 20, 100, 40, 10),
					PoiScoring.OwnershipMultiplier(PoiKind.SupplyRoute, PlayerRelationship.Enemy, 100, 70, 70, 100)),
				80);

			Assert.That(neutralOilb, Is.GreaterThan(enemySr),
				"opening should secure a near neutral derrick before pushing the distant enemy base");
		}

		[Test]
		public void OpeningPriority_ClosestNeutralIncomeRanksFirstAmongCapturables()
		{
			// Among equal-value neutral money POIs the CLOSEST wins (closest-first default).
			var near = PoiScoring.ApplyBias(PoiScoring.Score(50, PoiScoring.DistanceFactor(10, 20), 100, 100), 150);
			var far = PoiScoring.ApplyBias(PoiScoring.Score(50, PoiScoring.DistanceFactor(35, 20), 100, 100), 150);
			Assert.That(near, Is.GreaterThan(far));
		}

		// ---------- CompareForOrder: deterministic tie-break ----------

		[Test]
		public void CompareForOrder_HigherScoreFirst()
		{
			Assert.That(PoiScoring.CompareForOrder(500, 10, 1, 300, 5, 2), Is.LessThan(0),
				"higher score sorts before (negative = a first)");
			Assert.That(PoiScoring.CompareForOrder(300, 5, 2, 500, 10, 1), Is.GreaterThan(0));
		}

		[Test]
		public void CompareForOrder_EqualScoreBreaksByDistanceThenId()
		{
			Assert.That(PoiScoring.CompareForOrder(500, 5, 9, 500, 12, 1), Is.LessThan(0),
				"equal score → nearer wins");
			Assert.That(PoiScoring.CompareForOrder(500, 7, 1, 500, 7, 2), Is.LessThan(0),
				"equal score + equal distance → lower id wins");
			Assert.That(PoiScoring.CompareForOrder(500, 7, 5, 500, 7, 5), Is.EqualTo(0),
				"fully equal → 0");
		}

		[Test]
		public void CompareForOrder_ProducesStableSortedRanking()
		{
			// Three POIs; sort with the comparator and assert the expected order.
			var items = new List<(long score, int dist, uint id, string tag)>
			{
				(300, 5, 3, "mid"),
				(500, 20, 1, "top"),
				(300, 2, 2, "mid-closer"),
			};

			items.Sort((a, b) => PoiScoring.CompareForOrder(a.score, a.dist, a.id, b.score, b.dist, b.id));

			Assert.That(items[0].tag, Is.EqualTo("top"), "highest score first");
			Assert.That(items[1].tag, Is.EqualTo("mid-closer"), "tie on score → nearer next");
			Assert.That(items[2].tag, Is.EqualTo("mid"));
		}
	}
}
