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
 * does the engine-side work — the axis centroid distance, the artillery's reach from its echelon anchor, the
 * per-axis tick stamp, and the FOG-LEGAL suppression gather — and passes plain integers in. Phase 3 must never
 * read an enemy's suppression state omniscient-ly: the gather is over enemies the player may legally see
 * (Actor.CanBeViewedByPlayer), exactly the discipline the fires EV gate's clump scan already uses. Keeping the
 * predicates pure here is what makes them NUnit-pinnable with no game run (influence-stack.md §Invariants).
 *
 * LOAD-BEARING SAFETY PROPERTY: the hold is a BOUNDED integer countdown and can never deadlock an axis. Both
 * predicates release unconditionally once prepTicksElapsed reaches prepMaxTicks, and a non-positive prepMaxTicks
 * disables the hold outright (ShouldHoldScreen false / ScreenMayAdvance true) — so a mis-set knob fails OPEN,
 * toward the frozen "assault immediately" behaviour, never toward a screen that sits still forever.
 *
 * THE HOLD IS BOUNDED IN DISTANCE AS WELL AS TIME (review FIX 6). Holding is only productive where the guns can
 * actually range the objective FROM THE ANCHOR THEY WILL OCCUPY. Under EchelonPositioning the guns sit BEHIND
 * the screen, so their reach past the screen collapses to roughly (screen engagement range - EchelonBuffer) —
 * a fresh axis 40-100 cells out is far outside it, and holding there would be a pure stall with no barrage
 * landing. ShouldHoldScreen therefore takes the reach as an explicit upper bound and refuses to hold beyond it;
 * a reach of 0 (no live gun) can never hold. The hold is likewise refused INSIDE the assault radius: a screen
 * that has already closed is committed, and pulling it back to prep would be worse than pressing.
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws, integer-only comparisons, no world reads.
 *
 * v3-portable: engine-free static math (NUnit-pinned in PrepFireMathTest); only the tick stamp / centroid
 * distance / reach derivation / suppression gather that feed it are engine-specific.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	public static class PrepFireMath
	{
		/// <summary>Fires Phase 2 — should the assault SCREEN hold at its start line while the guns prep the
		/// objective? True only while the axis is in the band where a hold is PRODUCTIVE — beyond the assault
		/// radius (still approaching) but no further out than the artillery's reach from the anchor it will
		/// occupy — AND the prep window has not elapsed. Four release edges, all fail-open:
		/// <list type="bullet">
		/// <item>a non-positive <paramref name="prepMaxTicks"/> disables prep entirely (the frozen path);</item>
		/// <item>an axis already INSIDE the assault radius never holds (it is committed — pulling it back to prep
		/// would cost more than pressing, and the existing cohesion gate has already massed it for the assault);</item>
		/// <item>an axis FURTHER OUT than <paramref name="firesReachCells"/> never holds — no shell would land on
		/// the objective during the hold, so it would be a stall, not a barrage. A reach of 0 (no live gun on the
		/// axis) therefore never holds either;</item>
		/// <item>the window elapsing releases the screen unconditionally, so the hold is a bounded countdown.</item>
		/// </list>
		/// Pure, integer-only, zero RNG.</summary>
		public static bool ShouldHoldScreen(int distToTargetCells, int assaultRadiusCells, int firesReachCells,
			int prepTicksElapsed, int prepMaxTicks)
		{
			if (prepMaxTicks <= 0)
				return false;

			if (distToTargetCells <= assaultRadiusCells)
				return false;

			if (distToTargetCells > firesReachCells)
				return false;

			return prepTicksElapsed < prepMaxTicks;
		}
	}

	public static class AdvanceUnderCoverMath
	{
		/// <summary><para>Fires Phase 3 — normalise a fog-legal suppression gather into the scalar
		/// <see cref="ScreenMayAdvance"/> tests. The gather is a SUM over the armed enemy defenders the player
		/// can legally see at the objective, so comparing it raw to a threshold would make the bar EASIER the
		/// more defenders there are — five lightly-rattled defenders would read as "suppressed" while one pinned
		/// defender would not, which is backwards (review FIX 7). Averaging over the observed defender count
		/// makes the threshold mean "the defenders are suppressed" independent of how many there are, on the
		/// same per-actor scale the suppression mechanic itself uses (GarrisonManager ducks a soldier at 30
		/// stacks and recalls it at 60).</para>
		///
		/// <para>No observed defenders ⇒ 0 ⇒ below any positive threshold ⇒ keep prepping. That is the honest
		/// fog-legal answer: an objective we cannot see into is not known to be soft, and the prep window still
		/// bounds the wait. Integer division (floor), deterministic, zero RNG.</para></summary>
		public static int NormalizeSuppression(int totalSuppression, int observedDefenders)
		{
			if (observedDefenders <= 0)
				return 0;

			return totalSuppression / observedDefenders;
		}

		/// <summary><para>Fires Phase 3 — may the SCREEN step off now? This is the release predicate that upgrades
		/// Phase 2's pure timer with a suppression read: the screen advances as soon as the OBSERVED suppression
		/// on the objective reaches <paramref name="suppressThreshold"/> (the barrage has suppressed the
		/// defenders — go now, while it lasts), and otherwise waits out the prep window. The hard release at
		/// <paramref name="prepMaxTicks"/> is kept so an objective that never suppresses (empty, fogged, or
		/// defenders immune) can still be assaulted — the coordination never becomes a deadlock.</para>
		///
		/// <para><paramref name="observedSuppression"/> is the per-defender average from
		/// <see cref="NormalizeSuppression"/>, supplied by the caller — this class never reads the world, so no
		/// omniscient read can leak in through it. IT IS RE-EVALUATED ON EVERY EVALUATION PASS WHILE THE HOLD IS
		/// ACTIVE (review FIX 5): evaluating it once at the start of the window would sample the objective
		/// before a single shell had landed, so its only reachable effect would be cancelling the prep of an
		/// objective that happened to be suppressed already — the inverse of the intent. A threshold of 0 means
		/// "any state counts", which advances immediately; that is intentional and matches the fail-open
		/// discipline above. Pure, integer-only, zero RNG.</para></summary>
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
