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
using OpenRA.Primitives;

namespace OpenRA
{
	/// <summary>
	/// <para>WW3MOD shadow cache: for every "from" cell, the pre-computed (ground, airborne) shadow toward
	/// every "to" cell in the radius-<see cref="MinRange"/>..<see cref="MaxRange"/> annulus around it.</para>
	///
	/// <para>Storage is one flat array holding only that annulus window per from-cell. The representation
	/// this replaces was CellLayer&lt;CellLayer&lt;(byte, byte)&gt;&gt; — a full-map array per cell, of
	/// which only the annulus was ever written: 98x82 cost 123 MB across 8,036 arrays of ~16 KB, all of
	/// them just under the 85,000-byte LOH threshold and so promoted into gen2.</para>
	///
	/// <para>Read semantics are deliberately identical to the nested CellLayer, because shadow feeds
	/// visibility and visibility feeds the simulation:
	///  - a pair outside the annulus reads (0, 0), the default the dense array returned because
	///    nothing ever wrote there;
	///  - an index outside the map throws IndexOutOfRangeException, as indexing CellLayer.Entries did;
	///  - two MPos that resolve to the same linear cell index alias onto the same entry, as they did
	///    when CellLayer stored them by that index.</para>
	/// </summary>
	public sealed class MapShadowLayer
	{
		public const int MinRange = 2;
		public const int MaxRange = 32;

		public readonly Size Size;
		public readonly MapGridType GridType;

		readonly Rectangle bounds;
		readonly int cells;
		readonly int radiusU;
		readonly int radiusV;
		readonly int windowWidth;
		readonly int slotCount;
		readonly int[] slotOfOffset;
		readonly (byte GroundShadow, byte AirborneShadow)[] entries;

		public MapShadowLayer(Map map)
			: this(map.Grid.Type, new Size(map.MapSize.X, map.MapSize.Y)) { }

		public MapShadowLayer(MapGridType gridType, Size size)
		{
			Size = size;
			GridType = gridType;
			bounds = new Rectangle(0, 0, size.Width, size.Height);
			cells = size.Width * size.Height;

			// MapGrid.CreateTilesByDistance buckets a CVec by ceil(sqrt(x^2 + y^2)), so every offset the
			// annulus can yield satisfies x^2 + y^2 <= MaxRange^2. On a rectangular grid MPos == CPos, so
			// the disc of that radius is an exact superset of the writable offsets. On an isometric grid
			// v = x + y but u = (x - y) / 2 truncates toward zero, which makes du depend on the from cell
			// as well as the offset — so cover every case with the bounding box instead of a disc.
			var disc = gridType == MapGridType.Rectangular;
			radiusU = disc ? MaxRange : MaxRange + 1;
			radiusV = disc ? MaxRange : 2 * MaxRange;
			windowWidth = (2 * radiusU) + 1;

			slotOfOffset = new int[windowWidth * ((2 * radiusV) + 1)];
			for (var dv = -radiusV; dv <= radiusV; dv++)
			{
				for (var du = -radiusU; du <= radiusU; du++)
				{
					var inWindow = !disc || (du * du) + (dv * dv) <= MaxRange * MaxRange;
					slotOfOffset[((dv + radiusV) * windowWidth) + du + radiusU] = inWindow ? slotCount++ : -1;
				}
			}

			if ((long)cells * slotCount > int.MaxValue)
				throw new InvalidOperationException($"Map of {size.Width}x{size.Height} is too large for a shadow layer.");

			entries = new (byte, byte)[cells * slotCount];
		}

		/// <summary>Number of annulus entries stored per from-cell.</summary>
		public int SlotsPerCell => slotCount;

		public bool Contains(MPos uv)
		{
			return bounds.Contains(uv.U, uv.V);
		}

		public bool Contains(CPos cell)
		{
			// .ToMPos() returns the same result if the X and Y coordinates are switched. X < Y is invalid
			// in the RectangularIsometric coordinate system, so pre-filter these — as CellLayer does.
			if (GridType == MapGridType.RectangularIsometric && cell.X < cell.Y)
				return false;

			return Contains(cell.ToMPos(GridType));
		}

		public (byte GroundShadow, byte AirborneShadow) this[MPos from, MPos to]
		{
			get
			{
				var index = Index(from, to);
				return index < 0 ? default : entries[index];
			}

			set
			{
				var index = Index(from, to);
				if (index < 0)
					throw new ArgumentOutOfRangeException(nameof(to),
						$"{to} is outside the radius-{MaxRange} shadow window around {from}.");

				entries[index] = value;
			}
		}

		int Index(MPos from, MPos to)
		{
			var fromIndex = CellIndex(from);
			var toIndex = CellIndex(to);

			var du = (toIndex % Size.Width) - (fromIndex % Size.Width);
			var dv = (toIndex / Size.Width) - (fromIndex / Size.Width);
			if (du < -radiusU || du > radiusU || dv < -radiusV || dv > radiusV)
				return -1;

			var slot = slotOfOffset[((dv + radiusV) * windowWidth) + du + radiusU];
			return slot < 0 ? -1 : (fromIndex * slotCount) + slot;
		}

		int CellIndex(MPos uv)
		{
			var index = (uv.V * Size.Width) + uv.U;
			if ((uint)index >= (uint)cells)
				throw new IndexOutOfRangeException($"{uv} is outside the {Size.Width}x{Size.Height} shadow layer.");

			return index;
		}
	}
}
