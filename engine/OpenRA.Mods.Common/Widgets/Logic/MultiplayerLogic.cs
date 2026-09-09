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
using OpenRA.Network;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class MultiplayerLogic : ChromeLogic
	{
		static readonly Action DoNothing = () => { };

		readonly Action onStart;
		readonly Action onExit;
		readonly ServerListLogic serverListLogic;

		[ObjectCreator.UseCtor]
		public MultiplayerLogic(Widget widget, ModData modData, Action onStart, Action onExit, ConnectionTarget directConnectEndPoint)
		{
			// MultiplayerLogic is a superset of the ServerListLogic
			// but cannot be a direct subclass because it needs to pass object-level state to the constructor
			serverListLogic = new ServerListLogic(widget, modData, Join);

			this.onStart = onStart;
			this.onExit = onExit;

			var directConnectButton = widget.Get<ButtonWidget>("DIRECTCONNECT_BUTTON");
			directConnectButton.OnClick = () =>
			{
				Ui.OpenWindow("DIRECTCONNECT_PANEL", new WidgetArgs
				{
					{ "openLobby", OpenLobby },
					{ "onExit", DoNothing },
					{ "directConnectEndPoint", null },
				});
			};

			var createServerButton = widget.Get<ButtonWidget>("CREATE_BUTTON");
			createServerButton.OnClick = () =>
			{
				Ui.OpenWindow("MULTIPLAYER_CREATESERVER_PANEL", new WidgetArgs
				{
					{ "openLobby", OpenLobby },
					{ "onExit", DoNothing }
				});
			};

			// WW3MOD: upstream asked "is any map NOT a shellmap", using not-a-shellmap as a proxy
			// for playable. That proxy is false here: this mod's shellmap system rotates the menu
			// background across the whole map pool (Game.cs:656-671, plus the ShellmapEnabled /
			// ShellmapOrder settings), so all ten shipped maps carry the Shellmap flag ON TOP OF
			// Lobby -- and every one of them failed the test, disabling Create on any real install.
			// It never reproduced in a checkout because the 319 autotest scenarios are
			// MissionSelector and satisfied it. Ask the question the button actually cares about,
			// which is what MainMenuLogic.cs:389 already asks to gate Skirmish.
			var hasMaps = modData.MapCache.Any(p => p.Visibility.HasFlag(MapVisibility.Lobby));
			createServerButton.Disabled = !hasMaps;

			widget.Get<ButtonWidget>("BACK_BUTTON").OnClick = () => { Ui.CloseWindow(); onExit(); };

			if (directConnectEndPoint != null)
			{
				// The connection window must be opened at the end of the tick for the widget hierarchy to
				// work out, but we also want to prevent the server browser from flashing visible for one tick.
				widget.Visible = false;
				Game.RunAfterTick(() =>
				{
					Ui.OpenWindow("DIRECTCONNECT_PANEL", new WidgetArgs
					{
						{ "openLobby", OpenLobby },
						{ "onExit", DoNothing },
						{ "directConnectEndPoint", directConnectEndPoint },
					});

					widget.Visible = true;
				});
			}
		}

		void OpenLobby()
		{
			// Close the multiplayer browser
			Ui.CloseWindow();

			void OnLobbyExit()
			{
				// Open a fresh copy of the multiplayer browser
				Ui.OpenWindow("MULTIPLAYER_PANEL", new WidgetArgs
				{
					{ "onStart", onStart },
					{ "onExit", onExit },
					{ "directConnectEndPoint", null },
				});

				Game.Disconnect();

				DiscordService.UpdateStatus(DiscordState.InMenu);
			}

			Game.OpenWindow("SERVER_LOBBY", new WidgetArgs
			{
				{ "onStart", onStart },
				{ "onExit", OnLobbyExit },
				{ "skirmishMode", false }
			});
		}

		void Join(GameServer server)
		{
			if (server == null || !server.IsJoinable)
				return;

			var host = server.Address.Split(':')[0];
			var port = Exts.ParseInt32Invariant(server.Address.Split(':')[1]);

			ConnectionLogic.Connect(new ConnectionTarget(host, port), "", OpenLobby, DoNothing);
		}

		bool disposed;
		protected override void Dispose(bool disposing)
		{
			if (disposing && !disposed)
			{
				disposed = true;
				serverListLogic.Dispose();
			}

			base.Dispose(disposing);
		}
	}
}
