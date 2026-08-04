#region Copyright & License Information
/*
 * WW3MOD combat-quality force-preservation (@experimental) — retreat-oscillation damper (pure math).
 *
 * PERCEIVED BEHAVIOUR: small early-spread axes (EarlyGameSpread makes 2-3-unit packets) stop ping-ponging into
 * the Supply-Route bubble. Without the damper a tiny axis advances, reads "losing" the instant it meets the
 * enemy, falls back into the RetreatSafeDistanceCells bubble, flips back to Engaged, advances, loses, retreats
 * again — a standing oscillation that parks and shuffles units in a ~10-cell bubble around the SR (the user's
 * "lots of units pooling near spawn making small movements" symptom).
 *
 * This class layers on TOP of CombatRetreatMath's FSM (which already gates ENTRY into a retreat behind a
 * sustained losing streak + an asymmetric re-engage band). It adds the two anti-oscillation pieces that streak
 * did not cover, and deliberately does NOT change CombatRetreatMath.Step (so the base retreat lever stays
 * byte-identical when the damper flag is off):
 *   (a) POST-RETREAT DWELL — after a retreat COMPLETES (FSM Retreating -> Engaged at safety/recovery), the axis
 *       must hold for a minimum number of evals before it may RE-ADVANCE on the same target. Converts the tight
 *       advance/lose/retreat churn into hold-then-push-as-a-group.
 *   (b) ADVANCE-STRENGTH FLOOR — an axis still massing in the rear whose own force is below a floor waits/merges
 *       rather than trickling 2-3 units forward into the enemy where they trip the retreat FSM.
 *
 * WAVE B RESHAPE — the floor is FILL-COMPLETION, not an absolute bar. As first shipped, (b) compared own strength
 * against a constant on the health-weighted build-value scale, and held whenever it came up short. On a small army
 * the allocator never funds an axis past 2-3 hulls, so that comparison could never be satisfied and "wait for mass"
 * became "never advance" — the axis parked in the rear bubble for the rest of the match, which is the units-pooling
 * symptom this pass removes. (b) now holds only while the force the allocator ALLOCATED to the axis has not all
 * arrived yet (FillIncomplete), and a max-evals cap (HoldBudgetExhausted) bounds even that. Both new conditions
 * fail OPEN — toward advancing — because parking is the failure being cured, not a safe default.
 *
 * The cap is spent ONCE PER MASSING EPISODE, which is what makes it a terminating condition rather than a
 * duty-cycle limiter. StepFillHold therefore PERSISTS the counter across the release eval while the episode is
 * still alive (near the rally and still under-filled) and clears it only when the episode genuinely ends. A
 * counter that reset on release refunded the budget the instant it was spent, so the axis re-parked next eval and
 * ping-ponged hold x N / advance x 1 — issuing a fall-back order on every hold. See StepFillHold's remarks.
 *
 * LOAD-BEARING SAFETY PROPERTY (stated so it can't silently regress): the damper only ever DELAYS RE-ADVANCE and
 * FILTERS massing — it NEVER delays or blocks a genuine RETREAT. A truly-losing axis still withdraws promptly
 * (the retreat decision is taken upstream by CombatRetreatMath.Step, unaffected here). The dwell counter is
 * ZERO whenever the axis is Retreating, and the strength floor is applied by the consumer only to an axis that
 * is NOT retreating and is still near the rally. So the damper cannot turn a retreat into a last stand.
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws, pure integer comparisons. Two clients over the
 * same synced state decide identically.
 *
 * v3-portable: engine-free static math (NUnit-pinned in RetreatDamperTest); only the field-reading plumbing that
 * feeds it the FSM transition + own strength is engine-specific.
 */
#endregion

using System;

namespace OpenRA.Mods.Common.Traits
{
	public static class RetreatDamperMath
	{
		/// <summary>Advance the post-retreat dwell counter given this eval's FSM transition (<paramref name="prev"/>
		/// -&gt; <paramref name="now"/>). Returns the new hold value:
		///   * <paramref name="dwellEvals"/> &lt;= 0 ⇒ 0 (damper inert).
		///   * <paramref name="now"/> == Retreating ⇒ 0 (a retreat is in progress — NEVER a dwell; the safety
		///     property above depends on this).
		///   * Retreating -&gt; Engaged (a retreat just completed) ⇒ arm the dwell to <paramref name="dwellEvals"/>.
		///   * Engaged -&gt; Engaged ⇒ count the dwell down by one (floored at 0).
		/// While the returned hold is &gt; 0 the consumer holds the axis at the rally instead of re-advancing.
		/// Pure integer, zero RNG.</summary>
		public static int StepReadvanceHold(int hold, RetreatDecision prev, RetreatDecision now, int dwellEvals)
		{
			if (dwellEvals <= 0)
				return 0;

			if (now == RetreatDecision.Retreating)
				return 0;

			if (prev == RetreatDecision.Retreating)
				return dwellEvals; // retreat just ended — begin the re-advance dwell.

			return hold > 0 ? hold - 1 : 0;
		}

		/// <summary>Is the axis too WEAK to advance alone — own force below <paramref name="floor"/>? Used by the
		/// consumer to hold a sub-floor axis that is still massing in the rear (so it merges with reinforcements
		/// instead of trickling forward). <paramref name="floor"/> &lt;= 0 ⇒ false (inert). This is a FRESH-ADVANCE
		/// gate only — it must never be applied to an axis already in a retreat (that decision is upstream), which
		/// is why the safety property holds. Pure integer, zero RNG.</summary>
		public static bool BelowAdvanceStrength(int ownStrength, int floor)
			=> floor > 0 && ownStrength < floor;

		/// <summary>Is the axis still RECEIVING the force the allocator promised it — i.e. does it hold fewer units
		/// than its allocated size? This is the FILL-COMPLETION half of the strength gate, and it is what stops the
		/// floor parking an axis permanently.
		///
		/// <para>The defect it fixes: <see cref="BelowAdvanceStrength"/> alone is an ABSOLUTE bar on the
		/// health-weighted build-value scale. An axis the allocator only ever funds to 2-3 hulls can sit below a
		/// 1200-value floor for the entire match, so "wait for mass" became "never advance" and the units parked in
		/// the rear bubble forever — the pooling symptom. Reading the floor as "wait until your ALLOCATED force has
		/// actually arrived" instead makes the wait terminate by construction: unit conservation in
		/// AllocateProportional means an axis reaches its allocation as soon as the recruit pass runs, so the hold
		/// lasts only while units are genuinely still walking up (or while a reinforcement skip is withholding
		/// them).</para>
		///
		/// <paramref name="allocatedUnits"/> &lt;= 0 (no allocation known) ⇒ false: an axis with nothing promised is
		/// not "filling", so it is never held on this gate. Pure integer, zero RNG.</summary>
		public static bool FillIncomplete(int currentUnits, int allocatedUnits)
			=> allocatedUnits > 0 && currentUnits < allocatedUnits;

		/// <summary>Has this axis used up its massing budget — held for <paramref name="maxHoldEvals"/> evals or
		/// more? The BACKSTOP behind <see cref="FillIncomplete"/>: even an axis that never completes its fill (the
		/// pool ran dry, a reinforcement skip is starving it) must eventually advance or the hold is indefinite
		/// again. <paramref name="maxHoldEvals"/> &lt;= 0 ⇒ false (uncapped — the pre-cap reading). Pure.</summary>
		public static bool HoldBudgetExhausted(int heldEvals, int maxHoldEvals)
			=> maxHoldEvals > 0 && heldEvals >= maxHoldEvals;

		/// <summary>Advance the hold counter that <see cref="HoldBudgetExhausted"/> reads. Counts up while the
		/// consumer is holding; on a non-hold eval it PERSISTS while <paramref name="stillMassing"/> and clears only
		/// once the massing episode has genuinely ended. Kept as a separate step (rather than folded into
		/// <see cref="ShouldHold"/>) so the predicate stays side-effect-free and the counter is owned by the caller.
		///
		/// <para>The persistence is what makes the cap a TERMINATING condition rather than a duty-cycle limiter. A
		/// plain <c>holding ? held + 1 : 0</c> refunds the whole budget on the very eval it is spent, so a
		/// still-near-rally, still-under-filled axis re-parks on the next eval (<see cref="HoldBudgetExhausted"/>
		/// reads 0 again) — hold x N, release, hold x N, with every hold re-issuing a fall-back order. That is the
		/// "small movements near spawn" symptom, and it self-terminated only when something else happened to carry
		/// the axis out of the rally bubble (never, for an axis whose objective sits inside the bubble or that is
		/// pinned in contact near its own SR). Spending the budget ONCE per episode means that after exhaustion the
		/// axis advances EVERY eval for as long as the episode lasts (a sticky release), and a fresh budget is
		/// granted only when the episode really ends.</para>
		///
		/// <paramref name="stillMassing"/> is the episode-alive test — near the rally AND
		/// <see cref="FillIncomplete"/>. The other two episode ends (the axis entered a retreat, or lost its units)
		/// are reset by the consumer's own FSM update, not here. Pure.</summary>
		public static int StepFillHold(int heldEvals, bool holding, bool stillMassing)
			=> holding ? heldEvals + 1 : (stillMassing ? heldEvals : 0);

		/// <summary>Should the damper HOLD this axis at the muster point instead of letting it re-advance? Folds
		/// the two hold gates behind a DEFENSIVE safety guard so the "never delays a genuine withdrawal" property
		/// is STRUCTURAL, not dependent on the caller running its retreat gate first:
		///   * <paramref name="current"/> == Retreating ⇒ ALWAYS false. A retreating axis is owned by the retreat
		///     path; the damper must never hold it (this is the load-bearing safety guard NIT-3 hardens).
		///   * else (a) post-retreat dwell: <paramref name="readvanceHold"/> &gt; 0 ⇒ hold.
		///   * else (b) advance-strength floor, in FILL-COMPLETION form: hold only while ALL of — still massing near
		///     the rally (<paramref name="nearRally"/>), <see cref="BelowAdvanceStrength"/> the
		///     <paramref name="advanceFloor"/>, <see cref="FillIncomplete"/> (the allocated force has not all
		///     arrived yet), and the massing budget is not <see cref="HoldBudgetExhausted"/>.
		///
		/// <para>Gates (b3) and (b4) are the anti-park reshape. The floor used to be an ABSOLUTE strength bar, so a
		/// small axis the allocator never funds past 2-3 hulls held in the rear bubble for the whole match. Now the
		/// floor only buys time for a force that is genuinely still assembling, and the eval cap bounds even that.
		/// Both fail OPEN (toward advancing) — an unknown allocation or an exhausted budget releases the axis —
		/// because parking is the failure mode being removed.</para>
		/// Pure, zero RNG.</summary>
		public static bool ShouldHold(RetreatDecision current, int readvanceHold, bool nearRally,
			int ownStrength, int advanceFloor, int currentUnits, int allocatedUnits,
			int fillHoldEvals, int maxFillHoldEvals)
		{
			if (current == RetreatDecision.Retreating)
				return false;

			if (readvanceHold > 0)
				return true;

			if (!nearRally || !BelowAdvanceStrength(ownStrength, advanceFloor))
				return false;

			// Waiting only makes sense while reinforcements are still owed to this axis, and only for so long.
			return FillIncomplete(currentUnits, allocatedUnits)
				&& !HoldBudgetExhausted(fillHoldEvals, maxFillHoldEvals);
		}
	}
}
