#region Copyright & License Information
/*
 * WW3MOD power-purchase wiring tests — the links between rules/powers.yaml, the two files that
 * define support powers, and the sidebar chrome, NONE of which the engine validates at load.
 *
 * A purchasable power is spread across files that never mention each other by type:
 *
 *     rules/player.yaml            MissileStrikePower@X, `RequiresPurchase: True`, an `OrderName`,
 *                                  and the `Prerequisites` TIER for every power in the mod --
 *                                  including the six defined in the file below
 *     rules/ingame/nuclear-arsenal.yaml   six more of the same
 *     rules/powers.yaml            a proxy actor naming that OrderName
 *     chrome.yaml + chrome/ingame-player.yaml   the tab glyph and the button that selects the queue
 *
 * Every link is a bare string compared at runtime. The failure modes are all quiet:
 *
 *   - a power with RequiresPurchase and no proxy is UNREACHABLE FOREVER. Setting the flag removes
 *     the timer, so it does not become slow, it never arrives.
 *   - a proxy naming a nonexistent power takes the money for the whole build and then refunds.
 *   - a proxy pointing at a power WITHOUT RequiresPurchase completes and the charge is discarded.
 *   - a proxy missing a trait the palette or the lint gate needs is invisible to the compiler and
 *     to every other test — see below, this one has already happened.
 *
 * ---------------------------------------------------------------------------------------------
 * WHY THE CONSTRUCTIBILITY HALF EXISTS. On 2026-09-06 this branch shipped four proxies carrying
 * Valued + Tooltip + RenderSprites + Buildable and nothing else. The build was clean and 2,728
 * NUnit tests passed. The merge gate then produced 2,555 lint errors — one pair of faults per
 * proxy, repeated across all 326 maps:
 *
 *     Actor `power.kinzhal` is not constructible; failure: ... Missing: OpenRA.Traits.IMouseBoundsInfo
 *     Actor type `power.kinzhal` does not define a default visibility type.
 *
 * because `Tooltip` is `TooltipInfoBase : ConditionalTraitInfo, Requires<IMouseBoundsInfo>`
 * (Tooltip.cs:16) and a bodiless actor can only satisfy that from `Interactable`, and because
 * CheckDefaultVisibility wants some IDefaultVisibilityInfo, which only `AlwaysVisible` supplies here.
 *
 * NOTHING THE WORKER WAS ALLOWED TO RUN WOULD HAVE CAUGHT IT: only --check-yaml constructs actors,
 * and the standing rules forbid running it. So this fixture builds the proxies' REAL trait sets by
 * reflection from the YAML — inheritance resolved — and calls ActorInfo.TraitsInConstructOrder(),
 * which is the very method whose exception CheckTraitPrerequisites.cs:42 reports. A green run here
 * means that specific gate agrees, with no mod load, no World and no launch slot.
 *
 * This deliberately overlaps BuyLoopProxyTest, which pins the same trait chain for the autotest
 * scenario's `powerproxy.strike`. That one models a hand-written list; this one reads the shipped
 * file. Both are worth having: the hand-written list explains the chain, the file walk catches the
 * eleventh proxy somebody adds without reading either.
 *
 * SCOPE, HONESTLY. Trait sets and string links only. It does NOT prove the queue banks a charge
 * (SupportPowerChargeBankTest), that the tab renders, or that any OTHER lint rule is satisfied —
 * only the two that actually failed.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.Traits;

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

		static string FindEngine(params string[] relative)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(new[] { dir.FullName, "engine" }.Concat(relative).ToArray());
				if (File.Exists(candidate))
					return candidate;
			}

			throw new FileNotFoundException("could not locate engine/" + string.Join("/", relative));
		}

		static string Field(MiniYamlNode node, string key)
		{
			return node.Value.Nodes.FirstOrDefault(n => n.Key == key)?.Value.Value?.Trim();
		}

		static List<MiniYamlNode> PowersFile()
		{
			return MiniYaml.FromFile(FindMod("rules", "powers.yaml"));
		}

		/// <summary>Proxy actor name -> the power OrderName it claims to charge.</summary>
		static Dictionary<string, string> Proxies()
		{
			var found = new Dictionary<string, string>();
			foreach (var actor in PowersFile())
			{
				var charge = actor.Value.Nodes.FirstOrDefault(n => n.Key == "ProvidesSupportPowerCharge");
				if (charge != null)
					found[actor.Key] = Field(charge, "Power");
			}

			return found;
		}

		/// <summary>
		/// Every support power in the mod -> whether it opted into being bought. BOTH files, because
		/// the arsenal put six of the ten somewhere other than player.yaml, and a walk that misses a
		/// file reports those six as "no such power" or misses their missing proxies entirely.
		/// </summary>
		static Dictionary<string, bool> Powers()
		{
			var found = new Dictionary<string, bool>();
			foreach (var (order, purchasable, _) in PowerRows())
				found[order] = purchasable;

			return found;
		}

		/// <summary>The three tier prerequisites, exactly as rules/player.yaml provides them.</summary>
		static readonly string[] Tiers = { "powers.america", "powers.russia", "powers.event" };

		/// <summary>
		/// The four Russian warheads authored on a CONCURRENT BRANCH (weapons-nuclear-arsenal.yaml
		/// and rules/ingame/nuclear-arsenal.yaml), whose OrderNames rules/powers.yaml already sells
		/// proxies for. Until that branch merges the powers do not exist, and a proxy naming a
		/// missing power is INERT rather than broken -- SupportPowerProductionQueue resolves through
		/// Manager.Powers and filters out anything that misses (SupportPowerProductionQueue.cs:80-103),
		/// so the entry simply never appears. No crash, no lint error, no lost money.
		///
		/// THIS LIST IS A COUNTDOWN, NOT AN EXEMPTION. It only permits ABSENCE. The moment one of
		/// these powers exists it is held to every rule the other eleven are, including the tier
		/// prerequisite -- which is the specific thing the two branches agreed but could not write
		/// down in one file, because a tier goes on the power trait and only the OrderName was fixed
		/// between them. Delete a name from here as it lands.
		/// </summary>
		static readonly string[] PendingSiblingPowers =
		{
			"Ru9M729Strike", "RuIskanderStrike", "RuKinzhalNStrike", "RuKalibrStrike",
		};

		/// <summary>
		/// Every support power in the mod: its OrderName, whether it opted into being bought, and
		/// the `Prerequisites` string it carries. BOTH files, because the arsenal put six of the
		/// eleven somewhere other than player.yaml, and a walk that misses a file reports those six
		/// as "no such power" or misses their missing proxies entirely.
		/// </summary>
		static IEnumerable<(string Order, bool Purchasable, string Prerequisites)> PowerRows()
		{
			// A trait's fields can be SPLIT ACROSS BOTH FILES and merging them is not optional here.
			// rules/player.yaml deliberately carries the tier for the six powers DEFINED in
			// nuclear-arsenal.yaml (see "TIERS FOR THE SIX POWERS" there), so a per-file walk sees
			// six powers with an OrderName and no Prerequisites, plus six with a Prerequisites and
			// no OrderName, and concludes the arsenal is ungated. MiniYaml merges the Player node
			// across files at load; this reproduces that for the two fields we care about.
			var purchasable = new Dictionary<string, bool>();
			var prereqs = new Dictionary<string, string>();
			var orderOfTrait = new Dictionary<string, string>();

			foreach (var file in new[] { FindMod("rules", "player.yaml"), FindMod("rules", "ingame", "nuclear-arsenal.yaml") })
			{
				var player = MiniYaml.FromFile(file).FirstOrDefault(n => n.Key == "Player");
				if (player == null)
					continue;

				foreach (var trait in player.Value.Nodes)
				{
					// Every support power trait in this mod ends in "Power", optionally with an @suffix.
					if (!trait.Key.Split('@')[0].EndsWith("Power", StringComparison.Ordinal))
						continue;

					var order = Field(trait, "OrderName");
					if (order != null)
					{
						orderOfTrait[trait.Key] = order;
						purchasable[order] = string.Equals(Field(trait, "RequiresPurchase"), "True", StringComparison.OrdinalIgnoreCase);
					}

					var prereq = Field(trait, "Prerequisites");
					if (prereq != null && orderOfTrait.TryGetValue(trait.Key, out var known))
						prereqs[known] = prereq;
					else if (prereq != null)
						prereqs["\0trait:" + trait.Key] = prereq;
				}
			}

			// Second pass for tiers written against a trait whose OrderName lives in the file read
			// LATER. Order-independence matters: mod.yaml lists player.yaml before nuclear-arsenal.yaml
			// today, and a reordering there must not silently turn this fixture green.
			foreach (var (traitKey, order) in orderOfTrait)
				if (!prereqs.ContainsKey(order) && prereqs.TryGetValue("\0trait:" + traitKey, out var late))
					prereqs[order] = late;

			foreach (var (order, buy) in purchasable)
				yield return (order, buy, prereqs.TryGetValue(order, out var p) ? p : null);
		}

		/// <summary>
		/// The trait keys an actor really ends up with, following `Inherits:` within powers.yaml.
		/// Without this the walk sees only what is written under each proxy and misses exactly the
		/// three traits the template carries — which are the three that broke the gate.
		/// </summary>
		static string[] ResolvedTraits(string actorName, int depth = 0)
		{
			if (depth > 8)
				throw new AssertionException($"`Inherits:` cycle reaching {actorName} in powers.yaml");

			var node = PowersFile().FirstOrDefault(n => n.Key == actorName)
				?? throw new AssertionException($"powers.yaml defines no `{actorName}`");

			var traits = new List<string>();
			foreach (var child in node.Value.Nodes)
			{
				if (child.Key.Split('@')[0] == "Inherits")
					traits.AddRange(ResolvedTraits(child.Value.Value.Trim(), depth + 1));
				else
					traits.Add(child.Key.Split('@')[0]);
			}

			return traits.Distinct().ToArray();
		}

		static TraitInfo Instantiate(string traitName)
		{
			var type = typeof(TraitInfo).Assembly.GetTypes()
				.Concat(typeof(OpenRA.Mods.Common.Traits.BuildableInfo).Assembly.GetTypes())
				.FirstOrDefault(t => t.Name == traitName + "Info" && typeof(TraitInfo).IsAssignableFrom(t) && !t.IsAbstract)
				?? throw new AssertionException($"no TraitInfo type named `{traitName}Info` — is the trait misspelled in powers.yaml?");

			return (TraitInfo)Activator.CreateInstance(type);
		}

		static ActorInfo Build(string actorName, params string[] without)
		{
			var traits = ResolvedTraits(actorName).Where(t => !without.Contains(t)).Select(Instantiate).ToArray();
			return new ActorInfo(actorName, traits);
		}

		[Test]
		public void TheFixtureFindsSomethingToCheck()
		{
			// Guards everything below against passing vacuously. Both walks key off naming
			// conventions that a rename would quietly break, turning the assertions into loops over
			// an empty set.
			Assert.That(Proxies(), Is.Not.Empty, "found no purchase proxies in rules/powers.yaml");
			Assert.That(Powers().Values.Where(p => p), Is.Not.Empty, "found no purchasable powers");

			// And the arsenal specifically, since it lives in the second file and an earlier version
			// of this fixture read only the first.
			Assert.That(Powers().Keys, Contains.Item("TsarBombaStrike"),
				"the nuclear-arsenal.yaml walk found nothing; the six arsenal powers are unchecked");
		}

		// ---------- the fault that failed the gate ----------

		[Test]
		public void EveryProxyIsConstructible()
		{
			foreach (var proxy in Proxies().Keys)
				Assert.DoesNotThrow(() => Build(proxy).TraitsInConstructOrder(),
					$"`{proxy}` no longer resolves its traits, which is exactly how this branch failed " +
					"the merge gate on 2026-09-06 with 2,555 errors. Read the exception: it names the " +
					"unsatisfied interface.");
		}

		[Test]
		public void EveryProxyDeclaresADefaultVisibilityType()
		{
			// CheckDefaultVisibility.cs:43. Counts IDefaultVisibilityInfo traits and wants exactly
			// one — zero and two are both errors, so this asserts the count rather than presence.
			foreach (var proxy in Proxies().Keys)
			{
				var count = Build(proxy).TraitInfos<IDefaultVisibilityInfo>().Count;
				Assert.That(count, Is.EqualTo(1),
					$"`{proxy}` declares {count} default visibility types; the gate wants exactly one " +
					"(AlwaysVisible, on the ^PurchasableSupportPower template).");
			}
		}

		[Test]
		public void RemovingInteractableIsWhatBreaksConstruction()
		{
			// THE NEGATIVE HALF. Without it, someone tidying an "unused" Interactable off a
			// positionless proxy — it has no body, so it looks like decoration — gets a green
			// fixture and a red gate. This states in the place they would look that it is
			// load-bearing, and why.
			var proxy = Proxies().Keys.First();
			var ex = Assert.Throws<YamlException>(() => Build(proxy, "Interactable").TraitsInConstructOrder());

			Assert.That(ex.Message, Does.Contain("IMouseBoundsInfo"),
				"the failure must still be the mouse-bounds one this fixture documents; if it has " +
				"become a different unsatisfied dependency, re-derive the template's trait set");
			Assert.That(ex.Message, Does.Contain("TooltipInfo"),
				"Tooltip is the trait carrying the requirement — but removing IT instead is not the " +
				"fix, because CheckTooltips errors on any Buildable actor with no enabled Tooltip");
		}

		[Test]
		public void RemovingAlwaysVisibleIsWhatBreaksVisibility()
		{
			var proxy = Proxies().Keys.First();

			Assert.That(Build(proxy, "AlwaysVisible").TraitInfos<IDefaultVisibilityInfo>(), Is.Empty,
				"AlwaysVisible is the only trait on the proxy supplying IDefaultVisibilityInfo; if " +
				"something else now does, this fixture's account of the 2026-09-06 failure is stale");
		}

		// ---------- the cross-file string links ----------

		[Test]
		public void EveryProxyNamesAPowerThatExistsAndIsPurchasable()
		{
			var powers = Powers();

			foreach (var (proxy, order) in Proxies())
			{
				Assert.That(order, Is.Not.Null.And.Not.Empty,
					$"{proxy} has ProvidesSupportPowerCharge with no Power");

				if (!powers.ContainsKey(order) && PendingSiblingPowers.Contains(order))
					continue;

				Assert.That(powers.ContainsKey(order), Is.True,
					$"{proxy} sells `{order}`, which is not the OrderName of any power in the mod. " +
					"At runtime the queue filters the entry out and it never appears in the tab. " +
					"If this power is arriving on another branch, add it to PendingSiblingPowers.");

				Assert.That(powers[order], Is.True,
					$"{proxy} sells `{order}`, but that power does not set RequiresPurchase. " +
					"The purchase would complete and the charge would be silently discarded.");
			}
		}

		[Test]
		public void EveryPurchasablePowerHasSomewhereToBeBought()
		{
			var sold = Proxies().Values.ToHashSet();

			foreach (var (order, _) in Powers().Where(p => p.Value))
				Assert.That(sold.Contains(order), Is.True,
					$"`{order}` sets RequiresPurchase but no proxy in rules/powers.yaml sells it, " +
					"so it can never be charged and will never appear in the support bin.");
		}

		[Test]
		public void NoPowerIsLeftOnATimerWhileTheRestAreBought()
		{
			// The user's ruling was "that should be the case for all powers", and a half-converted
			// arsenal is the worst of both: the biggest weapons in the mod would be the only free
			// ones. Player-actor powers only — the mslo NukePower is a structure power on an
			// unbuildable building and is out of scope.
			var onTimers = Powers().Where(p => !p.Value).Select(p => p.Key).ToArray();

			Assert.That(onTimers, Is.Empty,
				"these powers still charge on a timer while the rest are bought: " +
				string.Join(", ", onTimers) + ". Either give them a proxy in rules/powers.yaml or " +
				"say in the report why they are exempt.");
		}

		// ---------- the faction gate ----------

		[Test]
		public void EveryPurchasablePowerDeclaresATier()
		{
			// THE FAULT THIS BRANCH EXISTED TO FIX, pinned so it cannot come back. Before 2026-09-07
			// rules/powers.yaml contained ZERO Prerequisites lines and its own header asserted the
			// opposite -- "faction-locked to opposite sides, so no player ever holds both" -- with the
			// case for pricing the two conventional strikes identically resting on it. Eight of the
			// ten powers were in fact buyable by anybody.
			//
			// The gate is `Prerequisites` on the power, NOT on the proxy's Buildable, and the
			// difference is visible: SupportPowerProductionQueue filters AllItems() as well as
			// BuildableItems() on instance.Purchasable, which makes an out-of-tier power ABSENT.
			// A Buildable.Prerequisites gate would only DIM it (ProductionPaletteWidget.cs:707,:780),
			// so a NATO player would see Russia's five greyed out instead of not at all.
			var ungated = PowerRows()
				.Where(r => r.Purchasable)
				.Where(r => r.Prerequisites == null || !Tiers.Any(t => r.Prerequisites.Contains(t, StringComparison.Ordinal)))
				.Select(r => r.Order)
				.ToArray();

			Assert.That(ungated, Is.Empty,
				"these purchasable powers name no tier prerequisite, so EVERY player can buy them " +
				"regardless of faction: " + string.Join(", ", ungated) + ". Add one of " +
				string.Join(" / ", Tiers) + " to the power in rules/player.yaml (the six arsenal " +
				"powers are tiered there too, in the block headed \"TIERS FOR THE SIX POWERS\").");
		}

		[Test]
		public void TheTierPrerequisitesAreActuallyProvided()
		{
			// The other half: a tier nothing provides is a power nobody can ever buy, and it fails
			// EXACTLY like a correctly-locked power looks -- silently absent from the tab. A typo in
			// `powers.america` is invisible to the test above, which only checks the string is one of
			// the three known ones; this checks the three are real.
			var player = MiniYaml.FromFile(FindMod("rules", "player.yaml")).First(n => n.Key == "Player");
			var provided = player.Value.Nodes
				.Where(n => n.Key.Split('@')[0] == "ProvidesPrerequisite")
				.Select(n => Field(n, "Prerequisite"))
				.Where(v => v != null)
				.ToHashSet();

			foreach (var tier in Tiers)
				Assert.That(provided, Contains.Item(tier),
					$"no ProvidesPrerequisite in player.yaml grants `{tier}`, so every power gated on " +
					"it is unbuyable by everyone -- which looks identical in game to being correctly " +
					"faction-locked.");
		}

		[Test]
		public void TheSandboxOptionUnlocksAllThreeTiers()
		{
			// The user's test mode, and it must survive: they had all ten powers buyable specifically
			// so they could test them, and the faction gate would otherwise have taken that away.
			// Sandbox works by providing all three tiers with NO Factions filter, so this asserts
			// three unfiltered providers gated on the sandbox condition -- one per tier. A provider
			// that grew a `Factions:` line would silently make sandbox faction-locked again.
			var player = MiniYaml.FromFile(FindMod("rules", "player.yaml")).First(n => n.Key == "Player");

			var sandbox = player.Value.Nodes
				.Where(n => n.Key.Split('@')[0] == "ProvidesPrerequisite")
				.Where(n => Field(n, "RequiresCondition") == "!powers-sandbox-disabled")
				.ToArray();

			foreach (var tier in Tiers)
			{
				var provider = sandbox.FirstOrDefault(n => Field(n, "Prerequisite") == tier);
				Assert.That(provider, Is.Not.Null,
					$"the sandbox lobby option does not grant `{tier}`, so ticking it still leaves " +
					"that tier faction-locked (or, for powers.event, unreachable entirely).");

				Assert.That(provider.Value.Nodes.Any(n => n.Key == "Factions"), Is.False,
					$"the sandbox provider for `{tier}` carries a Factions filter, which defeats the " +
					"whole point of the option -- it exists to IGNORE faction.");
			}

			// And the polarity. Written the other way round -- grant a `powers-sandbox` condition
			// when the option is ENABLED -- an unregistered option falls back to true and hands both
			// factions the Tsar Bomba. Same trap as @tacnuke and @highyieldnuke.
			var gate = player.Value.Nodes
				.Where(n => n.Key.Split('@')[0] == "GrantConditionOnLobbyOption")
				.FirstOrDefault(n => Field(n, "Option") == "powers-sandbox");

			Assert.That(gate, Is.Not.Null, "nothing in player.yaml reads the `powers-sandbox` lobby option");
			Assert.That(Field(gate, "Condition"), Is.EqualTo("powers-sandbox-disabled"),
				"the sandbox gate must grant a DISABLING condition, not an enabling one");
			Assert.That(Field(gate, "GrantWhenOptionDisabled"), Is.EqualTo("true"),
				"GrantWhenOptionDisabled must be true, or an unregistered option defaults to " +
				"SANDBOX ON and every match becomes one");
		}

		[Test]
		public void TheSandboxOptionIsRegisteredAndDefaultsOff()
		{
			// The C# half of the pair above. GrantConditionOnLobbyOption's fallback is
			// !GrantWhenOptionDisabled and NOT this default (they are separate values), so this is not
			// a safety property -- it is the plain statement that a host who configures nothing gets a
			// normal, faction-locked match.
			var src = File.ReadAllText(FindEngine("OpenRA.Mods.Common", "Traits", "World", "PowersLobbyOptions.cs"));

			Assert.That(src, Does.Contain("\"powers-sandbox\""),
				"PowersLobbyOptions does not register a `powers-sandbox` option, so the checkbox " +
				"never appears in the lobby and player.yaml's gate falls back to off forever");
			Assert.That(src, Does.Contain("PowersSandboxCheckboxEnabled = false"),
				"the sandbox checkbox must ship defaulting OFF");
		}

		// ---------- what the palette and the chrome dereference ----------

		[Test]
		public void EveryProxyCarriesWhatTheProductionPaletteDereferences()
		{
			// ProductionPaletteWidget.RefreshIcons calls item.TraitInfo<RenderSpritesInfo>() and
			// item.TraitInfo<BuildableInfo>() with no null check (ProductionPaletteWidget.cs:713-715),
			// so either one missing is a crash the moment the tab is opened rather than a missing icon.
			foreach (var proxy in Proxies().Keys)
			{
				var traits = ResolvedTraits(proxy);
				foreach (var required in new[] { "RenderSprites", "Buildable", "Valued", "Tooltip" })
					Assert.That(traits, Contains.Item(required), $"{proxy} has no {required}");

				var buildable = PowersFile().First(n => n.Key == proxy).Value.Nodes.First(n => n.Key == "Buildable");
				Assert.That(Field(buildable, "Queue"), Is.EqualTo("Powers"),
					$"{proxy} is not on the Powers queue, so nothing would display it");
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

		[Test]
		public void TheSupportBinBindsAHotkeyForEveryPowerAPlayerCanHold()
		{
			// SupportPowersWidget draws one cameo per non-Disabled power, stacked, with no scrolling
			// and no wrapping, and binds hotkeys only for the first HotkeyCount of them. With the
			// arsenal merged a player can hold nine at once — all ten less the faction-locked
			// conventional strike they do not get — so a count of 6 left three reachable by mouse only.
			var bin = MiniYaml.FromFile(FindMod("chrome", "ingame-player.yaml"));

			MiniYamlNode Find(IEnumerable<MiniYamlNode> nodes)
			{
				foreach (var n in nodes)
				{
					if (n.Key != null && n.Key.StartsWith("SupportPowers@", StringComparison.Ordinal))
						return n;

					var inner = Find(n.Value.Nodes);
					if (inner != null)
						return inner;
				}

				return null;
			}

			var widget = Find(bin) ?? throw new AssertionException("no SupportPowers widget in ingame-player.yaml");
			var count = int.Parse(Field(widget, "HotkeyCount") ?? "0", System.Globalization.CultureInfo.InvariantCulture);

			// HOW MANY CAN ONE PLAYER ACTUALLY HOLD? This used to be "everything less the one
			// conventional strike you do not get", because only two powers were faction-locked. Every
			// power now names a tier, so the answer is a max over the tiers a single player can hold
			// at once -- and the worst case is SANDBOX, where one player holds all three at once and
			// the bin has to draw every power in the mod.
			//
			// SANDBOX IS THE BINDING CASE AND IT IS NOT AN EDGE CASE. It is the mode the user tests
			// in, so it is the mode most likely to have more cameos on screen than the bin can bind.
			// Sizing to a normal match instead would leave exactly the person using the feature
			// reaching for a mouse.
			var rows = PowerRows().Where(r => r.Purchasable).ToArray();
			var perTier = Tiers.ToDictionary(
				t => t,
				t => rows.Count(r => r.Prerequisites != null && r.Prerequisites.Contains(t, StringComparison.Ordinal)));

			// Normal match: your faction's tier only. Sandbox: all three.
			var normalMatch = Math.Max(perTier["powers.america"], perTier["powers.russia"]);
			var holdable = perTier.Values.Sum();

            Assert.That(holdable, Is.GreaterThanOrEqualTo(normalMatch),
				"tier arithmetic is wrong; sandbox cannot hold fewer powers than a normal match");

			Assert.That(count, Is.GreaterThanOrEqualTo(holdable),
				$"a player can hold {holdable} powers at once with the sandbox lobby option on " +
				$"({normalMatch} in a normal match) but the bin binds only {count} hotkeys, so the " +
				"last few are mouse-only. HotkeyCount is POSITIONAL -- SupportPowersWidget binds " +
				"hotkeys[IconCount] by draw index (SupportPowersWidget.cs:161), not by power -- so " +
				"raising it is safe and never rebinds an existing slot to a different weapon.");

			// And every slot the count promises must be a defined hotkey, or CheckChromeHotkeys
			// fails the gate (CheckChromeHotkeys.cs:97). 01-06 come from common; the rest are ours.
			var defined = MiniYaml.FromFile(FindMod("hotkeys.yaml")).Select(n => n.Key).ToHashSet();
			for (var i = 7; i <= count; i++)
				Assert.That(defined, Contains.Item("SupportPower" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)),
					"common|hotkeys/supportpowers.yaml stops at SupportPower06, so every slot above " +
					"that must be defined in ww3mod|hotkeys.yaml");
		}
	}
}
