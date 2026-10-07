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
	/// The Supply Route is UNTARGETABLE, not indestructible. It has Health: HP: 75000; what protects it
	/// is `Targetable: TargetTypes: NoAutoTarget` on the actor, a target type NO weapon lists, so
	/// Warhead.IsValidAgainst rejects every warhead before damage is computed. `Armor: Indestructable`
	/// is inert. Two things therefore stand between a weapon and the beachhead, and this fixture pins
	/// both: no weapon may list NoAutoTarget, and the SR must keep both the target type and the
	/// `-Vaporizable:` opt-out (VaporizeWarhead skips the target-type test on purpose).
	///
	/// UNVERIFIED AT AUTHORING (2026-10-08, written under a no-build deadline): this file has NOT been
	/// compiled, NOT run under `make check` (analyzers), and NOT run under `dotnet test`. Its YAML
	/// loading is copied from VaporizeScopeTest (RulesDir / TopLevel / FindAll-not-first-match), so it
	/// should read the same file set — every *.yaml under mods/ww3mod/rules, INCLUDING files that
	/// mod.yaml does not list (cameo-captions.yaml, campaign/). That is wider than the shipped Rules
	/// and Weapons lists, which errs toward false RED, never false green. Run at merge:
	///   make check
	///   dotnet test engine/OpenRA.Test/OpenRA.Test.csproj -c Release --filter NoAutoTargetInvariantTest
	/// </summary>
	[TestFixture]
	public class NoAutoTargetInvariantTest
	{
		const string NoAutoTarget = "NoAutoTarget";

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

		static List<MiniYamlNode> TopLevel(string relativeDir)
		{
			var dir = relativeDir == null
				? RulesDir()
				: new DirectoryInfo(Path.Combine(RulesDir().FullName, relativeDir));

			var nodes = new List<MiniYamlNode>();
			foreach (var file in dir.GetFiles("*.yaml", SearchOption.AllDirectories))
				nodes.AddRange(MiniYaml.FromFile(file.FullName));

			Assert.That(nodes.Count, Is.GreaterThan(50),
				$"only {nodes.Count} nodes parsed under {dir.FullName} — this fixture is scanning nothing, not passing.");

			return nodes;
		}

		/// <summary>
		/// Every node declaring <paramref name="key"/>, not the first. Since 2026-09-19
		/// rules/cameo-captions.yaml declares SUPPLYROUTE a second time to caption its cameo, and it
		/// enumerates before ingame/, so first-match returns the caption node. See VaporizeScopeTest.FindAll.
		/// </summary>
		static List<MiniYamlNode> FindAll(IEnumerable<MiniYamlNode> nodes, string key)
		{
			var found = nodes.Where(n => n.Key == key).ToList();
			Assert.That(found, Is.Not.Empty, $"`{key}` is not in the shipped rules — this fixture is pinning nothing.");
			return found;
		}

		static IEnumerable<string> SplitList(string value)
		{
			return (value ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0);
		}

		static void Walk(MiniYamlNode node, string path, ref int validTargetsSeen, List<string> offenders)
		{
			if (node.Key == "ValidTargets")
			{
				validTargetsSeen++;
				if (SplitList(node.Value.Value).Contains(NoAutoTarget))
					offenders.Add(path);
			}

			foreach (var child in node.Value.Nodes)
				Walk(child, path + "." + child.Key, ref validTargetsSeen, offenders);
		}

		[Test]
		public void NoWeaponListsNoAutoTarget()
		{
			var offenders = new List<string>();
			var seen = 0;
			foreach (var weapon in TopLevel("weapons"))
				Walk(weapon, weapon.Key, ref seen, offenders);

			Assert.That(seen, Is.GreaterThan(100),
				$"only {seen} ValidTargets keys found under mods/ww3mod/rules/weapons — the walk is broken, " +
				"not the arsenal clean.");

			Assert.That(offenders, Is.Empty,
				$"{offenders.Count} weapon node(s) list `{NoAutoTarget}` in ValidTargets: " +
				string.Join(", ", offenders) + ". NoAutoTarget is the ONLY thing protecting the Supply Route " +
				"(Health: HP: 75000, no invulnerability; Armor: Indestructable is inert). A weapon that lists it " +
				"can damage the beachhead directly and bypass the contestation siege, which is the mod's win " +
				"condition. If you need a new protected class, invent a new target type instead.");
		}

		[Test]
		public void TheSupplyRouteIsProtectedByTargetTypeAndVaporizeOptOut()
		{
			// FindAll, not first-match: cameo-captions.yaml redeclares SUPPLYROUTE (see FindAll above).
			var sr = FindAll(TopLevel(null), "SUPPLYROUTE");

			var targetTypes = sr
				.SelectMany(d => d.Value.Nodes)
				.Where(n => n.Key == "Targetable" || n.Key.StartsWith("Targetable@", StringComparison.Ordinal))
				.SelectMany(t => t.Value.Nodes)
				.Where(n => n.Key == "TargetTypes")
				.SelectMany(n => SplitList(n.Value.Value))
				.ToList();

			Assert.That(targetTypes, Does.Contain(NoAutoTarget),
				"SUPPLYROUTE's Targetable no longer carries TargetTypes: NoAutoTarget (found: " +
				string.Join(", ", targetTypes) + "). The SR has Health: HP: 75000 and nothing else making it " +
				"untargetable, so every weapon whose ValidTargets match its new type can now kill the beachhead.");

			Assert.That(sr.Any(d => d.Value.Nodes.Any(n => n.Key == "-Vaporizable")), Is.True,
				"SUPPLYROUTE lost its `-Vaporizable:` line. VaporizeWarhead deliberately ignores target types, " +
				"so NoAutoTarget does not protect against it: one nuke would be an instant win.");
		}
	}
}
