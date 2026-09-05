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
 * THE ADMIT LEVEL AND THE RELEASE LEVEL MUST BE THE SAME NUMBER, AND THE RELEASE MUST READ ONLY RESPONSIVE
 * TERMS. The first version of this fix got both wrong in the same place: it gated selection at the ENTRY
 * threshold (60) while releasing at ReleaseLevel (45) via a shared helper that ORed the destination reading. A
 * destination in [45, 59] then passed the gate AND made the release permanently true no matter where the truck
 * drove — the original bug at full amplitude, one threshold lower. Selection is therefore gated at
 * ReleaseLevel, so no gap is left between "will go here" and "will leave here", and EvacuateWithDwell's
 * release reads dangerAtTruck alone. See the RESPONSIVE-TERMS INVARIANT on that method.
 *
 * WHAT THE HYSTERESIS DOES NOT DO. Do not read the 45/60 gap as the thing that stops the oscillating — it is
 * not, and the earlier framing of it as a Schmitt deadband overclaimed. The danger field steps by tens to
 * hundreds per cell near a believed contact, so 15 units is spatially SUB-CELL: the truck crosses the entire
 * band in one step and never dwells inside it. The stabilisation is entirely temporal and comes from the two
 * memory mechanisms — the dwell (the branch cannot be re-decided mid-leg) and the caller's leg model (the
 * retreat is not re-issued until it has been driven). The hysteresis' real job is choosing WHICH geometric
 * contour of the contact envelope counts as the edge, which matters for delivery margin, not for settling.
 *
 * ASYMMETRY (load-bearing safety property, stated so it cannot silently regress): the damper only ever DELAYS
 * THE RETURN TO FOLLOWING. ENTERING an evac is never delayed — EvacuateWithDwell tests the entry threshold
 * FIRST and returns true immediately, whatever the dwell counter holds. So a truck standing in fire always
 * pulls back on the very scan that sees the fire, and the damper cannot turn a withdrawal into a last stand.
 * This mirrors RetreatDamperMath's guard for the infantry retreat FSM, which cures the same failure mode.
 *
 *   (5) COMMITMENT ON THE FOLLOW PATH — KeepHeldCluster: the need-margin deadband that stops a truck being
 *       re-pointed at the other cluster every scan, plus AssignSectors' `held` seed, which is the same rule
 *       applied through the spread. Added 2026-09-05 for item 56; OFF at a margin of 0.
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws. AssignSectors iterates trucks in the given
 * (caller-sorted, stable) order and sectors in index order, choosing on strict merit (unserved over served,
 * then Need desc, distance asc, sector-index asc) so two clients over the same synced state pick the same
 * assignment; the `held` seed adds one earlier pass over the same order and reads nothing the greedy writes.
 * The geometry is integer WPos/WVec math with a long intermediate so the scale never overflows.
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
		/// Returns assignment[t] = sector index or <see cref="NoSector"/>.
		///
		/// <para><paramref name="held"/> SEEDS THE ASSIGNMENT WITH SECTORS TRUCKS ARE ALREADY SERVING, and it
		/// is the spread path's half of the per-truck cluster stickiness (see <see cref="KeepHeldCluster"/>).
		/// A truck with <c>held[t] != NoSector</c> keeps that sector outright and does not enter the greedy
		/// pass at all. Null / absent ⇒ the pure greedy this method shipped with, unchanged.
		///
		/// <para>APPLYING THE SEED CANNOT BE FOLDED INTO THE GREEDY LOOP, and the reason is the dedup. Every
		/// held sector has to be marked served BEFORE any truck picks, or a truck earlier in the order would
		/// claim a sector a later truck is already driving to and the distinct-cluster property — the entire
		/// point of the spread — would hold only for trucks that happened to be re-picked first. So this is
		/// two passes over the same caller-sorted order: stamp every seed, then run the greedy for the rest.
		/// Determinism is unaffected; both passes are index-ordered and neither reads the other's order.
		///
		/// <para>THE CALLER IS RESPONSIBLE FOR THE SEED BEING LEGAL. This method does not re-test a held
		/// sector's distance against <paramref name="maxFollowLength"/>, does not check the margin, and will
		/// honour a duplicate seed by letting the later truck double up — a seed is an instruction, not a
		/// candidate. Every one of those tests already exists engine-side, where the cluster identity and the
		/// per-cluster leash live; re-deriving weaker copies of them here would make it ambiguous which one
		/// was authoritative.</para></summary>
		public static int[] AssignSectors(IReadOnlyList<WPos> truckPositions, IReadOnlyList<Sector> sectors, int maxFollowLength,
			IReadOnlyList<int> held = null)
		{
			var count = truckPositions.Count;
			var assignment = new int[count];
			var served = new bool[sectors.Count];
			var maxSq = (long)maxFollowLength * maxFollowLength;

			// PASS 1 — stamp the held sectors, so the greedy below sees them as already served.
			if (held != null)
			{
				for (var t = 0; t < count; t++)
				{
					var seed = t < held.Count ? held[t] : NoSector;
					assignment[t] = seed;
					if (seed >= 0 && seed < sectors.Count)
						served[seed] = true;
					else
						assignment[t] = NoSector;
				}
			}

			// PASS 2 — the greedy, for every truck that is not holding one.
			for (var t = 0; t < count; t++)
			{
				if (held != null && assignment[t] != NoSector)
					continue;

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
		///
		/// <para>NOTE THE POLARITY, which the caller must preserve: the parameter asks whether the reading is
		/// KNOWN GOOD, not whether it is known bad. This function cannot enforce the bound — the caller still
		/// supplies the bool — so the next best thing is that forgetting to supply it fails SAFE. A caller
		/// tracking "was relieved" would default a newly-added selection path to trusted and silently restore
		/// the latch, which is how this defect got through twice; a caller tracking "was gated" defaults it to
		/// ignored, costing at most some sensitivity. The flag must be set where the gate is applied and
		/// nowhere else, so it can never claim more than the gate established.</para>
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

		/// <summary>Should the caller re-issue the plain follow Move, given the truck may already be driving to
		/// a follow cell from an earlier scan? The follow destination is a MOVING cluster centroid, so it
		/// differs by a cell or two every scan by construction, and the Move is non-queued — re-issuing cancels
		/// the drive, discards the path and restarts it, which is the visible stutter. This is the same
		/// deadband the Stage-E detour waypoint already carries one branch over, applied to the branch that
		/// never got one.
		///
		/// <para>TWO TERMS, BOTH COPIED IN SHAPE FROM THIS MODULE rather than newly invented — the codebase
		/// already carries ~28 independently-reimplemented dampers and the shapes disagreeing with each other
		/// is itself part of the defect. The distance test is <c>lastVia</c>'s
		/// (<c>LengthSquared &gt;= threshold²</c>, the same <c>RepathThresholdCells</c> field, no new knob);
		/// the liveness test is <see cref="SupplyDropMath.ErrandStillRunning"/>, CALLED rather than re-derived
		/// so there is exactly one definition of "is my errand still running" in the supply subsystem.</para>
		///
		/// <para>THE IDLE TERM IS NOT OPTIONAL — WITHOUT IT THIS IS A LATCH, NOT A DEADBAND. It is the
		/// responsive term: re-issuing makes the truck non-idle, so the suppression switches itself off, and a
		/// truck that arrived (or whose Move died on a blocked cell or an absent path) reads idle and is
		/// re-ordered rather than parked forever on a stale record. That is the exact defect
		/// <see cref="SupplyDropMath.ErrandStillRunning"/> was written to fix on the drop path, and it applies
		/// here for the same reason. Note the whole predicate is reachable ONLY by a truck that is NOT
		/// evacuating — the evac branch issues its own retreat and returns before the follow branch — so
		/// damping here can never delay a withdrawal, which is this file header's safety invariant.</para>
		///
		/// <para><paramref name="thresholdCells"/> &lt;= 0 ⇒ always re-issue, the undamped per-scan behaviour,
		/// so the deadband can be turned back off to a known baseline.</para>
		/// Pure integer, zero RNG.</summary>
		public static bool ShouldReissueFollow(bool dispatched, bool idle, int prevX, int prevY, int cellX, int cellY,
			int thresholdCells)
		{
			if (!SupplyDropMath.ErrandStillRunning(dispatched, idle))
				return true;

			if (thresholdCells <= 0)
				return true;

			var dx = cellX - prevX;
			var dy = cellY - prevY;
			return dx * dx + dy * dy >= thresholdCells * thresholdCells;
		}

		/// <summary>Should a truck KEEP serving the cluster it is already driving to, rather than take the
		/// best cluster this scan offers? The held cluster survives unless a challenger beats it on need by
		/// more than <paramref name="needMargin"/>.
		///
		/// <para>WHY THE FOLLOW PATH NEEDS THIS AT ALL. The cluster list is rebuilt from scratch every scan
		/// and the per-truck pick is re-derived from live AmmoNeed with no memory of the previous answer —
		/// via <see cref="AssignSectors"/> under SectorSpread, or the need-descending pick without it. Need is
		/// a live quantity that moves as men shoot, so two clusters sitting close together in need can swap
		/// places between two consecutive scans with no danger term, no enemy and no event involved. The
		/// follow Move is NON-QUEUED, so the re-issue cancels the drive already in progress and the truck
		/// turns around — every scan, indefinitely. That is a truck that never arrives, and it is the whole
		/// of the user's "going back and forth, not committing" with nothing exotic in it.
		///
		/// <para>THE MARGIN IS THE POINT, NOT THE MEMORY. A bare "keep what you have" would be a LATCH: a
		/// cluster that got fed, or a genuinely desperate front elsewhere, could never take the truck off its
		/// held customer. The margin makes the hold a DEADBAND instead — ordinary consumption noise cannot
		/// move the truck, a materially needier cluster still can. It is the same instrument, one layer up,
		/// as DropAnchorHysteresisCells on the drop anchor, and it is expressed in the caller's own need
		/// units so both sides of the comparison are the quantity the pick already ranks on.
		///
		/// <para>THE BOUNDARY IS INCLUSIVE ON THE CHALLENGER'S SIDE: a challenger exactly
		/// <paramref name="needMargin"/> ahead WINS. Stated because the other spelling makes the margin
		/// mean "strictly more than", and a config value chosen as "the amount of noise to ignore" then
		/// ignores one point more than it says.
		///
		/// <para><paramref name="needMargin"/> &lt;= 0 ⇒ never keep, i.e. the undamped per-scan re-pick this
		/// module shipped with, so the deadband can be turned back off to a known baseline. That is also the
		/// engine default, which is what keeps a profile that does not set the key byte-identical.
		///
		/// <para>The CALLER owns both of the other release conditions — the held cluster no longer being in
		/// this scan's list, and it having fallen outside that truck's follow leash — because both are
		/// engine-side lookups. Deliberately NOT passed in as bools: a predicate that took them would look
		/// like it enforced them, and the caller would still be the only thing that could.</para>
		/// Pure integer, zero RNG.</summary>
		public static bool KeepHeldCluster(int heldNeed, int bestChallengerNeed, int needMargin)
		{
			if (needMargin <= 0)
				return false;

			return bestChallengerNeed < heldNeed + needMargin;
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
