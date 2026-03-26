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
	public class RangeCircleAnnotationRenderable : IRenderable, IFinalizedRenderable
	{
		const int RangeCircleSegments = 32;
		static readonly Int32Matrix4x4[] RangeCircleStartRotations = Exts.MakeArray(RangeCircleSegments, i => WRot.FromFacing(8 * i).AsMatrix());
		static readonly Int32Matrix4x4[] RangeCircleEndRotations = Exts.MakeArray(RangeCircleSegments, i => WRot.FromFacing(8 * i + 6).AsMatrix());
		static readonly Int32Matrix4x4[] RangeCircleMidRotations = Exts.MakeArray(RangeCircleSegments, i => WRot.FromFacing(8 * i + 3).AsMatrix());

		readonly WPos centerPosition;
		readonly WDist radius;
		readonly int zOffset;
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
			this.radius = radius;
			this.zOffset = zOffset;
			this.color = color;
			this.width = width;
			this.borderColor = borderColor;
			this.borderWidth = borderWidth;
			this.otherCircles = otherCircles;
			this.dimColor = dimColor;
			this.dimBorderColor = dimBorderColor;
		}

		public WPos Pos => centerPosition;
		public int ZOffset => zOffset;
		public bool IsDecoration => true;

		public IRenderable WithZOffset(int newOffset)
		{
			return new RangeCircleAnnotationRenderable(centerPosition, radius, newOffset, color, width, borderColor, borderWidth, otherCircles, dimColor, dimBorderColor);
		}

		public IRenderable OffsetBy(in WVec vec)
		{
			return new RangeCircleAnnotationRenderable(centerPosition + vec, radius, zOffset, color, width, borderColor, borderWidth, otherCircles, dimColor, dimBorderColor);
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

		public static void DrawRangeCircle(WorldRenderer wr, WPos centerPosition, WDist radius,
			float width, Color color, float borderWidth, Color borderColor)
		{
			var cr = Game.Renderer.RgbaColorRenderer;
			var offset = new WVec(radius.Length, 0, 0);
			for (var i = 0; i < RangeCircleSegments; i++)
			{
				var a = wr.Viewport.WorldToViewPx(wr.ScreenPosition(centerPosition + offset.Rotate(ref RangeCircleStartRotations[i])));
				var b = wr.Viewport.WorldToViewPx(wr.ScreenPosition(centerPosition + offset.Rotate(ref RangeCircleEndRotations[i])));

				if (borderWidth > 0)
					cr.DrawLine(a, b, borderWidth, borderColor);

				if (width > 0)
					cr.DrawLine(a, b, width, color);
			}
		}

		/// <summary>
		/// Draws a range circle with per-segment dimming. Segments whose midpoint
		/// falls inside another circle in the same group render at dimColor/dimBorderColor;
		/// segments on the outer frontier render at full color.
		/// </summary>
		public static void DrawGroupedRangeCircle(WorldRenderer wr, WPos centerPosition, WDist radius,
			float width, Color color, Color dimColor, float borderWidth, Color borderColor, Color dimBorderColor,
			(WPos Center, long RadiusSq)[] otherCircles)
		{
			var cr = Game.Renderer.RgbaColorRenderer;
			var offset = new WVec(radius.Length, 0, 0);
			for (var i = 0; i < RangeCircleSegments; i++)
			{
				var a = wr.Viewport.WorldToViewPx(wr.ScreenPosition(centerPosition + offset.Rotate(ref RangeCircleStartRotations[i])));
				var b = wr.Viewport.WorldToViewPx(wr.ScreenPosition(centerPosition + offset.Rotate(ref RangeCircleEndRotations[i])));

				// Check if midpoint of this segment is inside any other circle in the group
				var mid = centerPosition + offset.Rotate(ref RangeCircleMidRotations[i]);
				var isInner = false;
				for (var j = 0; j < otherCircles.Length; j++)
				{
					var dx = (long)(mid.X - otherCircles[j].Center.X);
					var dy = (long)(mid.Y - otherCircles[j].Center.Y);
					if (dx * dx + dy * dy < otherCircles[j].RadiusSq)
					{
						isInner = true;
						break;
					}
				}

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
