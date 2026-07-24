#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// Contract for SupplyProvider.CountsAsEmptyResidue — the "counts as empty" predicate.
	/// A truck holding a residue no reachable unit can use must count as empty (so it
	/// evacuates instead of parking at the front). Eligibility mirrors the live rearm path:
	/// a unit can be served if the provider can afford ONE batch (currentSupply >= a needy
	/// pool's SupplyValue). The trait's live scan feeds this same rule; here it is exercised
	/// as pure logic so regressions break a unit test rather than a playtest.
	/// Each element is one reachable unit's SupplyValue costs for its non-full ammo pools.
	/// </summary>
	[TestFixture]
	public class SupplyResidueTest
	{
		static IReadOnlyList<int> Unit(params int[] needyPoolCosts) => needyPoolCosts;

		static bool CountsAsEmpty(int supply, params IReadOnlyList<int>[] reachable)
			=> SupplyProvider.CountsAsEmptyResidue(supply, reachable);

		[Test]
		public void DrainedProviderAlwaysCountsAsEmpty()
		{
			// No supply at all → empty regardless of who is nearby.
			Assert.That(CountsAsEmpty(0, Unit(50)), Is.True);
			Assert.That(CountsAsEmpty(-1), Is.True);
		}

		[Test]
		public void ResidueTooSmallForAnyNeedyUnitCountsAsEmpty()
		{
			// 40 supply left; the only nearby soldier needs a 65-cost pool → cannot afford
			// even one batch for anyone → unusable residue.
			Assert.That(CountsAsEmpty(40, Unit(65)), Is.True);
		}

		[Test]
		public void ResidueThatCanStillServeSomeoneIsNotEmpty()
		{
			// 40 supply; a soldier has a cheap 30-cost pool we can afford → still usable.
			Assert.That(CountsAsEmpty(40, Unit(65), Unit(30)), Is.False);
		}

		[Test]
		public void AffordableIfAnyOfAUnitsPoolsIsAffordable()
		{
			// One unit, two needy pools (missile 65, rifle 20). We can afford the rifle
			// batch → the unit is serviceable → not residue.
			Assert.That(CountsAsEmpty(40, Unit(65, 20)), Is.False);
		}

		[Test]
		public void NoDemandIsNotResidue()
		{
			// Supply remains but nobody in reach needs anything (empty demand lists).
			// That is usable supply waiting for customers, not an unusable residue.
			Assert.That(CountsAsEmpty(200, Unit(), Unit()), Is.False);
			Assert.That(CountsAsEmpty(200), Is.False);
		}

		[Test]
		public void ExactlyAffordableBatchIsServiceable()
		{
			// currentSupply == pool cost → one batch is affordable → serviceable.
			Assert.That(CountsAsEmpty(65, Unit(65)), Is.False);
		}

		[Test]
		public void OneUnaffordableUnitAmongEmptyDemandCountsAsEmpty()
		{
			// A full unit (no needy pools) plus one soldier we can't afford → residue.
			Assert.That(CountsAsEmpty(10, Unit(), Unit(65)), Is.True);
		}
	}
}
