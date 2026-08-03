#region Copyright & License Information
/*
 * WW3MOD frontline-influence Phase 7 — offense-side LATERAL SPREAD allocator (pure math).
 *
 * The diagnosis behind this (WORKSPACE/DISCOVERIES.md 2026-08-03): with SrPressureScoreMultiplier ~260 the
 * enemy-Supply-Route "Pressure" axis dominates the score field, so PoiOffenseMath.AllocateProportional hands it
 * the whole remainder — ~90% of the army funnels onto ONE standoff axis and pools mid-map. The design vision is
 * the opposite: SPREAD ALONG THE FRONT, press where the enemy is weakest, hold where too strong.
 *
 * This is a THIN post-transform on the existing score-proportional size vector — it invents no new
 * apportionment: it CAPS the Supply-Route Pressure axis at a configured share of the pool, then hands the capped
 * excess to the OTHER axes by REUSING the already-pinned FrontlineAllocationMath.AllocateAcrossAvenues (the same
 * coverage-first + Hamilton largest-remainder used by man-the-line), feeding per-axis OPPORTUNITY as the weight.
 * Coverage-first ⇒ every non-SR axis is staffed (the front is covered); the mass phase ⇒ surplus concentrates on
 * the weakest-enemy sectors (press where thin). The transform CONSERVES the total unit count exactly.
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws, pure integer math; all tie-breaks inherited from
 * AllocateAcrossAvenues (remainder desc, weight desc, lowest index). Two clients over the same synced inputs
 * decide identically.
 *
 * FOG: the caller derives OPPORTUNITY from the belief-side ControlField per-sector profile (same read the
 * weakest-point bias already uses) — no ground truth. v3-brain-portable: engine-free static math.
 */
#endregion

using System;
using System.Collections.Generic;

namespace OpenRA.Mods.Common.Traits
{
	public static class LateralSpreadMath
	{
		/// <summary>Cap the Supply-Route Pressure axis's share of the pool and redistribute the freed units across
		/// the non-SR axes by OPPORTUNITY, conserving the total. Inputs are aligned by axis index:
		///   * <paramref name="baseSizes"/> — the score-proportional sizes from AllocateProportional (authoritative
		///     base; this only RESHAPES it).
		///   * <paramref name="isSrPressure"/> — true for the enemy-Supply-Route Pressure axis (the funnel to cap).
		///   * <paramref name="opportunity"/> — per non-SR axis, "how weak is the enemy where this axis presses"
		///     (higher ⇒ more mass). Values &lt; 1 are floored to 1 so every non-SR axis is still covered. Ignored
		///     for SR axes.
		///
		/// <paramref name="srCapPct"/> is the SR axis's max share of <paramref name="total"/> (e.g. 40 = at most
		/// 40% of the pool), floored at <paramref name="minAxisSize"/> so a capped SR axis is never pushed below
		/// its funding minimum. <paramref name="srCapPct"/> &lt;= 0 OR &gt;= 100 ⇒ INERT (returns a copy of
		/// baseSizes unchanged — no cap). If there is no non-SR axis to receive the excess, the cap is NOT applied
		/// (the units would otherwise strand): when the enemy SR is the only viable target, funnelling onto it is
		/// correct.
		///
		/// Reuses <see cref="FrontlineAllocationMath.AllocateAcrossAvenues"/> for the redistribution: the per-axis
		/// opportunity is passed as the avenue ENEMY weight with a null OWN, so the mass weight is max(0, opp) = opp
		/// and coverage-first (minThreat 1) mans every non-SR axis. The returned add-vector sums to exactly the
		/// excess (coverage + Hamilton distribute the whole budget when at least one slot is manned), so
		/// sum(result) == sum(baseSizes). Pure, zero RNG.</summary>
		public static int[] Rebalance(IReadOnlyList<int> baseSizes, IReadOnlyList<bool> isSrPressure,
			IReadOnlyList<int> opportunity, int total, int srCapPct, int minAxisSize)
		{
			var n = baseSizes?.Count ?? 0;
			var result = new int[n];
			for (var i = 0; i < n; i++)
				result[i] = baseSizes[i];

			if (n == 0 || srCapPct <= 0 || srCapPct >= 100)
				return result; // inert: no cap.

			var cap = Math.Max(Math.Max(0, minAxisSize), (int)((long)Math.Max(0, total) * srCapPct / 100));

			long excess = 0;
			for (var i = 0; i < n; i++)
			{
				if (isSrPressure != null && i < isSrPressure.Count && isSrPressure[i] && result[i] > cap)
				{
					excess += result[i] - cap;
					result[i] = cap;
				}
			}

			if (excess <= 0)
				return result;

			// Compact the non-SR axes + their (floored) opportunity, preserving index order for a deterministic
			// mapping back. If NONE exist, the excess has nowhere to go ⇒ do not cap (restore the base).
			var idx = new List<int>(n);
			var opp = new List<int>(n);
			for (var i = 0; i < n; i++)
			{
				if (isSrPressure != null && i < isSrPressure.Count && isSrPressure[i])
					continue;
				idx.Add(i);
				var o = opportunity != null && i < opportunity.Count ? opportunity[i] : 1;
				opp.Add(o < 1 ? 1 : o);
			}

			if (idx.Count == 0)
			{
				for (var i = 0; i < n; i++)
					result[i] = baseSizes[i];
				return result;
			}

			// REUSE the pinned coverage-first + Hamilton allocator: opportunity as the enemy weight, null own ⇒
			// mass weight = opp; minThreat 1 ⇒ every non-SR axis is covered. Sums to exactly `excess`.
			var add = FrontlineAllocationMath.AllocateAcrossAvenues(opp, null, (int)excess, 1);
			for (var j = 0; j < idx.Count; j++)
				result[idx[j]] += add[j];

			return result;
		}
	}
}
