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
using OpenRA.Graphics;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Orders;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Orders
{
	/// <summary>
	/// Order generator for patrol waypoint queuing.
	/// Click to add waypoints, click Patrol button again to confirm.
	/// Escape or right-click cancels.
	/// </summary>
	public class PatrolOrderGenerator : IOrderGenerator
	{
		readonly List<CPos> waypoints = new List<CPos>();
		Actor[] subjects;
		bool confirmed;

		public bool HasWaypoints => waypoints.Count > 0;
		public bool IsConfirmed => confirmed;

		public PatrolOrderGenerator(IEnumerable<Actor> subjects)
		{
			this.subjects = subjects.Where(a => !a.IsDead && a.IsInWorld).ToArray();
		}

		/// <summary>Called when player confirms the patrol route (clicks Patrol button again).</summary>
		public void Confirm(World world)
		{
			if (waypoints.Count < 2)
			{
				world.CancelInputMode();
				return;
			}

			confirmed = true;

			var waypointArray = waypoints.ToArray();
			foreach (var actor in subjects)
			{
				if (actor.IsDead || !actor.IsInWorld)
					continue;

				actor.CancelActivity();
				actor.QueueActivity(false, new PatrolActivity(actor, waypointArray));
				actor.ShowTargetLines();
			}

			world.CancelInputMode();
		}

		public IEnumerable<Order> Order(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			if (mi.Button == MouseButton.Right)
			{
				// Right-click cancels
				world.CancelInputMode();
				yield break;
			}

			if (mi.Button == MouseButton.Left && mi.Event == MouseInputEvent.Down)
			{
				// Add waypoint
				var clampedCell = world.Map.Clamp(cell);
				if (world.Map.Contains(clampedCell))
					waypoints.Add(clampedCell);
			}

			yield break;
		}

		public void Tick(World world)
		{
			// If all subjects are dead or selection changed, cancel
			subjects = subjects.Where(a => !a.IsDead && a.IsInWorld).ToArray();
			if (subjects.Length == 0)
				world.CancelInputMode();
		}

		public IEnumerable<IRenderable> Render(WorldRenderer wr, World world) { yield break; }
		public IEnumerable<IRenderable> RenderAboveShroud(WorldRenderer wr, World world) { yield break; }

		public IEnumerable<IRenderable> RenderAnnotations(WorldRenderer wr, World world)
		{
			if (waypoints.Count == 0)
				yield break;

			var color = Color.Cyan;

			// Draw lines between waypoints
			for (var i = 0; i < waypoints.Count - 1; i++)
			{
				var from = world.Map.CenterOfCell(waypoints[i]);
				var to = world.Map.CenterOfCell(waypoints[i + 1]);
				yield return new TargetLineRenderable(new[] { from, to }, color, 1, 1);
			}

			// Draw line from last waypoint back to first if close enough (circular indicator)
			if (waypoints.Count >= 3)
			{
				var first = waypoints[0];
				var last = waypoints[waypoints.Count - 1];
				var dx = last.X - first.X;
				var dy = last.Y - first.Y;
				if (dx * dx + dy * dy <= 4)
				{
					var from = world.Map.CenterOfCell(last);
					var to = world.Map.CenterOfCell(first);
					yield return new TargetLineRenderable(new[] { from, to }, Color.LightCyan, 1, 1);
				}
			}
		}

		public string GetCursor(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			return "attackmove"; // Reuse the attack-move cursor for patrol waypoints
		}

		public void Deactivate() { }

		public bool HandleKeyPress(KeyInput e)
		{
			if (e.Event == KeyInputEvent.Down && e.Key == Keycode.ESCAPE)
				return false; // Let the default handler cancel input mode

			return false;
		}

		public void SelectionChanged(World world, IEnumerable<Actor> selected)
		{
			subjects = selected.Where(a => !a.IsDead && a.IsInWorld).ToArray();
			if (subjects.Length == 0)
				world.CancelInputMode();
		}
	}
}
