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

using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets
{
	/// <summary>
	/// Sidebar panel for a selected transport: what it is carrying, and the transport-level
	/// actions that are not per-passenger. Choosing individual passengers is the unload menu's
	/// job (<see cref="Logic.Ingame.CargoUnloadMenuLogic"/>) — this panel only advertises the key for it,
	/// because a panel pinned to the screen edge is the wrong place to aim a drop from.
	/// </summary>
	public class CargoPanelLogic : ChromeLogic
	{
		readonly World world;
		readonly Widget panel;

		int selectionHash;
		Actor selectedTransport;
		Cargo cargo;
		SupplyProvider supplyProvider;

		[ObjectCreator.UseCtor]
		public CargoPanelLogic(Widget widget, World world, ModData modData)
		{
			this.world = world;
			panel = widget;

			var headerLabel = panel.GetOrNull<LabelWidget>("CARGO_HEADER");
			if (headerLabel != null)
			{
				headerLabel.GetText = () =>
				{
					if (selectedTransport == null)
						return "";

					var passengerCount = cargo?.PassengerCount ?? 0;
					var supplyCount = supplyProvider?.CurrentSupply ?? 0;

					if (supplyCount > 0 && passengerCount > 0)
						return $"CARGO [{passengerCount} troops, {supplyCount} supply]";
					else if (supplyCount > 0)
						return $"CARGO [{supplyCount} supply]";
					else if (passengerCount > 0)
						return $"CARGO [{passengerCount} troops]";
					else
						return "CARGO [empty]";
				};
			}

			// Read the binding rather than hardcoding "J", so a rebind does not leave the panel
			// telling the player to press a key that no longer opens anything.
			var unloadMenuKey = modData.Hotkeys["UnloadMenu"];
			var hintLabel = panel.GetOrNull<LabelWidget>("CARGO_HINT");
			if (hintLabel != null)
			{
				hintLabel.GetText = () => $"Press [{unloadMenuKey.GetValue().DisplayString()}] to unload by class";
				hintLabel.IsVisible = () => cargo != null && !cargo.IsEmpty();
			}

			var unloadAllButton = panel.GetOrNull<ButtonWidget>("UNLOAD_ALL_TROOPS");
			if (unloadAllButton != null)
			{
				unloadAllButton.OnClick = () =>
				{
					if (selectedTransport != null && cargo != null && !cargo.IsEmpty())
						world.IssueOrder(new Order("Unload", selectedTransport, false));
				};
				unloadAllButton.IsDisabled = () => selectedTransport == null || cargo == null || cargo.IsEmpty();
			}

			var supplyLabel = panel.GetOrNull<LabelWidget>("SUPPLY_LABEL");
			if (supplyLabel != null)
			{
				supplyLabel.GetText = () =>
				{
					if (supplyProvider == null || supplyProvider.CurrentSupply <= 0)
						return "";

					return $"Supply: {supplyProvider.CurrentSupply} / {supplyProvider.Info.TotalSupply}";
				};
				supplyLabel.IsVisible = () => supplyProvider != null && supplyProvider.CurrentSupply > 0;
			}

			// Drop Supply button — unloads all supply as SUPPLYCACHE
			var dropSupplyButton = panel.GetOrNull<ButtonWidget>("DROP_SUPPLY");
			if (dropSupplyButton != null)
			{
				dropSupplyButton.OnClick = () =>
				{
					if (selectedTransport != null && supplyProvider != null && supplyProvider.CurrentSupply > 0)
						world.IssueOrder(new Order("DropSupplyCache", selectedTransport, false));
				};
				dropSupplyButton.IsDisabled = () => selectedTransport == null || supplyProvider == null || supplyProvider.CurrentSupply <= 0;
				dropSupplyButton.IsVisible = () => supplyProvider != null && supplyProvider.CurrentSupply > 0;
			}

			// Panel visibility — use IsVisible function so it evaluates every frame
			// (LogicTicker inside a hidden container never ticks, causing chicken-and-egg)
			panel.IsVisible = () =>
			{
				UpdateSelection();
				return selectedTransport != null;
			};
		}

		void UpdateSelection()
		{
			if (selectionHash == world.Selection.Hash)
				return;

			selectionHash = world.Selection.Hash;

			selectedTransport = null;
			cargo = null;
			supplyProvider = null;

			var selected = world.Selection.Actors
				.Where(a => a.Owner == world.LocalPlayer && a.IsInWorld && !a.IsDead)
				.ToArray();

			if (selected.Length != 1)
				return;

			var c = selected[0].TraitOrDefault<Cargo>();
			var sp = selected[0].TraitOrDefault<SupplyProvider>();

			// Nothing to show if neither Cargo nor SupplyProvider is present.
			if (c == null && sp == null)
				return;

			// Garrison buildings have their own panel.
			if (selected[0].TraitOrDefault<GarrisonManager>() != null)
				return;

			// Stationary SupplyProviders (LC, SUPPLYCACHE) have no movement and aren't
			// "transports" — only show the panel for mobile supply units (trucks).
			if (sp != null && selected[0].TraitOrDefault<IMove>() == null)
				sp = null;

			// Only show if transport has passengers or supply.
			var hasCargoContent = c != null && !c.IsEmpty();
			var hasSupplyContent = sp != null && sp.CurrentSupply > 0;
			if (!hasCargoContent && !hasSupplyContent)
				return;

			selectedTransport = selected[0];
			cargo = c;
			supplyProvider = sp;
		}
	}
}
