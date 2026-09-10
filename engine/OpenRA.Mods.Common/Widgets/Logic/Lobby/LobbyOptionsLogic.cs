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
	public class LobbyOptionsLogic : ChromeLogic
	{
		[FluentReference]
		const string NotAvailable = "label-not-available";

		// Visual treatment for placeholder options (LobbyOption.Placeholder=true).
		// ink-2 — the neutral gray used elsewhere in the dim/disabled UI palette.
		static readonly Color PlaceholderTextColor = Color.FromArgb(0x96, 0x96, 0x96);
		const string PlaceholderTooltipSuffix = "Not yet implemented — visual placeholder for a future feature.";

		// The same fact as PlaceholderTooltipSuffix, said where it does not need a hover: on the
		// row itself, and on the header of a section that is placeholder all the way down.
		const string PlaceholderLabelSuffix = "  (not wired)";
		const string PlaceholderSectionSuffix = "   — NOT YET WIRED";

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

		// Each instance of this logic is bound to one category — Common, Advanced or
		// All — declared via a hidden Label@CATEGORY_FILTER inside the panel widget.
		// Defaults to Advanced if no marker is found, so existing callers keep working.
		readonly string category;

		// Set when Test.HoverLobbyOption names an option rendered by this panel.
		CheckboxWidget pendingHover;

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
		const string CategoryAll = "All";

		// Shared single source of truth: LobbyActiveChangesLogic consumes these sets
		// too (chip filtering). Keep them here — a duplicated copy over there once
		// drifted (missing `cheats`) and sent chip clicks to the wrong tab.
		// WHAT THIS SET MEANS NOW. It once chose a TAB, back when the lobby had a Match/Advanced
		// strip; that strip is gone (LobbyLogic.cs:599-605 hides it unconditionally) and the
		// pre-game panel renders category All, so membership no longer changes what a host sees
		// there. What it still decides is the IN-GAME Game Info options tab, which is pinned to
		// CATEGORY_FILTER: Common (ww3mod|chrome/ingame-info-lobby-options.yaml:30).
		//
		// So the line is now "a real option a player may need to look up mid-match" vs "a dummy
		// that governs nothing". Everything that ships is Common; what stays Advanced is exactly
		// the LobbyDummyOptions placeholders, which have no gameplay hook to report on. This is
		// what makes DEFCON mode, the nuclear ceiling and Doomsday visible during a match —
		// previously every DEFCON id was Advanced, so a player could not check which mode they
		// were in without leaving the game.
		internal static readonly HashSet<string> CommonOptionIds = new()
		{
			// Economy basics
			"startingcash", "passiveincome", "incomemodifier",
			// Map visibility
			"explored", "fog", "separateteamspawns",
			// Rule basics
			"gamespeed", "timelimit", "startingunits", "forwarddeployment",
			// Player-level
			"bounty",
			// Debug Menu is the one developer-flagged option that's useful to skirmish
			// players, so we surface it in the Match panel directly.
			"cheats",
			// Both sides pay the cost of sync reports, so both sides agree on it here.
			Session.SyncReportsOptionId,
			// How the match ends, and what it ends with.
			DoomsdayStrikeInfo.DoomsdayOptionId,
			// The DEFCON feature, all four dropdowns of it.
			DefconEscalationInfo.ModeOptionId,
			DefconEscalationInfo.StartOptionId,
			DefconEscalationInfo.PaceOptionId,
			DefconEscalationInfo.CeilingOptionId,
			// Which weapons this match permits — the question most worth being able to
			// re-read once the shooting starts.
			"tactical-nuke", "high-yield-nuke", "nuclear-arsenal", "powers-sandbox",
			// And WHEN they come up for sale. Both belong here for the same reason as the line
			// above and rather more sharply: a player who has just been told a tier is not
			// purchasable yet wants to check how long the wait is without leaving the match.
			NuclearUnlockClockInfo.IntervalOptionId,
			NuclearUnlockClockInfo.HighestYieldOptionId,
		};

		// Options never shown in the lobby (deliberately removed from WW3MOD).
		//
		// THIS SET HIDES; IT DOES NOT UNREGISTER. Both consumers — the options panel at
		// RenderOptions below and the change chips in LobbyActiveChangesLogic — filter for
		// DISPLAY only. LobbyCommands.LoadMapSettings still registers every id here with its
		// shipped default, so nothing keyed to one of these ids changes behaviour by being listed.
		//
		// `powers-enabled` is hidden by user ruling as redundant: it is a LobbyDummyOptions
		// placeholder (LobbyDummyOptions.cs:217-219, stamped Placeholder=true at :37) with NO
		// consumer anywhere in engine or mod — nothing grants a condition or a prerequisite from
		// it. It advertised itself as a master switch over the weapon gates that actually work,
		// which is the one thing a dead control must not do. `friendly-fire` is the other
		// placeholder that still renders and it deliberately STAYS.
		internal static readonly HashSet<string> HiddenOptionIds = new()
		{
			"shortgame", "crates", "creeps", "buildradius", "allybuild", "techlevel", "powers-enabled"
		};

		// ONE section list, shared by every category. Sections render in the declared order and
		// each is named for the QUESTION a host is answering, not for the trait that happens to
		// own the options in it — which is why the four DEFCON dropdowns are together (they were
		// split across "Game Rules" and "Other"), and why the Doomsday Clock and the Doomsday
		// checkbox are together (they are one feature: the dropdown sets WHEN the match ends,
		// the checkbox sets WHAT HAPPENS then, and the checkbox does nothing at all while the
		// clock reads "No limit").
		//
		// Any option not listed in OptionSection still ends up in the implicit "Other" section
		// at the bottom — that fallback is a safety net, not a home. It is how `nuclear-ceiling`
		// came to be stranded there alone.
		const string SectionMatch = "Match";
		const string SectionEscalation = "Escalation";
		const string SectionArsenal = "Arsenal";
		const string SectionEconomy = "Economy";
		const string SectionBattlefield = "Battlefield";
		const string SectionDeveloper = "Developer";
		const string SectionUnitAvailability = "Unit Availability";
		const string SectionCombatTuning = "Combat Tuning";

		static readonly string[] SectionOrder =
		{
			SectionMatch,
			SectionEscalation,
			SectionArsenal,
			SectionEconomy,
			SectionBattlefield,
			SectionDeveloper,
			SectionUnitAvailability,
			SectionCombatTuning,
		};

		// Sections to hide outright when every option in them is a placeholder. These two are
		// the dummy soup — 24 unit toggles and 7 tuning knobs that govern nothing — and a
		// header full of dimmed rows is worse than no header.
		//
		// Escalation is deliberately NOT in this set even though it is entirely placeholder
		// today: it is the spine of a real shipping feature, a host still picks a Game Mode
		// with it, and hiding it would answer "what mode am I playing?" with silence. It
		// renders with a "not yet wired" suffix on the header instead.
		static readonly HashSet<string> SuppressWhenAllPlaceholder = new()
		{
			SectionUnitAvailability,
			SectionCombatTuning,
		};

		static readonly Dictionary<string, string> OptionSection = new()
		{
			// Match — how long the match runs, and how it ends.
			{ "gamespeed", SectionMatch },
			{ "timelimit", SectionMatch },
			{ DoomsdayStrikeInfo.DoomsdayOptionId, SectionMatch },

			// Escalation — the DEFCON feature. All four dropdowns live on one trait
			// (DefconEscalation); the ceiling used to be missing from this map entirely.
			{ DefconEscalationInfo.ModeOptionId, SectionEscalation },
			{ DefconEscalationInfo.StartOptionId, SectionEscalation },
			{ DefconEscalationInfo.PaceOptionId, SectionEscalation },
			{ DefconEscalationInfo.CeilingOptionId, SectionEscalation },

			// Arsenal — which weapons this match permits, in ascending yield. TWO of these four
			// now render: `nuclear-arsenal` is hidden at the trait (world.yaml,
			// NuclearArsenalCheckboxVisible: False) and `powers-enabled` is hidden by id in
			// HiddenOptionIds above, so a host sees the tactical and high-yield gates only.
			// Both mappings are kept rather than deleted: they cost nothing, and they are what
			// puts either option back in the right section if it is ever un-hidden — an option
			// with no entry here lands in the implicit "Other" bucket at the bottom instead.
			{ "tactical-nuke", SectionArsenal },
			{ "high-yield-nuke", SectionArsenal },
			{ "nuclear-arsenal", SectionArsenal },
			{ "powers-enabled", SectionArsenal },

			// The Skirmish unlock clock. In Arsenal rather than Escalation or Match because it
			// answers the same question the four above do — what this match will let a player have
			// — and because it is meaningless in Escalation, where nothing is purchasable at all.
			// These two are NOT placeholders: they govern behaviour from the moment the trait is
			// registered, so they render live where the DEFCON four render dimmed.
			{ NuclearUnlockClockInfo.IntervalOptionId, SectionArsenal },
			{ NuclearUnlockClockInfo.HighestYieldOptionId, SectionArsenal },

			// Economy — the budget you fight the war on.
			{ "startingcash", SectionEconomy },
			{ "incomemodifier", SectionEconomy },
			{ "passiveincome", SectionEconomy },
			{ "bounty", SectionEconomy },

			// Battlefield — what is on the map at t=0, and what you can see of it.
			{ "startingunits", SectionBattlefield },
			{ "forwarddeployment", SectionBattlefield },
			{ "fog", SectionBattlefield },
			{ "explored", SectionBattlefield },
			{ "separateteamspawns", SectionBattlefield },
			{ "friendly-fire", SectionBattlefield },

			// Developer — off the path a host reads to set up a match.
			{ "cheats", SectionDeveloper },
			{ Session.SyncReportsOptionId, SectionDeveloper },
			{ "powers-sandbox", SectionDeveloper },

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

				// SplitDescription, not SplitOnFirstToken: MiniYaml does not unescape, so a
				// description authored in YAML as "title\nbody" arrives carrying the literal
				// two-character escape and a real-newline search never matches it.
				(title, desc) = LobbyUtils.SplitDescription(d);

				// A single-line description used to render as a bold title with an EMPTY body —
				// i.e. the tooltip restated the label and explained nothing. Most options in the
				// mod are authored that way, so fall back to naming the option in the title and
				// letting the whole description be the body. An author who wants a custom title
				// still gets it by putting a newline in.
				if (string.IsNullOrEmpty(desc))
				{
					title = FluentProvider.TryGetMessage(option.Name, out var fluentName) ? fluentName : option.Name;
					desc = d;
				}
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
			// Two panels declare one: lobby-players.yaml:880 reads "All", and
			// ww3mod's ingame-info-lobby-options.yaml reads "Common". NO panel
			// declares "Advanced" — it exists only as the fallback below, which is
			// why a panel that forgets the label renders Advanced-only.
			var categoryLabel = widget.GetOrNull<LabelWidget>("CATEGORY_FILTER");
			category = categoryLabel?.Text ?? CategoryAdvanced;

			mapPreview = getMap();
			RebuildOptions();
		}

		public override void Tick()
		{
			// Deferred to Tick: RenderBounds is only meaningful after a layout pass,
			// and the tooltip is positioned from the cursor, not from the widget.
			if (pendingHover != null && pendingHover.RenderBounds.Width > 0)
			{
				var bounds = pendingHover.RenderBounds;
				Viewport.LastMousePos = new int2(bounds.X + bounds.Width / 4, bounds.Y + bounds.Height / 2);
				pendingHover.MouseEntered();
				pendingHover = null;
			}

			var newMapPreview = getMap();
			if (newMapPreview == mapPreview)
				return;

			Game.RunAfterTick(() =>
			{
				mapPreview = newMapPreview;
				RebuildOptions();
			});
		}

		void AddSectionHeader(string text, string section = null, bool allPlaceholder = false)
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
				// Cleaner expand/collapse glyphs — ▸ collapsed, ▾ expanded — read
				// as triangles instead of the [+]/[-] ASCII brackets which look
				// like console output.
				var glyph = section != null ? (collapsed ? "▸  " : "▾  ") : string.Empty;
				var displayText = glyph + text.ToUpperInvariant() + (allPlaceholder ? PlaceholderSectionSuffix : string.Empty);
				label.GetText = () => displayText;
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
						RebuildOptions(false);
					};
				}
			}

			optionsContainer.AddChild(header);
		}

		// resetScroll: snap back to the top of the panel. Right when the OPTION SET
		// changed under the user (new map, first build). Wrong when the user
		// themselves collapsed a section — since the lobby left column became one
		// scroll, this panel also holds the map preview, so a reset there yanks
		// them up past the whole preview because they clicked a header.
		void RebuildOptions(bool resetScroll = true)
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

			var visibleOptions = allOptions
				.Where(o => !HiddenOptionIds.Contains(o.Id))
				.ToArray();

			// One section list for every category. The category used to decide whether headers
			// were drawn AT ALL — All rendered the Common half header-less and the Advanced half
			// with headers — which is what made this panel read as an unlabelled list that
			// suddenly grows headings two-thirds of the way down.
			var renderOptions = category == CategoryAll
				? visibleOptions
				: visibleOptions.Where(o => GetCategory(o) == category).ToArray();

			RenderSections(renderOptions);

			panel.ContentHeight = yMargin + optionsContainer.Bounds.Height;
			optionsContainer.Bounds.Y = yMargin;
			if (resetScroll)
				panel.ScrollToTop();
			else
				panel.ClampScroll();
		}

		void RenderSections(LobbyOption[] options)
		{
			foreach (var section in SectionOrder)
			{
				var sectionOptions = options.Where(o => GetSection(o) == section).ToArray();
				if (sectionOptions.Length == 0)
					continue;

				// A section of nothing but dummy options is visual noise — the placeholder
				// treatment shouts louder than any working option. That applies to the two
				// soup sections only; see SuppressWhenAllPlaceholder for why Escalation is
				// shown-and-labelled rather than hidden.
				var allPlaceholder = sectionOptions.All(o => o.Placeholder);
				if (allPlaceholder && SuppressWhenAllPlaceholder.Contains(section))
					continue;

				AddSectionHeader(section, section, allPlaceholder);
				if (collapsedSections.TryGetValue(section, out var collapsed) && collapsed)
					continue;

				RenderFlatOptions(sectionOptions);
			}

			// Any option that didn't map to a known section renders under "Other" rather than
			// being silently dropped. With the map complete this should be empty for the shipped
			// rules — a map-scoped option (river-zeta's `difficulty`) is the expected occupant.
			var declared = new HashSet<string>(SectionOrder);
			var unsectioned = options.Where(o => !declared.Contains(GetSection(o))).ToArray();
			if (unsectioned.Length > 0 && !unsectioned.All(o => o.Placeholder))
			{
				const string other = "Other";
				AddSectionHeader(other, other);
				if (!(collapsedSections.TryGetValue(other, out var oc) && oc))
					RenderFlatOptions(unsectioned);
			}
		}

		// Options render in DISPLAY ORDER, not "every checkbox, then every dropdown". The two
		// control types cannot share a row template, so a run of consecutive same-type options
		// becomes one or more rows and a change of type starts a fresh row. Before this,
		// DisplayOrder only sorted within each type — which is the mechanical reason the DEFCON
		// dropdowns drew BELOW the nuclear checkboxes in the old Game Rules section despite
		// sorting after them, and why the order looked arbitrary to anyone reading the panel.
		void RenderFlatOptions(LobbyOption[] options)
		{
			var start = 0;
			while (start < options.Length)
			{
				var isCheckbox = options[start] is LobbyBooleanOption;
				var end = start;
				while (end < options.Length && options[end] is LobbyBooleanOption == isCheckbox)
					end++;

				RenderRun(options[start..end]);
				start = end;
			}
		}

		void RenderRun(LobbyOption[] options)
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

				// Say it on the face of the panel, not only in a tooltip nobody hovers. A
				// placeholder row is dimmed (below) but its TICK still draws at full strength —
				// CheckboxWidget.Draw colours the label and the check independently — so a dimmed
				// row reads as "on, but locked by something" rather than "governs nothing".
				if (option.Placeholder)
					checkboxName += PlaceholderLabelSuffix;

				checkbox.GetText = () => checkboxName;

				var (cbText, cbDesc) = ResolveTooltip(option);
				checkbox.GetTooltipText = () => cbText;
				checkbox.GetTooltipDesc = () => cbDesc;

				if (TestMode.IsActive && option.Id == TestMode.HoverLobbyOption)
					pendingHover = checkbox;

				if (option.Placeholder)
					checkbox.GetColor = () => PlaceholderTextColor;

				checkbox.IsVisible = () => true;
				checkbox.IsChecked = () => optionEnabled.Update(orderManager.LobbyInfo.GlobalSettings);

				// A PLACEHOLDER ROW IS DIMMED AND MUST ALSO BE DEAD TO THE MOUSE. Dimming it was
				// only ever half the treatment: OnClick below issues a real `option <id> <state>`
				// order, and the server answers that by resetting EVERY client to NotReady and
				// posting a settings-changed chat line (LobbyCommands.cs) — a visible, disruptive
				// consequence for a control that governs nothing.
				checkbox.IsDisabled = () => option.Placeholder || configurationDisabled() || optionLocked.Update(orderManager.LobbyInfo.GlobalSettings);
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

				// Same rule as the checkbox above, and this is the site that is actually reachable
				// today: the four DEFCON dropdowns are placeholders in a section deliberately
				// exempt from SuppressWhenAllPlaceholder, so they render and were fully clickable.
				dropdown.IsDisabled = () => option.Placeholder || configurationDisabled() ||
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

					// Spacing pass: micro-labels render uppercase without the old
					// trailing colon (it was appended here, not in the source
					// strings; TrimEnd also covers any name that carries its own).
					var displayName = dropdownName.TrimEnd(':').ToUpperInvariant();
					label.GetText = () => displayName;
					label.IsVisible = () => true;
					if (option.Placeholder)
						label.GetColor = () => PlaceholderTextColor;
				}
			}
		}

	}
}
