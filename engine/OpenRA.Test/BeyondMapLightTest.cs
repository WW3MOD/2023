#region Copyright & License Information
/*
 * WW3MOD: the arithmetic behind BeyondMapLightRenderable.
 *
 * Past the cell grid there is no terrain to tint and no fog quad to undo -- WorldRenderer.DrawBeyondMapFog fills
 * the region with opaque black, and until this renderable existed nothing put light back on it, so a nuclear
 * flash lit the map and stopped at a rectangle. What it draws there has to end up at exactly the brightness the
 * same light reaches ON the grid once the whole shroud stack has been drawn, or the rectangle is still there,
 * just softer.
 *
 * Two properties carry that, and both are pinned below: the BRIGHTNESS has to equal the on-grid net (which is
 * why the draw happens after WorldRenderer.DrawBeyondMapActorFog rather than in the above-fog slot beside
 * FogPiercingLightRenderable), and the SHAPE has to be continuous across the grid boundary, which it is because
 * the quad on each side derives the corner they share to the same WPos. Plus the boundary that carries the
 * no-vision-leak guarantee: a quad whose governing playable cell is unexplored draws exactly nothing.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Lighting;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class BeyondMapLightTest
	{
		// mods/ww3mod/rules/world.yaml.
		const float ShippedFogDarkness = 1.4f;

		/// <summary>
		/// What a light's contribution is multiplied by ON the grid, after everything: the fog transmits some of
		/// it and, for a GlowAboveFog light, FogPiercingLightRenderable adds the rest back. This is the target
		/// BeyondMapLightRenderable.SurvivalByVisibility has to hit, expressed from the other side so the two are
		/// not the same expression asserting itself.
		/// </summary>
		static float OnGridNet(int visibility, bool glowAboveFog, float fogDarkness)
		{
			var transmitted = ShroudRenderer.CompositeTransmission(visibility, fogDarkness);
			if (visibility == 0 || !glowAboveFog)
				return transmitted;

			return transmitted + (1f - transmitted);
		}

		[Test]
		public void UnexploredGroundIsDrawnBlack()
		{
			// THE NO-LEAK BOUNDARY AS A NUMBER. A quad out here is masked by the visibility of the playable cell
			// ShroudRenderer.ClampToPlayable hands it -- the same cell that chooses the fog strip
			// WorldRenderer.DrawBeyondMapActorFog lays over the same pixels -- and where that cell has never been
			// scouted the light contributes exactly zero. Nothing about the flag changes that: a GlowAboveFog
			// light is not exempt, because the opaque unexplored layer erases rather than darkens and there is no
			// attenuated value under it to restore.
			Assert.That(BeyondMapLightRenderable.SurvivalByVisibility(0, true, ShippedFogDarkness), Is.EqualTo(0f));
			Assert.That(BeyondMapLightRenderable.SurvivalByVisibility(0, false, ShippedFogDarkness), Is.EqualTo(0f));
		}

		[Test]
		public void OffGridBrightnessEqualsTheOnGridNetAtEveryVisibility()
		{
			// THE IDENTITY THE WHOLE RENDERABLE RESTS ON. The rectangle the user reported is the difference
			// between what the light does inside the grid and what it does outside; making that difference zero
			// at every fog level is the fix. Asserted for both flags because a light without GlowAboveFog keeps
			// the fog's bite on the map and must therefore keep it off the map too -- matching means matching
			// whatever the on-grid value happens to be, not always being full brightness.
			for (var v = 0; v < MapLayers.VisionLayers; v++)
			{
				Assert.That(BeyondMapLightRenderable.SurvivalByVisibility(v, true, ShippedFogDarkness),
					Is.EqualTo(OnGridNet(v, true, ShippedFogDarkness)).Within(1e-6f), $"GlowAboveFog, visibility {v}");

				Assert.That(BeyondMapLightRenderable.SurvivalByVisibility(v, false, ShippedFogDarkness),
					Is.EqualTo(OnGridNet(v, false, ShippedFogDarkness)).Within(1e-6f), $"plain, visibility {v}");
			}
		}

		[Test]
		public void AGlowAboveFogLightIsFullBrightnessOnEveryExploredCell()
		{
			// The same identity read the other way, and the reason the fix works over fogged ground at all: on
			// the grid, fog eats about six sevenths of a nuclear flash and FogPiercingLightRenderable puts it
			// straight back, so the on-grid light is full at every explored level. Off the grid it therefore has
			// to be full as well -- not fog-attenuated -- which is only reachable because this draw lands AFTER
			// the beyond-map fog strips rather than under them.
			for (var v = 1; v < MapLayers.VisionLayers; v++)
				Assert.That(BeyondMapLightRenderable.SurvivalByVisibility(v, true, ShippedFogDarkness),
					Is.EqualTo(1f), $"visibility {v}");
		}

		[Test]
		public void DrawingUnderTheBeyondMapFogStripsWouldLeaveTheRectangleBehind()
		{
			// THE ORDERING DECISION, AS A NUMBER, so the next person to move this draw into the above-fog slot
			// beside FogPiercingLightRenderable sees what it costs. Those strips multiply everything under them
			// by the border cell's transmission; a full-brightness quad drawn beneath them would reach the screen
			// at that fraction while the on-grid half of the same light is at 1. Over deeply fogged ground that
			// is a step of more than sevenfold, along precisely the edge this work exists to erase.
			var underTheStrips = ShroudRenderer.CompositeTransmission(1, ShippedFogDarkness);
			Assert.That(underTheStrips, Is.EqualTo(0.1378f).Within(5e-4f));
			Assert.That(BeyondMapLightRenderable.SurvivalByVisibility(1, true, ShippedFogDarkness) / underTheStrips,
				Is.GreaterThan(7f));

			// And over ground the player can actually see there is no strip at all, which is why the defect
			// reads as "the light stops at a rectangle" rather than "the light dims at a rectangle": in the case
			// a player usually watches a nuke in, the two orderings would look identical.
			Assert.That(ShroudRenderer.CompositeTransmission(MapLayers.VisionLayers - 1, ShippedFogDarkness),
				Is.EqualTo(1f));
		}

		// ---------------------------------------------------------------------------------------------
		// The SHAPE across the seam. Everything above pins the brightness; the rest pins that the quads on the
		// two sides of the grid boundary meet without a visible join.
		// ---------------------------------------------------------------------------------------------

		// mods/ww3mod/rules/weapons/weapons-superweapons.yaml, the FireballLight warhead at its tick-0 keyframe.
		static readonly WDist ShippedRadius = new WDist(12288);
		const float ShippedPeakIntensity = 7f;
		static readonly float3 ShippedPeakTint = new float3(0xE6 / 255f, 0xF0 / 255f, 0xFF / 255f);
		const LightFalloff ShippedFalloff = LightFalloff.InverseSquare;

		/// <summary>
		/// Centre of cell (x, 0), the way Map.CenterOfCell computes it on a rectangular grid (Map.cs:1501-1502).
		/// Pure arithmetic with no bounds check, which is exactly why an off-grid quad can be placed on the same
		/// lattice as an on-grid one rather than needing a lattice of its own.
		/// </summary>
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
		public void TheCellsEitherSideOfTheGridEdgeAgreeOnTheCornerTheyShare()
		{
			// THE PROPERTY THAT MAKES THE SEAM GO AWAY, and it is the same one FogPiercingLightTest asserts
			// between two on-grid cells -- what is new is that it still holds when one of the two cells does not
			// exist. Both renderables sample the light at the four corners of a 1024-unit quad derived from the
			// cell centre, and both call the same ContributionAt, so the last real cell and the first virtual one
			// compute the identical value along the boundary they share. Exactly, not within a tolerance: a
			// half-unit disagreement is a visible line down the map edge.
			//
			// x = 6 stands in for the grid edge; the arithmetic does not know or care where MapSize falls.
			const int LastGridCell = 6;
			var insideTop = Contribution(CellCentre(LastGridCell) + new WVec(512, -512, 0));
			var outsideTop = Contribution(CellCentre(LastGridCell + 1) + new WVec(-512, -512, 0));
			Assert.That(outsideTop, Is.EqualTo(insideTop));

			var insideBottom = Contribution(CellCentre(LastGridCell) + new WVec(512, 512, 0));
			var outsideBottom = Contribution(CellCentre(LastGridCell + 1) + new WVec(-512, 512, 0));
			Assert.That(outsideBottom, Is.EqualTo(insideBottom));

			// And the corner is genuinely lit where it matters -- a seam test that only ever compared two zeroes
			// would pass while the light stopped at the edge, which is the bug.
			Assert.That(insideTop.X, Is.GreaterThan(0f));
		}

		[Test]
		public void TheOffGridQuadsContinueTheSameFalloffCurve()
		{
			// The off-grid half is not a separate approximation of the light: it is the same function of position
			// evaluated further out. So brightness has to keep falling monotonically across the boundary and
			// reach zero at the light's own radius, rather than plateauing on a value carried over from the last
			// real cell.
			var previous = Contribution(CellCentre(0)).X;
			for (var x = 1; x <= 12; x++)
			{
				var here = Contribution(CellCentre(x)).X;
				Assert.That(here, Is.LessThan(previous), $"cell {x}");
				previous = here;
			}

			// 12288 units is 12 cells, so a centre at 12c512 is outside the radius and draws nothing at all.
			Assert.That(Contribution(CellCentre(12)), Is.EqualTo(float3.Zero));
		}
	}
}
