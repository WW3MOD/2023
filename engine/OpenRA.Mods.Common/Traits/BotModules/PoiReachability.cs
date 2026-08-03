#region Copyright & License Information
/*
 * WW3MOD frontline-influence Phase 1 — reachability-gated + amphibious-typed POI scoring math.
 *
 * PoiMap scores a POI's distance as EUCLIDEAN crow-flies from the Supply Route (PoiMap.cs), with no
 * pathfinding and no water/bridge awareness — so a far-bank objective behind an uncrossable river
 * scores as if adjacent, and a land-only army is repeatedly sent at targets it physically cannot reach
 * (the two central bridges always dominate; flank POIs reachable only by amphibious units or by an
 * engineer repairing a destroyed bridge never win an axis).
 *
 * This pure math turns the Phase-0 CrossingMap reachability classification into a score multiplier and
 * an axis-typing decision:
 *   - a POI in the SAME ground component as the SR, or reachable via an INTACT crossing, is unpenalised;
 *   - a POI reachable by the AMPHIBIOUS locomotor keeps FULL value WHEN the bot has amphibious units to
 *     send (and the axis is then typed amphibious so those units — not stranded land units — go);
 *   - a POI reachable only via a REPAIRABLE (destroyed) bridge is reduced but NOT eliminated, so it stays
 *     on the radar for the Phase-6 engineer route-opening wiring;
 *   - a genuinely unreachable POI is heavily damped.
 *
 * Engine-free ⇒ NUnit-pinned (PoiReachabilityTest), zero RNG, ports verbatim to a future v3 brain.
 * ALL of this is inert unless the caller opts in (default-off flag) — with the gate off the factor is a
 * constant 100 and axis typing never fires, so scoring is byte-identical to current main.
 */
#endregion

using System;

namespace OpenRA.Mods.Common.Traits
{
	public static class PoiReachabilityMath
	{
		/// <summary>Score multiplier (x100) for a POI given how a GROUND force reaches it from the SR and
		/// whether the bot can instead send amphibious units. Ground-reachable ⇒ 100 (inert). Amphibious
		/// rescue (route exists AND we own amphibious units) ⇒ 100 (full value; the axis is typed amphibious).
		/// Repairable-only ⇒ repairablePct (reduced, kept on the radar for the engineer). Amphibious route we
		/// can't crew, or unreachable ⇒ the heavier damps. Callers set repairablePct ≥ amphibiousPct ≥
		/// unreachablePct, all &lt; 100. Pure ⇒ unit-tested, v3-portable.</summary>
		public static int ReachabilityFactor(GroundReach reach, bool amphibiousReachable, bool hasAmphibiousPool,
			int repairablePct, int amphibiousPct, int unreachablePct)
		{
			// Ground can walk there — no change.
			if (reach == GroundReach.Same || reach == GroundReach.IntactCrossing)
				return 100;

			// Ground blocked, but we own amphibious units and a water route exists ⇒ full value; the axis
			// will be crewed by amphibious units (ShouldTypeAmphibious below fires on the same condition).
			if (amphibiousReachable && hasAmphibiousPool)
				return 100;

			// Ground blocked, no amphibious rescue available.
			if (reach == GroundReach.RepairableCrossing)
				return Clamp(repairablePct);
			if (reach == GroundReach.AmphibiousOnly)
				return Clamp(amphibiousPct);

			return Clamp(unreachablePct);
		}

		/// <summary>Should this POI's axis be crewed by AMPHIBIOUS units? True exactly when the ground route
		/// is blocked (not Same / IntactCrossing) yet the amphibious locomotor connects the banks AND the bot
		/// owns amphibious units. Mirrors the "amphibious rescue" branch of ReachabilityFactor so the score
		/// boost and the unit typing agree. Pure.</summary>
		public static bool ShouldTypeAmphibious(GroundReach reach, bool amphibiousReachable, bool hasAmphibiousPool)
			=> reach != GroundReach.Same && reach != GroundReach.IntactCrossing
				&& amphibiousReachable && hasAmphibiousPool;

		/// <summary>Approximate through-crossing ground distance (cells): the leg from the SR to the crossing
		/// plus the leg from the crossing to the POI. Reflects that a far-bank target is reached by routing
		/// THROUGH a bridge, not along the crow-flies line the raw PoiMap distance uses. Clamped ≥ the direct
		/// distance so a crossing never reads CLOSER than straight-line. Pure.</summary>
		public static int ThroughCrossingDistanceCells(int directCells, int srToCrossingCells, int crossingToPoiCells)
		{
			var through = Math.Max(0, srToCrossingCells) + Math.Max(0, crossingToPoiCells);
			return Math.Max(Math.Max(0, directCells), through);
		}

		static int Clamp(int pct) => pct < 0 ? 0 : pct > 100 ? 100 : pct;
	}
}
