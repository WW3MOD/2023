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

/*
 * THE EDITOR SIDE OF map.yaml's `Zones:` NODE -- the painted cell regions, live in the editor world.
 *
 * MODELLED ON MarkerLayerOverlay AND DELIBERATELY NOT REUSING IT. The marker layer is the right
 * SHAPE for a painted per-cell layer -- a CellLayer of type ids, a per-type cell set for selective
 * clearing, an IRenderAnnotations overlay -- and it is copied here almost line for line. What is NOT
 * copied is where it puts the data: the marker layer serialises to JSON under Platform.SupportDir,
 * OUTSIDE the map package (MarkerLayerOverlay.WorldLoaded). That is correct for a mapper's private
 * scratch layer and completely wrong for map data, which has to travel with the map to every client
 * in a lobby. A zone lives in map.yaml and nowhere else.
 *
 * ONE ZONE PER CELL, which is the marker layer's model and is a real constraint rather than an
 * accident. Painting zone B over a cell of zone A removes it from A. With the single shipped zone
 * (DMZ) nothing can observe this. A second zone that is MEANT to overlap the first -- say a no-build
 * region drawn across the DMZ -- would need one CellLayer per zone instead, which is a contained
 * change to this file and the brush; the storage format (MapZones) already allows it, because it
 * keys cells by zone rather than the other way round.
 *
 * WORLD-ACTOR SAFETY. This is [TraitLocation(SystemActors.EditorWorld)], so World.WorldActor is
 * still null while its constructor and INotifyCreated.Created run (World.cs assigns WorldActor after
 * CreateActor returns; see tools/worldactor-gate). Nothing here reads .WorldActor -- the map comes
 * from self.World.Map, which is already assigned. The brush and the panel logic look this trait up
 * through world.WorldActor, and they are chrome, constructed long afterwards.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Traits;
using Color = OpenRA.Primitives.Color;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.EditorWorld)]
	public class ZoneLayerOverlayInfo : TraitInfo
	{
		[Desc("The zones this mod's editor can paint, as `Id: Colour`. The id is the key written to",
			"map.yaml's `Zones:` node and is shown verbatim in the editor's zone list -- `DMZ` is an",
			"acronym and reads the same in every language, which is why it is not a Fluent string.",
			"",
			"ADDING A ZONE IS THIS ONE LINE. The editor list, the overlay colours and the save path",
			"are all driven from this dictionary. What a new id does NOT get is engine meaning: only",
			"`DMZ` is read at runtime (DefconWall unions it into the region path). Any other id is",
			"map metadata that rides along in the package until something is written to read it.")]
		public readonly Dictionary<string, Color> Zones = new()
		{
			{ MapZones.Dmz, Color.FromArgb(0, 168, 255) },
		};

		[Desc("Alpha the zone fill is drawn at, 1-255. Low enough to read the terrain underneath,",
			"because a border is judged against the ground it is drawn on.")]
		public readonly int Alpha = 96;

		public override object Create(ActorInitializer init)
		{
			return new ZoneLayerOverlay(init.Self, this);
		}
	}

	public class ZoneLayerOverlay : IRenderAnnotations, IWorldLoaded
	{
		readonly World world;
		readonly Color[] blendedColors;

		/// <summary>Zone index per cell, or null for a cell in no zone. Index into <see cref="ZoneIds"/>.</summary>
		public readonly CellLayer<int?> CellLayer;

		/// <summary>The zone ids, sorted, so an index is stable for the lifetime of the editor session.</summary>
		// SORTED RATHER THAN IN Info.Zones's ORDER because Dictionary enumeration order is an
		// implementation detail, and these indices end up in undo records.
		public readonly string[] ZoneIds;

		readonly HashSet<CPos>[] cells;

		public bool Enabled = true;
		public ZoneLayerOverlayInfo Info { get; }

		/// <summary>
		/// Bumped every time a cell actually changes zone. The panel's split readout keys its cache on
		/// this, which is why it covers paint, erase, undo, redo and clear without any of them having
		/// to tell it: all five go through <see cref="SetCell"/>.
		/// </summary>
		public int Revision { get; private set; }

		public ZoneLayerOverlay(Actor self, ZoneLayerOverlayInfo info)
		{
			Info = info;
			world = self.World;

			ZoneIds = info.Zones.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
			cells = new HashSet<CPos>[ZoneIds.Length];
			blendedColors = new Color[ZoneIds.Length];
			for (var i = 0; i < ZoneIds.Length; i++)
			{
				cells[i] = new HashSet<CPos>();
				blendedColors[i] = Color.FromArgb(info.Alpha.Clamp(1, 255), info.Zones[ZoneIds[i]]);
			}

			CellLayer = new CellLayer<int?>(self.World.Map);
		}

		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			LoadFrom(w.Map);
		}

		/// <summary>The index of a zone id, or -1 for an id this mod's editor does not paint.</summary>
		public int IndexOf(string zoneId)
		{
			return Array.IndexOf(ZoneIds, zoneId);
		}

		/// <summary>The cells currently painted for one zone. Live -- do not mutate; use <see cref="SetCell"/>.</summary>
		public IReadOnlyCollection<CPos> CellsOf(int zone)
		{
			return cells[zone];
		}

		public Color ColorOf(int zone)
		{
			return Info.Zones[ZoneIds[zone]];
		}

		/// <summary>
		/// Paint or erase one cell. Out-of-bounds cells are REFUSED rather than stored, unlike the
		/// storage format, which keeps them: a zone may legitimately contain cells outside Bounds
		/// (see MapZones), but the editor cannot show the user a cell they cannot see or click, so it
		/// will not create one. A hand-authored out-of-Bounds cell survives a round trip anyway,
		/// because <see cref="WriteTo"/> only rewrites the zones this trait actually loaded.
		/// </summary>
		/// <summary>
		/// Is this a cell the brush may write? EXACTLY <see cref="SetCell"/>'s own test, exposed so an
		/// editor action can decide whether a cell belongs in its undo record BEFORE mutating. A
		/// record of cells SetCell silently refused would undo nothing and miscount the stroke.
		/// </summary>
		public bool CanPaint(CPos cell)
		{
			return world.Map.Contains(cell);
		}

		public void SetCell(CPos cell, int? zone)
		{
			if (!CanPaint(cell))
				return;

			var previous = CellLayer[cell];
			if (previous == zone)
				return;

			if (previous.HasValue)
				cells[previous.Value].Remove(cell);

			if (zone.HasValue)
				cells[zone.Value].Add(cell);

			CellLayer[cell] = zone;
			Revision++;
		}

		/// <summary>Replace one zone's cells wholesale. The undo half of a clear.</summary>
		public void SetZone(int zone, IEnumerable<CPos> newCells)
		{
			foreach (var cell in cells[zone].ToArray())
				SetCell(cell, null);

			foreach (var cell in newCells)
				SetCell(cell, zone);
		}

		public void ClearZone(int zone)
		{
			foreach (var cell in cells[zone].ToArray())
				SetCell(cell, null);
		}

		/// <summary>Read every zone this editor paints out of the map. Zones it does not know are left alone.</summary>
		public void LoadFrom(Map map)
		{
			for (var i = 0; i < ZoneIds.Length; i++)
				SetZone(i, map.Zones[ZoneIds[i]]);
		}

		/// <summary>
		/// Write every zone this editor paints back into the map, ready for Map.Save. Called from
		/// SaveMapLogic.SaveMapInner immediately before the save, which is the one place every editor
		/// save path passes through.
		/// </summary>
		public void WriteTo(Map map)
		{
			var zones = map.Zones;
			for (var i = 0; i < ZoneIds.Length; i++)
				zones = zones.WithZone(ZoneIds[i], cells[i]);

			map.Zones = zones;
		}

		/// <summary>
		/// How many connected pieces this zone leaves the map's in-Bounds ground in. Two is the shape
		/// a DEFCON border wants; one means it divides nothing and DefconWall will discard it.
		/// </summary>
		// THE ENGINE'S OWN FLOOD, NOT A SECOND ONE. DefconWallRegion is what BuildRegion runs at load
		// to decide whether to raise the wall at all, and it takes the same Map.Contains passability
		// predicate here that BuildRegion passes there. A readout computed by a lookalike flood with
		// slightly different rules -- 4- versus 8-connected, Bounds versus AllCells -- would tell the
		// mapper their border is fine and then not raise it, which is the exact failure this readout
		// exists to prevent.
		public int ComponentCount(int zone)
		{
			var bounds = world.Map.Bounds;
			var region = new DefconWallRegion(bounds.Left, bounds.Top, bounds.Width, bounds.Height,
				cells[zone], c => world.Map.Contains(c));

			return region.ComponentCount;
		}

		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (!Enabled)
				yield break;

			for (var i = 0; i < cells.Length; i++)
				foreach (var cell in cells[i])
					yield return new MarkerTileRenderable(cell, blendedColors[i]);
		}

		bool IRenderAnnotations.SpatiallyPartitionable => false;
	}
}
