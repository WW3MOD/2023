#region Copyright & License Information
/*
 * WW3MOD CombatRetreatMath tests — @experimental force-preservation (combat-quality levers 1+2).
 *
 * Pure-logic pins for the retreat FSM that turns a believed local force ratio into a
 * retreat/engage decision. Like PoiOffenseMath / CaptureSupplyMath the decision math is a
 * pure static class validated here without a World — it ports verbatim into a future v3 brain.
 *
 * These encode the combat-quality invariants:
 *   * an axis retreats only when losing is SUSTAINED for the window (no single-read flinch);
 *   * hysteresis — a retreating axis COMMITS until safe or the ratio recovers past a STRICTER
 *     re-engage margin (no advance/retreat oscillation at the boundary);
 *   * safety ends a retreat;
 *   * flag-off == legacy (never retreats regardless of state).
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class CombatRetreatTest
	{
		// ---------- LosingBeyond ----------

		[Test]
		public void LosingBeyond_TriggersAtRatioBoundary()
		{
			// ratio 200 = enemy must be >= 2x own. own=100.
			Assert.That(CombatRetreatMath.LosingBeyond(100, 199, 200), Is.False, "just under 2x is not losing");
			Assert.That(CombatRetreatMath.LosingBeyond(100, 200, 200), Is.True, "exactly 2x is losing (>=)");
			Assert.That(CombatRetreatMath.LosingBeyond(100, 201, 200), Is.True, "over 2x is losing");
		}

		[Test]
		public void LosingBeyond_NoEnemyNeverLosing()
		{
			Assert.That(CombatRetreatMath.LosingBeyond(0, 0, 200), Is.False);
			Assert.That(CombatRetreatMath.LosingBeyond(100, 0, 200), Is.False);
		}

		[Test]
		public void LosingBeyond_WipedAxisWithEnemyPresentIsLosing()
		{
			Assert.That(CombatRetreatMath.LosingBeyond(0, 50, 200), Is.True);
		}

		// ---------- RecoveredWithin ----------

		[Test]
		public void RecoveredWithin_AtReengageBoundary()
		{
			// reengage 120 = recovered once enemy <= 1.2x own. own=100.
			Assert.That(CombatRetreatMath.RecoveredWithin(100, 120, 120), Is.True, "exactly 1.2x is recovered (<=)");
			Assert.That(CombatRetreatMath.RecoveredWithin(100, 121, 120), Is.False, "over 1.2x is not recovered");
		}

		[Test]
		public void RecoveredWithin_NoEnemyIsRecovered()
		{
			Assert.That(CombatRetreatMath.RecoveredWithin(0, 0, 120), Is.True);
			Assert.That(CombatRetreatMath.RecoveredWithin(50, 0, 120), Is.True);
		}

		[Test]
		public void RecoveredWithin_WipedAxisWithEnemyNotRecovered()
		{
			Assert.That(CombatRetreatMath.RecoveredWithin(0, 10, 120), Is.False);
		}

		// ---------- Step: sustained window ----------

		[Test]
		public void Step_RetreatOnlyAfterSustainedLosing()
		{
			// window 2: one losing eval does NOT retreat, the second commits.
			var (d1, s1) = CombatRetreatMath.Step(RetreatDecision.Engaged, 0,
				ownStrength: 100, enemyStrength: 300, retreatRatioPct: 200, reengageRatioPct: 120,
				reachedSafety: false, sustainWindow: 2);
			Assert.That(d1, Is.EqualTo(RetreatDecision.Engaged), "first losing eval only accumulates the streak");
			Assert.That(s1, Is.EqualTo(1));

			var (d2, s2) = CombatRetreatMath.Step(d1, s1,
				100, 300, 200, 120, false, 2);
			Assert.That(d2, Is.EqualTo(RetreatDecision.Retreating), "second consecutive losing eval commits");
			Assert.That(s2, Is.EqualTo(0), "streak resets on entering the retreat");
		}

		[Test]
		public void Step_NonLosingEvalResetsStreak()
		{
			var (d1, s1) = CombatRetreatMath.Step(RetreatDecision.Engaged, 0,
				100, 300, 200, 120, false, 3);
			Assert.That(s1, Is.EqualTo(1));

			// A recovering eval mid-streak clears it, so a later single losing read can't ride the old count.
			var (d2, s2) = CombatRetreatMath.Step(d1, s1,
				100, 100, 200, 120, false, 3);
			Assert.That(d2, Is.EqualTo(RetreatDecision.Engaged));
			Assert.That(s2, Is.EqualTo(0));
		}

		[Test]
		public void Step_WindowOfOneRetreatsImmediately()
		{
			var (d, _) = CombatRetreatMath.Step(RetreatDecision.Engaged, 0,
				100, 300, 200, 120, false, 1);
			Assert.That(d, Is.EqualTo(RetreatDecision.Retreating));
		}

		// ---------- Step: hysteresis ----------

		[Test]
		public void Step_RetreatingHoldsInTheHysteresisBand()
		{
			// In the [120%, 200%] band (enemy 1.5x own): engaged wouldn't trigger, and retreating does NOT
			// re-engage — the anti-flip-flop guarantee. own=100, enemy=150.
			var (dEng, sEng) = CombatRetreatMath.Step(RetreatDecision.Engaged, 0,
				100, 150, 200, 120, false, 2);
			Assert.That(dEng, Is.EqualTo(RetreatDecision.Engaged), "1.5x doesn't trip the 2x retreat trigger");
			Assert.That(sEng, Is.EqualTo(0), "not losing beyond the trigger ⇒ no streak");

			var (dRet, _) = CombatRetreatMath.Step(RetreatDecision.Retreating, 0,
				100, 150, 200, 120, false, 2);
			Assert.That(dRet, Is.EqualTo(RetreatDecision.Retreating), "1.5x hasn't recovered past the 1.2x margin");
		}

		[Test]
		public void Step_RetreatingReengagesOnlyWhenRecovered()
		{
			// Enemy drops to parity (<= 1.2x) ⇒ recovered ⇒ back to Engaged.
			var (d, s) = CombatRetreatMath.Step(RetreatDecision.Retreating, 0,
				100, 100, 200, 120, reachedSafety: false, sustainWindow: 2);
			Assert.That(d, Is.EqualTo(RetreatDecision.Engaged));
			Assert.That(s, Is.EqualTo(0));
		}

		[Test]
		public void Step_ReachingSafetyEndsTheRetreat()
		{
			// Still badly outnumbered, but arrived at friendly control ⇒ retreat ends.
			var (d, _) = CombatRetreatMath.Step(RetreatDecision.Retreating, 0,
				100, 500, 200, 120, reachedSafety: true, sustainWindow: 2);
			Assert.That(d, Is.EqualTo(RetreatDecision.Engaged));
		}

		// ---------- ShouldRetreat: flag-off == legacy ----------

		[Test]
		public void ShouldRetreat_DisabledIsAlwaysLegacy()
		{
			Assert.That(CombatRetreatMath.ShouldRetreat(false, RetreatDecision.Retreating), Is.False,
				"gate off ⇒ never retreats even in the Retreating state (assault path taken = legacy)");
			Assert.That(CombatRetreatMath.ShouldRetreat(false, RetreatDecision.Engaged), Is.False);
		}

		[Test]
		public void ShouldRetreat_EnabledFollowsState()
		{
			Assert.That(CombatRetreatMath.ShouldRetreat(true, RetreatDecision.Retreating), Is.True);
			Assert.That(CombatRetreatMath.ShouldRetreat(true, RetreatDecision.Engaged), Is.False);
		}
	}
}
