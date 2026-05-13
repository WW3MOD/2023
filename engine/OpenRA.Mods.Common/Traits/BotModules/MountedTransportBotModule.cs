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
	[Desc("WW3MOD v2: ferries infantry to the frontline using idle IFVs/APCs.")]
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
		}

		readonly World world;
		readonly Player player;

		readonly Dictionary<Actor, CarrierTask> carrierTasks = new();
		int scanCountdown;
		InfluenceMap influenceMap;

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
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (player.WinState != WinState.Undefined)
				return;

			if (--scanCountdown > 0)
				return;
			scanCountdown = Info.ScanInterval;

			// Find own SR — anchor for the reserve zone + return target.
			var ownSR = world.Actors.FirstOrDefault(a =>
				a.Owner == player && !a.IsDead && a.IsInWorld
				&& Info.SupplyRouteTypes.Contains(a.Info.Name));
			if (ownSR == null)
				return;
			var srCell = ownSR.Location;

			// Drop stale tasks (dead/foreign carriers).
			var stale = carrierTasks.Keys
				.Where(c => c.IsDead || !c.IsInWorld || c.Owner != player)
				.ToList();
			foreach (var c in stale)
				carrierTasks.Remove(c);

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
				carrierTasks.Remove(carrier);
				return;
			}

			switch (task.State)
			{
				case CarrierState.Loading:
					// Wait until we have at least MinPassengersPerLoad OR timeout fires.
					if (cargo.PassengerCount >= Info.MinPassengersPerLoad)
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
							carrierTasks.Remove(carrier);
						}
					}

					break;

				case CarrierState.Delivering:
					// Arrived at drop-off?
					var distToDrop = (carrier.Location - task.DropOff).LengthSquared;
					if (distToDrop <= Info.DropOffArrivalRadius * Info.DropOffArrivalRadius)
					{
						bot.QueueOrder(new Order("UnloadCargo", carrier, Target.Invalid, false));
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
						bot.QueueOrder(new Order("Move", carrier, Target.FromCell(world, task.Return), false));
						task.State = CarrierState.Returning;
						task.StateChangedAtTick = world.WorldTick;
						AIUtils.BotDebug("AI ({0}): mounted-transport — {1} returning to {2}",
							player.ClientIndex, carrier.Info.Name, task.Return);
					}

					break;

				case CarrierState.Returning:
					var distToReturn = (carrier.Location - task.Return).LengthSquared;
					if (distToReturn <= Info.ReturnArrivalRadius * Info.ReturnArrivalRadius)
					{
						AIUtils.BotDebug("AI ({0}): mounted-transport — {1} returned, ready for next load",
							player.ClientIndex, carrier.Info.Name);
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
			// Candidate carriers: bot's idle carriers, currently empty, not already tasked.
			var candidates = world.Actors
				.Where(a => a.Owner == player && !a.IsDead && a.IsInWorld && a.IsIdle
					&& Info.CarrierTypes.Contains(a.Info.Name.ToLowerInvariant())
					&& !carrierTasks.ContainsKey(a)
					&& a.Info.HasTraitInfo<CargoInfo>())
				.ToList();
			if (candidates.Count == 0)
				return;

			// Reserve passengers: idle infantry of an accepted type within the reserve zone.
			// Also exclude passengers already reserved by another in-flight task.
			var reservedByOthers = new HashSet<Actor>(
				carrierTasks.Values.SelectMany(t => t.ReservedPassengers));

			var reserveRadiusSq = (long)Info.ReserveZoneRadiusCells * Info.ReserveZoneRadiusCells;
			var availablePassengers = world.Actors
				.Where(a => a.Owner == player && !a.IsDead && a.IsInWorld && a.IsIdle
					&& Info.PassengerTypes.Contains(a.Info.Name.ToLowerInvariant())
					&& !reservedByOthers.Contains(a)
					&& a.Info.HasTraitInfo<PassengerInfo>()
					&& (a.Location - srCell).LengthSquared <= reserveRadiusSq)
				.ToList();

			if (availablePassengers.Count == 0)
				return;

			// Compute one shared drop-off cell per pass — the thinnest part of our frontline.
			// All carriers in this pass deliver to it; next pass picks a fresh one.
			var dropOff = PickDropOffCell(srCell);
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
				return null;

			var frontline = influenceMap.GetFrontline(player);
			if (frontline == null)
				return null;

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

			return best;
		}
	}
}
