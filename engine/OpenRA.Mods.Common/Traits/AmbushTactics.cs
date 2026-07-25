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
	}
}
