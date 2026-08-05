#region Copyright & License Information
/*
 * WW3MOD @experimental — composition-directed purchasing math (pure integer).
 *
 * PERCEIVED BEHAVIOUR: the army holds the shape the designer asked for instead of drifting toward whatever
 * type happens to live longest. The frozen path buys ground units by uniform lottery (UnitBuilderBotModule
 * .ChooseRandomUnitToBuild), so with a uniform BUY rate the standing composition settles proportional to each
 * type's LIFETIME — long-lived rear-line mortars pile up while frontline armour is eaten. This math replaces
 * the lottery with a census-vs-target deficit: measure what we ACTUALLY own (by value share), compare against
 * the designer's target shares, and buy the type that is furthest BELOW its target.
 *
 * WHY VALUE SHARES, NOT HEAD COUNTS: composition is a budget statement. Ten riflemen are not "ten times the
 * army" of one tank, so both census and targets are per-mille of army VALUE.
 *
 * COUNTER-BIAS: the target vector is nudged by the BELIEVED enemy composition (fog-legal, supplied by the
 * caller) — heavy believed enemy armor raises the anti-tank target, heavy believed air raises anti-air. It is
 * a BIAS on the designer's shape, clamped to +/-biasMaxPct, never a replacement for it; the shares are then
 * renormalised so the vector still sums to exactly 1000.
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws, integer-only, no world/actor references — plain
 * arrays in and out. Apportionment is largest-remainder (Hamilton) with an ORDINAL-INDEX tie-break, and the
 * argmax is a single ordered walk with a strict-greater comparison, so every function is a pure deterministic
 * map from its arguments. The caller is responsible for building its arrays in a fixed ordinal order (the
 * module flattens its Dictionaries ONCE in Created for exactly this reason).
 *
 * BYTE-IDENTITY: nothing here is reachable unless UnitBuilderBotModuleInfo.CompositionDirected is true, which
 * defaults false and is set only in the two @experimental faction blocks — so normal/rush/turtle/@stable never
 * enter this path and keep their RNG draw count and order.
 *
 * Split out as a pure static class (mirrors CompositionNeedMath / EscortSizingMath / FrontlineAllocationMath)
 * so the whole decision is NUnit-pinned WITHOUT a game run.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	public static class ForceCompositionMath
	{
		/// <summary>Total the shares always sum to. Per-mille keeps the integer math precise enough to
		/// distinguish a 50‰ mortar allocation from a 20‰ one without floating point.</summary>
		public const int Total = 1000;

		/// <summary>Per-mille apportionment of <paramref name="values"/> summing to EXACTLY <see cref="Total"/>
		/// (largest-remainder / Hamilton). Each entry gets <c>value * 1000 / sum</c> floored, then the leftover
		/// units are handed out one each to the largest remainders, ties broken by the LOWER ordinal index.
		/// An all-zero (or null/empty/negative-only) input returns all zeros — "no army yet" is not a shape.</summary>
		public static int[] SharesPerMille(int[] values)
		{
			if (values == null || values.Length == 0)
				return System.Array.Empty<int>();

			var result = new int[values.Length];

			long sum = 0;
			for (var i = 0; i < values.Length; i++)
				if (values[i] > 0)
					sum += values[i];

			// No value at all ⇒ all zeros. Deliberately NOT an even split: an empty army has no shape, and
			// returning zeros lets every target read as a full deficit so the first buys follow the targets.
			if (sum <= 0)
				return result;

			var allocated = 0;
			for (var i = 0; i < values.Length; i++)
			{
				var v = values[i] > 0 ? values[i] : 0;
				result[i] = (int)(v * (long)Total / sum);
				allocated += result[i];
			}

			// Hand out the floor remainder, largest fractional part first, ordinal-index tie-break.
			var remaining = Total - allocated;
			while (remaining > 0)
			{
				var best = -1;
				long bestRemainder = -1;
				for (var i = 0; i < values.Length; i++)
				{
					var v = values[i] > 0 ? values[i] : 0;
					if (v == 0)
						continue;

					// Fractional part of v*Total/sum, scaled by sum so it stays integer.
					var remainder = v * (long)Total - result[i] * sum;
					if (remainder > bestRemainder)
					{
						bestRemainder = remainder;
						best = i;
					}
				}

				if (best < 0)
					break;

				result[best]++;
				remaining--;
			}

			return result;
		}

		/// <summary>Integer exponential moving average: <c>(prev*(100-alpha) + obs*alpha)/100</c>, applied
		/// elementwise. Smooths the believed-enemy threat shares so a single scouted tank does not whipsaw the
		/// whole purchase plan. <paramref name="alphaPct"/> is clamped to [0,100]; 0 freezes at
		/// <paramref name="prevSmoothed"/>, 100 tracks <paramref name="observed"/> exactly. A null/short
		/// <paramref name="prevSmoothed"/> (first tick, or a config change) is treated as zeros.</summary>
		public static int[] SmoothShares(int[] prevSmoothed, int[] observed, int alphaPct)
		{
			if (observed == null || observed.Length == 0)
				return System.Array.Empty<int>();

			if (alphaPct < 0)
				alphaPct = 0;

			if (alphaPct > 100)
				alphaPct = 100;

			var result = new int[observed.Length];
			for (var i = 0; i < observed.Length; i++)
			{
				var prev = prevSmoothed != null && i < prevSmoothed.Length ? prevSmoothed[i] : 0;
				result[i] = (prev * (100 - alphaPct) + observed[i] * alphaPct) / 100;
			}

			return result;
		}

		/// <summary>Nudge the designer's <paramref name="baseTargets"/> toward countering the believed enemy,
		/// then renormalise to exactly <see cref="Total"/>.
		///
		/// For own-role <c>i</c>: <c>bias_i = sum_j matrixPct[j,i] * threatShares[j] / 1000</c> — i.e. each
		/// enemy class j contributes its counter weight in proportion to how much of the believed enemy force
		/// it is. A threat share strictly below <paramref name="deadbandPerMille"/> contributes NOTHING (one
		/// scouted scout must not re-plan the army). The summed bias is clamped to
		/// +/-<paramref name="biasMaxPct"/>, applied as <c>base_i * (100 + bias) / 100</c>, and the result is
		/// re-apportioned by largest remainder so the vector still sums to exactly 1000.
		///
		/// <paramref name="matrixPct"/> is indexed [enemyClass, ownRole]; a null matrix, a null/empty threat
		/// vector, or <paramref name="biasMaxPct"/> &lt;= 0 makes this an identity pass (targets renormalised
		/// only) — the inert default.</summary>
		public static int[] ApplyCounterBias(int[] baseTargets, int[] threatShares, int[,] matrixPct,
			int biasMaxPct, int deadbandPerMille)
		{
			if (baseTargets == null || baseTargets.Length == 0)
				return System.Array.Empty<int>();

			var roles = baseTargets.Length;
			var adjusted = new int[roles];

			var canBias = matrixPct != null && threatShares != null && threatShares.Length > 0 && biasMaxPct > 0
				&& matrixPct.GetLength(0) > 0 && matrixPct.GetLength(1) == roles;

			for (var i = 0; i < roles; i++)
			{
				var baseTarget = baseTargets[i] > 0 ? baseTargets[i] : 0;
				if (!canBias || baseTarget == 0)
				{
					adjusted[i] = baseTarget;
					continue;
				}

				long bias = 0;
				var classes = matrixPct.GetLength(0) < threatShares.Length ? matrixPct.GetLength(0) : threatShares.Length;
				for (var j = 0; j < classes; j++)
				{
					var share = threatShares[j];

					// Deadband: an enemy class we have barely seen contributes nothing at all.
					if (share < deadbandPerMille || share <= 0)
						continue;

					bias += (long)matrixPct[j, i] * share / Total;
				}

				if (bias > biasMaxPct)
					bias = biasMaxPct;
				else if (bias < -biasMaxPct)
					bias = -biasMaxPct;

				var scaled = baseTarget * (100 + bias) / 100;

				// A large negative bias must never flip a target negative; 0 is the floor.
				adjusted[i] = scaled > 0 ? (int)scaled : 0;
			}

			return SharesPerMille(adjusted);
		}

		/// <summary>Pick the eligible entry with the LARGEST <c>target - census</c> — the type the army is
		/// furthest short of. Strict-greater comparison walking ordinally, so ties resolve to the LOWER index.
		///
		/// There is deliberately NO positive-deficit requirement: when every eligible type already sits at or
		/// above its target this returns the LEAST-OVER one. That keeps the purchase VOLUME identical to the
		/// frozen path (we still buy on every cycle — budget is spent, not withheld) while keeping the
		/// proportions as close to target as a single buy can. Returns -1 ONLY when nothing is eligible, which
		/// the caller treats as "this queue has no composition opinion" and falls back to the legacy pick.
		///
		/// NOTE for anyone tempted to add a "only buy classes under target" filter here: restricting this
		/// argmax to deficit &gt; 0 and falling back to the unrestricted argmax when that finds nothing is a
		/// PROVABLE NO-OP. If any eligible slot is under target then the unrestricted maximizer is itself under
		/// target, so it lies in the restricted candidate set and both walks tie-break to the same lowest
		/// index; if none is, the restricted pass returns -1 and the fallback runs the unrestricted pass
		/// anyway. The useful property — an over-target class is never bought while any class is still short —
		/// is already a property of the plain argmax below.</summary>
		public static int SelectDeficit(int[] targetsPerMille, int[] censusPerMille, bool[] eligible)
		{
			if (targetsPerMille == null || eligible == null)
				return -1;

			var best = -1;
			var bestDeficit = 0;

			for (var i = 0; i < targetsPerMille.Length; i++)
			{
				if (i >= eligible.Length || !eligible[i])
					continue;

				var census = censusPerMille != null && i < censusPerMille.Length ? censusPerMille[i] : 0;
				var deficit = targetsPerMille[i] - census;

				if (best < 0 || deficit > bestDeficit)
				{
					best = i;
					bestDeficit = deficit;
				}
			}

			return best;
		}

		/// <summary>Should this build cycle DECLINE rather than fall back to the legacy uniform lottery?
		///
		/// Only when the ceiling is enabled, the deficit pick found nothing, AND at least one composed type is
		/// actually buildable from this queue. That last term is the whole point: "no composed type is
		/// buildable here at all" is genuine no-opinion (a heli-only pool) and MUST still fall back so purchase
		/// volume is unchanged, whereas "composed types exist but every one is priced out or at its UnitLimit"
		/// is a decision not to buy — and falling back there draws the lifetime-proportional lottery that
		/// composition-directed purchasing exists to remove.</summary>
		public static bool ShouldDeclineCycle(bool ceilingEnabled, bool selectionFound, bool anyComposedTypeBuildable)
		{
			return ceilingEnabled && !selectionFound && anyComposedTypeBuildable;
		}

		/// <summary>Is an externally requested call-in of <paramref name="candidateCost"/> at slot
		/// <paramref name="slot"/> ALREADY at or over its target share — i.e. excluding the request's own
		/// pending credit?
		///
		/// The caller's census credits every entry of its request lists, and the request under test is still on
		/// one of them when this is asked, so its own cost has to come back out first. Without that subtraction
		/// the rule silently becomes "would be over AFTER this buy", which refuses a class that is legitimately
		/// still short by less than one unit's worth of share.
		///
		/// <paramref name="censusValues"/> is the raw per-slot VALUE census (not yet apportioned); it is
		/// copied, never mutated. A slot outside either array, or a null input, reads as "not over".</summary>
		public static bool RequestExceedsCeiling(int[] censusValues, int slot, int candidateCost, int[] targetsPerMille)
		{
			if (censusValues == null || targetsPerMille == null || slot < 0 || slot >= censusValues.Length)
				return false;

			var adjusted = (int[])censusValues.Clone();
			adjusted[slot] = adjusted[slot] > candidateCost ? adjusted[slot] - candidateCost : 0;

			return DeficitAt(targetsPerMille, SharesPerMille(adjusted), slot) <= 0;
		}

		/// <summary>The deficit the selection was made on — exposed so the caller can log it without
		/// recomputing. Safe on an out-of-range index (returns 0).</summary>
		public static int DeficitAt(int[] targetsPerMille, int[] censusPerMille, int index)
		{
			if (targetsPerMille == null || index < 0 || index >= targetsPerMille.Length)
				return 0;

			var census = censusPerMille != null && index < censusPerMille.Length ? censusPerMille[index] : 0;
			return targetsPerMille[index] - census;
		}
	}
}
