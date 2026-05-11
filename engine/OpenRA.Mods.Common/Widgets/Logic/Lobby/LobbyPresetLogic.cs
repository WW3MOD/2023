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
		readonly DropDownButtonWidget dropdown;
		readonly ButtonWidget saveButton;
		readonly ButtonWidget resetButton;

		readonly Dictionary<string, Dictionary<string, string>> presets = new();
		string activePreset = DefaultPresetName;

		[ObjectCreator.UseCtor]
		internal LobbyPresetLogic(Widget widget, OrderManager orderManager, Func<MapPreview> getMap, Func<bool> configurationDisabled)
		{
			this.orderManager = orderManager;
			this.getMap = getMap;
			this.configurationDisabled = configurationDisabled;

			dropdown = widget.Get<DropDownButtonWidget>("PRESET_DROPDOWN");
			saveButton = widget.Get<ButtonWidget>("SAVE_PRESET_BUTTON");
			resetButton = widget.Get<ButtonWidget>("RESET_PRESET_BUTTON");

			LoadPresets();

			dropdown.GetText = () => activePreset;
			dropdown.IsDisabled = () => configurationDisabled();
			dropdown.OnMouseDown = _ => ShowDropdown();

			saveButton.IsDisabled = () => configurationDisabled();
			saveButton.OnClick = SaveCurrent;

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
					var optionsNode = entry.Value.Nodes.FirstOrDefault(n => n.Key == "Options");
					if (optionsNode == null)
						continue;
					var opts = new Dictionary<string, string>();
					foreach (var kv in optionsNode.Value.Nodes)
						opts[kv.Key] = kv.Value.Value ?? string.Empty;
					presets[entry.Key] = opts;
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
					foreach (var opt in preset.Value.OrderBy(o => o.Key))
						lines.Add($"\t\t\t{opt.Key}: {opt.Value}");
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
				bool IsSelected() => activePreset == name;
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
			activePreset = name;
			if (name == DefaultPresetName)
			{
				ResetToDefault();
				return;
			}

			if (!presets.TryGetValue(name, out var opts))
				return;

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
			activePreset = DefaultPresetName;
			var map = getMap();
			if (map == null || map.WorldActorInfo == null)
				return;

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

		void SaveCurrent()
		{
			var map = getMap();
			if (map == null || map.WorldActorInfo == null)
				return;

			var snapshot = new Dictionary<string, string>();
			var allOptions = map.PlayerActorInfo.TraitInfos<ILobbyOptions>()
				.Concat(map.WorldActorInfo.TraitInfos<ILobbyOptions>())
				.SelectMany(t => t.LobbyOptions(map));

			foreach (var opt in allOptions)
			{
				if (!orderManager.LobbyInfo.GlobalSettings.LobbyOptions.TryGetValue(opt.Id, out var state))
					continue;
				if (state.Value != opt.DefaultValue)
					snapshot[opt.Id] = state.Value;
			}

			// Auto-pick a name. A rename UI is a TODO; for now this just makes save reversible.
			var n = 1;
			string name;
			do { name = $"Preset {n++}"; } while (presets.ContainsKey(name));

			presets[name] = snapshot;
			activePreset = name;
			WritePresets();
		}
	}
}
