#region Copyright & License Information
/*
 * WW3MOD @experimental — resupply PRECEDENCE (pure integer / boolean decisions).
 *
 * PERCEIVED BEHAVIOUR: when soldiers are out of ammo the bot buys a supply truck, and it buys it SOON —
 * instead of spending every call-in on cheap infantry it cannot arm and never accumulating the price of a
 * truck.
 *
 * USER RULING 2026-08-15, and it is a PRECEDENCE, not a weight: "soldiers out of ammo are useless. That
 * should be the first priority to solve at all times." Nothing in the procurement path expressed that. The
 * system had only two axes — a per-mille share of army VALUE, and a fleet SIZE — and neither can say "this
 * one comes first". This file is that missing axis.
 *
 * THE MEASURED DEFECT IT ANSWERS (tournament-arena-composition-2p, @experimental mirror, full match):
 * ZERO trucks ordered by any lane, while `ammo-need=True` held continuously from tick 1240 to the end.
 * Two routes to a truck were closed simultaneously, for unrelated reasons:
 *
 *   1. THE FLEET WAS SIZED FROM THE WRONG PREDICATE. DesiredTrucks is fed CountStarvingCustomers, which
 *      counts units below SupplyStarvingThresholdPerMille (250 => under 25% ammo). The gate that reports
 *      "somebody needs resupply" is AnyFieldedUnitNeedsResupply, which mirrors SupplyProvider's OWN
 *      MinNeedThreshold — a much looser bar, and the bar at which the supply system actually SERVES a
 *      customer. So `starving` read 0 at every single snapshot while `ammo-need` read True: two predicates
 *      for one fact with different bars, and the stricter one sized the fleet. Fix: size from the same bar
 *      that serves. NOT by lowering SupplyStarvingThresholdPerMille, which is also the truck's own SEEK
 *      threshold (SupplyHuntMath.BelowSeekThreshold) and would silently retarget delivery behaviour.
 *
 *   2. EVEN SIZED CORRECTLY, THE TRUCK COULD NOT BE AFFORDED — because the bot spends to ~0 every cycle on
 *      cheap types. Measured cash after tick 760: 43 / 121 / 95 / 64 / 40 / 9 / 3, against a 1000-cost
 *      truck, with income ~79 per census interval. The money to buy a truck existed in AGGREGATE and was
 *      spent piecemeal before it could accumulate. A pre-empt that merely SKIPS when it cannot afford the
 *      item is not a priority at all: the cycle then falls through and buys a rifleman with the very cash
 *      the truck was waiting for. Precedence has to be able to say "buy nothing yet".
 *
 * WHY THIS IS NOT A RESTORED SupplyTruckFloor. A floor is a constant with no denominator, and that is the
 * bug the user reported FIRST (two trucks at t=0, PIPELINE 57(a)) and again as two medics — see
 * SupportFloorMath for the general statement. Nothing here fires when no unit needs resupply: banking is
 * gated on live, measured demand, so at t=0 with a full-ammo army it is inert. The fleet SIZE remains
 * demand-proportional (ceil(needy / customersPerTruck)); this only decides whether to WAIT for it.
 *
 * DETERMINISM (influence-stack invariant): zero random draws, pure boolean/integer functions of caller-
 * supplied scalars, no world or actor references — NUnit-pinned without a game run.
 *
 * OFF-SWITCH CONTRACT: maxBankCycles <= 0 (the default) makes ShouldBankCycle constantly false, which
 * reproduces the pre-feature answer verbatim — the cycle falls through exactly as it does today. @stable
 * additionally never reaches any of this, since it does not set SupplyDemandSizing.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	public static class SupplyPrecedenceMath
	{
		/// <summary>Should this build cycle buy NOTHING and bank the cash toward a supply truck?
		///
		/// True only when all of: the fleet is genuinely short of what current demand wants
		/// (<paramref name="fleetShort"/> — which is itself demand-derived, so it is false whenever nobody
		/// needs resupply), the truck is not affordable yet (<paramref name="truckAffordable"/> false — if we
		/// CAN afford it the caller buys it outright and never asks), and we have not already banked for
		/// <paramref name="maxBankCycles"/> consecutive cycles.
		///
		/// THE BOUND IS THE WHOLE SAFETY ARGUMENT. Precedence without a bound is a deadlock: an army whose
		/// income never reaches the truck price would buy nothing at all, forever, and lose to a bot that at
		/// least kept making riflemen. <paramref name="maxBankCycles"/> caps how long the whole procurement
		/// path may stay silent, after which the cycle falls through and ordinary buying resumes; the counter
		/// resets as soon as a truck is bought or demand goes away, so the next spell of demand gets a fresh
		/// budget of patience rather than inheriting an exhausted one.
		///
		/// <paramref name="maxBankCycles"/> &lt;= 0 is the OFF switch and returns false unconditionally.</summary>
		public static bool ShouldBankCycle(bool fleetShort, bool truckAffordable, int consecutiveBanked, int maxBankCycles)
		{
			if (maxBankCycles <= 0)
				return false;

			if (!fleetShort || truckAffordable)
				return false;

			return consecutiveBanked < maxBankCycles;
		}

		/// <summary>How many customers to size the supply fleet from.
		///
		/// <paramref name="useNeedBar"/> off ⇒ <paramref name="starvingCustomers"/> verbatim, today's answer.
		/// On ⇒ <paramref name="needyCustomers"/>, the count at SupplyProvider's own service bar.
		///
		/// The max() is not defensive padding, it is a monotonicity guarantee worth stating: the need bar is
		/// LOOSER than the starving bar, so needy should always be the larger of the two, and taking the max
		/// means switching this flag on can only ever raise the fleet, never lower it. If the two counts ever
		/// cross — a config that sets SupplyStarvingThresholdPerMille above the need threshold — this keeps
		/// the change one-directional instead of silently shrinking the fleet on a threshold edit.</summary>
		public static int SizingCustomers(bool useNeedBar, int starvingCustomers, int needyCustomers)
		{
			if (starvingCustomers < 0)
				starvingCustomers = 0;

			if (!useNeedBar)
				return starvingCustomers;

			if (needyCustomers < 0)
				needyCustomers = 0;

			return needyCustomers > starvingCustomers ? needyCustomers : starvingCustomers;
		}
	}
}
