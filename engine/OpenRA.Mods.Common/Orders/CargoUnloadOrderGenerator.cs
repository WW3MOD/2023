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
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Orders;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Orders
{
	/// <summary>
	/// Order generator for waypoint-based selective cargo unloading.
	/// Left-click on map to assign marked passengers to that waypoint.
	/// The transport moves to each waypoint and unloads designated passengers.
	/// Right-click or Escape cancels.
	/// </summary>
	public class CargoUnloadOrderGenerator : IOrderGenerator
	{
		readonly Actor transport;
		readonly Func<HashSet<uint>> getMarkedIds;
		readonly Action<HashSet<uint>> clearMarkedIds;
		readonly List<WaypointAssignment> assignedWaypoints = new List<WaypointAssignment>();

		struct WaypointAssignment
		{
			public CPos Cell;
			public uint[] PassengerIds;
		}

		public CargoUnloadOrderGenerator(Actor transport, Func<HashSet<uint>> getMarkedIds, Action<HashSet<uint>> clearMarkedIds)
		{
			this.transport = transport;
			this.getMarkedIds = getMarkedIds;
			this.clearMarkedIds = clearMarkedIds;
		}

		public IEnumerable<Order> Order(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			if (mi.Button == MouseButton.Right)
			{
				// Right-click cancels without issuing orders
				world.CancelInputMode();
				yield break;
			}

			if (mi.Button == MouseButton.Left && mi.Event == MouseInputEvent.Down)
			{
				var clampedCell = world.Map.Clamp(cell);
				if (!world.Map.Contains(clampedCell))
					yield break;

				var marked = getMarkedIds();
				if (marked == null || marked.Count == 0)
				{
					// No passengers marked — just cancel
					world.CancelInputMode();
					yield break;
				}

				// Snapshot current marked IDs and assign to this waypoint
				var snapshot = marked.ToArray();
				assignedWaypoints.Add(new WaypointAssignment { Cell = clampedCell, PassengerIds = snapshot });

				// Clear the marks so user can mark different passengers for next waypoint
				clearMarkedIds(marked);

				// Issue all waypoint orders now
				IssueWaypointOrders(world);

				// Done — exit order generator
				world.CancelInputMode();
			}

			yield break;
		}

		void IssueWaypointOrders(World world)
		{
			if (transport.IsDead || !transport.IsInWorld)
				return;

			var cargo = transport.TraitOrDefault<Cargo>();
			if (cargo == null)
				return;

			var isFirst = true;
			foreach (var wp in assignedWaypoints)
			{
				// Move to waypoint location
				var target = Target.FromCell(world, wp.Cell);
				world.IssueOrder(new Order("Move", transport, target, !isFirst));

				// Queue unload orders for each assigned passenger (queued=true so they execute after move)
				foreach (var id in wp.PassengerIds)
				{
					world.IssueOrder(new Order("UnloadCargoPassenger", transport, true)
						{ ExtraData = id });
				}

				isFirst = false;
			}
		}

		public void Tick(World world)
		{
			if (transport.IsDead || !transport.IsInWorld)
				world.CancelInputMode();
		}

		public IEnumerable<IRenderable> Render(WorldRenderer wr, World world) { yield break; }
		public IEnumerable<IRenderable> RenderAboveShroud(WorldRenderer wr, World world) { yield break; }

		public IEnumerable<IRenderable> RenderAnnotations(WorldRenderer wr, World world)
		{
			if (transport.IsDead || !transport.IsInWorld)
				yield break;

			var color = Color.FromArgb(255, 0, 200, 255); // Cyan-ish

			// Draw lines from transport to first waypoint, then between waypoints
			var points = new List<WPos> { transport.CenterPosition };
			foreach (var wp in assignedWaypoints)
				points.Add(world.Map.CenterOfCell(wp.Cell));

			for (var i = 0; i < points.Count - 1; i++)
				yield return new TargetLineRenderable(new[] { points[i], points[i + 1] }, color, 1, 1);
		}

		public string GetCursor(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			return "deploy";
		}

		public void Deactivate() { }

		public bool HandleKeyPress(KeyInput e)
		{
			if (e.Event == KeyInputEvent.Down && e.Key == Keycode.ESCAPE)
				return false; // Let default handler cancel
			return false;
		}

		public void SelectionChanged(World world, IEnumerable<Actor> selected)
		{
			// If user deselects the transport, cancel
			if (!selected.Contains(transport))
				world.CancelInputMode();
		}
	}
}
