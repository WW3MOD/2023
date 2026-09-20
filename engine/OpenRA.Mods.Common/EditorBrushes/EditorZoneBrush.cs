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
 * THE ZONE BRUSH -- paints map.yaml's `Zones:` cells. See ZoneLayerOverlay for the layer it writes.
 *
 * RIGHT-DRAG ERASES HERE, AND EVERY OTHER BRUSH IN THE EDITOR USES RIGHT-CLICK TO CANCEL.
 * That divergence is deliberate and it is the one thing about this brush a reader has to be told.
 *
 *   WHY. A zone is a REGION the mapper shapes: paint a bit too far, take a bit back, judge the
 *   result, repeat. That loop is the whole task, and running it through "select the erase swatch,
 *   erase, select the zone swatch again" -- which is what the marker layer's X item makes you do --
 *   turns a two-second correction into four clicks. Every raster editor ever written binds the
 *   secondary button to the inverse of the primary one for exactly this reason. The tile and actor
 *   brushes have no inverse to bind, so the question never came up for them.
 *
 *   WHAT IT COSTS, and it is a real cost rather than a nominal one: right-click no longer cancels,
 *   so the editor's ONLY brush-cancel gesture does not work here. Escape is bound in its place, by
 *   MapZonesLogic through the zone panel's own LogicKeyListener -- not in this class, because
 *   IEditorBrush has no key handling at all. If you ever find the brush stuck, that is the key.
 *
 *   IT ERASES THE SELECTED ZONE ONLY. A right-drag across a cell belonging to a DIFFERENT zone
 *   leaves it alone, so erasing DMZ can never silently delete someone else's region.
 *
 * ONE UNDO STEP PER STROKE. The drag mutates the layer live, so Execute() is EMPTY and the replay
 * lives in Do() -- the contract IEditorAction states and the marker layer's action already follows
 * (EditorActionManager.Add calls Execute, redo calls Do). The action is registered on mouse-UP and
 * only if the stroke actually changed a cell, so dragging over ground that is already painted adds
 * nothing to the history.
 *
 * THE BRUSH IS ROUND AND ITS SIZE IS A DIAMETER IN CELLS, integer-only: a cell is in the brush when
 * 4*(dx^2 + dy^2) <= size^2. That is the disc of diameter `size` with the comparison scaled by two
 * to keep the half-cell radius in integers. Size 1 is a single cell and size 3 is exactly the 3x3
 * block, which is the width every shipped DEFCON border is drawn at -- see the band-thickness note
 * in DefconWallInfo.RegionCells for why three and not one.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Widgets;
using Color = OpenRA.Primitives.Color;

namespace OpenRA.Mods.Common.Widgets
{
	public sealed class EditorZoneBrush : IEditorBrush
	{
		/// <summary>The smallest and largest brush diameter the size slider offers.</summary>
		public const int MinSize = 1;
		public const int MaxSize = 9;

		public readonly int Zone;

		readonly WorldRenderer worldRenderer;
		readonly World world;
		readonly EditorActionManager editorActionManager;
		readonly ZoneLayerOverlay overlay;
		readonly Func<int> getSize;

		PaintZoneEditorAction action;
		MouseButton? stroke;
		CPos cursor;

		public EditorZoneBrush(EditorViewportControllerWidget editorWidget, ZoneLayerOverlay overlay,
			int zone, Func<int> getSize, WorldRenderer wr)
		{
			worldRenderer = wr;
			world = wr.World;
			this.overlay = overlay;
			this.getSize = getSize;
			Zone = zone;

			editorActionManager = world.WorldActor.Trait<EditorActionManager>();
			action = new PaintZoneEditorAction(overlay, zone, erasing: false);
			cursor = wr.Viewport.ViewToWorld(wr.Viewport.WorldToViewPx(Viewport.LastMousePos));
		}

		/// <summary>
		/// The cells a brush of <paramref name="size"/> covers when centred on <paramref name="center"/>.
		/// Pure and static so the footprint the preview draws is provably the footprint the stroke paints.
		/// </summary>
		public static IEnumerable<CPos> Footprint(CPos center, int size)
		{
			size = size.Clamp(MinSize, MaxSize);
			var limit = size * size;
			var reach = size / 2;

			for (var dy = -reach; dy <= reach; dy++)
				for (var dx = -reach; dx <= reach; dx++)
					if (4 * (dx * dx + dy * dy) <= limit)
						yield return center + new CVec(dx, dy);
		}

		public bool HandleMouseInput(MouseInput mi)
		{
			if (mi.Button != MouseButton.Left && mi.Button != MouseButton.Right)
				return false;

			// A second button pressed mid-stroke is ignored rather than switching the stroke's sense
			// half way through, which would put a paint and an erase in one undo step.
			if (stroke.HasValue && mi.Button != stroke.Value)
				return true;

			if (mi.Event == MouseInputEvent.Down)
			{
				stroke = mi.Button;
				action = new PaintZoneEditorAction(overlay, Zone, erasing: mi.Button == MouseButton.Right);
			}

			if (!stroke.HasValue)
				return true;

			if (mi.Event == MouseInputEvent.Down || mi.Event == MouseInputEvent.Move)
			{
				cursor = worldRenderer.Viewport.ViewToWorld(mi.Location);
				action.Add(Footprint(cursor, getSize()));
			}
			else if (mi.Event == MouseInputEvent.Up)
			{
				if (action.DidChangeCells)
					editorActionManager.Add(action);

				action = new PaintZoneEditorAction(overlay, Zone, erasing: false);
				stroke = null;
			}

			return true;
		}

		public void Tick()
		{
			ApplyPendingTestStroke();

			if (!stroke.HasValue)
				cursor = worldRenderer.Viewport.ViewToWorld(worldRenderer.Viewport.WorldToViewPx(Viewport.LastMousePos));
		}

		/// <summary>
		/// Consume one stroke armed by the `zone-paint` / `zone-erase` cmd-file verbs, so a
		/// screenshot driver can cut a hole in a band with no cursor and no human.
		/// </summary>
		// IT GOES THROUGH PaintZoneEditorAction, NOT THROUGH ZoneLayerOverlay.SetCell. A scripted
		// stroke that wrote cells directly would paint the same pixels and be a different operation:
		// not undoable, absent from the editor's history, and -- because it would skip the action --
		// trivially divergent from what a dragged stroke does the next time either changes. The only
		// thing this does that a drag does not is arrive in one tick instead of over several.
		void ApplyPendingTestStroke()
		{
			if (!TestMode.IsActive)
				return;

			var pending = TestMode.ZoneStroke;
			if (string.IsNullOrEmpty(pending))
				return;

			// Cleared whether or not it parses: a malformed stroke that stayed armed would be
			// retried every tick for the rest of the session.
			TestMode.ZoneStroke = null;

			var space = pending.IndexOf(' ');
			if (space < 0)
			{
				Log.Write("debug", $"[TestMode] zone stroke ignored, no argument: '{pending}'");
				return;
			}

			var erasing = pending.StartsWith("erase", StringComparison.OrdinalIgnoreCase);
			var parts = pending[(space + 1)..].Split(',');
			if (parts.Length < 2 ||
				!int.TryParse(parts[0].Trim(), NumberStyles.Integer, NumberFormatInfo.InvariantInfo, out var x) ||
				!int.TryParse(parts[1].Trim(), NumberStyles.Integer, NumberFormatInfo.InvariantInfo, out var y))
			{
				Log.Write("debug", $"[TestMode] zone stroke ignored, expected <x>,<y>[,<size>]: '{pending}'");
				return;
			}

			var size = getSize();
			if (parts.Length > 2 && int.TryParse(parts[2].Trim(), NumberStyles.Integer, NumberFormatInfo.InvariantInfo, out var s))
				size = s.Clamp(MinSize, MaxSize);

			var scripted = new PaintZoneEditorAction(overlay, Zone, erasing);
			scripted.Add(Footprint(new CPos(x, y), size));
			if (scripted.DidChangeCells)
				editorActionManager.Add(scripted);

			Log.Write("debug", $"[TestMode] zone stroke applied: {(erasing ? "erase" : "paint")} " +
				$"{x},{y} size {size} -> {(scripted.DidChangeCells ? "changed cells" : "CHANGED NOTHING")}");
		}

		void IEditorBrush.TickRender(WorldRenderer wr, Actor self) { }

		IEnumerable<IRenderable> IEditorBrush.RenderAboveShroud(Actor self, WorldRenderer wr) { yield break; }

		// THE FOOTPRINT PREVIEW IS THE WHOLE POINT OF DRAWING ANYTHING HERE. At size 9 the brush
		// covers 69 cells; without a preview the mapper finds out where it reached by painting.
		IEnumerable<IRenderable> IEditorBrush.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			var color = Color.FromArgb(160, overlay.ColorOf(Zone));
			foreach (var cell in Footprint(cursor, getSize()))
				if (world.Map.Contains(cell))
					yield return new MarkerTileRenderable(cell, color);
		}

		public void Dispose() { }
	}

	readonly struct PaintZoneCell
	{
		public readonly CPos Cell;
		public readonly int? Previous;

		public PaintZoneCell(CPos cell, int? previous)
		{
			Cell = cell;
			Previous = previous;
		}
	}

	sealed class PaintZoneEditorAction : IEditorAction
	{
		[FluentReference("amount", "zone")]
		const string AddedZoneCells = "notification-added-zone-cells";

		[FluentReference("amount", "zone")]
		const string RemovedZoneCells = "notification-removed-zone-cells";

		public string Text { get; private set; }

		readonly ZoneLayerOverlay overlay;
		readonly int zone;
		readonly bool erasing;
		readonly List<PaintZoneCell> changed = new();

		public bool DidChangeCells => changed.Count > 0;

		public PaintZoneEditorAction(ZoneLayerOverlay overlay, int zone, bool erasing)
		{
			this.overlay = overlay;
			this.zone = zone;
			this.erasing = erasing;
			Text = "";
		}

		/// <summary>EMPTY ON PURPOSE -- the stroke already mutated the layer. See the file header.</summary>
		public void Execute()
		{
		}

		public void Do()
		{
			foreach (var c in changed)
				overlay.SetCell(c.Cell, erasing ? null : zone);
		}

		public void Undo()
		{
			foreach (var c in changed)
				overlay.SetCell(c.Cell, c.Previous);
		}

		public void Add(IEnumerable<CPos> cells)
		{
			foreach (var cell in cells)
			{
				if (!overlay.CanPaint(cell))
					continue;

				var previous = overlay.CellLayer[cell];

				// Already at the target state, so it is not part of this stroke and must not enter the
				// undo record. An erase over a cell of ANOTHER zone falls out of the same test: its
				// previous value is neither null nor ours, so it is skipped and that zone survives.
				if (erasing ? previous != zone : previous == zone)
					continue;

				changed.Add(new PaintZoneCell(cell, previous));
				overlay.SetCell(cell, erasing ? null : zone);
			}

			Text = FluentProvider.GetMessage(erasing ? RemovedZoneCells : AddedZoneCells,
				"amount", changed.Count, "zone", overlay.ZoneIds[zone]);
		}
	}

	sealed class ClearZoneEditorAction : IEditorAction
	{
		[FluentReference("amount", "zone")]
		const string ClearedZone = "notification-cleared-zone";

		public string Text { get; }

		readonly ZoneLayerOverlay overlay;
		readonly int zone;
		readonly CPos[] cells;

		public ClearZoneEditorAction(ZoneLayerOverlay overlay, int zone)
		{
			this.overlay = overlay;
			this.zone = zone;
			cells = overlay.CellsOf(zone).ToArray();

			Text = FluentProvider.GetMessage(ClearedZone, "amount", cells.Length, "zone", overlay.ZoneIds[zone]);
		}

		public void Execute()
		{
			Do();
		}

		public void Do()
		{
			overlay.ClearZone(zone);
		}

		public void Undo()
		{
			overlay.SetZone(zone, cells);
		}
	}
}
