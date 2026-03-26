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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenRA.Network;
using OpenRA.Support;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class MainMenuLogic : ChromeLogic
	{
		protected enum MenuType { Main, Singleplayer, Extras, MapEditor, StartupPrompts, None }

		protected enum MenuPanel { None, Missions, Skirmish, Multiplayer, MapEditor, Replays, GameSaves }

		protected MenuType menuType = MenuType.Main;
		readonly Widget rootMenu;
		readonly ScrollPanelWidget newsPanel;
		readonly Widget newsTemplate;
		readonly LabelWidget newsStatus;
		readonly ModData modData;

		[TranslationReference]
		static readonly string LoadingNews = "loading-news";

		[TranslationReference("message")]
		static readonly string NewsRetrivalFailed = "news-retrival-failed";

		[TranslationReference("message")]
		static readonly string NewsParsingFailed = "news-parsing-failed";

		// Update news once per game launch
		static bool fetchedNews;

		protected static MenuPanel lastGameState = MenuPanel.None;

		bool newsOpen;

		void SwitchMenu(MenuType type)
		{
			menuType = type;

			DiscordService.UpdateStatus(DiscordState.InMenu);

			// Update button mouseover
			Game.RunAfterTick(Ui.ResetTooltips);
		}

		void SetupShellmapSelector(Widget widget, World world)
		{
			var shellmaps = Game.GetAvailableShellmaps();
			if (shellmaps.Length == 0)
				return;

			var currentUid = world.Map.Uid;
			var currentIndex = Array.FindIndex(shellmaps, m => m.Uid == currentUid);

			// Helper: get ordered list for display — preferred shellmaps first, then rest alphabetically
			MapPreview[] GetOrderedShellmaps()
			{
				var settings = Game.Settings.Game;
				var ordered = new List<MapPreview>();

				// Add preferred maps in order (if they still exist)
				foreach (var uid in settings.ShellmapOrder)
				{
					var match = shellmaps.FirstOrDefault(m => m.Uid == uid);
					if (match != null)
						ordered.Add(match);
				}

				// Add remaining maps alphabetically
				foreach (var m in shellmaps.OrderBy(m => m.Title))
				{
					if (!ordered.Contains(m))
						ordered.Add(m);
				}

				return ordered.ToArray();
			}

			// Helper: get display name for a UID
			string GetShellmapLabel(string uid)
			{
				if (string.IsNullOrEmpty(uid))
					return "Random";

				var map = shellmaps.FirstOrDefault(m => m.Uid == uid);
				return map?.Title ?? "Unknown";
			}

			// Helper: is the current mode "Random"?
			bool IsRandomMode() => !Game.Settings.Game.ShellmapUseOrder;

			// Helper: get current display text
			string GetCurrentLabel()
			{
				if (IsRandomMode())
					return "Random";

				var settings = Game.Settings.Game;
				if (settings.ShellmapOrder.Length > 0)
					return GetShellmapLabel(settings.ShellmapOrder[0]);

				return world.Map.Title ?? "Random";
			}

			// Alt+click: promote a shellmap UID to front of order list
			void PromoteShellmap(string uid)
			{
				var settings = Game.Settings.Game;
				var order = settings.ShellmapOrder.Where(u => u != uid).ToList();
				order.Insert(0, uid);
				settings.ShellmapOrder = order.ToArray();
				settings.ShellmapUseOrder = true;
				Game.Settings.Save();
			}

			// Alt+click when already #1: add to end instead (for building ordered list)
			void AppendShellmap(string uid)
			{
				var settings = Game.Settings.Game;
				if (settings.ShellmapOrder.Contains(uid))
					return;

				var order = settings.ShellmapOrder.ToList();
				order.Add(uid);
				settings.ShellmapOrder = order.ToArray();
				Game.Settings.Save();
			}

			// Set to random mode
			void SetRandomMode()
			{
				var settings = Game.Settings.Game;
				settings.ShellmapUseOrder = false;
				Game.Settings.Save();
			}

			// Navigate to next/prev shellmap
			void NavigateShellmap(int direction)
			{
				var ordered = GetOrderedShellmaps();
				if (ordered.Length <= 1)
					return;

				var idx = Array.FindIndex(ordered, m => m.Uid == currentUid);
				if (idx < 0)
					idx = 0;

				idx = (idx + direction + ordered.Length) % ordered.Length;
				var targetUid = ordered[idx].Uid;
				Game.RunAfterTick(() => Game.LoadShellMap(targetUid));
			}

			// Prev button
			var prevButton = widget.GetOrNull<ButtonWidget>("SHELLMAP_PREV");
			if (prevButton != null)
				prevButton.OnClick = () => NavigateShellmap(-1);

			// Next button
			var nextButton = widget.GetOrNull<ButtonWidget>("SHELLMAP_NEXT");
			if (nextButton != null)
				nextButton.OnClick = () => NavigateShellmap(1);

			// Dropdown
			var dropdown = widget.GetOrNull<DropDownButtonWidget>("SHELLMAP_DROPDOWN");
			if (dropdown != null)
			{
				dropdown.GetText = GetCurrentLabel;

				dropdown.OnMouseDown = _ =>
				{
					ShowShellmapDropdown(dropdown, shellmaps, IsRandomMode, SetRandomMode, PromoteShellmap, AppendShellmap, GetOrderedShellmaps);
				};
			}
		}

		static void ShowShellmapDropdown(
			DropDownButtonWidget dropdown,
			MapPreview[] shellmaps,
			Func<bool> isRandomMode,
			Action setRandomMode,
			Action<string> promoteShellmap,
			Action<string> appendShellmap,
			Func<MapPreview[]> getOrderedShellmaps)
		{
			var options = new List<(string uid, string label, bool isRandom)>();
			options.Add(("", "Random", true));

			foreach (var m in getOrderedShellmaps())
			{
				var settings = Game.Settings.Game;
				var rank = Array.IndexOf(settings.ShellmapOrder, m.Uid);
				var label = rank >= 0 ? $"{rank + 1}. {m.Title}" : m.Title;
				options.Add((m.Uid, label, false));
			}

			ScrollItemWidget SetupItem((string uid, string label, bool isRandom) option, ScrollItemWidget template)
			{
				var isSelected = option.isRandom
					? (Func<bool>)isRandomMode
					: () => !isRandomMode() && Game.Settings.Game.ShellmapOrder.Length > 0
						&& Game.Settings.Game.ShellmapOrder[0] == option.uid;

				var item = ScrollItemWidget.Setup(template, isSelected, () =>
				{
					var modifiers = Game.GetModifierKeys();
					var isAlt = modifiers.HasFlag(Modifiers.Alt);

					if (isAlt)
					{
						// Alt+click: reorder without loading
						if (option.isRandom)
						{
							setRandomMode();
							return;
						}

						var settings = Game.Settings.Game;
						if (settings.ShellmapOrder.Contains(option.uid))
						{
							// Already in list — promote to front
							promoteShellmap(option.uid);
						}
						else
						{
							// Not in list — append to end
							appendShellmap(option.uid);
							settings.ShellmapUseOrder = true;
							Game.Settings.Save();
						}
					}
					else
					{
						// Normal click: select this shellmap and load it
						if (option.isRandom)
						{
							setRandomMode();
							Game.RunAfterTick(Game.LoadShellMap);
						}
						else
						{
							promoteShellmap(option.uid);
							Game.RunAfterTick(() => Game.LoadShellMap(option.uid));
						}
					}
				});

				item.Get<LabelWidget>("LABEL").GetText = () => option.label;
				return item;
			}

			dropdown.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 300, options, SetupItem);
		}

		[ObjectCreator.UseCtor]
		public MainMenuLogic(Widget widget, World world, ModData modData)
		{
			this.modData = modData;

			rootMenu = widget;
			rootMenu.Get<ButtonWidget>("INFO_BUTTON").OnClick = () =>
			{
				Ui.OpenWindow("MOD_INFO_PANEL", new WidgetArgs
				{
					{ "onExit", (Action)(() => { }) },
					{ "shellmapName", world.Map.Title ?? "" }
				});
			};

			// Shellmap selector — prev/next/dropdown with alt-click reordering
			SetupShellmapSelector(widget, world);

			// Shellmap Replay button — restarts current shellmap battle
			var replayButton = rootMenu.GetOrNull<ButtonWidget>("REPLAY_BUTTON");
			if (replayButton != null)
				replayButton.OnClick = () => Game.RunAfterTick(() => Game.LoadShellMap(world.Map.Uid));

			// Shellmap Nuke button — lets user click to nuke a location
			var nukeButton = rootMenu.GetOrNull<ButtonWidget>("NUKE_BUTTON");
			var nukeOverlay = rootMenu.GetOrNull<ShellmapNukeOverlayWidget>("NUKE_OVERLAY");
			if (nukeButton != null && nukeOverlay != null)
			{
				nukeButton.OnClick = () =>
				{
					if (nukeOverlay.IsNukeMode)
						nukeOverlay.Deactivate();
					else
						nukeOverlay.Activate(() => { });
				};
			}

			// Menu buttons
			var mainMenu = widget.Get("MAIN_MENU");
			mainMenu.IsVisible = () => menuType == MenuType.Main;

			mainMenu.Get<ButtonWidget>("SINGLEPLAYER_BUTTON").OnClick = () => SwitchMenu(MenuType.Singleplayer);

			mainMenu.Get<ButtonWidget>("MULTIPLAYER_BUTTON").OnClick = OpenMultiplayerPanel;

			mainMenu.Get<ButtonWidget>("CONTENT_BUTTON").OnClick = () =>
			{
				// Switching mods changes the world state (by disposing it),
				// so we can't do this inside the input handler.
				Game.RunAfterTick(() =>
				{
					var content = modData.Manifest.Get<ModContent>();
					Game.InitializeMod(content.ContentInstallerMod, new Arguments(new[] { "Content.Mod=" + modData.Manifest.Id }));
				});
			};

			mainMenu.Get<ButtonWidget>("SETTINGS_BUTTON").OnClick = () =>
			{
				SwitchMenu(MenuType.None);
				Game.OpenWindow("SETTINGS_PANEL", new WidgetArgs
				{
					{ "onExit", () => SwitchMenu(MenuType.Main) }
				});
			};

			mainMenu.Get<ButtonWidget>("EXTRAS_BUTTON").OnClick = () => SwitchMenu(MenuType.Extras);

			mainMenu.Get<ButtonWidget>("QUIT_BUTTON").OnClick = Game.Exit;

			// Singleplayer menu
			var singleplayerMenu = widget.Get("SINGLEPLAYER_MENU");
			singleplayerMenu.IsVisible = () => menuType == MenuType.Singleplayer;

			var missionsButton = singleplayerMenu.Get<ButtonWidget>("MISSIONS_BUTTON");
			missionsButton.OnClick = () => OpenMissionBrowserPanel(modData.MapCache.PickLastModifiedMap(MapVisibility.MissionSelector));

			var hasCampaign = modData.Manifest.Missions.Length > 0;
			var hasMissions = modData.MapCache
				.Any(p => p.Status == MapStatus.Available && p.Visibility.HasFlag(MapVisibility.MissionSelector));

			missionsButton.Disabled = !hasCampaign && !hasMissions;

			var hasMaps = modData.MapCache.Any(p => p.Visibility.HasFlag(MapVisibility.Lobby));
			var skirmishButton = singleplayerMenu.Get<ButtonWidget>("SKIRMISH_BUTTON");
			skirmishButton.OnClick = StartSkirmishGame;
			skirmishButton.Disabled = !hasMaps;

			var loadButton = singleplayerMenu.Get<ButtonWidget>("LOAD_BUTTON");
			loadButton.IsDisabled = () => !GameSaveBrowserLogic.IsLoadPanelEnabled(modData.Manifest);
			loadButton.OnClick = OpenGameSaveBrowserPanel;

			var encyclopediaButton = singleplayerMenu.GetOrNull<ButtonWidget>("ENCYCLOPEDIA_BUTTON");
			if (encyclopediaButton != null)
				encyclopediaButton.OnClick = OpenEncyclopediaPanel;

			singleplayerMenu.Get<ButtonWidget>("BACK_BUTTON").OnClick = () => SwitchMenu(MenuType.Main);

			// Extras menu
			var extrasMenu = widget.Get("EXTRAS_MENU");
			extrasMenu.IsVisible = () => menuType == MenuType.Extras;

			extrasMenu.Get<ButtonWidget>("REPLAYS_BUTTON").OnClick = OpenReplayBrowserPanel;

			extrasMenu.Get<ButtonWidget>("MUSIC_BUTTON").OnClick = () =>
			{
				SwitchMenu(MenuType.None);
				Ui.OpenWindow("MUSIC_PANEL", new WidgetArgs
				{
					{ "onExit", () => SwitchMenu(MenuType.Extras) },
					{ "world", world }
				});
			};

			extrasMenu.Get<ButtonWidget>("MAP_EDITOR_BUTTON").OnClick = () => SwitchMenu(MenuType.MapEditor);

			var assetBrowserButton = extrasMenu.GetOrNull<ButtonWidget>("ASSETBROWSER_BUTTON");
			if (assetBrowserButton != null)
				assetBrowserButton.OnClick = () =>
				{
					SwitchMenu(MenuType.None);
					Game.OpenWindow("ASSETBROWSER_PANEL", new WidgetArgs
					{
						{ "onExit", () => SwitchMenu(MenuType.Extras) },
					});
				};

			extrasMenu.Get<ButtonWidget>("CREDITS_BUTTON").OnClick = () =>
			{
				SwitchMenu(MenuType.None);
				Ui.OpenWindow("CREDITS_PANEL", new WidgetArgs
				{
					{ "onExit", () => SwitchMenu(MenuType.Extras) },
				});
			};

			extrasMenu.Get<ButtonWidget>("BACK_BUTTON").OnClick = () => SwitchMenu(MenuType.Main);

			// Map editor menu
			var mapEditorMenu = widget.Get("MAP_EDITOR_MENU");
			mapEditorMenu.IsVisible = () => menuType == MenuType.MapEditor;

			// Loading into the map editor
			Game.BeforeGameStart += RemoveShellmapUI;

			var onSelect = new Action<string>(uid =>
			{
				if (modData.MapCache[uid].Status != MapStatus.Available)
					SwitchMenu(MenuType.Extras);
				else
					LoadMapIntoEditor(modData.MapCache[uid].Uid);
			});

			var newMapButton = widget.Get<ButtonWidget>("NEW_MAP_BUTTON");
			newMapButton.OnClick = () =>
			{
				SwitchMenu(MenuType.None);
				Game.OpenWindow("NEW_MAP_BG", new WidgetArgs()
				{
					{ "onSelect", onSelect },
					{ "onExit", () => SwitchMenu(MenuType.MapEditor) }
				});
			};

			var loadMapButton = widget.Get<ButtonWidget>("LOAD_MAP_BUTTON");
			loadMapButton.OnClick = () =>
			{
				SwitchMenu(MenuType.None);
				Game.OpenWindow("MAPCHOOSER_PANEL", new WidgetArgs()
				{
					{ "initialMap", null },
					{ "initialTab", MapClassification.User },
					{ "onExit", () => SwitchMenu(MenuType.MapEditor) },
					{ "onSelect", onSelect },
					{ "filter", MapVisibility.Lobby | MapVisibility.Shellmap | MapVisibility.MissionSelector },
				});
			};

			loadMapButton.Disabled = !hasMaps;

			mapEditorMenu.Get<ButtonWidget>("BACK_BUTTON").OnClick = () => SwitchMenu(MenuType.Extras);

			var newsBG = widget.GetOrNull("NEWS_BG");
			if (newsBG != null)
			{
				newsBG.IsVisible = () => Game.Settings.Game.FetchNews && menuType != MenuType.None && menuType != MenuType.StartupPrompts;

				newsPanel = Ui.LoadWidget<ScrollPanelWidget>("NEWS_PANEL", null, new WidgetArgs());
				newsTemplate = newsPanel.Get("NEWS_ITEM_TEMPLATE");
				newsPanel.RemoveChild(newsTemplate);

				newsStatus = newsPanel.Get<LabelWidget>("NEWS_STATUS");
				SetNewsStatus(modData.Translation.GetString(LoadingNews));
			}

			Game.OnRemoteDirectConnect += OnRemoteDirectConnect;

			// Check for updates in the background
			var webServices = modData.Manifest.Get<WebServices>();
			if (Game.Settings.Debug.CheckVersion)
				webServices.CheckModVersion();

			var updateLabel = rootMenu.GetOrNull("UPDATE_NOTICE");
			if (updateLabel != null)
				updateLabel.IsVisible = () => !newsOpen && menuType != MenuType.None &&
					menuType != MenuType.StartupPrompts &&
					webServices.ModVersionStatus == ModVersionStatus.Outdated;

			var playerProfile = widget.GetOrNull("PLAYER_PROFILE_CONTAINER");
			if (playerProfile != null)
			{
				Func<bool> minimalProfile = () => Ui.CurrentWindow() != null;
				Game.LoadWidget(world, "LOCAL_PROFILE_PANEL", playerProfile, new WidgetArgs()
				{
					{ "minimalProfile", minimalProfile }
				});
			}

			menuType = MenuType.StartupPrompts;

			Action onIntroductionComplete = () =>
			{
				Action onSysInfoComplete = () =>
				{
					LoadAndDisplayNews(webServices, newsBG);
					SwitchMenu(MenuType.Main);
				};

				if (SystemInfoPromptLogic.ShouldShowPrompt())
				{
					Ui.OpenWindow("MAINMENU_SYSTEM_INFO_PROMPT", new WidgetArgs
					{
						{ "onComplete", onSysInfoComplete }
					});
				}
				else
					onSysInfoComplete();
			};

			if (IntroductionPromptLogic.ShouldShowPrompt())
			{
				Game.OpenWindow("MAINMENU_INTRODUCTION_PROMPT", new WidgetArgs
				{
					{ "onComplete", onIntroductionComplete }
				});
			}
			else
				onIntroductionComplete();

			Game.OnShellmapLoaded += OpenMenuBasedOnLastGame;

			DiscordService.UpdateStatus(DiscordState.InMenu);
		}

		void LoadAndDisplayNews(WebServices webServices, Widget newsBG)
		{
			if (newsBG != null && Game.Settings.Game.FetchNews)
			{
				var cacheFile = Path.Combine(Platform.SupportDir, webServices.GameNewsFileName);
				var currentNews = ParseNews(cacheFile);
				if (currentNews != null)
					DisplayNews(currentNews);

				var newsButton = newsBG.GetOrNull<DropDownButtonWidget>("NEWS_BUTTON");
				if (newsButton != null)
				{
					if (!fetchedNews)
					{
						Task.Run(async () =>
						{
							try
							{
								var client = HttpClientFactory.Create();

								// Send the mod and engine version to support version-filtered news (update prompts)
								var url = new HttpQueryBuilder(webServices.GameNews)
								{
									{ "version", Game.EngineVersion },
									{ "mod", modData.Manifest.Id },
									{ "modversion", modData.Manifest.Metadata.Version }
								}.ToString();

								// Parameter string is blank if the player has opted out
								url += SystemInfoPromptLogic.CreateParameterString();

								var response = await client.GetStringAsync(url);
								await File.WriteAllTextAsync(cacheFile, response);

								Game.RunAfterTick(() => // run on the main thread
								{
									fetchedNews = true;
									var newNews = ParseNews(cacheFile);
									if (newNews == null)
										return;

									DisplayNews(newNews);

									if (currentNews == null || newNews.Any(n => !currentNews.Select(c => c.DateTime).Contains(n.DateTime)))
										OpenNewsPanel(newsButton);
								});
							}
							catch (Exception e)
							{
								Game.RunAfterTick(() => // run on the main thread
								{
									SetNewsStatus(modData.Translation.GetString(NewsRetrivalFailed, Translation.Arguments("message", e.Message)));
								});
							}
						});
					}

					newsButton.OnClick = () => OpenNewsPanel(newsButton);
				}
			}
		}

		void OpenNewsPanel(DropDownButtonWidget button)
		{
			newsOpen = true;
			button.AttachPanel(newsPanel, () => newsOpen = false);
		}

		void OnRemoteDirectConnect(ConnectionTarget endpoint)
		{
			SwitchMenu(MenuType.None);
			Ui.OpenWindow("MULTIPLAYER_PANEL", new WidgetArgs
			{
				{ "onStart", RemoveShellmapUI },
				{ "onExit", () => SwitchMenu(MenuType.Main) },
				{ "directConnectEndPoint", endpoint },
			});
		}

		static void LoadMapIntoEditor(string uid)
		{
			Game.LoadEditor(uid);

			DiscordService.UpdateStatus(DiscordState.InMapEditor);

			lastGameState = MenuPanel.MapEditor;
		}

		void SetNewsStatus(string message)
		{
			message = WidgetUtils.WrapText(message, newsStatus.Bounds.Width, Game.Renderer.Fonts[newsStatus.Font]);
			newsStatus.GetText = () => message;
		}

		class NewsItem
		{
			public string Title;
			public string Author;
			public DateTime DateTime;
			public string Content;
		}

		NewsItem[] ParseNews(string path)
		{
			if (!File.Exists(path))
				return null;

			try
			{
				return MiniYaml.FromFile(path).Select(node =>
				{
					var nodesDict = node.Value.ToDictionary();
					return new NewsItem
					{
						Title = nodesDict["Title"].Value,
						Author = nodesDict["Author"].Value,
						DateTime = FieldLoader.GetValue<DateTime>("DateTime", node.Key),
						Content = nodesDict["Content"].Value
					};
				}).ToArray();
			}
			catch (Exception ex)
			{
				SetNewsStatus(modData.Translation.GetString(NewsParsingFailed, Translation.Arguments("message", ex.Message)));
			}

			return null;
		}

		void DisplayNews(IEnumerable<NewsItem> newsItems)
		{
			newsPanel.RemoveChildren();
			SetNewsStatus("");

			foreach (var i in newsItems)
			{
				var item = i;

				var newsItem = newsTemplate.Clone();

				var titleLabel = newsItem.Get<LabelWidget>("TITLE");
				titleLabel.GetText = () => item.Title;

				var authorDateTimeLabel = newsItem.Get<LabelWidget>("AUTHOR_DATETIME");
				var authorDateTime = authorDateTimeLabel.Text.F(item.Author, item.DateTime.ToLocalTime());
				authorDateTimeLabel.GetText = () => authorDateTime;

				var contentLabel = newsItem.Get<LabelWidget>("CONTENT");
				var content = item.Content.Replace("\\n", "\n");
				content = WidgetUtils.WrapText(content, contentLabel.Bounds.Width, Game.Renderer.Fonts[contentLabel.Font]);
				contentLabel.GetText = () => content;
				contentLabel.Bounds.Height = Game.Renderer.Fonts[contentLabel.Font].Measure(content).Y;
				newsItem.Bounds.Height += contentLabel.Bounds.Height;

				newsPanel.AddChild(newsItem);
				newsPanel.Layout.AdjustChildren();
			}
		}

		void RemoveShellmapUI()
		{
			rootMenu.Parent.RemoveChild(rootMenu);
		}

		void StartSkirmishGame()
		{
			var map = modData.MapCache.ChooseInitialMap(modData.MapCache.PickLastModifiedMap(MapVisibility.Lobby) ?? Game.Settings.Server.Map, Game.CosmeticRandom);
			Game.Settings.Server.Map = map;
			Game.Settings.Save();

			ConnectionLogic.Connect(Game.CreateLocalServer(map),
				"",
				OpenSkirmishLobbyPanel,
				() => { Game.CloseServer(); SwitchMenu(MenuType.Main); });
		}

		void OpenMissionBrowserPanel(string map)
		{
			SwitchMenu(MenuType.None);
			Game.OpenWindow("MISSIONBROWSER_PANEL", new WidgetArgs
			{
				{ "onExit", () => SwitchMenu(MenuType.Singleplayer) },
				{ "onStart", () => { RemoveShellmapUI(); lastGameState = MenuPanel.Missions; } },
				{ "initialMap", map }
			});
		}

		void OpenEncyclopediaPanel()
		{
			SwitchMenu(MenuType.None);
			Game.OpenWindow("ENCYCLOPEDIA_PANEL", new WidgetArgs
			{
				{ "onExit", () => SwitchMenu(MenuType.Singleplayer) }
			});
		}

		void OpenSkirmishLobbyPanel()
		{
			SwitchMenu(MenuType.None);
			Game.OpenWindow("SERVER_LOBBY", new WidgetArgs
			{
				{ "onExit", () => { Game.Disconnect(); SwitchMenu(MenuType.Singleplayer); } },
				{ "onStart", () => { RemoveShellmapUI(); lastGameState = MenuPanel.Skirmish; } },
				{ "skirmishMode", true }
			});
		}

		void OpenMultiplayerPanel()
		{
			SwitchMenu(MenuType.None);
			Ui.OpenWindow("MULTIPLAYER_PANEL", new WidgetArgs
			{
				{ "onStart", () => { RemoveShellmapUI(); lastGameState = MenuPanel.Multiplayer; } },
				{ "onExit", () => SwitchMenu(MenuType.Main) },
				{ "directConnectEndPoint", null },
			});
		}

		void OpenReplayBrowserPanel()
		{
			SwitchMenu(MenuType.None);
			Ui.OpenWindow("REPLAYBROWSER_PANEL", new WidgetArgs
			{
				{ "onExit", () => SwitchMenu(MenuType.Extras) },
				{ "onStart", () => { RemoveShellmapUI(); lastGameState = MenuPanel.Replays; } }
			});
		}

		void OpenGameSaveBrowserPanel()
		{
			SwitchMenu(MenuType.None);
			Ui.OpenWindow("GAMESAVE_BROWSER_PANEL", new WidgetArgs
			{
				{ "onExit", () => SwitchMenu(MenuType.Singleplayer) },
				{ "onStart", () => { RemoveShellmapUI(); lastGameState = MenuPanel.GameSaves; } },
				{ "isSavePanel", false },
				{ "world", null }
			});
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				Game.OnRemoteDirectConnect -= OnRemoteDirectConnect;
				Game.BeforeGameStart -= RemoveShellmapUI;
			}

			Game.OnShellmapLoaded -= OpenMenuBasedOnLastGame;
			base.Dispose(disposing);
		}

		void OpenMenuBasedOnLastGame()
		{
			switch (lastGameState)
			{
				case MenuPanel.Missions:
					OpenMissionBrowserPanel(null);
					break;

				case MenuPanel.Replays:
					OpenReplayBrowserPanel();
					break;

				case MenuPanel.Skirmish:
					StartSkirmishGame();
					break;

				case MenuPanel.Multiplayer:
					OpenMultiplayerPanel();
					break;

				case MenuPanel.MapEditor:
					SwitchMenu(MenuType.MapEditor);
					break;

				case MenuPanel.GameSaves:
					SwitchMenu(MenuType.Singleplayer);
					break;
			}

			lastGameState = MenuPanel.None;
		}
	}
}
