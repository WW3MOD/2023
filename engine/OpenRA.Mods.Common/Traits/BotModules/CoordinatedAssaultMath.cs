#region Copyright & License Information
/*
 * WW3MOD coordinated assaults (@experimental) — mass-before-commit + multi-axis synchronized release (pure math).
 *
 * PERCEIVED BEHAVIOUR: two or more pushes land TOGETHER. Today each offensive axis decides to step off in its own
 * CommitAndOrder call, reading only its own state — there is no cross-axis channel anywhere in the module, so two
 * axes converging on the same defender arrive minutes apart and are defeated in detail. With this on, an axis that
 * has reached its start line WAITS (its guns already at their standoff anchors, shelling) until a quorum of the
 * other committed axes is also ready, and then they release on the same evaluation pass.
 *
 * WHAT THIS IS *NOT*, AND THE DECISION THAT BOUNDS IT. This does not restore the forward-assemble SR flow shape.
 * The user's SR-flow-shape decision (2026-08-05, WORKSPACE/AWAITING-USER.md:225) picked "advance immediately,
 * singly" and is implemented as ImmediateReinforcementCommit, which suppresses the fill-completion massing hold at
 * the forward muster — i.e. a FRESH REINFORCEMENT never waits for its allocation to walk up. That decision governs
 * the SR->axis flow and is untouched here: this gate lives at the far end of the approach, at the start line, and
 * asks a different question ("is this axis strong enough for what it believes is in front of it, and are its peers
 * ready?") than the muster gate it must not re-litigate. An operator who wants the assembly shape back reverts the
 * one YAML line the fork record names; this flag is orthogonal and does not bring it back by the side door.
 *
 * WHY THE MASS TEST IS RELATIVE AND NEVER ABSOLUTE. The module already shipped an absolute advance-strength floor
 * (MinAdvanceStrength) and it parked units in the rear for whole matches, because a bar stated in absolute build
 * value can never be cleared by an axis the allocator funds to two or three hulls; Wave B had to re-shape it into
 * a fill-completion test to make it terminate. That failure is not repeated here. Sufficiency is a RATIO against
 * the force this axis BELIEVES is in front of it, so an axis facing nothing believed is sufficient at any size and
 * walks in, and an axis facing a real defence must out-mass it before committing. Both halves are then bounded by
 * one countdown, so no combination of knobs can express a permanent hold.
 *
 * LOAD-BEARING SAFETY PROPERTY: ONE bounded window governs BOTH halves. ShouldHoldForSync releases
 * unconditionally once syncTicksElapsed reaches syncMaxTicks, and a non-positive syncMaxTicks disables the gate
 * outright — so a mis-set knob fails OPEN, toward today's "commit on arrival" behaviour, never toward an axis that
 * stands still forever. This is the same discipline PrepFireMath carries, and it is deliberate: the project's
 * SectorPostureHold incident (bd3abacf) is the standing proof that in this module a coupling which looks like
 * caution reads in play as paralysis. Every edge below resolves toward pressing the attack.
 *
 * QUORUM, NOT UNANIMITY. Waiting for ALL axes makes the slowest axis a single point of failure for the whole army
 * — one axis pinned in a corner would hold every other push to the window expiry, every window, converting the
 * synchronizer into a global stall generator. The bar is therefore a PERCENTAGE of the participating axes, so a
 * straggler is outvoted rather than obeyed; at 100 the caller has explicitly asked for unanimity and still gets
 * the window as its backstop.
 *
 * FACT/DECISION SPLIT: this class is pure and world-free. The consumer (PoiOffensiveBotModule.CommitAndOrder) does
 * the engine-side work — the health-weighted own strength, the FOG-LEGAL believed-enemy sum from the belief store,
 * the per-axis tick stamp, and the count of participating/ready peers — and passes plain integers in. Keeping the
 * predicate pure is what makes it NUnit-pinnable with no game run (influence-stack.md §Invariants).
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws, integer-only comparisons, no world reads. The peer
 * counts are cardinalities of a set, so they are order-independent and need no sort.
 *
 * v3-portable: engine-free static math (NUnit-pinned in CoordinatedAssaultMathTest); only the strength reads, the
 * tick stamp and the peer census that feed it are engine-specific.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	public static class CoordinatedAssaultMath
	{
		/// <summary><para>Is this axis massed enough to commit against what it BELIEVES is in front of it? A ratio
		/// test on the shared cost scale: own health-weighted build value versus the sum of believed armed contact
		/// cost x confidence near the objective. <paramref name="requiredRatioPct"/> 150 means "bring half again
		/// what you think is there".</para>
		///
		/// <para>Deliberately RELATIVE. An absolute floor is the shape that already failed in this module
		/// (MinAdvanceStrength, see the header) because a small funded axis can never clear one. Two consequences
		/// follow and both are intended: an axis that believes it faces NOTHING is sufficient at any size — which
		/// is what lets an unopposed walk-in stay a walk-in rather than becoming a muster — and an axis facing a
		/// belief it can never out-mass is released by the caller's window rather than by this predicate.</para>
		///
		/// <para>A non-positive <paramref name="requiredRatioPct"/> disables the mass test (always sufficient),
		/// matching the fail-open discipline of every other edge. Pure, integer-only, zero RNG. The multiplication
		/// is widened to long because build-value sums over a late-game army times a percentage can exceed int
		/// range on the enemy side.</para></summary>
		public static bool MassSufficient(int ownStrength, int believedEnemyStrength, int requiredRatioPct)
		{
			if (requiredRatioPct <= 0)
				return true;

			if (believedEnemyStrength <= 0)
				return true;

			return (long)ownStrength * 100 >= (long)believedEnemyStrength * requiredRatioPct;
		}

		/// <summary><para>Have enough of the participating axes reached readiness to justify a joint step-off?
		/// A percentage bar rather than unanimity, so one pinned axis cannot hold the army (see the header).
		/// <paramref name="readyAxes"/> is expected to INCLUDE the asking axis — the caller censuses the whole
		/// set — so a lone ready axis among four reads 1/4.</para>
		///
		/// <para>Fails OPEN on a degenerate census (<paramref name="participatingAxes"/> non-positive) and on a
		/// non-positive <paramref name="quorumPct"/>, both of which mean "no synchronization is being asked for".
		/// Pure, integer-only, zero RNG.</para></summary>
		public static bool QuorumMet(int readyAxes, int participatingAxes, int quorumPct)
		{
			if (participatingAxes <= 0)
				return true;

			if (quorumPct <= 0)
				return true;

			return (long)readyAxes * 100 >= (long)participatingAxes * quorumPct;
		}

		/// <summary><para>Should this axis HOLD at its start line instead of assaulting now — either because it is
		/// not yet massed for what it believes it faces (anti-trickle), or because it is massed but its peers are
		/// not yet ready (synchronization)? The two reasons share one bounded window on purpose: an axis cannot be
		/// handed off from one hold to the other and thereby wait twice.</para>
		///
		/// <para>Release edges, in evaluation order, ALL fail-open:</para>
		/// <list type="bullet">
		/// <item><paramref name="enabled"/> false — the C# default, and what the @stable twin reads — never holds,
		/// so the gate is byte-identical when off;</item>
		/// <item>a non-positive <paramref name="syncMaxTicks"/> disables the gate outright;</item>
		/// <item>the window elapsing releases unconditionally, whichever reason was holding — this is the single
		/// property that makes the gate structurally incapable of deadlocking an axis;</item>
		/// <item>an axis that is not yet massed holds (the anti-trickle arm) — bounded by the window above;</item>
		/// <item>fewer than two participating axes means there is nobody to synchronize WITH, so a massed lone
		/// axis presses on rather than waiting out a window for company that cannot arrive;</item>
		/// <item>quorum reached releases the whole set on the same evaluation pass, which is the coordination this
		/// gate exists to produce.</item>
		/// </list>
		///
		/// <para>Note the ordering of the mass arm BEFORE the peer-count arm: an under-massed axis holds even when
		/// it is alone, because trickling one unit into a defended objective is the failure this is named for, and
		/// it holds even when quorum is already met, because a ready quorum is not a reason to send a unit that
		/// cannot fight. Pure, integer-only, zero RNG.</para></summary>
		public static bool ShouldHoldForSync(bool enabled, bool massSufficient, int participatingAxes,
			int readyAxes, int quorumPct, int syncTicksElapsed, int syncMaxTicks)
		{
			if (!enabled)
				return false;

			if (syncMaxTicks <= 0)
				return false;

			if (syncTicksElapsed >= syncMaxTicks)
				return false;

			if (!massSufficient)
				return true;

			if (participatingAxes < 2)
				return false;

			return !QuorumMet(readyAxes, participatingAxes, quorumPct);
		}
	}
}
