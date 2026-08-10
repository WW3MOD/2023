#region Copyright & License Information
/*
 * WW3MOD @experimental — supply-truck FLEET SIZING (pure integer math).
 *
 * PERCEIVED BEHAVIOUR: the number of supply trucks on the map follows how many soldiers are actually dry,
 * instead of being a fixed slice of the budget. A front with forty starving men gets a column of trucks; a
 * fed army keeps a small standing reserve and nothing more.
 *
 * WHY THIS EXISTS: composition-directed purchasing sizes every type as a per-mille share of army VALUE
 * (ForceCompositionMath), which is the right question for combat types and the WRONG one for logistics.
 * A truck's job scales with the number of CUSTOMERS, not with how expensive the army is — and because the
 * share is per value, a 1000-cost truck at a 40-per-mille target admits exactly one truck per 25,000 value
 * of army. Measured over a full 30-minute match: one standing truck per player, while half the infantry sat
 * out of ammo. Demand sizes logistics; budget share only bounds it.
 *
 * DELIBERATELY OVER-PROVISIONED. overcompensationPercent is a named, tunable multiplier on the honest
 * number, not a hidden fudge. Trucks are consumable — they drive toward the fighting, they get shot, and a
 * fleet sized to the exact requirement is a fleet that is short the moment one dies. Erring high costs
 * budget and is immediately visible; erring low is invisible and starves the army. Walk it down once the
 * fleet is observably working, and change it HERE rather than by re-deriving customersPerTruck.
 *
 * DETERMINISM (influence-stack invariant): zero random draws, integer-only, no world/actor references —
 * plain scalars in and out, so this is a pure deterministic map from its arguments and is NUnit-pinned
 * without a game run (mirrors ForceCompositionMath / EscortSizingMath / SupplyTruckHuntMath).
 *
 * BYTE-IDENTITY: nothing here is reachable unless UnitBuilderBotModuleInfo.SupplyDemandSizing is true,
 * which defaults false and is set only in the two @experimental faction blocks — so normal/rush/turtle and
 * @stable never enter this path.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	public static class SupplyFleetMath
	{
		/// <summary>How many supply trucks the fleet should hold, given how many customers are currently
		/// starving.
		///
		/// <c>ceil(starving / customersPerTruck)</c> is the honest requirement — one truck per load-out of
		/// demand. That is then scaled by <paramref name="overcompensationPercent"/> (100 = the honest
		/// number, 200 = double it) and clamped into [<paramref name="floor"/>, <paramref name="ceiling"/>].
		///
		/// The floor applies even with ZERO starving customers, and that is the point of having one: a fleet
		/// that is only bought once men are already dry arrives after the fight it was needed for. The
		/// ceiling is the budget guard — supply must never be able to consume the whole call-in allowance,
		/// however bad the front gets.
		///
		/// Degenerate config is absorbed rather than trusted: a non-positive customersPerTruck reads as 1
		/// (one truck per starving man — expensive, but never a divide-by-zero), a non-positive
		/// overcompensationPercent reads as 100, a negative floor reads as 0, and a ceiling below the floor
		/// is raised to it so the returned range is never empty.</summary>
		public static int DesiredTrucks(int starvingCustomers, int customersPerTruck,
			int overcompensationPercent, int floor, int ceiling)
		{
			if (customersPerTruck <= 0)
				customersPerTruck = 1;

			if (overcompensationPercent <= 0)
				overcompensationPercent = 100;

			if (floor < 0)
				floor = 0;

			if (ceiling < floor)
				ceiling = floor;

			if (starvingCustomers < 0)
				starvingCustomers = 0;

			// Both divisions round UP: eight starving men against a six-man truck load is two trucks, not
			// one — rounding down would leave a remainder of demand permanently unserved.
			//
			// WIDENED TO long BEFORE the round-up addition, not after. Both round-ups add to their numerator,
			// so `starving + customersPerTruck - 1` and `honest * percent` each overflow int at the extremes
			// and wrap NEGATIVE — which the floor then silently returns as a plausible-looking small fleet.
			// Narrowing happens only after the ceiling clamp, where the value is known to fit.
			var honest = ((long)starvingCustomers + customersPerTruck - 1) / customersPerTruck;
			var scaled = (honest * overcompensationPercent + 99) / 100;

			if (scaled < floor)
				return floor;

			return scaled > ceiling ? ceiling : (int)scaled;
		}
	}
}
