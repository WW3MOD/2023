#region Copyright & License Information
/*
 * WW3MOD BotOrderGate CALLER-level tests — bot-brain Stage 1, review round 2.
 *
 * The first round of pins measured the gate's internal decisions and said nothing about system
 * safety. Every defect the adversarial review found lived in the SEAM between a silent drop and a
 * caller that records "I already ordered this unit to X" regardless — so the order was discarded,
 * the caller's dedup advanced, and the unit was stranded on its old destination PERMANENTLY while
 * the module believed it was on the new one. None of that was visible to a test of the gate alone.
 *
 * Three kinds of pin here, in increasing strength:
 *
 *  1. MODEL CALLER — a miniature of the real order-then-cache shape (PoiOffensiveBotModule's fires
 *     standoff, LayeredDefence's line assignment, CaptureCoordinator's reserve muster all have it).
 *     The correct and the BUGGY caller are both driven, so the test proves the difference matters
 *     rather than merely asserting the fixed behaviour.
 *
 *  2. SEQUENCE ATOMICITY — a partly-issued multi-leg maneuver is worse than no suppression at all.
 *     Pinned in the gate, because the gate is what makes it impossible to get wrong at a call site.
 *
 *  3. SOURCE SCAN — the only pin here that covers the REAL fifteen call sites. It reads the shipped
 *     BotModules sources and fails if any suppressible order is issued unguarded and immediately
 *     followed by a cache write. Its scope is derived from OrderArbitrationMath.Classify, so it
 *     cannot drift from the production whitelist.
 *
 * STATED GAP: nothing here executes ModularBot itself. Target extraction from Subject ∪
 * GroupedActors and destination-key derivation from Order.Target need a World, so the adapter
 * between the real Order and this gate remains unpinned by construction.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class BotOrderGateCallerTest
	{
		const int Dwell = 120;

		// The census-named churn sources, and the only orders the funnel may drop. Enumerated in
		// OrderArbitrationMath's header.
		const int ExpectedRecurringSites = 6;

		static long Cell(int x, int y) => OrderArbitrationMath.DestinationKey(false, 0, x, y, true);
		static List<BotOrderTarget> One(uint id, string objective = null, bool busy = true)
			=> new() { new BotOrderTarget(id, objective, busy) };

		/// <summary>A miniature of the shipped order-then-cache shape: dedup on the remembered
		/// destination, issue, then advance the memory. <paramref name="honourRefusal"/> switches between
		/// the fixed caller and the defective one the review found.</summary>
		sealed class ModelCaller
		{
			readonly BotOrderGate gate;
			readonly bool honourRefusal;
			readonly Dictionary<uint, long> orderedTo = new();

			// Deliberately two counters. Delivered counts orders the funnel actually accepted; Believed
			// counts the times the caller advanced its memory as though it had ordered. The defect IS the
			// gap between them — a caller that cannot see a refusal reports work it never did.
			public int Delivered;
			public int Believed;

			public ModelCaller(BotOrderGate gate, bool honourRefusal)
			{
				this.gate = gate;
				this.honourRefusal = honourRefusal;
			}

			public long? Remembered(uint actor) => orderedTo.TryGetValue(actor, out var v) ? v : null;

			public BotOrderVerdict Reposition(uint actor, long dest, int tick, bool busy = true, string module = "PoiOffensiveBotModule")
			{
				// The real guard: "never re-issue the identical destination" (PoiOffensiveBotModule.cs:3163).
				if (orderedTo.TryGetValue(actor, out var prev) && prev == dest)
					return BotOrderVerdict.Admitted;

				var verdict = gate.Admit("AttackMove", false, BotOrderDamping.Recurring, module, tick, dest, One(actor, null, busy));
				if (verdict == BotOrderVerdict.Admitted)
					Delivered++;

				if (honourRefusal && verdict != BotOrderVerdict.Admitted)
					return verdict;

				orderedTo[actor] = dest;
				Believed++;
				return verdict;
			}
		}

		[Test]
		public void Caller_HonouringRefusalRecoversAfterTheDwell()
		{
			var caller = new ModelCaller(new BotOrderGate(true, Dwell), honourRefusal: true);

			Assert.That(caller.Reposition(1, Cell(10, 10), 0), Is.EqualTo(BotOrderVerdict.Admitted));
			Assert.That(caller.Remembered(1), Is.EqualTo(Cell(10, 10)));

			// Anchor drifts inside the dwell window: refused, and the memory must NOT advance.
			Assert.That(caller.Reposition(1, Cell(14, 14), 30), Is.EqualTo(BotOrderVerdict.SuppressedDwell));
			Assert.That(caller.Remembered(1), Is.EqualTo(Cell(10, 10)), "a refused order must not be remembered");
			Assert.That(caller.Delivered, Is.EqualTo(1));
			Assert.That(caller.Believed, Is.EqualTo(1), "belief and delivery stay in step when refusals are honoured");

			// Past the window the same anchor is re-offered and accepted — the unit is not stranded.
			Assert.That(caller.Reposition(1, Cell(14, 14), 30 + Dwell), Is.EqualTo(BotOrderVerdict.Admitted));
			Assert.That(caller.Remembered(1), Is.EqualTo(Cell(14, 14)));
			Assert.That(caller.Delivered, Is.EqualTo(2), "the errand completes one dwell late, not never");
			Assert.That(caller.Believed, Is.EqualTo(2));
		}

		[Test]
		public void Caller_IgnoringRefusalStrandsTheUnitForever()
		{
			// THE DEFECT, reproduced. This is the shape shipped in 72431390 at five sites in
			// PoiOffensiveBotModule alone. Because the caller's dedup then believes the unit is already
			// heading to the new anchor, it never re-issues — the piece sits on its old destination for
			// the rest of the match, and the module's own logs claim otherwise.
			var caller = new ModelCaller(new BotOrderGate(true, Dwell), honourRefusal: false);

			caller.Reposition(1, Cell(10, 10), 0);
			caller.Reposition(1, Cell(14, 14), 30);
			Assert.That(caller.Remembered(1), Is.EqualTo(Cell(14, 14)), "the buggy caller advanced its cache on a refusal");

			// Every later attempt is deduped away by the caller's own guard, at any tick, forever.
			for (var t = 30; t <= 30 + 10 * Dwell; t += 10)
				caller.Reposition(1, Cell(14, 14), t);

			// The gap: the module is certain it repositioned the unit; the unit never got the order, and the
			// caller's own dedup guarantees it never will.
			Assert.That(caller.Believed, Is.EqualTo(2), "the buggy caller thinks it issued twice");
			Assert.That(caller.Delivered, Is.EqualTo(1), "but only one order was ever delivered, and no retry is possible");
		}

		[Test]
		public void Caller_RefusalIsAlsoSignalledForOwnership()
		{
			// The other refusal reason must be equally visible, or the same strand happens via predicate (a)
			// — and worse, the caller would go on to write a ledger claim for a unit it never moved.
			var caller = new ModelCaller(new BotOrderGate(true, 0), honourRefusal: true);
			var gate = new BotOrderGate(true, 0);
			Assert.That(
				gate.Admit("AttackMove", false, BotOrderDamping.Recurring, "LayeredDefenceBotModule", 0, Cell(1, 1), One(7, "capture-escort:9")),
				Is.EqualTo(BotOrderVerdict.SuppressedOwnership));
			Assert.That(caller.Delivered, Is.EqualTo(0));
		}

		// ---------- Sequence atomicity (F1) ----------

		[Test]
		public void Sequence_QueuedTailIsDroppedWithItsSuppressedHead()
		{
			// The supply-truck detour: leg 1 is the danger-avoiding waypoint (non-queued, suppressible),
			// leg 2 chains the direct line (queued). Admitting leg 2 alone drives the truck along exactly
			// the straight path the detour existed to avoid — a partly-issued plan, worse than none.
			var g = new BotOrderGate(false, Dwell);
			Assert.That(
				g.Admit("Move", false, BotOrderDamping.Recurring, "SupplyFollowerBotModule", 0, Cell(5, 5), One(3)),
				Is.EqualTo(BotOrderVerdict.Admitted));

			// Same tick as the head's refusal.
			Assert.That(
				g.Admit("Move", false, BotOrderDamping.Recurring, "SupplyFollowerBotModule", 40, Cell(9, 1), One(3)),
				Is.EqualTo(BotOrderVerdict.SuppressedDwell), "the waypoint leg is refused");
			Assert.That(
				g.Admit("Move", true, BotOrderDamping.Recurring, "SupplyFollowerBotModule", 40, Cell(20, 20), One(3)),
				Is.EqualTo(BotOrderVerdict.SuppressedSequence), "so its chained direct leg must go too");
		}

		[Test]
		public void Sequence_QueuedTailSurvivesWhenItsHeadWasAdmitted()
		{
			var g = new BotOrderGate(false, Dwell);
			Assert.That(
				g.Admit("Move", false, BotOrderDamping.Recurring, "SupplyFollowerBotModule", 0, Cell(5, 5), One(3)),
				Is.EqualTo(BotOrderVerdict.Admitted));
			Assert.That(
				g.Admit("Move", true, BotOrderDamping.Recurring, "SupplyFollowerBotModule", 0, Cell(20, 20), One(3)),
				Is.EqualTo(BotOrderVerdict.Admitted));
		}

		[Test]
		public void Sequence_BindingIsScopedToASingleTick()
		{
			// The legs of a maneuver are always adjacent statements inside one module tick, so the binding
			// is inferred from structure rather than declared — an atomicity marker would have to be
			// remembered by every future author of a pair, which is the failure mode that caused this.
			// It must therefore not leak into a LATER tick's unrelated queued order.
			var g = new BotOrderGate(false, Dwell);
			g.Admit("Move", false, BotOrderDamping.Recurring, "SupplyFollowerBotModule", 0, Cell(5, 5), One(3));
			g.Admit("Move", false, BotOrderDamping.Recurring, "SupplyFollowerBotModule", 40, Cell(9, 1), One(3));

			Assert.That(
				g.Admit("Move", true, BotOrderDamping.Recurring, "SupplyFollowerBotModule", 41, Cell(20, 20), One(3)),
				Is.EqualTo(BotOrderVerdict.Admitted), "a later tick's queued order is not bound to a stale refusal");
		}

		[Test]
		public void Sequence_AnAdmittedHeadLaterInTheSameTickReopensTheActor()
		{
			var g = new BotOrderGate(false, Dwell);
			g.Admit("Move", false, BotOrderDamping.Recurring, "SupplyFollowerBotModule", 0, Cell(5, 5), One(3));
			g.Admit("Move", false, BotOrderDamping.Recurring, "SupplyFollowerBotModule", 40, Cell(9, 1), One(3));

			// A Protected head in the same tick (an evacuation) is admitted, so its own chained leg must be.
			Assert.That(
				g.Admit("Move", false, BotOrderDamping.Protected, "SupplyFollowerBotModule", 40, Cell(0, 0), One(3)),
				Is.EqualTo(BotOrderVerdict.Admitted));
			Assert.That(
				g.Admit("Move", true, BotOrderDamping.Recurring, "SupplyFollowerBotModule", 40, Cell(1, 0), One(3)),
				Is.EqualTo(BotOrderVerdict.Admitted));
		}

		// ---------- Reflex is absolute (F3, F4 depend on it) ----------

		[Test]
		public void Protected_IsNeverSuppressedUnderAnyCombination()
		{
			// THE FAIL-SAFE PIN, and the reason the default was inverted. Protected is what an UNMARKED
			// call site gets, so this is the property that makes forgetting an annotation cost damping
			// instead of safety. Two review rounds found six flee/withdraw/disengage sites nobody had
			// marked — GroundStates' flee, NavyStates' and ProtectionStates' via StateBase, and
			// HelicopterStates, which stamps committedRetreatCell BEFORE its issue loop and so would have
			// lost the withdrawal permanently. All six are now safe without being marked at all.
			foreach (var ownership in new[] { true, false })
				foreach (var objective in new[] { null, "offense:12", "capture-escort:9", "tacpos:5" })
					foreach (var busy in new[] { true, false })
						foreach (var headRefused in new[] { true, false })
						{
							var g = new BotOrderGate(ownership, Dwell);
							g.Admit("Move", false, BotOrderDamping.Recurring, "PoiOffensiveBotModule", 0, Cell(9, 9), One(1, objective, busy));
							if (headRefused)
								g.Admit("Move", false, BotOrderDamping.Recurring, "PoiOffensiveBotModule", 10, Cell(4, 4), One(1, objective, busy));

							var why = $"ownership={ownership} objective={objective ?? "none"} busy={busy} headRefused={headRefused}";
							Assert.That(
								g.Admit("Move", false, BotOrderDamping.Protected, "SquadManagerBotModule", 10, Cell(0, 0), One(1, objective, busy)),
								Is.EqualTo(BotOrderVerdict.Admitted), why);
						}
		}

		// ---------- Activation grace (F7) ----------

		[Test]
		public void Grace_AJustOrderedUnitCountsAsBusyWhileItsOrderIsStillQueued()
		{
			// ModularBot drains ceil(N/5) orders per tick, so an order sits in the queue for at least one
			// tick before World.IssueOrder runs it — the unit reads IDLE meanwhile. Without a grace window
			// a competing grab gets a free pass in exactly the window the fastest churn sources live in.
			var g = new BotOrderGate(false, Dwell);
			g.Admit("AttackMove", false, BotOrderDamping.Recurring, "LayeredDefenceBotModule", 0, Cell(10, 10), One(1, null, busy: true));

			Assert.That(
				g.Admit("EnterTransport", false, BotOrderDamping.Recurring, "MountedTransportBotModule", 2, Cell(3, 3), One(1, null, busy: false)),
				Is.EqualTo(BotOrderVerdict.SuppressedDwell), "the order has not landed yet, so 'idle' is not 'finished'");

			// Past the grace window a genuinely idle unit is re-orderable at once — the escape hatch that
			// keeps the dwell from ever stalling a unit whose errand really has ended.
			Assert.That(
				g.Admit("EnterTransport", false, BotOrderDamping.Recurring, "MountedTransportBotModule", 60, Cell(3, 3), One(1, null, busy: false)),
				Is.EqualTo(BotOrderVerdict.Admitted));
		}

		// ---------- Source scan: the only pin covering the real call sites ----------

		// Matches the CALL, not the call plus its first argument. Requiring `new Order("` on the same
		// physical line is what hid PoiOffensive's staging site from this scan: fitting the damping
		// argument wrapped `QueueOrder(` and `new Order("` onto separate lines, so the census's §4.2 beat
		// silently left the scan's scope and a `> 0` sentinel could not tell 5 sites from 6. The dot
		// keeps this from matching the interface declarations (which live outside BotModules anyway).
		static readonly Regex OrderIssue = new(@"\.QueueOrder\(", RegexOptions.Compiled);
		static readonly Regex RecurringMark = new(@"BotOrderDamping\.Recurring", RegexOptions.Compiled);
		static readonly Regex Guarded = new(@"if\s*\(\s*!?\w+(\.\w+)*\.QueueOrder|!\w+(\.\w+)*\.QueueOrder", RegexOptions.Compiled);

		// The TWO harm models. A stale cache is the one round 2 fixed. An unconditional STATE TRANSITION is
		// strictly worse, and the first version of this scan was blind to it — which is how GroundStates'
		// flee got through: that site is followed by ChangeState, not by an assignment, so a pattern
		// looking only for writes broke out clean and reported nothing.
		// `(var )?x = new T` is in here because of the case this scan UNDERREPORTED on its first run:
		// MountedTransport built a CarrierTask whose ReservedPassengers set named passengers whose boarding
		// order had been refused, so the carrier waited 90 s for someone who was never told to come.
		// Building a record OF the order you just issued is the same harm as advancing a dedup cache.
		static readonly Regex CacheWrite = new(
			@"^\s*((var\s+)?\w+\s*=\s*new\s+\w+|\w+\[[^\]]+\]\s*=[^=]|\w+\.\w+\s*=[^=]|\w+\s*=\s*true\s*;|\w+\.Add\()",
			RegexOptions.Compiled);
		static readonly Regex StateTransition = new(@"ChangeState\(", RegexOptions.Compiled);

		static string FindBotModulesDir()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(dir.FullName, "OpenRA.Mods.Common", "Traits", "BotModules");
				if (Directory.Exists(candidate))
					return candidate;
			}

			return null;
		}

		/// <summary>Walk forward yielding one entry per STATEMENT rather than per line. The first version
		/// counted lines, so a wrapped Log.Write consumed the whole three-entry window and hid the cache
		/// write behind it — GarrisonBotModule's garrison order was missed in exactly that way.</summary>
		static List<string> ForwardStatements(string[] lines, int from, int count)
		{
			var result = new List<string>();
			var i = from;
			while (i < lines.Length && result.Count < count)
			{
				var trimmed = lines[i].Trim();
				if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal))
				{
					i++;
					continue;
				}

				// A closing brace or an early exit ends the region a dropped order could corrupt.
				if (trimmed[0] == '}' || trimmed.StartsWith("break", StringComparison.Ordinal)
					|| trimmed.StartsWith("return", StringComparison.Ordinal)
					|| trimmed.StartsWith("continue", StringComparison.Ordinal))
					break;

				var statement = lines[i];
				while (CountOf(statement, '(') > CountOf(statement, ')') && i + 1 < lines.Length)
					statement += " " + lines[++i].Trim();

				result.Add(statement);
				i++;
			}

			return result;
		}

		[Test]
		public void SourceScan_EveryRecurringCallSiteChecksWhetherItsOrderWasAccepted()
		{
			// Marking a site Recurring ASSERTS that it honours a refusal. This is the only pin over the
			// REAL call sites, and its scope is exactly the set of orders the gate can drop — so it cannot
			// drift from production, and it stays silent about the many Protected sites which by
			// construction cannot be refused.
			var root = FindBotModulesDir();
			if (root == null)
				Assert.Ignore("source tree not reachable from the test assembly — scan skipped, not passed");

			var offenders = new List<string>();
			var recurringSites = 0;
			foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
			{
				var lines = File.ReadAllLines(file);
				for (var i = 0; i < lines.Length; i++)
				{
					if (!OrderIssue.IsMatch(lines[i]))
						continue;

					// Accumulate the whole STATEMENT before testing anything about it, and skip the lines it
					// consumed so a wrapped call cannot be counted twice.
					var statement = lines[i];
					var end = i;
					while (CountOf(statement, '(') > CountOf(statement, ')') && end + 1 < lines.Length)
						statement += " " + lines[++end].Trim();

					i = end;

					if (!RecurringMark.IsMatch(statement))
						continue;

					recurringSites++;
					if (Guarded.IsMatch(statement))
						continue;

					// Unguarded is only a DEFECT where there is state to corrupt. The contract is "do not
					// advance anything on a refusal", not "assign the bool somewhere" — a site with nothing
					// following it satisfies that trivially, and demanding a guard with an empty body would
					// be ceremony. If a cache write or a state transition is ever added below such a site,
					// this scan starts failing then, which is exactly when it matters.
					string harm = null;
					foreach (var next in ForwardStatements(lines, end + 1, 3))
					{
						if (CacheWrite.IsMatch(next))
						{
							harm = "then advances a cache: " + next.Trim();
							break;
						}

						if (StateTransition.IsMatch(next))
						{
							harm = "then transitions state unconditionally: " + next.Trim();
							break;
						}
					}

					if (harm != null)
						offenders.Add($"{Path.GetFileName(file)}:{i + 1} discards its result, {harm}");
				}
			}

			// An EXACT count, so the scan is a contract rather than a smoke test. A `> 0` sentinel cannot
			// distinguish "the scope is right" from "the scope silently lost a site", which is precisely
			// what happened. Adding or removing a suppressible site must be a deliberate edit here.
			Assert.That(recurringSites, Is.EqualTo(ExpectedRecurringSites),
				$"expected exactly {ExpectedRecurringSites} BotOrderDamping.Recurring call sites — "
				+ "MountedTransport passenger boarding, LayeredDefence line assignment x2, PoiOffensive "
				+ "StageFreePool, and SupplyFollower's two follow Moves. If you added or removed one "
				+ "deliberately, update ExpectedRecurringSites and say why in the commit message; if you "
				+ "did not, the scan has lost sight of a site it is supposed to be policing.");
			Assert.That(offenders, Is.Empty,
				"A Recurring order may be dropped by the funnel. Every such call site must check QueueOrder's "
				+ "return value before advancing any memory, booking, ledger claim or state transition — "
				+ "otherwise the order is discarded while the module believes it was delivered, and the "
				+ "caller's own dedup guarantees it is never re-offered.\n  " + string.Join("\n  ", offenders));
		}

		[Test]
		public void SourceScan_NoSquadStateOrderIsMarkedRecurring()
		{
			// The squad-state FSMs issue once and ChangeState immediately: GroundUnitsFleeState.Tick sends
			// its flee Move and transitions to Regroup in the same call, so there is no retry, ever. Such a
			// site can NEVER satisfy the Recurring contract, and marking one would walk a squad that had
			// just decided it could not win straight back into the enemy it was fleeing.
			var root = FindBotModulesDir();
			if (root == null)
				Assert.Ignore("source tree not reachable from the test assembly — scan skipped, not passed");

			var states = Path.Combine(root, "Squads");
			if (!Directory.Exists(states))
				Assert.Ignore("squad states directory not found — scan skipped, not passed");

			var offenders = new List<string>();
			foreach (var file in Directory.EnumerateFiles(states, "*.cs", SearchOption.AllDirectories))
			{
				var lines = File.ReadAllLines(file);
				for (var i = 0; i < lines.Length; i++)
					if (RecurringMark.IsMatch(lines[i]))
						offenders.Add($"{Path.GetFileName(file)}:{i + 1}");
			}

			Assert.That(offenders, Is.Empty,
				"Squad-state orders are one-shot per state transition and are never re-offered, so they must "
				+ "stay Protected: " + string.Join(", ", offenders));
		}

		static int CountOf(string s, char c)
		{
			var n = 0;
			foreach (var ch in s)
				if (ch == c)
					n++;

			return n;
		}
	}
}
