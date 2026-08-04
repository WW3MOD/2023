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
	/// Contract for SupplyProvider.InAuraRange — the single range predicate every site in the
	/// trait uses (target eligibility, the Hunt-mode move-toward decision, and the delivery gate
	/// in ResupplyTarget). The delivery gate is the one that was missing: selection could hand
	/// ResupplyTarget an out-of-aura actor (the Hunt branch scans the whole map) and GiveAmmo
	/// fired regardless of distance. These pin the boundary so a regression breaks here.
	/// </summary>
	[TestFixture]
	public class SupplyAuraRangeTest
	{
		static readonly WDist Range = new WDist(5120);   // 5c0, the TRUK/SUPPLYCACHE aura
		static readonly WPos Origin = new WPos(10240, 10240, 0);

		static WPos Offset(int dx, int dy, int dz = 0)
		{
			return Origin + new WVec(dx, dy, dz);
		}

		[Test]
		public void UnitAtTheProviderIsInRange()
		{
			Assert.That(SupplyProvider.InAuraRange(Origin, Origin, Range), Is.True);
		}

		[Test]
		public void BoundaryIsInclusive()
		{
			// Exactly Range away counts as in-aura, matching FindActorsInCircle's <= filter.
			Assert.That(SupplyProvider.InAuraRange(Origin, Offset(5120, 0), Range), Is.True);
			Assert.That(SupplyProvider.InAuraRange(Origin, Offset(0, -5120), Range), Is.True);
		}

		[Test]
		public void OneUnitPastTheBoundaryIsOut()
		{
			// The floor() in WVec.HorizontalLength used to swallow this sub-unit overshoot;
			// the squared comparison rejects it exactly.
			Assert.That(SupplyProvider.InAuraRange(Origin, Offset(5121, 0), Range), Is.False);
			Assert.That(SupplyProvider.InAuraRange(Origin, Offset(-5121, 0), Range), Is.False);
		}

		[Test]
		public void DistanceIsEuclideanNotChebyshev()
		{
			// A diagonal target 4c0 out on each axis is ~5.66c away — outside a 5c0 aura even
			// though both axis components are individually well inside it.
			Assert.That(SupplyProvider.InAuraRange(Origin, Offset(4096, 4096), Range), Is.False);

			// 3-4-5 triangle: exactly on the circle, so still served.
			Assert.That(SupplyProvider.InAuraRange(Origin, Offset(3072, 4096), Range), Is.True);
		}

		[Test]
		public void HeightIsIgnored()
		{
			// Horizontal distance only — a soldier on a cliff directly above is still in the aura,
			// and altitude can never rescue a target that is horizontally out of reach.
			Assert.That(SupplyProvider.InAuraRange(Origin, Offset(0, 0, 100000), Range), Is.True);
			Assert.That(SupplyProvider.InAuraRange(Origin, Offset(6000, 0, -100000), Range), Is.False);
		}

		[Test]
		public void MapWideHuntTargetIsRejectedByTheDeliveryGate()
		{
			// The concrete defect: FindNeedsResupplyTarget (Hunt branch) picks a flagged unit
			// anywhere on the map. Before the gate, ResupplyTarget delivered ammo to it from
			// across the map while merely starting to drive over.
			Assert.That(SupplyProvider.InAuraRange(Origin, Offset(60 * 1024, 40 * 1024), Range), Is.False);
		}

		[Test]
		public void ZeroRangeServesOnlyTheExactPosition()
		{
			Assert.That(SupplyProvider.InAuraRange(Origin, Origin, WDist.Zero), Is.True);
			Assert.That(SupplyProvider.InAuraRange(Origin, Offset(1, 0), WDist.Zero), Is.False);
		}

		[Test]
		public void IsSymmetric()
		{
			var far = Offset(3000, 4200);
			Assert.That(
				SupplyProvider.InAuraRange(Origin, far, Range),
				Is.EqualTo(SupplyProvider.InAuraRange(far, Origin, Range)));
		}
	}
}
