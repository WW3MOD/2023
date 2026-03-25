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
using OpenRA.Mods.Common.Traits;
using OpenRA.Orders;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Orders
{
	/// <summary>
	/// Order generator for setting a post-eject rally point for a specific cargo passenger.
	/// Left-click on map/actor to set rally. Right-click or Escape cancels.
	/// </summary>
	public class EjectRallyOrderGenerator : IOrderGenerator
	{
		readonly Actor transport;
		readonly uint passengerActorId;
		readonly string passengerName;

		public EjectRallyOrderGenerator(Actor transport, uint passengerActorId, string passengerName)
		{
			this.transport = transport;
			this.passengerActorId = passengerActorId;
			this.passengerName = passengerName;
		}

		public IEnumerable<Order> Order(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			if (mi.Button == MouseButton.Right)
			{
				world.CancelInputMode();
				yield break;
			}

			if (mi.Button == MouseButton.Left && mi.Event == MouseInputEvent.Down)
			{
				var clampedCell = world.Map.Clamp(cell);
				if (!world.Map.Contains(clampedCell))
					yield break;

				var cargo = transport.TraitOrDefault<Cargo>();
				if (cargo == null)
				{
					world.CancelInputMode();
					yield break;
				}

				// Set rally point as cell target
				var target = Target.FromCell(world, clampedCell);
				cargo.SetEjectRally(passengerActorId, target);

				world.CancelInputMode();
			}

			yield break;
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

			// Draw a line from transport to indicate rally direction
			var cargo = transport.TraitOrDefault<Cargo>();
			if (cargo == null || !cargo.HasEjectRally(passengerActorId))
				yield break;

			var rally = cargo.GetEjectRally(passengerActorId);
			if (rally.Type != TargetType.Invalid)
			{
				yield return new TargetLineRenderable(
					new[] { transport.CenterPosition, rally.CenterPosition },
					Color.FromArgb(255, 0, 255, 128), 1, 1);
			}
		}

		public string GetCursor(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			return "move";
		}

		public void Deactivate() { }

		public bool HandleKeyPress(KeyInput e)
		{
			if (e.Event == KeyInputEvent.Down && e.Key == Keycode.ESCAPE)
				return false;
			return false;
		}

		public void SelectionChanged(World world, IEnumerable<Actor> selected)
		{
			if (!selected.Contains(transport))
				world.CancelInputMode();
		}
	}
}
