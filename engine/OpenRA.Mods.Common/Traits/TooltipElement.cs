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

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>
	/// The kind of a tooltip row. Styling is a property of the kind — never of characters
	/// an author typed into a description string — so a restyle touches one table and no content.
	/// </summary>
	public enum TooltipElementKind
	{
		/// <summary>Class/role line under the unit name. Uppercase, dimmed.</summary>
		Subhead,

		/// <summary>A wrapped paragraph. The opening sentence of Buildable.Description.</summary>
		Prose,

		/// <summary>A " - " bullet: armaments and capabilities.</summary>
		ListItem,

		/// <summary>A named quantity, two columns: Armour, Speed, Sight.</summary>
		StatRow,

		/// <summary>As StatRow, but the value is a price and is drawn in supply amber.</summary>
		CostRow,

		/// <summary>A 1px horizontal rule. Replaces the "\n\n" that did this job invisibly.</summary>
		Separator,

		/// <summary>A demoted caveat, dimmer and smaller than Prose.</summary>
		Note,
	}

	/// <summary>
	/// One row of a tooltip. <see cref="Label"/> is the left column (StatRow/CostRow) or the whole
	/// text (Prose/ListItem/Note/Subhead); <see cref="Value"/> is the right column and is null for
	/// the single-column kinds. <see cref="Separator"/> uses neither.
	/// </summary>
	/// <remarks>
	/// A C# 9 positional record rather than the `readonly record struct` this was first drafted as:
	/// engine/Directory.Build.props pins LangVersion 9 and record structs are C# 10.
	/// </remarks>
	public record TooltipElement(TooltipElementKind Kind, string Label, string Value)
	{
		public static TooltipElement Separator()
		{
			return new TooltipElement(TooltipElementKind.Separator, null, null);
		}

		public static TooltipElement Prose(string text)
		{
			return new TooltipElement(TooltipElementKind.Prose, text, null);
		}

		public static TooltipElement ListItem(string text)
		{
			return new TooltipElement(TooltipElementKind.ListItem, text, null);
		}

		public static TooltipElement Note(string text)
		{
			return new TooltipElement(TooltipElementKind.Note, text, null);
		}

		public static TooltipElement Subhead(string text)
		{
			return new TooltipElement(TooltipElementKind.Subhead, text, null);
		}

		public static TooltipElement Stat(string label, string value)
		{
			return new TooltipElement(TooltipElementKind.StatRow, label, value);
		}

		public static TooltipElement Cost(string label, string value)
		{
			return new TooltipElement(TooltipElementKind.CostRow, label, value);
		}
	}
}
