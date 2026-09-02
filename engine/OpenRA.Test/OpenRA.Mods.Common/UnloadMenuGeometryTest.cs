#region Copyright & License Information
/*
 * WW3MOD unload-menu geometry tests — the clip height, the cliff, and the scrollbar's gutter.
 *
 * WHAT THESE EXIST TO CATCH. The transport unload menu grows to fit its 24 class rows and is capped by the
 * screen. Below roughly 578px of window height the tail is cut off. That is survivable on its own — the wheel
 * still scrolls a panel whose ScrollBar is Hidden — but for a while nothing on screen admitted it, so the
 * player had no way to know the rows existed. The invariant is therefore "clip only where you say so".
 *
 * THE ASSERTION THAT WOULD HAVE PROVEN NOTHING. CargoUnloadMenuLogic.Refresh adds every class row to the panel
 * and sizes it afterwards, so Children.Count is 24 on the broken build as well as the fixed one. A row-count
 * assertion passes in both directions. The quantity that differs is the CLIP HEIGHT, so that is what is
 * asserted here.
 *
 * THE SECOND FALSE CONTROL, WHICH IS EASY TO WALK INTO. UnloadMenuGeometry derives Overflows as
 * `contentHeight > clipHeight` from a clipHeight that is itself `Min(ceiling, contentHeight)`. Asserting
 * `Overflows == (ContentHeight > ClipHeight)` is thus a tautology: it holds no matter how wrong the ceiling is.
 * Every assertion below is against a literal derived independently from the authored chrome (see Authored
 * geometry), never against one output field's relationship to another. The live-widget form of the
 * biconditional does have teeth, because it reads a really-rendered panel, and it lives in the launched
 * scenario tools/autotest/scenarios/test-unload-menu-scrollbar.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Widgets;

namespace OpenRA.Test
{
	[TestFixture]
	public class UnloadMenuGeometryTest
	{
		// Authored geometry, read off mods/ww3mod/chrome/unload-menu.yaml. Literals rather than derived, so
		// that changing the chrome has to be a deliberate act in two places instead of silently re-baselining
		// the numbers this file is here to defend.
		const int ListY = 19;           // ScrollPanel@CLASS_LIST Y
		const int RowHeight = 22;       // ScrollItem@CLASS_TEMPLATE Height
		const int ItemSpacing = 1;      // ScrollPanel@CLASS_LIST ItemSpacing
		const int ListWidth = 178;      // ScrollPanel@CLASS_LIST Width
		const int MenuWidth = 186;      // Background@CARGO_UNLOAD_MENU Width
		const int CountRight = 174;     // Label@CLASS_COUNT: X 140 + Width 34
		const int ScrollbarWidth = 24;  // ScrollPanelWidget.ScrollbarWidth default

		// The widest legal hold: Cargo `Types: Infantry` accepts 24 distinct classes (combat, civilians,
		// pilots, ejected crews), all loadable into one 36-slot Chinook.
		const int Classes = 24;

		// ListLayout.AdjustChild seeds ContentHeight at (2 * TopBottomSpacing - ItemSpacing) — TopBottomSpacing
		// is 0 here — then adds (RowHeight + ItemSpacing) per row. 24 * 23 - 1 = 551.
		const int ContentHeight = Classes * (RowHeight + ItemSpacing) - ItemSpacing;

		// The cliff. The panel's ceiling is (screen - ListY - 2 * ScreenMargin), so the content first fits at
		// 551 + 19 + 8 = 578. This is the number unload-menu.yaml and the scrollbar scenario's description
		// both quote; if it moves, they are now lying and should be updated with this test.
		const int Cliff = ContentHeight + ListY + 2 * UnloadMenuGeometry.ScreenMargin;

		static UnloadMenuLayout MeasureAt(int screenHeight, int contentHeight = ContentHeight)
		{
			return UnloadMenuGeometry.Measure(screenHeight, ListY, RowHeight, contentHeight,
				ScrollbarWidth, ListWidth, MenuWidth);
		}

		[Test]
		public void ContentHeightForAFullHold_IsTheDocumentedFigure()
		{
			// Pins the arithmetic the other tests are built on. 551px is the figure quoted in
			// unload-menu.yaml's comment; if ListLayout or the row height changes, start here.
			Assert.That(ContentHeight, Is.EqualTo(551), "24 class rows at 22px on 1px spacing");
			Assert.That(Cliff, Is.EqualTo(578), "the window height at which those rows first fit uncut");
		}

		[Test]
		public void TheCliffIsWhereTheChromeSaysItIs()
		{
			// One pixel below the cliff the rows must not all fit, and the menu must say so.
			var below = MeasureAt(Cliff - 1);
			Assert.That(below.ClipHeight, Is.EqualTo(ContentHeight - 1),
				"at 577px the panel gets 550px for 551px of rows");
			Assert.That(below.Overflows, Is.True,
				"the tail is cut at 577px, so the scrollbar must be shown — a clip nothing advertises is the bug");

			// Exactly at the cliff everything fits, and a bar would be a lie in the other direction.
			var at = MeasureAt(Cliff);
			Assert.That(at.ClipHeight, Is.EqualTo(ContentHeight),
				"at 578px all 551px of rows fit");
			Assert.That(at.Overflows, Is.False,
				"nothing is cut at 578px, so showing a scrollbar would claim otherwise");
		}

		[Test]
		public void ClipHeightTracksTheWindowWhileTheCapBites()
		{
			// Below the cliff the panel is the window minus the chrome above and below it — NOT some fixed
			// row count. The old bug was a fixed 380px cap that held 16 rows and dropped the other 8.
			foreach (var screenHeight in new[] { 200, 300, 480, 540, 577 })
			{
				var layout = MeasureAt(screenHeight);
				Assert.That(layout.ClipHeight, Is.EqualTo(screenHeight - ListY - 2 * UnloadMenuGeometry.ScreenMargin),
					$"at {screenHeight}px the panel should take all the room the window leaves it");
				Assert.That(layout.Overflows, Is.True,
					$"551px of rows cannot fit a {screenHeight}px window, so the bar must be shown");
			}
		}

		[Test]
		public void TheScrollbarGetsAGutterInsteadOfTheCountColumn()
		{
			// This is the failure that sank the first attempt at a scrollbar here: a right-hand bar does not
			// inset the rows, so it was drawn straight over the right-aligned 'x1' counts and they vanished.
			var layout = MeasureAt(Cliff - 1);

			Assert.That(layout.Overflows, Is.True, "precondition: this size must overflow for the bar to exist");

			// Multiple, so a build that drops the widening reports the consequence (the bar landing on the
			// counts) as well as the cause, instead of stopping at the first line and hiding the one that
			// describes what the player would actually see.
			Assert.Multiple(() =>
			{
				Assert.That(layout.ListWidth, Is.EqualTo(ListWidth + ScrollbarWidth),
					"the panel widens by the bar's width so the bar has somewhere of its own to sit");
				Assert.That(layout.MenuWidth, Is.EqualTo(MenuWidth + ScrollbarWidth),
					"the menu background widens in step, or the bar hangs off the edge of it");
				Assert.That(layout.ScrollBarLeft, Is.GreaterThanOrEqualTo(CountRight),
					$"the bar starts at x={layout.ScrollBarLeft} but the count column runs to x={CountRight} — "
					+ "the bar is drawn over the counts, which is why the first one was removed");
			});
		}

		[Test]
		public void AMenuThatFitsCarriesNoBarAndNoGutter()
		{
			// The control arm. A bar shown when nothing is cut is its own defect: it eats 24px of width and
			// tells the player rows exist that do not.
			foreach (var screenHeight in new[] { Cliff, 720, 1080, 1440 })
			{
				var layout = MeasureAt(screenHeight);
				Assert.That(layout.Overflows, Is.False, $"all 551px of rows fit a {screenHeight}px window");
				Assert.That(layout.ClipHeight, Is.EqualTo(ContentHeight), "the panel shrinks to its content");
				Assert.That(layout.ListWidth, Is.EqualTo(ListWidth), "no bar means no gutter");
				Assert.That(layout.MenuWidth, Is.EqualTo(MenuWidth), "no bar means no gutter");
				Assert.That(layout.ScrollBarLeft, Is.EqualTo(-1), "there is no bar to place");
			}
		}

		[Test]
		public void ThePanelNeverCollapsesBelowASingleRow()
		{
			// A hostile window size must leave one usable row rather than a sliver the player cannot click.
			// Without the Max floor a 30px window would give the panel 3px.
			foreach (var screenHeight in new[] { 1, 30, 48 })
			{
				var layout = MeasureAt(screenHeight);
				Assert.That(layout.ClipHeight, Is.EqualTo(RowHeight),
					$"a {screenHeight}px window should still show one whole row, not a sliver");
				Assert.That(layout.Overflows, Is.True, "23 of the 24 rows are cut, and that must be advertised");
			}
		}

		[Test]
		public void TheMenuFitsTheWindowWheneverTheWindowCanHoldIt()
		{
			// The menu is positioned with a ScreenMargin at the top, so its height has to leave room for that
			// or PositionAtCursor clamps it off the bottom of the screen. Swept above 48px, where the
			// single-row floor deliberately wins instead.
			for (var screenHeight = 100; screenHeight <= 1600; screenHeight += 7)
			{
				var layout = MeasureAt(screenHeight);
				Assert.That(layout.MenuHeight, Is.LessThanOrEqualTo(screenHeight - UnloadMenuGeometry.ScreenMargin),
					$"at {screenHeight}px the menu is {layout.MenuHeight}px tall and would hang off the bottom");
				Assert.That(layout.MenuHeight, Is.EqualTo(ListY + layout.ClipHeight + UnloadMenuGeometry.ScreenMargin),
					"the background is the header, the list, and one margin at its foot");
			}
		}

		[Test]
		public void AShortHoldNeverOverflowsAnyPlausibleWindow()
		{
			// Guards the other direction of the cliff: a transport carrying three classes must not get a
			// scrollbar just because the window is small-ish. 3 rows is 68px.
			var threeRows = 3 * (RowHeight + ItemSpacing) - ItemSpacing;
			Assert.That(threeRows, Is.EqualTo(68));

			var layout = MeasureAt(480, threeRows);
			Assert.That(layout.Overflows, Is.False, "68px of rows fit a 480px window with room to spare");
			Assert.That(layout.ClipHeight, Is.EqualTo(threeRows), "the panel shrinks to the three rows");
			Assert.That(layout.ListWidth, Is.EqualTo(ListWidth), "and takes no gutter");
		}
	}
}
