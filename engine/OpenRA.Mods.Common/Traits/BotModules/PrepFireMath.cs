#region Copyright & License Information
/*
 * WW3MOD fires doctrine Phases 2+3 (@experimental) — preparatory fires + suppression-coordinated advance
 * (pure math). Design: WORKSPACE/plans/260803_fires_cycle_design.md §3 Phase 2 / Phase 3 (gaps G2 + G3).
 *
 * PERCEIVED BEHAVIOUR: an assault stops arriving all at once. Today the screen (MainBattle tanks/infantry) and
 * the guns reach the objective together — the barrage and the manoeuvre never talk to each other, so a viewer
 * sees tanks walk into an un-softened position. With these two phases the screen HOLDS at a start line while
 * the already-standing-off artillery (Phase 1 of the cycle: FiresStandoff/EchelonPositioning, shipped) shells
 * the objective, and only then steps off:
 *   Phase 2 (PrepFireMath)          — release on a bounded PREP-WINDOW countdown.
 *   Phase 3 (AdvanceUnderCoverMath) — release EARLY once the objective is observably SUPPRESSED (the barrage
 *                                     has done its job), else at the same window expiry.
 *
 * FACT/DECISION SPLIT: both classes are pure and world-free. The consumer (PoiOffensiveBotModule.CommitAndOrder)
 * does the engine-side work — the axis centroid distance, the per-axis tick stamp, and the FOG-LEGAL suppression
 * tally — and passes plain integers in. Phase 3 must never read an enemy's suppression state omniscient-ly: the
 * tally is over enemies the player may legally see (Actor.CanBeViewedByPlayer), exactly the discipline the fires
 * EV gate's clump scan already uses. Keeping the predicate pure here is what makes it NUnit-pinnable with no
 * game run (influence-stack.md §Invariants, fires design §3 "design rules honoured by every phase").
 *
 * LOAD-BEARING SAFETY PROPERTY: the hold is a BOUNDED integer countdown and can never deadlock an axis. Both
 * predicates release unconditionally once prepTicksElapsed reaches prepMaxTicks, and a non-positive prepMaxTicks
 * disables the hold outright (ShouldHoldScreen false / ScreenMayAdvance true) — so a mis-set knob fails OPEN,
 * toward the frozen "assault immediately" behaviour, never toward a screen that sits still forever. The hold is
 * also refused inside the assault radius: a screen that has already closed on the objective is committed, and
 * pulling it back to prep would be worse than pressing.
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws, integer-only comparisons, no world reads.
 *
 * v3-portable: engine-free static math (NUnit-pinned in PrepFireMathTest); only the tick stamp / centroid
 * distance / suppression tally plumbing that feeds it is engine-specific.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	public static class PrepFireMath
	{
		/// <summary>Fires Phase 2 — should the assault SCREEN hold at its start line while the guns prep the
		/// objective? True only while the axis is still in its APPROACH (centroid strictly beyond the assault
		/// radius) AND the prep window has not elapsed. Three release edges, all fail-open:
		/// <list type="bullet">
		/// <item>a non-positive <paramref name="prepMaxTicks"/> disables prep entirely (the frozen path);</item>
		/// <item>an axis already INSIDE the assault radius never holds (it is committed — pulling it back to prep
		/// would cost more than pressing, and the existing cohesion gate has already massed it for the assault);</item>
		/// <item>the window elapsing releases the screen unconditionally, so the hold is a bounded countdown.</item>
		/// </list>
		/// Pure, integer-only, zero RNG.</summary>
		public static bool ShouldHoldScreen(int distToTargetCells, int assaultRadiusCells, int prepTicksElapsed, int prepMaxTicks)
		{
			if (prepMaxTicks <= 0)
				return false;

			if (distToTargetCells <= assaultRadiusCells)
				return false;

			return prepTicksElapsed < prepMaxTicks;
		}
	}

	public static class AdvanceUnderCoverMath
	{
		/// <summary>Fires Phase 3 — may the SCREEN step off now? This is the release predicate that upgrades
		/// Phase 2's pure timer with a suppression read: the screen advances as soon as the OBSERVED suppression
		/// on the objective reaches <paramref name="suppressThreshold"/> (the barrage has suppressed the
		/// defenders — go now, while it lasts), and otherwise waits out the prep window. The hard release at
		/// <paramref name="prepMaxTicks"/> is kept so an objective that never suppresses (empty, or defenders
		/// immune) can still be assaulted — the coordination never becomes a deadlock.
		///
		/// <paramref name="observedSuppression"/> is a FOG-LEGAL tally supplied by the caller (summed suppression
		/// condition stacks over enemies the player may legally see near the objective) — this class never reads
		/// the world, so no omniscient read can leak in through it. A threshold of 0 means "any state counts",
		/// which advances immediately; that is intentional and matches the fail-open discipline above.
		/// Pure, integer-only, zero RNG.</summary>
		public static bool ScreenMayAdvance(int observedSuppression, int suppressThreshold, int prepTicksElapsed, int prepMaxTicks)
		{
			if (prepMaxTicks <= 0)
				return true;

			if (prepTicksElapsed >= prepMaxTicks)
				return true;

			return observedSuppression >= suppressThreshold;
		}
	}
}
