#region Copyright & License Information
/*
 * WW3MOD (2026-09-15). GARRISON_PANEL could never become visible, for ANY selection, since it was
 * written -- and the reason is a shape, not a typo, which is why this fixture pins the shape.
 *
 * GarrisonPanelLogic set `panel.Visible = false` in its constructor and put the only other write to
 * that field inside the OnTick of GARRISON_TICKER, a LogicTicker declared as a CHILD of the panel.
 * Widget.TickOuter is gated on IsVisible(), so a hidden container ticks none of its children: the
 * ticker that would have shown the panel only ran once the panel was already up. Nothing else in
 * the tree writes that field, so selecting a garrisoned building raised nothing.
 *
 * CargoPanelLogic, one file over and sharing the same 228x240 rectangle, already carried the note
 * ("LogicTicker inside a hidden container never ticks, causing chicken-and-egg") and already used
 * an IsVisible delegate. The fix was to make the garrison panel the same shape.
 *
 * THREE INDEPENDENT HALVES, because any one of them alone can be satisfied by a broken build:
 *   1. The ENGINE premise -- a hidden container really does not tick its children, and a visible
 *      one really does. Without the control half, half 1 passes on a harness that ticks nothing.
 *   2. The FIX mechanism -- a child's own IsVisible delegate is invoked by its visible parent even
 *      on frames where it answers false. That is what makes the delegate escape the trap.
 *   3. The two SHIPPED panels -- neither logic class assigns Widget.Visible anywhere, and the
 *      shipped chrome declares no LogicTicker inside GARRISON_PANEL.
 *
 * WHAT THIS CANNOT SAY: whether the panel draws anything legible once it is up, or whether
 * UpdateSelection picks the right actor. Those want a capture and a running world respectively.
 * This fixture only answers "can it ever be on screen at all", which is the question that was
 * silently answered "no".
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Widgets;

namespace OpenRA.Test
{
	[TestFixture]
	public class GarrisonPanelVisibilityTest
	{
		sealed class BareWidget : Widget { }

		const BindingFlags AllMembers = BindingFlags.Instance | BindingFlags.Static
			| BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

		// ---- 1. THE ENGINE PREMISE THE WHOLE BUG RESTS ON -------------------------------------------
		[TestCase(true, TestName = "A visible container ticks its LogicTicker child")]
		[TestCase(false, TestName = "A hidden container ticks nothing inside it")]
		public void TickOuterOnlyDescendsIntoVisibleSubtrees(bool panelVisible)
		{
			var ticks = 0;
			var root = new BareWidget();
			var panel = new BareWidget { Visible = panelVisible };
			var ticker = new LogicTickerWidget { OnTick = () => ticks++ };

			root.AddChild(panel);
			panel.AddChild(ticker);

			root.TickOuter();

			// Both cases are asserted from one body on purpose. Run only the hidden case and a
			// harness that ticks nothing at all -- a root left invisible, an AddChild that does not
			// parent -- reads as a pass and the fixture proves nothing.
			Assert.That(ticks, Is.EqualTo(panelVisible ? 1 : 0),
				panelVisible
					? "A LogicTicker inside a VISIBLE container did not tick. Widget.TickOuter no longer " +
					  "descends the way this fixture assumes, so its hidden-container case is now vacuous."
					: "A LogicTicker inside a HIDDEN container ticked. Widget.TickOuter has stopped gating " +
					  "on IsVisible(); the chicken-and-egg this fixture guards against is no longer real, " +
					  "and the GarrisonPanelLogic/CargoPanelLogic comments that cite it are now wrong.");
		}

		// ---- 2. WHY A DELEGATE ESCAPES THE TRAP AND A TICKER CANNOT ---------------------------------
		[Test]
		public void AHiddenChildsOwnIsVisibleDelegateIsStillInvokedByItsVisibleParent()
		{
			var calls = 0;
			var root = new BareWidget();
			var panel = new BareWidget { IsVisible = () => { calls++; return false; } };

			root.AddChild(panel);

			root.TickOuter();
			root.TickOuter();

			// This is the entire difference between the two idioms. The delegate runs from the
			// PARENT's descent, which never stops, so it gets to change its own answer. A ticker
			// runs from the panel's own descent, which is exactly what being hidden suspends.
			Assert.That(calls, Is.GreaterThanOrEqualTo(2),
				$"A child's IsVisible delegate was invoked {calls} times across two parent ticks while " +
				"answering false. If it is not invoked every tick it cannot be what flips a panel on, " +
				"and GarrisonPanelLogic/CargoPanelLogic both need a different mechanism.");
		}

		// ---- 3a. NEITHER SHIPPED PANEL WRITES Widget.Visible ----------------------------------------
		[TestCase(typeof(GarrisonPanelLogic))]
		[TestCase(typeof(CargoPanelLogic))]
		public void PanelLogicNeverAssignsWidgetVisible(Type logic)
		{
			var writes = AllFieldWrites(logic).ToList();

			// FLOOR. A scanner that resolves nothing reads as clean, and these constructors assign
			// their own readonly fields and a pile of closure fields, so a real scan is never empty.
			Assert.That(writes.Count, Is.GreaterThan(5),
				$"IL scan found only {writes.Count} field writes across {logic.Name} and its nested " +
				"types — the scanner is broken and its absence of a Widget.Visible write means nothing.");

			var visible = writes.Where(f => f.DeclaringType == typeof(Widget) && f.Name == nameof(Widget.Visible));
			Assert.That(visible, Is.Empty,
				$"{logic.Name} assigns Widget.Visible. These two panels are hidden for almost the whole " +
				"match and own their own root's visibility, so a plain Visible write is only reachable " +
				"from a tick that a hidden panel does not get — which is the bug this fixture exists " +
				"for. Drive visibility from the IsVisible delegate instead.");

			// Weak on its own -- every per-row label in both classes assigns IsVisible too, so this
			// cannot tell a working panel from a broken one. It is here so that a rewrite which
			// abandons delegates entirely fails loudly rather than quietly satisfying the negative
			// assertion above by writing nothing at all.
			var ctor = logic.GetConstructors().Single();
			Assert.That(IlScan.ScanFieldWrites(ctor)
					.Any(f => f.DeclaringType == typeof(Widget) && f.Name == nameof(Widget.IsVisible)),
				Is.True,
				$"{logic.Name}'s constructor assigns no Widget.IsVisible delegate at all.");
		}

		static IEnumerable<FieldInfo> AllFieldWrites(Type type)
		{
			// Nested types included: a lambda that captures becomes a method on a generated
			// <>c__DisplayClass, and the broken version's `panel.Visible = ...` lived in exactly
			// such a lambda rather than in the constructor body.
			var types = new[] { type }.Concat(type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));
			foreach (var t in types)
			{
				foreach (var m in t.GetMethods(AllMembers).Cast<MethodBase>().Concat(t.GetConstructors(AllMembers)))
					foreach (var f in IlScan.ScanFieldWrites(m))
						yield return f;
			}
		}

		// ---- 3b. THE SHIPPED CHROME DECLARES NO TICKER INSIDE THE PANEL -----------------------------
		[Test]
		public void GarrisonPanelDeclaresNoLogicTicker()
		{
			var chrome = MiniYaml.FromFile(FindMod("chrome", "ingame-player.yaml"));
			var panel = Descendants(chrome)
				.FirstOrDefault(n => n.Key == "Container@GARRISON_PANEL");

			Assert.That(panel, Is.Not.Null,
				"Container@GARRISON_PANEL is not in mods/ww3mod/chrome/ingame-player.yaml — this test " +
				"no longer reads the widget it claims to.");

			var tickers = Descendants(panel.Value.Nodes)
				.Where(n => n.Key != null && n.Key.StartsWith("LogicTicker", StringComparison.Ordinal))
				.Select(n => n.Key)
				.ToArray();

			Assert.That(tickers, Is.Empty,
				$"GARRISON_PANEL declares {string.Join(", ", tickers)}. Anything parented inside this " +
				"container only ticks while the container is already visible, so a ticker here cannot " +
				"be what shows the panel and is very likely a reintroduction of the original bug. If it " +
				"genuinely needs to run only while the panel is up, move this assertion and say why.");
		}

		static IEnumerable<MiniYamlNode> Descendants(IEnumerable<MiniYamlNode> nodes)
		{
			foreach (var n in nodes)
			{
				yield return n;
				foreach (var inner in Descendants(n.Value.Nodes))
					yield return inner;
			}
		}

		static string FindMod(params string[] relative)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(new[] { dir.FullName, "mods", "ww3mod" }.Concat(relative).ToArray());
				if (File.Exists(candidate))
					return candidate;
			}

			throw new FileNotFoundException("could not locate mods/ww3mod/" + string.Join("/", relative));
		}
	}
}
