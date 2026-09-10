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

/*
 * A nuclear blast used to leave crop fields looking untouched (2026-09-10), and NOT because the scar
 * was missing. Fields are `GroundCover`, which `BlockingActorsAt` already filters out, so those cells
 * were being marked all along -- the smudge was there, underneath. What hid it was DRAW ORDER.
 *
 * Smudges are a terrain-pass decal: IRenderOverlay is called by TerrainRenderer.RenderTerrain
 * (TerrainRenderer.cs:112), which WorldRenderer.Draw runs at :378. Actor sprites are the renderable
 * loop at :388. Two sequential passes, not one sorted list -- so a field is not "above" the scar in
 * any depth sense that could be reordered, and ^CivField's ZOffset: -8192 (which does keep fields
 * under UNITS, within the sorted pass) has no bearing on it whatever.
 *
 * The fix is a second draw of the same layer from IRenderAboveWorld (:396), which is the next pass
 * after actors. THE WHOLE DIFFICULTY IS THE RESTRICTION, and it is what this fixture pins: a cell is
 * redrawn only when it holds at least one actor and EVERY actor in it is cosmetic ground cover. A
 * tank, a soldier or a building in the cell vetoes it, so a scar can never be painted over a unit.
 *
 * The user chose this outcome over destroying the field actors, and the reason bounds any future
 * "simplification" back to that: "the fields are squares, so if we remove them it wont be perfectly
 * circular."
 *
 * WHAT THIS FIXTURE CANNOT SEE. It pins the rule, not the pixels. Two visual properties are asserted
 * nowhere and were verified by reading only:
 *   - that the field sprite is OPAQUE and covers its cell. If it is not, the terrain-pass copy of the
 *     decal shows through from under it and composites with the over-pass copy, leaving field cells
 *     darker than bare ground. ^CivField draws at RenderSprites.Scale 1.15 specifically to avoid gaps
 *     between neighbours, which is the reason to expect it holds.
 *   - that one 1x1 decal covers the field sprite it is drawn over. Same Scale 1.15 makes the sprite
 *     slightly LARGER than its cell, so a fringe may survive at the edges of a patch.
 * Both need a screenshot. Neither can fail a build.
 */

using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class ScarOverGroundCoverTest
	{
		/// <summary>The five smudge types that make up a nuclear scar.</summary>
		static readonly string[] ScarTypes = { "ScarCore", "ScarCrater", "ScarChar", "ScarBurn", "ScarRim" };

		// Readability only: these stand for "the flag Actor.IsGroundCover() would return".
		const bool Field = true;
		const bool Unit = false;

		static bool DrawsOver(params bool[] occupants)
		{
			return SmudgeLayer.IsGroundCoverOnly(occupants, o => o);
		}

		// ---- 1. the restriction ------------------------------------------------------------------

		[Test]
		public void AScarIsDrawnOverACellHoldingOnlyGroundCover()
		{
			// The headline, and the whole point of the change.
			Assert.That(DrawsOver(Field), Is.True, "a crop field must not hide the scar on its own cell");

			// More than one ground-cover actor in a cell is not the shipped arrangement (^CivField is
			// 1x1 and tiled one per cell), but nothing forbids it and it must not change the answer.
			Assert.That(DrawsOver(Field, Field), Is.True);
		}

		[Test]
		public void AScarIsNeverDrawnOverAUnit()
		{
			// THE ACCEPTANCE BAR. A tank parked on a field cell: the cell holds ground cover, so the
			// naive "does this cell contain a field" test would say yes and paint a nuclear scar across
			// the tank's hull. Every occupant must qualify, not any.
			Assert.That(DrawsOver(Field, Unit), Is.False, "a scar was drawn over a unit standing on a field");

			// Order must not matter. A cell's occupant list is an InfluenceNode chain in insertion
			// order, so which of the two the enumeration reaches first depends on which arrived first --
			// a rule that passed only in one order would work until a unit drove onto ground cover
			// rather than a field being placed under a unit.
			Assert.That(DrawsOver(Unit, Field), Is.False, "the veto depends on occupant order");
		}

		[Test]
		public void AScarIsNeverDrawnOverAnythingThatIsNotGroundCover()
		{
			// Buildings, soldiers, husks, crates: all non-ground-cover, all vetoing. Their cells keep
			// the stock look -- the scar is under them, exactly as ScarUnderActorsTest requires, and it
			// stays covered.
			Assert.That(DrawsOver(Unit), Is.False);
			Assert.That(DrawsOver(Unit, Unit), Is.False);
		}

		[Test]
		public void AnEmptyCellIsNotRedrawn()
		{
			// Deliberate, and the easiest thing to "fix" wrongly. The terrain pass already drew this
			// cell; drawing it a second time composites the decal onto itself, and open ground would
			// come out DARKER than the farmland the feature exists to darken. Emptiness is not
			// "trivially all ground cover" -- it is the case with nothing to draw over.
			Assert.That(DrawsOver(), Is.False, "an empty cell must not be redrawn over itself");
		}

		// ---- 2. the opt-in -----------------------------------------------------------------------

		[Test]
		public void TheCodeDefaultIsTheBaselineLook()
		{
			// A behavioural field on a shared trait defaults to the pre-feature behaviour and is opted
			// in via YAML, so nothing changes for a layer nobody edited. Same contract as
			// ShoreFadeCells: 0 and LeaveSmudgeWarhead.IgnoreActors: false.
			Assert.That(new SmudgeLayerInfo().GroundCoverOverlay, Is.False,
				"GroundCoverOverlay now defaults ON, so every smudge layer in every mod silently gains a " +
				"second render pass. It must be opted in per layer.");
		}

		[Test]
		public void EveryScarBandOptsIn()
		{
			var layers = SmudgeLayers();

			foreach (var type in ScarTypes)
			{
				Assert.That(layers.ContainsKey(type), Is.True, $"world.yaml no longer declares a {type} smudge layer");
				Assert.That(layers[type], Is.EqualTo("true"),
					$"{type} lost GroundCoverOverlay. That band alone will now vanish under crop fields, so a " +
					"blast crossing farmland loses one ring of its gradient and nothing reports it.");
			}
		}

		[Test]
		public void TheStockSmudgeLayersAreLeftAlone()
		{
			// The other half of the scope, pinned from the opposite side -- the same boundary
			// ScarUnderActorsTest draws around IgnoreActors. Scorch and Crater are the RA smudge types
			// every ordinary weapon in the mod shares; opting them in would change conventional craters
			// on farmland, which is not what was asked for.
			var layers = SmudgeLayers();

			foreach (var type in new[] { "Scorch", "Crater" })
			{
				Assert.That(layers.ContainsKey(type), Is.True, $"world.yaml no longer declares a {type} smudge layer");
				Assert.That(layers[type], Is.Null,
					$"the stock {type} layer has been opted in to GroundCoverOverlay. That widens a decision " +
					"made about nuclear scarring onto every ordinary weapon in the mod. Deliberate? Say so in " +
					"the commit message and move it into the scar list above.");
			}
		}

		/// <summary>Every SmudgeLayer in world.yaml, as smudge Type -> its GroundCoverOverlay value or null.</summary>
		static Dictionary<string, string> SmudgeLayers()
		{
			var world = MiniYaml.FromFile(FindRules("world.yaml")).First(n => n.Key == "World");
			var layers = new Dictionary<string, string>();

			foreach (var node in world.Value.Nodes.Where(n => n.Key.StartsWith("SmudgeLayer", System.StringComparison.Ordinal)))
			{
				var type = NodeValue(node.Value, "Type");
				if (type != null)
					layers[type] = NodeValue(node.Value, "GroundCoverOverlay");
			}

			Assert.That(layers.Count, Is.GreaterThan(5),
				$"only {layers.Count} SmudgeLayers parsed out of world.yaml -- this test is no longer reading " +
				"what it claims to, rather than the mod having lost its scar bands.");

			return layers;
		}

		static string NodeValue(MiniYaml node, string key)
		{
			return node.Nodes.FirstOrDefault(n => n.Key == key)?.Value.Value?.Trim();
		}

		static string FindRules(params string[] parts)
		{
			var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(new[] { dir.FullName, "mods", "ww3mod", "rules" }.Concat(parts).ToArray());
				if (File.Exists(candidate))
					return candidate;
			}

			throw new FileNotFoundException($"could not locate mods/ww3mod/rules/{string.Join("/", parts)}");
		}
	}
}
