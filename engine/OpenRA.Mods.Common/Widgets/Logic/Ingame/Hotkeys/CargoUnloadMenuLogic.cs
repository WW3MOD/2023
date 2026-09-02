#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Lint;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic.Ingame
{
	/// <summary>
	/// Class-grouped unload menu, opened at the mouse cursor for the selected transport.
	/// One row per soldier class with a live count; click a row to drop one man, click again
	/// to drop another. The menu is sticky, so repeat clicks do not dismiss it.
	/// </summary>
	[ChromeLogicArgsHotkeys("UnloadMenuKey")]
	public class CargoUnloadMenuLogic : SingleHotkeyBaseLogic
	{
		// The menu grows to fit its content and is capped only by the screen (see Refresh). A scrollbar
		// is shown only when that cap actually bites, because ScrollPanelWidget draws the bar whenever
		// it is enabled rather than on overflow, and for a right-hand bar ChildOrigin does not inset the
		// rows (ScrollPanelWidget.cs:236) — an always-on bar therefore sat on top of the count column and
		// hid it, visible in the first capture of this menu as rows with no counts at all. Refresh widens
		// the menu by the bar's width when it turns the bar on, so it gets a gutter instead of the counts.
		//
		// The sizing itself lives in UnloadMenuGeometry so NUnit can pin it without a renderer; this file
		// keeps the widget wiring. One copy of the arithmetic, deliberately.
		const int ScreenMargin = UnloadMenuGeometry.ScreenMargin;

		readonly World world;

		Widget menu;
		MaskWidget mask;
		ScrollPanelWidget list;
		ScrollItemWidget rowTemplate;

		// Widths as authored, captured before Refresh starts adding and removing the scrollbar gutter.
		// Refresh runs repeatedly (Drop and Tick both call it), so the gutter has to be reapplied from
		// a fixed base rather than added to whatever the width happens to be now.
		int listWidth;
		int menuWidth;

		Actor transport;
		Cargo cargo;

		// The first drop replaces whatever the transport was doing; every drop after it queues.
		// Sending queued:false twice in quick succession would CancelActivity() the unload that
		// is still inside its BeforeUnloadDelay wait, and that man is then never dropped at all.
		bool hasDropped;

		[ObjectCreator.UseCtor]
		public CargoUnloadMenuLogic(Widget widget, ModData modData, World world, Dictionary<string, MiniYaml> logicArgs)
			: base(widget, modData, "UnloadMenuKey", "PLAYER_KEYHANDLER", logicArgs)
		{
			this.world = world;
		}

		protected override bool OnHotkeyActivated(KeyInput e)
		{
			var candidate = SelectedTransport();
			var openFor = transport;

			if (menu != null)
				Close();

			// Toggle shut only when the menu already belonged to what is selected now. Pressing the
			// key after switching transports RETARGETS it: closing there would make the player press
			// twice to see the transport they just clicked, for no reason they could infer. The old
			// unconditional close also made "did it open?" untestable, because both outcomes
			// returned true — which is how a capture of a stale menu passed its own assertion.
			if (candidate == null || ReferenceEquals(candidate, openFor))
				return openFor != null;

			Open(candidate);
			return true;
		}

		Actor SelectedTransport()
		{
			if (world.IsGameOver || world.LocalPlayer == null)
				return null;

			var selected = world.Selection.Actors
				.Where(a => a.Owner == world.LocalPlayer && a.IsInWorld && !a.IsDead)
				.ToArray();

			if (selected.Length != 1)
				return null;

			var c = selected[0].TraitOrDefault<Cargo>();
			if (c == null || c.IsEmpty())
				return null;

			return selected[0];
		}

		void Open(Actor candidate)
		{
			transport = candidate;
			cargo = candidate.Trait<Cargo>();
			hasDropped = false;

			menu = Ui.LoadWidget("CARGO_UNLOAD_MENU", null, new WidgetArgs());

			// Name the transport rather than the action. The menu floats over open ground at the
			// cursor, and a player could reasonably read that as "the men come out here" — they do
			// not, so the header points back at the vehicle the menu is actually about.
			var header = menu.GetOrNull<LabelWidget>("MENU_HEADER");
			if (header != null)
			{
				var title = CargoManifest.DisplayName(candidate).ToUpperInvariant();
				header.GetText = () => cargo == null ? "" : $"{title}  {cargo.PassengerCount}/{cargo.Info.MaxWeight}";
			}

			list = menu.Get<ScrollPanelWidget>("CLASS_LIST");
			rowTemplate = list.Get<ScrollItemWidget>("CLASS_TEMPLATE");
			listWidth = list.Bounds.Width;
			menuWidth = menu.Bounds.Width;
			list.RemoveChildren();

			var keys = menu.GetOrNull<LogicKeyListenerWidget>("MENU_KEYHANDLER");
			keys?.AddHandler(e =>
			{
				if (e.Event != KeyInputEvent.Down || e.Key != Keycode.ESCAPE)
					return false;

				Close();
				return true;
			});

			// Added before the menu so that the menu, being the later sibling, wins the click.
			// Anything landing outside the menu hits the mask instead of the world and dismisses.
			mask = new MaskWidget
			{
				Bounds = new WidgetBounds(0, 0, Game.Renderer.Resolution.Width, Game.Renderer.Resolution.Height)
			};

			mask.OnMouseDown += _ => Close();
			Ui.Root.AddChild(mask);
			Ui.Root.AddChild(menu);

			Refresh();
			PositionAtCursor();
		}

		void Refresh()
		{
			if (cargo == null)
				return;

			list.RemoveChildren();

			foreach (var group in GroupByClass())
			{
				var key = group.Key;
				var label = group.Label;

				var row = ScrollItemWidget.Setup(rowTemplate, () => false, () => Drop(key, false));

				var name = row.GetOrNull<LabelWidget>("CLASS_NAME");
				if (name != null)
					name.GetText = () => label;

				var count = row.GetOrNull<LabelWidget>("CLASS_COUNT");
				if (count != null)
					count.GetText = () => $"x{CountIn(key)}";

				var all = row.GetOrNull<ButtonWidget>("CLASS_ALL");
				if (all != null)
					all.OnClick = () => Drop(key, true);

				list.AddChild(row);
			}

			// Size to the content, capped by the screen rather than by a fixed row count, and advertise the
			// cap exactly when it bites. Under roughly 578px of screen height the 24 rows stop fitting and
			// the tail is cut off; the wheel still reaches it — ScrollPanelWidget handles Scroll whatever
			// ScrollBar is set to — but with the bar hidden nothing told the player the rest was there.
			// The arithmetic, and why each term is what it is, is in UnloadMenuGeometry.
			var layout = UnloadMenuGeometry.Measure(Game.Renderer.Resolution.Height, list.Bounds.Y,
				rowTemplate.Bounds.Height, list.ContentHeight, list.ScrollbarWidth, listWidth, menuWidth);

			list.Bounds.Height = layout.ClipHeight;
			menu.Bounds.Height = layout.MenuHeight;
			list.ScrollBar = layout.Overflows ? ScrollBar.Right : ScrollBar.Hidden;
			list.Bounds.Width = layout.ListWidth;
			menu.Bounds.Width = layout.MenuWidth;

			// Refresh also runs after the menu has been placed — units already ordered to Enter can
			// still board while it is open, which adds rows and can be what pushes it into overflow.
			// Re-clamp rather than reposition: moving the menu out from under the cursor mid-click
			// would be worse than the gutter hanging off the edge it is being kept out of.
			menu.Bounds.X = menu.Bounds.X.Clamp(ScreenMargin,
				Math.Max(ScreenMargin, Game.Renderer.Resolution.Width - menu.Bounds.Width - ScreenMargin));
		}

		/// <summary>Grouped rows for the live passenger list. Shared with the sidebar panel via
		/// <see cref="CargoManifest"/> so the two cannot disagree about what is aboard.</summary>
		List<CargoManifestRow> GroupByClass()
		{
			return CargoManifest.Group(cargo.Passengers);
		}

		int CountIn(string key)
		{
			if (cargo == null)
				return 0;

			return cargo.Passengers.Count(p => p != null && !p.IsDead && CargoManifest.GroupKey(p) == key);
		}

		void Drop(string key, bool wholeClass)
		{
			if (cargo == null || transport == null || transport.IsDead || !transport.IsInWorld)
				return;

			// Re-read the live passenger list on every click. Each order names one man by ActorID
			// and Cargo.ResolveOrder revalidates cargo.Contains(passenger), so a pick that went
			// stale between the click and the order resolving is dropped rather than desyncing.
			var members = cargo.Passengers
				.Where(p => p != null && !p.IsDead && CargoManifest.GroupKey(p) == key)
				.ToArray();

			if (members.Length == 0)
				return;

			foreach (var passenger in wholeClass ? members : members.Take(1))
			{
				world.IssueOrder(new Order("UnloadCargoPassenger", transport, hasDropped)
				{
					ExtraData = passenger.ActorID
				});

				hasDropped = true;
			}

			Refresh();
		}

		void PositionAtCursor()
		{
			var cursor = Viewport.LastMousePos;
			var screen = Game.Renderer.Resolution;

			// Flip rather than clamp, so the menu never covers the cell the cursor is resting on.
			var x = cursor.X + menu.Bounds.Width + ScreenMargin > screen.Width
				? cursor.X - menu.Bounds.Width
				: cursor.X;

			var y = cursor.Y + menu.Bounds.Height + ScreenMargin > screen.Height
				? cursor.Y - menu.Bounds.Height
				: cursor.Y;

			menu.Bounds.X = x.Clamp(ScreenMargin, Math.Max(ScreenMargin, screen.Width - menu.Bounds.Width - ScreenMargin));
			menu.Bounds.Y = y.Clamp(ScreenMargin, Math.Max(ScreenMargin, screen.Height - menu.Bounds.Height - ScreenMargin));
		}

		public override void Tick()
		{
			if (menu == null)
				return;

			// The menu is anchored to a screen position, not a world one, so the transport is free
			// to fly off while it is open. What it may not do is stop existing behind it.
			if (transport == null || transport.IsDead || !transport.IsInWorld
				|| cargo == null || cargo.IsEmpty()
				|| !world.Selection.Actors.Contains(transport))
			{
				Close();
				return;
			}

			if (list.Children.Count != GroupByClass().Count())
				Refresh();
		}

		void Close()
		{
			if (menu == null)
				return;

			var closingMenu = menu;
			var closingMask = mask;

			menu = null;
			mask = null;
			list = null;
			rowTemplate = null;
			transport = null;
			cargo = null;

			// Close is reachable from Tick, which runs inside Ui.Root's own Children traversal.
			// Removing there would mutate the collection mid-iteration.
			Game.RunAfterTick(() =>
			{
				Ui.Root.RemoveChild(closingMenu);
				Ui.Root.RemoveChild(closingMask);
				Ui.ResetTooltips();
			});
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
				Close();

			base.Dispose(disposing);
		}
	}
}
