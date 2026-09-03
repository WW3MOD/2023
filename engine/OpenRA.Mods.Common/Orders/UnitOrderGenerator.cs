#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
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
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Orders
{
	public class UnitOrderGenerator : IOrderGenerator
	{
		readonly string worldSelectCursor = ChromeMetrics.Get<string>("WorldSelectCursor");
		readonly string worldDefaultCursor = ChromeMetrics.Get<string>("WorldDefaultCursor");

		protected static Target TargetForInput(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			var controlAll = DeveloperMode.IsControlAllUnitsActive(world);
			var actor = world.ScreenMap.ActorsAtMouse(mi)
				.Where(a => !a.Actor.IsDead && a.Actor.Info.HasTraitInfo<ITargetableInfo>()
					&& (controlAll || a.Actor.IsRevealedForMouseInput(world)))
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

		/// <summary>
		/// The seam where a click nobody in the selection answered can instead pick its own capture unit
		/// — see CaptureDispatchManager. Returns null unless the world opts in by carrying that trait,
		/// which keeps every other mod on the stock behaviour.
		/// </summary>
		/// <remarks>
		/// Frozen actors are admitted as well as live ones. An enemy structure is usually under fog, and
		/// the stock capture targeter already accepts one (Captures.CaptureOrderTargeter
		/// .CanTargetFrozenActor), so refusing it here would make the dispatch gesture the only way of
		/// ordering a capture that stops working at the shroud line.
		/// </remarks>
		static CaptureDispatchManager DispatcherFor(World world, in Target target)
		{
			if (target.Type != TargetType.Actor && target.Type != TargetType.FrozenActor)
				return null;

			return world.WorldActor.TraitOrDefault<CaptureDispatchManager>();
		}

		/// <summary>
		/// Orders for a click the selection had no answer for. Gated on the RESOLVED ORDER COUNT rather
		/// than on the selection being empty: see CaptureDispatchMath.SelectionYieldsToDispatch for why
		/// those are different tests, and why this one is what lets a selected structure be clicked.
		/// </summary>
		/// <remarks>
		/// Public so the scripted test API can resolve a click through the SAME decision the mouse uses.
		/// A scenario that re-implemented this rule would keep passing after the rule changed, which is
		/// the one thing an autotest for this gesture must not do.
		/// </remarks>
		public static List<Order> DispatchOrders(World world, in Target target, List<UnitOrderResult> orderResults, bool queued)
		{
			if (!CaptureDispatchMath.SelectionYieldsToDispatch(orderResults.Count))
				return null;

			var dispatcher = DispatcherFor(world, target);
			if (dispatcher == null)
				return null;

			var dispatched = target.Type == TargetType.FrozenActor
				? dispatcher.DispatchAt(world, target.FrozenActor, queued).ToList()
				: dispatcher.DispatchAt(world, target.Actor, queued).ToList();

			return dispatched.Count > 0 ? dispatched : null;
		}

		public virtual IEnumerable<Order> Order(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			var target = TargetForInput(world, cell, worldPixel, mi);

			var orderResults = OrdersForSelection(world.Selection.Actors, target, cell, mi);

			// Attempted only once the selection has been resolved and produced nothing, so a selection
			// that CAN act on the target — combat units on an enemy building — keeps its own order and
			// is never overridden by a dispatch.
			var dispatched = DispatchOrders(world, target, orderResults, mi.Modifiers.HasModifier(Modifiers.Shift));
			if (dispatched != null)
			{
				foreach (var o in dispatched)
					yield return o;

				yield break;
			}

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
				// Resolved for the whole selection, exactly as the click will be: the cursor has to
				// name the order the player is about to get, or the two disagree and the pointer
				// stops meaning anything.
				var orderResults = OrdersForSelection(world.Selection.Actors, target, cell, mi);
				var cursor = CursorForOrders(orderResults);
				if (cursor != null)
					return cursor;

				// The selection answered nothing, so the pointer is free to name the dispatch instead of
				// the select it would otherwise show. Gated on the same count Order() dispatches on, so
				// the two cannot disagree about whether this click is a dispatch.
				if (CaptureDispatchMath.SelectionYieldsToDispatch(orderResults.Count))
				{
					var dispatcher = DispatcherFor(world, target);
					if (dispatcher != null)
					{
						var state = target.Type == TargetType.FrozenActor
							? dispatcher.Evaluate(world, target.FrozenActor, out _)
							: dispatcher.Evaluate(world, target.Actor, out _);

						var dispatchCursor = dispatcher.CursorForState(state);
						if (dispatchCursor != null)
							return dispatchCursor;
					}
				}

				useSelect = target.Type == TargetType.Actor && target.Actor.Info.HasTraitInfo<ISelectableInfo>() &&
					(mi.Modifiers.HasModifier(Modifiers.Shift) || world.Selection.Actors.Count == 0);
			}

			return useSelect ? worldSelectCursor : worldDefaultCursor;
		}

		public void Deactivate() { }

		bool IOrderGenerator.HandleKeyPress(KeyInput e) { return false; }

		public virtual bool InputOverridesSelection(World world, int2 xy, MouseInput mi)
		{
			var controlAll = DeveloperMode.IsControlAllUnitsActive(world);
			var actor = world.ScreenMap.ActorsAtMouse(xy)
				.Where(a => !a.Actor.IsDead && a.Actor.Info.HasTraitInfo<ISelectableInfo>()
					&& (controlAll || a.Actor.Owner.IsAlliedWith(world.RenderPlayer)
						|| a.Actor.IsRevealedForMouseInput(world)))
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

			foreach (var o in OrdersForSelection(world.Selection.Actors, target, cell, mi))
				if (o.Order.TargetOverridesSelection(o.Actor, target, actorsAt, cell, modifiers))
					return true;

			return false;
		}

		public virtual void SelectionChanged(World world, IEnumerable<Actor> selected) { }

		/// <summary>
		/// Resolves one click for a whole selection, and is the only entry point the mouse should use.
		/// </summary>
		/// <remarks>
		/// A unit that cannot carry out the click gets no order rather than a walk into the target —
		/// but that is a rule about a SPECIFIC order some of the selection is carrying out, so it only
		/// applies while somebody is. When nothing selected accepted the click, it re-resolves with the
		/// default order permitted for everyone: the player gets the move they asked for, and — because
		/// GetCursor comes through here too — a cursor that says so beforehand.
		/// </remarks>
		public static List<UnitOrderResult> OrdersForSelection(IEnumerable<Actor> actors, Target target, CPos xy, MouseInput mi)
		{
			return ResolveSelection(actors, (a, allowRelocation) => OrderForUnit(a, target, xy, mi, allowRelocation));
		}

		/// <summary>
		/// <see cref="OrdersForSelection(IEnumerable{Actor}, Target, CPos, MouseInput)"/> for callers
		/// that already hold the modifiers — the scripted test API, which has no mouse.
		/// </summary>
		public static List<UnitOrderResult> OrdersForSelection(IEnumerable<Actor> actors, Target target, CPos xy, TargetModifiers modifiers)
		{
			return ResolveSelection(actors, (a, allowRelocation) => OrderForUnit(a, target, xy, modifiers, allowRelocation));
		}

		// The selection rule itself, shared by both overloads so the two entry points cannot drift.
		static List<UnitOrderResult> ResolveSelection(IEnumerable<Actor> actors, Func<Actor, bool, UnitOrderResult> resolve)
		{
			var results = actors.Select(a => resolve(a, false)).Where(o => o != null).ToList();
			if (OrderFallbackMath.SelectionSuppressesRefusers(results.Count))
				return results;

			return actors.Select(a => resolve(a, true)).Where(o => o != null).ToList();
		}

		/// <summary>
		/// The cursor for an already-resolved selection: the highest-priority order that names one.
		/// </summary>
		public static string CursorForOrders(IEnumerable<UnitOrderResult> results)
		{
			return results.Where(o => o.Cursor != null)
				.MaxByOrDefault(o => o.Order.OrderPriority)?.Cursor;
		}

		static UnitOrderResult OrderForUnit(Actor self, Target target, CPos xy, MouseInput mi, bool allowRelocationOntoEnemy)
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

			return OrderForUnit(self, target, xy, modifiers, allowRelocationOntoEnemy);
		}

		/// <summary>
		/// Resolves the click on <paramref name="target"/> against this actor's targeter chain, in
		/// descending OrderPriority, exactly as a mouse click does. Returns null when the actor
		/// refuses the click outright — which for a hostile target it cannot engage is the normal
		/// outcome, rather than a Move order onto its cell.
		/// </summary>
		/// <param name="self">The actor resolving the click.</param>
		/// <param name="target">What was clicked on.</param>
		/// <param name="xy">The cell under the cursor, used to look up what else is standing there.</param>
		/// <param name="modifiers">The target modifiers in effect for this click.</param>
		/// <param name="allowRelocationOntoEnemy">
		/// Set by <see cref="ResolveSelection"/>, behind both <c>OrdersForSelection</c> overloads, when
		/// no other unit in the selection accepted the click, which reopens the default order for this
		/// one. Callers resolving a single unit in isolation want the default, false.
		/// </param>
		public static UnitOrderResult OrderForUnit(Actor self, in Target target, CPos xy, TargetModifiers modifiers,
			bool allowRelocationOntoEnemy = false)
		{
			if (self.Disposed || !target.IsValidFor(self))
				return null;

			var actorsAt = self.World.ActorMap.GetActorsAt(xy).ToList();
			var orders = self.TraitsImplementing<IIssueOrder>()
				.SelectMany(trait => trait.Orders.Select(x => new { Trait = trait, Order = x }))
				.OrderByDescending(x => x.Order.OrderPriority);

			// Whether an order that drives this unit ONTO the clicked cell is acceptable.
			var relocationAllowed = allowRelocationOntoEnemy || AllowsMoveFallback(self, target, modifiers);

			var candidate = target;
			for (var i = 0; i < 2; i++)
			{
				foreach (var o in orders)
				{
					string cursor = null;
					if (!o.Order.CanTarget(self, candidate, actorsAt, xy, modifiers, ref cursor))
						continue;

					// PITFALL: the second pass is the only route to ANY order against a cell an actor
					// occupies — every terrain-only targeter, Move and AttackMove among them, but also
					// force-fire at the ground. So it always runs and the RESULT is gated instead;
					// skipping the pass took out force-attack-ground under an untargetable enemy.
					// Reasoning in OrderFallbackMath.
					if (i == 1 && !OrderFallbackMath.AllowsRetryResult(o.Order.OrderID, relocationAllowed))
						continue;

					return new UnitOrderResult(self, o.Order, o.Trait, cursor, candidate);
				}

				candidate = Target.FromCell(self.World, xy);
			}

			return null;
		}

		static bool AllowsMoveFallback(Actor self, in Target target, TargetModifiers modifiers)
		{
			Player owner = null;
			if (target.Type == TargetType.Actor)
				owner = target.Actor.Owner;
			else if (target.Type == TargetType.FrozenActor)
				owner = target.FrozenActor.Owner;

			return OrderFallbackMath.AllowsMoveFallback(
				owner != null,
				owner != null ? self.Owner.RelationshipWith(owner) : PlayerRelationship.None,
				modifiers);
		}

		static Order CheckSameOrder(IOrderTargeter iot, Order order)
		{
			if (order == null && iot.OrderID != null)
				Log.Write("debug", $"BUG: in order targeter - decided on {iot.OrderID} but then didn't order");
			else if (order != null && iot.OrderID != order.OrderString)
				Log.Write("debug", $"BUG: in order targeter - decided on {iot.OrderID} but ordered {order.OrderString}");
			return order;
		}

		public sealed class UnitOrderResult
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
