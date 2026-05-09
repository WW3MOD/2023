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
	[Desc("Lets an armed unit issue a 'walk to a Supply Route and stay until resolved' order.",
		"Higher order priority than the default Attack so right-clicking an SR queues this",
		"instead of bouncing the unit off the SR's indestructible 1-HP floor.")]
	public class AttacksSupplyRoutesInfo : TraitInfo
	{
		[Desc("Order priority. Must beat AttackBase's AttackOrderTargeter (priority 6).")]
		public readonly int OrderPriority = 8;

		[CursorReference]
		[Desc("Cursor when targeting an enemy SR (we're attacking it).")]
		public readonly string EnemyCursor = "attack";

		[CursorReference]
		[Desc("Cursor when targeting an allied SR (we're defending it).")]
		public readonly string AllyCursor = "guard";

		[Desc("Color of the target line for SR attack orders.")]
		public readonly Color TargetLineColor = Color.OrangeRed;

		[VoiceReference]
		public readonly string Voice = "Action";

		public override object Create(ActorInitializer init) { return new AttacksSupplyRoutes(this); }
	}

	public class AttacksSupplyRoutes : IIssueOrder, IResolveOrder, IOrderVoice
	{
		readonly AttacksSupplyRoutesInfo info;

		public AttacksSupplyRoutes(AttacksSupplyRoutesInfo info)
		{
			this.info = info;
		}

		public IEnumerable<IOrderTargeter> Orders
		{
			get { yield return new SupplyRouteOrderTargeter(info); }
		}

		public Order IssueOrder(Actor self, IOrderTargeter order, in Target target, bool queued)
		{
			if (order.OrderID != "AttackSupplyRoute")
				return null;

			return new Order(order.OrderID, self, target, queued);
		}

		public string VoicePhraseForOrder(Actor self, Order order)
		{
			return order.OrderString == "AttackSupplyRoute" ? info.Voice : null;
		}

		public void ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString != "AttackSupplyRoute")
				return;

			if (order.Target.Type != TargetType.Actor || order.Target.Actor == null)
				return;

			self.QueueActivity(order.Queued, new AttackSupplyRoute(self, order.Target, info.TargetLineColor));
			self.ShowTargetLines();
		}

		sealed class SupplyRouteOrderTargeter : UnitOrderTargeter
		{
			readonly AttacksSupplyRoutesInfo info;

			public SupplyRouteOrderTargeter(AttacksSupplyRoutesInfo info)
				: base("AttackSupplyRoute", info.OrderPriority, info.EnemyCursor, true, true)
			{
				this.info = info;
			}

			public override bool CanTargetActor(Actor self, Actor target, TargetModifiers modifiers, ref string cursor)
			{
				if (!target.Info.HasTraitInfo<SupplyRouteContestationInfo>())
					return false;

				// Don't intercept clicks on the player's own SR — let the Enter/Evacuate
				// handler (Cargo on the SR) take over so right-click = evacuate, with the enter cursor.
				if (target.Owner == self.Owner)
					return false;

				var rel = self.Owner.RelationshipWith(target.Owner);
				if (rel == PlayerRelationship.Enemy)
				{
					cursor = info.EnemyCursor;
					return true;
				}

				if (rel == PlayerRelationship.Ally)
				{
					cursor = info.AllyCursor;
					return true;
				}

				// Neutral SRs — treat as enemy-like (any enemies pressing them = stay until resolved).
				cursor = info.EnemyCursor;
				return true;
			}

			public override bool CanTargetFrozenActor(Actor self, FrozenActor target, TargetModifiers modifiers, ref string cursor)
			{
				if (!target.Info.HasTraitInfo<SupplyRouteContestationInfo>())
					return false;

				if (target.Owner == self.Owner)
					return false;

				var rel = self.Owner.RelationshipWith(target.Owner);
				cursor = rel == PlayerRelationship.Ally ? info.AllyCursor : info.EnemyCursor;
				return true;
			}
		}
	}
}
