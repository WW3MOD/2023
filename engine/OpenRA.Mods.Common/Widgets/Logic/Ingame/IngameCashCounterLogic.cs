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
using System.Linq;
using System.Text;
using OpenRA.Mods.Common.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class IngameCashCounterLogic : ChromeLogic
	{
		const float DisplayFracPerFrame = .07f;
		const int DisplayDeltaPerFrame = 37;

		readonly Player player;
		readonly PlayerResources playerResources;
		readonly LabelWithTooltipWidget cashLabel;
		readonly string cashTemplate;

		int displayResources;

		[ObjectCreator.UseCtor]
		public IngameCashCounterLogic(Widget widget, ModData modData, World world)
		{
			player = world.LocalPlayer;
			playerResources = player.PlayerActor.Trait<PlayerResources>();

			displayResources = playerResources.Cash + playerResources.Resources;

			cashLabel = widget.Get<LabelWithTooltipWidget>("CASH");
			cashLabel.GetTooltipText = GetBreakdownText;

			cashTemplate = cashLabel.Text;
		}

		string GetBreakdownText()
		{
			var sb = new StringBuilder();

			sb.AppendLine("--- INCOME ---");
			sb.AppendLine("Passive: +$" + playerResources.PassiveIncomeAmount);

			var incomeByType = playerResources.IncomeEntries
				.GroupBy(e => e.ActorType)
				.OrderByDescending(g => g.Sum(e => e.AmountPerInterval));

			foreach (var group in incomeByType)
			{
				var name = group.First().Name;
				var count = group.Count();
				var total = (int)group.Sum(e => e.AmountPerInterval);
				if (count > 1)
					sb.AppendLine(name + " x" + count + ": +$" + total);
				else
					sb.AppendLine(name + ": +$" + total);
			}

			sb.AppendLine("Total: +$" + playerResources.TotalIncome);
			sb.AppendLine();
			sb.AppendLine("--- UPKEEP ---");

			var upkeepByType = playerResources.UpkeepEntries
				.GroupBy(e => e.ActorType)
				.OrderByDescending(g => g.Sum(e => e.Cost));

			foreach (var group in upkeepByType)
			{
				var name = group.First().Name;
				var count = group.Count();
				var total = (int)group.Sum(e => e.Cost);
				if (count > 1)
					sb.AppendLine(name + " x" + count + ": -$" + total);
				else
					sb.AppendLine(name + ": -$" + total);
			}

			sb.AppendLine("Total: -$" + (int)playerResources.Upkeep);
			sb.AppendLine();

			var net = playerResources.NetChange;
			var sign = net >= 0 ? "+" : "";
			sb.Append("Net: " + sign + "$" + net + " / interval");

			return sb.ToString();
		}

		public override void Tick()
		{
			var actual = playerResources.Cash + playerResources.Resources;

			var diff = Math.Abs(actual - displayResources);
			var move = Math.Min(Math.Max((int)(diff * DisplayFracPerFrame), DisplayDeltaPerFrame), diff);

			if (displayResources < actual)
			{
				displayResources += move;
			}
			else if (displayResources > actual)
			{
				displayResources -= move;
			}

			var net = playerResources.NetChange;
			var sign = net >= 0 ? "+" : "";
			cashLabel.Text = cashTemplate.F(displayResources) + " (" + sign + cashTemplate.F(net) + ")";
		}
	}
}
