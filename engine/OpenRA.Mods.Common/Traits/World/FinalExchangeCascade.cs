#region Copyright & License Information
/*
 * WW3MOD addition. Not upstream OpenRA.
 */
#endregion

/*
 * ONE IMPACT CASCADE, as a state machine with no World in it.
 *
 * ==== THE DEFECT THIS EXISTS TO CLOSE ====
 * Before this, every warhead in the final exchange flew its own schedule and they had nothing to do
 * with each other. Dead Hand's salvo was built with LeadInTicks 30 plus a 44-tick flight, so its
 * first impact landed at open+74; a Sarmat ordered on the FIRST tick of the window carried
 * MissileDelay 500 plus a ~92-tick flight and landed at order+592. The machine's warheads therefore
 * landed roughly eight seconds before the player's own, every time, on every map -- which is what
 * the user watched happen and reported. Nothing was wrong with either number; they were simply two
 * unrelated clocks, and the ending is one event.
 *
 * ==== THE RULE ====
 * The exchange has ONE anchor tick. The first warhead of the exchange lands on it, and every
 * warhead after that lands `spacing` ticks behind the one before, in the order the warheads were
 * launched. The launcher then works backwards: a missile with a known flight time launches at
 * `scheduled - flight`, or NOW if that is already in the past.
 *
 * ==== SLOTS ARE SEQUENTIAL, NOT INTERLEAVED BY SIDE, AND THAT WAS A CHOICE ====
 * The alternative on the table was parity -- the trigger takes the even slots, the responder the
 * odd ones -- so the two packages would arrive shuffled together. Three arguments against it, in
 * the order they matter:
 *   * IT NEEDS BOTH COUNTS UP FRONT. A responder that never places is auto-fired at the window's
 *     close, which is long after the trigger's slots have been handed out; parity would have to
 *     reserve holes for warheads that may never be ordered, and then decide what to do with the
 *     holes when they are not.
 *   * SEQUENTIAL SLOTS MAKE THE BOUND EXACT. With one slot per warhead in launch order, a
 *     two-side exchange of N each occupies slots 0..2N-1 and the whole cascade provably fits in
 *     [anchor, anchor + (2N-1) * spacing]. FinalExchangeCascadeTest asserts exactly that.
 *   * IT READS AS A STORY RATHER THAN AS A SHUFFLE: their salvo lands, then yours. Interleaving
 *     two packages fired seconds apart makes the arrival order a property of a mapping instead of
 *     a property of who fired first.
 * AimPointInterval is consequently INERT inside the exchange -- it staggers launches, and the
 * launch times are now derived from the impact times rather than the other way round. It still
 * governs every strike outside the exchange, which is every strike in the match but these.
 *
 * ==== THE FLOOR, AND WHY THE ANCHOR IS NOT SIMPLY THE TRIGGER'S FIRST IMPACT ====
 * The design ruled the anchor to be "the trigger's first impact tick" on the release door and
 * "close + flight" on the time-limit door. Taken literally the first of those does not hold: a
 * responder placing on the LAST tick of a 250-tick window cannot land at anchor + a few slots when
 * the anchor was fixed 250 ticks earlier by somebody else's launch -- its own flight has not even
 * started. It would be clamped to "launch now" and arrive outside the cascade, which is the defect
 * again with the sides swapped. So the anchor is floored at `close + FinalExchangeFlightTicks` on
 * BOTH doors: it is still the trigger's first impact whenever that is late enough, and where it is
 * not, the floor is what makes "nobody's warheads have to be launched in the past" true for every
 * placement the window allows. See SetFloor.
 *
 * ==== DETERMINISM ====
 * Integer only, no RNG, no wall clock, no collection enumeration. Reserve is called from the synced
 * order-resolution path, so every client hands out the same slot to the same warhead on the same
 * tick. Holding the state here rather than in DoomsdayStrike is the same split FinalExchangeWindow
 * took, and for the same reason: it is testable without a World.
 */

namespace OpenRA.Mods.Common.Traits
{
	public sealed class FinalExchangeCascade
	{
		readonly int spacingTicks;

		int anchorTick = -1;
		int floorTick = -1;
		int slotsIssued;

		/// <summary>The tick the FIRST warhead of the exchange lands on, or -1 before one is reserved.</summary>
		public int AnchorTick => anchorTick;

		/// <summary>How many warheads have been given a place in the cascade.</summary>
		public int SlotsIssued => slotsIssued;

		/// <summary>The last impact tick reserved so far, or -1 when nothing has been.</summary>
		public int LastImpactTick => anchorTick < 0 ? -1 : anchorTick + ((slotsIssued - 1) * spacingTicks);

		public FinalExchangeCascade(int spacingTicks)
		{
			// A zero or negative spacing would stack the whole exchange on one tick, which is the
			// pre-cascade failure mode in miniature. One tick is the floor.
			this.spacingTicks = spacingTicks < 1 ? 1 : spacingTicks;
		}

		/// <summary>
		/// <para>The earliest tick the cascade may start on. Set once, when the window opens, to
		/// `ClosesTick + FinalExchangeFlightTicks` -- late enough that a warhead ordered on the very
		/// last tick of the window still has its whole flight ahead of it.</para>
		///
		/// <para>It is a FLOOR and not an assignment: on the release door the trigger's own first
		/// impact may legitimately be later still (a long window, a slow bomb), and the cascade then
		/// starts there. Raising the floor after the anchor has been fixed does nothing -- the
		/// warheads already scheduled cannot be moved.</para>
		/// </summary>
		public void SetFloor(int tick)
		{
			if (tick > floorTick)
				floorTick = tick;
		}

		/// <summary>
		/// <para>Take the next place in the cascade for a warhead whose UNCONSTRAINED impact tick --
		/// the one it would reach on its own MissileDelay and flight -- is
		/// <paramref name="naturalImpactTick"/>. Returns the tick it must land on instead.</para>
		///
		/// <para>The first call fixes the anchor at max(natural, floor). Every call after it returns
		/// anchor + slot * spacing, whatever the caller's natural tick was: once the cascade exists,
		/// a warhead's own flight decides when it LAUNCHES and no longer decides when it ARRIVES.</para>
		/// </summary>
		public int Reserve(int naturalImpactTick)
		{
			if (anchorTick < 0)
				anchorTick = naturalImpactTick > floorTick ? naturalImpactTick : floorTick;

			var slot = slotsIssued++;
			return anchorTick + (slot * spacingTicks);
		}

		/// <summary>
		/// <para>Ticks between the arrival <see cref="BallisticMissileFly.EstimateArcTicks"/> predicts
		/// and the tick the warhead is OBSERVED to detonate on. The estimate is not wrong so much as
		/// it is measuring a different thing, and the difference is a fixed pipeline rather than a
		/// proportion of the flight -- so it is a constant here and not a percentage.</para>
		///
		/// <para>WHERE THE THREE TICKS GO, read off the activity:
		///   +1  SpawnActorEffect adds the missile at the end of a tick, so BallisticMissileFly
		///       first runs on the FOLLOWING one.
		///   +2  the termination: the `horizontalProgress >= 1f` test is at the TOP of Tick, so it is
		///       seen the tick after progress completes, and it then QUEUES a CallFunc to do the
		///       Kill -- which runs a tick later again. Explodes fires on that Kill.</para>
		///
		/// <para>THERE WAS A FOURTH TERM AND IT IS NOT A CONSTANT. Run 260920_165621 measured this
		/// and split the two nations: America's warheads needed 4 ticks and Russia's 3. The
		/// difference is <see cref="ArcCeilingTicks"/> -- EstimateArcTicks' flat branch truncates
		/// where the activity ceilings, so it under-reports by one whenever hDist does not divide
		/// exactly by the missile's speed, and by NOTHING when it does. It is a property of one
		/// salvo's standoff, not of the pipeline, so it is computed per missile and added by the
		/// caller.</para>
		/// </summary>
		// NOT FOLDED INTO EstimateArcTicks, deliberately, and this is the byte-identity argument the
		// whole cascade has been built on: that method is read by every missile power in the mod for
		// its camera and beacon timings, and correcting its truncation there would shift all of them
		// by a tick. What is wrong is not the estimate, it is that the EXCHANGE needs a warhead to
		// land on an exact tick and nothing else does. So the correction lives here, on the one path
		// that has that requirement.
		public const int DetonationPipelineTicks = 3;

		/// <summary>
		/// <para>The tick <see cref="BallisticMissileFly.EstimateArcTicks"/> loses to integer
		/// division, or zero when it loses none.</para>
		///
		/// <para>The flat branch (Acceleration 0, which every nuclear delivery body in the mod uses)
		/// returns `hDist / speed`, a FLOOR. The activity advances `horizontalProgress += speed /
		/// hDist` in float and terminates at `>= 1f`, which is the CEILING. They agree only when the
		/// division is exact -- and whether it is exact is a property of THIS salvo's standoff,
		/// which is the map diagonal plus ApproachMargin and therefore different on every map and
		/// for every aim point.</para>
		///
		/// <para>MEASURED, NOT ASSUMED. Run 260920_165621 landed America's four warheads a tick
		/// later than Russia's four relative to the same reserved slots, on the same map, with the
		/// same missile body and speed -- the whole of the difference being that one salvo's hDist
		/// divided exactly and the other's did not.</para>
		///
		/// <para>THE ACCELERATING BRANCH NEEDS NOTHING: it integrates tick by tick until the
		/// distance is covered, which already counts the partial tick.</para>
		/// </summary>
		public static int ArcCeilingTicks(int hDist, int speed, int acceleration)
		{
			if (acceleration > 0 || speed <= 0 || hDist <= 0)
				return 0;

			// EstimateArcTicks clamps its floor to a minimum of 1, so a flight shorter than one
			// tick's travel is already rounded UP and must not be rounded again.
			return hDist / speed >= 1 && hDist % speed != 0 ? 1 : 0;
		}

		/// <summary>
		/// The launch delay that lands a warhead on <paramref name="impactTick"/> given a flight of
		/// <paramref name="flightTicks"/> from <paramref name="now"/>. Never negative: a warhead that
		/// is already late launches immediately and arrives when it arrives, which is strictly better
		/// than not launching it.
		/// </summary>
		public static int LaunchDelayFor(int now, int impactTick, int flightTicks, int pipelineTicks = DetonationPipelineTicks)
		{
			// THE PIPELINE COMES OUT OF THE WAIT. Solving backwards from a reserved slot means the
			// launch has to be that much EARLIER, not the arrival later -- the arrival is the fixed
			// point. See DetonationPipelineTicks, and ArcCeilingTicks for the part of it the caller
			// has to work out per missile.
			var delay = impactTick - now - flightTicks - pipelineTicks;
			return delay > 0 ? delay : 0;
		}
	}
}
