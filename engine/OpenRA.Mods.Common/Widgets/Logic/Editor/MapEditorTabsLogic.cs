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

using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class MapEditorTabsLogic : ChromeLogic
	{
		enum MenuType { Select, Tiles, Layers, Actors, Tools, History }

		readonly Widget panelContainer;
		readonly Widget tabContainer;
		readonly EditorViewportControllerWidget editor;

		MenuType menuType = MenuType.Tiles;
		MenuType lastSelectedTab = MenuType.Tiles;

		[ObjectCreator.UseCtor]
		public MapEditorTabsLogic(Widget widget)
		{
			panelContainer = widget.Parent;
			tabContainer = widget.Get("MAP_EDITOR_TAB_CONTAINER");

			editor = widget.Parent.Parent.Get<EditorViewportControllerWidget>("MAP_EDITOR");
			editor.DefaultBrush.UpdateSelectedTab += HandleUpdateSelectedTab;

			SetupTab("SELECT_TAB", "SELECT_WIDGETS", MenuType.Select);
			SetupTab("TILES_TAB", "TILE_WIDGETS", MenuType.Tiles);
			SetupTab("OVERLAYS_TAB", "LAYER_WIDGETS", MenuType.Layers);
			SetupTab("ACTORS_TAB", "ACTOR_WIDGETS", MenuType.Actors);
			SetupTab("TOOLS_TAB", "TOOLS_WIDGETS", MenuType.Tools);
			SetupTab("HISTORY_TAB", "HISTORY_WIDGETS", MenuType.History);

			// WW3MOD: Test.EditorTool names a tool, and a tool that is selected but not SHOWING is
			// the shape of a screenshot that photographs the wrong panel and reports success.
			// MapToolsLogic selects the tool inside TOOLS_WIDGETS; this is what puts TOOLS_WIDGETS
			// on screen, because tab visibility lives here and nowhere else
			// (`container.IsVisible = () => menuType == tabType`).
			//
			// FOUND BY A CAPTURE, NOT BY READING: the first run of
			// tools/autotest/screenshot-editor-zones.sh came back with both frames showing the
			// TILES tab, because menuType defaults to Tiles and nothing moved it. The driver
			// reported PASS -- every marker it checked was about the tool, and none was about the
			// tab. lastSelectedTab is set too, so a selection appearing and clearing returns here
			// rather than to Tiles.
			if (TestMode.IsActive && !string.IsNullOrEmpty(TestMode.EditorTool))
			{
				menuType = lastSelectedTab = MenuType.Tools;
				Log.Write("debug", "[TestMode] editor tab: Tools");
			}
		}

		protected override void Dispose(bool disposing)
		{
			editor.DefaultBrush.UpdateSelectedTab -= HandleUpdateSelectedTab;

			base.Dispose(disposing);
		}

		void SetupTab(string buttonId, string tabId, MenuType tabType)
		{
			var tab = tabContainer.Get<ButtonWidget>(buttonId);
			tab.IsHighlighted = () => menuType == tabType;
			tab.OnClick = () =>
			{
				if (tabType != MenuType.Select)
					lastSelectedTab = tabType;

				menuType = tabType;

				// Clear keyboard focus when switching tabs.
				Ui.KeyboardFocusWidget = null;
			};

			// Selection tab is special, it can only be selected if a selection exists.
			if (tabType == MenuType.Select)
				tab.IsDisabled = () => !editor.DefaultBrush.Selection.HasSelection;

			var container = panelContainer.Get<ContainerWidget>(tabId);
			container.IsVisible = () => menuType == tabType;
		}

		void HandleUpdateSelectedTab()
		{
			var hasSelection = editor.DefaultBrush.Selection.HasSelection;

			if (menuType != MenuType.Select && hasSelection)
				menuType = MenuType.Select;
			else if (menuType == MenuType.Select && !hasSelection)
				menuType = lastSelectedTab;
		}
	}
}
