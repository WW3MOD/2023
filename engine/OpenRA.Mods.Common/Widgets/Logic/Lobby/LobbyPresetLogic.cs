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
		const string LastGamePresetName = "Last game";

		// Static hook so LobbyLogic can snapshot the current state into "Last game" right
		// before a match starts, without holding a direct reference to this logic instance.
		// PITFALL: the chrome layer creates exactly one preset bar instance per lobby; if
		// that ever changes we'll need a List<Action> here instead.
		public static Action SnapshotLastGame;

		// Static hook callable from outside (e.g. LobbyUtils.SetupEmptySlotButtons).
		// Enqueues a faction order for a slot's bot that will be drained by Tick()
		// once the bot's client index appears in LobbyInfo.
		public static Action<string, string> EnqueueBotFaction;

		readonly OrderManager orderManager;
		readonly Func<MapPreview> getMap;
		readonly Func<bool> configurationDisabled;
		readonly TextFieldWidget nameField;
		readonly DropDownButtonWidget dropdown;
		readonly ButtonWidget saveButton;
		readonly ButtonWidget renameButton;
		readonly ButtonWidget deleteButton;
		readonly ButtonWidget resetButton;

		// Tracks which preset name was most recently *applied* — that's what Rename targets.
		// Save updates a preset under nameField.Text without changing this; Rename takes
		// the entry at activePresetName and re-keys it to nameField.Text.
		string activePresetName = DefaultPresetName;

		sealed class BotConfig
		{
			public string Type;
			public string Faction;
			public int Team;
		}

		sealed class Preset
		{
			public Dictionary<string, string> Options { get; } = new();
			public Dictionary<string, BotConfig> Bots { get; } = new();
		}

		readonly Dictionary<string, Preset> presets = new();

		// Faction/team orders need the bot's client index, which we don't have at the moment
		// we issue slot_bot. Queue them here and apply once the bot actually shows up in
		// LobbyInfo. The TicksLeft counter discards stale entries (e.g. invalid bot types)
		// so the queue can't grow forever. 90 ticks ≈ 3 seconds at default tick rate.
		sealed class PendingBotApply
		{
			public string SlotKey;
			public string Faction;
			public int Team;
			public int TicksLeft;
		}

		readonly List<PendingBotApply> pendingBotApplies = new();
		const int PendingBotApplyTtlTicks = 90;

		[ObjectCreator.UseCtor]
		internal LobbyPresetLogic(Widget widget, OrderManager orderManager, Func<MapPreview> getMap, Func<bool> configurationDisabled)
		{
			this.orderManager = orderManager;
			this.getMap = getMap;
			this.configurationDisabled = configurationDisabled;

			nameField = widget.Get<TextFieldWidget>("PRESET_NAME");
			dropdown = widget.Get<DropDownButtonWidget>("PRESET_DROPDOWN");
			saveButton = widget.Get<ButtonWidget>("SAVE_PRESET_BUTTON");
			renameButton = widget.Get<ButtonWidget>("RENAME_PRESET_BUTTON");
			deleteButton = widget.Get<ButtonWidget>("DELETE_PRESET_BUTTON");
			resetButton = widget.Get<ButtonWidget>("RESET_PRESET_BUTTON");

			LoadPresets();

			nameField.Text = DefaultPresetName;
			nameField.IsDisabled = () => configurationDisabled();
			// Pressing Enter inside the name field is the same as clicking Save.
			nameField.OnEnterKey = _ => { SaveCurrent(); return true; };

			dropdown.IsDisabled = () => configurationDisabled();
			dropdown.OnMouseDown = _ => ShowDropdown();

			saveButton.IsDisabled = SaveDisabled;
			saveButton.OnClick = SaveCurrent;

			renameButton.IsDisabled = RenameDisabled;
			renameButton.OnClick = RenameActive;

			// Delete only enables when the typed name matches a saved preset (reserved names excluded).
			deleteButton.IsDisabled = DeleteDisabled;
			deleteButton.OnClick = DeleteCurrent;

			resetButton.IsDisabled = () => configurationDisabled();
			resetButton.OnClick = ResetToDefault;

			SnapshotLastGame = () => SnapshotAs(LastGamePresetName);
			EnqueueBotFaction = (slotKey, faction) =>
			{
				pendingBotApplies.Add(new PendingBotApply
				{
					SlotKey = slotKey,
					Faction = faction,
					Team = 0,
					TicksLeft = PendingBotApplyTtlTicks,
				});
			};
		}

		bool IsReserved(string name) => name == DefaultPresetName || name == LastGamePresetName;

		bool SaveDisabled()
		{
			if (configurationDisabled()) return true;
			var n = (nameField.Text ?? string.Empty).Trim();
			return string.IsNullOrEmpty(n) || IsReserved(n);
		}

		bool DeleteDisabled()
		{
			if (configurationDisabled()) return true;
			var n = (nameField.Text ?? string.Empty).Trim();
			return string.IsNullOrEmpty(n) || IsReserved(n) || !presets.ContainsKey(n);
		}

		bool RenameDisabled()
		{
			if (configurationDisabled()) return true;
			if (IsReserved(activePresetName)) return true;
			var n = (nameField.Text ?? string.Empty).Trim();
			return string.IsNullOrEmpty(n) || IsReserved(n) || n == activePresetName || !presets.ContainsKey(activePresetName);
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
					{
						foreach (var kv in botsNode.Value.Nodes)
						{
							var cfg = new BotConfig();
							// Backwards-compatible shape: a scalar value is treated as Type-only.
							// e.g. "Multi1: hardbot" still works alongside the new sub-node form.
							if (kv.Value.Nodes.Length == 0)
								cfg.Type = kv.Value.Value ?? string.Empty;
							else
							{
								foreach (var sub in kv.Value.Nodes)
								{
									var v = sub.Value.Value ?? string.Empty;
									if (sub.Key == "Type") cfg.Type = v;
									else if (sub.Key == "Faction") cfg.Faction = v;
									else if (sub.Key == "Team" && int.TryParse(v, out var t)) cfg.Team = t;
								}
							}

							if (!string.IsNullOrEmpty(cfg.Type))
								preset.Bots[kv.Key] = cfg;
						}
					}

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
						{
							lines.Add($"\t\t\t{bot.Key}:");
							lines.Add($"\t\t\t\tType: {bot.Value.Type}");
							if (!string.IsNullOrEmpty(bot.Value.Faction))
								lines.Add($"\t\t\t\tFaction: {bot.Value.Faction}");
							if (bot.Value.Team > 0)
								lines.Add($"\t\t\t\tTeam: {bot.Value.Team}");
						}
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
			// Built-ins on top, then saved presets alphabetically.
			var entries = new List<string> { DefaultPresetName };
			if (presets.ContainsKey(LastGamePresetName))
				entries.Add(LastGamePresetName);
			entries.AddRange(presets.Keys.Where(k => k != LastGamePresetName).OrderBy(k => k));

			ScrollItemWidget Setup(string name, ScrollItemWidget template)
			{
				bool IsSelected() => activePresetName == name;
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
			activePresetName = name;
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
			activePresetName = DefaultPresetName;
			var map = getMap();
			if (map == null || map.WorldActorInfo == null)
				return;

			// Defaults = no bots and all options at their declared default.
			ApplyBots(new Dictionary<string, BotConfig>());

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

		// Reconciles the lobby's bot slots against `target` (slot-key → BotConfig).
		// Issues slot_open for any bot in a slot not present in target, slot_bot for any
		// target slot that isn't already running the requested bot type, then queues
		// faction/team orders to be issued once the bot's client index appears in
		// LobbyInfo (the order doesn't return that index, we have to look it up).
		void ApplyBots(IReadOnlyDictionary<string, BotConfig> target)
		{
			var botController = orderManager.LobbyInfo.Clients.FirstOrDefault(c => c.IsAdmin);
			if (botController == null)
				return;

			foreach (var slot in orderManager.LobbyInfo.Slots)
			{
				var current = orderManager.LobbyInfo.ClientInSlot(slot.Key);
				var wantBot = target.TryGetValue(slot.Key, out var cfg) ? cfg : null;
				var hasBot = current?.Bot != null;

				if (hasBot && wantBot == null)
				{
					orderManager.IssueOrder(Order.Command("slot_open " + slot.Value.PlayerReference));
				}
				else if (wantBot != null && slot.Value.AllowBots && (!hasBot || current.Bot != wantBot.Type))
				{
					orderManager.IssueOrder(Order.Command($"slot_bot {slot.Key} {botController.Index} {wantBot.Type}"));
				}

				if (wantBot != null && (!string.IsNullOrEmpty(wantBot.Faction) || wantBot.Team > 0))
				{
					// Defer faction/team — bot may not exist yet locally even if we already
					// issued slot_bot a moment ago. Tick() will retry every frame for up to TTL.
					pendingBotApplies.Add(new PendingBotApply
					{
						SlotKey = slot.Key,
						Faction = wantBot.Faction,
						Team = wantBot.Team,
						TicksLeft = PendingBotApplyTtlTicks,
					});
				}
			}
		}

		public override void Tick()
		{
			if (pendingBotApplies.Count == 0)
				return;

			for (var i = pendingBotApplies.Count - 1; i >= 0; i--)
			{
				var pending = pendingBotApplies[i];
				var client = orderManager.LobbyInfo.ClientInSlot(pending.SlotKey);
				if (client?.Bot != null)
				{
					if (!string.IsNullOrEmpty(pending.Faction))
						orderManager.IssueOrder(Order.Command($"faction {client.Index} {pending.Faction}"));
					if (pending.Team > 0)
						orderManager.IssueOrder(Order.Command($"team {client.Index} {pending.Team}"));
					pendingBotApplies.RemoveAt(i);
					continue;
				}

				pending.TicksLeft--;
				if (pending.TicksLeft <= 0)
					pendingBotApplies.RemoveAt(i);
			}
		}

		void SaveCurrent()
		{
			var name = (nameField.Text ?? string.Empty).Trim();
			if (string.IsNullOrEmpty(name) || IsReserved(name))
				return;
			SnapshotAs(name);
			activePresetName = name;
		}

		void SnapshotAs(string name)
		{
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
				{
					preset.Bots[slot.Key] = new BotConfig
					{
						Type = c.Bot,
						Faction = c.Faction,
						Team = c.Team,
					};
				}
			}

			presets[name] = preset;
			WritePresets();
		}

		void DeleteCurrent()
		{
			var name = (nameField.Text ?? string.Empty).Trim();
			if (string.IsNullOrEmpty(name) || IsReserved(name))
				return;
			if (!presets.Remove(name))
				return;
			WritePresets();
			if (activePresetName == name)
				activePresetName = DefaultPresetName;
			nameField.Text = DefaultPresetName;
		}

		void RenameActive()
		{
			var newName = (nameField.Text ?? string.Empty).Trim();
			if (string.IsNullOrEmpty(newName) || IsReserved(newName) || newName == activePresetName)
				return;
			if (IsReserved(activePresetName)) return;
			if (!presets.TryGetValue(activePresetName, out var preset)) return;
			if (presets.ContainsKey(newName)) return; // would clobber

			presets.Remove(activePresetName);
			presets[newName] = preset;
			activePresetName = newName;
			WritePresets();
		}
	}
}
