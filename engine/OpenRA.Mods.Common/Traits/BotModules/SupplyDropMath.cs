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
		/// <item><paramref name="cacheSupplyNearAnchor"/> &lt; <paramref name="redundantCacheSupply"/> — the
		/// demand is not already covered by a crate we (or an ally) left here. Responsive, and the strongest
		/// of the four: the drop creates exactly the cache this gate then sees. Same-cell drops merge
		/// (DropsSupplyCache.DropSupplyCacheHere), so this gate exists for the NEAR-miss case where the
		/// truck stopped a cell or two off the anchor.</item>
		/// </list>
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
			int cacheSupplyNearAnchor, int redundantCacheSupply)
		{
			if (!anchorEstablished)
				return false;

			if (truckSupply < (minSupply > 0 ? minSupply : 1))
				return false;

			if (starvingNearAnchor < (minStarving > 0 ? minStarving : 1))
				return false;

			if (redundantCacheSupply > 0 && cacheSupplyNearAnchor >= redundantCacheSupply)
				return false;

			return true;
		}
	}
}
