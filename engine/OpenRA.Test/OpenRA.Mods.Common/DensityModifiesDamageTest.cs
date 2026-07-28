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
	/// Pins the pure threshold-selection behind PIPELINE item 26 phase 2 forest cover
	/// (DensityModifiesDamage). The trait itself is coupled to Actor/Map, so — following the
	/// project idiom — the reviewable arithmetic (highest threshold &lt;= windowed density wins)
	/// is a static pure function and pinned here. Mirrors the shipped ^Infantry ladder.
	/// </summary>
	[TestFixture]
	public class DensityModifiesDamageTest
	{
		// The exact ladder wired onto ^Infantry in infantry.yaml.
		static readonly Dictionary<int, int> Ladder = new() { { 15, 94 }, { 30, 88 }, { 50, 80 } };

		[TestCase(0, 100, TestName = "Open ground → full damage")]
		[TestCase(14, 100, TestName = "Below the lowest threshold → full damage")]
		[TestCase(15, 94, TestName = "Light cover threshold exactly → 94%")]
		[TestCase(20, 94, TestName = "Between light and moderate → still 94%")]
		[TestCase(30, 88, TestName = "Moderate cover threshold → 88%")]
		[TestCase(49, 88, TestName = "Just below deep threshold → 88%")]
		[TestCase(50, 80, TestName = "Deep forest threshold → 80%")]
		[TestCase(200, 80, TestName = "Very deep forest saturates at the top tier → 80%")]
		public void LadderMatchesShippedValues(int windowedDensity, int expectedPercent)
		{
			Assert.That(DensityModifiesDamage.SelectModifier(Ladder, windowedDensity), Is.EqualTo(expectedPercent));
		}

		[Test(Description = "Selection is order-independent — an unsorted dictionary yields the same tier.")]
		public void OrderIndependent()
		{
			var shuffled = new Dictionary<int, int> { { 50, 80 }, { 15, 94 }, { 30, 88 } };
			Assert.That(DensityModifiesDamage.SelectModifier(shuffled, 40), Is.EqualTo(88));
			Assert.That(DensityModifiesDamage.SelectModifier(shuffled, 55), Is.EqualTo(80));
		}

		[Test(Description = "An empty ladder never modifies damage.")]
		public void EmptyLadderIsFullDamage()
		{
			Assert.That(DensityModifiesDamage.SelectModifier(new Dictionary<int, int>(), 999), Is.EqualTo(100));
		}

		[Test(Description = "Damage percentage never increases as density grows (cover only ever helps).")]
		public void MonotonicNonIncreasing()
		{
			var prev = 100;
			for (var d = 0; d <= 300; d++)
			{
				var pct = DensityModifiesDamage.SelectModifier(Ladder, d);
				Assert.That(pct, Is.LessThanOrEqualTo(prev), $"cover got worse at density {d}");
				prev = pct;
			}
		}
	}
}
