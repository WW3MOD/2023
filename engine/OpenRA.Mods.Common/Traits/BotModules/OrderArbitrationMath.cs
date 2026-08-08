#region Copyright & License Information
/*
 * WW3MOD order arbitration — the bot order funnel's incumbency + dwell gate.
 * Bot-brain Stage 1 (WORKSPACE/plans/260808-bot-brain-staging.md §3).
 *
 * THE DISEASE (WORKSPACE/recon/260808-order-churn-census.md §5.1): "eligibility-coupled
 * amnesia". There are ~28 anti-churn dampers in this codebase and every one of them is
 * private to the module that wrote it and is deliberately purged the moment the unit
 * leaves that module's ELIGIBILITY set — and eligibility is exactly what flickers, since
 * it derives from believed-danger fields, POI visibility, residue verdicts and ledger TTLs
 * on faster clocks. So the dedup memory is destroyed by the same event that triggers the
 * re-issue. Seven independent per-module dedups have failed for this reason; an eighth
 * would too.
 *
 * THE FIX, AND EXACTLY HOW FAR IT REACHES: predicate (b)'s standing record is one record per
 * unit, owned by the PLAYER (not by any module), whose lifetime depends on nothing but the tick
 * clock and whether the unit is still executing something. No module can purge it and no
 * eligibility predicate can reach it. That lifetime property — not the predicate shape — is the
 * point, and it is the half of this gate that actually cures the disease.
 *
 * PREDICATE (a) DOES NOT HAVE THAT PROPERTY, and the claim must not be overstated. It reads
 * GoalGuardLedger, which is still eligibility-coupled in two ways: Commit() with a DIFFERENT
 * objective silently overwrites the incumbent claim (PoiGoalGuard.cs:68-76), and Release() is
 * keyed on the actor rather than on the objective (:100), so e.g.
 * StancePositioningExecutor.ReleaseManagement deletes whichever claim the actor happens to hold.
 * So ownership arbitration remains subject to the same amnesia it is meant to arbitrate around.
 * It fails open, so that costs damping and not correctness — but the cure is (b), not (a).
 *
 * TWO COMPOSED PREDICATES, both applied at ModularBot.QueueOrder (the single funnel):
 *
 *   (a) OWNERSHIP / incumbency. Today, when two modules want the same unit, the winner is
 *       whichever module is declared LATER in ai.yaml — an emergent property of trait
 *       construct order, documented nowhere. Replaced here by: the module that already
 *       holds the unit's commitment keeps it unless the challenger OUTRANKS it.
 *
 *       AUDITED REACH, because the plan's "closes five of the six worst poachers" was wrong.
 *       Predicate (a) only adds anything where the poacher does not ALREADY consult the ledger when
 *       building its pool, which turns on whether its goalGuard FIELD is resolved at all:
 *         - LayeredDefence          — field resolved only under @experimental-only flags
 *                                     (LayeredDefenceBotModule.cs:215), so null on @stable; its
 *                                     IsCommitted read at :400 is inert there   ⇒ CLOSED by (a)
 *         - GarrisonBotModule@defenses — LedgerActive false for a non-experimental bot, and the read
 *                                     at :287 is `!LedgerActive || !IsCommitted` ⇒ CLOSED by (a)
 *         - MountedTransport        — field resolved only under CommitPassengers
 *                                     (MountedTransportBotModule.cs:313)         ⇒ CLOSED by (a)
 *         - HelicopterSquad         — field resolved UNCONDITIONALLY
 *                                     (HelicopterSquadBotModule.cs:496), so it already skips
 *                                     committed units on both profiles            ⇒ redundant
 *         - CaptureCoordinator      — likewise, resolved unconditionally
 *                                     (CaptureCoordinatorBotModule.cs:516-520)    ⇒ redundant
 *         - StancePositioningExecutor — activity layer, never reaches this funnel  ⇒ out of scope
 *       So THREE of six, and only on @stable: on @experimental every flag is on, so (a) closes
 *       nothing new there. The predicate that damps the user's churn is (b).
 *
 *   (b) DWELL. Suppress a DIFFERENT-destination order to a unit whose standing order is
 *       still young and still running. Note this is the INVERSE of a destination-
 *       equivalence dedup, which the census (§7.2) ruled out explicitly: in both top churn
 *       suspects the destinations genuinely DIFFER (forward cell -> carrier -> a different
 *       forward cell), so an equivalence gate passes all three. The churn is decision
 *       instability, not duplicate orders.
 *
 * THE GUARANTEE, STATED PRECISELY: a NON-QUEUED Protected order is never dropped. A QUEUED Tasking
 * order marked Protected IS dropped when the head it continues was suppressed in the same tick — the
 * sequence-binding branch is not damping-aware, and that is deliberate. Exempting a Protected tail
 * from a dropped Recurring head would let the tail execute alone, which is the entire defect the
 * binding exists to prevent. So the exception is the one case where dropping a Protected order is the
 * safe choice, and it is the only one.
 *
 * SUPPRESSION IS OPT-IN PER CALL SITE (BotOrderDamping.Recurring); RECORDING IS NOT. Every tasking
 * order establishes the standing record, so an unmarked flee or withdrawal still PROTECTS its unit
 * from the next Recurring challenger — the narrow suppressible set narrows what can be dropped
 * without narrowing what can be defended. As shipped, FIVE call sites are Recurring and they are
 * the census's named beats that this gate is the right layer for: MountedTransport passenger
 * boarding (50 t) and LayeredDefence line assignment x2 (75 t) — the §4.1 pair that matches the
 * user's report verbatim and is live on both profiles — and PoiOffensive StageFreePool (§4.2).
 *
 * SupplyFollower's two follow Moves (§3.3) are deliberately NOT in that set, though the census
 * called one of them the most undamped site it found. The gate provably cannot suppress a truck
 * order: trucks are single-owner and never ledger-committed, so predicate (a) has no incumbent to
 * find, and SupplyFollower's ScanInterval (150 t) strictly exceeds ReorderDwellTicks (120 t), so two
 * consecutive standing records for one truck are always further apart than the dwell window and
 * predicate (b) cannot fire either. That oscillation is damped in SupplyFollowerBotModule instead,
 * by a distance deadband — the right instrument for a destination that moves by construction, and
 * the one the equivalence-vs-dwell argument above says belongs at the caller rather than here.
 *
 * RANK IS NOT DECORATION, IT IS A CORRECTNESS REQUIREMENT. StancePositioningExecutor
 * stamps a `tacpos:` claim on every @experimental bot-owned combatant it positions
 * (StancePositioningExecutor.cs:643, ClaimTicks 150, re-committed every 30 ticks) and
 * never reads the ledger back. Without a rank, that claim is "foreign" to every bot module,
 * so a naive incumbent-wins rule would suppress EVERY order to EVERY positioned unit and
 * the bot would stop playing. Ambient claims must lose to real tasking; that is what the
 * rank ladder encodes.
 *
 * FAIL-OPEN EVERYWHERE, INCLUDING THE CHALLENGER. An unknown objective prefix, an unattributed
 * order, an unknown order string, a missing ledger AND an unrecognised issuing module all ADMIT.
 * The challenger case matters most for the stages still to come: a module added in Stage 2+ that
 * nobody remembered to add to the table below would otherwise be unable to task any committed
 * unit at all, with no signal whatsoever to its author. Table rot must degrade to "no
 * suppression", never to "this module silently cannot give orders".
 *
 * Integer/string-only, zero RNG, no iteration-order dependence: the only dictionary is
 * keyed by ActorID (a uint) and is read by key; its one iteration (Prune) removes by age,
 * which is order-independent.
 */
#endregion

using System;
using System.Collections.Generic;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>How the funnel treats an order string. A whitelist, deliberately: a NEW order type
	/// defaults to <see cref="Passthrough"/> and therefore cannot silently become suppressible.</summary>
	public enum BotOrderClass
	{
		/// <summary>Not a movement/tasking order (stances, cohesion, production, repair, deploy…).
		/// Never suppressed, never recorded — the gate does not exist for these.</summary>
		Passthrough,

		/// <summary>Cancels the unit's work ("Stop"). Always admitted, and it CLEARS the standing
		/// record: a stopped unit has no standing order, so it must not be dwell-suppressed
		/// afterwards (that would freeze it for a whole dwell window).</summary>
		Cancel,

		/// <summary>Redirects the unit somewhere. Subject to both predicates.</summary>
		Tasking,
	}

	/// <summary>Why the funnel dropped an order (or <see cref="Admitted"/>).</summary>
	public enum BotOrderVerdict
	{
		Admitted,
		SuppressedOwnership,
		SuppressedDwell,

		/// <summary>A queued follow-on leg whose non-queued head was suppressed this tick. See
		/// <see cref="BotOrderGate.Admit"/>: a partly-issued multi-leg maneuver is worse than none of it.</summary>
		SuppressedSequence,
	}

	/// <summary>Per-target state the funnel resolves before asking for a verdict. Deliberately
	/// engine-free (a uint, a string and a bool) so the whole decision — not merely a leaf
	/// predicate — is testable without a World.</summary>
	public readonly struct BotOrderTarget
	{
		/// <summary>Actor.ActorID.</summary>
		public readonly uint ActorId;

		/// <summary>The unit's LIVE (unexpired) commitment objective from GoalGuardLedger, or null
		/// when it holds none / the ledger is absent.</summary>
		public readonly string Objective;

		/// <summary>The unit is executing something (i.e. !IsIdle). A unit that finished or was
		/// interrupted is immediately re-orderable — this is the escape hatch that keeps the dwell
		/// from ever stalling a unit whose errand has ended.</summary>
		public readonly bool Busy;

		public BotOrderTarget(uint actorId, string objective, bool busy)
		{
			ActorId = actorId;
			Objective = objective;
			Busy = busy;
		}
	}

	public static class OrderArbitrationMath
	{
		public const int RankUnknown = -1;

		/// <summary>Idle-filling / cosmetic positioning. Loses to everything real (see the header:
		/// this is what stops `tacpos:` claims from freezing the bot).</summary>
		public const int RankAmbient = 0;

		/// <summary>Ordinary combat tasking: offense, defence, garrison, ambush, transport.</summary>
		public const int RankTasking = 1;

		/// <summary>Scarce-unit missions that are expensive to restart and use near-irreplaceable
		/// actors (TECN capture parties, bridge engineers). Outranks ordinary tasking, so a capture
		/// may recruit an offense unit but nothing may poach a capture escort.</summary>
		public const int RankMission = 2;

		readonly struct OwnerEntry
		{
			public readonly string Prefix;
			public readonly int Rank;
			public readonly string ModuleA;
			public readonly string ModuleB;

			public OwnerEntry(string prefix, int rank, string moduleA, string moduleB = null)
			{
				Prefix = prefix;
				Rank = rank;
				ModuleA = moduleA;
				ModuleB = moduleB;
			}
		}

		// Objective prefix -> owning module type name(s) + rank. The prefixes are the ones the
		// modules' own *ObjectiveKey helpers emit; they are NOT re-read from those modules, so this
		// table can in principle drift from them. That is tolerable precisely because an unmatched
		// prefix returns RankUnknown and the gate then ADMITS: drift costs damping, never
		// correctness. `transport:` legitimately has two owners (MountedTransportBotModule and
		// HelicopterSquadBotModule both emit it) — they arbitrate between themselves with a
		// hand-rolled reservation (MountedTransportBotModule.cs:613-616) and the gate stays out of it.
		static readonly OwnerEntry[] Table =
		{
			new("offense:", RankTasking, "PoiOffensiveBotModule"),
			new("bombard:", RankTasking, "PoiOffensiveBotModule"),
			new("defend:", RankTasking, "PoiGarrisonBotModule"),
			new("defend-line:", RankTasking, "LayeredDefenceBotModule"),
			new("garrison:", RankTasking, "GarrisonBotModule"),
			new("ambush:", RankTasking, "LaneAmbushBotModule"),
			new("transport:", RankTasking, "MountedTransportBotModule", "HelicopterSquadBotModule"),
			new("capture:", RankMission, "CaptureCoordinatorBotModule"),
			new("capture-escort:", RankMission, "CaptureCoordinatorBotModule"),
			new("capture-defend:", RankMission, "CaptureCoordinatorBotModule"),
			new("bridge-repair:", RankMission, "EngineerRouteOpenBotModule"),
			new("bridge-screen:", RankMission, "EngineerRouteOpenBotModule"),
			// StancePositioningExecutor is a per-unit ConditionalTrait, not an IBotTick, so currentModuleTag
			// can never equal this name and the owner column here is documentation only — the row exists
			// purely to RANK the claim below everything real. It also only ever lands on an IDLE unit
			// (CommitManagement is reachable only from the INotifyIdle.TickIdle region), which is precisely
			// why ranking it as tasking would be so damaging: it marks units that are free.
			new("tacpos:", RankAmbient, "StancePositioningExecutor"),
		};

		/// <summary>Is this module tag present in the table at all? A tag that is absent is a module that
		/// never writes an objective (SupplyFollower, Scout, SquadManager…) or one added after this table
		/// was written. Either way it must FAIL OPEN — see the file header.</summary>
		public static bool IsKnownModule(string moduleTag)
		{
			if (string.IsNullOrEmpty(moduleTag))
				return false;

			foreach (var e in Table)
				if (e.ModuleA == moduleTag || (e.ModuleB != null && e.ModuleB == moduleTag))
					return true;

			return false;
		}

		/// <summary>Classify an order string. WHITELIST: only these four strings are suppressible, and
		/// they are exactly the Tier-1 churn sources the census ranked (EnterTransport ~3.0s,
		/// AttackMove ~4.5s/6.0s, Move ~9.0s). Everything else — including "Attack" (target
		/// acquisition, re-evaluated every 5 ticks by the air squad FSM) and every state/production
		/// order — passes through untouched.</summary>
		public static BotOrderClass Classify(string orderString)
		{
			switch (orderString)
			{
				case "Move":
				case "AttackMove":
				case "EnterTransport":
				case "DropSupplyCacheAt":
					return BotOrderClass.Tasking;
				case "Stop":
					return BotOrderClass.Cancel;
				default:
					return BotOrderClass.Passthrough;
			}
		}

		/// <summary>Rank of the objective a live commitment names, or <see cref="RankUnknown"/> for an
		/// absent / unrecognised objective.</summary>
		public static int ObjectiveRank(string objective)
		{
			if (string.IsNullOrEmpty(objective))
				return RankUnknown;

			foreach (var e in Table)
				if (objective.StartsWith(e.Prefix, StringComparison.Ordinal))
					return e.Rank;

			return RankUnknown;
		}

		/// <summary>True when <paramref name="moduleTag"/> is an owner of the objective's prefix, i.e.
		/// the challenger is the incumbent and is merely refreshing its own task.</summary>
		public static bool ObjectiveOwnedBy(string objective, string moduleTag)
		{
			if (string.IsNullOrEmpty(objective) || string.IsNullOrEmpty(moduleTag))
				return false;

			foreach (var e in Table)
				if (objective.StartsWith(e.Prefix, StringComparison.Ordinal))
					return e.ModuleA == moduleTag || (e.ModuleB != null && e.ModuleB == moduleTag);

			return false;
		}

		/// <summary>Rank a challenging module carries, derived from the highest-ranked objective prefix it
		/// owns. Only meaningful for a module the table knows; <see cref="OwnershipBlocks"/> checks
		/// <see cref="IsKnownModule"/> first, so the RankTasking floor here is never used to BLOCK an
		/// unrecognised module.</summary>
		public static int ModuleRank(string moduleTag)
		{
			if (string.IsNullOrEmpty(moduleTag))
				return RankTasking;

			var best = RankUnknown;
			foreach (var e in Table)
				if ((e.ModuleA == moduleTag || (e.ModuleB != null && e.ModuleB == moduleTag)) && e.Rank > best)
					best = e.Rank;

			return best == RankUnknown ? RankTasking : best;
		}

		/// <summary>Predicate (a), per target: does an incumbent commitment block this challenger?
		/// Fails open on no incumbent, an unattributed challenger, an unknown prefix, or the
		/// challenger's own claim. Otherwise the incumbent wins ties — that is the whole point, since
		/// today's tie-break is ai.yaml declaration order.</summary>
		public static bool OwnershipBlocks(string challengerModuleTag, string incumbentObjective)
		{
			if (string.IsNullOrEmpty(incumbentObjective) || string.IsNullOrEmpty(challengerModuleTag))
				return false;

			if (ObjectiveOwnedBy(incumbentObjective, challengerModuleTag))
				return false;

			// FAIL OPEN on a challenger the table does not know. Blocking here would mean a module whose
			// author never touched this file silently cannot task any committed unit, with no diagnostic.
			if (!IsKnownModule(challengerModuleTag))
				return false;

			var incumbentRank = ObjectiveRank(incumbentObjective);
			if (incumbentRank == RankUnknown)
				return false;

			return incumbentRank >= ModuleRank(challengerModuleTag);
		}

		/// <summary>Predicate (b): suppress a redirect of a unit whose standing order is young AND still
		/// running AND aimed somewhere else. Every clause is load-bearing:
		/// <list type="bullet">
		/// <item>same destination ADMITS — this is not an equivalence dedup (census §7.2);</item>
		/// <item>an idle unit ADMITS — a finished or interrupted errand must never hold a unit;</item>
		/// <item>dwellTicks &lt;= 0 ADMITS — the inert shipped default.</item>
		/// </list></summary>
		public static bool DwellBlocks(int standingTick, int currentTick, int dwellTicks, bool destinationDiffers, bool targetBusy)
		{
			if (dwellTicks <= 0 || !destinationDiffers || !targetBusy)
				return false;

			var age = currentTick - standingTick;
			return age >= 0 && age < dwellTicks;
		}

		/// <summary>Collapse an order's destination to one comparable integer. Actor targets and cell
		/// targets live in disjoint ranges so "board carrier 7" never compares equal to "walk to
		/// (7,0)"; an invalid target collapses to 0 and therefore compares equal to itself only.</summary>
		public static long DestinationKey(bool targetIsActor, uint targetActorId, int cellX, int cellY, bool hasTarget)
		{
			if (!hasTarget)
				return 0L;

			if (targetIsActor)
				return (1L << 48) | targetActorId;

			return (2L << 48) | ((long)(ushort)cellX << 16) | (ushort)cellY;
		}
	}

	/// <summary>
	/// The funnel's per-player order gate. Engine-free by construction (uints, strings, bools) so the
	/// COMPOSITION — urgency handling, the whitelist, all-or-nothing over grouped targets, the standing
	/// record and its lifetime — is pinned in NUnit rather than only its leaf predicates.
	///
	/// RECORD LIFETIME, which is the entire point (see the file header): a record is born when an order
	/// is admitted and can only die from (i) the dwell elapsing, (ii) the unit no longer executing
	/// anything, (iii) an explicit Stop, or (iv) an age-based prune. Nothing a module does — no
	/// eligibility set, no roster rebuild, no TTL expiry, no pool exit — can reach it.
	/// </summary>
	public sealed class BotOrderGate
	{
		public readonly struct SuppressionCount
		{
			public readonly string ModuleTag;
			public readonly BotOrderVerdict Verdict;
			public readonly int Count;

			public SuppressionCount(string moduleTag, BotOrderVerdict verdict, int count)
			{
				ModuleTag = moduleTag;
				Verdict = verdict;
				Count = count;
			}
		}

		struct Standing
		{
			public long DestinationKey;
			public int Tick;
		}

		// Keyed by ActorID, so a dead unit's record simply ages out and no Actor reference is retained.
		readonly Dictionary<uint, Standing> standing = new();

		// Insertion-ordered (module tick order is deterministic) so the drained report is reproducible.
		readonly List<SuppressionCount> counters = new();

		// Sequence binding for multi-leg maneuvers (see Admit). Scoped to a single world tick and
		// self-clearing, because the legs of a maneuver are always adjacent statements inside one module
		// tick. Membership tests only — never iterated — so it cannot introduce order dependence.
		readonly HashSet<uint> suppressedThisTick = new();
		int sequenceTick = int.MinValue;

		// A queued order waits at least one tick in ModularBot's queue before World.IssueOrder runs, and
		// longer under a large ceil(N/5) burst, so a unit reads IDLE for the first few ticks after we
		// recorded its standing order — which would hand a competing grab a free pass in exactly the
		// window the fastest churn sources live in. Treat a just-ordered unit as busy until its order has
		// had time to land. Safe against the idle escape hatch: a genuinely finished errand is always far
		// older than this, because no movement completes in five ticks.
		const int ActivationGraceTicks = 5;

		readonly int dwellTicks;
		readonly bool ownershipEnabled;
		readonly int pruneIntervalTicks;

		int lastPruneTick = int.MinValue;

		// pruneIntervalTicks <= 0 derives the sweep period from the dwell, so a record outlives its
		// usefulness by at most one dwell window rather than by a fixed 250 ticks.
		public BotOrderGate(bool ownershipEnabled, int dwellTicks, int pruneIntervalTicks = 0)
		{
			this.ownershipEnabled = ownershipEnabled;
			this.dwellTicks = dwellTicks;
			this.pruneIntervalTicks = pruneIntervalTicks > 0 ? pruneIntervalTicks : Math.Max(60, dwellTicks);
		}

		/// <summary>Diagnostics: live standing records.</summary>
		public int StandingCount => standing.Count;

		public IReadOnlyList<SuppressionCount> Suppressions => counters;

		/// <summary>Decide, and record. Returns <see cref="BotOrderVerdict.Admitted"/> for everything the
		/// gate does not own, so the caller's only job is to drop the order on a non-Admitted verdict.</summary>
		public BotOrderVerdict Admit(
			string orderString,
			bool queued,
			BotOrderDamping damping,
			string moduleTag,
			int currentTick,
			long destinationKey,
			List<BotOrderTarget> targets)
		{
			// CLASSIFY FIRST. This ordering is load-bearing: the previous cut tested `queued` before the
			// whitelist, so a QUEUED PASSTHROUGH order could be dropped by the sequence binding below.
			// That really happened — CaptureCoordinator's on-foot fallback `CaptureActor` (queued, and
			// deliberately outside the whitelist) was dropped because the ferry attempt's EnterTransport
			// for the same capturer had been suppressed in the same tick. An order the gate does not own
			// must be unreachable from every path in it, not merely from the suppression predicates.
			var cls = OrderArbitrationMath.Classify(orderString);
			if (cls == BotOrderClass.Passthrough)
				return BotOrderVerdict.Admitted;

			// A queued order APPENDS (Actor.QueueActivity(true, …)) so it cancels nothing and is not itself
			// a churn source. But it is NOT independent of what came before it. A two-leg maneuver issues
			// the danger-avoiding waypoint non-queued and then CHAINS the direct leg queued. Admitting the
			// tail after dropping the head leaves the direct leg to execute ALONE — i.e. it drives exactly
			// the straight line the detour existed to avoid. A partly-issued plan is worse than no
			// suppression at all, so bind the tail to its head: same tick, same actor, head suppressed ⇒
			// drop the tail. Restricted to Tasking orders, which is what keeps an order the gate does not
			// own out of reach of this branch entirely.
			//
			// WHY THIS IS SOUND, stated exactly rather than by the slogan it used to carry ("a queued
			// tasking order is by construction a continuation"), which is FALSE — there are two
			// counterexamples in the tree. The binding can only fire when a head was suppressed this tick,
			// and only a Recurring order can be suppressed. Of the five queued Tasking sites:
			//   * PoiOffensiveBotModule:3057, SupplyFollowerBotModule:716 and
			//     HelicopterSquadBotModule:1270 each have a Protected NON-QUEUED head for the same actor
			//     in the same tick, and a Protected order calls ClearSequenceSuppressed — so the mark is
			//     always clear by the time the tail arrives. These are genuine continuations.
			//   * McvManagerBotModule:163 queues a Move behind the MCV's EXISTING activity with no
			//     same-tick head at all, and MountedTransportBotModule:428's predecessor is Unload, which
			//     is Passthrough and therefore never clears the mark. Neither is a continuation of
			//     anything this gate saw.
			// Those last two are safe ONLY because their actors are disjoint from every Recurring site's
			// actor set: an MCV fails AttackBaseInfo in IsEligibleCombatUnit and is in none of
			// LayeredDefence's whitelists, and carriers are excluded by name in ExcludedActorTypes and by
			// ExcludeUnitTypes + IsTroopCarrier on both profiles. THAT DISJOINTNESS IS A REAL DEPENDENCY
			// AND NOTHING ENFORCES IT. If a future Recurring mark ever reaches an MCV or a troop carrier,
			// these two queued orders become droppable with no head to justify it — re-check them then.
			//
			// Same-tick is the correct scope and is deliberately INFERRED rather than declared. An
			// atomicity marker on the call site would have to be remembered by every future author of a
			// multi-leg pair — the same failure mode that produced this defect. Structure cannot be
			// forgotten, and a tick boundary needs no lifetime management.
			//
			// As shipped no multi-leg HEAD is marked Recurring, so this has zero live cases today. It is
			// kept as the structural guard that stops the first future Recurring mark on a chain head from
			// re-running that defect.
			if (queued)
			{
				if (targets != null && targets.Count > 0 && SequenceSuppressed(targets, currentTick))
				{
					Count(moduleTag, BotOrderVerdict.SuppressedSequence);
					return BotOrderVerdict.SuppressedSequence;
				}

				return BotOrderVerdict.Admitted;
			}

			if (cls == BotOrderClass.Cancel)
			{
				if (targets != null)
				{
					ClearSequenceSuppressed(targets, currentTick);
					foreach (var t in targets)
						standing.Remove(t.ActorId);
				}

				return BotOrderVerdict.Admitted;
			}

			if (targets == null || targets.Count == 0)
				return BotOrderVerdict.Admitted;

			// SUPPRESSION IS OPT-IN. A Protected order — which is everything the call site did not
			// explicitly mark — is recorded and admitted. Recording it anyway is what keeps the damping
			// broad despite the narrow suppressible set: a flee, a withdrawal or a one-shot delivery still
			// establishes the standing order that PROTECTS its unit from the next Recurring challenger.
			// So the inversion narrows what can be dropped without narrowing what can be defended.
			//
			// Note this is reached only for a NON-QUEUED order: the queued branch above returns before it
			// and is deliberately NOT damping-aware. See the guarantee stated in the file header.
			if (damping != BotOrderDamping.Recurring)
			{
				ClearSequenceSuppressed(targets, currentTick);
				Record(targets, destinationKey, currentTick);
				return BotOrderVerdict.Admitted;
			}

			// (a) Ownership, ALL-OR-NOTHING: Order.Subject/GroupedActors are readonly, so a grouped order
			// cannot be partially dropped. Requiring every target to be blocked is the conservative
			// direction, and near-lossless in practice because the POI modules recruit only from the
			// ledger-checked free pool and their groups are therefore homogeneous in ownership.
			if (ownershipEnabled)
			{
				var allBlocked = true;
				foreach (var t in targets)
				{
					if (!OrderArbitrationMath.OwnershipBlocks(moduleTag, t.Objective))
					{
						allBlocked = false;
						break;
					}
				}

				if (allBlocked)
				{
					MarkSequenceSuppressed(targets, currentTick);
					Count(moduleTag, BotOrderVerdict.SuppressedOwnership);
					return BotOrderVerdict.SuppressedOwnership;
				}
			}

			// (b) Dwell, single-target only. Grouped orders are excluded deliberately: every module that
			// issues one already carries a working same-destination dedup on its aggregate anchor, and the
			// grabs that actually turn a unit around mid-walk name one actor.
			if (dwellTicks > 0 && targets.Count == 1)
			{
				var t = targets[0];
				var stillWorking = t.Busy || IsWithinActivationGrace(t.ActorId, currentTick);
				if (standing.TryGetValue(t.ActorId, out var s)
					&& OrderArbitrationMath.DwellBlocks(s.Tick, currentTick, dwellTicks, s.DestinationKey != destinationKey, stillWorking))
				{
					MarkSequenceSuppressed(targets, currentTick);
					Count(moduleTag, BotOrderVerdict.SuppressedDwell);
					return BotOrderVerdict.SuppressedDwell;
				}
			}

			ClearSequenceSuppressed(targets, currentTick);
			Record(targets, destinationKey, currentTick);
			return BotOrderVerdict.Admitted;
		}

		bool IsWithinActivationGrace(uint actorId, int currentTick)
			=> standing.TryGetValue(actorId, out var s) && currentTick - s.Tick >= 0 && currentTick - s.Tick < ActivationGraceTicks;

		void MarkSequenceSuppressed(List<BotOrderTarget> targets, int currentTick)
		{
			if (sequenceTick != currentTick)
			{
				suppressedThisTick.Clear();
				sequenceTick = currentTick;
			}

			foreach (var t in targets)
				suppressedThisTick.Add(t.ActorId);
		}

		// ANY member, not all: if one member of a group lost its head leg, the group's chained leg is
		// already an incoherent plan for that member.
		bool SequenceSuppressed(List<BotOrderTarget> targets, int currentTick)
		{
			if (sequenceTick != currentTick || suppressedThisTick.Count == 0)
				return false;

			foreach (var t in targets)
				if (suppressedThisTick.Contains(t.ActorId))
					return true;

			return false;
		}

		// A later ADMITTED head in the same tick re-opens the actor for its own chained tail.
		void ClearSequenceSuppressed(List<BotOrderTarget> targets, int currentTick)
		{
			if (sequenceTick != currentTick || suppressedThisTick.Count == 0)
				return;

			foreach (var t in targets)
				suppressedThisTick.Remove(t.ActorId);
		}

		void Record(List<BotOrderTarget> targets, long destinationKey, int currentTick)
		{
			// Nothing reads the record when the dwell is off, so don't keep one — that also makes the
			// both-levers-off gate genuinely stateless rather than merely inert.
			if (dwellTicks <= 0)
				return;

			foreach (var t in targets)
				standing[t.ActorId] = new Standing { DestinationKey = destinationKey, Tick = currentTick };
		}

		void Count(string moduleTag, BotOrderVerdict verdict)
		{
			var tag = moduleTag ?? "";
			for (var i = 0; i < counters.Count; i++)
			{
				if (counters[i].ModuleTag == tag && counters[i].Verdict == verdict)
				{
					counters[i] = new SuppressionCount(tag, verdict, counters[i].Count + 1);
					return;
				}
			}

			counters.Add(new SuppressionCount(tag, verdict, 1));
		}

		public void ResetSuppressions() => counters.Clear();

		/// <summary>Age out records that can no longer suppress anything. Tick-stamped rather than
		/// countdown-decremented, so it is unaffected by how often it is called. Removal is by age only,
		/// hence independent of dictionary iteration order.</summary>
		public void Prune(int currentTick)
		{
			if (lastPruneTick != int.MinValue && currentTick - lastPruneTick < pruneIntervalTicks)
				return;

			lastPruneTick = currentTick;
			if (standing.Count == 0)
				return;

			List<uint> drop = null;
			foreach (var kv in standing)
				if (currentTick - kv.Value.Tick >= dwellTicks)
					(drop ??= new List<uint>()).Add(kv.Key);

			if (drop != null)
				foreach (var k in drop)
					standing.Remove(k);
		}
	}
}
