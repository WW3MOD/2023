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
 * WHERE A FORWARD-DEPLOYED PACKAGE LANDS RELATIVE TO THE DEFCON 3 BORDER.
 *
 * Forward Deployment advances a package 35% of the way to the enemy; the derived border sits at
 * 50%, the midpoint. That LOOKS like fifteen percent of clear air, and on a big map it is -- but
 * the package has a radius and the band has a thickness, and neither scales with the map. The
 * clearance in cells is
 *
 *     0.15 * separation  -  OuterSupportRadius  -  HalfWidth
 *
 * which for the motorized package (outer radius 7) and the shipped one-cell half-width goes
 * NEGATIVE below about 53 cells of spawn separation. arena-tank-duel's spawns are 52 apart.
 *
 * THIS FILE IS THE ARITHMETIC OF THAT OVERLAP AND NOTHING ELSE. It pins, for arena-tank-duel's
 * real numbers, that the band and the annulus genuinely intersect -- so the placement filter added
 * to SpawnStartingUnits has something to filter, and stays pinned if the advance percentage, the
 * half-width or the package radius is ever retuned into or out of collision.
 *
 * IT ALSO PINS THE TWO CASES SelectDeploymentCenter's UNFILTERED RESCAN TURNS ON: that a derived
 * LINE border never leaves the retreat ladder with nothing (so the rescan is inert on all ten
 * shipped maps), and that a REGION border can (so it is not dead code). The rescan's rationale was
 * first written about the line and that was geometrically wrong -- a three-column band cannot cover
 * a fifteen-column annulus at any separation. The swept test is what caught it.
 *
 * WHAT IT CANNOT COVER, stated rather than hidden: the filter itself. SpawnStartingUnits needs a
 * World, a Map and a Locomotor, none of which OpenRA.Test can construct -- the same constraint
 * DefconWallTest's header records. The filter is verified by the scenario
 * tools/autotest/scenarios/test-forward-deploy-clears-band, which reads the terrain under every
 * unit the trait actually placed -- but only probabilistically: the annulus and the band overlap in
 * ONE cell of 68 there, so a pre-fix run misses it about three times in four. The scenario asserts
 * the SHAPE of the overlap for that reason, and TheOverlapIsExactlyOneCellOfSixtyEight is where
 * those two integers are fixed.
 */

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class ForwardDeploymentBandOverlapTest
	{
		const int Cell = 1024;
		const int HalfCell = 512;

		// arena-tank-duel, mods/ww3mod/maps/arena-tank-duel/map.yaml: MapSize 66,34 and two spawns
		// facing each other down row 16, 52 cells apart.
		static readonly CPos UsaHome = new CPos(6, 16);
		static readonly CPos RussiaHome = new CPos(58, 16);
		const int Separation = 52;

		// world.yaml's DefconWall block: DeriveFromSpawns with the C# HalfWidth default of 1024.
		const int HalfWidth = 1024;
		const int ExtendCells = 512;

		// The motorized package, world.yaml StartingUnits@Motorized_america / _russia. The trait asks
		// FindTilesInAnnulus for InnerSupportRadius + 1 .. OuterSupportRadius.
		const int InnerSupportRadius = 5;
		const int OuterSupportRadius = 7;

		static long CentreOf(int cell)
		{
			return (cell * Cell) + HalfCell;
		}

		/// <summary>The border the trait derives, built exactly as DefconWall.ResolveBorder builds it.</summary>
		static DefconWallGeometry DerivedBorder(CPos a, CPos b)
		{
			// Group 0 and group 1: two alliance groups of one player each, which is what a 1v1 gives.
			var (start, end) = DefconWallGeometry.BisectorOfSides(
				new[] { (0, a), (1, b) }, ExtendCells);

			return new DefconWallGeometry(
				CentreOf(start.X), CentreOf(start.Y), CentreOf(end.X), CentreOf(end.Y), HalfWidth);
		}

		static bool InBand(DefconWallGeometry geometry, int cellX, int cellY)
		{
			return geometry.IsInWallBand(CentreOf(cellX), CentreOf(cellY));
		}

		[Test]
		public void ArenaTankDuelDerivesABandOverColumns31To33()
		{
			Assert.That(RussiaHome.X - UsaHome.X, Is.EqualTo(Separation),
				"The fixtures below are arena-tank-duel's; its two spawns are 52 cells apart.");

			var geometry = DerivedBorder(UsaHome, RussiaHome);

			Assert.That(geometry.IsDegenerate, Is.False,
				"Two distinct homes in two alliance groups must derive a line.");

			// Midpoint of 6 and 58 is column 32, and a 1024 half-width reaches one whole cell either
			// side of a cell centre -- so the band is three columns wide, not one.
			for (var x = 31; x <= 33; x++)
				Assert.That(InBand(geometry, x, 16), Is.True,
					$"Column {x} should be inside the derived band.");

			Assert.That(InBand(geometry, 30, 16), Is.False, "Column 30 is clear ground on USA's side.");
			Assert.That(InBand(geometry, 34, 16), Is.False, "Column 34 is clear ground on Russia's side.");
		}

		[Test]
		public void TheMotorizedAnnulusReachesTheBandFromBothSpawns()
		{
			var geometry = DerivedBorder(UsaHome, RussiaHome);

			// 35% of 52 cells is 18.2, and the integer arithmetic in AdvancedCenter truncates to 18.
			var usaCentre = ForwardDeploymentGeometry.AdvancedCenter(
				UsaHome, RussiaHome, ForwardDeploymentGeometry.DefaultAdvancePercent);
			var russiaCentre = ForwardDeploymentGeometry.AdvancedCenter(
				RussiaHome, UsaHome, ForwardDeploymentGeometry.DefaultAdvancePercent);

			Assert.That(usaCentre, Is.EqualTo(new CPos(24, 16)), "USA's full advance is 18 cells east of x 6.");
			Assert.That(russiaCentre, Is.EqualTo(new CPos(40, 16)), "Russia's full advance is 18 cells west of x 58.");

			// The annulus reaches OuterSupportRadius cells from the centre, so these two columns hold
			// real candidate cells -- and both are in the band. THIS IS THE BUG, in its arithmetic
			// form: the outer ring and the border occupy the same ground.
			var usaReach = usaCentre.X + OuterSupportRadius;
			var russiaReach = russiaCentre.X - OuterSupportRadius;

			Assert.That(usaReach, Is.EqualTo(31));
			Assert.That(russiaReach, Is.EqualTo(33));

			Assert.That(InBand(geometry, usaReach, 16), Is.True,
				"USA's outer annulus ring reaches x 31, which is the band's first column.");
			Assert.That(InBand(geometry, russiaReach, 16), Is.True,
				"Russia's outer annulus ring reaches x 33, which is the band's last column.");
		}

		[Test]
		public void TheClearanceIsNegativeAtArenaTankDuelsSeparation()
		{
			// The margin from the header, in cells: 15% of the separation, less the package radius,
			// less the band's half-width. Negative means the two overlap.
			var margin = ((15 * Separation) / 100) - OuterSupportRadius - (HalfWidth / Cell);

			Assert.That(margin, Is.LessThan(0),
				"arena-tank-duel is inside the range where the package and the band collide.");
		}

		[Test]
		public void TwoMoreCellsOfSeparationClearTheBand()
		{
			// 54 rather than 53: the midpoint is an integer cell, so an odd separation truncates the
			// border half a cell back toward the near spawn and 53 still collides. The threshold is
			// therefore 54, not the 53 the continuous arithmetic suggests -- which is exactly why the
			// filter is a per-cell question asked of the real geometry rather than a clamp on 35%.
			var farHome = new CPos(UsaHome.X + 54, UsaHome.Y);
			var geometry = DerivedBorder(UsaHome, farHome);

			var centre = ForwardDeploymentGeometry.AdvancedCenter(
				UsaHome, farHome, ForwardDeploymentGeometry.DefaultAdvancePercent);

			Assert.That(InBand(geometry, centre.X + OuterSupportRadius, 16), Is.False,
				"At 54 cells of separation the outer ring stops short of the band.");
		}

		/// <summary>
		/// The cells SpawnStartingUnits would score at one retreat step: the package's support annulus,
		/// enumerated the way Map.FindTilesInAnnulus does. MapGrid.CreateTilesByDistance buckets a cell
		/// by ceil(sqrt(dx^2 + dy^2)), so buckets lo..hi are exactly (lo-1)^2 &lt; d^2 &lt;= hi^2.
		/// </summary>
		static List<CPos> Annulus(CPos centre, int lo, int hi)
		{
			var cells = new List<CPos>();
			for (var dy = -hi; dy <= hi; dy++)
				for (var dx = -hi; dx <= hi; dx++)
				{
					var d2 = (dx * dx) + (dy * dy);
					if (d2 == 0)
						continue;

					var bucket = Exts.ISqrt(d2, Exts.ISqrtRoundMode.Ceiling);
					if (bucket >= lo && bucket <= hi)
						cells.Add(centre + new CVec(dx, dy));
				}

			return cells;
		}

		/// <summary>
		/// How many annulus cells the border leaves usable, at every step of the retreat ladder. This is
		/// SelectDeploymentCenter's score with the terrain term removed -- so zero here means zero there
		/// on open ground, which is the condition the unfiltered rescan exists for.
		/// </summary>
		static int[] LegalCellsPerStep(CPos home, CPos enemyHome, DefconWallGeometry geometry)
		{
			var side = geometry.SideOf(CentreOf(home.X), CentreOf(home.Y));
			var steps = ForwardDeploymentGeometry.DefaultRetreatSteps;
			var scores = new int[steps + 1];

			for (var step = steps; step >= 0; step--)
			{
				var advance = ForwardDeploymentGeometry.AdvanceAtStep(
					ForwardDeploymentGeometry.DefaultAdvancePercent, steps, step);
				var centre = ForwardDeploymentGeometry.AdvancedCenter(home, enemyHome, advance);

				scores[step] = Annulus(centre, InnerSupportRadius + 1, OuterSupportRadius)
					.Count(c => !geometry.IsBeyond(side, CentreOf(c.X), CentreOf(c.Y)));
			}

			return scores;
		}

		[Test]
		public void TheOverlapIsExactlyOneCellOfSixtyEight()
		{
			// THE SCENARIO'S FIXTURES, AND THE REASON IT IS NOT A RELIABLE RED ARM. The annulus and
			// the band meet in ONE cell out of 68, so reverting the filter and rerunning
			// test-forward-deploy-clears-band leaves twenty units drawing from 68 cells and missing
			// that one about three runs in four. These two integers are what the scenario's geometry
			// leg asserts instead, and they are deterministic.
			var geometry = DerivedBorder(UsaHome, RussiaHome);
			var side = geometry.SideOf(CentreOf(UsaHome.X), CentreOf(UsaHome.Y));
			var centre = ForwardDeploymentGeometry.AdvancedCenter(
				UsaHome, RussiaHome, ForwardDeploymentGeometry.DefaultAdvancePercent);

			var cells = Annulus(centre, InnerSupportRadius + 1, OuterSupportRadius);
			var forbidden = cells.Where(c => geometry.IsBeyond(side, CentreOf(c.X), CentreOf(c.Y))).ToList();

			Assert.That(cells.Count, Is.EqualTo(68), "The motorized annulus at radius 6..7 is 68 cells.");
			Assert.That(forbidden.Count, Is.EqualTo(1), "Exactly one annulus cell is behind the border.");
			Assert.That(forbidden[0], Is.EqualTo(new CPos(31, 16)),
				"The one overlapping cell is the band's first column on the package's own row.");
		}

		[Test]
		public void ALineBorderNeverLeavesTheLadderWithNothing()
		{
			// THE RESCAN IN SelectDeploymentCenter CANNOT FIRE ON THE DERIVED LINE, and that is worth
			// pinning because it is what makes the guard inert on all ten shipped maps rather than a
			// second behaviour nobody sees. The reasoning is that the band is THREE columns wide while
			// the annulus is fifteen: a vertical band can shave the forward edge off an annulus but
			// never cover it, and the ladder's step 0 sits at home, where the whole rear half of the
			// ring is on the player's own side by construction. Swept rather than argued.
			for (var separation = 2; separation <= 60; separation += 2)
			{
				var enemy = new CPos(UsaHome.X + separation, UsaHome.Y);
				var geometry = DerivedBorder(UsaHome, enemy);
				if (geometry.IsDegenerate)
					continue;

				var scores = LegalCellsPerStep(UsaHome, enemy, geometry);
				Assert.That(scores.Min(), Is.GreaterThan(0),
					$"At {separation} cells of separation a step of the ladder scored zero against a " +
					"LINE border, which the rescan's reasoning says cannot happen.");
			}
		}

		[Test]
		public void ARegionBorderCanForbidEveryCellOfTheAnnulus()
		{
			// THE CASE THE RESCAN ACTUALLY EXISTS FOR, and it is the REGION path rather than the line.
			// A region border is an arbitrary cell set, so it can enclose a spawn in a component
			// SMALLER THAN THE PACKAGE'S OWN ANNULUS -- and then every candidate cell at every step of
			// the ladder is either border or another component, i.e. IsBeyond, and the score is zero
			// all the way down. SelectDeploymentCenter's bestScore starts at -1 and the ladder walks
			// from the FULL advance downward, so nothing ever displaces the deepest centre and without
			// the rescan the whole package would be placed past a closed border.
			//
			// AND IT TAKES A ONE-CELL POCKET, WHICH IS WORTH STATING PLAINLY: the ladder's centres sit
			// 0, 2, 5, 8, 11 and 14 cells out, and a ring of radius 6..7 around ANY of them sweeps back
			// through a pocket of any appreciable size, leaving that step a non-zero score. So this
			// guard covers a pathological authored region and nothing a shipped map can produce -- the
			// swept line test above is the other half of that statement. It is four lines and it
			// reproduces the pre-change answer exactly when it fires, which is the trade being made.
			const int Size = 60;
			var home = new CPos(10, 10);

			var blocked = new List<CPos>();
			for (var dx = -1; dx <= 1; dx++)
				for (var dy = -1; dy <= 1; dy++)
					if (dx != 0 || dy != 0)
						blocked.Add(home + new CVec(dx, dy));

			var region = new DefconWallRegion(0, 0, Size, Size, blocked, _ => true);
			Assert.That(region.IsDegenerate, Is.False, "The ring must separate home from the rest.");

			var side = region.SideOf(home);
			Assert.That(side, Is.Not.EqualTo(DefconWallRegion.Unlabelled), "Home must be in a component.");
			Assert.That(region.SideOf(new CPos(40, 40)), Is.Not.EqualTo(side),
				"...and it must not be the same component as the open map.");

			var steps = ForwardDeploymentGeometry.DefaultRetreatSteps;
			var enemy = new CPos(50, 10);

			for (var step = steps; step >= 0; step--)
			{
				var advance = ForwardDeploymentGeometry.AdvanceAtStep(
					ForwardDeploymentGeometry.DefaultAdvancePercent, steps, step);
				var centre = ForwardDeploymentGeometry.AdvancedCenter(home, enemy, advance);

				var legal = Annulus(centre, InnerSupportRadius + 1, OuterSupportRadius)
					.Count(c => !region.IsBeyond(side, c));

				Assert.That(legal, Is.Zero,
					$"Step {step} left {legal} legal cell(s); the all-zero case the rescan guards " +
					"against is not being reproduced.");
			}
		}

		[Test]
		public void TheFarSideOfTheBorderIsForbiddenToo()
		{
			// The filter rejects IsBeyond, not merely IsInBand, and this is why: half of USA's
			// annulus at x 24 would otherwise be legal ground on RUSSIA's side of a closed border.
			var geometry = DerivedBorder(UsaHome, RussiaHome);
			var usaSide = geometry.SideOf(CentreOf(UsaHome.X), CentreOf(UsaHome.Y));
			var russiaSide = geometry.SideOf(CentreOf(RussiaHome.X), CentreOf(RussiaHome.Y));

			Assert.That(usaSide, Is.Not.EqualTo(DefconWallGeometry.NoSide), "USA's home must resolve to a side.");
			Assert.That(russiaSide, Is.EqualTo(-usaSide), "The two homes must land in opposite half-planes.");

			// Every column the band covers, plus everything past it, is beyond for USA.
			for (var x = 31; x <= 38; x++)
				Assert.That(geometry.IsBeyond(usaSide, CentreOf(x), CentreOf(16)), Is.True,
					$"Column {x} must be beyond the border for USA.");

			// And the mirror image for Russia, whose own annulus at x 40 reaches back to x 33.
			for (var x = 26; x <= 33; x++)
				Assert.That(geometry.IsBeyond(russiaSide, CentreOf(x), CentreOf(16)), Is.True,
					$"Column {x} must be beyond the border for Russia.");

			// The home sides stay legal, or the filter would reject the whole annulus and the search
			// would step all the way back to the spawn point.
			Assert.That(geometry.IsBeyond(usaSide, CentreOf(30), CentreOf(16)), Is.False);
			Assert.That(geometry.IsBeyond(russiaSide, CentreOf(34), CentreOf(16)), Is.False);
		}
	}
}
