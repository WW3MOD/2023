#region Copyright & License Information
/*
 * WW3MOD PoiGarrison tests — POI-strategy Phase 4 (hold captured money).
 *
 * Pure-logic tests of the two pieces of math Phase 4 adds:
 *   * PoiScoring.DefendThreatFactor — the DEFENCE-urgency bucket (the mirror of the
 *     capture ThreatFactor: for a POI we hold, enemy pressure RAISES urgency).
 *   * PoiGarrisonMath.GarrisonSize / AllocateGarrisons — how big a garrison each held
 *     money POI wants (value ramp + threat bump, clamped small) and how a shared pool
 *     is split across them in priority order.
 *
 * Exactly like PoiScoring / PoiOffenseMath / GoalGuardLedger, the decision math is a
 * pure static class validated here without a World — it ports verbatim into a future
 * v3 brain. These encode the Phase 4 invariants from the plan (§5.3, §6 Phase 4):
 *   * every held money POI gets a small garrison (1-3), scaled by value;
 *   * a POI under assault pulls a bigger garrison (still capped) and ranks first;
 *   * garrisons are funded highest-urgency-first from spare capacity, tail dropped —
 *     so a handful of held POIs never starves the offense pool.
 */
#endregion

using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class PoiGarrisonTest
	{
		// ---------- DefendThreatFactor (defence-urgency, inverse of capture threat) ----------

		[Test]
		public void DefendThreatFactor_BucketsRaiseUrgencyWithEnemyInfluence()
		{
			// mildThreshold=20, calm=100, probed=150, assaulted=250.
			Assert.That(PoiScoring.DefendThreatFactor(0, 20, 100, 150, 250), Is.EqualTo(100), "no influence → calm");
			Assert.That(PoiScoring.DefendThreatFactor(20, 20, 100, 150, 250), Is.EqualTo(150), "at threshold → probed");
			Assert.That(PoiScoring.DefendThreatFactor(21, 20, 100, 150, 250), Is.EqualTo(250), "above threshold → assaulted");
		}

		[Test]
		public void DefendThreatFactor_NegativeInfluenceIsCalm()
		{
			Assert.That(PoiScoring.DefendThreatFactor(-5, 20, 100, 150, 250), Is.EqualTo(100));
		}

		[Test]
		public void DefendThreatFactor_IsTheInverseOfCaptureThreatFactor()
		{
			// For CAPTURE, more enemies = LOWER factor (deters). For DEFENCE, more
			// enemies = HIGHER factor (urgency). Same buckets, opposite ordering.
			var captureSafe = PoiScoring.ThreatFactor(0, 20, 100, 40, 10);
			var captureHostile = PoiScoring.ThreatFactor(50, 20, 100, 40, 10);
			Assert.That(captureSafe, Is.GreaterThan(captureHostile), "capture: threat deters");

			var defendCalm = PoiScoring.DefendThreatFactor(0, 20, 100, 150, 250);
			var defendAssaulted = PoiScoring.DefendThreatFactor(50, 20, 100, 150, 250);
			Assert.That(defendAssaulted, Is.GreaterThan(defendCalm), "defence: threat raises urgency");
		}

		[Test]
		public void DefendScore_AssaultedHeldPoiOutranksCalmHeldPoi_SameValue()
		{
			// Two equal-value derricks we hold; the one under assault must sort first so
			// the garrison layer defends the contested income before the quiet one.
			var calm = PoiScoring.Score(50, 100,
				PoiScoring.DefendThreatFactor(0, 20, 100, 150, 250), 100);
			var assaulted = PoiScoring.Score(50, 100,
				PoiScoring.DefendThreatFactor(50, 20, 100, 150, 250), 100);
			Assert.That(assaulted, Is.GreaterThan(calm));
		}

		// ---------- GarrisonSize: value ramp ----------

		[Test]
		public void GarrisonSize_ValueRampGivesOneTwoThreeForOilbFcomBio()
		{
			// valuePerUnit=50, [min 1, max 3], calm (no threat bump).
			Assert.That(PoiGarrisonMath.GarrisonSize(50, 0, 20, 50, 1, 3, 1), Is.EqualTo(1), "OILB $50 → 1");
			Assert.That(PoiGarrisonMath.GarrisonSize(100, 0, 20, 50, 1, 3, 1), Is.EqualTo(2), "FCOM $100 → 2");
			Assert.That(PoiGarrisonMath.GarrisonSize(150, 0, 20, 50, 1, 3, 1), Is.EqualTo(3), "BIO $150 → 3");
		}

		[Test]
		public void GarrisonSize_ClampsToMinAndMax()
		{
			Assert.That(PoiGarrisonMath.GarrisonSize(5, 0, 20, 50, 1, 3, 1), Is.EqualTo(1),
				"a near-worthless POI still gets the token min garrison");
			Assert.That(PoiGarrisonMath.GarrisonSize(1000, 0, 20, 50, 1, 3, 1), Is.EqualTo(3),
				"a huge-value POI is still capped at max (garrisons stay small)");
		}

		[Test]
		public void GarrisonSize_IsMonotonicNonDecreasingInValue()
		{
			var prev = 0;
			for (var v = 0; v <= 300; v += 25)
			{
				var s = PoiGarrisonMath.GarrisonSize(v, 0, 20, 50, 1, 3, 1);
				Assert.That(s, Is.GreaterThanOrEqualTo(prev), $"size must not drop as value grows (v={v})");
				prev = s;
			}
		}

		// ---------- GarrisonSize: threat bump ----------

		[Test]
		public void GarrisonSize_AssaultBumpsGarrison_StillCapped()
		{
			// Under HOSTILE influence (>mildThreshold) a cheap POI reinforces by the bonus…
			Assert.That(PoiGarrisonMath.GarrisonSize(50, 50, 20, 50, 1, 3, 1), Is.EqualTo(2),
				"OILB under assault: 1 + 1 bonus = 2");
			// …but the priciest POI is already at the cap, so the bonus can't exceed max.
			Assert.That(PoiGarrisonMath.GarrisonSize(150, 50, 20, 50, 1, 3, 1), Is.EqualTo(3),
				"BIO under assault: 3 + 1 bonus clamps back to 3");
		}

		[Test]
		public void GarrisonSize_MildThreatDoesNotBump()
		{
			// At/below the mild threshold the POI is only "probed", not assaulted — no bump.
			Assert.That(PoiGarrisonMath.GarrisonSize(50, 20, 20, 50, 1, 3, 1), Is.EqualTo(1),
				"probed (≤ threshold) does not add the assault bonus");
			Assert.That(PoiGarrisonMath.GarrisonSize(50, 21, 20, 50, 1, 3, 1), Is.EqualTo(2),
				"just above the threshold does add it");
		}

		// ---------- AllocateGarrisons: priority funding from a shared pool ----------

		[Test]
		public void AllocateGarrisons_FundsEachFullyWhenPoolIsAmple()
		{
			var alloc = PoiGarrisonMath.AllocateGarrisons(new[] { 3, 2, 1 }, 10);
			Assert.That(alloc, Is.EqualTo(new[] { 3, 2, 1 }));
			Assert.That(alloc.Sum(), Is.EqualTo(6), "spare pool keeps the rest for offense");
		}

		[Test]
		public void AllocateGarrisons_PrioritisesHighestUrgencyFirst_TailDropped()
		{
			// Sizes arrive score-desc; pool of 4 funds the first two fully, drops the tail
			// (so we hold the most-urgent POIs rather than dribble every one thin).
			var alloc = PoiGarrisonMath.AllocateGarrisons(new[] { 3, 2, 2 }, 4);
			Assert.That(alloc.Sum(), Is.LessThanOrEqualTo(4));
			Assert.That(alloc[0], Is.EqualTo(3), "top-urgency POI fully funded first");
			Assert.That(alloc[1], Is.EqualTo(1), "next gets the remainder");
			Assert.That(alloc[2], Is.EqualTo(0), "lowest-urgency POI dropped when the pool is exhausted");
		}

		[Test]
		public void AllocateGarrisons_NeverExceedsPool_ProtectsOffense()
		{
			var alloc = PoiGarrisonMath.AllocateGarrisons(new[] { 3, 3, 3, 3 }, 5);
			Assert.That(alloc.Sum(), Is.EqualTo(5), "garrisons never claim more than the spare pool");
		}

		[Test]
		public void AllocateGarrisons_EmptyOrZeroPool()
		{
			Assert.That(PoiGarrisonMath.AllocateGarrisons(new int[0], 10), Is.Empty);
			Assert.That(PoiGarrisonMath.AllocateGarrisons(new[] { 3, 2 }, 0), Is.EqualTo(new[] { 0, 0 }));
		}

		// ---------- Integration: the "hold, don't starve offense" invariant ----------

		[Test]
		public void HeldMoneyPois_GetSmallGarrisons_LeavingArmyForOffense()
		{
			// Three held money POIs (OILB/FCOM/BIO) out of a 25-unit ground pool: garrisons
			// total at most 1+2+3 = 6, leaving ≥19 for the offense axes — no starvation.
			var sizes = new[]
			{
				PoiGarrisonMath.GarrisonSize(150, 0, 20, 50, 1, 3, 1), // BIO → 3
				PoiGarrisonMath.GarrisonSize(100, 0, 20, 50, 1, 3, 1), // FCOM → 2
				PoiGarrisonMath.GarrisonSize(50, 0, 20, 50, 1, 3, 1),  // OILB → 1
			};

			var alloc = PoiGarrisonMath.AllocateGarrisons(sizes, 25);
			Assert.That(alloc.Sum(), Is.EqualTo(6), "all three held POIs garrisoned…");
			Assert.That(25 - alloc.Sum(), Is.EqualTo(19), "…and the bulk of the army is free for offense");
			foreach (var a in alloc)
				Assert.That(a, Is.InRange(1, 3), "each garrison is small (1-3)");
		}
	}
}
