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

		// ---------- FillIncomplete / HoldBudgetExhausted / StepFillHold (Wave B reshape) ----------

		[Test]
		public void FillIncomplete_OnlyWhileForceIsStillOwed()
		{
			Assert.Multiple(() =>
			{
				Assert.That(RetreatDamperMath.FillIncomplete(2, 5), Is.True, "2 of an allocated 5 ⇒ still filling");
				Assert.That(RetreatDamperMath.FillIncomplete(5, 5), Is.False,
					"allocation satisfied ⇒ fill COMPLETE, waiting longer is futile");
				Assert.That(RetreatDamperMath.FillIncomplete(7, 5), Is.False, "over-strength ⇒ complete");
				Assert.That(RetreatDamperMath.FillIncomplete(0, 0), Is.False,
					"no allocation known ⇒ fails OPEN (never held) — parking is the failure mode being cured");
			});
		}

		[Test]
		public void HoldBudgetExhausted_Boundary()
		{
			Assert.Multiple(() =>
			{
				Assert.That(RetreatDamperMath.HoldBudgetExhausted(5, 6), Is.False, "inside the budget");
				Assert.That(RetreatDamperMath.HoldBudgetExhausted(6, 6), Is.True, "budget spent ⇒ release");
				Assert.That(RetreatDamperMath.HoldBudgetExhausted(99, 6), Is.True);
				Assert.That(RetreatDamperMath.HoldBudgetExhausted(9999, 0), Is.False,
					"cap <= 0 ⇒ uncapped (the pre-reshape reading)");
			});
		}

		[Test]
		public void StepFillHold_CountsUpWhileHoldingAndResetsOtherwise()
		{
			Assert.Multiple(() =>
			{
				Assert.That(RetreatDamperMath.StepFillHold(0, true), Is.EqualTo(1));
				Assert.That(RetreatDamperMath.StepFillHold(3, true), Is.EqualTo(4));
				Assert.That(RetreatDamperMath.StepFillHold(4, false), Is.EqualTo(0),
					"a released axis gets its full massing budget back");
			});
		}

		// ---------- ShouldHold (NIT-3: structural safety guard) ----------

		[Test]
		public void ShouldHold_NeverHoldsWhileRetreating()
		{
			// The defensive guard: even with a live dwell AND below the strength floor, a Retreating axis is NEVER
			// held by the damper — the retreat path owns it. This makes "never delays a withdrawal" structural,
			// independent of caller gate-ordering.
			Assert.That(RetreatDamperMath.ShouldHold(RetreatDecision.Retreating, readvanceHold: 5,
				nearRally: true, ownStrength: 1, advanceFloor: 1000,
				currentUnits: 1, allocatedUnits: 9, fillHoldEvals: 0, maxFillHoldEvals: 6), Is.False,
				"a retreating axis is never held, whatever the dwell/strength say");
		}

		[Test]
		public void ShouldHold_DwellHoldsWhenEngaged()
		{
			// The dwell is checked BEFORE the strength/fill gates, so a fill-complete axis still serves its dwell.
			Assert.That(RetreatDamperMath.ShouldHold(RetreatDecision.Engaged, readvanceHold: 2,
				nearRally: false, ownStrength: 9999, advanceFloor: 1000,
				currentUnits: 9, allocatedUnits: 9, fillHoldEvals: 99, maxFillHoldEvals: 6), Is.True,
				"(a) an in-dwell engaged axis holds regardless of strength/position/fill");
		}

		[Test]
		public void ShouldHold_StrengthFloorOnlyNearRally()
		{
			Assert.Multiple(() =>
			{
				Assert.That(RetreatDamperMath.ShouldHold(RetreatDecision.Engaged, 0,
					nearRally: true, ownStrength: 500, advanceFloor: 1000,
					currentUnits: 2, allocatedUnits: 5, fillHoldEvals: 0, maxFillHoldEvals: 6), Is.True,
					"(b) sub-floor, near the rally, force still arriving ⇒ hold (wait for mass)");
				Assert.That(RetreatDamperMath.ShouldHold(RetreatDecision.Engaged, 0,
					nearRally: false, ownStrength: 500, advanceFloor: 1000,
					currentUnits: 2, allocatedUnits: 5, fillHoldEvals: 0, maxFillHoldEvals: 6), Is.False,
					"sub-floor but already forward ⇒ not yanked back");
				Assert.That(RetreatDamperMath.ShouldHold(RetreatDecision.Engaged, 0,
					nearRally: true, ownStrength: 1500, advanceFloor: 1000,
					currentUnits: 2, allocatedUnits: 5, fillHoldEvals: 0, maxFillHoldEvals: 6), Is.False,
					"at/above the floor ⇒ advance");
				Assert.That(RetreatDamperMath.ShouldHold(RetreatDecision.Engaged, 0,
					nearRally: true, ownStrength: 1, advanceFloor: 0,
					currentUnits: 2, allocatedUnits: 5, fillHoldEvals: 0, maxFillHoldEvals: 6), Is.False,
					"floor <= 0 ⇒ inert (never holds on strength)");
			});
		}

		[Test]
		public void ShouldHold_FillCompleteAxisAdvancesDespiteSubFloorStrength()
		{
			// THE WAVE B DEFECT, pinned. A 3-hull axis worth 900 sits under a 1200 floor forever, because the
			// allocator will never fund it past 3 — as an absolute bar the gate parked it in the rear all match.
			// Fill-completion reads the same axis as "you already have everything you were promised" ⇒ advance.
			Assert.That(RetreatDamperMath.ShouldHold(RetreatDecision.Engaged, 0,
				nearRally: true, ownStrength: 900, advanceFloor: 1200,
				currentUnits: 3, allocatedUnits: 3, fillHoldEvals: 0, maxFillHoldEvals: 6), Is.False,
				"a permanently sub-floor axis at full allocation must ADVANCE, not park");
		}

		[Test]
		public void ShouldHold_CapReleasesAnAxisWhoseFillNeverCompletes()
		{
			// The backstop: an axis being starved (pool dry / NoReinforceLostFights) never completes its fill, so
			// fill-completion alone would hold it indefinitely. The eval cap bounds that hold.
			Assert.Multiple(() =>
			{
				Assert.That(RetreatDamperMath.ShouldHold(RetreatDecision.Engaged, 0,
					nearRally: true, ownStrength: 500, advanceFloor: 1000,
					currentUnits: 2, allocatedUnits: 9, fillHoldEvals: 5, maxFillHoldEvals: 6), Is.True,
					"still inside the massing budget ⇒ hold");
				Assert.That(RetreatDamperMath.ShouldHold(RetreatDecision.Engaged, 0,
					nearRally: true, ownStrength: 500, advanceFloor: 1000,
					currentUnits: 2, allocatedUnits: 9, fillHoldEvals: 6, maxFillHoldEvals: 6), Is.False,
					"budget spent ⇒ advance even though the allocated force never arrived");
			});
		}

		[Test]
		public void Sequence_StarvedAxisHoldsForABoundedNumberOfEvalsThenAdvances()
		{
			// End-to-end anti-park pin: an axis that is permanently sub-floor AND permanently under-filled (the pool
			// is dry / a reinforcement skip is starving it) must not hold forever. Drive the counter exactly the way
			// the consumer does. `nearRally` models the integration: while held the axis stays at the muster point,
			// and the eval it is released it advances off the rally — after which the strength gate cannot apply to
			// it again at all (that is what makes the release final rather than an oscillation).
			const int Cap = 6;
			var consecutiveHolds = 0;
			var fillHoldEvals = 0;
			var nearRally = true;
			var advanced = false;

			for (var i = 0; i < Cap + 10; i++)
			{
				var hold = RetreatDamperMath.ShouldHold(RetreatDecision.Engaged, 0,
					nearRally, ownStrength: 100, advanceFloor: 5000,
					currentUnits: 1, allocatedUnits: 9, fillHoldEvals: fillHoldEvals, maxFillHoldEvals: Cap);

				fillHoldEvals = RetreatDamperMath.StepFillHold(fillHoldEvals, hold);

				if (hold)
				{
					Assert.That(advanced, Is.False, "once released the axis must never be re-parked by this gate");
					consecutiveHolds++;
				}
				else
				{
					advanced = true;
					nearRally = false; // it left the rally bubble
				}
			}

			Assert.That(consecutiveHolds, Is.EqualTo(Cap),
				"the hold is bounded by the cap — never indefinite, whatever the strength/fill say");
			Assert.That(advanced, Is.True, "the axis does eventually advance");
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
