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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Widgets
{
	public readonly struct CargoManifestRow
	{
		public readonly string Key;
		public readonly string Label;
		public readonly int Count;

		public CargoManifestRow(string key, string label, int count)
		{
			Key = key;
			Label = label;
			Count = count;
		}
	}

	/// <summary>
	/// How a transport's passenger list is turned into rows, shared by the sidebar panel and the
	/// unload menu so the two cannot disagree about what is aboard. Grouping is the subtle half:
	/// it keys on <c>ISelectable.Class</c>, which is what keeps the veteran variants (E1R1, E3R1)
	/// folded into their base row instead of appearing as a second row with the same Tooltip name.
	/// Kept free of Actor so the row arithmetic can be pinned without a World.
	/// </summary>
	public static class CargoManifest
	{
		/// <summary>Key of the "+n more" row. Null is not reachable as a real group key — the fallback
		/// is the actor name, which always exists — so <c>row.Key == OverflowKey</c> is a sound test
		/// for a row that names no class and must not be acted on.</summary>
		public const string OverflowKey = null;

		/// <summary>Class if the passenger has one, actor name otherwise. Civilians and ejected crews
		/// reach the cargo hold with no Selectable.Class, and keying those on an empty string would
		/// collapse every one of them into a single unnamed row.</summary>
		public static string GroupKey(string selectableClass, string actorName)
		{
			return string.IsNullOrEmpty(selectableClass) ? actorName : selectableClass;
		}

		/// <summary>
		/// Fits grouped rows into a fixed number of slots, spending the last slot on a "+n more"
		/// marker when they do not all fit. The marker is the point: a panel that simply stops at
		/// its last slot looks complete, so the one thing it must never do is truncate quietly.
		/// Its Count is the men it hides, not the rows, so the count column stays in one unit.
		/// </summary>
		public static List<CargoManifestRow> Fit(IReadOnlyList<CargoManifestRow> groups, int slots)
		{
			if (groups == null || slots <= 0)
				return new List<CargoManifestRow>();

			if (groups.Count <= slots)
				return groups.ToList();

			var shown = groups.Take(slots - 1).ToList();
			var hidden = groups.Skip(slots - 1).ToList();

			shown.Add(new CargoManifestRow(OverflowKey, $"+{hidden.Count} more", hidden.Sum(g => g.Count)));
			return shown;
		}

		public static string GroupKey(Actor passenger)
		{
			return GroupKey(passenger.TraitOrDefault<ISelectable>()?.Class, passenger.Info.Name);
		}

		/// <summary>Tooltip name, which is what the player is called elsewhere in the UI. Note that
		/// the veteran variants inherit their base Tooltip verbatim, which is exactly why grouping
		/// cannot key on this.</summary>
		public static string DisplayName(Actor passenger)
		{
			return passenger.TraitOrDefault<Tooltip>()?.Info.Name ?? passenger.Info.Name;
		}

		public static List<CargoManifestRow> Group(IEnumerable<Actor> passengers)
		{
			return passengers
				.Where(p => p != null && !p.IsDead)
				.GroupBy(GroupKey)
				.Select(g => new CargoManifestRow(g.Key, DisplayName(g.First()), g.Count()))
				.ToList();
		}
	}
}
