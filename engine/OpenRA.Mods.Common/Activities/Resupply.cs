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
using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class Resupply : Activity
	{
		readonly IHealth health;
		readonly RepairsUnits[] allRepairsUnits;
		readonly Target host;
		readonly WDist closeEnough;
		readonly Repairable repairable;
		readonly RepairableNear repairableNear;
		readonly Rearmable rearmable;
		readonly INotifyResupply[] notifyResupplies;
		readonly INotifyDockHost[] notifyDockHosts;
		readonly INotifyDockClient[] notifyDockClients;
		readonly ICallForTransport[] transportCallers;
		readonly IMove move;
		readonly Aircraft aircraft;
		readonly Mobile mobile;
		readonly IMoveInfo moveInfo;
		readonly bool stayOnResupplier;
		readonly bool wasRepaired;
		readonly PlayerResources playerResources;
		readonly int unitCost;
		readonly MoveCooldownHelper moveCooldownHelper;
		readonly AmmoPool[] pools;

		/// <summary>
		/// <para>
		/// True when this visit was the unit's OWN idea, taken because every pool was empty — the
		/// ammo branch of <see cref="AmmoPool.AutoRearm"/>. False everywhere else, and the five other
		/// construction sites (aircraft at a pad, the two repair orders, the minelayer, the Lua
		/// binding) leave it so: none of them is an ammunition errand, so none of them has a reason
		/// that can lapse. With it false this activity is stock, down to
		/// <see cref="Activity.ChildHasPriority"/>.
		/// </para>
		/// <para>
		/// It buys exactly one thing: permission to re-ask the question the constructor froze. The
		/// answer is <see cref="AmmoPool.OutOfEssentialAmmo(IEnumerable{AmmoPool})"/> negated — the
		/// dispatch condition itself, not a second definition of "enough ammo". Both sides must name
		/// the SAME function: an exit still reading AllPoolsEmpty after the dispatch predicate widened
		/// would already be satisfied when a partially-dry unit set off, ending the errand on tick one.
		/// Matching
		/// <see cref="SeekSupplyProvider"/>, which runs the truck/cache half of the same errand.
		/// </para>
		/// </summary>
		readonly bool dispatchedBecauseDry;

		int remainingTicks;
		bool played;
		bool actualResupplyStarted;
		ResupplyType activeResupplyTypes = ResupplyType.None;

		CPos origin;
		bool returningHome;
		bool homeMoveQueued;

		// Cells. The origin can be occupied by the time we get back, so settle for close by.
		// Matches SeekSupplyProvider and SeekSuppliesAndReturn, which run the same leg.
		const int HomeNearEnough = 2;

		public Resupply(Actor self, Actor host, WDist closeEnough, bool stayOnResupplier = false, bool dispatchedBecauseDry = false)
		{
			this.host = Target.FromActor(host);
			this.closeEnough = closeEnough;
			this.stayOnResupplier = stayOnResupplier;
			allRepairsUnits = host.TraitsImplementing<RepairsUnits>().ToArray();
			health = self.TraitOrDefault<IHealth>();
			repairable = self.TraitOrDefault<Repairable>();
			repairableNear = self.TraitOrDefault<RepairableNear>();
			rearmable = self.TraitOrDefault<Rearmable>();
			notifyResupplies = host.TraitsImplementing<INotifyResupply>().ToArray();
			notifyDockHosts = host.TraitsImplementing<INotifyDockHost>().ToArray();
			notifyDockClients = self.TraitsImplementing<INotifyDockClient>().ToArray();
			transportCallers = self.TraitsImplementing<ICallForTransport>().ToArray();
			move = self.Trait<IMove>();
			aircraft = move as Aircraft;
			mobile = move as Mobile;
			moveInfo = self.Info.TraitInfo<IMoveInfo>();
			playerResources = self.Owner.PlayerActor.Trait<PlayerResources>();
			moveCooldownHelper = new MoveCooldownHelper(self.World, move as Mobile) { RetryIfDestinationBlocked = true };

			this.dispatchedBecauseDry = dispatchedBecauseDry;

			// EVERY pool, not Rearmable.RearmableAmmoPools — the two sets answer different questions
			// and are not the same actor-for-actor (AmmoPool.AllPoolsEmpty).
			pools = self.TraitsImplementing<AmmoPool>().ToArray();

			// PITFALL: an activity that re-evaluates its own reason inside Tick MUST clear this.
			// With the default, Activity.TickOuter runs `TickChild(self) && (finishing ||
			// Tick(self))` (Activity.cs:112), so Tick is skipped entirely for as long as a child is
			// alive — and the approach to the host IS one long child. That is why the constructor's
			// frozen activeResupplyTypes was never revisited before arrival. Clearing it also makes
			// the parent responsible for ticking the child, which is handled in Tick below.
			ChildHasPriority = !dispatchedBecauseDry;

			var valued = self.Info.TraitInfoOrDefault<ValuedInfo>();
			unitCost = valued != null ? valued.Cost : 0;

			var cannotRepairAtHost = health == null || health.DamageState == DamageState.Undamaged
				|| allRepairsUnits.Length == 0
				|| ((repairable == null || !repairable.Info.RepairActors.Contains(host.Info.Name))
					&& (repairableNear == null || !repairableNear.Info.RepairActors.Contains(host.Info.Name)));

			if (!cannotRepairAtHost)
			{
				activeResupplyTypes |= ResupplyType.Repair;

				// HACK: Reservable logic can't handle repairs, so force a take-off if resupply included repairs.
				// TODO: Make reservation logic or future docking logic properly handle this.
				wasRepaired = true;
			}

			var cannotRearmAtHost = rearmable == null || !rearmable.Info.RearmActors.Contains(host.Info.Name) || rearmable.RearmableAmmoPools.All(p => p.HasFullAmmo);
			if (!cannotRearmAtHost)
				activeResupplyTypes |= ResupplyType.Rearm;
		}

		protected override void OnFirstRun(Actor self)
		{
			// The cell to come back to. Read here rather than in the constructor because AutoRearm
			// queues us with QueueActivity(false, …), which CANCELS the pre-empted order and leaves
			// us sitting BEHIND it — the unit is still finishing that cell when we are constructed.
			// The cancelled order is gone rather than paused (Activity.Cancel nulls NextActivity,
			// Activity.cs:198), so the origin cell is all that survives of "where he came from".
			if (dispatchedBecauseDry)
				origin = self.Location;
		}

		public override bool Tick(Actor self)
		{
			// Wait for the cooldown to expire before releasing the unit if this was cancelled
			if (IsCanceling && remainingTicks > 0)
			{
				remainingTicks--;
				return false;
			}

			var isHostInvalid = host.Type != TargetType.Actor || !host.Actor.IsInWorld;
			var isCloseEnough = false;
			if (!isHostInvalid)
			{
				// Negative means there's no distance limit.
				// If RepairableNear, use TargetablePositions instead of CenterPosition
				// to ensure the actor moves close enough to the host.
				// Otherwise check against host CenterPosition.
				if (closeEnough < WDist.Zero)
					isCloseEnough = true;
				else if (repairableNear != null)
					isCloseEnough = host.IsInRange(self.CenterPosition, closeEnough);
				else
				{
					// PITFALL: measure a ground unit's arrival from its CELL, not from its body. A
					// cell-sharing unit's CenterPosition carries a MapGrid.SubCellOffsets entry it
					// neither chose nor can shed — up to ~393 units — while closeEnough is
					// WDist.Zero for any host that has no RearmsUnits trait to read a CloseEnough
					// off (AmmoPool.cs:374), which is every ground rearm host in this mod. Only the
					// holder of the zero-offset subcell could ever satisfy that, and two soldiers
					// cannot both hold it on one cell: whichever reached the depot second used to
					// stand there forever, re-queueing a no-op approach every tick while the pool
					// he came for filled from the host's proximity aura instead.
					//
					// Subtracting the offset is a NO-OP for full-cell units (SubCellOffsets[0] is
					// zero, so every vehicle is unchanged) and for any caller whose tolerance
					// already exceeds the subcell reach (Repairable and LayMines both pass 512). It
					// changes behaviour only where the tolerance is smaller than a subcell offset.
					// Aircraft are untouched twice over: they are not Mobile, and the approach test
					// below excludes them anyway.
					//
					// Deliberately NOT widened to the host's footprint. That would also paper over
					// an EVEN-dimensioned host, whose CenterPosition is a cell CORNER
					// (BuildingInfo.CenterOffset) that no unit can stand on — so a zero tolerance is
					// unsatisfiable there for vehicles too. No ground rearm host is even-sized
					// today; if one is ever added, that is a separate defect and wants its own fix
					// rather than being hidden by a loose arrival test here.
					var selfPos = self.CenterPosition;
					if (mobile != null)
						selfPos -= self.World.Map.Grid.OffsetOfSubCell(mobile.ToSubCell);

					isCloseEnough = (host.CenterPosition - selfPos).HorizontalLengthSquared <= closeEnough.LengthSquared;
				}
			}

			// This ensures transports are also cancelled when the host becomes invalid
			if (!IsCanceling && isHostInvalid)
				Cancel(self, true);

			if (IsCanceling || isHostInvalid)
			{
				// Only tick host INotifyResupply traits one last time if host is still alive
				if (!isHostInvalid)
					foreach (var notifyResupply in notifyResupplies)
						notifyResupply.ResupplyTick(host.Actor, self, ResupplyType.None);

				// HACK: If the activity is cancelled while we're on the host resupplying (or about to start resupplying),
				// move actor outside the resupplier footprint to prevent it from blocking other actors.
				// Additionally, if the host is no longer valid, make aircraft take off.
				if (isCloseEnough || isHostInvalid)
					OnResupplyEnding(self, isHostInvalid);

				return true;
			}

			// Everything in this block is unreachable unless dispatchedBecauseDry — with the flag
			// false ChildHasPriority keeps its default and Tick is not even called while the
			// approach is running.
			if (dispatchedBecauseDry)
			{
				if (returningHome)
					return TickReturnHome(self);

				// The two bits of the frozen set that can go stale mid-walk, re-asked once per tick.
				//
				// Only while still EN ROUTE: once actualResupplyStarted the unit is standing at the
				// host with the dock notifications sent, and the right thing then is to fill up and
				// leave properly, not to turn round on the first batch.
				//
				// Repair is deliberately untouched. It was decided from a damage state that no
				// passer-by can mend (nothing pushes health in the field), and a unit that also came
				// to be repaired still has a reason to arrive — so the errand only ends when the
				// WHOLE frozen set has emptied, which is this activity's existing exit condition
				// rather than a new one.
				if (!actualResupplyStarted
					&& activeResupplyTypes.HasFlag(ResupplyType.Rearm)
					&& (AmmoPool.SelfAssignedErrandIsOver(dispatchedBecauseDry, pools) || !HostStillWorthReaching()))
				{
					activeResupplyTypes &= ~ResupplyType.Rearm;

					if (activeResupplyTypes == 0)
						return BeginReturnHome(self);
				}

				// ChildHasPriority is clear, so the parent owns the child's ticking.
				if (ChildActivity != null)
					TickChild(self);
			}

			var result = moveCooldownHelper.Tick(false);
			if (result != null)
				return result.Value;

			if (activeResupplyTypes != 0 && aircraft == null && !isCloseEnough)
			{
				// Only reachable on the dry errand, where the parent ticks alongside a live
				// approach: QueueChild APPENDS (Activity.cs:220-226), so planning a second approach
				// here would walk the route twice. Inert for every other caller — when they reach
				// Tick at all, TickChild has already emptied the child chain.
				if (ChildActivity != null)
					return false;

				var targetCell = self.World.Map.CellContaining(host.Actor.CenterPosition);

				// HACK: Repairable needs the actor to move to host center.
				// TODO: Get rid of this or at least replace it with something less hacky.
				moveCooldownHelper.NotifyMoveQueued();
				if (repairableNear == null)
					QueueChild(move.MoveOntoTarget(self, host, WVec.Zero, null, moveInfo.GetTargetLineColor()));
				else
					QueueChild(move.MoveWithinRange(host, closeEnough, targetLineColor: moveInfo.GetTargetLineColor()));

				var delta = (self.CenterPosition - host.CenterPosition).LengthSquared;
				transportCallers.FirstOrDefault(t => t.MinimumDistance.LengthSquared < delta)?.RequestTransport(self, targetCell);

				return false;
			}

			// We don't want to trigger this until we've reached the resupplier and can start resupplying
			if (!actualResupplyStarted && activeResupplyTypes > 0)
			{
				actualResupplyStarted = true;
				foreach (var notifyResupply in notifyResupplies)
					notifyResupply.BeforeResupply(host.Actor, self, activeResupplyTypes);

				foreach (var nd in notifyDockClients)
					nd.Docked(self, host.Actor);

				foreach (var nd in notifyDockHosts)
					nd.Docked(host.Actor, self);
			}

			if (activeResupplyTypes.HasFlag(ResupplyType.Repair))
				RepairTick(self);

			if (activeResupplyTypes.HasFlag(ResupplyType.Rearm) && rearmable.RearmTick(self, host.Actor))
				activeResupplyTypes &= ~ResupplyType.Rearm;

			foreach (var notifyResupply in notifyResupplies)
				notifyResupply.ResupplyTick(host.Actor, self, activeResupplyTypes);

			if (activeResupplyTypes == 0)
			{
				OnResupplyEnding(self);

				// A self-assigned errand ends where it began: the man who left the line to fetch
				// ammunition walks back to it rather than loitering at the depot. Same rule as
				// SeekSupplyProvider, which runs the truck/cache half of this errand — the two
				// halves of one user report should not behave differently.
				//
				// After OnResupplyEnding, not instead of it, so the host still gets its Undocked
				// notifications; BeginReturnHome cancels the leave-host child it may have queued.
				if (dispatchedBecauseDry)
					return BeginReturnHome(self);

				return true;
			}

			return false;
		}

		/// <summary>
		/// <para>Can the host we are walking to still pay for a batch of something we want — asked every
		/// tick of the approach, because the answer was frozen in the constructor and the depot is being
		/// drained by everyone else in the meantime.</para>
		///
		/// <para>THE REPORTED BUG, in one sentence: a unit dispatched to a stocked Logistics Centre that
		/// ran dry while it walked used to arrive anyway and stand there. The dock path could not fix
		/// itself on arrival either — <c>Rearmable.RearmTick</c> correctly declines to serve a pool the
		/// provider cannot pay for and reports the pool done, so the activity ends with the unit parked at
		/// a useless depot, and the idle re-decision then chose to wait beside it
		/// (<see cref="AmmoPool.AnyRearmHostWithinLeash"/>, fixed in the same change).</para>
		///
		/// <para>PARITY, not a new idea. <see cref="SeekSupplyProvider"/> runs the truck/cache half of the
		/// very same errand and has re-asked exactly this since the affordability work landed — its
		/// <c>TargetValid</c> is <see cref="AmmoPool.HostCanAffordSomethingWeNeed"/>, and
		/// <see cref="SupplyHuntMath.NextState"/> answers <c>!providerUsable</c> with <c>Returning</c>.
		/// The Logistics Centre half was simply never given the same test, and the two halves of one user
		/// report should not behave differently. Going HOME rather than stopping where we stand is that
		/// same parity: it is what the truck half does on a drained provider.</para>
		///
		/// <para>THE POOL SET IS <c>pools</c> — every pool, the set the DISPATCHERS chose on
		/// (<see cref="AmmoPool.ChooseAffordableResupplier"/>), not the narrower <c>Rearmable</c> subset.
		/// An exit test stricter than the dispatch test that started the errand would end it on tick one
		/// for any actor whose two sets differ; that is the trap EssentialAmmoTest's
		/// <c>TheErrandExitTestCannotBeSatisfiedAtDispatch</c> exists to pin, in its other form.</para>
		///
		/// <para>NO THRASH GUARD, deliberately. Abandoning hands the unit back to
		/// <see cref="AmmoPool.AutoRearmIfDry"/> on the next idle, which re-picks with
		/// <see cref="AmmoPool.ChooseAffordableResupplier"/> — so the depot we just walked away from is
		/// excluded by the same predicate that made us leave, and cannot be re-chosen until it can
		/// genuinely serve us. Re-choosing it once it CAN is correct rather than oscillation.</para>
		/// </summary>
		bool HostStillWorthReaching()
		{
			// Host death and removal are handled by the isHostInvalid branch above; this is only about
			// supply. A host with no SupplyProvider charges nothing and always passes.
			return AmmoPool.HostCanAffordSomethingWeNeed(host.Actor, pools);
		}

		/// <summary>Switch to the walk home. Returns what Tick should return.</summary>
		bool BeginReturnHome(Actor self)
		{
			returningHome = true;

			if (ChildActivity != null)
				ChildActivity.Cancel(self);

			// TickReturnHome plans the return leg once the cancelled approach has finished the cell
			// it was crossing.
			homeMoveQueued = false;
			return false;
		}

		bool TickReturnHome(Actor self)
		{
			// A child cancelled by the approach has to unwind before the return leg is planned:
			// QueueChild APPENDS, so queuing now would run the stale move first and carry us the
			// rest of the way to the host we just gave up on.
			if (!homeMoveQueued && ChildActivity != null)
			{
				TickChild(self);
				return false;
			}

			if (!homeMoveQueued)
			{
				QueueChild(move.MoveTo(origin, HomeNearEnough, targetLineColor: moveInfo.GetTargetLineColor()));
				homeMoveQueued = true;
			}

			TickChild(self);

			// Done when the walk home finishes — including the case where the origin cell was taken
			// while we were away and MoveTo settled for a nearby one.
			return ChildActivity == null;
		}

		public override void Cancel(Actor self, bool keepQueue = false)
		{
			// HACK: force move activities to ignore the transit-only cells when cancelling
			// The idle handler will take over and move them into a safe cell
			if (ChildActivity != null)
				foreach (var c in ChildActivity.ActivitiesImplementing<Move>())
					c.Cancel(self, false, true);

			foreach (var t in transportCallers)
				t.MovementCancelled(self);

			base.Cancel(self, keepQueue);
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			if (returningHome)
			{
				yield return new TargetLineNode(Target.FromCell(self.World, origin), moveInfo.GetTargetLineColor());
				yield break;
			}

			if (ChildActivity == null)
				yield return new TargetLineNode(host, moveInfo.GetTargetLineColor());
			else
			{
				var current = ChildActivity;
				while (current != null)
				{
					foreach (var n in current.TargetLineNodes(self))
						yield return n;

					current = current.NextActivity;
				}
			}
		}

		void OnResupplyEnding(Actor self, bool isHostInvalid = false)
		{
			var rp = !isHostInvalid ? host.Actor.TraitOrDefault<RallyPoint>() : null;
			if (aircraft != null)
			{
				if (wasRepaired || isHostInvalid || (!stayOnResupplier && aircraft.Info.TakeOffOnResupply))
				{
					if (self.CurrentActivity.NextActivity == null && rp != null && rp.Path.Count > 0)
					{
						moveCooldownHelper.NotifyMoveQueued();
						foreach (var cell in rp.Cells)
							QueueChild(new AttackMoveActivity(self, () => move.MoveTo(
								cell,
								1,
								ignoreActor: repairableNear != null ? null : host.Actor,
								targetLineColor: aircraft.Info.TargetLineColor)));
					}
					else
						QueueChild(new TakeOff(self));

					aircraft.UnReserve();
				}

				// Aircraft without TakeOffOnResupply remain on the resupplier until something else needs it
				// The rally point location is queried by the aircraft before it takes off
				else
					aircraft.AllowYieldingReservation();
			}
			else if (!stayOnResupplier && !isHostInvalid)
			{
				// If there's no next activity, move to rallypoint if available, else just leave host if Repairable.
				// Do nothing if RepairableNear (RepairableNear actors don't enter their host and will likely remain within closeEnough).
				// If there's a next activity and we're not RepairableNear, first leave host if the next activity is not a Move.
				moveCooldownHelper.NotifyMoveQueued();
				if (self.CurrentActivity.NextActivity == null)
				{
					if (rp != null && rp.Path.Count > 0)
						foreach (var cell in rp.Cells)
							QueueChild(new AttackMoveActivity(self, () => move.MoveTo(cell, 1, repairableNear != null ? null : host.Actor, true, moveInfo.GetTargetLineColor())));
					else if (repairableNear == null)
						QueueChild(move.MoveToTarget(self, host));
				}
				else if (repairableNear == null && self.CurrentActivity.NextActivity is not Move)
					QueueChild(move.MoveToTarget(self, host));
			}

			foreach (var nd in notifyDockClients)
				nd.Undocked(self, host.Actor);

			foreach (var nd in notifyDockHosts)
				nd.Undocked(host.Actor, self);
		}

		void RepairTick(Actor self)
		{
			var repairsUnits = allRepairsUnits.FirstOrDefault(r => !r.IsTraitDisabled && !r.IsTraitPaused);
			if (repairsUnits == null)
			{
				if (!allRepairsUnits.Any(r => r.IsTraitPaused))
					activeResupplyTypes &= ~ResupplyType.Repair;

				return;
			}

			if (health.DamageState == DamageState.Undamaged)
			{
				if (host.Actor.Owner != self.Owner)
					host.Actor.Owner.PlayerActor.TraitOrDefault<PlayerExperience>()?.GiveExperience(repairsUnits.Info.PlayerExperience);

				Game.Sound.PlayNotification(self.World.Map.Rules, self.Owner, "Speech", repairsUnits.Info.FinishRepairingNotification, self.Owner.Faction.InternalName);
				TextNotificationsManager.AddTransientLine(self.Owner, repairsUnits.Info.FinishRepairingTextNotification);

				activeResupplyTypes &= ~ResupplyType.Repair;
				return;
			}

			if (remainingTicks == 0)
			{
				var hpToRepair = repairable != null && repairable.Info.HpPerStep > 0 ? repairable.Info.HpPerStep : repairsUnits.Info.HpPerStep;

				// Cast to long to avoid overflow when multiplying by the health
				var value = (long)unitCost * repairsUnits.Info.ValuePercentage;
				var cost = value == 0 ? 0 : Math.Max(1, (int)(hpToRepair * value / (health.MaxHP * 100L)));

				if (!played)
				{
					played = true;
					Game.Sound.PlayNotification(self.World.Map.Rules, self.Owner, "Speech", repairsUnits.Info.StartRepairingNotification, self.Owner.Faction.InternalName);
					TextNotificationsManager.AddTransientLine(self.Owner, repairsUnits.Info.StartRepairingTextNotification);
				}

				if (!playerResources.TakeCash(cost, true))
				{
					remainingTicks = 1;
					return;
				}

				self.InflictDamage(host.Actor, new Damage(-hpToRepair, repairsUnits.Info.RepairDamageTypes));
				remainingTicks = repairsUnits.Info.Interval;
			}
			else
				--remainingTicks;
		}
	}
}
