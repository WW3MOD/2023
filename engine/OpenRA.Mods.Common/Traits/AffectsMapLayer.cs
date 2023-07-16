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
using System.Collections.Generic;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public abstract class AffectsMapLayerInfo : ConditionalTraitInfo
	{
		public readonly MapLayers.Type Type = MapLayers.Type.Vision;

		public readonly WDist MinRange = WDist.Zero;

		public readonly WDist Range = WDist.Zero;

		[Desc("If >= 0, prevent cells that are this much higher than the actor from being revealed.")]
		public readonly int MaxHeightDelta = -1;

		[Desc("If > 0, force visibility to be recalculated if the unit moves within a cell by more than this distance.")]
		public readonly WDist MoveRecalculationThreshold = new WDist(256);

		[Desc("Possible values are CenterPosition (measure range from the center) and ",
			"Footprint (measure range from the footprint)")]
		public readonly SignaturePosition Position = SignaturePosition.Footprint;
	}

	public abstract class AffectsMapLayer : ConditionalTrait<AffectsMapLayerInfo>, IAffectsMapLayer, ISync, INotifyAddedToWorld,
		INotifyRemovedFromWorld, INotifyMoving, INotifyCenterPositionChanged, ITick
	{
		static readonly PPos[] NoCells = Array.Empty<PPos>();

		readonly HashSet<PPos> footprint;

		[Sync]
		CPos cachedLocation;

		[Sync]
		WDist cachedRange;

		[Sync]
		protected bool CachedTraitDisabled { get; private set; }

		WPos cachedPos;
		/* int checkTick = 0; */

		protected abstract void AddCellsToPlayerMapLayer(Actor self, Player player, PPos[] uv);
		protected abstract void RemoveCellsFromPlayerMapLayer(Actor self, Player player);

		public AffectsMapLayer(AffectsMapLayerInfo info)
			: base(info)
		{
			if (Info.Position == SignaturePosition.Footprint)
				footprint = new HashSet<PPos>();
		}

		PPos[] ProjectedCells(Actor self)
		{
			var map = self.World.Map;
			var minRange = MinRange;
			var maxRange = Range;
			if (maxRange <= minRange)
				return NoCells;

			if (Info.Position == SignaturePosition.Footprint)
			{
				// PERF: Reuse collection to avoid allocations.
				footprint.UnionWith(self.OccupiesSpace.OccupiedCells()
					.SelectMany(kv => MapLayers.ProjectedCellsInRange(map, map.CenterOfCell(kv.Cell), minRange, maxRange, Info.MaxHeightDelta)));
				var cells = footprint.ToArray();
				footprint.Clear();
				return cells;
			}

			var pos = self.CenterPosition;
			if (Info.Position == SignaturePosition.Ground)
				pos -= new WVec(WDist.Zero, WDist.Zero, self.World.Map.DistanceAboveTerrain(pos));

			return MapLayers.ProjectedCellsInRange(map, pos, minRange, maxRange, Info.MaxHeightDelta)
				.ToArray();
		}

		void INotifyCenterPositionChanged.CenterPositionChanged(Actor self, byte oldLayer, byte newLayer)
		{
			if (!self.IsInWorld)
				return;

			var centerPosition = self.CenterPosition;
			var projectedPos = centerPosition - new WVec(0, centerPosition.Z, centerPosition.Z);
			var projectedLocation = self.World.Map.CellContaining(projectedPos);
			var pos = self.CenterPosition;

			var dirty = Info.MoveRecalculationThreshold.Length > 0 && (pos - cachedPos).LengthSquared > Info.MoveRecalculationThreshold.LengthSquared;
			if (!dirty && cachedLocation == projectedLocation)
				return;

			cachedLocation = projectedLocation;
			cachedPos = pos;

			// CPU improvement - Update shroud every 10 ticks
			/* if (checkTick-- <= 0)
			{
				checkTick = 10;
				UpdateCells(self);
			} */

			UpdateCells(self);
		}

		void ITick.Tick(Actor self)
		{
			if (!self.IsInWorld)
				return;

			var traitDisabled = IsTraitDisabled;
			var range = Range;

			if (cachedRange == range && traitDisabled == CachedTraitDisabled)
				return;

			cachedRange = range;
			CachedTraitDisabled = traitDisabled;

			// if (checkTick-- <= 0)
			/* {
				// CPU improvement - Update shroud every 10 ticks
				checkTick = 10;
				UpdateCells(self);
			} */

			UpdateCells(self);
		}

		void UpdateCells(Actor self)
		{
			var cells = ProjectedCells(self);
			foreach (var p in self.World.Players)
			{
				RemoveCellsFromPlayerMapLayer(self, p);
				AddCellsToPlayerMapLayer(self, p, cells);
			}
		}

		void INotifyAddedToWorld.AddedToWorld(Actor self)
		{
			var centerPosition = self.CenterPosition;
			var projectedPos = centerPosition - new WVec(0, centerPosition.Z, centerPosition.Z);
			cachedLocation = self.World.Map.CellContaining(projectedPos);
			cachedPos = centerPosition;
			CachedTraitDisabled = IsTraitDisabled;
			var cells = ProjectedCells(self);

			foreach (var p in self.World.Players)
				AddCellsToPlayerMapLayer(self, p, cells);
		}

		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self)
		{
			foreach (var p in self.World.Players)
				RemoveCellsFromPlayerMapLayer(self, p);
		}
		public virtual MapLayers.Type Type => throw new NotImplementedException();
		public virtual WDist MinRange => CachedTraitDisabled ? WDist.Zero : Info.MinRange;
		public virtual WDist Range => CachedTraitDisabled ? WDist.Zero : Info.Range;

		void INotifyMoving.MovementTypeChanged(Actor self, MovementType type)
		{
			// Recalculate the visibility at our final stop position
			if (type == MovementType.None && self.IsInWorld)
			{
				var centerPosition = self.CenterPosition;
				var projectedPos = centerPosition - new WVec(0, centerPosition.Z, centerPosition.Z);
				var projectedLocation = self.World.Map.CellContaining(projectedPos);
				var pos = self.CenterPosition;

				cachedLocation = projectedLocation;
				cachedPos = pos;

				UpdateCells(self);
			}
		}
	}
}
