#region Copyright & License Information
/*
 * WW3MOD — what an E6 combat engineer should be doing, and whether to re-order him (pure math).
 *
 * Split out of EngineerOperatorBotModule so the employment rules can be tested without standing up a
 * world, mirroring DroneTaskingMath. Every function here is a pure function of its arguments: no
 * world, no RNG, no tick clock.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>
	/// <para>What the engineer is being asked to do this cycle.</para>
	///
	/// <para>THE ORDER OF THESE MEMBERS IS NOT THE PRIORITY ORDER. Priority lives in
	/// <see cref="EngineerTaskingMath.ChooseEmployment"/> and nowhere else, so that re-ordering the enum
	/// for any reason cannot silently re-rank the bot's behaviour.</para>
	/// </summary>
	public enum EngineerEmployment
	{
		/// <summary>No job this cycle — leave the engineer alone (and, when he is dry, leave him IDLE).</summary>
		None,

		/// <summary>Plant C4 on a believed enemy static our own troops are already engaged against.</summary>
		Breach,

		/// <summary>Park near damaged friendly armour so Armament@Repair auto-targets it.</summary>
		Repair,

		/// <summary>Park with the forward friendly group so DetectCloaked@Mine + Armament@ClearMines cover it.</summary>
		Screen
	}

	public static class EngineerTaskingMath
	{
		/// <summary>
		/// <para>Which of the three employments to run this cycle, in a FIXED priority order:
		/// breach, then repair, then screen.</para>
		///
		/// <para>WHY BREACH OUTRANKS REPAIR EVEN THOUGH REPAIR IS THE SAFER JOB. The three employments are
		/// not equally replaceable. A damaged vehicle has other routes back to full health — it can drive
		/// to a logisticscenter (Repairable.RepairActors) under its own power — and the mine screen is a
		/// standing posture that costs nothing to resume. Demolishing a static defence has NO other
		/// provider anywhere in this bot: no module issues the "C4" order, and the charges expire with the
		/// engineer when he dies. So breach is ranked first because it is the only employment whose
		/// opportunity is genuinely lost, not because it is the most valuable in the abstract.</para>
		///
		/// <para><paramref name="canDemolish"/> is the CHARGE-COUNT term and is separate from
		/// <paramref name="hasBreachTarget"/> on purpose: "he has no C4 left" and "there is nothing worth
		/// blowing up" are different states that want the same fallback but different diagnostics, and
		/// collapsing them makes an engineer that has spent all three charges indistinguishable in the log
		/// from one that is looking at an empty map.</para>
		/// </summary>
		public static EngineerEmployment ChooseEmployment(
			bool canDemolish,
			bool hasBreachTarget,
			bool hasRepairWork,
			bool hasScreenAnchor)
		{
			if (canDemolish && hasBreachTarget)
				return EngineerEmployment.Breach;

			if (hasRepairWork)
				return EngineerEmployment.Repair;

			if (hasScreenAnchor)
				return EngineerEmployment.Screen;

			return EngineerEmployment.None;
		}

		/// <summary>
		/// <para>Whether a believed enemy static is a legitimate breach target for THIS engineer.</para>
		///
		/// <para>THE FRESHNESS TERM IS A FOG-LEGALITY GUARD, NOT A TUNING KNOB, AND REMOVING IT CHANGES WHAT
		/// THE BOT IS ALLOWED TO KNOW. The module has to hand the engine a real Actor to build the "C4"
		/// order, because Demolish derives from Enter and Enter only ever enters a TargetType.Actor
		/// (Enter.cs — the "we are next to where we thought the target should be, but it isn't here" branch
		/// is what a frozen or terrain target falls into). Resolving a believed contact's key to an actor
		/// is a lookup that can SUCCEED OR FAIL, and the failure is itself information: it says the
		/// believed structure is already dead. Requiring the sighting to be fresh removes that leak at the
		/// source — a contact seen within the last <paramref name="freshnessTicks"/> is one we are looking
		/// at right now, so the lookup tells us nothing our own eyes have not already told us.</para>
		///
		/// <para>It is NOT a stand-in for "currently visible" for engine reasons: bot players are exempt from
		/// the visibility recalculation entirely (TargetExtensions.Recalculate returns an Actor target
		/// unmodified with targetIsHiddenActor false when viewer.IsBot), so the engine would happily let
		/// this module demolish something it has never seen. Nothing downstream enforces fog legality on a
		/// bot; this predicate is where it is enforced.</para>
		///
		/// <para><paramref name="friendlyNearby"/> is the "an axis is stalled against it" term, expressed as
		/// the thing that can be measured legally: our OWN units standing near the believed static. A
		/// defence with none of our troops near it is not blocking anything yet, and walking a 250-cost
		/// unarmoured specialist to it alone is how the charges get donated.</para>
		/// </summary>
		public static bool IsBreachViable(
			int friendlyNearby,
			int minFriendlyNearby,
			int distanceCells,
			int maxDistanceCells,
			int contactAgeTicks,
			int freshnessTicks)
		{
			if (contactAgeTicks < 0 || contactAgeTicks > freshnessTicks)
				return false;

			if (distanceCells < 0 || distanceCells > maxDistanceCells)
				return false;

			return friendlyNearby >= minFriendlyNearby;
		}

		/// <summary>
		/// <para>Rank two viable breach targets. Higher wins; the caller takes the argmax with a strict
		/// comparison and an ActorID tie-break, so ties never depend on enumeration order.</para>
		///
		/// <para>PRESSURE DOMINATES PROXIMITY, and the scaling is what makes that true rather than a comment
		/// claiming it. Friendly presence is multiplied past the largest distance penalty the caller can
		/// produce, so one extra engaged squad outranks any distance difference inside
		/// <paramref name="maxDistanceCells"/>. The reason is that the cheap-looking target — the nearest
		/// one — is routinely a lone bunker on a flank nobody is fighting over, and blowing it changes
		/// nothing; the target worth three charges is the one a stalled axis is piled up against. Distance
		/// still breaks ties among equally-contested targets, which is all it should decide.</para>
		/// </summary>
		public static long BreachScore(int friendlyNearby, int distanceCells, int maxDistanceCells)
		{
			if (friendlyNearby < 0)
				friendlyNearby = 0;

			if (distanceCells < 0)
				distanceCells = 0;

			// +1 so that maxDistanceCells itself — a legal distance — cannot collide with the next
			// pressure tier. A bare maxDistanceCells multiplier makes friendlyNearby=1 at distance 0 score
			// exactly the same as friendlyNearby=2 at the maximum distance.
			var span = maxDistanceCells < 0 ? 0 : maxDistanceCells;
			return ((long)friendlyNearby * (span + 1)) - distanceCells;
		}

		/// <summary>
		/// <para>Whether to issue a fresh order this cycle, given what this engineer was last told to do.</para>
		///
		/// <para>WHY THIS IS NOT AN IDLE CHECK, WHICH IS THE TRAP THE DRONE MODULE ALREADY PAID FOR ONCE. A
		/// held Attack activity reports non-idle forever when the armament is merely PAUSED rather than
		/// disabled (Attack.cs — ChooseArmamentsForTarget filters IsTraitDisabled only), and ^E6 carries
		/// three armaments with pause conditions on them. Gating recruitment on Actor.IsIdle therefore
		/// latches false and caps the module at one order per engineer for the rest of the match. The
		/// standing-order latch below asks the question that actually matters — has anything CHANGED about
		/// what he should be doing — and never consults his activity at all.</para>
		///
		/// <para>THE SETTLE WINDOW IS LOAD-BEARING HERE IN A WAY IT IS NOT FOR DRONES. Every order this
		/// module issues is unqueued, and an unqueued order cancels the current activity
		/// (Actor.QueueActivity(false, …)). For a drone that costs a launch; for an engineer mid-Demolish
		/// it destroys a walk that may be most of the way to the target, and the charges are not spent so
		/// the module will simply re-pick the same target and start the walk again — a livelock that looks
		/// exactly like an engineer wandering. So a standing order is held for the full window even when
		/// the target has changed.</para>
		/// </summary>
		public static bool ShouldRetask(
			bool hasStandingOrder,
			bool sameEmployment,
			bool sameTarget,
			int ticksSinceOrder,
			int settleTicks)
		{
			if (!hasStandingOrder)
				return true;

			if (ticksSinceOrder < settleTicks)
				return false;

			return !sameEmployment || !sameTarget;
		}

		/// <summary>
		/// <para>Whether a PARKING employment (repair / screen) should be re-issued because its anchor has
		/// genuinely moved, rather than jittered.</para>
		///
		/// <para>WHAT THIS EXISTS TO STOP. Both parking employments aim at a CENTROID of a live set of units,
		/// which shifts by a cell or two every time one of them takes a step or dies. Re-ordering on every
		/// shift walks the engineer a cell, cancels the repair Armament's auto-acquired target on the way
		/// (an unqueued Move cancels the current activity), and re-acquires on arrival — so a group under
		/// fire, which is precisely the group that needs repairs, is the one whose engineer never
		/// finishes a repair burst. Requiring a real displacement makes him hold his post.</para>
		///
		/// <para>A <paramref name="minShiftCells"/> of 0 makes every shift material, which restores the
		/// thrash; it is the off switch for the damping, not a neutral default.</para>
		/// </summary>
		public static bool AnchorMovedMaterially(int shiftCells, int minShiftCells)
		{
			return shiftCells >= minShiftCells && shiftCells > 0;
		}

		/// <summary>The eight cells adjacent to a serviced actor, in a FIXED canonical order: the four
		/// orthogonal neighbours walked clockwise from north, then the four diagonals likewise. Consumed
		/// only through <see cref="ParkCandidateOffsets"/>, whose stable sort leaves this order intact
		/// between candidates that tie on distance — so the canonical order IS the tiebreak, and it is a
		/// declared one rather than whatever a dictionary walk produced.</summary>
		static readonly CVec[] CanonicalParkOffsets =
		{
			new CVec(0, -1), new CVec(1, 0), new CVec(0, 1), new CVec(-1, 0),
			new CVec(1, -1), new CVec(1, 1), new CVec(-1, 1), new CVec(-1, -1)
		};

		/// <summary>
		/// <para>The eight cells a parking engineer may hold ADJACENT to the actor he is servicing,
		/// ordered nearest-first for an engineer standing at <paramref name="approach"/> cells from that
		/// actor (i.e. <c>engineer.Location - target.Location</c>). Orthogonal neighbours come before
		/// diagonals at equal distance; ties beyond that resolve to
		/// <see cref="CanonicalParkOffsets"/>'s declared order.</para>
		///
		/// <para>WHY AN ADJACENT RING EXISTS AT ALL, and it is the bug this function was extracted to fix.
		/// The repair employment used to park the engineer on the CASUALTY'S OWN CELL: it passed
		/// <c>repair.Location</c> as the anchor and <see cref="BotTerrain.TryNearestStandable"/> handed it
		/// straight back, because that clamp tests TERRAIN ONLY and the ground under a tank is ordinary
		/// ground. The engineer was then ordered to Move into the body of the thing he was sent to repair,
		/// which he cannot enter — a mobile blocker is not <c>BlockedByActor.Immovable</c>, so
		/// <see cref="Mobile.NearestMoveableCell(CPos, int, int)"/> does not relocate it either — and he
		/// stalled short, outside the repair weapon's one-cell reach, for the whole settle window. The
		/// module's own Desc says "PARKED within one cell of the thing he is servicing"; this is the cell
		/// set that sentence describes.</para>
		///
		/// <para>ORTHOGONAL BEFORE DIAGONAL IS RANGE MARGIN, NOT STYLE. `Repair` has Range: 1c0 = 1024
		/// (weapons-other.yaml:369) and range is measured to the target HitShape's EDGE, not its centre.
		/// Against the ^Vehicle rectangle (+/-350 x, +/-500 y, vehicles.yaml:28-32) an orthogonal
		/// neighbour sits 674 from the edge and a diagonal 854 — both inside 1024, so a diagonal is a
		/// legal park and is offered rather than discarded, but the orthogonal cell keeps 350 units of
		/// margin against a footprint this arithmetic has not been re-derived for.</para>
		///
		/// <para>PURE AND ALLOCATION-STABLE: no world, no occupancy, no RNG. Whether a candidate can
		/// actually be entered and stood on is the caller's question, because only the caller has the
		/// mover — see <c>EngineerOperatorBotModule.RepairParkAnchor</c>, which asks
		/// <see cref="Mobile.CanEnterCell"/> and <see cref="Mobile.CanStayInCell"/> in this order and
		/// takes the first cell that answers yes.</para>
		/// </summary>
		public static CVec[] ParkCandidateOffsets(CVec approach)
		{
			var ordered = new CVec[CanonicalParkOffsets.Length];
			var keys = new int[CanonicalParkOffsets.Length];
			var n = 0;

			foreach (var offset in CanonicalParkOffsets)
			{
				var dx = offset.X - approach.X;
				var dy = offset.Y - approach.Y;
				if (dx < 0)
					dx = -dx;

				if (dy < 0)
					dy = -dy;

				var key = dx > dy ? dx : dy;

				// Insertion sort, STRICTLY-GREATER shift so equal keys keep canonical order. Eight
				// elements, so the loop is cheaper than a comparer allocation, and a stable sort is what
				// makes the tiebreak declared rather than incidental.
				var i = n++;
				while (i > 0 && keys[i - 1] > key)
				{
					keys[i] = keys[i - 1];
					ordered[i] = ordered[i - 1];
					i--;
				}

				keys[i] = key;
				ordered[i] = offset;
			}

			return ordered;
		}

		/// <summary>
		/// <para>One axis of an integer centroid: <paramref name="sum"/> of coordinates over
		/// <paramref name="count"/> contributors, rounded to nearest rather than truncated.</para>
		///
		/// <para>ROUNDING RATHER THAN TRUNCATING MATTERS BECAUSE THE RESULT IS COMPARED, NOT JUST USED.
		/// Truncation biases every centroid toward the map origin by up to a cell per axis, and
		/// <see cref="AnchorMovedMaterially"/> then reads that bias as a real displacement whenever the
		/// contributor count changes parity — so a group standing perfectly still would re-task its
		/// engineer every time one member died. Negative coordinates round away from zero by the same
		/// amount so the bias does not flip sign across the origin; map cells are non-negative in practice,
		/// which is exactly why an untested sign convention here would never be noticed.</para>
		///
		/// <para>A <paramref name="count"/> of zero returns 0; the caller must not build a centroid from an
		/// empty set, and gets a harmless value rather than a divide-by-zero if it does.</para>
		/// </summary>
		public static int CentroidAxis(int sum, int count)
		{
			if (count <= 0)
				return 0;

			return sum >= 0
				? (sum + (count / 2)) / count
				: -((-sum + (count / 2)) / count);
		}
	}
}
