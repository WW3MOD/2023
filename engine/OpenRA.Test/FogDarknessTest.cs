#region Copyright & License Information
/*
 * WW3MOD fog darkness tests — how dark explored-but-unseen ground ends up, and what stays untouched.
 *
 * ShroudRenderer draws the shroud/fog stack as ten separate layers. A cell at visibility v draws every layer from v
 * up to 9, so opacity compounds towards the low-visibility end. Layer 0 is SHROUD (unexplored) and is a different
 * thing from fog: it must stay fully opaque whatever the fog knob is set to, or unexplored ground stops being black.
 *
 * The alpha computed here is only half of what reaches the framebuffer. combined.frag does "c *= vTint", so the
 * shader multiplies this vertex alpha by the layer's palette colour. For the solid fog tile that palette alpha is
 * 160/255: shroud.shp frame 0 is entirely palette index 12, and MapLayersPalettes repeats its colours every 8
 * entries, so index 12 resolves to FogColors[4] = ARGB(160,0,0,0). Blending is GL_ONE / GL_ONE_MINUS_SRC_ALPHA over
 * black, so each layer simply scales the terrain underneath by (1 - paletteAlpha * vertexAlpha), and the layers
 * multiply. That is the whole model, and CompositeTransmission below is it written out.
 *
 * The numbers are pinned rather than recomputed from the formula on purpose — recomputing would assert that the code
 * equals itself and would pass just as happily if the curve were rescaled by accident.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class FogDarknessTest
	{
		// Alpha of the solid fog tile's palette colour: FogColors[4] is ARGB(160,0,0,0).
		const float FogPaletteAlpha = 160f / 255f;

		// The shipped mods/ww3mod/rules/world.yaml value.
		const float ShippedFogDarkness = 1.85f;

		// Fraction of the lit terrain still showing through a cell at visibility v, which draws layers v..9.
		static float CompositeTransmission(int visibility, float fogDarkness)
		{
			var transmission = 1f;
			for (var layer = visibility; layer <= 9; layer++)
				transmission *= 1f - FogPaletteAlpha * ShroudRenderer.LayerAlpha(layer, fogDarkness);

			return transmission;
		}

		[Test]
		public void DefaultDarknessLeavesEveryLayerAtTheEngineBaseline()
		{
			// FogDarkness defaults to 1 so that adding the knob cannot change any mod that does not set it.
			// 1/3, then falling by 1/12 of a third per layer.
			Assert.That(ShroudRenderer.LayerAlpha(1, 1f), Is.EqualTo(1f / 3f).Within(1e-6f));
			Assert.That(ShroudRenderer.LayerAlpha(2, 1f), Is.EqualTo(11f / 36f).Within(1e-6f));
			Assert.That(ShroudRenderer.LayerAlpha(9, 1f), Is.EqualTo(1f / 9f).Within(1e-6f));
		}

		[Test]
		public void ShroudLayerIsNeverScaled()
		{
			// Layer 0 is unexplored ground, not fog. It has to stay at 1 — combined with its own opaque black
			// palette entry that is what makes unexplored ground read as flat black — no matter how dark fog gets.
			Assert.That(ShroudRenderer.LayerAlpha(0, 1f), Is.EqualTo(1f));
			Assert.That(ShroudRenderer.LayerAlpha(0, ShippedFogDarkness), Is.EqualTo(1f));
			Assert.That(ShroudRenderer.LayerAlpha(0, 25f), Is.EqualTo(1f));
		}

		[Test]
		public void ShippedValueIsSeventyFivePercentDarkerAtFullFog()
		{
			// The user-facing claim. Explored ground with no observer resolves to visibility 1 (MapLayers stamps 1
			// on every explored cell that no vision source covers), so it takes the full nine-layer stack.
			var baseline = CompositeTransmission(1, 1f);
			var shipped = CompositeTransmission(1, ShippedFogDarkness);

			// 1.85 is the round number one notch under the exact 75% solution of 1.8544; the 0.2 of a
			// percentage point that costs is far below anything visible, and a tidy YAML value is worth more.
			Assert.That(baseline, Is.EqualTo(0.2557f).Within(0.0005f));
			Assert.That(shipped, Is.EqualTo(0.0644f).Within(0.0005f));
			Assert.That(1f - shipped / baseline, Is.EqualTo(0.748f).Within(0.002f),
				"the shipped FogDarkness must leave fogged terrain ~75% darker than the engine baseline");
		}

		[Test]
		public void FullyVisibleGroundIsUnaffected()
		{
			// Visibility 10 draws no layer at all, so the contrast the knob buys comes entirely from darkening the
			// fogged side. If this ever stops holding, raising FogDarkness would dim the lit map too and the
			// change would be a brightness control rather than a contrast one.
			Assert.That(CompositeTransmission(10, 1f), Is.EqualTo(1f));
			Assert.That(CompositeTransmission(10, ShippedFogDarkness), Is.EqualTo(1f));
		}

		[Test]
		public void NearVisionRingStaysMostlyReadable()
		{
			// WW3MOD's ^StandardVision is a ramp, not a switch: strength 10 out to 4c0, then falling a step every
			// 3c0 to strength 1 at 32c0. So visibility 9 is ground a few cells from your own unit, and darkening it
			// too far would shrink how much of their surroundings a player can actually read.
			Assert.That(CompositeTransmission(9, ShippedFogDarkness), Is.GreaterThan(0.85f));
			Assert.That(CompositeTransmission(7, ShippedFogDarkness), Is.GreaterThan(0.55f));
		}

		[Test]
		public void PerLayerAlphaIsClampedToOpaque()
		{
			// A large FogDarkness must saturate rather than emit an alpha above 1, which would drive the
			// GL_ONE_MINUS_SRC_ALPHA blend factor negative.
			Assert.That(ShroudRenderer.LayerAlpha(1, 100f), Is.EqualTo(1f));
			Assert.That(ShroudRenderer.LayerAlpha(9, 100f), Is.EqualTo(1f));
		}
	}
}
