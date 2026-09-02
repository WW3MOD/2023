#region Copyright & License Information
/*
 * WW3MOD @experimental — trade-efficiency purchasing feedback (pure integer).
 *
 * PERCEIVED BEHAVIOUR: the bot stops re-buying a unit class that keeps dying without taking anything with
 * it, and buys more of the class that is actually earning its cost. ForceCompositionMath already fixes the
 * SHAPE of the army (census-vs-target) and biases it by what the enemy is BELIEVED to field
 * (ApplyCounterBias). Neither of those notices that the shape it is holding is losing. This closes that
 * loop with the one signal that needs no belief store and no fog reasoning at all: our own ledger of what
 * each of our classes destroyed versus what it cost us when it died.
 *
 * THE COMPARISON IS RELATIVE, NOT ABSOLUTE. A role is judged against THIS PLAYER'S OWN average trade
 * ratio, not against break-even. That matters because a bot that is losing the match trades below 1.0 in
 * every role at once, and "downweight everything" is not a decision — after renormalisation it is a no-op
 * with extra steps. Asking instead "which of my classes is doing better than my army as a whole" always
 * yields a usable ordering, whether the bot is winning or losing.
 *
 * SMALL SAMPLES CANNOT SWING THE PLAN. Two independent guards:
 *   * an EVIDENCE FLOOR — a role whose (killed + lost) value is under evidenceFloor contributes no bias at
 *     all, so the first skirmish does not re-plan the army;
 *   * the same floor is used to SMOOTH the divisor (ratio = killed / max(lost, floor)), which both removes
 *     the divide-by-zero for a role that has lost nothing and stops "killed 1 truck, lost nothing" reading
 *     as an infinite trade ratio. One constant, two failure modes.
 *
 * KNOWN MEASUREMENT GAP, and why the RELATIVE comparison contains it. WORKSPACE/analysis/0902-loss-mining.md
 * §1.2 records that roughly 60% of losses in the surviving artifacts are credited to NO killer. The loss side
 * of this ledger is complete (UpdatesPlayerStatistics.Killed always records the victim), but the kill side is
 * only credited when e.Attacker resolves — so killedValue is systematically UNDER-counted. A uniform
 * under-count cancels exactly here: both the per-role ratio and the army average share the same numerator
 * scale, and the bias is computed from (ratio - average) / average, which is invariant to it. What does NOT
 * cancel is NON-uniform attribution — if one role's kills go unattributed more often than another's, this
 * pass will read that role as trading worse than it does. That is unquantified and is the main reason the
 * shipped scale is half the measured deviation with a 25% clamp rather than the full signal.
 *
 * FOG: nothing here is a belief. Our own losses and our own kills are facts the player is always entitled
 * to (they are the same numbers PlayerStatistics has always kept for the end-of-match graphs); the ledger
 * is UnitTypeTelemetry, fed by UpdatesPlayerStatistics on lifecycle callbacks that already ran. This pass
 * never inspects an enemy actor, so it cannot leak unscouted information the way a composition read would.
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws, integer-only, no world/actor references —
 * plain arrays in and out, one ordered walk each. The caller supplies its arrays in a fixed ordinal order
 * (UnitBuilderBotModule flattens its Dictionaries ONCE in Created for exactly this reason), so every
 * function is a pure deterministic map from its arguments.
 *
 * BYTE-IDENTITY: unreachable unless UnitBuilderBotModuleInfo.TradeFeedbackMaxPct is positive, which
 * defaults to 0 and is set only in the two @experimental faction blocks — so normal/rush/turtle/@stable
 * never enter this path and keep their RNG draw count and order.
 *
 * Split out as a pure static class (mirrors ForceCompositionMath / CompositionNeedMath) so the whole
 * decision is NUnit-pinned WITHOUT a game run.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	public static class TradeEfficiencyMath
	{
		/// <summary><para>Per-slot trade bias in PERCENT, ready to scale a target share.</para>
		///
		/// <para>For slot i: <c>ratio_i = killed_i * 1000 / max(lost_i, evidenceFloor)</c>, compared against the
		/// same ratio computed over the summed totals. The percentage deviation from that average is scaled by
		/// <paramref name="scalePct"/> and clamped to +/-<paramref name="maxPct"/>.</para>
		///
		/// <para>A slot returns exactly 0 — byte-identical to no feedback — when it is under the evidence floor,
		/// when the lever is off (<paramref name="maxPct"/> or <paramref name="scalePct"/> &lt;= 0), or when the
		/// army as a whole has destroyed nothing (no average to compare against, so no opinion).</para>
		///
		/// <para><paramref name="evidenceFloor"/> is clamped up to 1 so the divisor is never zero even if a caller
		/// passes 0.</para></summary>
		public static int[] BiasPercent(long[] killedValue, long[] lostValue, int evidenceFloor, int scalePct, int maxPct)
		{
			if (killedValue == null || killedValue.Length == 0)
				return System.Array.Empty<int>();

			var slots = killedValue.Length;
			var result = new int[slots];

			if (maxPct <= 0 || scalePct <= 0)
				return result;

			if (evidenceFloor < 1)
				evidenceFloor = 1;

			long totalKilled = 0;
			long totalLost = 0;
			for (var i = 0; i < slots; i++)
			{
				var killed = killedValue[i] > 0 ? killedValue[i] : 0;
				var lost = lostValue != null && i < lostValue.Length && lostValue[i] > 0 ? lostValue[i] : 0;
				totalKilled += killed;
				totalLost += lost;
			}

			// Nothing killed anywhere ⇒ no average ⇒ no opinion. Deliberately NOT "everything traded badly":
			// an army that has not fought yet must not have its shape rewritten before the first engagement.
			if (totalKilled <= 0)
				return result;

			var avgRatio = totalKilled * 1000 / (totalLost > evidenceFloor ? totalLost : evidenceFloor);
			if (avgRatio <= 0)
				return result;

			for (var i = 0; i < slots; i++)
			{
				var killed = killedValue[i] > 0 ? killedValue[i] : 0;
				var lost = lostValue != null && i < lostValue.Length && lostValue[i] > 0 ? lostValue[i] : 0;

				// Evidence floor: this class has not yet done enough for its ratio to mean anything.
				if (killed + lost < evidenceFloor)
					continue;

				var ratio = killed * 1000 / (lost > evidenceFloor ? lost : evidenceFloor);

				// Percentage deviation from the army's own average trade ratio.
				var deviationPct = (ratio - avgRatio) * 100 / avgRatio;
				var bias = deviationPct * scalePct / 100;

				if (bias > maxPct)
					bias = maxPct;
				else if (bias < -maxPct)
					bias = -maxPct;

				result[i] = (int)bias;
			}

			return result;
		}

		/// <summary><para>Apply <paramref name="biasPct"/> to <paramref name="baseTargets"/> as
		/// <c>base_i * (100 + bias_i) / 100</c> and re-apportion to exactly
		/// <see cref="ForceCompositionMath.Total"/>.</para>
		///
		/// <para>A zero base stays zero (a class the designer excluded is never reintroduced by feedback) and a
		/// large negative bias floors at 0 rather than inverting. Renormalisation is delegated to
		/// <see cref="ForceCompositionMath.SharesPerMille"/> — the largest-remainder apportionment with its
		/// ordinal tie-break is the subtle part of this arithmetic and exists in exactly one place.</para>
		///
		/// <para>A null/empty bias vector is an identity pass (targets renormalised only) — the inert
		/// default.</para></summary>
		public static int[] ApplyBias(int[] baseTargets, int[] biasPct)
		{
			if (baseTargets == null || baseTargets.Length == 0)
				return System.Array.Empty<int>();

			var adjusted = new int[baseTargets.Length];
			for (var i = 0; i < baseTargets.Length; i++)
			{
				var baseTarget = baseTargets[i] > 0 ? baseTargets[i] : 0;
				var bias = biasPct != null && i < biasPct.Length ? biasPct[i] : 0;
				if (baseTarget == 0 || bias == 0)
				{
					adjusted[i] = baseTarget;
					continue;
				}

				var scaled = (long)baseTarget * (100 + bias) / 100;
				adjusted[i] = scaled > 0 ? (int)scaled : 0;
			}

			return ForceCompositionMath.SharesPerMille(adjusted);
		}
	}
}
