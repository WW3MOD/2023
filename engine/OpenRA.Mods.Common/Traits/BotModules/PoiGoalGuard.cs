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

	// ============================================================
	// Pure MISSION-commitment decision math — engine-free, unit-tested
	// (MissionCommitmentMathTest). The GoalGuardLedger above answers "is this
	// UNIT still claimed"; this answers "should this SQUAD's committed mission be
	// abandoned THIS eval". Kept a pure static class (like PoiOffenseMath) so it
	// ports verbatim into the future v3 brain — only the snapshot plumbing (the
	// offense module's Axis fields) is engine-specific.
	//
	// The rule: once a squad has an objective, HOLD it. Do NOT re-task merely
	// because scores jittered. Re-task ONLY on an explicit trigger:
	//   1. objective invalid  — target gone / captured / POI resolved,
	//   2. danger spike        — believed danger at the objective jumped vs commit,
	//   3. better opportunity  — a rival objective beats the committed one by a
	//                            hysteresis margin (not a tie-break flip),
	//   4. combat-ineffective  — the squad is ground below a fraction of its
	//                            commit-time strength,
	//   (+ an optional bounded commit window as a safety re-plan valve).
	// Integer-only, deterministic, zero RNG.
	// ============================================================
	public static class MissionCommitmentMath
	{
		/// <summary>Trigger 2 — believed danger at/along the objective spiked materially above what it
		/// was when the squad committed. Fires when current strictly exceeds commit + max(floor, commit·pct/100).
		/// The absolute floor lets a fresh weapon envelope over previously-quiet ground (commit ≈ 0) trip it;
		/// the percentage scales the reaction to an already-dangerous commit so ambient baseline jitter does
		/// not. Integer-only, monotonic.</summary>
		public static bool DangerSpiked(int commitDanger, int currentDanger, int spikePct, int spikeFloor)
		{
			if (currentDanger <= commitDanger)
				return false;

			var margin = commitDanger * Math.Max(0, spikePct) / 100;
			if (margin < spikeFloor)
				margin = spikeFloor;

			return currentDanger - commitDanger > margin;
		}

		/// <summary>Trigger 3 — a rival objective is MATERIALLY better than the committed one. Same
		/// hysteresis form as PoiOffenseMath.ScoreBeatsByThreshold: the alternative must beat the committed
		/// score by strictly more than marginPct (a mere tie-break flip does not qualify). A non-positive
		/// alternative never wins; a non-positive committed score is always beatable by any positive rival.</summary>
		public static bool BetterOpportunity(long committedScore, long bestAlternativeScore, int marginPct)
		{
			if (bestAlternativeScore <= 0)
				return false;
			if (committedScore <= 0)
				return true;

			return bestAlternativeScore * 100 > committedScore * (100L + Math.Max(0, marginPct));
		}

		/// <summary>Phase 1c — snap an axis score DOWN to the low edge of a coarse band (floor-to-band).
		/// The believed-field factors that build the score are BUCKETED, not continuous (BalanceOfPowerFactor
		/// steps 150/100/60, BelievedDangerFactor steps 100/60/20 — PoiOffensiveBotModule), so a single bucket
		/// crossing multiplies a raw score by up to 3×. Comparing raw scores therefore ping-pongs abort/re-propose
		/// every time a believed-field read wobbles at a bucket edge. Quantizing both sides to a band before the
		/// trigger-3 compare snaps a wobbling upper value DOWN into the committed axis's band, so the bucket-edge
		/// wobble can no longer clear the better-opportunity margin. A band ≤ 0 is IDENTITY (returns score
		/// unchanged) — the frozen default, so the raw comparison is preserved byte-for-byte when the caller opts
		/// out. Integer-only, deterministic, zero RNG. Non-negative scores expected (offense scores are a product
		/// of non-negative factors); a negative score truncates toward zero, still monotonic.</summary>
		public static long QuantizeAxisScore(long score, long band)
		{
			if (band <= 0)
				return score;

			return (score / band) * band;
		}

		/// <summary>Phase 1c — trigger-3 better-opportunity test on QUANTIZED scores. The band is RELATIVE to the
		/// pair (a percent of the larger operand) so it is scale-invariant across the wide offense-score range
		/// (value × distance × threat × ownership × believed-field mul spans ~1e8–1e12). Both scores are floored
		/// to that band, then the same hysteresis-margin test as <see cref="BetterOpportunity"/> is applied. Effect:
		/// a rival must be a full band (≈ quantizeBandPct% of the top score) clear of the committed axis to count as
		/// materially better — a bucket-aware threshold that a single believed-field bucket wobble on EITHER cell
		/// cannot manufacture. A quantizeBandPct ≤ 0 collapses VERBATIM to <see cref="BetterOpportunity"/> (the
		/// byte-identical frozen path). Integer-only, deterministic, zero RNG.</summary>
		public static bool BetterOpportunityQuantized(
			long committedScore, long bestAlternativeScore, int marginPct, int quantizeBandPct)
		{
			if (quantizeBandPct <= 0)
				return BetterOpportunity(committedScore, bestAlternativeScore, marginPct);

			var band = Math.Max(committedScore, bestAlternativeScore) * quantizeBandPct / 100;
			return BetterOpportunity(
				QuantizeAxisScore(committedScore, band),
				QuantizeAxisScore(bestAlternativeScore, band),
				marginPct);
		}

		/// <summary>Trigger 4 — the squad has been ground below a fraction of its commit-time strength.
		/// Fires when current·denom &lt; commit·numer (e.g. numer/denom = 1/2 ⇒ below half). A degenerate
		/// commit strength of 0 (or denom ≤ 0) never trips — nothing to lose.</summary>
		public static bool CombatIneffective(int commitStrength, int currentStrength, int numer, int denom)
		{
			if (commitStrength <= 0 || denom <= 0)
				return false;

			return (long)currentStrength * denom < (long)commitStrength * numer;
		}

		/// <summary>Aggregate: should a committed mission be RELEASED for re-tasking this eval? Trigger 1
		/// (objective invalid) short-circuits; then the optional bounded commit window; then the danger /
		/// opportunity / attrition triggers. A commitWindowTicks ≤ 0 disables the time valve (hold purely
		/// on the triggers — objective completion is itself trigger 1). Pure &amp; deterministic: the module
		/// snapshots the commit-time values and feeds the current reads.</summary>
		public static bool ShouldReassign(
			bool objectiveValid,
			int commitTick, int currentTick, int commitWindowTicks,
			int commitDanger, int currentDanger, int dangerSpikePct, int dangerSpikeFloor,
			long committedScore, long bestAlternativeScore, int betterOppMarginPct,
			int commitStrength, int currentStrength, int ineffectiveNumer, int ineffectiveDenom)
			=> ShouldReassign(
				objectiveValid,
				commitTick, currentTick, commitWindowTicks,
				commitDanger, currentDanger, dangerSpikePct, dangerSpikeFloor,
				committedScore, bestAlternativeScore, betterOppMarginPct, 0,
				commitStrength, currentStrength, ineffectiveNumer, ineffectiveDenom);

		/// <summary>Phase 1c overload — identical to the 15-arg <see cref="ShouldReassign(bool,int,int,int,int,int,int,int,long,long,int,int,int,int,int)"/>
		/// except trigger 3 runs on QUANTIZED scores (<see cref="BetterOpportunityQuantized"/>). A
		/// betterOppQuantizeBandPct ≤ 0 makes this collapse VERBATIM to the raw path, so the 15-arg overload
		/// above — which delegates here with 0 — is byte-identical to the pre-1c predicate.</summary>
		public static bool ShouldReassign(
			bool objectiveValid,
			int commitTick, int currentTick, int commitWindowTicks,
			int commitDanger, int currentDanger, int dangerSpikePct, int dangerSpikeFloor,
			long committedScore, long bestAlternativeScore, int betterOppMarginPct, int betterOppQuantizeBandPct,
			int commitStrength, int currentStrength, int ineffectiveNumer, int ineffectiveDenom)
		{
			if (!objectiveValid)
				return true;

			if (commitWindowTicks > 0 && currentTick - commitTick >= commitWindowTicks)
				return true;

			if (DangerSpiked(commitDanger, currentDanger, dangerSpikePct, dangerSpikeFloor))
				return true;

			if (BetterOpportunityQuantized(committedScore, bestAlternativeScore, betterOppMarginPct, betterOppQuantizeBandPct))
				return true;

			if (CombatIneffective(commitStrength, currentStrength, ineffectiveNumer, ineffectiveDenom))
				return true;

			return false;
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
