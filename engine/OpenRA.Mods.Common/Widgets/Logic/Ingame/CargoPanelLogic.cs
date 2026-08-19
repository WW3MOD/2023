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

using System.Collections.Generic;
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
	///
	/// <para>The manifest rows below are READ-ONLY, and that is the whole of the distinction this
	/// panel draws. The per-passenger BUTTONS deleted at 7b5c692b are not coming back here: aiming a
	/// drop from the screen edge is what the unload menu exists to replace. What went with them by
	/// accident was the READOUT. The menu names what is aboard too, but only while it is open, only
	/// over the battlefield, and only for one selected transport — so between 7b5c692b and here, a
	/// loaded transport at rest was a number and nothing else. Rows are grouped and labelled by the
	/// same <see cref="CargoManifest"/> the menu uses, so the panel reads as a preview of the menu
	/// rather than a second, differently-shaped account of the same hold. A 36-slot Chinook takes
	/// `Types: Infantry`, which admits more distinct classes than the chrome has rows for;
	/// <see cref="CargoManifest.Fit"/> spends the last row saying so.</para>
	/// </summary>
	public class CargoPanelLogic : ChromeLogic
	{
		readonly World world;
		readonly Widget panel;
		readonly List<CargoManifestRow> rows = new List<CargoManifestRow>();

		// Counted off the chrome rather than declared here. Fit sizes its "+n more" row against this
		// number, so a constant that disagreed with the YAML would not leave a blank row — it would
		// print a wrong count of hidden classes above one.
		readonly int slots;

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

			for (var i = 0; ; i++)
			{
				var classLabel = panel.GetOrNull<LabelWidget>($"CARGO_CLASS_{i}");
				if (classLabel == null)
					break;

				var slot = i;
				slots = i + 1;

				classLabel.GetText = () => slot < rows.Count ? rows[slot].Label : "";
				classLabel.IsVisible = () => slot < rows.Count;

				var countLabel = panel.GetOrNull<LabelWidget>($"CARGO_COUNT_{i}");
				if (countLabel != null)
				{
					countLabel.GetText = () => slot < rows.Count ? $"x{rows[slot].Count}" : "";
					countLabel.IsVisible = () => slot < rows.Count;
				}
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
				UpdateRows();
				return selectedTransport != null;
			};
		}

		// Rebuilt here rather than in each label's GetText, so the ten rows of one frame are ten
		// slices of one manifest instead of ten independent reads that a mid-frame unload could
		// disagree across.
		void UpdateRows()
		{
			rows.Clear();
			if (cargo == null)
				return;

			rows.AddRange(CargoManifest.Fit(CargoManifest.Group(cargo.Passengers), slots));
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
