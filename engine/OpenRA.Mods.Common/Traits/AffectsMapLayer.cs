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
		public readonly WDist MoveRecalculationThreshold = new WDist(1024);

		[Desc("Minimum ticks between vision recalculations triggered by movement.",
			"Bounds the recalc rate even for fast units. The final position is always recomputed on stop via INotifyMoving.")]
		public readonly int MoveRecalculationInterval = 5;

		[Desc("Possible values are CenterPosition (measure range from the center) and ",
			"Footprint (measure range from the footprint)")]
		public readonly DetectablePosition Position = DetectablePosition.Ground;
	}

	public abstract class AffectsMapLayer : ConditionalTrait<AffectsMapLayerInfo>, IAffectsMapLayer, ISync, INotifyAddedToWorld,
		INotifyRemovedFromWorld, INotifyMoving, INotifyCenterPositionChanged, ITick
	{
		readonly HashSet<PPos> footprint;

		// Reusable cell buffer — avoids allocating a fresh PPos[] on every UpdateCells call.
		// This trait fires dozens of times per second across all moving units; pooling the buffer
		// removed a major Gen-0 GC pressure source. Sized once and grown as needed.
		readonly List<PPos> cellBuffer = new List<PPos>(128);

		[Sync]
		CPos cachedLocation;

		[Sync]
		WDist cachedRange;

		[Sync]
		protected bool CachedTraitDisabled { get; private set; }

		WPos cachedPos;
		int lastUpdateTick;

		protected abstract void AddCellsToPlayerMapLayer(Actor self, Player player, IReadOnlyList<PPos> uv);
		protected abstract void RemoveCellsFromPlayerMapLayer(Actor self, Player player);

		public AffectsMapLayer(AffectsMapLayerInfo info)
			: base(info)
		{
			if (Info.Position == DetectablePosition.Footprint)
				footprint = new HashSet<PPos>();

			// Initialise so the first CenterPositionChanged after AddedToWorld always passes the interval gate.
			lastUpdateTick = int.MinValue / 2;
		}

		IReadOnlyList<PPos> ProjectedCells(Actor self)
		{
			cellBuffer.Clear();

			var map = self.World.Map;
			var minRange = MinRange;
			var maxRange = Range;
			if (maxRange <= minRange)
				return cellBuffer;

			if (Info.Position == DetectablePosition.Footprint)
			{
				// PERF: Reuse collection to avoid allocations.
				footprint.UnionWith(self.OccupiesSpace.OccupiedCells()
					.SelectMany(kv => MapLayers.ProjectedCellsInRange(map, map.CenterOfCell(kv.Cell), minRange, maxRange, Info.MaxHeightDelta)));
				foreach (var p in footprint)
					cellBuffer.Add(p);
				footprint.Clear();
				return cellBuffer;
			}

			var pos = self.CenterPosition;
			if (Info.Position == DetectablePosition.Ground)
				pos -= new WVec(WDist.Zero, WDist.Zero, self.World.Map.DistanceAboveTerrain(pos));

			foreach (var p in MapLayers.ProjectedCellsInRange(map, pos, minRange, maxRange, Info.MaxHeightDelta))
				cellBuffer.Add(p);

			return cellBuffer;
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

			// Throttle: this fires on every tick a unit moves more than the threshold (typically every
			// few ticks during movement). Recomputing the projected cells and updating every player's
			// map layer is one of the most expensive per-tick operations in the simulation. The final
			// stop position is always recomputed via INotifyMoving.MovementTypeChanged, so a small
			// during-movement lag is invisible.
			var worldTick = self.World.WorldTick;
			if (Info.MoveRecalculationInterval > 0 && worldTick - lastUpdateTick < Info.MoveRecalculationInterval)
				return;

			cachedLocation = projectedLocation;
			cachedPos = pos;
			lastUpdateTick = worldTick;

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

			// Stagger lastUpdateTick by ActorID so groups of units that enter the world together
			// (e.g. reinforcements arriving from the map edge in waves) don't all become eligible
			// for the next throttled recalc on the same world tick.
			var interval = Info.MoveRecalculationInterval > 0 ? Info.MoveRecalculationInterval : 1;
			lastUpdateTick = self.World.WorldTick - (int)(self.ActorID % (uint)interval);

			CachedTraitDisabled = IsTraitDisabled;
			var cells = ProjectedCells(self);

			foreach (var p in self.World.Players)
				AddCellsToPlayerMapLayer(self, p, cells);
		}

		// Note: cells passed to AddCellsToPlayerMapLayer reference the per-trait cellBuffer.
		// Subclasses must consume them synchronously — the buffer is reused on the next call.

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
			// Recalculate the visibility at our final stop position. Bypasses the interval throttle
			// because the player needs accurate vision once the unit has actually stopped.
			if (type == MovementType.None && self.IsInWorld)
			{
				var centerPosition = self.CenterPosition;
				var projectedPos = centerPosition - new WVec(0, centerPosition.Z, centerPosition.Z);
				var projectedLocation = self.World.Map.CellContaining(projectedPos);
				var pos = self.CenterPosition;

				cachedLocation = projectedLocation;
				cachedPos = pos;
				lastUpdateTick = self.World.WorldTick;

				UpdateCells(self);
			}
		}
	}
}
