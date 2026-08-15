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

		[Desc("Default false = frozen: when no frontline contact exists yet, still deliver toward a forward",
			"staging cell (the top PoiMap offensive target) instead of sitting idle until contact.",
			"NOTE (b8d2e601, 2026-08-02): the @poi twin IS the @stable one (RequiresCondition:",
			"enable-ai-stable) and it now sets this true (ai.yaml, block MountedTransportBotModule@poi), so",
			"@stable no longer takes the default path here — this is not experimental-only any more and the",
			"'@stable keeps the frozen idle-until-contact' claim is dead.")]
		public readonly bool DeliverBeforeContact = false;

		[Desc("Fraction (percent) of the SR→staging-target distance used as the pre-contact drop-off cell.",
			"50 = halfway between our SR and the top offensive POI. Only used when DeliverBeforeContact is set.")]
		public readonly int PreContactStagingPct = 50;

		[Desc("Default false = baseline: deliver pre-contact infantry to the cell the OFFENSIVE RESERVE is",
			"mustering on (PoiOffensiveBotModule.ForwardStagingAnchor) rather than to this module's own",
			"SR→POI lerp. Combined arms: the lerp and the offensive's control-field staging anchor are",
			"computed by different maths from different inputs, so before this the ferry delivered its",
			"passengers away from the armour that was supposed to protect them. Falls back to the lerp",
			"whenever no anchor is published or the anchor fails the reach bound below, so nothing ever",
			"waits on anything — see RendezvousMath.")]
		public readonly bool RendezvousWithOffensiveStaging = false;

		[Desc("Cells the offensive staging anchor may sit BEYOND this module's own drop-off cell (measured",
			"from our SR) and still be accepted as a rendezvous. The anchor advances with the believed",
			"front; without this bound a loaded carrier would follow it into enemy ground, and one AA/AT",
			"hit takes the carrier, its passengers and the tempo together. Deliberately a distance",
			"comparison rather than a danger threshold, so it does not need re-tuning when the danger",
			"field is rescaled. Only used when RendezvousWithOffensiveStaging is set.")]
		public readonly int RendezvousMaxAdvanceCells = 6;

		[Desc("Experimental (default false = frozen): issue the engine-correct \"Unload\" order on arrival",
			"so carriers actually disembark their passengers. The frozen default issues \"UnloadCargo\" —",
			"which is the UnloadCargo ACTIVITY class name, not an order string, so Cargo.ResolveOrder",
			"silently drops it and passengers never dismount (carrier idles at the drop-off loaded forever).",
			"NOTE (b8d2e601, 2026-08-02): the @poi twin is the @stable one (enable-ai-stable) and now sets",
			"this true (ai.yaml, block MountedTransportBotModule@poi), so BOTH shipped twins issue the correct",
			"order; nothing runs the broken default any more and the '@stable stays byte-identical' claim",
			"is dead.")]
		public readonly bool UnloadOnArrival = false;

		[Desc("Experimental (default 0 = off): half-width, in map cells, of a pickup CORRIDOR along the",
			"SR→drop-off lane. Fresh infantry spawns at the map edge and WALKS toward the front, transiting",
			"the ReserveZoneRadiusCells bubble between scans and never getting caught — so it walks the whole",
			"map. When > 0, PassengerTypes infantry within this perpendicular distance of the SR→drop lane (and",
			"within the lane's span) are ALSO eligible for loading, catching mid-walk units. 0 keeps the frozen",
			"reserve-bubble-only gate. NOTE (b8d2e601, 2026-08-02): the @poi twin is the @stable one",
			"(enable-ai-stable) and now sets this to 6 (ai.yaml, block MountedTransportBotModule@poi), so no",
			"shipped twin is left on 0 and the 'only set on the @experimental twin' claim is dead.")]
		public readonly int PickupCorridorCells = 0;

		[Desc("Experimental (default false = frozen): make the drop-off fog-LEGAL and vision-aware. When set,",
			"the chosen drop cell (frontline OR pre-contact staging) is backed off toward our SR until the",
			"believed anti-ground danger (DangerFieldLayer.GroundDanger — derived from the BeliefStore, no",
			"world scan of enemy actors) at the cell is at/below StandoffDangerUnits, plus StandoffMarginCells",
			"more. Reads only the fog-legal believed field; zero RNG. Default off ⇒ the frozen omniscient",
			"thinnest-frontline drop. NOTE (b8d2e601, 2026-08-02): the @poi twin is the @stable one",
			"(enable-ai-stable) and now sets this true (ai.yaml, block MountedTransportBotModule@poi), so",
			"@stable reads the danger field too — its drop is fog-legal now and the omniscient-drop",
			"byte-identity claim is dead.")]
		public readonly bool BelievedDangerStandoff = false;

		[Desc("Believed anti-ground danger (DangerFieldLayer.GroundDanger) at/below which a cell counts as",
			"\"outside believed enemy sight/danger\" — a safe drop. Only used when BelievedDangerStandoff is set.",
			"IN DANGER UNITS (100 = one reference contact at point-blank), NOT raw field units.",
			"NOT 0 any more, and the change is behavioural. At a literal 0 against the GROUND channel — which",
			"unlike the air channel DOES carry the Stage-C territory baseline — no candidate whose cell carried",
			"ANY stamp could qualify. For a FRONTLINE drop, where the whole approach lane sits inside a believed",
			"envelope, that means ChooseStandoffIndex fell through its entire loop and returned the LAST index:",
			"the drop was walked all the way back toward the SR regardless of where the danger actually was.",
			"(A pre-contact staging drop into unstamped ground still qualified at index 0, so this was the",
			"frontline case rather than literally every drop.) A small positive value restores the intended",
			"behaviour of stopping at the first candidate genuinely outside a believed weapon envelope.")]
		public readonly int StandoffDangerUnits = 10;

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

		[Desc("Default false = baseline: a loading carrier drives off the instant MinPassengersPerLoad are",
			"aboard. That is what makes transports leave half empty — TryAssignNewTasks orders up to the",
			"carrier's capacity aboard but the departure test reads the MINIMUM, so a carrier that ordered 5",
			"soldiers drives away the moment the 2nd boards and the other 3 are left chasing it (they then",
			"hold cargo reservations that keep the carrier Locked; see Cargo.LockForPickup).",
			"When set, the carrier instead waits until every seat it actually ordered is filled — but with",
			"three independent releases so it can never hang waiting for a passenger that is not coming:",
			"no reserved passenger left walking, BoardingStallTicks without progress, or LoadingTimeoutTicks",
			"outright. See MountedTransportMath.DecideDeparture, where the no-hang property is NUnit-pinned.")]
		public readonly bool FillBeforeDeparture = false;

		[Desc("Ticks without a single passenger boarding after which a carrier stops waiting and drives with",
			"what it has (provided the load is at least MinPassengersPerLoad). This is the release for a",
			"passenger that is ALIVE but was re-tasked away by another module mid-walk — the still-coming",
			"count cannot see that case because the soldier still exists. Only read when FillBeforeDeparture",
			"is set. 0 disables this release, leaving LoadingTimeoutTicks as the only time-based bound.")]
		public readonly int BoardingStallTicks = 250;

		[Desc("Default 0 = baseline: a capture ferry reserves the whole carrier for the technician, which then",
			"rides ALONE — 4 of a Bradley's 5 seats are spent carrying nobody. When > 0, up to this many",
			"PassengerTypes soldiers are boarded into the seats the technician does not use, so the ferry",
			"arrives with an escort already on site instead of an empty hold.",
			"This is deliberately NOT done by adding tecn to PassengerTypes. That would return the technician",
			"to the general frontline passenger pool and re-open the bug 09877fd5 closed (a capture-layer unit",
			"grabbed by a transport/garrison module and stranded); dd441876 built the directed reservation path",
			"specifically to avoid it. The capturer's claim on the carrier is untouched — this only spends",
			"capacity that is otherwise wasted.",
			"The escorts are NOT capture-capable and must never be told to capture: CarrierTask.Capturer keeps",
			"the technician distinct so the CaptureActor hand-back on unload reaches only it. Handing a rifleman",
			"CaptureActor would NEUTRALISE the building the ferry was sent to take (game-model.md: soldiers",
			"clear, only technicians own).")]
		public readonly int CaptureFerryEscortSeats = 0;

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

			// How many passengers were actually ORDERED aboard. The load can never beat this, so it is
			// what "full" means for the departure decision — the carrier's physical capacity would be the
			// wrong bar whenever there was not enough infantry to fill it.
			public int SeatTarget;

			// Tick of the last observed boarding, for the stall release. Seeded at task creation so a
			// carrier nobody ever walks to still stalls out rather than waiting on a first boarding
			// that never happens.
			public int LastBoardingTick;

			// Passenger count at the previous scan — the edge the stall timer is reset on.
			public int LastSeenAboard;

			// Non-null => a DIRECTED capture ferry requested by CaptureCoordinator, not a
			// frontline delivery. On unload the carrier hands the capturer its CaptureActor so it
			// finishes the capture the last few cells on foot.
			public Actor CaptureTarget;

			// The TECN on a capture ferry — the ONE passenger that may be handed CaptureActor on unload.
			// Tracked apart from ReservedPassengers so the ferry can also carry ordinary soldiers: they
			// ride and dismount as escort, and must never be told to capture, since a soldier entering a
			// building neutralises it instead of taking it (game-model.md). Null on a frontline delivery.
			public Actor Capturer;
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
		// is set. NOTE (b8d2e601, 2026-08-02): BOTH shipped twins set it — MountedTransportBotModule
		// @experimental AND @poi, which IS the @stable twin (RequiresCondition: enable-ai-stable) — so this
		// field is resolved and the standoff path is LIVE on both; the "@poi/@stable leaves this null and
		// keeps its omniscient drop byte-identically" claim is dead.
		// See DOCS/reference/influence-stack.md.
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

		// Commit a task's reserved passengers to the shared ledger so offense's BuildFreePool (which honours
		// the ledger but NOT IsPassengerReserved) cannot yank them mid-board.
		//
		// The CAPTURER is skipped rather than the whole capture-ferry task: CaptureCoordinator already holds
		// it under capture:<targetId> and committing it here would clobber that key. Its escorts have no such
		// claim, and leaving them uncommitted is precisely how offense poaches a soldier that is already
		// walking up the ramp — so they are committed like any other passenger.
		// Inert when the flag is off (goalGuard null) ⇒ byte-identical frozen path.
		void CommitTaskPassengers(CarrierTask task)
		{
			if (!CommitOnOrderMath.ShouldCommit(Info.CommitPassengers, goalGuard != null && !goalGuard.IsTraitDisabled))
				return;

			var key = TransportObjectiveKey(task.Carrier);
			foreach (var pax in task.ReservedPassengers)
				if (pax != task.Capturer)
					goalGuard.Ledger.Commit(pax, key, world.WorldTick, goalGuard.DefaultCommitmentTicks);
		}

		// Release a task's passengers from the ledger (on unload / task teardown) so a delivered unit re-enters
		// the free pool for offense immediately rather than idling until the TTL lapses. Idempotent — a second
		// release for an already-freed unit is a no-op, so calling it at both unload and teardown is safe.
		// Mirrors the commit above in skipping the capturer, whose claim belongs to CaptureCoordinator:
		// releasing it here would drop a capture commitment this module never made.
		void ReleaseTaskPassengers(CarrierTask task)
		{
			if (goalGuard == null || goalGuard.IsTraitDisabled)
				return;

			foreach (var pax in task.ReservedPassengers)
				if (pax != task.Capturer)
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

			// Board the capturer FIRST, then park the carrier. The reverse order left the carrier stopped
			// with no task whenever the boarding order was abandoned below. Both land in the same drain
			// batch and the passenger's arrival is many ticks away, so the "stationary entry frame" the
			// Stop exists for is unaffected by which of the two is issued first.
			//
			// Refused ⇒ do not stop the carrier and do not create the task: a CarrierTask whose capturer was
			// never told to board would occupy the carrier until the loading timeout for nothing. This order
			// is Protected (a directed one-shot ferry, not a recurring stream), so it cannot in fact be
			// refused today — the check is the standing convention for a bool-returning QueueOrder.
			if (!bot.QueueOrder(new Order("EnterTransport", capturer, Target.FromActor(carrier), false)))
				return false;

			bot.QueueOrder(new Order("Stop", carrier, false));

			var task = new CarrierTask
			{
				Carrier = carrier,
				State = CarrierState.Loading,
				DropOff = target.Location,
				Return = ownSR.Location,
				CaptureTarget = target,
				Capturer = capturer,
				StateChangedAtTick = world.WorldTick,
				LastBoardingTick = world.WorldTick,
				SeatTarget = 1,
				ReservedPassengers = new HashSet<Actor> { capturer },
			};

			// Spend the seats the technician does not use. The capturer's claim above is already made and is
			// not touched by this — we only fill capacity that would otherwise travel empty.
			var escorts = RecruitCaptureFerryEscorts(bot, carrier, capturer);
			foreach (var e in escorts)
				task.ReservedPassengers.Add(e);
			task.SeatTarget += escorts.Count;

			carrierTasks[carrier] = task;

			// Commit the escorts (never the capturer — CaptureCoordinator owns its capture:<id> key) so
			// offense's BuildFreePool cannot yank them back off the ramp mid-board.
			CommitTaskPassengers(task);

			AIUtils.BotDebug("AI ({0}): mounted-transport — capture-ferry {1} + {2} escort boards {3} → {4}@{5}",
				player.ClientIndex, capturer.Info.Name, escorts.Count, carrier.Info.Name, target.Info.Name, target.Location);
			return true;
		}

		/// <summary>Board ordinary soldiers into a capture ferry's unused seats. Returns the ones actually
		/// told to board, so a refused order never leaves a phantom reservation the carrier then waits on.
		/// Empty (and completely inert) at the default CaptureFerryEscortSeats of 0.
		///
		/// Draws from PassengerTypes, which deliberately does NOT contain tecn — that exclusion is the
		/// standing protection against a capture-layer unit being pulled into a general transport pool
		/// (dd441876 / 09877fd5), and this method must never be the thing that erodes it. Candidates are
		/// measured from the CARRIER rather than the SR because a ferry carrier is chosen for its proximity
		/// to the capturer, which is not necessarily the reserve bubble's centre.</summary>
		List<Actor> RecruitCaptureFerryEscorts(IBot bot, Actor carrier, Actor capturer)
		{
			var boarded = new List<Actor>();
			if (Info.CaptureFerryEscortSeats <= 0)
				return boarded;

			var cargoInfo = carrier.Info.TraitInfo<CargoInfo>();
			var seats = System.Math.Min(
				System.Math.Min(Info.CaptureFerryEscortSeats, Info.MaxPassengersPerLoad - 1),
				cargoInfo.MaxWeight - 1);
			if (seats <= 0)
				return boarded;

			var reservedByOthers = new HashSet<Actor>(
				carrierTasks.Values.SelectMany(t => t.ReservedPassengers));

			var heliTransport = player.PlayerActor.TraitsImplementing<HelicopterSquadBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);

			var radiusSq = (long)Info.ReserveZoneRadiusCells * Info.ReserveZoneRadiusCells;
			var candidates = world.Actors
				.Where(a => a.Owner == player && !a.IsDead && a.IsInWorld
					&& a != capturer
					&& Info.PassengerTypes.Contains(a.Info.Name.ToLowerInvariant())
					&& !reservedByOthers.Contains(a)
					&& (heliTransport == null || !heliTransport.IsPassengerReserved(a))
					&& (goalGuard == null || !goalGuard.Ledger.IsCommitted(a, world.WorldTick))
					&& a.Info.HasTraitInfo<PassengerInfo>()
					&& (a.Location - carrier.Location).LengthSquared <= radiusSq)
				.OrderBy(a => (a.Location - carrier.Location).LengthSquared)
				.Take(seats)
				.ToList();

			// UNMARKED ⇒ Protected, deliberately, and NOT Recurring like the frontline pool's boarding order.
			// A capture ferry is a directed one-shot: TryReserveCaptureFerry is called once per capture
			// dispatch and never re-offers, so an order dropped here is a seat lost for the whole run rather
			// than one retried on the next scan. Marked Recurring it was suppressed by the arbitration gate's
			// dwell rule for every candidate (a soldier that received any standing order in the preceding
			// dwell window is blocked) — measured as `boarded=0` against `candidates=3`, i.e. the fill
			// silently did nothing. Protected matches the capturer's own EnterTransport a few lines above,
			// which carries the same one-shot rationale.
			foreach (var pax in candidates)
				if (bot.QueueOrder(new Order("EnterTransport", pax, Target.FromActor(carrier), false)))
					boarded.Add(pax);

			Log.Write("debug",
				$"[exp-transport] ferry-escort player={player.PlayerName} carrier={carrier.Info.Name} " +
				$"seats={seats} candidates={candidates.Count} boarded={boarded.Count} tick={world.WorldTick}");

			return boarded;
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

			// A twin that leaves BelievedDangerStandoff off keeps dangerField null, so ApplyStandoff is an
			// identity pass-through for it. NOTE (b8d2e601, 2026-08-02): no shipped twin is in that state —
			// @poi (which IS @stable) and @experimental both set the flag in ai.yaml.
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
					// A capture ferry's floor is ONE — its technician. MinPassengersPerLoad is a frontline
					// notion and must not gate a capture: a ferry that could not raise an escort still has to
					// go. With FillBeforeDeparture the ferry does briefly wait for the escorts it ordered
					// (measured at ~100 ticks on the reference run, 115 → 215), which is the fullness/tempo
					// trade being made deliberately; every release in DecideDeparture still applies, so a
					// missing escort delays the capture by at most the stall bound, never indefinitely.
					var minPax = task.CaptureTarget != null ? 1 : Info.MinPassengersPerLoad;
					var aboard = cargo.PassengerCount;

					// Reset the stall timer on the edge where the load actually grew. A boarded passenger is
					// removed from the world, so this also drives the still-coming count below.
					if (aboard != task.LastSeenAboard)
					{
						if (aboard > task.LastSeenAboard)
							task.LastBoardingTick = world.WorldTick;
						task.LastSeenAboard = aboard;
					}

					// Still walking = reserved, alive, in the world. Boarding removes a passenger from the
					// world and death removes it outright, so this counts down without any bookkeeping and
					// can never stay positive for a passenger that no longer exists.
					var stillComing = 0;
					foreach (var pax in task.ReservedPassengers)
						if (!pax.IsDead && pax.IsInWorld && pax.Owner == player)
							stillComing++;

					var departure = MountedTransportMath.DecideDeparture(
						Info.FillBeforeDeparture,
						aboard, task.SeatTarget, stillComing, minPax,
						world.WorldTick - task.StateChangedAtTick, Info.LoadingTimeoutTicks,
						world.WorldTick - task.LastBoardingTick, Info.BoardingStallTicks);

					if (departure == CarrierDeparture.AbortEmpty)
					{
						// No one boarded — abandon task; carrier returns to idle pool. Stragglers still
						// walking are released so they stop chasing a carrier that has no task.
						AIUtils.BotDebug("AI ({0}): mounted-transport — {1} loading gave up empty, releasing",
							player.ClientIndex, carrier.Info.Name);
						ReleaseTaskPassengers(task);
						carrierTasks.Remove(carrier);
					}
					else if (departure != CarrierDeparture.Wait)
					{
						Log.Write("debug",
							$"[exp-transport] depart player={player.PlayerName} carrier={carrier.Info.Name} " +
							$"aboard={aboard} target={task.SeatTarget} still-coming={stillComing} " +
							$"reason={departure} ferry={task.CaptureTarget != null} tick={world.WorldTick}");
						LaunchDelivery(bot, task);
					}

					break;

				case CarrierState.Delivering:
					// Arrived at drop-off?
					var distToDrop = (carrier.Location - task.DropOff).LengthSquared;

					// A carrier that is idle short of its drop has lost its Move and, since Delivering has no
					// timeout, would sit there loaded for the rest of the match. That happens for real: a
					// passenger arriving after departure calls Cargo.ReserveSpace, whose LockForPickup does
					// self.CancelActivity() on the CARRIER — killing the delivery move outright. Re-issuing is
					// the recovery. FillBeforeDeparture also removes the usual cause (it does not leave
					// stragglers walking toward a departed carrier), so this is the belt to that braces.
					if (Info.FillBeforeDeparture && carrier.IsIdle
						&& distToDrop > Info.DropOffArrivalRadius * Info.DropOffArrivalRadius)
					{
						Log.Write("debug",
							$"[exp-transport] delivery-move-reissued player={player.PlayerName} " +
							$"carrier={carrier.Info.Name}@{carrier.Location} drop={task.DropOff} tick={world.WorldTick}");
						bot.QueueOrder(new Order("Move", carrier, Target.FromCell(world, task.DropOff), false));
						break;
					}

					if (distToDrop <= Info.DropOffArrivalRadius * Info.DropOffArrivalRadius)
					{
						// "UnloadCargo" is the UnloadCargo ACTIVITY name, not an order string — Cargo
						// only resolves "Unload"/"UnloadCargoPassenger", so the legacy string is a no-op
						// and passengers never dismount. UnloadOnArrival issues the correct order; the default
						// keeps the broken string. NOTE (b8d2e601, 2026-08-02): @stable (the @poi twin) sets
						// UnloadOnArrival true (ai.yaml, MountedTransportBotModule@poi), so the broken
						// branch is dead config for it too — no shipped profile issues "UnloadCargo".
						bot.QueueOrder(new Order(Info.UnloadOnArrival ? "Unload" : "UnloadCargo", carrier, Target.Invalid, false));

						// WW3MOD retreat-on-unload: QUEUE the return move right behind the Unload so the carrier
						// withdraws the instant unloading completes, instead of hovering IDLE at the drop for up to
						// a full ScanInterval until the Unloading state below detects empty. Only when the REAL
						// "Unload" order was issued (UnloadOnArrival) AND a drop cell is free right now (CanUnload) —
						// otherwise the Unload is dropped by Cargo.ResolveOrder and a queued Move would fly the carrier
						// home still LOADED. When not pre-queued (frozen "UnloadCargo" no-op, or no free cell yet), the
						// Unloading state's empty-detection issues the move exactly as before (ledger release + capture
						// hand-back still run there). Gated implicitly on UnloadOnArrival ⇒ a twin with it off (engine
						// default) never unloads, so this branch stays inert and byte-identical for it.
						if (Info.UnloadOnArrival && cargo.CanUnload())
							bot.QueueOrder(new Order("Move", carrier, Target.FromCell(world, task.Return), true));

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
						// INVARIANT: CaptureActor goes to task.Capturer and to NOTHING ELSE. The ferry may
						// now carry ordinary soldiers in the technician's spare seats
						// (CaptureFerryEscortSeats), and a soldier handed CaptureActor would walk in and
						// NEUTRALISE the building the ferry was sent to take — soldiers clear, only
						// technicians own (game-model.md). Iterating ReservedPassengers here, as this loop
						// used to, is exactly that bug; the single-TECN restriction it relied on is what
						// the Capturer field replaces. The escorts simply dismount and are released.
						var capturer = task.Capturer;
						if (task.CaptureTarget != null && !task.CaptureTarget.IsDead && task.CaptureTarget.IsInWorld
							&& capturer != null && !capturer.IsDead && capturer.IsInWorld && capturer.Owner == player)
						{
							bot.QueueOrder(new Order("CaptureActor", capturer, Target.FromActor(task.CaptureTarget), false));
							AIUtils.BotDebug("AI ({0}): mounted-transport — capture-ferry unloaded {1}, capturing {2}",
								player.ClientIndex, capturer.Info.Name, task.CaptureTarget.Info.Name);
						}

						// Delivered: the passengers have dismounted at the front. Release their ledger claim so
						// offense can recruit them straight away (better than holding them to the transport TTL
						// through the carrier's whole return trip — the bespoke IsPassengerReserved used to).
						ReleaseTaskPassengers(task);

						// Only advance the FSM if the move was accepted. Neither Delivering nor Returning has a
						// timeout (LoadingTimeoutTicks guards Loading only), so a dropped Move plus a state
						// advance parks the carrier at the front forever.
						if (!bot.QueueOrder(new Order("Move", carrier, Target.FromCell(world, task.Return), false)))
							break;

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
			// Same rule, and this is the worse case: the carrier is LOADED. Staying in Loading means the
			// existing LoadingTimeoutTicks path retries and can still release the passengers; advancing to
			// Delivering without a move would strand them aboard permanently.
			if (!bot.QueueOrder(new Order("Move", task.Carrier, Target.FromCell(world, task.DropOff), false)))
				return;

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

			// The HELICOPTER transport module draws from the same reserve bubble and its passenger filter is a
			// superset of PassengerTypes, so without this we yank soldiers that are already walking to a heli
			// (and it yanks ours — it consults our IsPassengerReserved for the same reason). The commitment
			// ledger does NOT cover this on @stable: neither module sets its commit flag there, so both leave
			// goalGuard null and never touch it. Resolved per-pass rather than cached because this module is
			// constructed before the heli twin on some profiles; TraitsImplementing + first-enabled because the
			// module is twinned and TraitOrDefault would throw on "multiple traits".
			var heliTransport = player.PlayerActor.TraitsImplementing<HelicopterSquadBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);

			// Drop-off cell (thinnest frontline / pre-contact staging, fog-legal standoff when enabled).
			// When a pickup corridor is configured we need the drop cell FIRST to define the SR→drop lane.
			// At PickupCorridorCells 0 the original ordering holds — the passenger scan below runs on the
			// reserve bubble only, then PickDropOffCell is called once. NOTE (b8d2e601, 2026-08-02): no
			// shipped twin is on 0 any more; @stable (the @poi twin) sets 6, so the corridor ordering is
			// what actually runs on both profiles.
			var corridorOn = Info.PickupCorridorCells > 0;
			CPos? dropOff = null;
			if (corridorOn)
				dropOff = PickDropOffCell(srCell);

			var reserveRadiusSq = (long)Info.ReserveZoneRadiusCells * Info.ReserveZoneRadiusCells;
			var availablePassengers = world.Actors
				.Where(a => a.Owner == player && !a.IsDead && a.IsInWorld
					&& Info.PassengerTypes.Contains(a.Info.Name.ToLowerInvariant())
					&& !reservedByOthers.Contains(a)
					&& (heliTransport == null || !heliTransport.IsPassengerReserved(a))
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

				// Issue EnterTransport order to each. They walk to the carrier and board.
				// RECURRING — census §2 rank 1 and the other half of the §4.1 beat: 50 t (3.0 s), no dedup on
				// the passenger, and IsIdle DELIBERATELY not required, so it turns a unit that LayeredDefence
				// just sent forward straight back around. TryAssignNewTasks re-offers it every scan.
				//
				// Reserve only the passengers that were actually told to board. A refused passenger left in
				// ReservedPassengers is one the carrier then waits LoadingTimeoutTicks (1500 t = 90 s) for
				// while it never walks over — bounded, but a carrier idling a minute and a half for nobody.
				var boarding = new List<Actor>();
				foreach (var pax in toLoad)
					if (bot.QueueOrder(new Order("EnterTransport", pax, Target.FromActor(carrier), false), BotOrderDamping.Recurring))
						boarding.Add(pax);

				if (boarding.Count == 0)
					continue;

				// Park the carrier so passengers can board, but only now that at least one of them was
				// actually told to come. Without this, AutoTarget can hold the carrier in an Attack
				// activity against a distant target; passengers walking up to it never catch a stationary
				// entry frame and Loading times out empty. Stop clears the current activity (Attack,
				// Move, …); the carrier idles in place while passengers EnterTransport. Issued AFTER the
				// boarding loop for the same reason as the capture ferry: stopping first would leave a
				// carrier parked with no task whenever every boarding order was refused.
				bot.QueueOrder(new Order("Stop", carrier, false));

				var task = new CarrierTask
				{
					Carrier = carrier,
					State = CarrierState.Loading,
					DropOff = dropOff.Value,
					Return = srCell,
					StateChangedAtTick = world.WorldTick,
					LastBoardingTick = world.WorldTick,

					// The seats we actually ordered filled — not `capacity`, which we may not have had the
					// infantry to reach. Waiting on capacity we never recruited would be a guaranteed stall.
					SeatTarget = boarding.Count,
					ReservedPassengers = new HashSet<Actor>(boarding),
				};
				carrierTasks[carrier] = task;

				// Phase 2 commit-on-order (§4): stake the boarding passengers in the shared ledger so offense's
				// BuildFreePool (which honours the ledger but NOT IsPassengerReserved) can't yank them mid-board.
				CommitTaskPassengers(task);

				// Remove from the pool for the next carrier in this pass. Deliberately toLoad and not
				// boarding: a passenger whose order was just refused would be refused again by every later
				// carrier in the same pass, since they all read the same standing record on the same tick.
				// Skipping it for the rest of the pass is right; it returns to the pool on the next scan.
				foreach (var p in toLoad)
					availablePassengers.Remove(p);

				AIUtils.BotDebug("AI ({0}): mounted-transport — {1} reserved {2} of {3} pax (cap {4}), drop-off {5}",
					player.ClientIndex, carrier.Info.Name, boarding.Count, toLoad.Count, capacity, dropOff.Value);

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

			// Fog-legal standoff: back the thinnest-frontline cell off toward our SR until it leaves the
			// believed enemy ground-danger envelope. Identity pass-through when dangerField is null.
			// NOTE (b8d2e601, 2026-08-02): @stable (the @poi twin) sets BelievedDangerStandoff true
			// (ai.yaml, MountedTransportBotModule@poi), so it no longer keeps the raw omniscient cell —
			// the standoff runs for it too and the byte-identity claim is dead.
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
			var lerp = ApplyStandoff(cell, srCell);

			// COMBINED ARMS: prefer the cell the offensive reserve is actually mustering on, so infantry
			// arrive mounted AT the armour rather than at a cell only this module ever knew about. Applied
			// AFTER the standoff so the fallback we hand RendezvousMath is the cell we would really have
			// used — comparing against the pre-standoff cell would measure a reach we never intended to take.
			return ResolveRendezvous(lerp, srCell);
		}

		// Fold the offensive reserve's published staging anchor into a pre-contact drop-off decision.
		// Returns `fallback` untouched whenever the rendezvous is off, no offensive module is enabled, no
		// anchor has been published yet, or the anchor fails RendezvousMath's reach bound — so every failure
		// path degrades to the legacy behaviour rather than stalling. Nothing here waits on anything.
		CPos? ResolveRendezvous(CPos? fallback, CPos srCell)
		{
			if (!Info.RendezvousWithOffensiveStaging || !fallback.HasValue)
				return fallback;

			// Resolved PER PASS, deliberately NOT cached behind a one-shot latch. CaptureCoordinatorBotModule
			// caches its transport-module lookup that way (`transportModuleResolved`), and a lookup that
			// happened to resolve null would stay null for the rest of the match with nothing to show for it.
			// Same twinning reason as the heliTransport lookup above: TraitOrDefault would throw on the twin.
			var offensive = player.PlayerActor.TraitsImplementing<PoiOffensiveBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);

			var anchor = offensive?.ForwardStagingAnchor;

			RendezvousMath.ResolveDropOff(
				Info.RendezvousWithOffensiveStaging, anchor.HasValue,
				srCell.X, srCell.Y,
				anchor?.X ?? 0, anchor?.Y ?? 0,
				fallback.Value.X, fallback.Value.Y,
				Info.RendezvousMaxAdvanceCells,
				out var x, out var y);

			var rendezvous = new CPos(x, y);

			// The anchor is a control-field cell the offensive walked to, so it is on-map by construction —
			// but this module owns the order that follows, so it verifies rather than assuming.
			if (!world.Map.Contains(rendezvous))
				return fallback;

			if (rendezvous != fallback.Value)
				Log.Write("debug",
					$"[exp-transport] rendezvous player={player.PlayerName} anchor={anchor} " +
					$"lerp={fallback.Value} → drop={rendezvous} tick={world.WorldTick}");

			return rendezvous;
		}

		// Fog-legal standoff: walk the drop cell back toward our SR (deterministic 1-cell steps, zero RNG)
		// sampling the believed anti-ground danger field, and pick the first cell at/below the threshold
		// plus StandoffMarginCells more. When dangerField is null (flag off / no field) this is an identity
		// pass-through. NOTE (b8d2e601, 2026-08-02): that no longer describes @poi/@stable — it sets
		// BelievedDangerStandoff true (ai.yaml, MountedTransportBotModule@poi), so the walk-back runs for
		// it and the byte-identity claim is dead. Reads ONLY DangerFieldLayer (from the BeliefStore) —
		// never a world scan of enemy actors.
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
				dangerField.GroundDangerUnitsToField(Info.StandoffDangerUnits), Info.StandoffMarginCells);
			return cells[idx];
		}
	}

	/// <summary>Why a loading carrier was (or was not) released to drive. Every non-Wait value names the
	/// specific thing that ended the wait, so a debug line can explain a half-empty departure instead of
	/// leaving it to be inferred.</summary>
	public enum CarrierDeparture
	{
		/// <summary>More passengers are still walking in and both patience bounds have time left.</summary>
		Wait,

		/// <summary>Every seat we ordered aboard is filled — the load cannot get fuller.</summary>
		Full,

		/// <summary>Baseline path only: the configured MinPassengersPerLoad is aboard.</summary>
		Threshold,

		/// <summary>No reserved passenger is left walking, so this load is as full as it will ever get.</summary>
		NobodyElseComing,

		/// <summary>Boarding stopped progressing — a reserved passenger is alive but was re-tasked away
		/// and is never going to arrive.</summary>
		Stalled,

		/// <summary>Hard patience bound elapsed; drive with whoever is aboard.</summary>
		Timeout,

		/// <summary>Nothing boarded at all — abandon the task and return the carrier to the pool.</summary>
		AbortEmpty,
	}

	// Pure, world-free geometry for MountedTransportBotModule — split out for NUnit like the other
	// influence-stack math classes (GroundDangerNav, DangerKernelMath). Zero RNG; integer-only.
	public static class MountedTransportMath
	{
		/// <summary>Decide whether a loading carrier drives now, keeps waiting, or gives up.
		///   <paramref name="fillBeforeDeparture"/> — false reproduces the legacy rule exactly (depart the
		///     instant <paramref name="minPassengers"/> are aboard) so a profile that does not opt in is
		///     unchanged; true waits for the seats it actually ordered.
		///   <paramref name="aboard"/>              — Cargo.PassengerCount right now.
		///   <paramref name="seatTarget"/>          — how many passengers were actually ordered aboard. The
		///     load can never beat this, so it is the honest definition of "full" — NOT the carrier's
		///     physical capacity, which we may never have had the infantry to fill.
		///   <paramref name="stillComing"/>         — reserved passengers still alive, in the world and
		///     therefore still walking. A boarded passenger leaves the world, so it counts down on its own.
		///   <paramref name="ticksSinceLastBoarding"/>/<paramref name="boardingStallTicks"/> — progress bound.
		///   <paramref name="ticksLoading"/>/<paramref name="loadTimeoutTicks"/> — hard patience bound.
		///
		/// WHY A WAITING CARRIER CAN NEVER HANG. Waiting for a fuller load is only safe if something ends
		/// the wait no matter what the passengers do, so there are three independent releases and the last
		/// two do not consult passenger state at all:
		///   (a) stillComing hits 0 — every reserved passenger has boarded or died. Covers the case the
		///       user called out: the last seat can never be filled because that soldier no longer exists.
		///   (b) ticksSinceLastBoarding passes the stall bound — covers a passenger that is alive but was
		///       re-tasked away by another module and will never arrive, which (a) cannot see.
		///   (c) ticksLoading passes the hard timeout — the unconditional backstop.
		/// (b) and (c) are monotonic functions of elapsed time alone, so Wait is unreachable once either
		/// bound is passed. NUnit pins that as an invariant rather than leaving it as a claim.</summary>
		public static CarrierDeparture DecideDeparture(
			bool fillBeforeDeparture,
			int aboard, int seatTarget, int stillComing, int minPassengers,
			int ticksLoading, int loadTimeoutTicks,
			int ticksSinceLastBoarding, int boardingStallTicks)
		{
			if (!fillBeforeDeparture)
			{
				if (aboard >= minPassengers)
					return CarrierDeparture.Threshold;

				if (ticksLoading > loadTimeoutTicks)
					return aboard > 0 ? CarrierDeparture.Timeout : CarrierDeparture.AbortEmpty;

				return CarrierDeparture.Wait;
			}

			// Guarded against a zero/negative target: without this, `aboard >= seatTarget` would report a
			// carrier that ordered nobody aboard as Full with an empty hold.
			if (seatTarget > 0 && aboard >= seatTarget)
				return CarrierDeparture.Full;

			if (stillComing <= 0)
				return aboard > 0 ? CarrierDeparture.NobodyElseComing : CarrierDeparture.AbortEmpty;

			// Only releases a load that is already worth delivering; a stalled load still under the minimum
			// keeps waiting for the hard bound below, which will take it at whatever it has.
			if (boardingStallTicks > 0 && ticksSinceLastBoarding >= boardingStallTicks && aboard >= minPassengers)
				return CarrierDeparture.Stalled;

			if (ticksLoading > loadTimeoutTicks)
				return aboard > 0 ? CarrierDeparture.Timeout : CarrierDeparture.AbortEmpty;

			return CarrierDeparture.Wait;
		}

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
