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
		GarrisonProtection garrisonProtection;
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

			// Shelter info labels (RESERVE_LABEL_0 through RESERVE_LABEL_3)
			for (var i = 0; i < 4; i++)
			{
				var shelterIndex = i;
				var reserveLabel = panel.GetOrNull<LabelWidget>($"RESERVE_LABEL_{i}");
				if (reserveLabel != null)
				{
					reserveLabel.GetText = () => GetShelterText(shelterIndex);
					reserveLabel.IsVisible = () => IsShelterVisible(shelterIndex);
				}
			}

			// Garrison header label — includes protection percentage
			var headerLabel = panel.GetOrNull<LabelWidget>("GARRISON_HEADER");
			if (headerLabel != null)
			{
				headerLabel.GetText = () =>
				{
					if (selectedGarrison == null)
						return "";

					if (garrisonProtection != null)
					{
						var prot = garrisonProtection.GetCurrentProtection();
						return $"GARRISON [Shield: {prot}%]";
					}

					return "GARRISON";
				};
			}

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
			garrisonProtection = null;
			cargo = null;

			var selected = world.Selection.Actors
				.Where(a => a.Owner == world.LocalPlayer && a.IsInWorld && !a.IsDead)
				.ToArray();

			if (selected.Length != 1)
				return;

			var gm = selected[0].TraitOrDefault<GarrisonManager>();
			var c = selected[0].TraitOrDefault<Cargo>();
			if (gm == null || c == null)
				return;

			// Show panel if there are any soldiers (deployed or in shelter)
			var hasDeployed = gm.PortStates.Any(ps => ps.DeployedSoldier != null);
			var hasShelter = gm.ShelterPassengers.Any();
			if (!hasDeployed && !hasShelter && c.IsEmpty())
				return;

			selectedGarrison = selected[0];
			garrisonManager = gm;
			garrisonProtection = selected[0].TraitOrDefault<GarrisonProtection>();
			cargo = c;
		}

		string GetPortText(int portIndex)
		{
			if (garrisonManager == null || portIndex >= garrisonManager.PortStates.Length)
				return "";

			var ps = garrisonManager.PortStates[portIndex];
			var portName = ps.Port.Name;

			if (ps.DeployedSoldier == null || ps.DeployedSoldier.IsDead)
				return $"{portName}: (empty)";

			var tooltip = ps.DeployedSoldier.TraitOrDefault<Tooltip>();
			var unitName = tooltip?.Info.Name ?? ps.DeployedSoldier.Info.Name;

			var ammoStr = "";
			var ammo = ps.DeployedSoldier.TraitsImplementing<AmmoPool>().FirstOrDefault();
			if (ammo != null)
				ammoStr = $" [{ammo.CurrentAmmoCount}/{ammo.Info.Ammo}]";

			// Port soldiers always have 80% damage reduction via garrisoned-at-port condition
			return $"{portName}: {unitName}{ammoStr} (80% cover)";
		}

		bool IsPortVisible(int portIndex)
		{
			return garrisonManager != null && portIndex < garrisonManager.PortStates.Length;
		}

		bool HasPortOccupant(int portIndex)
		{
			if (garrisonManager == null || portIndex >= garrisonManager.PortStates.Length)
				return false;

			return garrisonManager.PortStates[portIndex].DeployedSoldier != null;
		}

		void EjectPortOccupant(int portIndex)
		{
			if (garrisonManager == null || portIndex >= garrisonManager.PortStates.Length)
				return;

			var soldier = garrisonManager.PortStates[portIndex].DeployedSoldier;
			if (soldier == null || selectedGarrison == null)
				return;

			world.IssueOrder(new Order("EjectGarrisonPassenger", selectedGarrison, false) { ExtraData = soldier.ActorID });
		}

		string GetShelterText(int shelterIndex)
		{
			if (garrisonManager == null)
				return "";

			var shelter = garrisonManager.ShelterPassengers.ToArray();
			if (shelterIndex >= shelter.Length)
				return "";

			var pax = shelter[shelterIndex];
			if (pax == null || pax.IsDead)
				return "[S] (dead)";

			var tooltip = pax.TraitOrDefault<Tooltip>();
			var name = tooltip?.Info.Name ?? pax.Info.Name;

			// Show current shelter protection from GarrisonProtection trait
			if (garrisonProtection != null)
			{
				var prot = garrisonProtection.GetCurrentProtection();
				return $"[S] {name} ({prot}% cover)";
			}

			return $"[S] {name}";
		}

		bool IsShelterVisible(int shelterIndex)
		{
			if (garrisonManager == null)
				return false;

			return shelterIndex < garrisonManager.ShelterPassengers.Count();
		}
	}
}
