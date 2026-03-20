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
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets
{
	/// <summary>
	/// Logic for the garrison info panel that appears when a garrisoned building is selected.
	/// Shows port occupant names, roles, and provides eject controls.
	/// Requires a Container widget with child Label widgets for port info display.
	/// </summary>
	public class GarrisonPanelLogic : ChromeLogic
	{
		readonly World world;
		readonly Widget panel;

		int selectionHash;
		Actor selectedGarrison;
		GarrisonManager garrisonManager;
		Cargo cargo;

		[ObjectCreator.UseCtor]
		public GarrisonPanelLogic(Widget widget, World world)
		{
			this.world = world;
			panel = widget;

			// Eject All button
			var ejectAllButton = panel.GetOrNull<ButtonWidget>("EJECT_ALL");
			if (ejectAllButton != null)
			{
				ejectAllButton.OnClick = () =>
				{
					if (selectedGarrison != null && cargo != null && !cargo.IsEmpty())
						world.IssueOrder(new Order("Unload", selectedGarrison, false));
				};
				ejectAllButton.IsDisabled = () => selectedGarrison == null || cargo == null || cargo.IsEmpty();
			}

			// Port info labels (PORT_LABEL_0 through PORT_LABEL_7)
			for (var i = 0; i < 8; i++)
			{
				var portIndex = i;
				var portLabel = panel.GetOrNull<LabelWidget>($"PORT_LABEL_{i}");
				if (portLabel != null)
				{
					portLabel.GetText = () => GetPortText(portIndex);
					portLabel.IsVisible = () => IsPortVisible(portIndex);
				}

				// Eject buttons per port (EJECT_PORT_0 through EJECT_PORT_7)
				var ejectBtn = panel.GetOrNull<ButtonWidget>($"EJECT_PORT_{i}");
				if (ejectBtn != null)
				{
					ejectBtn.OnClick = () => EjectPortOccupant(portIndex);
					ejectBtn.IsDisabled = () => !HasPortOccupant(portIndex);
					ejectBtn.IsVisible = () => IsPortVisible(portIndex);
				}
			}

			// Reserve info labels (RESERVE_LABEL_0 through RESERVE_LABEL_3)
			for (var i = 0; i < 4; i++)
			{
				var reserveIndex = i;
				var reserveLabel = panel.GetOrNull<LabelWidget>($"RESERVE_LABEL_{i}");
				if (reserveLabel != null)
				{
					reserveLabel.GetText = () => GetReserveText(reserveIndex);
					reserveLabel.IsVisible = () => IsReserveVisible(reserveIndex);
				}
			}

			// Garrison header label
			var headerLabel = panel.GetOrNull<LabelWidget>("GARRISON_HEADER");
			if (headerLabel != null)
				headerLabel.GetText = () => selectedGarrison != null ? "GARRISON" : "";

			// Panel visibility ticker
			var ticker = panel.GetOrNull<LogicTickerWidget>("GARRISON_TICKER");
			if (ticker != null)
			{
				ticker.OnTick = () =>
				{
					UpdateSelection();
					panel.Visible = selectedGarrison != null;
				};
			}

			panel.Visible = false;
		}

		void UpdateSelection()
		{
			if (selectionHash == world.Selection.Hash)
				return;

			selectionHash = world.Selection.Hash;

			selectedGarrison = null;
			garrisonManager = null;
			cargo = null;

			var selected = world.Selection.Actors
				.Where(a => a.Owner == world.LocalPlayer && a.IsInWorld && !a.IsDead)
				.ToArray();

			if (selected.Length != 1)
				return;

			var gm = selected[0].TraitOrDefault<GarrisonManager>();
			var c = selected[0].TraitOrDefault<Cargo>();
			if (gm == null || c == null || c.IsEmpty())
				return;

			selectedGarrison = selected[0];
			garrisonManager = gm;
			cargo = c;
		}

		string GetPortText(int portIndex)
		{
			if (garrisonManager == null || portIndex >= garrisonManager.PortStates.Length)
				return "";

			var ps = garrisonManager.PortStates[portIndex];
			var portName = ps.Port.Name;

			if (ps.Occupant == null || ps.Occupant.IsDead)
				return $"{portName}: (empty)";

			var tooltip = ps.Occupant.TraitOrDefault<Tooltip>();
			var unitName = tooltip?.Info.Name ?? ps.Occupant.Info.Name;
			var role = GarrisonManager.GetGarrisonRole(ps.Occupant);

			var ammoStr = "";
			var ammo = ps.Occupant.TraitsImplementing<AmmoPool>().FirstOrDefault();
			if (ammo != null)
				ammoStr = $" [{ammo.CurrentAmmoCount}/{ammo.Info.Ammo}]";

			return $"{portName}: {unitName} ({role}){ammoStr}";
		}

		bool IsPortVisible(int portIndex)
		{
			return garrisonManager != null && portIndex < garrisonManager.PortStates.Length;
		}

		bool HasPortOccupant(int portIndex)
		{
			if (garrisonManager == null || portIndex >= garrisonManager.PortStates.Length)
				return false;

			return garrisonManager.PortStates[portIndex].Occupant != null;
		}

		void EjectPortOccupant(int portIndex)
		{
			if (garrisonManager == null || portIndex >= garrisonManager.PortStates.Length)
				return;

			var occ = garrisonManager.PortStates[portIndex].Occupant;
			if (occ == null || selectedGarrison == null)
				return;

			world.IssueOrder(new Order("EjectGarrisonPassenger", selectedGarrison, false) { ExtraData = occ.ActorID });
		}

		string GetReserveText(int reserveIndex)
		{
			if (garrisonManager == null)
				return "";

			var reserves = garrisonManager.ReservePassengers.ToArray();
			if (reserveIndex >= reserves.Length)
				return "";

			var pax = reserves[reserveIndex];
			if (pax == null || pax.IsDead)
				return "[R] (dead)";

			var tooltip = pax.TraitOrDefault<Tooltip>();
			return $"[R] {tooltip?.Info.Name ?? pax.Info.Name}";
		}

		bool IsReserveVisible(int reserveIndex)
		{
			if (garrisonManager == null)
				return false;

			return reserveIndex < garrisonManager.ReservePassengers.Count();
		}
	}
}
