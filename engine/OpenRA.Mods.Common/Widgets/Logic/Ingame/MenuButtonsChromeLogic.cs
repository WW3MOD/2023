#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class MenuButtonsChromeLogic : ChromeLogic
	{
		// WW3MOD: bump to re-show the how-to-play briefing to players who have already seen it.
		public const int HowToPlayVersion = 1;

		readonly World world;
		readonly Widget worldRoot;
		readonly Widget menuRoot;

		bool disableSystemButtons;
		Widget currentWidget;

		[ObjectCreator.UseCtor]
		public MenuButtonsChromeLogic(Widget widget, World world)
		{
			this.world = world;

			worldRoot = Ui.Root.Get("WORLD_ROOT");
			menuRoot = Ui.Root.Get("MENU_ROOT");

			// System buttons
			var options = widget.GetOrNull<MenuButtonWidget>("OPTIONS_BUTTON");
			if (options != null)
			{
				var blinking = false;
				var lp = world.LocalPlayer;
				options.IsDisabled = () => disableSystemButtons;
				options.OnClick = () =>
				{
					blinking = false;
					OpenMenuPanel(options, new WidgetArgs()
					{
						{ "initialPanel", IngameInfoPanel.AutoSelect }
					});
				};
				options.IsHighlighted = () => blinking && Game.LocalTick % 50 < 25;

				if (lp != null)
				{
					void StartBlinking(Player player, bool inhibitAnnouncement)
					{
						if (!inhibitAnnouncement && player == world.LocalPlayer)
							blinking = true;
					}

					var mo = lp.PlayerActor.TraitOrDefault<MissionObjectives>();

					if (mo != null)
						mo.ObjectiveAdded += StartBlinking;
				}
			}

			var debug = widget.GetOrNull<MenuButtonWidget>("DEBUG_BUTTON");
			if (debug != null)
			{
				// Can't use DeveloperMode.Enabled because there is a hardcoded hack to *always*
				// enable developer mode for singleplayer games, but we only want to show the button
				// if it has been explicitly enabled
				var def = world.Map.Rules.Actors[SystemActors.Player].TraitInfo<DeveloperModeInfo>().CheckboxEnabled;
				var enabled = world.LobbyInfo.GlobalSettings.OptionOrDefault("cheats", def);
				debug.IsVisible = () => enabled;
				debug.IsDisabled = () => disableSystemButtons;
				debug.OnClick = () => OpenMenuPanel(debug, new WidgetArgs()
				{
					{ "initialPanel", IngameInfoPanel.Debug }
				});
			}

			// WW3MOD: when launched with Test.OpenIngameInfoPanel=<panel>, open that
			// panel straight away. Lets external screenshot drivers capture the ingame
			// menu tabs without simulating mouse input.
			if (TestMode.IsActive && !string.IsNullOrEmpty(TestMode.OpenIngameInfoPanel))
			{
				if (!Enum.TryParse<IngameInfoPanel>(TestMode.OpenIngameInfoPanel, true, out var panel))
					panel = IngameInfoPanel.AutoSelect;

				var button = panel == IngameInfoPanel.Debug ? debug : options;
				// PITFALL: OpenMenuPanel calls World.CancelInputMode, which sets
				// World.OrderGenerator and so asserts unsynced (Sync.cs:208). The real
				// OnClick reaches it from input handling, which is already unsynced, but
				// RunAfterTick fires inside LogicTick's synced frame — hence RunUnsynced.
				if (button != null)
					Game.RunAfterTick(() => Sync.RunUnsynced(world, () => OpenMenuPanel(button, new WidgetArgs()
					{
						{ "initialPanel", panel }
					})));
			}
			else if (options != null && ShouldShowHowToPlay(world))
			{
				// Recorded on open rather than on dismiss: a player who alt-F4s out of the
				// briefing has still seen it, and should not meet it again every match.
				Game.Settings.Game.HowToPlayVersion = HowToPlayVersion;
				Game.Settings.Save();

				Game.RunAfterTick(() => Sync.RunUnsynced(world, () => OpenMenuPanel(options, new WidgetArgs()
				{
					{ "initialPanel", IngameInfoPanel.HowToPlay }
				})));
			}
		}

		// WW3MOD: singleplayer only, because that is the only case where OpenMenuPanel
		// actually pauses — auto-opening a world-hiding panel over a live multiplayer
		// match would be hostile. In multiplayer the tab is still there to be opened.
		// The TestMode exclusion is load-bearing: an autotest is a one-client match on a
		// machine whose settings may never have shown the briefing, so without it every
		// scenario would open this panel, pause the world and hide the UI.
		static bool ShouldShowHowToPlay(World world)
		{
			return !TestMode.IsActive
				&& Game.Settings.Game.HowToPlayVersion < HowToPlayVersion
				&& world.LocalPlayer != null
				&& !world.IsReplay
				&& !world.IsLoadingGameSave
				&& world.LobbyInfo.NonBotClients.Count() == 1;
		}

		void OpenMenuPanel(MenuButtonWidget button, WidgetArgs widgetArgs = null)
		{
			disableSystemButtons = true;
			var cachedPause = world.PredictedPaused;

			if (button.HideIngameUI)
			{
				// Cancel custom input modes (guard, building placement, etc)
				world.CancelInputMode();

				worldRoot.IsVisible = () => false;
			}

			if (button.Pause && world.LobbyInfo.NonBotClients.Count() == 1)
				world.SetPauseState(true);

			var cachedDisableWorldSounds = Game.Sound.DisableWorldSounds;
			if (button.DisableWorldSounds)
				Game.Sound.DisableWorldSounds = true;

			widgetArgs ??= new WidgetArgs();
			widgetArgs.Add("onExit", () =>
			{
				if (button.HideIngameUI)
					worldRoot.IsVisible = () => true;

				if (button.DisableWorldSounds)
					Game.Sound.DisableWorldSounds = cachedDisableWorldSounds;

				if (button.Pause && world.LobbyInfo.NonBotClients.Count() == 1)
					world.SetPauseState(cachedPause);

				menuRoot.RemoveChild(currentWidget);
				disableSystemButtons = false;
			});

			currentWidget = Game.LoadWidget(world, button.MenuContainer, menuRoot, widgetArgs);
			Game.RunAfterTick(Ui.ResetTooltips);
		}
	}
}
