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
	public class LobbyOptionsLogic : ChromeLogic
	{
		[FluentReference]
		const string NotAvailable = "label-not-available";

		readonly ScrollPanelWidget panel;
		readonly Widget optionsContainer;
		readonly Widget categoryHeaderTemplate;
		readonly Widget checkboxRowTemplate;
		readonly Widget dropdownRowTemplate;
		readonly int yMargin;

		readonly Func<MapPreview> getMap;
		readonly OrderManager orderManager;
		readonly Func<bool> configurationDisabled;
		MapPreview mapPreview;

		[ObjectCreator.UseCtor]
		internal LobbyOptionsLogic(Widget widget, OrderManager orderManager, Func<MapPreview> getMap, Func<bool> configurationDisabled)
		{
			this.getMap = getMap;
			this.orderManager = orderManager;
			this.configurationDisabled = configurationDisabled;

			panel = (ScrollPanelWidget)widget;
			optionsContainer = widget.Get("LOBBY_OPTIONS");
			yMargin = optionsContainer.Bounds.Y;
			categoryHeaderTemplate = optionsContainer.Get("CATEGORY_HEADER_TEMPLATE");
			checkboxRowTemplate = optionsContainer.Get("CHECKBOX_ROW_TEMPLATE");
			dropdownRowTemplate = optionsContainer.Get("DROPDOWN_ROW_TEMPLATE");

			mapPreview = getMap();
			RebuildOptions();
		}

		public override void Tick()
		{
			var newMapPreview = getMap();
			if (newMapPreview == mapPreview)
				return;

			// We are currently enumerating the widget tree and so can't modify any layout
			// Defer it to the end of tick instead
			Game.RunAfterTick(() =>
			{
				mapPreview = newMapPreview;
				RebuildOptions();
			});
		}

		void RebuildOptions()
		{
			if (mapPreview == null || mapPreview.WorldActorInfo == null)
				return;

			optionsContainer.RemoveChildren();
			optionsContainer.Bounds.Height = 0;
			var allOptions = mapPreview.PlayerActorInfo.TraitInfos<ILobbyOptions>()
					.Concat(mapPreview.WorldActorInfo.TraitInfos<ILobbyOptions>())
					.SelectMany(t => t.LobbyOptions(mapPreview))
					.Where(o => o.IsVisible)
					.OrderBy(o => o.DisplayOrder)
					.ToArray();

			// Group options by category, preserving order
			var categories = allOptions
				.GroupBy(o => o.Category ?? "")
				.OrderBy(g => g.Min(o => o.DisplayOrder))
				.ToArray();

			foreach (var category in categories)
			{
				// Add category header if category has a name
				if (!string.IsNullOrEmpty(category.Key))
				{
					var header = categoryHeaderTemplate.Clone();
					header.Bounds.Y = optionsContainer.Bounds.Height;
					optionsContainer.Bounds.Height += header.Bounds.Height;

					var headerLabel = header.Get<LabelWidget>("LABEL");
					var categoryName = category.Key;
					headerLabel.GetText = () => categoryName;
					headerLabel.IsVisible = () => true;

					optionsContainer.AddChild(header);
				}

<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
				Widget row = null;
				var checkboxColumns = new Queue<CheckboxWidget>();
				var dropdownColumns = new Queue<DropDownButtonWidget>();
=======
				checkbox.GetText = () => option.Name;
				if (option.Description != null)
				{
					var (text, desc) = LobbyUtils.SplitOnFirstToken(option.Description);
					checkbox.GetTooltipText = () => text;
					checkbox.GetTooltipDesc = () => desc;
				}
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp

				foreach (var option in category.Where(o => o is LobbyBooleanOption))
				{
					if (checkboxColumns.Count == 0)
					{
						row = checkboxRowTemplate.Clone();
						row.Bounds.Y = optionsContainer.Bounds.Height;
						optionsContainer.Bounds.Height += row.Bounds.Height;
						foreach (var child in row.Children)
							if (child is CheckboxWidget childCheckbox)
								checkboxColumns.Enqueue(childCheckbox);

						optionsContainer.AddChild(row);
					}

					var checkbox = checkboxColumns.Dequeue();
					var optionEnabled = new PredictedCachedTransform<Session.Global, bool>(
						gs => gs.LobbyOptions[option.Id].IsEnabled);

					var optionLocked = new CachedTransform<Session.Global, bool>(
						gs => gs.LobbyOptions[option.Id].IsLocked);

					checkbox.GetText = () => option.Name;
					if (option.Description != null)
						checkbox.GetTooltipText = () => option.Description;

					checkbox.IsVisible = () => true;
					checkbox.IsChecked = () => optionEnabled.Update(orderManager.LobbyInfo.GlobalSettings);
					checkbox.IsDisabled = () => configurationDisabled() || optionLocked.Update(orderManager.LobbyInfo.GlobalSettings);
					checkbox.OnClick = () =>
					{
						var state = !optionEnabled.Update(orderManager.LobbyInfo.GlobalSettings);
						orderManager.IssueOrder(Order.Command($"option {option.Id} {state}"));
						optionEnabled.Predict(state);
					};
				}

<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
				foreach (var option in category.Where(o => !(o is LobbyBooleanOption)))
				{
					if (dropdownColumns.Count == 0)
					{
						row = dropdownRowTemplate.Clone() as Widget;
						row.Bounds.Y = optionsContainer.Bounds.Height;
						optionsContainer.Bounds.Height += row.Bounds.Height;
						foreach (var child in row.Children)
							if (child is DropDownButtonWidget dropDown)
								dropdownColumns.Enqueue(dropDown);
=======
			foreach (var option in allOptions.Where(o => o is not LobbyBooleanOption))
			{
				if (dropdownColumns.Count == 0)
				{
					row = dropdownRowTemplate.Clone();
					row.Bounds.Y = optionsContainer.Bounds.Height;
					optionsContainer.Bounds.Height += row.Bounds.Height;
					foreach (var child in row.Children)
						if (child is DropDownButtonWidget dropDown)
							dropdownColumns.Enqueue(dropDown);

					optionsContainer.AddChild(row);
				}
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp

						optionsContainer.AddChild(row);
					}

<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
					var dropdown = dropdownColumns.Dequeue();
					var optionValue = new CachedTransform<Session.Global, Session.LobbyOptionState>(
						gs => gs.LobbyOptions[option.Id]);
=======
				var getOptionLabel = new CachedTransform<string, string>(id =>
				{
					if (id == null || !option.Values.TryGetValue(id, out var value))
						return FluentProvider.GetMessage(NotAvailable);
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp

					var getOptionLabel = new CachedTransform<string, string>(id =>
					{
						if (id == null || !option.Values.TryGetValue(id, out var value))
							return modData.Translation.GetString(NotAvailable);

<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
						return value;
					});

					dropdown.GetText = () => getOptionLabel.Update(optionValue.Update(orderManager.LobbyInfo.GlobalSettings).Value);
					if (option.Description != null)
						dropdown.GetTooltipText = () => option.Description;
					dropdown.IsVisible = () => true;
					dropdown.IsDisabled = () => configurationDisabled() ||
						optionValue.Update(orderManager.LobbyInfo.GlobalSettings).IsLocked;

					dropdown.OnMouseDown = _ =>
					{
						Func<KeyValuePair<string, string>, ScrollItemWidget, ScrollItemWidget> setupItem = (c, template) =>
						{
							Func<bool> isSelected = () => optionValue.Update(orderManager.LobbyInfo.GlobalSettings).Value == c.Key;
							Action onClick = () => orderManager.IssueOrder(Order.Command($"option {option.Id} {c.Key}"));

							var item = ScrollItemWidget.Setup(template, isSelected, onClick);
							item.Get<LabelWidget>("LABEL").GetText = () => c.Value;
							return item;
						};

						dropdown.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", option.Values.Count * 30, option.Values, setupItem);
					};
=======
				dropdown.GetText = () => getOptionLabel.Update(optionValue.Update(orderManager.LobbyInfo.GlobalSettings).Value);
				if (option.Description != null)
				{
					var (text, desc) = LobbyUtils.SplitOnFirstToken(option.Description);
					dropdown.GetTooltipText = () => text;
					dropdown.GetTooltipDesc = () => desc;
				}

				dropdown.IsVisible = () => true;
				dropdown.IsDisabled = () => configurationDisabled() ||
					optionValue.Update(orderManager.LobbyInfo.GlobalSettings).IsLocked;

				dropdown.OnMouseDown = _ =>
				{
					ScrollItemWidget SetupItem(KeyValuePair<string, string> c, ScrollItemWidget template)
					{
						bool IsSelected() => optionValue.Update(orderManager.LobbyInfo.GlobalSettings).Value == c.Key;
						void OnClick() => orderManager.IssueOrder(Order.Command($"option {option.Id} {c.Key}"));

						var item = ScrollItemWidget.Setup(template, IsSelected, OnClick);
						item.Get<LabelWidget>("LABEL").GetText = () => c.Value;
						return item;
					}

					dropdown.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", option.Values.Count * 30, option.Values, SetupItem);
				};
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp

					var label = row.GetOrNull<LabelWidget>(dropdown.Id + "_DESC");
					if (label != null)
					{
						label.GetText = () => option.Name + ":";
						label.IsVisible = () => true;
					}
				}
			}

			panel.ContentHeight = yMargin + optionsContainer.Bounds.Height;
			optionsContainer.Bounds.Y = yMargin;

			panel.ScrollToTop();
		}
	}
}
