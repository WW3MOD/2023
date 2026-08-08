#region Copyright & License Information
/*
 * WW3MOD OrderArbitrationMath / BotOrderGate tests — the bot order funnel gate (bot-brain Stage 1).
 *
 * These pin the CONSUMER, not merely the leaf predicates. BotOrderGate is deliberately engine-free
 * so the composition that actually decides — the order-string whitelist, the urgency bypass, the
 * all-or-nothing rule over a grouped order, the standing record and above all its LIFETIME — is
 * pinned here rather than only reachable through a running World.
 *
 * The invariants worth reading before changing anything:
 *   * an ambient `tacpos:` claim NEVER blocks real tasking — without that, every positioned
 *     @experimental unit would be unorderable and the bot would stop playing;
 *   * "Stop" CLEARS the standing record — otherwise a cancel freezes the unit for a whole dwell;
 *   * a Reflex order (retreat / evacuation / damage response) bypasses both predicates AND becomes
 *     the new standing order, so nothing can immediately undo a withdrawal;
 *   * a grouped order is exempt from the dwell but still records EVERY member, which is what stops
 *     a single-actor grab from turning one unit out of a moving group around;
 *   * the standing record is not coupled to the commitment ledger, which is the entire point: every
 *     one of the ~28 existing dampers is purged when a module's eligibility set changes, and
 *     eligibility is exactly what flickers;
 *   * with both levers off the gate is inert (the pre-Stage-1 pass-through).
 */
#endregion

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class OrderArbitrationMathTest
	{
		const int Dwell = 120;

		static long Cell(int x, int y) => OrderArbitrationMath.DestinationKey(false, 0, x, y, true);
		static long ActorTarget(uint id) => OrderArbitrationMath.DestinationKey(true, id, 0, 0, true);

		static List<BotOrderTarget> Targets(params BotOrderTarget[] t) => new(t);
		static BotOrderTarget Unit(uint id, string objective = null, bool busy = true) => new(id, objective, busy);

		static BotOrderGate Gate(bool ownership = true, int dwell = Dwell) => new(ownership, dwell);

		// The exact objective strings the shipped modules emit, copied from their *ObjectiveKey helpers.
		// If a module ever renames its prefix the gate fails OPEN (no suppression), never closed — so
		// this list is the damping coverage, not a correctness dependency.
		static readonly string[] ShippedObjectives =
		{
			"offense:12", "bombard:12", "defend:7", "defend-line:3,4", "garrison:4", "ambush:6",
			"transport:8", "capture:9", "capture-escort:9", "capture-defend:9",
			"bridge-repair:2", "bridge-screen:2", "tacpos:5",
		};

		// ---------- Classify: a whitelist, so a new order type cannot silently become suppressible ----------

		[Test]
		public void Classify_OnlyTheFourCensusChurnSourcesAreSuppressible()
		{
			Assert.That(OrderArbitrationMath.Classify("Move"), Is.EqualTo(BotOrderClass.Tasking));
			Assert.That(OrderArbitrationMath.Classify("AttackMove"), Is.EqualTo(BotOrderClass.Tasking));
			Assert.That(OrderArbitrationMath.Classify("EnterTransport"), Is.EqualTo(BotOrderClass.Tasking));
			Assert.That(OrderArbitrationMath.Classify("DropSupplyCacheAt"), Is.EqualTo(BotOrderClass.Tasking));

			Assert.That(OrderArbitrationMath.Classify("Stop"), Is.EqualTo(BotOrderClass.Cancel));
		}

		[Test]
		public void Classify_EveryOtherShippedBotOrderStringPassesThrough()
		{
			// The full inventory of order strings bot modules and squad states issue. "Attack" is
			// deliberately NOT suppressible (the air squad FSM re-evaluates targets every 5 ticks, and
			// this gate exists for ground repositioning churn); neither is any state/production order.
			var passthrough = new[]
			{
				"Attack", "ReturnToBase", "Unload", "CaptureActor", "Harvest", "RepairBridge",
				"RepairBuilding", "SetUnitStance", "SetEngagementStance", "SetCohesion", "SetRallyPoint",
				"DeployTransform", "AfterDeployTransform", "GrantConditionOnDeploy", "PlaceMine",
				"BeginMinefield", "PlaceMinefield", "DropCrate", "DropSupplyCache", "ActivateCondition",
				"", "SomeOrderInventedNextYear",
			};

			foreach (var o in passthrough)
				Assert.That(OrderArbitrationMath.Classify(o), Is.EqualTo(BotOrderClass.Passthrough), o);
		}

		// ---------- Ownership ----------

		[Test]
		public void Ownership_EveryShippedObjectivePrefixIsRecognised()
		{
			foreach (var o in ShippedObjectives)
				Assert.That(OrderArbitrationMath.ObjectiveRank(o), Is.Not.EqualTo(OrderArbitrationMath.RankUnknown), o);
		}

		[Test]
		public void Ownership_ForeignTaskingIncumbentBeatsTaskingChallenger()
		{
			// The five-poacher fix: LayeredDefence may no longer yank an offense-committed unit, and it
			// needed no edit of its own to stop.
			Assert.That(OrderArbitrationMath.OwnershipBlocks("LayeredDefenceBotModule", "offense:12"), Is.True);
			Assert.That(OrderArbitrationMath.OwnershipBlocks("GarrisonBotModule", "defend:7"), Is.True);
			Assert.That(OrderArbitrationMath.OwnershipBlocks("PoiOffensiveBotModule", "defend-line:3,4"), Is.True);
		}

		[Test]
		public void Ownership_OwnClaimNeverBlocks()
		{
			// A module refreshing its own task, including via its OTHER prefix, must always pass.
			Assert.That(OrderArbitrationMath.OwnershipBlocks("PoiOffensiveBotModule", "offense:12"), Is.False);
			Assert.That(OrderArbitrationMath.OwnershipBlocks("PoiOffensiveBotModule", "bombard:12"), Is.False);
			Assert.That(OrderArbitrationMath.OwnershipBlocks("CaptureCoordinatorBotModule", "capture-escort:9"), Is.False);
		}

		[Test]
		public void Ownership_AmbientTacposClaimNeverBlocksRealTasking()
		{
			// LOAD-BEARING. StancePositioningExecutor stamps `tacpos:` on every @experimental bot-owned
			// combatant it positions (ClaimTicks 150, re-committed every 30 ticks) and never reads it back.
			// Rank it as ordinary tasking and this claim is "foreign" to every bot module, so the gate
			// would suppress EVERY order to EVERY positioned unit — the bot would stand still.
			Assert.That(OrderArbitrationMath.ObjectiveRank("tacpos:5"), Is.EqualTo(OrderArbitrationMath.RankAmbient));
			foreach (var m in new[]
			{
				"PoiOffensiveBotModule", "PoiGarrisonBotModule", "LayeredDefenceBotModule",
				"LaneAmbushBotModule", "MountedTransportBotModule", "CaptureCoordinatorBotModule",
				"SupplyFollowerBotModule", "ScoutBotModule", "SquadManagerBotModule",
			})
				Assert.That(OrderArbitrationMath.OwnershipBlocks(m, "tacpos:5"), Is.False, m);
		}

		[Test]
		public void Ownership_MissionOutranksTaskingButNotViceVersa()
		{
			// A capture party may recruit an offense unit; nothing may poach a capture escort.
			Assert.That(OrderArbitrationMath.OwnershipBlocks("CaptureCoordinatorBotModule", "offense:12"), Is.False);
			Assert.That(OrderArbitrationMath.OwnershipBlocks("EngineerRouteOpenBotModule", "defend-line:3,4"), Is.False);
			Assert.That(OrderArbitrationMath.OwnershipBlocks("PoiOffensiveBotModule", "capture-escort:9"), Is.True);
			Assert.That(OrderArbitrationMath.OwnershipBlocks("LayeredDefenceBotModule", "bridge-repair:2"), Is.True);
		}

		[Test]
		public void Ownership_TransportPrefixHasTwoLegitimateOwners()
		{
			// MountedTransport and HelicopterSquad both emit `transport:`; they arbitrate between
			// themselves with a hand-rolled reservation and the gate stays out of it.
			Assert.That(OrderArbitrationMath.OwnershipBlocks("MountedTransportBotModule", "transport:8"), Is.False);
			Assert.That(OrderArbitrationMath.OwnershipBlocks("HelicopterSquadBotModule", "transport:8"), Is.False);
			Assert.That(OrderArbitrationMath.OwnershipBlocks("PoiOffensiveBotModule", "transport:8"), Is.True);
		}

		[Test]
		public void Ownership_FailsOpenOnEveryUnknown()
		{
			// No incumbent, an unattributed order (queued outside a module tick), and an unrecognised
			// prefix all ADMIT. Table rot therefore costs damping, never correctness.
			Assert.That(OrderArbitrationMath.OwnershipBlocks("LayeredDefenceBotModule", null), Is.False);
			Assert.That(OrderArbitrationMath.OwnershipBlocks("LayeredDefenceBotModule", ""), Is.False);
			Assert.That(OrderArbitrationMath.OwnershipBlocks("", "offense:12"), Is.False);
			Assert.That(OrderArbitrationMath.OwnershipBlocks(null, "offense:12"), Is.False);
			Assert.That(OrderArbitrationMath.OwnershipBlocks("LayeredDefenceBotModule", "supply-follow:3"), Is.False);
			Assert.That(OrderArbitrationMath.OwnershipBlocks("ModuleAddedNextYear", "offense:12"), Is.True,
				"an unlisted CHALLENGER still ranks as ordinary tasking, so it cannot poach silently");
		}

		// ---------- Dwell ----------

		[Test]
		public void Dwell_SameDestinationAdmits()
		{
			// This is NOT an equivalence dedup: the census found equivalence gates cannot see the top
			// suspects, because their destinations genuinely differ. Re-issuing the SAME destination is
			// left alone so the modules that rely on a re-issue to recover keep working.
			Assert.That(OrderArbitrationMath.DwellBlocks(0, 10, Dwell, destinationDiffers: false, targetBusy: true), Is.False);
		}

		[Test]
		public void Dwell_DifferentDestinationInsideTheWindowSuppresses()
		{
			Assert.That(OrderArbitrationMath.DwellBlocks(0, 1, Dwell, true, true), Is.True);
			Assert.That(OrderArbitrationMath.DwellBlocks(0, Dwell - 1, Dwell, true, true), Is.True);
		}

		[Test]
		public void Dwell_OutsideTheWindowAdmits()
		{
			Assert.That(OrderArbitrationMath.DwellBlocks(0, Dwell, Dwell, true, true), Is.False, "the window is half-open");
			Assert.That(OrderArbitrationMath.DwellBlocks(0, Dwell + 500, Dwell, true, true), Is.False);
		}

		[Test]
		public void Dwell_IdleTargetAlwaysAdmits()
		{
			// The escape hatch that keeps the dwell from ever stalling a unit: if the errand ended (or was
			// interrupted — e.g. the carrier it was boarding died) the unit is re-orderable at once.
			Assert.That(OrderArbitrationMath.DwellBlocks(0, 1, Dwell, true, targetBusy: false), Is.False);
		}

		[Test]
		public void Dwell_ZeroTicksDisables()
		{
			Assert.That(OrderArbitrationMath.DwellBlocks(0, 1, 0, true, true), Is.False);
			Assert.That(OrderArbitrationMath.DwellBlocks(0, 1, -5, true, true), Is.False);
		}

		// ---------- DestinationKey ----------

		[Test]
		public void DestinationKey_ActorAndCellTargetsNeverCollide()
		{
			Assert.That(ActorTarget(7), Is.Not.EqualTo(Cell(7, 0)));
			Assert.That(ActorTarget(7), Is.Not.EqualTo(ActorTarget(8)));
			Assert.That(Cell(3, 4), Is.Not.EqualTo(Cell(4, 3)));
			Assert.That(Cell(3, 4), Is.EqualTo(Cell(3, 4)));
			Assert.That(OrderArbitrationMath.DestinationKey(false, 0, 0, 0, hasTarget: false), Is.EqualTo(0L));
		}

		// ---------- Gate composition: the consumer pins ----------

		[Test]
		public void Gate_LeversOffIsTheInertPassThrough()
		{
			// The default-off shipped state: a foreign mission incumbent AND a young standing order still
			// admit, so the pre-Stage-1 funnel is reproduced exactly.
			var g = Gate(ownership: false, dwell: 0);
			Assert.That(
				g.Admit("AttackMove", false, BotOrderUrgency.Directive, "LayeredDefenceBotModule", 0, Cell(1, 1), Targets(Unit(1))),
				Is.EqualTo(BotOrderVerdict.Admitted));
			Assert.That(
				g.Admit("AttackMove", false, BotOrderUrgency.Directive, "LayeredDefenceBotModule", 5, Cell(9, 9), Targets(Unit(1, "capture-escort:9"))),
				Is.EqualTo(BotOrderVerdict.Admitted));
			Assert.That(g.StandingCount, Is.EqualTo(0), "the inert gate keeps no state at all");
		}

		[Test]
		public void Gate_PassthroughOrderIsNeitherSuppressedNorRecorded()
		{
			var g = Gate();
			Assert.That(
				g.Admit("SetUnitStance", false, BotOrderUrgency.Directive, "PoiOffensiveBotModule", 0, 0L, Targets(Unit(1, "capture-escort:9"))),
				Is.EqualTo(BotOrderVerdict.Admitted));
			Assert.That(g.StandingCount, Is.EqualTo(0));
		}

		[Test]
		public void Gate_QueuedOrderIsNeverSuppressedAndDoesNotMoveTheStandingRecord()
		{
			// A queued order APPENDS and cancels nothing, so it is not a churn source. It must also not
			// overwrite the record, or the leading non-queued leg of a two-leg maneuver would be forgotten.
			var g = Gate();
			g.Admit("AttackMove", false, BotOrderUrgency.Directive, "PoiOffensiveBotModule", 0, Cell(1, 1), Targets(Unit(1)));
			Assert.That(
				g.Admit("AttackMove", true, BotOrderUrgency.Directive, "PoiOffensiveBotModule", 10, Cell(5, 5), Targets(Unit(1))),
				Is.EqualTo(BotOrderVerdict.Admitted));

			// The record still says (1,1)@0, so re-issuing (1,1) is "same destination" and admits...
			Assert.That(
				g.Admit("AttackMove", false, BotOrderUrgency.Directive, "PoiOffensiveBotModule", 20, Cell(1, 1), Targets(Unit(1))),
				Is.EqualTo(BotOrderVerdict.Admitted));

			// ...while a third cell inside the window is still suppressed.
			Assert.That(
				g.Admit("AttackMove", false, BotOrderUrgency.Directive, "PoiOffensiveBotModule", 30, Cell(7, 7), Targets(Unit(1))),
				Is.EqualTo(BotOrderVerdict.SuppressedDwell));
		}

		[Test]
		public void Gate_StopClearsTheStandingRecord()
		{
			// Without this a cancel would freeze the unit for a whole dwell window: the record would still
			// name a destination the unit is no longer heading to, and every real re-task would be dropped.
			var g = Gate();
			g.Admit("Move", false, BotOrderUrgency.Directive, "SupplyFollowerBotModule", 0, Cell(1, 1), Targets(Unit(1)));
			Assert.That(g.StandingCount, Is.EqualTo(1));

			Assert.That(
				g.Admit("Stop", false, BotOrderUrgency.Directive, "SupplyFollowerBotModule", 5, 0L, Targets(Unit(1))),
				Is.EqualTo(BotOrderVerdict.Admitted));
			Assert.That(g.StandingCount, Is.EqualTo(0));

			// Inside the dwell window, and would have been suppressed had the Stop not cleared the record.
			Assert.That(
				g.Admit("Move", false, BotOrderUrgency.Directive, "SupplyFollowerBotModule", 10, Cell(9, 9), Targets(Unit(1))),
				Is.EqualTo(BotOrderVerdict.Admitted));
		}

		[Test]
		public void Gate_DwellSuppressesTheRedirectItIsMeantTo()
		{
			// The census's §4.1 shape: a forward cell, then a grab to somewhere else 25 ticks later.
			var g = Gate();
			Assert.That(
				g.Admit("AttackMove", false, BotOrderUrgency.Directive, "LayeredDefenceBotModule", 100, Cell(20, 5), Targets(Unit(42))),
				Is.EqualTo(BotOrderVerdict.Admitted));
			Assert.That(
				g.Admit("EnterTransport", false, BotOrderUrgency.Directive, "MountedTransportBotModule", 125, ActorTarget(77), Targets(Unit(42))),
				Is.EqualTo(BotOrderVerdict.SuppressedDwell));
			Assert.That(
				g.Admit("EnterTransport", false, BotOrderUrgency.Directive, "MountedTransportBotModule", 100 + Dwell, ActorTarget(77), Targets(Unit(42))),
				Is.EqualTo(BotOrderVerdict.Admitted), "the window is finite — the unit is not owned forever");
		}

		[Test]
		public void Gate_ReflexBypassesBothPredicates()
		{
			// A withdrawal, an evacuation or a damage response must never be held back — by a stale
			// incumbency or by a young dwell. A unit that cannot flee is far worse than one that wiggles.
			var g = Gate();
			g.Admit("AttackMove", false, BotOrderUrgency.Directive, "PoiOffensiveBotModule", 0, Cell(20, 5), Targets(Unit(42)));

			Assert.That(
				g.Admit("AttackMove", false, BotOrderUrgency.Reflex, "PoiOffensiveBotModule", 5, Cell(1, 1), Targets(Unit(42, "capture-escort:9"))),
				Is.EqualTo(BotOrderVerdict.Admitted));

			// And the same order as a Directive would have been dropped — proving Reflex is what saved it.
			var h = Gate();
			h.Admit("AttackMove", false, BotOrderUrgency.Directive, "PoiOffensiveBotModule", 0, Cell(20, 5), Targets(Unit(42)));
			Assert.That(
				h.Admit("AttackMove", false, BotOrderUrgency.Directive, "PoiOffensiveBotModule", 5, Cell(1, 1), Targets(Unit(42, "capture-escort:9"))),
				Is.EqualTo(BotOrderVerdict.SuppressedOwnership));
		}

		[Test]
		public void Gate_ReflexBecomesTheNewStandingOrder()
		{
			// Otherwise the record would still name the pre-retreat destination, and the very next
			// ordinary order would be measured against a stale cell and let through — undoing the retreat.
			var g = Gate(ownership: false);
			g.Admit("AttackMove", false, BotOrderUrgency.Reflex, "PoiOffensiveBotModule", 0, Cell(1, 1), Targets(Unit(42)));
			Assert.That(
				g.Admit("AttackMove", false, BotOrderUrgency.Directive, "PoiOffensiveBotModule", 10, Cell(20, 5), Targets(Unit(42))),
				Is.EqualTo(BotOrderVerdict.SuppressedDwell));
		}

		[Test]
		public void Gate_GroupedOrderIsExemptFromDwellButRecordsEveryMember()
		{
			// Grouped orders already carry a working aggregate-anchor dedup in every module that issues
			// one, and Order.GroupedActors is readonly so a partial drop is impossible. But every member
			// must still get a record — that is what stops a single-actor grab from turning one unit out
			// of a moving group around, which is the shape the user sees as "units wiggle".
			var g = Gate(ownership: false);
			g.Admit("AttackMove", false, BotOrderUrgency.Directive, "PoiOffensiveBotModule", 0, Cell(30, 30), Targets(Unit(1), Unit(2), Unit(3)));
			Assert.That(g.StandingCount, Is.EqualTo(3));

			// A second grouped order to a different cell inside the window is NOT dwell-suppressed.
			Assert.That(
				g.Admit("AttackMove", false, BotOrderUrgency.Directive, "PoiOffensiveBotModule", 10, Cell(31, 31), Targets(Unit(1), Unit(2), Unit(3))),
				Is.EqualTo(BotOrderVerdict.Admitted));

			// A single-actor grab of member #2 is.
			Assert.That(
				g.Admit("EnterTransport", false, BotOrderUrgency.Directive, "MountedTransportBotModule", 20, ActorTarget(9), Targets(Unit(2))),
				Is.EqualTo(BotOrderVerdict.SuppressedDwell));
		}

		[Test]
		public void Gate_OwnershipIsAllOrNothingOverAGroup()
		{
			var g = Gate(dwell: 0);

			// One free member ⇒ the whole grouped order goes through (conservative: no partial drops).
			Assert.That(
				g.Admit("AttackMove", false, BotOrderUrgency.Directive, "LayeredDefenceBotModule", 0, Cell(5, 5),
					Targets(Unit(1, "offense:12"), Unit(2))),
				Is.EqualTo(BotOrderVerdict.Admitted));

			// Every member claimed by a module that outranks-or-ties us ⇒ dropped.
			Assert.That(
				g.Admit("AttackMove", false, BotOrderUrgency.Directive, "LayeredDefenceBotModule", 0, Cell(5, 5),
					Targets(Unit(1, "offense:12"), Unit(2, "capture:9"))),
				Is.EqualTo(BotOrderVerdict.SuppressedOwnership));
		}

		[Test]
		public void Gate_EmptyTargetSetAdmits()
		{
			// Every target dead / out of world ⇒ nothing to arbitrate, and nothing to record.
			var g = Gate();
			Assert.That(
				g.Admit("Move", false, BotOrderUrgency.Directive, "ScoutBotModule", 0, Cell(1, 1), Targets()),
				Is.EqualTo(BotOrderVerdict.Admitted));
			Assert.That(
				g.Admit("Move", false, BotOrderUrgency.Directive, "ScoutBotModule", 0, Cell(1, 1), null),
				Is.EqualTo(BotOrderVerdict.Admitted));
			Assert.That(g.StandingCount, Is.EqualTo(0));
		}

		[Test]
		public void Gate_StandingRecordIsIndependentOfTheCommitmentLedger()
		{
			// THE ANTI-AMNESIA PIN. Every one of the ~28 existing dampers dies when the unit leaves the
			// owning module's eligibility set, and eligibility is exactly what flickers. This record is
			// keyed to the order, not to any claim: it damps identically whether the unit is uncommitted,
			// committed to us, or committed to somebody we outrank — i.e. nothing a module does to the
			// ledger can purge it.
			foreach (var objective in new[] { null, "offense:12", "tacpos:5" })
			{
				var g = Gate();
				g.Admit("AttackMove", false, BotOrderUrgency.Directive, "PoiOffensiveBotModule", 0, Cell(20, 5), Targets(Unit(42, objective)));
				Assert.That(
					g.Admit("AttackMove", false, BotOrderUrgency.Directive, "PoiOffensiveBotModule", 30, Cell(21, 6), Targets(Unit(42, objective))),
					Is.EqualTo(BotOrderVerdict.SuppressedDwell), objective ?? "uncommitted");
			}
		}

		[Test]
		public void Gate_SuppressionsAreCountedPerModuleAndReason()
		{
			var g = Gate();
			g.Admit("AttackMove", false, BotOrderUrgency.Directive, "PoiOffensiveBotModule", 0, Cell(1, 1), Targets(Unit(1)));
			g.Admit("AttackMove", false, BotOrderUrgency.Directive, "PoiOffensiveBotModule", 1, Cell(2, 2), Targets(Unit(1)));
			g.Admit("AttackMove", false, BotOrderUrgency.Directive, "PoiOffensiveBotModule", 2, Cell(3, 3), Targets(Unit(1)));
			g.Admit("AttackMove", false, BotOrderUrgency.Directive, "LayeredDefenceBotModule", 3, Cell(4, 4), Targets(Unit(2, "capture:9")));

			Assert.That(g.Suppressions.Count, Is.EqualTo(2), "one bucket per (module, reason), not one line per event");
			Assert.That(g.Suppressions[0].ModuleTag, Is.EqualTo("PoiOffensiveBotModule"));
			Assert.That(g.Suppressions[0].Verdict, Is.EqualTo(BotOrderVerdict.SuppressedDwell));
			Assert.That(g.Suppressions[0].Count, Is.EqualTo(2));
			Assert.That(g.Suppressions[1].Verdict, Is.EqualTo(BotOrderVerdict.SuppressedOwnership));
			Assert.That(g.Suppressions[1].Count, Is.EqualTo(1));

			g.ResetSuppressions();
			Assert.That(g.Suppressions.Count, Is.EqualTo(0));
		}

		[Test]
		public void Gate_PruneDropsOnlyRecordsPastTheDwell()
		{
			var g = Gate(dwell: 100);
			g.Admit("Move", false, BotOrderUrgency.Directive, "ScoutBotModule", 0, Cell(1, 1), Targets(Unit(1)));
			g.Admit("Move", false, BotOrderUrgency.Directive, "ScoutBotModule", 250, Cell(2, 2), Targets(Unit(2)));

			// Prune is tick-stamped, not countdown-decremented, so calling it more often changes nothing.
			for (var t = 0; t <= 300; t++)
				g.Prune(t);

			Assert.That(g.StandingCount, Is.EqualTo(1), "the tick-0 record aged out; the tick-250 one is still live");
			Assert.That(
				g.Admit("Move", false, BotOrderUrgency.Directive, "ScoutBotModule", 300, Cell(3, 3), Targets(Unit(2))),
				Is.EqualTo(BotOrderVerdict.SuppressedDwell));
		}
	}
}
