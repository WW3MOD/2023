#region Copyright & License Information
/*
 * WW3MOD supply-truck DROP-AND-LEAVE — the when-to-drop decision (pure math).
 *
 * PERCEIVED BEHAVIOUR: instead of shadowing an army it can never catch up with, a loaded supply truck
 * drives ONCE to a forward supply point behind the line, unloads its whole stock as a SUPPLYCACHE, and
 * leaves. Infantry walk to the cache. The cache does not move, so the errand terminates for both parties.
 *
 * WHY A STATIC DESTINATION IS THE WHOLE POINT (2026-08-08). The follow/evac cycle is a limit cycle BY
 * CONSTRUCTION, not a tuning failure: the relief valve re-selects the same needy cluster the moment the
 * truck cools, so approach -> abort -> approach repeats forever (WORKSPACE/recon/260808-truck-post-fix-
 * behaviour.md). The pull side has the mirror defect: AutoSeekSupplies applies its 20-cell leash at
 * SELECTION ONLY and then rides MoveWithinRange, which re-paths every time the provider's cell changes and
 * stops only on arrival — so infantry at speed 25 chase a truck at speed 75 and lose ~7.3 cells per scan,
 * combat-inert the whole way. BOTH defects are the same shape and BOTH dissolve against a destination that
 * does not move. That is why this is a new mode rather than more damping: damping bounds an excursion,
 * a static destination removes the excursion.
 *
 * THE GATE INVARIANT (SupplyLogisticsMath's header, restated because this file adds a gate). A TEST THAT
 * CAN PIN A BRANCH TRUE MAY READ ONLY RESPONSIVE TERMS, UNLESS ITS NON-RESPONSIVE TERMS ARE BOUNDED BY A
 * GATE APPLIED IN THE SAME SCAN. Three review rounds on the evac fix each found a violation of it. This
 * decision is deliberately built so it cannot join them:
 *   * It has NO MEMORY. There is no "already dropping" latch to forget to clear. The caller re-evaluates
 *     from scratch every scan and re-issues the identical errand to the identical anchor, which is
 *     idempotent precisely BECAUSE the anchor is static — the property the whole mode is built on.
 *   * Every term RESPONDS to the action it causes. Dropping empties the truck (truckSupply -> 0, fails
 *     the load gate) and creates a cache at the anchor (cacheSupplyNearAnchor jumps, fails the redundancy
 *     gate) and refills the soldiers that walk to it (starvingNearAnchor falls). So a drop switches its
 *     own decision off through three independent terms. A second drop needs the situation to genuinely
 *     re-arise.
 *   * Every misconfiguration fails toward NOT DROPPING, never toward dropping. See the floors below.
 *
 * DETERMINISM (influence-stack invariant): pure integer comparisons, zero random draws, no iteration. The
 * caller's inputs are counts and sums, which are order-independent by construction.
 *
 * v3-portable: engine-free static math (NUnit-pinned in SupplyDropMathTest); the plumbing that samples the
 * terms and issues the errand (SupplyFollowerBotModule) is engine-specific.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	public static class SupplyDropMath
	{
		/// <summary>Should this truck unload its whole stock at the forward supply point this scan?
		///
		/// <para>Four gates, ALL of which must pass, and each of which the drop itself then switches off:</para>
		/// <list type="number">
		/// <item><paramref name="anchorEstablished"/> — a forward supply point was actually resolved. False
		/// when the belief field is flat or the front is on top of the Supply Route, in which case the
		/// descent returns the SR unchanged and there is nowhere sensible to leave a crate. NOT responsive
		/// to the drop, and deliberately so: it is the caller's own gate, established in the same scan, which
		/// is what the invariant above permits. Named for what was ESTABLISHED, so a caller that forgets to
		/// set it refuses to drop rather than dropping at the beachhead.</item>
		/// <item><paramref name="truckSupply"/> ≥ <paramref name="minSupply"/> — worth the trip. A truck
		/// holding dribble should keep serving from its own aura rather than littering the map with crates
		/// that vanish at RemoveBelowSupply. Responsive: the drop sets supply to 0.</item>
		/// <item><paramref name="starvingNearAnchor"/> ≥ <paramref name="minStarving"/> — real demand within
		/// walking distance of the anchor. Responsive: soldiers that reach the cache stop starving.</item>
		/// <item><paramref name="cacheSupplyNearAnchor"/> + <paramref name="inFlightSupplyToAnchor"/> &lt;
		/// <paramref name="redundantCacheSupply"/> — the demand is not already covered, counting BOTH the
		/// crates on the ground and the loads of trucks already dispatched here. Responsive, and the load
		/// bearer of the four (see the note below on why it carries more weight than first assumed).</item>
		/// </list>
		///
		/// <para>IN-FLIGHT SUPPLY IS NOT AN OPTIMISATION — WITHOUT IT THE GATE SEES THE FLEET ONE SCAN LATE
		/// AND EVERY TRUCK DROPS AT ONCE. The three responsive terms all respond to a COMPLETED drop, and
		/// completion takes a drive. Trucks are evaluated in one loop over unchanged world state, so on the
		/// first scan that the conditions hold, every truck reads <paramref name="cacheSupplyNearAnchor"/> = 0,
		/// every truck passes, and the whole fleet unloads at one cell and is then retired empty. The
		/// invariant "every term responds to the action" survives literally and is still useless here,
		/// because it is satisfied a scan too late for every truck but the first. So the gate must count
		/// supply that is COMMITTED, not merely supply that has LANDED. The caller derives this by summing
		/// the loads of trucks whose recorded drop target is this anchor — memory of the ORDER, not of the
		/// decision, and the same map that suppresses duplicate re-issue, so there is no second piece of
		/// state that can drift out of agreement with the first.</para>
		///
		/// <para>WHY THIS GATE CARRIES MORE THAN IT LOOKS LIKE. An earlier version of this comment said
		/// same-cell drops merge and that this gate therefore only covered the near-miss case. That premise
		/// is FALSE: SUPPLYCACHE is a Building with `Footprint: x` (misc.yaml), and `x` is
		/// FootprintCellType.Occupied — a blocked cell (Building.cs:20-27) — so a truck can never stand on a
		/// cache's cell. `DropSupplyCacheHere`'s merge branch and `CanDropCache`'s co-located allowance are
		/// therefore both UNREACHABLE, and EVERY drop is the near-miss case. Nothing coalesces crates; this
		/// gate alone is what stops them stacking.</para>
		///
		/// <para>FLOOR POLICY — every knob fails toward NOT dropping. <paramref name="minSupply"/> and
		/// <paramref name="minStarving"/> are floored at 1, so "0" cannot be read as "no requirement" and
		/// dump a crate for nobody. <paramref name="redundantCacheSupply"/> is the one inverted knob (a
		/// SMALLER value is stricter), so 0 or less DISABLES that gate instead of flooring it — the literal
		/// reading, `cacheSupply >= 0`, would be permanently true and would silently disable the whole mode,
		/// which is the failure that looks like a config typo and reads like a broken feature.</para>
		/// Pure integer, zero RNG.</summary>
		public static bool ShouldDrop(
			bool anchorEstablished,
			int truckSupply, int minSupply,
			int starvingNearAnchor, int minStarving,
			int cacheSupplyNearAnchor, int inFlightSupplyToAnchor, int redundantCacheSupply)
		{
			if (!anchorEstablished)
				return false;

			if (truckSupply < (minSupply > 0 ? minSupply : 1))
				return false;

			if (starvingNearAnchor < (minStarving > 0 ? minStarving : 1))
				return false;

			if (redundantCacheSupply > 0 && cacheSupplyNearAnchor + inFlightSupplyToAnchor >= redundantCacheSupply)
				return false;

			return true;
		}

		/// <summary>Has the truck actually reached the cell it was sent to unload at? Chebyshev-free squared
		/// comparison against the move's own stop tolerance, matching how Move measures "near enough"
		/// (Move.cs: `(mobile.ToCell - destination).LengthSquared &lt;= cellRange * cellRange`).
		///
		/// <para>THIS IS THE GUARD AGAINST DUMPING THE LOAD AT THE WRONG END OF THE MAP, and it is needed
		/// because an unreachable destination does not fail loudly. The anchor comes from a belief-field
		/// descent that guards bounds and danger but NOT terrain, so it can land on water, cliff or outside
		/// the playable area. `PathFinder.FindPathToTargetCell` bails to NoPath when the target cell is
		/// inaccessible, and `Move.Tick` treats an empty path as "arrived" — it sets destination to the
		/// current cell and completes in about two ticks. The move's own `nearEnough` tolerance does NOT
		/// rescue this: it is consumed only inside `PopPath`'s actor-blocked branch, which an empty path
		/// never reaches. So without this check the follow-on unload runs at whatever cell the truck was
		/// standing on when it got the order — typically the beachhead — and the caller's redundancy gate,
		/// which measures around the ANCHOR, never sees the crate and lets the next truck repeat it.</para>
		///
		/// <para>Note the actor-blocked case is genuinely handled by the engine and is not what this guards:
		/// a path exists, `PopPath` finds the next cell occupied, and the tolerance applies as intended.
		/// Terrain-impassable is the case with no path at all.</para>
		/// Pure integer, zero RNG.</summary>
		public static bool ArrivedAtDropCell(int dx, int dy, int toleranceCells)
		{
			var t = toleranceCells > 0 ? toleranceCells : 0;
			return dx * dx + dy * dy <= t * t;
		}

		/// <summary>Should the caller ISSUE a drop errand, given it may already have one in flight? False when
		/// this truck was already dispatched to this exact cell.
		///
		/// <para>Suppressing the re-issue matters even though the order handler rebuilds the whole chain, so
		/// it is not merely tidiness. The errand is issued non-queued, so re-issuing CANCELS the running
		/// activity — nulling its continuation and destroying the queued unload and restock tail — then
		/// rebuilds it, at the cost of a fresh pathfind and, via `Move.Cancel` clearing the path mid-step, up
		/// to a cell of backslide. Repeated every scan forever, on a unit whose entire job is to arrive, that
		/// reproduces the visible stutter this mode exists to remove. `PoiOffensiveBotModule`, the sibling
		/// consumer of the same staging primitive, keeps exactly this per-unit last-target dedup for the same
		/// reason even though its anchor is static too.</para>
		///
		/// <para>The dedup key is the TARGET CELL, not a boolean, and that is what keeps it from becoming a
		/// latch: an anchor that moves differs from what was recorded and re-issues by itself, so no separate
		/// "is it still valid" flag exists to go stale. The caller is still responsible for clearing the
		/// record whenever something else cancels the errand — see the revoke path in the module.</para>
		/// Pure, zero RNG.</summary>
		public static bool ShouldIssueDrop(bool alreadyDispatched, int sentToX, int sentToY, int anchorX, int anchorY)
		{
			return !alreadyDispatched || sentToX != anchorX || sentToY != anchorY;
		}

		/// <summary>Is a recorded drop errand still actually RUNNING? False when the truck has gone idle while
		/// still holding its load, which means the errand ended without unloading — and the caller must then
		/// void its dispatch record so the retry is not suppressed.
		///
		/// <para>THIS EXISTS BECAUSE THE RE-ISSUE DEDUP CREATED A LATCH, and it is the specific defect species
		/// that has bitten this branch repeatedly: a test that pins a branch while reading a term that cannot
		/// respond. Suppressing re-issue is correct while an errand runs, but two refusals inside the errand
		/// are designed to be SELF-CORRECTING — arrival on a cell that turned out to be occupied
		/// (`CanDropCache` false), and a destination that became unreachable after issue (the arrival check
		/// refuses) — and both self-corrections happen by RE-ISSUING next scan. The dedup silently deleted
		/// both, converting "retries" into "parks on its anchor forever, holding a full load".</para>
		///
		/// <para><paramref name="idle"/> is the responsive term and is what makes this safe: re-issuing the
		/// errand makes the truck non-idle, so the void condition switches itself off. It is the same
		/// observable `StepEvac`'s leg model uses to notice a Move that never arrived. A truck that DID unload
		/// sits at 0 supply and has already left the eligible roster, so it is pruned rather than reaching
		/// here — which is why "idle" can be read as "finished without effect" rather than merely
		/// "finished".</para>
		///
		/// <para>Note the polarity, chosen so a forgotten call fails SAFE: the question is whether the errand
		/// is KNOWN to be running. A caller that never asks keeps `dispatched` true and suppresses — so the
		/// call site is the load-bearing part, and it is pinned in both directions rather than only in the
		/// direction that happens to be true today.</para>
		/// Pure, zero RNG.</summary>
		public static bool ErrandStillRunning(bool dispatched, bool idle)
		{
			return dispatched && !idle;
		}
	}
}
