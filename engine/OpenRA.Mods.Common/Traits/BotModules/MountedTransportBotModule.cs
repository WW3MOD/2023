#region Copyright & License Information
/*
 * WW3MOD MountedTransportBotModule — Stage B.4 of the doctrine roadmap.
 *
 * Doctrine intent (from playtest observation O3): infantry holds the
 * front; vehicles are mobile fire-support that ALSO act as delivery
 * platforms. Without this, infantry walks slowly forward while
 * vehicles outrun them and pile up at the front waiting.
 *
 * This module pairs idle IFVs/APCs with infantry reserves, drives them
 * to the frontline, drops them off near cover, and sends the carrier
 * back to the reserve zone. Repeats.
 *
 * Per-carrier state machine:
 *   Idle      → carrier sitting in reserve, empty, no plan
 *   Loading   → EnterTransport orders sent to passengers; waiting for boarding
 *   Delivering → cargo full; carrier driving to drop-off cell
 *   Unloading → arrived; UnloadCargo issued; waiting for passengers to disembark
 *   Returning → empty; driving back to reserve cell
 *
 * Carriers in any active state are EXCLUDED by other modules (the
 * LayeredDefenceBotModule already excludes carriers by actor type, so
 * they won't be re-tasked while ferrying).
 *
 * Spec: WORKSPACE/ai/stage_b4_mounted_transport.md.
 * Doctrine: WORKSPACE/ai/doctrine.md.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.Player)]
	[Desc("WW3MOD experimental: ferries infantry to the frontline using idle IFVs/APCs.")]
	public class MountedTransportBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Ticks between assignment passes.")]
		public readonly int ScanInterval = 100;

		[Desc("Actor types eligible as transports. Must have Cargo trait.")]
		public readonly HashSet<string> CarrierTypes = new();

		[Desc("Actor types eligible as passengers. Must have Passenger trait.")]
		public readonly HashSet<string> PassengerTypes = new();

		[Desc("Minimum passengers to wait for before triggering delivery. Smaller = quicker",
			"but less efficient (single-passenger runs).")]
		public readonly int MinPassengersPerLoad = 2;

		[Desc("Maximum passengers per load (also bounded by Cargo.MaxWeight per the engine).")]
		public readonly int MaxPassengersPerLoad = 5;

		[Desc("Map-cell radius around the bot's home SR where reserves wait. Passengers within",
			"this radius are eligible for loading; out-of-range infantry is already on the line.")]
		public readonly int ReserveZoneRadiusCells = 14;

		[Desc("How long to wait for passengers to board before forcing delivery with whoever's on (or aborting).")]
		public readonly int LoadingTimeoutTicks = 1500;

		[Desc("How close (map cells) the carrier must reach to its drop-off cell before unloading.")]
		public readonly int DropOffArrivalRadius = 3;

		[Desc("How close (map cells) the carrier must reach to its return cell before going idle.")]
		public readonly int ReturnArrivalRadius = 5;

		[Desc("Actor types of the bot's home Supply Route — used to anchor the reserve zone.")]
		public readonly HashSet<string> SupplyRouteTypes = new() { "supplyroute" };

		[Desc("Experimental (default false = frozen): when no frontline contact exists yet, still deliver",
			"toward a forward staging cell (the top PoiMap offensive target) instead of sitting idle until",
			"contact. Only set on the @experimental twin; @stable/controls keep the frozen idle-until-contact.")]
		public readonly bool DeliverBeforeContact = false;

		[Desc("Fraction (percent) of the SR→staging-target distance used as the pre-contact drop-off cell.",
			"50 = halfway between our SR and the top offensive POI. Only used when DeliverBeforeContact is set.")]
		public readonly int PreContactStagingPct = 50;

		[Desc("Experimental (default false = frozen): issue the engine-correct \"Unload\" order on arrival",
			"so carriers actually disembark their passengers. The frozen default issues \"UnloadCargo\" —",
			"which is the UnloadCargo ACTIVITY class name, not an order string, so Cargo.ResolveOrder",
			"silently drops it and passengers never dismount (carrier idles at the drop-off loaded forever).",
			"Kept default-off so @stable/controls stay byte-identical; only set on the @experimental twin.")]
		public readonly bool UnloadOnArrival = false;

		[Desc("Experimental (default 0 = off): half-width, in map cells, of a pickup CORRIDOR along the",
			"SR→drop-off lane. Fresh infantry spawns at the map edge and WALKS toward the front, transiting",
			"the ReserveZoneRadiusCells bubble between scans and never getting caught — so it walks the whole",
			"map. When > 0, PassengerTypes infantry within this perpendicular distance of the SR→drop lane (and",
			"within the lane's span) are ALSO eligible for loading, catching mid-walk units. 0 keeps the frozen",
			"reserve-bubble-only gate; only set on the @experimental twin.")]
		public readonly int PickupCorridorCells = 0;

		[Desc("Experimental (default false = frozen): make the drop-off fog-LEGAL and vision-aware. When set,",
			"the chosen drop cell (frontline OR pre-contact staging) is backed off toward our SR until the",
			"believed anti-ground danger (DangerFieldLayer.GroundDanger — derived from the BeliefStore, no",
			"world scan of enemy actors) at the cell is at/below StandoffDangerThreshold, plus StandoffMarginCells",
			"more. Reads only the fog-legal believed field; zero RNG. Default off ⇒ the frozen @poi/@stable twin",
			"keeps its omniscient thinnest-frontline drop byte-identically. Only set on the @experimental twin.")]
		public readonly bool BelievedDangerStandoff = false;

		[Desc("Believed anti-ground danger (DangerFieldLayer.GroundDanger) at/below which a cell counts as",
			"\"outside believed enemy sight/danger\" — a safe drop. Only used when BelievedDangerStandoff is set.",
			"Default 0 = drop only where the believed field reads completely clear.")]
		public readonly int StandoffDangerThreshold = 0;

		[Desc("Extra cells to back off toward our SR beyond the first believed-safe cell, for a standoff buffer.",
			"Only used when BelievedDangerStandoff is set.")]
		public readonly int StandoffMarginCells = 2;

		[Desc("Cap (map cells) on how far back toward our SR the standoff search walks before giving up and",
			"using the furthest-back sampled cell. Only used when BelievedDangerStandoff is set.")]
		public readonly int StandoffMaxBackoffCells = 20;

		[Desc("Phase 2 commit-on-order audit (§4): COMMIT frontline-delivery passengers to the shared PoiGoalGuard",
			"ledger (key transport:<carrierId>) on load, and RELEASE them on unload. Today this module is fully",
			"ledger-blind — its only cross-module lock is the bespoke IsPassengerReserved seam, which offense's",
			"BuildFreePool does NOT honour, so offense yanks infantry mid-boarding (cancels their EnterTransport).",
			"Committing the passengers in the SHARED ledger makes BuildFreePool's existing IsCommitted check exclude",
			"them — the single-lock replacement for the bespoke seam the design specifies. The capture-ferry path is",
			"NOT committed here: its TECN is already committed by CaptureCoordinator (capture:<id>) and is not in the",
			"world while aboard. Default false ⇒ no commit ⇒ byte-identical @stable/@poi (which omit it).")]
		public readonly bool CommitPassengers = false;

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			base.RulesetLoaded(rules, ai);

			// Case-harden actor-name config (see ActorNameCase). CarrierTypes/PassengerTypes are the
			// "half-guarded" fields — the query sites lowercase the actor name but the sets were built
			// case-sensitively; normalizing the sets closes that gap. SupplyRouteTypes is a hardcoded
			// lowercase default and stays untouched.
			ActorNameCase.NormalizeInPlace(CarrierTypes);
			ActorNameCase.NormalizeInPlace(PassengerTypes);
		}

		public override object Create(ActorInitializer init) { return new MountedTransportBotModule(init.Self, this); }
	}

	public class MountedTransportBotModule : ConditionalTrait<MountedTransportBotModuleInfo>, IBotTick
	{
		enum CarrierState { Loading, Delivering, Unloading, Returning }

		sealed class CarrierTask
		{
			public Actor Carrier;
			public CarrierState State;
			public CPos DropOff;
			public CPos Return;
			public int StateChangedAtTick;
			public HashSet<Actor> ReservedPassengers = new();

			// Non-null => a DIRECTED capture ferry requested by CaptureCoordinator, not a
			// frontline delivery. The single passenger is a TECN; on unload the carrier issues
			// its CaptureActor so it finishes the capture the last few cells on foot.
			public Actor CaptureTarget;
		}

		readonly World world;
		readonly Player player;

		readonly Dictionary<Actor, CarrierTask> carrierTasks = new();
		int scanCountdown;
		InfluenceMap influenceMap;
		PoiMap poiMap;

		// Phase 2 commit-on-order (§4): shared commitment ledger. Resolved only when CommitPassengers is on,
		// so the frozen @stable/@poi twin never looks it up ⇒ byte-identical. Null when the player has no
		// PoiGoalGuard ⇒ every commit/release below is inert.
		PoiGoalGuard goalGuard;

		// Fog-legal believed anti-ground danger field (Stage B). Resolved ONLY when BelievedDangerStandoff
		// is set (the @experimental twin) — the frozen @poi/@stable twin leaves this null and never touches
		// the layer, so its omniscient drop path stays byte-identical. See DOCS/reference/influence-stack.md.
		DangerFieldLayer dangerField;

		/// <summary>True if `actor` is currently reserved by any of this module's carrier tasks
		/// (loading, delivering, unloading, returning). Used by LayeredDefenceBotModule to
		/// avoid issuing AttackMove orders that would override the EnterTransport.</summary>
		public bool IsPassengerReserved(Actor actor)
		{
			foreach (var task in carrierTasks.Values)
				if (task.ReservedPassengers.Contains(actor))
					return true;
			return false;
		}

		// Phase 2 commit-on-order (§4). Objective key namespaces the carrier so the grammar is disjoint from
		// every other executor's (capture:/offense:/defend:/garrison:/…) — audit requirement (d).
		static string TransportObjectiveKey(Actor carrier) => "transport:" + carrier.ActorID;

		// Commit every reserved passenger of a FRONTLINE-DELIVERY task to the shared ledger. Skipped for a
		// capture ferry (task.CaptureTarget != null): its passenger is a TECN already committed to capture:<id>
		// by CaptureCoordinator and is not in the world while aboard, so committing here would only clobber that
		// key. Inert when the flag is off (goalGuard null) ⇒ byte-identical frozen path.
		void CommitTaskPassengers(CarrierTask task)
		{
			if (!CommitOnOrderMath.ShouldCommit(Info.CommitPassengers, goalGuard != null && !goalGuard.IsTraitDisabled)
				|| task.CaptureTarget != null)
				return;

			var key = TransportObjectiveKey(task.Carrier);
			foreach (var pax in task.ReservedPassengers)
				goalGuard.Ledger.Commit(pax, key, world.WorldTick, goalGuard.DefaultCommitmentTicks);
		}

		// Release a task's passengers from the ledger (on unload / task teardown) so a delivered unit re-enters
		// the free pool for offense immediately rather than idling until the TTL lapses. Idempotent — a second
		// release for an already-freed unit is a no-op, so calling it at both unload and teardown is safe.
		void ReleaseTaskPassengers(CarrierTask task)
		{
			if (goalGuard == null || goalGuard.IsTraitDisabled || task.CaptureTarget != null)
				return;

			foreach (var pax in task.ReservedPassengers)
				goalGuard.Ledger.Release(pax);
		}

		/// <summary>Directed capture ferry (experimental, TECN-first). CaptureCoordinator calls this
		/// instead of walking a TECN to a DISTANT capture on foot: reserve the nearest free carrier,
		/// board the capturer, drive it to the target and (on unload) hand the capturer back its
		/// CaptureActor so it finishes the last cells and captures. Returns false when no carrier is
		/// free — the caller then falls back to the on-foot capture, so behaviour degrades gracefully.
		/// Bypasses the frontline PickDropOffCell path entirely: the destination IS the capture target,
		/// so this works pre-contact with no frontline (the 3.1/3.2 unification).</summary>
		public bool TryReserveCaptureFerry(IBot bot, Actor capturer, Actor target)
		{
			if (capturer == null || capturer.IsDead || !capturer.IsInWorld || capturer.Owner != player)
				return false;
			if (target == null || target.IsDead || !target.IsInWorld)
				return false;

			var ownSR = FindOwnSupplyRoute();
			if (ownSR == null)
				return false;

			// Nearest free, empty carrier to the capturer.
			Actor carrier = null;
			var bestDistSq = long.MaxValue;
			foreach (var a in world.Actors)
			{
				if (a.Owner != player || a.IsDead || !a.IsInWorld)
					continue;
				if (!Info.CarrierTypes.Contains(a.Info.Name.ToLowerInvariant()))
					continue;
				if (carrierTasks.ContainsKey(a))
					continue;
				var cargo = a.TraitOrDefault<Cargo>();
				if (cargo == null || !cargo.IsEmpty())
					continue;

				var distSq = (a.CenterPosition - capturer.CenterPosition).LengthSquared;
				if (distSq < bestDistSq)
				{
					bestDistSq = distSq;
					carrier = a;
				}
			}

			if (carrier == null)
				return false;

			// Park the carrier and board the capturer (queued false cancels any AutoTarget/Move
			// so the passenger catches a stationary entry frame — same pattern as TryAssignNewTasks).
			bot.QueueOrder(new Order("Stop", carrier, false));
			bot.QueueOrder(new Order("EnterTransport", capturer, Target.FromActor(carrier), false));

			carrierTasks[carrier] = new CarrierTask
			{
				Carrier = carrier,
				State = CarrierState.Loading,
				DropOff = target.Location,
				Return = ownSR.Location,
				CaptureTarget = target,
				StateChangedAtTick = world.WorldTick,
				ReservedPassengers = new HashSet<Actor> { capturer },
			};

			AIUtils.BotDebug("AI ({0}): mounted-transport — capture-ferry {1} boards {2} → {3}@{4}",
				player.ClientIndex, capturer.Info.Name, carrier.Info.Name, target.Info.Name, target.Location);
			return true;
		}

		Actor FindOwnSupplyRoute()
		{
			return world.Actors.FirstOrDefault(a =>
				a.Owner == player && !a.IsDead && a.IsInWorld
				&& Info.SupplyRouteTypes.Contains(a.Info.Name));
		}

		public MountedTransportBotModule(Actor self, MountedTransportBotModuleInfo info)
			: base(info)
		{
			world = self.World;
			player = self.Owner;
		}

		protected override void TraitEnabled(Actor self)
		{
			scanCountdown = world.LocalRandom.Next(0, Info.ScanInterval);
			influenceMap = world.WorldActor.TraitOrDefault<InfluenceMap>();
			poiMap = world.WorldActor.TraitOrDefault<PoiMap>();

			// Only the @experimental twin (BelievedDangerStandoff set) reads the danger field; the frozen
			// twin keeps dangerField null so ApplyStandoff is an identity pass-through for it.
			dangerField = Info.BelievedDangerStandoff
				? world.WorldActor.TraitOrDefault<DangerFieldLayer>() : null;

			// Commit-on-order ledger (experimental twin only): the frozen twin leaves this null so its
			// commit/release calls are inert and its byte-identity is preserved.
			goalGuard = Info.CommitPassengers
				? player.PlayerActor.TraitOrDefault<PoiGoalGuard>() : null;

			// Visible confirmation in chat that experimental transport is wired for this player.
			// Without this, "is the module even active?" is impossible to verify ingame.
			TextNotificationsManager.AddSystemLine(
				$"[exp-transport] enabled for {player.PlayerName} ({player.Faction.Name})");
			Log.Write("debug",
				$"[exp-transport] TraitEnabled — player={player.PlayerName} carriers={string.Join(",", Info.CarrierTypes)} passengers={Info.PassengerTypes.Count} types");
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (player.WinState != WinState.Undefined)
				return;

			if (--scanCountdown > 0)
				return;
			scanCountdown = Info.ScanInterval;

			// Find own SR — anchor for the reserve zone + return target.
			var ownSR = FindOwnSupplyRoute();
			if (ownSR == null)
				return;
			var srCell = ownSR.Location;

			// Drop stale tasks (dead/foreign carriers). A carrier destroyed mid-Loading still has ALIVE
			// passengers committed under transport:<carrierId> — release them here too (like every other
			// teardown path) or they stay ledger-locked out of offense's free pool until the TTL lapses.
			var stale = carrierTasks.Keys
				.Where(c => c.IsDead || !c.IsInWorld || c.Owner != player)
				.ToList();
			foreach (var c in stale)
			{
				ReleaseTaskPassengers(carrierTasks[c]);
				carrierTasks.Remove(c);
			}

			// Advance existing tasks.
			foreach (var task in carrierTasks.Values.ToList())
				AdvanceTask(bot, task, srCell);

			// Start new tasks for idle empty carriers, if we have passengers.
			TryAssignNewTasks(bot, srCell);
		}

		void AdvanceTask(IBot bot, CarrierTask task, CPos srCell)
		{
			var carrier = task.Carrier;
			var cargo = carrier.TraitOrDefault<Cargo>();
			if (cargo == null)
			{
				ReleaseTaskPassengers(task);
				carrierTasks.Remove(carrier);
				return;
			}

			switch (task.State)
			{
				case CarrierState.Loading:
					// Wait until we have enough passengers OR timeout fires. A directed capture
					// ferry launches with its single TECN aboard (MinPassengers doesn't apply).
					var minPax = task.CaptureTarget != null ? 1 : Info.MinPassengersPerLoad;
					if (cargo.PassengerCount >= minPax)
					{
						LaunchDelivery(bot, task);
					}
					else if (world.WorldTick - task.StateChangedAtTick > Info.LoadingTimeoutTicks)
					{
						if (cargo.PassengerCount > 0)
						{
							LaunchDelivery(bot, task);
						}
						else
						{
							// No one boarded in time — abandon task; carrier returns to idle pool.
							AIUtils.BotDebug("AI ({0}): mounted-transport — {1} loading timed out empty, releasing",
								player.ClientIndex, carrier.Info.Name);
							ReleaseTaskPassengers(task);
							carrierTasks.Remove(carrier);
						}
					}

					break;

				case CarrierState.Delivering:
					// Arrived at drop-off?
					var distToDrop = (carrier.Location - task.DropOff).LengthSquared;
					if (distToDrop <= Info.DropOffArrivalRadius * Info.DropOffArrivalRadius)
					{
						// "UnloadCargo" is the UnloadCargo ACTIVITY name, not an order string — Cargo
						// only resolves "Unload"/"UnloadCargoPassenger", so the legacy string is a no-op
						// and passengers never dismount. UnloadOnArrival (experimental) issues the correct
						// order; the frozen default keeps the broken string so @stable stays byte-identical.
						bot.QueueOrder(new Order(Info.UnloadOnArrival ? "Unload" : "UnloadCargo", carrier, Target.Invalid, false));
						task.State = CarrierState.Unloading;
						task.StateChangedAtTick = world.WorldTick;
						AIUtils.BotDebug("AI ({0}): mounted-transport — {1} unloading at {2}",
							player.ClientIndex, carrier.Info.Name, task.DropOff);
					}

					break;

				case CarrierState.Unloading:
					// Wait until cargo is empty.
					if (cargo.IsEmpty())
					{
						// Capture ferry: hand the disembarked TECN back its CaptureActor so it
						// finishes on foot the last few cells to the target it was ferried to.
						if (task.CaptureTarget != null && !task.CaptureTarget.IsDead && task.CaptureTarget.IsInWorld)
						{
							foreach (var pax in task.ReservedPassengers)
								if (!pax.IsDead && pax.IsInWorld && pax.Owner == player)
								{
									bot.QueueOrder(new Order("CaptureActor", pax, Target.FromActor(task.CaptureTarget), false));
									AIUtils.BotDebug("AI ({0}): mounted-transport — capture-ferry unloaded {1}, capturing {2}",
										player.ClientIndex, pax.Info.Name, task.CaptureTarget.Info.Name);
								}
						}

						// Delivered: the passengers have dismounted at the front. Release their ledger claim so
						// offense can recruit them straight away (better than holding them to the transport TTL
						// through the carrier's whole return trip — the bespoke IsPassengerReserved used to).
						ReleaseTaskPassengers(task);

						bot.QueueOrder(new Order("Move", carrier, Target.FromCell(world, task.Return), false));
						task.State = CarrierState.Returning;
						task.StateChangedAtTick = world.WorldTick;
						AIUtils.BotDebug("AI ({0}): mounted-transport — {1} returning to {2}",
							player.ClientIndex, carrier.Info.Name, task.Return);
					}
					else if (Info.UnloadOnArrival && carrier.IsIdle && cargo.CanUnload())
					{
						// The first Unload order is dropped by Cargo.ResolveOrder when no adjacent cell
						// is free on arrival (!CanUnload); the carrier would then idle here loaded forever.
						// Re-issue once a cell frees up (carrier idle + CanUnload) so it can never stall.
						bot.QueueOrder(new Order("Unload", carrier, Target.Invalid, false));
					}

					break;

				case CarrierState.Returning:
					var distToReturn = (carrier.Location - task.Return).LengthSquared;
					if (distToReturn <= Info.ReturnArrivalRadius * Info.ReturnArrivalRadius)
					{
						AIUtils.BotDebug("AI ({0}): mounted-transport — {1} returned, ready for next load",
							player.ClientIndex, carrier.Info.Name);
						ReleaseTaskPassengers(task);
						carrierTasks.Remove(carrier);
					}

					break;
			}
		}

		void LaunchDelivery(IBot bot, CarrierTask task)
		{
			bot.QueueOrder(new Order("Move", task.Carrier, Target.FromCell(world, task.DropOff), false));
			task.State = CarrierState.Delivering;
			task.StateChangedAtTick = world.WorldTick;
			AIUtils.BotDebug("AI ({0}): mounted-transport — {1} delivering {2} pax to {3}",
				player.ClientIndex, task.Carrier.Info.Name, task.Carrier.Trait<Cargo>().PassengerCount, task.DropOff);
		}

		void TryAssignNewTasks(IBot bot, CPos srCell)
		{
			// Carrier eligibility: owned, alive, in-world, of a configured type, has Cargo,
			// is EMPTY, and not already in a task. We DO NOT require IsIdle. Carriers
			// in the SR rally area still tick AutoTarget and may be in an Attack activity
			// against distant scouts — making them never-idle. Loading sends them a Stop
			// order which cancels that activity and parks them while passengers board.
			//
			// PITFALL (2026-05): adding `a.IsIdle` back to this filter re-introduces
			// the `carriers-candidate=0` bug. See WORKSPACE/ai/handoff_260513.md.
			var owned = world.Actors
				.Where(a => a.Owner == player && !a.IsDead && a.IsInWorld
					&& Info.CarrierTypes.Contains(a.Info.Name.ToLowerInvariant()))
				.ToList();

			var candidates = new List<Actor>();
			foreach (var a in owned)
			{
				var cargo = a.TraitOrDefault<Cargo>();
				var hasCargo = cargo != null;
				var isEmpty = hasCargo && cargo.IsEmpty();
				var inTask = carrierTasks.ContainsKey(a);
				var ok = hasCargo && isEmpty && !inTask;
				if (ok)
					candidates.Add(a);

				// Per-carrier diagnostic — shows why each owned carrier did or didn't qualify.
				// Activity name is the most useful field for diagnosing "never idle" theories.
				var activity = a.CurrentActivity?.GetType().Name ?? "<none>";
				Log.Write("debug",
					$"[exp-transport] carrier {a.Info.Name}@{a.Location} idle={a.IsIdle} activity={activity} pax={(hasCargo ? cargo.PassengerCount.ToString() : "no-cargo")} task={inTask} → {(ok ? "OK" : "skip")}");
			}

			Log.Write("debug",
				$"[exp-transport] scan player={player.PlayerName} carriers-total={owned.Count} carriers-candidate={candidates.Count} tasks-active={carrierTasks.Count}");

			if (candidates.Count == 0)
				return;

			// Passenger pool: infantry of an accepted type within the reserve zone.
			//
			// We DELIBERATELY do not require IsIdle. LayeredDefence often grabs fresh
			// production and orders it forward before we get a tick; if we waited for
			// idle we'd never see them. EnterTransport with queued=false cancels the
			// existing AttackMove, so a passenger walking forward 2 cells turns around
			// to board the carrier — that's the desired flow.
			//
			// The reserve-zone radius is the gate: passengers ALREADY on the line
			// (far from the SR) are excluded. They keep their forward orders.
			var reservedByOthers = new HashSet<Actor>(
				carrierTasks.Values.SelectMany(t => t.ReservedPassengers));

			// Drop-off cell (thinnest frontline / pre-contact staging, fog-legal standoff when enabled).
			// Experimental-only: when a pickup corridor is configured we need the drop cell FIRST to define
			// the SR→drop lane. The frozen twin (PickupCorridorCells 0) keeps the original ordering — the
			// passenger scan below runs on the reserve bubble only, then PickDropOffCell is called once,
			// exactly as before (byte-identical).
			var corridorOn = Info.PickupCorridorCells > 0;
			CPos? dropOff = null;
			if (corridorOn)
				dropOff = PickDropOffCell(srCell);

			var reserveRadiusSq = (long)Info.ReserveZoneRadiusCells * Info.ReserveZoneRadiusCells;
			var availablePassengers = world.Actors
				.Where(a => a.Owner == player && !a.IsDead && a.IsInWorld
					&& Info.PassengerTypes.Contains(a.Info.Name.ToLowerInvariant())
					&& !reservedByOthers.Contains(a)
					// Commit-on-order (§4): never ferry a unit another POI-stack writer already committed —
					// otherwise Commit() below would overwrite its objective. Inert when the flag is off
					// (goalGuard null) ⇒ byte-identical. Mirrors GarrisonBotModule's free-pool gate.
					&& (goalGuard == null || !goalGuard.Ledger.IsCommitted(a, world.WorldTick))
					&& a.Info.HasTraitInfo<PassengerInfo>()
					&& ((a.Location - srCell).LengthSquared <= reserveRadiusSq
						|| (dropOff.HasValue
							&& MountedTransportMath.InCorridor(srCell, dropOff.Value, a.Location, Info.PickupCorridorCells))))
				.ToList();

			Log.Write("debug",
				$"[exp-transport] passengers-eligible={availablePassengers.Count} (reserve-radius={Info.ReserveZoneRadiusCells} corridor={Info.PickupCorridorCells} sr-cell={srCell})");

			if (availablePassengers.Count == 0)
				return;

			// Compute one shared drop-off cell per pass — the thinnest part of our frontline.
			// All carriers in this pass deliver to it; next pass picks a fresh one. Already computed
			// above when a corridor is active; otherwise compute it now (frozen call-site preserved).
			if (!corridorOn)
				dropOff = PickDropOffCell(srCell);
			if (!dropOff.HasValue)
				return;

			foreach (var carrier in candidates)
			{
				var cargo = carrier.TraitOrDefault<Cargo>();
				if (cargo == null)
					continue;

				// Capacity per carrier — bounded by cargo MaxWeight, our config, and supply.
				var cargoInfo = carrier.Info.TraitInfo<CargoInfo>();
				var capacity = System.Math.Min(Info.MaxPassengersPerLoad, cargoInfo.MaxWeight);
				if (capacity <= 0)
					continue;

				// Closest infantry that fits in this carrier and isn't already reserved.
				var toLoad = availablePassengers
					.OrderBy(p => (p.Location - carrier.Location).LengthSquared)
					.Take(capacity)
					.ToList();
				if (toLoad.Count < Info.MinPassengersPerLoad)
					continue;

				// Park the carrier so passengers can board. Without this, AutoTarget can hold the
				// carrier in an Attack activity against a distant target; passengers walking
				// up to it never catch a stationary entry frame and Loading times out empty.
				// Stop clears the current activity (Attack, Move, …); the carrier idles in place
				// while passengers EnterTransport.
				bot.QueueOrder(new Order("Stop", carrier, false));

				// Issue EnterTransport order to each. They walk to the carrier and board.
				foreach (var pax in toLoad)
					bot.QueueOrder(new Order("EnterTransport", pax, Target.FromActor(carrier), false));

				var task = new CarrierTask
				{
					Carrier = carrier,
					State = CarrierState.Loading,
					DropOff = dropOff.Value,
					Return = srCell,
					StateChangedAtTick = world.WorldTick,
					ReservedPassengers = new HashSet<Actor>(toLoad),
				};
				carrierTasks[carrier] = task;

				// Phase 2 commit-on-order (§4): stake the boarding passengers in the shared ledger so offense's
				// BuildFreePool (which honours the ledger but NOT IsPassengerReserved) can't yank them mid-board.
				CommitTaskPassengers(task);

				// Remove reserved passengers from the pool for the next carrier in this pass.
				foreach (var p in toLoad)
					availablePassengers.Remove(p);

				AIUtils.BotDebug("AI ({0}): mounted-transport — {1} reserved {2} pax (cap {3}), drop-off {4}",
					player.ClientIndex, carrier.Info.Name, toLoad.Count, capacity, dropOff.Value);

				if (availablePassengers.Count < Info.MinPassengersPerLoad)
					break;
			}
		}

		// Drop-off cell selection. Picks a contested cell where our line is thinnest, so the
		// delivered infantry plugs the most useful gap. If no frontline yet (no contact),
		// returns null — the module sits idle, waiting for contact.
		CPos? PickDropOffCell(CPos srCell)
		{
			if (influenceMap == null)
				return Info.DeliverBeforeContact ? PreContactStagingCell(srCell) : (CPos?)null;

			var frontline = influenceMap.GetFrontline(player);
			if (frontline == null)
				return Info.DeliverBeforeContact ? PreContactStagingCell(srCell) : (CPos?)null;

			var friendly = influenceMap.GetFriendlyInfluence(player);
			var cellSize = influenceMap.Info.CellSize;

			CPos? best = null;
			var bestScore = long.MinValue;
			var w = frontline.GetLength(0);
			var h = frontline.GetLength(1);

			for (var x = 0; x < w; x++)
			{
				for (var y = 0; y < h; y++)
				{
					if (!frontline[x, y])
						continue;

					var mapCell = new CPos(x * cellSize + cellSize / 2, y * cellSize + cellSize / 2);
					if (!world.Map.Contains(mapCell))
						continue;

					// Score: prefer low friendly influence (gap in OUR line). Drop infantry where
					// they're most needed. Enemy concentration is intentionally NOT considered —
					// delivering infantry into a heavy enemy is a different problem; the LayeredDefence
					// scoring still picks the contested cells; we just choose where to focus delivery.
					var score = -(long)friendly[x, y];
					if (score > bestScore)
					{
						bestScore = score;
						best = mapCell;
					}
				}
			}

			// Fog-legal standoff (experimental): back the thinnest-frontline cell off toward our SR until it
			// leaves the believed enemy ground-danger envelope. Identity pass-through when dangerField is null
			// (frozen twin) — so @poi/@stable keep the raw omniscient cell byte-identically.
			return best.HasValue ? ApplyStandoff(best.Value, srCell) : best;
		}

		// Pre-contact fallback (experimental, DeliverBeforeContact). No frontline exists yet, so
		// stage delivered infantry a fraction of the way from our SR toward the highest-ranked
		// PoiMap offensive target — pushing the reserve forward instead of piling it at the SR.
		CPos? PreContactStagingCell(CPos srCell)
		{
			if (poiMap == null)
				return null;

			var targets = poiMap.GetOffensiveTargets(player);
			if (targets.Count == 0)
				return null;

			var srPos = world.Map.CenterOfCell(srCell);
			var tgtPos = world.Map.CenterOfCell(targets[0].Location);
			var stagePos = srPos + (tgtPos - srPos) * Info.PreContactStagingPct / 100;
			var cell = world.Map.CellContaining(stagePos);
			if (!world.Map.Contains(cell))
				return null;

			// Pre-contact the 50% lerp is blind; apply the same fog-legal standoff so we never stage the
			// reserve inside a believed enemy ground-danger envelope. Identity pass-through when disabled.
			return ApplyStandoff(cell, srCell);
		}

		// Fog-legal standoff: walk the drop cell back toward our SR (deterministic 1-cell steps, zero RNG)
		// sampling the believed anti-ground danger field, and pick the first cell at/below the threshold
		// plus StandoffMarginCells more. When dangerField is null (frozen twin / no field) this is an
		// identity pass-through, preserving @poi/@stable byte-identity. Reads ONLY DangerFieldLayer
		// (derived from the BeliefStore) — never a world scan of enemy actors.
		CPos ApplyStandoff(CPos target, CPos srCell)
		{
			if (dangerField == null || target == srCell)
				return target;

			var targetPos = world.Map.CenterOfCell(target);
			var delta = world.Map.CenterOfCell(srCell) - targetPos;
			var totalCells = delta.Length / 1024;
			if (totalCells <= 0)
				return target;

			var maxBack = System.Math.Min(Info.StandoffMaxBackoffCells, totalCells);

			// Sample distinct on-map cells from the target (step 0) back toward the SR.
			var cells = new List<CPos>();
			var dangers = new List<int>();
			for (var i = 0; i <= maxBack; i++)
			{
				var cell = world.Map.CellContaining(targetPos + delta * i / totalCells);
				if (!world.Map.Contains(cell))
					continue;
				if (cells.Count > 0 && cell == cells[cells.Count - 1])
					continue;
				cells.Add(cell);
				dangers.Add(dangerField.GroundDanger(player, cell));
			}

			if (cells.Count == 0)
				return target;

			var idx = MountedTransportMath.ChooseStandoffIndex(dangers,
				Info.StandoffDangerThreshold, Info.StandoffMarginCells);
			return cells[idx];
		}
	}

	// Pure, world-free geometry for MountedTransportBotModule — split out for NUnit like the other
	// influence-stack math classes (GroundDangerNav, DangerKernelMath). Zero RNG; integer-only.
	public static class MountedTransportMath
	{
		/// <summary>Is cell <paramref name="p"/> within <paramref name="halfWidthCells"/> perpendicular
		/// cells of the segment <paramref name="a"/>→<paramref name="b"/>, AND within the segment's span
		/// (its projection lies between the endpoints)? Exact integer math — the perpendicular test is
		/// `cross² ≤ halfWidth² · |b−a|²` to avoid a square root. A degenerate (zero-length) lane or a
		/// non-positive width is never in-corridor.</summary>
		public static bool InCorridor(CPos a, CPos b, CPos p, int halfWidthCells)
		{
			if (halfWidthCells <= 0)
				return false;

			long dx = b.X - a.X, dy = b.Y - a.Y;
			var lenSq = dx * dx + dy * dy;
			if (lenSq == 0)
				return false;

			long ex = p.X - a.X, ey = p.Y - a.Y;
			var dot = ex * dx + ey * dy;
			if (dot < 0 || dot > lenSq)
				return false;

			var cross = ex * dy - ey * dx;
			long w = halfWidthCells;
			return cross * cross <= w * w * lenSq;
		}

		/// <summary>Given believed anti-ground danger sampled at successive cells from the intended drop
		/// (index 0) back toward our SR, pick the index of the standoff drop cell: the target itself if it
		/// is already at/below <paramref name="threshold"/>, otherwise the first at/below-threshold cell
		/// plus <paramref name="margin"/> more (clamped to the sampled range). If no cell within the sampled
		/// budget is safe, returns the furthest-back cell (closest to our SR). Deterministic; no RNG.</summary>
		public static int ChooseStandoffIndex(IReadOnlyList<int> dangers, int threshold, int margin)
		{
			if (dangers == null || dangers.Count == 0)
				return 0;

			if (dangers[0] <= threshold)
				return 0;

			for (var i = 1; i < dangers.Count; i++)
				if (dangers[i] <= threshold)
					return System.Math.Min(i + System.Math.Max(0, margin), dangers.Count - 1);

			return dangers.Count - 1;
		}
	}
}
