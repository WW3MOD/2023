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
using OpenRA.Network;
using OpenRA.Primitives;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	// WW3MOD: inline replacement for the lobby's SLOTS_DROPDOWNBUTTON ("Slot Admin").
	// Surfaces the three actions players actually use — bulk add bots, bulk remove
	// bots, and team auto-assignment — as visible buttons on the Players panel so
	// nothing important hides behind a dropdown.
	public class LobbySetupRowLogic : ChromeLogic
	{
		readonly OrderManager orderManager;
		readonly Func<MapPreview> getMap;
		readonly Func<bool> configurationDisabled;

		[ObjectCreator.UseCtor]
		internal LobbySetupRowLogic(Widget widget, OrderManager orderManager, Func<MapPreview> getMap, Func<bool> configurationDisabled)
		{
			this.orderManager = orderManager;
			this.getMap = getMap;
			this.configurationDisabled = configurationDisabled;

			var addBots = widget.Get<ButtonWidget>("ADD_BOTS_BUTTON");
			var removeBots = widget.Get<ButtonWidget>("REMOVE_BOTS_BUTTON");
			var autoTeam = widget.Get<DropDownButtonWidget>("AUTO_TEAM_BUTTON");

			addBots.IsDisabled = () => configurationDisabled() || !AnySlotAllowsBots();
			addBots.OnClick = FillEmptySlotsWithBots;

			removeBots.IsDisabled = () => configurationDisabled() || !AnyBotInSlots();
			removeBots.OnClick = RemoveAllBots;

			autoTeam.IsDisabled = () => configurationDisabled() || MaxTeamCount() < 2;
			autoTeam.OnMouseDown = _ => ShowAutoTeamDropdown(autoTeam);
		}

		bool AnySlotAllowsBots() => orderManager.LobbyInfo.Slots.Values.Any(s => s.AllowBots);

		bool AnyBotInSlots() => orderManager.LobbyInfo.Clients.Any(c => c.Bot != null);

		int MaxTeamCount()
		{
			var occupied = orderManager.LobbyInfo.Slots
				.Count(s => !s.Value.LockTeam && orderManager.LobbyInfo.ClientInSlot(s.Key) != null);
			return (occupied + 1) / 2;
		}

		void FillEmptySlotsWithBots()
		{
			var map = getMap();
			if (map == null || map.PlayerActorInfo == null)
				return;

			var botTypes = map.PlayerActorInfo.TraitInfos<IBotInfo>().Select(t => t.Type).ToArray();
			if (botTypes.Length == 0)
				return;

			var botController = orderManager.LobbyInfo.Clients.FirstOrDefault(c => c.IsAdmin);
			if (botController == null)
				return;

			foreach (var slot in orderManager.LobbyInfo.Slots)
			{
				if (!slot.Value.AllowBots)
					continue;
				var c = orderManager.LobbyInfo.ClientInSlot(slot.Key);
				if (c != null && c.Bot == null)
					continue;
				var bot = botTypes.Random(Game.CosmeticRandom);
				orderManager.IssueOrder(Order.Command($"slot_bot {slot.Key} {botController.Index} {bot}"));
			}
		}

		void RemoveAllBots()
		{
			foreach (var slot in orderManager.LobbyInfo.Slots)
			{
				var c = orderManager.LobbyInfo.ClientInSlot(slot.Key);
				if (c != null && c.Bot != null)
					orderManager.IssueOrder(Order.Command("slot_open " + slot.Value.PlayerReference));
			}
		}

		void ShowAutoTeamDropdown(DropDownButtonWidget dropdown)
		{
			var max = MaxTeamCount();
			var counts = Enumerable.Range(2, Math.Max(0, max - 1)).Reverse().ToList();

			ScrollItemWidget Setup(int count, ScrollItemWidget template)
			{
				bool IsSelected() => false;
				void OnClick() => orderManager.IssueOrder(Order.Command($"assignteams {count}"));
				var item = ScrollItemWidget.Setup(template, IsSelected, OnClick);
				var label = item.Get<LabelWidget>("LABEL");
				var captured = count;
				label.GetText = () => $"{captured} teams";
				return item;
			}

			dropdown.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", Math.Min(180, counts.Count * 25), counts, Setup);
		}
	}
}
