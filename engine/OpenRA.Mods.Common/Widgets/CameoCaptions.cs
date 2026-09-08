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

namespace OpenRA.Mods.Common.Widgets
{
	/// <summary>
	/// A caption drawn along the bottom of a cameo at runtime, so the wording is data and not pixels.
	/// <para>The shipped cameos bake their caption into the art, which means a wording change is a
	/// re-render and a shared sprite cannot say two different things. Several ww3mod support powers
	/// do share one sprite - three B61-12 yields all draw <c>paranuke</c> - so the baked caption
	/// cannot name them and the icons are indistinguishable in the bin.</para>
	/// <para>Fitted text, never overflowing text. At <c>IconSize: 62, 46</c> with a 1px icon margin,
	/// a caption one pixel too wide bleeds into the neighbouring cameo rather than being clipped, so
	/// <see cref="CameoCaptionCache"/> shortens until it fits and returns null when even one glyph
	/// will not. No ellipsis: a "..." costs three glyphs of the little width there is, and the
	/// intended fix for a caption that does not fit is a shorter caption.</para>
	/// </summary>
	public sealed class CameoCaption
	{
		/// <summary>The text as it will actually be drawn - resolved, trimmed and fitted.</summary>
		public readonly string Text;

		/// <summary>Offset from the icon slot's top-left corner to the text's top-left corner.</summary>
		public readonly int2 Offset;

		/// <summary>
		/// Full-width band behind the text, relative to the icon slot's top-left corner. Drawn only
		/// when a background colour is configured, and the reason it exists is that every shipped
		/// cameo ALREADY has a caption baked into these exact pixels - `paranukeicon` reads
		/// "PARANUKE" across the bottom - so drawing over one without covering it first produces two
		/// overlapping words. A band turns the runtime caption into a replacement rather than an
		/// overlay, which is what makes the wording editable on art that is already captioned.
		/// </summary>
		public readonly Rectangle Background;

		public CameoCaption(string text, int2 offset, Rectangle background)
		{
			Text = text;
			Offset = offset;
			Background = background;
		}
	}

	/// <summary>
	/// Resolves, fits and positions cameo captions once each, then hands back the same instance for
	/// every subsequent frame. Both cameo palettes - the production one and the support power one -
	/// own one of these, because the two widgets configure their slots independently.
	/// <para>Measuring and Fluent lookup arrive as delegates rather than as a SpriteFont and a static
	/// provider, which is what lets the fitting rules be asserted in CameoCaptionsTest with no
	/// renderer and no mod loaded.</para>
	/// </summary>
	public sealed class CameoCaptionCache
	{
		readonly Func<string, int2> measure;
		readonly Func<string, string> resolve;
		readonly int slotWidth;
		readonly int slotHeight;
		readonly int sideMargin;
		readonly int bottomMargin;
		readonly int backgroundPadding;

		// Null is a real cached answer - "this caption cannot be drawn at all" - and is stored so the
		// fitting loop does not re-run every frame for a caption that will never fit.
		readonly Dictionary<string, CameoCaption> cache = new();

		public CameoCaptionCache(Func<string, int2> measure, Func<string, string> resolve, int slotWidth, int slotHeight, int sideMargin, int bottomMargin, int backgroundPadding)
		{
			this.measure = measure;
			this.resolve = resolve;
			this.slotWidth = slotWidth;
			this.slotHeight = slotHeight;
			this.sideMargin = sideMargin;
			this.bottomMargin = bottomMargin;
			this.backgroundPadding = backgroundPadding;
		}

		/// <summary>
		/// The caption to draw for a raw YAML value, or null to draw nothing. Null and empty are the
		/// OFF state and are the default for every actor and every power, so an unconfigured cameo
		/// looks exactly as it did before this existed.
		/// </summary>
		public CameoCaption Get(string raw)
		{
			if (string.IsNullOrEmpty(raw))
				return null;

			if (cache.TryGetValue(raw, out var cached))
				return cached;

			cached = Build(raw);
			cache[raw] = cached;
			return cached;
		}

		CameoCaption Build(string raw)
		{
			// Fitted inside the side margins, but centred across the WHOLE slot. Centring inside the
			// margins instead would be a no-op for a symmetric margin and silently wrong the moment
			// one is not, which is exactly the sort of thing nobody looks at again.
			var text = Fit(resolve(raw), measure, slotWidth - 2 * sideMargin);
			if (text == null)
				return null;

			var size = measure(text);
			var top = slotHeight - bottomMargin - size.Y;

			// The band is clamped into the slot so a generous padding cannot reach up over the art
			// or down past the cameo's bottom bevel.
			var bandTop = Math.Max(0, top - backgroundPadding);
			var bandBottom = Math.Min(slotHeight, top + size.Y + backgroundPadding);

			return new CameoCaption(text,
				new int2((slotWidth - size.X) / 2, top),
				Rectangle.FromLTRB(0, bandTop, slotWidth, bandBottom));
		}

		/// <summary>
		/// The longest leading run of <paramref name="caption"/> that measures no wider than
		/// <paramref name="maxWidth"/>, or null if that is nothing at all. Trailing whitespace is
		/// dropped from a shortened result so a clipped caption does not sit visibly off-centre.
		/// </summary>
		public static string Fit(string caption, Func<string, int2> measure, int maxWidth)
		{
			if (string.IsNullOrWhiteSpace(caption) || maxWidth <= 0)
				return null;

			caption = caption.Trim();
			if (measure(caption).X <= maxWidth)
				return caption;

			for (var length = caption.Length - 1; length > 0; length--)
			{
				var candidate = caption[..length].TrimEnd();
				if (candidate.Length == 0)
					break;

				if (measure(candidate).X <= maxWidth)
					return candidate;
			}

			return null;
		}
	}
}
