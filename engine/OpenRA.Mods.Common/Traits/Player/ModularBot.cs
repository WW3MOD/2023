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

		[Desc("WW3MOD bot-brain Stage 1, predicate (a). Enforce the commitment ledger AT THE FUNNEL:",
			"drop a tasking order when every one of its target units already holds a live commitment",
			"belonging to a module that outranks (or ties) the issuer. Replaces today's conflict",
			"resolution, which is 'whichever module is declared later in ai.yaml wins' — an emergent",
			"property of trait construct order that is documented nowhere and that no losing module is",
			"ever told about. Default false ⇒ the funnel is a pass-through, byte-identical to before.")]
		public readonly bool RespectCommitmentsOnIssue = false;

		[Desc("WW3MOD bot-brain Stage 1, predicate (b). Minimum ticks a unit keeps a standing order",
			"before a DIFFERENT destination may be issued to it. Only bites while the unit is still",
			"executing something: an idle unit is always re-orderable, and a Reflex order (retreat /",
			"executing something, and only where a call site has OPTED IN by passing",
			"BotOrderDamping.Recurring. This is the inverse of a same-destination",
			"dedup — the churn census found the top suspects all issue genuinely DIFFERENT",
			"destinations, so equivalence dedup cannot see them. 0 disables (the inert default).")]
		public readonly int ReorderDwellTicks = 0;

		[Desc("Ticks between funnel-gate suppression summaries in the unit-lifecycle log. One line per",
			"(issuing module, reason) pair per window, so the stream stays bounded. 0 disables.",
			"Costs nothing unless Test.Mode=true Test.UnitLifecycleLog=<path> is set.")]
		public readonly int OrderGateLogIntervalTicks = 500;

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

		// WW3MOD bot-brain Stage 1. Null unless at least one gate lever is set, in which case
		// QueueOrder is the pre-Stage-1 pass-through exactly as before.
		BotOrderGate gate;
		PoiGoalGuard goalGuard;
		readonly List<BotOrderTarget> gateTargets = new();

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
			goalGuard = p.PlayerActor.TraitOrDefault<PoiGoalGuard>();
			if (info.RespectCommitmentsOnIssue || info.ReorderDwellTicks > 0)
				gate = new BotOrderGate(info.RespectCommitmentsOnIssue, info.ReorderDwellTicks);

			foreach (var ibe in p.PlayerActor.TraitsImplementing<IBotEnabled>())
				ibe.BotEnabled(this);
		}

		bool IBot.QueueOrder(Order order) => QueueOrder(order, BotOrderDamping.Protected);

		bool IBot.QueueOrder(Order order, BotOrderDamping damping) => QueueOrder(order, damping);

		bool QueueOrder(Order order, BotOrderDamping damping)
		{
			// HUMANS CANNOT REACH THIS. IBot.QueueOrder is only ever called by bot modules holding an
			// IBot; a human's orders come from the UI straight to World.IssueOrder and never enter this
			// queue. The gate is therefore unreachable from a human-owned unit by construction, not by a
			// predicate that could be got wrong. What it also means: the gate is blind to the SECOND order
			// layer — the activity-queueing traits (StancePositioningExecutor, AutoSeekSupplies,
			// CohesionSlotMemory, DropsSupplyCache) that call Actor.QueueActivity directly and emit no
			// Order at all. Two of those are default-ON for humans. Nothing here can damp them, in either
			// direction. See WORKSPACE/recon/260807-order-source-census.md.
			if (gate != null)
			{
				var verdict = gate.Admit(
					order.OrderString, order.Queued, damping, currentModuleTag, world.WorldTick,
					DestinationKeyOf(order), ResolveGateTargets(order));

				if (verdict != BotOrderVerdict.Admitted)
					return false;
			}

			// Attribute this order to the module currently ticking, then queue it unchanged. LogOrder
			// self-gates to a no-op when lifecycle logging is off, so this is free in normal play and
			// never touches the order. Logged AFTER the gate deliberately: the `order` stream then
			// contains only orders that were really queued, so comparing two runs' order counts measures
			// exactly what the gate removed, and the `ordgate` lines say who lost and why.
			lifecycleLogger?.LogOrder(player, currentModuleTag, order);
			orders.Enqueue(order);
			return true;
		}

		// Subject ∪ GroupedActors: a grouped order carries a null Subject, so both must be walked.
		List<BotOrderTarget> ResolveGateTargets(Order order)
		{
			gateTargets.Clear();
			var ledger = goalGuard != null && !goalGuard.IsTraitDisabled ? goalGuard.Ledger : null;
			var tick = world.WorldTick;

			AddGateTarget(order.Subject, ledger, tick);
			if (order.GroupedActors != null)
				foreach (var a in order.GroupedActors)
					if (a != order.Subject)
						AddGateTarget(a, ledger, tick);

			return gateTargets;
		}

		void AddGateTarget(Actor a, GoalGuardLedger<Actor> ledger, int tick)
		{
			if (a == null || a.IsDead || !a.IsInWorld)
				return;

			string objective = null;
			if (ledger != null && ledger.IsCommitted(a, tick) && ledger.TryGetObjective(a, out var o))
				objective = o;

			gateTargets.Add(new BotOrderTarget(a.ActorID, objective, !a.IsIdle));
		}

		long DestinationKeyOf(Order order)
		{
			var target = order.Target;
			switch (target.Type)
			{
				case TargetType.Actor:
					if (target.Actor != null)
						return OrderArbitrationMath.DestinationKey(true, target.Actor.ActorID, 0, 0, true);

					break;
				case TargetType.FrozenActor:
				case TargetType.Terrain:
					var cell = world.Map.CellContaining(target.CenterPosition);
					return OrderArbitrationMath.DestinationKey(false, 0, cell.X, cell.Y, true);
			}

			return OrderArbitrationMath.DestinationKey(false, 0, 0, 0, false);
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
						// DO NOT GATE THIS LOOP. Every module's cadence is a per-call `--countdown`
						// decrement (24 sites) and every Ledger.Commit refresh in every bot module sits
						// behind its module's own countdown, so withholding a BotTick stretches that
						// module's interval by the withhold factor and — at TTL/interval = 250/100 = 2.5x
						// headroom on the POI modules — silently drops its units out of the ledger while
						// it still lists them in axis.Units. An attention scheduler must refuse the
						// RE-DECISION inside the module's own eval (where the claim refresh already
						// lives), never the tick. Gating here re-opens a 25-site tick-stamp conversion
						// whose coverage is not statically verifiable.
						// See WORKSPACE/plans/260808-bot-brain-staging.md §7.
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

			if (gate != null)
			{
				gate.Prune(world.WorldTick);
				ReportGateSuppressions();
			}

			// NOTE (Stage 1): suppressing orders above perturbs orders.Count, and this quotient is
			// computed from it — so the drain SCHEDULE changes for every module, including ones the gate
			// never inspects. Fewer queued orders means a smaller per-tick quotient, which can delay an
			// unrelated order in the tail of a burst by a few ticks. FIFO order among survivors is
			// preserved exactly and nothing is lost, so the effect is bounded latency, not reordering.
			// This is a deliberate, visible @stable change: re-take the ai-bench baseline knowingly.
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

		// One line per (issuing module, reason) per window into the same stream ModularBot already logs
		// orders to, so a single Test.Mode run can measure how much churn the gate actually removed
		// without one line per suppression per tick.
		void ReportGateSuppressions()
		{
			if (info.OrderGateLogIntervalTicks <= 0 || lifecycleLogger == null || !lifecycleLogger.Enabled)
				return;

			if (world.WorldTick % info.OrderGateLogIntervalTicks != 0 || gate.Suppressions.Count == 0)
				return;

			foreach (var s in gate.Suppressions)
				lifecycleLogger.LogOrderGate(player, s.ModuleTag, s.Verdict.ToString(), s.Count, gate.StandingCount);

			gate.ResetSuppressions();
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
