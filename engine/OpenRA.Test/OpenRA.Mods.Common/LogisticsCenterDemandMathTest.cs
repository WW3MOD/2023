#region Copyright & License Information
/*
 * WW3MOD @experimental — Logistics Center / supply-truck purchase-gate tests.
 *
 * Pins USER RULING 2026-09-03, reported from a watched bot-vs-bot game: "They need to be bought when there
 * is a need... We definitely do not need them at the start of the game... there are already a neutral LC on
 * each side, so the bots should instead capture the exisiting one."
 *
 * The four cases the ruling names, each with its own test below:
 *   1. the no-need OPENING          — must not buy (OpeningWithNobodyForwardRefusesTheCentre)
 *   2. a soldier BELOW HALF ammo    — must buy a truck (SoldierBelowHalfAmmoBuysTheFirstTruck)
 *   3. a capturable Centre on offer — must not buy (CapturableCentreWithinReachVetoesThePurchase)
 *   4. late game, army FAR from SR  — may buy (LateGameForwardArmyFarFromTheSupplyRouteBuysTheCentre)
 *
 * Prices are the shipped ones so the arithmetic is checkable against the mod: LOGISTICSCENTER/LCCV 3000
 * (structures.yaml:422), abrams 2500 (vehicles-america.yaml:456). ResidualTripCells 6 is the module default.
 *
 * Both directions are pinned throughout, because "does not buy at the start" is satisfied just as happily by
 * a predicate stuck on False that never buys at all. Every refusal test has a mirror that must BUY.
 *
 * Pure integer/boolean decisions; no world mounted.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class LogisticsCenterDemandMathTest
	{
		const int CenterCost = 3000;
		const int TankCost = 2500;
		const int Residual = 6;

		static bool Buy(
			int centersHeld = 0,
			int capturable = 0,
			long funds = 20000,
			int forwardValue = 0,
			int needPerMille = 0,
			int forwardCells = 0,
			bool requireDemand = true)
		{
			return LogisticsCenterDemandMath.ShouldRequestCenter(
				centersHeld, 1, capturable, funds, CenterCost, TankCost,
				forwardValue, needPerMille, forwardCells, Residual, requireDemand);
		}

		// ================= 1. THE OPENING =================

		[Test]
		public void OpeningWithNobodyForwardRefusesTheCentre()
		{
			// THE REPORTED BUG. Rich (20000 against a 3000 Centre), quota unfilled, nothing capturable —
			// which is exactly what the shipped gate tested, and it bought. Every unit is on the beachhead
			// at full ammo, so there is no forward customer at all: value 0, and 0 does not beat a tank.
			Assert.That(Buy(), Is.False);
		}

		[Test]
		public void AFullyArmedArmyStandingForwardStillRefusesTheCentre()
		{
			// "It is expensive and has no purpose in the beginning because all units are fully armed."
			// A big army genuinely far out, but nobody needs ammo ⇒ no round-trip is pending ⇒ nothing to
			// shorten. This is the factor that must not be substitutable by the other two.
			Assert.That(Buy(forwardValue: 30000, needPerMille: 0, forwardCells: 40), Is.False);
		}

		[Test]
		public void AStarvingArmyPARKEDONTheSupplyRouteRefusesTheCentre()
		{
			// The mirror of the above, and the reason the model multiplies rather than scores: everyone is
			// dry, but they are standing on the Supply Route already. A forward depot shortens a journey
			// they are not making. Distance 0 ⇒ TripSaving 0 ⇒ value 0.
			Assert.That(Buy(forwardValue: 30000, needPerMille: 1000, forwardCells: 0), Is.False);
		}

		// ================= 3. CAPTURE BEATS PURCHASE =================

		[Test]
		public void CapturableCentreWithinReachVetoesThePurchase()
		{
			// "there are already a neutral LC on each side, so the bots should instead capture the
			// exisiting one." Demand is otherwise overwhelming — this must still refuse, because a free
			// Centre is strictly better than a 3000-credit one.
			Assert.That(Buy(capturable: 1, forwardValue: 30000, needPerMille: 800, forwardCells: 40), Is.False);
		}

		[Test]
		public void TheVetoIsQuotaRELATIVENotABlanketBan()
		{
			// One capturable covers a desire of one, but not a desire of two. Pinning this stops the veto
			// degenerating into "never buy a Centre while any neutral one exists anywhere".
			Assert.That(LogisticsCenterDemandMath.CaptureCoversDemand(0, 1, 1), Is.True);
			Assert.That(LogisticsCenterDemandMath.CaptureCoversDemand(0, 1, 2), Is.False);

			// One already held plus one capturable covers two.
			Assert.That(LogisticsCenterDemandMath.CaptureCoversDemand(1, 1, 2), Is.True);
		}

		// ================= 4. THE LATE-GAME BUY =================

		[Test]
		public void LateGameForwardArmyFarFromTheSupplyRouteBuysTheCentre()
		{
			// "it is only really useful when we have a lot of money and units, and we need a forward
			// resupply point far from our SR." 12000 credits of army, 40 cells out, 60% depleted.
			// TripSaving = 1000*(40-6)/40 = 850. Value = 12000*600*850/1e6 = 6120 > 2500. Buys.
			Assert.That(Buy(forwardValue: 12000, needPerMille: 600, forwardCells: 40), Is.True);
		}

		[Test]
		public void TheSameNeedWithNoCapturableCentreIsWhatDistinguishesItFromCaseThree()
		{
			// Guards against a gate that refuses for the RIGHT answer but the WRONG reason: identical
			// inputs to the veto test except capturable, and the answer flips. If this ever fails together
			// with the veto test, the veto is not what is doing the work.
			Assert.That(Buy(capturable: 0, forwardValue: 30000, needPerMille: 800, forwardCells: 40), Is.True);
			Assert.That(Buy(capturable: 1, forwardValue: 30000, needPerMille: 800, forwardCells: 40), Is.False);
		}

		// ================= THE OPPORTUNITY COST =================

		[Test]
		public void ATieGoesToTheTank()
		{
			// "some kind of logic needs to decide that the LC is worth more than a tank at that moment" —
			// WORTH MORE, so the comparison is strict. 5000 army, 100% dry, 12 cells out:
			// TripSaving = 1000*(12-6)/12 = 500; value = 5000*1000*500/1e6 = 2500, exactly a tank. Refuse:
			// the tank is combat power now, the Centre is only ever a saving on a journey.
			Assert.That(
				LogisticsCenterDemandMath.ForwardResupplyValue(5000, 1000, LogisticsCenterDemandMath.TripSavingPerMille(12, Residual)),
				Is.EqualTo(TankCost));
			Assert.That(Buy(forwardValue: 5000, needPerMille: 1000, forwardCells: 12), Is.False);

			// One credit of army over the line and it buys — so the boundary is the tank price, not an
			// unreachable bar that would make the feature dead.
			Assert.That(Buy(forwardValue: 5100, needPerMille: 1000, forwardCells: 12), Is.True);
		}

		[Test]
		public void MoneyThatCannotAlsoCoverATankRefuses()
		{
			// "not crowding out combat power": affordable means the Centre AND a tank afterwards, 5500.
			// 5000 buys the Centre outright and would have, under the shipped MinCashToRequest of 3000.
			Assert.That(Buy(funds: 5000, forwardValue: 30000, needPerMille: 800, forwardCells: 40), Is.False);
			Assert.That(Buy(funds: 5500, forwardValue: 30000, needPerMille: 800, forwardCells: 40), Is.True);

			Assert.That(LogisticsCenterDemandMath.AffordableAlongsideCombat(5499, CenterCost, TankCost), Is.False);
			Assert.That(LogisticsCenterDemandMath.AffordableAlongsideCombat(5500, CenterCost, TankCost), Is.True);
		}

		[Test]
		public void TripSavingIsZeroInsideTheCentresOwnCatchment()
		{
			// A Centre standing a residual walk from the line saves nothing to units already that close.
			Assert.That(LogisticsCenterDemandMath.TripSavingPerMille(0, Residual), Is.EqualTo(0));
			Assert.That(LogisticsCenterDemandMath.TripSavingPerMille(6, Residual), Is.EqualTo(0));
			Assert.That(LogisticsCenterDemandMath.TripSavingPerMille(12, Residual), Is.EqualTo(500));
			Assert.That(LogisticsCenterDemandMath.TripSavingPerMille(40, Residual), Is.EqualTo(850));
		}

		[Test]
		public void ValueIsZeroIfAnyFactorIsZeroAndDoesNotOverflow()
		{
			Assert.That(LogisticsCenterDemandMath.ForwardResupplyValue(0, 1000, 1000), Is.EqualTo(0));
			Assert.That(LogisticsCenterDemandMath.ForwardResupplyValue(30000, 0, 1000), Is.EqualTo(0));
			Assert.That(LogisticsCenterDemandMath.ForwardResupplyValue(30000, 1000, 0), Is.EqualTo(0));

			// A six-figure forward army multiplies to ~1e11 on the intermediate, which overflows int if the
			// arithmetic is not widened. Full need, full saving ⇒ the value IS the army value.
			Assert.That(LogisticsCenterDemandMath.ForwardResupplyValue(2000000, 1000, 1000), Is.EqualTo(2000000));
		}

		// ================= QUOTA AND THE OFF SWITCH =================

		[Test]
		public void QuotaStillBindsAndTheOffSwitchReproducesThePreviousAnswer()
		{
			// Already holding one: refused before any demand term is consulted.
			Assert.That(Buy(centersHeld: 1, forwardValue: 30000, needPerMille: 800, forwardCells: 40), Is.False);

			// requireDemand false is the documented off switch — quota plus a bare affordability floor,
			// which is precisely the gate that shipped and bought at t≈6 s.
			Assert.That(Buy(requireDemand: false), Is.True);
			Assert.That(Buy(funds: 2999, requireDemand: false), Is.False);
		}

		// ================= 2. THE SUPPLY TRUCK BAR =================
		// The truck gate itself (SupplyPrecedenceMath.RefuseResupplyBuy) already shipped on 2026-08-17 and
		// is unchanged. What changes is the BAR fed to it for the first truck: the user's "below half ammo"
		// against a service threshold of 0.05. These pin the composition of the two.

		const float FirstTruckBar = 0.5f;
		const float ServiceBar = 0.05f;

		static bool RefusesTruck(int ammo, int current, float bar, bool heldFirstTruck = false, bool fleetShort = true)
		{
			var need = ResupplyDemand.UnitNeed(new[] { (ammo, current, 1) });
			return SupplyPrecedenceMath.RefuseResupplyBuy(fleetShort, heldFirstTruck, ResupplyDemand.MeetsThreshold(need, bar));
		}

		[Test]
		public void SoldierBelowHalfAmmoBuysTheFirstTruck()
		{
			// "When some units are about to run out of ammo we buy one." 40 of 100 rounds ⇒ need 0.6.
			Assert.That(RefusesTruck(100, 40, FirstTruckBar), Is.False);

			// Exactly half is AT the bar and buys — MeetsThreshold is >=, and "below half ammo" reads as
			// "no more than half left".
			Assert.That(RefusesTruck(100, 50, FirstTruckBar), Is.False);
		}

		[Test]
		public void ASoldierWhoFiredAFewRoundsNoLongerBuysATruck()
		{
			// THE DELTA, and the reason the opening truck was bought at all. 95 of 100 rounds is need 0.05
			// — meaningful to SupplyProvider's service bar, and therefore enough to buy a truck under the
			// shipped threshold. Under the opening bar it is not.
			Assert.That(RefusesTruck(100, 95, ServiceBar), Is.False, "the shipped bar buys here — this is the regression");
			Assert.That(RefusesTruck(100, 95, FirstTruckBar), Is.True);
		}

		[Test]
		public void AFullyArmedArmyBuysNoTruckAtEitherBar()
		{
			Assert.That(RefusesTruck(100, 100, FirstTruckBar), Is.True);
			Assert.That(RefusesTruck(100, 100, ServiceBar), Is.True);
		}

		[Test]
		public void TheTighterBarAppliesToTheOPENINGONLY()
		{
			// Once the latch has closed the standing reserve governs alone and a fleet shortfall buys
			// freely — the 2026-08-17 split is untouched by this change. If this fails, the new bar has
			// leaked past the opening and is throttling the mid-match reserve.
			Assert.That(RefusesTruck(100, 100, FirstTruckBar, heldFirstTruck: true, fleetShort: true), Is.False);

			// ...and with the fleet at target, still nothing is bought.
			Assert.That(RefusesTruck(100, 100, FirstTruckBar, heldFirstTruck: true, fleetShort: false), Is.True);
		}
	}
}
