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
using System.Text;
using OpenRA.Network;
using OpenRA.Primitives;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	// WW3MOD: renders a strip of color-coded chips on the lobby's PLAYERS panel
	// summarising every option whose current value differs from its default.
	// Increased (green), Decreased (red), Warning (amber) for high-impact toggles.
	public class LobbyActiveChangesLogic : ChromeLogic
	{
		readonly Widget container;
		readonly OrderManager orderManager;
		readonly Func<MapPreview> getMap;
		readonly Widget chipTemplate;
		readonly LabelWidget emptyHint;
		readonly Widget sectionBg;
		string lastSnapshot = "<uninitialised>";

		// Options that always render with the amber Warning treatment when set,
		// regardless of direction — they affect the match in a way that matters.
		static readonly HashSet<string> WarningOptionIds = new()
		{
			"timelimit",
			"cheats",
			"friendly-fire",
		};

		// PITFALL: this set must stay in sync with LobbyOptionsLogic.CommonOptionIds.
		// If they drift, a chip's click-to-jump will land on the wrong tab and the
		// option won't appear there because the option-list filter disagrees. Keep
		// them as one list mentally; the duplication is just to avoid a public API.
		static readonly HashSet<string> CommonOptionIds = new()
		{
			"startingcash", "passiveincome", "incomemodifier",
			"explored", "fog", "separateteamspawns",
			"gamespeed", "timelimit", "startingunits",
			"bounty",
		};

		// Pass-2 polish: chips use a single neutral dark fill so they blend with the
		// dark UI; the leading +/-/! glyph carries the classification color. Was
		// previously pastel pink/green/amber backgrounds — read like sticky notes
		// against the dark theme.
		static readonly Color ChipFill = Color.FromArgb(0x2a, 0x2a, 0x2a);
		static readonly Color IncreasedText = Color.FromArgb(0x6e, 0xd6, 0x8a);
		static readonly Color DecreasedText = Color.FromArgb(0xe7, 0x7d, 0x7d);
		static readonly Color WarningText = Color.FromArgb(0xf0, 0xb0, 0x60);

		enum Classification { Increased, Decreased, Warning }

		[ObjectCreator.UseCtor]
		internal LobbyActiveChangesLogic(Widget widget, OrderManager orderManager, Func<MapPreview> getMap)
		{
			container = widget;
			this.orderManager = orderManager;
			this.getMap = getMap;
			chipTemplate = widget.Get("CHIP_TEMPLATE");
			emptyHint = widget.GetOrNull<LabelWidget>("EMPTY_HINT");
			sectionBg = widget.GetOrNull("SECTION_BG");
		}

		public override void Tick()
		{
			var map = getMap();
			if (map == null || map.WorldActorInfo == null)
				return;

			var snapshot = ComputeSnapshot();
			if (snapshot == lastSnapshot)
				return;

			lastSnapshot = snapshot;
			Rebuild(map);
		}

		string ComputeSnapshot()
		{
			var sb = new StringBuilder();
			foreach (var kv in orderManager.LobbyInfo.GlobalSettings.LobbyOptions.OrderBy(k => k.Key))
				sb.Append(kv.Key).Append('=').Append(kv.Value.Value).Append(';');
			return sb.ToString();
		}

		void Rebuild(MapPreview map)
		{
			// Drop previous chips (everything except the persistent template / hint /
			// background widgets — those are part of the chrome, not generated chips).
			for (var i = container.Children.Count - 1; i >= 0; i--)
			{
				var c = container.Children[i];
				if (c == chipTemplate || c == emptyHint || c == sectionBg)
					continue;
				container.RemoveChild(c);
			}

			var options = map.PlayerActorInfo.TraitInfos<ILobbyOptions>()
				.Concat(map.WorldActorInfo.TraitInfos<ILobbyOptions>())
				.SelectMany(t => t.LobbyOptions(map))
				.Where(o => o.IsVisible)
				.OrderBy(o => o.DisplayOrder)
				.ToArray();

			// Counter label sits on its own row at Y=0; chips flow on the row below
			// at Y=22 starting from x=5 (no horizontal sharing with the counter
			// anymore, so the chips get the full row width).
			var x = 5;
			const int spacing = 10;
			var count = 0;

			foreach (var opt in options)
			{
				if (!orderManager.LobbyInfo.GlobalSettings.LobbyOptions.TryGetValue(opt.Id, out var state))
					continue;
				if (state.Value == opt.DefaultValue)
					continue;

				var (text, klass) = Classify(opt, state.Value);
				var chip = chipTemplate.Clone();
				chip.IsVisible = () => true;
				chip.Bounds.X = x;
				chip.Bounds.Y = 22;

				var bg = chip.GetOrNull<ColorBlockWidget>("BG");
				var lbl = chip.GetOrNull<LabelWidget>("CHIP_LABEL");

				// Size each chip to its text rather than the template's fixed 180px.
				// 24px total internal padding (12 left + 12 right) so the label
				// doesn't kiss the chip edges.
				if (lbl != null)
				{
					var font = Game.Renderer.Fonts[lbl.Font];
					var textWidth = font.Measure(text).X;
					var chipWidth = Math.Min(textWidth + 24, 260);
					chip.Bounds.Width = chipWidth;
					lbl.Bounds.Width = chipWidth;
				}

				Color ink;
				switch (klass)
				{
					case Classification.Increased: ink = IncreasedText; break;
					case Classification.Decreased: ink = DecreasedText; break;
					default: ink = WarningText; break;
				}

				if (bg != null)
					bg.GetColor = () => ChipFill;
				if (lbl != null)
				{
					var captured = text;
					lbl.GetText = () => captured;
					lbl.GetColor = () => ink;
				}

				// Clicking the chip jumps to the panel that owns this option.
				var hit = chip.GetOrNull<ButtonWidget>("HIT");
				if (hit != null)
				{
					var optId = opt.Id;
					hit.OnClick = () =>
					{
						var target = CommonOptionIds.Contains(optId) ? "Players" : "Options";
						LobbyLogic.SwitchPanel?.Invoke(target);
					};
				}

				container.AddChild(chip);
				x += chip.Bounds.Width + spacing;
				count++;
			}

			if (emptyHint != null)
			{
				emptyHint.IsVisible = () => true;
				var hintText = count == 0 ? "All settings at default" : $"ACTIVE CHANGES ({count})";
				emptyHint.GetText = () => hintText;
			}
		}

		static (string Label, Classification Class) Classify(LobbyOption opt, string currentValue)
		{
			var name = opt.Name;
			if (FluentProvider.TryGetMessage(name, out var translated))
				name = translated;

			var display = ResolveValueLabel(opt, currentValue);

			if (WarningOptionIds.Contains(opt.Id))
				return ($"!  {name}  {display}", Classification.Warning);

			if (opt is LobbyBooleanOption)
			{
				var enabled = currentValue == bool.TrueString;
				return (enabled ? $"+  {name}  ON" : $"-  {name}  OFF",
					enabled ? Classification.Increased : Classification.Decreased);
			}

			if (int.TryParse(currentValue, out var cur) && int.TryParse(opt.DefaultValue, out var def))
			{
				var klass = cur > def ? Classification.Increased : Classification.Decreased;
				var prefix = cur > def ? "+" : "-";
				return ($"{prefix}  {name}  {display}", klass);
			}

			// Enum-style dropdown that isn't numeric: show as Warning (neutral colour, "changed").
			return ($"~  {name}  {display}", Classification.Warning);
		}

		static string ResolveValueLabel(LobbyOption opt, string value)
		{
			if (opt.Values.TryGetValue(value, out var v))
				return FluentProvider.TryGetMessage(v, out var translated) ? translated : v;

			return value;
		}
	}
}
