#region Copyright & License Information
/*
 * A Lua test binding that dispatches into UI handler code must do it inside Sync.RunUnsynced.
 *
 * Lua runs inside the SYNCED world tick. Real input does not: DefaultInputHandler.OnKeyInput
 * (InputHandler.cs:40) wraps Ui.HandleKeyPress in Sync.RunUnsynced, and that wrapper is what
 * licenses a handler to touch client-only state. A binding that dispatches the same handler bare
 * from Lua therefore runs it in a context the handler was never written for, and the one piece of
 * state that notices — World.OrderGenerator, whose setter opens with Sync.AssertUnsynced
 * (World.cs:170) — throws and kills the run.
 *
 * WHY THIS NEEDS A TEST AND NOT A CODE REVIEW: the failure is invisible until a specific caller
 * arrives. Test.PressHotkey shipped bare and worked for the unload menu, the resupply bar and the
 * evac hotkeys for months, because none of those handlers touch client-only state. It threw the
 * first time anything asked it to engage a command-bar mode, and the honest reading of that is not
 * "PressHotkey was broken" but "no autotest had ever been able to engage a mode, and nothing said
 * so". ClickProductionIcon carried the identical latent bug at the same time — a left click on a
 * COMPLETED BUILDING icon reaches PickUpCompletedBuildingIcon (ProductionPaletteWidget.cs:384) —
 * and had survived only because every existing caller happens to click a unit.
 *
 * So the property being pinned is not "these four methods are correct today". It is that a binding
 * CANNOT acquire a dispatch call without also acquiring the wrapper, which is the only way to stop
 * the fifth instance of this from being found by a crash months later.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OpenRA.Mods.Common.Scripting.Global;

namespace OpenRA.Test
{
	[TestFixture]
	public class TestGlobalSyncTest
	{
		/// <summary>
		/// Entry points that run handler code written for the unsynced input context. Matched by name
		/// because the declaring types are spread across the widget layer and the mod's command layer.
		///
		/// `Invoke` is restricted to a bare <see cref="Action"/> on purpose, and the boundary is load
		/// bearing rather than incidental: a widget handler is an `Action` (`someButton.OnClick()`),
		/// while a widget ACCESSOR is a `Func&lt;T&gt;` (`label.GetText()`). Matching every delegate
		/// caught ForceDesyncAndCapture, which invokes `GetText` only to read a label's string — a read,
		/// in a callback that does not even run on the Lua tick. Rejecting a real finding to keep a rule
		/// quiet would be the wrong trade; narrowing it to the shape that actually dispatches is not.
		/// The known cost is that a handler typed `Action&lt;T&gt;` would slip through.
		/// </summary>
		static bool IsDispatch(MethodBase m)
		{
			switch (m.Name)
			{
				case "HandleKeyPress":
				case "SimulateIconClick":
				case "InvokeCommand":
					return true;
				case "Invoke":
					return m.DeclaringType == typeof(Action);
				default:
					return false;
			}
		}

		static bool IsRunUnsynced(MethodBase m)
		{
			return m.Name == "RunUnsynced" && m.DeclaringType == typeof(Sync);
		}

		/// <summary>
		/// A method's own body plus the bodies of its lambdas. Both halves are needed and neither is
		/// optional: IlScan follows call/callvirt/newobj only, and a lambda is reached by `ldftn`, which
		/// it does not decode — so after a dispatch is wrapped, the dispatch call moves into a closure
		/// that a body-only scan can no longer see, and the rule would go quiet exactly when it starts
		/// being satisfied. Lambdas are recovered by Roslyn's naming convention instead: they live on
		/// the declaring type or a nested display class, named `&lt;Caller&gt;b__N`.
		/// </summary>
		static List<MethodBase> BodyAndLambdas(Type type, string methodName)
		{
			const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic
				| BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

			var marker = "<" + methodName + ">";
			var found = new List<MethodBase>();
			found.AddRange(type.GetMethods(All).Where(m => m.Name == methodName));

			foreach (var m in type.GetMethods(All))
				if (m.Name.Contains(marker, StringComparison.Ordinal))
					found.Add(m);

			foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
				foreach (var m in nested.GetMethods(All))
					if (m.Name.Contains(marker, StringComparison.Ordinal))
						found.Add(m);

			return found;
		}

		static IEnumerable<string> PublicBindingNames()
		{
			return typeof(TestGlobal)
				.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
				.Where(m => !m.IsSpecialName)
				.Select(m => m.Name)
				.Distinct();
		}

		static (bool Dispatches, bool Wrapped, int Resolved) Classify(string name)
		{
			var dispatches = false;
			var wrapped = false;
			var resolved = 0;

			foreach (var m in BodyAndLambdas(typeof(TestGlobal), name))
			{
				var scan = IlScan.Scan(m);
				resolved += scan.ResolvedCalls;
				foreach (var callee in scan.Callees)
				{
					if (IsDispatch(callee))
						dispatches = true;

					// Only the binding's OWN body may establish the context; a wrapper found inside the
					// lambda would be Sync.RunUnsynced called from within already-dispatched code.
					if (m.Name == name && IsRunUnsynced(callee))
						wrapped = true;
				}
			}

			return (dispatches, wrapped, resolved);
		}

		/// <summary>
		/// Guards the rule below against passing vacuously. IlScan is deliberately naive and a body it
		/// cannot read yields an empty result rather than throwing, so a fixture built on it reads as
		/// clean in exactly the same way whether every binding is wrapped or nothing was scanned at all.
		/// </summary>
		[Test]
		public void TheScanActuallyReadsTestGlobal()
		{
			var names = PublicBindingNames().ToList();
			Assert.That(names.Count, Is.GreaterThan(40),
				"expected TestGlobal to expose dozens of bindings — a much smaller number means the " +
				"reflection filter stopped matching and the rule below is checking nothing");

			var dispatchers = names.Where(n => Classify(n).Dispatches).ToList();

			// Named rather than merely counted. "Is.Not.Empty" would still pass with three of the four
			// silently unrecognised, and an unrecognised binding is exempt from the rule below rather
			// than failing it — the quiet direction. Each of these reaches the detector by a different
			// route: PressHotkey and RunChatCommand through a named callee inside a lambda,
			// ClickProductionIcon through a named callee, ClickUnloadMenuRow only through Action.Invoke
			// recovered from a display class. A regression in any one of those paths shows up here.
			foreach (var expected in new[] { "PressHotkey", "ClickProductionIcon", "ClickUnloadMenuRow", "RunChatCommand" })
				Assert.That(dispatchers, Does.Contain(expected),
					$"`{expected}` dispatches into UI handler code but the detector no longer sees it, so " +
					"the rule below now exempts it instead of checking it. IsDispatch or BodyAndLambdas " +
					"has gone stale.");

			foreach (var n in dispatchers)
				Assert.That(Classify(n).Resolved, Is.GreaterThan(0),
					$"scanned no call at all in `{n}` — IlScan read nothing and cannot have checked it");
		}

		/// <summary>
		/// The rule itself. Deliberately a detector rather than a list of known-bad names: a list would
		/// pin today's four and stay silent on the fifth, which is the whole failure pattern here.
		/// </summary>
		[Test]
		public void EveryUiDispatchingBindingRunsUnsynced()
		{
			foreach (var name in PublicBindingNames())
			{
				var (dispatches, wrapped, _) = Classify(name);
				if (!dispatches)
					continue;

				Assert.That(wrapped, Is.True,
					$"TestGlobal.{name} dispatches into UI handler code without wrapping it in " +
					"Sync.RunUnsynced. Lua runs inside the synced world tick, so the handler will throw " +
					"AssertUnsynced the moment it touches client-only state — most likely by setting " +
					"World.OrderGenerator — and will work fine until then, which is why this is a test " +
					"and not a review comment. Wrap the dispatch the way DefaultInputHandler.OnKeyInput " +
					"does (InputHandler.cs:40): Sync.RunUnsynced(Context.World, () => ...).");
			}
		}
	}
}
