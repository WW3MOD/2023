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

		// Alpha the outer envelope is lifted to while a group is drawn. Prominence here is a CONTRAST property,
		// not an alpha property: annotations blend as dst + a*(src - dst), so whatever alpha gap separates the
		// envelope from the interior arcs gets scaled by how far the circle colour sits from what is underneath.
		// The configured Alpha is tuned for a lone circle and defaults to 35, which left a gap of 27/255 — plenty
		// against the black beyond-map band, a couple of units per channel against lit ground, where the envelope
		// then read as just another dim arc. 120 clears the mod's own "plainly readable" circle alpha of 80.
		const int ProminentAlpha = 120;

		/// <summary>
		/// Envelope and interior colours for a grouped circle. Only the envelope moves: the interior keeps the
		/// quarter-alpha it has always had, so a group looks the same against black and only gains over terrain.
		/// A colour already configured brighter than the floor is left alone rather than pulled down to it.
		/// </summary>
		public static (Color Prominent, Color Dim) Prominence(Color color)
		{
			return (Color.FromArgb(Math.Max((int)color.A, ProminentAlpha), color),
				Color.FromArgb(Math.Max(color.A / 4, 3), color));
		}

		/// <summary>
		/// Distance from a peer's centre within which a segment midpoint counts as interior, in world units.
		/// </summary>
		public static int DimRadius(int radius)
		{
			return radius + radius * BoundaryMarginPercent / 100;
		}

		/// <summary>
		/// True when an equal-radius peer sits close enough that some part of our circle falls inside its dim
		/// radius. Peers further out can never dim a segment, so they are dropped — which is also what keeps a
		/// spread-out selection, whose circles never touch, drawing as plain single circles instead of being
		/// promoted to the louder group styling for no visible reason.
		/// </summary>
		public static bool CanDim(WPos self, WPos peer, int radius)
		{
			var dx = (long)self.X - peer.X;
			var dy = (long)self.Y - peer.Y;
			var reach = (long)radius + DimRadius(radius);

			return dx * dx + dy * dy < reach * reach;
		}

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
			var expandedRadius = DimRadius(range.Length);
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
					if (c.Range != range || !CanDim(center, c.Center, range.Length))
						continue;

					peers ??= new List<(WPos, long)>();
					peers.Add((c.Center, expandedRadiusSq));
				}
			}

			if (peers == null)
				return new RangeCircleAnnotationRenderable(center, range, zOffset, color, width, borderColor, borderWidth);

			var (prominentColor, dimColor) = Prominence(color);

			// The border is left at its configured colour on both sides. BorderWidth defaults to 0 and no shipped
			// actor sets it, so no border line is drawn at all and lifting a black outline to ProminentAlpha would
			// be inventing an appearance nobody can currently see.
			var dimBorderColor = Color.FromArgb(Math.Max(borderColor.A / 4, 1), borderColor);

			return new RangeCircleAnnotationRenderable(center, range, zOffset, prominentColor, width, borderColor, borderWidth,
				peers.ToArray(), dimColor, dimBorderColor);
		}
	}
}
