#region Copyright & License Information
/*
 * WW3MOD move-order terms — the ONE statement of the two things every player move-type order
 * agrees on, shared by the three sites that issue one (2026-09-01).
 *
 * THE THREE SITES. Mobile.ResolveOrder's "Move" and "ForceMove", and AttackMove.ResolveOrder's
 * "AttackMove". They take the same click, clamp it the same way, and hand it to the same Move
 * activity with the same nearEnough. Until this file existed they each wrote that out by hand, and
 * one of them had already drifted.
 *
 * THE DRIFT, which is why this is a file and not a comment. AttackMove resolved
 * Mobile.NearestMoveableCell EAGERLY, inside ResolveOrder, and captured the answer in the closure it
 * handed to AttackMoveActivity. ResolveOrder runs the moment the order arrives — including for a
 * SHIFT-QUEUED order, whose activity is then built there and left sitting in the queue behind
 * whatever is still running. So a queued attack-move carried an answer about what ground was
 * reachable when the player clicked, and acted on it later, after that had stopped being true.
 * Demonstrated 2026-09-01 by tools/autotest/scenarios/test-queued-attackmove-stale-cell: a unit
 * queued onto a cell buried in a block of buildings walked to (28,16) — a Chebyshev-2 ring answer —
 * after the buildings were removed mid-march, instead of to the ordered (30,16).
 *
 * THE RULE: RELOCATION IS RESOLVED WHEN THE MOVE STARTS, NEVER WHEN THE ORDER IS ISSUED.
 *
 * THE TWO SITES IMPLEMENT THAT RULE BY DIFFERENT MECHANISMS, DELIBERATELY. Do not "unify" them.
 *   * Mobile passes evaluateNearestMovableCell: true and the raw cell, so Move.OnFirstRun relocates.
 *   * AttackMove calls NearestMoveableCell INSIDE its closure, which AttackMoveActivity invokes when
 *     the move actually starts.
 * They are not interchangeable, and the asymmetry is load-bearing. Move.OnFirstRun does not merely
 * relocate — it NULLS the destination when the relocated cell still cannot be entered
 * (Move.cs:139-142), and a null destination makes getPath return AlreadyAtDestination with no path,
 * so the unit does not move at all. NearestMoveableCell's own miss behaviour is the opposite: it
 * returns the cell it was given (Mobile.cs:850-870), which then paths under PathSearchOrder's last
 * resort of BlockedByActor.None (Move.cs:30-37) and walks the unit up to the obstruction. For an
 * attack-move ordered into a dense base — every cell within the 10-cell annulus occupied — that is
 * the difference between advancing to the edge of the enemy position and standing still on the spot.
 * Standing still is the worse bug, so AttackMove keeps the closure.
 *
 * WHAT IS NOT SHARED, and must not be forced to be: the shroud gate. Mobile reads
 * Info.LocomotorInfo.MoveIntoShroud and AttackMove reads its own AttackMoveInfo.MoveIntoShroud.
 * Those are separate mod-facing fields with separate defaults; collapsing them would silently retune
 * one of the two. Share the terms, not the configuration.
 */
#endregion

using OpenRA.Traits;

namespace OpenRA.Mods.Common.Orders
{
	public static class MoveOrderTerms
	{
		/// <summary>How close counts as arrived when a blocker stands between the unit and its
		/// destination. NOT a general arrival tolerance — Move only consults it on the step that is
		/// actually blocked (Move.cs:264-269), so the destination cell still determines where an
		/// unobstructed unit ends up.</summary>
		public const int NearEnoughCells = 8;

		/// <summary>The cell a player's move-type order targets. Clamped to the map, because the
		/// click can land outside the playable bounds.</summary>
		public static CPos DestinationCell(Map map, in Target target)
		{
			return map.Clamp(map.CellContaining(target.CenterPosition));
		}
	}
}
