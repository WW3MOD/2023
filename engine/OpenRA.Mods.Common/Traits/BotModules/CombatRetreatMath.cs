#region Copyright & License Information
/*
 * WW3MOD combat-quality force-preservation (@experimental) — retreat-when-losing decision (pure math).
 *
 * PERCEIVED BEHAVIOUR: an attack axis that is clearly LOSING its fight falls back toward friendly control
 * instead of grinding to the death at the objective, and a lost fight is not fed fresh reinforcements
 * piecemeal. Targets the measured combat deficit — the @experimental bot kills evenly with @stable yet
 * dies for ~2x the army value (WORKSPACE/benchmarks/260802-exp-vs-stable0730-combatweighted.md): it treats
 * its combat force as expendable while it wins the capture race.
 *
 * The decision is a two-state FSM over a BELIEVED local force ratio (fog-legal, tallied from the belief /
 * danger stack by the consumer; this class is engine-free integer math):
 *   * Engaged  — accumulate a losing STREAK; commit to a retreat only once losing is SUSTAINED for a window
 *                (a single unlucky field read never triggers a fall-back). A non-losing eval resets it.
 *   * Retreating — COMMIT: hold the retreat until the squad reaches safety OR the ratio recovers past a
 *                STRICTER re-engage margin. This is the anti-flip-flop hysteresis — no advance/retreat
 *                oscillation at the ratio boundary.
 *
 * SHAPE: an ABORT TRIGGER (a force-ratio spike), deliberately NOT a competing order stream — so it composes
 * with the parallel squad mission-commitment work (a retreat is just another abort input, resolved here).
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws, pure integer comparisons. Two clients over the
 * same synced state decide identically.
 *
 * BYTE-IDENTITY: the consumer never calls this unless its @experimental force-preservation flags are on, and
 * ShouldRetreat folds in the master gate so flag-off is legacy (never retreats) by construction.
 *
 * v3-portable: engine-free static math (NUnit-pinned in CombatRetreatTest); only the field-reading plumbing
 * that feeds it own/enemy strengths is engine-specific.
 */
#endregion

using System;

namespace OpenRA.Mods.Common.Traits
{
	public enum RetreatDecision { Engaged, Retreating }

	public static class CombatRetreatMath
	{
		/// <summary>Is the axis LOSING beyond the retreat ratio — believed local enemy force at least
		/// <paramref name="retreatRatioPct"/>% of own remaining force? 200 ⇒ the enemy is believed twice our
		/// force. A wiped axis (<paramref name="ownStrength"/> &lt;= 0) with any believed enemy present counts
		/// as losing; no believed enemy ⇒ never losing. Pure integer (widened to avoid overflow), zero RNG.</summary>
		public static bool LosingBeyond(int ownStrength, int enemyStrength, int retreatRatioPct)
		{
			if (enemyStrength <= 0)
				return false;
			if (ownStrength <= 0)
				return true;
			return (long)enemyStrength * 100 >= (long)ownStrength * retreatRatioPct;
		}

		/// <summary>Has the axis RECOVERED within the re-engage ratio — believed enemy force at/below
		/// <paramref name="reengageRatioPct"/>% of own? This is the hysteresis EXIT and is a STRICTER bar than
		/// the retreat trigger (callers set reengageRatioPct &lt;= retreatRatioPct), so a retreating squad does
		/// not flip back to the assault the instant the ratio wobbles. No believed enemy ⇒ recovered; a wiped
		/// axis with enemy present ⇒ not recovered. Pure integer, zero RNG.</summary>
		public static bool RecoveredWithin(int ownStrength, int enemyStrength, int reengageRatioPct)
		{
			if (enemyStrength <= 0)
				return true;
			if (ownStrength <= 0)
				return false;
			return (long)enemyStrength * 100 <= (long)ownStrength * reengageRatioPct;
		}

		/// <summary>One step of the retreat FSM. Pure, deterministic, zero RNG.
		///   * From <see cref="RetreatDecision.Engaged"/>: increment the losing streak while
		///     <see cref="LosingBeyond"/>; once the streak reaches <paramref name="sustainWindow"/> commit to
		///     <see cref="RetreatDecision.Retreating"/>. A non-losing eval resets the streak to 0.
		///   * From <see cref="RetreatDecision.Retreating"/>: HOLD the retreat (ignoring the per-eval trigger)
		///     until <paramref name="reachedSafety"/> OR <see cref="RecoveredWithin"/> the stricter re-engage
		///     margin — then return to Engaged. This is the no-oscillation hysteresis.
		/// The returned streak is always 0 in the Retreating state and while not losing, so it only ever counts
		/// a genuine unbroken losing run.</summary>
		public static (RetreatDecision Decision, int Streak) Step(
			RetreatDecision current, int streak,
			int ownStrength, int enemyStrength,
			int retreatRatioPct, int reengageRatioPct,
			bool reachedSafety, int sustainWindow)
		{
			if (current == RetreatDecision.Retreating)
			{
				if (reachedSafety || RecoveredWithin(ownStrength, enemyStrength, reengageRatioPct))
					return (RetreatDecision.Engaged, 0);
				return (RetreatDecision.Retreating, 0);
			}

			if (LosingBeyond(ownStrength, enemyStrength, retreatRatioPct))
			{
				var next = streak + 1;
				if (next >= Math.Max(1, sustainWindow))
					return (RetreatDecision.Retreating, 0);
				return (RetreatDecision.Engaged, next);
			}

			return (RetreatDecision.Engaged, 0);
		}

		/// <summary>Caller convenience folding in the master gate: an axis retreats iff the lever is
		/// <paramref name="enabled"/> AND its FSM state is Retreating. Makes the flag-off == legacy property
		/// explicit (disabled ⇒ false regardless of state ⇒ the assault path is taken, byte-identical).</summary>
		public static bool ShouldRetreat(bool enabled, RetreatDecision decision)
			=> enabled && decision == RetreatDecision.Retreating;

		/// <summary>Lever composition (N3): must a mission-commitment HELD axis be RELEASED into the live set so
		/// the retreat FSM can step it? A held axis skips the FSM entirely, so mission-commitment's hold would
		/// freeze a losing squad in place — the retreat could only fire after the attrition it exists to prevent.
		/// Release iff the retreat lever is <paramref name="retreatEnabled"/> AND the axis is EITHER already
		/// <see cref="RetreatDecision.Retreating"/> (keep it released until its FSM transitions back to Engaged at
		/// safety/recovery) OR its CURRENT force ratio reads <see cref="LosingBeyond"/> the trigger (catch an axis
		/// that STARTS losing while held — its frozen FSM state is still Engaged, so the fresh read is what trips
		/// the release). <paramref name="retreatEnabled"/> false ⇒ false regardless, so byte-identity holds when
		/// the retreat lever is off (mission-commitment's hold is unchanged). Pure, zero RNG.</summary>
		public static bool ShouldReleaseHeld(bool retreatEnabled, RetreatDecision current,
			int ownStrength, int enemyStrength, int retreatRatioPct)
			=> retreatEnabled
				&& (current == RetreatDecision.Retreating
					|| LosingBeyond(ownStrength, enemyStrength, retreatRatioPct));
	}
}
