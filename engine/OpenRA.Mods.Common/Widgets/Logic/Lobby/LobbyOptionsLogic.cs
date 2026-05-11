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

		// Visual treatment for placeholder options (LobbyOption.Placeholder=true).
		// Matches the neutral gray used elsewhere in the dim/disabled UI palette.
		static readonly Color PlaceholderTextColor = Color.FromArgb(0xad, 0xb5, 0xbd);
		const string PlaceholderTooltipSuffix = "Not yet implemented — visual placeholder for a future feature.";

		// Amber accent for ADVANCED section headers whose options are all placeholder.
		// Tells the user "this whole group does nothing yet" once, instead of cluttering every row.
		static readonly Color PlaceholderSectionColor = Color.FromArgb(0xc2, 0x41, 0x0c);
		const string PlaceholderSectionSuffix = "  —  placeholders, not yet wired";

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

		// Each instance of this logic is bound to one category — Common or Advanced —
		// declared via a hidden Label@CATEGORY_FILTER inside the panel widget.
		// Defaults to Advanced if no marker is found, so existing callers keep working.
		readonly string category;

		// Accordion state per section name. Initialised so the two big placeholder sections
		// default to collapsed — the user only opens them when they want to look at the soup.
		readonly Dictionary<string, bool> collapsedSections = new()
		{
			{ SectionUnitAvailability, true },
			{ SectionCombatTuning, true },
		};

		// WW3MOD: options are split into two top-level groups.
		// Common — frequently-changed, fully-working options. (Step 3 will move these onto the PLAYERS panel.)
		// Advanced — everything else. Mostly placeholder dummies plus developer toggles.
		const string CategoryCommon = "Common";
		const string CategoryAdvanced = "Advanced";

		static readonly HashSet<string> CommonOptionIds = new()
		{
			// Economy basics
			"startingcash", "passiveincome", "incomemodifier",
			// Map visibility
			"explored", "fog", "separateteamspawns",
			// Rule basics
			"gamespeed", "timelimit", "startingunits",
			// Player-level
			"bounty",
		};

		// Options never shown in the lobby (deliberately removed from WW3MOD).
		static readonly HashSet<string> HiddenOptionIds = new()
		{
			"shortgame", "crates", "creeps", "buildradius", "allybuild", "techlevel"
		};

		// Section grouping within the ADVANCED tab. Sections render in the declared order.
		// Any option not listed here ends up in the implicit "Other" section at the bottom.
		const string SectionUnitAvailability = "Unit Availability";
		const string SectionCombatTuning = "Combat Tuning";
		const string SectionGameRules = "Game Rules";
		const string SectionDeveloper = "Developer";

		static readonly string[] AdvancedSectionOrder =
		{
			SectionUnitAvailability,
			SectionCombatTuning,
			SectionGameRules,
			SectionDeveloper,
		};

		static readonly Dictionary<string, string> OptionSection = new()
		{
			// Unit Availability — every "unit-*" option from LobbyDummyOptions
			{ "unit-conscripts", SectionUnitAvailability },
			{ "unit-riflemen", SectionUnitAvailability },
			{ "unit-grenadiers", SectionUnitAvailability },
			{ "unit-snipers", SectionUnitAvailability },
			{ "unit-antitank", SectionUnitAvailability },
			{ "unit-manpads", SectionUnitAvailability },
			{ "unit-specops", SectionUnitAvailability },
			{ "unit-flamethrower", SectionUnitAvailability },
			{ "unit-support-inf", SectionUnitAvailability },
			{ "unit-drone-ops", SectionUnitAvailability },
			{ "unit-light-vehicles", SectionUnitAvailability },
			{ "unit-apcs", SectionUnitAvailability },
			{ "unit-ifvs", SectionUnitAvailability },
			{ "unit-mbts", SectionUnitAvailability },
			{ "unit-artillery", SectionUnitAvailability },
			{ "unit-mlrs", SectionUnitAvailability },
			{ "unit-shorad", SectionUnitAvailability },
			{ "unit-tactical-missiles", SectionUnitAvailability },
			{ "unit-thermobaric", SectionUnitAvailability },
			{ "unit-transport-heli", SectionUnitAvailability },
			{ "unit-scout-heli", SectionUnitAvailability },
			{ "unit-attack-heli", SectionUnitAvailability },
			{ "unit-ground-attack", SectionUnitAvailability },
			{ "unit-fighters", SectionUnitAvailability },

			// Combat Tuning — all dummy global tuning knobs
			{ "weapon-range", SectionCombatTuning },
			{ "damage-scale", SectionCombatTuning },
			{ "suppression", SectionCombatTuning },
			{ "veterancy-rate", SectionCombatTuning },
			{ "build-speed", SectionCombatTuning },
			{ "supply-capacity", SectionCombatTuning },
			{ "sight-range", SectionCombatTuning },

			// Game Rules
			{ "friendly-fire", SectionGameRules },
			{ "bounty-percent", SectionGameRules },
			{ "powers-enabled", SectionGameRules },

			// Developer
			{ "cheats", SectionDeveloper },
			{ "sync", SectionDeveloper },
		};

		static string GetCategory(LobbyOption option)
		{
			return CommonOptionIds.Contains(option.Id) ? CategoryCommon : CategoryAdvanced;
		}

		static string GetSection(LobbyOption option)
		{
			return OptionSection.TryGetValue(option.Id, out var section) ? section : "Other";
		}

		static (string Title, string Desc) ResolveTooltip(LobbyOption option)
		{
			var title = string.Empty;
			var desc = string.Empty;

			if (option.Description != null)
			{
				var d = option.Description;
				if (FluentProvider.TryGetMessage(option.Description, out var fluentDesc))
					d = fluentDesc;
				(title, desc) = LobbyUtils.SplitOnFirstToken(d);
			}

			if (option.Placeholder)
			{
				if (string.IsNullOrEmpty(title))
					title = option.Name;
				desc = string.IsNullOrEmpty(desc) ? PlaceholderTooltipSuffix : desc + "\n\n" + PlaceholderTooltipSuffix;
			}

			return (title, desc);
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

			// Read this panel's category from the hidden CATEGORY_FILTER label.
			// PLAYERS panel embeds one with "Common", Options-Bin uses "Advanced".
			var categoryLabel = widget.GetOrNull<LabelWidget>("CATEGORY_FILTER");
			category = categoryLabel?.Text ?? CategoryAdvanced;

			mapPreview = getMap();
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

		void AddSectionHeader(string text, bool allPlaceholder = false, string section = null)
		{
			if (sectionHeaderTemplate == null)
				return;

			var header = sectionHeaderTemplate.Clone();
			header.Bounds.Y = optionsContainer.Bounds.Height;
			header.IsVisible = () => true;
			optionsContainer.Bounds.Height += header.Bounds.Height;

			var label = header.GetOrNull<LabelWidget>("HEADER_LABEL");
			if (label != null)
			{
				var collapsed = section != null && collapsedSections.TryGetValue(section, out var c) && c;
				var glyph = section != null ? (collapsed ? "[+] " : "[-] ") : string.Empty;
				var displayText = glyph + (allPlaceholder ? text.ToUpperInvariant() + PlaceholderSectionSuffix : text.ToUpperInvariant());
				label.GetText = () => displayText;
				if (allPlaceholder)
					label.GetColor = () => PlaceholderSectionColor;
			}

			// Toggle button overlay — invisible background, full-width click target.
			// Clicking re-runs RebuildOptions which re-evaluates collapsedSections.
			if (section != null)
			{
				var toggle = header.GetOrNull<ButtonWidget>("TOGGLE");
				if (toggle != null)
				{
					var captured = section;
					toggle.OnClick = () =>
					{
						collapsedSections[captured] = !(collapsedSections.TryGetValue(captured, out var was) && was);
						RebuildOptions();
					};
				}
			}

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

			var filteredOptions = allOptions
				.Where(o => !HiddenOptionIds.Contains(o.Id))
				.Where(o => GetCategory(o) == category)
				.ToArray();

			if (category == CategoryAdvanced)
				RenderAdvancedSections(filteredOptions);
			else
				RenderFlatOptions(filteredOptions);

			panel.ContentHeight = yMargin + optionsContainer.Bounds.Height;
			optionsContainer.Bounds.Y = yMargin;

			panel.ScrollToTop();
		}

		void RenderAdvancedSections(LobbyOption[] options)
		{
			foreach (var section in AdvancedSectionOrder)
			{
				var sectionOptions = options.Where(o => GetSection(o) == section).ToArray();
				if (sectionOptions.Length == 0)
					continue;

				AddSectionHeader(section, sectionOptions.All(o => o.Placeholder), section);
				if (collapsedSections.TryGetValue(section, out var collapsed) && collapsed)
					continue;
				RenderFlatOptions(sectionOptions);
			}

			// Any options that didn't map to a known section render under "Other".
			var declared = new HashSet<string>(AdvancedSectionOrder);
			var unsectioned = options.Where(o => !declared.Contains(GetSection(o))).ToArray();
			if (unsectioned.Length > 0)
			{
				const string other = "Other";
				AddSectionHeader(other, unsectioned.All(o => o.Placeholder), other);
				if (!(collapsedSections.TryGetValue(other, out var oc) && oc))
					RenderFlatOptions(unsectioned);
			}
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

				var (cbText, cbDesc) = ResolveTooltip(option);
				checkbox.GetTooltipText = () => cbText;
				checkbox.GetTooltipDesc = () => cbDesc;

				if (option.Placeholder)
					checkbox.GetColor = () => PlaceholderTextColor;

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

				var (ddText, ddDesc) = ResolveTooltip(option);
				dropdown.GetTooltipText = () => ddText;
				dropdown.GetTooltipDesc = () => ddDesc;

				if (option.Placeholder)
					dropdown.GetColor = () => PlaceholderTextColor;

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
					if (option.Placeholder)
						label.GetColor = () => PlaceholderTextColor;
				}
			}
		}

	}
}
