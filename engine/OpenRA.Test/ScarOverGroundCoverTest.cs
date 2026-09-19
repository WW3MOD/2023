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
 * WHAT THIS FIXTURE CANNOT SEE. It pins the rule, not the pixels.
 *   - Whether the field sprite is OPAQUE and covers its cell. If it is not, the terrain-pass copy of
 *     the decal shows through from under it and composites with the over-pass copy, leaving field
 *     cells darker than bare ground. MEASURED 2026-09-19 and it holds: the v14 frame these rigs use
 *     is 100.0% opaque over its 24x24, of which 52.4% is bright wheat
 *     (tools/impact-scar/field_overlay_preview.py decodes it through the engine's own SHP reader).
 *     Still not asserted here -- nothing in OpenRA.Test can decode a sprite -- but no longer a guess.
 *   - That one 1x1 decal covers the field sprite it is drawn over. Same RenderSprites.Scale 1.15
 *     makes the sprite slightly LARGER than its cell, so a fringe may survive at the edges of a
 *     patch. Still needs a screenshot.
 */

using System;
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

		// ---- 3. the cell the restriction above could not take -------------------------------------
		//
		// The rule in section 1 is exact and it has a cost: a cell holding a field AND a vehicle is
		// vetoed, so the crop sprite goes on hiding the terrain-pass decal and the cell reads as bright
		// unburnt wheat inside a black disc. The user's eye found that on 2026-09-19; measured on
		// test-field-swallows-nuke's witness cell 36,14 it was 51% bright wheat against 31.6% on its
		// same-band neighbours (tools/impact-scar/scar_density.py).
		//
		// `GroundCoverOverlayUnderActors` sends those cells down a different path -- one sorted
		// renderable each, into the actor pass, at a ZOffset between the field's and the unit's. The
		// batched pass is untouched and so is its rule, which is why section 1 still passes verbatim.

		[Test]
		public void ACellSharedByGroundCoverAndAUnitIsClassifiedSeparately()
		{
			// THE POINT OF THE THREE-WAY SPLIT. Both of these are "not CoverOnly", which is all the
			// boolean could say, and they want opposite treatment: the first has a hidden scar to
			// rescue, the second has nothing under it to rescue.
			Assert.That(Classify(Field, Unit), Is.EqualTo(SmudgeLayer.GroundCoverOccupancy.Mixed));
			Assert.That(Classify(Unit), Is.EqualTo(SmudgeLayer.GroundCoverOccupancy.None));

			// Order must not matter, for the same reason it must not in AScarIsNeverDrawnOverAUnit: a
			// cell's occupant list is an InfluenceNode chain in arrival order.
			Assert.That(Classify(Unit, Field), Is.EqualTo(SmudgeLayer.GroundCoverOccupancy.Mixed));
		}

		[Test]
		public void TheThreeWaySplitAgreesWithTheBooleanItReplaced()
		{
			// IsGroundCoverOnly is now Classify narrowed, so this cannot drift -- but it is the
			// assertion that says section 1's fixture is still testing the shipped rule and not a copy
			// of it that was left behind.
			foreach (var occupants in new[]
			{
				Array.Empty<bool>(),
				new[] { Field },
				new[] { Field, Field },
				new[] { Unit },
				new[] { Unit, Unit },
				new[] { Field, Unit },
				new[] { Unit, Field }
			})
			{
				var expected = Classify(occupants) == SmudgeLayer.GroundCoverOccupancy.CoverOnly;
				Assert.That(SmudgeLayer.IsGroundCoverOnly(occupants, o => o), Is.EqualTo(expected),
					$"[{string.Join(", ", occupants)}]");
			}
		}

		[Test]
		public void TheUnderActorsPassDefaultsToTheBaselineLook()
		{
			// Same contract as GroundCoverOverlay, ShoreFadeCells and LeaveSmudgeWarhead.IgnoreActors:
			// a behavioural field on a trait every mod shares defaults to the pre-feature behaviour.
			Assert.That(new SmudgeLayerInfo().GroundCoverOverlayUnderActors, Is.False,
				"GroundCoverOverlayUnderActors now defaults ON, so every smudge layer in every mod " +
				"silently starts emitting renderables into the actor pass. It must be opted in per layer.");
		}

		[Test]
		public void TheOverlayZOffsetSitsBetweenGroundCoverAndUnits()
		{
			// THE ACCEPTANCE BAR FOR THIS HALF, and the reason it reads ^CivField rather than restating
			// -8192 here. The renderable is only above the crop and below the vehicle because its
			// ZOffset is strictly between theirs; the primary sort key is Y + Z + ZOffset
			// (WorldRenderer.RenderableZPositionComparisonKey) and nothing else orders them. So the
			// value is DERIVED from the field's, and a future edit to civilian.yaml that this file did
			// not know about has to fail here rather than in a screenshot.
			var fieldZ = CivFieldZOffset();
			var overlayZ = new SmudgeLayerInfo().GroundCoverOverlayZOffset;

			Assert.That(overlayZ, Is.GreaterThan(fieldZ),
				$"the overlay renderable ({overlayZ}) no longer sorts ABOVE ^CivField ({fieldZ}), so the " +
				"crop sprite draws over the scar again and the cell reads as unburnt farmland -- the " +
				"exact defect this pass exists to remove.");

			Assert.That(overlayZ, Is.LessThan(0),
				$"the overlay renderable ({overlayZ}) no longer sorts BELOW a unit, whose ZOffset is 0. " +
				"A scar drawn over a vehicle hull is the one outcome this whole feature is forbidden to " +
				"produce; see AScarIsNeverDrawnOverAUnit.");

			// Half a cell of margin, not one unit. The field actor's render position is not guaranteed
			// to be exactly its cell centre, and a one-unit gap would invert on any sub-cell offset.
			Assert.That(overlayZ - fieldZ, Is.GreaterThanOrEqualTo(512),
				$"the gap between the overlay ({overlayZ}) and ^CivField ({fieldZ}) is under half a cell. " +
				"That is within the range a sprite offset can move a renderable's sort position, so the " +
				"ordering stops being guaranteed and starts being a coincidence.");
		}

		[Test]
		public void EveryScarBandOptsIntoTheUnderActorsPass()
		{
			var layers = SmudgeLayers("GroundCoverOverlayUnderActors");

			foreach (var type in ScarTypes)
			{
				Assert.That(layers.ContainsKey(type), Is.True, $"world.yaml no longer declares a {type} smudge layer");
				Assert.That(layers[type], Is.EqualTo("true"),
					$"{type} lost GroundCoverOverlayUnderActors. Every cell of that band holding a vehicle " +
					"on farmland goes back to showing bright unburnt crop, and nothing reports it.");
			}

			// The other half of the scope. Opting the stock layers in would put renderables in the actor
			// pass for every ordinary crater in the mod, which is not what was asked for.
			foreach (var type in new[] { "Scorch", "Crater" })
				Assert.That(layers[type], Is.Null,
					$"the stock {type} layer has been opted in to GroundCoverOverlayUnderActors. Deliberate? " +
					"Say so in the commit message and move it into the scar list above.");
		}

		/// <summary>^CivField's WithSpriteBody.ZOffset, read from the mod rather than restated.</summary>
		static int CivFieldZOffset()
		{
			var field = MiniYaml.FromFile(FindRules("ingame", "civilian.yaml"))
				.FirstOrDefault(n => n.Key == "^CivField");

			Assert.That(field, Is.Not.Null,
				"^CivField is gone from ingame/civilian.yaml, so the ZOffset this overlay is derived " +
				"from cannot be read. If crop fields were renamed, point this at the new template.");

			var body = field.Value.Nodes.FirstOrDefault(n => n.Key == "WithSpriteBody");
			Assert.That(body, Is.Not.Null, "^CivField no longer carries a WithSpriteBody to take a ZOffset from");

			var z = NodeValue(body.Value, "ZOffset");
			Assert.That(z, Is.Not.Null,
				"^CivField's WithSpriteBody has lost its ZOffset, so fields now sort at 0 like units. " +
				"That is a bigger problem than this overlay: read the PITFALL comment that used to be there.");

			return int.Parse(z, System.Globalization.CultureInfo.InvariantCulture);
		}

		static SmudgeLayer.GroundCoverOccupancy Classify(params bool[] occupants)
		{
			return SmudgeLayer.Classify(occupants, o => o);
		}

		/// <summary>Every SmudgeLayer in world.yaml, as smudge Type -> its GroundCoverOverlay value or null.</summary>
		static Dictionary<string, string> SmudgeLayers()
		{
			return SmudgeLayers("GroundCoverOverlay");
		}

		/// <summary>Every SmudgeLayer in world.yaml, as smudge Type -> the named key's value or null.</summary>
		static Dictionary<string, string> SmudgeLayers(string key)
		{
			var world = MiniYaml.FromFile(FindRules("world.yaml")).First(n => n.Key == "World");
			var layers = new Dictionary<string, string>();

			foreach (var node in world.Value.Nodes.Where(n => n.Key.StartsWith("SmudgeLayer", System.StringComparison.Ordinal)))
			{
				var type = NodeValue(node.Value, "Type");
				if (type != null)
					layers[type] = NodeValue(node.Value, key);
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
