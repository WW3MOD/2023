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
 * THE FIX: one record per unit, owned by the PLAYER (not by any module), whose lifetime
 * depends on nothing but the tick clock and whether the unit is still executing something.
 * No module can purge it and no eligibility predicate can reach it. That lifetime property
 * — not the predicate shape — is the whole point.
 *
 * TWO COMPOSED PREDICATES, both applied at ModularBot.QueueOrder (the single funnel):
 *
 *   (a) OWNERSHIP / incumbency. Today, when two modules want the same unit, the winner is
 *       whichever module is declared LATER in ai.yaml — an emergent property of trait
 *       construct order, documented nowhere. Replaced here by: the module that already
 *       holds the unit's commitment keeps it unless the challenger OUTRANKS it.
 *
 *   (b) DWELL. Suppress a DIFFERENT-destination order to a unit whose standing order is
 *       still young and still running. Note this is the INVERSE of a destination-
 *       equivalence dedup, which the census (§7.2) ruled out explicitly: in both top churn
 *       suspects the destinations genuinely DIFFER (forward cell -> carrier -> a different
 *       forward cell), so an equivalence gate passes all three. The churn is decision
 *       instability, not duplicate orders.
 *
 * RANK IS NOT DECORATION, IT IS A CORRECTNESS REQUIREMENT. StancePositioningExecutor
 * stamps a `tacpos:` claim on every @experimental bot-owned combatant it positions
 * (StancePositioningExecutor.cs:643, ClaimTicks 150, re-committed every 30 ticks) and
 * never reads the ledger back. Without a rank, that claim is "foreign" to every bot module,
 * so a naive incumbent-wins rule would suppress EVERY order to EVERY positioned unit and
 * the bot would stop playing. Ambient claims must lose to real tasking; that is what the
 * rank ladder encodes.
 *
 * FAIL-OPEN EVERYWHERE. An unknown objective prefix, an unattributed order, a missing
 * ledger and an unknown order string all ADMIT. The consequence is that table rot degrades
 * to "no suppression", never to a wrong suppression.
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
			new("tacpos:", RankAmbient, "StancePositioningExecutor"),
		};

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

		/// <summary>Rank a challenging module carries, derived from the highest-ranked objective prefix
		/// it owns. A module that writes no objective at all (SupplyFollower, Scout, SquadManager…)
		/// ranks as ordinary tasking — enough to beat an ambient `tacpos:` claim, not enough to poach a
		/// mission.</summary>
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

		readonly int dwellTicks;
		readonly bool ownershipEnabled;
		readonly int pruneIntervalTicks;

		int lastPruneTick = int.MinValue;

		public BotOrderGate(bool ownershipEnabled, int dwellTicks, int pruneIntervalTicks = 250)
		{
			this.ownershipEnabled = ownershipEnabled;
			this.dwellTicks = dwellTicks;
			this.pruneIntervalTicks = Math.Max(1, pruneIntervalTicks);
		}

		/// <summary>Diagnostics: live standing records.</summary>
		public int StandingCount => standing.Count;

		public IReadOnlyList<SuppressionCount> Suppressions => counters;

		/// <summary>Decide, and record. Returns <see cref="BotOrderVerdict.Admitted"/> for everything the
		/// gate does not own, so the caller's only job is to drop the order on a non-Admitted verdict.</summary>
		public BotOrderVerdict Admit(
			string orderString,
			bool queued,
			BotOrderUrgency urgency,
			string moduleTag,
			int currentTick,
			long destinationKey,
			List<BotOrderTarget> targets)
		{
			// A queued order APPENDS (Actor.QueueActivity(true, …)) — it cancels nothing, so it is not a
			// churn source and suppressing it could only strand the leading leg of a two-leg maneuver.
			if (queued)
				return BotOrderVerdict.Admitted;

			var cls = OrderArbitrationMath.Classify(orderString);
			if (cls == BotOrderClass.Passthrough)
				return BotOrderVerdict.Admitted;

			if (cls == BotOrderClass.Cancel)
			{
				if (targets != null)
					foreach (var t in targets)
						standing.Remove(t.ActorId);

				return BotOrderVerdict.Admitted;
			}

			if (targets == null || targets.Count == 0)
				return BotOrderVerdict.Admitted;

			// Reflex — evacuation, retreat, damage response. Bypasses BOTH predicates and still becomes
			// the new standing order, so a Directive aimed elsewhere cannot immediately undo it.
			if (urgency == BotOrderUrgency.Reflex)
			{
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
				if (standing.TryGetValue(t.ActorId, out var s)
					&& OrderArbitrationMath.DwellBlocks(s.Tick, currentTick, dwellTicks, s.DestinationKey != destinationKey, t.Busy))
				{
					Count(moduleTag, BotOrderVerdict.SuppressedDwell);
					return BotOrderVerdict.SuppressedDwell;
				}
			}

			Record(targets, destinationKey, currentTick);
			return BotOrderVerdict.Admitted;
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
