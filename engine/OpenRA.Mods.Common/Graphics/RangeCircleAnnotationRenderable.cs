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

using OpenRA.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Mods.Common.Graphics
{
	public class RangeCircleAnnotationRenderable : IRenderable, IFinalizedRenderable
	{
		readonly WPos centerPosition;
		readonly WDist radius;
		readonly Color color;
		readonly float width;
		readonly Color borderColor;
		readonly float borderWidth;

		// Grouped rendering: other circles in the same range group
		readonly (WPos Center, long RadiusSq)[] otherCircles;
		readonly Color dimColor;
		readonly Color dimBorderColor;

		public RangeCircleAnnotationRenderable(WPos centerPosition, WDist radius, int zOffset, Color color, float width, Color borderColor, float borderWidth)
			: this(centerPosition, radius, zOffset, color, width, borderColor, borderWidth, null, default, default) { }

		public RangeCircleAnnotationRenderable(WPos centerPosition, WDist radius, int zOffset,
			Color color, float width, Color borderColor, float borderWidth,
			(WPos Center, long RadiusSq)[] otherCircles, Color dimColor, Color dimBorderColor)
		{
			this.centerPosition = centerPosition;
			Pos = centerPosition;
			this.radius = radius;
			ZOffset = zOffset;
			this.color = color;
			this.width = width;
			this.borderColor = borderColor;
			this.borderWidth = borderWidth;
			this.otherCircles = otherCircles;
			this.dimColor = dimColor;
			this.dimBorderColor = dimBorderColor;
		}

		public WPos Pos { get; }
		public int ZOffset { get; }
		public bool IsDecoration => true;

		public IRenderable WithZOffset(int newOffset)
		{
			return new RangeCircleAnnotationRenderable(centerPosition, radius, newOffset, color, width, borderColor, borderWidth, otherCircles, dimColor, dimBorderColor);
		}

		public IRenderable OffsetBy(in WVec vec)
		{
			return new RangeCircleAnnotationRenderable(centerPosition + vec, radius, ZOffset, color, width, borderColor, borderWidth, otherCircles, dimColor, dimBorderColor);
		}

		public IRenderable AsDecoration() { return this; }

		public IFinalizedRenderable PrepareRender(WorldRenderer wr) { return this; }
		public void Render(WorldRenderer wr)
		{
			if (otherCircles != null && otherCircles.Length > 0)
				DrawGroupedRangeCircle(wr, centerPosition, radius, width, color, dimColor, borderWidth, borderColor, dimBorderColor, otherCircles);
			else
				DrawRangeCircle(wr, centerPosition, radius, width, color, borderWidth, borderColor);
		}

		/// <summary>
		/// Screen-space frame of the circle: its centre, and the screen vectors one world radius east and south of
		/// it. WorldRenderer.ScreenPosition is linear in a WPos and every point of the circle shares the centre's
		/// Z, so a dash endpoint at unit direction d is exactly origin + d.X * east + d.Y * south. Computing the
		/// three of them once per circle keeps the whole arc in floating point: there is no integer step anywhere
		/// between the dash angle and the pixel, and the shape stays a true circle (a true ellipse, on a mod whose
		/// tiles are not square) instead of a 128-gon whose corners each sit a fraction of a pixel off the radius.
		/// </summary>
		static (float2 Origin, float2 East, float2 South) ScreenFrame(WorldRenderer wr, WPos center, int radius)
		{
			var origin = wr.ScreenPosition(center);
			return (origin,
				wr.ScreenPosition(center + new WVec(radius, 0, 0)) - origin,
				wr.ScreenPosition(center + new WVec(0, radius, 0)) - origin);
		}

		/// <summary>
		/// Projects a dash endpoint into view pixels WITHOUT snapping it to a whole pixel. Snapping is what
		/// Viewport.WorldToViewPx does, and on a curve it is destructive: each of the 256 endpoints rounds
		/// independently, so adjacent dashes sit at radii up to 1.4px apart and the ring reads ragged at any zoom;
		/// a dash's drawn length wanders by up to 2px against a true length of 3.5px on the smallest shipped
		/// circle; and at the 0.25 zoom floor, where that dash is 0.9px long, both ends land on the same pixel and
		/// RgbaColorRenderer.DrawLine divides by a zero length, writes NaN into all four vertices and draws nothing
		/// at all. Because the snap is taken against TopLeft and Zoom rather than against the world, the whole
		/// pattern also re-rolls whenever the camera scrolls or the selected unit moves, which is what made the
		/// rings shimmer rather than sit still on the ground.
		/// </summary>
		static float2 DashPx(WorldRenderer wr, float2 origin, float2 east, float2 south, float2 dir)
		{
			return wr.Viewport.WorldToViewPxF(origin + dir.X * east + dir.Y * south);
		}

		public static void DrawRangeCircle(WorldRenderer wr, WPos centerPosition, WDist radius,
			float width, Color color, float borderWidth, Color borderColor)
		{
			var cr = Game.Renderer.RgbaColorRenderer;
			var (origin, east, south) = ScreenFrame(wr, centerPosition, radius.Length);
			for (var i = 0; i < RangeCircleGeometry.Segments; i++)
			{
				var a = DashPx(wr, origin, east, south, RangeCircleGeometry.DashStartDir[i]);
				var b = DashPx(wr, origin, east, south, RangeCircleGeometry.DashEndDir[i]);

				if (borderWidth > 0)
					cr.DrawLine(a, b, borderWidth, borderColor);

				if (width > 0)
					cr.DrawLine(a, b, width, color);
			}
		}

		/// <summary>
		/// Draws a range circle with per-dash dimming. Dashes whose midpoint falls inside another circle in the
		/// same group render at dimColor/dimBorderColor; dashes on the outer frontier render at full colour.
		/// </summary>
		public static void DrawGroupedRangeCircle(WorldRenderer wr, WPos centerPosition, WDist radius,
			float width, Color color, Color dimColor, float borderWidth, Color borderColor, Color dimBorderColor,
			(WPos Center, long RadiusSq)[] otherCircles)
		{
			var cr = Game.Renderer.RgbaColorRenderer;
			var (origin, east, south) = ScreenFrame(wr, centerPosition, radius.Length);
			for (var i = 0; i < RangeCircleGeometry.Segments; i++)
			{
				var a = DashPx(wr, origin, east, south, RangeCircleGeometry.DashStartDir[i]);
				var b = DashPx(wr, origin, east, south, RangeCircleGeometry.DashEndDir[i]);

				// The interior test is world-space and so is the dash it colours, so the boundary between the
				// emphasised envelope and the dimmed interior stays on the same patch of ground as the camera moves.
				var mid = RangeCircleGeometry.MidPos(centerPosition, radius.Length, i);
				var isInner = RangeCircleGeometry.IsInterior(mid, otherCircles);

				var segColor = isInner ? dimColor : color;
				var segBorder = isInner ? dimBorderColor : borderColor;

				if (borderWidth > 0)
					cr.DrawLine(a, b, borderWidth, segBorder);

				if (width > 0)
					cr.DrawLine(a, b, width, segColor);
			}
		}

		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr) { return Rectangle.Empty; }
	}
}
