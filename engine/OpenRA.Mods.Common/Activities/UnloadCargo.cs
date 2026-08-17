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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
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
		readonly IMoveInfo moveInfo;

		/// <summary>Waypoint to stamp the dismount marker on, or null to draw none. Set only by the player
		/// order path — an AI errand or an emergency bail is not a plan anybody asked to be shown.</summary>
		readonly CPos? markerCell;

		Target destination;
		bool takeOffAfterUnload;

		// Passengers unloaded so far by this activity — drives the group pacing.
		int unloaded;

		public UnloadCargo(Actor self, WDist unloadRange, bool unloadAll = true, CPos? markerCell = null)
			: this(self, Target.Invalid, unloadRange, unloadAll)
		{
			assignTargetOnFirstRun = true;
			this.markerCell = markerCell;
		}

		/// <summary>Unload a specific passenger (used by cargo panel individual eject).</summary>
		public UnloadCargo(Actor self, WDist unloadRange, Actor specificPassenger, CPos? markerCell = null)
			: this(self, Target.Invalid, unloadRange, false)
		{
			assignTargetOnFirstRun = true;
			this.specificPassenger = specificPassenger;
			this.markerCell = markerCell;
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
			moveInfo = self.Info.TraitInfoOrDefault<IMoveInfo>();
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			if (markerCell == null || cargo.UnloadMarker == null)
				yield break;

			// WHAT THIS MARKER CLAIMS: "THE DOORS OPEN HERE" — not "a soldier will be on this cell".
			// The distinction is easy to lose because the sprite is a man, and the icon actively invites
			// the stronger reading. Two separate reasons it cannot promise placement. First, this cell is
			// a PREDICTION of where the transport will be standing, not a destination: the order carries
			// no cell at all and OnFirstRun assigns destination = self.Location whenever the activity
			// happens to come up. Second, even once it has stopped, each passenger takes a SHUFFLED pick
			// of CurrentAdjacentCells (see ChooseExitSubCell) rolled per passenger at dismount time — so
			// no soldier's cell exists to be drawn when this node is emitted. The infantryman is kept
			// over a neutral glyph because it reads as "troops out here" at a glance where a crate reads
			// as cargo; that is a legibility trade made knowingly, not a placement forecast.
			//
			// A TILE node only — deliberately no line node. The leg out to this cell is already drawn by
			// the move that precedes us, and emitting a second node for the same cell would stack a
			// zero-length leg and a duplicate end marker on top of it. What is missing from the picture
			// without this is not the line, it is WHAT happens at the end of it.
			//
			// The marker is an infantry sprite, so it needs the owner's palette; the terrain palette that
			// cell-overlay tiles use would render it in scrambled colours.
			yield return new TargetLineNode(
				Target.FromCell(self.World, markerCell.Value),
				moveInfo?.GetTargetLineColor() ?? Color.White,
				cargo.UnloadMarker,
				"player" + self.Owner.InternalName,
				cargo.Info.UnloadMarkerAlpha);
		}

		public (CPos Cell, SubCell SubCell)? ChooseExitSubCell(Actor passenger)
		{
			var pos = passenger.Trait<IPositionable>();

			// Cast<T?> so FirstOrDefault yields null, not (default, Invalid), when no cell is free — Tick's
			// exitSubCell == null branch (NotifyBlocker + retry) is unreachable without it. A post-6.0 analyzer
			// calls this cast always-empty (CA2021); that is a false positive, do not "simplify" it away.
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

		/// <summary>Ticks to hold before the next passenger steps out. The rhythm itself lives in
		/// Cargo.NextUnloadDelay, shared with the emergency bail so both dismounts pace alike.</summary>
		int NextUnloadDelay()
		{
			return Cargo.NextUnloadDelay(unloaded, cargo.Info.UnloadGroupSize,
				cargo.Info.IntraGroupUnloadDelay, cargo.Info.InterGroupUnloadDelayMultiplier);
		}
	}
}
