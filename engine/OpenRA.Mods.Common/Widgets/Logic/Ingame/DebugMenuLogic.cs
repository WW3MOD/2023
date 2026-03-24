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
using System.IO;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class DebugMenuLogic : ChromeLogic
	{
		static readonly string PresetPath = Path.Combine(Platform.SupportDir, "ww3mod", "debug-preset.yaml");

		// Color scheme: OFF = red, ME = green, ALL = blue
		static readonly Color ColorOff = Color.FromArgb(255, 80, 80);
		static readonly Color ColorMe = Color.FromArgb(80, 220, 80);
		static readonly Color ColorAll = Color.FromArgb(80, 160, 255);

		// Static so state persists across panel open/close within the same game
		static readonly Dictionary<string, int> CheatStates = new Dictionary<string, int>();

		[ObjectCreator.UseCtor]
		public DebugMenuLogic(Widget widget, World world)
		{
			var devTrait = world.LocalPlayer.PlayerActor.Trait<DeveloperMode>();
			var debugVis = world.WorldActor.TraitOrDefault<DebugVisualizations>();
			var terrainGeometryTrait = world.WorldActor.TraitOrDefault<TerrainGeometryOverlay>();
			var customTerrainDebugTrait = world.WorldActor.TraitOrDefault<CustomTerrainDebugOverlay>();

			// Sync button states from actual DeveloperMode trait values on panel open
			SyncStatesFromTrait(world, devTrait);

			// --- 3-state cheat buttons ---
			// Left column
			Bind3StateButton(widget, world, "INSTANT_BUILD", "Quick Build",
				"DevFastBuild", "DevFastBuildAll", "DevFastBuildReset");

			Bind3StateButton(widget, world, "ENABLE_TECH", "Build Everything",
				"DevEnableTech", "DevEnableTechAll", "DevEnableTechReset");

			Bind3StateButton(widget, world, "BUILD_ANYWHERE", "Build Anywhere",
				"DevBuildAnywhere", "DevBuildAnywhereAll", "DevBuildAnywhereReset");

			Bind3StateButton(widget, world, "INSTANT_CHARGE", "Instant Charge",
				"DevFastCharge", "DevFastChargeAll", "DevFastChargeReset");

			// Right column
			Bind3StateButton(widget, world, "COSMETIC_REVEAL", "Reveal All",
				"DevCosmeticReveal", "DevCosmeticRevealAll", "DevCosmeticRevealReset");

			Bind3StateButton(widget, world, "CONTROL_ALL_UNITS", "Control All",
				"DevControlAllUnits", "DevControlAllUnitsAll", "DevControlAllUnitsReset");

			Bind3StateButton(widget, world, "DISABLE_SHROUD", "No Shroud",
				"DevVisibility", "DevVisibilityAll", "DevVisibilityReset");

			Bind3StateButton(widget, world, "SHOW_UNIT_PATHS", "Show Paths",
				"DevPathDebug", "DevPathDebugAll", "DevPathDebugReset");

			// --- Action buttons ---
			var cashButton = widget.GetOrNull<ButtonWidget>("GIVE_CASH");
			if (cashButton != null)
				cashButton.OnClick = () => IssueOrder(world, "DevGiveCash");

			var growResourcesButton = widget.GetOrNull<ButtonWidget>("GROW_RESOURCES");
			if (growResourcesButton != null)
				growResourcesButton.OnClick = () => IssueOrder(world, "DevGrowResources");

			var giveCashAllButton = widget.GetOrNull<ButtonWidget>("GIVE_CASH_ALL");
			if (giveCashAllButton != null)
				giveCashAllButton.OnClick = () => IssueOrder(world, "DevGiveCashAll");

			var explorationButton = widget.GetOrNull<ButtonWidget>("GIVE_EXPLORATION");
			if (explorationButton != null)
				explorationButton.OnClick = () => IssueOrder(world, "DevGiveExploration");

			var noexplorationButton = widget.GetOrNull<ButtonWidget>("RESET_EXPLORATION");
			if (noexplorationButton != null)
				noexplorationButton.OnClick = () => IssueOrder(world, "DevResetExploration");

			// --- Visualization checkboxes (local only, no 3-state) ---
			var showCombatCheckbox = widget.GetOrNull<CheckboxWidget>("SHOW_COMBATOVERLAY");
			if (showCombatCheckbox != null)
			{
				showCombatCheckbox.Disabled = debugVis == null;
				showCombatCheckbox.IsChecked = () => debugVis != null && debugVis.CombatGeometry;
				showCombatCheckbox.OnClick = () => debugVis.CombatGeometry ^= true;
			}

			var showGeometryCheckbox = widget.GetOrNull<CheckboxWidget>("SHOW_GEOMETRY");
			if (showGeometryCheckbox != null)
			{
				showGeometryCheckbox.Disabled = debugVis == null;
				showGeometryCheckbox.IsChecked = () => debugVis != null && debugVis.RenderGeometry;
				showGeometryCheckbox.OnClick = () => debugVis.RenderGeometry ^= true;
			}

			var showScreenMapCheckbox = widget.GetOrNull<CheckboxWidget>("SHOW_SCREENMAP");
			if (showScreenMapCheckbox != null)
			{
				showScreenMapCheckbox.Disabled = debugVis == null;
				showScreenMapCheckbox.IsChecked = () => debugVis != null && debugVis.ScreenMap;
				showScreenMapCheckbox.OnClick = () => debugVis.ScreenMap ^= true;
			}

			var showTerrainGeometryCheckbox = widget.GetOrNull<CheckboxWidget>("SHOW_TERRAIN_OVERLAY");
			if (showTerrainGeometryCheckbox != null && terrainGeometryTrait != null)
			{
				showTerrainGeometryCheckbox.IsChecked = () => terrainGeometryTrait.Enabled;
				showTerrainGeometryCheckbox.OnClick = () => terrainGeometryTrait.Enabled ^= true;
			}

			var showDepthPreviewCheckbox = widget.GetOrNull<CheckboxWidget>("SHOW_DEPTH_PREVIEW");
			if (showDepthPreviewCheckbox != null)
			{
				showDepthPreviewCheckbox.Disabled = debugVis == null;
				showDepthPreviewCheckbox.IsChecked = () => debugVis != null && debugVis.DepthBuffer;
				showDepthPreviewCheckbox.OnClick = () => debugVis.DepthBuffer ^= true;
			}

			var showActorTagsCheckbox = widget.GetOrNull<CheckboxWidget>("SHOW_ACTOR_TAGS");
			if (showActorTagsCheckbox != null)
			{
				showActorTagsCheckbox.Disabled = debugVis == null;
				showActorTagsCheckbox.IsChecked = () => debugVis != null && debugVis.ActorTags;
				showActorTagsCheckbox.OnClick = () => debugVis.ActorTags ^= true;
			}

			var showCustomTerrainCheckbox = widget.GetOrNull<CheckboxWidget>("SHOW_CUSTOMTERRAIN_OVERLAY");
			if (showCustomTerrainCheckbox != null)
			{
				showCustomTerrainCheckbox.Disabled = customTerrainDebugTrait == null;
				if (customTerrainDebugTrait != null)
				{
					showCustomTerrainCheckbox.IsChecked = () => customTerrainDebugTrait.Enabled;
					showCustomTerrainCheckbox.OnClick = () => customTerrainDebugTrait.Enabled ^= true;
				}
			}

			// --- Save/Load preset buttons ---
			var saveButton = widget.GetOrNull<ButtonWidget>("SAVE_PRESET");
			if (saveButton != null)
				saveButton.OnClick = () => SavePreset(debugVis, terrainGeometryTrait, customTerrainDebugTrait);

			var loadButton = widget.GetOrNull<ButtonWidget>("LOAD_PRESET");
			if (loadButton != null)
				loadButton.OnClick = () => LoadPreset(world, debugVis, terrainGeometryTrait, customTerrainDebugTrait);
		}

		/// <summary>
		/// Reads the actual DeveloperMode trait state and determines whether each cheat
		/// is OFF (0), ME only (1), or ALL players (2). Called every time the panel opens
		/// so the buttons always reflect reality.
		/// </summary>
		static void SyncStatesFromTrait(World world, DeveloperMode localDev)
		{
			SyncOneCheat(world, "INSTANT_BUILD", localDev.FastBuild, dm => dm.FastBuild);
			SyncOneCheat(world, "INSTANT_CHARGE", localDev.FastCharge, dm => dm.FastCharge);
			SyncOneCheat(world, "ENABLE_TECH", localDev.AllTech, dm => dm.AllTech);
			SyncOneCheat(world, "BUILD_ANYWHERE", localDev.BuildAnywhere, dm => dm.BuildAnywhere);
			SyncOneCheat(world, "DISABLE_SHROUD", localDev.DisableShroud, dm => dm.DisableShroud);
			SyncOneCheat(world, "COSMETIC_REVEAL", localDev.CosmeticReveal, dm => dm.CosmeticReveal);
			SyncOneCheat(world, "CONTROL_ALL_UNITS", localDev.ControlAllUnits, dm => dm.ControlAllUnits);
			SyncOneCheat(world, "SHOW_UNIT_PATHS", localDev.PathDebug, dm => dm.PathDebug);
		}

		static void SyncOneCheat(World world, string widgetId, bool localValue, Func<DeveloperMode, bool> getter)
		{
			if (!localValue)
			{
				CheatStates[widgetId] = 0;
				return;
			}

			// Local is on — check if ALL other playable players also have it on
			var allOn = true;
			foreach (var player in world.Players.Where(p => p.Playable && p != world.LocalPlayer))
			{
				var dm = player.PlayerActor.TraitOrDefault<DeveloperMode>();
				if (dm == null || !getter(dm))
				{
					allOn = false;
					break;
				}
			}

			CheatStates[widgetId] = allOn && world.Players.Any(p => p.Playable && p != world.LocalPlayer) ? 2 : 1;
		}

		void Bind3StateButton(Widget widget, World world, string widgetId, string label,
			string toggleOrder, string allOrder, string resetOrder)
		{
			var button = widget.GetOrNull<ButtonWidget>(widgetId);
			if (button == null)
				return;

			// Ensure key exists (SyncStatesFromTrait may not have set it if widget wasn't in the sync list)
			if (!CheatStates.ContainsKey(widgetId))
				CheatStates[widgetId] = 0;

			button.GetText = () =>
			{
				var state = CheatStates[widgetId];
				switch (state)
				{
					case 1: return label + ": ME";
					case 2: return label + ": ALL";
					default: return label + ": OFF";
				}
			};

			button.GetColor = () =>
			{
				var state = CheatStates[widgetId];
				switch (state)
				{
					case 1: return ColorMe;
					case 2: return ColorAll;
					default: return ColorOff;
				}
			};

			button.OnClick = () =>
			{
				var state = CheatStates[widgetId];
				switch (state)
				{
					case 0:
						CheatStates[widgetId] = 1;
						IssueOrder(world, toggleOrder);
						break;
					case 1:
						CheatStates[widgetId] = 2;
						IssueOrder(world, allOrder);
						break;
					case 2:
						CheatStates[widgetId] = 0;
						IssueOrder(world, resetOrder);
						break;
				}
			};

			button.OnRightClick = () =>
			{
				if (CheatStates[widgetId] != 0)
				{
					CheatStates[widgetId] = 0;
					IssueOrder(world, resetOrder);
				}
			};
		}

		void SavePreset(DebugVisualizations debugVis,
			TerrainGeometryOverlay terrainOverlay, CustomTerrainDebugOverlay customTerrainOverlay)
		{
			try
			{
				var dir = Path.GetDirectoryName(PresetPath);
				if (!Directory.Exists(dir))
					Directory.CreateDirectory(dir);

				var nodes = new List<MiniYamlNode>();

				// Cheat states
				var cheatNodes = new List<MiniYamlNode>();
				foreach (var kv in CheatStates)
					cheatNodes.Add(new MiniYamlNode(kv.Key, kv.Value.ToString()));
				nodes.Add(new MiniYamlNode("Cheats", new MiniYaml("", cheatNodes)));

				// Visualization toggles
				if (debugVis != null)
				{
					var visNodes = new List<MiniYamlNode>
					{
						new MiniYamlNode("CombatGeometry", debugVis.CombatGeometry.ToString()),
						new MiniYamlNode("RenderGeometry", debugVis.RenderGeometry.ToString()),
						new MiniYamlNode("ScreenMap", debugVis.ScreenMap.ToString()),
						new MiniYamlNode("ActorTags", debugVis.ActorTags.ToString()),
						new MiniYamlNode("DepthBuffer", debugVis.DepthBuffer.ToString()),
					};

					if (terrainOverlay != null)
						visNodes.Add(new MiniYamlNode("TerrainGeometry", terrainOverlay.Enabled.ToString()));

					if (customTerrainOverlay != null)
						visNodes.Add(new MiniYamlNode("CustomTerrain", customTerrainOverlay.Enabled.ToString()));

					nodes.Add(new MiniYamlNode("Visualizations", new MiniYaml("", visNodes)));
				}

				nodes.WriteToFile(PresetPath);
				TextNotificationsManager.Debug("Debug preset saved.");
			}
			catch (Exception e)
			{
				TextNotificationsManager.Debug($"Failed to save debug preset: {e.Message}");
			}
		}

		void LoadPreset(World world, DebugVisualizations debugVis,
			TerrainGeometryOverlay terrainOverlay, CustomTerrainDebugOverlay customTerrainOverlay)
		{
			if (!File.Exists(PresetPath))
			{
				TextNotificationsManager.Debug("No debug preset found.");
				return;
			}

			try
			{
				var yaml = MiniYaml.FromFile(PresetPath);
				foreach (var section in yaml)
				{
					if (section.Key == "Cheats")
					{
						foreach (var node in section.Value.Nodes)
						{
							if (!int.TryParse(node.Value.Value, out var targetState))
								continue;

							var widgetId = node.Key;
							if (!CheatStates.ContainsKey(widgetId))
								continue;

							var orders = GetOrdersForWidget(widgetId);
							if (orders == null)
								continue;

							// Reset first if currently active
							if (CheatStates[widgetId] != 0)
							{
								IssueOrder(world, orders.Value.reset);
								CheatStates[widgetId] = 0;
							}

							// Apply target state
							if (targetState == 1)
							{
								IssueOrder(world, orders.Value.toggle);
								CheatStates[widgetId] = 1;
							}
							else if (targetState == 2)
							{
								IssueOrder(world, orders.Value.all);
								CheatStates[widgetId] = 2;
							}
						}
					}
					else if (section.Key == "Visualizations")
					{
						foreach (var node in section.Value.Nodes)
						{
							if (!bool.TryParse(node.Value.Value, out var val))
								continue;

							switch (node.Key)
							{
								case "CombatGeometry":
									if (debugVis != null) debugVis.CombatGeometry = val;
									break;
								case "RenderGeometry":
									if (debugVis != null) debugVis.RenderGeometry = val;
									break;
								case "ScreenMap":
									if (debugVis != null) debugVis.ScreenMap = val;
									break;
								case "ActorTags":
									if (debugVis != null) debugVis.ActorTags = val;
									break;
								case "DepthBuffer":
									if (debugVis != null) debugVis.DepthBuffer = val;
									break;
								case "TerrainGeometry":
									if (terrainOverlay != null) terrainOverlay.Enabled = val;
									break;
								case "CustomTerrain":
									if (customTerrainOverlay != null) customTerrainOverlay.Enabled = val;
									break;
							}
						}
					}
				}

				TextNotificationsManager.Debug("Debug preset loaded.");
			}
			catch (Exception e)
			{
				TextNotificationsManager.Debug($"Failed to load debug preset: {e.Message}");
			}
		}

		static (string toggle, string all, string reset)? GetOrdersForWidget(string widgetId)
		{
			switch (widgetId)
			{
				case "INSTANT_BUILD": return ("DevFastBuild", "DevFastBuildAll", "DevFastBuildReset");
				case "INSTANT_CHARGE": return ("DevFastCharge", "DevFastChargeAll", "DevFastChargeReset");
				case "ENABLE_TECH": return ("DevEnableTech", "DevEnableTechAll", "DevEnableTechReset");
				case "BUILD_ANYWHERE": return ("DevBuildAnywhere", "DevBuildAnywhereAll", "DevBuildAnywhereReset");
				case "DISABLE_SHROUD": return ("DevVisibility", "DevVisibilityAll", "DevVisibilityReset");
				case "COSMETIC_REVEAL": return ("DevCosmeticReveal", "DevCosmeticRevealAll", "DevCosmeticRevealReset");
				case "CONTROL_ALL_UNITS": return ("DevControlAllUnits", "DevControlAllUnitsAll", "DevControlAllUnitsReset");
				case "SHOW_UNIT_PATHS": return ("DevPathDebug", "DevPathDebugAll", "DevPathDebugReset");
				default: return null;
			}
		}

		public static void IssueOrder(World world, string order)
		{
			world.IssueOrder(new Order(order, world.LocalPlayer.PlayerActor, false));
		}
	}
}
