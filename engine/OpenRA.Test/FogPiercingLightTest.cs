#region Copyright & License Information
/*
 * WW3MOD: the arithmetic behind FogPiercingLightRenderable.
 *
 * A light reaches the screen as a TerrainLighting TINT applied inside the ordinary sprite and terrain-vertex draw,
 * and ShroudRenderer paints its fog quads over the finished world afterwards. So a light under fog is not hidden,
 * it is ATTENUATED by exactly the fog stack's transmission, and the glow that fixes it has to add back the
 * complement of that transmission -- no more, or a lit cell ends up brighter than an unfogged one; no less, or the
 * seam the user reported is still there, just fainter.
 *
 * Everything below is about that one identity, plus the two boundaries that carry the no-vision-leak guarantee:
 * unexplored ground restores exactly nothing, and fully-visible ground restores exactly nothing either (because
 * nothing was taken from it, and adding anything there would double the light the player can already see).
 */
#endregion

using System;
using NUnit.Framework;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Lighting;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class FogPiercingLightTest
	{
		// mods/ww3mod/rules/world.yaml.
		const float ShippedFogDarkness = 1.4f;

		/// <summary>What FogPiercingLightRenderable multiplies a light's contribution by, per cell.</summary>
		static float Restored(int visibility, float fogDarkness)
		{
			return visibility == 0 ? 0f : 1f - ShroudRenderer.CompositeTransmission(visibility, fogDarkness);
		}

		[Test]
		public void UnexploredGroundRestoresNothing()
		{
			// THE NO-LEAK GUARANTEE AS A NUMBER, and the scope decision the user signed off on: under
			// never-explored shroud the glow contributes exactly zero, so a fireball cannot be seen
			// floating on ground nobody has scouted. Layer 0 is opaque black and erases rather than
			// darkens, so there is no "attenuated" value there to restore in the first place.
			Assert.That(Restored(0, ShippedFogDarkness), Is.EqualTo(0f));
			Assert.That(ShroudRenderer.CompositeTransmission(0, ShippedFogDarkness), Is.EqualTo(0f));
		}

		[Test]
		public void FullyVisibleGroundRestoresNothing()
		{
			// The other end of the same guarantee. At full visibility no fog layer is drawn at all, so
			// TerrainLighting has already put the whole light on screen and the glow must add zero --
			// otherwise every nuke would double its own brightness inside the player's vision.
			var full = MapLayers.VisionLayers - 1;
			Assert.That(ShroudRenderer.CompositeTransmission(full, ShippedFogDarkness), Is.EqualTo(1f));
			Assert.That(Restored(full, ShippedFogDarkness), Is.EqualTo(0f));
		}

		[Test]
		public void RestoredPlusTransmittedIsExactlyOne()
		{
			// The identity the whole renderable rests on: what survives the fog plus what the glow puts
			// back is the unfogged light, at every fog level. If this ever fails the glow is either
			// over- or under-compensating and the fog boundary becomes visible again.
			for (var v = 1; v < MapLayers.VisionLayers; v++)
			{
				var transmitted = ShroudRenderer.CompositeTransmission(v, ShippedFogDarkness);
				Assert.That(transmitted + Restored(v, ShippedFogDarkness), Is.EqualTo(1f).Within(1e-6f),
					$"visibility {v}");
			}
		}

		[Test]
		public void DeepFogSwallowsMostOfTheLight()
		{
			// Pinned rather than recomputed from the formula -- recomputing would only assert the code
			// equals itself. These are the numbers that make the defect worth fixing: at the shipped
			// FogDarkness a fully-fogged cell keeps under a sixth of the light falling on it, so the
			// glow is putting back the large majority of a nuclear flash rather than trimming an edge.
			Assert.That(ShroudRenderer.CompositeTransmission(1, ShippedFogDarkness), Is.EqualTo(0.1378f).Within(5e-4f));
			Assert.That(Restored(1, ShippedFogDarkness), Is.EqualTo(0.8622f).Within(5e-4f));

			// The engine default, for a mod that never touches FogDarkness.
			Assert.That(ShroudRenderer.CompositeTransmission(1, 1f), Is.EqualTo(0.2557f).Within(5e-4f));
		}

		[Test]
		public void TransmissionRisesMonotonicallyWithVisibility()
		{
			// A cell at visibility v draws layers v..VisionLayers-2, so a better-seen cell can only ever
			// have fewer layers over it. A non-monotonic curve would draw the glow in bands.
			for (var v = 1; v < MapLayers.VisionLayers - 1; v++)
				Assert.That(ShroudRenderer.CompositeTransmission(v + 1, ShippedFogDarkness),
					Is.GreaterThan(ShroudRenderer.CompositeTransmission(v, ShippedFogDarkness)), $"visibility {v}");
		}

		[Test]
		public void DarkerFogTransmitsLess()
		{
			// FogDarkness scales every fog layer, so raising it must lower transmission at every fog
			// level and raise what the glow restores. This is what keeps the glow tracking the knob
			// instead of being tuned against one particular setting of it.
			for (var v = 1; v < MapLayers.VisionLayers - 1; v++)
				Assert.That(ShroudRenderer.CompositeTransmission(v, 2f),
					Is.LessThan(ShroudRenderer.CompositeTransmission(v, 1f)), $"visibility {v}");
		}

		[Test]
		public void CompositeMatchesTheLayerCurveItIsBuiltFrom()
		{
			// CompositeTransmission is the single authority for this curve; WorldRenderer.DrawBeyondMapActorFog
			// carries a hand-copy of the per-layer half for the strip outside the map grid. This pins the
			// composite to LayerAlpha and the fog palette's own alpha, so a change to either has to be made
			// deliberately rather than drifting past the glow unnoticed.
			const float FogPaletteAlpha = 160f / 255f;
			for (var v = 1; v < MapLayers.VisionLayers; v++)
			{
				var expected = 1f;
				for (var layer = v; layer <= MapLayers.VisionLayers - 2; layer++)
					expected *= 1f - FogPaletteAlpha * ShroudRenderer.LayerAlpha(layer, ShippedFogDarkness);

				Assert.That(ShroudRenderer.CompositeTransmission(v, ShippedFogDarkness), Is.EqualTo(expected).Within(1e-6f),
					$"visibility {v}");
			}

			Assert.That(ShroudRenderer.FogPaletteAlpha, Is.EqualTo(FogPaletteAlpha));
		}

		// ---------------------------------------------------------------------------------------------
		// The SHAPE of the glow, as opposed to its brightness. Everything above pins how much light the
		// fog ate; everything below pins that putting it back does not draw a grid of tiles while doing
		// so. The user reported exactly that after playing: the falloff resolved into flat squares on the
		// terrain grid, worst in the mid-brightness ring away from the blown-out core.
		// ---------------------------------------------------------------------------------------------

		// mods/ww3mod/rules/weapons/weapons-superweapons.yaml, the FireballLight warhead at its tick-0
		// keyframe -- the brightest instant, and the one the user's screenshot was taken at.
		static readonly WDist ShippedRadius = new WDist(12288);
		const float ShippedPeakIntensity = 7f;
		static readonly float3 ShippedPeakTint = new float3(0xE6 / 255f, 0xF0 / 255f, 0xFF / 255f);
		const LightFalloff ShippedFalloff = LightFalloff.InverseSquare;

		/// <summary>Centre of cell (x, 0) on a rectangular grid, the way Map.CenterOfCell computes it.</summary>
		static WPos CellCentre(int x)
		{
			return new WPos(1024 * x + 512, 512, 0);
		}

		static float3 Contribution(in WPos pos)
		{
			return FogPiercingLightRenderable.ContributionAt(
				CellCentre(0), ShippedRadius, ShippedPeakIntensity, ShippedPeakTint, ShippedFalloff, pos);
		}

		[Test]
		public void AdjacentCellsAgreeOnTheCornerTheyShare()
		{
			// THE PROPERTY THAT MAKES THE GRID GO AWAY. The glow is still one quad per cell -- it has to be,
			// because the fog factor it multiplies by is per-cell data -- but the light inside a quad is now
			// sampled at the four corners and interpolated, so what a viewer sees along a cell boundary is
			// the value both quads computed there. That only holds if the two cells derive the shared corner
			// to the SAME position from their own centres, which is what this asserts: exactly, not within a
			// tolerance, because a half-unit disagreement is a seam.
			for (var x = 0; x < 14; x++)
			{
				var rightOfThisCell = Contribution(CellCentre(x) + new WVec(512, -512, 0));
				var leftOfNextCell = Contribution(CellCentre(x + 1) + new WVec(-512, -512, 0));
				Assert.That(leftOfNextCell, Is.EqualTo(rightOfThisCell), $"cell {x} / {x + 1} top corner");
			}
		}

		[Test]
		public void FlatCentreSamplingWouldStepByTensOfEightBitLevels()
		{
			// THE DEFECT, as a number, so nobody has to re-derive it from a screenshot. Filling each cell
			// flat from its centre made the visible step between two neighbours the whole centre-to-centre
			// difference. Against an 8-bit quantum of 1 level, that is 60-odd levels halfway out and never
			// fewer than about 10 anywhere the glow is drawn at all -- which is why it read as a hard grid
			// rather than as dithering, and why 8-bit rounding in ToColor was never a plausible cause.
			var worst = 0f;
			for (var x = 0; x < 11; x++)
			{
				var here = Contribution(CellCentre(x)).X;
				var next = Contribution(CellCentre(x + 1)).X;
				var levels = (here - next) * 255f;
				Assert.That(levels, Is.GreaterThan(8f), $"step from cell {x} to {x + 1}");
				worst = Math.Max(worst, levels);
			}

			Assert.That(worst, Is.GreaterThan(200f));
		}

		[Test]
		public void TheGlowReachesZeroAtItsOwnRadius()
		{
			// The outer edge is the other place a hard edge can hide. The windowed inverse-square curve is 0
			// exactly at the radius, so the last lit corner fades to nothing instead of the glow stopping on
			// a lit cell -- and beyond the radius there is nothing to draw at all.
			var edge = Contribution(CellCentre(0) + new WVec(ShippedRadius.Length, 0, 0));
			Assert.That(edge.X, Is.EqualTo(0f).Within(1e-6f));
			Assert.That(edge.Y, Is.EqualTo(0f).Within(1e-6f));
			Assert.That(edge.Z, Is.EqualTo(0f).Within(1e-6f));

			var beyond = Contribution(CellCentre(0) + new WVec(ShippedRadius.Length + 1, 0, 0));
			Assert.That(beyond, Is.EqualTo(float3.Zero));
		}

		[Test]
		public void TheFogMaskStillAnnihilatesEveryCornerOfAnUnexploredCell()
		{
			// Interpolating the LIGHT does not interpolate the MASK, and this is the assertion that says so.
			// Each of the four corners is multiplied by its own cell's `lost`, which is 0 under never-explored
			// shroud -- so an unexplored cell contributes nothing at any corner however bright its neighbour
			// is. Smoothing the mask instead would bleed a neighbour's value into that corner and light ground
			// nobody has scouted, which is the one thing this renderable may never do.
			var lost = Restored(0, ShippedFogDarkness);
			foreach (var offset in new[]
			{
				new WVec(-512, -512, 0), new WVec(512, -512, 0), new WVec(512, 512, 0), new WVec(-512, 512, 0)
			})
			{
				var corner = Contribution(CellCentre(0) + offset);
				Assert.That(corner.X, Is.GreaterThan(0f), "the light itself must be non-zero here");
				Assert.That(lost * corner, Is.EqualTo(float3.Zero), $"corner {offset}");
			}
		}

		// mods/ww3mod/maps: every shipped map is MapSize = Bounds + 2 with Bounds origin (1,1),
		// i.e. a one-cell unplayable ring. x-lake is 130,130 against Bounds 1,1,128,128.
		static readonly Rectangle ShippedRing = new Rectangle(1, 1, 128, 128);

		[Test]
		public void TheRingBorrowsTheMaskOfThePlayableCellItAbuts()
		{
			// The band the user reported, as a number. A ring cell is drawn by TerrainRenderer, tinted by
			// TerrainLighting and fogged by ShroudRenderer -- all three run past Bounds -- but GetVisibility
			// answers for the SIMULATION and returns 0 out there, so the restoration used to skip it and the
			// ring came out at bare transmission while the cell one step inside came out whole. At the shipped
			// FogDarkness under full fog that is a factor of seven across one tile boundary.
			var ringAbove = new PPos(40, ShippedRing.Top - 1);
			var clamped = ShroudRenderer.ClampToPlayable(ShippedRing, ringAbove);

			Assert.That(clamped, Is.EqualTo(new PPos(40, ShippedRing.Top)),
				"a ring cell must resolve to the playable cell it abuts, not to itself");

			// Whatever that playable cell restores, the ring now restores the same -- so there is no step.
			Assert.That(Restored(1, ShippedFogDarkness), Is.GreaterThan(0.8f));
			Assert.That(ShroudRenderer.CompositeTransmission(1, ShippedFogDarkness), Is.LessThan(0.15f));
		}

		[Test]
		public void TheRingStaysBlackWhenThePlayableCellItAbutsIsUnexplored()
		{
			// The no-leak guarantee at the new boundary, and the reason widening the sweep is safe. The ring
			// borrows its mask from ONE specific playable cell. If that cell has never been scouted the clamp
			// hands back its 0, restore[0] is 0, and the ring contributes nothing -- which is also exactly the
			// opaque quad the shroud drew there. Widening the sweep cannot light unscouted ground because the
			// mask it widens into is still the mask of a cell inside Bounds.
			var clamped = ShroudRenderer.ClampToPlayable(ShippedRing, new PPos(40, ShippedRing.Top - 1));
			Assert.That(Restored(0, ShippedFogDarkness), Is.EqualTo(0f));
			Assert.That(clamped.V, Is.EqualTo(ShippedRing.Top), "the borrowed cell is inside Bounds");
		}

		[Test]
		public void TheClampIsTheIdentityEverywhereInsideBounds()
		{
			// Mid-map drawing must be untouched: this is what says the widened sweep changes the ring and
			// nothing else. Every corner and centre of the playable area maps to itself.
			foreach (var puv in new[]
			{
				new PPos(ShippedRing.Left, ShippedRing.Top),
				new PPos(ShippedRing.Right - 1, ShippedRing.Bottom - 1),
				new PPos(ShippedRing.Left, ShippedRing.Bottom - 1),
				new PPos(ShippedRing.Right - 1, ShippedRing.Top),
				new PPos(64, 64)
			})
				Assert.That(ShroudRenderer.ClampToPlayable(ShippedRing, puv), Is.EqualTo(puv), $"{puv}");
		}

		[Test]
		public void TheClampNeverLeavesThePlayableArea()
		{
			// The array-safety half. GetVisibility indexes ResolvedVisibility, so a PPos handed to it from the
			// widened sweep has to land inside Bounds however far outside the caller started -- including the
			// diagonal corners, where both axes clamp at once.
			foreach (var puv in new[]
			{
				new PPos(-50, -50), new PPos(500, 500), new PPos(-1, 64), new PPos(64, 999), new PPos(129, 0)
			})
			{
				var c = ShroudRenderer.ClampToPlayable(ShippedRing, puv);
				Assert.That(c.U, Is.InRange(ShippedRing.Left, ShippedRing.Right - 1), $"U of {puv}");
				Assert.That(c.V, Is.InRange(ShippedRing.Top, ShippedRing.Bottom - 1), $"V of {puv}");
			}
		}

		/// <summary>
		/// The curve WorldRenderer.DrawBeyondMapActorFog used to rebuild by hand, reproduced exactly as
		/// it stood before 2026-09-08 so the size of the error it caused can be asserted rather than
		/// described. It is ShroudRenderer.LayerAlpha's product WITHOUT the fog palette's own alpha.
		/// </summary>
		static float LegacyBeyondGridAlpha(int visibility, float fogDarkness)
		{
			if (visibility <= 0)
				return 1f;
			if (visibility >= MapLayers.VisionLayers - 1)
				return 0f;

			var transparency = 1f;
			for (var layer = visibility; layer <= MapLayers.VisionLayers - 2; layer++)
				transparency *= 1f - ShroudRenderer.LayerAlpha(layer, fogDarkness);

			return 1f - transparency;
		}

		/// <summary>What the overlay beyond the cell grid must cost, to match the fog over the map.</summary>
		static float BeyondGridAlpha(int visibility, float fogDarkness)
		{
			return 1f - ShroudRenderer.CompositeTransmission(visibility, fogDarkness);
		}

		[Test]
		public void TheOverlayBeyondTheGridCostsExactlyWhatTheFogOverTheMapCosts()
		{
			// A sprite's pixels that spill past the cell grid are dimmed by DrawBeyondMapActorFog; the
			// pixels one cell further in are dimmed by ShroudRenderer's fog quads. They are the same
			// sprite, so the two must agree at every visibility or the sprite has a step through it.
			for (var v = 0; v < MapLayers.VisionLayers; v++)
				Assert.That(BeyondGridAlpha(v, ShippedFogDarkness),
					Is.EqualTo(1f - ShroudRenderer.CompositeTransmission(v, ShippedFogDarkness)),
					$"visibility {v}");
		}

		[Test]
		public void TheHandRolledCurveWasWrongByMoreThanFourfoldOverFullyFoggedGround()
		{
			// The regression this replaced, as a number. Omitting FogPaletteAlpha made every layer bite
			// harder, so the overlay was too opaque everywhere between the two fixed ends -- worst in
			// relative terms at visibility 1, which is ordinary fully-fogged ground and exactly where a
			// nuclear cloud spilling past the map edge is seen.
			var legacyTransmitted = 1f - LegacyBeyondGridAlpha(1, ShippedFogDarkness);
			var correctTransmitted = 1f - BeyondGridAlpha(1, ShippedFogDarkness);

			Assert.That(legacyTransmitted, Is.LessThan(correctTransmitted),
				"the old curve must be the DARKER of the two");
			Assert.That(correctTransmitted / legacyTransmitted, Is.GreaterThan(4f),
				"a sprite beyond the grid was more than four times too dark at visibility 1");

			// And it was wrong in the same direction at every level that draws an overlay at all.
			for (var v = 1; v < MapLayers.VisionLayers - 1; v++)
				Assert.That(LegacyBeyondGridAlpha(v, ShippedFogDarkness),
					Is.GreaterThan(BeyondGridAlpha(v, ShippedFogDarkness)), $"visibility {v}");
		}

		[Test]
		public void TheOverlayStillAnnihilatesUnexploredAndVanishesAtFullVisibility()
		{
			// The two ends are what keep this change cosmetic rather than a vision change. Beyond the
			// grid under never-explored shroud the overlay is fully opaque, so nothing shows; at full
			// visibility it is absent entirely, so a sprite in the player's own sight is untouched by
			// this change. Every unit that carries vision lights its own border cell to full, which is
			// why the correction cannot brighten a unit the player is already watching.
			Assert.That(BeyondGridAlpha(0, ShippedFogDarkness), Is.EqualTo(1f));
			Assert.That(BeyondGridAlpha(MapLayers.VisionLayers - 1, ShippedFogDarkness), Is.EqualTo(0f));
			Assert.That(LegacyBeyondGridAlpha(0, ShippedFogDarkness), Is.EqualTo(1f));
			Assert.That(LegacyBeyondGridAlpha(MapLayers.VisionLayers - 1, ShippedFogDarkness), Is.EqualTo(0f));
		}
	}
}
