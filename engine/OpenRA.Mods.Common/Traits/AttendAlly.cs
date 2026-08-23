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

using System.Collections.Generic;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Orders;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("The player can order this unit to attend a friendly one: walk to him, stay with him, and",
		"treat him whenever he is hurt. Exists because a healer's weapon can only target the wounded,",
		"so clicking an unhurt ally produced no order at all — the unit looked broken.")]
	public class AttendAllyInfo : TraitInfo, Requires<IMoveInfo>
	{
		[Desc("Target types that can be attended. These are the ally's ordinary target types, NOT the",
			"healer's Heal type — the whole point is that the order works on an undamaged ally.")]
		public readonly BitSet<TargetableType> ValidTargets = default;

		[Desc("How close to trail the attended ally. Keep this inside the healer's weapon range, or he",
			"will stand next to his patient without ever being close enough to treat him.")]
		public readonly WDist Range = WDist.FromCells(1);

		[Desc("Order priority. Above AttackBase's targeter (6) so that ordering a healer onto a wounded",
			"ally attends him — stays with him afterwards — instead of issuing a one-shot heal.")]
		public readonly int OrderPriority = 7;

		[Desc("Ticks without changing cell before a healer stops holding his ordered patient exclusively.",
			"The ORDER survives — the follow keeps trying and the patient is taken back the moment ground",
			"is made again — but while stuck he treats whoever else needs it instead of nobody. Mobile's",
			"MoveResult is never assigned, so an unpathable follow never reports failure and this is the",
			"only evidence available. 0 disables the fallback and leaves an unreachable patient holding",
			"the healer for as long as the order stands.")]
		public readonly int MaxStalledTicks = 100;

		public readonly string Cursor = "heal";

		[VoiceReference]
		public readonly string Voice = "Action";

		[Desc("Color to use for the target line.")]
		public readonly Color TargetLineColor = Color.LimeGreen;

		public override object Create(ActorInitializer init) { return new AttendAlly(this); }
	}

	public class AttendAlly : IIssueOrder, IResolveOrder, IOrderVoice, INotifyCreated
	{
		const string OrderName = "AttendAlly";

		readonly AttendAllyInfo info;
		IMove move;

		public AttendAlly(AttendAllyInfo info)
		{
			this.info = info;
		}

		void INotifyCreated.Created(Actor self)
		{
			move = self.Trait<IMove>();
		}

		IEnumerable<IOrderTargeter> IIssueOrder.Orders
		{
			get
			{
				yield return new AttendAllyOrderTargeter(info.ValidTargets, OrderName, info.OrderPriority, info.Cursor);
			}
		}

		/// <summary>Allies only, and genuinely only allies.
		/// PITFALL: <see cref="UnitOrderTargeter"/>'s own relationship checks are BOTH skipped when the
		/// ForceAttack modifier is held, and neither of them tests Neutral in the first place. Passing
		/// targetEnemyUnits: false is therefore not enough on its own — since this targeter outranks
		/// AttackBase's, a Ctrl+Alt click would otherwise send an unarmed healer to stand one cell from
		/// an enemy and hold him there. The relationship is re-checked here, where nothing can skip it.</summary>
		sealed class AttendAllyOrderTargeter : TargetTypeOrderTargeter
		{
			public AttendAllyOrderTargeter(BitSet<TargetableType> targetTypes, string order, int priority, string cursor)
				: base(targetTypes, order, priority, cursor, false, true)
			{
				// Refuse outright while ForceAttack is held, rather than relying on checks it bypasses.
				ForceAttack = false;
			}

			public override bool CanTargetActor(Actor self, Actor target, TargetModifiers modifiers, ref string cursor)
			{
				if (target == self || !self.Owner.IsAlliedWith(target.Owner))
					return false;

				return base.CanTargetActor(self, target, modifiers, ref cursor);
			}

			public override bool CanTargetFrozenActor(Actor self, FrozenActor target, TargetModifiers modifiers, ref string cursor)
			{
				return false;
			}

			public override bool TargetOverridesSelection(Actor self, in Target target, List<Actor> actorsAt, CPos xy, TargetModifiers modifiers)
			{
				// Same rule Mobile uses: a plain click on a friendly who is not already selected should
				// still SELECT him. Only override selection once he is selected (so the player is plainly
				// giving an order about him) or when force-move is held.
				if (target.Type == TargetType.Actor && self.World.Selection.Contains(target.Actor))
					return true;

				return modifiers.HasModifier(TargetModifiers.ForceMove);
			}
		}

		Order IIssueOrder.IssueOrder(Actor self, IOrderTargeter order, in Target target, bool queued)
		{
			return order.OrderID == OrderName ? new Order(order.OrderID, self, target, queued) : null;
		}

		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString != OrderName || order.Target.Type != TargetType.Actor)
				return;

			// AttackMoveActivity around a Follow, exactly as Guard does. Follow never completes, so the
			// unit stays with his man until the player orders otherwise; the attack-move wrapper is what
			// makes him treat anyone in reach on the way — an actor running an activity is not idle, and
			// AutoTarget only heals from the idle path. The AttendAlly subclass adds the half that makes
			// the order name a patient rather than a position: see AttendAllyActivity.
			self.QueueActivity(order.Queued, new AttendAllyActivity(self,
				() => move.MoveFollow(self, order.Target, WDist.Zero, info.Range, targetLineColor: info.TargetLineColor),
				order.Target, info.Range, info.MaxStalledTicks));

			self.ShowTargetLines();
		}

		string IOrderVoice.VoicePhraseForOrder(Actor self, Order order)
		{
			return order.OrderString == OrderName ? info.Voice : null;
		}
	}
}
