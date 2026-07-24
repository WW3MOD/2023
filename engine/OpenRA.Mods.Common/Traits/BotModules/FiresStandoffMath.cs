#region Copyright & License Information
/*
 * WW3MOD fires doctrine (PIPELINE item 11) — artillery standoff geometry (pure math).
 *
 * PERCEIVED BEHAVIOUR: artillery holds off at weapon range and rains suppressive fire DURING an
 * assault instead of driving to contact and dying to direct fire. Assaults read as combined-arms
 * pushes with the guns kept safely behind the line elements.
 *
 * The whole decision reduces to ONE geometric anchor: a point at (max weapon range - margin) from
 * the piece's current target, on the bearing from the target back toward the piece. AttackMove to
 * that anchor gives all three required behaviours from the SAME order — the shared, tested
 * AttackMove -> AutoTarget path (mirrors the Stage-0 heli standoff):
 *   (a) too far  (dist > maxRange)             -> the anchor is nearer than the piece, so it closes
 *                                                  up to standoff and follows the assault forward;
 *   (b) in band  (inner <= dist <= maxRange)    -> the anchor is ~where it already stands, it holds
 *                                                  and AutoTarget keeps firing while the line presses;
 *   (c) too close(dist < inner, target closed)  -> the anchor is FARTHER from the (now nearer) target
 *                                                  than the piece, so the piece backs a leg off.
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws, pure integer WDist/WVec math. The
 * bearing scale uses a long intermediate so it never overflows on large maps. Two clients over the
 * same synced positions compute the identical anchor and reposition decision.
 *
 * v3-portable: engine-free static math (NUnit-pinned in FiresStandoffTest); only the tasking
 * plumbing that consumes it (PoiOffensiveBotModule.OrderFiresStandoff) is engine-specific.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	public static class FiresStandoffMath
	{
		/// <summary>The distance an indirect-fire piece should sit from its target: its own max weapon
		/// range pulled in by <paramref name="marginLength"/> for a safety cushion, but never below
		/// <paramref name="floorLength"/> (guards a piece whose range is at/under the margin from
		/// anchoring on top of the target). All lengths in WDist units.</summary>
		public static int StandoffRadius(int maxRangeLength, int marginLength, int floorLength)
		{
			var desired = maxRangeLength - marginLength;
			return desired < floorLength ? floorLength : desired;
		}

		/// <summary>The standoff anchor: a point at <see cref="StandoffRadius"/> from <paramref name="target"/>
		/// on the bearing from the target toward <paramref name="unit"/> — the piece backs straight off along
		/// its current line (minimal lateral travel, stays behind the assault). A degenerate unit==target
		/// falls back to a fixed north offset so the result stays deterministic. Pure integer vector math with
		/// a long intermediate so the bearing scale never overflows on large maps.</summary>
		public static WPos StandoffAnchor(WPos target, WPos unit, int maxRangeLength, int marginLength, int floorLength)
		{
			var radius = StandoffRadius(maxRangeLength, marginLength, floorLength);
			var toUnit = unit - target;
			var dist = toUnit.HorizontalLength;
			if (dist <= 0)
				return target + new WVec(0, -radius, 0);

			var x = (int)((long)toUnit.X * radius / dist);
			var y = (int)((long)toUnit.Y * radius / dist);
			return target + new WVec(x, y, 0);
		}

		/// <summary>True when the piece is OUTSIDE its standoff band and must reposition to the anchor:
		/// farther than its max weapon range (can't fire — close up), or nearer than
		/// (<see cref="StandoffRadius"/> - <paramref name="hysteresisLength"/>) (danger closed inside — back
		/// off). Within the band it holds and keeps firing; the hysteresis stops band-edge order chatter.
		/// Pure integer distance test.</summary>
		public static bool NeedsReposition(WPos target, WPos unit, int maxRangeLength, int marginLength,
			int hysteresisLength, int floorLength)
		{
			var radius = StandoffRadius(maxRangeLength, marginLength, floorLength);
			var inner = radius - hysteresisLength;
			if (inner < 0)
				inner = 0;

			var dist = (unit - target).HorizontalLength;
			return dist > maxRangeLength || dist < inner;
		}
	}
}
