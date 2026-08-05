#region Copyright & License Information
/*
 * WW3MOD influence stack — opportunistic advance (@experimental) — exploit-a-free-path math (pure).
 *
 * PERCEIVED BEHAVIOUR (PIPELINE item 31, design §2.6): the bot stops sitting on its captures. When a sector
 * ahead of its line reads UNDEFENDED and a free corridor leads to it, the idle reserve walks INTO that sector
 * instead of holding at the muster standoff — and keeps extending forward while the ground ahead stays clear.
 * The moment a believed contact or a danger envelope appears in front, the walk stops short and the reserve
 * falls back to the muster line. That fall-back is not a separate abort path: the anchor is re-derived from
 * scratch every evaluation, so "abort" is simply the same walk yielding fewer steps.
 *
 * WHAT "UNDEFENDED WITH A FREE PATH" MEANS HERE — the four §2.6 conditions, all fog-legal reads of fields the
 * influence stack already builds, and all evaluated PER STEP so the corridor is verified along its whole length
 * rather than only at its far end (SectorIsClear):
 *   (1) not BELIEVED enemy ground   — ControlField.OwnerAt(cell) != Enemy. A belief read, not "is empty".
 *   (2) no believed contact there   — no BeliefStore contact inside the coarse sector.
 *   (3) low believed danger         — DangerFieldLayer.GroundDanger <= the (knob-shifted) ceiling.
 *   (4) reachable                   — the mover's locomotor can stand on the cell.
 *
 * WHY A GRADIENT WALK RATHER THAN A SECTOR SEARCH. AdvanceCell descends the SAME distance-to-enemy-frontier
 * BFS that ForwardStagingMath.StagingCell descends, from the staging anchor the reserve already musters at —
 * it invents no new field and needs no target. Two properties fall out of that reuse and are load-bearing:
 *   - Each accepted step is ONE coarse cell from the last and is itself clear-tested, so the accepted path IS
 *     the corridor. There is no "score the far sector, then hope the lane is open" gap.
 *   - The BFS seeds from cells the control field classifies Enemy, so a frontier distance of 0 IS believed
 *     enemy ground. Condition (1) rejects those cells, so the WALK provably halts on OUR side of the frontline
 *     contour — the "forward frontier cell" §2.6 names as the objective. Condition (3) is what stops it inside
 *     no-man's-land that a defender covers by fire.
 *
 * SCOPE OF THAT HALTING GUARANTEE — it covers the ANCHOR, not the units, and the difference is a whole sector.
 * The walk's output is one cell; the consumer then FANS the screen around it (ForwardStagingMath.SpreadCell).
 * At the shipped spacing one spread ring is a full coarse sector, and a depth-maximising walk routinely ends at
 * frontier distance 1 — so several octants of ring 1 land at distance 0, i.e. inside the very ground the walk
 * refused. StableSlot's own contract assumes the caller bounds the ring radius by a standoff, and the advance
 * HAS no standoff. The fan-out is therefore grant-tested slot by slot in the consumer (a failing slot falls back
 * to the anchor cell), which is what extends the guarantee from the anchor to every ordered destination. Do not
 * re-derive the ring bound from the staging path's arithmetic: that bound is the standoff, which does not exist
 * here.
 *
 * WHAT THE WALK DOES NOT DO — cross-eval chaining. Each evaluation re-seeds from the STAGING anchor, so the
 * advance is a bounded (<= maxSectors) offset ahead of the muster point, re-derived from scratch; it deepens
 * across evals only as the staging anchor itself creeps forward. §2.6's "extends while clear" is therefore
 * realised WITHIN one walk, not as a ratchet that accumulates ground over time. This bounds what a sweep can
 * show: raising the depth dial widens the standing offset, it does not let the screen run away.
 *
 * AGGRESSIVENESS (§2.7) enters as the three shifts §2.6 asks for, each through the single PoiOffenseMath
 * .ShiftByKnob seam with its own base/slope pair: how marginal a danger reading still counts as clear
 * (DangerCeiling), how many sectors a chaining advance will take (MaxSectors), and how much of the reserve it
 * is willing to spend (ForceCap, via AdvanceGroupSize). At a low setting that composes into "a small screen,
 * into totally clear ground only"; at a high setting, "a larger force, through thinly-covered ground".
 *
 * TERMINATION / BOUNDEDNESS: an accepted step STRICTLY decreases frontier distance, and the step count is
 * capped at maxSectors, so the walk always terminates and is never a free search. Every entry point returns
 * its input unchanged (or 0) when its gate fails, so the whole class collapses to "no advance" — which is what
 * makes the default-off consumer byte-identical.
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws, integer only, fixed 8-neighbour scan order with
 * iteration-order tie-breaks, and replacement only on a STRICT improvement — so two clients over the same
 * synced belief state advance to the same cell.
 *
 * v3-portable: engine-free static math (NUnit-pinned in OpportunisticAdvanceMathTest); only the plumbing that
 * binds the samplers (PoiOffensiveBotModule.ResolveAdvanceAnchor / StageFreePool) is engine-specific. It ports
 * to the SquadBrain's Advance mission (design §3.1) unchanged — the Brain supplies different samplers, not
 * different math.
 */
#endregion

using System;

namespace OpenRA.Mods.Common.Traits
{
	public static class OpportunisticAdvanceMath
	{
		// Fixed 8-neighbour scan order (cardinals first, then diagonals) — deliberately the same order
		// ForwardStagingMath uses, so the advance extends the muster walk along the same tie-break.
		static readonly (int Dx, int Dy)[] Neighbours =
		{
			(0, -1), (1, 0), (0, 1), (-1, 0), (1, -1), (1, 1), (-1, 1), (-1, -1)
		};

		/// <summary>The believed-danger reading at/below which a sector still counts as clear enough to advance
		/// into, shifted by the Aggressiveness knob (§2.7 base ± slope). Higher aggressiveness RAISES the bar it
		/// will accept — a bold commander pushes through ground a cautious one calls covered — so the slope is
		/// POSITIVE for that direction. Floored at 0, which reads "only totally clear ground qualifies" rather
		/// than inverting into a negative ceiling no cell could ever satisfy. Scale is the danger field's
		/// throughput scale (see influence-stack.md Stage B), so it is comparable to StagingDangerSafeThreshold
		/// and BelievedDangerMildThreshold, NOT to InfluenceMap threat.</summary>
		public static int DangerCeiling(int baseCeiling, int aggressiveness, int slopePct)
		{
			return Math.Max(0, PoiOffenseMath.ShiftByKnob(baseCeiling, aggressiveness, slopePct));
		}

		/// <summary>How many coarse sectors forward a chaining advance may take this evaluation, shifted by the
		/// Aggressiveness knob. Higher aggressiveness commits DEEPER (positive slope). Floored at 0, which the
		/// walk reads as "no advance" — so the cautious extreme is well-defined rather than negative.</summary>
		public static int MaxSectors(int baseSectors, int aggressiveness, int slopePct)
		{
			return Math.Max(0, PoiOffenseMath.ShiftByKnob(baseSectors, aggressiveness, slopePct));
		}

		/// <summary>The most idle units the advance may spend, shifted by the Aggressiveness knob. Higher
		/// aggressiveness sends MORE (positive slope) — §2.6's "a small screen at low, a larger force at high".
		/// Floored at 0; <see cref="AdvanceGroupSize"/> turns a cap below the minimum into no advance at all
		/// rather than an under-strength probe.</summary>
		public static int ForceCap(int baseCap, int aggressiveness, int slopePct)
		{
			return Math.Max(0, PoiOffenseMath.ShiftByKnob(baseCap, aggressiveness, slopePct));
		}

		/// <summary>The §2.6 grant test for ONE sector, as an explicit conjunction (house style — cf.
		/// SupplyTruckHuntMath.ShouldHunt, CommitOnOrderMath.ShouldCommitShared): the ground must not be
		/// BELIEVED enemy-held, must carry no believed contact, must read at/under the danger ceiling, and must
		/// be terrain the mover can actually occupy.
		///
		/// The passability term is not decoration. Unstamped impassable ground (water, cliff) reads
		/// GroundDanger 0 — maximally "safe" — so a danger-only test would actively PREFER a lake, and the
		/// resulting Move would no-op while the reserve stood still believing it was advancing. This is the same
		/// trap GroundDangerNav guards its detour waypoint against; here every accepted cell is a waypoint, so
		/// every accepted cell is guarded.
		///
		/// Note what is deliberately NOT required: that the sector be OURS. Contested/neutral no-man's-land is
		/// exactly the ground §2.6 wants taken by walking into it, so only the Enemy classification is
		/// disqualifying.</summary>
		public static bool SectorIsClear(bool believedEnemyOwned, bool contactPresent, int danger, int dangerCeiling, bool passable)
		{
			return !believedEnemyOwned
				&& !contactPresent
				&& danger <= dangerCeiling
				&& passable;
		}

		/// <summary>How many idle units to peel off for the advance, or 0 for none. Total by construction: a
		/// pool that cannot field the minimum screen, a non-positive minimum, or a knob-shifted cap that has
		/// fallen BELOW that minimum all read 0 — the cautious extreme declines to advance rather than sending a
		/// token force into no-man's-land. Otherwise the group is the cap, bounded by what is actually idle.</summary>
		public static int AdvanceGroupSize(int idleCount, int minUnits, int cap)
		{
			if (minUnits < 1 || cap < minUnits || idleCount < minUnits)
				return 0;

			return Math.Min(idleCount, cap);
		}

		/// <summary>The master gate, conjunctive and explicit so the whole enable path is one readable line at
		/// the call site. <paramref name="fieldsAvailable"/> is the caller's "I have ALL THREE reads this
		/// decision needs" — control field, danger field AND belief store. None may be waived to a default:
		/// a missing danger field would read 0 danger everywhere and turn the cautious ceiling into a blank
		/// cheque (condition 3), and a missing belief store would read no contacts anywhere and silently waive
		/// condition (2) — the one condition that has no proxy, since a lone cheap contact may neither flip
		/// control nor stamp danger above the ceiling. Both failure modes are permissive, which is why they are
		/// gated here rather than handled with a null-coalescing default at the sampler.</summary>
		public static bool ShouldAdvance(bool advanceEnabled, bool fieldsAvailable, int maxSectors, int groupSize)
		{
			return advanceEnabled
				&& fieldsAvailable
				&& maxSectors > 0
				&& groupSize > 0;
		}

		/// <summary>Whether to adopt the freshly-walked <paramref name="candidateDepth"/> cell over the anchor
		/// already being held, given plain Chebyshev hysteresis in
		/// <paramref name="shiftedPastHysteresis"/>. Depth is FRONTIER DISTANCE, so a LARGER value is SHALLOWER.
		///
		/// Plain hysteresis alone is wrong here, and the reason is the asymmetry with staging. A stale STAGING
		/// anchor is still safe ground — it was chosen a standoff behind the line, so holding it one eval too
		/// long costs nothing and the jitter damping is pure win. A stale ADVANCE anchor is the opposite: it was
		/// chosen precisely because it passed a grant test, so the moment it stops passing, holding it means
		/// ordering the screen at ground we have just decided is not free. Worse, the candidates are grid-cell
		/// CENTRES spaced one coarse cell apart, so a one-sector shortening moves the candidate by exactly
		/// CellSize map cells — below a hysteresis of 3 — and the §2.6 abort would be suppressed exactly when it
		/// is needed most.
		///
		/// So the guard is an enter/exit asymmetry, the same shape as the Stage-E/F strict-improvement rule:
		/// hysteresis may only ever damp a DEEPENING or lateral move. Either of the two retreat cases adopts
		/// immediately — the held anchor no longer passing the grant test, or the fresh walk not reaching as far
		/// as the held anchor. Giving ground is never damped.
		///
		/// THE SHIPPED CONFIG PASSES <paramref name="shiftedPastHysteresis"/> = true ALWAYS, so in practice this
		/// reduces to "adopt the fresh walk". That is deliberate. Making the asymmetry one-way removed the danger
		/// from damping, but it also exposed that damping had nothing safe left to do: the anchor moves only in
		/// whole sectors, so any scalar threshold either admits every move or blocks a one-sector re-deepening —
		/// and a blocked re-deepening never gets a later pass to win, because the identical one-sector gap recurs
		/// each eval. The advance would ratchet to the shallowest depth it had ever reached. See
		/// PoiOffensiveBotModule.AdvanceHysteresisCells for the full argument and for why grid-space units do not
		/// rescue it. The parameter is kept — the asymmetry is the load-bearing part and must survive anyone who
		/// sets a non-zero threshold — but the shipped answer is 0.</summary>
		public static bool AdoptAdvanceAnchor(bool heldStillGranted, int heldDepth, int candidateDepth, bool shiftedPastHysteresis)
		{
			return !heldStillGranted
				|| candidateDepth > heldDepth
				|| shiftedPastHysteresis;
		}

		/// <summary>Walk forward from the muster seed (<paramref name="startX"/>,<paramref name="startY"/>) down
		/// the distance-to-enemy-frontier gradient, taking only steps into sectors that pass
		/// <see cref="SectorIsClear"/>, and return the deepest cell reached. Each step moves to the 8-neighbour
		/// with the STRICTLY smallest frontier distance among the CLEAR neighbours, so a covered lane is not
		/// merely deprioritised — it is not a candidate at all.
		///
		/// Returns the seed unchanged whenever nothing qualifies: <paramref name="maxSectors"/> non-positive, an
		/// unpopulated/flat field (every cell reads the same 'far' sentinel ⇒ no neighbour is strictly closer),
		/// or the ground ahead reading contested/dangerous. The caller treats "returned the seed" as "no
		/// advance this evaluation" and falls back to the plain muster anchor — which is also, unchanged, the
		/// §2.6 abort: a contact appearing in the corridor simply removes those steps from the next walk.
		///
		/// Terminates by two independent bounds: the step budget, and the strict decrease in frontier distance
		/// that every accepted step forces.</summary>
		public static (int X, int Y) AdvanceCell(
			int startX, int startY,
			int maxSectors, int dangerCeiling,
			Func<int, int, int> frontierAt, Func<int, int, int> dangerAt,
			Func<int, int, bool> believedEnemyOwnedAt, Func<int, int, bool> contactAt,
			Func<int, int, bool> passableAt, Func<int, int, bool> onGrid)
		{
			var cx = startX;
			var cy = startY;
			if (maxSectors <= 0)
				return (cx, cy);

			for (var step = 0; step < maxSectors; step++)
			{
				var bestF = frontierAt(cx, cy);
				var bx = cx;
				var by = cy;

				for (var d = 0; d < Neighbours.Length; d++)
				{
					var nx = cx + Neighbours[d].Dx;
					var ny = cy + Neighbours[d].Dy;
					if (!onGrid(nx, ny))
						continue;

					if (!SectorIsClear(believedEnemyOwnedAt(nx, ny), contactAt(nx, ny), dangerAt(nx, ny), dangerCeiling, passableAt(nx, ny)))
						continue;

					var nf = frontierAt(nx, ny);
					if (nf < bestF)
					{
						bestF = nf;
						bx = nx;
						by = ny;
					}
				}

				if (bx == cx && by == cy)
					break; // nothing clear ahead — hold at the deepest ground already granted.

				cx = bx;
				cy = by;
			}

			return (cx, cy);
		}
	}
}
