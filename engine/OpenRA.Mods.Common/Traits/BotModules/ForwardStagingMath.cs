#region Copyright & License Information
/*
 * WW3MOD influence stack — free-pool forward staging (@experimental) — reserve muster-point math (pure).
 *
 * PERCEIVED BEHAVIOUR: uncommitted combat units (the free pool) no longer idle at the Supply Route where they
 * mustered in, clogging the road to the front. They are walked to a FORWARD STAGING AREA — a point a safe
 * standoff BEHIND the believed friendly frontier — and the muster point ADVANCES as the front moves.
 *
 * Three coordinate-agnostic decisions over the coarse control grid, fed by caller-sampled fields:
 *   - StagingCell: steepest descent on the ControlField distance-to-enemy-frontier field, starting from the SR
 *     grid cell, toward the nearest front, stopping at a SAFE STANDOFF (frontier distance <= standoffCells, and
 *     never stepping into a believed anti-ground danger envelope). Reuses the EXISTING frontier-distance BFS —
 *     it invents no new field. The gradient IS the "behind the friendly frontier" read: the SR sits deep in the
 *     rear (large frontier distance) and each step toward a smaller distance is a step toward the front, so the
 *     walk halts a fixed number of coarse cells short of the line. A FLAT field (no believed enemy anywhere ⇒
 *     every cell reads the 'far' sentinel) yields no improving neighbour ⇒ the walk returns the SR unchanged,
 *     so staging is INERT until the field is populated (byte-identical to the legacy idle-at-SR path).
 *   - SpreadCell: a deterministic ring/octant offset by unit index, so the reserve fans out over several cells
 *     rather than piling on one — anti-clog. Fixed offset table, integer only, zero RNG.
 *   - AnchorShifted: Chebyshev hysteresis on the anchor, so a 1-cell field wobble does not re-lay the whole
 *     staging formation every eval (order-spam guard).
 *
 * WHY NOT FrontierStandoffMath.RearwardSteps? That walks a point AWAY from a KNOWN target until it clears the
 * front — it needs a single axis target to push back from (the echelon anchor pushes away from its axis's POI).
 * The free pool is a TARGET-LESS reserve, so there is no away-bearing to walk; the frontier-distance gradient is
 * the natural primitive for "get behind the nearest front at a standoff". Both consume the same BFS field.
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws, pure integer stepping over fixed scan/offset
 * orders with iteration-order tie-breaks. Two clients over the same synced belief state stage identically.
 *
 * v3-portable: engine-free static math (NUnit-pinned in ForwardStagingMathTest); only the plumbing that binds
 * the samplers (PoiOffensiveBotModule) is engine-specific.
 */
#endregion

using System;

namespace OpenRA.Mods.Common.Traits
{
	public static class ForwardStagingMath
	{
		// Fixed 8-neighbour scan order for the steepest-descent walk (cardinals first, then diagonals). Ties
		// (two neighbours equally closer to the front) break by this order, so the walk is deterministic.
		static readonly (int Dx, int Dy)[] Neighbours =
		{
			(0, -1), (1, 0), (0, 1), (-1, 0), (1, -1), (1, 1), (-1, 1), (-1, -1)
		};

		// Number of octant directions per spread ring (must equal SpreadDirs.Length below).
		public const int RingOctants = 8;

		// Fixed octant order for the reserve spread. Ring r puts up to 8 units at r*step along each octant, so
		// consecutive slots fan out over distinct cells (cardinals first for a tidy near-anchor cluster).
		static readonly (int Dx, int Dy)[] SpreadDirs =
		{
			(0, -1), (1, 0), (0, 1), (-1, 0), (1, -1), (1, 1), (-1, 1), (-1, -1)
		};

		/// <summary>The STABLE spread slot for a unit, derived from its OWN <paramref name="actorId"/> so that a
		/// pool-composition change (one unit dies/leaves) never re-slots any OTHER unit — eliminating the order
		/// churn a list-position slot would cause. Bounded to [0, <paramref name="maxRings"/>·<see cref="RingOctants"/>]
		/// so the resulting ring radius stays within the staging standoff: slots therefore cannot land forward of
		/// the frontier the anchor already stood off from (the danger the anchor descent excluded). The caller sizes
		/// <paramref name="maxRings"/> from the standoff (see PoiOffensiveBotModule.StageFreePool). Two ids congruent
		/// mod the slot count share a slot (a cell, not a crash) — accepted: stability beats perfect packing here.
		/// <paramref name="maxRings"/> &lt;= 0 ⇒ everyone on the anchor (slot 0). Pure integer, zero RNG.</summary>
		public static int StableSlot(uint actorId, int maxRings)
		{
			if (maxRings <= 0)
				return 0;

			var slotCount = maxRings * RingOctants + 1; // +1 for the anchor (slot 0).
			return (int)(actorId % (uint)slotCount);
		}

		/// <summary>Walk from the SR grid cell (<paramref name="startX"/>,<paramref name="startY"/>) DOWN the
		/// distance-to-enemy-frontier gradient toward the nearest front, and return the staging grid cell — a
		/// SAFE STANDOFF behind the line. Each step moves to the 8-neighbour with the STRICTLY smallest frontier
		/// distance, but NEVER into a cell whose <paramref name="dangerAt"/> exceeds
		/// <paramref name="dangerSafeThreshold"/> (stay out of believed weapon envelopes) and NEVER off
		/// <paramref name="onGrid"/>. Stops at the first cell whose frontier distance is at/under
		/// <paramref name="standoffCells"/>, or when no safe strictly-closer neighbour exists, or at the
		/// <paramref name="maxSteps"/> budget. Returns the start unchanged (⇒ no staging, byte-identical) when
		/// staging is disabled (standoffCells/maxSteps &lt;= 0), the start already clears the standoff, or the
		/// field is flat (unpopulated). Frontier distance STRICTLY decreases each accepted step, so the walk
		/// always terminates. Integer only, zero RNG.
		/// A negative <paramref name="dangerSafeThreshold"/> disables the danger guard (pure frontier descent).</summary>
		public static (int X, int Y) StagingCell(
			int startX, int startY,
			int standoffCells, int dangerSafeThreshold, int maxSteps,
			Func<int, int, int> frontierAt, Func<int, int, int> dangerAt, Func<int, int, bool> onGrid)
		{
			var cx = startX;
			var cy = startY;
			if (standoffCells <= 0 || maxSteps <= 0)
				return (cx, cy);
			if (frontierAt(cx, cy) <= standoffCells)
				return (cx, cy);

			for (var step = 0; step < maxSteps; step++)
			{
				var bestF = frontierAt(cx, cy);
				var bx = cx;
				var by = cy;

				for (var d = 0; d < Neighbours.Length; d++)
				{
					var nx = cx + Neighbours[d].Dx;
					var ny = cy + Neighbours[d].Dy;
					if (!onGrid(nx, ny))
						continue;

					// Never descend into a believed anti-ground danger envelope — that IS the "safe standoff":
					// as the walk nears the front, danger rises and closes off the forward neighbours, so the
					// unit holds BEHIND the defended line rather than on it.
					if (dangerSafeThreshold >= 0 && dangerAt(nx, ny) > dangerSafeThreshold)
						continue;

					var nf = frontierAt(nx, ny);
					if (nf < bestF)
					{
						bestF = nf;
						bx = nx;
						by = ny;
					}
				}

				if (bx == cx && by == cy)
					break; // no safe neighbour strictly closer to the front — hold here.

				cx = bx;
				cy = by;
				if (frontierAt(cx, cy) <= standoffCells)
					break; // reached the standoff behind the front.
			}

			return (cx, cy);
		}

		/// <summary>The spread cell for a staged unit's <paramref name="index"/> (its <see cref="StableSlot"/>)
		/// around the anchor (<paramref name="anchorX"/>,<paramref name="anchorY"/>) in MAP cells. Index 0 sits on
		/// the anchor; each subsequent index takes the next octant on the current ring, incrementing the ring every
		/// <see cref="RingOctants"/> slots, so slots fan out over concentric octagons at <paramref name="ringStep"/>-
		/// cell spacing. Falls back to the anchor when the computed cell is off <paramref name="onGrid"/> (or when
		/// ringStep &lt;= 0). Deterministic: a given index always maps to the same cell — and because the caller
		/// feeds a per-unit STABLE slot (not a list position), a unit keeps its cell across evals (no churn).
		/// NOTE: the ring is NOT danger-guarded per cell; correctness relies on the caller bounding the max ring
		/// radius below the staging standoff (via StableSlot's maxRings) so slots stay behind the frontier the
		/// anchor descent already cleared of danger.</summary>
		public static (int X, int Y) SpreadCell(int anchorX, int anchorY, int index, int ringStep, Func<int, int, bool> onGrid)
		{
			if (index <= 0 || ringStep <= 0)
				return (anchorX, anchorY);

			var ring = (index - 1) / SpreadDirs.Length + 1;
			var dir = (index - 1) % SpreadDirs.Length;
			var cx = anchorX + SpreadDirs[dir].Dx * ring * ringStep;
			var cy = anchorY + SpreadDirs[dir].Dy * ring * ringStep;
			if (onGrid != null && !onGrid(cx, cy))
				return (anchorX, anchorY);

			return (cx, cy);
		}

		/// <summary>The largest ring index a spread may use, given a standoff of <paramref name="standoffMapCells"/>
		/// map cells and <paramref name="ringStep"/>-cell ring spacing. This is the bound <see cref="SpreadCell"/>
		/// documents as its correctness precondition: the widest ring's Chebyshev radius (maxRings * ringStep)
		/// must stay STRICTLY inside the standoff, so no spread slot sits forward of the frontier the anchor
		/// descent already cleared of believed danger. The -1 is what makes it strict rather than touching.
		/// A non-positive ringStep means "no fan-out" and yields 0. Pure integer, zero RNG.
		///
		/// <para>NOTE PoiOffensiveBotModule.StageFreePool still computes this inline and differs for a
		/// non-positive ringStep (it clamps the divisor to 1 instead, giving a large but inert maxRings — inert
		/// because SpreadCell returns the anchor at ringStep &lt;= 0 regardless). Deliberately NOT unified here:
		/// that call site is a shipped @experimental/@stable lever, so folding it in is a behaviour question
		/// needing its own measurement rather than a correction-round change.</para></summary>
		public static int MaxSpreadRings(int standoffMapCells, int ringStep)
		{
			if (ringStep <= 0)
				return 0;

			return Math.Max(0, (standoffMapCells - 1) / ringStep);
		}

		/// <summary>Has the staging anchor moved far enough (Chebyshev &gt;= <paramref name="thresholdCells"/>) to
		/// be re-ADOPTED? Hysteresis so a small field wobble doesn't re-lay the whole formation every eval. A
		/// non-positive threshold always re-adopts (no hysteresis). Pure integer, zero RNG.</summary>
		public static bool AnchorShifted(int prevX, int prevY, int curX, int curY, int thresholdCells)
		{
			if (thresholdCells <= 0)
				return true;

			var dx = Math.Abs(curX - prevX);
			var dy = Math.Abs(curY - prevY);
			return Math.Max(dx, dy) >= thresholdCells;
		}
	}
}
