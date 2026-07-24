#region Copyright & License Information
/*
 * WW3MOD influence stack — Stage E: ground danger-field consumer (pure navigation math).
 *
 * Stage D taught HELICOPTERS to route around the anti-AIR field; Stage E teaches GROUND
 * movers to route around the anti-GROUND field. Two perceived behaviours reduce to ONE
 * decision — "insert a lateral waypoint when the straight path crosses a kill zone":
 *   (1) ATTACKS FLOW AROUND STRONGPOINTS. An offensive axis whose beeline to the objective
 *       crosses a dense defended choke is steered onto a lateral lane past it, then in.
 *   (2) HIGH-VALUE MOVERS PULL BACK, TRAVEL LATERALLY, RE-ENTER. A supply truck relocating
 *       along the front doesn't drive point-to-point through the danger; it detours toward
 *       the SAFER side. Because the Stage-B/C ground field carries a TERRITORY BASELINE
 *       (deep believed-enemy ground reads expensive, the friendly rear reads ~0), the
 *       exposure-minimising side of the detour is the REAR — so the rear-lateral-re-enter
 *       pattern EMERGES from the danger cost, it is not scripted. A larger lateral budget
 *       (MaxSteps) just lets a high-value mover push that safe waypoint deeper.
 *
 * This is the Stage-D pattern deliberately kept SEPARATE from HeliDangerNav (which stays
 * byte-identical): a pure-function class over a ground-danger sampler (Func<CPos,int>),
 * NUnit-testable without mounting a world. The caller binds the sampler to the mover's own
 * per-player GROUND channel — fog-legal by construction. Off-map / invalid cells are passed
 * as `Impassable` so a detour never routes off the playable area.
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws. Every search is an integer-only
 * walk over a fixed candidate order, the safer side wins on strict merit (worst < best), ties
 * break by iteration order — two clients over the same field pick the same waypoint.
 *
 * PERF: a request is O(steps · pathLen) sampler reads (one direct line-walk + a bounded set of
 * two-leg candidates). No per-tick full-field scan, no O(map) work — far under a single A*
 * request, and only issued at the module's slow re-eval cadence.
 */
#endregion

using System;

namespace OpenRA.Mods.Common.Traits.BotModules.Squads
{
	public static class GroundDangerNav
	{
		// Callers pass this for off-map / otherwise-invalid cells: treated as maximally unsafe so a
		// detour search never lands a waypoint off the playable area.
		public const int Impassable = int.MaxValue;

		// Deterministic candidate order for detour lateral offsets: side +1 before -1 (stable tie-break).
		// Static to avoid per-call allocation.
		static readonly int[] DetourSides = { 1, -1 };

		// Integer division rounded to nearest, sign-correct (den assumed > 0). Interpolates cells
		// along a line without floating point, so the sampled path is identical on every client.
		static int RoundDiv(int num, int den)
		{
			return num >= 0 ? (num + den / 2) / den : -((-num + den / 2) / den);
		}

		/// <summary>Highest ground-danger reading sampled along the integer cell line from
		/// <paramref name="from"/> to <paramref name="to"/> (both inclusive). The "how exposed is this
		/// route" measure — the input to the flow-around / rear-route decision.</summary>
		public static int PathMaxGroundDanger(CPos from, CPos to, Func<CPos, int> groundDangerAt)
		{
			var dx = to.X - from.X;
			var dy = to.Y - from.Y;
			var steps = Math.Max(Math.Abs(dx), Math.Abs(dy));

			var max = groundDangerAt(from);
			for (var i = 1; i <= steps; i++)
			{
				var x = from.X + RoundDiv(dx * i, steps);
				var y = from.Y + RoundDiv(dy * i, steps);
				var d = groundDangerAt(new CPos(x, y));
				if (d > max)
					max = d;
			}

			return max;
		}

		/// <summary>When the straight route from <paramref name="from"/> to <paramref name="to"/> would
		/// cross ground-danger above <paramref name="safeThreshold"/>, returns a lateral waypoint that
		/// reduces the worst-case exposure of the two-leg route (from→wp→to); otherwise null (go direct).
		/// Candidates are perpendicular offsets of the midpoint at ±<paramref name="lateralCells"/> ×
		/// {1..<paramref name="maxSteps"/>}, evaluated in a fixed order. The SAFER side is chosen on
		/// strict merit, so against the Stage-B/C danger gradient the rear-lateral detour emerges by
		/// itself; a larger <paramref name="maxSteps"/> lets a high-value mover route deeper into safety.</summary>
		public static CPos? DetourWaypoint(CPos from, CPos to, int lateralCells, int maxSteps,
			int safeThreshold, Func<CPos, int> groundDangerAt)
		{
			var direct = PathMaxGroundDanger(from, to, groundDangerAt);
			if (direct <= safeThreshold)
				return null;

			var dx = to.X - from.X;
			var dy = to.Y - from.Y;
			var axisLen = Exts.ISqrt(dx * dx + dy * dy);
			if (axisLen <= 0)
				return null;

			if (maxSteps < 1)
				maxSteps = 1;

			var mx = from.X + dx / 2;
			var my = from.Y + dy / 2;

			var best = direct;
			CPos? bestWp = null;

			foreach (var side in DetourSides)
			{
				for (var step = 1; step <= maxSteps; step++)
				{
					var mag = side * lateralCells * step;

					// Perpendicular to the from→to axis is (-dy, dx); scale to `mag` cells via the axis length.
					var wp = new CPos(mx + RoundDiv(-dy * mag, axisLen), my + RoundDiv(dx * mag, axisLen));

					var worst = Math.Max(
						PathMaxGroundDanger(from, wp, groundDangerAt),
						PathMaxGroundDanger(wp, to, groundDangerAt));

					if (worst < best)
					{
						best = worst;
						bestWp = wp;
					}
				}
			}

			return bestWp;
		}
	}
}
