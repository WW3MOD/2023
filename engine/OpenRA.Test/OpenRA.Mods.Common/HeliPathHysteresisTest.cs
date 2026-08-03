#region Copyright & License Information
/*
 * WW3MOD attack-heli flight-path hysteresis test.
 *
 * Pins HeliPathHysteresis — the decision math that stops the heli FSM re-issuing a fresh move /
 * attack-move path on every 5-tick re-eval (which read as indecisive trajectory churn during
 * employment cycles). A recomputed destination is ADOPTED only when it has shifted at least the
 * threshold (Chebyshev) from the leg the squad is already committed to; a sub-threshold jitter is
 * ignored so the squad holds its committed leg and moves deliberately. Pure integer math, no world
 * mounted, deterministic — it affects order CADENCE only, never which destination the standoff /
 * danger-nav / frontier logic chooses, so the first-contact AA gate and strategic pin are untouched.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits.BotModules.Squads;

namespace OpenRA.Test
{
	[TestFixture]
	public class HeliPathHysteresisTest
	{
		const int Threshold = 3;

		// ---------- Chebyshev cell distance ----------

		[Test]
		public void CellDistance_IsChebyshev_MaxOfAxisDeltas()
		{
			// Diagonal reads the max axis delta, NOT the Euclidean ~1.4x over-read (conventions.md).
			Assert.That(HeliPathHysteresis.CellDistance(new CPos(0, 0), new CPos(3, 3)), Is.EqualTo(3));
			Assert.That(HeliPathHysteresis.CellDistance(new CPos(0, 0), new CPos(5, 2)), Is.EqualTo(5));
			Assert.That(HeliPathHysteresis.CellDistance(new CPos(10, 4), new CPos(10, 4)), Is.EqualTo(0));
		}

		[Test]
		public void CellDistance_IsSignAgnostic()
		{
			// Negative deltas are magnitude-only and the metric is symmetric.
			Assert.That(HeliPathHysteresis.CellDistance(new CPos(7, 7), new CPos(2, 9)), Is.EqualTo(5));
			Assert.That(HeliPathHysteresis.CellDistance(new CPos(2, 9), new CPos(7, 7)), Is.EqualTo(5));
		}

		// ---------- ShouldRetarget: no commit yet ----------

		[Test]
		public void NoCommittedDestination_AlwaysRetargets()
		{
			// The very first leg has nothing to hold onto — always adopt the candidate.
			Assert.That(HeliPathHysteresis.ShouldRetarget(false, new CPos(0, 0), new CPos(0, 0), Threshold), Is.True);
			Assert.That(HeliPathHysteresis.ShouldRetarget(false, default, new CPos(99, 99), Threshold), Is.True);
		}

		// ---------- ShouldRetarget: hysteresis disabled ----------

		[Test]
		public void ThresholdNonPositive_AlwaysRetargets_HysteresisOff()
		{
			// Threshold <= 0 = hysteresis off ⇒ always adopt the fresh cell (the byte-identical-off contract:
			// the caller feeds 0 when the flag is off, so every re-eval re-paths exactly as the frozen code did).
			Assert.That(HeliPathHysteresis.ShouldRetarget(true, new CPos(0, 0), new CPos(0, 0), 0), Is.True);
			Assert.That(HeliPathHysteresis.ShouldRetarget(true, new CPos(0, 0), new CPos(1, 0), 0), Is.True);
			Assert.That(HeliPathHysteresis.ShouldRetarget(true, new CPos(0, 0), new CPos(1, 0), -5), Is.True);
		}

		// ---------- ShouldRetarget: the hysteresis band ----------

		[Test]
		public void SubThresholdShift_HoldsCommittedLeg()
		{
			// A committed leg at (10,10): a candidate 1 or 2 cells away is below the 3-cell threshold ⇒ HOLD
			// (do not re-path). This is the anti-churn core — small target/field jitter no longer re-issues.
			Assert.That(HeliPathHysteresis.ShouldRetarget(true, new CPos(10, 10), new CPos(11, 10), Threshold), Is.False);
			Assert.That(HeliPathHysteresis.ShouldRetarget(true, new CPos(10, 10), new CPos(12, 12), Threshold), Is.False);
		}

		[Test]
		public void AtOrAboveThreshold_Retargets_BoundaryExact()
		{
			// Exactly at the threshold retargets (>=), and beyond it too — a genuinely relocated objective is
			// still followed.
			Assert.That(HeliPathHysteresis.ShouldRetarget(true, new CPos(10, 10), new CPos(13, 10), Threshold), Is.True);
			Assert.That(HeliPathHysteresis.ShouldRetarget(true, new CPos(10, 10), new CPos(10, 14), Threshold), Is.True);
		}

		// ---------- anti-thrash across an eval sequence ----------

		[Test]
		public void AntiThrash_HeldWhileCandidateJittersWithinBand()
		{
			// Simulate a committed retreat/approach leg at (20,20) while the recomputed candidate jitters ±2
			// cells each eval (target shuffling, danger field re-stamp). With a 3-cell threshold the squad must
			// NOT retarget on any of them — the pure-math analogue of "movement reads deliberate".
			var committed = new CPos(20, 20);
			var jitter = new[] { new CPos(21, 20), new CPos(19, 21), new CPos(22, 22), new CPos(20, 18), new CPos(18, 20) };

			var retargets = 0;
			foreach (var candidate in jitter)
				if (HeliPathHysteresis.ShouldRetarget(true, committed, candidate, Threshold))
					retargets++;

			Assert.That(retargets, Is.EqualTo(0), "sub-threshold jitter must never re-path the committed leg");
		}
	}
}
