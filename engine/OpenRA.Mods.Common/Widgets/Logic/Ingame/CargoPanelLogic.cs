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
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets
{
	/// <summary>
	/// Sidebar panel showing cargo contents of a selected transport.
	/// Displays individual passengers with mark/eject buttons and supply count.
	/// Supports waypoint-based selective unloading via CargoUnloadOrderGenerator.
	/// </summary>
	public class CargoPanelLogic : ChromeLogic
	{
		readonly World world;
		readonly Widget panel;

		int selectionHash;
		Actor selectedTransport;
		Cargo cargo;
		CargoSupply cargoSupply;

		readonly HashSet<uint> markedPassengerIds = new HashSet<uint>();

		const int MaxDisplaySlots = 10;

		[ObjectCreator.UseCtor]
		public CargoPanelLogic(Widget widget, World world)
		{
			this.world = world;
			panel = widget;

			// Header label
			var headerLabel = panel.GetOrNull<LabelWidget>("CARGO_HEADER");
			if (headerLabel != null)
			{
				headerLabel.GetText = () =>
				{
					if (selectedTransport == null || cargo == null)
						return "";

					var passengerCount = cargo.PassengerCount;
					var supplyCount = cargoSupply?.SupplyCount ?? 0;
					var markedCount = CountValidMarks();

					var markedSuffix = markedCount > 0 ? $" | {markedCount} marked" : "";

					if (supplyCount > 0 && passengerCount > 0)
						return $"CARGO [{passengerCount} troops, {supplyCount} supply{markedSuffix}]";
					else if (supplyCount > 0)
						return $"CARGO [{supplyCount} supply{markedSuffix}]";
					else if (passengerCount > 0)
						return $"CARGO [{passengerCount} troops{markedSuffix}]";
					else
						return "CARGO [empty]";
				};
			}

			// Passenger slots (CARGO_LABEL_0 through CARGO_LABEL_9)
			for (var i = 0; i < MaxDisplaySlots; i++)
			{
				var slotIndex = i;

				// Mark/toggle button — left side of each row
				var markBtn = panel.GetOrNull<ButtonWidget>($"MARK_CARGO_{i}");
				if (markBtn != null)
				{
					markBtn.OnClick = () => ToggleMark(slotIndex);
					markBtn.IsVisible = () => IsPassengerSlotVisible(slotIndex);
					markBtn.GetText = () => IsPassengerMarked(slotIndex) ? "[x]" : "[ ]";
					markBtn.IsHighlighted = () => IsPassengerMarked(slotIndex);
				}

				var label = panel.GetOrNull<LabelWidget>($"CARGO_LABEL_{i}");
				if (label != null)
				{
					label.GetText = () => GetPassengerText(slotIndex);
					label.IsVisible = () => IsPassengerSlotVisible(slotIndex);
				}

				var ejectBtn = panel.GetOrNull<ButtonWidget>($"EJECT_CARGO_{i}");
				if (ejectBtn != null)
				{
					ejectBtn.OnClick = () => EjectPassenger(slotIndex);
					ejectBtn.IsDisabled = () => !IsPassengerSlotVisible(slotIndex);
					ejectBtn.IsVisible = () => IsPassengerSlotVisible(slotIndex);
				}

				// Rally button — set post-eject move target
				var rallyBtn = panel.GetOrNull<ButtonWidget>($"RALLY_CARGO_{i}");
				if (rallyBtn != null)
				{
					rallyBtn.OnClick = () => SetPassengerRally(slotIndex);
					rallyBtn.IsVisible = () => IsPassengerSlotVisible(slotIndex);
					rallyBtn.GetText = () => HasPassengerRally(slotIndex) ? "R!" : "R";
					rallyBtn.IsHighlighted = () => HasPassengerRally(slotIndex);
				}
			}

			// Deploy Marked button — activates waypoint unload mode
			var deployMarkedButton = panel.GetOrNull<ButtonWidget>("DEPLOY_MARKED");
			if (deployMarkedButton != null)
			{
				deployMarkedButton.OnClick = () =>
				{
					if (selectedTransport == null || markedPassengerIds.Count == 0)
						return;

					// Activate waypoint unload order generator
					world.OrderGenerator = new CargoUnloadOrderGenerator(
						selectedTransport,
						() => new HashSet<uint>(markedPassengerIds),
						ids => markedPassengerIds.Clear());
				};
				deployMarkedButton.IsDisabled = () => CountValidMarks() == 0;
				deployMarkedButton.IsVisible = () => selectedTransport != null && cargo != null && !cargo.IsEmpty();
				deployMarkedButton.IsHighlighted = () => world.OrderGenerator is CargoUnloadOrderGenerator;
			}

			// Mark All / Unmark All toggle
			var markAllButton = panel.GetOrNull<ButtonWidget>("MARK_ALL_CARGO");
			if (markAllButton != null)
			{
				markAllButton.OnClick = () =>
				{
					if (cargo == null)
						return;

					var passengers = cargo.Passengers.ToArray();
					if (markedPassengerIds.Count >= passengers.Length)
					{
						// All marked — unmark all
						markedPassengerIds.Clear();
					}
					else
					{
						// Mark all
						markedPassengerIds.Clear();
						foreach (var p in passengers)
							if (!p.IsDead)
								markedPassengerIds.Add(p.ActorID);
					}
				};
				markAllButton.IsVisible = () => selectedTransport != null && cargo != null && !cargo.IsEmpty();
				markAllButton.GetText = () =>
				{
					if (cargo == null)
						return "Mark All";
					return markedPassengerIds.Count >= cargo.PassengerCount ? "Unmark All" : "Mark All";
				};
			}

			// Unload All Troops button (excludes supply)
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

			// Supply info label
			var supplyLabel = panel.GetOrNull<LabelWidget>("SUPPLY_LABEL");
			if (supplyLabel != null)
			{
				supplyLabel.GetText = () =>
				{
					if (cargoSupply == null || cargoSupply.SupplyCount <= 0)
						return "";

					return $"Supply: {cargoSupply.SupplyCount} units ({cargoSupply.EffectiveSupply} ammo)";
				};
				supplyLabel.IsVisible = () => cargoSupply != null && cargoSupply.SupplyCount > 0;
			}

			// Drop Supply button — unloads all supply as SUPPLYCACHE
			var dropSupplyButton = panel.GetOrNull<ButtonWidget>("DROP_SUPPLY");
			if (dropSupplyButton != null)
			{
				dropSupplyButton.OnClick = () =>
				{
					if (selectedTransport != null && cargoSupply != null && cargoSupply.SupplyCount > 0)
						world.IssueOrder(new Order("UnloadCargoSupply", selectedTransport, false)
							{ ExtraData = (uint)cargoSupply.SupplyCount });
				};
				dropSupplyButton.IsDisabled = () => selectedTransport == null || cargoSupply == null || cargoSupply.SupplyCount <= 0;
				dropSupplyButton.IsVisible = () => cargoSupply != null && cargoSupply.SupplyCount > 0;
			}

			// Drop 1 Supply button — unloads a single supply unit
			var dropOneSupplyButton = panel.GetOrNull<ButtonWidget>("DROP_ONE_SUPPLY");
			if (dropOneSupplyButton != null)
			{
				dropOneSupplyButton.OnClick = () =>
				{
					if (selectedTransport != null && cargoSupply != null && cargoSupply.SupplyCount > 0)
						world.IssueOrder(new Order("UnloadCargoSupply", selectedTransport, false)
							{ ExtraData = 1 });
				};
				dropOneSupplyButton.IsDisabled = () => selectedTransport == null || cargoSupply == null || cargoSupply.SupplyCount <= 0;
				dropOneSupplyButton.IsVisible = () => cargoSupply != null && cargoSupply.SupplyCount > 0;
			}

			// Panel visibility ticker
			var ticker = panel.GetOrNull<LogicTickerWidget>("CARGO_TICKER");
			if (ticker != null)
			{
				ticker.OnTick = () =>
				{
					UpdateSelection();
					panel.Visible = selectedTransport != null;
				};
			}

			panel.Visible = false;
		}

		void UpdateSelection()
		{
			if (selectionHash == world.Selection.Hash)
				return;

			selectionHash = world.Selection.Hash;

			selectedTransport = null;
			cargo = null;
			cargoSupply = null;
			markedPassengerIds.Clear();

			var selected = world.Selection.Actors
				.Where(a => a.Owner == world.LocalPlayer && a.IsInWorld && !a.IsDead)
				.ToArray();

			if (selected.Length != 1)
				return;

			var c = selected[0].TraitOrDefault<Cargo>();
			if (c == null)
				return;

			// Don't show cargo panel for garrison buildings (they have their own panel)
			if (selected[0].TraitOrDefault<GarrisonManager>() != null)
				return;

			// Only show if transport has passengers or supply
			var cs = selected[0].TraitOrDefault<CargoSupply>();
			var hasContent = !c.IsEmpty() || (cs != null && cs.SupplyCount > 0);
			if (!hasContent)
				return;

			selectedTransport = selected[0];
			cargo = c;
			cargoSupply = cs;
		}

		int CountValidMarks()
		{
			if (cargo == null)
				return 0;

			// Only count marks for passengers still in cargo
			var passengers = cargo.Passengers.ToArray();
			return markedPassengerIds.Count(id => passengers.Any(p => p.ActorID == id && !p.IsDead));
		}

		bool IsPassengerMarked(int slotIndex)
		{
			if (cargo == null)
				return false;

			var passengers = cargo.Passengers.ToArray();
			if (slotIndex >= passengers.Length)
				return false;

			return markedPassengerIds.Contains(passengers[slotIndex].ActorID);
		}

		void ToggleMark(int slotIndex)
		{
			if (cargo == null)
				return;

			var passengers = cargo.Passengers.ToArray();
			if (slotIndex >= passengers.Length)
				return;

			var id = passengers[slotIndex].ActorID;
			if (markedPassengerIds.Contains(id))
				markedPassengerIds.Remove(id);
			else
				markedPassengerIds.Add(id);
		}

		string GetPassengerText(int slotIndex)
		{
			if (cargo == null)
				return "";

			var passengers = cargo.Passengers.ToArray();
			if (slotIndex >= passengers.Length)
				return "";

			var pax = passengers[slotIndex];
			if (pax == null || pax.IsDead)
				return "(dead)";

			var tooltip = pax.TraitOrDefault<Tooltip>();
			var name = tooltip?.Info.Name ?? pax.Info.Name;

			// Show ammo status if unit has ammo
			var ammo = pax.TraitsImplementing<AmmoPool>().FirstOrDefault();
			if (ammo != null)
				return $"{name} [{ammo.CurrentAmmoCount}/{ammo.Info.Ammo}]";

			// Show HP for non-ammo units
			var health = pax.TraitOrDefault<Health>();
			if (health != null && health.DamageState != DamageState.Undamaged)
				return $"{name} ({health.HP * 100 / health.MaxHP}%)";

			return name;
		}

		bool IsPassengerSlotVisible(int slotIndex)
		{
			if (cargo == null)
				return false;

			return slotIndex < cargo.PassengerCount;
		}

		void EjectPassenger(int slotIndex)
		{
			if (cargo == null || selectedTransport == null)
				return;

			var passengers = cargo.Passengers.ToArray();
			if (slotIndex >= passengers.Length)
				return;

			var passenger = passengers[slotIndex];
			if (passenger == null || passenger.IsDead)
				return;

			world.IssueOrder(new Order("UnloadCargoPassenger", selectedTransport, false) { ExtraData = passenger.ActorID });
		}

		void SetPassengerRally(int slotIndex)
		{
			if (cargo == null || selectedTransport == null)
				return;

			var passengers = cargo.Passengers.ToArray();
			if (slotIndex >= passengers.Length)
				return;

			var passenger = passengers[slotIndex];
			if (passenger == null || passenger.IsDead)
				return;

			var tooltip = passenger.TraitOrDefault<Tooltip>();
			var name = tooltip?.Info.Name ?? passenger.Info.Name;

			// If already has rally, clear it (toggle)
			if (cargo.HasEjectRally(passenger.ActorID))
			{
				cargo.ClearEjectRally(passenger.ActorID);
				return;
			}

			// Enter rally target selection mode
			world.OrderGenerator = new EjectRallyOrderGenerator(selectedTransport, passenger.ActorID, name);
		}

		bool HasPassengerRally(int slotIndex)
		{
			if (cargo == null)
				return false;

			var passengers = cargo.Passengers.ToArray();
			if (slotIndex >= passengers.Length)
				return false;

			return cargo.HasEjectRally(passengers[slotIndex].ActorID);
		}
	}
}
