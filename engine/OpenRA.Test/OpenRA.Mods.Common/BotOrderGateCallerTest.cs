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

				var verdict = gate.Admit("AttackMove", false, BotOrderUrgency.Directive, module, tick, dest, One(actor, null, busy));
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
				gate.Admit("AttackMove", false, BotOrderUrgency.Directive, "LayeredDefenceBotModule", 0, Cell(1, 1), One(7, "capture-escort:9")),
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
				g.Admit("Move", false, BotOrderUrgency.Directive, "SupplyFollowerBotModule", 0, Cell(5, 5), One(3)),
				Is.EqualTo(BotOrderVerdict.Admitted));

			// Same tick as the head's refusal.
			Assert.That(
				g.Admit("Move", false, BotOrderUrgency.Directive, "SupplyFollowerBotModule", 40, Cell(9, 1), One(3)),
				Is.EqualTo(BotOrderVerdict.SuppressedDwell), "the waypoint leg is refused");
			Assert.That(
				g.Admit("Move", true, BotOrderUrgency.Directive, "SupplyFollowerBotModule", 40, Cell(20, 20), One(3)),
				Is.EqualTo(BotOrderVerdict.SuppressedSequence), "so its chained direct leg must go too");
		}

		[Test]
		public void Sequence_QueuedTailSurvivesWhenItsHeadWasAdmitted()
		{
			var g = new BotOrderGate(false, Dwell);
			Assert.That(
				g.Admit("Move", false, BotOrderUrgency.Directive, "SupplyFollowerBotModule", 0, Cell(5, 5), One(3)),
				Is.EqualTo(BotOrderVerdict.Admitted));
			Assert.That(
				g.Admit("Move", true, BotOrderUrgency.Directive, "SupplyFollowerBotModule", 0, Cell(20, 20), One(3)),
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
			g.Admit("Move", false, BotOrderUrgency.Directive, "SupplyFollowerBotModule", 0, Cell(5, 5), One(3));
			g.Admit("Move", false, BotOrderUrgency.Directive, "SupplyFollowerBotModule", 40, Cell(9, 1), One(3));

			Assert.That(
				g.Admit("Move", true, BotOrderUrgency.Directive, "SupplyFollowerBotModule", 41, Cell(20, 20), One(3)),
				Is.EqualTo(BotOrderVerdict.Admitted), "a later tick's queued order is not bound to a stale refusal");
		}

		[Test]
		public void Sequence_AnAdmittedHeadLaterInTheSameTickReopensTheActor()
		{
			var g = new BotOrderGate(false, Dwell);
			g.Admit("Move", false, BotOrderUrgency.Directive, "SupplyFollowerBotModule", 0, Cell(5, 5), One(3));
			g.Admit("Move", false, BotOrderUrgency.Directive, "SupplyFollowerBotModule", 40, Cell(9, 1), One(3));

			// A Reflex head in the same tick (an evacuation) is admitted, so its own chained leg must be.
			Assert.That(
				g.Admit("Move", false, BotOrderUrgency.Reflex, "SupplyFollowerBotModule", 40, Cell(0, 0), One(3)),
				Is.EqualTo(BotOrderVerdict.Admitted));
			Assert.That(
				g.Admit("Move", true, BotOrderUrgency.Directive, "SupplyFollowerBotModule", 40, Cell(1, 0), One(3)),
				Is.EqualTo(BotOrderVerdict.Admitted));
		}

		// ---------- Reflex is absolute (F3, F4 depend on it) ----------

		[Test]
		public void Reflex_IsNeverSuppressedUnderAnyCombination()
		{
			// HelicopterStates stamps committedRetreatCell BEFORE its issue loop, so a suppressed
			// withdrawal Move would be lost permanently (retargeted stays false and the unit is not idle).
			// That pre-stamp is sound ONLY because Reflex is absolute — so pin it exhaustively rather
			// than relying on the one path the other tests happen to take.
			foreach (var ownership in new[] { true, false })
				foreach (var objective in new[] { null, "offense:12", "capture-escort:9", "tacpos:5" })
					foreach (var busy in new[] { true, false })
						foreach (var headRefused in new[] { true, false })
						{
							var g = new BotOrderGate(ownership, Dwell);
							g.Admit("Move", false, BotOrderUrgency.Directive, "PoiOffensiveBotModule", 0, Cell(9, 9), One(1, objective, busy));
							if (headRefused)
								g.Admit("Move", false, BotOrderUrgency.Directive, "PoiOffensiveBotModule", 10, Cell(4, 4), One(1, objective, busy));

							var why = $"ownership={ownership} objective={objective ?? "none"} busy={busy} headRefused={headRefused}";
							Assert.That(
								g.Admit("Move", false, BotOrderUrgency.Reflex, "SquadManagerBotModule", 10, Cell(0, 0), One(1, objective, busy)),
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
			g.Admit("AttackMove", false, BotOrderUrgency.Directive, "LayeredDefenceBotModule", 0, Cell(10, 10), One(1, null, busy: true));

			Assert.That(
				g.Admit("EnterTransport", false, BotOrderUrgency.Directive, "MountedTransportBotModule", 2, Cell(3, 3), One(1, null, busy: false)),
				Is.EqualTo(BotOrderVerdict.SuppressedDwell), "the order has not landed yet, so 'idle' is not 'finished'");

			// Past the grace window a genuinely idle unit is re-orderable at once — the escape hatch that
			// keeps the dwell from ever stalling a unit whose errand really has ended.
			Assert.That(
				g.Admit("EnterTransport", false, BotOrderUrgency.Directive, "MountedTransportBotModule", 60, Cell(3, 3), One(1, null, busy: false)),
				Is.EqualTo(BotOrderVerdict.Admitted));
		}

		// ---------- Source scan: the only pin covering the real call sites ----------

		static readonly Regex OrderIssue = new(@"QueueOrder\(\s*new Order\(""(\w+)""", RegexOptions.Compiled);
		static readonly Regex CacheWrite = new(
			@"^\s*(\w+\[[^\]]+\]\s*=[^=]|\w+\.\w+\s*=[^=]|\w+\s*=\s*true\s*;|\w+\.Add\()", RegexOptions.Compiled);
		static readonly Regex Guarded = new(@"if\s*\(\s*\w+(\.\w+)*\.QueueOrder|!\w+(\.\w+)*\.QueueOrder", RegexOptions.Compiled);
		static readonly Regex QueuedTail = new(@",\s*true\s*,\s*groupedActors|,\s*true\s*\)\s*\)|queued:\s*true", RegexOptions.Compiled);

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

		[Test]
		public void SourceScan_NoSuppressibleOrderAdvancesACacheWithoutCheckingAcceptance()
		{
			var root = FindBotModulesDir();
			if (root == null)
				Assert.Ignore("source tree not reachable from the test assembly — scan skipped, not passed");

			var offenders = new List<string>();
			foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
			{
				var lines = File.ReadAllLines(file);
				for (var i = 0; i < lines.Length; i++)
				{
					var m = OrderIssue.Match(lines[i]);

					// SCOPE DERIVED FROM PRODUCTION: only orders the gate can actually suppress. Adding a
					// string to Classify's Tasking set automatically widens this scan.
					if (!m.Success || OrderArbitrationMath.Classify(m.Groups[1].Value) != BotOrderClass.Tasking)
						continue;

					// Stitch a statement that wraps onto following lines so the queued-tail test sees it all.
					var statement = lines[i];
					var end = i;
					while (CountOf(statement, '(') > CountOf(statement, ')') && end + 1 < lines.Length)
						statement += " " + lines[++end].Trim();

					// A queued follow-on cannot be a sequence head; the gate binds it to its head instead.
					if (QueuedTail.IsMatch(statement) || Guarded.IsMatch(lines[i]))
						continue;

					var seen = 0;
					for (var k = end + 1; k < lines.Length && seen < 3; k++)
					{
						var t = lines[k].Trim();
						if (t.Length == 0 || t.StartsWith("//", StringComparison.Ordinal))
							continue;

						seen++;
						if (CacheWrite.IsMatch(lines[k]))
						{
							offenders.Add($"{Path.GetFileName(file)}:{i + 1} issues '{m.Groups[1].Value}' unguarded, then writes at :{k + 1} -> {t}");
							break;
						}

						if (t[0] == '}' || t.StartsWith("break", StringComparison.Ordinal)
							|| t.StartsWith("return", StringComparison.Ordinal) || t.StartsWith("continue", StringComparison.Ordinal))
							break;
					}
				}
			}

			Assert.That(offenders, Is.Empty,
				"A suppressible order whose result is discarded, followed by a cache write, strands the unit "
				+ "permanently: the order is dropped while the caller's dedup believes it was delivered. Guard "
				+ "the issue with `if (!bot.QueueOrder(...)) continue;` (or `return;`) before advancing any "
				+ "memory, booking or ledger claim.\n  " + string.Join("\n  ", offenders));
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
