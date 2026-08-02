#region Copyright & License Information
/*
 * WW3MOD MissionCommitmentMath tests — POI-strategy Phase-1 anti-thrash stopgap.
 *
 * Pure-logic tests of the SQUAD-level mission-commitment decision math that stops
 * the experimental offense module re-tasking a committed axis every re-eval just
 * because scores jittered (the user's live-game "go one way, stop, reverse, loop"
 * churn). Where GoalGuardLedger answers "is this UNIT still claimed",
 * MissionCommitmentMath answers "should this SQUAD's mission be ABANDONED now".
 *
 * The headline is Hold_PersistsUnderSmallScoreShift: a committed axis whose score
 * merely wobbles is NOT released; only the four explicit triggers release it.
 * Exactly like PoiOffenseMath / GoalGuardLedger, the math is a pure static class
 * validated without a World, so it ports verbatim into a future v3 brain.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class MissionCommitmentMathTest
	{
		// Default tuning mirrored from PoiOffensiveBotModuleInfo so the pins document the shipped knobs.
		const int SpikePct = 50;
		const int SpikeFloor = 40;
		const int OppMarginPct = 50;
		const int IneffNum = 1;
		const int IneffDen = 2;

		// Convenience wrapper: a HEALTHY committed axis (valid objective, no window, danger unchanged,
		// no rival, full strength) so each test can perturb exactly one input and assert the trigger.
		static bool ShouldReassign(
			bool objectiveValid = true,
			int commitTick = 0, int currentTick = 0, int windowTicks = 0,
			int commitDanger = 0, int currentDanger = 0,
			long committedScore = 1000, long bestAlternativeScore = 0,
			int commitStrength = 6, int currentStrength = 6)
			=> MissionCommitmentMath.ShouldReassign(
				objectiveValid,
				commitTick, currentTick, windowTicks,
				commitDanger, currentDanger, SpikePct, SpikeFloor,
				committedScore, bestAlternativeScore, OppMarginPct,
				commitStrength, currentStrength, IneffNum, IneffDen);

		// ---------- headline: hold under jitter ----------

		[Test]
		public void Hold_PersistsUnderSmallScoreShift()
		{
			// Committed at 1000; a rival at 1400 is +40% — below the 50% margin — and the committed
			// score itself wobbling ±10% changes nothing. The mission is HELD (do not re-task).
			Assert.That(ShouldReassign(committedScore: 1000, bestAlternativeScore: 1400), Is.False);
			Assert.That(ShouldReassign(committedScore: 900, bestAlternativeScore: 1000), Is.False);
			Assert.That(ShouldReassign(committedScore: 1100, bestAlternativeScore: 1000), Is.False);
		}

		[Test]
		public void Hold_PersistsWithNoRivalAndStableConditions()
		{
			Assert.That(ShouldReassign(), Is.False);
		}

		// ---------- trigger 1: objective invalid ----------

		[Test]
		public void Trigger1_InvalidObjective_ReleasesImmediately()
		{
			// Even a strong, safe, full-strength squad is released the instant its target is gone.
			Assert.That(ShouldReassign(objectiveValid: false, bestAlternativeScore: 0, currentStrength: 6),
				Is.True);
		}

		// ---------- trigger 2: danger spike ----------

		[Test]
		public void Trigger2_DangerSpike_FromQuietGround_UsesAbsoluteFloor()
		{
			// commit danger 0: a fresh envelope must exceed the absolute floor (40) to trip.
			Assert.That(MissionCommitmentMath.DangerSpiked(0, 40, SpikePct, SpikeFloor), Is.False, "== floor holds");
			Assert.That(MissionCommitmentMath.DangerSpiked(0, 41, SpikePct, SpikeFloor), Is.True, "> floor spikes");
			Assert.That(ShouldReassign(commitDanger: 0, currentDanger: 41), Is.True);
			Assert.That(ShouldReassign(commitDanger: 0, currentDanger: 40), Is.False);
		}

		[Test]
		public void Trigger2_DangerSpike_FromDangerousGround_UsesPercentage()
		{
			// commit danger 100: margin = max(40, 100*50/100 = 50) = 50, so needs > 150 to trip.
			Assert.That(MissionCommitmentMath.DangerSpiked(100, 150, SpikePct, SpikeFloor), Is.False, "== 100+50 holds");
			Assert.That(MissionCommitmentMath.DangerSpiked(100, 151, SpikePct, SpikeFloor), Is.True, "> 100+50 spikes");
		}

		[Test]
		public void Trigger2_DangerDropOrEqual_NeverSpikes()
		{
			Assert.That(MissionCommitmentMath.DangerSpiked(100, 100, SpikePct, SpikeFloor), Is.False);
			Assert.That(MissionCommitmentMath.DangerSpiked(100, 20, SpikePct, SpikeFloor), Is.False);
		}

		// ---------- trigger 3: better opportunity (hysteresis margin) ----------

		[Test]
		public void Trigger3_BetterOpportunity_BoundaryAtMargin()
		{
			// margin 50%: committed 1000 ⇒ threshold is strictly > 1500.
			Assert.That(MissionCommitmentMath.BetterOpportunity(1000, 1500, OppMarginPct), Is.False, "== 1.5x holds");
			Assert.That(MissionCommitmentMath.BetterOpportunity(1000, 1501, OppMarginPct), Is.True, "> 1.5x switches");
			Assert.That(ShouldReassign(committedScore: 1000, bestAlternativeScore: 1501), Is.True);
			Assert.That(ShouldReassign(committedScore: 1000, bestAlternativeScore: 1500), Is.False);
		}

		[Test]
		public void Trigger3_NonPositiveScores_AreGuarded()
		{
			Assert.That(MissionCommitmentMath.BetterOpportunity(1000, 0, OppMarginPct), Is.False, "no rival");
			Assert.That(MissionCommitmentMath.BetterOpportunity(0, 1, OppMarginPct), Is.True, "zeroed commit is beatable");
			Assert.That(MissionCommitmentMath.BetterOpportunity(-5, 1, OppMarginPct), Is.True);
		}

		// ---------- trigger 4: combat-ineffective ----------

		[Test]
		public void Trigger4_CombatIneffective_BelowHalfCommitStrength()
		{
			// commit 6, fraction 1/2 ⇒ threshold at 3: current 3 holds, current 2 releases.
			Assert.That(MissionCommitmentMath.CombatIneffective(6, 3, IneffNum, IneffDen), Is.False, "half holds");
			Assert.That(MissionCommitmentMath.CombatIneffective(6, 2, IneffNum, IneffDen), Is.True, "below half releases");
			Assert.That(ShouldReassign(commitStrength: 6, currentStrength: 2), Is.True);
			Assert.That(ShouldReassign(commitStrength: 6, currentStrength: 3), Is.False);
		}

		[Test]
		public void Trigger4_DegenerateCommitStrength_NeverTrips()
		{
			Assert.That(MissionCommitmentMath.CombatIneffective(0, 0, IneffNum, IneffDen), Is.False);
			Assert.That(MissionCommitmentMath.CombatIneffective(6, 3, IneffNum, 0), Is.False, "denom 0 guarded");
		}

		// ---------- optional commit window ----------

		[Test]
		public void CommitWindow_ZeroMeansHoldUntilTrigger()
		{
			// window 0 (default): even a very old commitment holds if no other trigger fires.
			Assert.That(ShouldReassign(commitTick: 0, currentTick: 100000, windowTicks: 0), Is.False);
		}

		[Test]
		public void CommitWindow_ElapsedForcesReplan()
		{
			// window 250: still held at 249 ticks, released at exactly 250.
			Assert.That(ShouldReassign(commitTick: 0, currentTick: 249, windowTicks: 250), Is.False);
			Assert.That(ShouldReassign(commitTick: 0, currentTick: 250, windowTicks: 250), Is.True);
		}

		// ---------- anti-thrash across an eval sequence ----------

		[Test]
		public void AntiThrash_HeldForWholeSequenceOfJitteryEvals()
		{
			// Simulate 20 re-evals of a committed axis whose score and danger wobble within noise and
			// which takes light casualties (6→4, still above half). It must be held EVERY eval — the
			// pure-math analogue of "one order for the whole flicker window" from the ledger tests.
			var releases = 0;
			var committedScore = 1000L;
			for (var eval = 0; eval < 20; eval++)
			{
				// Deterministic wobble: score ±12%, a rival that never clears the 50% margin, danger drifts
				// under the floor, strength ebbs to 4. No RNG — a fixed, reproducible perturbation.
				var score = committedScore + (eval % 2 == 0 ? 120 : -120);
				var rival = score + score * 40 / 100;    // +40% rival, below the 50% margin
				var danger = eval * 2;                    // 0..38, below the 40 floor
				var strength = eval < 10 ? 6 : 4;         // loses two, still above half of 6

				if (ShouldReassign(committedScore: score, bestAlternativeScore: rival,
					commitDanger: 0, currentDanger: danger, commitStrength: 6, currentStrength: strength))
					releases++;
			}

			Assert.That(releases, Is.EqualTo(0), "a committed mission is never re-tasked on jitter alone");
		}
	}
}
