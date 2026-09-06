#region Copyright & License Information
/*
 * WW3MOD power-purchase wiring tests — the links between rules/powers.yaml, rules/player.yaml and
 * the sidebar chrome that NOTHING ELSE CHECKS.
 *
 * A purchasable power is spread across four files that never mention each other by type:
 *
 *     rules/player.yaml   MissileStrikePower@X with `RequiresPurchase: True` and an `OrderName`
 *     rules/powers.yaml   a proxy actor with `ProvidesSupportPowerCharge: Power: <that OrderName>`
 *     chrome.yaml         three production-icons regions for the tab glyph
 *     chrome/ingame-player.yaml   the ProductionTypeButton that selects the queue
 *
 * Every one of those links is a bare string compared at runtime, and NONE of them is validated by
 * --check-yaml, by nav-guard, or by the engine at load. The failure modes are all silent-ish and
 * all expensive:
 *
 *   - a power with RequiresPurchase and no proxy is UNREACHABLE FOREVER. There is no timer left to
 *     fall back to, so it simply never appears, and the YAML looks entirely correct.
 *   - a proxy naming a power that does not exist takes the player's money for the whole build,
 *     then refuses to complete and refunds. Visible in play, invisible at load.
 *   - a proxy pointing at a power WITHOUT RequiresPurchase completes, calls GrantCharge, and the
 *     charge is discarded by the bank because the power is still on its timer. The player pays and
 *     gets nothing, with no error anywhere.
 *   - a missing production-icons region CRASHES on the first sidebar frame (ImageWidget.Draw
 *     dereferences a null sprite), and the three region names are synthesised in C# from the tab's
 *     ProductionGroup, so they are not visible anywhere in the chrome YAML.
 *
 * SCOPE, HONESTLY. This is a wiring check read off the YAML text. It does NOT prove the queue
 * banks a charge, that the bank empties on fire, or that the tab renders — the first two are
 * SupportPowerChargeBankTest, and the third needs the game on screen.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace OpenRA.Test
{
	[TestFixture]
	public class PowerPurchaseWiringTest
	{
		static string FindMod(params string[] relative)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var parts = new[] { dir.FullName, "mods", "ww3mod" }.Concat(relative).ToArray();
				var candidate = Path.Combine(parts);
				if (File.Exists(candidate))
					return candidate;
			}

			throw new FileNotFoundException("could not locate mods/ww3mod/" + string.Join("/", relative));
		}

		static string Field(MiniYamlNode node, string key)
		{
			return node.Value.Nodes.FirstOrDefault(n => n.Key == key)?.Value.Value?.Trim();
		}

		/// <summary>Proxy actor name -> the power OrderName it claims to charge.</summary>
		static Dictionary<string, string> Proxies()
		{
			var found = new Dictionary<string, string>();
			foreach (var actor in MiniYaml.FromFile(FindMod("rules", "powers.yaml")))
			{
				var charge = actor.Value.Nodes.FirstOrDefault(n => n.Key == "ProvidesSupportPowerCharge");
				if (charge != null)
					found[actor.Key] = Field(charge, "Power");
			}

			return found;
		}

		/// <summary>Every support power on the Player actor -> whether it opted into being bought.</summary>
		static Dictionary<string, bool> Powers()
		{
			var player = MiniYaml.FromFile(FindMod("rules", "player.yaml"))
				.FirstOrDefault(n => n.Key == "Player")
				?? throw new AssertionException("player.yaml defines no `Player` actor");

			var found = new Dictionary<string, bool>();
			foreach (var trait in player.Value.Nodes)
			{
				// Every support power trait in this mod ends in "Power", optionally with an @suffix.
				var name = trait.Key.Split('@')[0];
				if (!name.EndsWith("Power", StringComparison.Ordinal))
					continue;

				var order = Field(trait, "OrderName");
				if (order == null)
					continue;

				found[order] = string.Equals(Field(trait, "RequiresPurchase"), "True", StringComparison.OrdinalIgnoreCase);
			}

			return found;
		}

		[Test]
		public void TheFixtureFindsSomethingToCheck()
		{
			// Guards every other test here against passing vacuously. Both walks key off naming
			// conventions ("...Power" traits, a ProvidesSupportPowerCharge node) that a rename would
			// quietly break, turning the assertions below into loops over an empty set.
			Assert.That(Proxies(), Is.Not.Empty, "found no purchase proxies in rules/powers.yaml");
			Assert.That(Powers().Values.Where(p => p), Is.Not.Empty, "found no purchasable powers in rules/player.yaml");
		}

		[Test]
		public void EveryProxyNamesAPowerThatExistsAndIsPurchasable()
		{
			var powers = Powers();

			foreach (var (proxy, order) in Proxies())
			{
				Assert.That(order, Is.Not.Null.And.Not.Empty,
					$"{proxy} has ProvidesSupportPowerCharge with no Power");

				Assert.That(powers.ContainsKey(order), Is.True,
					$"{proxy} sells `{order}`, which is not the OrderName of any power on the Player actor. " +
					"At runtime this takes the money for the whole build and then refunds it.");

				Assert.That(powers[order], Is.True,
					$"{proxy} sells `{order}`, but that power does not set RequiresPurchase. " +
					"The purchase would complete and the charge would be silently discarded.");
			}
		}

		[Test]
		public void EveryPurchasablePowerHasSomewhereToBeBought()
		{
			// The unreachable-forever direction, and the one a reviewer will not catch by reading
			// either file alone: adding RequiresPurchase removes the timer, so a power without a
			// proxy is not "slow to arrive", it never arrives.
			var sold = Proxies().Values.ToHashSet();

			foreach (var (order, purchasable) in Powers().Where(p => p.Value))
				Assert.That(sold.Contains(order), Is.True,
					$"`{order}` sets RequiresPurchase but no proxy in rules/powers.yaml sells it, " +
					"so it can never be charged and will never appear in the support bin.");
		}

		[Test]
		public void EveryProxyCarriesWhatTheProductionPaletteDereferences()
		{
			// ProductionPaletteWidget.RefreshIcons calls item.TraitInfo<RenderSpritesInfo>() and
			// item.TraitInfo<BuildableInfo>() with no null check (ProductionPaletteWidget.cs:713-715),
			// so either one missing is a crash the moment the tab is opened rather than a missing icon.
			foreach (var actor in MiniYaml.FromFile(FindMod("rules", "powers.yaml")))
			{
				if (actor.Value.Nodes.All(n => n.Key != "ProvidesSupportPowerCharge"))
					continue;

				var keys = actor.Value.Nodes.Select(n => n.Key).ToHashSet();
				Assert.That(keys, Contains.Item("RenderSprites"), $"{actor.Key} has no RenderSprites");
				Assert.That(keys, Contains.Item("Buildable"), $"{actor.Key} has no Buildable");
				Assert.That(keys, Contains.Item("Valued"), $"{actor.Key} has no Valued, so it would be free");
				Assert.That(keys, Contains.Item("Tooltip"), $"{actor.Key} has no Tooltip, so it would be nameless");

				var buildable = actor.Value.Nodes.First(n => n.Key == "Buildable");
				Assert.That(Field(buildable, "Queue"), Is.EqualTo("Powers"),
					$"{actor.Key} is not on the Powers queue, so nothing would display it");
			}
		}

		[Test]
		public void TheSidebarTabHasAllThreeGlyphRegionsItWillDereference()
		{
			// ClassicProductionLogic synthesises `<group>`, `<group>-disabled` and `<group>-alert`
			// from the button's ProductionGroup (ClassicProductionLogic.cs:82-95), overwriting any
			// ImageName in the YAML — so these three names appear nowhere in the chrome files and a
			// reviewer has no way to notice one is missing. ImageWidget.Draw then dereferences the
			// sprite without a null check, so absence is a crash on the first sidebar frame.
			var chrome = MiniYaml.Merge(new[] { MiniYaml.FromFile(FindMod("chrome.yaml")) });
			var icons = chrome.FirstOrDefault(n => n.Key == "production-icons")
				?? throw new AssertionException("chrome.yaml defines no `production-icons` collection");

			var regions = icons.Value.Nodes.FirstOrDefault(n => n.Key == "Regions")?.Value.Nodes
				.Select(n => n.Key).ToHashSet() ?? new HashSet<string>();

			var groups = new List<string>();
			void Walk(IEnumerable<MiniYamlNode> nodes)
			{
				foreach (var n in nodes)
				{
					if (n.Key != null && n.Key.StartsWith("ProductionTypeButton@", StringComparison.Ordinal))
					{
						var group = Field(n, "ProductionGroup");
						if (!string.IsNullOrEmpty(group))
							groups.Add(group);
					}

					Walk(n.Value.Nodes);
				}
			}

			Walk(MiniYaml.FromFile(FindMod("chrome", "ingame-player.yaml")));

			Assert.That(groups, Is.Not.Empty, "found no production tab buttons to check");

			foreach (var group in groups)
			{
				var stem = group.ToLowerInvariant();
				foreach (var suffix in new[] { "", "-disabled", "-alert" })
					Assert.That(regions, Contains.Item(stem + suffix),
						$"the {group} tab will ask production-icons for `{stem + suffix}` and crash without it");
			}
		}
	}
}
