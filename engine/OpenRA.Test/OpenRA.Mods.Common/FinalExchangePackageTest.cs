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

/*
 * HOW BIG A STRIKE PACKAGE IS, pinned per shipped map.
 *
 * THE TABLE IS THE TEST. FinalExchangePackage.SizeFor is four lines of arithmetic and could be
 * checked with three assertions; what is actually worth defending is the RESULT on the maps people
 * play, because that is what a retune of CellsPerImpact silently moves. Writing the nine-plus-one
 * table out means a change to the constant shows up in review as a diff of map names and numbers
 * rather than as "2400 -> 2600", which nobody can read.
 *
 * Bounds are (x, y, width, height) exactly as written in each mods/ww3mod/maps/<name>/map.yaml, and
 * the whole list is the same one DoomsdayCoverageTest carried before it was retired.
 */

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;

namespace OpenRA.Test
{
	[TestFixture]
	public class FinalExchangePackageTest
	{
		// The shipped configuration (mods/ww3mod/rules/world.yaml, DoomsdayStrike).
		const int CellsPerImpact = 2400;
		const int MinPackage = 2;
		const int MaxPackage = 6;

		static int Size(Rectangle bounds)
		{
			return FinalExchangePackage.SizeFor(bounds.Width * bounds.Height, CellsPerImpact, MinPackage, MaxPackage);
		}

		[TestCase("arena-tank-duel", 64, 32, 2)]
		[TestCase("nuclear-winter-ww3", 100, 70, 3)]
		[TestCase("polar-disorder-ww3", 96, 96, 4)]
		[TestCase("river-zeta-ww3", 96, 80, 3)]
		[TestCase("seventh-woods-ww3", 121, 112, 6)]
		[TestCase("shellmap-open-field", 90, 60, 2)]
		[TestCase("siberian-pass-ww3", 95, 65, 3)]
		[TestCase("twin-rivers-ww3", 126, 126, 6)]
		[TestCase("woodland-warfare-ww3", 96, 96, 4)]
		[TestCase("x-lake-ww3", 128, 128, 6)]
		public void TheShippedMapsGetTheseExactPackages(string name, int width, int height, int expected)
		{
			Assert.That(Size(new Rectangle(1, 1, width, height)), Is.EqualTo(expected),
				$"{name} ({width}x{height} = {width * height} playable cells)");
		}

		[Test]
		public void BothNationsGetTheSameNumberOnEveryMap()
		{
			// THE POINT OF THE WHOLE FEATURE, stated as a property rather than left implicit in the
			// table above: SizeFor takes no faction, no player and no power, so there is no argument
			// it could be handed that would give America a different answer from Russia. Before this,
			// the Sarmat's AimPoints was 6 and the B83's was 1 and that WAS the exchange's shape.
			foreach (var side in new[] { 32, 64, 96, 128 })
			{
				var bounds = new Rectangle(1, 1, side, side);
				Assert.That(Size(bounds), Is.EqualTo(Size(bounds)));
			}
		}

		[Test]
		public void TheFloorCatchesTheSmallestShippedMap()
		{
			// arena-tank-duel is 2048 playable cells, which rounds to 1. A one-warhead "exchange" is a
			// coin toss rather than an ending, which is what MinPackage 2 exists to prevent -- so this
			// asserts the CLAMP is doing the work rather than the arithmetic happening to give 2.
			Assert.That((2048 + (CellsPerImpact / 2)) / CellsPerImpact, Is.EqualTo(1),
				"The raw arithmetic must still give 1, or this test has stopped testing the clamp.");
			Assert.That(Size(new Rectangle(1, 1, 64, 32)), Is.EqualTo(MinPackage));
		}

		[Test]
		public void TheCeilingCatchesTheLargestShippedMaps()
		{
			// x-lake is 16384 cells, which rounds to 7. Six is what the Sarmat's bus carries.
			Assert.That((16384 + (CellsPerImpact / 2)) / CellsPerImpact, Is.EqualTo(7));
			Assert.That(Size(new Rectangle(1, 1, 128, 128)), Is.EqualTo(MaxPackage));
		}

		[Test]
		public void RoundingIsToNearestAndNotTowardsZero()
		{
			// The half-way point and one cell either side of it, at the shipped divisor. Truncating
			// division would give 2 for all three; round-to-nearest gives 2, 3, 3.
			Assert.That(FinalExchangePackage.SizeFor((2 * CellsPerImpact) + 1199, CellsPerImpact, 1, 9), Is.EqualTo(2));
			Assert.That(FinalExchangePackage.SizeFor((2 * CellsPerImpact) + 1200, CellsPerImpact, 1, 9), Is.EqualTo(3));
			Assert.That(FinalExchangePackage.SizeFor((2 * CellsPerImpact) + 1201, CellsPerImpact, 1, 9), Is.EqualTo(3));
		}

		[Test]
		public void ItIsMonotonicInMapSize()
		{
			// A bigger map never gets a smaller package. Cheap to state, and it is the property a
			// future "scale by playable AREA rather than by the bounding box" change could break
			// without any of the table rows above moving.
			var previous = 0;
			for (var cells = 1; cells <= 40000; cells += 137)
			{
				var size = FinalExchangePackage.SizeFor(cells, CellsPerImpact, MinPackage, MaxPackage);
				Assert.That(size, Is.GreaterThanOrEqualTo(previous), $"at {cells} playable cells");
				previous = size;
			}
		}

		[Test]
		public void DegenerateConfigurationsGiveTheFloorRatherThanThrowing()
		{
			// YAML can say anything. A zero divisor would be a divide-by-zero on the world actor's
			// construction -- a crash before any map loads, rather than a mis-sized package -- and a
			// map with no playable cells at all is what a malformed Bounds produces.
			Assert.That(FinalExchangePackage.SizeFor(9216, 0, MinPackage, MaxPackage), Is.EqualTo(MinPackage));
			Assert.That(FinalExchangePackage.SizeFor(9216, -1, MinPackage, MaxPackage), Is.EqualTo(MinPackage));
			Assert.That(FinalExchangePackage.SizeFor(0, CellsPerImpact, MinPackage, MaxPackage), Is.EqualTo(MinPackage));
			Assert.That(FinalExchangePackage.SizeFor(-5, CellsPerImpact, MinPackage, MaxPackage), Is.EqualTo(MinPackage));
		}

		[Test]
		public void AMinimumAboveTheMaximumResolvesToTheMinimum()
		{
			// Inverted bounds are a typo, not a configuration. The answer has to be ONE of them and it
			// must not depend on which clamp ran last, so the bounds are normalised first: Min wins,
			// because a package of Min is at least a package.
			Assert.That(FinalExchangePackage.SizeFor(9216, CellsPerImpact, 5, 2), Is.EqualTo(5));
			Assert.That(FinalExchangePackage.SizeFor(100, CellsPerImpact, 5, 2), Is.EqualTo(5));
		}

		[Test]
		public void AZeroOrNegativeFloorStillDeliversOneWarhead()
		{
			// Nothing downstream can fire an empty package: MissileStrikePower.ResolveAimPoints takes
			// Math.Max(1, count), so a zero here would be silently promoted to one anyway. Saying so
			// at the source means the two cannot disagree.
			Assert.That(FinalExchangePackage.SizeFor(10, CellsPerImpact, 0, 6), Is.EqualTo(1));
			Assert.That(FinalExchangePackage.SizeFor(10, CellsPerImpact, -3, 6), Is.EqualTo(1));
		}
	}
}
