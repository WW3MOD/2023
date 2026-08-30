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
		readonly Widget emptyHintAccent;
		string lastSnapshot = "<uninitialised>";

		// Options that always render with the amber Warning treatment when set,
		// regardless of direction — they affect the match in a way that matters.
		static readonly HashSet<string> WarningOptionIds = new()
		{
			"timelimit",
			"cheats",
			"friendly-fire",
		};

		// Option-id sets live on LobbyOptionsLogic (single source of truth shared by
		// both panels) — a duplicated copy here once drifted and misfiltered chips.

		// Pass-2 polish: chips use a single neutral dark fill so they blend with the
		// dark UI; the leading +/-/! glyph carries the classification color. Was
		// previously pastel pink/green/amber backgrounds — read like sticky notes
		// against the dark theme.
		static readonly Color ChipFill = Color.FromArgb(0x22, 0x22, 0x22);
		static readonly Color IncreasedText = Color.FromArgb(0x6e, 0xd6, 0x8a); // sanctioned green exception — increase/decrease chips are informative color-coding (see _lobby-palette.yaml)
		static readonly Color DecreasedText = Color.FromArgb(0xe7, 0x7d, 0x7d);
		static readonly Color WarningText = Color.FromArgb(0xf0, 0xb0, 0x60);
		static readonly Color OverflowText = Color.FromArgb(0x96, 0x96, 0x96); // ink-2 — "+N more" chip is informational, not a change classification

		enum Classification { Increased, Decreased, Warning }

		[ObjectCreator.UseCtor]
		internal LobbyActiveChangesLogic(Widget widget, OrderManager orderManager, Func<MapPreview> getMap)
		{
			container = widget;
			this.orderManager = orderManager;
			this.getMap = getMap;
			chipTemplate = widget.Get("CHIP_TEMPLATE");
			emptyHint = widget.GetOrNull<LabelWidget>("EMPTY_HINT");
			emptyHintAccent = widget.GetOrNull("EMPTY_HINT_ACCENT");
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
			// accent widgets — those are part of the chrome, not generated chips).
			for (var i = container.Children.Count - 1; i >= 0; i--)
			{
				var c = container.Children[i];
				if (c == chipTemplate || c == emptyHint || c == emptyHintAccent)
					continue;
				container.RemoveChild(c);
			}

			// Same visibility filtering as LobbyOptionsLogic — no chip for an option
			// the player can't see or reset in the options grid.
			var options = map.PlayerActorInfo.TraitInfos<ILobbyOptions>()
				.Concat(map.WorldActorInfo.TraitInfos<ILobbyOptions>())
				.SelectMany(t => t.LobbyOptions(map))
				.Where(o => o.IsVisible && o.Id != "scenario" && !LobbyOptionsLogic.HiddenOptionIds.Contains(o.Id))
				.OrderBy(o => o.DisplayOrder)
				.ToArray();

			// Chips flow horizontally starting at x=16 (the quadrant's shared
			// content inset — matches the EMPTY_HINT and accent line). First chip
			// sits just below the header strip (EMPTY_HINT Y:8 H:12 + accent
			// Y:26 H:2 + 8px gap). Keep these in sync with LOBBY_ACTIVE_CHANGES
			// in lobby-players.yaml.
			const int startX = 16;
			const int startY = 36;
			const int spacing = 8;
			const int rowStride = 38;
			var containerWidth = container.Bounds.Width;
			var chipHeight = chipTemplate.Bounds.Height;

			// Size each chip to its text rather than the template's fixed 180px.
			// 24px total internal padding (12 left + 12 right) so the label
			// doesn't kiss the chip edges. Cap at the usable row width, NOT a
			// magic constant, so the cap tracks the panel instead of needing a
			// bump every time an option name grows.
			//
			// This measurement was never the reason chips overflowed their
			// background — that was the cloned BG block keeping the template's
			// width, fixed in AddChip below. A previous pass raised a 260px cap
			// here to chase the same symptom and changed nothing visible.
			var templateLabel = chipTemplate.GetOrNull<LabelWidget>("CHIP_LABEL");
			var font = templateLabel != null ? Game.Renderer.Fonts[templateLabel.Font] : null;
			var maxChipWidth = Math.Max(24, containerWidth - 2 * startX);
			int MeasureChipWidth(string text) => font != null ? Math.Min(font.Measure(text).X + 24, maxChipWidth) : 180;

			// Collect every changed option first so the row cap and the "+N more"
			// overflow chip can be computed against the full set before rendering.
			var entries = new List<(string Text, Classification Klass, int Width)>();
			foreach (var opt in options)
			{
				if (!orderManager.LobbyInfo.GlobalSettings.LobbyOptions.TryGetValue(opt.Id, out var state))
					continue;
				if (state.Value == opt.DefaultValue)
					continue;

				var (text, klass) = Classify(opt, state.Value);
				entries.Add((text, klass, MeasureChipWidth(text)));
			}

			// Cap rendering by the measured container height: row r fits when
			// startY + r * rowStride + chipHeight stays inside the container.
			var maxRows = 0;
			if (container.Bounds.Height >= startY + chipHeight)
				maxRows = (container.Bounds.Height - startY - chipHeight) / rowStride + 1;

			bool FitsAll(IReadOnlyList<int> widths)
			{
				var sx = startX;
				var row = 0;
				foreach (var w in widths)
				{
					if (sx > startX && sx + w > containerWidth - startX)
					{
						sx = startX;
						row++;
					}

					if (row >= maxRows)
						return false;

					sx += w + spacing;
				}

				return true;
			}

			var total = entries.Count;
			var renderCount = total;
			string overflowText = null;
			var widths = entries.Select(e => e.Width).ToList();
			if (maxRows == 0)
				renderCount = 0;
			else if (!FitsAll(widths))
			{
				// Render the longest prefix that still leaves room for a compact
				// "+N more" chip in the last available slot. k=0 always fits when
				// maxRows >= 1 (a lone chip never wraps), so the loop terminates
				// with a valid split.
				for (var k = total - 1; k >= 0; k--)
				{
					var candidate = $"+{total - k} more";
					var trial = widths.Take(k).ToList();
					trial.Add(MeasureChipWidth(candidate));
					if (FitsAll(trial))
					{
						renderCount = k;
						overflowText = candidate;
						break;
					}
				}
			}

			var x = startX;
			var y = startY;
			void AddChip(string text, Color ink, int chipWidth)
			{
				// Wrap to a new row if the chip won't fit on this row.
				if (x > startX && x + chipWidth > containerWidth - startX)
				{
					x = startX;
					y += rowStride;
				}

				var chip = chipTemplate.Clone();
				chip.IsVisible = () => true;
				chip.Bounds.X = x;
				chip.Bounds.Y = y;
				chip.Bounds.Width = chipWidth;

				var bg = chip.GetOrNull<ColorBlockWidget>("BG");
				if (bg != null)
				{
					// BG is declared Width: PARENT_WIDTH, but that expression is
					// evaluated once at load against the 180px template, and Clone()
					// copies resolved bounds instead of re-running Initialize. Nothing
					// downstream re-derives it, so the fill must be resized here or
					// every chip paints 180px wide whatever its text says.
					bg.Bounds.Width = chipWidth;
					bg.Bounds.Height = chip.Bounds.Height;
					bg.GetColor = () => ChipFill;
				}

				var lbl = chip.GetOrNull<LabelWidget>("CHIP_LABEL");
				if (lbl != null)
				{
					lbl.Bounds.Width = chipWidth;
					var captured = text;
					lbl.GetText = () => captured;
					lbl.GetColor = () => ink;
				}

				container.AddChild(chip);
				x += chipWidth + spacing;
			}

			for (var i = 0; i < renderCount; i++)
			{
				var (text, klass, width) = entries[i];
				var ink = klass switch
				{
					Classification.Increased => IncreasedText,
					Classification.Decreased => DecreasedText,
					_ => WarningText,
				};
				AddChip(text, ink, width);
			}

			if (overflowText != null)
				AddChip(overflowText, OverflowText, MeasureChipWidth(overflowText));

			if (emptyHint != null)
			{
				emptyHint.IsVisible = () => true;

				// N is always the true total of changed options, even when the
				// row cap hides some behind the "+N more" chip.
				var hintText = total == 0 ? "All settings at default" : $"ACTIVE CHANGES ({total})";
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
