#region Copyright & License Information
/*
 * WW3MOD rally-order replay gate — decides which activity a produced unit runs for each waypoint of
 * its Supply Route rally path, and what target-line colour that waypoint shows.
 *
 * The player tags SR rally waypoints with the same modifier keys as unit orders (RallyPoint.cs):
 * Alt = AttackMove, Ctrl = ForceMove, bare = Move. The tag is carried on the waypoint and replayed
 * on every unit that arrives. This file owns the replay half of that contract, so the two halves
 * cannot drift: whatever colour the SR line was drawn in is the colour the arriving unit must show,
 * and an orange-red SR line must produce an actual attack-move.
 *
 * PITFALL: the eligibility test is NOT a locomotor test. It used to be `move is Mobile`, which reads
 * as "ground units only" and silently degraded every aircraft's Alt-tagged waypoint to a plain Move —
 * the SR line stayed red while the helicopter flew a green Move. Nothing about AttackMoveActivity is
 * ground-specific: it drives an IMove, never a Mobile (AttackMoveActivity.cs), the player's own
 * Alt+click path applies no such guard (AttackMove.ResolveOrder), and the engine already queues one
 * for aircraft on the normal production rally path (Aircraft.cs:1526). Ask whether the ACTOR accepts
 * attack-move orders — AttackMove.CanBeOrderedToAttackMove, the same predicate the cursor and the
 * order resolver use — and the SR agrees with the click by construction.
 *
 * Units that genuinely cannot attack-move (LCCV carries -AttackMove:) fall back to a plain Move and
 * must show the plain Move colour with it. An orange-red line over a unit that will never engage
 * anything is the same lie in the other direction.
 */
#endregion

using OpenRA.Primitives;

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>How a single rally waypoint is replayed on a unit arriving from the Supply Route.</summary>
	public readonly struct RallyReplayPlan
	{
		/// <summary>The order type actually replayed, after eligibility fallback.</summary>
		public readonly RallyOrderType Effective;

		/// <summary>Target-line colour the arriving unit shows for this waypoint while selected.</summary>
		public readonly Color TargetLineColor;

		/// <summary>Tolerance passed to <see cref="IMove.MoveTo(CPos, int, bool, Color?)"/>.</summary>
		public readonly int NearEnoughCells;

		public RallyReplayPlan(RallyOrderType effective, Color targetLineColor, int nearEnoughCells)
		{
			Effective = effective;
			TargetLineColor = targetLineColor;
			NearEnoughCells = nearEnoughCells;
		}
	}

	public static class RallyOrderReplayMath
	{
		/// <summary>
		/// Resolve a waypoint's tagged order type against what the arriving unit can actually be
		/// ordered to do.
		/// </summary>
		/// <param name="orderType">The type the player tagged the waypoint with.</param>
		/// <param name="canAttackMove">
		/// <see cref="AttackMove.CanBeOrderedToAttackMove"/> for the arriving actor. Deliberately a
		/// bool rather than an Actor so this stays testable without a World.
		/// </param>
		public static RallyReplayPlan Resolve(RallyOrderType orderType, bool canAttackMove)
		{
			if (orderType == RallyOrderType.AttackMove && canAttackMove)
				return new RallyReplayPlan(RallyOrderType.AttackMove, Color.OrangeRed, 1);

			if (orderType == RallyOrderType.ForceMove)
				return new RallyReplayPlan(RallyOrderType.ForceMove, Color.DeepSkyBlue, 2);

			return new RallyReplayPlan(RallyOrderType.Move, Color.Green, 2);
		}
	}
}
