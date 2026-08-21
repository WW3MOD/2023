#region Copyright & License Information
/*
 * WW3MOD developer test harness — Lua scripting bindings.
 * Activated only when TestMode.IsActive (i.e. the game was launched with
 * Test.Mode=true). All methods are no-ops outside test mode so accidental
 * calls from a regular map don't write result files or quit the game.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Eluant;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Projectiles;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.Common.Widgets.Logic.Ingame;
using OpenRA.Scripting;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Scripting.Global
{
	[ScriptGlobal("Test")]
	public class TestGlobal : ScriptGlobal
	{
		public TestGlobal(ScriptContext context)
			: base(context) { }

		[Desc("Mark the current test as passed and exit the game. " +
			"Optional note is surfaced in the verdict (useful for logging metrics " +
			"like hit rate while still passing). No-op outside test mode.")]
		public void Pass(string note = "")
		{
			if (!TestMode.IsActive)
				return;

			ExitWhenCapturesFlushed("pass", note ?? "");
		}

		[Desc("Mark the current test as failed (with a reason) and exit the game. " +
			"No-op outside test mode.")]
		public void Fail(string reason = "")
		{
			if (!TestMode.IsActive)
				return;

			ExitWhenCapturesFlushed("fail", reason ?? "");
		}

		[Desc("Mark the current test as skipped (with a reason) and exit the game. " +
			"No-op outside test mode.")]
		public void Skip(string reason = "")
		{
			if (!TestMode.IsActive)
				return;

			ExitWhenCapturesFlushed("skip", reason ?? "");
		}

		// PITFALL: Renderer.SaveScreenshot dispatches PNG encoding via
		// ThreadPool.QueueUserWorkItem. If Game.Exit() runs while those workers
		// are mid-flush, the process termination kills them and the files never
		// appear on disk. So instead of writing the verdict and exiting
		// immediately, poll AllCapturesFlushed() every 100ms (giving the render
		// loop + ThreadPool time to actually run) until either every captured
		// screenshot is on disk or we hit a 5s timeout — only then write the
		// verdict and exit. 5s ceiling covers slow PNG encodes at high
		// resolutions and multiple captures queued back-to-back. Tests with no
		// captures fall through immediately.
		static void ExitWhenCapturesFlushed(string status, string notes, int attempts = 0)
		{
			const int MaxAttempts = 50;     // 50 × 100ms = 5s ceiling
			const int PollDelayMs = 100;

			if (TestModeScreenshots.AllCapturesFlushed() || attempts >= MaxAttempts)
			{
				TestMode.WriteResult(status, notes);
				Game.Exit();
				return;
			}

			Game.RunAfterDelay(PollDelayMs, () =>
				ExitWhenCapturesFlushed(status, notes, attempts + 1));
		}

		[Desc("Capture a screenshot tagged with `label`. The PNG lands in the per-run " +
			"screenshot directory (Test.ScreenshotDir launch arg or default) under " +
			"NNN_<sanitized-label>.png, and the path is emitted into the verdict JSON's " +
			"'screenshots' array. Optional `note` is surfaced alongside (use it for " +
			"semantic expectations: 'expects: muzzle flash visible'). " +
			"No-op outside test mode. Capture is async — the file appears on disk " +
			"shortly after this call returns. Returns the planned path, or null if " +
			"screenshots are disabled.")]
		public string Screenshot(string label, string note = "")
		{
			if (!TestMode.IsActive)
				return null;

			var tick = Context.World != null ? Context.World.WorldTick : -1;
			return TestModeScreenshots.Capture(label, note ?? "", tick);
		}

		[Desc("Force the real out-of-sync path the netcode takes when two clients disagree, wait for the " +
			"resulting dialog to appear, capture it as `label`, then mark the test passed. " +
			"Force, capture and verdict are one verb because a desync permanently pauses the world " +
			"(World.OutOfSync -> EndGame -> SetPauseState), so Trigger.AfterDelay never fires again and " +
			"neither a follow-up screenshot nor Test.Pass could be scheduled from Lua. " +
			"Run with --sync-reports, or the report the dialog names will not have been generated. " +
			"No-op outside test mode.")]
		public void ForceDesyncAndCapture(string label, string note = "")
		{
			if (!TestMode.IsActive || Context.World == null)
				return;

			var world = Context.World;
			world.ForceOutOfSync();

			if (!world.IsOutOfSync)
			{
				ExitWhenCapturesFlushed("fail", "ForceOutOfSync did not latch the world out of sync.");
				return;
			}

			if (string.IsNullOrEmpty(world.OutOfSyncReportPath))
			{
				ExitWhenCapturesFlushed("fail",
					"No sync report was written for the desynced frame - rerun with --sync-reports.");
				return;
			}

			// DesyncWatcherLogic reacts on the next UI tick and opens the dialog through RunAfterTick,
			// so the capture has to land after both. Real time, because game ticks have stopped.
			Game.RunAfterDelay(1000, () =>
			{
				// The verdict is decided HERE, on state, and the screenshot is only ever asked whether
				// the text fits. A dialog that failed to open and a desync that never happened
				// photograph identically, so a passing verdict must not rest on reading the image.
				// Topmost specifically: World.EndGame fires GameOver, whose handler opens the ingame
				// menu, so "the dialog exists" is not the same claim as "the player can see it".
				var window = Ui.CurrentWindow();
				if (window == null || window.Id != "DESYNC_PROMPT")
				{
					ExitWhenCapturesFlushed("fail",
						$"Desync dialog is not the topmost window (found '{window?.Id ?? "none"}').");
					return;
				}

				// ButtonPrompt clones PROMPT_TEXT once per line, so the report filename lives in one
				// of the clones. Naming that file is the entire point of the dialog, so assert it
				// rather than trusting a human to spot its absence in a screenshot.
				var wanted = Path.GetFileName(world.OutOfSyncReportPath);
				var namesReport = window.Children
					.OfType<LabelWidget>()
					.Any(l => l.GetText?.Invoke()?.Contains(wanted, StringComparison.Ordinal) == true);

				if (!namesReport)
				{
					ExitWhenCapturesFlushed("fail", $"Desync dialog does not name the sync report '{wanted}'.");
					return;
				}

				TestModeScreenshots.Capture(label, note ?? "", world.WorldTick);
				ExitWhenCapturesFlushed("pass", $"Desync dialog raised on top and names {wanted}.");
			});
		}

		[Desc("Set the camera zoom to `scale` × the viewport's default (minimum) zoom, so screenshots " +
			"can be taken at reproducible zoom levels: 1 = default, >1 zoomed in, <1 zoomed out " +
			"(0.25 is the fully-zoomed-out floor). Clamped to the viewport's own limits. " +
			"Returns the resulting zoom as a multiple of MinZoom. Test mode only.")]
		public double SetZoom(double scale)
		{
			if (!TestMode.IsActive)
				return 0;

			var viewport = Context.WorldRenderer.Viewport;

			// Zoom has no public setter — AdjustZoom applies an exponential delta and does the clamping.
			viewport.AdjustZoom((float)Math.Log(viewport.MinZoom * scale / viewport.Zoom));
			return viewport.Zoom / viewport.MinZoom;
		}

		[Desc("Resolve the rally-point order type a click on `cell` from `producer` (with optional " +
			"modifier keys) would produce. `modifiers` is a space-separated list, any of " +
			"'Alt' (= attack-move), 'Ctrl' (= force-move), 'Shift' (= queue), 'CtrlAlt' (= force-attack/SR override). " +
			"Returns 'Move', 'AttackMove', 'ForceMove' (or null if the click is rejected). Test mode only.")]
		public string GetRallyOrderTypeForClick(Actor producer, CPos cell, string modifiers = "")
		{
			if (!TestMode.IsActive || producer == null)
				return null;

			var mods = TargetModifiers.None;
			if (modifiers.Contains("Alt") && !modifiers.Contains("CtrlAlt"))
				mods |= TargetModifiers.AttackMove;
			if (modifiers.Contains("Ctrl") && !modifiers.Contains("CtrlAlt"))
				mods |= TargetModifiers.ForceMove;
			if (modifiers.Contains("CtrlAlt"))
				mods |= TargetModifiers.ForceAttack;
			if (modifiers.Contains("Shift"))
				mods |= TargetModifiers.ForceQueue;

			var target = Target.FromCell(producer.World, cell);
			var actorsAt = producer.World.ActorMap.GetActorsAt(cell).ToList();

			foreach (var trait in producer.TraitsImplementing<IIssueOrder>())
			{
				foreach (var o in trait.Orders)
				{
					string cursor = null;
					if (o.OrderID != "SetRallyPoint" || !o.CanTarget(producer, target, actorsAt, cell, mods, ref cursor))
						continue;

					var order = trait.IssueOrder(producer, o, target, false);
					if (order == null || order.OrderString != "SetRallyPoint")
						continue;

					// Decode OrderType from bits 1..3 of ExtraData (must match RallyPoint encoding).
					var ot = (RallyOrderType)((order.ExtraData >> 1) & 7);
					return ot.ToString();
				}
			}

			return null;
		}

		[Desc("Click the build-menu icon for `actorType`, switching to the queue that offers it. " +
			"`modifiers` is a space-separated list, any of 'Ctrl', 'Alt', 'Shift'; 'Ctrl Alt' is the " +
			"select-all-units-of-this-type gesture. Drives the real ProductionPaletteWidget click " +
			"handler. Returns false if no enabled queue offers the type. Test mode only.")]
		public bool ClickProductionIcon(string actorType, string modifiers = "")
		{
			if (!TestMode.IsActive)
				return false;

			var palette = Ui.Root?.GetOrNull<ProductionPaletteWidget>("PRODUCTION_PALETTE");
			if (palette == null)
				return false;

			var mods = Modifiers.None;
			if (modifiers.Contains("Ctrl"))
				mods |= Modifiers.Ctrl;
			if (modifiers.Contains("Alt"))
				mods |= Modifiers.Alt;
			if (modifiers.Contains("Shift"))
				mods |= Modifiers.Shift;

			return palette.SimulateIconClick(actorType, MouseButton.Left, mods);
		}

		[Desc("State of the class-grouped unload menu: an empty string when it is closed, otherwise " +
			"'<menus>:<rows>' — how many CARGO_UNLOAD_MENU widgets are attached to the UI root, and " +
			"how many class rows the first of them lists. A test needs this because PressHotkey's " +
			"return value cannot tell opening from closing, and a menu left over from a previous " +
			"transport photographs just as happily as a fresh one. A `menus` count above 1 means a " +
			"close failed to detach its widget. Test mode only.")]
		public string GetUnloadMenuState()
		{
			if (!TestMode.IsActive)
				return "";

			var menus = Ui.Root.Children.Where(c => c.Id == "CARGO_UNLOAD_MENU").ToArray();
			if (menus.Length == 0)
				return "";

			var list = menus[0].GetOrNull<ScrollPanelWidget>("CLASS_LIST");
			return $"{menus.Length}:{(list == null ? -1 : list.Children.Count)}";
		}

		[Desc("Geometry of the open unload menu as 'rows=N content=N clip=N panel=N screen=N', or an " +
			"empty string when it is closed. `content` is what the rows need, `clip` is the height the " +
			"scroll panel actually gives them. A row count ALONE cannot see the bug this exists for: " +
			"Refresh adds every class row to the list whatever the panel's height, so rows past the " +
			"cap were counted but drawn nowhere — with ScrollBar Hidden advertising nothing. " +
			"clip < content is that bug, in a number. Test mode only.")]
		public string GetUnloadMenuGeometry()
		{
			if (!TestMode.IsActive)
				return "";

			var menu = Ui.Root.Children.FirstOrDefault(c => c.Id == "CARGO_UNLOAD_MENU");
			var list = menu?.GetOrNull<ScrollPanelWidget>("CLASS_LIST");
			if (list == null)
				return "";

			return $"rows={list.Children.Count} content={list.ContentHeight} clip={list.Bounds.Height} " +
				$"panel={menu.Bounds.Height} screen={Game.Renderer.Resolution.Height}";
		}

		[Desc("Click a row of the open unload menu: the row itself drops one man of that class, or " +
			"set `all` to hit its ALL chip and drop the whole class. Rows are indexed from 0 in the " +
			"order the menu lists them. Drives the real click handlers, so the orders issued are the " +
			"ones a player's click would issue. Returns false if the menu is closed or the index is " +
			"out of range. Test mode only.")]
		public bool ClickUnloadMenuRow(int index, bool all = false)
		{
			if (!TestMode.IsActive)
				return false;

			var menu = Ui.Root.Children.FirstOrDefault(c => c.Id == "CARGO_UNLOAD_MENU");
			var list = menu?.GetOrNull<ScrollPanelWidget>("CLASS_LIST");
			if (list == null || index < 0 || index >= list.Children.Count)
				return false;

			if (list.Children[index] is not ScrollItemWidget row)
				return false;

			if (!all)
			{
				row.OnClick();
				return true;
			}

			var allButton = row.GetOrNull<ButtonWidget>("CLASS_ALL");
			if (allButton == null)
				return false;

			allButton.OnClick();
			return true;
		}

		[Desc("Press whatever key is currently bound to `hotkeyName` (as named in the mod's hotkey " +
			"definitions, e.g. 'UnloadMenu'). Dispatched through Ui.HandleKeyPress, so it walks the " +
			"real widget chain in the real order and honours a rebind rather than hardcoding a key. " +
			"Returns true if a widget consumed it. Test mode only.")]
		public bool PressHotkey(string hotkeyName)
		{
			if (!TestMode.IsActive)
				return false;

			var hotkey = Game.ModData.Hotkeys[hotkeyName].GetValue();
			if (!hotkey.IsValid())
				return false;

			return Ui.HandleKeyPress(new KeyInput
			{
				Event = KeyInputEvent.Down,
				Key = hotkey.Key,
				Modifiers = hotkey.Modifiers,
				MultiTapCount = 1,
			});
		}

		[Desc("Replace the local player's selection with ALL of `actors`. UserInterface.Select takes a " +
			"single actor and replaces the selection, so it cannot build a multi-unit selection at " +
			"all — and anything that renders per-selection (range circles, the grouped concealment " +
			"gauge, the command bar's multi-unit state) is unreachable without one. The only other " +
			"route in the corpus is Ctrl+Alt on a build-menu icon, which selects by TYPE and selects " +
			"nothing when the icon is hidden by prerequisites. Test mode only.")]
		public void SelectActors(Actor[] actors)
		{
			if (!TestMode.IsActive || actors == null || actors.Length == 0)
				return;

			var alive = actors.Where(a => a != null && a.IsInWorld && !a.IsDead).ToArray();
			if (alive.Length == 0)
				return;

			alive[0].World.Selection.Combine(alive[0].World, alive, false, true);
		}

		[Desc("Detectable.CurrentVisibility for `actor` — the observer vision STRENGTH (1-10) required " +
			"to reveal it, after every DetectableAddativeModifier has been applied. Higher means " +
			"HARDER to see, which is the opposite of the intuitive reading of the word. Returns -1 " +
			"when the actor carries no Detectable trait. " +
			"This is the tier the concealment gauge draws a radius for, so a capture scenario can " +
			"assert WHICH tier it photographed rather than hoping the rings look different: three " +
			"shots of an unchanged tier and three of a working gauge are distinguished by this value " +
			"and by nothing else the verdict records. Test mode only.")]
		public int GetVisibilityLevel(Actor actor)
		{
			if (!TestMode.IsActive || actor == null)
				return -1;

			var detectable = actor.TraitOrDefault<Detectable>();
			return detectable == null ? -1 : detectable.CurrentVisibility;
		}

		[Desc("Number of actors currently selected. Test mode only.")]
		public int GetSelectedCount()
		{
			return TestMode.IsActive ? Context.World.Selection.Actors.Count : 0;
		}

		[Desc("Number of currently selected actors whose type is `actorType`. Lets a test assert that " +
			"a selection contains every unit of a type and nothing else. Test mode only.")]
		public int GetSelectedCountOfType(string actorType)
		{
			if (!TestMode.IsActive)
				return 0;

			return Context.World.Selection.Actors.Count(a => a.Info.Name == actorType);
		}

		[Desc("Resolve the right-click OrderID that `unit` would issue when targeting `target`. " +
			"Walks the same IIssueOrder/IOrderTargeter pipeline as the UI cursor resolver, so " +
			"this catches order-priority and CanTargetActor regressions that direct unit.Attack/Move " +
			"calls bypass. Returns the OrderID string of the highest-priority matching targeter, " +
			"or null if nothing matches. Test mode only.")]
		public string GetTargetOrder(Actor unit, Actor target)
		{
			if (!TestMode.IsActive || unit == null || target == null)
				return null;

			var t = Target.FromActor(target);
			var xy = target.Location;
			var actorsAt = unit.World.ActorMap.GetActorsAt(xy).ToList();

			var orders = unit.TraitsImplementing<IIssueOrder>()
				.SelectMany(trait => trait.Orders)
				.OrderByDescending(o => o.OrderPriority);

			foreach (var o in orders)
			{
				string cursor = null;
				if (o.CanTarget(unit, t, actorsAt, xy, TargetModifiers.None, ref cursor))
					return o.OrderID;
			}

			return null;
		}

		ProductionQueue FindQueueForActor(Player player, string actorType)
		{
			if (!player.World.Map.Rules.Actors.TryGetValue(actorType, out var ai))
				return null;

			var bi = ai.TraitInfoOrDefault<BuildableInfo>();
			if (bi == null)
				return null;

			foreach (var q in player.PlayerActor.TraitsImplementing<ProductionQueue>())
				if (q.Enabled && bi.Queue.Contains(q.Info.Type))
					return q;

			return null;
		}

		[Desc("Enqueue `count` of `actorType` on `player`'s production queue. Routes through " +
			"the StartProduction order so it exercises the real queue pipeline. Test mode only.")]
		public void QueueProduction(Player player, string actorType, int count = 1)
		{
			if (!TestMode.IsActive || player == null)
				return;

			var queue = FindQueueForActor(player, actorType);
			if (queue == null)
				return;

			queue.ResolveOrder(player.PlayerActor, Order.StartProduction(player.PlayerActor, actorType, count));
		}

		[Desc("Pause or resume production of `actorType` on `player`'s queue. Routes through the " +
			"PauseProduction order. Test mode only.")]
		public void PauseProduction(Player player, string actorType, bool paused)
		{
			if (!TestMode.IsActive || player == null)
				return;

			var queue = FindQueueForActor(player, actorType);
			if (queue == null)
				return;

			var order = new Order("PauseProduction", player.PlayerActor, false)
			{
				TargetString = actorType,
				ExtraData = paused ? 1u : 0u,
			};
			queue.ResolveOrder(player.PlayerActor, order);
		}

		[Desc("Issue a real EnterTransport order on `passenger` targeting `transport`. Goes through " +
			"Passenger.ResolveOrder so the resulting RideTransport activity has its target-line " +
			"color set, matching player right-click orders (the activity-direct Lua " +
			"`unit.EnterTransport` API passes null color, which makes the activity invisible to " +
			"target-line scans). Test mode only.")]
		public void IssueEnterTransport(Actor passenger, Actor transport, bool queued = true)
		{
			if (!TestMode.IsActive || passenger == null || transport == null)
				return;

			passenger.World.IssueOrder(new Order("EnterTransport", passenger, Target.FromActor(transport), queued));
		}

		[Desc("Issue a real DropSupplyCacheAt order on `truck` targeting `cell` — the drive-out-and-drop " +
			"errand the bot issues. Use this rather than the bare DropSupplyCache order: that one drops " +
			"unconditionally in ResolveOrder, while the occupancy test under DropsSupplyCache.CanDropCache " +
			"is only consulted on ARRIVAL here (and by the deploy cursor), so the bare order cannot " +
			"observe a blocked cell at all. Test mode only.")]
		public void IssueDropSupplyCacheAt(Actor truck, CPos cell, bool queued = false)
		{
			if (!TestMode.IsActive || truck == null)
				return;

			truck.World.IssueOrder(new Order("DropSupplyCacheAt", truck, Target.FromCell(truck.World, cell), queued));
		}

		[Desc("Set a SupplyProvider's remaining supply (truck, cache or Logistics Center). Clamped to " +
			"TotalSupply by the trait. Exists because a provider's behaviour near its own thresholds " +
			"cannot otherwise be staged: a crate is born with whatever the truck was carrying, so " +
			"reaching a specific low load in-scenario would mean draining one through real rearms and " +
			"waiting. Test mode only.")]
		public void SetSupply(Actor provider, int amount)
		{
			if (!TestMode.IsActive || provider == null)
				return;

			provider.TraitOrDefault<SupplyProvider>()?.SetSupply(amount);
		}

		[Desc("A SupplyProvider's remaining supply, or -1 if the actor has no SupplyProvider. Test mode only.")]
		public int GetSupply(Actor provider)
		{
			if (!TestMode.IsActive || provider == null)
				return -1;

			var supply = provider.TraitOrDefault<SupplyProvider>();
			return supply == null ? -1 : supply.CurrentSupply;
		}

		[Desc("Issue a real AttendAlly order on `healer` targeting `ally` — the order a left-click on a " +
			"friendly unit produces. There is no scripting binding for arbitrary orders, and the " +
			"behaviour under test exists only on the order path. Test mode only.")]
		public void IssueAttendAlly(Actor healer, Actor ally, bool queued = false)
		{
			if (!TestMode.IsActive || healer == null || ally == null)
				return;

			healer.World.IssueOrder(new Order("AttendAlly", healer, Target.FromActor(ally), queued));
		}

		[Desc("Issue the Resupply order the RESUPPLY command-bar button produces (CommandBarLogic.cs:187). " +
			"This is the only route into AmmoPool.AutoRearmIfAnyNotFull, which — unlike the dry " +
			"AutoRearmIfAllEmpty path everything else uses — dispatches a unit that is merely " +
			"PARTIALLY empty. Test mode only.")]
		public void IssueResupply(Actor self, bool queued = false)
		{
			if (!TestMode.IsActive || self == null)
				return;

			self.World.IssueOrder(new Order("Resupply", self, queued));
		}

		[Desc("Issue the order a real right-click on `target` would produce, resolving the whole " +
			"IIssueOrder targeter chain in descending OrderPriority exactly as UnitOrderGenerator does, " +
			"and return the OrderString that won (nil if the click is refused). " +
			"PITFALL: naming the order you expect — Actor.Attack, Test.IssueAttendAlly — skips the " +
			"priority contest entirely, so a test written that way passes while the click a player " +
			"actually makes is routed to a different trait and a different activity. Use this whenever " +
			"the ROUTING is part of what is under test. `modifiers` is a space-separated list, any of " +
			"'Ctrl' (force-move), 'Alt' (attack-move), 'Shift' (queue), 'CtrlAlt' (force-attack). " +
			"Test mode only.")]
		public string ClickOrder(Actor self, Actor target, string modifiers = "")
		{
			if (!TestMode.IsActive || self == null || target == null)
				return null;

			var mods = TargetModifiers.None;
			if (modifiers.Contains("CtrlAlt"))
				mods |= TargetModifiers.ForceAttack;
			else
			{
				if (modifiers.Contains("Ctrl"))
					mods |= TargetModifiers.ForceMove;
				if (modifiers.Contains("Alt"))
					mods |= TargetModifiers.AttackMove;
			}

			var queued = modifiers.Contains("Shift");
			if (queued)
				mods |= TargetModifiers.ForceQueue;

			// Delegates rather than replicating the chain. The private copy this replaced omitted
			// UnitOrderGenerator's terrain retry, so it answered a question the real click does not
			// ask — it could never report the Move that a refused attack used to produce.
			var result = UnitOrderGenerator.OrderForUnit(self, Target.FromActor(target), target.Location, mods);
			if (result == null)
				return null;

			var order = result.Trait.IssueOrder(self, result.Order, result.Target, queued);
			if (order == null)
				return null;

			self.World.IssueOrder(order);
			return order.OrderString;
		}

		[Desc("Issue a real AttackMove order, going through AttackMove.ResolveOrder the way a player's " +
			"attack-move click does. Use this rather than the activity-direct Lua `unit.AttackMove`, " +
			"which constructs AttackMoveActivity itself and never consults the order layer at all — if " +
			"what you are testing is whether the ORDER is accepted (queued vs immediate, ammo state at " +
			"issue time), the activity-direct API cannot see it. Test mode only.")]
		public void IssueAttackMove(Actor actor, CPos cell, bool queued = false)
		{
			if (!TestMode.IsActive || actor == null)
				return;

			actor.World.IssueOrder(new Order("AttackMove", actor, Target.FromCell(actor.World, cell), queued));
		}

		[Desc("Issue a real Move order (force = true for the Ctrl+click ForceMove variant), going through " +
			"Mobile.ResolveOrder the way a player's click does. The two are NOT the same activity graph: " +
			"\"Move\" is wrapped by every IWrapMove trait — SmartMove on ^Infantry — while \"ForceMove\" " +
			"deliberately bypasses the wrappers (Mobile.cs:1021 vs :1032). The activity-direct Lua " +
			"`unit.Move` always takes the wrapped path, so it cannot tell the two apart. Test mode only.")]
		public void IssueMove(Actor actor, CPos cell, bool force = false, bool queued = false)
		{
			if (!TestMode.IsActive || actor == null)
				return;

			actor.World.IssueOrder(new Order(force ? "ForceMove" : "Move", actor, Target.FromCell(actor.World, cell), queued));
		}

		[Desc("Force the target-line display setting to Automatic for this run, so order lines and their " +
			"waypoint markers render without a human holding Shift. The engine default is Manual " +
			"(Settings.cs), under which DrawLineToTarget draws nothing at all unless a modifier key is " +
			"physically down — which no scripted test can arrange, so a screenshot of a queued order " +
			"would otherwise always come back empty. Test mode only.")]
		public void ShowTargetLinesAlways()
		{
			if (!TestMode.IsActive)
				return;

			Game.Settings.Game.TargetLines = TargetLinesType.Automatic;
		}

		[Desc("Cells of the target-line nodes `actor`'s activity queue would draw right now, in draw " +
			"order. `withTile` picks which half: false (default) returns the LINE nodes — the waypoint " +
			"chain a player sees — and true returns the TILE nodes, the sprite overlays stamped onto a " +
			"cell (a queued deploy's ghosted crate, LayMines' minefield stamp). " +
			"The tile half literally calls DrawLineToTarget.TileNodes, the same enumeration the renderer " +
			"draws from, so an answer here cannot disagree with what is on screen — including the " +
			"collapsing of duplicate markers, so ten unloads queued onto one cell report ONE cell here " +
			"because one is what is drawn. That is the point of the binding: a marker's CELL is " +
			"otherwise checkable only by looking at a screenshot, which no assertion can do, so a " +
			"regression that moved it to a different waypoint, or stamped it ten times over, would " +
			"render wrongly and pass every test. Test mode only.")]
		public CPos[] GetTargetLineCells(Actor actor, bool withTile = false)
		{
			if (!TestMode.IsActive || actor == null)
				return Array.Empty<CPos>();

			var cells = new List<CPos>();

			// The tile half defers to DrawLineToTarget.TileNodes rather than re-walking the queue,
			// so this binding keeps the promise in its own description: it collapses duplicate
			// markers on a cell exactly as the renderer does, instead of reporting ten stamps where
			// the screen shows one.
			if (withTile)
			{
				foreach (var n in DrawLineToTarget.TileNodes(actor))
					cells.Add(actor.World.Map.CellContaining(n.Target.CenterPosition));

				return cells.ToArray();
			}

			for (var a = actor.CurrentActivity; a != null; a = a.NextActivity)
				if (!a.IsCanceling)
					foreach (var n in a.TargetLineNodes(actor))
						if (n.Target.Type != TargetType.Invalid && n.Tile == null)
							cells.Add(actor.World.Map.CellContaining(n.Target.CenterPosition));

			return cells.ToArray();
		}

		[Desc("Issue the deploy order that the command bar's Deploy button — and its hotkey — produce, " +
			"through IIssueDeployOrder exactly as CommandBarLogic.PerformDeployOrderOnSelection does. " +
			"`queued` is the Shift modifier. There is no activity-direct equivalent worth using here: " +
			"a deploy's whole contract is what its trait's ResolveOrder does with the queued flag, and " +
			"queueing the resulting activity by hand would bypass that. Test mode only.")]
		public void IssueDeploy(Actor actor, bool queued = false)
		{
			if (!TestMode.IsActive || actor == null)
				return;

			foreach (var deploy in actor.TraitsImplementing<IIssueDeployOrder>())
			{
				if (!deploy.CanIssueDeployOrder(actor, queued))
					continue;

				var order = deploy.IssueDeployOrder(actor, queued);
				if (order != null)
					actor.World.IssueOrder(order);
			}
		}

		[Desc("Return the CPos this actor was last assigned by CohesionMoveModifier (the slot the " +
			"sticky-cover leash will try to walk back to). Returns CPos.Zero if no slot is set.")]
		public CPos GetCohesionSlot(Actor actor)
		{
			if (!TestMode.IsActive || actor == null)
				return CPos.Zero;

			var memory = actor.TraitOrDefault<CohesionSlotMemory>();
			return memory?.AssignedSlot ?? CPos.Zero;
		}

		[Desc("Force an actor's Cohesion stance (\"Tight\", \"Loose\", or \"Spread\") so a test can " +
			"exercise a specific formation spacing deterministically, independent of the dev's " +
			"persisted per-type defaults. No-op outside test mode.")]
		public void SetCohesion(Actor actor, string mode)
		{
			if (!TestMode.IsActive || actor == null)
				return;

			var autoTarget = actor.TraitOrDefault<AutoTarget>();
			if (autoTarget == null)
				return;

			if (System.Enum.TryParse<CohesionMode>(mode, true, out var value))
				autoTarget.SetCohesion(actor, value);
		}

		[Desc("Set the per-TYPE FireStance default in UnitDefaultsManager, exactly as Ctrl+Alt on the " +
			"stance bar does. This is a client-local preference: it must reach the world ONLY by the " +
			"owning client issuing SetUnitStance orders on ActorAdded, never by simulation reading the " +
			"store. Returns false if the manager is absent or the stance name is unknown, so a test can " +
			"assert its own setup took effect rather than asserting against a silently-empty default.")]
		public bool SetUnitTypeFireStance(string actorType, string stance)
		{
			if (!TestMode.IsActive || string.IsNullOrEmpty(actorType))
				return false;

			var mgr = Context.World.WorldActor.TraitOrDefault<UnitDefaultsManager>();
			if (mgr == null)
				return false;

			if (!System.Enum.TryParse<UnitStance>(stance, true, out var value))
				return false;

			var key = actorType.ToLowerInvariant();
			mgr.SetFireStance(key, value);
			return mgr.GetDefaults(key)?.FireStance == value;
		}

		[Desc("Read Map.DensityLayer at a cell. Returns the byte value (0-255) summed from all " +
			"density-bearing actors whose footprint covers this cell. Test mode only.")]
		public int GetDensity(CPos cell)
		{
			if (!TestMode.IsActive)
				return 0;

			var map = Context.World.Map;
			if (map.DensityLayer == null || !map.DensityLayer.IsValidCoordinate(cell.X, cell.Y))
				return 0;

			return map.DensityLayer[cell];
		}

		[Desc("Issue a grouped Move (or AttackMove) order to a collection of actors as if the " +
			"player had selected them all and right-clicked `cell`. Goes through the real Order " +
			"pipeline so IModifyGroupOrder traits (CohesionMoveModifier and friends) fire and " +
			"redistribute per-unit destinations. Unlike Actor.Move (which queues a Move activity " +
			"directly and bypasses the order system), this exercises the cover-aware slot bidder. " +
			"`orderString` defaults to 'Move' — pass 'AttackMove' for the attack variant. " +
			"Test mode only.")]
		public void GroupMove(Actor[] actors, CPos cell, string orderString = "Move")
		{
			if (!TestMode.IsActive || actors == null || actors.Length == 0)
				return;

			var alive = actors.Where(a => a != null && a.IsInWorld && !a.IsDead).ToArray();
			if (alive.Length == 0)
				return;

			var world = alive[0].World;
			var target = Target.FromCell(world, cell);

			if (alive.Length == 1)
				world.IssueOrder(new Order(orderString, alive[0], target, false));
			else
				world.IssueOrder(new Order(orderString, null, target, false, null, alive));
		}

		[Desc("Run the Group Scatter (Shift-G) spread on the given actors as if the user had " +
			"selected them and pressed the hotkey. Useful for verifying that the spread doesn't " +
			"redistribute unit-specific waypoints (e.g. EnterTransport) across the rest of the " +
			"selection. Test mode only.")]
		public void GroupScatter(Actor[] actors)
		{
			if (!TestMode.IsActive || actors == null || actors.Length == 0)
				return;

			var alive = actors.Where(a => a != null && a.IsInWorld && !a.IsDead).ToList();
			if (alive.Count == 0)
				return;

			GroupScatterHotkeyLogic.PerformGroupScatter(alive[0].World, alive);
		}

		[Desc("Running count of CreateEffectWarhead impacts that PASSED THE IMPACT VALIDITY GATES this " +
			"run — impacts NOT discarded for landing on an invalid actor or invalid terrain. This is " +
			"the only way a scenario can tell a shell that detonated from one silently swallowed at " +
			"impact. It counts the gate decision, which is upstream of the sprite (skipped when the " +
			"warhead defines no Image/Explosions) and of the sound (skipped by ImpactSoundChance), so " +
			"do NOT assert on it as 'a sprite was drawn' or 'a sound played'. Snapshot it before " +
			"ordering the shot and compare deltas. Test mode only.")]
		public int GetImpactEffectCount()
		{
			return TestMode.IsActive ? TestMode.ImpactEffectCount : 0;
		}

		[Desc("Returns the number of in-flight Missile projectiles currently in the world. " +
			"Useful for asserting that a missile reached its target / fuel-out and detonated " +
			"within a deadline. Test mode only.")]
		public int GetActiveMissileCount()
		{
			if (!TestMode.IsActive)
				return 0;

			return Context.World.Effects.OfType<Missile>().Count();
		}

		[Desc("Switch the Phase-0 missile trace on for this run. Call from WorldLoaded, before " +
			"anything fires — missiles already in flight are not retro-tracked. `path` is optional: " +
			"pass one to also write the JSONL stream to disk, omit it to keep the summary records in " +
			"memory for Test.GetMissileRecord assertions only. `tickRecords=false` suppresses the " +
			"per-tick lines and keeps only one summary record per missile. Also switchable without " +
			"touching the scenario via the Test.MissileTraceLog=<true|path> launch arg " +
			"(tools/autotest/run-test.sh --missile-trace). Test mode only.")]
		public void EnableMissileTrace(string path = "", bool tickRecords = true)
		{
			if (!TestMode.IsActive)
				return;

			MissileTrace.Enable(path, tickRecords);
		}

		[Desc("True when the MissileTrace sink is actually recording. A scenario that steers off " +
			"live missile state must check this first: with the trace off Test.GetLiveMissileRange " +
			"returns -1 forever, which is indistinguishable from 'nothing is flying' — so the " +
			"scenario would run to completion having perturbed nothing and still report a verdict. " +
			"Test mode only.")]
		public bool IsMissileTraceEnabled()
		{
			if (!TestMode.IsActive)
				return false;

			// The Test.MissileTraceLog launch arg is resolved lazily, from the Missile constructor.
			// A scenario asks this from WorldLoaded — before anything has fired — so without forcing
			// the gate here the answer is always false and the check reads as "you forgot the flag".
			MissileTrace.EnsureInitialized();
			return MissileTrace.Enabled;
		}

		[Desc("Smallest true 3D separation between `target` and any missile currently in flight at " +
			"it, or -1 when none is airborne. This is `currentDistance` as Missile.cs computes it " +
			"for the FlyStraightIfMiss predicate — no lead term, no inaccuracy offset — so a " +
			"scenario can act at a chosen remaining range instead of a guessed tick offset. The " +
			"sample is the traced missile's most recent tick and so may lag the simulation by one " +
			"tick (up to one missile-speed of travel). Requires the MissileTrace sink: check " +
			"Test.IsMissileTraceEnabled() first. Test mode only.")]
		public int GetLiveMissileRange(Actor target)
		{
			return Nearest(target, out _);
		}

		[Desc("Trace id of the missile Test.GetLiveMissileRange just measured, or -1 when none is " +
			"airborne. Ids are unique for the run, so a scenario can perturb each individual missile " +
			"exactly once — which range alone cannot express, because a missile that misses and " +
			"survives loiters downrange while the next shot is already inbound. Test mode only.")]
		public int GetLiveMissileNearestId(Actor target)
		{
			Nearest(target, out var id);
			return id;
		}

		static int Nearest(Actor target, out int id)
		{
			id = -1;
			if (!TestMode.IsActive || target == null)
				return -1;

			var best = -1;
			foreach (var rec in MissileTrace.LiveRecords)
			{
				if (rec.TargetId != target.ActorID)
					continue;

				var d = (rec.TargetPos - rec.Pos).Length;
				if (best >= 0 && d >= best)
					continue;

				best = d;
				id = rec.Id;
			}

			return best;
		}

		[Desc("Number of completed missile summary records so far. A missile gets its record when it " +
			"ends (detonates, is removed pre-Arm, or the match ends with it still aloft), so poll " +
			"Test.GetActiveMissileCount() == 0 before asserting. Test mode only.")]
		public int GetMissileRecordCount()
		{
			if (!TestMode.IsActive)
				return 0;

			return MissileTrace.Records.Count;
		}

		[Desc("Fetch missile summary record `index` (1-based, launch order) as a table. Keys: id, " +
			"launcher, launcher_id, owner, weapon, target, target_id, launch_x/y/z, launch_alt, " +
			"launch_range, launch_hor_range, homing_tick, arm_tick, range_limit, max_speed, " +
			"close_enough, min_dist, min_dist_tick, min_aim_dist, min_aim_dist_tick, " +
			"flystraight_tick, flystraight_hor_dist, flystraight_min_dist, flystraight_state, " +
			"flystraight_latches, end_tick, end_x/y/z, end_dat, end_dat_bucket, air_threshold, " +
			"reason, outcome, armed, explode_calls, distance_covered, damage, " +
			"damage_to_target, damage_unattributed, victim_count. " +
			"`end_dat` is DistanceAboveTerrain at the detonation and `end_dat_bucket` is " +
			"subterrain/ground/air against the weapon's own AirThreshold — an `air` impact on a " +
			"weapon with no air-valid CreateEffect warhead renders no sprite and no sound. " +
			"`flystraight_*` capture the FlyStraightIfMiss latch edge and the two distances its " +
			"predicate compared. " +
			"`damage_to_target` counts only the actor the missile was launched at, so splash " +
			"onto a neighbour does not read as a hit. `reason` names the exact code path that ended the missile " +
			"(ground / close_enough / segment_closest / fuel_out / off_map / terrain_bound / " +
			"airburst / blocked / jammed_aps / unterminated); `outcome` separates a real detonation " +
			"from dud_prearm (removed before Arm, no warhead) and unterminated (never ended). " +
			"Returns an empty table for an out-of-range index. Test mode only.")]
		public LuaTable GetMissileRecord(int index)
		{
			var t = Context.CreateTable();
			if (!TestMode.IsActive)
				return t;

			var records = MissileTrace.Records;
			if (index < 1 || index > records.Count)
				return t;

			var r = records[index - 1];
			Put(t, "id", r.Id);
			Put(t, "launcher", r.LauncherType);
			Put(t, "launcher_id", (int)r.LauncherId);
			Put(t, "owner", r.OwnerClientIndex);
			Put(t, "weapon", r.Weapon);
			Put(t, "target", r.TargetType);
			Put(t, "target_id", (int)r.TargetId);
			Put(t, "launch_x", r.LaunchPos.X);
			Put(t, "launch_y", r.LaunchPos.Y);
			Put(t, "launch_z", r.LaunchPos.Z);
			Put(t, "launch_alt", r.LaunchAltitude);
			Put(t, "launch_range", r.LaunchRange);
			Put(t, "launch_hor_range", r.LaunchHorRange);
			Put(t, "homing_tick", r.HomingTick);
			Put(t, "arm_tick", r.ArmTick);
			Put(t, "range_limit", r.RangeLimit);
			Put(t, "max_speed", r.MaxSpeed);
			Put(t, "close_enough", r.CloseEnough);
			Put(t, "min_dist", r.MinDist == int.MaxValue ? -1 : r.MinDist);
			Put(t, "min_dist_tick", r.MinDistTick);
			Put(t, "min_aim_dist", r.MinAimDist == int.MaxValue ? -1 : r.MinAimDist);
			Put(t, "min_aim_dist_tick", r.MinAimDistTick);
			Put(t, "flystraight_tick", r.FlyStraightTick);
			Put(t, "flystraight_hor_dist", r.FlyStraightHorDist);
			Put(t, "flystraight_min_dist", r.FlyStraightMinDist);
			Put(t, "flystraight_state", r.FlyStraightState);
			Put(t, "flystraight_latches", r.FlyStraightLatches);
			Put(t, "end_tick", r.EndTick);
			Put(t, "end_x", r.EndPos.X);
			Put(t, "end_y", r.EndPos.Y);
			Put(t, "end_z", r.EndPos.Z);
			Put(t, "end_dat", r.EndDistanceAboveTerrain);
			Put(t, "end_dat_bucket", MissileTrace.DatBucket(r.EndDistanceAboveTerrain, r.AirThreshold));
			Put(t, "air_threshold", r.AirThreshold);
			Put(t, "reason", MissileTrace.ReasonName(r.EndReason));
			Put(t, "outcome", MissileTrace.OutcomeName(r.Outcome));
			Put(t, "armed", r.Outcome == MissileOutcome.Detonated ? 1 : 0);
			Put(t, "explode_calls", r.ExplodeCalls);
			Put(t, "distance_covered", r.DistanceCovered);
			Put(t, "damage", r.DamageTotal);
			Put(t, "damage_to_target", r.DamageToTarget);
			Put(t, "damage_unattributed", r.DamageUnattributed ? 1 : 0);
			Put(t, "victim_count", r.Victims.Count);
			return t;
		}

		[Desc("Actor type name of victim `victimIndex` (1-based) of missile record `index` (1-based), " +
			"or an empty string if either index is out of range. Test mode only.")]
		public string GetMissileVictimType(int index, int victimIndex)
		{
			var v = Victim(index, victimIndex);
			return v?.Type ?? "";
		}

		[Desc("Damage attributed to victim `victimIndex` (1-based) of missile record `index` (1-based), " +
			"or -1 if either index is out of range. Test mode only.")]
		public int GetMissileVictimDamage(int index, int victimIndex)
		{
			var v = Victim(index, victimIndex);
			return v?.Damage ?? -1;
		}

		static MissileVictim Victim(int index, int victimIndex)
		{
			if (!TestMode.IsActive)
				return null;

			var records = MissileTrace.Records;
			if (index < 1 || index > records.Count)
				return null;

			var victims = records[index - 1].Victims;
			if (victimIndex < 1 || victimIndex > victims.Count)
				return null;

			return victims[victimIndex - 1];
		}

		static void Put(LuaTable t, string key, int value)
		{
			using (LuaValue k = key, v = value)
				t.Add(k, v);
		}

		static void Put(LuaTable t, string key, string value)
		{
			using (LuaValue k = key, v = value ?? "")
				t.Add(k, v);
		}

		[Desc("Returns the RemainingTime (in ticks) of the first queued item of `actorType` on " +
			"`player`'s queue, or -1 if no such item is queued. Test mode only.")]
		public int GetQueueRemainingTime(Player player, string actorType)
		{
			if (!TestMode.IsActive || player == null)
				return -1;

			var queue = FindQueueForActor(player, actorType);
			if (queue == null)
				return -1;

			var item = queue.AllQueued().FirstOrDefault(i => i.Item == actorType);
			return item?.RemainingTime ?? -1;
		}

		[Desc("Returns true if `player` has counter-battery radar coverage at `cell`. " +
			"Used by tests to verify CBR coverage is properly added/removed as the source actor " +
			"moves, deploys/undeploys, or dies. Test mode only.")]
		public bool HasCounterBatteryRadarCover(Player player, CPos cell)
		{
			if (!TestMode.IsActive || player == null)
				return false;

			return player.MapLayers.CounterBatteryRadarCover(cell);
		}

		[Desc("Returns true if `player` has radar coverage at `cell`. " +
			"Test mode only.")]
		public bool HasRadarCover(Player player, CPos cell)
		{
			if (!TestMode.IsActive || player == null)
				return false;

			return player.MapLayers.RadarCover(cell);
		}

		[Desc("Count of AutoTarget scans on `actor` that ran a free ChooseTarget because NOTHING held " +
			"the unit to its current fight — no live RequestedTarget and no persistent OpportunityTarget. " +
			"This is the signature of an engagement having LAPSED, and it is the only route by which a " +
			"unit re-acquires a better target WITHOUT target preemption. Preemption never increments it: " +
			"it hands over while the incumbent is still held. Sample it when the provoking target arrives " +
			"and again when the unit engages, and compare — an unchanged count means the switch happened " +
			"mid-engagement, a raised one means the unit merely re-scanned after losing its grip. " +
			"Monotonic and latched in the simulation, so it cannot be missed by per-tick Lua polling the " +
			"way an intra-tick idle window can. Returns 0 for an actor with no AutoTarget. Test mode only.")]
		public int GetUncommittedScanCount(Actor actor)
		{
			if (!TestMode.IsActive || actor == null || actor.IsDead || !actor.IsInWorld)
				return 0;

			var autoTarget = actor.TraitOrDefault<AutoTarget>();
			return autoTarget?.UncommittedScanCount ?? 0;
		}

		[Desc("Returns the resolved fog-of-war visibility strength (0-10) for `player` at `cell`. " +
			"0 = shrouded, 1 = explored-fog or minimum visible, higher values = more vision sources / less shadow attenuation. " +
			"Used by tests to verify that obstacles (trees, etc.) actually attenuate vision via the ShadowLayer path. " +
			"Test mode only.")]
		public int GetVisibility(Player player, CPos cell)
		{
			if (!TestMode.IsActive || player == null)
				return 0;

			return player.MapLayers.GetVisibility(player.World.Map.CenterOfCell(cell));
		}

		[Desc("Whether `player` can currently see `actor` — the real engine answer, straight through " +
			"Actor.CanBeViewedByPlayer into Detectable and MapLayers.IsDetected, so a test asserts the " +
			"shipped detection path instead of re-deriving it from a cell's visibility strength and the " +
			"unit's tier. NOTE the ally shortcut: Detectable.AlwaysVisibleRelationships defaults to Ally, " +
			"so this is only meaningful for an ENEMY observer. Test mode only.")]
		public bool IsDetectedBy(Actor actor, Player player)
		{
			if (!TestMode.IsActive || actor == null || player == null)
				return false;

			return actor.CanBeViewedByPlayer(player);
		}

		[Desc("Whether the RENDER player may click `actor` — right-click it as an order target, or " +
			"select it. This is the predicate the mouse paths run (MouseTargetVisibility), not the one " +
			"IsDetectedBy asks. PITFALL: the two disagree, and that gap IS the radar-targeting bug — " +
			"IsDetectedBy returns true for a radar-only contact whether or not the bug is present, so a " +
			"scenario asserting it goes green against the defect. Assert this instead. Test mode only.")]
		public bool IsMouseTargetable(Actor actor)
		{
			if (!TestMode.IsActive || actor == null || actor.IsDead || !actor.IsInWorld)
				return false;

			return actor.IsRevealedForMouseInput(Context.World);
		}

		[Desc("Invoke a registered chat command (as if typed into the chatbox), e.g. \"intel\" or " +
			"\"/intel\" to toggle the Phase-1 intel overlay's dev always-on switch. Test mode only.")]
		public void RunChatCommand(string command)
		{
			if (!TestMode.IsActive || string.IsNullOrEmpty(command))
				return;

			var cc = Context.World.WorldActor.TraitOrDefault<OpenRA.Mods.Common.Commands.ChatCommands>();
			if (cc == null)
				return;

			var name = command.TrimStart('/').Split(' ')[0].ToLowerInvariant();
			if (cc.Commands.TryGetValue(name, out var cmd))
				cmd.InvokeCommand(name, "");
		}

		SightingThreatLayer Sighting()
		{
			return Context.World?.WorldActor.TraitOrDefault<SightingThreatLayer>();
		}

		[Desc("Read the §3a SightingThreatLayer enemy (threat) intensity for `player` at `cell`. " +
			"Non-zero means the player has a live/decaying enemy sighting there. Test mode only.")]
		public int GetThreatIntensity(Player player, CPos cell)
		{
			if (!TestMode.IsActive || player == null)
				return 0;

			return Sighting()?.ThreatIntensity(player, cell) ?? 0;
		}

		[Desc("Read the §3a SightingThreatLayer friendly (own + visible allied) intensity for `player` at `cell`. " +
			"Test mode only.")]
		public int GetFriendlyIntensity(Player player, CPos cell)
		{
			if (!TestMode.IsActive || player == null)
				return 0;

			return Sighting()?.FriendlyIntensity(player, cell) ?? 0;
		}

		[Desc("Read the §3a SightingThreatLayer threat bearing (WAngle, 0-1023, counterclockwise) for " +
			"`player` at `cell` — the dominant direction toward recent enemy sightings. Test mode only.")]
		public int GetThreatDirection(Player player, CPos cell)
		{
			if (!TestMode.IsActive || player == null)
				return 0;

			return Sighting()?.ThreatDirection(player, cell).Angle ?? 0;
		}
	}
}
