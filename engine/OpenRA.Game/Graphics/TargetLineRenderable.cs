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

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Primitives;

namespace OpenRA.Graphics
{
	public class TargetLineRenderable : IRenderable, IFinalizedRenderable
	{
		readonly IEnumerable<WPos> waypoints;
		readonly Color color;
		readonly int width;
		readonly int markerSize;
		readonly bool dashed;

		public TargetLineRenderable(IEnumerable<WPos> waypoints, Color color, int width, int markerSize)
			: this(waypoints, color, width, markerSize, false) { }

		public TargetLineRenderable(IEnumerable<WPos> waypoints, Color color, int width, int markerSize, bool dashed)
		{
			this.waypoints = waypoints;
			this.color = color;
			this.width = width;
			this.markerSize = markerSize;
			this.dashed = dashed;
		}

		public WPos Pos => waypoints.First();
		public int ZOffset => 0;
		public bool IsDecoration => true;

		public IRenderable WithZOffset(int newOffset) { return this; }

		public IRenderable OffsetBy(in WVec vec)
		{
			// Lambdas can't use 'in' variables, so capture a copy for later
			var offset = vec;
			return new TargetLineRenderable(waypoints.Select(w => w + offset), color, width, markerSize, dashed);
		}

		public IRenderable AsDecoration() { return this; }

		public IFinalizedRenderable PrepareRender(WorldRenderer wr) { return this; }
		public void Render(WorldRenderer wr)
		{
			if (!waypoints.Any())
				return;

			var lineColor = Color.FromArgb(50, color);
			var first = wr.Viewport.WorldToViewPx(wr.Screen3DPosition(waypoints.First()));
			var a = first;
			foreach (var b in waypoints.Skip(1).Select(pos => wr.Viewport.WorldToViewPx(wr.Screen3DPosition(pos))))
			{
				if (dashed)
					DrawDashedLine(a, b, width, lineColor);
				else
					Game.Renderer.RgbaColorRenderer.DrawLine(a, b, width, lineColor);
				DrawTargetMarker(color, b, markerSize);
				a = b;
			}

			DrawTargetMarker(color, first);
		}

		static void DrawDashedLine(int2 from, int2 to, int width, Color color)
		{
			var dx = to.X - from.X;
			var dy = to.Y - from.Y;
			var lenSq = dx * dx + dy * dy;
			if (lenSq == 0)
				return;

			var length = Math.Sqrt(lenSq);
			// Dash pattern in screen pixels — scales gap with width so wider lines
			// still read as dashed rather than as a solid bar with seams.
			var dashLen = 10.0;
			var gapLen = 6.0 + width;
			var ux = dx / length;
			var uy = dy / length;

			var t = 0.0;
			while (t < length)
			{
				var endT = Math.Min(t + dashLen, length);
				var p1 = new int2((int)Math.Round(from.X + ux * t), (int)Math.Round(from.Y + uy * t));
				var p2 = new int2((int)Math.Round(from.X + ux * endT), (int)Math.Round(from.Y + uy * endT));
				Game.Renderer.RgbaColorRenderer.DrawLine(p1, p2, width, color);
				t += dashLen + gapLen;
			}
		}

		public static void DrawTargetMarker(Color color, int2 screenPos, int size = 1)
		{
			var offset = new int2(size, size);
			var tl = screenPos - offset;
			var br = screenPos + offset;
			Game.Renderer.RgbaColorRenderer.FillRect(tl, br, color);
		}

		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr) { return Rectangle.Empty; }
	}
}
