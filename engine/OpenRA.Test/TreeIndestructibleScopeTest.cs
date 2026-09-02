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
using NUnit.Framework;

namespace OpenRA.Test
{
	/// <summary>
	/// Trees are indestructible (owner ruling 2026-09-02: "stop all burnt trees from appearing").
	/// The invulnerability must land on the TREES and nowhere else.
	///
	/// It was first written as one Inherits line on ^Tree, and ^Tree is not trees-only: it is the
	/// mod's general base for "neutral decoration with forest cover and Trees-only targeting", so
	/// BOXES01-09 (via ^Box), ICE01-05 and UTILPOL1-2 silently became invulnerable as well. That is a
	/// gameplay change nobody asked for and — this is why it needs a fixture rather than a
	/// screenshot — nobody can SEE. A tree that wrongly dies leaves a burnt sprite on the map; a
	/// crate that wrongly survives a napalm strike looks exactly like a crate. The failure is
	/// invisible at runtime in the direction the bug actually went.
	///
	/// The discriminator is deliberately NOT a hardcoded list of tree names. An actor needs this
	/// treatment precisely when dying would leave a husk behind, so the rule is:
	///
	///     inherits ^Tree AND defines SpawnActorOnDeath  &lt;=&gt;  must reach ^TreeIndestructible
	///
	/// which holds in both directions today (22 actors: T01-T17, TC01-TC05). A tree added later gets
	/// a husk, so it is caught here if it forgets to opt in; a decoration added later has no husk, so
	/// it is caught here if it opts in by accident.
	/// </summary>
	[TestFixture]
	public class TreeIndestructibleScopeTest
	{
		const string Base = "^Tree";
		const string Marker = "^TreeIndestructible";
		const string Husk = "SpawnActorOnDeath";

		static DirectoryInfo RulesDir()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = new DirectoryInfo(Path.Combine(dir.FullName, "mods", "ww3mod", "rules"));
				if (candidate.Exists)
					return candidate;
			}

			throw new DirectoryNotFoundException("could not locate mods/ww3mod/rules");
		}

		static List<(string File, MiniYamlNode Node)> AllRuleNodes()
		{
			var nodes = new List<(string, MiniYamlNode)>();
			foreach (var file in RulesDir().GetFiles("*.yaml", SearchOption.AllDirectories))
				foreach (var node in MiniYaml.FromFile(file.FullName))
					nodes.Add((file.Name, node));

			Assert.That(nodes.Count, Is.GreaterThan(100),
				$"only {nodes.Count} rule nodes parsed — this fixture is scanning nothing, not passing.");

			return nodes;
		}

		static IEnumerable<string> Parents(MiniYamlNode node)
		{
			return node.Value.Nodes
				.Where(n => n.Key == "Inherits" || n.Key.StartsWith("Inherits@", StringComparison.Ordinal))
				.Select(n => n.Value.Value?.Trim())
				.Where(v => !string.IsNullOrEmpty(v));
		}

		/// <summary>Transitive closure of everything reaching <paramref name="root"/> by inheritance,
		/// excluding the root itself.</summary>
		static HashSet<string> Descendants(List<(string File, MiniYamlNode Node)> all, string root)
		{
			var seen = new HashSet<string> { root };
			for (var grew = true; grew;)
			{
				grew = false;
				foreach (var (_, node) in all)
					if (!seen.Contains(node.Key) && Parents(node).Any(seen.Contains))
						grew |= seen.Add(node.Key);
			}

			seen.Remove(root);
			return seen;
		}

		static bool Defines(MiniYamlNode node, string key)
		{
			return node.Value.Nodes.Any(n => n.Key == key);
		}

		[Test]
		public void TheMarkerTemplateActuallyGrantsInvulnerability()
		{
			// Everything below is about WHO inherits the template. None of it is worth anything if the
			// template stopped meaning "invulnerable" — Modifier 100 is DamageMultiplier's own default,
			// so a dropped or edited line here makes every other assertion in this fixture pass while
			// trees burn normally.
			var marker = AllRuleNodes().Where(x => x.Node.Key == Marker).ToArray();
			Assert.That(marker.Length, Is.EqualTo(1),
				$"expected exactly one definition of {Marker}; found {marker.Length}.");

			var multiplier = marker[0].Node.Value.Nodes
				.FirstOrDefault(n => n.Key.StartsWith("DamageMultiplier", StringComparison.Ordinal));

			Assert.That(multiplier, Is.Not.Null,
				$"{Marker} no longer defines a DamageMultiplier, so inheriting it does nothing.");

			Assert.That(multiplier.Value.Nodes.FirstOrDefault(n => n.Key == "Modifier")?.Value.Value?.Trim(),
				Is.EqualTo("0"),
				$"{Marker} does not set Modifier: 0. Anything other than 0 leaves trees destructible " +
				"(100 is the trait's default), and burnt trees come back.");
		}

		[Test]
		public void TheMarkerIsNotOnTheSharedTreeTemplate()
		{
			// The exact regression. ^Tree is shared with non-trees, so the marker must never sit on it.
			var tree = AllRuleNodes().Where(x => x.Node.Key == Base).ToArray();
			Assert.That(tree.Length, Is.EqualTo(1), $"expected exactly one definition of {Base}.");

			Assert.That(Parents(tree[0].Node).Contains(Marker), Is.False,
				$"{Base} inherits {Marker} directly. {Base} is NOT trees-only — BOXES01-09, ICE01-05 and " +
				"UTILPOL1-2 inherit it too, and this makes all of them invulnerable as well. Put the " +
				"inherit on the individual tree actors instead.");
		}

		[Test]
		public void EveryActorThatWouldLeaveAHuskIsIndestructible()
		{
			var all = AllRuleNodes();
			var decorations = Descendants(all, Base);
			var invulnerable = Descendants(all, Marker);

			Assert.That(decorations.Count, Is.GreaterThanOrEqualTo(30),
				$"only {decorations.Count} actors resolve to {Base} — the inheritance chain changed " +
				"shape, so this fixture is no longer looking at the actors it was written for.");

			var huskers = all
				.Where(x => decorations.Contains(x.Node.Key) && Defines(x.Node, Husk))
				.ToArray();

			Assert.That(huskers.Length, Is.EqualTo(22),
				$"expected exactly 22 husk-spawning {Base} descendants (T01-T17, TC01-TC05); found " +
				$"{huskers.Length}: {string.Join(", ", huskers.Select(h => h.Node.Key))}. If a tree was " +
				"added or removed, update this count deliberately.");

			foreach (var (file, node) in huskers)
				Assert.That(invulnerable.Contains(node.Key), Is.True,
					$"{node.Key} ({file}) defines {Husk} but does not reach {Marker}. It can be killed, " +
					"and killing it spawns the burnt husk sprite the owner asked to be rid of. Add " +
					$"`Inherits@Indestructible: {Marker}` to it.");
		}

		[Test]
		public void NothingWithoutAHuskWasSweptUp()
		{
			// The other direction, and the one the original bug went. These actors have no husk, so they
			// cannot produce the artifact this mechanism exists to suppress; making them bulletproof is
			// an unrequested gameplay change that nobody would ever observe in play.
			var all = AllRuleNodes();
			var decorations = Descendants(all, Base);
			var invulnerable = Descendants(all, Marker);

			foreach (var expected in new[] { "BOXES01", "BOXES09", "ICE01", "ICE05", "UTILPOL1", "UTILPOL2" })
				Assert.That(decorations.Contains(expected), Is.True,
					$"{expected} no longer resolves to {Base}, so this fixture is not testing what it " +
					"claims to. Re-derive which non-trees share the template.");

			foreach (var (file, node) in all)
			{
				if (!decorations.Contains(node.Key) || Defines(node, Husk) || !invulnerable.Contains(node.Key))
					continue;

				Assert.Fail(
					$"{node.Key} ({file}) reaches {Marker} but defines no {Husk}, so it cannot leave a " +
					"burnt sprite and has no reason to be invulnerable. This is how the 2026-09-02 leak " +
					$"happened: the marker was put on {Base}, which 16 non-trees share. If this actor " +
					"really should be indestructible, that is a separate gameplay decision — make it " +
					"deliberately and amend this fixture.");
			}
		}
	}
}
