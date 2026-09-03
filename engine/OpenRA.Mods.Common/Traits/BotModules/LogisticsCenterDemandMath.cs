#region Copyright & License Information
/*
 * WW3MOD @experimental — should the bot BUY a Logistics Center right now? (pure integer decisions)
 *
 * USER RULING 2026-09-03, and it is the specification this file implements verbatim: "they are usually
 * not necessary. I personally only buy them when I want to keep pressure up and let vehicle rearm while
 * they are far from the SR. We definitely do not need them at the start of the game... It is expensive
 * and has no purpose in the beginning because all units are fully armed... The LC is such a big
 * investment, that it is only really useful when we have a lot of money and units, and we need a forward
 * resupply point far from our SR. So it is more of a late game thing than early. Units can always
 * evacuate to rearm." And, on the same map: "there are already a neutral LC on each side, so the bots
 * should instead capture the exisiting one."
 *
 * WHAT WAS THERE BEFORE: nothing. LogisticsCenterBotModule.MaintainCenterDemand tested
 * `centers + mcvs + pending >= DesiredCenters` and `funds >= MinCashToRequest`, and that was the whole
 * gate. DesiredCenters is 1 and MinCashToRequest is 3000, so with the default opening balance the FIRST
 * evaluation — ScanInterval 100 ticks, ~6 s in — buys a 3000-credit Centre while every soldier is at
 * full ammo and nothing is anywhere near the front. That is the reported behaviour exactly, and it is
 * not a mistuned constant: there was no need term of any kind to mistune.
 *
 * ===== WHY THE VALUE MODEL IS TEMPO, NOT CAPABILITY =====
 *
 * "Units can always evacuate to rearm" is the load-bearing half of the ruling, because it fixes what the
 * Centre is worth. Evacuation is always available and always free, so a Logistics Center adds NO
 * capability the bot lacks — a bot without one rearms perfectly well, just slower. What it buys is
 * UPTIME: the fraction of a round-trip to the Supply Route that a forward unit no longer has to drive.
 *
 * That makes the do-nothing option viable by construction, which is the property the ruling asks for,
 * and it means the Centre's value must go to ZERO in every situation the user named as wasteful:
 *   * nobody forward        ⇒ no trip to shorten          ⇒ tripSaving 0    ⇒ value 0
 *   * everybody full of ammo ⇒ no trip is pending          ⇒ ammoNeed 0      ⇒ value 0
 *   * a tiny army           ⇒ almost no uptime to return  ⇒ forwardValue ~0 ⇒ value ~0
 * All three of the user's conditions therefore fall out of ONE number rather than being three ad-hoc
 * flags that can be satisfied separately, and the opening buy is refused by all three at once.
 *
 * ===== THE TANK IS THE UNIT OF ACCOUNT, ON BOTH AXES =====
 *
 * "it costs more than a tank, so some kind of logic needs to decide that the LC is worth more than a
 * tank at that moment". It literally does cost more: LCCV/LOGISTICSCENTER are 3000 (structures.yaml:422,
 * vehicles.yaml:LCCV) against abrams 2500 (vehicles-america.yaml:456) and t90 2400. So the tank is the
 * thing actually given up, and it is used here for BOTH tests the ruling contains:
 *
 *   WORTH   — ForwardResupplyValue(...) > tankCost. The uptime returned to the line, priced in credits,
 *             must beat the combat power the same money buys outright.
 *   AFFORD  — funds >= centerCost + tankCost. "we have a lot of money" and "not crowding out combat
 *             power" are the same requirement stated twice: we may buy the Centre only if we could
 *             still buy a tank afterwards. A bot that spends its last 3000 on a depot has crowded out
 *             its army by exactly the amount the ruling objects to.
 *
 * Deriving the comparand from the ruleset rather than hard-coding 2500 is deliberate: a balance pass
 * that reprices the tank must move this decision with it, and a literal here would silently stop
 * tracking the thing it claims to compare against.
 *
 * ===== CAPTURE BEATS PURCHASE, AND WHY THE DISTANCE BOUND IS NOT OPTIONAL =====
 *
 * A neutral Centre is free and a bought one is 3000, so a reachable neutral Centre must veto the buy —
 * that is the first half of the report. But the veto cannot be unconditional. GetCaptureTargets is
 * map-wide, and three of the ten shipped maps place their neutral Centres in PAIRS, one per side
 * (woodland-warfare, river-zeta, polar-disorder). An unconditional veto therefore lets the ENEMY's
 * Centre — sitting behind their line, which this bot will never take — block a purchase forever, which
 * converts "prefer the free one" into "never own one". The distance bound is what keeps the veto
 * meaning "there is a free one worth going for" instead of "a free one exists somewhere on the planet".
 *
 * DETERMINISM (influence-stack invariant): zero random draws, pure functions of caller-supplied scalars,
 * no world or actor references. Integer throughout — per-mille rather than float — so the answer cannot
 * vary with FP contraction between platforms. NUnit-pinned without a game run.
 *
 * OFF-SWITCH CONTRACT: ShouldRequestCenter with requireDemand false reproduces the pre-feature answer
 * exactly (quota + affordability only), so the whole model can be switched out from YAML without
 * deleting it. @stable additionally never reaches any of this: LogisticsCenterBotModule is declared
 * only under enable-ai-experimental (ai.yaml:1178-1179) and @stable never instantiates the trait.
 */
#endregion

using System;

namespace OpenRA.Mods.Common.Traits
{
	public static class LogisticsCenterDemandMath
	{
		/// <summary><para>Is a free Centre already on offer, close enough to be worth waiting for?</para>
		///
		/// <para>Counts capturable Centres against the SAME quota the purchase gate uses, so "we want one and
		/// there is one to take" refuses the buy while "we want two and there is one to take" still allows
		/// the second to be bought. <paramref name="capturableWithinReach"/> must already be filtered by
		/// distance at the call site — see the header for why an unbounded count lets the enemy's Centre
		/// veto this bot's purchase for the whole match.</para>
		///
		/// <para>Deliberately NOT time-bounded. A patience timer was considered and rejected: it would buy a
		/// Centre precisely when a capture is being contested and therefore slow, which is the moment the
		/// 3000 credits are most needed for the fight over it.</para></summary>
		public static bool CaptureCoversDemand(int centersHeld, int capturableWithinReach, int desiredCenters)
		{
			if (desiredCenters <= 0)
				return true;

			return Math.Max(0, centersHeld) + Math.Max(0, capturableWithinReach) >= desiredCenters;
		}

		/// <summary><para>How much of the rearm round-trip a forward Centre removes, in per mille.</para>
		///
		/// <para>The units are <paramref name="forwardDistanceCells"/> from the Supply Route, so that is the
		/// trip they drive today. A Centre sited a standoff behind the believed line stands near them
		/// rather than on top of them, so a residual walk survives — <paramref name="residualTripCells"/>,
		/// the distance from the fighting line back to that standoff. The saving is the difference.</para>
		///
		/// <para>Returns 0 when the army is no further out than the residual: units already inside the
		/// Centre's own catchment have nothing to save, which is exactly the opening — everyone parked on
		/// the beachhead, distance ~0, saving 0, and the buy refused for that reason rather than by a
		/// timer that would let it through at t+1.</para></summary>
		public static int TripSavingPerMille(int forwardDistanceCells, int residualTripCells)
		{
			if (forwardDistanceCells <= 0)
				return 0;

			var residual = Math.Max(0, residualTripCells);
			if (forwardDistanceCells <= residual)
				return 0;

			return 1000 * (forwardDistanceCells - residual) / forwardDistanceCells;
		}

		/// <summary><para>What that tempo is worth, in CREDITS, at this moment.</para>
		///
		/// <para>forwardArmyValue × ammoNeed × tripSaving. Each factor answers one of the user's three
		/// conditions and any one of them at zero zeroes the product, which is the point of multiplying
		/// rather than scoring: "a lot of units" cannot compensate for "nobody needs ammo", and a starving
		/// army parked on the Supply Route cannot justify a forward depot it would never drive to.</para>
		///
		/// <para><paramref name="ammoNeedPerMille"/> is the need across the FORWARD army only, on the same
		/// missing/capacity metric SupplyProvider itself uses (ResupplyDemand.UnitNeed), so this reads the
		/// economy's own signal rather than a parallel one invented here.</para>
		///
		/// <para>long arithmetic internally: a late-game forward army can be worth six figures and the two
		/// per-mille factors multiply before they divide, which overflows int on the intermediate.</para></summary>
		public static int ForwardResupplyValue(int forwardArmyValue, int ammoNeedPerMille, int tripSavingPerMille)
		{
			if (forwardArmyValue <= 0 || ammoNeedPerMille <= 0 || tripSavingPerMille <= 0)
				return 0;

			var need = Math.Min(1000, ammoNeedPerMille);
			var saving = Math.Min(1000, tripSavingPerMille);

			var value = (long)forwardArmyValue * need * saving / 1000000L;

			return value > int.MaxValue ? int.MaxValue : (int)value;
		}

		/// <summary><para>Can the Centre be paid for without crowding out combat power? True only when the
		/// treasury still covers a tank AFTER the Centre — see the header: "we have a lot of money" and
		/// "not crowding out combat power" are one requirement, and this is it.</para></summary>
		public static bool AffordableAlongsideCombat(long funds, int centerCost, int tankCost)
		{
			return funds >= (long)Math.Max(0, centerCost) + Math.Max(0, tankCost);
		}

		/// <summary><para>THE GATE. Should a Logistics Center be requested this evaluation?</para>
		///
		/// <para>Ordered cheapest-refusal-first, and the order is load-bearing for the reported bug: the
		/// quota and the capture veto both refuse without any of the army walks behind the demand terms,
		/// so the opening — where the answer is "no" for three independent reasons — costs almost
		/// nothing.</para>
		///
		/// <para><paramref name="requireDemand"/> false is the OFF SWITCH and reproduces the pre-2026-09-03
		/// answer verbatim: quota plus a bare affordability floor, no need model, no capture veto.</para></summary>
		public static bool ShouldRequestCenter(
			int centersHeld,
			int desiredCenters,
			int capturableWithinReach,
			long funds,
			int centerCost,
			int tankCost,
			int forwardArmyValue,
			int ammoNeedPerMille,
			int forwardDistanceCells,
			int residualTripCells,
			bool requireDemand)
		{
			if (desiredCenters <= 0)
				return false;

			// Already have (or have coming) as many as we want.
			if (Math.Max(0, centersHeld) >= desiredCenters)
				return false;

			if (!requireDemand)
				return funds >= centerCost;

			// A free one is on offer within reach — capturing it is strictly better than paying 3000.
			if (CaptureCoversDemand(centersHeld, capturableWithinReach, desiredCenters))
				return false;

			if (!AffordableAlongsideCombat(funds, centerCost, tankCost))
				return false;

			// The opportunity cost, which is the decision the user asked for by name: the uptime this
			// Centre returns to the line has to beat the tank the same money buys instead. Strictly
			// greater — a tie goes to the tank, because the tank is combat power now and the Centre is
			// only ever a saving on a journey.
			var value = ForwardResupplyValue(
				forwardArmyValue,
				ammoNeedPerMille,
				TripSavingPerMille(forwardDistanceCells, residualTripCells));

			return value > tankCost;
		}
	}
}
