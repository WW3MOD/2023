#region Copyright & License Information
/*
 * WW3MOD — supply-transfer arbitration tests.
 *
 * Pins the gesture polarity from the user ruling of 2026-08-30:
 *
 *     "the default action for trucks when ordered to an LC should be to resupply the LC, unless they
 *      are empty then they are themselves resupplied. If we use 'force-move' it could be inverted, so
 *      force move to a LC means it resupplies the truck."
 *
 * The polarity SHIPPED INVERTED before this change — a plain click restocked the truck and only Ctrl
 * delivered — so these are not tests of a fresh feature but of a deliberate reversal, and the four
 * cases the ruling names are asserted by name so the reversal cannot be quietly undone.
 *
 * DisjointByConstruction is the load-bearing one. The two directions are offered by two separate
 * IOrderTargeters at different priorities, and UnitOrderGenerator.OrderForUnit takes the FIRST that
 * matches walking down priority — so if both could ever match, the priority numbers would silently
 * decide the player's supply direction. That exact bug has already been fixed once on this pair.
 *
 * Pure integer/boolean decisions; no world mounted.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class SupplyTransferMathTest
	{
		const int Capacity = 750;

		static SupplyTransferDirection Resolve(bool forceMove, int carried)
		{
			return SupplyTransferMath.ResolveDirection(forceMove, carried, Capacity, true, true);
		}

		// ---- The four cases the ruling names ----

		[Test]
		public void LoadedTruckOnNormalOrderFillsTheCentre()
		{
			Assert.That(Resolve(false, Capacity), Is.EqualTo(SupplyTransferDirection.ToHost));
		}

		[Test]
		public void PartiallyLoadedTruckOnNormalOrderStillFillsTheCentre()
		{
			// "has supply" is the test, not "is full" — a truck that has served anybody is the common
			// case, and gating delivery on a full load is what made the feature unreachable before.
			Assert.That(Resolve(false, 1), Is.EqualTo(SupplyTransferDirection.ToHost));
		}

		[Test]
		public void EmptyTruckOnNormalOrderIsServedInstead()
		{
			Assert.That(Resolve(false, 0), Is.EqualTo(SupplyTransferDirection.ToTruck));
		}

		[Test]
		public void ForceMoveIsServedRegardlessOfLoad()
		{
			Assert.That(Resolve(true, Capacity - 1), Is.EqualTo(SupplyTransferDirection.ToTruck));
			Assert.That(Resolve(true, 1), Is.EqualTo(SupplyTransferDirection.ToTruck));
			Assert.That(Resolve(true, 0), Is.EqualTo(SupplyTransferDirection.ToTruck));
		}

		// ---- No cursor over a no-op ----

		[Test]
		public void ForceMoveOnAFullTruckIsRefusedSoRepairCanHaveTheClick()
		{
			// Nothing left to receive. Refusing is not merely tidy: it is the ONLY way the pre-existing
			// repair gesture stays reachable, because Repairable's targeter sits below both of these at
			// priority 5 and only ever sees clicks that neither direction claimed. Steering this case to
			// Restock instead — as the predecessor of this method did — sent the truck on a drive that
			// transferred nothing and repaired nothing, under an enter cursor that promised service.
			Assert.That(Resolve(true, Capacity), Is.EqualTo(SupplyTransferDirection.None));
		}

		[Test]
		public void AHostThatCannotAbsorbTakesNoDelivery()
		{
			Assert.That(
				SupplyTransferMath.ResolveDirection(false, Capacity, Capacity, hostAbsorbs: false, hostDocks: true),
				Is.EqualTo(SupplyTransferDirection.None));
		}

		[Test]
		public void AHostThatCannotDockServesNobody()
		{
			// A ground crate absorbs but cannot dock a truck; an empty truck clicking one must not
			// resolve to ToTruck, which is the path that assumes an arrival gate.
			Assert.That(
				SupplyTransferMath.ResolveDirection(false, 0, Capacity, hostAbsorbs: true, hostDocks: false),
				Is.EqualTo(SupplyTransferDirection.None));
		}

		[Test]
		public void DisjointByConstruction()
		{
			// Exactly one direction can ever be returned, so no ordering of the two targeters can change
			// which one the player gets. Exhaustive over the axes that feed the decision.
			foreach (var forceMove in new[] { false, true })
				foreach (var carried in new[] { 0, 1, Capacity / 2, Capacity })
					foreach (var absorbs in new[] { false, true })
						foreach (var docks in new[] { false, true })
						{
							var d = SupplyTransferMath.ResolveDirection(
								forceMove, carried, Capacity, absorbs, docks);

							if (d == SupplyTransferDirection.ToHost)
							{
								Assert.That(forceMove, Is.False, "a force-move must never deliver");
								Assert.That(carried, Is.GreaterThan(0), "an empty truck must never deliver");
							}

							if (d == SupplyTransferDirection.ToTruck)
								Assert.That(carried, Is.LessThan(Capacity), "a full truck must never be served");
						}
		}

		// ---- AmountToDeliver: the partial/partial policy lives here and nowhere else ----

		[Test]
		public void DeliversWhatTheCentreHasHeadroomFor()
		{
			Assert.That(SupplyTransferMath.AmountToDeliver(750, 2000, 2250), Is.EqualTo(250));
		}

		[Test]
		public void DeliversTheWholeLoadWhenItFits()
		{
			Assert.That(SupplyTransferMath.AmountToDeliver(750, 0, 2250), Is.EqualTo(750));
		}

		[Test]
		public void DeliversNothingIntoAFullCentre()
		{
			Assert.That(SupplyTransferMath.AmountToDeliver(750, 2250, 2250), Is.EqualTo(0));
		}

		[Test]
		public void NeverCreatesSupplyAndNeverGoesNegative()
		{
			Assert.That(SupplyTransferMath.AmountToDeliver(0, 100, 2250), Is.EqualTo(0));

			// An over-full host (AddSupply is documented as able to exceed TotalSupply) must yield a
			// refusal, not a negative transfer that would credit the truck out of thin air.
			Assert.That(SupplyTransferMath.AmountToDeliver(750, 3000, 2250), Is.EqualTo(0));
		}
	}
}
