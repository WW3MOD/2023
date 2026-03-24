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
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class DebugMenuLogic : ChromeLogic
	{
		[ObjectCreator.UseCtor]
		public DebugMenuLogic(Widget widget, World world)
		{
			var devTrait = world.LocalPlayer.PlayerActor.Trait<DeveloperMode>();
			var debugVis = world.WorldActor.TraitOrDefault<DebugVisualizations>();

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
			// 0 = Off, 1 = Me only, 2 = All players
			var fastBuildState = 0;
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
							// Off → Me: toggle on for local player
							fastBuildState = 1;
							IssueOrder(world, "DevFastBuild");
							break;
						case 1:
							// Me → All: enable for all players
							fastBuildState = 2;
							IssueOrder(world, "DevFastBuildAll");
							break;
						case 2:
							// All → Off: reset everyone
							fastBuildState = 0;
							IssueOrder(world, "DevFastBuildReset");
							break;
					}
				};

				fastBuildButton.OnRightClick = () =>
				{
					// Right-click: reset to Off from any state
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

			var terrainGeometryTrait = world.WorldActor.TraitOrDefault<TerrainGeometryOverlay>();
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
				var customTerrainDebugTrait = world.WorldActor.TraitOrDefault<CustomTerrainDebugOverlay>();
				showCustomTerrainCheckbox.Disabled = customTerrainDebugTrait == null;
				if (customTerrainDebugTrait != null)
				{
					showCustomTerrainCheckbox.IsChecked = () => customTerrainDebugTrait.Enabled;
					showCustomTerrainCheckbox.OnClick = () => customTerrainDebugTrait.Enabled ^= true;
				}
			}
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
