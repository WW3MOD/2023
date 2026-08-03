#region Copyright & License Information
/*
 * WW3MOD RetreatDamperMath tests — @experimental retreat-oscillation damper (Phase 3).
 *
 * Pure-logic pins for the anti-oscillation gates that layer ON TOP of CombatRetreatMath's retreat FSM:
 *   (a) post-retreat DWELL — after a retreat completes, an axis holds a bounded number of evals before it may
 *       re-advance on the same target (breaks the small-axis advance/lose/retreat ping-pong);
 *   (b) advance-STRENGTH floor — a sub-strength axis still massing in the rear waits instead of trickling forward.
 *
 * The load-bearing safety property is pinned by an END-TO-END sequence combining CombatRetreatMath.Step with the
 * damper: a SUSTAINED-losing axis still retreats within the bounded sustain delay, and the dwell only ever
 * delays the RE-advance AFTER a completed retreat — it never keeps the axis engaged while it is losing. A noisy
 * (oscillating) losing signal never trips a retreat, so it never arms a dwell either.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class RetreatDamperTest
	{
		// ---------- StepReadvanceHold ----------

		[Test]
		public void StepReadvanceHold_DisabledIsAlwaysZero()
		{
			Assert.That(RetreatDamperMath.StepReadvanceHold(5, RetreatDecision.Retreating, RetreatDecision.Engaged, dwellEvals: 0),
				Is.EqualTo(0), "dwellEvals <= 0 ⇒ damper inert (no hold)");
		}

		[Test]
		public void StepReadvanceHold_ZeroWhileRetreating()
		{
			// A retreat in progress is NEVER a dwell — this is what guarantees the damper can't delay a withdrawal.
			Assert.That(RetreatDamperMath.StepReadvanceHold(3, RetreatDecision.Engaged, RetreatDecision.Retreating, dwellEvals: 4),
				Is.EqualTo(0));
			Assert.That(RetreatDamperMath.StepReadvanceHold(3, RetreatDecision.Retreating, RetreatDecision.Retreating, dwellEvals: 4),
				Is.EqualTo(0));
		}

		[Test]
		public void StepReadvanceHold_ArmsOnRetreatEnd()
		{
			// Retreating -> Engaged (retreat completed at safety/recovery) arms the full dwell.
			Assert.That(RetreatDamperMath.StepReadvanceHold(0, RetreatDecision.Retreating, RetreatDecision.Engaged, dwellEvals: 4),
				Is.EqualTo(4), "a completed retreat begins the re-advance dwell");
		}

		[Test]
		public void StepReadvanceHold_CountsDownWhileEngaged()
		{
			Assert.Multiple(() =>
			{
				Assert.That(RetreatDamperMath.StepReadvanceHold(4, RetreatDecision.Engaged, RetreatDecision.Engaged, 4), Is.EqualTo(3));
				Assert.That(RetreatDamperMath.StepReadvanceHold(1, RetreatDecision.Engaged, RetreatDecision.Engaged, 4), Is.EqualTo(0));
				Assert.That(RetreatDamperMath.StepReadvanceHold(0, RetreatDecision.Engaged, RetreatDecision.Engaged, 4), Is.EqualTo(0),
					"the dwell floors at zero");
			});
		}

		// ---------- BelowAdvanceStrength ----------

		[Test]
		public void BelowAdvanceStrength_FloorBoundary()
		{
			Assert.Multiple(() =>
			{
				Assert.That(RetreatDamperMath.BelowAdvanceStrength(199, 200), Is.True, "below the floor ⇒ hold");
				Assert.That(RetreatDamperMath.BelowAdvanceStrength(200, 200), Is.False, "at the floor ⇒ advance");
				Assert.That(RetreatDamperMath.BelowAdvanceStrength(500, 200), Is.False, "above the floor ⇒ advance");
				Assert.That(RetreatDamperMath.BelowAdvanceStrength(0, 0), Is.False, "floor <= 0 ⇒ inert (never holds)");
			});
		}

		// ---------- End-to-end: damper composed with the retreat FSM ----------

		[Test]
		public void Sequence_SustainedLosingRetreatsThenDwellsBeforeReadvancing()
		{
			// Drive the real retreat FSM + the damper through: sustained losing (retreat within the window) ->
			// reach safety (retreat ends, dwell arms) -> a run of Engaged evals (dwell counts down). "Held" =
			// hold > 0 (the consumer would hold at the muster point). Proves: bounded retreat delay, then a
			// bounded RE-advance dwell — never a delayed withdrawal.
			const int Sustain = 2;
			const int Dwell = 3;
			var state = RetreatDecision.Engaged;
			var streak = 0;
			var hold = 0;

			// Two sustained losing evals (enemy 3x own) ⇒ retreat commits on the 2nd (bounded delay == Sustain).
			for (var i = 0; i < Sustain; i++)
			{
				var prev = state;
				(state, streak) = CombatRetreatMath.Step(state, streak, 100, 300, 200, 120, reachedSafety: false, Sustain);
				hold = RetreatDamperMath.StepReadvanceHold(hold, prev, state, Dwell);
			}

			Assert.That(state, Is.EqualTo(RetreatDecision.Retreating), "sustained losing retreats within the window");
			Assert.That(hold, Is.EqualTo(0), "no dwell is armed while retreating");

			// Reach safety: retreat ends, dwell arms.
			{
				var prev = state;
				(state, streak) = CombatRetreatMath.Step(state, streak, 100, 300, 200, 120, reachedSafety: true, Sustain);
				hold = RetreatDamperMath.StepReadvanceHold(hold, prev, state, Dwell);
			}

			Assert.That(state, Is.EqualTo(RetreatDecision.Engaged), "reaching safety ends the retreat");
			Assert.That(hold, Is.EqualTo(Dwell), "the re-advance dwell arms the moment the retreat completes");

			// Enemy is gone now (own uncontested). The axis would love to re-advance, but the dwell holds it for
			// exactly Dwell evals.
			var heldEvals = 0;
			for (var i = 0; i < Dwell + 2; i++)
			{
				var prev = state;
				(state, streak) = CombatRetreatMath.Step(state, streak, 100, 0, 200, 120, reachedSafety: false, Sustain);
				hold = RetreatDamperMath.StepReadvanceHold(hold, prev, state, Dwell);
				if (hold > 0)
					heldEvals++;
			}

			Assert.That(heldEvals, Is.EqualTo(Dwell - 1),
				"after the arming eval the axis holds for the remaining dwell evals, then re-advances");
			Assert.That(hold, Is.EqualTo(0), "the dwell has fully elapsed ⇒ re-advance allowed");
		}

		[Test]
		public void Sequence_OscillatingLosingSignalNeverRetreatsSoNeverDwells()
		{
			// A noisy losing signal (lose, recover, lose, recover ...) with a sustain window of 2 never commits a
			// retreat — the streak resets each recovering eval. Because no retreat ever completes, the dwell is
			// never armed: the damper adds nothing on noise (it only acts on a genuine completed retreat).
			const int Sustain = 2;
			const int Dwell = 3;
			var state = RetreatDecision.Engaged;
			var streak = 0;
			var hold = 0;

			var enemies = new[] { 300, 100, 300, 100, 300, 100 }; // alternating losing / parity
			foreach (var enemy in enemies)
			{
				var prev = state;
				(state, streak) = CombatRetreatMath.Step(state, streak, 100, enemy, 200, 120, reachedSafety: false, Sustain);
				hold = RetreatDamperMath.StepReadvanceHold(hold, prev, state, Dwell);
				Assert.That(state, Is.EqualTo(RetreatDecision.Engaged), "a single-eval losing flicker never retreats");
				Assert.That(hold, Is.EqualTo(0), "no completed retreat ⇒ no dwell armed on noise");
			}
		}
	}
}
