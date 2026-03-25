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
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Orders
{
	public class UnitOrderGenerator : IOrderGenerator
	{
		readonly string worldSelectCursor = ChromeMetrics.Get<string>("WorldSelectCursor");
		readonly string worldDefaultCursor = ChromeMetrics.Get<string>("WorldDefaultCursor");

		static Target TargetForInput(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			var controlAll = DeveloperMode.IsControlAllUnitsActive(world);
			var actor = world.ScreenMap.ActorsAtMouse(mi)
				.Where(a => !a.Actor.IsDead && a.Actor.Info.HasTraitInfo<ITargetableInfo>() && (controlAll || !world.FogObscures(a.Actor)))
				.WithHighestSelectionPriority(worldPixel, mi.Modifiers);

			if (actor != null)
				return Target.FromActor(actor);

			var frozen = world.ScreenMap.FrozenActorsAtMouse(world.RenderPlayer, mi)
				.Where(a => a.Info.HasTraitInfo<ITargetableInfo>() && a.Visible && a.HasRenderables)
				.WithHighestSelectionPriority(worldPixel, mi.Modifiers);

			if (frozen != null)
				return Target.FromFrozenActor(frozen);

			return Target.FromCell(world, cell);
		}

		public virtual IEnumerable<Order> Order(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			var target = TargetForInput(world, cell, worldPixel, mi);
			var orderResults = world.Selection.Actors
				.Select(a => OrderForUnit(a, target, cell, mi))
				.Where(o => o != null)
				.ToList();

			var actorsInvolved = orderResults.Select(o => o.Actor).Distinct();
			if (!actorsInvolved.Any())
				yield break;

			// Use LocalPlayer for CreateGroup when available (handles mixed-owner selections in control-all mode)
			var groupOwner = world.LocalPlayer?.PlayerActor ?? actorsInvolved.First().Owner.PlayerActor;
			yield return new Order("CreateGroup", groupOwner, false, actorsInvolved.ToArray());

			// Mark non-owned actors as player-controlled so bots don't override our orders
			var controlAllManager = world.WorldActor.TraitOrDefault<ControlAllUnitsManager>();
			if (controlAllManager != null && DeveloperMode.IsControlAllUnitsActive(world))
			{
				foreach (var a in actorsInvolved)
					if (a.Owner != world.LocalPlayer)
						controlAllManager.MarkPlayerControlled(a);
			}

			// Issue orders, grouping Move/AttackMove into grouped orders for formation support.
			// CohesionMoveModifier (IModifyGroupOrder) will assign box formation positions.
			var queued = mi.Modifiers.HasModifier(Modifiers.Shift);
			var moveActors = new List<Actor>();
			var attackMoveActors = new List<Actor>();
			Order moveTemplate = null;
			Order attackMoveTemplate = null;

			foreach (var o in orderResults)
			{
				var issued = CheckSameOrder(o.Order, o.Trait.IssueOrder(o.Actor, o.Order, o.Target, queued));
				if (issued == null)
					continue;

				// Group terrain-targeting Move/AttackMove orders for formation processing
				if (issued.OrderString == "Move" && issued.Target.Type == TargetType.Terrain)
				{
					moveActors.Add(issued.Subject);
					if (moveTemplate == null)
						moveTemplate = issued;
				}
				else if (issued.OrderString == "AttackMove" && issued.Target.Type == TargetType.Terrain)
				{
					attackMoveActors.Add(issued.Subject);
					if (attackMoveTemplate == null)
						attackMoveTemplate = issued;
				}
				else
				{
					// Non-groupable orders pass through individually
					yield return issued;
				}
			}

			// Yield grouped Move order (2+ units) or individual (1 unit)
			if (moveActors.Count > 1)
				yield return new Order("Move", null, moveTemplate.Target, moveTemplate.Queued, null, moveActors.ToArray());
			else if (moveActors.Count == 1)
				yield return new Order("Move", moveActors[0], moveTemplate.Target, moveTemplate.Queued);

			// Yield grouped AttackMove order (2+ units) or individual (1 unit)
			if (attackMoveActors.Count > 1)
				yield return new Order("AttackMove", null, attackMoveTemplate.Target, attackMoveTemplate.Queued, null, attackMoveActors.ToArray());
			else if (attackMoveActors.Count == 1)
				yield return new Order("AttackMove", attackMoveActors[0], attackMoveTemplate.Target, attackMoveTemplate.Queued);
		}

		public virtual void Tick(World world) { }
		public virtual IEnumerable<IRenderable> Render(WorldRenderer wr, World world) { yield break; }
		public virtual IEnumerable<IRenderable> RenderAboveShroud(WorldRenderer wr, World world) { yield break; }
		public virtual IEnumerable<IRenderable> RenderAnnotations(WorldRenderer wr, World world) { yield break; }

		public virtual string GetCursor(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			var target = TargetForInput(world, cell, worldPixel, mi);

			bool useSelect;
			if (Game.Settings.Game.UseClassicMouseStyle && !InputOverridesSelection(world, worldPixel, mi))
				useSelect = target.Type == TargetType.Actor && target.Actor.Info.HasTraitInfo<ISelectableInfo>();
			else
			{
				var ordersWithCursor = world.Selection.Actors
					.Select(a => OrderForUnit(a, target, cell, mi))
					.Where(o => o != null && o.Cursor != null);

				var cursorOrder = ordersWithCursor.MaxByOrDefault(o => o.Order.OrderPriority);
				if (cursorOrder != null)
					return cursorOrder.Cursor;

				useSelect = target.Type == TargetType.Actor && target.Actor.Info.HasTraitInfo<ISelectableInfo>() &&
					(mi.Modifiers.HasModifier(Modifiers.Shift) || !world.Selection.Actors.Any());
			}

			return useSelect ? worldSelectCursor : worldDefaultCursor;
		}

		public void Deactivate() { }

		bool IOrderGenerator.HandleKeyPress(KeyInput e) { return false; }

		public virtual bool InputOverridesSelection(World world, int2 xy, MouseInput mi)
		{
			var controlAll = DeveloperMode.IsControlAllUnitsActive(world);
			var actor = world.ScreenMap.ActorsAtMouse(xy)
				.Where(a => !a.Actor.IsDead && a.Actor.Info.HasTraitInfo<ISelectableInfo>() && (controlAll || a.Actor.Owner.IsAlliedWith(world.RenderPlayer) || !world.FogObscures(a.Actor)))
				.WithHighestSelectionPriority(xy, mi.Modifiers);

			if (actor == null)
				return true;

			var target = Target.FromActor(actor);
			var cell = world.Map.CellContaining(target.CenterPosition);
			var actorsAt = world.ActorMap.GetActorsAt(cell).ToList();

			var modifiers = OpenRA.Traits.TargetModifiers.None;
			var settings = Game.Settings.Game;
			var modsNoShift = mi.Modifiers & ~Modifiers.Shift;
			if (modsNoShift == settings.ForceAttackModifiers)
				modifiers |= TargetModifiers.ForceAttack;
			if (mi.Modifiers.HasModifier(Modifiers.Shift))
				modifiers |= TargetModifiers.ForceQueue;
			if (modsNoShift == settings.ForceMoveModifiers)
				modifiers |= TargetModifiers.ForceMove;
			if (modsNoShift == settings.AttackMoveModifiers && mi.Button == settings.AttackMoveButton)
				modifiers |= TargetModifiers.AttackMove; // Custom modifier for WW3MOD

			foreach (var a in world.Selection.Actors)
			{
				var o = OrderForUnit(a, target, cell, mi);
				if (o != null && o.Order.TargetOverridesSelection(a, target, actorsAt, cell, modifiers))
					return true;
			}

			return false;
		}

		public virtual void SelectionChanged(World world, IEnumerable<Actor> selected) { }

		static UnitOrderResult OrderForUnit(Actor self, Target target, CPos xy, MouseInput mi)
		{
			if (mi.Button != Game.Settings.Game.MouseButtonPreference.Action &&
				!(mi.Button == Game.Settings.Game.AttackMoveButton && (mi.Modifiers & ~Modifiers.Shift) == Game.Settings.Game.AttackMoveModifiers))
				return null;

			if (self.Owner != self.World.LocalPlayer && !DeveloperMode.IsControlAllUnitsActive(self.World))
				return null;

			if (self.World.IsGameOver)
				return null;

			if (self.Disposed || !target.IsValidFor(self))
				return null;

			var settings = Game.Settings.Game;
			var modifiers = TargetModifiers.None;
			var modsNoShift = mi.Modifiers & ~Modifiers.Shift;
			if (modsNoShift == settings.ForceAttackModifiers)
				modifiers |= TargetModifiers.ForceAttack;
			if (mi.Modifiers.HasModifier(Modifiers.Shift))
				modifiers |= TargetModifiers.ForceQueue;
			if (modsNoShift == settings.ForceMoveModifiers)
				modifiers |= TargetModifiers.ForceMove;
			if (modsNoShift == settings.AttackMoveModifiers && mi.Button == settings.AttackMoveButton)
				modifiers |= TargetModifiers.AttackMove; // Custom modifier for WW3MOD

			var actorsAt = self.World.ActorMap.GetActorsAt(xy).ToList();
			var orders = self.TraitsImplementing<IIssueOrder>()
				.SelectMany(trait => trait.Orders.Select(x => new { Trait = trait, Order = x }))
				.OrderByDescending(x => x.Order.OrderPriority);

			for (var i = 0; i < 2; i++)
			{
				foreach (var o in orders)
				{
					string cursor = null;
					if (o.Order.CanTarget(self, target, actorsAt, xy, modifiers, ref cursor))
						return new UnitOrderResult(self, o.Order, o.Trait, cursor, target);
				}

				target = Target.FromCell(self.World, xy);
			}

			return null;
		}

		static Order CheckSameOrder(IOrderTargeter iot, Order order)
		{
			if (order == null && iot.OrderID != null)
				TextNotificationsManager.Debug("BUG: in order targeter - decided on {0} but then didn't order", iot.OrderID);
			else if (order != null && iot.OrderID != order.OrderString)
				TextNotificationsManager.Debug("BUG: in order targeter - decided on {0} but ordered {1}", iot.OrderID, order.OrderString);
			return order;
		}

		class UnitOrderResult
		{
			public readonly Actor Actor;
			public readonly IOrderTargeter Order;
			public readonly IIssueOrder Trait;
			public readonly string Cursor;
			public ref readonly Target Target => ref target;

			readonly Target target;

			public UnitOrderResult(Actor actor, IOrderTargeter order, IIssueOrder trait, string cursor, in Target target)
			{
				Actor = actor;
				Order = order;
				Trait = trait;
				Cursor = cursor;
				this.target = target;
			}
		}

		public virtual bool ClearSelectionOnLeftClick => true;
	}
}
