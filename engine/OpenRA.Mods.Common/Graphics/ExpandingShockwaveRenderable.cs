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
	/// Renders a thick, feathered shockwave ring in world space.
	/// The ring has a sharp leading edge (outer) and a gradual fade on the inside (dust/debris).
	/// Rendered as quad segments with per-vertex alpha for smooth gradients.
	/// </summary>
	public class ExpandingShockwaveRenderable : IRenderable, IFinalizedRenderable
	{
		// More segments = smoother circle. 64 is good for large radii.
		const int Segments = 64;

		readonly WPos center;
		readonly WDist radius;
		readonly WDist thickness;
		readonly int zOffset;
		readonly Color color;
		readonly float outerAlpha;
		readonly float innerAlpha;

		/// <param name="center">World position of the blast center (ground level)</param>
		/// <param name="radius">Current outer radius of the shockwave</param>
		/// <param name="thickness">Width of the ring band in WDist</param>
		/// <param name="zOffset">Z-order offset</param>
		/// <param name="color">Base color of the shockwave (RGB, alpha ignored)</param>
		/// <param name="outerAlpha">Alpha at the outer (leading) edge, 0.0-1.0</param>
		/// <param name="innerAlpha">Alpha at the inner (trailing) edge, 0.0-1.0</param>
		public ExpandingShockwaveRenderable(WPos center, WDist radius, WDist thickness,
			int zOffset, Color color, float outerAlpha, float innerAlpha)
		{
			this.center = center;
			this.radius = radius;
			this.thickness = thickness;
			this.zOffset = zOffset;
			this.color = color;
			this.outerAlpha = outerAlpha;
			this.innerAlpha = innerAlpha;
		}

		public WPos Pos => center;
		public int ZOffset => zOffset;
		public bool IsDecoration => true;

		public IRenderable WithZOffset(int newOffset)
		{
			return new ExpandingShockwaveRenderable(center, radius, thickness, newOffset, color, outerAlpha, innerAlpha);
		}

		public IRenderable OffsetBy(in WVec vec)
		{
			return new ExpandingShockwaveRenderable(center + vec, radius, thickness, zOffset, color, outerAlpha, innerAlpha);
		}

		public IRenderable AsDecoration() { return this; }

		public IFinalizedRenderable PrepareRender(WorldRenderer wr) { return this; }

		public void Render(WorldRenderer wr)
		{
			var cr = Game.Renderer.RgbaColorRenderer;

			var outerR = radius.Length;
			var innerR = Math.Max(0, outerR - thickness.Length);

			// Build three concentric rings for feathering:
			// Inner edge (transparent) -> mid-inner (peak inner alpha) -> mid-outer (peak outer alpha) -> outer edge (sharp then fade)
			// Simplified to: inner transparent -> center of band (peak) -> outer (sharp leading edge)
			var midR = (outerR + innerR) / 2;

			var outerColor = Color.FromArgb((int)(outerAlpha * 255), color.R, color.G, color.B);
			var midColor = Color.FromArgb((int)(Math.Max(outerAlpha, innerAlpha) * 0.8f * 255), color.R, color.G, color.B);
			var innerColor = Color.FromArgb((int)(innerAlpha * 255), color.R, color.G, color.B);
			var fadeColor = Color.FromArgb(0, color.R, color.G, color.B);

			for (var i = 0; i < Segments; i++)
			{
				var angle1 = (float)i / Segments * 2 * Math.PI;
				var angle2 = (float)(i + 1) / Segments * 2 * Math.PI;

				var cos1 = (float)Math.Cos(angle1);
				var sin1 = (float)Math.Sin(angle1);
				var cos2 = (float)Math.Cos(angle2);
				var sin2 = (float)Math.Sin(angle2);

				// Four points per segment at each ring boundary
				var outerPos1 = WorldToScreen(wr, center, outerR, cos1, sin1);
				var outerPos2 = WorldToScreen(wr, center, outerR, cos2, sin2);
				var midPos1 = WorldToScreen(wr, center, midR, cos1, sin1);
				var midPos2 = WorldToScreen(wr, center, midR, cos2, sin2);
				var innerPos1 = WorldToScreen(wr, center, innerR, cos1, sin1);
				var innerPos2 = WorldToScreen(wr, center, innerR, cos2, sin2);

				// Inner band: inner edge (fade) -> mid (peak)
				// Gradual trailing edge (dust settling behind the shockwave)
				cr.FillRect(innerPos1, innerPos2, midPos2, midPos1,
					fadeColor, fadeColor, midColor, midColor);

				// Outer band: mid (peak) -> outer edge (sharp leading front)
				cr.FillRect(midPos1, midPos2, outerPos2, outerPos1,
					midColor, midColor, outerColor, outerColor);
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
