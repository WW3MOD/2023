#region Copyright & License Information
/*
 * WW3MOD defence-in-depth echelon (builds on PIPELINE item 11 fires standoff) — line-order geometry (pure math).
 *
 * PERCEIVED BEHAVIOUR: indirect-fire pieces (artillery) no longer drive alone to the front and die. The bot
 * keeps a role-aware line order — the MainBattle SCREEN (tanks/line infantry) forward, each indirect-fire
 * piece held ECHELONED BEHIND the screen line by roughly its RANGE SURPLUS over the screen's engagement
 * range (e.g. artillery outranging a tank engagement by ~15 cells holds ~16 cells behind the screen). It can
 * still range the front the screen is fighting on, but sits on the friendly side of the shield.
 *
 * RELATIONSHIP TO THE FIRES STANDOFF (item 11): the standoff anchors a piece at its weapon range from the
 * axis TARGET, on the bearing from the target toward the piece's CURRENT position — so it never guarantees
 * the screen is between the piece and the enemy (the piece can be anchored off to a flank, or advance ahead
 * of a slower screen, and be caught in the open). The echelon anchors relative to the piece's OWN SCREEN
 * instead, so the piece is always on the friendly side of the line. The echelon REPLACES the target-standoff
 * anchor for a piece only when a live screen exists to hide behind; a piece with NO screen on its axis (a
 * pure-artillery axis, or a deliberately-solo fire tasking) falls back to the target-standoff and goes where
 * the mission needs it — the override the doctrine requires.
 *
 * ECHELON DEPTH FORMULA (deterministic, all lengths WDist):
 *   surplus = max(0, ownMaxRange - screenRange)                // how much the piece outranges the shield
 *   depth   = max(minDepth, surplus + buffer)                  // buffer keeps it a touch further back than
 *                                                              //   "just barely in range of the front"; the
 *                                                              //   minDepth floor holds a same-range piece back too
 * The anchor is that depth behind the screen line (its centroid), offset directly AWAY from the target — the
 * shortest hop onto the friendly side of the line.
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws, pure integer WDist/WVec math. The bearing scale
 * uses a long intermediate so it never overflows on large maps. The screen centroid is an order-independent
 * sum and the screen range an order-independent max, so no ActorID sort is needed to make it byte-identical.
 *
 * v3-portable: engine-free static math (NUnit-pinned in EchelonMathTest); only the tasking plumbing that
 * consumes it (PoiOffensiveBotModule.OrderFiresStandoff) is engine-specific.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	public static class EchelonMath
	{
		/// <summary>How far behind the screen line an indirect-fire piece holds (WDist length): its range
		/// surplus over the screen's engagement range, plus <paramref name="bufferLength"/>, but never below
		/// <paramref name="minDepthLength"/>. A piece that does NOT outrange the screen (surplus 0) still sits
		/// at least the floor back. Pure integer math.</summary>
		public static int EchelonDepth(int ownRangeLength, int screenRangeLength, int bufferLength, int minDepthLength)
		{
			var surplus = ownRangeLength - screenRangeLength;
			if (surplus < 0)
				surplus = 0;

			var depth = surplus + bufferLength;
			return depth < minDepthLength ? minDepthLength : depth;
		}

		/// <summary>The echelon anchor: a point <paramref name="depthLength"/> behind <paramref name="screenLine"/>
		/// (the screen centroid), offset directly AWAY from <paramref name="target"/> — the piece drops back onto
		/// the friendly side of the line by the shortest hop. A degenerate screenLine==target falls back to a
		/// fixed north offset so the result stays deterministic. Pure integer vector math with a long
		/// intermediate so the bearing scale never overflows on large maps.</summary>
		public static WPos EchelonAnchor(WPos screenLine, WPos target, int depthLength)
		{
			var awayFromTarget = screenLine - target;
			var dist = awayFromTarget.HorizontalLength;
			if (dist <= 0)
				return screenLine + new WVec(0, -depthLength, 0);

			var x = (int)((long)awayFromTarget.X * depthLength / dist);
			var y = (int)((long)awayFromTarget.Y * depthLength / dist);
			return screenLine + new WVec(x, y, 0);
		}

		/// <summary>True when the piece is farther than <paramref name="toleranceLength"/> from its echelon
		/// <paramref name="anchor"/> and must reposition. Within the tolerance it holds so AutoTarget keeps
		/// firing and band-edge jitter doesn't re-order it every re-eval. Pure integer distance test.</summary>
		public static bool NeedsReposition(WPos anchor, WPos unit, int toleranceLength)
		{
			return (unit - anchor).HorizontalLength > toleranceLength;
		}
	}
}
