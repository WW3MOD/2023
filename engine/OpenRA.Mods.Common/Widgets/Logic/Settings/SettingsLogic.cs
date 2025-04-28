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
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class SettingsLogic : ChromeLogic
	{
		[TranslationReference]
		static readonly string SettingsSaveTitle = "settings-save-title";

		[TranslationReference]
		static readonly string SettingsSavePrompt = "settings-save-prompt";

		[TranslationReference]
		static readonly string SettingsSaveCancel = "settings-save-cancel";

		[TranslationReference]
		static readonly string RestartTitle = "restart-title";

		[TranslationReference]
		static readonly string RestartPrompt = "restart-prompt";

		[TranslationReference]
		static readonly string RestartAccept = "restart-accept";

		[TranslationReference]
		static readonly string RestartCancel = "restart-cancel";

		[TranslationReference("panel")]
		static readonly string ResetTitle = "reset-title";

		[TranslationReference]
		static readonly string ResetPrompt = "reset-prompt";

		[TranslationReference]
		static readonly string ResetAccept = "reset-accept";

		[TranslationReference]
		static readonly string ResetCancel = "reset-cancel";

		readonly Dictionary<string, Func<bool>> leavePanelActions = new Dictionary<string, Func<bool>>();
		readonly Dictionary<string, Action> resetPanelActions = new Dictionary<string, Action>();

		readonly Widget panelContainer, tabContainer;
		readonly ButtonWidget tabTemplate;
		readonly int2 buttonStride;
		readonly List<ButtonWidget> buttons = new List<ButtonWidget>();
		readonly Dictionary<string, string> panels = new Dictionary<string, string>();
		string activePanel;

		bool needsRestart = false;

		static SettingsLogic() { }

		[ObjectCreator.UseCtor]
		public SettingsLogic(Widget widget, Action onExit, WorldRenderer worldRenderer, Dictionary<string, MiniYaml> logicArgs, ModData modData)
		{
			panelContainer = widget.Get("PANEL_CONTAINER");
			var panelTemplate = panelContainer.Get<ContainerWidget>("PANEL_TEMPLATE");
			panelContainer.RemoveChild(panelTemplate);

			tabContainer = widget.Get("SETTINGS_TAB_CONTAINER");
			tabTemplate = tabContainer.Get<ButtonWidget>("BUTTON_TEMPLATE");
			tabContainer.RemoveChild(tabTemplate);

			if (logicArgs.TryGetValue("ButtonStride", out var buttonStrideNode))
				buttonStride = FieldLoader.GetValue<int2>("ButtonStride", buttonStrideNode.Value);

			if (logicArgs.TryGetValue("Panels", out var settingsPanels))
			{
				panels = settingsPanels.ToDictionary(kv => kv.Value);

				foreach (var panel in panels)
				{
					var container = panelTemplate.Clone() as ContainerWidget;
					container.Id = panel.Key;
					panelContainer.AddChild(container);

					Game.LoadWidget(worldRenderer.World, panel.Key, container, new WidgetArgs()
					{
						{ "registerPanel", (Action<string, string, Func<Widget, Func<bool>>, Func<Widget, Action>>)RegisterSettingsPanel },
						{ "panelID", panel.Key },
						{ "label", panel.Value }
					});
				}
			}

			// WW3MOD: Input panel logic for new key bindings
			var inputPanel = panelContainer.GetOrNull("INPUT_PANEL");
			if (inputPanel != null)
			{
				var settings = Game.Settings.Game;

				// Helper method to set up dropdown items
				ScrollItemWidget SetupDropdownItem(string labelText, ScrollItemWidget template)
				{
					var item = template.Clone() as ScrollItemWidget;
					var label = item.GetOrNull<LabelWidget>("LABEL");
					if (label != null)
					{
						label.GetText = () => labelText;
					}

					item.IsSelected = () => false; // Selection handled by OnClick
					return item;
				}

				// Attack Move Button
				var attackMoveButton = inputPanel.GetOrNull<DropDownButtonWidget>("ATTACK_MOVE_BUTTON");
				if (attackMoveButton != null)
				{
					attackMoveButton.OnClick = () =>
					{
						var options = new Dictionary<MouseButton, string>
						{
							{ MouseButton.Left, "Left" },
							{ MouseButton.Right, "Right" },
							{ MouseButton.Middle, "Middle" }
						};
						attackMoveButton.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 150, options, (item, sb) =>
						{
							var option = SetupDropdownItem(item.Value, sb);
							option.OnClick = () =>
							{
								settings.AttackMoveButton = item.Key;
								Game.Settings.Save();
							};
							return option;
						});
					};
					attackMoveButton.GetText = () => settings.AttackMoveButton.ToString();
				}

				// Attack Move Modifiers
				var attackMoveMods = inputPanel.GetOrNull<DropDownButtonWidget>("ATTACK_MOVE_MODIFIERS");
				if (attackMoveMods != null)
				{
					attackMoveMods.OnClick = () =>
					{
						var options = new Dictionary<Modifiers, string>
						{
							{ Modifiers.None, "None" },
							{ Modifiers.Alt, "Alt" },
							{ Modifiers.Ctrl, "Ctrl" },
							{ Modifiers.Shift, "Shift" }
						};
						attackMoveMods.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 150, options, (item, sb) =>
						{
							var option = SetupDropdownItem(item.Value, sb);
							option.OnClick = () =>
							{
								settings.AttackMoveModifiers = item.Key;
								Game.Settings.Save();
							};
							return option;
						});
					};
					attackMoveMods.GetText = () => settings.AttackMoveModifiers.ToString();
				}

				// Force Move Modifiers
				var forceMoveMods = inputPanel.GetOrNull<DropDownButtonWidget>("FORCE_MOVE_MODIFIERS");
				if (forceMoveMods != null)
				{
					forceMoveMods.OnClick = () =>
					{
						var options = new Dictionary<Modifiers, string>
						{
							{ Modifiers.None, "None" },
							{ Modifiers.Alt, "Alt" },
							{ Modifiers.Ctrl, "Ctrl" },
							{ Modifiers.Shift, "Shift" }
						};
						forceMoveMods.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 150, options, (item, sb) =>
						{
							var option = SetupDropdownItem(item.Value, sb);
							option.OnClick = () =>
							{
								settings.ForceMoveModifiers = item.Key;
								Game.Settings.Save();
							};
							return option;
						});
					};
					forceMoveMods.GetText = () => settings.ForceMoveModifiers.ToString();
				}

				// Force Attack Modifiers
				var forceAttackMods = inputPanel.GetOrNull<DropDownButtonWidget>("FORCE_ATTACK_MODIFIERS");
				if (forceAttackMods != null)
				{
					forceAttackMods.OnClick = () =>
					{
						var options = new Dictionary<Modifiers, string>
						{
							{ Modifiers.None, "None" },
							{ Modifiers.Alt, "Alt" },
							{ Modifiers.Ctrl, "Ctrl" },
							{ Modifiers.Shift, "Shift" },
							{ Modifiers.Ctrl | Modifiers.Alt, "Ctrl+Alt" }
						};
						forceAttackMods.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 150, options, (item, sb) =>
						{
							var option = SetupDropdownItem(item.Value, sb);
							option.OnClick = () =>
							{
								settings.ForceAttackModifiers = item.Key;
								Game.Settings.Save();
							};
							return option;
						});
					};
					forceAttackMods.GetText = () => settings.ForceAttackModifiers.ToString();
				}

				// Register input panel actions
				RegisterSettingsPanel("INPUT_PANEL", "Input", p => () => false, p => () =>
				{
					settings.AttackMoveButton = MouseButton.Right;
					settings.AttackMoveModifiers = Modifiers.Alt;
					settings.ForceMoveModifiers = Modifiers.Ctrl;
					settings.ForceAttackModifiers = Modifiers.Ctrl | Modifiers.Alt;

					// Reset other input settings if needed
					Game.Settings.Save();
				});
			}

			widget.Get<ButtonWidget>("BACK_BUTTON").OnClick = () =>
			{
				needsRestart |= leavePanelActions[activePanel]();
				var current = Game.Settings;
				current.Save();

				Action closeAndExit = () => { Ui.CloseWindow(); onExit(); };
				if (needsRestart)
				{
					Action noRestart = () => ConfirmationDialogs.ButtonPrompt(modData,
						title: SettingsSaveTitle,
						text: SettingsSavePrompt,
						onCancel: closeAndExit,
						cancelText: SettingsSaveCancel);

					if (!Game.ExternalMods.TryGetValue(ExternalMod.MakeKey(Game.ModData.Manifest), out var external))
					{
						noRestart();
						return;
					}

					ConfirmationDialogs.ButtonPrompt(modData,
						title: RestartTitle,
						text: RestartPrompt,
						onConfirm: () => Game.SwitchToExternalMod(external, null, noRestart),
						onCancel: closeAndExit,
						confirmText: RestartAccept,
						cancelText: RestartCancel);
				}
				else
					closeAndExit();
			};

			widget.Get<ButtonWidget>("RESET_BUTTON").OnClick = () =>
			{
				Action reset = () =>
				{
					resetPanelActions[activePanel]();
					Game.Settings.Save();
				};

				ConfirmationDialogs.ButtonPrompt(modData,
					title: ResetTitle,
					titleArguments: Translation.Arguments("panel", panels[activePanel]),
					text: ResetPrompt,
					onConfirm: reset,
					onCancel: () => { },
					confirmText: ResetAccept,
					cancelText: ResetCancel);
			};
		}

		public void RegisterSettingsPanel(string panelID, string label, Func<Widget, Func<bool>> init, Func<Widget, Action> reset)
		{
			var panel = panelContainer.Get(panelID);

			if (activePanel == null)
				activePanel = panelID;

			panel.IsVisible = () => activePanel == panelID;

			leavePanelActions.Add(panelID, init(panel));
			resetPanelActions.Add(panelID, reset(panel));

			AddSettingsTab(panelID, label);
		}

		ButtonWidget AddSettingsTab(string id, string label)
		{
			var tab = tabTemplate.Clone() as ButtonWidget;
			var lastButton = buttons.LastOrDefault();
			if (lastButton != null)
			{
				tab.Bounds.X = lastButton.Bounds.X + buttonStride.X;
				tab.Bounds.Y = lastButton.Bounds.Y + buttonStride.Y;
			}

			tab.Id = id;
			tab.GetText = () => label;
			tab.IsHighlighted = () => activePanel == id;
			tab.OnClick = () =>
			{
				needsRestart |= leavePanelActions[activePanel]();
				Game.Settings.Save();
				activePanel = id;
			};

			tabContainer.AddChild(tab);
			buttons.Add(tab);

			return tab;
		}
	}
}
