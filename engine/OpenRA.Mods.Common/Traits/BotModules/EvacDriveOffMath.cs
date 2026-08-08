#region Copyright & License Information
/*
 * WW3MOD evacuation drive-off (PIPELINE item 38) — how long a ground unit keeps moving once it is past the last
 * cell it can legally path to (pure math).
 *
 * WHY THIS EXISTS: a ground unit cannot path outside the playable area — Locomotor.MovementCostForCell reports
 * every cell where !Map.Contains(cell) as unreachable (Locomotor.cs:191-193) — so the last leg of an evacuation is
 * driven by Drag, in world space, instead of by the pathfinder. Drag takes a DURATION IN TICKS rather than a
 * speed, so "this far, at this unit's pace" has to be converted to a tick count somewhere. Getting it wrong is
 * invisible in review and obvious on screen: too few ticks and the vehicle teleports off the map, too many and it
 * crawls.
 *
 * WHY IT IS BOUNDED RATHER THAN A BARE DIVISION: Drag refuses to advance while its mover trait is disabled
 * (Drag.cs:49-50), and RotateToEdge only sells when the leg ends, so a duration derived from an unvalidated speed
 * strands a unit rather than merely looking wrong. Every degenerate ruleset input collapses to a finite, positive
 * count.
 *
 * SCOPE NOTE — the refund is unchanged, but the leg is NOT purely presentational. The unit stays alive for the
 * length of the drive, so it keeps counting in PlayerStatistics.ArmyValue until Dispose, deferring elimination /
 * army-zero win conditions by that long (order of a second for a vehicle, several for slow infantry). It also
 * keeps occupying the cell it left from — see the SetLocation note in RotateToEdge.StartDriveOff.
 *
 * DETERMINISM (influence-stack invariant): zero random draws, pure integer arithmetic, no floating point and no
 * collection iteration. Two clients over the same synced state compute the same tick count.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	public static class EvacDriveOffMath
	{
		/// <summary>Hard ceiling on a single drive-off, in ticks (25 ticks/s ⇒ 20s). Reached only by a unit so slow
		/// that the honest duration would be a visible crawl.</summary>
		public const int MaxDriveOffTicks = 500;

		/// <summary>Ticks to move <paramref name="distance"/> world units at <paramref name="speed"/> world units per
		/// tick — the Drag duration that makes the off-map leg travel at the unit's own pace.
		///
		/// Rounds UP: a leg shorter than one tick's travel still takes a tick, because Drag lerps over
		/// <c>length - 1</c> and a length of 0 or 1 snaps straight to the end — the teleport this change exists to
		/// remove.
		///
		/// Defensive on both inputs because both come from the ruleset: a non-positive speed (immobilised or
		/// misconfigured) would divide by zero, and a non-positive distance means the unit is already there. Both
		/// collapse to the 1-tick floor rather than throwing, because this runs inside an activity that must
		/// terminate. Capped at <see cref="MaxDriveOffTicks"/>. Pure.</summary>
		public static int DriveOffTicks(int distance, int speed)
		{
			if (distance <= 0 || speed <= 0)
				return 1;

			// Ceiling division, long-widened: a large span plus the rounding term overflows int32 well before the
			// cap could clamp it, and an overflowed intermediate wraps negative and returns the 1-tick floor — a
			// silent teleport.
			var ticks = (int)(((long)distance + speed - 1) / speed);
			return ticks > MaxDriveOffTicks ? MaxDriveOffTicks : (ticks < 1 ? 1 : ticks);
		}
	}
}
