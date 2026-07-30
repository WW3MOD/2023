#region Copyright & License Information
/*
 * WW3MOD supply-truck logistics (@experimental) — sector assignment + evac geometry (pure math).
 *
 * PERCEIVED BEHAVIOUR: supply trucks no longer all pile onto the single biggest blob. When several trucks
 * are free they SPREAD — each claims a DISTINCT needy cluster (neediest first), so small squads in other
 * sectors get served too; a truck only doubles up on an already-served cluster once trucks outnumber
 * clusters. And a truck whose follow position reads high believed ground danger PULLS BACK toward its
 * Supply Route instead of idling in the fire.
 *
 * This carries the three decisions SupplyFollowerBotModule turns into Move orders when the @experimental
 * keys are on:
 *   (1) SECTOR SPREAD — AssignSectors: greedy distinct-cluster assignment over a caller-sorted truck list.
 *   (2) DANGER EVAC decision — ShouldEvacuate: the higher of the truck's / cluster's believed danger vs a
 *       threshold (the danger reads themselves are supplied by the caller and are fog-legal).
 *   (3) EVAC GEOMETRY — RetreatTarget: a pull-back point stepped toward the SR, clamped to never overshoot.
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws. AssignSectors iterates trucks in the given
 * (caller-sorted, stable) order and sectors in index order, choosing on strict merit (unserved over served,
 * then Need desc, distance asc, sector-index asc) so two clients over the same synced state pick the same
 * assignment. The geometry is integer WPos/WVec math with a long intermediate so the scale never overflows.
 *
 * v3-portable: engine-free static math (NUnit-pinned in SupplyLogisticsMathTest); only the tasking plumbing
 * that consumes it (SupplyFollowerBotModule.BotTick) is engine-specific.
 */
#endregion

using System.Collections.Generic;

namespace OpenRA.Mods.Common.Traits
{
	public static class SupplyLogisticsMath
	{
		/// <summary>Assignment sentinel: a truck with no eligible sector this scan.</summary>
		public const int NoSector = -1;

		/// <summary>A candidate resupply cluster for the spread: its centroid and a non-negative "need" score
		/// (higher = needier — the caller scales the ammo-need sum to a stable integer). The array index is the
		/// deterministic tie-break of last resort.</summary>
		public readonly struct Sector
		{
			public readonly WPos Center;
			public readonly int Need;

			public Sector(WPos center, int need)
			{
				Center = center;
				Need = need;
			}
		}

		/// <summary>Greedy DISTINCT-sector assignment. Trucks — in the given, caller-sorted order — each claim
		/// the neediest ELIGIBLE sector no earlier truck has claimed; only when every in-range sector is already
		/// claimed does a truck double up on the best in-range one. Eligibility = within
		/// <paramref name="maxFollowLength"/> of the truck. Selection order: unserved before served (the dedup),
		/// then Need desc, then distance asc, then sector index asc — fully deterministic, no random draws.
		/// Returns assignment[t] = sector index or <see cref="NoSector"/>.</summary>
		public static int[] AssignSectors(IReadOnlyList<WPos> truckPositions, IReadOnlyList<Sector> sectors, int maxFollowLength)
		{
			var count = truckPositions.Count;
			var assignment = new int[count];
			var served = new bool[sectors.Count];
			var maxSq = (long)maxFollowLength * maxFollowLength;

			for (var t = 0; t < count; t++)
			{
				var pos = truckPositions[t];

				var pick = NoSector;
				var pickNeed = 0;
				var pickDistSq = 0L;
				var pickServed = true; // seed "served" so the first unserved candidate always wins over it

				for (var s = 0; s < sectors.Count; s++)
				{
					var distSq = (sectors[s].Center - pos).LengthSquared;
					if (distSq > maxSq)
						continue;

					var need = sectors[s].Need;
					var isServed = served[s];

					// Unserved always beats served (that is the dedup). Within the same served-state, order by
					// Need desc, distance asc, index asc (index asc falls out of only replacing on a STRICT win).
					var better =
						pick == NoSector
						|| (!isServed && pickServed)
						|| (isServed == pickServed && (need > pickNeed || (need == pickNeed && distSq < pickDistSq)));

					if (better)
					{
						pick = s;
						pickNeed = need;
						pickDistSq = distSq;
						pickServed = isServed;
					}
				}

				assignment[t] = pick;
				if (pick != NoSector)
					served[pick] = true;
			}

			return assignment;
		}

		/// <summary>True when a truck should abandon its follow position and pull back: the higher of the
		/// believed ground danger at the truck itself (<paramref name="dangerAtTruck"/>) and at its target
		/// cluster centroid (<paramref name="dangerAtCluster"/>) reaches <paramref name="threshold"/>. Pure —
		/// the caller supplies fog-legal danger reads (DangerFieldLayer only).</summary>
		public static bool ShouldEvacuate(int dangerAtTruck, int dangerAtCluster, int threshold)
		{
			return dangerAtTruck >= threshold || dangerAtCluster >= threshold;
		}

		/// <summary>A pull-back point <paramref name="retreatLength"/> toward <paramref name="towards"/> (the
		/// Supply Route / safe rear) from <paramref name="from"/>. Clamped so it never overshoots the
		/// destination. Pure integer vector math with a long intermediate so the scale never overflows on large
		/// maps; the truck's own Z is preserved. Deterministic.</summary>
		public static WPos RetreatTarget(WPos from, WPos towards, int retreatLength)
		{
			var delta = towards - from;
			var dist = delta.HorizontalLength;
			if (dist <= 0 || retreatLength >= dist)
				return towards;

			var x = from.X + (int)((long)delta.X * retreatLength / dist);
			var y = from.Y + (int)((long)delta.Y * retreatLength / dist);
			return new WPos(x, y, from.Z);
		}
	}
}
