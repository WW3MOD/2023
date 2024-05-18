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
using OpenRA.Primitives;

namespace OpenRA
{
	// Represents a layer of "something" that covers the map
	public sealed class CellLayer<T> : CellLayerBase<T>
	{
		public event Action<CPos> CellEntryChanged = null;

		public CellLayer(Map map)
			: base(map) { }

		public CellLayer(MapGridType gridType, Size size)
			: base(gridType, size) { }

		public override void CopyValuesFrom(CellLayerBase<T> anotherLayer)
		{
			if (CellEntryChanged != null)
				throw new InvalidOperationException(
					$"Cannot copy values when there are listeners attached to the {nameof(CellEntryChanged)} event.");

			base.CopyValuesFrom(anotherLayer);
		}

		public override void Clear()
		{
			if (CellEntryChanged != null)
				throw new InvalidOperationException(
					$"Cannot clear values when there are listeners attached to the {nameof(CellEntryChanged)} event.");

			base.Clear();
		}

		public override void Clear(T clearValue)
		{
			if (CellEntryChanged != null)
				throw new InvalidOperationException(
					$"Cannot clear values when there are listeners attached to the {nameof(CellEntryChanged)} event.");

			base.Clear(clearValue);
		}

		// Resolve an array index from cell coordinates
		int Index(CPos cell)
		{
			// PERF: Inline CPos.ToMPos to avoid MPos allocation
			var x = cell.X;
			var y = cell.Y;
			if (GridType == MapGridType.Rectangular)
				return y * Size.Width + x;

			var u = (x - y) / 2;
			var v = x + y;
			return v * Size.Width + u;
		}

		// Resolve an array index from map coordinates
		int Index(MPos uv)
		{
			return uv.V * Size.Width + uv.U;
		}

		/// <summary>Gets or sets the <see cref="CellLayer"/> using cell coordinates</summary>
		public T this[CPos cell]
		{
			get => Entries[Index(cell)];

			set
			{
				Entries[Index(cell)] = value;

				CellEntryChanged?.Invoke(cell);
			}
		}

		/// <summary>Gets or sets the layer contents using raw map coordinates (not CPos!)</summary>
		public T this[MPos uv]
		{
			// Exception has occurred: CLR/System.IndexOutOfRangeException
			// An unhandled exception of type 'System.IndexOutOfRangeException' occurred in OpenRA.Game.dll: 'Index was outside the bounds of the array.'
			// at OpenRA.CellLayer`1.get_Item(MPos uv) in C:\Users\fredr\Desktop\WW3MOD\engine\OpenRA.Game\Map\CellLayer.cs:line 104
			// at OpenRA.Traits.MapLayers.AddSource(IAffectsMapLayer mapLayer, Int32 strength, PPos[] projectedCells, Actor self) in C:\Users\fredr\Desktop\WW3MOD\engine\OpenRA.Game\Traits\Player\MapLayers.cs:line 325
			// at OpenRA.Mods.Common.Traits.Vision.AddCellsToPlayerMapLayer(Actor self, Player p, PPos[] uv) in C:\Users\fredr\Desktop\WW3MOD\engine\OpenRA.Mods.Common\Traits\Vision.cs:line 53
			// at OpenRA.Mods.Common.Traits.AffectsMapLayer.UpdateCells(Actor self) in C:\Users\fredr\Desktop\WW3MOD\engine\OpenRA.Mods.Common\Traits\AffectsMapLayer.cs:line 150
			// at OpenRA.Mods.Common.Traits.AffectsMapLayer.OpenRA.Mods.Common.Traits.INotifyCenterPositionChanged.CenterPositionChanged(Actor self, Byte oldLayer, Byte newLayer) in C:\Users\fredr\Desktop\WW3MOD\engine\OpenRA.Mods.Common\Traits\AffectsMapLayer.cs:line 117
			// at OpenRA.Mods.Common.Traits.Aircraft.SetPosition(Actor self, WPos pos) in C:\Users\fredr\Desktop\WW3MOD\engine\OpenRA.Mods.Common\Traits\Air\Aircraft.cs:line 867
			// at OpenRA.Mods.Common.Activities.Fly.FlyTowardsTick(Actor self, Aircraft aircraft, WAngle desiredFacing, WDist desiredAltitude, WVec& moveOverride, Boolean idleTurn) in C:\Users\fredr\Desktop\WW3MOD\engine\OpenRA.Mods.Common\Activities\Air\Fly.cs:line 101
			// at OpenRA.Mods.Common.Activities.Fly.FlyTowardsTick(Actor self, Aircraft aircraft, WAngle desiredFacing, WDist desiredAltitude, Boolean idleTurn) in C:\Users\fredr\Desktop\WW3MOD\engine\OpenRA.Mods.Common\Activities\Air\Fly.cs:line 106
			// at OpenRA.Mods.Common.Activities.Fly.Tick(Actor self) in C:\Users\fredr\Desktop\WW3MOD\engine\OpenRA.Mods.Common\Activities\Air\Fly.cs:line 269
			get => Entries[Index(uv)];

			set
			{
				Entries[Index(uv)] = value;

				CellEntryChanged?.Invoke(uv.ToCPos(GridType));
			}
		}

		public bool TryGetValue(CPos cell, out T value)
		{
			// .ToMPos() returns the same result if the X and Y coordinates
			// are switched. X < Y is invalid in the RectangularIsometric coordinate system,
			// so we pre-filter these to avoid returning the wrong result
			if (GridType == MapGridType.RectangularIsometric && cell.X < cell.Y)
			{
				value = default;
				return false;
			}

			var uv = cell.ToMPos(GridType);
			if (Bounds.Contains(uv.U, uv.V))
			{
				value = Entries[Index(uv)];
				return true;
			}

			value = default;
			return false;
		}

		public bool Contains(CPos cell)
		{
			// .ToMPos() returns the same result if the X and Y coordinates
			// are switched. X < Y is invalid in the RectangularIsometric coordinate system,
			// so we pre-filter these to avoid returning the wrong result
			if (GridType == MapGridType.RectangularIsometric && cell.X < cell.Y)
				return false;

			return Contains(cell.ToMPos(GridType));
		}

		public bool Contains(MPos uv)
		{
			return Bounds.Contains(uv.U, uv.V);
		}

		public CPos Clamp(CPos uv)
		{
			return Clamp(uv.ToMPos(GridType)).ToCPos(GridType);
		}

		public MPos Clamp(MPos uv)
		{
			return uv.Clamp(new Rectangle(0, 0, Size.Width - 1, Size.Height - 1));
		}

		public System.Collections.Generic.IEnumerable<MPos> TilesIntersectingLine(MPos from, MPos to)
		{
			int startX = from.U;
			int startY = from.V;
			int endX = to.U;
			int endY = to.V;

			// Validate input coordinates
			if (!IsValidCoordinate(startX, startY) || !IsValidCoordinate(endX, endY))
			{
				yield break;
			}

			// Use Bresenham's line algorithm to find the coordinates of the line
			int dx = Math.Abs(endX - startX);
			int dy = Math.Abs(endY - startY);
			int sx = startX < endX ? 1 : -1;
			int sy = startY < endY ? 1 : -1;
			int err = dx - dy;

			while (true)
			{
				if (IsValidCoordinate(startX, startY))
				{
					// Store the tile in a collection or perform other operations here
					yield return new MPos(startX, startY);
				}

				if (startX == endX && startY == endY)
					yield break;

				int e2 = 2 * err;
				if (e2 > -dy)
				{
					err -= dy;
					startX += sx;
				}
				if (e2 < dx)
				{
					err += dx;
					startY += sy;
				}
			}
		}

		public bool IsValidCoordinate(int x, int y)
		{
			return x >= 0 && (x + 1) < Size.Width && y >= 0 && (y + 1) < Size.Height;
		}
	}

	// Helper functions
	public static class CellLayer
	{
		/// <summary>Create a new layer by resizing another layer. New cells are filled with defaultValue.</summary>
		public static CellLayer<T> Resize<T>(CellLayer<T> layer, Size newSize, T defaultValue)
		{
			var result = new CellLayer<T>(layer.GridType, newSize);
			var width = Math.Min(layer.Size.Width, newSize.Width);
			var height = Math.Min(layer.Size.Height, newSize.Height);

			result.Clear(defaultValue);
			for (var j = 0; j < height; j++)
				for (var i = 0; i < width; i++)
					result[new MPos(i, j)] = layer[new MPos(i, j)];

			return result;
		}
	}
}
