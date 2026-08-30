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
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Orders;
using OpenRA.Primitives;
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("This actor can enter Cargo actors.")]
	public class PassengerInfo : TraitInfo, IObservesVariablesInfo
	{
		public readonly string CargoType = null;

		[Desc("If defined, use a custom pip type defined on the transport's WithCargoPipsDecoration.CustomPipSequences list.")]
		public readonly string CustomPipType = null;

		public readonly int Weight = 1;

		[GrantedConditionReference]
		[Desc("The condition to grant to when this actor is loaded inside any transport.")]
		public readonly string CargoCondition = null;

		[ActorReference(dictionaryReference: LintDictionaryReference.Keys)]
		[Desc("Conditions to grant when this actor is loaded inside specified transport.",
			"A dictionary of [actor name]: [condition].")]
		public readonly Dictionary<string, string> CargoConditions = new Dictionary<string, string>();

		[GrantedConditionReference]
		public IEnumerable<string> LinterCargoConditions => CargoConditions.Values;

		[VoiceReference]
		public readonly string Voice = "Action";

		[Desc("Color to use for the target line.")]
		public readonly Color TargetLineColor = Color.Green;

		[ConsumedConditionReference]
		[Desc("Boolean expression defining the condition under which the regular (non-force) enter cursor is disabled.")]
		public readonly BooleanExpression RequireForceMoveCondition = null;

		[Desc("Role hint for garrison targeting: General, MachineGunner, AntiTank, AntiAir, Sniper, Support.")]
		public readonly string GarrisonRole = "General";

		[CursorReference]
		[Desc("Cursor to display when able to enter target actor.")]
		public readonly string EnterCursor = "enter";

		[CursorReference]
		[Desc("Cursor to display when unable to enter target actor.")]
		public readonly string EnterBlockedCursor = "enter-blocked";

		public override object Create(ActorInitializer init) { return new Passenger(this); }
	}

	public class Passenger : IIssueOrder, IResolveOrder, IOrderVoice, INotifyRemovedFromWorld, INotifyEnteredCargo, INotifyExitedCargo, INotifyKilled, IObservesVariables
	{
		public readonly PassengerInfo Info;
		public Actor Transport;
		bool requireForceMove;

		int anyCargoToken = Actor.InvalidConditionToken;
		int specificCargoToken = Actor.InvalidConditionToken;

		public Passenger(PassengerInfo info)
		{
			Info = info;
		}

		public Cargo ReservedCargo { get; private set; }

		IEnumerable<IOrderTargeter> IIssueOrder.Orders
		{
			get
			{
				yield return new EnterAlliedActorTargeter<CargoInfo>(
					"EnterTransport",
					5,
					Info.EnterCursor,
					Info.EnterBlockedCursor,
					IsCorrectCargoType,
					CanEnter);
			}
		}

		public Order IssueOrder(Actor self, IOrderTargeter order, in Target target, bool queued)
		{
			if (order.OrderID == "EnterTransport")
				return new Order(order.OrderID, self, target, queued);

			return null;
		}

		bool IsCorrectCargoType(Actor target, TargetModifiers modifiers)
		{
			if (requireForceMove && !modifiers.HasModifier(TargetModifiers.ForceMove))
				return false;

			// DELIBERATELY NOT `&& CanEnter(target)`. A full transport still passes targeting, still
			// consumes the click at priority 5, and ResolveOrder then drops it — so right-clicking a
			// full APC does nothing at all, and the dead click plus `enter-blocked` art IS the
			// feedback that tells the player the transport is full.
			//
			// USER RULING, 2026-08-30 — considered and ruled against, do not re-propose without
			// reading this. Folding CanEnter in here was built on this branch so the refused click
			// would fall through to a Move instead. It was reverted because it is the same shape as
			// a shipped rule — an order must never silently become a move order — and because it
			// spent the only feedback the player has: after it, the unit drove over and stopped,
			// which reads as a move the player asked for rather than "that one is full".
			//
			// Secondary reason, recorded because it is not obvious from here:
			// EnterAlliedActorTargeter.CanTargetFrozenActor passes the REAL actor to this predicate,
			// so anything consulted here is also consulted for a FOGGED transport. Adding an
			// occupancy term therefore widened a fog leak. The pre-existing LoadingBlocked leak on
			// the same path is filed separately — do not chase it from here.
			return IsCorrectCargoType(target);
		}

		bool IsCorrectCargoType(Actor target)
		{
			var cargo = target.TraitOrDefault<Cargo>();
			if (cargo != null && cargo.LoadingBlocked)
				return false;

			var ci = target.Info.TraitInfo<CargoInfo>();
			return ci.Types.Contains(Info.CargoType);
		}

		bool CanEnter(Cargo cargo)
		{
			return cargo != null && !cargo.LoadingBlocked && cargo.HasSpace(Info.Weight);
		}

		bool CanEnter(Actor target)
		{
			return CanEnter(target.TraitOrDefault<Cargo>());
		}

		Actor GetActor(Target target)
		{
			if (target.FrozenActor != null)
			{
				return target.FrozenActor.Actor;
			}

			return target.Actor;
		}

		public string VoicePhraseForOrder(Actor self, Order order)
		{
			if (order.OrderString != "EnterTransport")
				return null;

			if (order.Target.Type != TargetType.Actor || !CanEnter(order.Target.Actor))
				return null;

			return Info.Voice;
		}

		void INotifyEnteredCargo.OnEnteredCargo(Actor self, Actor cargo)
		{
			if (anyCargoToken == Actor.InvalidConditionToken)
				anyCargoToken = self.GrantCondition(Info.CargoCondition);

			if (specificCargoToken == Actor.InvalidConditionToken && Info.CargoConditions.TryGetValue(cargo.Info.Name, out var specificCargoCondition))
				specificCargoToken = self.GrantCondition(specificCargoCondition);

			// Allow scripted / initial actors to move from the unload point back into the cell grid on unload
			// This is handled by the RideTransport activity for player-loaded cargo
			if (self.IsIdle)
			{
				// IMove is not used anywhere else in this trait, there is no benefit to caching it from Created.
				var move = self.TraitOrDefault<IMove>();
				if (move != null)
					self.QueueActivity(move.ReturnToCell(self));
			}
		}

		void INotifyExitedCargo.OnExitedCargo(Actor self, Actor cargo)
		{
			if (anyCargoToken != Actor.InvalidConditionToken)
				anyCargoToken = self.RevokeCondition(anyCargoToken);

			if (specificCargoToken != Actor.InvalidConditionToken)
				specificCargoToken = self.RevokeCondition(specificCargoToken);
		}

		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString != "EnterTransport")
				return;

			// The order can resolve a tick or more after it was issued (orders travel through
			// the net queue). By then the target transport may have died or left the world — a
			// bot ferry (or a human) can issue EnterTransport against a carrier that dies before
			// it resolves. For an Actor target the Type property already reads Invalid once the
			// actor is dead/out-of-world; for a FrozenActor target GetActor can return null when
			// the real actor isn't revealed. Guard both before dereferencing, so ResolveOrder
			// never crashes regardless of issuer. Mirrors CrewMember.ResolveOrder.
			if (order.Target.Type != TargetType.Actor && order.Target.Type != TargetType.FrozenActor)
				return;

			var targetActor = GetActor(order.Target);
			if (targetActor == null || targetActor.IsDead || !targetActor.IsInWorld)
				return;

			if (!CanEnter(targetActor))
				return;

			if (!IsCorrectCargoType(targetActor))
				return;

			self.QueueActivity(order.Queued, new RideTransport(self, order.Target, Info.TargetLineColor));
			self.ShowTargetLines();
		}

		public bool Reserve(Actor self, Cargo cargo)
		{
			if (cargo == ReservedCargo)
				return true;

			Unreserve(self);
			if (!cargo.ReserveSpace(self))
				return false;

			ReservedCargo = cargo;
			return true;
		}

		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self) { Unreserve(self); }

		public void Unreserve(Actor self)
		{
			if (ReservedCargo == null)
				return;

			ReservedCargo.UnreserveSpace(self);
			ReservedCargo = null;
		}

		void INotifyKilled.Killed(Actor self, AttackInfo e)
		{
			if (Transport == null)
				return;

			// Something killed us, but it wasn't our transport blowing up. Remove us from the cargo.
			if (!Transport.IsDead)
				Transport.Trait<Cargo>().Unload(Transport, self);
		}

		IEnumerable<VariableObserver> IObservesVariables.GetVariableObservers()
		{
			if (Info.RequireForceMoveCondition != null)
				yield return new VariableObserver(RequireForceMoveConditionChanged, Info.RequireForceMoveCondition.Variables);
		}

		void RequireForceMoveConditionChanged(Actor self, IReadOnlyDictionary<string, int> conditions)
		{
			requireForceMove = Info.RequireForceMoveCondition.Evaluate(conditions);
		}
	}
}
