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
	public class LoadPresetButtonLogic : ChromeLogic
	{
		[ObjectCreator.UseCtor]
		public LoadPresetButtonLogic(Widget widget, World world)
		{
			var button = widget as ButtonWidget;
			if (button == null)
				return;

			// Only visible when cheats are enabled
			var def = world.Map.Rules.Actors[SystemActors.Player].TraitInfo<DeveloperModeInfo>().CheckboxEnabled;
			var cheatsEnabled = world.LobbyInfo.GlobalSettings.OptionOrDefault("cheats", def);
			button.IsVisible = () => cheatsEnabled;

			button.OnClick = () => DebugMenuLogic.QuickLoadPreset(world);
		}
	}
}
