#region Copyright & License Information
/*
 * WW3MOD capture-escort right-sizing (@experimental) — believed-threat escort tier (pure math).
 *
 * PERCEIVED BEHAVIOUR: the capture coordinator no longer walks a fixed escort to EVERY derrick. It scales
 * the capture party by BELIEVED threat at the target. An oil derrick sitting in our own verified-safe
 * territory next to our Supply Route gets the technician ALONE (no combat units reserved to babysit it);
 * a mildly-exposed one gets a small escort; a contested/enemy-territory one keeps the full escort. Combat
 * units not reserved for a safe capture stay idle and are picked up by the offense/other captures instead.
 *
 * This carries ONE decision the module turns into an escort count: given the believed surroundings of a
 * capture target — the ring-averaged control score (positive = believed ours), the believed anti-ground
 * danger at the cell, and the target's distance from our own SR — bucket it into an escort TIER
 * (None / Light / Full). The field reads are supplied by the caller and are fog-legal (ControlField +
 * DangerFieldLayer only); this class never touches the world.
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws, pure integer comparisons over caller-supplied
 * scores. Two clients over the same synced belief state bucket a target identically.
 *
 * v3-portable: engine-free static math (NUnit-pinned in EscortSizingMathTest); only the tasking plumbing
 * that consumes it (CaptureCoordinatorBotModule.DispatchEscort) is engine-specific.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	public static class EscortSizingMath
	{
		public enum EscortTier
		{
			/// <summary>Verified-safe own territory near our SR — send the capturer alone, reserve no escort.</summary>
			None,

			/// <summary>Mildly exposed open ground — a small fixed escort.</summary>
			Light,

			/// <summary>Contested / believed-enemy ground — keep the full escort.</summary>
			Full,
		}

		/// <summary>Bucket a capture target into an escort tier from its BELIEVED surroundings. Order of tests
		/// is load-bearing: a target is FULL the moment either threat channel reads contested (a believed weapon
		/// envelope over the cell, OR the ring-averaged control reads deep believed-enemy), so a hot target is
		/// never shrunk. Only a target that clears BOTH threat tests AND reads strongly-ours + low-danger +
		/// near-SR falls to NONE; everything in between is LIGHT.
		///
		/// Inputs (all caller-sampled, fog-legal):
		///   <paramref name="neighborhoodControlScore"/> — ring-averaged ControlField score around the target
		///     (positive = believed ours, negative = believed enemy). The RING, not the target's own cell, which
		///     a site anchor floors to deep-enemy regardless of who surrounds it.
		///   <paramref name="groundDanger"/> — DangerFieldLayer.GroundDanger at the target cell.
		///   <paramref name="distanceFromSRCells"/> — target distance from our own SR in cells, or a negative
		///     value when unknown (legacy no-PoiMap path); unknown never satisfies the near-SR gate.
		///
		/// Thresholds:
		///   <paramref name="safeControlScore"/> — ring control at/above which the surroundings count strongly-ours.
		///   <paramref name="safeDangerThreshold"/> — ground danger at/below which the cell counts low-danger.
		///   <paramref name="safeMaxDistanceFromSRCells"/> — target within this many cells of our SR counts near;
		///     &lt;= 0 disables the distance gate (near always satisfied).
		///   <paramref name="contestedControlBand"/> — ring control STRICTLY below its negation counts deep-enemy
		///     (pass ControlField.GrayBand so the tri-state matches the field's own classification).
		///   <paramref name="contestedDangerThreshold"/> — ground danger ABOVE which the cell counts contested.</summary>
		public static EscortTier Resolve(
			int neighborhoodControlScore,
			int groundDanger,
			int distanceFromSRCells,
			int safeControlScore,
			int safeDangerThreshold,
			int safeMaxDistanceFromSRCells,
			int contestedControlBand,
			int contestedDangerThreshold)
		{
			// FULL first: a believed weapon envelope reaches the cell, or the surroundings read deep believed-enemy.
			if (groundDanger > contestedDangerThreshold || neighborhoodControlScore < -contestedControlBand)
				return EscortTier.Full;

			// NONE: strongly-ours ring AND low believed danger AND near our SR — the technician goes alone.
			var nearSR = safeMaxDistanceFromSRCells <= 0
				|| (distanceFromSRCells >= 0 && distanceFromSRCells <= safeMaxDistanceFromSRCells);
			if (neighborhoodControlScore >= safeControlScore && groundDanger <= safeDangerThreshold && nearSR)
				return EscortTier.None;

			// Everything between contested and verified-safe.
			return EscortTier.Light;
		}
	}
}
