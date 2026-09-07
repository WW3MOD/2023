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

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
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
	}
}
