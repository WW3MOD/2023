#region Copyright & License Information
/*
 * A Lua binding that dispatches into UI handler code must do it inside Sync.RunUnsynced.
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
 *
 * SCOPE, WIDENED 2026-09-02. The four fixes and the first version of this fixture were both scoped
 * to TestGlobal, which is one of 22 registered ScriptGlobals — so the property above was pinned for
 * a single type while the docstring claimed it of "a binding". An audit of the other 21 found no
 * second instance (they are overwhelmingly value math and trait pokes; none invokes a widget
 * handler), so this widening fixes no bug. It exists because "no instance today" is exactly the
 * state TestGlobal was in for months, and the type set is the one axis along which this fixture
 * could go stale without anyone noticing.
 *
 * KNOWN EXEMPTION: property accessors. PublicBindingNames filters IsSpecialName, so `Camera.Position`
 * — assignable from Lua, and its setter reaches the client-only Viewport — is not scanned. No global
 * property accessor dispatches a handler today (checked 2026-09-02), and nothing on the Viewport
 * carries an AssertUnsynced, so this is a coverage gap rather than a live hole. Left deliberately:
 * widening the member set is a second, independently-motivated change and belongs with a finding.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OpenRA.Mods.Common.Scripting.Global;
using OpenRA.Scripting;

namespace OpenRA.Test
{
	[TestFixture]
	public class ScriptGlobalSyncTest
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
		/// Every registered ScriptGlobal, not just TestGlobal. Mods.Cnc is scanned because ww3mod loads
		/// that assembly (mod.yaml Assemblies), so a global added there would be live Lua surface; it
		/// holds none today, which is why <see cref="TheScanReadsEveryScriptGlobal"/> asserts on a
		/// Mods.Common name rather than on a Cnc one that does not exist to be asserted.
		/// </summary>
		static List<Type> ScriptGlobalTypes()
		{
			var assemblies = new[]
			{
				typeof(TestGlobal).Assembly,
				typeof(OpenRA.Mods.Cnc.Traits.Infiltrates).Assembly
			};

			return assemblies
				.SelectMany(a => a.GetTypes())
				.Where(t => !t.IsAbstract && typeof(ScriptGlobal).IsAssignableFrom(t))
				.OrderBy(t => t.FullName, StringComparer.Ordinal)
				.ToList();
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

		static IEnumerable<string> PublicBindingNames(Type type)
		{
			return type
				.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
				.Where(m => !m.IsSpecialName)
				.Select(m => m.Name)
				.Distinct();
		}

		/// <summary>
		/// Counted rather than flagged, and that is the difference between this catching a partial wrap
		/// and missing one. A boolean "does this method mention Sync.RunUnsynced anywhere" is satisfied
		/// by a method that wraps ONE of its two dispatch sites and leaves the other bare — which is a
		/// real shape: ClickUnloadMenuRow has two, `row.OnClick` and the CLASS_ALL button's, and an
		/// early draft of this fixture passed with only the second wrapped. Requiring at least as many
		/// wrappers as dispatches closes that. A method that deliberately covered two dispatches with a
		/// single RunUnsynced would report here; none does, and the message says what to do about it.
		/// </summary>
		static (int Dispatches, int Wrappers, int Resolved) Classify(Type type, string name)
		{
			var dispatches = 0;
			var wrappers = 0;
			var resolved = 0;

			foreach (var m in BodyAndLambdas(type, name))
			{
				var scan = IlScan.Scan(m);
				resolved += scan.ResolvedCalls;
				foreach (var callee in scan.Callees)
				{
					if (IsDispatch(callee))
						dispatches++;

					// Only the binding's OWN body may establish the context; a wrapper found inside the
					// lambda would be Sync.RunUnsynced called from within already-dispatched code.
					if (m.Name == name && IsRunUnsynced(callee))
						wrappers++;
				}
			}

			return (dispatches, wrappers, resolved);
		}

		/// <summary>
		/// Guards the rule below against passing vacuously. IlScan is deliberately naive and a body it
		/// cannot read yields an empty result rather than throwing, so a fixture built on it reads as
		/// clean in exactly the same way whether every binding is wrapped or nothing was scanned at all.
		/// </summary>
		[Test]
		public void TheScanReadsEveryScriptGlobal()
		{
			var types = ScriptGlobalTypes();

			// 22 registered globals as of 2026-09-02. The floor is set AT the known population rather
			// than comfortably below it, so that a reflection query which silently stops matching — or
			// an assembly that stops being referenced — fails here instead of passing on a smaller set.
			Assert.That(types.Count, Is.GreaterThanOrEqualTo(22),
				$"Only {types.Count} ScriptGlobal types found, expected at least 22. Either the " +
				"reflection query has drifted or OpenRA.Test lost an assembly reference, and the rule " +
				"below is now checking a subset without saying so.");

			Assert.That(types, Does.Contain(typeof(TestGlobal)),
				"TestGlobal is not in the scanned set — it is the type this fixture was written for.");
			Assert.That(types.Select(t => t.Name), Does.Contain("UserInterfaceGlobal"),
				"UserInterfaceGlobal is not in the scanned set, so the 2026-09-02 widening past " +
				"TestGlobal has come undone and this is a single-type fixture again.");

			// TestGlobal is the only global that dispatches today, so it is also the only place the
			// detector can be shown to still work. A fixture that scanned 22 types and recognised a
			// dispatch in none of them would be indistinguishable from one whose IsDispatch had rotted.
			var testGlobalBindings = PublicBindingNames(typeof(TestGlobal)).ToList();
			Assert.That(testGlobalBindings.Count, Is.GreaterThan(40),
				"expected TestGlobal to expose dozens of bindings — a much smaller number means the " +
				"reflection filter stopped matching and the rule below is checking nothing");

			var dispatchers = types
				.SelectMany(t => PublicBindingNames(t)
					.Where(n => Classify(t, n).Dispatches > 0)
					.Select(n => (Type: t, Name: n)))
				.ToList();

			// Named rather than merely counted. "Is.Not.Empty" would still pass with three of the four
			// silently unrecognised, and an unrecognised binding is exempt from the rule below rather
			// than failing it — the quiet direction. Each of these reaches the detector by a different
			// route: PressHotkey and RunChatCommand through a named callee inside a lambda,
			// ClickProductionIcon through a named callee, ClickUnloadMenuRow only through Action.Invoke
			// recovered from a display class. A regression in any one of those paths shows up here.
			var testGlobalDispatchers = dispatchers
				.Where(d => d.Type == typeof(TestGlobal))
				.Select(d => d.Name)
				.ToList();

			foreach (var expected in new[] { "PressHotkey", "ClickProductionIcon", "ClickUnloadMenuRow", "RunChatCommand" })
				Assert.That(testGlobalDispatchers, Does.Contain(expected),
					$"`{expected}` dispatches into UI handler code but the detector no longer sees it, so " +
					"the rule below now exempts it instead of checking it. IsDispatch or BodyAndLambdas " +
					"has gone stale.");

			foreach (var (type, name) in dispatchers)
				Assert.That(Classify(type, name).Resolved, Is.GreaterThan(0),
					$"scanned no call at all in `{type.Name}.{name}` — IlScan read nothing and cannot have checked it");
		}

		/// <summary>
		/// The rule itself. Deliberately a detector rather than a list of known-bad names: a list would
		/// pin today's four and stay silent on the fifth, which is the whole failure pattern here.
		/// </summary>
		[Test]
		public void EveryUiDispatchingBindingRunsUnsynced()
		{
			foreach (var type in ScriptGlobalTypes())
			{
				foreach (var name in PublicBindingNames(type))
				{
					var (dispatches, wrappers, _) = Classify(type, name);
					if (dispatches == 0)
						continue;

					Assert.That(wrappers, Is.GreaterThanOrEqualTo(dispatches),
						$"{type.Name}.{name} makes {dispatches} dispatch call(s) into UI handler code but only " +
						$"{wrappers} of them are wrapped in Sync.RunUnsynced. " +
						"Lua runs inside the synced world tick, so the handler will throw " +
						"AssertUnsynced the moment it touches client-only state — most likely by setting " +
						"World.OrderGenerator — and will work fine until then, which is why this is a test " +
						"and not a review comment. Wrap the dispatch the way DefaultInputHandler.OnKeyInput " +
						"does (InputHandler.cs:40): Sync.RunUnsynced(Context.World, () => ...).");
				}
			}
		}
	}
}
