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
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class MapToolsLogic : ChromeLogic
	{
		[FluentReference]
		const string MarkerTiles = "label-tool-marker-tiles";

		[FluentReference]
		const string Zones = "label-tool-zones";

		enum MapTool
		{
			MarkerTiles,
			Zones
		}

		readonly DropDownButtonWidget toolsDropdown;
		readonly Dictionary<MapTool, string> toolNames = new()
		{
			{ MapTool.MarkerTiles, MarkerTiles },
			{ MapTool.Zones, Zones }
		};

		readonly Dictionary<MapTool, Widget> toolPanels = new();

		MapTool selectedTool = MapTool.MarkerTiles;

		[ObjectCreator.UseCtor]
		public MapToolsLogic(Widget widget, World world, ModData modData, WorldRenderer worldRenderer, Dictionary<string, MiniYaml> logicArgs)
		{
			toolsDropdown = widget.Get<DropDownButtonWidget>("TOOLS_DROPDOWN");

			var markerToolPanel = widget.Get("MARKER_TOOL_PANEL");
			toolPanels.Add(MapTool.MarkerTiles, markerToolPanel);

			// THE ZONES TOOL IS CONDITIONAL ON THE MOD DECLARING ITS TRAIT, and that is not defensive
			// padding -- this chrome file is shared by every bundled mod, and only ww3mod declares
			// ZoneLayerOverlay on EditorWorld. A mod without it keeps exactly today's behaviour: one
			// tool, and a dropdown that disables itself below.
			var zoneToolPanel = widget.Get("ZONE_TOOL_PANEL");
			if (world.WorldActor.TraitOrDefault<ZoneLayerOverlay>() != null)
				toolPanels.Add(MapTool.Zones, zoneToolPanel);
			else
				zoneToolPanel.Visible = false;

			toolsDropdown.OnMouseDown = _ => ShowToolsDropDown(toolsDropdown);
			toolsDropdown.GetText = () => FluentProvider.GetMessage(toolNames[selectedTool]);

			// Enabled now that there is a second tool to switch to -- this was the TODO the slot was
			// left here for. Every panel but the selected one starts hidden; the chrome ships
			// ZONE_TOOL_PANEL with Visible: false and SelectTool does the rest.
			toolsDropdown.Disabled = toolPanels.Count < 2;
		}

		void ShowToolsDropDown(DropDownButtonWidget dropdown)
		{
			ScrollItemWidget SetupItem(MapTool tool, ScrollItemWidget itemTemplate)
			{
				var item = ScrollItemWidget.Setup(itemTemplate,
					() => selectedTool == tool,
					() => SelectTool(tool));

				item.Get<LabelWidget>("LABEL").GetText = () => FluentProvider.GetMessage(toolNames[tool]);

				return item;
			}

			var options = toolPanels.Keys.ToArray();
			dropdown.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 150, options, SetupItem);
		}

		void SelectTool(MapTool tool)
		{
			if (tool != selectedTool)
			{
				var currentToolPanel = toolPanels[selectedTool];
				currentToolPanel.Visible = false;
			}

			selectedTool = tool;

			var toolPanel = toolPanels[selectedTool];
			toolPanel.Visible = true;
		}
	}
}
