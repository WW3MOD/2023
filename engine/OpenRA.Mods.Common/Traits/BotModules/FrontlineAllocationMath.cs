#region Copyright & License Information
/*
 * WW3MOD frontline-influence Phase 5 — whole-front allocation + weakest-point attack (pure math).
 *
 * The DECISION math the Phase-5 consumers (LayeredDefence man-the-line, PoiOffensive weakest-point bias +
 * sector posture-hold) delegate to, split from the traits (mirroring FrontlineProfileMath / RetreatDamperMath /
 * CombatRetreatMath) so it is NUnit-pinnable without mounting a World. Reads the Phase-4 ControlField frontline
 * strength profile (per-sector believed OWN vs ENEMY strength + avenue→sector map) and turns it into three
 * numbers-only decisions:
 *
 *   (1) MAN-THE-LINE — AllocateAcrossAvenues: spread a fixed reserve budget across the enumerated crossing
 *       avenues so every avenue carrying a meaningful believed threat gets at least a picket (where force
 *       allows), and any surplus concentrates where the enemy OUTNUMBERS us. Coverage first, mass second.
 *
 *   (2) WEAKEST-POINT BIAS — WeakestSectorBiasFactor: a score MULTIPLIER (x100) that boosts an attack axis
 *       whose target sits in the believed-thinnest enemy frontier sector. A BIAS, not an override — the
 *       existing PoiOffense scoring/comparator stays authoritative; a bare enable (mul 100) is inert.
 *
 *   (3) POSTURE HOLD — SectorPostureHold: true when a target sector's believed enemy force is too strong
 *       relative to our own committed strength there (a ratio threshold), so the axis holds/defends rather
 *       than pressing. Shaped as a HOLD TRIGGER the consumer feeds into the SAME retreat/damper fall-back
 *       path (never a new order writer), and gated to run only AFTER the genuine-retreat gate — so it can
 *       never block a truly-losing withdrawal.
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws, pure integer math, fixed iteration + stable
 * tie-breaks (weight desc then lowest index). Two clients over the same synced profile decide identically.
 *
 * FOG: every input is a believed-side profile read (ControlField, already belief-side) or map-static avenue
 * geometry — no ground truth. v3-brain-portable: engine-free static math.
 */
#endregion

using System;
using System.Collections.Generic;

namespace OpenRA.Mods.Common.Traits
{
	public static class FrontlineAllocationMath
	{
		// ---------- (1) Man-the-line defend allocation ----------

		/// <summary><para>Spread <paramref name="totalForce"/> reserve pickets across avenues from the per-sector believed
		/// strength profile. <paramref name="avenueEnemy"/>/<paramref name="avenueOwn"/> carry, PER AVENUE, the
		/// believed ENEMY / OWN strength of the frontier sector that avenue opens into (so two crossings in one
		/// sector each read that sector's strength — both get manned). Returns a per-avenue picket count (length =
		/// avenue count), summing to at most <paramref name="totalForce"/>.</para>
		///
		/// <para>Two phases, both deterministic:
		///   * COVERAGE — every avenue with a MEANINGFUL threat (avenueEnemy &gt;= <paramref name="minThreat"/>) gets
		///     one guaranteed picket, in (enemy desc, index asc) order, until the budget runs out. This is the
		///     "man every crossing the enemy can actually use" guarantee.
		///   * MASS — any leftover budget is distributed across the manned avenues by largest-remainder (Hamilton),
		///     weighted by how far the enemy OUTNUMBERS us there (max(0, enemy − own)) — surplus concentrates where
		///     we are outweighed; a sector we already hold draws none of the surplus but keeps its picket.
		/// Zero RNG; tie-break is highest remainder then highest weight then lowest index.</para></summary>
		public static int[] AllocateAcrossAvenues(IReadOnlyList<int> avenueEnemy, IReadOnlyList<int> avenueOwn,
			int totalForce, int minThreat)
		{
			var n = avenueEnemy?.Count ?? 0;
			var result = new int[n];
			if (n == 0 || totalForce <= 0)
				return result;

			// The manned set: avenues carrying a meaningful believed threat. Ordered enemy desc, index asc, so the
			// heaviest-pressed crossing is picketed first when the budget can't cover them all.
			var manned = new List<int>();
			for (var i = 0; i < n; i++)
				if (avenueEnemy[i] >= minThreat)
					manned.Add(i);

			if (manned.Count == 0)
				return result;

			manned.Sort((a, b) =>
			{
				var byThreat = avenueEnemy[b].CompareTo(avenueEnemy[a]);
				return byThreat != 0 ? byThreat : a.CompareTo(b);
			});

			// COVERAGE: one guaranteed picket each, until the budget is spent.
			var remaining = totalForce;
			var mannedCovered = 0;
			foreach (var i in manned)
			{
				if (remaining <= 0)
					break;
				result[i] = 1;
				remaining--;
				mannedCovered++;
			}

			if (remaining <= 0)
				return result;

			// MASS: largest-remainder over the outnumbered-weight of the avenues that actually got a picket.
			var weight = new int[n];
			long weightSum = 0;
			for (var c = 0; c < mannedCovered; c++)
			{
				var i = manned[c];
				var w = avenueEnemy[i] - (avenueOwn != null && i < avenueOwn.Count ? avenueOwn[i] : 0);
				if (w < 0)
					w = 0;
				weight[i] = w;
				weightSum += w;
			}

			if (weightSum <= 0)
				return result; // every manned avenue is at/under parity — coverage only, no surplus concentration.

			// Floor share + fractional remainder per avenue (Hamilton apportionment of `remaining`).
			var remainders = new List<(int Index, long Rem, int Weight)>();
			for (var c = 0; c < mannedCovered; c++)
			{
				var i = manned[c];
				if (weight[i] == 0)
					continue;

				var exact = (long)remaining * weight[i];
				var floor = exact / weightSum;
				result[i] += (int)floor;
				remainders.Add((i, exact - floor * weightSum, weight[i]));
			}

			var handedOut = 0;
			for (var c = 0; c < mannedCovered; c++)
				if (weight[manned[c]] > 0)
					handedOut += (int)((long)remaining * weight[manned[c]] / weightSum);

			var leftover = remaining - handedOut;
			if (leftover <= 0)
				return result;

			// Deterministic tie-break: highest fractional remainder, then heaviest weight, then lowest index.
			remainders.Sort((a, b) =>
			{
				var byRem = b.Rem.CompareTo(a.Rem);
				if (byRem != 0)
					return byRem;
				var byWeight = b.Weight.CompareTo(a.Weight);
				return byWeight != 0 ? byWeight : a.Index.CompareTo(b.Index);
			});

			for (var k = 0; k < leftover && k < remainders.Count; k++)
				result[remainders[k].Index]++;

			return result;
		}

		// ---------- (2) Weakest-point attack bias ----------

		/// <summary>Score MULTIPLIER (x100) for an attack axis whose target lies in <paramref name="targetSector"/>,
		/// given the believed-weakest enemy frontier sector <paramref name="weakestSector"/>. Returns
		/// <paramref name="biasMultiplier"/> when the target sits in the weakest sector (bias the push there),
		/// else 100 (neutral). <paramref name="weakestSector"/> == <see cref="FrontlineProfileMath.NoSector"/>
		/// (−1, no believed front) ⇒ always 100. A BIAS, not an override: the caller multiplies the existing
		/// score and re-sorts with the SAME comparator, so a bare enable (biasMultiplier == 100) is inert and the
		/// ranking is byte-identical. Zero RNG.</summary>
		public static int WeakestSectorBiasFactor(int targetSector, int weakestSector, int biasMultiplier)
		{
			if (weakestSector == FrontlineProfileMath.NoSector || targetSector != weakestSector)
				return 100;

			return biasMultiplier;
		}

		// ---------- (3) Sector posture hold ----------

		/// <summary><para>Should an axis standing in a frontier sector HOLD/defend instead of pressing, because the
		/// believed enemy force there is too strong relative to our own committed strength? True when the sector is
		/// ON THE FRONT (<paramref name="frontierEdges"/> &gt; 0), carries believed enemy force, we actually OCCUPY
		/// it (<paramref name="sectorOwn"/> ≥ <paramref name="ownStrengthFloor"/>), and <paramref name="sectorEnemy"/>
		/// ≥ <paramref name="sectorOwn"/> × <paramref name="holdRatioPct"/>/100.</para>
		///
		/// <para><paramref name="holdRatioPct"/> &lt;= 0 ⇒ false (inert / disabled). The ratio comparison is cross-
		/// multiplied (no division), so the boundary is exact: at holdRatioPct 200, own 5 vs enemy 10 HOLDS (enemy
		/// is exactly 2× own) while own 5 vs enemy 9 presses.</para>
		///
		/// <para>OWN-STRENGTH FLOOR (<paramref name="ownStrengthFloor"/>): you cannot HOLD a sector you do not occupy.
		/// Below the floor of believed own presence the ratio is meaningless — own ≈ 0 makes "enemy ≥ own × ratio"
		/// trivially true, which (when the caller mistakenly evaluated the enemy-REAR target sector, where our
		/// believed presence is ~0) froze every offensive axis at home. The consumer now evaluates the axis's own
		/// CONTACT sector — where its units stand, so sectorOwn reflects the committed force — and this floor is the
		/// backstop that keeps an unoccupied sector from ever reading as a hold. <paramref name="ownStrengthFloor"/>
		/// &lt;= 0 disables the floor (legacy: own = 0 vs enemy present ⇒ hold).</para>
		///
		/// <para>SAFETY: this is a HOLD trigger the consumer runs AFTER its genuine-retreat gate, so it can never block a
		/// truly-losing withdrawal (that decision is upstream). It composes with the retreat/damper FSM by reusing
		/// the same fall-back-to-rally order, not by writing a competing order stream. Zero RNG.</para></summary>
		public static bool SectorPostureHold(int sectorOwn, int sectorEnemy, int frontierEdges, int holdRatioPct,
			int ownStrengthFloor)
		{
			if (holdRatioPct <= 0)
				return false;
			if (frontierEdges <= 0)
				return false;
			if (sectorEnemy <= 0)
				return false;
			if (sectorOwn < ownStrengthFloor)
				return false;

			return (long)sectorEnemy * 100 >= (long)sectorOwn * holdRatioPct;
		}

		// ---------- (3b) Posture hold BUDGET — "look, then commit" ----------

		/// <summary>Has this axis spent its per-mission budget of posture holds? <paramref name="maxHoldEvals"/>
		/// &lt;= 0 ⇒ false (unbounded — legacy behaviour).
		///
		/// <para>WHY A BUDGET AT ALL — the general reason, which holds however the hold fails in practice: an
		/// unclearable caution is a permanent veto wearing the costume of a delay. The consumer documents THREE
		/// candidate failure shapes (silent period-1 stall / period-2 oscillation / selection churn), none of them
		/// yet confirmed by a game run; a bound is warranted under all three, and is what makes the gate terminate.</para>
		///
		/// <para>THE COUPLING, which is a real defect independent of which shape dominates and is NOT solved here.
		/// <see cref="SectorPostureHold"/> is evaluated at the axis's CONTACT sector, and a sector's own-strength is
		/// a live count of our armed actors standing in it — so the gate can read its own consequence: if the hold
		/// moves the axis out of the sector, sectorOwn falls, the own-strength floor fails OPEN, and the verdict
		/// flips. HOW STRONG that self-reference is depends entirely on the sector, and the honest bound is much
		/// weaker than "the axis IS sectorOwn": a sector is a full-map-HEIGHT column strip (~12 cells wide on
		/// river-zeta) and the strength sum spans the WHOLE column, counting own AND allied actors including
		/// defensive structures. So the axis dominates sectorOwn only in a flank strip that crosses none of our
		/// line, no garrison and no other axis — the deep-flank case, and not the general one. Near our own base
		/// the column carries so much other strength that the axis barely moves the number. A budget TRUNCATES this
		/// coupling rather than removing it; a non-self-referential predicate (e.g. the axis's own OwnAxisStrength,
		/// which travels with the axis and is invariant to where it stands) would be the actual repair, and would
		/// STILL need a budget to terminate — so this bound is necessary either way, and the self-reference remains
		/// open.</para>
		///
		/// <para>A budget is the standard shape for this in the module (see RetreatDamperMath.HoldBudgetExhausted,
		/// added for the same class of failure): a momentary caution may DELAY a press, it may not veto it forever.
		/// The counter is monotone over one (target, uninterrupted axis existence) — an Axis is keyed by target and
		/// never retargeted, so the object's lifetime is the mission, but DESTROYING the axis refunds the budget in
		/// full. That is a real limit: selection churn that retires and re-forms an axis for the same POI defeats
		/// the bound entirely. Within one lifetime the monotonicity is what makes the counter immune to a flipping
		/// verdict — a counter that decayed on the press half would refund the budget as fast as it was spent.</para>
		///
		/// <para>SAFETY — handed off, not preserved unconditionally. Fails OPEN (toward pressing), as the damper's
		/// budget does. This gate only ever runs on a NON-retreating axis (the consumer's genuine-retreat gate
		/// returned upstream), so an exhausted budget cannot convert an in-progress withdrawal into a last stand.
		/// But "released" is not "retreating": PartitionHeldAxes releases a committed axis on a BARE force-ratio read
		/// (CombatRetreatMath.ShouldReleaseHeld → LosingBeyond, no sustain window) while the axis's own Retreat
		/// decision needs RetreatSustainEvals (2) consecutive losing evals. In that gap a losing axis re-enters the
		/// consumer, fails the retreat gate, and meets an exhausted posture gate that waves it through — roughly two
		/// evals in which it presses with no posture brake. The real safety argument is not that nothing changes, it
		/// is that the decision MOVES TO A BETTER SENSOR: the retreat gate compares the axis's own health-weighted
		/// build value against believed enemy COST within ForceRatioRadiusCells of its centroid, which is a strictly
		/// more local and better-scaled danger read than posture's whole-column presence COUNTS. Exhausting posture
		/// therefore defers to the sharper instrument, with a bounded latency.</para>
		/// Pure integer, zero RNG.</summary>
		public static bool PostureBudgetExhausted(int holdEvals, int maxHoldEvals)
			=> maxHoldEvals > 0 && holdEvals >= maxHoldEvals;

		/// <summary>Advance the posture-hold budget counter that <see cref="PostureBudgetExhausted"/> reads.
		/// Counts UP on an eval that actually held and is otherwise UNCHANGED — deliberately monotone, see the
		/// coupling note on <see cref="PostureBudgetExhausted"/>. WHEN A CAP IS SET (<paramref name="maxHoldEvals"/>
		/// &gt; 0) it saturates there, so exhaustion is sticky and the counter cannot grow without bound; with the
		/// cap at its 0 default nothing reads the counter and it simply keeps counting (the step stays total).
		/// Reset is external and happens only on a genuine mission break (the axis retreats or loses its units); a
		/// fresh target is a fresh Axis object, hence a fresh counter — which also means axis DESTRUCTION refunds
		/// the whole budget. Pure, zero RNG.</summary>
		public static int StepPostureHold(int holdEvals, bool holding, int maxHoldEvals)
		{
			if (!holding)
				return holdEvals;

			if (maxHoldEvals > 0 && holdEvals >= maxHoldEvals)
				return holdEvals;

			return holdEvals + 1;
		}
	}
}
