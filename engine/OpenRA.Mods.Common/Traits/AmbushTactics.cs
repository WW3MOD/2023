#region Copyright & License Information
/*
 * WW3MOD PIPELINE item 8 — widened Ambush behaviour.
 *
 * Pure, world-free decision helpers for the ambush stages. Keeping the decision here (rather than
 * inline in the activity/trait, which are coupled to Actor/World/Move) lets the halt/spring rules be
 * pinned directly by NUnit with no simulation harness — the same pattern as FormationRealism and the
 * FiresStandoff / Cohesion math helpers. Zero RNG, integer/bool only.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>Which Stage-3 trigger sprung a stationary literal-ambush this tick (None ⇒ keep holding).
	/// Ordered to encode the design's "first of triggers 1–5" precedence: detection/damage force an
	/// immediate spring; the score-derived triggers (degrading / saturation / overrun) only fire behind
	/// them. The enum value is diagnostic — the boolean "spring or not" is <c>!= None</c>.</summary>
	public enum AmbushSpringTrigger
	{
		None,
		Detected,           // 1 — a group member is visible to the target's owner
		Damaged,            // 2 — took fire
		BestStrikeDegrading, // 3 — best engageable target predicted to leave range AND worthwhile
		Saturation,         // 4 — worthwhile score high, sustained
		Overrun,            // 5 — an engageable enemy has breached minimum range
	}

	public static class AmbushTactics
	{
		/// <summary>
		/// Stage 2 — "halt before contact". Decides whether an Ambush unit that is attack-moving or
		/// auto-moving and has just scanned an enemy should HALT into an idle ambush (drop the march,
		/// hold fire, pre-aim) instead of the stock stop-and-fire-on-contact.
		///
		/// Precedence — any earlier gate failing returns false, i.e. "take the original engage path"
		/// (which keeps the ungated path byte-identical to stock):
		///   <paramref name="tacticsEnabled"/> — the default-off gate (AmbushTacticsCondition granted).
		///       Off ⇒ never halt. This is the clause that makes @stable / control bots byte-identical.
		///   <paramref name="stance"/> == Ambush — only Ambush units halt; FireAtWill / HoldFire engage
		///       (or hold) exactly as before.
		///   <paramref name="hasValidTarget"/> — nothing scanned ⇒ nothing to halt for.
		///   !<paramref name="groupDetected"/> — halt ONLY while the group is still unseen by the target's
		///       owner. Once the ambush is blown (any group member visible to the enemy) fall through and
		///       engage immediately; holding fire from an exposed position just wastes the alpha strike.
		/// </summary>
		public static bool ShouldHaltBeforeContact(bool tacticsEnabled, UnitStance stance, bool hasValidTarget, bool groupDetected)
		{
			if (!tacticsEnabled)
				return false;

			if (stance != UnitStance.Ambush)
				return false;

			if (!hasValidTarget)
				return false;

			return !groupDetected;
		}

		// ────────────────────────────────────────────────────────────────────────────────────────
		// Stage 3 — stationary literal-ambush state machine (design §5.2).
		//
		// The world-touching parts (the kill-zone FindActorsInCircle scan, fog filter, range/velocity
		// sampling, condition-gate read) live in AutoTarget. Everything below is the pure decision core
		// so the whole trigger table can be pinned by NUnit with no game running. Integer/bool only; the
		// caller must have already ordered any actor set by ActorID before deriving these scalars, so the
		// spring decision is iteration-order independent.
		// ────────────────────────────────────────────────────────────────────────────────────────

		/// <summary>Worthwhile contribution of ONE fog-visible enemy in the kill zone:
		/// <c>weightThreat·threatValue + weightValue·cellValue</c>. Threat represents "how dangerous"
		/// (armed durability); cellValue represents "how juicy" (economic value). Splitting them is what
		/// lets an undefended supply truck (threat ≈ 0, value &gt; 0) still register as worthwhile — a pure
		/// danger-field metric is value-blind and would ignore it (design §3.2). The aggregate is the sum
		/// over contacts; addition is order-independent so the total does not depend on scan order.</summary>
		public static int ContactScore(int threatValue, int cellValue, int weightThreat, int weightValue)
		{
			return weightThreat * threatValue + weightValue * cellValue;
		}

		/// <summary>Linear range extrapolation: where the target's range will be <paramref name="ticksAhead"/>
		/// ticks from now, given its current range and signed radial rate (positive = opening/leaving,
		/// negative = closing). Integer math; truncation toward zero is deterministic.</summary>
		public static int PredictedRange(int currentRange, int radialSpeedPerTick, int ticksAhead)
		{
			return currentRange + radialSpeedPerTick * ticksAhead;
		}

		/// <summary>Derive the signed radial rate per tick from two range samples taken
		/// <paramref name="sampleInterval"/> ticks apart (positive = the target is opening the range).
		/// Guards a zero/negative interval (returns 0 = "no motion known") so a first sample or a clock
		/// glitch can never divide by zero.</summary>
		public static int RadialSpeedPerTick(int previousRange, int currentRange, int sampleInterval)
		{
			if (sampleInterval <= 0)
				return 0;

			return (currentRange - previousRange) / sampleInterval;
		}

		/// <summary>Trigger-3 geometry half: is the best target predicted to be OUT of weapon range within
		/// <paramref name="ticksAhead"/> ticks at its current radial rate? A target already out of range
		/// (currentRange &gt; maxRange) trivially satisfies this. Combined with the worthwhile-score gate and
		/// the degrade hysteresis in <see cref="EvaluateSpring"/> to form the full "best strike is about to
		/// be lost" signal (the reframed optimal-stopping kernel, design §3.3/§5.2).</summary>
		public static bool PredictedToExitRange(int currentRange, int radialSpeedPerTick, int ticksAhead, int maxRange)
		{
			return PredictedRange(currentRange, radialSpeedPerTick, ticksAhead) > maxRange;
		}

		/// <summary>Is this range sample a "degrade" (the best target opened the range beyond the epsilon
		/// hysteresis band since the last sample)? A band, not a bare &gt;, so range jitter / spatial-hash
		/// rounding near a stationary target does not read as a steady retreat (design §3.6 oscillation
		/// case). Caller feeds the result to <see cref="UpdateSustainCounter"/> so only CONSECUTIVE degrade
		/// samples count.</summary>
		public static bool IsDegradeSample(int currentRange, int previousRange, int epsilon)
		{
			return currentRange - previousRange > epsilon;
		}

		/// <summary>Trigger-5: has an engageable enemy breached the ambush's minimum stand-off? At/inside
		/// the threshold the enemy is about to walk on top of the position, so spring now regardless of the
		/// worthwhile score. Threshold is the caller's max(weapon MinRange, overrun floor).</summary>
		public static bool IsOverrun(int nearestEngageableRange, int overrunThreshold)
		{
			return nearestEngageableRange <= overrunThreshold;
		}

		/// <summary>Increment a "sustained for N samples" counter when the condition held this sample, else
		/// reset it to 0. The reset-on-miss is what makes triggers 3 and 4 require CONSECUTIVE samples
		/// (hysteresis), not a lucky single spike.</summary>
		public static int UpdateSustainCounter(int current, bool conditionHeld)
		{
			return conditionHeld ? current + 1 : 0;
		}

		/// <summary>
		/// The Stage-3 trigger table (design §5.2), evaluated in the fixed 1→5 precedence order so the
		/// returned <see cref="AmbushSpringTrigger"/> names the FIRST satisfied trigger. Pure: every input
		/// is a scalar/bool the caller has already extracted, so this is fully NUnit-pinnable.
		///
		///   1 Detected            — <paramref name="detected"/> (a group member is visible to the enemy).
		///   2 Damaged             — <paramref name="damaged"/> (took fire).
		///   3 BestStrikeDegrading — <paramref name="bestTargetPredictedExit"/> AND score ≥
		///        <paramref name="minSpringThreshold"/> AND <paramref name="consecutiveDegradeSamples"/> ≥
		///        <paramref name="requiredDegradeSamples"/>. Fire while the best shot is still available
		///        rather than after the aggregate falls — but only once it is genuinely, repeatedly
		///        degrading and the target is worth it.
		///   4 Saturation          — <paramref name="consecutiveHighSamples"/> ≥
		///        <paramref name="requiredHighSamples"/> (the caller only advances that counter while the
		///        score ≥ HighSpringThreshold). Handles the "enemy stops / never decreases" degenerate
		///        case (§3.6) — the column is fully in the zone, spring at peak density.
		///   5 Overrun             — <paramref name="overrun"/>.
		///
		/// Detection and damage dominate the score-derived triggers because an exposed or hit ambush must
		/// commit its alpha strike immediately — waiting for a "nicer" score once seen just eats return
		/// fire (the AT-suppression trap, §3.3).
		/// </summary>
		public static AmbushSpringTrigger EvaluateSpring(
			bool detected,
			bool damaged,
			bool bestTargetPredictedExit,
			int score,
			int minSpringThreshold,
			int consecutiveDegradeSamples,
			int requiredDegradeSamples,
			int consecutiveHighSamples,
			int requiredHighSamples,
			bool overrun)
		{
			if (detected)
				return AmbushSpringTrigger.Detected;

			if (damaged)
				return AmbushSpringTrigger.Damaged;

			if (bestTargetPredictedExit
				&& score >= minSpringThreshold
				&& consecutiveDegradeSamples >= requiredDegradeSamples)
				return AmbushSpringTrigger.BestStrikeDegrading;

			if (consecutiveHighSamples >= requiredHighSamples)
				return AmbushSpringTrigger.Saturation;

			if (overrun)
				return AmbushSpringTrigger.Overrun;

			return AmbushSpringTrigger.None;
		}
	}
}
