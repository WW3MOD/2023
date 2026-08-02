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
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Bot that uses BotModules.")]
	[TraitLocation(SystemActors.Player)]
	public sealed class ModularBotInfo : TraitInfo, IBotInfo
	{
		[FieldLoader.Require]
		[Desc("Internal id for this bot.")]
		public readonly string Type = null;

		[FluentReference]
		[Desc("Human-readable name this bot uses.")]
		public readonly string Name = null;

		[Desc("Minimum portion of pending orders to issue each tick (e.g. 5 issues at least 1/5th of all pending orders). " +
			"Excess orders remain queued for subsequent ticks.")]
		public readonly int MinOrderQuotientPerTick = 5;

		string IBotInfo.Type => Type;

		string IBotInfo.Name => Name;

		public override object Create(ActorInitializer init) { return new ModularBot(this, init); }
	}

	public sealed class ModularBot : ITick, IBot, INotifyDamage
	{
		public bool IsEnabled;

		readonly ModularBotInfo info;
		readonly World world;
		readonly Queue<Order> orders = new();

		Player player;

		IBotTick[] tickModules;
		IBotRespondToAttack[] attackResponseModules;

		// Behavior-lint order funnel (WORKSPACE/behavior-lint-spec.md §1.3).
		// Set to the concrete type name of the module currently running its
		// BotTick/RespondToAttack, so every order queued below can be attributed
		// to its issuing module. Reset to "" outside a module tick. The logger is
		// resolved once at Activate; when the trait is inert (gate off) LogOrder
		// returns before any allocation, so this stays cost-free in normal play.
		string currentModuleTag = "";
		UnitLifecycleLogger lifecycleLogger;

		IBotInfo IBot.Info => info;
		Player IBot.Player => player;

		public ModularBot(ModularBotInfo info, ActorInitializer init)
		{
			this.info = info;
			world = init.World;
		}

		// Called by the host's player creation code
		public void Activate(Player p)
		{
			// Bot logic is not allowed to affect world state, and can only act by issuing orders
			// These orders are recorded in the replay, so bots shouldn't be enabled during replays
			if (p.World.IsReplay)
				return;

			IsEnabled = true;
			player = p;
			tickModules = p.PlayerActor.TraitsImplementing<IBotTick>().ToArray();
			attackResponseModules = p.PlayerActor.TraitsImplementing<IBotRespondToAttack>().ToArray();
			lifecycleLogger = world.WorldActor.TraitOrDefault<UnitLifecycleLogger>();
			foreach (var ibe in p.PlayerActor.TraitsImplementing<IBotEnabled>())
				ibe.BotEnabled(this);
		}

		void IBot.QueueOrder(Order order)
		{
			// Attribute this order to the module currently ticking, then queue it
			// unchanged. LogOrder self-gates to a no-op when lifecycle logging is
			// off, so this is free in normal play and never touches the order.
			lifecycleLogger?.LogOrder(player, currentModuleTag, order);
			orders.Enqueue(order);
		}

		void ITick.Tick(Actor self)
		{
			if (!IsEnabled || self.World.IsLoadingGameSave)
				return;

			using (new PerfSample("bot_tick"))
			{
				Sync.RunUnsynced(Game.Settings.Debug.SyncCheckBotModuleCode, world, () =>
				{
					try
					{
						foreach (var t in tickModules)
							if (t.IsTraitEnabled())
							{
								currentModuleTag = t.GetType().Name;
								t.BotTick(this);
							}
					}
					finally
					{
						// Never leave a stale tag if a module throws — orders queued
						// outside a module tick must attribute as "" (see QueueOrder).
						currentModuleTag = "";
					}
				});
			}

			var ordersToIssueThisTick = Math.Min((orders.Count + info.MinOrderQuotientPerTick - 1) / info.MinOrderQuotientPerTick, orders.Count);
			var controlAllManager = world.WorldActor.TraitOrDefault<ControlAllUnitsManager>();
			for (var i = 0; i < ordersToIssueThisTick; i++)
			{
				var order = orders.Dequeue();

				// Skip bot orders for actors currently under player control
				if (controlAllManager != null && order.Subject != null && controlAllManager.IsPlayerControlled(order.Subject))
					continue;

				world.IssueOrder(order);
			}
		}

		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
			if (!IsEnabled || self.World.IsLoadingGameSave)
				return;

			using (new PerfSample("bot_attack_response"))
			{
				Sync.RunUnsynced(Game.Settings.Debug.SyncCheckBotModuleCode, world, () =>
				{
					try
					{
						foreach (var t in attackResponseModules)
							if (t.IsTraitEnabled())
							{
								currentModuleTag = t.GetType().Name;
								t.RespondToAttack(this, self, e);
							}
					}
					finally
					{
						currentModuleTag = "";
					}
				});
			}
		}
	}
}
