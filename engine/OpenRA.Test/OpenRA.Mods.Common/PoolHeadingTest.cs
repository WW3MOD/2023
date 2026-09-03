#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// <para>Covers the two ways an ammo-pool heading can name something other than the munition the
	/// player is buying: a mount listed twice for its targeting modes, and an armament that is a dummy
	/// trigger for a spawned actor.</para>
	///
	/// <para>The merge rule is tested as a pure function because its interesting cases are the ones it
	/// must REFUSE. The TooltipName overrides are tested against the shipped rules instead, because an
	/// override is only justified while the thing it overrides is still wrong — if a targeter ever
	/// becomes a real weapon, the hand-written label silently stops matching the gun.</para>
	/// </summary>
	[TestFixture]
	public class PoolHeadingTest
	{
		static string FindRules(params string[] relative)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var parts = new[] { dir.FullName, "mods", "ww3mod", "rules" }.Concat(relative).ToArray();
				var candidate = Path.Combine(parts);
				if (File.Exists(candidate))
					return candidate;
			}

			throw new FileNotFoundException("could not locate mods/ww3mod/rules/" + string.Join("/", relative));
		}

		static MiniYamlNode Node(string name, params string[] relative)
		{
			var n = MiniYaml.FromFile(FindRules(relative)).FirstOrDefault(x => x.Key == name);
			Assert.That(n, Is.Not.Null, $"{name} is gone from {string.Join("/", relative)}.");
			return n;
		}

		static string Field(MiniYamlNode actor, string trait, string field)
		{
			return actor.Value.Nodes
				.Where(n => n.Key == trait || n.Key.StartsWith(trait + "@", StringComparison.Ordinal))
				.SelectMany(n => n.Value.Nodes)
				.FirstOrDefault(n => n.Key == field)?.Value.Value?.Trim();
		}

		[TestCase("7.62mm Minigun", "7.62mm Minigun AA", ExpectedResult = "7.62mm Minigun")]
		[TestCase("12.7mm Hind", "12.7mm Hind AA", ExpectedResult = "12.7mm Hind")]
		[TestCase("Ataka", "Ataka AA", ExpectedResult = "Ataka")]
		[TestCase("30mm Tunguska AA", "30mm Tunguska AG", ExpectedResult = "30mm Tunguska")]
		public string ModeVariantsOfOneMountCollapseToTheWeapon(string a, string b)
		{
			// The littlebird listed "7.62MM MINIGUN + 7.62MM MINIGUN AA" over a round count for one
			// magazine. Tunguska is the case where neither label is a prefix of the other — both
			// carry a mode tag — so a plain prefix test would not have merged it.
			return AmmoPoolInfo.MergeVariantLabels(new[] { a, b });
		}

		[Test]
		public void TwoRealWeaponsSharingAMagazineAreStillListedSeparately()
		{
			// FTUR feeds FireballLauncher and Flamespray.heavy from one pool. There is no shared stem,
			// so the join survives — this is the case the merge must not swallow.
			Assert.That(
				AmmoPoolInfo.MergeVariantLabels(new[] { "Fireball Launcher", "Flamespray heavy" }),
				Is.EqualTo("Fireball Launcher + Flamespray heavy"));
		}

		[Test]
		public void ASharedCalibreIsNotAMountAndMustNotBeMergedAway()
		{
			// The failure mode the all-caps test exists to prevent. Under a bare "longest common word
			// prefix" rule these collapse to "7.62mm", dropping the weapon and keeping the calibre —
			// worse than the duplication being fixed, because it destroys information silently.
			Assert.That(
				AmmoPoolInfo.MergeVariantLabels(new[] { "7.62mm Minigun", "7.62mm Sniper" }),
				Is.EqualTo("7.62mm Minigun + 7.62mm Sniper"));
		}

		[Test]
		public void AMixedCaseSuffixIsPartOfAWeaponNameNotATargetingMode()
		{
			// "Hellfire Littlebird" is a variant of the munition, not a mode of one mount, and the
			// player is entitled to see which Hellfire the pool holds.
			Assert.That(
				AmmoPoolInfo.MergeVariantLabels(new[] { "Hellfire", "Hellfire Littlebird" }),
				Is.EqualTo("Hellfire + Hellfire Littlebird"));
		}

		[TestCase("iskander", "IskanderTargeter", "vehicles-russia.yaml")]
		[TestCase("HIMARS", "HIMARSTargeter", "vehicles-america.yaml")]
		public void TheLauncherStillFiresADummyTriggerRatherThanTheMunition(string actor, string weapon, string file)
		{
			// The premise of the TooltipName. The armament names a targeter; the thing the pool counts
			// is the missile ACTOR spawned by MissileSpawnerMaster. If the armament is ever pointed at
			// a real warhead, the hand-written heading stops describing the gun and should go.
			var node = Node(actor, "ingame", file);

			Assert.That(Field(node, "Armament", "Weapon"), Is.EqualTo(weapon),
				$"{actor}'s armament no longer fires {weapon}. Re-check whether TooltipName is still " +
				"describing something the derived name gets wrong.");

			Assert.That(node.Value.Nodes.Any(n => n.Key == "MissileSpawnerMaster"), Is.True,
				$"{actor} no longer spawns its missile as an actor, which was the reason the armament's " +
				"weapon key named a mechanism instead of a munition.");

			Assert.That(Field(node, "AmmoPool", "TooltipName"), Is.Not.Null.And.Not.Empty,
				$"{actor}'s pool heading is back to being derived from the targeter's weapon key.");
		}

		[Test]
		public void TheDemolitionChargePoolsStillHaveNoArmamentToNameThem()
		{
			// ^E6 and ^SF meter their charges through Demolition, not an Armament, so the heading fell
			// back to the pool key and read "SECONDARY". The override is only needed while that holds.
			foreach (var actor in new[] { "^E6", "^SF" })
			{
				var node = Node(actor, "ingame", "infantry.yaml");

				Assert.That(node.Value.Nodes.Any(n => n.Key == "Demolition"), Is.True,
					$"{actor} no longer carries Demolition — recheck what spends its secondary pool.");

				var pool = node.Value.Nodes
					.Where(n => n.Key.StartsWith("AmmoPool", StringComparison.Ordinal))
					.FirstOrDefault(n => n.Value.Nodes.Any(f => f.Key == "Name" && f.Value.Value.Trim() == "secondary-ammo"));
				Assert.That(pool, Is.Not.Null, $"{actor} no longer declares secondary-ammo.");

				var named = pool.Value.Nodes.FirstOrDefault(n => n.Key == "TooltipName")?.Value.Value?.Trim();
				Assert.That(named, Is.EqualTo("Demolition charge"),
					$"{actor}'s charge pool heading is not the agreed label.");
			}
		}
	}
}
