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
		readonly CachedTransform<(int Resources, int Capacity), string> cashflowTooltipCache;
		readonly string cashTemplate;

		int displayResources;
		int displayUpkeep;

		string cashflowTooltip = "";

		[TranslationReference("cash", "income", "upkeep")]
		static readonly string Cashflow = "cashflow";

		[ObjectCreator.UseCtor]
		public IngameCashCounterLogic(Widget widget, ModData modData, World world)
		{
			player = world.LocalPlayer;
			playerResources = player.PlayerActor.Trait<PlayerResources>();

			displayResources = playerResources.Cash + playerResources.Resources;

			displayUpkeep = (int)playerResources.Upkeep; // Doesn't change anything?

			cashflowTooltipCache = new CachedTransform<(int Cash, int Upkeep), string>(x =>
				modData.Translation.GetString(Cashflow, Translation.Arguments("cash", displayResources, "upkeep", displayUpkeep)));

			cashLabel = widget.Get<LabelWithTooltipWidget>("CASH");
			cashLabel.GetTooltipText = () => cashflowTooltip;

			cashTemplate = cashLabel.Text;
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

			displayUpkeep = (int)playerResources.Upkeep;

			cashflowTooltip = cashflowTooltipCache.Update((displayResources, displayUpkeep));

			cashLabel.Text = cashTemplate.F(displayResources) + " (-" + cashTemplate.F(displayUpkeep) + ")";
		}
	}
}
