#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using OpenRA.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Mods.Common.Graphics
{
	/// <summary>
	/// Cross-section of the ring band: where inside the band the bright core sits, how far the
	/// transparent feather reaches past the leading edge, and how finely the circle is tessellated.
	/// The three radii are percentages of band width measured outward from the band's inner edge.
	/// </summary>
	public readonly struct ShockwaveRingShape
	{
		/// <summary>Sides in the ring polygon. Lower is cheaper and more angular.</summary>
		public readonly int Segments;

		/// <summary>Outer edge of the bright core.</summary>
		public readonly int PeakOuterPercent;

		/// <summary>Inner edge of the bright core.</summary>
		public readonly int PeakInnerPercent;

		/// <summary>How far the transparent feather overshoots the leading edge.</summary>
		public readonly int OuterFeatherPercent;

		/// <summary>The shape that shipped before any of this was configurable.</summary>
		public static readonly ShockwaveRingShape Default = new ShockwaveRingShape(64, 75, 55, 15);

		public ShockwaveRingShape(int segments, int peakOuterPercent, int peakInnerPercent, int outerFeatherPercent)
		{
			Segments = segments;
			PeakOuterPercent = peakOuterPercent;
			PeakInnerPercent = peakInnerPercent;
			OuterFeatherPercent = outerFeatherPercent;
		}
	}

	/// <summary>
	/// Renders a thick, feathered shockwave ring in world space.
	/// The ring has a sharp leading edge (outer) and a gradual fade on the inside (dust/debris).
	/// Rendered as quad segments with per-vertex alpha for smooth gradients.
	/// </summary>
	public class ExpandingShockwaveRenderable : IRenderable, IFinalizedRenderable
	{
		readonly WPos center;
		readonly WDist radius;
		readonly WDist thickness;
		readonly int zOffset;
		readonly Color color;
		readonly float outerAlpha;
		readonly float innerAlpha;
		readonly ShockwaveRingShape shape;

		/// <param name="center">World position of the blast center (ground level)</param>
		/// <param name="radius">Current outer radius of the shockwave</param>
		/// <param name="thickness">Width of the ring band in WDist</param>
		/// <param name="zOffset">Z-order offset</param>
		/// <param name="color">Base color of the shockwave (RGB, alpha ignored)</param>
		/// <param name="outerAlpha">Alpha at the outer (leading) edge, 0.0-1.0</param>
		/// <param name="innerAlpha">Alpha at the inner (trailing) edge, 0.0-1.0</param>
		/// <param name="shape">Band cross-section and tessellation</param>
		public ExpandingShockwaveRenderable(WPos center, WDist radius, WDist thickness,
			int zOffset, Color color, float outerAlpha, float innerAlpha, in ShockwaveRingShape shape)
		{
			this.center = center;
			this.radius = radius;
			this.thickness = thickness;
			this.zOffset = zOffset;
			this.color = color;
			this.outerAlpha = outerAlpha;
			this.innerAlpha = innerAlpha;
			this.shape = shape;
		}

		public WPos Pos => center;
		public int ZOffset => zOffset;
		public bool IsDecoration => true;

		public IRenderable WithZOffset(int newOffset)
		{
			return new ExpandingShockwaveRenderable(center, radius, thickness, newOffset, color, outerAlpha, innerAlpha, shape);
		}

		public IRenderable OffsetBy(in WVec vec)
		{
			return new ExpandingShockwaveRenderable(center + vec, radius, thickness, zOffset, color, outerAlpha, innerAlpha, shape);
		}

		public IRenderable AsDecoration() { return this; }

		public IFinalizedRenderable PrepareRender(WorldRenderer wr) { return this; }

		public void Render(WorldRenderer wr)
		{
			var cr = Game.Renderer.RgbaColorRenderer;

			var outerR = radius.Length;
			var innerR = Math.Max(0, outerR - thickness.Length);

			// Four concentric rings for soft feathering:
			// outerFade (transparent) → outerPeak (leading edge glow) → innerPeak (core) → innerFade (transparent dust trail)
			// At the default shape the peak zone is narrow (20% of band), with wide fades on both sides.
			//
			// Each percentage is divided by 100f rather than being folded into a literal so that the
			// default values reproduce the previous constants EXACTLY: an int quotient of exact
			// operands is correctly rounded, so 75/100f, 55/100f and 15/100f are bit-for-bit the
			// floats 0.75f, 0.55f and 0.15f that used to be written here. ShockwaveTuningTest pins it.
			var bandWidth = outerR - innerR;
			var peakOuterR = innerR + (int)(bandWidth * (shape.PeakOuterPercent / 100f));   // Peak near outer edge (leading front)
			var peakInnerR = innerR + (int)(bandWidth * (shape.PeakInnerPercent / 100f));   // Peak slightly behind leading front
			var outerFadeR = outerR + (int)(bandWidth * (shape.OuterFeatherPercent / 100f)); // Extend beyond for soft outer fade

			var peakAlpha = Math.Max(outerAlpha, innerAlpha);
			var peakColor = Color.FromArgb((int)(peakAlpha * 255), color.R, color.G, color.B);
			var outerPeakColor = Color.FromArgb((int)(outerAlpha * 255), color.R, color.G, color.B);
			var innerPeakColor = Color.FromArgb((int)(innerAlpha * 0.6f * 255), color.R, color.G, color.B);
			var fadeColor = Color.FromArgb(0, color.R, color.G, color.B);

			for (var i = 0; i < shape.Segments; i++)
			{
				var angle1 = (float)i / shape.Segments * 2 * Math.PI;
				var angle2 = (float)(i + 1) / shape.Segments * 2 * Math.PI;

				var cos1 = (float)Math.Cos(angle1);
				var sin1 = (float)Math.Sin(angle1);
				var cos2 = (float)Math.Cos(angle2);
				var sin2 = (float)Math.Sin(angle2);

				var outerFadePos1 = WorldToScreen(wr, center, outerFadeR, cos1, sin1);
				var outerFadePos2 = WorldToScreen(wr, center, outerFadeR, cos2, sin2);
				var peakOuterPos1 = WorldToScreen(wr, center, peakOuterR, cos1, sin1);
				var peakOuterPos2 = WorldToScreen(wr, center, peakOuterR, cos2, sin2);
				var peakInnerPos1 = WorldToScreen(wr, center, peakInnerR, cos1, sin1);
				var peakInnerPos2 = WorldToScreen(wr, center, peakInnerR, cos2, sin2);
				var innerPos1 = WorldToScreen(wr, center, innerR, cos1, sin1);
				var innerPos2 = WorldToScreen(wr, center, innerR, cos2, sin2);

				// Band 1: Inner fade (transparent → inner peak) — long gradual dust trail
				cr.FillRect(innerPos1, innerPos2, peakInnerPos2, peakInnerPos1,
					fadeColor, fadeColor, innerPeakColor, innerPeakColor);

				// Band 2: Core (inner peak → outer peak) — the densest part of the shockwave
				cr.FillRect(peakInnerPos1, peakInnerPos2, peakOuterPos2, peakOuterPos1,
					innerPeakColor, innerPeakColor, outerPeakColor, outerPeakColor);

				// Band 3: Outer fade (outer peak → transparent) — soft leading edge
				cr.FillRect(peakOuterPos1, peakOuterPos2, outerFadePos2, outerFadePos1,
					outerPeakColor, outerPeakColor, fadeColor, fadeColor);
			}
		}

		static float3 WorldToScreen(WorldRenderer wr, WPos center, int radiusLength, float cos, float sin)
		{
			var worldPos = center + new WVec((int)(radiusLength * cos), (int)(radiusLength * sin), 0);
			return wr.Viewport.WorldToViewPx(wr.ScreenPosition(worldPos));
		}

		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr) { return Rectangle.Empty; }
	}
}
