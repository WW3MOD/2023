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
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// Garrison buildings must not eject their occupants. The user's ruling (2026-09-02) is that men
	/// CAN keep sheltering in a wreck — it just becomes progressively lethal, which the
	/// GarrisonProtection curve already delivers — so the engine must not march them outside on top
	/// of it. Cargo's EmergencyBailDamageState defaults to Heavy (50% HP) and its own [Desc] says it
	/// "applies to ground transports only": it was picked for burning APCs, never for buildings.
	///
	/// The bail became reachable on buildings for the first time in four months when
	/// GarrisonBailReachabilityTest's fix scoped the GarrisonProtection guard to the block it names.
	/// That fix is deliberate and stays; this fixture pins the other half — that reviving the
	/// mechanism did not also switch it on for the ~42 garrisonable structures.
	///
	/// The mechanism itself stays live and tunable for vehicles. Nothing here constrains them.
	/// </summary>
	[TestFixture]
	public class GarrisonBailDisabledTest
	{
		const string Off = "Dead";

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

		/// <summary>Every top-level node across every rules file, as (file, node) pairs. Deliberately a
		/// flat list rather than a name-keyed dictionary: a later file redefining an actor must stay
		/// visible here, because that redefinition is exactly the kind of override this fixture exists
		/// to catch.</summary>
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

		static string Child(MiniYaml parent, string key)
		{
			return parent?.Nodes.FirstOrDefault(n => n.Key == key)?.Value.Value?.Trim();
		}

		static MiniYaml ChildNode(MiniYaml parent, string key)
		{
			return parent?.Nodes.FirstOrDefault(n => n.Key == key)?.Value;
		}

		/// <summary>The names a node inherits from, across the Inherits / Inherits@Suffix spellings.</summary>
		static IEnumerable<string> Parents(MiniYamlNode node)
		{
			return node.Value.Nodes
				.Where(n => n.Key == "Inherits" || n.Key.StartsWith("Inherits@", StringComparison.Ordinal))
				.Select(n => n.Value.Value?.Trim())
				.Where(v => !string.IsNullOrEmpty(v));
		}

		[Test]
		public void DeadIsATotalOffSwitchForEveryDamageState()
		{
			// The YAML below is only worth anything if this value really does mean "never". Note the
			// enum is [Flags] with no zero member, so Dead is the only value available to say this.
			foreach (var state in Enum.GetValues(typeof(DamageState)).Cast<DamageState>())
				Assert.That(Cargo.ShouldEmergencyBail(state, DamageState.Dead), Is.False,
					$"ShouldEmergencyBail({state}, Dead) is true — Dead is no longer an off switch, and " +
					"every garrison building's EmergencyBailDamageState: Dead has silently become live.");
		}

		[Test]
		public void TheOffSwitchDoesNotDisableTheBailForEveryoneElse()
		{
			// Guards the obvious wrong fix: satisfying the test above by making ShouldEmergencyBail
			// return false unconditionally. The mechanism must stay live at its shipped vehicle
			// threshold, which is the explicit half of the user's ruling.
			Assert.That(Cargo.ShouldEmergencyBail(DamageState.Heavy, DamageState.Heavy), Is.True,
				"a ground transport at Heavy no longer bails — the mechanism was disabled globally " +
				"rather than opted out of by the garrison templates.");

			Assert.That(Cargo.ShouldEmergencyBail(DamageState.Critical, DamageState.Heavy), Is.True,
				"a ground transport at Critical no longer bails.");

			Assert.That(Cargo.ShouldEmergencyBail(DamageState.Dead, DamageState.Heavy), Is.False,
				"a killing blow now reads as a bail — the Dead exclusion is gone, and a one-shot kill " +
				"on a loaded transport lets the whole squad walk away unhurt.");
		}

		[Test]
		public void EveryActorDefiningGarrisonProtectionTurnsTheBailOff()
		{
			// GarrisonProtection is the marker for "this is a garrison building" — it is what made the
			// bail unreachable for four months, and every garrisonable actor carries it. Requiring the
			// opt-out in the SAME node is what makes a fifth garrison building added later fail here
			// rather than quietly start evicting its occupants.
			var holders = AllRuleNodes()
				.Where(x => ChildNode(x.Node.Value, "GarrisonProtection") != null)
				.ToArray();

			Assert.That(holders.Length, Is.EqualTo(4),
				"expected exactly 4 nodes to define GarrisonProtection (^CivBuilding, GTWR, PBOX, HBOX); " +
				$"found {holders.Length}: {string.Join(", ", holders.Select(h => h.Node.Key))}. If a " +
				"garrison building was added or removed, update this count deliberately.");

			foreach (var (file, node) in holders)
			{
				var cargo = ChildNode(node.Value, "Cargo");
				Assert.That(cargo, Is.Not.Null,
					$"{node.Key} ({file}) defines GarrisonProtection but no Cargo block, so there is " +
					"nowhere for the bail opt-out to live. Occupants are Cargo passengers; if that " +
					"stopped being true, this fixture is pinning the wrong trait.");

				Assert.That(Child(cargo, "EmergencyBailDamageState"), Is.EqualTo(Off),
					$"{node.Key} ({file}) does not set Cargo.EmergencyBailDamageState: {Off}. It " +
					"therefore inherits the C# default of Heavy and will eject its occupants below 50% " +
					"HP — repeatedly, since Load() clears the latch and a building pinned at 1 HP still " +
					"receives zero-damage Damaged notifications. The user ruled buildings must not " +
					"force their men out (2026-09-02).");
			}
		}

		[Test]
		public void NoDescendantOfTheCivBuildingTemplateSwitchesTheBailBackOn()
		{
			// The other half of the claim, and the half that is easy to get wrong by reading. Only
			// ^CivBuilding carries the opt-out for the ~39 civilian structures; each of them reaches it
			// by inheritance. A descendant that re-states EmergencyBailDamageState with any other value
			// wins over the template and silently re-arms the bail for that one actor.
			var all = AllRuleNodes();

			var roots = new HashSet<string> { "^CivBuilding" };
			for (var grew = true; grew;)
			{
				grew = false;
				foreach (var (_, node) in all)
					if (!roots.Contains(node.Key) && Parents(node).Any(roots.Contains))
						grew |= roots.Add(node.Key);
			}

			// If inheritance ever stops reaching these actors, the count collapses and this fails
			// LOUDLY, rather than the fixture below passing vacuously over an empty set.
			Assert.That(roots.Count, Is.GreaterThanOrEqualTo(35),
				$"only {roots.Count} actors resolve to ^CivBuilding — the civilian inheritance chain " +
				"changed shape, so the single opt-out on the template no longer covers what this test " +
				"assumed it covered. Re-check which actors are garrisonable.");

			foreach (var (file, node) in all)
			{
				if (node.Key == "^CivBuilding" || !roots.Contains(node.Key))
					continue;

				var set = Child(ChildNode(node.Value, "Cargo"), "EmergencyBailDamageState");
				Assert.That(set, Is.Null.Or.EqualTo(Off),
					$"{node.Key} ({file}) overrides Cargo.EmergencyBailDamageState to '{set}', " +
					$"overriding ^CivBuilding's '{Off}'. That one building will eject its occupants " +
					"while every other civilian structure does not.");
			}
		}
	}
}
