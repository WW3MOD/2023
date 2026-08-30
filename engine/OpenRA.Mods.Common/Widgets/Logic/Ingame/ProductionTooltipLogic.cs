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
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class ProductionTooltipLogic : ChromeLogic
	{
		[ObjectCreator.UseCtor]
		public ProductionTooltipLogic(Widget widget, TooltipContainerWidget tooltipContainer, Player player, Func<ProductionIcon> getTooltipIcon)
		{
			var world = player.World;
			var mapRules = world.Map.Rules;
			var pm = player.PlayerActor.TraitOrDefault<PowerManager>();
			var pr = player.PlayerActor.Trait<PlayerResources>();

			widget.IsVisible = () => getTooltipIcon() != null && getTooltipIcon().Actor != null;
			var nameLabel = widget.Get<LabelWidget>("NAME");
			var hotkeyLabel = widget.Get<LabelWidget>("HOTKEY");
			var requiresLabel = widget.Get<LabelWidget>("REQUIRES");
			var powerLabel = widget.Get<LabelWidget>("POWER");
			var powerIcon = widget.Get<ImageWidget>("POWER_ICON");
			var timeLabel = widget.Get<LabelWidget>("TIME");
			var timeIcon = widget.Get<ImageWidget>("TIME_ICON");
			var costLabel = widget.Get<LabelWidget>("COST");
			var costIcon = widget.Get<ImageWidget>("COST_ICON");
			var descContainer = widget.Get<ContainerWidget>("DESC");

			// The per-kind style table, lifted out of the container and held here. The templates are
			// hidden and never drawn; each row clones the one its kind names. Pulling them out of the
			// container's child list first means the per-actor rebuild below can just RemoveChildren().
			var templates = new TooltipRowTemplates(descContainer);
			descContainer.RemoveChildren();

			var iconMargin = timeIcon.Bounds.X;

			var font = Game.Renderer.Fonts[nameLabel.Font];
			var requiresFont = Game.Renderer.Fonts[requiresLabel.Font];
			var formatBuildTime = new CachedTransform<int, string>(time => WidgetUtils.FormatTime(time, world.Timestep));
			var requiresFormat = requiresLabel.Text;

			ActorInfo lastActor = null;
			var lastHotkey = Hotkey.Invalid;
			var lastPowerState = pm?.PowerState ?? PowerState.Normal;
			var descContainerY = descContainer.Bounds.Y;
			var descContainerPadding = descContainer.Bounds.Height;
			const int MaxTooltipWidth = 350;

			tooltipContainer.BeforeRender = () =>
			{
				var tooltipIcon = getTooltipIcon();

				var actor = tooltipIcon?.Actor;
				if (actor == null)
					return;

				var hotkey = tooltipIcon.Hotkey?.GetValue() ?? Hotkey.Invalid;
				if (actor == lastActor && hotkey == lastHotkey && (pm == null || pm.PowerState == lastPowerState))
					return;

				var tooltip = actor.TraitInfos<TooltipInfo>().FirstOrDefault(info => info.EnabledByDefault);
				var name = tooltip?.Name ?? actor.Name;
				var buildable = actor.TraitInfo<BuildableInfo>();

				var cost = 0;
				if (tooltipIcon.ProductionQueue != null)
					cost = tooltipIcon.ProductionQueue.GetProductionCost(actor);
				else
				{
					var valued = actor.TraitInfoOrDefault<ValuedInfo>();
					if (valued != null)
						cost = valued.Cost;
				}

				nameLabel.Text = name;

				var nameSize = font.Measure(name);
				var hotkeyWidth = 0;
				hotkeyLabel.Visible = hotkey.IsValid();

				if (hotkeyLabel.Visible)
				{
					var hotkeyText = $"({hotkey.DisplayString()})";

					hotkeyWidth = font.Measure(hotkeyText).X + 2 * nameLabel.Bounds.X;
					hotkeyLabel.Text = hotkeyText;
					hotkeyLabel.Bounds.X = nameSize.X + 2 * nameLabel.Bounds.X;
				}

				var prereqs = buildable.Prerequisites.Select(a => ActorName(mapRules, a))
					.Where(s => !s.StartsWith("~", StringComparison.Ordinal) && !s.StartsWith("!", StringComparison.Ordinal));

				var requiresSize = int2.Zero;
				if (prereqs.Any())
				{
					requiresLabel.Text = string.Format(requiresFormat, prereqs.JoinWith(", "));
					requiresSize = requiresFont.Measure(requiresLabel.Text);
					requiresLabel.Visible = true;
					descContainer.Bounds.Y = descContainerY + requiresLabel.Bounds.Height;
				}
				else
				{
					requiresLabel.Visible = false;
					descContainer.Bounds.Y = descContainerY;
				}

				var powerSize = new int2(0, 0);
				if (pm != null)
				{
					var power = actor.TraitInfos<PowerInfo>().Where(i => i.EnabledByDefault).Sum(i => i.Amount);
					powerLabel.Text = power.ToString();
					powerLabel.GetColor = () => ((pm.PowerProvided - pm.PowerDrained) >= -power || power > 0)
						? Color.White : Color.Red;
					powerLabel.Visible = power != 0;
					powerIcon.Visible = power != 0;
					powerSize = font.Measure(powerLabel.Text);
				}
				else
				{
					// A mod without a PowerManager never reaches the branch above, and Widget.Visible
					// defaults to true — so the power sprite was drawn on every tooltip beside a
					// permanently empty label. WW3MOD is such a mod: player.yaml has PowerManager
					// commented out.
					powerLabel.Visible = false;
					powerIcon.Visible = false;
				}

				var buildTime = tooltipIcon.ProductionQueue?.GetBuildTime(actor, buildable) ?? 0;
				var timeModifier = pm != null && pm.PowerState != PowerState.Normal ? tooltipIcon.ProductionQueue.Info.LowPowerModifier : 100;

				timeLabel.Text = formatBuildTime.Update((buildTime * timeModifier) / 100);
				timeLabel.TextColor = (pm != null && pm.PowerState != PowerState.Normal && tooltipIcon.ProductionQueue.Info.LowPowerModifier > 100) ? Color.Red : Color.White;
				var timeSize = font.Measure(timeLabel.Text);

				costLabel.Text = cost.ToString();
				costLabel.GetColor = () => pr.Cash + pr.Resources >= cost ? Color.White : Color.Red;
				var costSize = font.Measure(costLabel.Text);

				var elements = BuildElements(actor, mapRules, buildable);
				descContainer.RemoveChildren();
				descContainer.Bounds.Width = MaxTooltipWidth;
				var descSize = LayOutElements(descContainer, templates, elements, MaxTooltipWidth);
				descContainer.Bounds.Height = descSize.Y + descContainerPadding;

				var leftWidth = Math.Clamp(
					new[] { nameSize.X + hotkeyWidth, requiresSize.X, descSize.X }.Aggregate(Math.Max),
					MaxTooltipWidth, MaxTooltipWidth);
				var rightWidth = new[] { powerSize.X, timeSize.X, costSize.X }.Aggregate(Math.Max);

				timeIcon.Bounds.X = powerIcon.Bounds.X = costIcon.Bounds.X = leftWidth + 2 * nameLabel.Bounds.X;
				timeLabel.Bounds.X = powerLabel.Bounds.X = costLabel.Bounds.X = timeIcon.Bounds.Right + iconMargin;
				widget.Bounds.Width = leftWidth + rightWidth + 3 * nameLabel.Bounds.X + timeIcon.Bounds.Width + iconMargin;

				// Set the bottom margin to match the left margin
				var leftHeight = descContainer.Bounds.Bottom + descContainer.Bounds.X;

				// Set the bottom margin to match the top margin
				var rightHeight = (powerLabel.Visible ? powerIcon.Bounds.Bottom : timeIcon.Bounds.Bottom) + costIcon.Bounds.Top;

				widget.Bounds.Height = Math.Max(leftHeight, rightHeight);

				lastActor = actor;
				lastHotkey = hotkey;
				if (pm != null)
					lastPowerState = pm.PowerState;
			};
		}

		/// <summary>
		/// The per-kind style table, held as widget templates. Each is hidden and never drawn; a row
		/// clones the one its <see cref="TooltipElementKind"/> names, so a restyle is a chrome-yaml
		/// edit and touches no logic and no content.
		/// </summary>
		sealed class TooltipRowTemplates
		{
			public readonly LabelWidget Subhead;
			public readonly LabelWidget Prose;
			public readonly LabelWidget ListItem;
			public readonly LabelWidget StatKey;
			public readonly LabelWidget StatValue;
			public readonly LabelWidget CostValue;
			public readonly LabelWidget Dots;
			public readonly LabelWidget Note;
			public readonly ColorBlockWidget Separator;

			public TooltipRowTemplates(Widget container)
			{
				Subhead = container.Get<LabelWidget>("TEMPLATE_SUBHEAD");
				Prose = container.Get<LabelWidget>("TEMPLATE_PROSE");
				ListItem = container.Get<LabelWidget>("TEMPLATE_LISTITEM");
				StatKey = container.Get<LabelWidget>("TEMPLATE_STATKEY");
				StatValue = container.Get<LabelWidget>("TEMPLATE_STATVALUE");
				CostValue = container.Get<LabelWidget>("TEMPLATE_COSTVALUE");
				Dots = container.Get<LabelWidget>("TEMPLATE_DOTS");
				Note = container.Get<LabelWidget>("TEMPLATE_NOTE");
				Separator = container.Get<ColorBlockWidget>("TEMPLATE_SEPARATOR");
			}
		}

		const int SeparatorMargin = 5;
		const int NoteIndent = 9;
		const int ColumnGap = 6;

		/// <summary>
		/// Stacks one widget per row down the container and returns the size consumed. Replaces
		/// measuring one wrapped string: each row is now measured in its own font, which is the
		/// thing a single <c>LabelWidget</c> could not do however the yaml was written.
		/// </summary>
		static int2 LayOutElements(ContainerWidget container, TooltipRowTemplates templates,
			List<TooltipElement> elements, int width)
		{
			var y = 0;
			var maxX = 0;

			foreach (var element in elements)
			{
				switch (element.Kind)
				{
					case TooltipElementKind.Separator:
					{
						y += SeparatorMargin;
						var rule = Show((ColorBlockWidget)templates.Separator.Clone());
						rule.Bounds = new WidgetBounds(0, y, width, 1);
						container.AddChild(rule);
						y += 1 + SeparatorMargin;
						break;
					}

					case TooltipElementKind.Subhead:
						y += AddTextRow(container, templates.Subhead, element.Label.ToUpperInvariant(),
							0, y, width, ref maxX);
						break;

					case TooltipElementKind.Prose:
						y += AddTextRow(container, templates.Prose, element.Label, 0, y, width, ref maxX);
						break;

					case TooltipElementKind.ListItem:
						y += AddTextRow(container, templates.ListItem, " - " + element.Label,
							0, y, width, ref maxX);
						break;

					case TooltipElementKind.Note:
					{
						var height = AddTextRow(container, templates.Note, element.Label,
							NoteIndent, y, width - NoteIndent, ref maxX);
						var rule = Show((ColorBlockWidget)templates.Separator.Clone());
						rule.Bounds = new WidgetBounds(0, y, 1, height);
						container.AddChild(rule);
						y += height;
						break;
					}

					case TooltipElementKind.StatRow:
					case TooltipElementKind.CostRow:
						y += AddStatRow(container, templates, element, y, width, ref maxX);
						break;
				}
			}

			return new int2(maxX, y);
		}

		/// <summary>
		/// Un-hides a cloned template. Setting <c>Visible</c> alone is NOT enough and fails silently.
		/// </summary>
		/// <remarks>
		/// Same shape as <see cref="SetText"/>, one layer up the hierarchy: <c>Widget()</c> sets
		/// <c>IsVisible = () =&gt; Visible</c> (Widget.cs:231), capturing the instance, and the copy
		/// constructor does <c>IsVisible = widget.IsVisible</c> (:246). A clone therefore asks the
		/// TEMPLATE whether it is visible — and the templates are declared `Visible: false`, so every
		/// row answered false and nothing drew. Observed: the panel sized itself correctly from the
		/// measured rows and rendered completely empty.
		/// Every `Func` on Widget behaves this way. Reassign, never assign the backing field alone.
		/// </remarks>
		static T Show<T>(T widget) where T : Widget
		{
			widget.Visible = true;
			widget.IsVisible = () => true;
			return widget;
		}

		/// <summary>
		/// Sets a cloned label's text so it actually renders.
		/// </summary>
		/// <remarks>
		/// Assigning <c>Text</c> alone is NOT enough on a clone and fails silently. LabelWidget's copy
		/// constructor does <c>GetText = other.GetText</c>, and that delegate is the closure
		/// <c>() => textCache.Update(Text)</c> built in the TEMPLATE's constructor — so it reads the
		/// template's Text field, not the clone's. Every cloned row would draw the template's text,
		/// which is empty, and the tooltip would render as a correctly-sized blank panel. Text is set
		/// as well as GetText so that Bounds measuring and any later IncreaseHeightToFitCurrentText
		/// agree with what is drawn.
		/// </remarks>
		static void SetText(LabelWidget label, string text)
		{
			label.Text = text;
			label.GetText = () => text;
		}

		/// <summary>Adds one wrapped, single-column label and returns the height it consumed.</summary>
		static int AddTextRow(ContainerWidget container, LabelWidget template, string text,
			int x, int y, int width, ref int maxX)
		{
			var label = Show((LabelWidget)template.Clone());

			var font = Game.Renderer.Fonts[label.Font];
			var wrapped = WidgetUtils.WrapText(text, width, font);
			SetText(label, wrapped);

			var size = font.Measure(wrapped);
			label.Bounds = new WidgetBounds(x, y, width, size.Y);
			container.AddChild(label);

			maxX = Math.Max(maxX, x + size.X);
			return size.Y;
		}

		/// <summary>
		/// Adds a two-column row: key at the left, value hard against the right edge, dot leaders
		/// bridging the gap. Right-aligning the value is what lets a player compare the same stat
		/// across two tooltips — the alignment is the feature, not decoration.
		/// </summary>
		static int AddStatRow(ContainerWidget container, TooltipRowTemplates templates,
			TooltipElement element, int y, int width, ref int maxX)
		{
			var key = Show((LabelWidget)templates.StatKey.Clone());
			var keyText = element.Label.ToUpperInvariant();
			SetText(key, keyText);

			var keyFont = Game.Renderer.Fonts[key.Font];
			var keySize = keyFont.Measure(keyText);

			var valueTemplate = element.Kind == TooltipElementKind.CostRow
				? templates.CostValue
				: templates.StatValue;

			var value = Show((LabelWidget)valueTemplate.Clone());
			var valueText = element.Value ?? string.Empty;
			SetText(value, valueText);

			var valueFont = Game.Renderer.Fonts[value.Font];
			var valueSize = valueFont.Measure(valueText);

			var height = Math.Max(keySize.Y, valueSize.Y);

			key.Bounds = new WidgetBounds(0, y, keySize.X, height);
			value.Bounds = new WidgetBounds(width - valueSize.X, y, valueSize.X, height);
			container.AddChild(key);
			container.AddChild(value);

			var gapStart = keySize.X + ColumnGap;
			var gapEnd = width - valueSize.X - ColumnGap;
			if (gapEnd > gapStart)
			{
				var dots = Show((LabelWidget)templates.Dots.Clone());

				var dotFont = Game.Renderer.Fonts[dots.Font];

				// Measured over a run rather than per character: a single "." measures its glyph
				// box, not its advance, and the two differ enough to overshoot the value column.
				var advance = Math.Max(1, dotFont.Measure(new string('.', 20)).X / 20);
				var count = (gapEnd - gapStart) / advance;
				if (count > 0)
				{
					SetText(dots, new string('.', count));
					dots.Bounds = new WidgetBounds(gapStart, y, gapEnd - gapStart, height);
					container.AddChild(dots);
				}
			}

			maxX = Math.Max(maxX, width);
			return height;
		}

		static string ActorName(Ruleset rules, string a)
		{
			if (rules.Actors.TryGetValue(a.ToLowerInvariant(), out var ai))
			{
				var actorTooltip = ai.TraitInfos<TooltipInfo>().FirstOrDefault(info => info.EnabledByDefault);
				if (actorTooltip != null)
					return actorTooltip.Name;
			}

			return a;
		}

		/// <summary>
		/// Builds the production tooltip as typed rows: the static <c>Buildable.Description</c>
		/// prose and bullets, then every <see cref="IProvideTooltipDescription"/> contributor in
		/// priority order, then the cross-pool refill total.
		/// </summary>
		static List<TooltipElement> BuildElements(ActorInfo actor, Ruleset rules, BuildableInfo buildable)
		{
			var elements = new List<TooltipElement>();

			// Buildable.Description is authored as an opening sentence, a blank line, then " - "
			// bullets. That shape is followed by all 40 live strings, so it is parsed rather than
			// reformatted: the sentence becomes Prose and each bullet a ListItem. The "\n\n" that
			// used to separate them invisibly becomes an actual Separator below.
			var staticDesc = buildable.Description?.Replace("\\n", "\n") ?? string.Empty;
			foreach (var line in staticDesc.Split('\n'))
			{
				var trimmed = line.Trim();
				if (trimmed.Length == 0)
					continue;

				if (trimmed.StartsWith("-", StringComparison.Ordinal))
					elements.Add(TooltipElement.ListItem(trimmed.Substring(1).Trim()));
				else
					elements.Add(TooltipElement.Prose(trimmed));
			}

			var contributed = new List<(int Priority, TooltipElement Element)>();
			foreach (var provider in actor.TraitInfos<IProvideTooltipDescription>())
			{
				var rows = provider.ProvideTooltipDescription(actor, rules, out var priority);
				if (rows == null)
					continue;

				foreach (var row in rows)
					contributed.Add((priority, row));
			}

			// What refilling this actor from empty costs, across every priced pool.
			// Lives in the renderer (not on AmmoPoolInfo) so individual pools don't
			// need to know about each other; the cross-pool sum is intrinsically global.
			//
			// UNCONDITIONAL on purpose. This was gated on `pools.Length >= 2`, which meant a
			// single-pool actor never saw a refill cost at all: an abrams' 240 appeared only as
			// the tail of "Ammo: 40 (8 batches x 5 rounds x 30 supply = 240)", where it reads as
			// the result of an arithmetic expression rather than as a price. The total is a
			// property of the ACTOR, not of its having happened to need two terms to compute.
			var pools = actor.TraitInfos<AmmoPoolInfo>()
				.Where(p => p.Ammo > 0 && p.SupplyValue > 0)
				.ToArray();
			if (pools.Length > 0)
			{
				var total = pools.Sum(p => p.PoolBudget);
				contributed.Add((510, TooltipElement.Cost("Full refill", $"{total} supply")));

				// The refill as a fraction of one Logistics Centre — but only for actors a Centre
				// can actually rearm. Rearmable is that test: without it no host, LC or truck, ever
				// puts a round in this actor, so relating its refill to a Centre's capacity would be
				// comparing against a provider it cannot use.
				var depot = LogisticsCentreCapacity(rules);
				if (depot > 0 && actor.HasTraitInfo<RearmableInfo>())
				{
					var percent = (total * 100f) / depot;
					contributed.Add((511, TooltipElement.Stat("Depot share", $"{percent:0.#}% of a Logistics Centre")));

					// NO SHIPPED ACTOR CURRENTLY REACHES THIS BRANCH, and it is not dead code by
					// accident — do not "fix" the Note element on the strength of never seeing it.
					// The only refills above a Centre's 2250 belong to the strategic launchers
					// (HIMARS, Iskander), and those are exactly the actors the Rearmable gate above
					// excludes: they carry no Rearmable by deliberate ruling and must be evacuated
					// rather than reloaded (vehicles-america.yaml:1153-1159). So the two conditions
					// are currently disjoint. Re-pricing any rearmable unit past 2250, or making a
					// launcher rearmable, brings this back. Checked against the units the tooltip
					// audit priced, not all 54 buildables.
					if (total > depot)
						contributed.Add((520, TooltipElement.Note(
							"One reload costs more supply than a full Logistics Centre holds.")));
				}
			}

			if (contributed.Count > 0)
			{
				elements.Add(TooltipElement.Separator());
				elements.AddRange(contributed.OrderBy(c => c.Priority).Select(c => c.Element));
			}

			return elements;
		}

		/// <summary>
		/// The supply capacity of the largest Logistics Centre in the ruleset, or 0 if the mod has
		/// no such provider. Read from rules rather than hard-coded so the reference point moves
		/// with the ruleset instead of silently going stale at 2250.
		/// </summary>
		static int LogisticsCentreCapacity(Ruleset rules)
		{
			var best = 0;
			foreach (var ai in rules.Actors.Values)
			{
				// A mobile provider is a supply truck, not a depot; the fraction is meant to read
				// against the fixed Centre a player builds their logistics around.
				if (ai.HasTraitInfo<MobileInfo>() || ai.HasTraitInfo<AircraftInfo>())
					continue;

				foreach (var provider in ai.TraitInfos<SupplyProviderInfo>())
					best = Math.Max(best, provider.TotalSupply);
			}

			return best;
		}
	}
}
