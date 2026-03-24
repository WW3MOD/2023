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
	public class ModInfoPanelLogic : ChromeLogic
	{
		[ObjectCreator.UseCtor]
		public ModInfoPanelLogic(Widget widget, ModData modData, Action onExit, string shellmapName)
		{
			widget.Get<LabelWidget>("MOD_VERSION").Text = "Version: Pre-Alpha";
			widget.Get<LabelWidget>("ENGINE_VERSION").Text = "Fork: " + modData.Manifest.Metadata.Version;
			widget.Get<LabelWidget>("BUILD_DATE").Text = "Built: " + DateTime.Now.ToString("yyyy-MM-dd");
			widget.Get<LabelWidget>("AUTHORS").Text = "By: FreadyFish & CmdrBambi";

			var shellmapLabel = widget.Get<LabelWidget>("SHELLMAP_NAME");
			if (!string.IsNullOrEmpty(shellmapName))
				shellmapLabel.Text = "Shellmap: " + shellmapName;
			else
				shellmapLabel.Visible = false;

			widget.Get<ButtonWidget>("CLOSE_BUTTON").OnClick = () =>
			{
				Ui.CloseWindow();
				onExit();
			};
		}
	}
}
