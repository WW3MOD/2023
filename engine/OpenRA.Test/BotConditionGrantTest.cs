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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace OpenRA.Test
{
	/// <summary>
	/// Every condition a bot module's RequiresCondition depends on must have a grantor on the Player actor
	/// (WORKSPACE/ideas/261007_test-coverage-scout.md §6.3, priority #2). Both bot profiles gate every module
	/// on `enable-ai-*` and most on `player.nato` / `player.brics` too; drop or rename one grant and every
	/// module reading it goes silently inert — no lint error, no crash, the bot simply never acts. Same for a
	/// renamed ModularBot Type that a GrantConditionOnBotOwner still lists.
	///
	/// Reads the shipped Rules list from mod.yaml IN ORDER and resolves it with the engine's own
	/// MiniYaml.Merge, so case-sensitive top-level keys, Inherits and `-Trait:` removals behave exactly as
	/// they do in game. Nothing is hand-copied: the bot-gate tokens are whatever GrantConditionOnBotOwner
	/// grants, the factions are whatever world.yaml defines.
	///
	/// A grantor is any Player trait field keyed `Condition` (GrantConditionOn*), or a value under a
	/// `Conditions:` block (GrantConditionOnDefconLevel). A condition granted only by some other field
	/// name would read as missing here — the safe direction, a false RED rather than a false green.
	/// </summary>
	[TestFixture]
	public class BotConditionGrantTest
	{
		// ConditionExpression identifiers: letters, digits, '.', '-', '_'. Numbers and true/false are not names.
		static readonly Regex Identifier = new Regex(@"[A-Za-z_][A-Za-z0-9_.\-]*", RegexOptions.Compiled);

		MiniYaml mergedPlayer;
		List<string> playableFactions;

		static string TraitType(string key)
		{
			var at = key.IndexOf('@');
			return at < 0 ? key : key[..at];
		}

		static IEnumerable<string> ConditionNames(string expression)
		{
			return Identifier.Matches(expression ?? "").Select(m => m.Value).Where(v => v != "true" && v != "false");
		}

		static IEnumerable<string> ListValues(string value)
		{
			return (value ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0);
		}

		static List<MiniYamlNode> LoadMergedRules()
		{
			var modDir = ModRulesYaml.RulesDir().Parent;
			var modYaml = MiniYaml.FromFile(Path.Combine(modDir.FullName, "mod.yaml"));
			var rulesList = modYaml.First(n => n.Key == "Rules").Value.Nodes.Select(n => n.Key).ToList();
			Assert.That(rulesList.Count, Is.GreaterThan(10), "mod.yaml Rules list parsed to almost nothing.");

			var sources = new List<IReadOnlyCollection<MiniYamlNode>>();
			foreach (var entry in rulesList)
			{
				// `ww3mod|rules/ai/ai.yaml` -> mods/ww3mod/rules/ai/ai.yaml
				var bar = entry.IndexOf('|');
				Assert.That(bar, Is.GreaterThan(0), $"mod.yaml Rules entry `{entry}` is not package-qualified; this reader needs updating.");
				var path = Path.Combine(modDir.FullName, entry[(bar + 1)..]);
				Assert.That(File.Exists(path), Is.True, $"mod.yaml lists {entry}, which does not exist at {path}.");
				sources.Add(MiniYaml.FromFile(path));
			}

			return MiniYaml.Merge(sources);
		}

		[OneTimeSetUp]
		public void Load()
		{
			var rules = LoadMergedRules();
			mergedPlayer = rules.FirstOrDefault(n => n.Key == "Player")?.Value;
			Assert.That(mergedPlayer, Is.Not.Null, "merged rules have no `Player` actor.");

			var world = rules.First(n => n.Key == "World").Value;
			playableFactions = world.Nodes
				.Where(n => TraitType(n.Key) == "Faction" && ModRulesYaml.ChildNode(n.Value, "RandomFactionMembers") == null)
				.Select(n => ModRulesYaml.Child(n.Value, "InternalName"))
				.ToList();
			Assert.That(playableFactions, Is.Not.Empty, "no playable Faction@ blocks found on the merged World actor.");
		}

		Dictionary<string, List<string>> Grants()
		{
			var grants = new Dictionary<string, List<string>>();
			void Add(string condition, string trait)
			{
				if (string.IsNullOrEmpty(condition))
					return;
				if (!grants.TryGetValue(condition, out var list))
					grants[condition] = list = new List<string>();
				list.Add(trait);
			}

			foreach (var trait in mergedPlayer.Nodes)
			{
				Add(ModRulesYaml.Child(trait.Value, "Condition"), trait.Key);
				var many = ModRulesYaml.ChildNode(trait.Value, "Conditions");
				if (many != null)
					foreach (var c in many.Nodes)
						Add(c.Value.Value?.Trim(), trait.Key);
			}

			return grants;
		}

		HashSet<string> BotGateConditions()
		{
			return mergedPlayer.Nodes
				.Where(n => TraitType(n.Key) == "GrantConditionOnBotOwner")
				.Select(n => ModRulesYaml.Child(n.Value, "Condition"))
				.Where(c => !string.IsNullOrEmpty(c))
				.ToHashSet();
		}

		/// <summary>Player traits that belong to a bot profile: every *BotModule, plus anything whose
		/// RequiresCondition names a bot-gate condition (BotBlackboard, PoiGoalGuard, ...).</summary>
		List<(string Trait, string Requires)> BotConsumers()
		{
			var gates = BotGateConditions();
			return mergedPlayer.Nodes
				.Select(n => (Trait: n.Key, Requires: ModRulesYaml.Child(n.Value, "RequiresCondition")))
				.Where(t => !string.IsNullOrEmpty(t.Requires))
				.Where(t => TraitType(t.Trait).EndsWith("BotModule", StringComparison.Ordinal)
					|| ConditionNames(t.Requires).Any(gates.Contains))
				.ToList();
		}

		[Test]
		public void EveryConditionABotModuleRequiresIsGranted()
		{
			var gates = BotGateConditions();
			Assert.That(gates.Count, Is.GreaterThanOrEqualTo(2),
				$"only {gates.Count} GrantConditionOnBotOwner grants on the merged Player — the bot layer did not load.");

			var consumers = BotConsumers();
			Assert.That(consumers.Count, Is.GreaterThan(40),
				$"only {consumers.Count} bot-gated Player traits found — this fixture is scanning nothing, not passing.");

			var grants = Grants();
			var missing = consumers
				.SelectMany(c => ConditionNames(c.Requires).Where(name => !grants.ContainsKey(name)).Select(name => $"{c.Trait} requires `{name}` ({c.Requires})"))
				.ToList();

			Assert.That(missing, Is.Empty,
				"bot module(s) gate on a condition nothing on the merged Player actor grants, so they can never enable:\n  " +
				string.Join("\n  ", missing));
		}

		[Test]
		public void EveryBotOwnerGrantNamesAShippedBotType()
		{
			var botTypes = mergedPlayer.Nodes
				.Where(n => TraitType(n.Key) == "ModularBot")
				.Select(n => ModRulesYaml.Child(n.Value, "Type"))
				.ToHashSet();
			Assert.That(botTypes, Is.Not.Empty, "no ModularBot on the merged Player actor.");

			var dangling = mergedPlayer.Nodes
				.Where(n => TraitType(n.Key) == "GrantConditionOnBotOwner")
				.SelectMany(n => ListValues(ModRulesYaml.Child(n.Value, "Bots")).Where(b => !botTypes.Contains(b)).Select(b => $"{n.Key} lists bot `{b}`"))
				.ToList();

			Assert.That(dangling, Is.Empty,
				$"GrantConditionOnBotOwner names a bot Type no ModularBot declares (shipped: {string.Join(", ", botTypes)}), " +
				"so its condition is never granted to that bot:\n  " + string.Join("\n  ", dangling));
		}

		[Test]
		public void FactionGatedBotConditionsReachAPlayableFaction()
		{
			// Prerequisite -> factions that receive it. A provider with no Factions list applies to all.
			var providers = new Dictionary<string, HashSet<string>>();
			foreach (var n in mergedPlayer.Nodes.Where(n => TraitType(n.Key) == "ProvidesPrerequisite"))
			{
				var prereq = ModRulesYaml.Child(n.Value, "Prerequisite");
				if (string.IsNullOrEmpty(prereq))
					continue;
				var factions = ListValues(ModRulesYaml.Child(n.Value, "Factions")).ToList();
				if (!providers.TryGetValue(prereq, out var set))
					providers[prereq] = set = new HashSet<string>();
				set.UnionWith(factions.Count == 0 ? playableFactions : factions.Intersect(playableFactions));
			}

			// Condition -> factions it is granted to, via GrantConditionOnPrerequisite (all prerequisites ANDed).
			var factionGrants = new Dictionary<string, HashSet<string>>();
			foreach (var n in mergedPlayer.Nodes.Where(n => TraitType(n.Key) == "GrantConditionOnPrerequisite"))
			{
				var condition = ModRulesYaml.Child(n.Value, "Condition");
				var reached = new HashSet<string>(playableFactions);
				foreach (var p in ListValues(ModRulesYaml.Child(n.Value, "Prerequisites")))
					reached.IntersectWith(providers.TryGetValue(p, out var f) ? f : new HashSet<string>());
				if (!factionGrants.TryGetValue(condition, out var set))
					factionGrants[condition] = set = new HashSet<string>();
				set.UnionWith(reached);
			}

			var consumed = BotConsumers().SelectMany(c => ConditionNames(c.Requires)).ToHashSet();
			var prerequisiteGated = consumed.Where(factionGrants.ContainsKey).ToList();
			Assert.That(prerequisiteGated, Is.Not.Empty,
				"no bot condition is granted through GrantConditionOnPrerequisite — the faction gating this pins has moved; re-point the fixture.");

			var unreachable = prerequisiteGated.Where(c => factionGrants[c].Count == 0).ToList();
			Assert.That(unreachable, Is.Empty,
				$"bot condition(s) granted only through prerequisites no playable faction ({string.Join(", ", playableFactions)}) is provided, " +
				"so every module gated on them is dead: " + string.Join(", ", unreachable));

			// Each playable faction must receive at least one faction condition the bot layer reads; otherwise
			// that faction's bots run only the faction-agnostic modules.
			var starved = playableFactions.Where(f => !prerequisiteGated.Any(c => factionGrants[c].Contains(f))).ToList();
			Assert.That(starved, Is.Empty,
				"playable faction(s) receive none of the faction-gated bot conditions (" + string.Join(", ", prerequisiteGated) + "): " +
				string.Join(", ", starved));
		}
	}
}
