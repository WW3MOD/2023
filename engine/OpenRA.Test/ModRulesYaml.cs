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
	/// Shared reader for the shipped mod rules, for fixtures that pin a YAML value rather than a code
	/// path. Extracted from GarrisonBailDisabledTest when a second fixture needed the same walk:
	/// directory discovery, the flat node list and — above all — the inheritance closure are subtle
	/// enough that two hand-kept copies would drift, and a drifted copy here does not fail loudly, it
	/// silently scans a smaller set and passes.
	/// </summary>
	public static class ModRulesYaml
	{
		public static DirectoryInfo RulesDir()
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

		/// <summary>Every top-level node across every rules file, as (file, node) pairs. Deliberately a
		/// flat list rather than a name-keyed dictionary: a later file redefining an actor must stay
		/// visible, because that redefinition is exactly the kind of override these fixtures exist to
		/// catch.</summary>
		public static List<(string File, MiniYamlNode Node)> AllRuleNodes()
		{
			var nodes = new List<(string, MiniYamlNode)>();
			foreach (var file in RulesDir().GetFiles("*.yaml", SearchOption.AllDirectories))
				foreach (var node in MiniYaml.FromFile(file.FullName))
					nodes.Add((file.Name, node));

			Assert.That(nodes.Count, Is.GreaterThan(100),
				$"only {nodes.Count} rule nodes parsed — this fixture is scanning nothing, not passing.");

			return nodes;
		}

		public static string Child(MiniYaml parent, string key)
		{
			return parent?.Nodes.FirstOrDefault(n => n.Key == key)?.Value.Value?.Trim();
		}

		public static MiniYaml ChildNode(MiniYaml parent, string key)
		{
			return parent?.Nodes.FirstOrDefault(n => n.Key == key)?.Value;
		}

		/// <summary>The names a node inherits from, across the Inherits / Inherits@Suffix spellings.</summary>
		public static IEnumerable<string> Parents(MiniYamlNode node)
		{
			return node.Value.Nodes
				.Where(n => n.Key == "Inherits" || n.Key.StartsWith("Inherits@", StringComparison.Ordinal))
				.Select(n => n.Value.Value?.Trim())
				.Where(v => !string.IsNullOrEmpty(v));
		}

		/// <summary>Transitive closure of `roots` over the inheritance graph, INCLUDING the roots
		/// themselves. Iterated to a fixed point rather than recursed because a template may be
		/// declared after the actor that inherits it.</summary>
		public static HashSet<string> DescendantsOf(List<(string File, MiniYamlNode Node)> all, params string[] roots)
		{
			var closure = new HashSet<string>(roots);
			for (var grew = true; grew;)
			{
				grew = false;
				foreach (var (_, node) in all)
					if (!closure.Contains(node.Key) && Parents(node).Any(closure.Contains))
						grew |= closure.Add(node.Key);
			}

			return closure;
		}
	}
}
