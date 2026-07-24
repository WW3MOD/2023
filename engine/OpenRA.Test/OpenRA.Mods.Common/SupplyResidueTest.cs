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

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// Contract for SupplyProvider.ResidueVerdict — the exact predicate the live scan calls
	/// (UpdateTarget) to drive the residueUnusable latch. Inputs mirror what a greatest-need
	/// scan produces: whether a serviceable unit cleared MinNeedThreshold (a best target was
	/// picked), and whether an unaffordable needy unit is in reach. This pins the real latch
	/// rule — including the mixed case where a near-full affordable unit does NOT keep a
	/// residue "usable" — so regressions break a unit test rather than a playtest.
	/// </summary>
	[TestFixture]
	public class SupplyResidueTest
	{
		[Test]
		public void DrainedProviderCountsAsEmpty()
		{
			// No supply at all → empty regardless of nearby demand.
			Assert.That(SupplyProvider.ResidueVerdict(0, serviceableNeedyPresent: false, unaffordableNeedyPresent: false), Is.True);
			Assert.That(SupplyProvider.ResidueVerdict(-5, serviceableNeedyPresent: true, unaffordableNeedyPresent: true), Is.True);
		}

		[Test]
		public void ServiceableUnitMakesResidueUsable()
		{
			// A reachable unit we can afford met the need threshold → not residue, keep serving.
			Assert.That(SupplyProvider.ResidueVerdict(40, serviceableNeedyPresent: true, unaffordableNeedyPresent: false), Is.False);
			Assert.That(SupplyProvider.ResidueVerdict(40, serviceableNeedyPresent: true, unaffordableNeedyPresent: true), Is.False);
		}

		[Test]
		public void UnaffordableDemandWithNoServiceableUnitIsUnusableResidue()
		{
			// Demand exists but we can't afford a batch for anyone → evacuate.
			Assert.That(SupplyProvider.ResidueVerdict(40, serviceableNeedyPresent: false, unaffordableNeedyPresent: true), Is.True);
		}

		[Test]
		public void MixedNearFullAffordablePlusUnaffordableNeedyCountsAsEmpty()
		{
			// The case the old pure predicate got backwards: the only affordable unit is
			// near-full (below MinNeedThreshold, so no best target was picked → serviceable
			// = false), while a needy unit we can't afford is also present. Live latches true;
			// this test locks that in.
			Assert.That(SupplyProvider.ResidueVerdict(40, serviceableNeedyPresent: false, unaffordableNeedyPresent: true), Is.True);
		}

		[Test]
		public void NoDemandLeavesLatchUnchanged()
		{
			// Supply remains, nobody needs anything in reach → indeterminate (null). The caller
			// leaves the latch as-is: a waiting truck stays waiting; an evacuating one stays so.
			Assert.That(SupplyProvider.ResidueVerdict(200, serviceableNeedyPresent: false, unaffordableNeedyPresent: false), Is.Null);
		}

		[Test]
		public void ServiceableWinsOverUnaffordableWhenBothPresent()
		{
			// If we can serve someone, the residue is usable even if another unit is
			// unaffordable — serviceable takes precedence.
			Assert.That(SupplyProvider.ResidueVerdict(1, serviceableNeedyPresent: true, unaffordableNeedyPresent: true), Is.False);
		}
	}
}
