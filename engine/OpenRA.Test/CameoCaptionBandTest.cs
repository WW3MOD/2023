#region Copyright & License Information
/*
 * WW3MOD caption-band geometry test — reads the SHIPPED chrome, because the bug this exists to
 * catch was in the configuration and not in the code.
 *
 * The runtime caption band is drawn behind a cameo caption so the caption REPLACES the lettering
 * baked into the art rather than overlapping it (CameoCaptions.cs). Every cameo the production
 * palette ships has such lettering, and it runs almost to the bottom of the art.
 *
 * THIS FIXTURE PASSED WHILE THE GAME WAS WRONG, for two independent reasons. Both are now closed.
 *
 * FIRST: IT APPLIED IconSpriteOffset AS IF IT MOVED THE SPRITE'S TOP-LEFT. It does not — it moves
 * the sprite's CENTRE. The widget draws at `0.5 * IconSize + IconSpriteOffset`
 * (ProductionPaletteWidget.cs:291, SupportPowersWidget.cs:160) through DrawSpriteCentered, which
 * then subtracts half the SPRITE (WidgetUtils.cs:86-89). A 48-row cameo in a 46-row slot therefore
 * begins at slot row (46 - 48) / 2 + (-1) = -2, and the baked ink on sprite rows 42-46 lands on
 * SLOT ROWS 40-44 — not 41-45, which is what the old constant here said. The number is now derived
 * from IconSize and IconSpriteOffset the way the widget derives it, so it cannot drift from the
 * chrome.
 *
 * SECOND: SLOT ROW 45 IS NOT THE CAPTION'S TO USE. `PALETTE_FOREGROUND` in
 * chrome/ingame-player.yaml composites a cell frame over every slot AFTER the palette widget has
 * drawn — `background-iconrow` (sidebar.png 0,116,238,47) in the production palette, whose row 46
 * is 238 opaque pixels, and `background-supportoverlay` (12,324,64,48 for nato, 77,324,64,48 for
 * brics) drawn at -2,-2 in the power bin, whose row 47 is 64 opaque pixels. Both land on the slot's LAST ROW. Ink placed there is drawn and
 * then painted over, which is precisely what shipped: with CaptionBottomMargin 0 the caption's
 * fifth glyph row fell on slot row 45 and every caption in the game read four rows tall — I as T,
 * L as I, E as F, RIFLEMAN as RTFLEMAS. Measured on an in-game capture of main @ 2f94dad1.
 *
 * WHY IT READS THE YAML RATHER THAN ASSERTING NUMBERS. CameoCaptionsTest already pins the
 * arithmetic; a second copy of it would have passed happily while the shipped chrome stayed wrong.
 * The only thing that catches a configuration bug is reading the configuration.
 *
 * The widgets state every key this needs explicitly so that this fixture never has to keep a copy
 * of the engine defaults — a copy that would silently rot the day someone changed one. A widget
 * that draws a band and omits one fails here rather than being assumed correct.
 *
 * SCOPE, HONESTLY. Geometry only, and only for widgets that actually draw a band (a transparent
 * CaptionBackgroundColor draws nothing, so the relationship does not matter). It does NOT decode
 * the art or the frame images: the sprite height, the baked ink row and the fact that the frame's
 * opaque rule sits on the slot's last row are measurements written down here, re-taken 2026-09-20
 * and re-takeable with tools/cameo/binmock.py.
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
		/// Every shipped cameo is 48 rows tall, on a 60 or 64 wide canvas. Only the height matters
		/// here: it is what decides how far above the slot the art starts once the widget centres it.
		/// </summary>
		const int CameoSpriteHeight = 48;

		/// <summary>
		/// The last row of a cameo's BAKED caption, in the ART's own coordinates. Measured, not
		/// chosen: the shipped cameos put 5 ink rows of caption on sprite rows 42-46 of a 48-row
		/// canvas, with a 1px dark surround and a cap height of 5.
		/// <para>Re-measured 2026-09-20 on e1americaicon, t90icon, abramsicon, e4americaicon,
		/// apcicon and mediamericaicon. FOUR OF THOSE SIX ARE ANTIALIASED rather than 1-bit, so a
		/// bright-pixel threshold tight enough for e4americaicon reports the span as 42-44 on the
		/// other four and loses the bottom two rows. Loosen the threshold before concluding the art
		/// has moved.</para>
		/// <para>This is a fact about the ART, so it can go stale in a way no test can see: new art
		/// that puts its lettering elsewhere would make this number wrong rather than make a test
		/// fail. Re-measure with tools/cameo/binmock.py if the house style ever moves.</para>
		/// <para>AND IT IS ALREADY STALE FOR ONE BIN, harmlessly. As of 2026-09-09 every cameo the
		/// SUPPORT POWER roster draws is a photograph with no baked lettering at all. The number is
		/// still right, because the ~87 lettered art files in the PRODUCTION palette are unchanged
		/// and that palette also draws a band; what it is no longer evidence of is the power bin,
		/// where there is now nothing underneath a caption to cover. Turning that widget's band
		/// transparent is therefore available and is NOT done: see tools/cameo/README.md
		/// §"Rollout order is decided by one fact" for the antialiasing it would expose.</para>
		/// </summary>
		const int BakedInkLastSpriteRow = 46;

		/// <summary>
		/// The FIRST row of the baked caption's ink, same coordinates and same measurement as
		/// <see cref="BakedInkLastSpriteRow"/>: a cap height of 5 sitting on sprite row 46. The band
		/// has to reach at least this far up or the top of the old lettering shows above the new.
		/// </summary>
		const int BakedInkFirstSpriteRow = 42;

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

		/// <summary>The second component of an "x, y" widget field, which is the only one this needs.</summary>
		static int SecondComponent(MiniYamlNode node, string key)
		{
			var value = Field(node, key);
			Assert.That(value, Is.Not.Null,
				$"{node.Key} draws a caption band but leaves {key} implicit. State it: this fixture " +
				"derives where the cameo art lands from it and will not assume an engine default.");

			var parts = value.Split(',');
			Assert.That(parts, Has.Length.EqualTo(2), $"{node.Key}: {key} is not an \"x, y\" pair");
			return int.Parse(parts[1].Trim(), CultureInfo.InvariantCulture);
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

				var slotHeight = SecondComponent(w, "IconSize");
				var spriteOffsetY = SecondComponent(w, "IconSpriteOffset");

				// WHERE THE ART STARTS, derived the way the widget derives it rather than assumed.
				// DrawSpriteCentered is handed `icon.Pos + 0.5 * IconSize + IconSpriteOffset` and
				// subtracts half the SPRITE, so the art's top-left row is
				// `(slotHeight - spriteHeight) / 2 + IconSpriteOffset.Y`. For the shipped 46-row slot
				// and 48-row cameos that is -2: the art overhangs the slot by one row at each end.
				// IconSpriteOffset is NOT a top-left offset, and reading it as one is what put the
				// old value of this fixture's baked-ink row one row too low.
				Assert.That((slotHeight - CameoSpriteHeight) % 2, Is.Zero,
					$"{w.Key}: IconSize.Y {slotHeight} and the {CameoSpriteHeight}-row cameo differ by an " +
					"odd number of rows, so the widget centres the art on a half row and this fixture's " +
					"integer arithmetic no longer describes it");

				var spriteTopSlotRow = (slotHeight - CameoSpriteHeight) / 2 + spriteOffsetY;
				var bakedInkLastSlotRow = spriteTopSlotRow + BakedInkLastSpriteRow;

				// THE ANCHOR. The generated caption's last ink row is `IconSize.Y - margin - 1`:
				// CameoCaptionCache puts the line box at `slotHeight - margin - lineHeight`,
				// SpriteFont.DrawText adds `size` to reach the baseline (SpriteFont.cs:99), and
				// lineHeight IS size, so the font cancels out and this holds for any caption font.
				// It has to equal the row the BAKED lettering ends on, which is measured, not
				// chosen -- see BakedInkLastSpriteRow.
				var generatedInkLastSlotRow = slotHeight - margin - 1;

				Assert.That(generatedInkLastSlotRow, Is.EqualTo(bakedInkLastSlotRow),
					$"{w.Key}: a generated caption's last ink row is {generatedInkLastSlotRow} but the baked " +
					$"caption it replaces ends on slot row {bakedInkLastSlotRow} (sprite row " +
					$"{BakedInkLastSpriteRow} of a {CameoSpriteHeight}-row cameo whose top-left is slot row " +
					$"{spriteTopSlotRow}), so the runtime text sits " +
					$"{generatedInkLastSlotRow - bakedInkLastSlotRow} row(s) off where the art it replaces " +
					"puts it. The captions are meant to be indistinguishable from the baked ones: set " +
					$"CaptionBottomMargin to {slotHeight - 1 - bakedInkLastSlotRow}.");

				// AND THE LAST ROW OF THE SLOT IS NOT AVAILABLE. Both bins composite a cell frame
				// over the palette AFTER it has drawn (PALETTE_FOREGROUND in the same chrome file):
				// `background-iconrow` row 46 is 238 opaque pixels and `background-supportoverlay`
				// row 47 is 64, and each lands on the slot's last row. Ink there is drawn and then
				// painted over. This is a separate reason from the anchor above and is asserted
				// separately, so that re-measuring the art can never quietly push ink back onto it.
				var frameCoveredSlotRow = slotHeight - 1;
				Assert.That(generatedInkLastSlotRow, Is.LessThan(frameCoveredSlotRow),
					$"{w.Key}: the caption's last ink row is slot row {generatedInkLastSlotRow}, which the " +
					"sidebar's own cell frame paints over after the palette has drawn. The row is written " +
					"and never seen, so the caption loses its bottom glyph row and reads I as T, L as I and " +
					$"E as F. Raise CaptionBottomMargin to at least {slotHeight - frameCoveredSlotRow}.");

				// THE BAND HAS TO REACH PAST THE BAKED INK'S LAST ROW, which is the thing the
				// padding rule below is a proxy for and is worth asserting directly now that the two
				// numbers have come apart. CameoCaptionCache clamps the band's bottom to
				// `min(slotHeight, slotHeight - margin + padding)`, exclusive -- font-free, because
				// the line height cancels out of the bottom edge exactly as it does out of the
				// anchor. The band's TOP is not checkable here: it is `top - padding` and `top`
				// carries the font's size, which this fixture deliberately does not load.
				var bakedInkFirstSlotRow = spriteTopSlotRow + BakedInkFirstSpriteRow;
				var bandLastSlotRow = Math.Min(slotHeight, slotHeight - margin + padding) - 1;
				Assert.That(bandLastSlotRow, Is.GreaterThanOrEqualTo(bakedInkLastSlotRow),
					$"{w.Key}: the caption band ends on slot row {bandLastSlotRow} but the baked lettering " +
					$"it exists to cover occupies slot rows {bakedInkFirstSlotRow}-{bakedInkLastSlotRow}, so " +
					$"{bakedInkLastSlotRow - bandLastSlotRow} row(s) of the old caption survive under the " +
					"new one. Raise CaptionBackgroundPadding.");

				Assert.That(padding, Is.GreaterThanOrEqualTo(margin),
					$"{w.Key}: CaptionBackgroundPadding {padding} is less than CaptionBottomMargin " +
					$"{margin}, so the band stops {margin - padding} row(s) short of the bottom of the " +
					"icon slot. Every shipped production cameo has a caption baked into those rows -- its " +
					$"last ink is slot row {bakedInkLastSlotRow} of {slotHeight} -- so a line of the old " +
					"lettering survives under the runtime caption. Raise the padding rather than lowering " +
					"the margin; the margin also positions the TEXT, and moving it drags the caption onto " +
					"the row the cell frame covers.");
			}
		}
	}
}
