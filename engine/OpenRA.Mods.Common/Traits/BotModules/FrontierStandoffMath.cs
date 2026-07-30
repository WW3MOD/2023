#region Copyright & License Information
/*
 * WW3MOD influence stack — frontier standoff (@experimental) — rearward-push decision (pure math).
 *
 * PERCEIVED BEHAVIOUR: standoff units (the artillery echelon anchor, attack-heli standoff) no longer hold
 * ON the believed front line — they hold BEHIND it. Given a chosen standoff point that sits too close to the
 * believed-enemy frontier (ControlField's distance-to-enemy-region), the consumer walks it rearward along its
 * existing away-from-target axis until it clears a minimum frontier distance.
 *
 * It carries TWO coordinate-agnostic decisions over WPos, shared by both consumers (WPos echelon anchor and
 * the CPos heli engage cell converted to WPos):
 *   - RearwardStep: the per-hop offset that advances EXACTLY one coarse cell along the DOMINANT axis of the
 *     away bearing (Chebyshev / max-norm scaling). This is load-bearing: Euclidean scaling under-advances a
 *     diagonal (a step of one coarse-cell LENGTH only crosses ~0.7 coarse cells per axis), so consecutive hops
 *     re-read the SAME coarse grid cell and the walk clears fewer cells than intended. Max-norm guarantees a
 *     DISTINCT coarse cell every hop, in any direction.
 *   - RearwardSteps: how many hops to take — stop at the first hop that clears minCells, NEVER counting a hop
 *     that lands OFF-GRID (an off-grid cell reads the field's 'far' sentinel and would falsely look clear,
 *     placing the unit past the playable edge). The walk halts at the grid boundary; the last on-grid hop stands.
 * The push is BOUNDED by a step budget: a walk-back, never a free search.
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws, pure integer math over caller-sampled readings.
 * Two clients over the same synced belief state take the identical step and the identical number of hops.
 *
 * v3-portable: engine-free static math (NUnit-pinned in FrontierStandoffMathTest); only the tasking plumbing
 * that binds the samplers (PoiOffensiveBotModule echelon, HelicopterStates heli standoff) is engine-specific.
 */
#endregion

using System;

namespace OpenRA.Mods.Common.Traits
{
	public static class FrontierStandoffMath
	{
		/// <summary>The per-hop rearward offset: <paramref name="away"/> scaled so its DOMINANT (max-magnitude)
		/// axis measures exactly <paramref name="coarseCellLength"/> (one coarse cell in WDist). Max-norm — NOT
		/// Euclidean — so a diagonal hop still crosses a full coarse cell in each axis and consecutive hops always
		/// land on distinct coarse grid cells (Euclidean scaling under-advances the diagonal and re-reads a cell).
		/// Returns <see cref="WVec.Zero"/> when the bearing is degenerate. Integer only, no floats.</summary>
		public static WVec RearwardStep(WVec away, int coarseCellLength)
		{
			var max = Math.Max(Math.Abs(away.X), Math.Abs(away.Y));
			if (max <= 0 || coarseCellLength <= 0)
				return WVec.Zero;

			var x = (int)((long)away.X * coarseCellLength / max);
			var y = (int)((long)away.Y * coarseCellLength / max);
			return new WVec(x, y, 0);
		}

		/// <summary>Walk <paramref name="start"/> rearward in <paramref name="step"/> hops (up to
		/// <paramref name="maxSteps"/>) and return the number of hops to take. Stops at the first hop whose sampled
		/// <paramref name="frontierAt"/> reaches <paramref name="minCells"/>. NEVER counts a hop that fails
		/// <paramref name="onGrid"/> — the walk halts at the grid boundary and the last on-grid hop stands, so the
		/// result position is always on the playable area (an off-grid cell reads the 'far' sentinel and would
		/// falsely look clear). Returns 0 when the start already clears (⇒ no push, byte-identical). The caller
		/// applies <c>start + step*result</c>. Bounded, integer, zero RNG.</summary>
		public static int RearwardSteps(WPos start, WVec step, int minCells, int maxSteps,
			Func<WPos, int> frontierAt, Func<WPos, bool> onGrid)
		{
			if (minCells <= 0 || maxSteps <= 0)
				return 0;
			if (frontierAt(start) >= minCells)
				return 0;

			var taken = 0;
			for (var i = 1; i <= maxSteps; i++)
			{
				var p = start + new WVec(step.X * i, step.Y * i, 0);
				if (!onGrid(p))
					break;

				taken = i;
				if (frontierAt(p) >= minCells)
					break;
			}

			return taken;
		}
	}
}
