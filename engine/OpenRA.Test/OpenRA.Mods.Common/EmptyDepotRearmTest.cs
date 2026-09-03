#region Copyright & License Information
/*
 * WW3MOD "an empty Logistics Centre is not a rearm destination" — SupplyHuntMath.HostCanServePool.
 * Pure-math test; no Actor / World.
 *
 * USER REPORT (LC-empty-rearm): "I noticed that units go to the LC even if it is empty, and they just
 * wait there." USER RULING, two halves:
 *   1. "Empty LC should count as no LC, as far as auto-rearming goes. Units evacuate instead if there
 *      is no LC WITH SUPPLIES."
 *   2. "If the supplies runs out, any unit queued to rearm there should cancel that order, and go
 *      somewhere else to rearm or evacuate or whatever their stance makes them do."
 *
 * THE PREDICATE IS AFFORDABILITY, NOT ZERO, and that is the single most important thing this fixture
 * pins. "Empty" as CurrentSupply == 0 is the intuitive reading and it is wrong: the whole band
 * 1 .. batchPrice-1 is stocked-but-useless, because Rearmable.RearmTick (Rearmable.cs:106) and
 * AmmoPool.TryServeBatch both refuse to serve a pool the provider cannot pay for. In this mod the band
 * is where the Logistics Centre SPENDS MOST OF ITS LIFE — an iskander batch is SupplyValue 1500 against
 * the Centre's TotalSupply 2250, so one refill leaves 750 behind — which means a zero-test would have
 * left the reported symptom fully intact for the unit that reported it.
 *
 * WHAT THIS FIXTURE DOES NOT PROVE. These are pure-predicate assertions. That a unit in a running game
 * abandons an in-flight errand and reaches an alternative is the autotest scenario
 * tools/autotest/scenarios/lc-empty-rearm-fallback, which is written but NOT run on this branch.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common.Test
{
	[TestFixture]
	public class EmptyDepotRearmTest
	{
		// The shipped numbers, so every case below is traceable to YAML rather than invented.
		// LOGISTICSCENTER SupplyProvider: TotalSupply 2250 (structures.yaml).
		// iskander (vehicles-russia.yaml:965) AmmoPool@1: Ammo 2, SupplyValue 1500 — two missiles, and
		// one batch costs 1500. So a full Centre buys ONE missile and is left holding 750, which is the
		// depot's normal resting state rather than an edge case.
		const int FullCentre = 2250;
		const int CentreAfterOneMissile = 750;
		const int IskanderBatch = 1500;
		const int IskanderCapacity = 2;

		// ^E3's rifle (infantry.yaml:1238-1244): Ammo 100, ReloadCount 20, SupplyValue 2.
		const int RifleBatch = 2;
		const int RifleCapacity = 100;

		[Test]
		public void AStockedCentreIsAValidDestination()
		{
			Assert.That(
				SupplyHuntMath.HostCanServePool(FullCentre, 0, IskanderCapacity, IskanderBatch), Is.True,
				"a full Logistics Centre must still be worth driving to — the fix must not have blunted " +
				"rearming into never happening");
		}

		[Test]
		public void AnEmptyCentreIsNotAValidDestination()
		{
			// USER RULING 1, as an assertion. This is the case the report names.
			Assert.That(
				SupplyHuntMath.HostCanServePool(0, 0, IskanderCapacity, IskanderBatch), Is.False,
				"an empty LC must count as no LC: it can serve nobody, so treating it as a destination " +
				"is what parked units beside it");
		}

		[Test]
		public void ACentreHoldingLessThanOneBatchIsAlsoInvalid()
		{
			// THE CASE A ZERO-TEST WOULD MISS, and the reason the predicate is affordability. 750 is not
			// a contrived number: it is exactly what one Logistics Centre holds after filling one
			// Iskander, i.e. the depot's normal resting state.
			Assert.That(
				SupplyHuntMath.HostCanServePool(CentreAfterOneMissile, 0, IskanderCapacity, IskanderBatch), Is.False,
				"750 against a 1500 batch is stocked and useless. A CurrentSupply > 0 test admits it, the " +
				"unit drives there, Rearmable.RearmTick declines to serve the pool it cannot pay for, and " +
				"the unit is parked at a depot that will never help it — the reported symptom, at nonzero " +
				"supply");

			// The boundary, both sides. Exactly one batch is affordable; one short is not.
			Assert.That(SupplyHuntMath.HostCanServePool(IskanderBatch, 0, IskanderCapacity, IskanderBatch), Is.True);
			Assert.That(SupplyHuntMath.HostCanServePool(IskanderBatch - 1, 0, IskanderCapacity, IskanderBatch), Is.False);
		}

		[Test]
		public void TheSameResidueCanServeACheaperClient()
		{
			// AFFORDABILITY IS PER CLIENT, not a property of the depot — which is why the rule takes the
			// pool's batch price rather than asking the host whether it "is empty". The 750 residue that
			// cannot touch an Iskander is 375 rifle batches.
			Assert.That(
				SupplyHuntMath.HostCanServePool(CentreAfterOneMissile, 0, RifleCapacity, RifleBatch), Is.True,
				"a residue too small for a missile is still plenty of bullets — a global is-empty flag on " +
				"the depot would wrongly turn infantry away from a Centre that can serve them");
		}

		[Test]
		public void AFullPoolMakesEvenARichDepotPointless()
		{
			// The other half of the predicate. Without it a loaded unit counts a depot as a reason to
			// stay put, which is the same stall by a different route.
			Assert.That(
				SupplyHuntMath.HostCanServePool(FullCentre, IskanderCapacity, IskanderCapacity, IskanderBatch), Is.False,
				"a unit that wants no rounds has nothing to gain from any depot, however full");

			// Partially loaded still wants rounds.
			Assert.That(SupplyHuntMath.HostCanServePool(FullCentre, RifleCapacity - 1, RifleCapacity, RifleBatch), Is.True);
		}

		[Test]
		public void TheInFlightQuestionIsTheSameQuestionAsTheDispatchOne()
		{
			// USER RULING 2, as arithmetic. A unit is dispatched to a Centre holding 2250, and the Centre
			// is drained to 750 by someone else while it walks. Resupply.HostStillWorthReaching and
			// SeekSupplyProvider.TargetValid both re-ask THIS function every tick of the approach, so the
			// answer must flip from true to false on the same numbers that made the dispatch valid.
			//
			// The two sites must name one function. A stricter exit test than the dispatch test would end
			// every errand on the tick it began — the trap EssentialAmmoTest pins in its other form.
			Assert.That(
				SupplyHuntMath.HostCanServePool(FullCentre, 0, IskanderCapacity, IskanderBatch), Is.True,
				"precondition: this dispatch was valid when it was made");

			Assert.That(
				SupplyHuntMath.HostCanServePool(CentreAfterOneMissile, 0, IskanderCapacity, IskanderBatch), Is.False,
				"once drained below a batch the errand must be abandoned in flight. Before this branch " +
				"Resupply never re-asked: the unit walked the whole way, arrived, was correctly refused, " +
				"and stood there");
		}

		[Test]
		public void NoThrashAcrossTheAffordabilityBoundary()
		{
			// THE OSCILLATION GUARD, and the reason no cooldown was added. Abandoning hands the unit back
			// to AutoRearmIfDry, which re-picks with ChooseAffordableResupplier — the SAME predicate that
			// made it leave. So the depot it walked away from is excluded until it can genuinely serve,
			// and a trickle of supply that does not reach one batch cannot re-attract it.
			for (var trickle = 0; trickle < IskanderBatch; trickle++)
				Assert.That(
					SupplyHuntMath.HostCanServePool(trickle, 0, IskanderCapacity, IskanderBatch), Is.False,
					$"a depot holding {trickle} must stay invalid — any supply level that re-admits the " +
					"unit below one batch price is a shuttle, since arrival cannot then serve it either");

			// And it does re-admit once the depot can actually pay, which is correct rather than thrash:
			// the unit gets a batch out of the trip.
			Assert.That(SupplyHuntMath.HostCanServePool(IskanderBatch, 0, IskanderCapacity, IskanderBatch), Is.True);
		}

		[Test]
		public void ZeroPriceAndZeroCapacityDoNotInvertTheRule()
		{
			// Degenerate authoring, pinned so a future YAML edit cannot silently turn every depot into a
			// valid destination or every unit into a permanent seeker. SupplyValue 1 is real in this mod
			// (crew.yaml:37, several aircraft pools); 0 is not, but nothing rejects it.
			Assert.That(SupplyHuntMath.HostCanServePool(0, 0, 1, 0), Is.True,
				"a free batch is affordable at any stock level — the comparison is >=, and a zero-priced " +
				"pool is served by an empty depot");

			Assert.That(SupplyHuntMath.HostCanServePool(FullCentre, 0, 0, IskanderBatch), Is.False,
				"a zero-capacity pool never wants rounds, so it can never justify a trip");
		}
	}
}
