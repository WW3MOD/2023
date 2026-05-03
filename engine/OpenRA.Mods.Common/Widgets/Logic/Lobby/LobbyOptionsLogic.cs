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
using OpenRA.Network;
using OpenRA.Primitives;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class LobbyOptionsLogic : ChromeLogic
	{
		[FluentReference]
		const string NotAvailable = "label-not-available";

		readonly ScrollPanelWidget panel;
		readonly Widget optionsContainer;
		readonly Widget checkboxRowTemplate;
		readonly Widget dropdownRowTemplate;
		readonly Widget sectionHeaderTemplate;
		readonly int yMargin;

		readonly Func<MapPreview> getMap;
		readonly OrderManager orderManager;
		readonly Func<bool> configurationDisabled;
		MapPreview mapPreview;

		// Tab support
		string activeTab = "";
		readonly Dictionary<string, ButtonWidget> tabButtons = new();
		readonly LabelWidget summaryLabel;

		// Category assignments for options that don't set their own Category
		static readonly Dictionary<string, string> CategoryOverrides = new()
		{
			// Economy
			{ "startingcash", "Economy" },
			{ "passiveincome", "Economy" },
			{ "incomemodifier", "Economy" },

			// Map
			{ "explored", "Map" },
			{ "fog", "Map" },
			{ "separateteamspawns", "Map" },

			// Rules
			{ "gamespeed", "Rules" },
			{ "timelimit", "Rules" },
			{ "startingunits", "Rules" },

			// Rules (player-level options)
			{ "bounty", "Rules" },

			// Advanced
			{ "cheats", "Advanced" },
			{ "sync", "Advanced" },
		};

		// Hidden options (removed from WW3MOD)
		static readonly HashSet<string> HiddenOptionIds = new()
		{
			"shortgame", "crates", "creeps", "buildradius", "allybuild", "techlevel"
		};

		// Map sub-categories to top-level tabs
		static readonly Dictionary<string, string> CategoryAliases = new()
		{
			{ "Powers", "Rules" },
			{ "Units.Infantry", "Units" },
			{ "Units.Vehicles", "Units" },
			{ "Units.Aircraft", "Units" },
		};

		// Section headers to insert for sub-categories within a tab
		static readonly Dictionary<string, string[]> TabSections = new()
		{
			{ "Units", new[] { "Units.Infantry", "Units.Vehicles", "Units.Aircraft" } },
		};

		static readonly Dictionary<string, string> SectionLabels = new()
		{
			{ "Units.Infantry", "-- INFANTRY --" },
			{ "Units.Vehicles", "-- VEHICLES --" },
			{ "Units.Aircraft", "-- AIRCRAFT --" },
		};

		static string GetCategory(LobbyOption option)
		{
			string cat;
			if (CategoryOverrides.TryGetValue(option.Id, out cat))
				return CategoryAliases.TryGetValue(cat, out var alias) ? alias : cat;

			if (!string.IsNullOrEmpty(option.Category))
			{
				cat = option.Category;
				return CategoryAliases.TryGetValue(cat, out var alias) ? alias : cat;
			}

			return "Rules";
		}

		static string GetSubCategory(LobbyOption option)
		{
			if (!string.IsNullOrEmpty(option.Category))
				return option.Category;

			return "";
		}

		[ObjectCreator.UseCtor]
		internal LobbyOptionsLogic(Widget widget, OrderManager orderManager, Func<MapPreview> getMap, Func<bool> configurationDisabled)
		{
			this.getMap = getMap;
			this.orderManager = orderManager;
			this.configurationDisabled = configurationDisabled;

			panel = (ScrollPanelWidget)widget;
			optionsContainer = widget.Get("LOBBY_OPTIONS");
			yMargin = optionsContainer.Bounds.Y;
			checkboxRowTemplate = optionsContainer.Get("CHECKBOX_ROW_TEMPLATE");
			dropdownRowTemplate = optionsContainer.Get("DROPDOWN_ROW_TEMPLATE");
			sectionHeaderTemplate = optionsContainer.GetOrNull("SECTION_HEADER_TEMPLATE");

			// Look for tab buttons in parent hierarchy
			var lobbyBin = panel.Parent;
			summaryLabel = lobbyBin?.GetOrNull<LabelWidget>("SUMMARY");

			var tabContainer = lobbyBin?.GetOrNull("TAB_BUTTONS");
			if (tabContainer != null)
			{
				var tabNames = new[] { "COMBAT", "ECONOMY", "UNITS", "MAP", "RULES", "ADVANCED" };
				foreach (var name in tabNames)
				{
					var btn = tabContainer.GetOrNull<ButtonWidget>("TAB_" + name);
					if (btn != null)
					{
						var tabName = name.Substring(0, 1) + name.Substring(1).ToLowerInvariant();
						tabButtons[tabName] = btn;
						var captured = tabName;
						btn.OnClick = () => SwitchTab(captured);
						btn.IsHighlighted = () => activeTab == captured;
					}
				}

				if (tabButtons.Count > 0)
					activeTab = "Combat";
			}

			mapPreview = getMap();
			RebuildOptions();
		}

		void SwitchTab(string tab)
		{
			activeTab = tab;
			RebuildOptions();
		}

		public override void Tick()
		{
			var newMapPreview = getMap();
			if (newMapPreview == mapPreview)
				return;

			Game.RunAfterTick(() =>
			{
				mapPreview = newMapPreview;
				RebuildOptions();
			});
		}

		void AddSectionHeader(string text)
		{
			if (sectionHeaderTemplate == null)
				return;

			var header = sectionHeaderTemplate.Clone();
			header.Bounds.Y = optionsContainer.Bounds.Height;
			header.IsVisible = () => true;
			optionsContainer.Bounds.Height += header.Bounds.Height;

			var label = header.GetOrNull<LabelWidget>("HEADER_LABEL");
			if (label != null)
				label.GetText = () => text;

			optionsContainer.AddChild(header);
		}

		void RebuildOptions()
		{
			if (mapPreview == null || mapPreview.WorldActorInfo == null)
				return;

			optionsContainer.RemoveChildren();
			optionsContainer.Bounds.Height = 0;
			var allOptions = mapPreview.PlayerActorInfo.TraitInfos<ILobbyOptions>()
					.Concat(mapPreview.WorldActorInfo.TraitInfos<ILobbyOptions>())
					.SelectMany(t => t.LobbyOptions(mapPreview))
					.Where(o => o.IsVisible && o.Id != "scenario")
					.OrderBy(o => o.DisplayOrder)
					.ToArray();

			// Filter by active tab if tabs are enabled
			var filteredOptions = allOptions;
			if (!string.IsNullOrEmpty(activeTab))
			{
				filteredOptions = allOptions
					.Where(o => !HiddenOptionIds.Contains(o.Id))
					.Where(o => GetCategory(o) == activeTab)
					.ToArray();
			}

			// Update summary label
			UpdateSummary(allOptions);

			// Check if this tab has sections
			if (TabSections.TryGetValue(activeTab, out var sections))
				RenderSectionedOptions(filteredOptions, sections);
			else
				RenderFlatOptions(filteredOptions);

			panel.ContentHeight = yMargin + optionsContainer.Bounds.Height;
			optionsContainer.Bounds.Y = yMargin;

			panel.ScrollToTop();
		}

		void RenderSectionedOptions(LobbyOption[] options, string[] sections)
		{
			foreach (var section in sections)
			{
				var sectionOptions = options.Where(o => GetSubCategory(o) == section).ToArray();
				if (sectionOptions.Length == 0)
					continue;

				if (SectionLabels.TryGetValue(section, out var label))
					AddSectionHeader(label);

				RenderFlatOptions(sectionOptions);
			}

			// Render any options that don't belong to a section
			var sectionSet = new HashSet<string>(sections);
			var unsectioned = options.Where(o => !sectionSet.Contains(GetSubCategory(o))).ToArray();
			if (unsectioned.Length > 0)
				RenderFlatOptions(unsectioned);
		}

		void RenderFlatOptions(LobbyOption[] options)
		{
			Widget row = null;
			var checkboxColumns = new Queue<CheckboxWidget>();
			var dropdownColumns = new Queue<DropDownButtonWidget>();

			foreach (var option in options.Where(o => o is LobbyBooleanOption))
			{
				if (checkboxColumns.Count == 0)
				{
					row = checkboxRowTemplate.Clone();
					row.Bounds.Y = optionsContainer.Bounds.Height;
					optionsContainer.Bounds.Height += row.Bounds.Height;
					foreach (var child in row.Children)
						if (child is CheckboxWidget childCheckbox)
							checkboxColumns.Enqueue(childCheckbox);

					optionsContainer.AddChild(row);
				}

				var checkbox = checkboxColumns.Dequeue();
				var optionEnabled = new PredictedCachedTransform<Session.Global, bool>(
					gs => gs.LobbyOptions[option.Id].IsEnabled);

				var optionLocked = new CachedTransform<Session.Global, bool>(
					gs => gs.LobbyOptions[option.Id].IsLocked);

				var checkboxName = option.Name;
				if (FluentProvider.TryGetMessage(option.Name, out var fluentName))
					checkboxName = fluentName;
				checkbox.GetText = () => checkboxName;
				if (option.Description != null)
				{
					var desc = option.Description;
					if (FluentProvider.TryGetMessage(option.Description, out var fluentDesc))
						desc = fluentDesc;
					var (text, descText) = LobbyUtils.SplitOnFirstToken(desc);
					checkbox.GetTooltipText = () => text;
					checkbox.GetTooltipDesc = () => descText;
				}

				checkbox.IsVisible = () => true;
				checkbox.IsChecked = () => optionEnabled.Update(orderManager.LobbyInfo.GlobalSettings);
				checkbox.IsDisabled = () => configurationDisabled() || optionLocked.Update(orderManager.LobbyInfo.GlobalSettings);
				checkbox.OnClick = () =>
				{
					var state = !optionEnabled.Update(orderManager.LobbyInfo.GlobalSettings);
					orderManager.IssueOrder(Order.Command($"option {option.Id} {state}"));
					optionEnabled.Predict(state);
				};
			}

			foreach (var option in options.Where(o => o is not LobbyBooleanOption))
			{
				if (dropdownColumns.Count == 0)
				{
					row = dropdownRowTemplate.Clone();
					row.Bounds.Y = optionsContainer.Bounds.Height;
					optionsContainer.Bounds.Height += row.Bounds.Height;
					foreach (var child in row.Children)
						if (child is DropDownButtonWidget dropDown)
							dropdownColumns.Enqueue(dropDown);

					optionsContainer.AddChild(row);
				}

				var dropdown = dropdownColumns.Dequeue();
				var optionValue = new CachedTransform<Session.Global, Session.LobbyOptionState>(
					gs => gs.LobbyOptions[option.Id]);

				var getOptionLabel = new CachedTransform<string, string>(id =>
				{
					if (id == null || !option.Values.TryGetValue(id, out var value))
						return FluentProvider.TryGetMessage(NotAvailable, out var na) ? na : "N/A";

					if (FluentProvider.TryGetMessage(value, out var translated))
						return translated;

					return value;
				});

				dropdown.GetText = () => getOptionLabel.Update(optionValue.Update(orderManager.LobbyInfo.GlobalSettings).Value);
				if (option.Description != null)
				{
					var desc = option.Description;
					if (FluentProvider.TryGetMessage(option.Description, out var fluentDesc))
						desc = fluentDesc;
					var (text, descText) = LobbyUtils.SplitOnFirstToken(desc);
					dropdown.GetTooltipText = () => text;
					dropdown.GetTooltipDesc = () => descText;
				}

				dropdown.IsVisible = () => true;
				dropdown.IsDisabled = () => configurationDisabled() ||
					optionValue.Update(orderManager.LobbyInfo.GlobalSettings).IsLocked;

				dropdown.OnMouseDown = _ =>
				{
					ScrollItemWidget SetupItem(KeyValuePair<string, string> c, ScrollItemWidget template)
					{
						bool IsSelected() => optionValue.Update(orderManager.LobbyInfo.GlobalSettings).Value == c.Key;
						void OnClick() => orderManager.IssueOrder(Order.Command($"option {option.Id} {c.Key}"));

						var item = ScrollItemWidget.Setup(template, IsSelected, OnClick);
						var displayValue = FluentProvider.TryGetMessage(c.Value, out var msg) ? msg : c.Value;
						item.Get<LabelWidget>("LABEL").GetText = () => displayValue;
						return item;
					}

					dropdown.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", option.Values.Count * 30, option.Values, SetupItem);
				};

				var label = row.GetOrNull<LabelWidget>(dropdown.Id + "_DESC");
				if (label != null)
				{
					var dropdownName = option.Name;
					if (FluentProvider.TryGetMessage(option.Name, out var fluentName))
						dropdownName = fluentName;
					label.GetText = () => dropdownName + ":";
					label.IsVisible = () => true;
				}
			}
		}

		void UpdateSummary(LobbyOption[] allOptions)
		{
			if (summaryLabel == null)
				return;

			var nonDefaults = new List<string>();
			foreach (var option in allOptions)
			{
				if (HiddenOptionIds.Contains(option.Id))
					continue;

				if (!orderManager.LobbyInfo.GlobalSettings.LobbyOptions.TryGetValue(option.Id, out var state))
					continue;

				if (state.Value != option.DefaultValue)
				{
					var name = option.Name;
					if (FluentProvider.TryGetMessage(option.Name, out var fluentName))
						name = fluentName;

					if (option is LobbyBooleanOption)
					{
						var enabled = state.Value == "True";
						nonDefaults.Add($"{name} {(enabled ? "ON" : "OFF")}");
					}
					else if (option.Values.TryGetValue(state.Value, out var valueLabel))
					{
						if (FluentProvider.TryGetMessage(valueLabel, out var fluentValue))
							valueLabel = fluentValue;
						nonDefaults.Add($"{name} {valueLabel}");
					}
					else
						nonDefaults.Add($"{name} {state.Value}");
				}
			}

			if (nonDefaults.Count == 0)
				summaryLabel.GetText = () => "Settings: All default";
			else
			{
				var summary = string.Join(" · ", nonDefaults);
				summaryLabel.GetText = () => summary;
			}
		}
	}
}
