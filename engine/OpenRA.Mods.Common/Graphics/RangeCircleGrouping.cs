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
using OpenRA.Primitives;

namespace OpenRA.Mods.Common.Graphics
{
	public static class RangeCircleGrouping
	{
		// Segments that fall within this much past a peer circle's edge dim too, so arcs meeting near two
		// circles' intersection points fade out instead of visibly crossing each other.
		const int BoundaryMarginPercent = 3;

		/// <summary>
		/// Draws one actor's range circle, dimming the arcs that fall inside an equal-radius circle belonging to
		/// another selected allied actor, so a selected group reads as a single outline rather than a pile of rings.
		/// <paramref name="circlesOn"/> yields the candidate circles a peer actor contributes.
		/// </summary>
		public static RangeCircleAnnotationRenderable Render(
			Actor self, WPos center, WDist range, int zOffset,
			Color color, float width, Color borderColor, float borderWidth,
			Func<Actor, IEnumerable<(WPos Center, WDist Range)>> circlesOn)
		{
			// Only equal-radius peers are collected below, so every peer shares this radius and it is
			// correct to measure them all against the one expanded value computed from our own range.
			var expandedRadius = range.Length + range.Length * BoundaryMarginPercent / 100;
			var expandedRadiusSq = (long)expandedRadius * expandedRadius;

			List<(WPos, long)> peers = null;
			foreach (var a in self.World.Selection.Actors)
			{
				if (a == self || !a.IsInWorld || a.Disposed)
					continue;

				if (!a.Owner.IsAlliedWith(self.World.RenderPlayer))
					continue;

				foreach (var c in circlesOn(a))
				{
					if (c.Range != range)
						continue;

					peers ??= new List<(WPos, long)>();
					peers.Add((c.Center, expandedRadiusSq));
				}
			}

			if (peers == null)
				return new RangeCircleAnnotationRenderable(center, range, zOffset, color, width, borderColor, borderWidth);

			var dimColor = Color.FromArgb(Math.Max(color.A / 4, 3), color);
			var dimBorderColor = Color.FromArgb(Math.Max(borderColor.A / 4, 1), borderColor);

			return new RangeCircleAnnotationRenderable(center, range, zOffset, color, width, borderColor, borderWidth,
				peers.ToArray(), dimColor, dimBorderColor);
		}
	}
}
