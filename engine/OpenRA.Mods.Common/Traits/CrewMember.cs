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
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Marks this actor as an ejected crew member that can re-enter allied vehicles with matching empty slots.")]
	public class CrewMemberInfo : TraitInfo
	{
		[FieldLoader.Require]
		[Desc("Crew role this actor fills (Driver, Gunner, Commander).")]
		public readonly string Role = null;

		[Desc("Cursor to display when targeting a valid vehicle.")]
		public readonly string EnterCursor = "enter";

		[Desc("Cursor when the vehicle slot is full.")]
		public readonly string EnterBlockedCursor = "enter-blocked";

		[Desc("Voice to use when ordered to enter a vehicle.")]
		public readonly string Voice = "Action";

		public override object Create(ActorInitializer init) { return new CrewMember(this); }
	}

	public class CrewMember : IIssueOrder, IResolveOrder, IOrderVoice
	{
		readonly CrewMemberInfo info;

		public string Role => info.Role;

		public CrewMember(CrewMemberInfo info)
		{
			this.info = info;
		}

		IEnumerable<IOrderTargeter> IIssueOrder.Orders
		{
			get
			{
				yield return new EnterAlliedActorTargeter<VehicleCrewInfo>(
					"EnterAsCrewMember",
					6,
					info.EnterCursor,
					info.EnterBlockedCursor,
					IsValidTarget,
					CanEnter);
			}
		}

		bool IsValidTarget(Actor target, TargetModifiers modifiers)
		{
			var vc = target.TraitOrDefault<VehicleCrew>();
			return vc != null && vc.HasEmptySlot(info.Role);
		}

		bool CanEnter(Actor target)
		{
			var vc = target.TraitOrDefault<VehicleCrew>();
			return vc != null && vc.CanAcceptRole(info.Role);
		}

		public Order IssueOrder(Actor self, IOrderTargeter order, in Target target, bool queued)
		{
			if (order.OrderID == "EnterAsCrewMember")
				return new Order(order.OrderID, self, target, queued);

			return null;
		}

		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString != "EnterAsCrewMember")
				return;

			if (order.Target.Type != TargetType.Actor)
			{
				Log.Write("debug", $"[CrewMember] {self.Info.Name} ResolveOrder rejected: Target.Type={order.Target.Type} (not Actor)");
				return;
			}

			var targetActor = order.Target.Actor;
			if (targetActor == null || targetActor.IsDead || !CanEnter(targetActor))
			{
				Log.Write("debug", $"[CrewMember] {self.Info.Name} (Role={info.Role}) ResolveOrder rejected: " +
					$"targetNull={targetActor == null}, targetDead={targetActor?.IsDead}, canEnter={targetActor != null && !targetActor.IsDead && CanEnter(targetActor)}");
				return;
			}

			Log.Write("debug", $"[CrewMember] {self.Info.Name} (Role={info.Role}) ResolveOrder ACCEPTED, queueing EnterAsCrew to {targetActor.Info.Name} (Owner={targetActor.Owner.PlayerName})");
			self.QueueActivity(order.Queued, new EnterAsCrew(self, order.Target, info.Role));
			self.ShowTargetLines();
		}

		string IOrderVoice.VoicePhraseForOrder(Actor self, Order order)
		{
			if (order.OrderString != "EnterAsCrewMember")
				return null;

			if (order.Target.Type != TargetType.Actor || !CanEnter(order.Target.Actor))
				return null;

			return info.Voice;
		}
	}
}
