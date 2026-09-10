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

namespace OpenRA.Test
{
	/// <summary>
	/// A nuclear blast scar used to stop dead at a river (2026-09-09). Two separate mechanisms cut it
	/// off and both are pinned here, because each one fails silently and neither shows up in a build.
	///
	/// 1. THE TERRAIN GATE. `LeaveSmudgeWarhead` drops any cell whose terrain type does not list the
	///    smudge type in `AcceptsSmudgeType` (LeaveSmudgeWarhead.cs:59-61). `Beach` listed NOTHING, so
	///    the disc ended at the Clear/Beach boundary — a full cell SHORT of the water, leaving a bright
	///    sand band between the black scar and the shoreline. That band was the hard straight edge the
	///    user reported. Beach now takes the five Scar types and only those: adding `Crater`/`Scorch`
	///    there would change every non-nuclear weapon in the mod, which is not what was asked for.
	///
	/// 2. THE ALPHA RAMP. Even landing on the true waterline, a near-solid core-band cell butting
	///    against untouched water is a hard edge. There is no sub-cell land/water data anywhere in the
	///    engine to feather against — `TerrainTileInfo` carries one terrain type per tile and nothing
	///    reads the tile art back — so the only lever is to ramp the whole cell down as the boundary
	///    approaches. `ShoreFadeCells` does that, and it DEFAULTS TO 0: the two stock layers (SCORCH,
	///    CRATER) are byte-identical to before, and only the five Scar layers opt in.
	///
	/// 3. THE RAMP'S OWN TWO ARTEFACTS (2026-09-10), because the fix in 2 went on to cause the next
	///    report — the scar "ending in hard axis-aligned rectangles" where it met water.
	///    (a) SHAPE. The ramp was Chebyshev, whose iso-contours ARE axis-aligned squares. Around a bend
	///        in a river those squares union into a rectangle with corners cells clear of any water.
	///        It is Euclidean now, and the test below pins diagonal-fades-less as the thing that
	///        distinguishes the two.
	///    (b) DEPTH. The ramp fell to 1/(fade+1) = 1/3 on the shoreline cell. The ramp is 2 cells wide
	///        and the rivers on these maps are 2-3 cells wide, so every cell of a ford is within reach
	///        of some water and the WHOLE CROSSING was held at 1/3-2/3 — the bright unscarred sand
	///        block that item 1 above exists to prevent, reintroduced by item 2. `ShoreFadeMinAlpha`
	///        floors the ramp; it defaults to 0, so a layer that does not opt in is unchanged.
	///
	/// The way this rots is letting off-map cells count as boundary: it would draw a half-strength
	/// ring around the entire edge of every map, which reads as a rendering bug and is nowhere near
	/// the code that caused it.
	/// </summary>
	[TestFixture]
	public class ScarShoreFadeTest
	{
		static readonly string[] ScarTypes = { "ScarCore", "ScarCrater", "ScarChar", "ScarBurn", "ScarRim" };

		static string ModDir()
		{
			var dir = Directory.GetCurrentDirectory();
			while (dir != null && !Directory.Exists(Path.Combine(dir, "mods", "ww3mod")))
				dir = Directory.GetParent(dir)?.FullName;

			Assert.That(dir, Is.Not.Null, "could not locate mods/ww3mod above the test working directory");
			return Path.Combine(dir, "mods", "ww3mod");
		}

		// ---- 1. the ramp ------------------------------------------------------------------------

		[TestCase(0, 1f, TestName = "ShoreFadeCells 0 is a no-op — the stock layers must not move")]
		[TestCase(-1, 1f, TestName = "A negative ShoreFadeCells is also a no-op, not a crash")]
		public void DisabledFadeIsExactlyOpaque(int fadeCells, float expected)
		{
			// Boundary immediately adjacent: still full strength, because the fade is off.
			var alpha = SmudgeLayer.ShoreAlphaAt(new CPos(10, 10), fadeCells, 0f, c => c == new CPos(11, 10));
			Assert.That(alpha, Is.EqualTo(expected).Within(0.0001f));
		}

		[Test]
		public void RampRisesOneStepPerCellAwayFromAStraightShore()
		{
			// Water fills the half-plane x >= 20. Cells at x = 19, 18, 17 are 1, 2, 3 cells clear of it.
			// Along a straight orthogonal shore Euclidean and Chebyshev agree exactly, so these are the
			// same numbers the Chebyshev ramp produced — the metric change is invisible here by design.
			static bool IsWater(CPos c) => c.X >= 20;

			Assert.That(SmudgeLayer.ShoreAlphaAt(new CPos(19, 5), 2, 0f, IsWater), Is.EqualTo(1f / 3f).Within(0.0001f));
			Assert.That(SmudgeLayer.ShoreAlphaAt(new CPos(18, 5), 2, 0f, IsWater), Is.EqualTo(2f / 3f).Within(0.0001f));
			Assert.That(SmudgeLayer.ShoreAlphaAt(new CPos(17, 5), 2, 0f, IsWater), Is.EqualTo(1f).Within(0.0001f));
			Assert.That(SmudgeLayer.ShoreAlphaAt(new CPos(3, 5), 2, 0f, IsWater), Is.EqualTo(1f).Within(0.0001f));
		}

		[Test]
		public void TheRampIsEuclideanSoItsContoursAreNotSquares()
		{
			// One single water cell. A diagonal neighbour is further from the water than an orthogonal
			// one — 1.41 against 1 — and must therefore fade LESS.
			//
			// This deliberately reverses the Chebyshev rule that stood here until 2026-09-10. Chebyshev
			// made all eight neighbours equal, which is the same as saying the iso-contour around a water
			// cell is a SQUARE; over a bend in a river those squares union into a rectangle whose corners
			// sit cells clear of any water, and that rectangle is what was reported as the scar ending in
			// hard axis-aligned blocks at the waterline.
			static bool IsWater(CPos c) => c == new CPos(0, 0);

			var orthogonal = SmudgeLayer.ShoreAlphaAt(new CPos(1, 0), 2, 0f, IsWater);
			var diagonal = SmudgeLayer.ShoreAlphaAt(new CPos(1, 1), 2, 0f, IsWater);

			Assert.That(orthogonal, Is.EqualTo(1f / 3f).Within(0.0001f));
			// Fully qualified: this fixture has no `using System;` and does not need one for a single call.
			Assert.That(diagonal, Is.EqualTo((float)System.Math.Sqrt(2) / 3f).Within(0.0001f));
			Assert.That(diagonal, Is.GreaterThan(orthogonal),
				"a diagonal neighbour is further from the water and must fade less, or the contour is a square");
		}

		[Test]
		public void TheFloorIsWhatKeepsANarrowCrossingScarred()
		{
			// A 2-cell river with land either side: the whole crossing is within the 2-cell ramp, so every
			// cell of it is faded. With no floor the shoreline cell falls to 1/3 and the ford reads as a
			// bright unscarred block — the artefact the fade was supposed to remove.
			static bool IsWater(CPos c) => c.X == 20 || c.X == 21;

			Assert.That(SmudgeLayer.ShoreAlphaAt(new CPos(19, 5), 2, 0f, IsWater), Is.EqualTo(1f / 3f).Within(0.0001f));
			Assert.That(SmudgeLayer.ShoreAlphaAt(new CPos(19, 5), 2, 0.7f, IsWater), Is.EqualTo(0.8f).Within(0.0001f));
			Assert.That(SmudgeLayer.ShoreAlphaAt(new CPos(18, 5), 2, 0.7f, IsWater), Is.EqualTo(0.9f).Within(0.0001f));

			// And the floor never pushes a cell that is clear of water above full strength.
			Assert.That(SmudgeLayer.ShoreAlphaAt(new CPos(5, 5), 2, 0.7f, IsWater), Is.EqualTo(1f).Within(0.0001f));
		}

		[TestCase(-0.5f, TestName = "A negative floor is clamped, not propagated as a negative alpha")]
		[TestCase(2f, TestName = "A floor above 1 is clamped to opaque rather than overflowing")]
		public void TheFloorIsClampedToUnitRange(float floor)
		{
			static bool IsWater(CPos c) => c.X >= 20;

			var alpha = SmudgeLayer.ShoreAlphaAt(new CPos(19, 5), 2, floor, IsWater);
			Assert.That(alpha, Is.InRange(0f, 1f));
		}

		[Test]
		public void OffMapCellsDoNotTriggerTheFade()
		{
			// The predicate the trait passes is `Contains(c) && !accepts(c)`, so anything off-map answers
			// false. Modelled here as a map that is only the first quadrant: a cell hard against the
			// corner must still draw at FULL strength. If this flips, every map grows a faded border.
			static bool IsBoundary(CPos c) => false;

			Assert.That(SmudgeLayer.ShoreAlphaAt(new CPos(0, 0), 2, 0f, IsBoundary), Is.EqualTo(1f).Within(0.0001f));
			Assert.That(SmudgeLayer.ShoreAlphaAt(new CPos(0, 0), 2, 0.7f, IsBoundary), Is.EqualTo(1f).Within(0.0001f));
		}

		// ---- 2. the terrain gate ----------------------------------------------------------------

		[TestCase("temperat")]
		[TestCase("snow")]
		[TestCase("desert")]
		public void BeachAcceptsEveryScarBandAndNoOtherSmudge(string tileset)
		{
			var accepts = AcceptsSmudgeType(Path.Combine(ModDir(), "tilesets", tileset + ".yaml"), "Beach");

			Assert.That(accepts, Is.Not.Null, $"{tileset}: Beach has no AcceptsSmudgeType at all — the scar will stop a cell short of the water again");
			foreach (var t in ScarTypes)
				Assert.That(accepts, Does.Contain(t), $"{tileset}: Beach is missing {t}, so that band alone will cut off early");

			Assert.That(accepts, Does.Not.Contain("Crater"), $"{tileset}: Beach must not take conventional craters");
			Assert.That(accepts, Does.Not.Contain("Scorch"), $"{tileset}: Beach must not take conventional scorches");
		}

		[TestCase("temperat")]
		[TestCase("snow")]
		[TestCase("desert")]
		public void WaterStillRefusesEveryScarBand(string tileset)
		{
			// The fade is what softens the shoreline. Letting Water accept a scar would paint the river
			// itself black and make the ramp pointless.
			var accepts = AcceptsSmudgeType(Path.Combine(ModDir(), "tilesets", tileset + ".yaml"), "Water");
			foreach (var t in ScarTypes)
				Assert.That(accepts ?? new List<string>(), Does.Not.Contain(t), $"{tileset}: Water must not accept {t}");
		}

		/// <summary>The AcceptsSmudgeType list of one terrain type, or null if it declares none.</summary>
		static List<string> AcceptsSmudgeType(string tilesetPath, string terrainType)
		{
			var lines = File.ReadAllLines(tilesetPath);
			for (var i = 0; i < lines.Length; i++)
			{
				if (lines[i].Trim() != "Type: " + terrainType)
					continue;

				// Walk this TerrainType block: everything indented under the same parent.
				for (var j = i + 1; j < lines.Length && lines[j].StartsWith("\t\t"); j++)
				{
					var t = lines[j].Trim();
					if (t.StartsWith("AcceptsSmudgeType:"))
						return t.Substring("AcceptsSmudgeType:".Length)
							.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
				}

				return null;
			}

			Assert.Fail($"{tilesetPath}: no TerrainType with Type: {terrainType}");
			return null;
		}
	}
}
