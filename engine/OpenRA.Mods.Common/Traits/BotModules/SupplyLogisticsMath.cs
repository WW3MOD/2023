#region Copyright & License Information
/*
 * WW3MOD supply-truck logistics (@experimental) — sector assignment + evac geometry (pure math).
 *
 * PERCEIVED BEHAVIOUR: supply trucks no longer all pile onto the single biggest blob. When several trucks
 * are free they SPREAD — each claims a DISTINCT needy cluster (neediest first), so small squads in other
 * sectors get served too; a truck only doubles up on an already-served cluster once trucks outnumber
 * clusters. And a truck whose follow position reads high believed ground danger PULLS BACK toward its
 * Supply Route instead of idling in the fire.
 *
 * This carries the decisions SupplyFollowerBotModule turns into Move orders when the @experimental
 * keys are on:
 *   (1) SECTOR SPREAD — AssignSectors: greedy distinct-cluster assignment over a caller-sorted truck list.
 *   (2) DANGER EVAC level test — ShouldEvacuate: the higher of the truck's / cluster's believed danger vs a
 *       threshold (the danger reads themselves are supplied by the caller and are fog-legal).
 *   (3) DANGER EVAC decision WITH MEMORY — EvacuateWithDwell / StepEvacDwell: the level test above is
 *       memoryless, which made it a guaranteed limit cycle (see the EVAC DAMPER note below).
 *   (4) EVAC GEOMETRY — RetreatTarget: a pull-back point stepped toward the SR, clamped to never overshoot.
 *
 * EVAC DAMPER (2026-08-07) — why (3) exists. As first shipped, the evac branch was chosen every scan by the
 * bare level test (2), and the module oscillated: drive part-way to the front, get ordered back toward the
 * Supply Route, repeat, never delivering. Two properties made that a limit cycle rather than mere jitter.
 *   * The dangerAtCluster term DOES NOT RESPOND TO THE TRUCK MOVING. Retreating lowers dangerAtTruck only, so
 *     while the assigned cluster read hot the truck re-took the retreat on EVERY scan, monotonically, until
 *     the geometry clamped it at the SR. A control loop whose input ignores its own output cannot settle.
 *   * SELECTION AND REJECTION WERE POSITIVELY CORRELATED. The caller picks the NEEDIEST cluster, and the
 *     neediest cluster is the one that has been fighting — i.e. the one deepest in believed danger, which is
 *     exactly what the level test then rejects. The module systematically chose the cluster it was about to
 *     refuse to approach.
 * The fix is in two halves and BOTH are load-bearing. The caller gates SELECTION on the believed danger at the
 * cell the truck would actually be sent to (breaking the correlation — see SupplyFollowerBotModule.BotTick),
 * which leaves dangerAtTruck as the term that actually drives the decision, and dangerAtTruck DOES fall as the
 * truck retreats. That makes the loop closed and therefore settleable. This file supplies the other half: a
 * dwell + release deadband so a retreat already ordered is not re-decided while it is still being driven.
 *
 * THE TWO LEVELS MUST BRACKET A REAL BAND, AND THE RELEASE MUST READ ONLY RESPONSIVE TERMS. The first version
 * of this fix got both wrong in the same place: it gated selection at the ENTRY threshold (60) while releasing
 * at ReleaseLevel (45) via a shared helper that ORed the destination reading. A destination in [45, 59] then
 * passed the gate AND made the release permanently true no matter where the truck drove — the original bug at
 * full amplitude, one threshold lower. Selection is therefore gated at ReleaseLevel, so "will go here" (< 45)
 * and "will leave here" (>= 60) are separated by a genuine band, and EvacuateWithDwell's release reads
 * dangerAtTruck alone. See the RESPONSIVE-TERMS INVARIANT on that method.
 *
 * ASYMMETRY (load-bearing safety property, stated so it cannot silently regress): the damper only ever DELAYS
 * THE RETURN TO FOLLOWING. ENTERING an evac is never delayed — EvacuateWithDwell tests the entry threshold
 * FIRST and returns true immediately, whatever the dwell counter holds. So a truck standing in fire always
 * pulls back on the very scan that sees the fire, and the damper cannot turn a withdrawal into a last stand.
 * This mirrors RetreatDamperMath's guard for the infantry retreat FSM, which cures the same failure mode.
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws. AssignSectors iterates trucks in the given
 * (caller-sorted, stable) order and sectors in index order, choosing on strict merit (unserved over served,
 * then Need desc, distance asc, sector-index asc) so two clients over the same synced state pick the same
 * assignment. The geometry is integer WPos/WVec math with a long intermediate so the scale never overflows.
 *
 * v3-portable: engine-free static math (NUnit-pinned in SupplyLogisticsMathTest); only the tasking plumbing
 * that consumes it (SupplyFollowerBotModule.BotTick) is engine-specific.
 */
#endregion

using System.Collections.Generic;

namespace OpenRA.Mods.Common.Traits
{
	public static class SupplyLogisticsMath
	{
		/// <summary>Assignment sentinel: a truck with no eligible sector this scan.</summary>
		public const int NoSector = -1;

		/// <summary>A candidate resupply cluster for the spread: its centroid and a non-negative "need" score
		/// (higher = needier — the caller scales the ammo-need sum to a stable integer). The array index is the
		/// deterministic tie-break of last resort.</summary>
		public readonly struct Sector
		{
			public readonly WPos Center;
			public readonly int Need;

			public Sector(WPos center, int need)
			{
				Center = center;
				Need = need;
			}
		}

		/// <summary>Greedy DISTINCT-sector assignment. Trucks — in the given, caller-sorted order — each claim
		/// the neediest ELIGIBLE sector no earlier truck has claimed; only when every in-range sector is already
		/// claimed does a truck double up on the best in-range one. Eligibility = within
		/// <paramref name="maxFollowLength"/> of the truck. Selection order: unserved before served (the dedup),
		/// then Need desc, then distance asc, then sector index asc — fully deterministic, no random draws.
		/// Returns assignment[t] = sector index or <see cref="NoSector"/>.</summary>
		public static int[] AssignSectors(IReadOnlyList<WPos> truckPositions, IReadOnlyList<Sector> sectors, int maxFollowLength)
		{
			var count = truckPositions.Count;
			var assignment = new int[count];
			var served = new bool[sectors.Count];
			var maxSq = (long)maxFollowLength * maxFollowLength;

			for (var t = 0; t < count; t++)
			{
				var pos = truckPositions[t];

				var pick = NoSector;
				var pickNeed = 0;
				var pickDistSq = 0L;
				var pickServed = true; // seed "served" so the first unserved candidate always wins over it

				for (var s = 0; s < sectors.Count; s++)
				{
					var distSq = (sectors[s].Center - pos).LengthSquared;
					if (distSq > maxSq)
						continue;

					var need = sectors[s].Need;
					var isServed = served[s];

					// Unserved always beats served (that is the dedup). Within the same served-state, order by
					// Need desc, distance asc, index asc (index asc falls out of only replacing on a STRICT win).
					var better =
						pick == NoSector
						|| (!isServed && pickServed)
						|| (isServed == pickServed && (need > pickNeed || (need == pickNeed && distSq < pickDistSq)));

					if (better)
					{
						pick = s;
						pickNeed = need;
						pickDistSq = distSq;
						pickServed = isServed;
					}
				}

				assignment[t] = pick;
				if (pick != NoSector)
					served[pick] = true;
			}

			return assignment;
		}

		/// <summary>True when a truck should abandon its follow position and pull back: the higher of the
		/// believed ground danger at the truck itself (<paramref name="dangerAtTruck"/>) and at its target
		/// cluster centroid (<paramref name="dangerAtCluster"/>) reaches <paramref name="threshold"/>. Pure —
		/// the caller supplies fog-legal danger reads (DangerFieldLayer only).</summary>
		public static bool ShouldEvacuate(int dangerAtTruck, int dangerAtCluster, int threshold)
		{
			return dangerAtTruck >= threshold || dangerAtCluster >= threshold;
		}

		/// <summary>The believed-danger level at which an ALREADY-evacuating truck is allowed to go back to
		/// following: <paramref name="threshold"/> minus <paramref name="releaseHysteresis"/>. The gap is the
		/// deadband — without it a reading parked on the threshold flips the branch on alternate scans.
		///
		/// <para>Floored at 1 on purpose. A hysteresis at or above the threshold would put the release level at
		/// or below 0, and since a danger read is never negative the truck would then satisfy "still hot" on a
		/// completely cold cell and evacuate forever. Failing to a level of 1 means a genuinely 0-danger cell
		/// always releases, so a misconfiguration costs sensitivity, never a permanently parked truck.</para>
		/// Pure integer, zero RNG.</summary>
		public static int ReleaseLevel(int threshold, int releaseHysteresis)
		{
			var release = threshold - (releaseHysteresis > 0 ? releaseHysteresis : 0);
			return release < 1 ? 1 : release;
		}

		/// <summary>The evac branch decision WITH MEMORY — the anti-oscillation replacement for calling
		/// <see cref="ShouldEvacuate"/> bare every scan. Three steps, in this order:
		///   1. ENTRY IS NEVER DAMPED. If danger at the truck OR at the cell it is being sent to reaches
		///      <paramref name="threshold"/> the answer is true immediately, whatever <paramref name="hold"/>
		///      says. This is the safety property in the file header: a genuine withdrawal is never delayed.
		///   2. A truck that was not evacuating and is not over the entry level keeps following.
		///   3. LEAVING IS DAMPED. An evacuating truck stays evacuating while <paramref name="hold"/> &gt; 0 (the
		///      dwell — the retreat it was already given is still being driven, so the branch is not re-decided),
		///      and once the dwell expires it must fall through <see cref="ReleaseLevel"/> before it follows
		///      again (the deadband).
		///
		/// <para>RESPONSIVE-TERMS INVARIANT — the release test reads <paramref name="dangerAtTruck"/> AND
		/// NOTHING ELSE, and that is load-bearing rather than incidental. A retreat moves the truck, so it can
		/// only change readings taken AT the truck; <paramref name="dangerAtDestination"/> is unaffected by
		/// anything the truck does. Putting a non-responsive term in the release makes the decision unable to
		/// become false — a LATCH, not a deadband — and it latches for every reading in the whole band between
		/// the destination gate and the release level. That is exactly the open-loop defect described in the
		/// EVAC DAMPER note, reintroduced one threshold lower, and it is why the entry and release tests here
		/// deliberately do not share a helper. Any term added to the release must be one the retreat moves.</para>
		/// <paramref name="hold"/> is stepped by <see cref="StepEvacDwell"/> and owned by the caller, matching
		/// RetreatDamperMath's split of predicate from counter. Pure integer, zero RNG.</summary>
		public static bool EvacuateWithDwell(bool wasEvacuating, int hold, int dangerAtTruck, int dangerAtDestination,
			int threshold, int releaseHysteresis)
		{
			if (ShouldEvacuate(dangerAtTruck, dangerAtDestination, threshold))
				return true;

			if (!wasEvacuating)
				return false;

			if (hold > 0)
				return true;

			return dangerAtTruck >= ReleaseLevel(threshold, releaseHysteresis);
		}

		/// <summary>The destination reading <see cref="EvacuateWithDwell"/> is allowed to see — the reading
		/// itself when <paramref name="destinationWasGated"/>, and 0 otherwise. This is the ENFORCEMENT POINT
		/// for the responsive-terms invariant, deliberately a named function rather than an inline test at the
		/// call site, because getting it wrong does not look like a bug at the call site.
		///
		/// <para>The invariant generalises: A TEST THAT CAN PIN THE BRANCH TRUE MAY READ ONLY RESPONSIVE
		/// TERMS, UNLESS ITS NON-RESPONSIVE TERMS ARE BOUNDED BY A GATE APPLIED IN THE SAME SCAN. The entry
		/// test can pin the branch true — it short-circuits ahead of both the dwell and the release, which is
		/// the safety asymmetry working as designed — and a destination reading is not responsive: retreating
		/// changes where the TRUCK is, never what the destination reads. What made the entry test safe was
		/// never the term itself but the caller's selection gate, which guaranteed the reading sat below the
		/// entry threshold. Any caller path that BYPASSES that gate — a relief valve handing back an
		/// over-threshold destination because nothing better exists — removes the bound and restores the
		/// latch: entry true on every scan whatever the truck does, so the truck legs to the SR, drifts out of
		/// follow range, releases, re-selects the same ungated destination and re-enters. Parked at the SR
		/// resupplying nobody, which is the starvation the valve exists to prevent.</para>
		///
		/// <para>Passing 0 for a relieved destination is not a fudge — it is the valve's contract made
		/// explicit. An ungated destination is one the caller has decided to approach ANYWAY because the
		/// alternative is not resupplying at all; the abort criterion for that approach is the truck's own
		/// reading, which the undamped entry test still applies at full strength. So the truck advances until
		/// its OWN cell is genuinely too hot and then pulls back, instead of refusing to set off. Capping the
		/// valve at the entry threshold instead would simply restore park-and-starve for the exact regime the
		/// valve exists for.</para>
		/// Pure, zero RNG.</summary>
		public static int DestinationDanger(bool destinationWasGated, int dangerAtDestination)
		{
			return destinationWasGated ? dangerAtDestination : 0;
		}

		/// <summary>Step the dwell counter <see cref="EvacuateWithDwell"/> reads. Armed to
		/// <paramref name="dwellScans"/> on the scan a truck STARTS evacuating (<paramref name="startedEvacuating"/>
		/// — the caller's <c>evacNow &amp;&amp; !wasEvacuating</c>), counted down otherwise, floored at 0.
		/// <paramref name="dwellScans"/> &lt;= 0 ⇒ 0 (damper inert, the pre-fix memoryless reading).
		///
		/// <para>The counter is armed on the ENTRY EDGE only, which is what bounds the retreat. A counter re-armed
		/// on every evacuating scan would hold the branch for as long as the truck stayed hot and could never
		/// expire; arming on the edge means the dwell covers exactly one retreat leg, after which the truck
		/// re-decides against the release level. The caller additionally uses hold &gt; 0 to SUPPRESS RE-ISSUING
		/// the retreat Move, because the retreat point is recomputed from the truck's own moving position — the
		/// same receding-target restart the Stage-E detour deadband cures one branch over.</para>
		/// Pure integer, zero RNG.</summary>
		public static int StepEvacDwell(int hold, bool startedEvacuating, int dwellScans)
		{
			if (dwellScans <= 0)
				return 0;

			if (startedEvacuating)
				return dwellScans;

			return hold > 0 ? hold - 1 : 0;
		}

		/// <summary>A pull-back point <paramref name="retreatLength"/> toward <paramref name="towards"/> (the
		/// Supply Route / safe rear) from <paramref name="from"/>. Clamped so it never overshoots the
		/// destination. Pure integer vector math with a long intermediate so the scale never overflows on large
		/// maps; the truck's own Z is preserved. Deterministic.</summary>
		public static WPos RetreatTarget(WPos from, WPos towards, int retreatLength)
		{
			var delta = towards - from;
			var dist = delta.HorizontalLength;
			if (dist <= 0 || retreatLength >= dist)
				return towards;

			var x = from.X + (int)((long)delta.X * retreatLength / dist);
			var y = from.Y + (int)((long)delta.Y * retreatLength / dist);
			return new WPos(x, y, from.Z);
		}
	}
}
