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
using System.Reflection;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets
{
	public class BarToggleLogic : ChromeLogic
	{
		[ObjectCreator.UseCtor]
		public BarToggleLogic(Widget widget, World world, Dictionary<string, MiniYaml> logicArgs)
		{
			var settingsField = logicArgs["SettingsField"].Value;
			var targetBar = logicArgs["TargetBar"].Value;

			var field = typeof(GameSettings).GetField(settingsField);
			if (field == null)
				throw new InvalidOperationException($"BarToggleLogic: GameSettings has no field '{settingsField}'");

			var barWidget = widget.Parent.GetOrNull(targetBar);

			var toggleButton = widget as ButtonWidget;
			if (toggleButton != null)
			{
				toggleButton.IsHighlighted = () => (bool)field.GetValue(Game.Settings.Game);
				toggleButton.OnClick = () =>
				{
					var current = (bool)field.GetValue(Game.Settings.Game);
					field.SetValue(Game.Settings.Game, !current);
					Game.Settings.Save();
				};
			}

			if (barWidget != null)
				barWidget.IsVisible = () => (bool)field.GetValue(Game.Settings.Game);
		}
	}
}
