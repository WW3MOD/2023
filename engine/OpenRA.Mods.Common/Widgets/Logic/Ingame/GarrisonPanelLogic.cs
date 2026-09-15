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
using System.Collections.Generic;
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

		/// <summary>The shelter rows, in declaration order. Held so their Y can be re-seated under
		/// however many firing ports THIS actor actually has — see SeatReserveRows.</summary>
		readonly LabelWidget[] reserveLabels;

		/// <summary>Geometry read off the chrome at construction rather than written here, so
		/// ingame-player.yaml stays the single source of truth for where these rows sit. All four are
		/// zero when the panel declares too few rows to derive them, which disables the re-seating.
		/// </summary>
		readonly int portBaseY, portPitch, reservePitch, portToReserveGap;

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

			// Eject All button — issue Unload when ANY occupants remain (port + shelter),
			// not just when cargo (shelter) is non-empty. At 1HP rubble all soldiers can be
			// deployed at ports → cargo.IsEmpty() is true but the player still needs to
			// evacuate them. GarrisonManager.HasAnyOccupants covers both cases.
			var ejectAllButton = panel.GetOrNull<ButtonWidget>("EJECT_ALL");
			if (ejectAllButton != null)
			{
				ejectAllButton.OnClick = () =>
				{
					if (selectedGarrison != null && garrisonManager != null && garrisonManager.HasAnyOccupants)
						world.IssueOrder(new Order("Unload", selectedGarrison, false));
				};
				ejectAllButton.IsDisabled = () =>
					selectedGarrison == null || garrisonManager == null || !garrisonManager.HasAnyOccupants;
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

			// Shelter info labels (RESERVE_LABEL_0 through RESERVE_LABEL_3). The count is DERIVED from
			// what the chrome declares rather than hardcoded, because the last of them doubles as the
			// overflow row and GetShelterText has to know which index that is.
			var reserves = new List<LabelWidget>();
			for (var i = 0; ; i++)
			{
				var reserveLabel = panel.GetOrNull<LabelWidget>($"RESERVE_LABEL_{i}");
				if (reserveLabel == null)
					break;

				var shelterIndex = i;
				reserveLabel.GetText = () => GetShelterText(shelterIndex);
				reserveLabel.IsVisible = () => IsShelterVisible(shelterIndex);
				reserves.Add(reserveLabel);
			}

			reserveLabels = reserves.ToArray();

			// THE GAP. Port rows are declared for the WIDEST garrison (8, for a civilian house) at
			// fixed Y, and IsPortVisible hides the surplus on anything narrower — but the shelter rows
			// below them keep their declared Y regardless. A PBOX or HBOX has 2 ports and a GTWR has 4
			// (structures-defenses.yaml), so those panels drew two or four rows, then six or four rows
			// of nothing, then the shelter. Re-seat the shelter block under the last port row that is
			// actually shown.
			//
			// Every number comes off the widgets themselves: the row pitch and the deliberate extra
			// space between the two blocks are whatever the chrome says they are, so moving a row in
			// ingame-player.yaml moves it here too instead of silently disagreeing.
			var port0 = panel.GetOrNull<LabelWidget>("PORT_LABEL_0");
			var port1 = panel.GetOrNull<LabelWidget>("PORT_LABEL_1");
			if (port0 != null && port1 != null && reserveLabels.Length > 1)
			{
				portBaseY = port0.Bounds.Y;
				portPitch = port1.Bounds.Y - port0.Bounds.Y;
				reservePitch = reserveLabels[1].Bounds.Y - reserveLabels[0].Bounds.Y;

				var declaredPortRows = 0;
				while (panel.GetOrNull<LabelWidget>($"PORT_LABEL_{declaredPortRows}") != null)
					declaredPortRows++;

				portToReserveGap = reserveLabels[0].Bounds.Y - (portBaseY + (portPitch * (declaredPortRows - 1)));
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

			// PANEL VISIBILITY. This MUST be an IsVisible delegate and MUST NOT be a LogicTicker,
			// for the reason CargoPanelLogic.cs carries the same note: Widget.TickOuter is gated on
			// IsVisible() (Widget.cs:512-518), so nothing parented inside a hidden container ticks.
			// A ticker declared as a child of this panel therefore cannot be the thing that shows
			// the panel -- it only runs once the panel is already up. That is exactly how this
			// shipped: `panel.Visible = false` here plus the only other write living inside
			// GARRISON_TICKER.OnTick meant the panel could never appear at all, for any selection.
			// The delegate runs from the PARENT's tick and draw, which is visible, so it is
			// evaluated every frame regardless of what it answered last frame.
			//
			// UpdateSelection is consequently called from a visibility check rather than from a
			// tick. Safe, and for the same reason it is safe in CargoPanelLogic: it is idempotent
			// and self-gates on Selection.Hash, so the several IsVisible() calls a frame makes
			// (TickOuter, DrawOuter, hit-testing, and DefconReadoutWidget.DrawBottom, which names
			// this panel in AvoidPanels) do the work at most once per selection change.
			panel.IsVisible = () =>
			{
				UpdateSelection();
				return selectedGarrison != null;
			};
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

			SeatReserveRows(gm.PortStates.Length);
		}

		/// <summary>
		/// Move the shelter block up under however many firing ports this actor actually has. Called
		/// only on a selection CHANGE (UpdateSelection self-gates on Selection.Hash), so this is not
		/// per-frame layout work.
		/// </summary>
		void SeatReserveRows(int visiblePortRows)
		{
			// Zero when the chrome declared too few rows to derive the geometry from. Leave the
			// declared positions alone rather than seating everything at y=0.
			if (portPitch == 0 || reservePitch == 0)
				return;

			var top = GarrisonPanelMath.ReserveRowTop(portBaseY, portPitch, portToReserveGap, visiblePortRows);

			for (var i = 0; i < reserveLabels.Length; i++)
			{
				var b = reserveLabels[i].Bounds;
				reserveLabels[i].Bounds = new WidgetBounds(b.X, top + (reservePitch * i), b.Width, b.Height);
			}
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

			// While suppressed, the live level replaces the (static) cover figure: it is the number
			// that explains why this port has stopped shooting, and it keeps the row the same width.
			var suppression = SuppressionText(ps.DeployedSoldier, garrisonManager.Info.SuppressionRecallThreshold);
			if (suppression != null)
				return $"{portName}: {unitName}{ammoStr} {suppression}";

			return $"{portName}: {unitName}{ammoStr} {CoverPercent(ps.DeployedSoldier)}% cover";
		}

		/// <summary>
		/// Damage reduction the soldier currently has, derived from its enabled DamageMultiplier
		/// traits rather than assumed. For a port soldier that is DamageMultiplier@GarrisonCover
		/// (infantry.yaml), gated on the garrisoned-at-port condition — so retuning the YAML retunes
		/// this readout instead of silently making it a lie.
		/// </summary>
		static int CoverPercent(Actor soldier)
		{
			// DamageMultiplier.GetDamageModifier ignores both arguments, so this is safe to evaluate
			// outside a real damage event. Do NOT widen this to IDamageModifier: TerrainModifiesDamage
			// dereferences the attacker and would NRE on a null one.
			var multiplier = 100;
			foreach (var dm in soldier.TraitsImplementing<DamageMultiplier>())
				if (!dm.IsTraitDisabled)
					multiplier = multiplier * dm.Info.Modifier / 100;

			return 100 - multiplier;
		}

		/// <summary>
		/// "PINNED n" once suppression has reached the level that currently stops this soldier doing
		/// its job (recall for a port soldier, redeployment for one in shelter), "SUPP n" below that,
		/// null when unsuppressed.
		/// </summary>
		string SuppressionText(Actor soldier, int pinnedThreshold)
		{
			var level = soldier.GetConditionCount(garrisonManager.Info.SuppressionCondition);
			if (level <= 0)
				return null;

			return pinnedThreshold > 0 && level >= pinnedThreshold ? $"PINNED {level}" : $"SUPP {level}";
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

		/// <summary>
		/// THE UNDER-REPORT, and why it is fixed with a summary row rather than more rows.
		/// <para>^CivBuilding is MaxWeight: 10 (civilian.yaml:61) and the chrome declares four shelter
		/// rows, so the panel could show 4 of 10. That fails precisely when it matters most: a soldier
		/// recalled under fire goes back to the shelter and cannot re-man a port until his suppression
		/// decays below SuppressionRedeployThreshold, so a garrison being suppressed is exactly the
		/// state that holds the most men in shelter at once — and the player saw four of them with
		/// nothing saying there were more.</para>
		/// <para>More rows was the other option and does not fit: the body already runs to y=216 in a
		/// 240-high container with EJECT_ALL at y=196, so six more rows needs the container resized,
		/// the shared CARGO_PANEL footprint broken, or both. The last row carrying the remainder costs
		/// no space and cannot overflow at ANY capacity, including one nobody has set yet.</para>
		/// </summary>
		bool IsOverflowRow(int shelterIndex, int shelterCount)
		{
			return GarrisonPanelMath.IsOverflowRow(shelterIndex, shelterCount, reserveLabels.Length);
		}

		string GetShelterText(int shelterIndex)
		{
			if (garrisonManager == null)
				return "";

			var shelter = garrisonManager.ShelterPassengers.ToArray();
			if (shelterIndex >= shelter.Length)
				return "";

			if (IsOverflowRow(shelterIndex, shelter.Length))
			{
				// The denominator is the hold's own capacity: the shelter IS the Cargo hold, and every
				// passenger weighs 1 throughout WW3MOD, so MaxWeight reads as a headcount here exactly
				// as CargoInfo's own tooltip already presents it ("N infantry", Cargo.cs:54).
				var hidden = GarrisonPanelMath.HiddenOccupants(shelterIndex, shelter.Length);
				var capacity = cargo != null ? cargo.Info.MaxWeight : 0;
				var of = capacity > 0 ? $"{shelter.Length}/{capacity}" : $"{shelter.Length}";

				return $"[S] +{hidden} more ({of} in shelter)";
			}

			var pax = shelter[shelterIndex];
			if (pax == null || pax.IsDead)
				return "[S] (dead)";

			var tooltip = pax.TraitOrDefault<Tooltip>();
			var name = tooltip?.Info.Name ?? pax.Info.Name;

			// A soldier recalled under fire keeps its suppression in shelter and cannot man a port
			// again until it decays below SuppressionRedeployThreshold — which is why a port can sit
			// empty with soldiers available. Name that instead of the cover figure while it applies.
			var suppression = SuppressionText(pax, garrisonManager.Info.SuppressionRedeployThreshold);
			if (suppression != null)
				return $"[S] {name} {suppression}";

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
