#region Copyright & License Information
/*
 * WW3MOD addition. Not upstream OpenRA.
 */
#endregion

/*
 * THE BAND EVERY ESCALATION BANNER IS DRAWN AS -- extracted 2026-09-13, when the third one was
 * about to be written.
 *
 * DefconTransitionBannerWidget and FinalExchangeBannerWidget had already grown byte-identical
 * copies of this geometry, down to the same two comments explaining it. Adding
 * NuclearArmedBannerWidget beside them would have made three, and three copies of a subtle rule is
 * how the rule diverges: the SECOND copy already had to be kept in step by hand, and nothing in the
 * tree would have failed if it had not been.
 *
 * ==== WHAT IS SUBTLE ABOUT IT, AND THEREFORE WORTH HAVING ONE OF ====
 *   - TOP AND BOTTOM RULES ONLY, NO SIDE BORDERS. The band runs off both edges of the screen, and
 *     that is what makes it read as the screen changing state rather than as a dialog the player
 *     could dismiss. A copy that "tidied" this into a full border would quietly change what the
 *     banner MEANS.
 *   - THE TWO LINES ARE CENTRED AS A BLOCK, not independently. Centring each line on its own makes
 *     the pair look like a heading with a caption under it; centring the block is what makes them
 *     read as one statement. The demo's frame 02 exists to catch exactly this.
 *   - THE SECOND LINE MAY BE DRAWN IN TWO PIECES so a trailing clock can carry its own colour
 *     without the line jumping sideways as the digits change width. That is the final exchange's
 *     requirement and it is the reason the signature is shaped this way rather than taking one
 *     string.
 *
 * Nothing here decides WHETHER to draw, for how long, or in what colour. Each widget owns its own
 * trigger and its own hold, because those are three different questions with three different
 * answers -- four seconds on an edge, the whole of a window, and four seconds on a different edge.
 */

using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets
{
	public static class DefconBannerBand
	{
		/// <summary>The gap between the title and the line under it, in pixels. The mockup's.</summary>
		public const int TitleGap = 7;

		static readonly Color Background = Color.FromArgb(209, 9, 10, 8);
		static readonly Color LineColor = Color.FromArgb(203, 201, 190);
		static readonly Color Shadow = Color.FromArgb(160, 0, 0, 0);

		/// <summary>
		/// <para>Draw the full-width band: a title in <paramref name="accent"/> over one centred line.</para>
		/// <para><paramref name="trailing"/> is an optional second piece of that line -- a clock --
		/// drawn immediately after <paramref name="line"/> in <paramref name="trailingColor"/>, with
		/// the PAIR centred together so the line does not shift sideways as the digits change width.
		/// Null draws the line alone.</para>
		/// </summary>
		public static void Draw(Rectangle rb, Color accent, SpriteFont titleFont, string title,
			SpriteFont lineFont, string line, string trailing = null, Color? trailingColor = null)
		{
			WidgetUtils.FillRectWithColor(rb, Background);

			var border = Color.FromArgb(128, accent);
			WidgetUtils.FillRectWithColor(new Rectangle(rb.X, rb.Y, rb.Width, 1), border);
			WidgetUtils.FillRectWithColor(new Rectangle(rb.X, rb.Bottom - 1, rb.Width, 1), border);

			var titleSize = titleFont.Measure(title);
			var lineSize = lineFont.Measure(line);
			var trailingSize = trailing != null ? lineFont.Measure(trailing) : new int2(0, 0);

			var blockHeight = titleSize.Y + TitleGap + lineSize.Y;
			var y = rb.Y + ((rb.Height - blockHeight) / 2);

			titleFont.DrawTextWithContrast(title, new float2(rb.X + ((rb.Width - titleSize.X) / 2), y),
				accent, Shadow, 2);

			y += titleSize.Y + TitleGap;

			// The two pieces are ONE centred line measured together, which is why the width summed
			// here is the pair's and not the first piece's.
			var x = rb.X + ((rb.Width - (lineSize.X + trailingSize.X)) / 2);

			lineFont.DrawTextWithContrast(line, new float2(x, y), LineColor, Shadow, 1);

			if (trailing != null)
				lineFont.DrawTextWithContrast(trailing, new float2(x + lineSize.X, y),
					trailingColor ?? LineColor, Shadow, 1);
		}
	}
}
