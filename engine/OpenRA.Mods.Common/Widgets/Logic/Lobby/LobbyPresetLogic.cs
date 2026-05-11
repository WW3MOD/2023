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
using System.IO;
using System.Linq;
using OpenRA.FileSystem;
using OpenRA.Network;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	// WW3MOD: save/load named bundles of lobby options to a YAML file in the support dir.
	// Built-in entries (Default, Last game) are always present at the top of the dropdown.
	public class LobbyPresetLogic : ChromeLogic
	{
		const string PresetsFileName = "lobby-presets.yaml";
		const string DefaultPresetName = "Default";

		readonly OrderManager orderManager;
		readonly Func<MapPreview> getMap;
		readonly Func<bool> configurationDisabled;
		readonly TextFieldWidget nameField;
		readonly DropDownButtonWidget dropdown;
		readonly ButtonWidget saveButton;
		readonly ButtonWidget deleteButton;
		readonly ButtonWidget resetButton;

		// One preset → its options snapshot (option-id → value) AND its bot snapshot
		// (slot-key → bot type). Bot faction/team are intentionally not captured yet:
		// re-applying them requires knowing the bot's client index after slot_bot
		// finishes server-side, which we can't predict without a deferred-order helper.
		// Add when user demand outweighs the implementation cost.
		sealed class Preset
		{
			public Dictionary<string, string> Options { get; } = new();
			public Dictionary<string, string> Bots { get; } = new();
		}

		readonly Dictionary<string, Preset> presets = new();

		[ObjectCreator.UseCtor]
		internal LobbyPresetLogic(Widget widget, OrderManager orderManager, Func<MapPreview> getMap, Func<bool> configurationDisabled)
		{
			this.orderManager = orderManager;
			this.getMap = getMap;
			this.configurationDisabled = configurationDisabled;

			nameField = widget.Get<TextFieldWidget>("PRESET_NAME");
			dropdown = widget.Get<DropDownButtonWidget>("PRESET_DROPDOWN");
			saveButton = widget.Get<ButtonWidget>("SAVE_PRESET_BUTTON");
			deleteButton = widget.Get<ButtonWidget>("DELETE_PRESET_BUTTON");
			resetButton = widget.Get<ButtonWidget>("RESET_PRESET_BUTTON");

			LoadPresets();

			nameField.Text = DefaultPresetName;
			nameField.IsDisabled = () => configurationDisabled();
			// Pressing Enter inside the name field is the same as clicking Save.
			nameField.OnEnterKey = _ => { SaveCurrent(); return true; };

			dropdown.IsDisabled = () => configurationDisabled();
			dropdown.OnMouseDown = _ => ShowDropdown();

			saveButton.IsDisabled = () => configurationDisabled() || string.IsNullOrWhiteSpace(nameField.Text);
			saveButton.OnClick = SaveCurrent;

			// Delete only enables when the typed name matches a saved preset (not Default).
			deleteButton.IsDisabled = () => configurationDisabled() || !presets.ContainsKey((nameField.Text ?? string.Empty).Trim());
			deleteButton.OnClick = DeleteCurrent;

			resetButton.IsDisabled = () => configurationDisabled();
			resetButton.OnClick = ResetToDefault;
		}

		static string PresetsPath => Path.Combine(Platform.SupportDir, PresetsFileName);

		void LoadPresets()
		{
			presets.Clear();
			try
			{
				if (!File.Exists(PresetsPath))
					return;

				var root = MiniYaml.FromFile(PresetsPath, false);
				var presetsNode = root.FirstOrDefault(n => n.Key == "Presets");
				if (presetsNode == null)
					return;

				foreach (var entry in presetsNode.Value.Nodes)
				{
					var preset = new Preset();

					var optionsNode = entry.Value.Nodes.FirstOrDefault(n => n.Key == "Options");
					if (optionsNode != null)
						foreach (var kv in optionsNode.Value.Nodes)
							preset.Options[kv.Key] = kv.Value.Value ?? string.Empty;

					var botsNode = entry.Value.Nodes.FirstOrDefault(n => n.Key == "Bots");
					if (botsNode != null)
						foreach (var kv in botsNode.Value.Nodes)
							preset.Bots[kv.Key] = kv.Value.Value ?? string.Empty;

					presets[entry.Key] = preset;
				}
			}
			catch (Exception e)
			{
				Log.Write("debug", $"LobbyPresetLogic: failed to load presets: {e.Message}");
			}
		}

		void WritePresets()
		{
			try
			{
				var lines = new List<string> { "Presets:" };
				foreach (var preset in presets.OrderBy(p => p.Key))
				{
					lines.Add($"\t{preset.Key}:");
					lines.Add($"\t\tCreated: {DateTime.UtcNow:yyyy-MM-dd}");
					lines.Add("\t\tOptions:");
					foreach (var opt in preset.Value.Options.OrderBy(o => o.Key))
						lines.Add($"\t\t\t{opt.Key}: {opt.Value}");
					if (preset.Value.Bots.Count > 0)
					{
						lines.Add("\t\tBots:");
						foreach (var bot in preset.Value.Bots.OrderBy(b => b.Key))
							lines.Add($"\t\t\t{bot.Key}: {bot.Value}");
					}
				}

				Directory.CreateDirectory(Platform.SupportDir);
				File.WriteAllLines(PresetsPath, lines);
			}
			catch (Exception e)
			{
				Log.Write("debug", $"LobbyPresetLogic: failed to write presets: {e.Message}");
			}
		}

		void ShowDropdown()
		{
			var entries = new List<string> { DefaultPresetName };
			entries.AddRange(presets.Keys.OrderBy(k => k));

			ScrollItemWidget Setup(string name, ScrollItemWidget template)
			{
				bool IsSelected() => (nameField.Text ?? string.Empty).Trim() == name;
				void OnClick() => ApplyPreset(name);
				var item = ScrollItemWidget.Setup(template, IsSelected, OnClick);
				var label = item.Get<LabelWidget>("LABEL");
				label.GetText = () => name;
				return item;
			}

			dropdown.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", Math.Min(300, entries.Count * 25), entries, Setup);
		}

		void ApplyPreset(string name)
		{
			nameField.Text = name;
			if (name == DefaultPresetName)
			{
				ResetToDefault();
				return;
			}

			if (!presets.TryGetValue(name, out var preset))
				return;

			ApplyBots(preset.Bots);

			var opts = preset.Options;

			var map = getMap();
			if (map == null || map.WorldActorInfo == null)
				return;

			// Anything currently non-default that isn't part of the preset → reset to default.
			// Anything in the preset → apply.
			var allOptions = map.PlayerActorInfo.TraitInfos<ILobbyOptions>()
				.Concat(map.WorldActorInfo.TraitInfos<ILobbyOptions>())
				.SelectMany(t => t.LobbyOptions(map))
				.ToDictionary(o => o.Id, o => o);

			foreach (var id in opts.Keys.Concat(allOptions.Keys).Distinct())
			{
				if (!allOptions.TryGetValue(id, out var opt))
					continue;
				var target = opts.TryGetValue(id, out var v) ? v : opt.DefaultValue;
				if (!orderManager.LobbyInfo.GlobalSettings.LobbyOptions.TryGetValue(id, out var state))
					continue;
				if (state.Value == target)
					continue;
				orderManager.IssueOrder(Order.Command($"option {id} {target}"));
			}
		}

		void ResetToDefault()
		{
			nameField.Text = DefaultPresetName;
			var map = getMap();
			if (map == null || map.WorldActorInfo == null)
				return;

			// Defaults = no bots and all options at their declared default.
			ApplyBots(new Dictionary<string, string>());

			var allOptions = map.PlayerActorInfo.TraitInfos<ILobbyOptions>()
				.Concat(map.WorldActorInfo.TraitInfos<ILobbyOptions>())
				.SelectMany(t => t.LobbyOptions(map));

			foreach (var opt in allOptions)
			{
				if (!orderManager.LobbyInfo.GlobalSettings.LobbyOptions.TryGetValue(opt.Id, out var state))
					continue;
				if (state.Value == opt.DefaultValue)
					continue;
				orderManager.IssueOrder(Order.Command($"option {opt.Id} {opt.DefaultValue}"));
			}
		}

		// Reconciles the lobby's bot slots against `target` (slot-key → bot-type).
		// Issues slot_open for any bot in a slot not present in target, then slot_bot
		// for any target slot that isn't already running the requested bot type.
		// Faction/team/color are intentionally not restored here — those need a
		// deferred-order helper that knows the bot's client index post-creation.
		void ApplyBots(IReadOnlyDictionary<string, string> target)
		{
			var botController = orderManager.LobbyInfo.Clients.FirstOrDefault(c => c.IsAdmin);
			if (botController == null)
				return;

			foreach (var slot in orderManager.LobbyInfo.Slots)
			{
				var current = orderManager.LobbyInfo.ClientInSlot(slot.Key);
				var wantBot = target.TryGetValue(slot.Key, out var wantedType) ? wantedType : null;
				var hasBot = current?.Bot != null;

				if (hasBot && wantBot == null)
				{
					orderManager.IssueOrder(Order.Command("slot_open " + slot.Value.PlayerReference));
				}
				else if (wantBot != null && slot.Value.AllowBots && (!hasBot || current.Bot != wantBot))
				{
					orderManager.IssueOrder(Order.Command($"slot_bot {slot.Key} {botController.Index} {wantBot}"));
				}
			}
		}

		void SaveCurrent()
		{
			var name = (nameField.Text ?? string.Empty).Trim();
			if (string.IsNullOrEmpty(name) || name == DefaultPresetName)
				return;

			var map = getMap();
			if (map == null || map.WorldActorInfo == null)
				return;

			var preset = new Preset();
			var allOptions = map.PlayerActorInfo.TraitInfos<ILobbyOptions>()
				.Concat(map.WorldActorInfo.TraitInfos<ILobbyOptions>())
				.SelectMany(t => t.LobbyOptions(map));

			foreach (var opt in allOptions)
			{
				if (!orderManager.LobbyInfo.GlobalSettings.LobbyOptions.TryGetValue(opt.Id, out var state))
					continue;
				if (state.Value != opt.DefaultValue)
					preset.Options[opt.Id] = state.Value;
			}

			foreach (var slot in orderManager.LobbyInfo.Slots)
			{
				var c = orderManager.LobbyInfo.ClientInSlot(slot.Key);
				if (c?.Bot != null)
					preset.Bots[slot.Key] = c.Bot;
			}

			presets[name] = preset;
			WritePresets();
		}

		void DeleteCurrent()
		{
			var name = (nameField.Text ?? string.Empty).Trim();
			if (string.IsNullOrEmpty(name) || name == DefaultPresetName)
				return;
			if (!presets.Remove(name))
				return;
			WritePresets();
			nameField.Text = DefaultPresetName;
		}
	}
}
