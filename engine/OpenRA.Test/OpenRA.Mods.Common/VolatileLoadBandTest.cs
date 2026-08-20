#region Copyright & License Information
/*
 * WW3MOD volatile cargo — death-explosion band selection.
 *
 * A supply/ammo carrier detonates proportionally to what it was carrying. The strength is picked by
 * a set of Explodes traits whose RequiresCondition expressions partition the load range into bands.
 *
 * Explodes is a ConditionalTrait implementing INotifyKilled with NO arbitration between instances
 * (Explodes.cs:84, :103) — every enabled instance fires its own weapon on the same death. So the
 * band predicates are not merely a lookup: OVERLAP MEANS TWO EXPLOSIONS from one death, and a GAP
 * means a loaded carrier dies silently. Both are invisible to an autotest, which sees a unit die
 * either way. That is why the partition is pinned here.
 *
 * The predicate strings below are copied verbatim from the RequiresCondition lines in
 * mods/ww3mod/rules/ingame/vehicles.yaml, vehicles-america.yaml, vehicles-russia.yaml and misc.yaml.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Support;

namespace OpenRA.Test
{
	[TestFixture]
	public class VolatileLoadBandTest
	{
		const int Bands = 8;

		// Supply carriers (TRUK, SUPPLYCACHE, LOGISTICSCENTER) — SupplyProvider grants supply-level
		// once per occupied step, so the band is a direct equality on the stacked count.
		static readonly string[] SupplyBandPredicates =
		{
			"supply-level == 1", "supply-level == 2", "supply-level == 3", "supply-level == 4",
			"supply-level == 5", "supply-level == 6", "supply-level == 7", "supply-level == 8",
		};

		// Ammo carriers — AmmoPool grants ammo-primary once per remaining round (AmmoPool.cs:456-460),
		// so each band is the round range that maps onto it. Ranges differ per unit because the
		// magazines differ: grad 40, m270 12, HIMARS 2.
		static readonly string[] GradBandPredicates =
		{
			"ammo-primary >= 1 && ammo-primary <= 5", "ammo-primary >= 6 && ammo-primary <= 10",
			"ammo-primary >= 11 && ammo-primary <= 15", "ammo-primary >= 16 && ammo-primary <= 20",
			"ammo-primary >= 21 && ammo-primary <= 25", "ammo-primary >= 26 && ammo-primary <= 30",
			"ammo-primary >= 31 && ammo-primary <= 35", "ammo-primary >= 36 && ammo-primary <= 40",
		};

		static readonly string[] M270BandPredicates =
		{
			"ammo-primary == 1", "ammo-primary >= 2 && ammo-primary <= 3",
			"ammo-primary == 4", "ammo-primary >= 5 && ammo-primary <= 6",
			"ammo-primary == 7", "ammo-primary >= 8 && ammo-primary <= 9",
			"ammo-primary == 10", "ammo-primary >= 11 && ammo-primary <= 12",
		};

		// A 2-round magazine can only ever land on the half-full and full bands; the other six are
		// unreachable and carry no trait. Nulls record that deliberately, so a later edit that adds
		// a trait for an unreachable band has to change this line.
		static readonly string[] HimarsBandPredicates =
		{
			null, null, null, "ammo-primary == 1",
			null, null, null, "ammo-primary == 2",
		};

		static int MatchingBand(IEnumerable<string> predicates, string variable, int load)
		{
			var values = new Dictionary<string, int> { { variable, load } };
			var matched = predicates
				.Select((p, i) => (Predicate: p, Band: i + 1))
				.Where(x => x.Predicate != null && new BooleanExpression(x.Predicate).Evaluate(values))
				.ToArray();

			Assert.That(matched.Length, Is.LessThanOrEqualTo(1),
				$"load {load} satisfies {matched.Length} band predicates " +
				$"({string.Join(", ", matched.Select(m => m.Predicate))}) — every enabled Explodes fires, " +
				"so overlapping bands detonate the carrier more than once.");

			return matched.Length == 1 ? matched[0].Band : 0;
		}

		static void AssertPartitions(string[] predicates, string variable, int capacity)
		{
			for (var load = 1; load <= capacity; load++)
			{
				var expected = SupplyProviderInfo.SupplyLevel(load, capacity, Bands);
				Assert.That(MatchingBand(predicates, variable, load), Is.EqualTo(expected),
					$"load {load}/{capacity} should fire band {expected}");
			}
		}

		[Test]
		public void EmptyCarrierMatchesNoBand()
		{
			// Band 0 is the pre-existing empty-carrier explosion, which is a separate trait. If a band
			// predicate were also true at zero load the carrier would fire both.
			Assert.That(MatchingBand(SupplyBandPredicates, "supply-level", 0), Is.EqualTo(0));
			Assert.That(MatchingBand(GradBandPredicates, "ammo-primary", 0), Is.EqualTo(0));
			Assert.That(MatchingBand(M270BandPredicates, "ammo-primary", 0), Is.EqualTo(0));
			Assert.That(MatchingBand(HimarsBandPredicates, "ammo-primary", 0), Is.EqualTo(0));
		}

		[Test]
		public void SupplyLevelIsCeilingOfOccupiedStep()
		{
			// A carrier holding anything at all is in band 1, never band 0 — the point of the feature
			// is that a nearly-empty truck still goes off, just weakly.
			Assert.That(SupplyProviderInfo.SupplyLevel(1, 750, Bands), Is.EqualTo(1));
			Assert.That(SupplyProviderInfo.SupplyLevel(0, 750, Bands), Is.EqualTo(0));

			// Only a full load reaches the top band.
			Assert.That(SupplyProviderInfo.SupplyLevel(750, 750, Bands), Is.EqualTo(8));
			Assert.That(SupplyProviderInfo.SupplyLevel(749, 750, Bands), Is.EqualTo(8));
			// 656/750 is 87.4% — just under the 87.5% top of band 7. One more unit crosses into band 8.
			Assert.That(SupplyProviderInfo.SupplyLevel(656, 750, Bands), Is.EqualTo(7));
			Assert.That(SupplyProviderInfo.SupplyLevel(657, 750, Bands), Is.EqualTo(8));

			// Exact step boundaries land on the lower band: 12.5% is the top of band 1, not band 2.
			Assert.That(SupplyProviderInfo.SupplyLevel(750 / 8, 750, Bands), Is.EqualTo(1));
			Assert.That(SupplyProviderInfo.SupplyLevel(375, 750, Bands), Is.EqualTo(4));
		}

		[Test]
		public void SupplyLevelIsInertWhenStepsAreUnset()
		{
			// SupplyLevelSteps defaults to 0 so every actor that does not opt in is untouched.
			Assert.That(SupplyProviderInfo.SupplyLevel(750, 750, 0), Is.EqualTo(0));
		}

		[Test]
		public void SupplyLevelNeverExceedsStepCount()
		{
			// currentSupply can exceed TotalSupply transiently; the band must still be addressable.
			Assert.That(SupplyProviderInfo.SupplyLevel(900, 750, Bands), Is.EqualTo(8));
		}

		[Test]
		public void SupplyBandsPartitionTheLoadRange()
		{
			for (var level = 1; level <= Bands; level++)
				Assert.That(MatchingBand(SupplyBandPredicates, "supply-level", level), Is.EqualTo(level));
		}

		[Test]
		public void GradBandsPartitionItsMagazine()
		{
			AssertPartitions(GradBandPredicates, "ammo-primary", 40);
		}

		[Test]
		public void M270BandsPartitionItsMagazine()
		{
			AssertPartitions(M270BandPredicates, "ammo-primary", 12);
		}

		[Test]
		public void HimarsBandsPartitionItsMagazine()
		{
			AssertPartitions(HimarsBandPredicates, "ammo-primary", 2);
		}
	}
}
