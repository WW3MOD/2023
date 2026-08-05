#region Copyright & License Information
/*
 * WW3MOD SR flow shape (@experimental) — fresh-reinforcement commit doctrine (pure math).
 *
 * PERCEIVED BEHAVIOUR: a unit called in through the Supply Route stops assembling. Under the shipped
 * "forward-assemble" shape a newly-formed axis standing in the SR bubble was ordered to the forward muster
 * point and WAITED there until the force the allocator had promised it finished walking up (capped at
 * MaxAdvanceHoldEvals evals), then advanced as a body. The user's SR-flow-shape decision (2026-08-05) takes
 * the other arm of that fork: advance immediately, singly. Each reinforcement now commits straight to the
 * objective of the axis it was recruited into, the moment it is recruited — maximally responsive, at the
 * cost of arriving piecemeal into contact.
 *
 * WHAT THIS GATE SUPPRESSES — exactly one thing: arm (b) of RetreatDamperMath.ShouldHold, the FILL-COMPLETION
 * massing hold. That is the only gate in the module where a unit that ALREADY HAS a demand point defers going
 * to it in order to gather with others, so it is the whole of the "assembly wait" in the reinforcement flow.
 *
 * WHAT IT DELIBERATELY LEAVES ALONE (each is a different discipline, not reinforcement assembly):
 *   * arm (a) POST-RETREAT DWELL (readvanceHold > 0) — retreat-oscillation damping for a unit set that has
 *     already fought and withdrawn. Suppressing it would resurrect the advance/lose/retreat ping-pong the
 *     damper exists to remove, and a retreated axis is not a fresh reinforcement. Hence the gate is
 *     CONJUNCTIVE on readvanceHold <= 0: it fires only where the massing arm is the hold that would apply.
 *   * SectorPostureHold — a forward axis declining to press into believed enemy strength in its own contact
 *     sector. A posture decision at the line, not a wait at the muster.
 *   * The free-pool forward stager — the disposition of units the allocator funded NO axis for. They hold no
 *     demand point to be committed to, so there is nothing to advance them at; bypassing their stage would
 *     strand them idling on the SR road, which is the original pooling symptom rather than a cure for it.
 *   * Transport-fill waits — a unit waiting to board/fill a transport is loading, not pooling (the fork
 *     record calls these legitimate under either arm).
 *
 * The muster MACHINERY is therefore untouched and still consumed by all three survivors above; only the
 * reinforcement flow's wait on it is removed.
 *
 * BYTE-IDENTITY: PoiOffensiveBotModule is instantiated as SEPARATE per-profile twins (@experimental /
 * @stable, each behind its own RequiresCondition), not as one shared enable-ai-any instance — so unlike
 * CommitOnOrderMath.ShouldCommitShared / SupplyTruckHuntMath.ShouldHunt this gate needs no BotType
 * confinement: the @stable twin omits the field, reads the C# default false, and never suppresses anything.
 * The flag is the whole enablement, and reverting the YAML line alone restores the forward-assemble shape.
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws, pure boolean/integer comparison. Two clients
 * over the same synced state decide identically.
 *
 * v3-portable: engine-free static math (NUnit-pinned in SpawnFlowMathTest); only the consumer
 * (PoiOffensiveBotModule.DamperShouldHold) is engine-specific.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	public static class SpawnFlowMath
	{
		/// <summary>Does the immediate-commit doctrine suppress the damper's FILL-COMPLETION massing hold for
		/// this axis this eval — i.e. should a still-filling axis commit straight to its objective instead of
		/// waiting at the forward muster for the rest of its allocation?
		///
		/// <para>Conjunctive, and the second conjunct is what keeps the blast radius to the reinforcement flow:
		/// <paramref name="readvanceHold"/> &gt; 0 means the hold that <see cref="RetreatDamperMath.ShouldHold"/>
		/// would return is the POST-RETREAT DWELL (arm a), which this doctrine does not touch — so the gate
		/// declines and the caller consults the damper unchanged. Only when no dwell is armed is the remaining
		/// reachable hold the massing arm (b), and only then is it suppressed.</para>
		///
		/// <para>A Retreating axis needs no special case here: ShouldHold already returns false for it
		/// unconditionally, so suppressing or not suppressing yields the same answer and the damper's
		/// load-bearing "never delays a genuine withdrawal" property is unaffected either way.</para>
		///
		/// <paramref name="immediateCommitEnabled"/> false (the C# default, and what the @stable twin reads)
		/// ⇒ always false ⇒ byte-identical. Pure, zero RNG.</summary>
		public static bool SuppressMassingHold(bool immediateCommitEnabled, int readvanceHold)
			=> immediateCommitEnabled && readvanceHold <= 0;
	}
}
