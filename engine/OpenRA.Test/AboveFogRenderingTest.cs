#region Copyright & License Information
/*
 * WW3MOD above-fog rendering tests — the numeric basis for drawing an effect between the fog
 * layers and the unexplored layer.
 *
 * The nuclear fireball used to be darkened cell-by-cell wherever it spanned fogged ground, because
 * fog is COMPOSITED over the world after sprites are drawn (ShroudRenderer.RenderShroud, called
 * from WorldRenderer.Draw after the renderable loop) rather than multiplied into each sprite's
 * tint. SpriteRenderable.Render carries no fog term at all. The fix moves the effect's draw past
 * the fog half of that stack and leaves it under the unexplored half.
 *
 * That split is only safe because the two halves occlude differently, and this file pins the
 * difference. Layer 0 is unexplored ground and its palette entry is ShroudColors[4] =
 * ARGB(255,0,0,0) — fully opaque, so it hides whatever is under it outright. Layers 1..9 are fog,
 * palette FogColors[4] = ARGB(160,0,0,0) — translucent, so they DARKEN what is under them. The
 * first is what keeps an above-fog effect from leaking intelligence about never-explored ground;
 * the second is what produced the seam the change removes.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class AboveFogRenderingTest
	{
		// MapLayersPalettes: ShroudColors[4] is ARGB(255,0,0,0), FogColors[4] is ARGB(160,0,0,0).
		const float ShroudPaletteAlpha = 255f / 255f;
		const float FogPaletteAlpha = 160f / 255f;

		// The shipped mods/ww3mod/rules/world.yaml value.
		const float ShippedFogDarkness = 1.85f;

		// Index of the first FOG layer. ShroudRenderer.RenderFog draws VisionLayers-2 down to this;
		// RenderUnexplored draws everything below it, which is layer 0 alone.
		const int FirstFogLayer = 1;

		static float Transmission(float paletteAlpha, int layer, float fogDarkness)
		{
			return 1f - paletteAlpha * ShroudRenderer.LayerAlpha(layer, fogDarkness);
		}

		[Test]
		public void UnexploredLayerHidesWhatIsDrawnUnderIt()
		{
			// The no-leak guarantee, as a number. An above-fog effect is drawn BEFORE layer 0, so this
			// transmission is exactly how much of it survives over never-explored ground. It must be
			// zero: a nuclear fireball is wide enough to reach ground the player has never seen, and
			// brightening it there would hand over free intelligence rather than just undo a seam.
			Assert.That(Transmission(ShroudPaletteAlpha, 0, 1f), Is.EqualTo(0f),
				"unexplored ground must fully occlude anything drawn beneath it");
			Assert.That(Transmission(ShroudPaletteAlpha, 0, ShippedFogDarkness), Is.EqualTo(0f));
		}

		[Test]
		public void FogLayersDarkenRatherThanHide()
		{
			// The other half of the split, and the reason the bug looked the way it did. Every fog layer
			// lets some of the sprite through, so a fireball spanning a fog boundary read as one
			// continuous object with a hard brightness step across it — a seam — rather than as
			// something partly missing. If any of these ever reached zero, fog would be occluding and
			// the seam would have been a visibility bug instead.
			for (var layer = FirstFogLayer; layer <= 9; layer++)
			{
				Assert.That(Transmission(FogPaletteAlpha, layer, 1f), Is.GreaterThan(0f),
					$"fog layer {layer} must darken, not hide");
				Assert.That(Transmission(FogPaletteAlpha, layer, ShippedFogDarkness), Is.GreaterThan(0f),
					$"fog layer {layer} must darken, not hide, at the shipped FogDarkness");
			}
		}

		[Test]
		public void TheSplitPointIsTheShroudFogBoundary()
		{
			// RenderFog stops at FirstFogLayer and RenderUnexplored takes everything below it. That is
			// only the shroud/fog boundary because layer 0 is the sole layer using the shroud palette
			// and the sole one exempt from FogDarkness. Pinning it here means moving the split without
			// moving the palette assignment in ShroudRenderer.Created fails loudly.
			Assert.That(FirstFogLayer, Is.EqualTo(1));
			Assert.That(ShroudRenderer.LayerAlpha(0, ShippedFogDarkness), Is.EqualTo(1f),
				"layer 0 must be unscaled by FogDarkness — it is shroud, not fog");
			Assert.That(ShroudRenderer.LayerAlpha(FirstFogLayer, ShippedFogDarkness),
				Is.Not.EqualTo(ShroudRenderer.LayerAlpha(FirstFogLayer, 1f)),
				"layer 1 must be scaled by FogDarkness — it is fog, not shroud");
		}
	}
}
