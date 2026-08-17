#region Copyright & License Information
/*
 * WW3MOD supply-truck hunt (@experimental) — idle-truck target selection (pure math).
 *
 * PERCEIVED BEHAVIOUR: a supply truck with no squad to follow no longer parks. It drives to the neediest
 * STARVING soldier inside its leash and lets its own push aura refill him. Tier 1 gave the infantry the
 * legs (AutoSeekSupplies walks a dry soldier to a truck); this is the other half of that meeting — the
 * truck now closes the distance too, instead of waiting for units to wander into aura range.
 *
 * This carries the three decisions SupplyFollowerBotModule turns into Move orders when IdleTruckHunt is on:
 *   (1) WHO IS STARVING — IsStarving / ShortfallPerMille: the demand reading, deferring to Tier 1's own
 *       threshold rule (SupplyHuntMath.BelowSeekThreshold) so both halves agree on the word "starving".
 *   (2) WHICH ONE — SelectDemand: leash-bounded (SupplyHuntMath.WithinLeash — Tier 1's leash, not a
 *       parallel one), then need-band desc, distance asc, ActorID asc.
 *   (3) WHETHER TO MOVE AT ALL — NeedsApproach: a truck already covering its pick issues no order.
 *
 * BOUNDED BY CONSTRUCTION: SelectDemand returns NoDemand when nothing starving sits inside the leash, and
 * the caller then issues no order — so a truck with no in-leash demand stays put. There is no cross-map
 * fallback here (contrast SupplyProvider.FindNeedsResupplyTarget, whose Hunt-stance branch scans the whole
 * map). Note the leash is anchored on the TRUCK and re-read each scan, so it bounds each HOP, not the total
 * journey: unlike Tier 1's infantry hunt there is no return leg, because a truck holds no post — being where
 * the demand is IS its post. What keeps that from becoming a wander is the candidate set: only the player's
 * OWN starving soldiers, so the truck can only ever converge on its own line.
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws, integer math only. SelectDemand walks the
 * candidate list in index order and replaces only on a STRICT win over a total order (band, then distance,
 * then ActorID), so two clients over the same synced state pick the same soldier regardless of enumeration
 * order. Shortfall is cross-multiplied rather than divided down, so a 3-round pool and a 900-round pool
 * band the same way instead of falling off an integer-truncation cliff.
 *
 * v3-portable: engine-free static math (NUnit-pinned in SupplyTruckHuntMathTest); only the tasking plumbing
 * that consumes it (SupplyFollowerBotModule.HuntStarvingInfantry) is engine-specific.
 */
#endregion

using System.Collections.Generic;

namespace OpenRA.Mods.Common.Traits
{
	public static class SupplyTruckHuntMath
	{
		/// <summary>Selection sentinel: no starving soldier inside the leash this scan.</summary>
		public const int NoDemand = -1;

		/// <summary>One starving soldier under consideration, reduced to the three facts the pick depends on.
		/// <see cref="ShortfallPerMille"/> is how EMPTY his worst servable pool is (higher = needier);
		/// ActorID is the deterministic tie-break of last resort.</summary>
		public readonly struct Demand
		{
			public readonly long DistanceSquared;
			public readonly int ShortfallPerMille;
			public readonly uint ActorId;

			public Demand(long distanceSquared, int shortfallPerMille, uint actorId)
			{
				DistanceSquared = distanceSquared;
				ShortfallPerMille = shortfallPerMille;
				ActorId = actorId;
			}
		}

		/// <summary>
		/// Whether a pool is empty enough for its owner to count as starving. Deliberately delegates to
		/// Tier 1's own seek rule rather than restating it: the truck must consider a soldier starving on
		/// exactly the reading that would make that soldier walk to a truck, or the two halves of the
		/// meeting disagree about who needs help.
		/// </summary>
		public static bool IsStarving(int currentAmmo, int maxAmmo, int thresholdPerMille)
		{
			return SupplyHuntMath.BelowSeekThreshold(currentAmmo, maxAmmo, thresholdPerMille);
		}

		/// <summary>
		/// How empty a pool is, in parts per thousand of capacity (0 = full, 1000 = dry). Cross-multiplied
		/// so small pools read on the same scale as large ones. Clamped: an over-full pool reads 0.
		/// </summary>
		public static int ShortfallPerMille(int currentAmmo, int maxAmmo)
		{
			if (maxAmmo <= 0)
				return 0;

			var missing = (long)maxAmmo - currentAmmo;
			if (missing <= 0)
				return 0;

			if (missing >= maxAmmo)
				return 1000;

			return (int)(missing * 1000 / maxAmmo);
		}

		/// <summary>
		/// Quantizes a shortfall into a band so near-equal need TIES and distance decides. Without it the
		/// pick is decided by single parts per thousand: two soldiers 1‰ apart would let the further one
		/// win, and because the sweep re-decides every scan the truck would re-target across the sector each
		/// time an ammo pip landed. A band of 0 or 1 disables the quantization (raw shortfall order).
		/// </summary>
		public static int NeedBand(int shortfallPerMille, int bandPerMille)
		{
			if (bandPerMille <= 1)
				return shortfallPerMille;

			return shortfallPerMille / bandPerMille;
		}

		/// <summary>
		/// Index of the starving soldier this truck should drive to, or <see cref="NoDemand"/> when none is
		/// inside the leash. Order: need band desc (relieve the emptiest first — the same neediest-first
		/// intent the cluster path already applies), then distance asc, then ActorID asc. Replacement only
		/// on a strict win, so the result never depends on enumeration order.
		/// </summary>
		public static int SelectDemand(IReadOnlyList<Demand> demands, int leashCells, int bandPerMille)
		{
			var best = NoDemand;
			var bestBand = 0;
			var bestDistanceSquared = 0L;
			var bestActorId = 0u;

			for (var i = 0; i < demands.Count; i++)
			{
				var d = demands[i];
				if (!SupplyHuntMath.WithinLeash(d.DistanceSquared, leashCells))
					continue;

				var band = NeedBand(d.ShortfallPerMille, bandPerMille);

				var better = best == NoDemand
					|| band > bestBand
					|| (band == bestBand && (d.DistanceSquared < bestDistanceSquared
						|| (d.DistanceSquared == bestDistanceSquared && d.ActorId < bestActorId)));

				if (!better)
					continue;

				best = i;
				bestBand = band;
				bestDistanceSquared = d.DistanceSquared;
				bestActorId = d.ActorId;
			}

			return best;
		}

		/// <summary>
		/// Whether the truck has to reposition to serve its pick, or already covers it. A truck standing in
		/// its own aura must issue NO order — the push is already reaching the soldier, and re-ordering it
		/// onto his cell every scan would nudge a serving truck off station for nothing. Inclusive at the
		/// boundary, matching SupplyProvider.InAuraRange.
		/// </summary>
		public static bool NeedsApproach(long distanceSquared, long auraLengthSquared)
		{
			return distanceSquared > auraLengthSquared;
		}

		/// <summary>
		/// <para>Where an approaching truck should STOP: the point on the soldier→truck line sitting one cell
		/// short of the aura edge, measured from the SOLDIER. Driving onto his cell instead would buy up to
		/// a full aura's worth of extra exposure (5 cells for TRUK) for nothing — the push only needs him
		/// inside the aura, and the truck is a soft, high-value target in a position the caller reaches
		/// precisely when the line has scattered.</para>
		///
		/// <para>THE MARGIN IS NOT COSMETIC — it is what stops the truck stalling. The destination is quantized to
		/// a cell (Map.CellContaining) before it is ordered, and a cell centre sits up to half a cell
		/// diagonal — 1024 * sqrt(2) / 2 ≈ 724 — from the point that produced it. Clamping to the exact aura
		/// edge would therefore resolve, half the time, to a cell OUTSIDE the aura; a truck at aura + ε would
		/// order itself somewhere it still cannot push from, re-derive the same point next scan, and park
		/// just out of range while the soldier starves. One CellLength of margin covers the error with room
		/// to spare (aura - 1024 + 724 = aura - 300, strictly inside).</para>
		///
		/// <para>The same margin is what keeps the order from being a no-op, i.e. from resolving to the cell the
		/// truck is already standing in. The chain: this function only runs behind NeedsApproach, which
		/// compares squared lengths as integers (WDist.LengthSquared is (long)Length * Length — no sqrt on
		/// that side), so dSq > auraSq gives true distance D > aura exactly. The stop point then sits within
		/// one world-unit of stop = aura - 1024 from the soldier — NOT merely at most stop: HorizontalLength
		/// is a FLOOR integer sqrt (Exts.ISqrt), so it understates the true length, which overstates the
		/// scale factor stop/distance and pushes the point OUTWARD, while the per-component (int) casts floor
		/// it back inward. Net it is strictly under stop + 1, since the overstatement is under one part in
		/// `distance` and distance > stop on this branch. Its distance from the truck therefore exceeds
		/// D - (stop + 1) > aura - stop - 1 = 1023. A RESTING truck sits on its cell centre, and every point
		/// of a cell is within 724 of that centre; since 1023 > 724, the stop point provably falls outside
		/// the truck's own cell. Caveat: a truck caught MID-MOVE is off
		/// centre, so it can in principle resolve to the cell it currently occupies — it then halts there,
		/// and the next scan recomputes from a cell centre and moves it. That is a one-scan delay, not the
		/// stall this margin exists to prevent.</para>
		///
		/// <para>Total by construction: an aura no wider than the margin has no room to stop short, so it falls
		/// back to the soldier's own position (TRUK's 5c0 never reaches that branch); a truck already inside
		/// the stop radius is returned unmoved rather than pushed back out. Integer WPos/WVec math with a
		/// long intermediate so the scale never overflows; WVec.HorizontalLength is the engine's
		/// deterministic integer sqrt. Zero RNG.</para>
		/// </summary>
		public static WPos ApproachTarget(WPos truckPos, WPos soldierPos, int auraLength)
		{
			if (auraLength <= SupplyHuntMath.CellLength)
				return soldierPos;

			var stop = auraLength - SupplyHuntMath.CellLength;

			var delta = truckPos - soldierPos;
			var distance = delta.HorizontalLength;

			// Also covers the co-located case (distance 0), so the scaling below never divides by zero.
			if (distance <= stop)
				return truckPos;

			var x = soldierPos.X + (int)((long)delta.X * stop / distance);
			var y = soldierPos.Y + (int)((long)delta.Y * stop / distance);
			return new WPos(x, y, truckPos.Z);
		}

		/// <summary>
		/// The shared-instance gate. SupplyFollowerBotModule is a single enable-ai-ANY instance shared by
		/// every bot profile, and post stable-0802 <see cref="InfluenceStack.Participates"/> admits @stable
		/// as well — so the YAML flag ALONE cannot keep this off the @stable player. Mirrors
		/// CommitOnOrderMath.ShouldCommitShared (PoiGoalGuard.cs:300), which exists for exactly this reason:
		/// the hunt fires only for the @experimental bot, leaving every profile sharing the instance
		/// byte-identical.
		/// </summary>
		public static bool ShouldHunt(bool huntEnabled, bool isExperimentalBot)
		{
			return huntEnabled && isExperimentalBot;
		}
	}
}
