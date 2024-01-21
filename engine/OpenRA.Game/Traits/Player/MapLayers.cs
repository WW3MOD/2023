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

namespace OpenRA.Traits
{
	[TraitLocation(SystemActors.Player | SystemActors.EditorPlayer)]
	[Desc("Required for shroud and fog visibility checks. Add this to the player actor.")]
	public class MapLayersInfo : TraitInfo, ILobbyOptions
	{
		[Desc("Descriptive label for the fog checkbox in the lobby.")]
		public readonly string FogCheckboxLabel = "Fog of War";

		[Desc("Tooltip description for the fog checkbox in the lobby.")]
		public readonly string FogCheckboxDescription = "Line of sight is required to view enemy forces";

		[Desc("Default value of the fog checkbox in the lobby.")]
		public readonly bool FogCheckboxEnabled = true;

		[Desc("Prevent the fog enabled state from being changed in the lobby.")]
		public readonly bool FogCheckboxLocked = false;

		[Desc("Whether to display the fog checkbox in the lobby.")]
		public readonly bool FogCheckboxVisible = true;

		[Desc("Display order for the fog checkbox in the lobby.")]
		public readonly int FogCheckboxDisplayOrder = 0;

		[Desc("Descriptive label for the explored map checkbox in the lobby.")]
		public readonly string ExploredMapCheckboxLabel = "Explored Map";

		[Desc("Tooltip description for the explored map checkbox in the lobby.")]
		public readonly string ExploredMapCheckboxDescription = "Initial map shroud is revealed";

		[Desc("Default value of the explore map checkbox in the lobby.")]
		public readonly bool ExploredMapCheckboxEnabled = true;

		[Desc("Prevent the explore map enabled state from being changed in the lobby.")]
		public readonly bool ExploredMapCheckboxLocked = false;

		[Desc("Whether to display the explore map checkbox in the lobby.")]
		public readonly bool ExploredMapCheckboxVisible = true;

		[Desc("Display order for the explore map checkbox in the lobby.")]
		public readonly int ExploredMapCheckboxDisplayOrder = 0;

		/* [Desc("How many layers of fog to create, excluding the shroud (black) layer and the visible layer.")]
		public readonly int FogLayers = 9; */

		IEnumerable<LobbyOption> ILobbyOptions.LobbyOptions(MapPreview map)
		{
			yield return new LobbyBooleanOption("explored", ExploredMapCheckboxLabel, ExploredMapCheckboxDescription,
				ExploredMapCheckboxVisible, ExploredMapCheckboxDisplayOrder, ExploredMapCheckboxEnabled, ExploredMapCheckboxLocked);

			yield return new LobbyBooleanOption("fog", FogCheckboxLabel, FogCheckboxDescription,
				FogCheckboxVisible, FogCheckboxDisplayOrder, FogCheckboxEnabled, FogCheckboxLocked);
		}

		public override object Create(ActorInitializer init) { return new MapLayers(init.Self, this); }
	}

	public class MapLayers : ISync, INotifyCreated, ITick
	{
		public static readonly int VisionLayers = 11;
		public enum Type : byte { Vision, Radar, PassiveVisibility }
		public event Action<PPos> OnShroudChanged;
		public int RevealedCells { get; private set; }

		class VisionSource
		{
			public readonly int Vision;
			public readonly PPos[] ProjectedCells;

			public VisionSource(int vision, PPos[] projectedCells)
			{
				Vision = vision;
				ProjectedCells = projectedCells;
			}
		}

		readonly MapLayersInfo info;
		readonly Map map;

		// Individual shroud modifier sources (type and area)
		readonly Dictionary<object, VisionSource> sources = new Dictionary<object, VisionSource>();

		// Per-cell count of each source type, used to resolve the final cell type
		readonly ProjectedCellLayer<short[]> visibilityCount;
		readonly ProjectedCellLayer<short> radarCount;

		readonly ProjectedCellLayer<short> passiveVisibleCount;
		readonly ProjectedCellLayer<short> visibleCount;
		/* readonly ProjectedCellLayer<short> generatedShroudCount; */
		readonly ProjectedCellLayer<bool> explored;
		readonly ProjectedCellLayer<bool> touched;
		bool anyCellTouched;

		// Per-cell cache of the resolved cell type (shroud/fog/visible)
		public ProjectedCellLayer<byte> ResolvedVisibility;

		bool disabledChanged;
		[Sync]
		bool shroudDisabled;
		public bool Disabled
		{
			get => shroudDisabled;

			set
			{
				if (shroudDisabled == value)
					return;

				shroudDisabled = value;
				disabledChanged = true;
			}
		}

		bool fogEnabled;
		public bool FogEnabled => !Disabled && fogEnabled;
		public bool ExploreMapEnabled { get; private set; }
		public int Hash { get; private set; }

		public MapLayers(Actor self, MapLayersInfo info)
		{
			this.info = info;
			map = self.World.Map;

			visibilityCount = new ProjectedCellLayer<short[]>(map);
			radarCount = new ProjectedCellLayer<short>(map);

			passiveVisibleCount = new ProjectedCellLayer<short>(map);
			visibleCount = new ProjectedCellLayer<short>(map);
			/* generatedShroudCount = new ProjectedCellLayer<short>(map); */
			explored = new ProjectedCellLayer<bool>(map);
			touched = new ProjectedCellLayer<bool>(map);
			anyCellTouched = true;

			ResolvedVisibility = new ProjectedCellLayer<byte>(map);
		}

		void INotifyCreated.Created(Actor self)
		{
			var gs = self.World.LobbyInfo.GlobalSettings;
			fogEnabled = gs.OptionOrDefault("fog", info.FogCheckboxEnabled);

			ExploreMapEnabled = gs.OptionOrDefault("explored", info.ExploredMapCheckboxEnabled);
			if (ExploreMapEnabled)
				self.World.AddFrameEndTask(w => ExploreAll());

			if (!fogEnabled && ExploreMapEnabled)
				RevealedCells = map.ProjectedCells.Length;
		}

		void ITick.Tick(Actor self)
		{
			if (!anyCellTouched && !disabledChanged)
				return;

			anyCellTouched = false;

			if (OnShroudChanged == null)
			{
				disabledChanged = false;
				return;
			}

			// PERF: Parts of this loop are very hot.
			// We loop over the direct index that represents the PPos in
			// the ProjectedCellLayers, converting to a PPos only if
			// it is needed (which is the uncommon case.)
			var maxIndex = touched.MaxIndex;
			for (var index = 0; index < maxIndex; index++)
			{
				// PERF: Most cells are not touched
				if (!touched[index] && !disabledChanged)
					continue;

				if (visibilityCount[index] == null)
					visibilityCount[index] = new short[VisionLayers];

				touched[index] = false;

				byte visibility = 0;

				if (explored[index])
				{
					// Find the highest visibility for position
					for (var i = (byte)(visibilityCount[index].Length - 1); i > 0; i--)
					{
						if (visibilityCount[index][i] > 0)
						{
							visibility = i;
							break;
						}
					}

					if (visibility == 0)
						visibility = 1;
				}

				// PERF: Most cells are unchanged
				var oldResolvedVisibility = ResolvedVisibility[index];
				if (visibility != oldResolvedVisibility || disabledChanged)
				{
					ResolvedVisibility[index] = visibility;
					var puv = touched.PPosFromIndex(index);

					if (map.Contains(puv))
						OnShroudChanged(puv);

					if (!disabledChanged && (fogEnabled || !ExploreMapEnabled))
					{
						if (visibility > 0)
							RevealedCells++;
						else if (fogEnabled && oldResolvedVisibility > 0)
							RevealedCells--;
					}

					if (self.Owner.WinState == WinState.Lost)
						RevealedCells = 0;
				}
			}

			Hash = Sync.HashPlayer(self.Owner) + self.World.WorldTick;
			disabledChanged = false;
		}

		public static IEnumerable<PPos> ProjectedCellsInRange(Map map, WPos pos, WDist minRange, WDist maxRange, int maxHeightDelta = -1)
		{
			// Account for potential extra half-cell from odd-height terrain
			var r = (maxRange.Length + 1023 + 512) / 1024;
			var minLimit = minRange.LengthSquared;
			var maxLimit = maxRange.LengthSquared;

			// Project actor position into the shroud plane
			var projectedPos = pos - new WVec(0, pos.Z, pos.Z);
			var projectedCell = map.CellContaining(projectedPos);
			var projectedHeight = pos.Z / 512;

			foreach (var c in map.FindTilesInAnnulus(projectedCell, minRange.Length / 1024, r, true))
			{
				var dist = (map.CenterOfCell(c) - projectedPos).HorizontalLengthSquared;
				if (dist <= maxLimit && (dist == 0 || dist > minLimit))
				{
					var puv = (PPos)c.ToMPos(map);
					if (maxHeightDelta < 0 || map.ProjectedHeight(puv) < projectedHeight + maxHeightDelta)
						yield return puv;
				}
			}
		}

		public static IEnumerable<PPos> ProjectedCellsInRange(Map map, CPos cell, WDist range, int maxHeightDelta = -1)
		{
			return ProjectedCellsInRange(map, map.CenterOfCell(cell), WDist.Zero, range, maxHeightDelta);
		}

		public void AddSource(IAffectsMapLayer mapLayer, int strength, PPos[] projectedCells)
		{
			if (sources.ContainsKey(mapLayer))
				throw new InvalidOperationException("Attempting to add duplicate shroud source");

			sources[mapLayer] = new VisionSource(strength, projectedCells);

			foreach (var puv in projectedCells)
			{
				// TODO: Possibly remove this, to render projectiles even if "outside" of map, when shooting arching weapons near edge they become invisible "outside". Maybe render based on ground position, so as long as they are over the map they render?
				// Force cells outside the visible bounds invisible
				if (!map.Contains(puv))
					continue;

				var index = touched.Index(puv);
				touched[index] = true;
				anyCellTouched = true;

				if (visibilityCount[index] == null)
					visibilityCount[index] = new short[VisionLayers];

				if (mapLayer.Type == Type.Vision)
				{
					visibilityCount[index][strength]++;

					if (strength > 0)
						explored[index] = true;
				}
				else if (mapLayer.Type == Type.Radar)
				{
					radarCount[index]++;
				}

				/* if (visibility == 0)
				{
					shroudGenerationEnabled = true;
					generatedShroudCount[index]++;
				} */
			}
		}

		public void RemoveSource(IAffectsMapLayer mapLayer)
		{
			if (!sources.TryGetValue(mapLayer, out var state))
				return;

			foreach (var puv in state.ProjectedCells)
			{
				// Cells outside the visible bounds don't increment visibleCount
				if (map.Contains(puv))
				{
					var index = touched.Index(puv);
					touched[index] = true;
					anyCellTouched = true;

					if (mapLayer.Type == Type.Vision)
					{
						visibilityCount[index][state.Vision]--;
					}
					else if (mapLayer.Type == Type.Radar)
					{
						radarCount[index]--;
					}
				}
			}

			sources.Remove(mapLayer);
		}

		public void ExploreProjectedCells(IEnumerable<PPos> cells)
		{
			foreach (var puv in cells)
			{
				if (map.Contains(puv))
				{
					var index = touched.Index(puv);
					if (!explored[index])
					{
						touched[index] = true;
						anyCellTouched = true;
						explored[index] = true;
					}
				}
			}
		}

		public void Explore(MapLayers s)
		{
			if (map.Bounds != s.map.Bounds)
				throw new ArgumentException("The map bounds of these shrouds do not match.", nameof(s));

			foreach (var puv in map.ProjectedCells)
			{
				var index = touched.Index(puv);
				if (!explored[index] && s.explored[index])
				{
					touched[index] = true;
					anyCellTouched = true;
					explored[index] = true;
				}
			}
		}

		public void ExploreAll()
		{
			foreach (var puv in map.ProjectedCells)
			{
				var index = touched.Index(puv);
				if (!explored[index])
				{
					touched[index] = true;
					anyCellTouched = true;
					explored[index] = true;
				}
			}
		}

		public void ResetExploration()
		{
			foreach (var puv in map.ProjectedCells)
			{
				var index = touched.Index(puv);
				touched[index] = true;
				explored[index] = (visibleCount[index] + passiveVisibleCount[index]) > 0;
			}

			anyCellTouched = true;
		}

		public bool IsExplored(WPos pos)
		{
			return IsExplored(map.ProjectedCellCovering(pos));
		}

		public bool IsExplored(CPos cell)
		{
			return IsExplored(cell.ToMPos(map));
		}

		public bool IsExplored(MPos uv)
		{
			if (!map.Contains(uv))
				return false;

			foreach (var puv in map.ProjectedCellsCovering(uv))
				if (IsExplored(puv))
					return true;

			return false;
		}

		public bool IsExplored(PPos puv)
		{
			if (Disabled)
				return map.Contains(puv);

			var rt = ResolvedVisibility[puv];

			return ResolvedVisibility.Contains(puv) && rt > 0;
		}

		public bool RadarCover(WPos pos)
		{
			return RadarCover(map.ProjectedCellCovering(pos));
		}

		public bool RadarCover(PPos puv)
		{
			return radarCount.Contains(puv) && radarCount[puv] > 0;
		}

		public bool IsVisible(WPos pos, int visibility)
		{
			return IsVisible(map.ProjectedCellCovering(pos), visibility);
		}

		public bool IsVisible(CPos cell, int visibility)
		{
			return IsVisible(cell.ToMPos(map), visibility);
		}

		public bool IsVisible(MPos uv, int visibility)
		{
			foreach (var puv in map.ProjectedCellsCovering(uv))
				if (IsVisible(puv, visibility))
					return true;

			return false;
		}

		// In internal shroud coords
		public bool IsVisible(PPos puv, int visibility)
		{
			if (!FogEnabled)
				return map.Contains(puv);

			return ResolvedVisibility.Contains(puv) && ResolvedVisibility[puv] > visibility;
		}

		public bool Contains(PPos uv)
		{
			// Check that uv is inside the map area. There is nothing special
			// about explored here: any of the CellLayers would have been suitable.
			return explored.Contains(uv);
		}

		public byte Normalize(byte check)
		{
			// TODO add not explored and all visible
			if (fogEnabled)
			{
				return check <= 1 ? (byte)1 : check >= 10 ? (byte)10 : check;
			}
			else if (Disabled) // ??
			{

			}

			return (byte)5;
		}

		public byte GetVisibility(WPos pos)
		{
			return GetVisibility(map.ProjectedCellCovering(pos));
		}

		public byte GetVisibility(PPos puv)
		{
			if (fogEnabled)
			{
				if (ResolvedVisibility.Contains(puv))
				{
					byte resolved = (byte)ResolvedVisibility[puv];
					byte modify = (byte)map.ModifyVisualLayer[(MPos)puv];

					return (byte)Normalize((byte)(resolved - modify));
				}
			}

			if (Disabled && map.Contains(puv))
			{
				return 10;
			}
			else if (ResolvedVisibility.Contains(puv) && ResolvedVisibility[puv] > 0)
			{
				return 10;
			}

			return 0;
		}
	}
}
