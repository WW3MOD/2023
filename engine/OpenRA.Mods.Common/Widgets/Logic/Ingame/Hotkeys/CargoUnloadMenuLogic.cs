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
		// A 36-slot Chinook can legally hold one of every class, which is taller than some
		// viewports. Past this the list scrolls rather than growing off the screen.
		const int MaxListHeight = 198;
		const int ScreenMargin = 4;

		readonly World world;

		Widget menu;
		MaskWidget mask;
		ScrollPanelWidget list;
		ScrollItemWidget rowTemplate;

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
			if (menu != null)
			{
				Close();
				return true;
			}

			var candidate = SelectedTransport();
			if (candidate == null)
				return false;

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
				var title = DisplayName(candidate).ToUpperInvariant();
				header.GetText = () => cargo == null ? "" : $"{title}  {cargo.PassengerCount}/{cargo.Info.MaxWeight}";
			}

			list = menu.Get<ScrollPanelWidget>("CLASS_LIST");
			rowTemplate = list.Get<ScrollItemWidget>("CLASS_TEMPLATE");
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

			list.Bounds.Height = Math.Min(MaxListHeight, list.ContentHeight);
			menu.Bounds.Height = list.Bounds.Y + list.Bounds.Height + ScreenMargin;
		}

		/// <summary>
		/// Groups live passengers by <see cref="ISelectable.Class"/>. Grouping on actor type
		/// instead would split the veteran variants (E1R1, E3R1, E2R1) into rows of their own —
		/// but those inherit their base Tooltip verbatim, so the player would just see two rows
		/// both reading "Rifleman" with no way to tell them apart.
		/// </summary>
		IEnumerable<(string Key, string Label)> GroupByClass()
		{
			return cargo.Passengers
				.Where(p => p != null && !p.IsDead)
				.GroupBy(GroupKey)
				.Select(g => (g.Key, DisplayName(g.First())))
				.ToList();
		}

		static string GroupKey(Actor p)
		{
			var selectable = p.TraitOrDefault<ISelectable>();
			return string.IsNullOrEmpty(selectable?.Class) ? p.Info.Name : selectable.Class;
		}

		static string DisplayName(Actor p)
		{
			return p.TraitOrDefault<Tooltip>()?.Info.Name ?? p.Info.Name;
		}

		int CountIn(string key)
		{
			if (cargo == null)
				return 0;

			return cargo.Passengers.Count(p => p != null && !p.IsDead && GroupKey(p) == key);
		}

		void Drop(string key, bool wholeClass)
		{
			if (cargo == null || transport == null || transport.IsDead || !transport.IsInWorld)
				return;

			// Re-read the live passenger list on every click. Each order names one man by ActorID
			// and Cargo.ResolveOrder revalidates cargo.Contains(passenger), so a pick that went
			// stale between the click and the order resolving is dropped rather than desyncing.
			var members = cargo.Passengers
				.Where(p => p != null && !p.IsDead && GroupKey(p) == key)
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
