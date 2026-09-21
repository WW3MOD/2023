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
using OpenRA.Network;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class ModInfoPanelLogic : ChromeLogic
	{
		[ObjectCreator.UseCtor]
		public ModInfoPanelLogic(Widget widget, ModData modData, Action onExit, string shellmapName)
		{
			// WW3MOD: this panel is not reachable -- MOD_INFO_PANEL is declared in info-panel.yaml and
			// opened by nothing -- but it carried the same three false lines as the live "v" dropdown,
			// so a later reader grepping for "Pre-Alpha" would have found this one and "fixed" a panel
			// no player can see. Routed through the same derivation instead of left as bait.
			widget.Get<LabelWidget>("MOD_VERSION").Text =
				ReleaseIdentity.VersionLabel(modData.Manifest.Metadata.Version, BuildFingerprint.EngineRevision);
			widget.Get<LabelWidget>("ENGINE_VERSION").Text = ReleaseIdentity.ForkLabel(Game.EngineVersion);
			widget.Get<LabelWidget>("BUILD_DATE").Text =
				ReleaseIdentity.BuildLabel(ReleaseIdentity.ResolveBuildTime(ReleaseIdentity.StampedAssembly));
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
