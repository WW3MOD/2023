#region Copyright & License Information
/*
 * WW3MOD evacuation drive-off (PIPELINE item 38) — how long a ground unit keeps moving once it is past the last
 * cell it can legally path to (pure math).
 *
 * PERCEIVED BEHAVIOUR: an evacuating vehicle no longer winks out of existence several cells inside the map. It
 * drives to the boundary and keeps going until it is off the battlefield, and only then does the refund tick
 * appear. Purely presentational — the refund is unchanged.
 *
 * WHY THIS LAYER EXISTS (the root cause it works around): a ground unit CANNOT path outside the playable area.
 * Locomotor.MovementCostForCell returns the unreachable cost for any cell where !Map.Contains(cell)
 * (Locomotor.cs:191-193), so every Mobile pathfind stops at the boundary by construction. Aircraft dodge this
 * because Fly takes a WPos and ignores the locomotor entirely (RotateToEdge.cs:184-187). The only way to carry a
 * ground unit past the edge is therefore to stop asking the pathfinder and move it in world space — the Drag
 * activity, which drives IPositionable.SetCenterPosition directly (Drag.cs:52-58). Drag takes a DURATION IN TICKS
 * rather than a speed, so somebody has to convert "this far, at this unit's speed" into a tick count. That is this
 * class, and the reason it is a class at all is that getting it wrong is invisible in code review but obvious on
 * screen: too few ticks and the vehicle teleports off the map, too many and it crawls.
 *
 * WHY A BOUND, NOT JUST A DIVISION: Drag is IsInterruptible = false and refuses to advance while its mover trait
 * is disabled (Drag.cs:49-50), so a duration derived from an unvalidated speed is a hang risk, not just a cosmetic
 * one — a unit whose Mobile is disabled mid-drag would sit outside the map forever, never selling. The engine-side
 * caller has its own independent deadline for that case; this class additionally refuses to hand out an unbounded
 * or non-positive duration in the first place, so neither layer relies on the other being correct.
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws, pure integer arithmetic, no collection iteration and
 * no floating point. Two clients over the same synced state compute the same tick count.
 *
 * v3-portable: engine-free static math (NUnit-pinned in EvacDriveOffMathTest); only the world-reading plumbing
 * that supplies the distance and the unit's speed is engine-specific.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	public static class EvacDriveOffMath
	{
		/// <summary>Hard ceiling on a single drive-off, in ticks (25 ticks/s ⇒ 20s). Reached only by a unit so slow
		/// that the honest duration would be a visible crawl; at that point the deadline in the caller sells it where
		/// it stands, which reads better than a vehicle inching off screen for a minute.</summary>
		public const int MaxDriveOffTicks = 500;

		/// <summary>Ticks to move <paramref name="distance"/> world units at <paramref name="speed"/> world units per
		/// tick — the Drag duration that makes the off-map leg travel at the unit's own pace instead of teleporting.
		///
		/// Rounds UP, so a leg shorter than one tick's travel still takes a tick: Drag with a length of 0 would divide
		/// by zero on its own lerp, and a length of 1 snaps straight to the end position. Both read as a teleport,
		/// which is the exact artefact this whole change exists to remove.
		///
		/// Defensive on both inputs because both come from the ruleset rather than from us: a non-positive speed (an
		/// immobilised or misconfigured unit) would otherwise divide by zero, and a non-positive distance means the
		/// unit is already where it is going. Both collapse to the 1-tick floor rather than throwing, because this
		/// runs inside an activity that MUST terminate — see the header note on why a hang here is worse than a
		/// slightly wrong duration. Capped at <see cref="MaxDriveOffTicks"/>. Pure.</summary>
		public static int DriveOffTicks(int distance, int speed)
		{
			if (distance <= 0 || speed <= 0)
				return 1;

			// Ceiling division, long-widened: distance is a world-unit span (1024/cell) and a large map diagonal
			// times the rounding term would overflow int32 well before the cap could clamp it.
			var ticks = (int)(((long)distance + speed - 1) / speed);
			return ticks > MaxDriveOffTicks ? MaxDriveOffTicks : (ticks < 1 ? 1 : ticks);
		}

		/// <summary>Is <paramref name="cell"/> far enough outside the playable area to count as "left the
		/// battlefield"? Expressed as a pure predicate over the boundary numbers so the off-map test is pinnable
		/// without a map: the caller supplies the unit's projected cell coordinates and the playable rectangle.
		///
		/// <paramref name="left"/>/<paramref name="top"/> are INCLUSIVE and <paramref name="right"/>/
		/// <paramref name="bottom"/> are EXCLUSIVE, matching Map.Bounds (Rectangle.FromLTRB(tl.U, tl.V, br.U + 1,
		/// br.V + 1), Map.cs:1590) — the asymmetry is the engine's, not ours, and inlining it here is what keeps the
		/// caller from having to remember it.
		///
		/// A unit is off the map when it has cleared the boundary by <paramref name="margin"/> cells on ANY ONE side;
		/// clearing a corner is not required, and must not be, or a unit evacuating due north of a side edge would
		/// never satisfy the test. Pure.</summary>
		public static bool IsClearOfBounds(int u, int v, int left, int top, int right, int bottom, int margin)
		{
			if (margin < 0)
				margin = 0;

			return u + margin < left || u >= right + margin
				|| v + margin < top || v >= bottom + margin;
		}
	}
}
