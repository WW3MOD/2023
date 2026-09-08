#region Copyright & License Information
/*
 * WW3MOD caption-band geometry test — reads the SHIPPED chrome, because the bug this exists to
 * catch was in the configuration and not in the code.
 *
 * The runtime caption band is drawn behind a cameo caption so the caption REPLACES the lettering
 * baked into the art rather than overlapping it (CameoCaptions.cs). Every cameo the mod ships has
 * such lettering, and it runs to the bottom of the art.
 *
 * The band's rows are `[top - padding, top + lineHeight + padding]` clamped into the slot, where
 * `top = slotHeight - bottomMargin - lineHeight` — so its bottom is `slotHeight - bottomMargin +
 * padding`, and it reaches the slot's last row only when PADDING >= BOTTOM MARGIN.
 *
 * THAT DID NOT HOLD WHEN THE FEATURE SHIPPED on 2026-09-08. Both palettes took the engine's default
 * padding of 1 against a bottom margin of 2, so the band covered slot rows 36–44 while the baked
 * ink's last row is slot row 45 — verified on all seven cameos the power roster uses, each of which
 * has its last bright row at sprite row 46, which is slot row 45 once IconSpriteOffset -1,-1 is
 * applied. The result was a dotted line of the old caption surviving under every new one, on every
 * captioned cameo in the mod. It was found by rendering the sidebar offline (tools/cameo/binmock.py)
 * and not in play, which is the whole reason this file exists: nothing else would have said so.
 *
 * WHY IT READS THE YAML RATHER THAN ASSERTING NUMBERS. CameoCaptionsTest already pins the
 * arithmetic; a second copy of it would have passed happily while the shipped chrome stayed wrong.
 * The only thing that catches a configuration bug is reading the configuration.
 *
 * The widgets state BOTH keys explicitly so that this fixture never has to keep a copy of the
 * engine defaults — a copy that would silently rot the day someone changed one. A widget that
 * draws a band and omits either key fails here rather than being assumed correct.
 *
 * SCOPE, HONESTLY. Geometry only, and only for widgets that actually draw a band (a transparent
 * CaptionBackgroundColor draws nothing, so the relationship does not matter). It does NOT check
 * that the art's baked ink really ends where the comment says — that was measured once, offline,
 * against the seven cameos in use, and new art could differ.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace OpenRA.Test
{
	[TestFixture]
	public class CameoCaptionBandTest
	{
		/// <summary>
		/// The last row of a cameo's BAKED caption, in icon-slot coordinates. Measured, not chosen:
		/// every shipped cameo puts its lettering on sprite rows 42-46 of a 64x48 sheet, pure white
		/// with a 1px dark surround and a cap height of 5, and IconSpriteOffset -1,-1 makes sprite
		/// row 46 slot row 45. Confirmed on all seven the power roster uses - precicon, paranukeicon,
		/// atomicon, atomfakeicon, cmissicon, v2bdgricon - plus the photo cameo built by tools/cameo,
		/// whose bevel lands on the same row.
		/// <para>This is a fact about the ART, so it can go stale in a way no test can see: new art
		/// that puts its lettering elsewhere would make this number wrong rather than make a test
		/// fail. Re-measure with tools/cameo/binmock.py if the house style ever moves.</para>
		/// </summary>
		const int BakedInkLastSlotRow = 45;

		static string FindMod(params string[] relative)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(new[] { dir.FullName, "mods", "ww3mod" }.Concat(relative).ToArray());
				if (File.Exists(candidate))
					return candidate;
			}

			throw new FileNotFoundException("could not locate mods/ww3mod/" + string.Join("/", relative));
		}

		static string Field(MiniYamlNode node, string key)
		{
			return node.Value.Nodes.FirstOrDefault(n => n.Key == key)?.Value.Value?.Trim();
		}

		/// <summary>Every widget node anywhere in the tree whose key starts with one of the prefixes.</summary>
		static IEnumerable<MiniYamlNode> Widgets(IEnumerable<MiniYamlNode> nodes, params string[] prefixes)
		{
			foreach (var n in nodes)
			{
				if (n.Key != null && prefixes.Any(p => n.Key.StartsWith(p, StringComparison.Ordinal)))
					yield return n;

				foreach (var inner in Widgets(n.Value.Nodes, prefixes))
					yield return inner;
			}
		}

		static IEnumerable<MiniYamlNode> BandDrawingPalettes()
		{
			var chrome = MiniYaml.FromFile(FindMod("chrome", "ingame-player.yaml"));
			return Widgets(chrome, "SupportPowers", "ProductionPalette")
				.Where(w =>
				{
					// A fully transparent background draws nothing, so the geometry is moot.
					var colour = Field(w, "CaptionBackgroundColor");
					return colour != null && !colour.EndsWith("00", StringComparison.OrdinalIgnoreCase);
				});
		}

		[Test]
		public void EveryPaletteThatDrawsABandCoversTheBakedCaptionUnderneathIt()
		{
			var palettes = BandDrawingPalettes().ToArray();

			// If this trips, either the widgets were renamed or the band was turned off wholesale.
			// Both are things somebody should have to notice rather than silently lose the check.
			Assert.That(palettes, Is.Not.Empty,
				"no cameo palette in chrome/ingame-player.yaml draws a caption band; if that is " +
				"deliberate this fixture is now checking nothing and should be deleted with a reason");

			foreach (var w in palettes)
			{
				var marginText = Field(w, "CaptionBottomMargin");
				var paddingText = Field(w, "CaptionBackgroundPadding");

				Assert.That(marginText, Is.Not.Null,
					$"{w.Key} draws a caption band but leaves CaptionBottomMargin implicit. State it: " +
					"this fixture compares it against the padding and will not assume an engine default.");
				Assert.That(paddingText, Is.Not.Null,
					$"{w.Key} draws a caption band but leaves CaptionBackgroundPadding implicit. State it.");

				var margin = int.Parse(marginText, CultureInfo.InvariantCulture);
				var padding = int.Parse(paddingText, CultureInfo.InvariantCulture);

				// THE ANCHOR. The generated caption's last ink row is `IconSize.Y - margin - 1`:
				// CameoCaptionCache puts the line box at `slotHeight - margin - lineHeight`,
				// SpriteFont.DrawText adds `size` to reach the baseline (SpriteFont.cs:99), and
				// lineHeight IS size, so the font cancels out and this holds for any caption font.
				// It has to equal the row the BAKED lettering ends on, which is measured, not
				// chosen -- see BakedInkLastSlotRow.
				var slotHeight = int.Parse(
					(Field(w, "IconSize") ?? throw new AssertionException($"{w.Key} sets no IconSize"))
					.Split(',')[1].Trim(), CultureInfo.InvariantCulture);

				Assert.That(slotHeight - margin - 1, Is.EqualTo(BakedInkLastSlotRow),
					$"{w.Key}: a generated caption's last ink row is {slotHeight - margin - 1} but every " +
					$"baked caption ends on slot row {BakedInkLastSlotRow}, so the runtime text floats " +
					$"{BakedInkLastSlotRow - (slotHeight - margin - 1)} row(s) clear of where the art it " +
					"replaces puts it. The captions are meant to be indistinguishable from the baked " +
					$"ones: set CaptionBottomMargin to {slotHeight - 1 - BakedInkLastSlotRow}.");

				Assert.That(padding, Is.GreaterThanOrEqualTo(margin),
					$"{w.Key}: CaptionBackgroundPadding {padding} is less than CaptionBottomMargin " +
					$"{margin}, so the band stops {margin - padding} row(s) short of the bottom of the " +
					"icon slot. Every shipped cameo has a caption baked into exactly those rows -- its " +
					"last ink is slot row 45 of 46 -- so a line of the old lettering survives under " +
					"the runtime caption. Raise the padding rather than lowering the margin; the " +
					"margin also positions the TEXT, and moving it drags the caption toward the edge.");
			}
		}
	}
}
