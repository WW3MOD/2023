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
 * DOOMSDAY coverage. The user's requirement is absolute — "always in such a fashion that it destroys
 * all units on map" — so the thing under test is a GUARANTEE, not a tendency, and these tests are
 * written to try to break it rather than to demonstrate it once.
 *
 * The property: after the Dead Hand pipeline runs (asset-driven aim points, minimum separation,
 * bounded jitter, then a fill pass), EVERY cell of the playable rectangle is within LethalRadius of
 * some impact, and so is every enumerated strategic asset.
 *
 * The adversarial part is the jitter. A test that draws one random layout and checks it proves almost
 * nothing, so JitterCannotUncoverAnything below does not sample: it displaces every aim point directly
 * AWAY from the cell that is worst for it, which is the actual worst case the RNG could ever produce,
 * and asserts the cover still holds.
 */

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;

namespace OpenRA.Test
{
	[TestFixture]
	public class DoomsdayCoverageTest
	{
		// The ten shipped maps, as of main @ 6e5721ae. Bounds are (x, y, width, height) exactly as
		// written in each mods/ww3mod/maps/<name>/map.yaml.
		static readonly (string Name, Rectangle Bounds)[] ShippedMaps =
		{
			("arena-tank-duel", new Rectangle(1, 1, 64, 32)),
			("nuclear-winter-ww3", new Rectangle(1, 1, 100, 70)),
			("polar-disorder-ww3", new Rectangle(1, 1, 96, 96)),
			("river-zeta-ww3", new Rectangle(1, 1, 96, 80)),
			("seventh-woods-ww3", new Rectangle(1, 1, 121, 112)),
			("shellmap-open-field", new Rectangle(1, 1, 90, 60)),
			("siberian-pass-ww3", new Rectangle(1, 1, 95, 65)),
			("twin-rivers-ww3", new Rectangle(1, 1, 126, 126)),
			("woodland-warfare-ww3", new Rectangle(1, 1, 96, 96)),
			("x-lake-ww3", new Rectangle(1, 1, 128, 128)),
		};

		// The shipped configuration (mods/ww3mod/rules/world.yaml, DoomsdayStrike).
		const int LethalCells = 20;
		const int JitterCells = 4;
		const int MinSeparationCells = 12;
		const int CityLinkCells = 8;
		const int CityMinBuildings = 4;
		const int WarheadsPerCity = 2;

		static int Effective => DoomsdayMath.EffectiveRadius(LethalCells, JitterCells);

		/// <summary>
		/// The mode's placement pipeline, in the same order DoomsdayStrike.BuildSalvo runs it, minus the
		/// engine types. Returns the final aim points, unjittered — the jitter is applied by the callers
		/// that care, so each test can choose between "as drawn" and "worst possible draw".
		/// </summary>
		static List<CPos> BuildAimPoints(Rectangle bounds, IReadOnlyList<CPos> cityAssets, IReadOnlyList<CPos> highValue)
		{
			var clusters = DoomsdayMath.ClusterAssets(cityAssets, CityLinkCells);

			var candidates = new List<CPos>();
			foreach (var c in clusters)
				if (c.Count >= CityMinBuildings)
					candidates.AddRange(DoomsdayMath.CityAimPoints(cityAssets, c, WarheadsPerCity, MinSeparationCells, Effective));

			candidates.AddRange(highValue);

			foreach (var c in clusters)
				if (c.Count < CityMinBuildings)
					candidates.Add(DoomsdayMath.Centroid(cityAssets, c));

			var kept = DoomsdayMath.MinSeparationFilter(candidates, MinSeparationCells)
				.Select(i => candidates[i]).ToList();

			var spacing = DoomsdayMath.GridSpacing(LethalCells, JitterCells);
			var uncovered = DoomsdayMath.UncoveredCells(bounds, kept, Effective);
			kept.AddRange(DoomsdayMath.FillPoints(bounds, uncovered, spacing));

			return kept;
		}

		/// <summary>A plausible asset layout: a few clustered "cities" plus scattered lone structures.</summary>
		static (List<CPos> City, List<CPos> HighValue) SyntheticAssets(Rectangle bounds, int seed)
		{
			var rng = new System.Random(seed);
			var city = new List<CPos>();
			var high = new List<CPos>();

			for (var c = 0; c < 3; c++)
			{
				var cx = bounds.Left + rng.Next(bounds.Width);
				var cy = bounds.Top + rng.Next(bounds.Height);
				for (var i = 0; i < 12; i++)
					city.Add(new CPos(
						System.Math.Clamp(cx + rng.Next(-5, 6), bounds.Left, bounds.Right - 1),
						System.Math.Clamp(cy + rng.Next(-5, 6), bounds.Top, bounds.Bottom - 1)));
			}

			for (var i = 0; i < 12; i++)
				high.Add(new CPos(bounds.Left + rng.Next(bounds.Width), bounds.Top + rng.Next(bounds.Height)));

			return (city, high);
		}

		[Test]
		public void EveryCellOfEveryShippedMapIsCovered()
		{
			foreach (var (name, bounds) in ShippedMaps)
			{
				var (city, high) = SyntheticAssets(bounds, name.Length * 7919);
				var aim = BuildAimPoints(bounds, city, high);

				var uncovered = DoomsdayMath.UncoveredCells(bounds, aim, Effective);
				Assert.That(uncovered, Is.Empty,
					$"{name} ({bounds.Width}x{bounds.Height}): {uncovered.Count} cells outside every lethal radius, " +
					$"first at {(uncovered.Count > 0 ? uncovered[0].ToString() : "-")}, with {aim.Count} impacts.");
			}
		}

		[Test]
		public void EveryStrategicAssetIsCovered()
		{
			// This is the one the user actually asked for: "making sure that all strategic targets are
			// within range of a blast". Cheap, and distinct from whole-map coverage — an asset sitting in
			// a corner the fill pass happens to reach is covered by accident, not by targeting.
			foreach (var (name, bounds) in ShippedMaps)
			{
				var (city, high) = SyntheticAssets(bounds, name.Length * 7919);
				var aim = BuildAimPoints(bounds, city, high);

				foreach (var asset in city.Concat(high))
				{
					var covered = aim.Any(p => DoomsdayMath.WithinRadius(asset, p, Effective));
					Assert.That(covered, Is.True, $"{name}: strategic asset at {asset} is outside every lethal radius.");
				}
			}
		}

		[Test]
		public void JitterCannotUncoverAnything()
		{
			// THE ADVERSARIAL CASE, and the reason the placement is done against EffectiveRadius rather
			// than LethalRadius. For each cell, take the impact that covers it and push that impact
			// directly AWAY from the cell by the full jitter bound — the worst displacement the RNG can
			// produce for that pairing. The cell must still be inside the LETHAL radius.
			//
			// This is stronger than sampling draws: it does not depend on the RNG at all.
			foreach (var (name, bounds) in ShippedMaps)
			{
				var (city, high) = SyntheticAssets(bounds, name.Length * 7919);
				var aim = BuildAimPoints(bounds, city, high);

				for (var y = bounds.Top; y < bounds.Bottom; y++)
				{
					for (var x = bounds.Left; x < bounds.Right; x++)
					{
						var cell = new CPos(x, y);
						var best = aim.Min(p => DoomsdayMath.DistanceSquared(cell, p));

						// Worst-case post-jitter distance: the nearest impact, displaced away by the bound.
						var worst = DoomsdayMath.Isqrt(best) + JitterCells;
						Assert.That(worst, Is.LessThanOrEqualTo(LethalCells),
							$"{name}: cell {cell} is {DoomsdayMath.Isqrt(best)} from its nearest impact; " +
							$"a full {JitterCells}-cell jitter would put it {worst} out, past the {LethalCells}-cell lethal radius.");
					}
				}
			}
		}

		[Test]
		public void DiscJitterNeverExceedsItsBound()
		{
			// The magnitude bound the coverage argument leans on. Swept over every angle the WAngle
			// resolution admits and every radius sample, so this is exhaustive rather than sampled.
			for (var bound = 0; bound <= 8; bound++)
			{
				for (var angle = 0; angle < 1024; angle++)
				{
					for (var r = 0; r <= bound; r++)
					{
						var j = DoomsdayMath.DiscJitter(angle, r, bound);
						var mag = (long)j.X * j.X + (long)j.Y * j.Y;
						Assert.That(mag, Is.LessThanOrEqualTo((long)bound * bound),
							$"DiscJitter(angle {angle}, r {r}, bound {bound}) = ({j.X},{j.Y}), magnitude^2 {mag} > {bound * bound}.");
					}
				}
			}
		}

		[Test]
		public void GridSpacingSatisfiesItsOwnHalfDiagonalBound()
		{
			// The derivation in GridSpacing is s <= sqrt(2) * (radius - jitter). Re-check it in integers:
			// the half-diagonal of a spacing-sized square, plus the jitter, must fit inside the radius.
			for (var radius = 2; radius <= 40; radius++)
			{
				for (var jitter = 0; jitter < radius; jitter++)
				{
					var s = DoomsdayMath.GridSpacing(radius, jitter);
					var halfDiagSq = 2L * (s / 2) * (s / 2);
					var halfDiag = DoomsdayMath.Isqrt(halfDiagSq);
					Assert.That(halfDiag + jitter, Is.LessThanOrEqualTo(radius),
						$"radius {radius}, jitter {jitter}: spacing {s} has half-diagonal {halfDiag}, which overruns.");
				}
			}
		}

		[Test]
		public void MinSeparationIsHonouredBetweenTargetedImpacts()
		{
			// "Never detonating too many too close." Fill points are excluded: they are laid on a lattice
			// whose spacing is a coverage constraint, and on a map where the lethal radius is large
			// relative to the separation the two can legitimately disagree. The targeted salvo — what the
			// viewer reads — is what has to be spread out.
			foreach (var (name, bounds) in ShippedMaps)
			{
				var (city, high) = SyntheticAssets(bounds, name.Length * 7919);
				var clusters = DoomsdayMath.ClusterAssets(city, CityLinkCells);

				var candidates = new List<CPos>();
				foreach (var c in clusters)
					if (c.Count >= CityMinBuildings)
						candidates.AddRange(DoomsdayMath.CityAimPoints(city, c, WarheadsPerCity, MinSeparationCells, Effective));

				candidates.AddRange(high);

				var kept = DoomsdayMath.MinSeparationFilter(candidates, MinSeparationCells)
					.Select(i => candidates[i]).ToList();

				for (var i = 0; i < kept.Count; i++)
				{
					for (var j = i + 1; j < kept.Count; j++)
					{
						var d = DoomsdayMath.DistanceSquared(kept[i], kept[j]);
						Assert.That(d, Is.GreaterThanOrEqualTo((long)MinSeparationCells * MinSeparationCells),
							$"{name}: impacts {kept[i]} and {kept[j]} are closer than the {MinSeparationCells}-cell minimum.");
					}
				}
			}
		}

		[Test]
		public void ClusteringIsOrderStableAndTransitive()
		{
			// Determinism at the head of the pipeline. A ribbon of buildings each within the link distance
			// of the next is ONE city, however long the chain, and the cluster contents must not depend on
			// which end the scan started from.
			var ribbon = new List<CPos>();
			for (var i = 0; i < 20; i++)
				ribbon.Add(new CPos(10 + i * CityLinkCells, 10));

			var clusters = DoomsdayMath.ClusterAssets(ribbon, CityLinkCells);
			Assert.That(clusters.Count, Is.EqualTo(1), "A chain of link-distance-spaced buildings should form one city.");
			Assert.That(clusters[0].Count, Is.EqualTo(20));

			// Two groups further apart than the link distance stay separate.
			var split = new List<CPos> { new(10, 10), new(12, 10), new(60, 60), new(62, 60) };
			var splitClusters = DoomsdayMath.ClusterAssets(split, CityLinkCells);
			Assert.That(splitClusters.Count, Is.EqualTo(2));
			Assert.That(splitClusters[0], Is.EqualTo(new List<int> { 0, 1 }));
			Assert.That(splitClusters[1], Is.EqualTo(new List<int> { 2, 3 }));
		}

		[Test]
		public void CitySplitKeepsEveryMemberCovered()
		{
			// Two warheads per city must not be pushed so far apart that a building on the far edge falls
			// out of both. The spread is capped by (effectiveRadius - clusterReach) for exactly this.
			var rng = new System.Random(4242);
			for (var trial = 0; trial < 200; trial++)
			{
				var members = new List<CPos>();
				var cx = rng.Next(20, 100);
				var cy = rng.Next(20, 100);
				var spread = rng.Next(1, 25);
				for (var i = 0; i < rng.Next(2, 20); i++)
					members.Add(new CPos(cx + rng.Next(-spread, spread + 1), cy + rng.Next(-spread, spread + 1)));

				var indices = Enumerable.Range(0, members.Count).ToList();
				var points = DoomsdayMath.CityAimPoints(members, indices, WarheadsPerCity, MinSeparationCells, Effective);

				foreach (var m in members)
				{
					// Only meaningful when the cluster itself fits inside one warhead — a city wider than
					// the lethal radius is covered by the fill pass, not by its own two warheads.
					var centre = DoomsdayMath.Centroid(members, indices);
					if (!DoomsdayMath.WithinRadius(m, centre, Effective))
						continue;

					Assert.That(points.Any(p => DoomsdayMath.WithinRadius(m, p, Effective)), Is.True,
						$"trial {trial}: city member {m} fell outside both of its own warheads.");
				}
			}
		}
	}
}
