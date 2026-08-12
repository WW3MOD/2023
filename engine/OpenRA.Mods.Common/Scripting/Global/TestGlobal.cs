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
using System.Linq;
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

			var t = Target.FromActor(target);
			var xy = target.Location;
			var actorsAt = self.World.ActorMap.GetActorsAt(xy).ToList();

			var candidates = self.TraitsImplementing<IIssueOrder>()
				.SelectMany(trait => trait.Orders.Select(o => (Trait: trait, Order: o)))
				.OrderByDescending(x => x.Order.OrderPriority);

			foreach (var c in candidates)
			{
				string cursor = null;
				if (!c.Order.CanTarget(self, t, actorsAt, xy, mods, ref cursor))
					continue;

				var order = c.Trait.IssueOrder(self, c.Order, t, queued);
				if (order == null)
					continue;

				self.World.IssueOrder(order);
				return order.OrderString;
			}

			return null;
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
			"The walk is DrawLineToTarget's own, down to skipping cancelling activities and Invalid " +
			"targets, and `withTile` is the exact test it splits on, so an answer here cannot disagree " +
			"with what is on screen. That is the point of the binding: a marker's CELL is otherwise " +
			"checkable only by looking at a screenshot, which no assertion can do, so a regression that " +
			"moved it to a different waypoint would render wrongly and pass every test. Test mode only.")]
		public CPos[] GetTargetLineCells(Actor actor, bool withTile = false)
		{
			if (!TestMode.IsActive || actor == null)
				return Array.Empty<CPos>();

			var cells = new List<CPos>();
			for (var a = actor.CurrentActivity; a != null; a = a.NextActivity)
				if (!a.IsCanceling)
					foreach (var n in a.TargetLineNodes(actor))
						if (n.Target.Type != TargetType.Invalid && (n.Tile != null) == withTile)
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

		[Desc("Returns the number of in-flight Missile projectiles currently in the world. " +
			"Useful for asserting that a missile reached its target / fuel-out and detonated " +
			"within a deadline. Test mode only.")]
		public int GetActiveMissileCount()
		{
			if (!TestMode.IsActive)
				return 0;

			return Context.World.Effects.OfType<Missile>().Count();
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
