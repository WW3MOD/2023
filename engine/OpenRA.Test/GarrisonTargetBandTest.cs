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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// DOCUMENTATION, not a guard: this fixture states the accepted targeting arithmetic for a
	/// garrisoned civilian house so that nobody has to re-derive it, and fails if any of it moves.
	///
	/// <para>^CivBuilding advertises `Defense` in Targetable@WhenGarrisoned, and that DOES invert one
	/// preference: ^AutoTargetGroundAntiTank puts `Defense` at 3 on @Default and `Infantry` at 2 on
	/// @Lower, so on its non-stance-fireatwill band such a unit prefers shelling the house to shooting
	/// the men at its ports. THE INVERSION IS ACCEPTED (manager ruling, 2026-09-15). Its real scope is
	/// two aircraft -- A10 and FROG are the only actors on that template -- and only off FireAtWill,
	/// where the two classes tie. An earlier commit dropped the token and was reverted the same day:
	/// see the cost below.</para>
	///
	/// <para>WHAT THIS FIXTURE REALLY PINS is the second-order fact, which is the one nobody can see
	/// from the actor's YAML: NO AutoTargetPriority band anywhere in the mod names `Ground` or
	/// `Structure` in an explicit ValidTargets. The ^AutoTarget base chain reaches a civilian house
	/// only because its @FireAtWill band declares no ValidTargets at all and therefore takes the
	/// engine default `Ground, Water, Air`. Every band on the ^AutoTargetGround* and ^AutoTargetAll*
	/// chains is explicit, drawn from {Infantry, Vehicle, Water, Underwater, Air, Defense, Mine} —
	/// so `Defense` was their ONLY handle on a house, and removing it removes the house from their
	/// candidate set outright rather than merely demoting it. Fifteen actors are on those chains
	/// (GTWR, FTUR, PBOX, HBOX, GUN, iskander, HIMARS, tunguska, strykershorad, A10, FROG, MI28, MIG,
	/// littlebird, HELI) -- including the mod's two rocket-artillery pieces, the units that reduce
	/// garrisons, which is why the drop was reverted: it would have defeated the point of making the
	/// building targetable at all. If a future band ever names `Ground` or `Structure` this fixture
	/// goes red, which is exactly when that cost changes and the ruling is worth revisiting.</para>
	///
	/// <para>The bands are evaluated through the engine's own predicate,
	/// AutoTarget.ResolveTargetPriorityBand, over AutoTargetPriorityInfo objects populated by
	/// FieldLoader from the real YAML nodes — not a hand-written copy of the table.</para>
	/// </summary>
	[TestFixture]
	public class GarrisonTargetBandTest
	{
		/// <summary>The one definition of `key` that actually DECLARES `childKey`. A top-level name can
		/// legitimately appear in more than one rules file — ^CivBuilding is declared in
		/// ingame/civilian.yaml and again in campaign/campaign-tooltips.yaml, which adds nothing but a
		/// Tooltip — so keying on the name alone finds two nodes and asserting "exactly one" is simply
		/// wrong. Requiring exactly one DECLARING node is the question these fixtures mean to ask, and
		/// it still fails loudly if a second file ever starts overriding the trait under test.</summary>
		static MiniYamlNode NodeDeclaring(List<(string File, MiniYamlNode Node)> all, string key, string childKey)
		{
			var match = all.Where(x => x.Node.Key == key && ModRulesYaml.ChildNode(x.Node.Value, childKey) != null)
				.Select(x => x.Node).ToArray();
			Assert.That(match.Length, Is.EqualTo(1),
				$"expected exactly one top-level `{key}` declaring `{childKey}`, found {match.Length}");
			return match[0];
		}

		static BitSet<TargetableType> TargetTypesOf(List<(string File, MiniYamlNode Node)> all, string key, string targetableKey)
		{
			var actor = NodeDeclaring(all, key, targetableKey);
			var targetable = ModRulesYaml.ChildNode(actor.Value, targetableKey);

			var types = ModRulesYaml.Child(targetable, "TargetTypes");
			Assert.That(types, Is.Not.Null.And.Not.Empty, $"{actor.Key}/{targetableKey} declares no TargetTypes");

			return new BitSet<TargetableType>(types.Split(',').Select(t => t.Trim()).ToArray());
		}

		/// <summary>`^AutoTarget*` templates from rules/defaults.yaml with their `Inherits` RESOLVED, by
		/// the engine's own MiniYaml.Merge. Resolving is not optional here and getting it wrong inverts
		/// the answer: ^AutoTargetGroundAntiTank declares `AutoTargetPriority@FireAtWill` with a Priority
		/// and NO ValidTargets, inheriting the list from ^AutoTargetGround. Load that node on its own and
		/// FieldLoader fills ValidTargets from the C# default instead — `Ground, Water, Air`
		/// (AutoTargetPriority.cs:21) — which matches a civilian house and reports a band that does not
		/// exist in the game. One source file is enough: the whole ^AutoTarget* chain is declared in
		/// defaults.yaml and inherits only from within it.</summary>
		static List<MiniYamlNode> MergedTemplates()
		{
			var file = Path.Combine(ModRulesYaml.RulesDir().FullName, "defaults.yaml");
			Assert.That(File.Exists(file), Is.True, $"could not find {file}");

			var merged = MiniYaml.Merge(new[] { (IReadOnlyCollection<MiniYamlNode>)MiniYaml.FromFile(file) });
			Assert.That(merged.Count, Is.GreaterThan(10), "defaults.yaml merged to almost nothing — this fixture is scanning nothing, not passing.");
			return merged;
		}

		/// <summary>Every AutoTargetPriority@* band on `template` after inheritance, loaded through
		/// FieldLoader so ValidTargets / InvalidTargets / Priority / ValidRelationships are parsed
		/// exactly as the game parses them.</summary>
		static AutoTargetPriorityInfo[] BandsOf(List<MiniYamlNode> merged, string template)
		{
			var node = merged.SingleOrDefault(n => n.Key == template);
			Assert.That(node, Is.Not.Null, $"no merged template `{template}`");

			var bands = new List<AutoTargetPriorityInfo>();
			foreach (var n in node.Value.Nodes)
			{
				if (n.Key != "AutoTargetPriority" && !n.Key.StartsWith("AutoTargetPriority@", System.StringComparison.Ordinal))
					continue;

				var info = new AutoTargetPriorityInfo();
				FieldLoader.Load(info, n.Value);
				bands.Add(info);
			}

			Assert.That(bands, Is.Not.Empty, $"{template} resolves to no AutoTargetPriority bands");
			return bands.ToArray();
		}

		[TestCase("^CivBuilding")]
		public void GarrisonedCivilianHouseKeepsDefense(string template)
		{
			var all = ModRulesYaml.AllRuleNodes();
			var garrisoned = TargetTypesOf(all, template, "Targetable@WhenGarrisoned");

			Assert.That(garrisoned.Contains("Defense"), Is.True,
				$"{template}'s garrisoned Targetable lost `Defense`. That token is the ONLY handle the " +
				"^AutoTargetGround* and ^AutoTargetAll* chains have on a civilian house -- none of their " +
				"bands names `Ground` or `Structure` -- so dropping it removes a garrisoned house from " +
				"the candidate set of fifteen actors, HIMARS and iskander among them. The AT band's " +
				"preference for the house over the port men is the accepted cost of keeping it.");

			// The two that keep it, so a blanket removal cannot pass this fixture: the building must
			// still be shootable at all, and the base Targetable is a different question entirely.
			Assert.That(garrisoned.Contains("Ground"), Is.True, $"{template} must stay auto-targetable via `Ground`");
			Assert.That(garrisoned.Contains("Structure"), Is.True, $"{template} must stay a `Structure`");
		}

		[Test]
		public void TheEmplacementsAndTheEmptyHouseCarryDefenseToo()
		{
			var all = ModRulesYaml.AllRuleNodes();

			Assert.That(TargetTypesOf(all, "^CivBuilding", "Targetable").Contains("Defense"), Is.True,
				"^CivBuilding's ungarrisoned Targetable lost `Defense`. An empty house is Neutral and " +
				"rejected two gates further out anyway (Armament.TargetRelationships, ChooseTarget's " +
				"AppearsHostileTo), so this is not load-bearing — but the garrisoned and ungarrisoned " +
				"blocks are meant to agree, and a blanket edit that split them should be noticed.");

			foreach (var emplacement in new[] { "GTWR", "PBOX", "HBOX" })
				Assert.That(TargetTypesOf(all, emplacement, "Targetable@WhenGarrisoned").Contains("Defense"), Is.True,
					$"{emplacement} lost `Defense`, and it is a real static defence.");
		}

		[Test]
		public void NoAutoTargetBandNamesGroundOrStructure()
		{
			// THE ONE KNOWN EXCEPTION, named rather than filtered by pattern so that a second one
			// cannot hide behind it. HIND does not inherit an ^AutoTarget* template at all (its
			// Inherits@AutoTarget line is commented out, aircraft-russia.yaml:92) and hand-rolls a copy
			// of ^AutoTargetGroundAntiTankandAir's bands, adding `Structure` to @FireAtWill only. So a
			// HIND on FireAtWill still sees a garrisoned house, via Structure and at priority 1 —
			// below the port men — while its @Default and @Lower bands no longer match it at all.
			var known = new[] { "HIND/AutoTargetPriority@FireAtWill" };

			var offenders = new List<string>();
			foreach (var (file, node) in ModRulesYaml.AllRuleNodes())
			{
				foreach (var n in node.Value.Nodes)
				{
					if (n.Key != "AutoTargetPriority" && !n.Key.StartsWith("AutoTargetPriority@", System.StringComparison.Ordinal))
						continue;

					var valid = ModRulesYaml.Child(n.Value, "ValidTargets");
					if (valid == null)
						continue;

					var set = valid.Split(',').Select(t => t.Trim()).ToArray();
					if (!set.Contains("Ground") && !set.Contains("Structure"))
						continue;

					if (known.Contains($"{node.Key}/{n.Key}"))
						continue;

					offenders.Add($"{file}: {node.Key}/{n.Key} = {valid}");
				}
			}

			Assert.That(offenders, Is.Empty,
				"a band now names `Ground` or `Structure` explicitly:\n  " + string.Join("\n  ", offenders) +
				"\nThis fixture is not forbidding that — it is the tripwire on the reasoning in " +
				"^CivBuilding's Targetable@WhenGarrisoned comment, which says `Defense` was the ONLY handle " +
				"the ^AutoTargetGround*/^AutoTargetAll* chains had on a civilian house. If that stopped " +
				"being true, re-read that comment before trusting it.");
		}

		[Test]
		public void TheAntiTankBandPrefersTheHouseOverThePortMen()
		{
			var all = ModRulesYaml.AllRuleNodes();
			var house = TargetTypesOf(all, "^CivBuilding", "Targetable@WhenGarrisoned");

			// A port soldier is an ordinary in-world ^Infantry actor; `Infantry` is the type the bands
			// score him on.
			var portMan = new BitSet<TargetableType>("Infantry");

			// Every band ^AutoTargetGroundAntiTank presents once its Inherits chain
			// (-> ^AutoTargetGroundAssaultMove -> ^AutoTargetGround) is resolved. Conditions are NOT
			// evaluated: the question is "can ANY band of this template match a garrisoned house", so
			// every band is offered regardless of the stance that would enable it -- the strictest
			// form of the claim.
			var bands = BandsOf(MergedTemplates(), "^AutoTargetGroundAntiTank");

			var houseBand = AutoTarget.ResolveTargetPriorityBand(bands, PlayerRelationship.Enemy, house);
			var manBand = AutoTarget.ResolveTargetPriorityBand(bands, PlayerRelationship.Enemy, portMan);

			// THE ACCEPTED INVERSION, stated as the numbers rather than as prose. 3 comes from
			// @Default's `Vehicle, Defense, Water, Underwater`; 2 from @Lower's `Infantry, Defense,
			// Mine`. Conditions are not evaluated here — every band the template can present is
			// offered — so this is the strictest reading, and @Default is in fact only active off
			// stance-fireatwill, where the two tie at 2.
			Assert.That(houseBand, Is.EqualTo(3),
				$"a garrisoned house resolves to band {houseBand} for ^AutoTargetGroundAntiTank, not the " +
				"3 this ruling was made against. If it dropped to NoTargetPriorityBand, `Defense` has been " +
				"removed from ^CivBuilding again and fifteen actors just lost the house from their " +
				"candidate sets; if it moved some other way, the band table changed and the ruling is " +
				"worth revisiting.");

			Assert.That(manBand, Is.EqualTo(2),
				$"a port soldier resolves to band {manBand}, not 2. The accepted inversion is precisely " +
				"3-over-2 on @Default; a different pair of numbers is a different tradeoff.");

			Assert.That(houseBand, Is.GreaterThan(manBand),
				"the inversion this fixture documents is gone. That is not necessarily bad news — but it " +
				"is a behaviour change nobody recorded, so re-read the ruling before accepting it.");
		}
	}
}
