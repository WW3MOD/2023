#region Copyright & License Information
/*
 * WW3MOD influence stack — Stage D: helicopter danger-field consumer (pure navigation math).
 *
 * The three perceived behaviours the Stage-D consumer must deliver — routed AROUND known
 * anti-air, LEASHED to the AA-safe envelope while attacking, and WITHDRAWN the moment a new
 * AA threat lights up — all reduce to a handful of decisions over the Stage-B anti-air danger
 * field. This class holds those decisions as PURE FUNCTIONS over an air-danger sampler
 * (Func<CPos,int>), mirroring how DangerKernelMath / ControlFieldMath were split from their
 * traits so the logic is NUnit-testable without mounting a world.
 *
 * The caller (HelicopterStates) supplies the sampler bound to the squad owner's own per-player
 * air channel — fog-legal by construction (the field is stamped from that player's belief
 * store) and reading 0 outside every believed envelope (the air channel carries NO territory
 * baseline, Stage-B guarantee). Off-map / invalid cells are passed as `Impassable` so they are
 * never chosen as a safe leash or retreat cell.
 *
 * DETERMINISM (influence-stack invariant, decision file 10): ZERO random draws. Every search is
 * an integer-only walk over a fixed candidate order, ties broken by iteration order, so two
 * clients computing the same field pick the same cell.
 */
#endregion

using System;

namespace OpenRA.Mods.Common.Traits.BotModules.Squads
{
	public static class HeliDangerNav
	{
		// Callers pass this for off-map / otherwise-invalid cells: treated as maximally unsafe so a
		// leash / detour / retreat search never lands the squad off the playable area.
		public const int Impassable = int.MaxValue;

		// Deterministic candidate order for detour lateral offsets: side +1 before -1 (stable tie-break),
		// near magnitude before far. Static to avoid per-call allocation.
		static readonly int[] DetourSides = { 1, -1 };

		// Integer division rounded to nearest, sign-correct (den assumed > 0). Used to interpolate cells
		// along a line without floating point, so the sampled path is identical on every client.
		static int RoundDiv(int num, int den)
		{
			return num >= 0 ? (num + den / 2) / den : -((-num + den / 2) / den);
		}

		/// <summary>Highest air-danger reading sampled along the integer cell line from <paramref name="from"/>
		/// to <paramref name="to"/> (both inclusive). The "how exposed is this flight path" measure.</summary>
		public static int PathMaxAirDanger(CPos from, CPos to, Func<CPos, int> airDangerAt)
		{
			var dx = to.X - from.X;
			var dy = to.Y - from.Y;
			var steps = Math.Max(Math.Abs(dx), Math.Abs(dy));

			var max = airDangerAt(from);
			for (var i = 1; i <= steps; i++)
			{
				var x = from.X + RoundDiv(dx * i, steps);
				var y = from.Y + RoundDiv(dy * i, steps);
				var d = airDangerAt(new CPos(x, y));
				if (d > max)
					max = d;
			}

			return max;
		}

		/// <summary>The cell nearest <paramref name="target"/> (by Chebyshev radius) whose air-danger is
		/// at or below <paramref name="safeThreshold"/> — i.e. the AA-safe edge to fire from instead of
		/// diving onto a target buried in anti-air. Returns <paramref name="target"/> unchanged when it is
		/// already safe, and falls back to <paramref name="target"/> when no safe cell exists within
		/// <paramref name="leashCells"/> (the withdraw-on-spike / hot-target guards handle that case).</summary>
		public static CPos LeashedEngageCell(CPos target, int leashCells, int safeThreshold, Func<CPos, int> airDangerAt)
		{
			if (airDangerAt(target) <= safeThreshold)
				return target;

			// Expanding rings: the first satisfying cell at the smallest radius is the closest safe cell
			// to the target. Fixed dy-then-dx scan order makes same-radius ties deterministic.
			for (var r = 1; r <= leashCells; r++)
			{
				for (var dy = -r; dy <= r; dy++)
				{
					for (var dx = -r; dx <= r; dx++)
					{
						if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
							continue;

						var cell = new CPos(target.X + dx, target.Y + dy);
						if (airDangerAt(cell) <= safeThreshold)
							return cell;
					}
				}
			}

			return target;
		}

		/// <summary>When the straight flight from <paramref name="from"/> to <paramref name="to"/> would
		/// cross air-danger above <paramref name="safeThreshold"/>, returns a lateral waypoint that
		/// reduces the worst-case exposure of the two-leg route (from→wp→to); otherwise null (fly direct).
		/// Candidates are perpendicular offsets of the midpoint at ±<paramref name="lateralCells"/> and
		/// ±2×, evaluated in a fixed order — the "route around the SAM, don't fly through it" step.</summary>
		public static CPos? DetourWaypoint(CPos from, CPos to, int lateralCells, int safeThreshold, Func<CPos, int> airDangerAt)
		{
			var direct = PathMaxAirDanger(from, to, airDangerAt);
			if (direct <= safeThreshold)
				return null;

			var dx = to.X - from.X;
			var dy = to.Y - from.Y;
			var axisLen = Exts.ISqrt(dx * dx + dy * dy);
			if (axisLen <= 0)
				return null;

			var mx = from.X + dx / 2;
			var my = from.Y + dy / 2;

			var best = direct;
			CPos? bestWp = null;

			foreach (var side in DetourSides)
			{
				for (var step = 1; step <= 2; step++)
				{
					var mag = side * lateralCells * step;

					// Perpendicular to the from→to axis is (-dy, dx); scale to `mag` cells via the axis length.
					var wp = new CPos(mx + RoundDiv(-dy * mag, axisLen), my + RoundDiv(dx * mag, axisLen));

					var worst = Math.Max(
						PathMaxAirDanger(from, wp, airDangerAt),
						PathMaxAirDanger(wp, to, airDangerAt));

					if (worst < best)
					{
						best = worst;
						bestWp = wp;
					}
				}
			}

			return bestWp;
		}

		/// <summary>The safest cell (lowest air-danger) on the Chebyshev ring at radius
		/// <paramref name="ringCells"/> around <paramref name="origin"/> — a deterministic air-aware
		/// retreat target that pulls the squad outward toward the least-covered heading. Fixed scan
		/// order breaks ties.</summary>
		public static CPos SafestAirCellOnRing(CPos origin, int ringCells, Func<CPos, int> airDangerAt)
		{
			if (ringCells < 1)
				return origin;

			var best = Impassable;
			var bestCell = origin;
			for (var dy = -ringCells; dy <= ringCells; dy++)
			{
				for (var dx = -ringCells; dx <= ringCells; dx++)
				{
					if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ringCells)
						continue;

					var cell = new CPos(origin.X + dx, origin.Y + dy);
					var d = airDangerAt(cell);
					if (d < best)
					{
						best = d;
						bestCell = cell;
					}
				}
			}

			return bestCell;
		}
	}
}
