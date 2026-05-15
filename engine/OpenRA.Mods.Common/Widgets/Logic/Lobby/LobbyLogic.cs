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
using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Network;
using OpenRA.Primitives;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class LobbyLogic : ChromeLogic, INotificationHandler<TextNotification>
	{
		[FluentReference]
		const string Add = "options-slot-admin.add-bots";

		[FluentReference]
		const string Remove = "options-slot-admin.remove-bots";

		[FluentReference]
		const string ConfigureBots = "options-slot-admin.configure-bots";

		[FluentReference("count")]
		const string NumberTeams = "options-slot-admin.teams-count";

		[FluentReference]
		const string HumanVsBots = "options-slot-admin.humans-vs-bots";

		[FluentReference]
		const string FreeForAll = "options-slot-admin.free-for-all";

		[FluentReference]
		const string ConfigureTeams = "options-slot-admin.configure-teams";

		[FluentReference]
		const string Back = "button-back";

		[FluentReference]
		const string TeamChat = "button-team-chat";

		[FluentReference]
		const string GeneralChat = "button-general-chat";

		[FluentReference("seconds")]
		const string ChatAvailability = "label-chat-availability";

		[FluentReference]
		const string ChatDisabled = "label-chat-disabled";

		static readonly Action DoNothing = () => { };

		readonly ModData modData;
		readonly Action onStart;
		readonly Action onExit;
		readonly OrderManager orderManager;
		readonly WorldRenderer worldRenderer;
		readonly bool skirmishMode;
		readonly Ruleset modRules;
		readonly WebServices services;

		enum PanelType { Players, Options, Music, Servers, Kick, ForceStart }

		// Static hook so other lobby logic classes (e.g. chip click handlers) can request
		// a top-level panel switch by string name without holding a LobbyLogic reference.
		// Accepts "Players" or "Options" — other panel types aren't exposed here.
		public static Action<string> SwitchPanel;
		PanelType panel = PanelType.Players;

		// Test-mode pending tab switch: filled in by the constructor when
		// Test.OpenLobbyTab is set, applied by Tick() once MapIsPlayable so the
		// OptionsTabDisabled guard doesn't immediately snap it back.
		PanelType? pendingTestTab;

		// Latches once the Test.LobbyReadyFile marker has been written so the
		// Tick handler doesn't keep retouching the file every tick.
		bool lobbyReadyMarkerWritten;

		readonly Widget lobby;
		readonly Widget editablePlayerTemplate;
		readonly Widget nonEditablePlayerTemplate;
		readonly Widget emptySlotTemplate;
		readonly Widget editableSpectatorTemplate;
		readonly Widget nonEditableSpectatorTemplate;
		// Phase 10: TEMPLATE_NEW_SPECTATOR removed — SPECTATE_AREA widget now lives
		// outside the roster scroll panel, wired up once at startup. Field kept off
		// the class on purpose; if you need a spectator row template again, restore it.

		readonly ScrollPanelWidget lobbyChatPanel;
		readonly Dictionary<TextNotificationPool, Widget> chatTemplates = new();
		readonly TextFieldWidget chatTextField;
		readonly CachedTransform<int, string> chatAvailableIn;
		readonly string chatDisabled;

		readonly ScrollPanelWidget players;

		// Roster auto-expand state — captured once at lobby load so we can recompute
		// y-offsets of widgets below the player list each time the slot list rebuilds.
		Widget rosterFlexSetupRow;
		Widget rosterFlexActiveChanges;
		Widget rosterFlexPresetBar;
		Widget rosterFlexCommonHeader;
		ScrollPanelWidget rosterFlexCommonPanel;
		ScrollPanelWidget rosterFlexOuterScroll;
		int rosterFlexOriginalPlayersHeight;
		int rosterFlexOriginalSetupY;
		int rosterFlexOriginalActiveChangesY;
		int rosterFlexOriginalPresetY;
		int rosterFlexOriginalCommonHeaderY;
		int rosterFlexOriginalCommonPanelY;

		readonly Dictionary<string, LobbyFaction> factions = new();

		readonly IColorPickerManagerInfo colorManager;

		readonly TabCompletionLogic tabCompletion = new();

		MapPreview map;
		Session.MapStatus mapStatus;

		bool chatEnabled;
		bool disableTeamChat;
		bool insufficientPlayerSpawns;
		bool teamChat;
		bool updateDiscordStatus = true;
		bool resetOptionsButtonEnabled;
		Dictionary<int, SpawnOccupant> spawnOccupants = new();

		readonly string chatLineSound;
		readonly string playerJoinedSound;
		readonly string playerLeftSound;
		readonly string lobbyOptionChangedSound;

		bool MapIsPlayable => (mapStatus & Session.MapStatus.Playable) == Session.MapStatus.Playable;

		// Listen for connection failures
		void ConnectionStateChanged(OrderManager om, string password, NetworkConnection connection)
		{
			if (connection.ConnectionState == ConnectionState.NotConnected)
			{
				// Show connection failed dialog
				Ui.CloseWindow();

				void OnConnect()
				{
					Game.OpenWindow("SERVER_LOBBY", new WidgetArgs()
					{
						{ "onExit", onExit },
						{ "onStart", onStart },
						{ "skirmishMode", false }
					});
				}

				Action<string> onRetry = pass => ConnectionLogic.Connect(connection.Target, pass, OnConnect, onExit);

				var switchPanel = CurrentServerSettings.ServerExternalMod != null ? "CONNECTION_SWITCHMOD_PANEL" : "CONNECTIONFAILED_PANEL";
				Ui.OpenWindow(switchPanel, new WidgetArgs()
				{
					{ "orderManager", om },
					{ "connection", connection },
					{ "password", password },
					{ "onAbort", onExit },
					{ "onQuit", null },
					{ "onRetry", onRetry }
				});
			}
		}

		[ObjectCreator.UseCtor]
		internal LobbyLogic(Widget widget, ModData modData, WorldRenderer worldRenderer, OrderManager orderManager,
			Action onExit, Action onStart, bool skirmishMode, Dictionary<string, MiniYaml> logicArgs)
		{
			map = MapCache.UnknownMap;
			lobby = widget;
			this.modData = modData;
			this.orderManager = orderManager;
			this.worldRenderer = worldRenderer;
			this.onStart = onStart;
			this.onExit = onExit;
			this.skirmishMode = skirmishMode;

			// TODO: This needs to be reworked to support per-map tech levels, bots, etc.
			modRules = modData.DefaultRules;

			services = modData.Manifest.Get<WebServices>();

			Game.LobbyInfoChanged += UpdateCurrentMap;
			Game.LobbyInfoChanged += UpdatePlayerList;
			Game.LobbyInfoChanged += UpdateDiscordStatus;
			Game.LobbyInfoChanged += UpdateSpawnOccupants;
			Game.LobbyInfoChanged += UpdateOptions;
			Game.BeforeGameStart += OnGameStart;
			Game.ConnectionStateChanged += ConnectionStateChanged;

			ChromeMetrics.TryGet("ChatLineSound", out chatLineSound);
			ChromeMetrics.TryGet("PlayerJoinedSound", out playerJoinedSound);
			ChromeMetrics.TryGet("PlayerLeftSound", out playerLeftSound);
			ChromeMetrics.TryGet("LobbyOptionChangedSound", out lobbyOptionChangedSound);

			var name = lobby.GetOrNull<LabelWidget>("SERVER_NAME");
			if (name != null)
				name.GetText = () => orderManager.LobbyInfo.GlobalSettings.ServerName;

			var mapContainer = Ui.LoadWidget("MAP_PREVIEW", lobby.Get("MAP_PREVIEW_ROOT"), new WidgetArgs
			{
				{ "orderManager", orderManager },
				{ "getMap", (Func<(MapPreview, Session.MapStatus)>)(() => (map, mapStatus)) },
				{
					"onMouseDown", (Action<MapPreviewWidget, MapPreview, MouseInput>)((preview, mapPreview, mi) =>
						LobbyUtils.SelectSpawnPoint(orderManager, preview, mapPreview, mi))
				},
				{ "getSpawnOccupants", (Func<Dictionary<int, SpawnOccupant>>)(() => spawnOccupants) },
				{ "getDisabledSpawnPoints", (Func<HashSet<int>>)(() => orderManager.LobbyInfo.DisabledSpawnPoints) },
				{ "showUnoccupiedSpawnpoints", true },
				{ "mapUpdatesEnabled", true },
				{
					"onMapUpdate", (Action<string>)(uid =>
					{
						orderManager.IssueOrder(Order.Command("map " + uid));
						Game.Settings.Server.Map = uid;
						Game.Settings.Save();
					})
				},
			});

			mapContainer.IsVisible = () => panel != PanelType.Servers;

			UpdateCurrentMap();

			// WW3MOD: pass option-related args through so the Players panel can embed a
			// "Common Options" sub-panel that runs LobbyOptionsLogic alongside the player list.
			// Set up configurationDisabled lazily — the actual delegate is constructed a few
			// lines down. We pass a stable wrapper that resolves to it once initialised.
			Func<bool> configurationDisabledRef = null;
			var playerBin = Ui.LoadWidget("LOBBY_PLAYER_BIN", lobby.Get("TOP_PANELS_ROOT"), new WidgetArgs()
			{
				{ "orderManager", orderManager },
				{ "getMap", (Func<MapPreview>)(() => map) },
				{ "configurationDisabled", (Func<bool>)(() => configurationDisabledRef != null && configurationDisabledRef()) }
			});
			// Single-panel lobby: left column (map/music + players + chat) and right
			// column (all options + active changes + preset) are always visible.
			playerBin.IsVisible = () => panel != PanelType.Servers;

			players = playerBin.Get<ScrollPanelWidget>("LOBBY_PLAYERS");
			editablePlayerTemplate = players.Get("TEMPLATE_EDITABLE_PLAYER");
			nonEditablePlayerTemplate = players.Get("TEMPLATE_NONEDITABLE_PLAYER");
			emptySlotTemplate = players.Get("TEMPLATE_EMPTY");
			editableSpectatorTemplate = players.Get("TEMPLATE_EDITABLE_SPECTATOR");
			nonEditableSpectatorTemplate = players.Get("TEMPLATE_NONEDITABLE_SPECTATOR");
			// Phase 10: SPECTATE_AREA — pulled out of the roster scroll panel and
			// pinned to the bottom-right of the players cell. Wired up once at
			// startup; visibility tracks whether the local client occupies a slot.
			var spectateArea = playerBin.GetOrNull("SPECTATE_AREA");
			if (spectateArea != null)
			{
				LobbyUtils.SetupKickSpectatorsWidget(spectateArea, orderManager, lobby,
					() => panel = PanelType.Kick, () => panel = PanelType.Players, skirmishMode);

				// PITFALL: orderManager.LocalClient is null during the early lobby tick
				// (before the client registers, and again during disconnect teardown).
				// Every lambda below runs every tick, so it MUST null-check first.
				var spectateBtn = spectateArea.Get<ButtonWidget>("SPECTATE");
				spectateBtn.OnClick = () => orderManager.IssueOrder(Order.Command("spectate"));
				spectateBtn.IsDisabled = () => orderManager.LocalClient == null || orderManager.LocalClient.IsReady;
				spectateBtn.IsVisible = () =>
					orderManager.LocalClient != null &&
					orderManager.LocalClient.Slot != null &&
					(orderManager.LobbyInfo.GlobalSettings.AllowSpectators || orderManager.LocalClient.IsAdmin);

				spectateArea.IsVisible = () => orderManager.LocalClient != null && orderManager.LocalClient.Slot != null;
			}

			// PITFALL: roster auto-expand depends on these widgets staying as direct
			// children of LOBBY_PLAYER_BIN with the IDs below. If the layout YAML is
			// refactored, the GetOrNull lookups will silently noop and the roster
			// will visually overlap the widgets below it.
			rosterFlexSetupRow = playerBin.GetOrNull("LOBBY_SETUP_ROW");
			rosterFlexActiveChanges = playerBin.GetOrNull("LOBBY_ACTIVE_CHANGES");
			rosterFlexPresetBar = playerBin.GetOrNull("LOBBY_PRESET_BAR");
			rosterFlexCommonHeader = playerBin.GetOrNull("COMMON_OPTIONS_HEADER");
			rosterFlexCommonPanel = playerBin.GetOrNull<ScrollPanelWidget>("COMMON_OPTIONS_PANEL");
			// LOBBY_PLAYER_BIN itself is now a ScrollPanel (unified left-column scroll).
			// We update its ContentHeight after every roster resize so the outer scroll
			// engages when total content exceeds the visible panel height.
			rosterFlexOuterScroll = playerBin as ScrollPanelWidget;
			rosterFlexOriginalPlayersHeight = players.Bounds.Height;
			rosterFlexOriginalSetupY = rosterFlexSetupRow?.Bounds.Y ?? 0;
			rosterFlexOriginalActiveChangesY = rosterFlexActiveChanges?.Bounds.Y ?? 0;
			rosterFlexOriginalPresetY = rosterFlexPresetBar?.Bounds.Y ?? 0;
			rosterFlexOriginalCommonHeaderY = rosterFlexCommonHeader?.Bounds.Y ?? 0;
			rosterFlexOriginalCommonPanelY = rosterFlexCommonPanel?.Bounds.Y ?? 0;

			colorManager = modRules.Actors[SystemActors.World].TraitInfo<IColorPickerManagerInfo>();

			foreach (var f in modRules.Actors[SystemActors.World].TraitInfos<FactionInfo>())
				factions.Add(f.InternalName, new LobbyFaction { Selectable = f.Selectable, Name = f.Name, Side = f.Side, Description = f.Description });

			var gameStarting = false;
			Func<bool> configurationDisabled = () => !Game.IsHost || gameStarting ||
				panel == PanelType.Kick || panel == PanelType.ForceStart || !MapIsPlayable ||
				orderManager.LocalClient == null || orderManager.LocalClient.IsReady;
			configurationDisabledRef = configurationDisabled;

			SwitchPanel = name =>
			{
				panel = name == "Options" ? PanelType.Options : PanelType.Players;
			};

			var mapButton = lobby.GetOrNull<ButtonWidget>("CHANGEMAP_BUTTON");
			if (mapButton != null)
			{
				mapButton.IsVisible = () => panel != PanelType.Servers;
				mapButton.IsDisabled = () => gameStarting || panel == PanelType.Kick || panel == PanelType.ForceStart ||
					orderManager.LocalClient == null || orderManager.LocalClient.IsReady;
				mapButton.OnClick = () =>
				{
					var onSelect = new Action<string>(uid =>
					{
						// Don't select the same map again, and handle map becoming unavailable
						var status = modData.MapCache[uid].Status;
						if (uid == map.Uid || (status != MapStatus.Available && status != MapStatus.DownloadAvailable))
							return;

						orderManager.IssueOrder(Order.Command("map " + uid));
						Game.Settings.Server.Map = uid;
						Game.Settings.Save();
					});

					// Check for updated maps, if the user has edited a map we'll preselect it for them
					modData.MapCache.UpdateMaps();

					Ui.OpenWindow("MAPCHOOSER_PANEL", new WidgetArgs()
					{
						{ "initialMap", modData.MapCache.PickLastModifiedMap(MapVisibility.Lobby) ?? map.Uid },
						{ "remoteMapPool", orderManager.ServerMapPool },
						{ "initialTab", MapClassification.System },
						{ "onExit", modData.MapCache.UpdateMaps },
						{ "onSelect", Game.IsHost ? onSelect : null },
						{ "filter", MapVisibility.Lobby },
						{ "initialCategory", (string)null },
					});
				};
			}

			var scenarioDropdown = lobby.GetOrNull<DropDownButtonWidget>("SCENARIO_DROPDOWNBUTTON");
			if (scenarioDropdown != null)
			{
				scenarioDropdown.IsVisible = () =>
				{
					if (panel == PanelType.Servers)
						return false;

					var scenarios = map.ScenarioNames;
					return scenarios != null && scenarios.Length > 0;
				};

				scenarioDropdown.IsDisabled = () => configurationDisabled() || gameStarting ||
					panel == PanelType.Kick || panel == PanelType.ForceStart;

				scenarioDropdown.GetText = () =>
				{
					var current = orderManager.LobbyInfo.GlobalSettings.OptionOrDefault("scenario", "none");
					return current == "none" ? "No Scenario" : current;
				};

				scenarioDropdown.OnMouseDown = _ =>
				{
					var scenarios = map.ScenarioNames;
					if (scenarios == null || scenarios.Length == 0)
						return;

					var values = new Dictionary<string, string> { { "none", "No Scenario" } };
					foreach (var name in scenarios)
						values[name] = name;

					ScrollItemWidget SetupItem(KeyValuePair<string, string> c, ScrollItemWidget template)
					{
						bool IsSelected() => orderManager.LobbyInfo.GlobalSettings.OptionOrDefault("scenario", "none") == c.Key;
						void OnClick() => orderManager.IssueOrder(Order.Command($"option scenario {c.Key}"));

						var item = ScrollItemWidget.Setup(template, IsSelected, OnClick);
						item.Get<LabelWidget>("LABEL").GetText = () => c.Value;
						return item;
					}

					scenarioDropdown.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", values.Count * 30, values, SetupItem);
				};
			}

			// WW3MOD: short blurb under the scenario dropdown describing what's selected.
			// Uses the map's first Category as a fallback when no scenario is picked.
			var scenarioDescription = lobby.GetOrNull<LabelWidget>("SCENARIO_DESCRIPTION");
			if (scenarioDescription != null)
			{
				scenarioDescription.IsVisible = () => panel != PanelType.Servers;
				scenarioDescription.GetText = () =>
				{
					var current = orderManager.LobbyInfo.GlobalSettings.OptionOrDefault("scenario", "none");
					if (current != null && current != "none")
						return "Scenario: " + current;

					var category = map?.Categories?.FirstOrDefault();
					return string.IsNullOrEmpty(category) ? "Standard skirmish" : category + " skirmish";
				};
			}

			var slotsButton = lobby.GetOrNull<DropDownButtonWidget>("SLOTS_DROPDOWNBUTTON");
			if (slotsButton != null)
			{
				// WW3MOD: hidden — replaced by the inline LOBBY_SETUP_ROW (Add Bots / Remove Bots /
				// Auto-Team buttons) on the Players panel. The remaining wiring below is left intact
				// so the dropdown still works if a future override re-enables it.
				slotsButton.IsVisible = () => false;
				slotsButton.IsDisabled = () => configurationDisabled() || panel != PanelType.Players ||
					(orderManager.LobbyInfo.Slots.Values.All(s => !s.AllowBots) &&
					!orderManager.LobbyInfo.Slots.Any(s => !s.Value.LockTeam && orderManager.LobbyInfo.ClientInSlot(s.Key) != null));

				slotsButton.OnMouseDown = _ =>
				{
					var botTypes = map.PlayerActorInfo.TraitInfos<IBotInfo>().Select(t => t.Type);
					var options = new Dictionary<string, IEnumerable<DropDownOption>>();

					var botController = orderManager.LobbyInfo.Clients.FirstOrDefault(c => c.IsAdmin);
					if (orderManager.LobbyInfo.Slots.Values.Any(s => s.AllowBots))
					{
						var botOptions = new List<DropDownOption>()
						{
							new()
							{
								Title = FluentProvider.GetMessage(Add),
								IsSelected = () => false,
								OnClick = () =>
								{
									foreach (var slot in orderManager.LobbyInfo.Slots)
									{
										var bot = botTypes.Random(Game.CosmeticRandom);
										var c = orderManager.LobbyInfo.ClientInSlot(slot.Key);
										if (slot.Value.AllowBots && (c == null || c.Bot != null))
											orderManager.IssueOrder(Order.Command($"slot_bot {slot.Key} {botController.Index} {bot}"));
									}
								}
							}
						};

						if (orderManager.LobbyInfo.Clients.Any(c => c.Bot != null))
						{
							botOptions.Add(new DropDownOption()
							{
								Title = FluentProvider.GetMessage(Remove),
								IsSelected = () => false,
								OnClick = () =>
								{
									foreach (var slot in orderManager.LobbyInfo.Slots)
									{
										var c = orderManager.LobbyInfo.ClientInSlot(slot.Key);
										if (c != null && c.Bot != null)
											orderManager.IssueOrder(Order.Command("slot_open " + slot.Value.PlayerReference));
									}
								}
							});
						}

						options.Add(FluentProvider.GetMessage(ConfigureBots), botOptions);
					}

					var teamCount = (orderManager.LobbyInfo.Slots.Count(s => !s.Value.LockTeam && orderManager.LobbyInfo.ClientInSlot(s.Key) != null) + 1) / 2;
					if (teamCount >= 1)
					{
						var teamOptions = Enumerable.Range(2, teamCount - 1).Reverse().Select(d => new DropDownOption
						{
							Title = FluentProvider.GetMessage(NumberTeams, "count", d),
							IsSelected = () => false,
							OnClick = () => orderManager.IssueOrder(Order.Command($"assignteams {d}"))
						}).ToList();

						if (orderManager.LobbyInfo.Slots.Any(s => s.Value.AllowBots))
						{
							teamOptions.Add(new DropDownOption
							{
								Title = FluentProvider.GetMessage(HumanVsBots),
								IsSelected = () => false,
								OnClick = () => orderManager.IssueOrder(Order.Command("assignteams 1"))
							});
						}

						teamOptions.Add(new DropDownOption
						{
							Title = FluentProvider.GetMessage(FreeForAll),
							IsSelected = () => false,
							OnClick = () => orderManager.IssueOrder(Order.Command("assignteams 0"))
						});

						options.Add(FluentProvider.GetMessage(ConfigureTeams), teamOptions);
					}

					ScrollItemWidget SetupItem(DropDownOption option, ScrollItemWidget template)
					{
						var item = ScrollItemWidget.Setup(template, option.IsSelected, option.OnClick);
						item.Get<LabelWidget>("LABEL").GetText = () => option.Title;
						return item;
					}

					slotsButton.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 175, options, SetupItem);
				};
			}

			var resetOptionsButton = lobby.GetOrNull<ButtonWidget>("RESET_OPTIONS_BUTTON");
			if (resetOptionsButton != null)
			{
				resetOptionsButton.IsVisible = () => false; // Reset lives in the preset bar now.
				resetOptionsButton.IsDisabled = () => configurationDisabled() || !resetOptionsButtonEnabled;
				resetOptionsButton.OnMouseDown = _ => orderManager.IssueOrder(Order.Command("reset_options"));
			}

			// Options live inline in the right column (RIGHT_COLUMN_MATCH's
			// COMMON_OPTIONS_PANEL runs LobbyOptionsLogic with category=All — Common
			// then Advanced sections in one scroll). No separate options bin.

			// Music panel overlays the map preview when the user clicks the Music
			// view button. Loaded into a left-column root, not TOP_PANELS_ROOT.
			var musicPanelRoot = lobby.GetOrNull("MUSIC_PANEL_ROOT");
			if (musicPanelRoot != null)
			{
				var musicBin = Ui.LoadWidget("LOBBY_MUSIC_BIN", musicPanelRoot, new WidgetArgs
				{
					{ "onExit", DoNothing },
					{ "world", worldRenderer.World }
				});

				// The BIN's own "Music" label is redundant with the Map/Music toggle above it.
				var musicLabel = musicBin.GetOrNull<LabelWidget>("MUSIC");
				if (musicLabel != null)
					musicLabel.IsVisible = () => false;
			}

			ServerListLogic serverListLogic = null;
			if (!skirmishMode)
			{
				Action<GameServer> doNothingWithServer = _ => { };

				var serversBin = Ui.LoadWidget("LOBBY_SERVERS_BIN", lobby.Get("TOP_PANELS_ROOT"), new WidgetArgs
				{
					{ "onJoin", doNothingWithServer },
				});

				serverListLogic = serversBin.LogicObjects.Select(l => l as ServerListLogic).FirstOrDefault(l => l != null);
				serversBin.IsVisible = () => panel == PanelType.Servers;
			}

			// Old global tab strip (Match / Advanced / Music / Servers) is gone — all
			// options live inline on the right, music has a local Map/Music toggle in
			// the top-left. The tab containers stay in the YAML but never become
			// visible. Servers panel is still reachable for multiplayer mode below.
			var tabContainer = skirmishMode ? lobby.GetOrNull("SKIRMISH_TABS") : lobby.GetOrNull("MULTIPLAYER_TABS");
			if (tabContainer != null)
				tabContainer.IsVisible = () => false;

			// Map/Music local toggle for the top-left panel.
			var mapPreviewRoot = lobby.GetOrNull("MAP_PREVIEW_ROOT");
			// Default to map view; Test.OpenLobbyTab=Music drives the toggle for screenshot tests.
			var showMusic = TestMode.IsActive && string.Equals(TestMode.OpenLobbyTab, "music", StringComparison.OrdinalIgnoreCase);
			if (mapPreviewRoot != null)
				mapPreviewRoot.IsVisible = () => !showMusic;
			if (musicPanelRoot != null)
				musicPanelRoot.IsVisible = () => showMusic;

			var mapMusicToggle = lobby.GetOrNull("MAP_MUSIC_TOGGLE");
			if (mapMusicToggle != null)
			{
				var mapViewButton = mapMusicToggle.GetOrNull<ButtonWidget>("MAP_VIEW_BUTTON");
				if (mapViewButton != null)
				{
					mapViewButton.IsHighlighted = () => !showMusic;
					mapViewButton.OnClick = () => showMusic = false;
				}
				var musicViewButton = mapMusicToggle.GetOrNull<ButtonWidget>("MUSIC_VIEW_BUTTON");
				if (musicViewButton != null)
				{
					musicViewButton.IsHighlighted = () => showMusic;
					musicViewButton.OnClick = () => showMusic = true;
				}
				var mapIndicator = mapMusicToggle.GetOrNull("MAP_VIEW_INDICATOR");
				if (mapIndicator != null)
					mapIndicator.IsVisible = () => !showMusic;
				var musicIndicator = mapMusicToggle.GetOrNull("MUSIC_VIEW_INDICATOR");
				if (musicIndicator != null)
					musicIndicator.IsVisible = () => showMusic;
			}

			// WW3MOD: allow external test drivers to land on a specific tab on
			// lobby open. PITFALL: setting panel here gets reverted by the Tick()
			// guard (OptionsTabDisabled snaps panel back to Players while
			// MapIsPlayable is false during initial load). Stash the request and
			// apply it inside Tick once the map is validated.
			if (TestMode.IsActive && !string.IsNullOrEmpty(TestMode.OpenLobbyTab))
			{
				switch (TestMode.OpenLobbyTab.ToLowerInvariant())
				{
					case "advanced": case "options": pendingTestTab = PanelType.Options; break;
					case "music": pendingTestTab = PanelType.Music; break;
					case "match": case "players": pendingTestTab = PanelType.Players; break;
				}
			}

			// Force start panel
			void StartGame()
			{
				// Refresh MapCache and check if the selected map is available before attempting to start the game
				if (modData.MapCache[map.Uid].Status == MapStatus.Available)
				{
					gameStarting = true;
					orderManager.IssueOrder(Order.Command("startgame"));
				}
				else
					modData.MapCache.UpdateMaps();
			}

			bool StartDisabled() => map.Status != MapStatus.Available ||
				orderManager.LobbyInfo.Slots.Any(sl => sl.Value.Required && orderManager.LobbyInfo.ClientInSlot(sl.Key) == null) ||
				orderManager.LobbyInfo.Slots.All(sl => orderManager.LobbyInfo.ClientInSlot(sl.Key) == null) ||
				(!orderManager.LobbyInfo.GlobalSettings.EnableSingleplayer && orderManager.LobbyInfo.NonBotPlayers.Count() < 2) ||
				insufficientPlayerSpawns;

			var startGameButton = lobby.GetOrNull<ButtonWidget>("START_GAME_BUTTON");
			if (startGameButton != null)
			{
				startGameButton.IsDisabled = () => configurationDisabled() || StartDisabled();

				startGameButton.OnClick = () =>
				{
					// WW3MOD: snapshot the current lobby state as the "Last game" preset
					// so it can be one-click-restored from the preset dropdown next time.
					LobbyPresetLogic.SnapshotLastGame?.Invoke();

					// Bots and admins don't count
					if (orderManager.LobbyInfo.Clients.Any(c => c.Slot != null && !c.IsAdmin && c.Bot == null && !c.IsReady))
						panel = PanelType.ForceStart;
					else
						StartGame();
				};
			}

			var forceStartBin = Ui.LoadWidget("FORCE_START_DIALOG", lobby.Get("TOP_PANELS_ROOT"), new WidgetArgs());
			forceStartBin.IsVisible = () => panel == PanelType.ForceStart;
			forceStartBin.Get("KICK_WARNING").IsVisible = () => orderManager.LobbyInfo.Clients.Any(c => c.IsInvalid);
			var forceStartButton = forceStartBin.Get<ButtonWidget>("OK_BUTTON");
			forceStartButton.OnClick = () =>
			{
				LobbyPresetLogic.SnapshotLastGame?.Invoke();
				StartGame();
			};
			forceStartButton.IsDisabled = StartDisabled;

			forceStartBin.Get<ButtonWidget>("CANCEL_BUTTON").OnClick = () => panel = PanelType.Players;

			var disconnectButton = lobby.Get<ButtonWidget>("DISCONNECT_BUTTON");
			disconnectButton.OnClick = () =>
			{
				Ui.CloseWindow();
				onExit();
				Game.Sound.PlayNotification(modRules, null, "Sounds", playerLeftSound, null);
			};

			if (skirmishMode)
			{
				var disconnectButtonText = FluentProvider.GetMessage(Back);
				disconnectButton.GetText = () => disconnectButtonText;
			}

			if (logicArgs.TryGetValue("ChatTemplates", out var templateIds))
			{
				foreach (var item in templateIds.Nodes)
				{
					var key = FieldLoader.GetValue<TextNotificationPool>("key", item.Key);
					chatTemplates[key] = Ui.LoadWidget(item.Value.Value, null, new WidgetArgs());
				}
			}

			var chatMode = lobby.Get<ButtonWidget>("CHAT_MODE");
			var team = FluentProvider.GetMessage(TeamChat);
			var all = FluentProvider.GetMessage(GeneralChat);
			chatMode.GetText = () => teamChat ? team : all;
			chatMode.OnClick = () => teamChat ^= true;
			chatMode.IsDisabled = () => disableTeamChat || !chatEnabled;

			chatTextField = lobby.Get<TextFieldWidget>("CHAT_TEXTFIELD");
			chatTextField.IsDisabled = () => !chatEnabled;
			chatTextField.MaxLength = UnitOrders.ChatMessageMaxLength;

			// In skirmish there's only one chat channel (you + bots), so the All/Team
			// toggle is meaningless visual noise. Hide it and reclaim the 55px the
			// textfield was offset by.
			if (skirmishMode)
			{
				chatMode.Visible = false;
				chatTextField.Bounds.X = 0;
				chatTextField.Bounds.Width = chatTextField.Parent.Bounds.Width;
			}

			chatTextField.OnEnterKey = _ =>
			{
				if (chatTextField.Text.Length == 0)
					return true;

				// Always scroll to bottom when we've typed something
				lobbyChatPanel.ScrollToBottom();

				var teamNumber = 0U;
				if (teamChat && orderManager.LocalClient != null)
					teamNumber = orderManager.LocalClient.IsObserver ? uint.MaxValue : (uint)orderManager.LocalClient.Team;

				orderManager.IssueOrder(Order.Chat(chatTextField.Text, teamNumber));
				chatTextField.Text = "";
				return true;
			};

			chatTextField.OnTabKey = e =>
			{
				if (!chatMode.Key.IsActivatedBy(e) || chatMode.IsDisabled())
				{
					chatTextField.Text = tabCompletion.Complete(chatTextField.Text);
					chatTextField.CursorPosition = chatTextField.Text.Length;
				}
				else
					chatMode.OnKeyPress(e);

				return true;
			};

			chatTextField.OnEscKey = _ => chatTextField.YieldKeyboardFocus();

			chatAvailableIn = new CachedTransform<int, string>(x => FluentProvider.GetMessage(ChatAvailability, "seconds", x));
			chatDisabled = FluentProvider.GetMessage(ChatDisabled);

			lobbyChatPanel = lobby.Get<ScrollPanelWidget>("CHAT_DISPLAY");
			lobbyChatPanel.RemoveChildren();

			var settingsButton = lobby.GetOrNull<ButtonWidget>("SETTINGS_BUTTON");
			if (settingsButton != null)
			{
				settingsButton.OnClick = () => Ui.OpenWindow("SETTINGS_PANEL", new WidgetArgs
				{
					{ "onExit", DoNothing },
					{ "worldRenderer", worldRenderer }
				});
			}

			if (logicArgs.TryGetValue("ChatLineSound", out var yaml))
				chatLineSound = yaml.Value;
			if (logicArgs.TryGetValue("PlayerJoinedSound", out yaml))
				playerJoinedSound = yaml.Value;
			if (logicArgs.TryGetValue("PlayerLeftSound", out yaml))
				playerLeftSound = yaml.Value;
			if (logicArgs.TryGetValue("LobbyOptionChangedSound", out yaml))
				lobbyOptionChangedSound = yaml.Value;
		}

		bool disposed;
		protected override void Dispose(bool disposing)
		{
			if (disposing && !disposed)
			{
				disposed = true;
				Game.LobbyInfoChanged -= UpdateCurrentMap;
				Game.LobbyInfoChanged -= UpdatePlayerList;
				Game.LobbyInfoChanged -= UpdateDiscordStatus;
				Game.LobbyInfoChanged -= UpdateSpawnOccupants;
				Game.BeforeGameStart -= OnGameStart;
				Game.ConnectionStateChanged -= ConnectionStateChanged;
			}

			base.Dispose(disposing);
		}

		bool OptionsTabDisabled()
		{
			return !MapIsPlayable || panel == PanelType.Kick || panel == PanelType.ForceStart;
		}

		public override void Tick()
		{
			if (panel == PanelType.Options && OptionsTabDisabled())
				panel = PanelType.Players;

			// Apply a pending test-mode tab switch once the map is playable.
			// This runs at most once per lobby load.
			if (pendingTestTab.HasValue && MapIsPlayable)
			{
				panel = pendingTestTab.Value;
				pendingTestTab = null;
			}

			// WW3MOD: drop a "lobby is ready" marker for external screenshot
			// drivers. Fires once per lobby load — wraps the file write in a
			// try so a filesystem error never spams the tick loop.
			if (!lobbyReadyMarkerWritten && MapIsPlayable
				&& TestMode.IsActive && !string.IsNullOrEmpty(TestMode.LobbyReadyFile))
			{
				lobbyReadyMarkerWritten = true;
				try { System.IO.File.WriteAllText(TestMode.LobbyReadyFile, System.DateTime.UtcNow.ToString("o")); }
				catch (System.Exception e) { Log.Write("debug", $"[TestMode] LobbyReadyFile write failed: {e.Message}"); }
			}

			var chatWasEnabled = chatEnabled;
			chatEnabled =
				worldRenderer.World.IsReplay ||
				(Game.RunTime >= TextNotificationsManager.ChatDisabledUntil && TextNotificationsManager.ChatDisabledUntil != uint.MaxValue);

			if (chatEnabled && !chatWasEnabled)
			{
				chatTextField.Text = "";
				if (Ui.KeyboardFocusWidget == null)
					chatTextField.TakeKeyboardFocus();
			}
			else if (!chatEnabled)
			{
				var remaining = 0;
				if (TextNotificationsManager.ChatDisabledUntil != uint.MaxValue)
					remaining = (int)(TextNotificationsManager.ChatDisabledUntil - Game.RunTime + 999) / 1000;

				chatTextField.Text = remaining == 0 ? chatDisabled : chatAvailableIn.Update(remaining);
			}
		}

		void INotificationHandler<TextNotification>.Handle(TextNotification notification)
		{
			var chatLine = chatTemplates[notification.Pool].Clone();
			WidgetUtils.SetupTextNotification(chatLine, notification, lobbyChatPanel.Bounds.Width - lobbyChatPanel.ScrollbarWidth, true);

			var scrolledToBottom = lobbyChatPanel.ScrolledToBottom;
			lobbyChatPanel.AddChild(chatLine);
			if (scrolledToBottom)
				lobbyChatPanel.ScrollToBottom(smooth: true);

			switch (notification.Pool)
			{
				case TextNotificationPool.Chat:
					Game.Sound.PlayNotification(modRules, null, "Sounds", chatLineSound, null);
					break;
				case TextNotificationPool.System:
					Game.Sound.PlayNotification(modRules, null, "Sounds", lobbyOptionChangedSound, null);
					break;
				case TextNotificationPool.Join:
					Game.Sound.PlayNotification(modRules, null, "Sounds", playerJoinedSound, null);
					break;
				case TextNotificationPool.Leave:
					Game.Sound.PlayNotification(modRules, null, "Sounds", playerLeftSound, null);
					break;
			}
		}

		void UpdateCurrentMap()
		{
			mapStatus = orderManager.LobbyInfo.GlobalSettings.MapStatus;
			var uid = orderManager.LobbyInfo.GlobalSettings.Map;
			if (map.Uid == uid)
				return;

			map = modData.MapCache[uid];

			// Tell the server that we have the map
			if (map.Status == MapStatus.Available)
				orderManager.IssueOrder(Order.Command($"state {Session.ClientState.NotReady}"));

			// We don't have the map
			else if (map.Status != MapStatus.DownloadAvailable && Game.Settings.Game.AllowDownloading)
				modData.MapCache.QueryRemoteMapDetails(services.MapRepository, new[] { uid });
		}

		void UpdatePlayerList()
		{
			if (orderManager.LocalClient == null)
				return;

			// Check if we are not assigned to any team, and are no spectator
			// If we are a spectator, check if there are more and enable spectator chat
			// Otherwise check if our assigned team has more players
			if (orderManager.LocalClient.Team == 0 && !orderManager.LocalClient.IsObserver)
				disableTeamChat = true;
			else if (orderManager.LocalClient.IsObserver)
				disableTeamChat = !orderManager.LobbyInfo.Clients.Any(c => c != orderManager.LocalClient && c.IsObserver);
			else
				disableTeamChat = !orderManager.LobbyInfo.Clients.Any(c =>
					c != orderManager.LocalClient &&
					c.Bot == null &&
					c.Team == orderManager.LocalClient.Team);

			insufficientPlayerSpawns = LobbyUtils.InsufficientEnabledSpawnPoints(map, orderManager.LobbyInfo);

			if (disableTeamChat)
				teamChat = false;

			var isHost = Game.IsHost;
			var idx = 0;
			foreach (var kv in orderManager.LobbyInfo.Slots)
			{
				var key = kv.Key;
				var slot = kv.Value;
				var client = orderManager.LobbyInfo.ClientInSlot(key);
				Widget template = null;

				// get template for possible reuse
				if (idx < players.Children.Count)
					template = players.Children[idx];

				if (client == null)
				{
					// Empty slot
					if (template == null || template.Id != emptySlotTemplate.Id)
						template = emptySlotTemplate.Clone();

					// WW3MOD: host gets a strip of inline quick-action buttons
					// (Play / + Any AI / + NATO AI / + Russia AI / Close|Open) instead of the
					// classic dropdown. Non-host falls back to the wide JOIN button.
					LobbyUtils.SetupEmptySlotButtons(template, slot, orderManager, map, isHost, key);
				}
				else if ((client.Index == orderManager.LocalClient.Index) ||
						 (client.Bot != null && isHost))
				{
					// Editable player in slot
					if (template == null || template.Id != editablePlayerTemplate.Id)
						template = editablePlayerTemplate.Clone();

					LobbyUtils.SetupLatencyWidget(template, client, orderManager);

					if (client.Bot != null)
						LobbyUtils.SetupEditableSlotWidget(template, slot, client, orderManager, map, modData);
					else
						LobbyUtils.SetupEditableNameWidget(template, client, orderManager, worldRenderer);

					LobbyUtils.SetupEditableColorWidget(template, slot, client, orderManager, worldRenderer, colorManager);
					LobbyUtils.SetupEditableFactionWidget(template, slot, client, orderManager, factions);
					LobbyUtils.SetupEditableTeamWidget(template, slot, client, orderManager, map);
					LobbyUtils.SetupEditableHandicapWidget(template, slot, client, orderManager);
					LobbyUtils.SetupEditableSpawnWidget(template, slot, client, orderManager, map);
					LobbyUtils.SetupEditableReadyWidget(template, client, orderManager, map, MapIsPlayable);
				}
				else
				{
					// Non-editable player in slot
					if (template == null || template.Id != nonEditablePlayerTemplate.Id)
						template = nonEditablePlayerTemplate.Clone();

					LobbyUtils.SetupLatencyWidget(template, client, orderManager);
					LobbyUtils.SetupColorWidget(template, client);
					LobbyUtils.SetupFactionWidget(template, client, factions);

					if (isHost)
					{
						LobbyUtils.SetupEditableSpawnWidget(template, slot, client, orderManager, map);
						LobbyUtils.SetupPlayerActionWidget(template, client, orderManager, worldRenderer,
							lobby, () => panel = PanelType.Kick, () => panel = PanelType.Players);
					}
					else
					{
						LobbyUtils.SetupNameWidget(template, client, orderManager, worldRenderer, map);
						LobbyUtils.SetupSpawnWidget(template, client);
					}

					// Phase 5 — V5 row drops Team and Handicap. Widgets stay
					// in the templates so other Get<>() calls resolve, but
					// nothing unhides them so they never paint stray text.
					LobbyUtils.HideChildWidget(template, "TEAM_DROPDOWN");
					LobbyUtils.HideChildWidget(template, "HANDICAP_DROPDOWN");
					LobbyUtils.HideChildWidget(template, "TEAM");
					LobbyUtils.HideChildWidget(template, "HANDICAP");

					LobbyUtils.SetupReadyWidget(template, client);
				}

				template.IsVisible = () => true;

				if (idx >= players.Children.Count)
					players.AddChild(template);
				else if (players.Children[idx].Id != template.Id)
					players.ReplaceChild(players.Children[idx], template);

				idx++;
			}

			// Add spectators
			foreach (var client in orderManager.LobbyInfo.Clients.Where(client => client.Slot == null))
			{
				Widget template = null;
				var c = client;

				// get template for possible reuse
				if (idx < players.Children.Count)
					template = players.Children[idx];

				// Editable spectator
				if (c.Index == orderManager.LocalClient.Index)
				{
					if (template == null || template.Id != editableSpectatorTemplate.Id)
						template = editableSpectatorTemplate.Clone();

					LobbyUtils.SetupEditableNameWidget(template, c, orderManager, worldRenderer);

					if (client.IsAdmin)
						LobbyUtils.SetupEditableReadyWidget(template, client, orderManager, map, MapIsPlayable);
					else
						LobbyUtils.HideReadyWidgets(template);
				}
				else
				{
					// Non-editable spectator
					if (template == null || template.Id != nonEditableSpectatorTemplate.Id)
						template = nonEditableSpectatorTemplate.Clone();

					if (isHost)
						LobbyUtils.SetupPlayerActionWidget(template, client, orderManager, worldRenderer,
							lobby, () => panel = PanelType.Kick, () => panel = PanelType.Players);
					else
						LobbyUtils.SetupNameWidget(template, client, orderManager, worldRenderer, map);

					if (client.IsAdmin)
						LobbyUtils.SetupReadyWidget(template, client);
					else
						LobbyUtils.HideReadyWidgets(template);
				}

				LobbyUtils.SetupLatencyWidget(template, c, orderManager);
				template.IsVisible = () => true;

				if (idx >= players.Children.Count)
					players.AddChild(template);
				else if (players.Children[idx].Id != template.Id)
					players.ReplaceChild(players.Children[idx], template);

				idx++;
			}

			// Spectate button is no longer rendered as a scroll-panel row — the
			// SPECTATE_AREA widget lives outside the roster (see lobby-players.yaml)
			// and is wired up once at startup further below in this constructor.

			while (players.Children.Count > idx)
				players.RemoveChild(players.Children[idx]);

			ResizeRosterToFit(idx);

			tabCompletion.Names = orderManager.LobbyInfo.Clients.Where(c => !c.IsBot).Select(c => c.Name).Distinct().ToList();
		}

		// Roster auto-resize was useful when sections stacked vertically inside a
		// single panel — growing the roster shifted everything below. With the
		// new 2x2 grid layout (pass 12), the roster lives in a fixed-height cell
		// and uses its own internal scroll for overflow. Other sections live in
		// their own cells and don't move. So this is now a no-op.
		void ResizeRosterToFit(int rowCount)
		{
			_ = rowCount;
		}

		void UpdateDiscordStatus()
		{
			var numberOfPlayers = 0;
			var slots = 0;

			if (!skirmishMode)
			{
				foreach (var kv in orderManager.LobbyInfo.Slots)
				{
					if (kv.Value.Closed)
						continue;

					slots++;
					var client = orderManager.LobbyInfo.ClientInSlot(kv.Key);

					if (client != null)
						numberOfPlayers++;
				}
			}

			// Add extra slots to keep the join button active for spectators
			if (numberOfPlayers == slots && orderManager.LobbyInfo.GlobalSettings.AllowSpectators)
				slots = numberOfPlayers + 1;

			var details = map.Title + " - " + orderManager.LobbyInfo.GlobalSettings.ServerName;
			if (updateDiscordStatus)
			{
				string secret = null;
				if (orderManager.LobbyInfo.GlobalSettings.Dedicated)
				{
					var endpoint = CurrentServerSettings.Target.GetConnectEndPoints().First();
					secret = string.Concat(endpoint.Address, "|", endpoint.Port);
				}

				var state = skirmishMode ? DiscordState.InSkirmishLobby : DiscordState.InMultiplayerLobby;

				DiscordService.UpdateStatus(state, details, secret, numberOfPlayers, slots);
				updateDiscordStatus = false;
			}
			else
			{
				if (!skirmishMode)
					DiscordService.UpdatePlayers(numberOfPlayers, slots);

				DiscordService.UpdateDetails(details);
			}
		}

		void UpdateSpawnOccupants()
		{
			spawnOccupants = orderManager.LobbyInfo.Clients
				.Where(c => c.SpawnPoint != 0)
				.ToDictionary(c => c.SpawnPoint, c => new SpawnOccupant(c));
		}

		void UpdateOptions()
		{
			if (map == null || map.WorldActorInfo == null)
				return;

			var serverOptions = orderManager.LobbyInfo.GlobalSettings.LobbyOptions;
			var mapOptions = map.PlayerActorInfo.TraitInfos<ILobbyOptions>()
				.Concat(map.WorldActorInfo.TraitInfos<ILobbyOptions>())
				.SelectMany(t => t.LobbyOptions(map))
				.Where(o => o.IsVisible)
				.OrderBy(o => o.DisplayOrder)
				.ToArray();

			resetOptionsButtonEnabled = mapOptions.Any(o => o.DefaultValue != serverOptions[o.Id].Value);
		}

		void OnGameStart()
		{
			Ui.CloseWindow();

			var state = skirmishMode ? DiscordState.PlayingSkirmish : DiscordState.PlayingMultiplayer;
			var details = map.Title + " - " + orderManager.LobbyInfo.GlobalSettings.ServerName;
			DiscordService.UpdateStatus(state, details);

			onStart();
		}
	}

	public class LobbyFaction
	{
		public bool Selectable;
		public string Name;
		public string Description;
		public string Side;
	}

	sealed class DropDownOption
	{
		public string Title;
		public Func<bool> IsSelected = () => false;
		public Action OnClick;
	}
}
