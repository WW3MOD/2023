import io


def edit(p, pairs):
    s = io.open(p, encoding='utf-8-sig').read()
    for old, new in pairs:
        assert old in s, (p, old[:90])
        s = s.replace(old, new, 1)
    io.open(p, 'w', encoding='utf-8', newline='').write(s)
    print('ok', p)


# ---- the Choose summary now describes six tiers, not "tiers and padding" ----
edit('engine/OpenRA.Mods.Common/Traits/World/FinalExchangeTargeting.cs', [
('''			/// <para>THE SEPARATION RULE APPLIES TO THE ASSET TIERS AND NOT TO THE PADDING, and the
			/// asymmetry is deliberate. An asset closer than <paramref name="minSeparationCells"/> to
			/// something already aimed at is DROPPED, because a second warhead there buys nothing --
			/// that is the same argument <see cref="MissileStrikePowerInfo.AimPointRadius"/> makes to the
			/// player during placement, and the caller passes that very number. Padding cannot drop
			/// anything, because its job is to fill the package; farthest-point sampling maximises the
			/// separation it can achieve rather than refusing the points it cannot.</para>
			///
			/// <para>Returns fewer than <paramref name="count"/> ONLY when the enemy side contains no
			/// cells at all to spread over -- a degenerate classifier, or a border that put the whole
			/// map on one side. The caller decides what to do about that; this does not invent cells.</para>''',
'''			/// <para>SEPARATION IS A PREFERENCE, NOT A VETO, and that distinction is the 2026-09-20
			/// fix. An asset closer than <paramref name="minSeparationCells"/> to something already
			/// aimed at is passed over on the FIRST walk, because a second warhead there buys nothing
			/// -- the same argument <see cref="MissileStrikePowerInfo.AimPointRadius"/> makes to the
			/// player during placement, and the caller passes that very number. But if the package is
			/// still short once every asset has been considered, those passed-over assets are taken
			/// ANYWAY, in the same priority order, before a single cell is invented. A warhead on the
			/// enemy's second-biggest concentration at twenty cells beats one on an empty map corner
			/// at fifty.</para>
			///
			/// <para>Returns fewer than <paramref name="count"/> ONLY when the enemy side contains no
			/// cells at all -- a degenerate classifier, or a border that put the whole map on one
			/// side. The caller decides what to do about that; this does not invent cells.</para>'''),
])

# ---- the four new cases ----
NEW = '''
		// ==== WHAT HAPPENS WHEN SEPARATION EMPTIES THE LIST (2026-09-20) ====================
		// Run 260920_155623 put a 750 kt warhead on cell (1,1), the corner of a 64x32 map, and it
		// was not a bug in the sampler: the Sarmat's AimPointRadius is 30 cells, the enemy half was
		// about 40x32, so EVERY asset sat inside 30 cells of the first and was dropped. Farthest-
		// point padding then did exactly its job. These four pin the fix.

		[Test]
		public void AClusteredEnemyBaseYieldsRealTargetsAndNeverACorner()
		{
			// THE SHIPPED CASE, at shipped-ish proportions: a base of five buildings inside ten
			// cells of each other, against a separation of 20. The first walk takes ONE of them and
			// drops the other four; the package must come out of that base rather than off the map
			// edge.
			var baseCells = new[] { new CPos(45, 18), new CPos(47, 20), new CPos(44, 22), new CPos(49, 17), new CPos(46, 25) };
			var assets = new List<FinalExchangeAsset>
			{
				Cluster(baseCells[0].X, baseCells[0].Y, 40),
				Cluster(baseCells[1].X, baseCells[1].Y, 30),
				Cluster(baseCells[2].X, baseCells[2].Y, 20),
				Cluster(baseCells[3].X, baseCells[3].Y, 10),
				Cluster(baseCells[4].X, baseCells[4].Y, 5),
			};

			var aim = Choose(4, assets, separation: 20);
			Assert.That(aim.Count, Is.EqualTo(4));

			foreach (var c in aim)
			{
				Assert.That(baseCells, Contains.Item(c),
					$"{c} is not one of the enemy's own buildings -- the package escaped to open ground");
				Assert.That(c.X, Is.LessThan(Bounds.Right - 2).And.GreaterThan(Bounds.Left + 1),
					$"{c} is on the map edge");
			}

			// AND IN PRIORITY ORDER THROUGHOUT. The first walk takes the biggest; the second walk
			// takes the rest by weight, not by whatever order they were dropped in.
			Assert.That(aim[0], Is.EqualTo(baseCells[0]), "the biggest concentration must still be aimed at first");
			Assert.That(aim[1], Is.EqualTo(baseCells[1]), "the second walk must keep the weight ordering");
		}

		[Test]
		public void ASingleAssetIsRingedRatherThanAbandoned()
		{
			// The enemy has exactly ONE distinct thing standing and the package wants four. Tier 4
			// has nothing to give, so the remaining three go on a ring at the separation radius --
			// close enough to read as one strike on that target, rather than three corners.
			var aim = Choose(4, new List<FinalExchangeAsset> { Sr(48, 20) }, separation: 8);

			Assert.That(aim.Count, Is.EqualTo(4));
			Assert.That(aim[0], Is.EqualTo(new CPos(48, 20)));

			for (var i = 1; i < aim.Count; i++)
			{
				var dx = aim[i].X - 48;
				var dy = aim[i].Y - 20;
				var d2 = (dx * dx) + (dy * dy);

				// On the ring, within the rounding a cell-space circle costs.
				Assert.That(d2, Is.InRange(6 * 6, 10 * 10),
					$"{aim[i]} is not on the 8-cell ring around the only target");
			}

			Assert.That(aim.Distinct().Count(), Is.EqualTo(aim.Count));
		}

		[Test]
		public void ASideWithNothingStandingStillGetsAFullSpread()
		{
			// The one case farthest-point padding is still for: no assets at all, so there is no
			// ring to hang anything off and open ground is the honest answer.
			var aim = Choose(4, new List<FinalExchangeAsset>(), separation: 20);

			Assert.That(aim.Count, Is.EqualTo(4));
			Assert.That(aim.Distinct().Count(), Is.EqualTo(4));

			for (var i = 0; i < aim.Count; i++)
				for (var j = i + 1; j < aim.Count; j++)
				{
					var dx = aim[i].X - aim[j].X;
					var dy = aim[i].Y - aim[j].Y;
					Assert.That((dx * dx) + (dy * dy), Is.GreaterThan(10 * 10),
						$"{aim[i]} and {aim[j]} are clumped");
				}
		}

		[Test]
		public void EveryPointOfEveryPaddingTierIsOnTheEnemyHalf()
		{
			// THE INVARIANT THAT MUST SURVIVE ALL SIX TIERS, swept across package sizes and asset
			// counts so each tier is reached in turn: 0 assets exercises the spread, 1 the ring, and
			// 5 clustered ones the dropped-asset walk.
			foreach (var n in new[] { 2, 3, 4, 6 })
			{
				foreach (var assetCount in new[] { 0, 1, 5 })
				{
					var assets = new List<FinalExchangeAsset>();
					for (var i = 0; i < assetCount; i++)
						assets.Add(Cluster(44 + i, 18 + (2 * i), 10 - i));

					var aim = Choose(n, assets, separation: 20);
					Assert.That(aim.Count, Is.EqualTo(n), $"N={n} assets={assetCount}");

					foreach (var c in aim)
					{
						Assert.That(SideOf(c), Is.EqualTo(EnemySide), $"{c} (N={n} assets={assetCount})");
						Assert.That(InBand(c), Is.False, $"{c} (N={n} assets={assetCount})");
						Assert.That(Bounds.Contains(c.X, c.Y), Is.True, $"{c} (N={n} assets={assetCount})");
					}
				}
			}
		}

		[Test]
'''

edit('engine/OpenRA.Test/OpenRA.Mods.Common/FinalExchangeTargetingTest.cs', [
('''		[Test]
		public void ThePackageIsFullEvenWhenTheEnemyHasNothingLeft()''',
 NEW + '''		public void ThePackageIsFullEvenWhenTheEnemyHasNothingLeft()'''),
])
