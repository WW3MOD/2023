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

namespace OpenRA.Primitives
{
	/// <summary>
	/// Spreads the players sharing one relationship band (self / allies / enemies / neutrals) across
	/// distinct lightness steps of that band's base colour, so a viewer can tell two enemies apart
	/// without losing the band's hue identity.
	/// </summary>
	public static class RelationshipShade
	{
		/// <summary>Lightness gap between adjacent players while the band is small enough to afford it.</summary>
		public const float PreferredStep = 0.12f;

		/// <summary>Widest lightness window the ramp may occupy. Compression starts once the band cannot fit PreferredStep inside it.</summary>
		public const float MaxSpan = 0.44f;

		/// <summary>Ramp stays inside these bounds so no shade degenerates to near-black or near-white.</summary>
		public const float MinLightness = 0.18f;
		public const float MaxLightness = 0.82f;

		/// <summary>
		/// Returns the shade of <paramref name="baseColor"/> for the player ranked <paramref name="index"/>
		/// among <paramref name="count"/> players sharing its relationship band. Index 0 is the lightest;
		/// each following index is darker, which is the ordering the ramp is specified against.
		/// Hue and saturation are preserved exactly, so shading can never move a player between bands.
		/// </summary>
		public static Color Shade(Color baseColor, int index, int count)
		{
			// A band with one player is the tuned metric colour untouched — this is the 1v1 case.
			if (count <= 1 || index < 0 || index >= count)
				return baseColor;

			var (a, h, sv, v) = baseColor.ToAhsv();

			// Color offers HSL -> HSV but not the inverse, so derive both HSL terms here.
			// Feeding the HSV saturation straight into FromAhsl would desaturate every shade.
			var l = v * (1 - sv / 2);
			var sl = l <= 0 || l >= 1 ? 0 : (v - l) / Math.Min(l, 1 - l);

			var step = Math.Min(PreferredStep, MaxSpan / (count - 1));
			var half = step * (count - 1) / 2;

			// Slide the window rather than clipping it, so a dark or bright base keeps full separation.
			// MaxSpan < MaxLightness - MinLightness, so the clamp bounds can never cross.
			var centre = Math.Min(Math.Max(l, MinLightness + half), MaxLightness - half);

			return Color.FromAhsl(a, h, sl, centre + half - index * step);
		}
	}
}
