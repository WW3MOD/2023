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
	/// The bottom edge of a cameo, laid out at runtime: a caption, a badge, or both, so that the
	/// wording and the marking are data and not pixels.
	/// <para>The shipped cameos bake their caption into the art, which means a wording change is a
	/// re-render and a shared sprite cannot say two different things. Several ww3mod support powers
	/// do share one sprite - three B61-12 yields all draw <c>paranuke</c> - so the baked caption
	/// cannot name them and the icons are indistinguishable in the bin.</para>
	/// <para>Fitted text, never overflowing text. At <c>IconSize: 62, 46</c> with a 1px icon margin,
	/// a caption one pixel too wide bleeds into the neighbouring cameo rather than being clipped, so
	/// <see cref="CameoCaptionCache"/> shortens until it fits and returns null when even one glyph
	/// will not. No ellipsis: a "..." costs three glyphs of the little width there is, and the
	/// intended fix for a caption that does not fit is a shorter caption.</para>
	/// <para>THE CAPTION AND THE BADGE ARE ONE UNIT, not two overlays that happen to share a corner.
	/// Both want the bottom edge - the caption because that is where every baked caption already is,
	/// the badge because the other three corners are spoken for in the production palette (rank
	/// chevron top-left, queue count top-right) - and 62 pixels is not enough for two things that
	/// each place themselves. So the badge is reserved out of the width FIRST and the caption is
	/// centred in what is left, which makes a collision impossible rather than unlikely. It costs
	/// the caption 14 of its 60 pixels; measured against the captions the mod ships, the widest
	/// ("6x750 KT") is 31px at the 7px caption font, so nothing is shortened by this.</para>
	/// </summary>
	public sealed class CameoCaption
	{
		/// <summary>
		/// The text as it will actually be drawn - resolved, trimmed and fitted - or null when this
		/// cameo carries only a badge. Null here means the band and the text are both skipped; it is
		/// not the same as the whole unit being absent, which is a null <see cref="CameoCaption"/>.
		/// </summary>
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

		/// <summary>
		/// Offset from the icon slot's top-left corner to the badge sprite's top-left ink, or null
		/// when there is no badge. The badge is bottom-aligned with the caption but is free to be
		/// TALLER than it and to stand above the band: it is an opaque disc with its own dark ring,
		/// so unlike the text it does not need the band underneath it to be readable. That is what
		/// keeps the band at caption height instead of growing it to badge height, which on a 46-row
		/// slot would have turned a third of the cameo black.
		/// </summary>
		public readonly int2? BadgeOffset;

		public CameoCaption(string text, int2 offset, Rectangle background, int2? badgeOffset)
		{
			Text = text;
			Offset = offset;
			Background = background;
			BadgeOffset = badgeOffset;
		}
	}

	/// <summary>
	/// Resolves, fits and positions cameo captions and badges once each, then hands back the same
	/// instance for every subsequent frame. Both cameo palettes - the production one and the support
	/// power one - own one of these, because the two widgets configure their slots independently.
	/// <para>Measuring and Fluent lookup arrive as delegates rather than as a SpriteFont and a static
	/// provider, which is what lets the fitting rules be asserted in CameoCaptionsTest with no
	/// renderer and no mod loaded. The badge arrives as a SIZE rather than as a sprite for the same
	/// reason: layout here is arithmetic on two integers and needs no graphics device to be tested.</para>
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
		readonly int badgeGap;

		// Null is a real cached answer - "this caption cannot be drawn at all" - and is stored so the
		// fitting loop does not re-run every frame for a caption that will never fit. Keyed on the
		// badge size as well as the raw text because the badge is reserved out of the caption's
		// width, so the same wording lays out differently beside a badge than without one.
		readonly Dictionary<(string Raw, int2 BadgeSize), CameoCaption> cache = new();

		public CameoCaptionCache(Func<string, int2> measure, Func<string, string> resolve, int slotWidth, int slotHeight,
			int sideMargin, int bottomMargin, int backgroundPadding, int badgeGap = 1)
		{
			this.measure = measure;
			this.resolve = resolve;
			this.slotWidth = slotWidth;
			this.slotHeight = slotHeight;
			this.sideMargin = sideMargin;
			this.bottomMargin = bottomMargin;
			this.backgroundPadding = backgroundPadding;
			this.badgeGap = badgeGap;
		}

		/// <summary>
		/// The bottom-edge unit to draw for a raw YAML caption and an already-resolved badge size, or
		/// null to draw nothing. An empty caption and a zero badge size are both the OFF state and are
		/// the default for every actor and every power, so an unconfigured cameo looks exactly as it
		/// did before this existed.
		/// </summary>
		public CameoCaption Get(string raw, int2 badgeSize = default)
		{
			var hasBadge = badgeSize.X > 0 && badgeSize.Y > 0;
			if (string.IsNullOrEmpty(raw) && !hasBadge)
				return null;

			var key = (raw ?? "", hasBadge ? badgeSize : int2.Zero);
			if (cache.TryGetValue(key, out var cached))
				return cached;

			cached = Build(raw, key.Item2);
			cache[key] = cached;
			return cached;
		}

		CameoCaption Build(string raw, int2 badgeSize)
		{
			var hasBadge = badgeSize.X > 0 && badgeSize.Y > 0;

			// The badge is reserved out of the width before the caption is fitted, so the two cannot
			// overlap however long the wording gets. A badge with no caption reserves nothing, because
			// there is no text for it to be pushed away from.
			var reserved = hasBadge ? badgeSize.X + badgeGap : 0;

			// Fitted inside the side margins and the badge's reservation, but centred across what is
			// LEFT of the slot once the badge has taken its block. Centring across the whole slot
			// instead would drift the text right, under the badge, by half the badge's width.
			var text = string.IsNullOrEmpty(raw)
				? null
				: Fit(resolve(raw), measure, slotWidth - 2 * sideMargin - reserved);

			if (text == null && !hasBadge)
				return null;

			// Both sit on the same bottom edge. The badge may be the taller of the two and simply
			// stands further up; neither is centred against the other's height.
			var bottom = slotHeight - bottomMargin;

			int2? badgeOffset = null;
			if (hasBadge)
				badgeOffset = new int2(
					Math.Max(0, slotWidth - sideMargin - badgeSize.X),
					Math.Max(0, bottom - badgeSize.Y));

			if (text == null)
				return new CameoCaption(null, int2.Zero, Rectangle.Empty, badgeOffset);

			var size = measure(text);
			var top = bottom - size.Y;

			// The band is clamped into the slot so a generous padding cannot reach up over the art
			// or down past the cameo's bottom bevel. It stays FULL WIDTH even with a badge present:
			// its job is covering the baked caption underneath, and that runs edge to edge.
			var bandTop = Math.Max(0, top - backgroundPadding);
			var bandBottom = Math.Min(slotHeight, top + size.Y + backgroundPadding);

			return new CameoCaption(text,
				new int2((slotWidth - reserved - size.X) / 2, top),
				Rectangle.FromLTRB(0, bandTop, slotWidth, bandBottom),
				badgeOffset);
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
