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

using OpenRA.Traits;

namespace OpenRA.Mods.Common
{
	public static class ShroudExts
	{
		public static bool AnyWithVisibility(this MapLayers shroud, (CPos Cell, SubCell SubCell)[] cells, int visibility)
		{
			// PERF: Avoid LINQ.
			foreach (var cell in cells)
				if (shroud.IsVisible(cell.Cell, visibility))
					return true;

			return false;
		}

		public static bool AnyExplored(this MapLayers shroud, (CPos Cell, SubCell SubCell)[] cells)
		{
			// PERF: Avoid LINQ.
			foreach (var cell in cells)
				if (shroud.IsExplored(cell.Cell))
					return true;

			return false;
		}

		public static bool AnyExplored(this MapLayers shroud, PPos[] puvs)
		{
			// PERF: Avoid LINQ.
			foreach (var puv in puvs)
				if (shroud.IsExplored(puv))
					return true;

			return false;
		}

		public static bool AnyVisible(this MapLayers mapLayers, (CPos Cell, SubCell SubCell)[] cells, int visibility)
		{
			// PERF: Avoid LINQ.
			foreach (var cell in cells)
				if (mapLayers.IsVisible(cell.Cell, visibility))
					return true;

			return false;
		}

		public static bool AnyVisibleOnRader(this MapLayers mapLayers, (CPos Cell, SubCell SubCell)[] cells)
		{
			// PERF: Avoid LINQ.
			foreach (var cell in cells)
				if (mapLayers.RadarCover(cell.Cell.ToWPos()))
					return true;

			return false;
		}
	}
}
