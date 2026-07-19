#region Copyright & License Information
/*
 * WW3MOD PoiGoalGuard — experimental AI, POI-strategy Phase 0/1 foundation.
 *
 * A per-unit COMMITMENT LEDGER: "unit U is pursuing objective O until tick T".
 * It exists to kill the order-overwriting bug class documented in
 * WORKSPACE/ai/02_problem_statement.md §3.1 — modules that filter available
 * units by `IsIdle` re-issue orders every scan whenever a unit's activity
 * momentarily flickers to null mid-task, so the task restarts and never
 * completes ("derricks ignored", "orders get overwritten").
 *
 * A module records a commitment when it issues a task order, then consults the
 * ledger BEFORE re-issuing: a still-committed unit (unexpired, valid objective)
 * is left alone regardless of its IsIdle flicker. Only when the commitment
 * EXPIRES (enough time to have finished) or the objective becomes invalid does
 * the unit re-enter the available pool. Net effect: at most one order per
 * commitment window instead of continuous thrash.
 *
 * DESIGN INTENT (Path A, decision #1): the timing/expiry logic lives in the
 * pure generic GoalGuardLedger<TKey> so it ports VERBATIM into the future v3
 * brain — only the assignment mechanism (this IBotTick-adjacent player trait vs
 * a brain method) moves. The trait is a thin holder; the ledger is the reusable
 * component. Objectives are namespaced strings ("capture:<actorId>") so they're
 * v3-friendly and greppable in logs.
 *
 * Gated enable-ai-experimental in ai.yaml — Normal / Rush / Turtle never see it.
 */
#endregion

using System;
using System.Collections.Generic;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	// Pure, engine-free commitment ledger. Generic over the unit key type so it
	// can be unit-tested with plain keys (int/string) without constructing an
	// Actor. The engine trait below instantiates GoalGuardLedger<Actor>.
	public sealed class GoalGuardLedger<TKey>
	{
		public struct Commitment
		{
			public string Objective;
			public int ExpiresAtTick;

			// How many times Commit() fired for the CURRENT objective. In a clean
			// single-task run this stays 1; a value > 1 means the unit was
			// re-committed to the same objective (i.e. the anti-thrash gate let a
			// re-issue through — expected only after an expiry or a genuine stall).
			public int CommitCount;
		}

		readonly Dictionary<TKey, Commitment> commitments = new();

		public int Count => commitments.Count;

		// Record (or refresh) unit's commitment. Re-committing to the SAME
		// objective extends the deadline and bumps CommitCount; a different (or
		// first) objective starts fresh with CommitCount = 1.
		public void Commit(TKey unit, string objective, int currentTick, int ttlTicks)
		{
			if (commitments.TryGetValue(unit, out var c) && c.Objective == objective)
			{
				c.ExpiresAtTick = currentTick + ttlTicks;
				c.CommitCount++;
				commitments[unit] = c;
			}
			else
			{
				commitments[unit] = new Commitment
				{
					Objective = objective,
					ExpiresAtTick = currentTick + ttlTicks,
					CommitCount = 1,
				};
			}
		}

		// True while the unit holds an unexpired commitment. This is the gate a
		// module checks before re-issuing: committed → skip (leave it working).
		public bool IsCommitted(TKey unit, int currentTick)
			=> commitments.TryGetValue(unit, out var c) && currentTick < c.ExpiresAtTick;

		public bool TryGetObjective(TKey unit, out string objective)
		{
			if (commitments.TryGetValue(unit, out var c))
			{
				objective = c.Objective;
				return true;
			}

			objective = null;
			return false;
		}

		// Diagnostics: number of Commit() calls for the unit's current objective.
		public int CommitCountFor(TKey unit)
			=> commitments.TryGetValue(unit, out var c) ? c.CommitCount : 0;

		public bool Release(TKey unit) => commitments.Remove(unit);

		// Drop expired commitments and (optionally) any whose key fails `keep`
		// (e.g. dead / no-longer-owned units). Safe to call every scan.
		public void Prune(int currentTick, Predicate<TKey> keep = null)
		{
			List<TKey> drop = null;
			foreach (var kv in commitments)
			{
				if (currentTick >= kv.Value.ExpiresAtTick || (keep != null && !keep(kv.Key)))
					(drop ??= new List<TKey>()).Add(kv.Key);
			}

			if (drop != null)
				foreach (var k in drop)
					commitments.Remove(k);
		}
	}

	[TraitLocation(SystemActors.Player)]
	[Desc("WW3MOD experimental AI: per-unit commitment ledger that stops capture/offense modules re-issuing",
		"orders when a unit's IsIdle flickers mid-task. Shared holder; the reusable logic is",
		"GoalGuardLedger<Actor>. Gate under enable-ai-experimental.")]
	public class PoiGoalGuardInfo : ConditionalTraitInfo
	{
		[Desc("Default commitment lifetime in ticks. A committed unit is left alone (not re-ordered)",
			"until this many ticks after the order, or until its objective becomes invalid. Must be",
			"long enough for a unit to walk to a distant POI and finish; success criterion S-E wants",
			"no second capture order within 200 ticks, so keep this >= 200.")]
		public readonly int DefaultCommitmentTicks = 300;

		public override object Create(ActorInitializer init) { return new PoiGoalGuard(this); }
	}

	public class PoiGoalGuard : ConditionalTrait<PoiGoalGuardInfo>
	{
		public readonly GoalGuardLedger<Actor> Ledger = new();

		public int DefaultCommitmentTicks => Info.DefaultCommitmentTicks;

		public PoiGoalGuard(PoiGoalGuardInfo info)
			: base(info) { }
	}
}
