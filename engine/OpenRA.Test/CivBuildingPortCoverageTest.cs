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
using System.Linq;
using NUnit.Framework;

namespace OpenRA.Test
{
	/// <summary>
	/// Guards the two things that make per-building firing ports work, both of which fail SILENTLY.
	///
	/// <para>ONE: ^CivBuilding must declare no Ports of its own. Per-building ports were introduced by
	/// deleting the shared eight-port ring from the template and giving every actor its own block.
	/// MiniYaml MERGES same-named children, so a ring restored to the template would be ADDED to each
	/// actor's rather than replacing it, and every civilian building would quietly grow eight extra
	/// ports at the old radius. Nothing would error.</para>
	///
	/// <para>TWO: every port must sit outside its building's footprint. This is the defect the
	/// 2026-09-15 lineup captures exposed. The shared ring was at +/-80,+/-280 -- 0.28 cells -- which
	/// is 55% of a 1x1's width but only 27% of a 2x2's and 11% of V37's 5x2, so on anything larger
	/// than one cell the garrison stood inside the building's own art and the sprite drew over it.
	/// GTWR, whose four ports sit at +/-600 on a 1x1, is the shipped counter-example: capture 014
	/// shows all four of its men. The test is that a port lies on or outside the footprint RECTANGLE
	/// -- |X| >= halfX or |Y| >= halfY -- because a man on the west wall is clear of the building
	/// whatever his Y is.</para>
	///
	/// <para>The second one is easy to reintroduce by arithmetic rather than by carelessness: the
	/// first cut of the new geometry used a fixed per-axis radius, which put the 60-degree diagonals
	/// of a six-port ring at (+/-966, +/-645) on a 2x2 of half-extent 1024 -- back inside the
	/// building. Ports are now placed on a ring that circumscribes the footprint along each port's own
	/// bearing. This fixture would have caught that cut.</para>
	///
	/// <para>Note what is NOT asserted: that a port looks right. Z is discarded for the soldier
	/// (GarrisonManager clamps it to terrain level), the sprite art is not the footprint, and neither
	/// is checkable from YAML. Those are judged from captures.</para>
	/// </summary>
	[TestFixture]
	public class CivBuildingPortCoverageTest
	{
		const string Template = "^CivBuilding";

		/// <summary>Concrete actors that still carry the garrison stack. Templates are skipped (they
		/// are never instantiated) and so is anything that removes GarrisonManager -- V19.Husk strips
		/// the whole stack deliberately, and a wreck has no firing ports.</summary>
		static IEnumerable<MiniYamlNode> GarrisonableActors(List<(string File, MiniYamlNode Node)> all)
		{
			var family = ModRulesYaml.DescendantsOf(all, Template);
			foreach (var (_, node) in all)
			{
				if (node.Key.StartsWith("^", System.StringComparison.Ordinal) || !family.Contains(node.Key))
					continue;

				if (node.Value.Nodes.Any(n => n.Key == "-GarrisonManager"))
					continue;

				yield return node;
			}
		}

		[Test]
		public void TemplateDeclaresNoSharedPortRing()
		{
			var all = ModRulesYaml.AllRuleNodes();
			var declaring = all.Where(x => x.Node.Key == Template
				&& ModRulesYaml.ChildNode(ModRulesYaml.ChildNode(x.Node.Value, "GarrisonManager"), "Ports") != null)
				.Select(x => x.File)
				.ToArray();

			Assert.That(declaring, Is.Empty,
				$"{Template} has grown a GarrisonManager/Ports block again (in {string.Join(", ", declaring)}). " +
				"MiniYaml merges same-named children, so this ADDS ports to every civilian building on top " +
				"of the ones it declares for itself rather than replacing them. Per-building ports only " +
				"work while the template declares none.");
		}

		[Test]
		public void EveryGarrisonableActorDeclaresItsOwnPorts()
		{
			var all = ModRulesYaml.AllRuleNodes();
			var actors = GarrisonableActors(all).ToArray();

			Assert.That(actors.Length, Is.GreaterThan(30),
				$"expected the {Template} family to be dozens of actors; found {actors.Length}. " +
				"This fixture is scanning nothing, not passing.");

			var missing = new List<string>();
			foreach (var a in actors)
			{
				var ports = ModRulesYaml.ChildNode(ModRulesYaml.ChildNode(a.Value, "GarrisonManager"), "Ports");
				if (ports == null || ports.Nodes.Count() < 2)
					missing.Add($"{a.Key} ({(ports == null ? "no Ports block" : ports.Nodes.Count() + " port(s)")})");
			}

			Assert.That(missing, Is.Empty,
				"these garrisonable actors do not declare at least two firing ports of their own: " +
				string.Join(", ", missing) + ". GarrisonManagerInfo.Ports defaults to an empty array and " +
				"nothing requires it to be populated, so such an actor loads fine and is silently " +
				"shelter-only -- men go in and no one can ever fire out.");
		}

		[Test]
		public void NoPortSitsInsideItsOwnFootprint()
		{
			var all = ModRulesYaml.AllRuleNodes();
			var offenders = new List<string>();

			foreach (var a in GarrisonableActors(all))
			{
				var dims = ModRulesYaml.Child(ModRulesYaml.ChildNode(a.Value, "Building"), "Dimensions");
				var w = 1;
				var h = 1;
				if (!string.IsNullOrEmpty(dims))
				{
					var parts = dims.Split(',');
					w = int.Parse(parts[0].Trim());
					h = int.Parse(parts[1].Trim());
				}

				// 1024 world units to a cell, so a half-extent is cells * 512.
				var halfX = w * 512;
				var halfY = h * 512;

				var ports = ModRulesYaml.ChildNode(ModRulesYaml.ChildNode(a.Value, "GarrisonManager"), "Ports");
				if (ports == null)
					continue;

				foreach (var p in ports.Nodes)
				{
					var offset = ModRulesYaml.Child(p.Value, "Offset");
					if (string.IsNullOrEmpty(offset))
					{
						offenders.Add($"{a.Key}/{p.Key} declares no Offset");
						continue;
					}

					var xy = offset.Split(',');
					var x = System.Math.Abs(int.Parse(xy[0].Trim()));
					var y = System.Math.Abs(int.Parse(xy[1].Trim()));

					if (x < halfX && y < halfY)
						offenders.Add($"{a.Key}/{p.Key} at ({xy[0].Trim()},{xy[1].Trim()}) inside a {w}x{h} footprint (half-extent {halfX},{halfY})");
				}
			}

			Assert.That(offenders, Is.Empty,
				"these firing ports sit inside their building's own footprint, where the sprite draws " +
				"over the man standing at them: " + string.Join("; ", offenders));
		}
	}
}
