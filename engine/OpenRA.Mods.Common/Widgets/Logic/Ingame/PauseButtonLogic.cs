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

using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class PauseButtonLogic : ChromeLogic
	{
		static readonly Color PausedColor = Color.FromArgb(255, 200, 80);

		[ObjectCreator.UseCtor]
		public PauseButtonLogic(Widget widget, World world)
		{
			var button = widget as ButtonWidget;
			if (button == null)
				return;

			button.GetText = () => "[]";
			button.GetTooltipText = () => world.Paused ? "Resume Game" : "Pause Game";
			button.IsHighlighted = () => world.Paused;
			button.GetColor = () => world.Paused ? PausedColor : button.TextColor;

			button.OnClick = () =>
			{
				world.SetPauseState(!world.Paused);
			};
		}
	}
}
