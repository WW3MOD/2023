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
using OpenRA.Activities;
using OpenRA.Mods.Common.Activities;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Makes the unit automatically run around when taking damage.")]
	sealed class ScaredyCatInfo : ConditionalTraitInfo, Requires<MobileInfo>
	{
		[Desc("Chance (out of 100) the unit has to enter panic mode when attacked.")]
		public readonly int PanicChance = 100;

		[Desc("How long (in ticks) the actor should panic for.")]
		public readonly int PanicDuration = 250;

		[Desc("Panic movement speed as a percentage of the normal speed.")]
		public readonly int PanicSpeedModifier = 200;

		[Desc("Chance (out of 100) the unit has to enter panic mode when attacking.")]
		public readonly int AttackPanicChance = 20;

		[Desc("The terrain types that this actor should avoid running on to while panicking.")]
		public readonly HashSet<string> AvoidTerrainTypes = new();

		[SequenceReference(prefix: true)]
		public readonly string PanicSequencePrefix = "panic-";

		public override object Create(ActorInitializer init) { return new ScaredyCat(init.Self, this); }
	}

	sealed class ScaredyCat : ConditionalTrait<ScaredyCatInfo>, ITick, INotifyIdle, INotifyDamage, INotifyAttack, ISpeedModifier, ISync, IRenderInfantrySequenceModifier
	{
		readonly Mobile mobile;
		readonly Actor self;
		readonly Func<CPos, bool> avoidTerrainFilter;
		readonly List<(Target Target, string OrderString)> stashedOrders = new();

		[Sync]
		int panicStartedTick;
		bool Panicking => panicStartedTick > 0;

		bool IRenderInfantrySequenceModifier.IsModifyingSequence => !IsTraitDisabled && Panicking;
		string IRenderInfantrySequenceModifier.SequencePrefix => Info.PanicSequencePrefix;

		public ScaredyCat(Actor self, ScaredyCatInfo info)
			: base(info)
		{
			this.self = self;
			mobile = self.Trait<Mobile>();

			if (info.AvoidTerrainTypes.Count > 0)
				avoidTerrainFilter = c => info.AvoidTerrainTypes.Contains(self.World.Map.GetTerrainInfo(c).Type);
		}

		// A critically damaged man is prone and still. Ending the panic here rather than only refusing to
		// start one is the load-bearing half: panic is triggered by ANY damage and runs for PanicDuration,
		// so the shot that pushes him INTO critical almost always lands mid-panic.
		protected override void TraitDisabled(Actor self)
		{
			if (!Panicking)
				return;

			panicStartedTick = 0;

			// Deliberately dropped rather than resumed. Every stashed order is a go-somewhere-and-act
			// intent (capture, demolish, board), and re-issuing one here would queue a fresh Move — which
			// claims the next cell before Mobile is ever asked how fast the actor can cross it.
			stashedOrders.Clear();
			self.CancelActivity();
		}

		public void Panic()
		{
			if (IsTraitDisabled)
				return;

			if (!Panicking)
			{
				StashPendingTaskOrders();
				self.CancelActivity();
			}

			panicStartedTick = self.World.WorldTick;
		}

		// Snapshot Enter-derived "go to X and act" intents so we can re-issue them after panic.
		void StashPendingTaskOrders()
		{
			stashedOrders.Clear();
			var activity = self.CurrentActivity;
			while (activity != null)
			{
				var orderString = OrderStringFor(activity);
				if (orderString != null)
				{
					foreach (var node in activity.TargetLineNodes(self))
					{
						if (node.Target.Type == TargetType.Actor && node.Target.Actor != null && !node.Target.Actor.IsDead)
						{
							stashedOrders.Add((node.Target, orderString));
							break;
						}
					}
				}

				activity = activity.NextActivity;
			}
		}

		static string OrderStringFor(Activity activity)
		{
			if (activity is CaptureActor) return "CaptureActor";
			if (activity is Demolish) return "C4";
			if (activity is RideTransport) return "EnterTransport";
			if (activity is EnterAsCrew) return "EnterAsCrewMember";
			return null;
		}

		void ResumeStashedOrders()
		{
			foreach (var (target, orderString) in stashedOrders)
			{
				if (target.Actor != null && !target.Actor.IsDead && target.Actor.IsInWorld)
					self.World.IssueOrder(new Order(orderString, self, target, true));
			}

			stashedOrders.Clear();
		}

		void ITick.Tick(Actor self)
		{
			if (!Panicking)
				return;

			if (self.World.WorldTick >= panicStartedTick + Info.PanicDuration)
			{
				self.CancelActivity();
				panicStartedTick = 0;
				ResumeStashedOrders();
			}
		}

		void INotifyIdle.TickIdle(Actor self)
		{
			if (!Panicking)
				return;

			// Note: This is just a modified copy of Mobile.Nudge
			var cell = mobile.GetAdjacentCell(self.Location, avoidTerrainFilter);
			if (cell != null)
				self.QueueActivity(false, mobile.MoveTo(cell.Value, 0, targetLineColor: AutomaticOrder.LineColor));
		}

		// PITFALL: do not hoist an IsTraitDisabled check above these SharedRandom draws. Panic() does the
		// gating, deliberately AFTER the roll, so a disabled trait consumes the shared RNG stream exactly
		// as an enabled one does. Skipping the draw would make every later consumer of World.SharedRandom
		// depend on whether some civilian happened to be critically wounded.
		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
			if (e.Damage.Value > 0 && self.World.SharedRandom.Next(100) < Info.PanicChance)
				Panic();
		}

		void INotifyAttack.Attacking(Actor self, in Target target, Armament a, Barrel barrel)
		{
			if (self.World.SharedRandom.Next(100) < Info.AttackPanicChance)
				Panic();
		}

		void INotifyAttack.PreparingAttack(Actor self, in Target target, Armament a, Barrel barrel) { }

		int ISpeedModifier.GetSpeedModifier()
		{
			return Panicking ? Info.PanicSpeedModifier : 100;
		}
	}
}
