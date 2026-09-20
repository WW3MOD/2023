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
 * WHERE AN UNPLACED PACKAGE GOES. Replaces DoomsdayCoverageTest, which was retired on 2026-09-20
 * with the map-wide salvo it described.
 *
 * WHAT THE OLD FILE ASSERTED AND WHY NONE OF IT SURVIVES. It was about COVERAGE: "every cell of the
 * playable rectangle is inside some impact's lethal radius", later weakened to "every high-value
 * target and every city member within one warhead of its own centroid is". Both are statements
 * about a salvo aimed at the WHOLE MAP, and there is no such salvo any more -- the ending is two
 * per-side packages of two to six warheads each, and the guarantee that nothing survives is carried
 * entirely by DoomsdayStrike.Annihilate, which is not geometry. Asserting coverage over a
 * three-warhead package would be asserting something false.
 *
 * WHAT IS WORTH DEFENDING INSTEAD, and it is a different KIND of property -- containment and
 * priority rather than reach:
 *   1. Nothing is ever aimed at the firer's own half of the map. THE defect Dead Hand had.
 *   2. Nothing is ever aimed inside the border band.
 *   3. The package is ALWAYS full, whatever the enemy has left standing.
 *   4. Supply Routes outrank concentrations outrank neutral assets, and bigger outranks smaller.
 *   5. It is a pure function of its inputs: same inputs, same aim points, every time.
 *
 * The clustering tests at the foot of the file are carried over unchanged in substance from
 * DoomsdayCoverageTest, because DoomsdayMath.ClusterAssets is the one piece of that pipeline with a
 * second life -- it now runs over one side's actors instead of over every building on the map.
 */

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;

namespace OpenRA.Test
{
	[TestFixture]
	public class FinalExchangeTargetingTest
	{
		// A 60x40 playable rectangle split down the middle: x < 28 is the firer's, x > 32 is the
		// enemy's, and the four columns between are the band. Modelled on a DefconWall region rather
		// than on a line, because the band is what the real classifier has that a bisector does not.
		static readonly Rectangle Bounds = new(1, 1, 60, 40);
		const int OwnSide = 0;
		const int EnemySide = 1;

		// AimPointRadius on the shipped Sarmat is 30c0; a 60-cell-wide test map cannot space six
		// warheads 30 cells apart, so the fixture uses a scaled-down 6 and says so.
		const int Separation = 6;

		static int SideOf(CPos c) { return c.X > 32 ? EnemySide : OwnSide; }

		static bool InBand(CPos c) { return c.X >= 28 && c.X <= 32; }

		static List<CPos> Choose(int count, IReadOnlyList<FinalExchangeAsset> assets, int separation = Separation)
		{
			return FinalExchangeTargeting.Choose(count, Bounds, assets, SideOf, InBand, EnemySide, separation);
		}

		static FinalExchangeAsset Sr(int x, int y) { return new FinalExchangeAsset(new CPos(x, y), FinalExchangeTier.SupplyRoute, 0); }

		static FinalExchangeAsset Cluster(int x, int y, int members) { return new FinalExchangeAsset(new CPos(x, y), FinalExchangeTier.Concentration, members); }

		static FinalExchangeAsset Neutral(int x, int y) { return new FinalExchangeAsset(new CPos(x, y), FinalExchangeTier.NeutralAsset, 0); }

		[Test]
		public void NothingIsEverAimedAtTheFirersOwnHalf()
		{
			// THE DEFECT DEAD HAND HAD, stated as the first property of its replacement. The old salvo
			// enumerated every derrick, Supply Route and city on the map and fired at all of them with
			// no owner, so the side that declined to aim was bombed exactly as hard as its enemy.
			//
			// Every asset here is on the WRONG side, so all of them must be discarded and the whole
			// package must come from the padding -- which is itself drawn only from enemy ground.
			var assets = new List<FinalExchangeAsset>
			{
				Sr(5, 5), Cluster(10, 20, 40), Cluster(3, 30, 12), Neutral(20, 10),
			};

			var aim = Choose(6, assets);
			Assert.That(aim.Count, Is.EqualTo(6));
			foreach (var c in aim)
				Assert.That(SideOf(c), Is.EqualTo(EnemySide), $"{c} is on the firer's own side");
		}

		[Test]
		public void NothingIsEverAimedInsideTheBand()
		{
			var assets = new List<FinalExchangeAsset> { Sr(30, 20), Cluster(29, 10, 50), Neutral(32, 35) };

			var aim = Choose(4, assets);
			Assert.That(aim.Count, Is.EqualTo(4));
			foreach (var c in aim)
				Assert.That(InBand(c), Is.False, $"{c} is inside the border band");
		}

		[Test]
		public void TheSupplyRouteIsAlwaysTheFirstAimPoint()
		{
			// One per player and the thing the whole mod is about. It outranks a concentration of any
			// size, which is why the decoy below is fifty members against the SR's zero weight.
			var assets = new List<FinalExchangeAsset> { Cluster(50, 30, 50), Sr(40, 10), Neutral(45, 20) };

			var aim = Choose(3, assets);
			Assert.That(aim[0], Is.EqualTo(new CPos(40, 10)));
		}

		[Test]
		public void BiggerConcentrationsAreAimedAtFirst()
		{
			var assets = new List<FinalExchangeAsset>
			{
				Cluster(40, 5, 3), Cluster(50, 15, 30), Cluster(45, 30, 11),
			};

			var aim = Choose(3, assets);
			Assert.That(aim[0], Is.EqualTo(new CPos(50, 15)));
			Assert.That(aim[1], Is.EqualTo(new CPos(45, 30)));
			Assert.That(aim[2], Is.EqualTo(new CPos(40, 5)));
		}

		[Test]
		public void TiersOutrankWeightAcrossTierBoundaries()
		{
			// A one-building concentration still beats a neutral derrick, because the tier comparison
			// runs before the weight comparison. Weight only ever ranks WITHIN a tier.
			var assets = new List<FinalExchangeAsset> { Neutral(40, 10), Cluster(50, 30, 1) };

			var aim = Choose(2, assets);
			Assert.That(aim[0], Is.EqualTo(new CPos(50, 30)));
			Assert.That(aim[1], Is.EqualTo(new CPos(40, 10)));
		}

		[Test]
		public void AnAssetTooCloseToOneAlreadyAimedAtIsDropped()
		{
			// The rule the placement overlay states to the player: put a second warhead inside
			// AimPointRadius and it is buying nothing. The machine obeys the same rule.
			var assets = new List<FinalExchangeAsset>
			{
				Cluster(40, 20, 30),
				Cluster(42, 21, 29),   // 2,1 away -- inside the 6-cell separation, dropped
				Cluster(50, 30, 28),
			};

			var aim = Choose(2, assets);
			Assert.That(aim, Is.EqualTo(new[] { new CPos(40, 20), new CPos(50, 30) }));
		}

		[Test]
		public void ThePackageIsFullEvenWhenTheEnemyHasNothingLeft()
		{
			// THE PROPERTY THAT MAKES THE PADDING TIER EXIST. A package is sized from the MAP, so a
			// side down to its last two buildings must still deliver all of it -- otherwise a player
			// who wiped their enemy out would face a smaller reply than one who had not.
			for (var n = 1; n <= 6; n++)
			{
				Assert.That(Choose(n, new List<FinalExchangeAsset>()).Count, Is.EqualTo(n), $"empty asset list, N={n}");
				Assert.That(Choose(n, new List<FinalExchangeAsset> { Sr(40, 20) }).Count, Is.EqualTo(n), $"one asset, N={n}");
			}
		}

		[Test]
		public void PaddingNeverRepeatsACell()
		{
			var aim = Choose(6, new List<FinalExchangeAsset>());
			Assert.That(aim.Distinct().Count(), Is.EqualTo(aim.Count));
		}

		[Test]
		public void PaddingSpreadsRatherThanClumping()
		{
			// Farthest-point sampling, asserted as the property rather than as a coordinate list: no
			// two padded points may be closer together than a quarter of the enemy region's short
			// side. A lattice or a scan-order fill would fail this immediately.
			var aim = Choose(4, new List<FinalExchangeAsset>());

			for (var i = 0; i < aim.Count; i++)
			{
				for (var j = i + 1; j < aim.Count; j++)
				{
					var dx = aim[i].X - aim[j].X;
					var dy = aim[i].Y - aim[j].Y;
					Assert.That((dx * dx) + (dy * dy), Is.GreaterThan(10 * 10),
						$"{aim[i]} and {aim[j]} are clumped");
				}
			}
		}

		[Test]
		public void PaddingIsPlacedAwayFromTheAssetsAlreadyAimedAt()
		{
			// The padding's first pick maximises its distance from what is already chosen, so a single
			// real target in one corner pushes the fill to the opposite one rather than beside it.
			var aim = Choose(2, new List<FinalExchangeAsset> { Sr(35, 3) });

			Assert.That(aim[0], Is.EqualTo(new CPos(35, 3)));
			var dx = aim[1].X - 35;
			var dy = aim[1].Y - 3;
			Assert.That((dx * dx) + (dy * dy), Is.GreaterThan(20 * 20));
		}

		[Test]
		public void ItIsAPureFunctionOfItsInputs()
		{
			// DETERMINISM IS THE WHOLE POINT: this decides where warheads land, so every client must
			// compute the same list. Run the same inputs repeatedly -- a hash-ordered container or an
			// unstable sort anywhere in the pipeline would show up here.
			var assets = new List<FinalExchangeAsset>
			{
				Cluster(40, 20, 7), Cluster(50, 30, 7), Sr(45, 10), Neutral(55, 35), Cluster(38, 38, 7),
			};

			var first = Choose(6, assets);
			for (var i = 0; i < 25; i++)
				Assert.That(Choose(6, assets), Is.EqualTo(first));
		}

		[Test]
		public void EqualWeightsTieBreakOnInputOrderAndNotOnTheSortsPivot()
		{
			// List.Sort is NOT a stable sort. Three same-tier, same-weight assets far enough apart to
			// all survive the separation filter must come out in the order they were handed in, or the
			// answer depends on the runtime's pivot choice -- which is a desync, not a detail.
			var assets = new List<FinalExchangeAsset> { Cluster(40, 5, 9), Cluster(50, 20, 9), Cluster(40, 35, 9) };

			Assert.That(Choose(3, assets), Is.EqualTo(new[] { new CPos(40, 5), new CPos(50, 20), new CPos(40, 35) }));
		}

		[Test]
		public void AZeroSizedPackageAimsAtNothing()
		{
			Assert.That(Choose(0, new List<FinalExchangeAsset> { Sr(40, 20) }), Is.Empty);
			Assert.That(Choose(-1, new List<FinalExchangeAsset> { Sr(40, 20) }), Is.Empty);
		}

		[Test]
		public void AnAssetOutsideTheBoundsIsDiscardedRatherThanAimedAt()
		{
			// The playable rectangle is the only ground a warhead may be aimed at. An actor outside it
			// is a map-editor artefact or a spawned-off-map missile; either way it is not a target.
			var assets = new List<FinalExchangeAsset> { Sr(500, 500), Sr(40, 20) };

			var aim = Choose(1, assets);
			Assert.That(aim, Is.EqualTo(new[] { new CPos(40, 20) }));
		}

		[Test]
		public void ANullClassifierPutsTheWholeMapInPlay()
		{
			// The "no border on this map" case. There are no sides to be on, so refusing every cell
			// would deliver an empty package; the honest answer is the whole playable rectangle.
			var aim = FinalExchangeTargeting.Choose(4, Bounds, new List<FinalExchangeAsset>(), null, null, EnemySide, Separation);
			Assert.That(aim.Count, Is.EqualTo(4));
			foreach (var c in aim)
				Assert.That(Bounds.Contains(c.X, c.Y), Is.True);
		}

		// ==== THE FALLBACK CLASSIFIER ===========================================================

		[Test]
		public void HomeProximityPutsEachSpawnOnItsOwnSide()
		{
			var homes = new List<CPos> { new(5, 20), new(55, 20) };

			Assert.That(FinalExchangeTargeting.HomeProximitySide(homes, new CPos(5, 20)), Is.EqualTo(0));
			Assert.That(FinalExchangeTargeting.HomeProximitySide(homes, new CPos(55, 20)), Is.EqualTo(1));
			Assert.That(FinalExchangeTargeting.HomeProximitySide(homes, new CPos(10, 25)), Is.EqualTo(0));
			Assert.That(FinalExchangeTargeting.HomeProximitySide(homes, new CPos(50, 5)), Is.EqualTo(1));
		}

		[Test]
		public void HomeProximityTiesGoToTheEarliestSeat()
		{
			// A cell exactly equidistant from both homes. Ties must resolve identically on every
			// client, and seat order is the ordering world.Players already guarantees.
			var homes = new List<CPos> { new(0, 0), new(20, 0) };
			Assert.That(FinalExchangeTargeting.HomeProximitySide(homes, new CPos(10, 7)), Is.EqualTo(0));
		}

		[Test]
		public void HomeProximityWithNoHomesAnswersNothingRatherThanGuessing()
		{
			Assert.That(FinalExchangeTargeting.HomeProximitySide(new List<CPos>(), new CPos(3, 3)), Is.EqualTo(-1));
		}

		// ==== CLUSTERING, CARRIED OVER FROM DoomsdayCoverageTest =================================

		[Test]
		public void ClusteringIsOrderStableAndTransitive()
		{
			// A RIBBON: each member within the link distance of the next, none within it of the one
			// two along. Single linkage must join the whole chain; a naive "within distance of the
			// seed" test would cut it into pieces.
			const int LinkCells = 8;
			var ribbon = new List<CPos>();
			for (var i = 0; i < 10; i++)
				ribbon.Add(new CPos(10 + (i * 6), 10));

			var clusters = DoomsdayMath.ClusterAssets(ribbon, LinkCells);
			Assert.That(clusters.Count, Is.EqualTo(1), "Single linkage must join a ribbon into one cluster.");
			Assert.That(clusters[0].Count, Is.EqualTo(ribbon.Count));

			// Members come out sorted, so the caller's centroid is a pure function of the input list.
			Assert.That(clusters[0], Is.Ordered);
		}

		[Test]
		public void ASeparatedGroupIsItsOwnCluster()
		{
			const int LinkCells = 8;
			var split = new List<CPos> { new(10, 10), new(12, 11), new(40, 40), new(41, 42) };

			var clusters = DoomsdayMath.ClusterAssets(split, LinkCells);
			Assert.That(clusters.Count, Is.EqualTo(2));
			Assert.That(clusters.Select(c => c.Count), Is.EqualTo(new[] { 2, 2 }));
		}

		[Test]
		public void ClusteringIsDeterministicAcrossRepeatedRuns()
		{
			const int LinkCells = 8;
			var assets = new List<CPos>();
			var rng = new System.Random(7);
			for (var i = 0; i < 120; i++)
				assets.Add(new CPos(rng.Next(1, 60), rng.Next(1, 40)));

			var first = DoomsdayMath.ClusterAssets(assets, LinkCells).Select(c => string.Join(",", c)).ToList();
			for (var i = 0; i < 10; i++)
				Assert.That(DoomsdayMath.ClusterAssets(assets, LinkCells).Select(c => string.Join(",", c)).ToList(),
					Is.EqualTo(first));
		}

		[Test]
		public void TheCentroidOfAClusterIsInteger()
		{
			var assets = new List<CPos> { new(10, 10), new(12, 10), new(11, 13) };
			var centre = DoomsdayMath.Centroid(assets, new List<int> { 0, 1, 2 });
			Assert.That(centre, Is.EqualTo(new CPos(11, 11)));
		}

		[Test]
		public void AnEmptyAssetListClustersIntoNothing()
		{
			Assert.That(DoomsdayMath.ClusterAssets(new List<CPos>(), 8), Is.Empty);
			Assert.That(DoomsdayMath.ClusterAssets(null, 8), Is.Empty);
		}
	}
}
