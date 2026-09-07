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
 * DOOMSDAY coverage — AND THE PROPERTY UNDER TEST CHANGED ON 2026-09-07. Read this before trusting
 * anything below, because the old version of this file asserted the opposite of what it asserts now
 * and the difference is deliberate.
 *
 * WHAT IT USED TO SAY. "The user's requirement is absolute — 'always in such a fashion that it
 * destroys all units on map' — so the thing under test is a GUARANTEE": every cell of the playable
 * rectangle inside some impact's lethal radius, achieved by topping the salvo up with fill warheads
 * until it was true.
 *
 * WHY IT NO LONGER SAYS THAT. The user played it and asked for far fewer warheads — "a maximum of
 * one nuke per Derrick, plus the two city destroyers". On river-zeta the fill pass WAS 17 of the 19
 * six-megaton detonations, so total coverage and the requested count are not both available. The
 * count won. Uncovered ground is now expected, and on river-zeta it is about 56% of the map.
 *
 * WHAT REPLACED IT, and it is genuinely weaker:
 *   1. Every HIGH-VALUE point target is inside some impact's lethal radius.       (still a guarantee)
 *   2. Every member of a city that is WITHIN ONE WARHEAD OF ITS OWN CENTROID is
 *      inside some impact's lethal radius.                                        (still a guarantee)
 *   3. A city wider than the lethal radius loses its edges.                        (now allowed)
 *   4. A structure in neither category may be missed entirely.                    (now allowed)
 *   5. Ground between targets is uncovered.                                       (now allowed)
 *
 * (2) IS NARROWER THAN IT LOOKS AND THE NARROWING IS THE RETUNE. WarheadsPerCity went from 2 to 1,
 * so a city gets one warhead at its centroid instead of a pair spread along its long axis, and a
 * town wider than 2 x LethalRadius now has corners no warhead reaches. This was first written here
 * as the unqualified "every city member is covered" and two shipped-map layouts failed it
 * immediately — correctly, because that property is no longer true and has not been since the fill
 * pass was removed. Those edges die in Annihilate like everything else.
 * "Nothing survives" is carried by DoomsdayStrike.Annihilate alone, which is not geometry and so is
 * not testable here. See the class remarks on DoomsdayStrike.
 *
 * (1) AND (2) ARE ONLY TRUE BECAUSE MinSeparation FITS INSIDE THE EFFECTIVE RADIUS. That used to be
 * an accident and is now load-bearing, so it has its own test below.
 *
 * The adversarial part is still the jitter. A test that draws one random layout and checks it proves
 * almost nothing, so the jitter tests below do not sample: they displace every aim point directly
 * AWAY from the worst cell for it, which is the actual worst case the RNG could ever produce.
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

		// The shipped configuration (mods/ww3mod/rules/world.yaml, DoomsdayStrike), post-retune.
		const int LethalCells = 14;
		const int JitterCells = 4;
		const int MinSeparationCells = 10;
		const int CityLinkCells = 8;
		const int CityMinBuildings = 4;
		const int WarheadsPerCity = 1;

		static int Effective => DoomsdayMath.EffectiveRadius(LethalCells, JitterCells);

		/// <summary>
		/// The mode's placement pipeline, in the same order DoomsdayStrike.BuildSalvo runs it, minus the
		/// engine types. Returns the final aim points, unjittered — the jitter is applied by the callers
		/// that care, so each test can choose between "as drawn" and "worst possible draw".
		///
		/// TWO STAGES THE OLD VERSION OF THIS HELPER HAD ARE GONE, matching the trait: clusters below
		/// CityMinBuildings no longer contribute a centroid aim point, and there is no fill pass.
		/// </summary>
		static List<CPos> BuildAimPoints(IReadOnlyList<CPos> cityAssets, IReadOnlyList<CPos> highValue)
		{
			var clusters = DoomsdayMath.ClusterAssets(cityAssets, CityLinkCells);

			var candidates = new List<CPos>();
			foreach (var c in clusters)
				if (c.Count >= CityMinBuildings)
					candidates.AddRange(DoomsdayMath.CityAimPoints(cityAssets, c, WarheadsPerCity, MinSeparationCells, Effective));

			candidates.AddRange(highValue);

			return DoomsdayMath.MinSeparationFilter(candidates, MinSeparationCells)
				.Select(i => candidates[i]).ToList();
		}

		/// <summary>
		/// The members of every city that are within one warhead of their own city's centroid — the set
		/// the coverage guarantee is stated over. A member further out than that is a corner of a town
		/// too wide for a single warhead and is knowingly left to the annihilation sweep.
		/// </summary>
		static List<CPos> CityMembersInReach(IReadOnlyList<CPos> cityAssets)
		{
			var inReach = new List<CPos>();
			foreach (var c in DoomsdayMath.ClusterAssets(cityAssets, CityLinkCells))
			{
				if (c.Count < CityMinBuildings)
					continue;

				var centre = DoomsdayMath.Centroid(cityAssets, c);
				foreach (var i in c)
					if (DoomsdayMath.WithinRadius(cityAssets[i], centre, Effective))
						inReach.Add(cityAssets[i]);
			}

			return inReach;
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
		public void MinSeparationFitsInsideTheEffectiveRadius()
		{
			// THE INVARIANT THE TWO COVERAGE TESTS BELOW STAND ON, and the one this retune nearly broke.
			//
			// MinSeparationFilter drops a candidate when something already kept is closer than
			// MinSeparation. That is only harmless if "closer than MinSeparation" implies "inside the
			// kept impact's effective radius" — otherwise an asset can be dropped as redundant and then
			// not be shot at, which with the fill pass gone means it is simply missed.
			//
			// It held by luck at 12 against an effective 16. Dropping LethalRadius from 20 to 14 for the
			// smaller outlier warhead took the effective radius to 10, and 12 > 10: two of river-zeta's
			// eighteen point targets came out uncovered. MinSeparation moved to 10 for exactly this.
			Assert.That(MinSeparationCells, Is.LessThanOrEqualTo(Effective),
				$"MinSeparation {MinSeparationCells} exceeds the effective radius {Effective} " +
				$"(LethalRadius {LethalCells} - JitterRadius {JitterCells}). The separation filter can now " +
				"drop a targeted asset and leave it outside every blast.");
		}

		[Test]
		public void EveryHighValueTargetIsCovered()
		{
			// (1) of the four properties in the file header, and the one the user's rule is written in:
			// one warhead per derrick. A derrick may be DROPPED by the separation filter — that is the
			// filter working — but it must then be inside the radius of whatever displaced it.
			foreach (var (name, bounds) in ShippedMaps)
			{
				var (city, high) = SyntheticAssets(bounds, name.Length * 7919);
				var aim = BuildAimPoints(city, high);

				foreach (var asset in high)
					Assert.That(aim.Any(p => DoomsdayMath.WithinRadius(asset, p, Effective)), Is.True,
						$"{name}: high-value target at {asset} is outside every lethal radius, with {aim.Count} impacts.");
			}
		}

		[Test]
		public void EveryCityMemberWithinOneWarheadOfItsCentreIsCovered()
		{
			// (2), stated at its real strength. A city gets ONE warhead, on its centroid, so what is
			// guaranteed is that the warhead lands on the city and is not stranded — not that a town of
			// arbitrary size is consumed by it. Members further from their own centroid than a single
			// lethal radius are outside the claim, and the assertion skips them rather than pretending.
			foreach (var (name, bounds) in ShippedMaps)
			{
				var (city, high) = SyntheticAssets(bounds, name.Length * 7919);
				var aim = BuildAimPoints(city, high);

				foreach (var member in CityMembersInReach(city))
					Assert.That(aim.Any(p => DoomsdayMath.WithinRadius(member, p, Effective)), Is.True,
						$"{name}: city member at {member} is within one warhead of its own city centre " +
						$"but outside every lethal radius, with {aim.Count} impacts.");
			}
		}

		[Test]
		public void CityAimPointsAlwaysSurviveTheSeparationFilter()
		{
			// WHAT MAKES THE TEST ABOVE MORE THAN A TAUTOLOGY. Its guarantee holds only because a city's
			// own aim point is always in the final salvo, and that is a property of ORDER: BuildSalvo
			// appends city candidates before high-value ones, and MinSeparationFilter is greedy over the
			// candidate order, so a city displaces a derrick and never the reverse.
			//
			// Reordering those two loops would silently drop a city's warhead in favour of a derrick 8
			// cells away and leave the whole town unshot. Nothing else in this file would notice.
			foreach (var (name, bounds) in ShippedMaps)
			{
				var (city, high) = SyntheticAssets(bounds, name.Length * 7919);
				var aim = BuildAimPoints(city, high);

				foreach (var c in DoomsdayMath.ClusterAssets(city, CityLinkCells))
				{
					if (c.Count < CityMinBuildings)
						continue;

					foreach (var p in DoomsdayMath.CityAimPoints(city, c, WarheadsPerCity, MinSeparationCells, Effective))
						Assert.That(aim.Contains(p), Is.True,
							$"{name}: the aim point {p} for a {c.Count}-building city was dropped by the separation filter.");
				}
			}
		}

		[Test]
		public void JitterCannotUncoverATargetedAsset()
		{
			// THE ADVERSARIAL CASE, and the reason placement is done against EffectiveRadius rather than
			// LethalRadius. For each targeted asset, take the impact that covers it and push that impact
			// directly AWAY by the full jitter bound — the worst displacement the RNG can produce for
			// that pairing. The asset must still be inside the LETHAL radius. Stronger than sampling
			// draws: it does not depend on the RNG at all.
			//
			// NARROWED FROM "ANY CELL" TO "ANY TARGETED ASSET" with the fill pass. The old version swept
			// every cell of the map, which was meaningful only while every cell was covered.
			foreach (var (name, bounds) in ShippedMaps)
			{
				var (city, high) = SyntheticAssets(bounds, name.Length * 7919);
				var aim = BuildAimPoints(city, high);

				foreach (var asset in high.Concat(CityMembersInReach(city)))
				{
					var best = aim.Min(p => DoomsdayMath.DistanceSquared(asset, p));
					var worst = DoomsdayMath.Isqrt(best) + JitterCells;
					Assert.That(worst, Is.LessThanOrEqualTo(LethalCells),
						$"{name}: targeted asset {asset} is {DoomsdayMath.Isqrt(best)} from its nearest impact; " +
						$"a full {JitterCells}-cell jitter would put it {worst} out, past the {LethalCells}-cell lethal radius.");
				}
			}
		}

		[Test]
		public void TheSalvoIsBoundedByTheAssetsItAimsAt()
		{
			// THE COUNT IS THE POINT OF THE RETUNE, so it gets an assertion rather than a comment. With
			// no fill pass and no small-cluster outliers, the salvo can never exceed one impact per
			// high-value target plus WarheadsPerCity per city — every candidate comes from one of those
			// two sources, and the separation filter only ever removes.
			//
			// A regression here means someone added a warhead source back. Before the retune the same
			// arithmetic on river-zeta gave 29 against an asset-derived bound of 14.
			foreach (var (name, bounds) in ShippedMaps)
			{
				var (city, high) = SyntheticAssets(bounds, name.Length * 7919);
				var aim = BuildAimPoints(city, high);

				var cityCount = DoomsdayMath.ClusterAssets(city, CityLinkCells).Count(c => c.Count >= CityMinBuildings);
				var bound = high.Count + (cityCount * WarheadsPerCity);

				Assert.That(aim.Count, Is.LessThanOrEqualTo(bound),
					$"{name}: {aim.Count} impacts against a bound of {bound} " +
					$"({high.Count} high-value + {cityCount} cities x {WarheadsPerCity}).");
			}
		}

		[Test]
		public void UncoveredGroundIsExpectedAndIsNotAFailure()
		{
			// THE TRADE, WRITTEN DOWN. This asserts the OPPOSITE of the test it replaced: the salvo does
			// NOT cover the map, and a future reader who finds ground outside every blast radius should
			// know that is the design rather than a bug to fix. Kept as an executable statement rather
			// than a comment so that silently restoring the fill pass fails here and has to be argued.
			//
			// UncoveredCells is retained in DoomsdayMath for this, and for anyone who wants to put the
			// fill pass back; it has no production caller any more.
			var (city, high) = SyntheticAssets(ShippedMaps[3].Bounds, ShippedMaps[3].Name.Length * 7919);
			var aim = BuildAimPoints(city, high);
			var uncovered = DoomsdayMath.UncoveredCells(ShippedMaps[3].Bounds, aim, Effective);

			Assert.That(uncovered, Is.Not.Empty,
				"river-zeta is fully covered by the targeted salvo alone. That is not impossible, but it " +
				"is not what this configuration should produce — check whether a fill pass came back.");
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
			//
			// GridSpacing has no production caller since the fill pass was removed. The test stays
			// because the function does, and because anyone restoring the fill pass needs it to be right.
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
		public void MinSeparationIsHonouredBetweenImpacts()
		{
			// "Never detonating too many too close." Now unconditional over the whole salvo: there are no
			// fill points left to exempt, so every impact in the salvo is subject to it.
			foreach (var (name, bounds) in ShippedMaps)
			{
				var (city, high) = SyntheticAssets(bounds, name.Length * 7919);
				var kept = BuildAimPoints(city, high);

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
			//
			// PINNED AT TWO ON PURPOSE, even though WarheadsPerCity shipped as 1 in the retune — at 1 the
			// function returns the centroid and tests nothing. This is a test of the spread geometry, so
			// it exercises the case where the spread exists.
			const int SplitWarheads = 2;
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
				var points = DoomsdayMath.CityAimPoints(members, indices, SplitWarheads, MinSeparationCells, Effective);

				foreach (var m in members)
				{
					// Only meaningful when the member is inside one warhead of the centroid to begin with.
					// A city wider than the lethal radius has edges nothing was ever going to reach, and
					// since the fill pass was removed nothing picks them up — Annihilate does.
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
