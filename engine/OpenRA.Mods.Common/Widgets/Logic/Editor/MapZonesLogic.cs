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
 * THE ZONES TOOL PANEL. Modelled on MapMarkerTilesLogic; see EditorZoneBrush for what the brush does
 * differently and why.
 *
 * THE SPLIT READOUT IS THE REASON THIS PANEL IS WORTH BUILDING. A DEFCON border that does not cut
 * the map in two is not a weak border -- it is NO border: DefconWall.BuildRegion discards a region
 * whose component count is below two and the wall never goes up, silently, at DEFCON 3, in a match
 * (DefconWallRegion.IsDegenerate). Before this tool the only way to find that out was to run
 * tools/nav-guard/defcon_wall_audit.py against a border already pasted into rules.yaml. Now the
 * mapper sees it while painting, from the ENGINE'S OWN flood rather than a lookalike -- see
 * ZoneLayerOverlay.ComponentCount.
 *
 * WHY THE READOUT IS THROTTLED RATHER THAN LIVE. ComponentCount runs a multi-source BFS over the
 * whole of Bounds, which is up to 256x256. That is nothing once, and too much every frame of a drag
 * across a 128-cell band. So it is recomputed at most every RecomputeIntervalMs, and only when the
 * layer's revision has actually moved -- which covers strokes, undo, redo and Clear alike, because
 * all four go through ZoneLayerOverlay.SetCell. A quarter-second lag on a number that only matters
 * when you stop painting is not observable.
 *
 * ESCAPE CANCELS THE BRUSH HERE, not in the brush class, because IEditorBrush has no key handling.
 * The listener lives inside this panel, so it is only reachable while the Zones tool is showing
 * (Widget.HandleKeyPressOuter early-returns on an invisible widget) -- and it is guarded on the
 * current brush anyway, so it can never eat an Escape meant for something else.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Widgets;
using Color = OpenRA.Primitives.Color;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class MapZonesLogic : ChromeLogic
	{
		[FluentReference("zone", "count")]
		const string ZoneSplits = "label-zone-splits-map";

		[FluentReference("zone")]
		const string ZoneDoesNotSplit = "label-zone-does-not-split-map";

		[FluentReference]
		const string NoZoneSelected = "label-zone-none-selected";

		/// <summary>How often the split readout may re-run its flood, in milliseconds.</summary>
		const int RecomputeIntervalMs = 250;

		readonly EditorActionManager editorActionManager;
		readonly ZoneLayerOverlay zoneLayerTrait;
		readonly EditorViewportControllerWidget editor;
		readonly Color warningColor;
		readonly Color normalColor;

		int? selectedZone;
		int brushSize = 3;

		int cachedRevision = -1;
		int cachedZone = -1;
		int cachedComponents;
		long nextRecomputeMs;

		[ObjectCreator.UseCtor]
		public MapZonesLogic(Widget widget, World world, ModData modData, WorldRenderer worldRenderer,
			Dictionary<string, MiniYaml> logicArgs)
		{
			// Constructed for every mod that loads the shared editor chrome, whether or not that mod
			// declares the zone layer. Without it there is nothing to paint, so the panel stays the
			// hidden widget the chrome shipped and MapToolsLogic never offers the tool.
			zoneLayerTrait = world.WorldActor.TraitOrDefault<ZoneLayerOverlay>();
			if (zoneLayerTrait == null)
				return;

			editorActionManager = world.WorldActor.Trait<EditorActionManager>();

			editor = widget.Parent.Parent.Parent.Parent.Get<EditorViewportControllerWidget>("MAP_EDITOR");
			editor.BrushChanged += HandleBrushChanged;

			var splitLabel = widget.Get<LabelWidget>("ZONE_SPLIT_LABEL");
			normalColor = splitLabel.TextColor;
			warningColor = ChromeMetrics.Get<Color>("NoticeErrorColor");

			var zoneList = widget.Get<ScrollPanelWidget>("ZONE_LIST");
			{
				var template = zoneList.Get<ScrollItemWidget>("ZONE_TEMPLATE");
				zoneList.RemoveChildren();

				// ONE ITEM PER Info.Zones ENTRY, so adding a zone kind really is the one dictionary
				// line ZoneLayerOverlayInfo advertises -- nothing here names DMZ.
				for (var i = 0; i < zoneLayerTrait.ZoneIds.Length; i++)
					zoneList.AddChild(SetupZoneItem(i, template));

				ScrollItemWidget SetupZoneItem(int index, ScrollItemWidget itemTemplate)
				{
					var item = ScrollItemWidget.Setup(itemTemplate,
						() => selectedZone == index && editor.CurrentBrush is EditorZoneBrush,
						() =>
						{
							selectedZone = index;
							editor.SetBrush(new EditorZoneBrush(editor, zoneLayerTrait, index, () => brushSize, worldRenderer));
						});

					var colorWidget = item.Get<ColorBlockWidget>("ZONE_COLOR");
					colorWidget.GetColor = () => zoneLayerTrait.ColorOf(index);

					var labelWidget = item.Get<LabelWidget>("ZONE_LABEL");
					labelWidget.GetText = () => zoneLayerTrait.ZoneIds[index];

					return item;
				}
			}

			var clearButton = widget.Get<ButtonWidget>("CLEAR_ZONE_BUTTON");
			clearButton.IsDisabled = () => selectedZone == null || zoneLayerTrait.CellsOf(selectedZone.Value).Count == 0;
			clearButton.OnClick = ClearZone;

			var sizeSlider = widget.Get<SliderWidget>("BRUSH_SIZE_SLIDER");
			sizeSlider.MinimumValue = EditorZoneBrush.MinSize;
			sizeSlider.MaximumValue = EditorZoneBrush.MaxSize;
			sizeSlider.Ticks = EditorZoneBrush.MaxSize - EditorZoneBrush.MinSize + 1;
			sizeSlider.OnChange += val => brushSize = ((int)val).Clamp(EditorZoneBrush.MinSize, EditorZoneBrush.MaxSize);
			sizeSlider.GetValue = () => brushSize;

			var sizeValue = widget.Get<LabelWidget>("BRUSH_SIZE_VALUE");
			sizeValue.GetText = () => brushSize.ToString(NumberFormatInfo.InvariantInfo);

			splitLabel.GetText = SplitText;
			splitLabel.GetColor = () => selectedZone != null && Components(selectedZone.Value) < 2 ? warningColor : normalColor;

			var keyhandler = widget.Get<LogicKeyListenerWidget>("ZONE_KEYHANDLER");
			keyhandler.AddHandler(e =>
			{
				if (e.Event != KeyInputEvent.Down || e.Key != Keycode.ESCAPE)
					return false;

				if (editor.CurrentBrush is not EditorZoneBrush)
					return false;

				editor.ClearBrush();
				return true;
			});
		}

		protected override void Dispose(bool disposing)
		{
			if (editor != null)
				editor.BrushChanged -= HandleBrushChanged;

			base.Dispose(disposing);
		}

		void HandleBrushChanged()
		{
			if (editor.CurrentBrush is not EditorZoneBrush)
				selectedZone = null;
		}

		void ClearZone()
		{
			if (selectedZone != null && zoneLayerTrait.CellsOf(selectedZone.Value).Count > 0)
				editorActionManager.Add(new ClearZoneEditorAction(zoneLayerTrait, selectedZone.Value));
		}

		/// <summary>The cached component count, re-floodied at most once per <see cref="RecomputeIntervalMs"/>.</summary>
		int Components(int zone)
		{
			// THE ZONE IS PART OF THE CACHE KEY, not just the revision. Selecting a different zone
			// changes the answer without changing the layer, and with one shipped zone that would be
			// an unobservable bug waiting for the second one.
			var fresh = cachedRevision == zoneLayerTrait.Revision && cachedZone == zone;
			if (fresh || (cachedZone == zone && Game.RunTime < nextRecomputeMs))
				return cachedComponents;

			cachedRevision = zoneLayerTrait.Revision;
			cachedZone = zone;
			cachedComponents = zoneLayerTrait.ComponentCount(zone);
			nextRecomputeMs = Game.RunTime + RecomputeIntervalMs;
			return cachedComponents;
		}

		string SplitText()
		{
			if (selectedZone == null)
				return FluentProvider.GetMessage(NoZoneSelected);

			var zone = zoneLayerTrait.ZoneIds[selectedZone.Value];
			var components = Components(selectedZone.Value);

			// BELOW TWO IS NOT "NEARLY THERE", IT IS OFF. Worded as a statement about the runtime
			// rather than a count, because the count alone reads like a minor imperfection.
			return components < 2
				? FluentProvider.GetMessage(ZoneDoesNotSplit, "zone", zone)
				: FluentProvider.GetMessage(ZoneSplits, "zone", zone, "count", components);
		}
	}
}
