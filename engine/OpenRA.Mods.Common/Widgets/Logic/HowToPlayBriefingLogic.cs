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
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	// WW3MOD: main-menu window around the same HOWTOPLAY_PANEL widget the ingame info menu
	// shows as a tab, so the briefing copy has exactly one source. The panel is a plain
	// container of labels with no logic of its own, hence Ui.LoadWidget rather than
	// Game.LoadWidget — no world is needed and none is available here before a match.
	public class HowToPlayBriefingLogic : ChromeLogic
	{
		[ObjectCreator.UseCtor]
		public HowToPlayBriefingLogic(Widget widget, Action onExit)
		{
			Ui.LoadWidget("HOWTOPLAY_PANEL", widget.Get("BRIEFING_CONTENT"), new WidgetArgs());

			widget.Get<ButtonWidget>("BACK_BUTTON").OnClick = () =>
			{
				Ui.CloseWindow();
				onExit();
			};
		}
	}
}
