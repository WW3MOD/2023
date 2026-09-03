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

namespace OpenRA.Mods.Common.Traits.Render
{
	/// <summary>
	/// Stacking arithmetic for the decoration cluster above a unit, and the derivation of the
	/// <c>Margin</c> values the mod's YAML authors there.
	///
	/// WHY THIS EXISTS. Every number in that cluster used to be an unexplained literal in
	/// defaults.yaml, and the visibility diamond shipped 8px off-centre because one of them was
	/// wrong in a way nobody could read off the file. The values are still authored in YAML — a
	/// decoration's Margin has to be, the engine loads it from there — but they are DERIVED here,
	/// so the file can cite a name instead of asserting a number.
	///
	/// THE COORDINATE MODEL, which is the part that is easy to get wrong:
	///
	/// 1. For <c>Position: Top</c> the origin is (bounds horizontal centre, bounds top), and the
	///    margin applied to it is <c>(-Margin.X, +Margin.Y)</c> — see
	///    <see cref="SelectionDecorations.GetDecorationMargin"/>. THE X IS NEGATED. A margin of
	///    <c>-8,0</c> therefore draws 8px to the RIGHT of centre, which is not what any reader
	///    expects and is exactly the bug this file was written to stop recurring. Y is NOT negated,
	///    and screen Y grows downward, so a negative Margin.Y moves a decoration UP.
	///
	/// 2. Both decoration renderers centre their content on that origin —
	///    <c>screenPos - size / 2</c> in WithDecoration (sprites) and WithTextDecoration (glyphs).
	///
	/// 3. For a GLYPH that centring is on the em box, not on the ink, and the two are not the same
	///    box. SpriteFont.Measure returns a height of exactly <c>rows * size</c> regardless of which
	///    character it measured, and SpriteFont.DrawText puts the baseline a further <c>size</c>
	///    below the draw position. Net: the baseline lands <c>size / 2</c> BELOW the nominal origin,
	///    so a glyph that sits on its baseline — every diamond, digit and capital — hangs below the
	///    point it is nominally centred on. <see cref="GlyphInkBottomOffset"/> is that overhang.
	/// </summary>
	public static class DecorationRowGeometry
	{
		/// <summary>Point size of the TinyBold font every decoration glyph in this cluster uses.</summary>
		public const int GlyphFontSize = 10;

		/// <summary>
		/// How far BELOW its nominal origin a baseline-sitting glyph's ink actually reaches.
		/// WithTextDecoration draws at <c>screenPos - size / 2</c>, and SpriteFont.DrawText then puts
		/// the baseline <c>size</c> below that, so the baseline is <c>size / 2</c> below the origin.
		/// A diamond's lowest ink is its bottom vertex, which sits on the baseline.
		/// </summary>
		public const int GlyphInkBottomOffset = GlyphFontSize / 2;

		/// <summary>
		/// Blank pixels wanted between the bottom of the visibility diamond and the top of the damage
		/// pip below it. This is the one number to change if the user asks for the diamond to sit
		/// tighter or looser; everything else here is measured from the art.
		/// </summary>
		public const int DiamondPipClearance = 3;

		/// <summary>Height of pip-damage-vehicle.shp. The sprite is a 17x5 BAR (defaults.yaml).</summary>
		public const int VehicleDamagePipHeight = 5;

		/// <summary>Margin.Y the vehicle damage pip is authored at in <c>^DamageVehiclePips</c>.</summary>
		public const int VehicleDamagePipMarginY = 0;

		/// <summary>Height of pip-damage-infantry.shp. The sprite is a 6x6 DOT (infantry.yaml).</summary>
		public const int InfantryDamagePipHeight = 6;

		/// <summary>Margin.Y the infantry damage pip is authored at in <c>^DamageInfantryPips</c>.</summary>
		public const int InfantryDamagePipMarginY = -5;

		/// <summary>
		/// Horizontal margin that centres a <c>Position: Top</c> decoration. Zero is the only value
		/// that centres, because the origin is already the horizontal centre of the bounds and any
		/// non-zero margin is a signed offset away from it.
		/// </summary>
		public const int CentredMarginX = 0;

		/// <summary>
		/// The top edge of a sprite decoration's ink, relative to the decoration bounds' top.
		/// The renderer centres the sprite on the origin, so the ink starts half its height above it.
		/// </summary>
		public static int SpritePipInkTop(int pipMarginY, int pipHeight)
		{
			return pipMarginY - pipHeight / 2;
		}

		/// <summary>
		/// The Margin.Y that places a baseline-sitting glyph's ink <paramref name="clearance"/> pixels
		/// above a sprite pip sharing the same <c>Position: Top</c> origin.
		/// </summary>
		public static int GlyphMarginYAbovePip(int pipMarginY, int pipHeight, int clearance)
		{
			return SpritePipInkTop(pipMarginY, pipHeight) - clearance - GlyphInkBottomOffset;
		}

		/// <summary>
		/// Margin.Y for the visibility diamond on a vehicle — clear above the 17x5 damage bar.
		/// Mirrored in <c>^UnitIndicators</c> in defaults.yaml.
		/// </summary>
		public static int VehicleDiamondMarginY =>
			GlyphMarginYAbovePip(VehicleDamagePipMarginY, VehicleDamagePipHeight, DiamondPipClearance);

		/// <summary>
		/// Margin.Y for the visibility diamond on infantry. Infantry author their damage pip 5px
		/// higher than vehicles do and it is a pixel taller, so a single margin cannot serve both:
		/// the value that clears the infantry dot leaves a visible gap on a vehicle, and the value
		/// that sits snug on a vehicle overlaps the dot. Mirrored in <c>^SpottedPipAdjustmentInfantry</c>
		/// in infantry.yaml, which is the same override idiom <c>^RankPipsAdjustmentInfantry</c>
		/// already uses one block below it for the same reason.
		/// </summary>
		public static int InfantryDiamondMarginY =>
			GlyphMarginYAbovePip(InfantryDamagePipMarginY, InfantryDamagePipHeight, DiamondPipClearance);
	}
}
