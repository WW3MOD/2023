#region Copyright & License Information
/*
 * WW3MOD @experimental — resupply PRECEDENCE (pure integer / boolean decisions).
 *
 * PERCEIVED BEHAVIOUR: when soldiers are out of ammo the bot saves up for a supply truck instead of
 * spending every call-in on cheap infantry it cannot arm — and it gives up saving the moment saving stops
 * working, rather than starving its own production forever.
 *
 * USER RULING 2026-08-15, and it is a PRECEDENCE, not a weight: "soldiers out of ammo are useless. That
 * should be the first priority to solve at all times." Nothing in the procurement path could express that.
 * The system had two axes — a per-mille share of army VALUE, and a fleet SIZE — and neither can say "this
 * one comes first". This file is that missing axis.
 *
 * ===== WHY THE BOUND IS A CASH-PROGRESS PREDICATE AND NOT A CYCLE COUNT =====
 *
 * The first cut of this bounded banking by a fixed number of cycles, and adversarial review showed that a
 * cycle count is the WRONG SHAPE for the job — not merely a badly chosen number. Three defects, all of them
 * consequences of the form:
 *
 *   1. IT DOES NOT TERMINATE. The counter resets whenever a cycle does not bank, INCLUDING when patience is
 *      spent, so hitting the cap inserts one purchase and restarts the bank. For any player whose income
 *      over the cap's span is below the truck price, the steady state is: N silent cycles, one buy, N silent
 *      cycles, forever, and the truck is NEVER bought.
 *
 *   2. THE FALL-THROUGH BUY IS PRICED BY THE SAVINGS. Composition eligibility is affordability-filtered, so
 *      a fat balance PROMOTES expensive slots into the argmax. Measured: a spell banked to cash 819 (181
 *      short of a 1000 truck), hit the cap, and immediately bought a 450 humvee — 819 down to 203. A cap set
 *      below the price does not delay the purchase, it periodically DESTROYS the savings, converting "many
 *      cheap units, promptly" into "one expensive non-truck unit, late".
 *
 *   3. A CYCLE COUNT CANNOT ENCODE A PER-MAP, PER-PLAYER ECONOMY RATE. The same constant is a safety limit
 *      for a rich player and the engine of the loop above for a poor one.
 *
 * The predicate below replaces all three: bank only while the balance is RISING toward the price, and
 * abandon once it stalls. Self-calibrating, and it TERMINATES BY CONSTRUCTION — banking continues only while
 * cash sets new highs, so the balance is monotonically approaching the price and must eventually reach it;
 * otherwise progress stops and the spell ends. There is no configuration under which it can loop forever.
 *
 * IT ALSO ABSORBS A DEFECT IT DOES NOT FIX, which is the honest reason to prefer it. The bank silences only
 * the composition lane: UnitBuilderBotModule.BotTick drains priorityBuildRequests and queuedBuildRequests
 * through the single-name BuildUnit overload BEFORE the queue loop, and that path never consults this
 * decision — so CaptureCoordinatorBotModule and AdaptiveProductionBotModule keep spending, as do the
 * separate .heli UnitBuilder trait instances, all from the same treasury. Silencing those lanes from here
 * would mean overriding other modules' own guarantees (the capture-supply floor is a correctness contract in
 * its own right), so instead this NOTICES the drain: if another spender is taking the income, cash sets no
 * new high, the stall counter climbs, and banking abandons within a few cycles instead of holding production
 * silent against a treasury it does not control. Measured shape of exactly that: a player banked 30
 * consecutive cycles while cash sat pinned at 3-9 — flat, never climbing — which is a drain, not poverty,
 * since a merely poor player's cash still creeps upward during silence.
 *
 * DETERMINISM (influence-stack invariant): zero random draws, pure functions of caller-supplied scalars, no
 * world or actor references — NUnit-pinned without a game run.
 *
 * OFF-SWITCH CONTRACT: maxStalledCycles <= 0 (the default) makes ShouldBankCycle constantly false, which
 * reproduces the pre-feature answer verbatim — the cycle falls through exactly as it does today. @stable
 * additionally never reaches any of this, since it does not set SupplyDemandSizing.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	public static class SupplyPrecedenceMath
	{
		/// <summary><para>The stall counter for this banking spell: reset to 0 when the balance sets a NEW HIGH,
		/// incremented otherwise.</para>
		///
		/// <para>"New high" rather than "rose since last cycle" on purpose — income arrives in lumps and upkeep
		/// nibbles between them, so a spell that is genuinely working still shows flat and even slightly
		/// falling cycles (a measured good spell ran 92/92/158/224/224/290/…/554/521/521/587, i.e. two flat
		/// cycles and a dip, while climbing overall). Comparing against the spell's best absorbs that
		/// jitter while still catching a balance that has stopped advancing, which is the thing that
		/// distinguishes "saving up slowly" from "another spender is taking the income".</para></summary>
		public static int UpdateStall(long cashNow, long bestCashThisSpell, int stalledCycles)
		{
			if (cashNow > bestCashThisSpell)
				return 0;

			return stalledCycles + 1;
		}

		/// <summary><para>Should this build cycle buy NOTHING and keep banking toward a supply truck?</para>
		///
		/// <para>True only when the fleet is genuinely short of what current demand wants
		/// (<paramref name="fleetShort"/> — itself demand-derived, so false whenever nobody needs resupply),
		/// the truck is not affordable yet (<paramref name="truckAffordable"/> false — if we CAN afford it the
		/// caller buys it outright and never asks), and the balance has not stalled for
		/// <paramref name="maxStalledCycles"/> consecutive cycles.</para>
		///
		/// <para>TERMINATION, which is the property the previous cycle-count bound lacked: banking persists only
		/// while <see cref="UpdateStall"/> keeps returning 0, i.e. only while cash sets new highs. A balance
		/// that keeps setting new highs reaches any fixed price in finite time; one that does not ends the
		/// spell. So this cannot become a permanent production freeze, and it cannot become the
		/// bank-to-just-under-the-price-then-spend-it-all loop either, because it gives up at the point
		/// progress stops rather than at an arbitrary tick count further on.</para>
		///
		/// <para><paramref name="maxStalledCycles"/> &lt;= 0 is the OFF switch and returns false unconditionally.</para></summary>
		public static bool ShouldBankCycle(bool fleetShort, bool truckAffordable, int stalledCycles, int maxStalledCycles)
		{
			if (maxStalledCycles <= 0)
				return false;

			if (!fleetShort || truckAffordable)
				return false;

			return stalledCycles < maxStalledCycles;
		}

		/// <summary><para>Has this player ever held a supply truck? Monotone: once true it stays true for the rest
		/// of the match, whatever happens to the fleet afterwards.</para>
		///
		/// <para>THE MONOTONICITY IS THE WHOLE POINT, not tidiness. <see cref="RefuseResupplyBuy"/> uses this to
		/// decide whether the ammo gate still binds, and a non-latching "do I own one right now" would re-arm
		/// the gate the moment the fleet is wiped — stalling the rebuild at the exact moment the bot most needs
		/// a truck, and oscillating between gated and ungated for the rest of the match. The user ruling being
		/// honoured is about the OPENING ("we don't want bots to spend money on things it doesn't need at the
		/// start"), so the thing it keys on has to be a once-per-match event, not a live fleet reading.</para>
		///
		/// <para><paramref name="ownedOrPending"/> counts queued and in-flight call-ins, so the latch closes on
		/// the cycle the first truck is ORDERED rather than waiting for it to walk in from the map edge.</para></summary>
		public static bool LatchFirstTruck(bool alreadyHeld, int ownedOrPending)
		{
			return alreadyHeld || ownedOrPending > 0;
		}

		/// <summary><para>Should this build cycle REFUSE to buy a resupply unit? The supply-truck procurement gate,
		/// shared by both call sites so the two cannot drift apart.</para>
		///
		/// <para>USER RULING 2026-08-17, splitting a genuine tension: "I still don't want unnecessary trucks… We
		/// don't want bots to spend money on things it doesn't need at the start", resolved by SCOPE — the
		/// earlier ruling ("no supply truck until at least one unit is below full ammo") governs the FIRST
		/// truck, and the standing reserve governs every truck after it. The tension is real rather than a bug:
		/// a reserve that waits until someone is dry is not a reserve, because a truck bought after the men are
		/// dry arrives after the fight — but a reserve that applies from t=0 makes trucks the opening buy,
		/// which is what the first ruling exists to forbid.</para>
		///
		/// <para>So <paramref name="fleetUnderDesired"/> may only override the ammo test once
		/// <paramref name="heldFirstTruck"/> is set. Before that the shortfall is ignored and a genuine ammo
		/// need is the only thing that buys a truck. This is the defect the previous shape had: both sites read
		/// `!fleetUnderDesired &amp;&amp; !anyUnitNeedsResupply`, so the standing floor SHORT-CIRCUITED the gate and the
		/// ammo test never ran at all — first truck at cycle 11, ~14 s in, with every soldier at full ammo.</para>
		///
		/// <para>OFF-SWITCH CONTRACT: with SupplyDemandSizing off the caller's
		/// <paramref name="fleetUnderDesired"/> is constantly false, so the returned answer is
		/// `!anyUnitNeedsResupply` regardless of the latch — the pre-feature predicate verbatim. Profiles that
		/// leave GateResupplyOnAmmoNeed off (every one but @experimental) short-circuit at the call site and
		/// never reach this at all, so they pay neither the decision nor the two actor walks behind it.</para></summary>
		public static bool RefuseResupplyBuy(bool fleetUnderDesired, bool heldFirstTruck, bool anyUnitNeedsResupply)
		{
			if (anyUnitNeedsResupply)
				return false;

			return !heldFirstTruck || !fleetUnderDesired;
		}

		/// <summary><para>How many customers to size the supply fleet from.</para>
		///
		/// <para><paramref name="useNeedBar"/> off ⇒ <paramref name="starvingCustomers"/> verbatim, today's answer.
		/// On ⇒ <paramref name="needyCustomers"/>, the count at SupplyProvider's own service bar.</para>
		///
		/// <para>The max() is not defensive padding, it is a monotonicity guarantee worth stating: the need bar is
		/// LOOSER than the starving bar, so needy should always be the larger of the two, and taking the max
		/// means switching this flag on can only ever raise the fleet, never lower it. If the two counts ever
		/// cross — a config that sets SupplyStarvingThresholdPerMille above the need threshold — this keeps
		/// the change one-directional instead of silently shrinking the fleet on a threshold edit.</para></summary>
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
