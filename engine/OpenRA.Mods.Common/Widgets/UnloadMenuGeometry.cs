#region Copyright & License Information
/*
 * WW3MOD unload-menu geometry — how tall the class list gets, and when it admits it is cut (pure math).
 *
 * WHY THIS IS A SEPARATE CLASS. The sizing lived inline in CargoUnloadMenuLogic.Refresh, which needs a live
 * Renderer, a loaded chrome tree and an open menu to run at all — so the only check on it was a launched
 * autotest, run by hand at two window sizes. Lifting the arithmetic out lets NUnit pin it in the gate.
 *
 * THE FALSE CONTROL THIS EXISTS TO AVOID. Refresh adds EVERY class row to the panel and only then sizes it,
 * so the row count is 24 whether or not the tail is reachable — an assertion on Children.Count passes on the
 * broken build too. The quantity that actually differs is the CLIP HEIGHT: the panel is capped by the screen,
 * and below roughly 578px of window height the 24 rows (551px) stop fitting. Everything here is expressed in
 * those pixels for that reason.
 *
 * ...AND THE SECOND ONE, which is subtler and cost a rewrite of the first RED attempt. `Overflows` is derived
 * as `contentHeight > clipHeight` where `clipHeight = Min(ceiling, contentHeight)`, so asserting
 * `Overflows == (ContentHeight > ClipHeight)` is a TAUTOLOGY at this level — it cannot fail however wrong the
 * geometry is. UnloadMenuGeometryTest therefore asserts against INDEPENDENTLY DERIVED literals (the cliff sits
 * at 578, the bar's left edge lands at 178) rather than against these fields' relationship to each other. The
 * live-widget form of the biconditional is still worth running, but it is only meaningful against a real
 * rendered panel, which is where it lives: tools/autotest/scenarios/test-unload-menu-scrollbar.
 *
 * CLIPPING IS NOT DATA LOSS. ScrollPanelWidget handles Scroll whatever ScrollBar is set to
 * (ScrollPanelWidget.cs:342-346), so a hidden bar still leaves the tail reachable by wheel. The defect was
 * that nothing on screen SAID so. Hence the invariant is not "never clip", it is "clip only where you say so".
 */
#endregion

using System;

namespace OpenRA.Mods.Common.Widgets
{
	/// <summary>The measured result of <see cref="UnloadMenuGeometry.Measure"/>. Pixels, list-local.</summary>
	public readonly struct UnloadMenuLayout
	{
		/// <summary>Height the scroll panel gives the rows. Less than the content height when the screen caps it.</summary>
		public int ClipHeight { get; }

		/// <summary>Height of the whole menu background, header included.</summary>
		public int MenuHeight { get; }

		/// <summary>Panel width, widened by the scrollbar's width when the bar is shown so it gets its own gutter.</summary>
		public int ListWidth { get; }

		/// <summary>Menu width, widened in step with <see cref="ListWidth"/>.</summary>
		public int MenuWidth { get; }

		/// <summary>True when the rows do not all fit and the scrollbar must be shown to admit it.</summary>
		public bool Overflows { get; }

		/// <summary>Left edge of the scrollbar in list-local x, or -1 when it is hidden. Compare against the
		/// right edge of a row's count column to prove the bar is beside the counts rather than on top of them.</summary>
		public int ScrollBarLeft { get; }

		public UnloadMenuLayout(int clipHeight, int menuHeight, int listWidth, int menuWidth, bool overflows, int scrollBarLeft)
		{
			ClipHeight = clipHeight;
			MenuHeight = menuHeight;
			ListWidth = listWidth;
			MenuWidth = menuWidth;
			Overflows = overflows;
			ScrollBarLeft = scrollBarLeft;
		}
	}

	/// <summary>Sizing for the class-grouped unload menu. Pure: no widget, no renderer, no RNG.</summary>
	public static class UnloadMenuGeometry
	{
		/// <summary>Gap kept between the menu and the edge of the screen, and between the list and the menu's foot.</summary>
		public const int ScreenMargin = 4;

		/// <summary>
		/// Size the list to its content, capped by the screen, and decide whether the cap has to be advertised.
		/// </summary>
		/// <param name="screenHeight">Renderer resolution height.</param>
		/// <param name="listY">Top of the scroll panel inside the menu (below the header).</param>
		/// <param name="rowHeight">Height of one class row; the floor the panel is never squeezed under.</param>
		/// <param name="contentHeight">What the rows need, as ScrollPanelWidget.ContentHeight reports it.</param>
		/// <param name="scrollbarWidth">ScrollPanelWidget.ScrollbarWidth.</param>
		/// <param name="listWidth">Panel width AS AUTHORED, before any gutter — never a previously widened one.</param>
		/// <param name="menuWidth">Menu width AS AUTHORED, before any gutter.</param>
		public static UnloadMenuLayout Measure(int screenHeight, int listY, int rowHeight, int contentHeight,
			int scrollbarWidth, int listWidth, int menuWidth)
		{
			// Cap by the screen rather than by a fixed row count. A fixed 380px held 16 rows, chosen for the 16
			// combat classes — but Cargo `Types: Infantry` also accepts civilians, pilots and ejected vehicle
			// crews, which is 24 distinct classes, all loadable into one 36-slot Chinook. The Max floor keeps a
			// hostile window size from collapsing the panel to a sliver: one row is always shown.
			var ceiling = Math.Max(rowHeight, screenHeight - listY - 2 * ScreenMargin);
			var clipHeight = Math.Min(ceiling, contentHeight);

			var overflows = contentHeight > clipHeight;

			// Widen by the bar's width when turning it on. A right-hand bar does NOT inset the rows
			// (ScrollPanelWidget.ChildOrigin, ScrollPanelWidget.cs:236), and the rows keep the width they were
			// cloned at — so the widening is what gives the bar somewhere to sit that is not on top of the count
			// column. Note the consequence the test pins: the bar's left edge lands exactly on the AUTHORED list
			// width, which is by construction clear of anything laid out inside that width.
			var gutter = overflows ? scrollbarWidth : 0;
			var panelWidth = listWidth + gutter;

			// Derived from the FINAL width, exactly as ScrollPanelWidget places the bar (rb.Right -
			// ScrollbarWidth, ScrollPanelWidget.cs:178-183) and as TestGlobal.GetUnloadMenuGeometry reports it.
			// Deriving it from the authored width instead would hold this at 178 whether or not the gutter was
			// ever applied — a check on it could then not fail, which is the trap this whole file is about.
			return new UnloadMenuLayout(
				clipHeight,
				listY + clipHeight + ScreenMargin,
				panelWidth,
				menuWidth + gutter,
				overflows,
				overflows ? panelWidth - scrollbarWidth : -1);
		}
	}
}
