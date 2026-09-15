#region Copyright & License Information
/*
 * THE TIMELINE'S RESERVED SPACE, read out of the SHIPPED CHROME rather than asserted as numbers.
 *
 * The timeline is a sibling of LOBBY_OPTIONS inside COMMON_OPTIONS_PANEL, not a child of it --
 * LobbyOptionsLogic.RebuildOptions calls RemoveChildren() on that container, so anything parented
 * there is destroyed on the first map change. Being a sibling means the option grid below it has to
 * be pushed down BY HAND, in yaml, by exactly the widget's height. Nothing enforces that at
 * runtime: get it wrong and the bar either overlaps the first section header or leaves a dead gap,
 * and both look like a drawing bug rather than a configuration one.
 *
 * That is the same reason CameoCaptionBandTest exists and reads chrome instead of restating
 * arithmetic: the bug class here is a configuration bug, and only reading the configuration catches
 * it.
 *
 * SINCE 2026-09-13 IT ALSO PINS THAT THE OPTION GRID IS ABOVE THE FOLD, which is a second
 * configuration bug of the same class and one that shipped: the map preview slot was sized as
 * 3/5 of the window while everything below it is fixed-height, so at 1440x900 LOBBY_OPTIONS began
 * at Y 582 inside a 580px viewport. The grid was two pixels below the bottom of its own scroll
 * panel. Nothing threw, nothing looked broken, and a host opening the lobby saw the timeline and
 * not one control -- against a stated user requirement that these controls be found without
 * hunting. Arithmetic that is only ever checked by eye at one window size regresses silently, so
 * it is checked here at three.
 *
 * WHAT THIS DOES NOT COVER, stated plainly: it does not catch the blank-bar deadlock this feature
 * actually shipped (see LobbyTimelineMathTest for that one), and it cannot check that the widget
 * draws anything. It reads numbers out of chrome and adds them up; whether the rows those numbers
 * describe are legible on screen is a screenshot's job.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Widgets.Logic;
using OpenRA.Support;

namespace OpenRA.Test
{
	[TestFixture]
	public class LobbyTimelineChromeTest
	{
		const string TimelineNode = "Timeline@MATCH_TIMELINE";
		const string OptionsNode = "Container@LOBBY_OPTIONS";

		// The engine's chrome lives under engine/mods/common, not mods/ww3mod, so this walks for a
		// different anchor than CameoCaptionBandTest does. Both forms are tried because the test
		// binary's depth below the repo root is a build-layout detail.
		static string FindChrome(string file)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null)
			{
				foreach (var prefix in new[] { new[] { "mods", "common", "chrome" }, new[] { "engine", "mods", "common", "chrome" } })
				{
					var candidate = Path.Combine(new[] { dir.FullName }.Concat(prefix).Append(file).ToArray());
					if (File.Exists(candidate))
						return candidate;
				}

				dir = dir.Parent;
			}

			throw new FileNotFoundException("could not locate mods/common/chrome/" + file);
		}

		static MiniYamlNode Find(IEnumerable<MiniYamlNode> nodes, string key)
		{
			foreach (var node in nodes)
			{
				if (node.Key == key)
					return node;

				var children = node.Value.NodeWithKeyOrDefault("Children");
				if (children == null)
					continue;

				var found = Find(children.Value.Nodes, key);
				if (found != null)
					return found;
			}

			return null;
		}

		static string Field(MiniYamlNode node, string key)
		{
			var field = node.Value.NodeWithKeyOrDefault(key);
			Assert.That(field, Is.Not.Null, $"{node.Key} has no {key}");
			return field.Value.Value.Trim();
		}

		// Both Y values are engine expressions of the form "<shared prefix> + <constant>". Only the
		// trailing constant is compared; the prefix must match exactly, because two rows anchored to
		// DIFFERENT expressions have no fixed spacing between them at all and the delta below would
		// be meaningless.
		static (string Prefix, int Offset) SplitOffset(string expression, string what)
		{
			var plus = expression.LastIndexOf('+');
			Assert.That(plus, Is.GreaterThan(0), $"{what} is not of the form '<expression> + <constant>': {expression}");

			var prefix = expression[..plus].Trim();
			var tail = expression[(plus + 1)..].Trim();
			Assert.That(int.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset), Is.True,
				$"{what} does not end in an integer constant: {expression}");

			return (prefix, offset);
		}

		[Test]
		public void TheOptionGridIsPushedDownByExactlyTheTimelineHeight()
		{
			var chrome = MiniYaml.FromFile(FindChrome("lobby-players.yaml"));

			var timeline = Find(chrome, TimelineNode);
			Assert.That(timeline, Is.Not.Null, $"{TimelineNode} is missing from lobby-players.yaml");

			var options = Find(chrome, OptionsNode);
			Assert.That(options, Is.Not.Null, $"{OptionsNode} is missing from lobby-players.yaml");

			var height = int.Parse(Field(timeline, "Height"), CultureInfo.InvariantCulture);
			var (timelinePrefix, timelineOffset) = SplitOffset(Field(timeline, "Y"), $"{TimelineNode} Y");
			var (optionsPrefix, optionsOffset) = SplitOffset(Field(options, "Y"), $"{OptionsNode} Y");

			Assert.That(optionsPrefix, Is.EqualTo(timelinePrefix),
				"the timeline and the option grid are anchored to different expressions, so the gap between them is not fixed.");

			Assert.That(optionsOffset - timelineOffset, Is.EqualTo(height),
				$"the option grid sits {optionsOffset - timelineOffset}px below the timeline, which is {height}px tall. "
				+ "If the gap is larger the panel shows dead space; if smaller the bar is drawn over by the first section header. "
				+ "Change both numbers together.");
		}

		// The timeline must NOT be inside LOBBY_OPTIONS: that container is emptied by
		// LobbyOptionsLogic.RebuildOptions on every rebuild, so a timeline parented there would
		// vanish the first time the host changed the map — intermittently, and only after an action,
		// which is the worst shape of bug to find by hand.
		[Test]
		public void TheTimelineIsNotAChildOfTheContainerThatGetsEmptied()
		{
			var chrome = MiniYaml.FromFile(FindChrome("lobby-players.yaml"));
			var options = Find(chrome, OptionsNode);
			Assert.That(options, Is.Not.Null);

			var children = options.Value.NodeWithKeyOrDefault("Children");
			var nested = children == null ? null : Find(children.Value.Nodes, TimelineNode);

			Assert.That(nested, Is.Null,
				$"{TimelineNode} is inside {OptionsNode}, which LobbyOptionsLogic.RebuildOptions clears with RemoveChildren(). "
				+ "It must be a sibling.");
		}
		// ==================== THE OPTION GRID MUST BE ABOVE THE FOLD ====================

		// Row heights, read from the templates in the same file rather than restated, so a spacing
		// pass that changes them changes this test's arithmetic with it.
		static int TemplateHeight(IEnumerable<MiniYamlNode> chrome, string node)
		{
			var found = Find(chrome, node);
			Assert.That(found, Is.Not.Null, $"{node} is missing from lobby-players.yaml");
			return int.Parse(Field(found, "Height"), CultureInfo.InvariantCulture);
		}

		static int Eval(string expression, int windowHeight, int parentHeight)
		{
			return new IntegerExpression(expression).Evaluate(new Dictionary<string, int>
			{
				{ "WINDOW_HEIGHT", windowHeight },
				{ "PARENT_HEIGHT", parentHeight },
			});
		}

		// 1080 and 1200 are the two sizes a host plausibly runs; 900 was the locked design's stage
		// and is temporarily excluded -- see the block below. The failure this guards was NOT
		// confined to small windows -- at 1500 the Escalation header was still clipped -- so testing
		// one size would have reproduced the original mistake.
		// ==== 900 IS TEMPORARILY OUT, AND THIS IS THE NOTE THAT PUTS IT BACK ====
		//
		// Excluded 2026-09-15, when the budget below was corrected from its hand-counted four
		// Escalation dropdowns to the real count. THE 900 CASE WAS NEVER PASSING ON ITS MERITS: with
		// the true row counts it needs 608px (grid top 336 + Match 128 + Escalation 144) against a
		// 580px viewport and overflows by 28. That was equally true at `a755942e` and at every ref
		// since `nuclear-posture` and `nuclear-retaliation-window` joined the section -- the stale
		// literal was hiding it, which is the fail-open direction this fixture exists to prevent.
		// Logged in WORKSPACE/bugs/discovered.md (2026-09-15).
		//
		// TWO CHANGES CLOSE IT, AND BOTH ARE NEEDED -- either alone leaves Escalation at five
		// dropdowns, which still ceils to two rows:
		//   1. Game mode moving from Escalation into Match (DONE on wt/lobby-cleanup): 6 -> 5.
		//   2. `wt/exchange-v2` deleting the Retaliation window option (IN PROGRESS, spec 02
		//      "Removed"): 5 -> 4.
		// At four, Escalation fills the 4-column grid exactly and is ONE row: 336 + 128 + 90 = 554
		// against 580, fitting with 26px to spare.
		//
		// SO WHOEVER MERGES SECOND RE-ENABLES IT. The restoration is exactly one line -- put
		// `[TestCase(900)]` back above the two below and delete this comment. Do not adjust the
		// arithmetic to make it pass; it is derived from OptionSection now and is already correct.
		// If it still fails after both changes have landed, the row counts moved again and the
		// budget is telling you the truth: read the numbers in the failure message.
		[TestCase(1080)]
		[TestCase(1200)]
		public void TheMatchAndEscalationRowsFitAboveTheFold(int windowHeight)
		{
			var chrome = MiniYaml.FromFile(FindChrome("lobby-players.yaml"));

			// TOP_PANELS_ROOT (lobby.yaml) is Y 116, Height WINDOW_HEIGHT - 196, and the left column
			// takes its parent's full height -- so this is COMMON_OPTIONS_PANEL's PARENT_HEIGHT.
			var columnHeight = windowHeight - 196;

			var panel = Find(chrome, "ScrollPanel@COMMON_OPTIONS_PANEL");
			Assert.That(panel, Is.Not.Null, "COMMON_OPTIONS_PANEL is missing from lobby-players.yaml");
			var viewport = Eval(Field(panel, "Height"), windowHeight, columnHeight);

			var options = Find(chrome, OptionsNode);
			var gridTop = Eval(Field(options, "Y"), windowHeight, columnHeight);

			var header = TemplateHeight(chrome, "Container@SECTION_HEADER_TEMPLATE");
			var dropdownRow = TemplateHeight(chrome, "Container@DROPDOWN_ROW_TEMPLATE");
			var checkboxRow = TemplateHeight(chrome, "Container@CHECKBOX_ROW_TEMPLATE");

			// ==== THE ROW COUNTS ARE DERIVED, NOT COUNTED BY HAND (corrected 2026-09-15) ====
			//
			// This block used to read "Escalation renders Game mode, Opening phase, No-rush period
			// and First warheads -- four, which is exactly the grid's column count, so they are ONE
			// row", and budgeted one dropdown row accordingly. Its own next sentence promised that
			// "if a fifth Escalation option is ever added it becomes two rows and this test is what
			// says the budget no longer holds" -- and then a fifth and a sixth WERE added (the
			// exchange's nuclear-posture and nuclear-retaliation-window, DisplayOrder 24 and 25) and
			// nothing fired, because the four was a literal in this file rather than a reading of
			// LobbyOptionsLogic.OptionSection. An understated budget FAILS OPEN: it passes while the
			// host really does have to scroll, which is the one outcome this fixture exists to catch.
			//
			// So both counts now come from the renderer's own table. What stays stated here is the
			// one thing that table cannot know -- which options are CHECKBOXES, since the row
			// template is chosen by the C# type of the LobbyOption and not by its section.
			const int columns = 4;

			var matchOptions = LobbyOptionsLogic.SectionOptionCount(LobbyOptionsLogic.SectionMatch);
			var escalationOptions = LobbyOptionsLogic.SectionOptionCount(LobbyOptionsLogic.SectionEscalation);

			// Match: Game mode, Game Speed and Time Limit are dropdowns; Nuclear ending is the only
			// checkbox in the section. Game mode leads it (DisplayOrder 9) as of 2026-09-15 -- it was
			// in Escalation, which left that header drawing over a lone dropdown in Skirmish.
			const int matchCheckboxes = 1;
			Assert.That(matchOptions, Is.EqualTo(4),
				"the Match section's dropdown/checkbox split is stated here because OptionSection cannot know it. "
				+ $"It now maps {matchOptions} options, not 4 -- re-derive matchCheckboxes below before trusting this budget.");

			// RenderFlatOptions packs a run of same-type options `columns` to a row.
			static int Rows(int options, int columns) => (options + columns - 1) / columns;

			var match = header + Rows(matchOptions - matchCheckboxes, columns) * dropdownRow + Rows(matchCheckboxes, columns) * checkboxRow;

			// Every Escalation option is a dropdown, and every one of them is mode-gated -- so this
			// is the budget in ESCALATION, and in Skirmish the section does not draw at all.
			var escalation = header + Rows(escalationOptions, columns) * dropdownRow;

			Assert.That(gridTop + match + escalation, Is.LessThanOrEqualTo(viewport),
				$"at 1440x{windowHeight} the option grid starts at Y {gridTop} and needs "
				+ $"{match + escalation}px for the Match ({matchOptions} options) and Escalation "
				+ $"({escalationOptions} options) sections, which runs past the {viewport}px scroll "
				+ "viewport, so a host would have to scroll to reach the bottom of Escalation. "
				+ "The map preview slot above it is what to shrink -- see MAP_PREVIEW_ROOT.");
		}

		// The overlay that replaces the preview when CHANGE MAP is active lives in a DIFFERENT FILE
		// and is not derived from the preview -- it restates the same expression. Two copies of one
		// number is exactly the shape that drifts, and the symptom is remote from the cause: a map
		// browser drawn over the option grid, or stopping short of the slot it replaces.
		[Test]
		public void TheMapBrowserOverlayIsTheSameHeightAsThePreviewItCovers()
		{
			var preview = Find(MiniYaml.FromFile(FindChrome("lobby-players.yaml")), "Container@MAP_PREVIEW_ROOT");
			Assert.That(preview, Is.Not.Null, "MAP_PREVIEW_ROOT is missing from lobby-players.yaml");

			var browse = Find(MiniYaml.FromFile(FindChrome("lobby.yaml")), "Container@MAP_BROWSE_ROOT");
			Assert.That(browse, Is.Not.Null, "MAP_BROWSE_ROOT is missing from lobby.yaml");

			// Compared by VALUE across the window sizes above, not by string: the two are written
			// against different symbols (the preview keys off WINDOW_HEIGHT, the overlay off its
			// parent's PARENT_HEIGHT, which is the same number) so a textual match would be wrong.
			foreach (var windowHeight in new[] { 768, 900, 1080, 1200, 1500 })
			{
				var previewHeight = Eval(Field(preview, "Height"), windowHeight, windowHeight - 196);
				var browseHeight = Eval(Field(browse, "Height"), windowHeight, windowHeight);

				Assert.That(browseHeight, Is.EqualTo(previewHeight),
					$"at 1440x{windowHeight} MAP_BROWSE_ROOT is {browseHeight}px over a {previewHeight}px preview slot.");
			}
		}
	}
}
