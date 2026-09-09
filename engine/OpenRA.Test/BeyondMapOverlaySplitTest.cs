#region Copyright & License Information
/*
 * WW3MOD: the arithmetic behind splitting WorldRenderer's beyond-map actor overlay into two passes.
 *
 * Over the map the shroud is drawn in two halves with the above-fog renderables between them, so an effect
 * carrying RenderAboveFog: true escapes the FOG and is still hidden by never-explored SHROUD. Outside the cell
 * grid there is no shroud stack, only WorldRenderer's own strips, and those used to be a single pass laid down
 * after everything -- so the same fireball was full brightness on the map and fog-dimmed off it. Splitting the
 * strips along the same seam is what makes an explosion look the same either side of the map edge, which is
 * half of what the user asked for; BeyondMapLightRenderable is the other half.
 *
 * Two properties have to hold, and neither is obvious from reading the two tables side by side. They must be
 * DISJOINT, so no pixel is ever covered by both passes and nothing is quantised twice -- that is what makes a
 * below-fog sprite beyond the grid come out unchanged to the bit rather than merely close. And their UNION has
 * to be the old single table, so the split moved the unexplored layer later without altering what any
 * visibility level is worth.
 */
#endregion

using System;
using NUnit.Framework;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class BeyondMapOverlaySplitTest
	{
		// mods/ww3mod/rules/world.yaml.
		const float ShippedFogDarkness = 1.4f;

		/// <summary>
		/// Stands in for ShroudRenderer so the shipped table builders can be called without a world. Only
		/// FogTransmission is reachable from them, and it answers exactly as ShroudRenderer does
		/// (ShroudRenderer.cs:444-447) -- the composite curve at the mod's FogDarkness. The three draw methods
		/// throw rather than returning quietly: if a table builder ever starts calling one, this test should
		/// fail loudly instead of silently exercising a different code path than the game does.
		/// </summary>
		sealed class FogCurveOnly : IRenderShroud
		{
			readonly float fogDarkness;

			public FogCurveOnly(float fogDarkness) { this.fogDarkness = fogDarkness; }

			public float FogTransmission(int visibility)
			{
				return ShroudRenderer.CompositeTransmission(visibility, fogDarkness);
			}

			public float FogDarkness => fogDarkness;

			public void RenderShroud(WorldRenderer wr) { throw new NotSupportedException(); }
			public void RenderFog(WorldRenderer wr) { throw new NotSupportedException(); }
			public void RenderUnexplored(WorldRenderer wr) { throw new NotSupportedException(); }
		}

		static float[] FogPass()
		{
			return WorldRenderer.BeyondMapFogLayerAlphas(new FogCurveOnly(ShippedFogDarkness));
		}

		/// <summary>What the single pass wrote before the split, at every visibility level.</summary>
		static float SinglePassAlpha(int visibility)
		{
			return 1f - ShroudRenderer.CompositeTransmission(visibility, ShippedFogDarkness);
		}

		[Test]
		public void TheTwoPassesNeverBothDrawAtTheSameVisibility()
		{
			// THE PROPERTY THAT MAKES THE SPLIT FREE. Two black quads at alphas a and b composite to
			// 1 - (1 - a)(1 - b), which is NOT a + b, so a split whose halves overlapped anywhere would darken
			// that level. They do not overlap: the fog table is zero at visibility 0 and the unexplored table
			// is zero everywhere else, so exactly one pass ever has anything to draw. Nothing has to be
			// composited and nothing is rounded to 8 bits twice.
			var fog = FogPass();
			var unexplored = WorldRenderer.BeyondMapUnexploredAlphas();

			Assert.That(fog.Length, Is.EqualTo(MapLayers.VisionLayers));
			Assert.That(unexplored.Length, Is.EqualTo(MapLayers.VisionLayers));

			for (var v = 0; v < MapLayers.VisionLayers; v++)
				Assert.That(fog[v] == 0f || unexplored[v] == 0f, Is.True, $"both passes draw at visibility {v}");
		}

		[Test]
		public void TheUnionIsExactlyTheTableTheSinglePassUsed()
		{
			// The split moved the unexplored layer LATER; it did not change what any level is worth. Asserted
			// exactly rather than within a tolerance, because the two halves are disjoint above: the sum is
			// the surviving entry itself, bit for bit, not an approximation of it.
			var fog = FogPass();
			var unexplored = WorldRenderer.BeyondMapUnexploredAlphas();

			for (var v = 0; v < MapLayers.VisionLayers; v++)
				Assert.That(fog[v] + unexplored[v], Is.EqualTo(SinglePassAlpha(v)), $"visibility {v}");
		}

		[Test]
		public void NeverExploredGroundIsOpaqueAndItIsTheLatePassThatMakesItSo()
		{
			// THE NO-LEAK BOUNDARY, and the reason the fog half is safe to escape at all. A fireball that has
			// wandered past the map edge over ground nobody has scouted must not be visible, and the pass that
			// hides it is the one drawn AFTER the above-fog renderables. If this ever moves into the fog table
			// the effect would be drawn on top of it and burn on unexplored ground.
			Assert.That(WorldRenderer.BeyondMapUnexploredAlphas()[0], Is.EqualTo(1f));
			Assert.That(FogPass()[0], Is.EqualTo(0f));
		}

		[Test]
		public void TheFogHalfIsWhatAnAboveFogEffectNowEscapes()
		{
			// THE DEFECT, as a number. Over fully-fogged ground the old single pass wrote alpha 0.8622, so a
			// nuclear fireball that is deliberately full brightness on the map arrived at 0.1378x of it one
			// cell outside -- a sevenfold step along the map edge, in the sprite channel, exactly mirroring
			// the one BeyondMapLightRenderable removes in the light channel. That whole alpha now sits in the
			// early pass, which an above-fog renderable is drawn after.
			Assert.That(FogPass()[1], Is.EqualTo(0.8622f).Within(5e-4f));
			Assert.That(1f - FogPass()[1], Is.EqualTo(0.1378f).Within(5e-4f));

			// And at full visibility there was never anything to escape: no fog layer is drawn, so neither
			// pass writes anything and the sprite was always at full brightness out there.
			var full = MapLayers.VisionLayers - 1;
			Assert.That(FogPass()[full], Is.EqualTo(0f));
			Assert.That(WorldRenderer.BeyondMapUnexploredAlphas()[full], Is.EqualTo(0f));
		}

		[Test]
		public void BelowFogSpritesKeepTheirExactQuantisedAlpha()
		{
			// A unit or building hanging off the map edge is drawn BEFORE both passes and so is dimmed by both
			// in turn, which has to leave it exactly where the single pass left it. The overlay quantises with
			// (int)(alpha * 255) and skips anything at or under 0.01, so this walks the levels the way
			// DrawBeyondMapActorOverlay does rather than comparing floats it never writes.
			var fog = FogPass();
			var unexplored = WorldRenderer.BeyondMapUnexploredAlphas();

			for (var v = 0; v < MapLayers.VisionLayers; v++)
			{
				var before = Quantised(SinglePassAlpha(v));
				var after = Quantised(fog[v]) + Quantised(unexplored[v]);
				Assert.That(after, Is.EqualTo(before), $"visibility {v}");
			}
		}

		/// <summary>The alpha byte DrawBeyondMapActorOverlay would actually write, or 0 where it skips.</summary>
		static int Quantised(float alpha)
		{
			return alpha > 0.01f ? (int)(alpha * 255) : 0;
		}
	}
}
