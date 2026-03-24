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
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
using System.Linq;
using System.Text;
=======
using System.Globalization;
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class IngameCashCounterLogic : ChromeLogic
	{
		[FluentReference("usage", "capacity")]
		const string SiloUsage = "label-silo-usage";

		const float DisplayFracPerFrame = .07f;
		const int DisplayDeltaPerFrame = 37;

		readonly Player player;
		readonly PlayerResources playerResources;
		readonly LabelWithTooltipWidget cashLabel;
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
		readonly string cashTemplate;
=======
		readonly CachedTransform<(int Resources, int Capacity), string> siloUsageTooltipCache;
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp

		int displayResources;

<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
=======
		string siloUsageTooltip = "";

>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp
		[ObjectCreator.UseCtor]
		public IngameCashCounterLogic(Widget widget, ModData modData, World world)
		{
			player = world.LocalPlayer;
			playerResources = player.PlayerActor.Trait<PlayerResources>();
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp

			displayResources = playerResources.Cash + playerResources.Resources;

			cashLabel = widget.Get<LabelWithTooltipWidget>("CASH");
			cashLabel.GetTooltipText = GetBreakdownText;

			cashTemplate = cashLabel.Text;
=======
			displayResources = playerResources.GetCashAndResources();

			siloUsageTooltipCache = new CachedTransform<(int Resources, int Capacity), string>(x =>
				FluentProvider.GetMessage(SiloUsage, "usage", x.Resources, "capacity", x.Capacity));
			cashLabel = widget.Get<LabelWithTooltipWidget>("CASH");
			cashLabel.GetTooltipText = () => siloUsageTooltip;
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp
		}

		string GetBreakdownText()
		{
			var lines = new StringBuilder();

			lines.Append("--- INCOME ---\n");
			lines.Append("Passive: +$" + playerResources.PassiveIncomeAmount + "\n");

			var incomeByType = playerResources.IncomeEntries
				.GroupBy(e => e.ActorType)
				.OrderByDescending(g => g.Sum(e => e.AmountPerInterval));

			foreach (var group in incomeByType)
			{
				var name = group.First().Name;
				var count = group.Count();
				var total = (int)group.Sum(e => e.AmountPerInterval);
				if (total <= 0)
					continue;

				if (count > 1)
					lines.Append(name + " x" + count + ": +$" + total + "\n");
				else
					lines.Append(name + ": +$" + total + "\n");
			}

			lines.Append("Total: +$" + playerResources.TotalIncome + "\n");
			lines.Append("\n");
			lines.Append("--- UPKEEP ---\n");

			var upkeepByType = playerResources.UpkeepEntries
				.GroupBy(e => e.ActorType)
				.OrderByDescending(g => g.Sum(e => e.Cost));

<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
			foreach (var group in upkeepByType)
			{
				var name = group.First().Name;
				var count = group.Count();
				var total = (int)group.Sum(e => e.Cost);
				if (total <= 0)
					continue;

				if (count > 1)
					lines.Append(name + " x" + count + ": -$" + total + "\n");
				else
					lines.Append(name + ": -$" + total + "\n");
			}

			lines.Append("Total: -$" + (int)playerResources.Upkeep + "\n");
			lines.Append("\n");

			var net = playerResources.NetChange;
			var sign = net >= 0 ? "+" : "";
			lines.Append("Net: " + sign + "$" + net + " / interval");

			return lines.ToString();
		}

		public override void Tick()
		{
			var actual = playerResources.Cash + playerResources.Resources;
=======
			var actual = playerResources.GetCashAndResources();
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp

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

<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
			var net = playerResources.NetChange;
			var sign = net >= 0 ? "+" : "";
			cashLabel.Text = cashTemplate.F(displayResources) + " (" + sign + cashTemplate.F(net) + ")";
=======
			siloUsageTooltip = siloUsageTooltipCache.Update((playerResources.Resources, playerResources.ResourceCapacity));
			var displayResourcesText = displayResources.ToString(CultureInfo.CurrentCulture);
			cashLabel.GetText = () => displayResourcesText;
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp
		}
	}
}
