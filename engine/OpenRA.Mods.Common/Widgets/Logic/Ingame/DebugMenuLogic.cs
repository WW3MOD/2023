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
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class DebugMenuLogic : ChromeLogic
	{
		static readonly string PresetPath = Path.Combine(Platform.SupportDir, "ww3mod", "debug-preset.yaml");

		// Tracked locally so save/load can read and set it
		int fastBuildState;

		[ObjectCreator.UseCtor]
		public DebugMenuLogic(Widget widget, World world)
		{
			var devTrait = world.LocalPlayer.PlayerActor.Trait<DeveloperMode>();
			var debugVis = world.WorldActor.TraitOrDefault<DebugVisualizations>();
			var terrainGeometryTrait = world.WorldActor.TraitOrDefault<TerrainGeometryOverlay>();
			var customTerrainDebugTrait = world.WorldActor.TraitOrDefault<CustomTerrainDebugOverlay>();

			var visibilityCheckbox = widget.GetOrNull<CheckboxWidget>("DISABLE_VISIBILITY_CHECKS");
			if (visibilityCheckbox != null)
				BindOrderCheckbox(visibilityCheckbox, world, "DevVisibility", () => devTrait.DisableShroud);

			var cosmeticRevealCheckbox = widget.GetOrNull<CheckboxWidget>("COSMETIC_REVEAL");
			if (cosmeticRevealCheckbox != null)
				BindOrderCheckbox(cosmeticRevealCheckbox, world, "DevCosmeticReveal", () => devTrait.CosmeticReveal);

			var controlAllCheckbox = widget.GetOrNull<CheckboxWidget>("CONTROL_ALL_UNITS");
			if (controlAllCheckbox != null)
				BindOrderCheckbox(controlAllCheckbox, world, "DevControlAllUnits", () => devTrait.ControlAllUnits);

			var pathCheckbox = widget.GetOrNull<CheckboxWidget>("SHOW_UNIT_PATHS");
			if (pathCheckbox != null)
				BindOrderCheckbox(pathCheckbox, world, "DevPathDebug", () => devTrait.PathDebug);

			var cashButton = widget.GetOrNull<ButtonWidget>("GIVE_CASH");
			if (cashButton != null)
				cashButton.OnClick = () => IssueOrder(world, "DevGiveCash");

			var growResourcesButton = widget.GetOrNull<ButtonWidget>("GROW_RESOURCES");
			if (growResourcesButton != null)
				growResourcesButton.OnClick = () => IssueOrder(world, "DevGrowResources");

			var giveCashAllButton = widget.GetOrNull<ButtonWidget>("GIVE_CASH_ALL");
			if (giveCashAllButton != null)
				giveCashAllButton.OnClick = () => IssueOrder(world, "DevGiveCashAll");

			// Instant Build: 3-state button (Off → Me → All, right-click resets)
			var fastBuildButton = widget.GetOrNull<ButtonWidget>("INSTANT_BUILD");
			if (fastBuildButton != null)
			{
				fastBuildButton.GetText = () =>
				{
					switch (fastBuildState)
					{
						case 1: return "Instant Build: ME";
						case 2: return "Instant Build: ALL";
						default: return "Instant Build: OFF";
					}
				};

				fastBuildButton.OnClick = () =>
				{
					switch (fastBuildState)
					{
						case 0:
							fastBuildState = 1;
							IssueOrder(world, "DevFastBuild");
							break;
						case 1:
							fastBuildState = 2;
							IssueOrder(world, "DevFastBuildAll");
							break;
						case 2:
							fastBuildState = 0;
							IssueOrder(world, "DevFastBuildReset");
							break;
					}
				};

				fastBuildButton.OnRightClick = () =>
				{
					if (fastBuildState != 0)
					{
						fastBuildState = 0;
						IssueOrder(world, "DevFastBuildReset");
					}
				};
			}

			var fastChargeCheckbox = widget.GetOrNull<CheckboxWidget>("INSTANT_CHARGE");
			if (fastChargeCheckbox != null)
				BindOrderCheckbox(fastChargeCheckbox, world, "DevFastCharge", () => devTrait.FastCharge);

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

			var allTechCheckbox = widget.GetOrNull<CheckboxWidget>("ENABLE_TECH");
			if (allTechCheckbox != null)
				BindOrderCheckbox(allTechCheckbox, world, "DevEnableTech", () => devTrait.AllTech);

			var powerCheckbox = widget.GetOrNull<CheckboxWidget>("UNLIMITED_POWER");
			if (powerCheckbox != null)
				BindOrderCheckbox(powerCheckbox, world, "DevUnlimitedPower", () => devTrait.UnlimitedPower);

			var buildAnywhereCheckbox = widget.GetOrNull<CheckboxWidget>("BUILD_ANYWHERE");
			if (buildAnywhereCheckbox != null)
				BindOrderCheckbox(buildAnywhereCheckbox, world, "DevBuildAnywhere", () => devTrait.BuildAnywhere);

			var explorationButton = widget.GetOrNull<ButtonWidget>("GIVE_EXPLORATION");
			if (explorationButton != null)
				explorationButton.OnClick = () => IssueOrder(world, "DevGiveExploration");

			var noexplorationButton = widget.GetOrNull<ButtonWidget>("RESET_EXPLORATION");
			if (noexplorationButton != null)
				noexplorationButton.OnClick = () => IssueOrder(world, "DevResetExploration");

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

			// Save/Load preset buttons
			var saveButton = widget.GetOrNull<ButtonWidget>("SAVE_PRESET");
			if (saveButton != null)
				saveButton.OnClick = () => SavePreset(devTrait, debugVis, terrainGeometryTrait, customTerrainDebugTrait);

			var loadButton = widget.GetOrNull<ButtonWidget>("LOAD_PRESET");
			if (loadButton != null)
				loadButton.OnClick = () => LoadPreset(world, devTrait, debugVis, terrainGeometryTrait, customTerrainDebugTrait);
		}

		void SavePreset(DeveloperMode devTrait, DebugVisualizations debugVis,
			TerrainGeometryOverlay terrainOverlay, CustomTerrainDebugOverlay customTerrainOverlay)
		{
			try
			{
				var dir = Path.GetDirectoryName(PresetPath);
				if (!Directory.Exists(dir))
					Directory.CreateDirectory(dir);

				var nodes = new List<MiniYamlNode>();

				// DeveloperMode toggles
				var devNodes = new List<MiniYamlNode>
				{
					new MiniYamlNode("FastBuild", fastBuildState.ToString()),
					new MiniYamlNode("FastCharge", devTrait.FastCharge.ToString()),
					new MiniYamlNode("AllTech", devTrait.AllTech.ToString()),
					new MiniYamlNode("BuildAnywhere", devTrait.BuildAnywhere.ToString()),
					new MiniYamlNode("DisableShroud", devTrait.DisableShroud.ToString()),
					new MiniYamlNode("UnlimitedPower", devTrait.UnlimitedPower.ToString()),
					new MiniYamlNode("CosmeticReveal", devTrait.CosmeticReveal.ToString()),
					new MiniYamlNode("ControlAllUnits", devTrait.ControlAllUnits.ToString()),
					new MiniYamlNode("PathDebug", devTrait.PathDebug.ToString()),
				};
				nodes.Add(new MiniYamlNode("DeveloperMode", new MiniYaml("", devNodes)));

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

		void LoadPreset(World world, DeveloperMode devTrait, DebugVisualizations debugVis,
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
					if (section.Key == "DeveloperMode")
					{
						foreach (var node in section.Value.Nodes)
						{
							switch (node.Key)
							{
								case "FastBuild":
								{
									if (!int.TryParse(node.Value.Value, out var targetState))
										break;

									// Reset first, then apply target state
									if (fastBuildState != 0)
									{
										IssueOrder(world, "DevFastBuildReset");
										fastBuildState = 0;
									}

									if (targetState == 1)
									{
										IssueOrder(world, "DevFastBuild");
										fastBuildState = 1;
									}
									else if (targetState == 2)
									{
										IssueOrder(world, "DevFastBuildAll");
										fastBuildState = 2;
									}

									break;
								}

								case "FastCharge":
									ToggleIfNeeded(world, "DevFastCharge", devTrait.FastCharge, node.Value.Value);
									break;
								case "AllTech":
									ToggleIfNeeded(world, "DevEnableTech", devTrait.AllTech, node.Value.Value);
									break;
								case "BuildAnywhere":
									ToggleIfNeeded(world, "DevBuildAnywhere", devTrait.BuildAnywhere, node.Value.Value);
									break;
								case "DisableShroud":
									ToggleIfNeeded(world, "DevVisibility", devTrait.DisableShroud, node.Value.Value);
									break;
								case "UnlimitedPower":
									ToggleIfNeeded(world, "DevUnlimitedPower", devTrait.UnlimitedPower, node.Value.Value);
									break;
								case "CosmeticReveal":
									ToggleIfNeeded(world, "DevCosmeticReveal", devTrait.CosmeticReveal, node.Value.Value);
									break;
								case "ControlAllUnits":
									ToggleIfNeeded(world, "DevControlAllUnits", devTrait.ControlAllUnits, node.Value.Value);
									break;
								case "PathDebug":
									ToggleIfNeeded(world, "DevPathDebug", devTrait.PathDebug, node.Value.Value);
									break;
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

		static void ToggleIfNeeded(World world, string order, bool currentState, string savedValue)
		{
			if (!bool.TryParse(savedValue, out var target))
				return;

			if (currentState != target)
				IssueOrder(world, order);
		}

		static void BindOrderCheckbox(CheckboxWidget checkbox, World world, string order, Func<bool> getValue)
		{
			var isChecked = new PredictedCachedTransform<bool, bool>(state => state);
			checkbox.IsChecked = () => isChecked.Update(getValue());
			checkbox.OnClick = () =>
			{
				isChecked.Predict(!getValue());
				IssueOrder(world, order);
			};
		}

		public static void IssueOrder(World world, string order)
		{
			world.IssueOrder(new Order(order, world.LocalPlayer.PlayerActor, false));
		}
	}
}
