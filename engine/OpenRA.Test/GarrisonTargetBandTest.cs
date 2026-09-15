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
	/// A garrisoned civilian house must not outrank the men leaning out of its own firing ports.
	///
	/// <para>^CivBuilding used to advertise `Defense` in Targetable@WhenGarrisoned, which inverted the
	/// preference for the one template that scores the two classes differently:
	/// ^AutoTargetGroundAntiTank puts `Defense` at 3 on @Default and `Infantry` at 2 on @Lower, so on
	/// its non-stance-fireatwill band an AT unit preferred shelling the house.</para>
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
	/// littlebird, HELI). That is a deliberate, costed consequence — and if a future band ever names
	/// `Ground` or `Structure` this fixture goes red, which is exactly when the cost changes.</para>
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
		public void GarrisonedCivilianHouseNoLongerAdvertisesDefense(string template)
		{
			var all = ModRulesYaml.AllRuleNodes();
			var garrisoned = TargetTypesOf(all, template, "Targetable@WhenGarrisoned");

			Assert.That(garrisoned.Contains("Defense"), Is.False,
				$"{template}'s garrisoned Targetable advertises `Defense`. A civilian house is not an " +
				"emplacement, and the token makes ^AutoTargetGroundAntiTank's @Default band (priority 3) " +
				"outrank the port men on @Lower (priority 2).");

			// The two that keep it, so a blanket removal cannot pass this fixture: the building must
			// still be shootable at all, and the base Targetable is a different question entirely.
			Assert.That(garrisoned.Contains("Ground"), Is.True, $"{template} must stay auto-targetable via `Ground`");
			Assert.That(garrisoned.Contains("Structure"), Is.True, $"{template} must stay a `Structure`");
		}

		[Test]
		public void AnEmptyCivilianHouseIsUnchangedAndRealEmplacementsKeepDefense()
		{
			var all = ModRulesYaml.AllRuleNodes();

			// The !loaded Targetable was not touched. An empty ^CivBuilding is Neutral and rejected
			// two gates further out (Armament.TargetRelationships, ChooseTarget's AppearsHostileTo),
			// so nothing here was ever load-bearing for it — but a later blanket edit should notice.
			Assert.That(TargetTypesOf(all, "^CivBuilding", "Targetable").Contains("Defense"), Is.True,
				"^CivBuilding's ungarrisoned Targetable lost `Defense` too — this change is scoped to the " +
				"garrisoned one, which is the only state in which port men exist to be preferred.");

			foreach (var emplacement in new[] { "GTWR", "PBOX", "HBOX" })
				Assert.That(TargetTypesOf(all, emplacement, "Targetable@WhenGarrisoned").Contains("Defense"), Is.True,
					$"{emplacement} lost `Defense`. It is a real static defence; only the civilian house was " +
					"miscategorised.");
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
		public void TheAntiTankBandNowPrefersThePortMenOverTheHouse()
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

			Assert.That(houseBand, Is.EqualTo(AutoTarget.NoTargetPriorityBand),
				"a garrisoned house still matches an ^AutoTargetGroundAntiTank band. Every band on that " +
				"chain lists its targets explicitly and none names `Ground` or `Structure`, so after " +
				$"dropping `Defense` the house should match nothing at all — it resolved to band {houseBand}.");

			Assert.That(manBand, Is.GreaterThan(AutoTarget.NoTargetPriorityBand),
				"the port soldier matches no band either, which would mean this template cannot engage a " +
				"garrison at all rather than preferring the men.");
		}
	}
}
