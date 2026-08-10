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
				yield return new TargetTypeOrderTargeter(info.ValidTargets, OrderName, info.OrderPriority,
					info.Cursor, false, true);
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
			// AutoTarget only heals from the idle path.
			self.QueueActivity(order.Queued, new AttackMoveActivity(self,
				() => move.MoveFollow(self, order.Target, WDist.Zero, info.Range, targetLineColor: info.TargetLineColor)));

			self.ShowTargetLines();
		}

		string IOrderVoice.VoicePhraseForOrder(Actor self, Order order)
		{
			return order.OrderString == OrderName ? info.Voice : null;
		}
	}
}
