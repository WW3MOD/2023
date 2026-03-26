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

using OpenRA.Mods.Common.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class SpeedControlButtonLogic : ChromeLogic
	{
		static readonly float[] SpeedMultipliers = { 0.25f, 0.5f, 1f, 2f, 3f, 4f, 8f };
		static readonly string[] SpeedLabels = { "¼", "½", "1x", "2x", "3x", "4x", "8x" };
		int currentIndex = 2; // Start at 1x
		readonly int baseTimestep;

		[ObjectCreator.UseCtor]
		public SpeedControlButtonLogic(Widget widget, World world)
		{
			var button = widget as ButtonWidget;
			if (button == null)
				return;

			baseTimestep = world.Timestep;

			// Only visible when cheats are enabled
			var def = world.Map.Rules.Actors[SystemActors.Player].TraitInfo<DeveloperModeInfo>().CheckboxEnabled;
			var cheatsEnabled = world.LobbyInfo.GlobalSettings.OptionOrDefault("cheats", def);
			button.IsVisible = () => cheatsEnabled;

			button.GetText = () => SpeedLabels[currentIndex];
			button.GetTooltipText = () => $"Game Speed: {SpeedLabels[currentIndex]} (Left=faster, Right=slower)";

			// Left click = faster
			button.OnClick = () =>
			{
				if (currentIndex < SpeedMultipliers.Length - 1)
					currentIndex++;
				ApplySpeed(world);
			};

			// Right click = slower
			button.OnRightClick = () =>
			{
				if (currentIndex > 0)
					currentIndex--;
				ApplySpeed(world);
			};
		}

		void ApplySpeed(World world)
		{
			var newTimestep = System.Math.Max((int)(baseTimestep / SpeedMultipliers[currentIndex]), 1);
			world.Timestep = newTimestep;
			TextNotificationsManager.Debug($"Game speed: {SpeedLabels[currentIndex]}");
		}
	}
}
