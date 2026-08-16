#region Copyright & License Information
/*
 * WW3MOD movement accelerator (pure math) — the two numbers inside the Move activity that decide how fast a unit
 * is travelling this tick.
 *
 * WHY THIS EXISTS: both of these were IEEE floating point, inline, inside the synchronised simulation. Move is
 * lockstep: every client ticks the same activity over the same state and must reach bit-identical results. A float
 * there is a determinism defect on principle — its result selects an acceleration step, which lands in
 * Mobile.CurrentSpeed, which accumulates into MovePart.progress, which is tested against `progress >= Distance` to
 * decide the tick a unit crosses into the next cell. A one-unit difference in CurrentSpeed moves that crossing by a
 * whole tick, and *none* of CurrentSpeed, progress or Distance carries [Sync], so the drift is invisible to the
 * desync detector until it finally flips a cell transition and the report blames Mobile.
 *
 * WHAT THIS IS NOT: a behaviour change. The desync investigation predicted the old form was ALSO arithmetically
 * wrong at exact thirds — 18f/54f is 0.33333334, which does round up — but that prediction is wrong, and the
 * exhaustive sweep in MoveAccelerationMathTest is what shows it: the product 0.33333334 * 3 = 1.0000000298 rounds
 * BACK to exactly 1.0f, because floats in [1, 2) are spaced 1.19e-7 apart and the excess is 2.98e-8. Across the
 * whole reachable domain the two forms select the same step every time. The defect removed here is the hazard, not
 * an observed miscalculation.
 *
 * DETERMINISM: pure integer arithmetic, zero random draws, no collection iteration, no floating point.
 */
#endregion

namespace OpenRA.Mods.Common.Activities
{
	public static class MoveAccelerationMath
	{
		/// <summary>Index into <c>Mobile.AccelerationSteps</c> for a unit at <paramref name="currentSpeed"/> heading
		/// for <paramref name="maxSpeed"/> — the cell's speed cap after terrain and speed modifiers. The steps array
		/// is a falloff curve sampled across that range, so the index is which band of the ramp we are in:
		/// <c>ceil(currentSpeed * stepCount / maxSpeed) - 1</c>, floored at 0 for a standing start.
		///
		/// Computed as <c>(a + b - 1) / b</c>, the standard exact integer ceiling division, valid because the
		/// numerator is non-negative (CurrentSpeed is clamped at 0 by every writer) and maxSpeed is positive.
		///
		/// PRECONDITION: <c>0 &lt;= currentSpeed &lt; maxSpeed</c>, which the caller guarantees — the
		/// <c>currentSpeed &gt; maxSpeed</c> case decelerates instead and equality skips the branch. Under it the
		/// result is in <c>[0, stepCount - 1]</c>. Deliberately NOT clamped at the top: a caller that breaks the
		/// precondition should throw on the array index rather than silently accelerate by the wrong step.
		///
		/// maxSpeed of 0 is unreachable here for the same reason (a positive speed decelerates, a zero one fails the
		/// inequality) and divides by zero if it ever becomes reachable — loudly, which is the intent. Widened to
		/// long only to keep the intermediate product safe from a pathological ruleset speed. Pure.</summary>
		public static int AccelerationStepIndex(int currentSpeed, int maxSpeed, int stepCount)
		{
			var index = (int)(((long)currentSpeed * stepCount + maxSpeed - 1) / maxSpeed) - 1;
			return index > 0 ? index : 0;
		}

		/// <summary>Percentage of its speed a unit keeps when it redirects mid-cell through a turn of
		/// <paramref name="angleDiff"/> (0-512, where 512 is a full reversal). Falls linearly from 100% at 256 (90°)
		/// to <paramref name="redirectSpeedPenalty"/>% at 512 (180°).
		///
		/// PRECONDITION: <c>256 &lt; angleDiff &lt;= 512</c> — the caller applies no penalty below 90°.
		///
		/// This one is a determinism fix with no behaviour change: the divisor is 256, so the old float form
		/// `(angleDiff - 256) / 256f * (100 - penalty)` was exactly representable and already agreed with exact
		/// arithmetic on every reachable input. It is converted anyway so that no float remains on the path that
		/// writes CurrentSpeed, and so a later edit to either constant cannot quietly reintroduce rounding. Pure.</summary>
		public static int RedirectSpeedRetained(int angleDiff, int redirectSpeedPenalty)
		{
			return 100 - ((angleDiff - 256) * (100 - redirectSpeedPenalty) / 256);
		}
	}
}
