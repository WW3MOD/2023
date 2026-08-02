#region Copyright & License Information
/*
 * WW3MOD @experimental — AdaptiveProduction threat-aware unit-need scoring (pure integer).
 *
 * PERCEIVED BEHAVIOUR: the bot buys what the battlefield NEEDS, not just a fixed composition. It reads the
 * BELIEVED enemy composition (the fog-legal belief store — never ground truth) and scores each thing it can
 * call in by how much that class is warranted RIGHT NOW:
 *   * heavy believed enemy ARMOR      -> raise the anti-armor score        (CounterScore)
 *   * heavy believed enemy INFANTRY   -> raise the anti-infantry score     (CounterScore)
 *   * heavy believed enemy AIR        -> raise the anti-air score          (CounterScore)
 *   * WEAK believed enemy ANTI-AIR    -> open an AIR-STRIKE window         (AirOpportunityScore)
 * The air-strike term is the interesting one: it is a GAP detector, not a counter. When the enemy sky reads
 * weakly defended AND there is a believed ground force worth hitting, the score for our expensive strike
 * airframes (attack heli / strike jet) rises — so a normally-rare buy becomes real when the situation warrants
 * it. An affordability gate then keeps pricey airframes rare-but-real: a call-in is only warranted when we hold
 * a reserve multiple of its cost banked, so cost naturally differentiates a $600 AT infantryman (always
 * affordable) from a $6000 attack helicopter (only when the economy is healthy).
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws. All scores are integer functions of caller-
 * supplied believed values; the winner is a single ordered argmax with a fixed Order tie-break. No dictionary
 * or hash enumeration feeds the decision — the caller passes an ordered candidate list.
 *
 * BYTE-IDENTITY: every weight defaults to 0 on the trait (CompositionNeedEnabled off, all *NeedWeight 0), so
 * every score is 0, SelectNeed returns -1, and the module makes no request — the @stable/frozen twin (which
 * omits these fields) is byte-identical. The pass is only wired up on the @experimental twins via YAML.
 *
 * Split out as a pure static class (mirrors AdaptiveRoutingMath / CaptureSupplyMath) so the scoring is
 * NUnit-pinned WITHOUT a game run: gap detection at the weak-AA boundary, the affordability gate, the
 * weights-off == legacy no-op, and tie-break determinism.
 */
#endregion

using System.Collections.Generic;

namespace OpenRA.Mods.Common.Traits
{
	public static class CompositionNeedMath
	{
		/// <summary>Concentration score: believed enemy value in a class raises the matching counter's need.
		/// <c>score = believedClassValue * weightPct / 100</c> (weightPct is a percent knob). Returns 0 when
		/// there is no believed value or the lever is off (weightPct &lt;= 0) — the legacy no-op.</summary>
		public static long CounterScore(long believedClassValue, int weightPct)
		{
			if (believedClassValue <= 0 || weightPct <= 0)
				return 0;

			return believedClassValue * weightPct / 100;
		}

		/// <summary>Gap score: a WEAK believed enemy anti-air posture opens an air-strike window. The score
		/// rises as the believed anti-air value falls below <paramref name="weakThreshold"/>, scaled by the
		/// believed ground force worth striking (more targets => more worth the expensive airframe):
		/// <c>score = weightPct * groundValue * (weakThreshold - aaValue) / (weakThreshold * 100)</c>.
		/// Returns 0 when the sky is defended (<paramref name="believedAaValue"/> &gt;= threshold), when there
		/// is nothing to hit (<paramref name="believedGroundValue"/> &lt;= 0), or when the lever is off
		/// (weightPct/threshold &lt;= 0). Integer math, monotonic in the gap.</summary>
		public static long AirOpportunityScore(long believedAaValue, long believedGroundValue,
			int weakThreshold, int weightPct)
		{
			if (weightPct <= 0 || weakThreshold <= 0 || believedGroundValue <= 0)
				return 0;

			if (believedAaValue < 0)
				believedAaValue = 0;

			if (believedAaValue >= weakThreshold)
				return 0;

			var gap = weakThreshold - believedAaValue; // (0, weakThreshold]
			return (long)weightPct * believedGroundValue * gap / ((long)weakThreshold * 100);
		}

		/// <summary>Affordability gate: an expensive call-in is warranted only when we hold
		/// <paramref name="reservePct"/>% of its cost banked (100 = exactly affordable, 200 = need 2x). This is
		/// what keeps pricey airframes rare-but-real — cost differentiates a cheap counter from a $6000 attack
		/// heli. An unknown cost (&lt;= 0) or a disabled gate (reservePct &lt;= 0) is always affordable.</summary>
		public static bool Affordable(long availableBudget, int unitCost, int reservePct)
		{
			if (unitCost <= 0 || reservePct <= 0)
				return true;

			return availableBudget * 100 >= (long)unitCost * reservePct;
		}

		/// <summary>A buyable category: its computed need <see cref="Score"/>, the <see cref="Cost"/> of the
		/// cheapest unit we would call in from its pool (drives affordability), and a fixed <see cref="Order"/>
		/// used only as the tie-break (lower wins).</summary>
		public readonly struct Candidate
		{
			public readonly long Score;
			public readonly int Cost;
			public readonly int Order;

			public Candidate(long score, int cost, int order)
			{
				Score = score;
				Cost = cost;
				Order = order;
			}
		}

		/// <summary>Deterministic argmax over the affordable candidates. Returns the index (into
		/// <paramref name="candidates"/>) of the affordable candidate with the greatest <see cref="Candidate.Score"/>;
		/// ties are broken by the smaller <see cref="Candidate.Order"/> (the earlier-priority category wins).
		/// Returns -1 when nothing qualifies (every score &lt;= 0, or none passes the affordability gate) — the
		/// caller then makes no request. Zero RNG: one ordered walk, no allocation.</summary>
		public static int SelectNeed(IReadOnlyList<Candidate> candidates, long availableBudget, int reservePct)
		{
			if (candidates == null)
				return -1;

			var best = -1;
			long bestScore = 0;
			var bestOrder = int.MaxValue;

			for (var i = 0; i < candidates.Count; i++)
			{
				var c = candidates[i];
				if (c.Score <= 0)
					continue;

				if (!Affordable(availableBudget, c.Cost, reservePct))
					continue;

				if (c.Score > bestScore || (c.Score == bestScore && c.Order < bestOrder))
				{
					bestScore = c.Score;
					bestOrder = c.Order;
					best = i;
				}
			}

			return best;
		}
	}
}
