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
		/// A negative <paramref name="dangerSafeThreshold"/> disables the danger guard (pure frontier descent).
		///
		/// <para>THE DESCENT MUST NOT BE ABLE TO RETURN A CELL ITS CALLER IS OBLIGED TO REJECT — that is what
		/// <paramref name="passable"/> is for, and leaving it null re-opens a real outage. Every caller already
		/// re-tests the returned cell against its mover's locomotor and refuses an unreachable one, because
		/// PathFinder bails to NoPath on an inaccessible target and Move treats an empty path as arrival. But
		/// this walk is DETERMINISTIC over a field that changes slowly, so a rejected result is not a one-scan
		/// miss: it re-derives the identical unreachable cell every scan, and the caller rejects it every scan,
		/// for as long as the field holds still. In the user's 2026-08-09 play log the west player's drop
		/// anchor descent returned cell 33,31 for 24 CONSECUTIVE scans (~2.4 minutes) and drop-and-leave was
		/// dark for the whole outage — the supply truck oscillation sits entirely inside that window, at both
		/// edges. Terrain is also actively ATTRACTIVE to a danger-guarded walk, which is what makes this more
		/// than a rare accident: unstamped water and cliff read danger 0, i.e. maximally safe, so the guard
		/// above steers TOWARD them. Filtering neighbours here — with the same predicate over the same
		/// representative cell the caller will test — is what converts a permanent stall into a detour.</para></summary>
		public static (int X, int Y) StagingCell(
			int startX, int startY,
			int standoffCells, int dangerSafeThreshold, int maxSteps,
			Func<int, int, int> frontierAt, Func<int, int, int> dangerAt, Func<int, int, bool> onGrid,
			Func<int, int, bool> passable = null)
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

					// Never step onto ground the mover cannot stand on. Checked BEFORE the improvement test so
					// an unreachable cell cannot win the step and end the walk on itself — see the note on this
					// method. The start cell is where the mover already is, so the walk only ever stands on
					// cells this predicate admits, and therefore cannot terminate on one it does not.
					if (passable != null && !passable(nx, ny))
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

		/// <summary>
		/// <para>Resolve one eval's forward staging anchor as a MAP cell, or report that the descent never left the
		/// Supply Route's grid cell.</para>
		///
		/// <para>This owns the whole coordinate handoff — map in, grid descent, map out — because splitting it across
		/// the caller is exactly what produced the bug it exists to prevent. The caller supplies the samplers and
		/// keeps its hysteresis; every decision about WHEN there is no anchor lives here, under NUnit.</para>
		///
		/// <para>Returns false when the descent stalled at its seed: a flat/unpopulated field (no believed enemy
		/// anywhere) or a front already inside the standoff. The caller must then publish NO anchor, so the
		/// reserve idles at the SR exactly as the legacy path did.</para>
		/// </summary>
		public static bool TryResolveAnchorCell(
			int cellSize, int srMapX, int srMapY,
			int standoffCells, int dangerSafeThreshold, int maxSteps,
			Func<int, int, int> frontierAt, Func<int, int, int> dangerAt, Func<int, int, bool> onGrid,
			out int anchorMapX, out int anchorMapY)
		{
			var sgx = InfluenceGridMath.MapToGrid(cellSize, srMapX);
			var sgy = InfluenceGridMath.MapToGrid(cellSize, srMapY);

			var (agx, agy) = StagingCell(sgx, sgy, standoffCells, dangerSafeThreshold, maxSteps,
				frontierAt, dangerAt, onGrid);

			anchorMapX = 0;
			anchorMapY = 0;

			// COMPARED IN GRID SPACE — the space the descent actually ran in, and the whole point of this
			// method. The obvious-looking alternative (convert to map cells, compare against the SR) is the
			// shipped bug: GridToMapCentre returns the block CENTRE, so a zero-step descent compares unequal
			// to its own seed unless the SR sits on that centre — at CellSize 2, one placement in four.
			if (agx == sgx && agy == sgy)
				return false;

			anchorMapX = InfluenceGridMath.GridToMapCentre(cellSize, agx);
			anchorMapY = InfluenceGridMath.GridToMapCentre(cellSize, agy);
			return true;
		}

		/// <summary>A DELIBERATE muster cell for the case <see cref="TryResolveAnchorCell"/> reports no gradient:
		/// <paramref name="maxCells"/> map cells from the Supply Route along the bearing toward
		/// (<paramref name="towardX"/>,<paramref name="towardY"/>), the first such cell at or inside that distance
		/// that <paramref name="passable"/> accepts. Returns false when the fallback is off
		/// (<paramref name="maxCells"/> &lt;= 0), when the bearing is degenerate (the SR IS the target point), or
		/// when no cell on the bearing is passable — in each case the caller publishes nothing and the reserve
		/// idles at the SR exactly as it does today, so OFF and UNRESOLVABLE both collapse to current behaviour.
		///
		/// <para>WHY A BEARING AND NOT A ROUNDING. The anchor this replaces was the SR re-projected through a
		/// lossy grid round trip — a cell that meant nothing, and whose only virtue was being somewhere other
		/// than the beachhead. Measured 2026-08-17, that accidental virtue was doing real work: with it the
		/// reserve's within-2-cells count was 0 and within-4 was 1; without it, 2 and 3, and every arriving
		/// reinforcement stayed put. So the dispersal has to be kept and the meaninglessness dropped. The map
		/// CENTRE is the defensible bearing: every SR sits near a map edge by construction (it spawns at the
		/// player's spawn point, and spawns are an edge phenomenon — DOCS/reference/supply-route.md), so
		/// "toward the middle" is the direction the front will form on every map, is well-defined without any
		/// belief, and is stable for the whole match.</para>
		///
		/// <para>Chebyshev-exact: the dominant axis moves by exactly d, so the returned cell is d cells from the
		/// SR in the same metric the censuses and the standoff arithmetic use. Walks d DOWNWARD from
		/// <paramref name="maxCells"/> so the farthest legal cell wins and a blocked bearing degrades toward the
		/// SR one cell at a time rather than giving up — the "sensible default puts a unit in the sea" failure
		/// is what <paramref name="passable"/> exists to stop, and it must be a real terrain test, not a bounds
		/// test. Pure integer, zero RNG, no allocation.</para></summary>
		public static bool TryResolveFallbackCell(
			int srX, int srY, int towardX, int towardY, int maxCells,
			Func<int, int, bool> passable,
			out int cellX, out int cellY)
		{
			cellX = srX;
			cellY = srY;

			if (maxCells <= 0)
				return false;

			var dx = towardX - srX;
			var dy = towardY - srY;
			var span = Math.Max(Math.Abs(dx), Math.Abs(dy));
			if (span == 0)
				return false;

			for (var d = maxCells; d >= 1; d--)
			{
				var cx = srX + dx * d / span;
				var cy = srY + dy * d / span;
				if (cx == srX && cy == srY)
					continue;

				if (passable != null && !passable(cx, cy))
					continue;

				cellX = cx;
				cellY = cy;
				return true;
			}

			return false;
		}

		/// <summary>The spread cell for a staged unit's <paramref name="index"/> (its <see cref="StableSlot"/>)
		/// around the anchor (<paramref name="anchorX"/>,<paramref name="anchorY"/>) in MAP cells. Index 0 sits on
		/// the anchor; each subsequent index takes the next octant on the current ring, incrementing the ring every
		/// <see cref="RingOctants"/> slots, so slots fan out over concentric octagons at <paramref name="ringStep"/>-
		/// cell spacing. Deterministic: a given index always maps to the same cell — and because the caller feeds a
		/// per-unit STABLE slot (not a list position), a unit keeps its cell across evals (no churn).
		///
		/// <para>A slot is LEGAL only when it is on the map (<paramref name="inBounds"/>) AND the mover can stand on
		/// it (<paramref name="passable"/>). A slot failing either collapses onto the anchor and raises
		/// <paramref name="collapsed"/>.</para>
		///
		/// <para>WHY <paramref name="passable"/> IS NOT OPTIONAL. Until 2026-08-17 the guard was assembled at the
		/// CALL SITE, and both call sites assembled it from bounds alone unless the anchor had come from the
		/// fallback path — so on the gradient path a ring slot could be on-map WATER or CLIFF and the unit was
		/// ordered into it. Measured on the shipped fallback geometry (2 rings at step 2) with the anchor 2 cells
		/// inland of a coast: 6 of 17 slots were in the sea. It survived thirteen days because the only
		/// instrumentation watching this counts DISTANCE FROM THE SUPPLY ROUTE, and a unit walking into the water
		/// improves that number. A null here is therefore a contract violation, not a default: the two call sites
		/// carried the same wrong assembly, which is the shape that produced the phantom-anchor class (three copies
		/// of one grid descent, two of them wrong), and a guard that is optional is a guard that will be omitted.
		/// <paramref name="inBounds"/> stays nullable because "no grid" is a meaningful caller state; "this mover
		/// can stand anywhere" is not — that is what an all-true predicate says explicitly.</para>
		///
		/// <para>A rejected slot collapses onto the ANCHOR rather than searching outward for the nearest standable
		/// cell. That is the answer <see cref="TryResolveFallbackCell"/> gives one level down, and the anchor is the
		/// one cell every caller has already proved it wants units at. The cost is real and accepted knowingly: on
		/// a coastal anchor several units share a cell, which is the clog the fan-out exists to prevent.
		/// <c>FiresStandoffMath.NearestPassableCell</c> is the better answer and would keep the dispersal, but it
		/// needs a clamp radius — a new behavioural lever — and a measured run to set it.</para>
		///
		/// <para>NOTE: the ring is NOT danger-guarded per cell; correctness relies on the caller bounding the max
		/// ring radius below the staging standoff (via StableSlot's maxRings) so slots stay behind the frontier the
		/// anchor descent already cleared of danger. Pure integer, zero RNG, no allocation.</para></summary>
		public static (int X, int Y) SpreadSlot(int anchorX, int anchorY, int index, int ringStep,
			Func<int, int, bool> inBounds, Func<int, int, bool> passable, out bool collapsed)
		{
			if (passable == null)
				throw new ArgumentNullException(nameof(passable), "a spread slot must be terrain-tested for the mover it is ordering");

			collapsed = false;
			if (index <= 0 || ringStep <= 0)
				return (anchorX, anchorY);

			var ring = (index - 1) / SpreadDirs.Length + 1;
			var dir = (index - 1) % SpreadDirs.Length;
			var cx = anchorX + SpreadDirs[dir].Dx * ring * ringStep;
			var cy = anchorY + SpreadDirs[dir].Dy * ring * ringStep;

			if ((inBounds != null && !inBounds(cx, cy)) || !passable(cx, cy))
			{
				collapsed = true;
				return (anchorX, anchorY);
			}

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
