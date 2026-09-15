#region Copyright & License Information
/*
 * WW3MOD (2026-09-16). A WIDGET THAT SUPPRESSES ITSELF INSIDE Draw() IS STILL MOUSE-LIVE, and three
 * shipped banners proved it: the mouse pointer fell back to the bare default in horizontal bands
 * across the upper-middle of the screen, for the whole match, wherever the invisible strips sat.
 *
 * ==== THE MECHANISM, WHICH IS ENTIRELY IN Widget AND NOT IN THE BANNERS ====
 * Game.cs:930 sets the cursor every rendered frame from Ui.Root.GetCursorOuter(LastMousePos). That
 * walk (Widget.cs:399-415) returns null only when `!IsVisible() || !EventBoundsContains(pos)`, and
 * otherwise descends Children IN REVERSE taking the first non-null answer. `Visible` defaults true
 * (Widget.cs:222) and an early `return` inside Draw() never touches it -- so a widget that draws
 * nothing still passes the IsVisible test, still claims the inherited EventBounds => RenderBounds
 * (Widget.cs:327), and still answers with the inherited default cursor (Widget.cs:398).
 *
 * PLAYER_ROOT is declared AFTER WorldInteractionController@INTERACTION_CONTROLLER (mods/common
 * chrome/ingame.yaml:46 vs :71), AddChild appends, and the walk is reverse -- so the HUD is asked
 * FIRST and its "default" beats the world's move/attack/attack-move cursor. Anything full-bleed in
 * the player HUD that answers the cursor at all therefore blanks the world underneath it.
 *
 * ==== WHAT THIS FIXTURE PINS, AND WHY IT IS THE YAML SIDE RATHER THAN AN IL SCAN ====
 * The defect's precondition is a CONJUNCTION -- big bounds AND no EventBounds override AND no
 * GetCursor override -- and only the first term lives in YAML. Detecting the fourth term ("guarded
 * early return in Draw()") by IL is what a scan would have to do, and a guarded return is not a
 * shape IL distinguishes from any other branch: every Draw() in the tree branches. So this asserts
 * the three terms that ARE decidable, which is strictly stronger than the four-term version anyway:
 * a full-bleed HUD widget that answers the default cursor is wrong whether or not it currently has
 * a suppression guard, because the guard is one commit away from being added.
 *
 * NOTHING HERE CONSTRUCTS A WIDGET. OpenRA.Test can build neither a Renderer nor a World (see
 * DefconReadoutTest's header), so the chrome is read as MiniYaml and the classes by reflection.
 * That is also why the fixture cannot check RenderBounds: it checks DECLARED bounds instead, which
 * is the thing a future author actually writes.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Test
{
	[TestFixture]
	public class OverlayWidgetEventBoundsTest
	{
		// The two chrome files that overlay the live world. A menu widget answering the default
		// cursor is answering over a menu, where default is the right answer.
		static readonly string[] WorldChrome = { "ingame-player.yaml", "ingame-observer.yaml" };

		const BindingFlags AllMembers = BindingFlags.Instance | BindingFlags.Public
			| BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

		// ---- 1. THE THREE BANNERS, BY NAME ----------------------------------------------------------
		// Named rather than discovered, so that deleting one from the chrome does not silently empty
		// the assertion. These are the classes the 2026-09-16 fix was for.
		[TestCase("DefconTransitionBannerWidget")]
		[TestCase("NuclearArmedBannerWidget")]
		[TestCase("FinalExchangeBannerWidget")]
		[TestCase("DefconReadoutWidget")]
		public void TheEscalationHudOverlaysClaimNoEventBounds(string typeName)
		{
			var type = WidgetType(typeName);
			Assert.That(type, Is.Not.Null, $"{typeName} no longer exists; this fixture names a dead class.");

			Assert.That(DeclaresEventBounds(type), Is.True,
				$"{typeName} does not override EventBounds. It is a non-interactive full-width HUD band " +
				"that suppresses itself inside Draw(), which does NOT make it invisible: Visible stays " +
				"true (Widget.cs:222), so GetCursorOuter (Widget.cs:399-415) still matches its inherited " +
				"RenderBounds (Widget.cs:327) and answers the default cursor (Widget.cs:398), blanking " +
				"the world's move/attack cursor across the whole strip. Restore " +
				"`public override Rectangle EventBounds => Rectangle.Empty;` (DefconReadoutWidget.cs:245).");
		}

		// ---- 2. THE GENERAL RULE, OVER WHATEVER THE CHROME CURRENTLY DECLARES ------------------------
		// This is the half that catches the banner-shaped widget written next month. It walks the two
		// world-overlay chrome files, keeps every widget whose DECLARED bounds are full-window-width,
		// and requires each to have answered the cursor question deliberately one way or another.
		[Test]
		public void EveryFullBleedWorldOverlayAnswersTheCursorQuestionDeliberately()
		{
			var offenders = new List<string>();
			var examined = 0;

			foreach (var file in WorldChrome)
			{
				foreach (var node in Descendants(MiniYaml.FromFile(FindMod("chrome", file))))
				{
					if (!IsFullBleed(node))
						continue;

					var type = WidgetType(TypeNameOf(node.Key) + "Widget");
					if (type == null)
						continue;

					examined++;

					// ANY ONE OF THESE IS A DELIBERATE ANSWER. An EventBounds override says "these are
					// the pixels I own"; a GetCursor override says "this is what I answer, possibly
					// null"; an input override says "I am interactive and my bounds are load-bearing"
					// -- and for that last one emptying the bounds would BREAK the widget, so it must
					// not be reported as an offender. What is left is the defect shape: a full-window
					// rectangle that has never considered the question and answers "default" by
					// inheritance.
					if (DeclaresEventBounds(type) || Declares(type, "GetCursor") || IsInteractive(type))
						continue;

					offenders.Add($"{type.Name} (as {node.Key} in mods/ww3mod/chrome/{file})");
				}
			}

			// FLOOR. If the YAML walk or the type resolution silently stops working, zero widgets are
			// examined, no offender is found, and the fixture passes while checking nothing. The three
			// banners plus the readout are full-bleed in ingame-player.yaml, so a real walk is never
			// anywhere near empty.
			Assert.That(examined, Is.GreaterThan(3),
				$"Only {examined} full-bleed widgets were resolved across {string.Join(", ", WorldChrome)}. " +
				"The chrome walk or the `<Name>Widget` resolution (WidgetLoader.cs:85) has broken, and " +
				"this fixture's empty offender list means nothing.");

			Assert.That(offenders, Is.Empty,
				$"These full-window-width widgets sit over the live world, override neither EventBounds " +
				$"nor GetCursor, and take no mouse input: {string.Join("; ", offenders)}.\n" +
				"Such a widget answers the inherited default cursor (Widget.cs:398) across its whole " +
				"rectangle whenever the pointer is inside it -- and because PLAYER_ROOT is walked before " +
				"the world widget (ingame.yaml:46 vs :71, reverse order at Widget.cs:407), that default " +
				"BEATS the world's move/attack cursor. Suppressing the widget inside Draw() does not " +
				"help: Visible stays true (Widget.cs:222) and GetCursorOuter never looks at Draw().\n" +
				"If it is a non-interactive overlay, add `public override Rectangle EventBounds => " +
				"Rectangle.Empty;` (precedent: DefconReadoutWidget.cs:245). If it IS interactive, give " +
				"it a real GetCursor or narrow its EventBounds to the pixels it actually owns.");
		}

		// ---- 3. THE ENGINE PREMISE THE OTHER TWO REST ON --------------------------------------------
		// Without this, both assertions above are statements about a rule that may no longer exist.
		//
		// IT STOPS ONE LINK SHORT OF THE CURSOR STRING, DELIBERATELY. GetCursorOuter's final answer is
		// `defaultCursor`, which Widget.Initialize assigns from ChromeMetrics (Widget.cs:283) -- and
		// Initialize needs a ModData this harness cannot build, so an uninitialised widget answers null
		// whatever its bounds are. Asserting on the string therefore tests the harness, not the engine:
		// the first draft of this fixture did exactly that, and its "control" case passed for the same
		// reason its subject failed. What IS decidable here is the pair of TESTS GetCursorOuter gates
		// on (Widget.cs:402) -- IsVisible() and EventBoundsContains -- and those are the whole defect.
		[Test]
		public void AWidgetSuppressedOnlyInDrawIsStillVisibleAndStillClaimsItsBounds()
		{
			var plain = new BareWidget { Bounds = new WidgetBounds(0, 0, 400, 100) };

			// THE TRAP ITSELF. Nothing about drawing has any bearing on either half, which is why a
			// guard inside Draw() suppresses pixels and nothing else.
			Assert.That(plain.IsVisible(), Is.True,
				"A freshly constructed Widget is no longer Visible by default. The premise that a " +
				"Draw()-side guard leaves a widget mouse-live has changed; re-read Widget.cs:222.");

			Assert.That(plain.EventBoundsContains(new int2(200, 50)), Is.True,
				"A plain Widget no longer claims the point inside its own Bounds. The inherited " +
				"EventBounds => RenderBounds (Widget.cs:327) has changed, and the " +
				"EventBounds => Rectangle.Empty guards on the Escalation banners may now be " +
				"unnecessary -- verify before removing any of them.");

			// And the control: the guard is what actually releases the point. Both halves are asserted
			// from one body so that a harness in which nothing contains anything cannot read as a pass.
			var guarded = new EmptyBoundsWidget { Bounds = new WidgetBounds(0, 0, 400, 100) };
			Assert.That(guarded.EventBoundsContains(new int2(200, 50)), Is.False,
				"EventBounds => Rectangle.Empty no longer releases a point inside the widget's Bounds. " +
				"The fix applied to the three Escalation banners does not work any more.");

			// GetCursorOuter gates on exactly the conjunction asserted above (Widget.cs:402), so the
			// guarded widget is silent at the walk level too. This half is safe to assert in the
			// null direction precisely because the harness cannot produce a non-null cursor.
			Assert.That(guarded.GetCursorOuter(new int2(200, 50)), Is.Null,
				"A widget with empty EventBounds answered GetCursorOuter.");
		}

		sealed class BareWidget : Widget { }

		sealed class EmptyBoundsWidget : Widget
		{
			public override Rectangle EventBounds => Rectangle.Empty;
		}

		// ---- resolution helpers ---------------------------------------------------------------------

		// A widget node is `Type@ID:` or bare `Type:`; the class is the type name plus "Widget"
		// (WidgetLoader.cs:85).
		static string TypeNameOf(string key)
		{
			if (key == null)
				return null;

			var at = key.IndexOf('@');
			return at < 0 ? key : key[..at];
		}

		static Type WidgetType(string name)
		{
			return typeof(Widget).Assembly.GetTypes()
				.Concat(typeof(OpenRA.Mods.Common.Widgets.DefconReadoutWidget).Assembly.GetTypes())
				.FirstOrDefault(t => t.Name == name && typeof(Widget).IsAssignableFrom(t));
		}

		// DECLARED full-window width. The defect needs a rectangle big enough to cover playfield the
		// player is trying to click; WINDOW_WIDTH is the shape all three banners had and is the one
		// that is unambiguous without evaluating the expression (which needs a Renderer).
		static bool IsFullBleed(MiniYamlNode node)
		{
			if (node.Key == null || !node.Key.Contains('@'))
				return false;

			var width = node.Value.Nodes.FirstOrDefault(n => n.Key == "Width")?.Value.Value;
			return width != null && width.Contains("WINDOW_WIDTH", StringComparison.Ordinal);
		}

		static bool DeclaresEventBounds(Type type)
		{
			for (var t = type; t != null && t != typeof(Widget); t = t.BaseType)
				if (t.GetProperty("EventBounds", AllMembers) != null)
					return true;

			return false;
		}

		static bool Declares(Type type, string method)
		{
			for (var t = type; t != null && t != typeof(Widget); t = t.BaseType)
				if (t.GetMethod(method, AllMembers) != null)
					return true;

			return false;
		}

		// Anything that takes mouse input or hover owns its bounds for a reason, and emptying them
		// would break it. ObserverArmyIconsWidget and its two siblings are the live examples: they
		// read EventBounds.Contains(LastMousePos) to drive their own tooltips.
		static bool IsInteractive(Type type)
		{
			return Declares(type, "HandleMouseInput") || Declares(type, "HandleMouseMove")
				|| Declares(type, "MouseEntered") || Declares(type, "MouseExited")
				|| Declares(type, "TakeMouseFocus") || Declares(type, "YieldMouseFocus")
				|| Declares(type, "EventBoundsContains");
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
