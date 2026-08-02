#region Copyright & License Information
/*
 * WW3MOD capture-supply guarantee (@experimental) — TECN floor + fan-out decisions (pure math).
 *
 * PERCEIVED BEHAVIOUR: the capture coordinator no longer fields zero capturers while a dozen neutral
 * income derricks sit uncaptured (the measured loss mechanism — S2 rung, WORKSPACE/recon/260731). Three
 * coupled decisions are turned into integers here, so the coordinator's plumbing (CaptureCoordinatorBotModule)
 * stays a thin consumer and the logic is NUnit-pinned without a game run:
 *
 *   1. EffectiveFloor  — how many capturers to keep alive-or-pending. A STATIC floor (today) or one SCALED
 *      to the number of reachable neutral money POIs (~1 per free oilb), clamped by a yaml cap.
 *   2. ShouldRequestTecn — the re-request predicate. Reproduces today's `alive + pending < floor` gate
 *      EXACTLY when the staleness knob is off, and additionally RE-ISSUES a request that the shared
 *      production FIFO has left undelivered past a bounded TICK age (the deadlock the recon flagged: a
 *      lone pending request suppressing all re-requests while the Infantry queue churns combat buys).
 *   3. (CaptureFanoutMath) SelectDistinctTargets — de-duplicate capture targets so N free capturers fan
 *      out to N DISTINCT neutral oilbs instead of clustering onto one already claimed by an in-flight
 *      capturer from a prior scan.
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws, pure integer comparisons / ordered walks
 * over caller-supplied values. No wall-clock — the staleness test is tick-based. Two clients over the same
 * synced state decide identically.
 *
 * BYTE-IDENTITY: every function has a caller-supplied switch/threshold whose default (scaleEnabled=false,
 * staleTicks<=0, an empty in-flight set) reproduces the frozen path, so the @stable twin — which sets none
 * of the new Info fields — is unchanged.
 *
 * v3-portable: engine-free static math (NUnit-pinned in CaptureSupplyMathTest / CaptureFanoutMathTest);
 * only the tasking plumbing that consumes it is engine-specific.
 */
#endregion

using System.Collections.Generic;

namespace OpenRA.Mods.Common.Traits
{
	public static class CaptureSupplyMath
	{
		/// <summary>How many capturers (TECN) to keep alive-or-pending this scan.
		///
		/// <paramref name="scaleEnabled"/> off ⇒ the STATIC <paramref name="staticFloor"/> verbatim (today's
		/// behaviour — byte-identical for any config that does not opt in). On ⇒ scale to opportunity: one
		/// capturer per reachable neutral money POI (<paramref name="neutralMoneyPoiCount"/>), never BELOW the
		/// static floor (so an opted-in build is at least as aggressive as today) and never above
		/// <paramref name="floorCap"/> (so the request pool can't balloon on a POI-dense map). Callers must set
		/// <paramref name="floorCap"/> >= <paramref name="staticFloor"/>; if mis-set below, the cap wins (the
		/// clamp is the outer bound), which only ever LOWERS demand — a safe direction. Pure integer, zero RNG.</summary>
		public static int EffectiveFloor(bool scaleEnabled, int staticFloor, int neutralMoneyPoiCount, int floorCap)
		{
			if (!scaleEnabled)
				return staticFloor;

			var scaled = neutralMoneyPoiCount;
			if (scaled < staticFloor)
				scaled = staticFloor; // never field fewer than today's floor
			if (scaled > floorCap)
				scaled = floorCap;    // cap opportunity so the request pool is bounded
			return scaled;
		}

		/// <summary>Combat-quality budget split (@experimental): clamp the capturer floor so the alive-or-pending
		/// TECN pool can never DEMAND more than <paramref name="sharePct"/>% of the current combat army — a single
		/// dial that shifts production budget between capture and combat. <paramref name="sharePct"/> &gt;= 100
		/// (the default) is INERT: the clamp never binds and the floor passes through verbatim (byte-identical, and
		/// the caller skips even counting the army). Below 100 it lowers the floor to
		/// floor(<paramref name="totalCombatArmy"/> * sharePct / 100) whenever that is smaller — so capture demand
		/// yields to combat while the army is thin, then relaxes as the army grows. Never RAISES the floor. Pure
		/// integer (widened to avoid overflow), zero RNG.</summary>
		public static int ClampFloorToArmyShare(int floor, int totalCombatArmy, int sharePct)
		{
			if (sharePct >= 100)
				return floor;

			var cap = (int)((long)totalCombatArmy * sharePct / 100);
			return floor < cap ? floor : cap;
		}

		/// <summary>Should the coordinator issue a floor production request this scan?
		///
		/// Reproduces today's gate EXACTLY when <paramref name="staleTicks"/> &lt;= 0 (the frozen path requests
		/// iff <c>alive + pending &lt; floor</c>):
		///   * <paramref name="alive"/> &gt;= <paramref name="floor"/> ⇒ false (enough capturers on the map).
		///   * <paramref name="alive"/> + <paramref name="pending"/> &lt; <paramref name="floor"/> ⇒ true
		///     (under floor even counting requests already in flight).
		///   * otherwise the floor is met ONLY by an in-flight <paramref name="pending"/> request. Frozen path
		///     returns false here (and can deadlock if that request never delivers). With
		///     <paramref name="staleTicks"/> &gt; 0 we RE-ISSUE once the outstanding request has gone
		///     undelivered for <paramref name="staleTicks"/> ticks (<paramref name="currentTick"/> −
		///     <paramref name="lastRequestTick"/> &gt;= staleTicks) — the un-deadlock. Tick-based, no wall-clock.
		///
		/// Pure integer, zero RNG.</summary>
		public static bool ShouldRequestTecn(int floor, int alive, int pending,
			int currentTick, int lastRequestTick, int staleTicks)
		{
			if (alive >= floor)
				return false;

			if (alive + pending < floor)
				return true;

			// Floor met only by a pending request. Frozen behaviour (staleTicks off) never re-issues.
			if (staleTicks <= 0)
				return false;

			// IN-FLIGHT CAP (@experimental): never re-issue once pending already meets the floor. The staleness
			// re-issue was a backstop for a request the shared FIFO silently DROPPED; supply-side peek-don't-pop
			// delivery now keeps a popped request until the queue accepts it, so an in-flight request is no longer
			// lost — re-issuing at/above the floor would only DUPLICATE it and let pending grow without bound (the
			// measured pending=82 smell). Capping at the floor bounds pending to [0, floor] while still allowing a
			// stale re-issue in the partial case (some alive, pending below floor). Inert on the frozen path
			// (reached only when staleTicks > 0), so @stable is byte-identical.
			if (pending >= floor)
				return false;

			return currentTick - lastRequestTick >= staleTicks;
		}
	}

	public static class CaptureFanoutMath
	{
		/// <summary>Pick up to <paramref name="capturerCount"/> DISTINCT capture targets from the ordered
		/// candidate list, skipping any target that an in-flight capturer has already claimed
		/// (<paramref name="inFlightTargetIds"/>). Preserves input order (the caller's deterministic
		/// value×distance ranking), so the top not-yet-claimed oilbs are taken first, one per capturer.
		///
		/// This is the fan-out invariant: with K free capturers and a ranked target list, the K capturers are
		/// assigned to the K best DISTINCT targets none of which is already being walked to — never two onto the
		/// same derrick. An empty <paramref name="inFlightTargetIds"/> reproduces "take the top-K distinct", the
		/// frozen assignment. Duplicates in the input (defensive — real target lists are already distinct actors)
		/// are collapsed. Pure, ordered, zero RNG; only membership of the sets is queried, never their
		/// enumeration order, so no hash-order leaks into a sim decision.</summary>
		public static List<uint> SelectDistinctTargets(IReadOnlyList<uint> orderedTargetIds,
			ISet<uint> inFlightTargetIds, int capturerCount)
		{
			var chosen = new List<uint>();
			if (capturerCount <= 0 || orderedTargetIds == null)
				return chosen;

			var seen = new HashSet<uint>();
			foreach (var id in orderedTargetIds)
			{
				if (chosen.Count >= capturerCount)
					break;
				if (inFlightTargetIds != null && inFlightTargetIds.Contains(id))
					continue;
				if (!seen.Add(id))
					continue; // already chosen this scan — one capturer per distinct target
				chosen.Add(id);
			}

			return chosen;
		}
	}
}
