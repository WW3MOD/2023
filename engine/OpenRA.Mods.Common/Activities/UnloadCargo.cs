#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
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
using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class UnloadCargo : Activity
	{
		readonly Actor self;
		readonly Cargo cargo;
		readonly INotifyUnloadCargo[] notifiers;
		readonly bool unloadAll;
		readonly Aircraft aircraft;
		readonly Mobile mobile;
		readonly bool assignTargetOnFirstRun;
		readonly WDist unloadRange;
		readonly Actor specificPassenger;

		Target destination;
		bool takeOffAfterUnload;

		// Passengers unloaded so far by this activity — drives the group pacing.
		int unloaded;

		public UnloadCargo(Actor self, WDist unloadRange, bool unloadAll = true)
			: this(self, Target.Invalid, unloadRange, unloadAll)
		{
			assignTargetOnFirstRun = true;
		}

		/// <summary>Unload a specific passenger (used by cargo panel individual eject).</summary>
		public UnloadCargo(Actor self, WDist unloadRange, Actor specificPassenger)
			: this(self, Target.Invalid, unloadRange, false)
		{
			assignTargetOnFirstRun = true;
			this.specificPassenger = specificPassenger;
		}

		public UnloadCargo(Actor self, in Target destination, WDist unloadRange, bool unloadAll = true)
		{
			this.self = self;
			cargo = self.Trait<Cargo>();
			notifiers = self.TraitsImplementing<INotifyUnloadCargo>().ToArray();
			this.unloadAll = unloadAll;
			aircraft = self.TraitOrDefault<Aircraft>();
			mobile = self.TraitOrDefault<Mobile>();
			this.destination = destination;
			this.unloadRange = unloadRange;
		}

		public (CPos Cell, SubCell SubCell)? ChooseExitSubCell(Actor passenger)
		{
			var pos = passenger.Trait<IPositionable>();

			return cargo.CurrentAdjacentCells
				.Shuffle(self.World.SharedRandom)
				.Select(c => (c, pos.GetAvailableSubCell(c)))
				.Cast<(CPos, SubCell SubCell)?>()
				.FirstOrDefault(s => s.Value.SubCell != SubCell.Invalid);
		}

		IEnumerable<CPos> BlockedExitCells(Actor passenger)
		{
			var pos = passenger.Trait<IPositionable>();

			// Find the cells that are blocked by transient actors
			return cargo.CurrentAdjacentCells
				.Where(c => pos.CanEnterCell(c, null, BlockedByActor.All) != pos.CanEnterCell(c, null, BlockedByActor.None));
		}

		protected override void OnFirstRun(Actor self)
		{
			if (assignTargetOnFirstRun)
				destination = Target.FromCell(self.World, self.Location);

			// Move to the target destination
			if (aircraft != null)
			{
				// Queue the activity even if already landed in case self.Location != destination
				QueueChild(new Land(self, destination, unloadRange));
				takeOffAfterUnload = !aircraft.AtLandAltitude;
			}
			else if (mobile != null)
			{
				var cell = self.World.Map.Clamp(this.self.World.Map.CellContaining(destination.CenterPosition));
				QueueChild(new Move(self, cell, unloadRange));
			}

			QueueChild(new Wait(cargo.Info.BeforeUnloadDelay));
		}

		public override bool Tick(Actor self)
		{
			if (IsCanceling || cargo.IsEmpty())
				return true;

			// If specific passenger was requested but is no longer in cargo, we're done
			if (specificPassenger != null && !cargo.Passengers.Contains(specificPassenger))
				return true;

			if (cargo.CanUnload())
			{
				foreach (var inu in notifiers)
					inu.Unloading(self);

				var actor = specificPassenger ?? cargo.Peek();
				var spawn = self.CenterPosition;

				var exitSubCell = ChooseExitSubCell(actor);
				if (exitSubCell == null)
				{
					self.NotifyBlocker(BlockedExitCells(actor));
					QueueChild(new Wait(10));
					return false;
				}

				// Check for pre-queued rally point before unloading
				var rallyTarget = cargo.GetEjectRally(actor.ActorID);
				cargo.ClearEjectRally(actor.ActorID);

				cargo.Unload(self, specificPassenger);
				unloaded++;
				self.World.AddFrameEndTask(w =>
				{
					if (actor.Disposed)
						return;

					if (cargo.PassengerCount == 0 && cargo.Info.Neutral)
					{
						var players = self.World.Players;
						var player = players.First(pl => pl.PlayerName == "Neutral");
						self.ChangeOwnerSync(player, false);
					}

					var move = actor.Trait<IMove>();
					var pos = actor.Trait<IPositionable>();

					pos.SetPosition(actor, exitSubCell.Value.Cell, exitSubCell.Value.SubCell);
					pos.SetCenterPosition(actor, spawn);

					actor.CancelActivity();
					w.Add(actor);

					// Apply pre-queued rally point order
					if (rallyTarget.Type != TargetType.Invalid)
					{
						w.IssueOrder(new Order("Move", actor, rallyTarget, false));
					}
				});
			}

			if (!unloadAll || !cargo.CanUnload())
			{
				if (cargo.Info.AfterUnloadDelay > 0)
					QueueChild(new Wait(cargo.Info.AfterUnloadDelay, false));

				if (takeOffAfterUnload)
					QueueChild(new TakeOff(self));

				return true;
			}

			// Pace the rest of the stick. Without this the loop unloads one passenger
			// per tick and a full transport empties in well under a second, which reads
			// as the whole squad falling out at once.
			// Interruptible: this is a cosmetic pause between passengers, and an
			// order given mid-dismount should not have to sit out the rest of an
			// inter-group gap (up to 12 ticks, ~0.7s) before it is noticed.
			var delay = NextUnloadDelay();
			if (delay > 0)
				QueueChild(new Wait(delay));

			return false;
		}

		/// <summary>Ticks to hold before the next passenger steps out. Passengers leave in groups of
		/// UnloadGroupSize back-to-back, then the gap widens by InterGroupUnloadDelayMultiplier before
		/// the next group starts — so a stick of four reads as two-pause-two, not as a single spill.</summary>
		int NextUnloadDelay()
		{
			var groupSize = cargo.Info.UnloadGroupSize;
			var intraDelay = cargo.Info.IntraGroupUnloadDelay;
			if (groupSize <= 0 || intraDelay <= 0)
				return 0;

			return unloaded % groupSize == 0
				? intraDelay * Math.Max(1, cargo.Info.InterGroupUnloadDelayMultiplier)
				: intraDelay;
		}
	}
}
