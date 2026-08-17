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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	/// <summary>
	/// <para>Walks a low-ammo unit to a supply provider's push aura, waits there while the aura refills
	/// it, then walks back to the cell it started from. Queued by <see cref="AutoSeekSupplies"/>
	/// when an idle unit trips its ammo threshold and its stances permit the errand.</para>
	///
	/// <para>The return leg is the point of the activity: without it a squad drains toward the trucks and
	/// the line it was holding quietly empties. Cancelling (a player order, or anything else that
	/// replaces the activity) ends the run immediately and the unit simply stays where it is —
	/// a player order always wins over an errand the unit gave itself.</para>
	/// </summary>
	public class SeekSuppliesAndReturn : Activity
	{
		readonly IMove move;
		readonly IMoveInfo moveInfo;
		readonly AmmoPool[] pools;
		readonly CPos origin;
		readonly Actor provider;
		readonly SupplyProvider providerTrait;
		readonly AutoSeekSupplies seeker;

		// A mobile provider can walk out from under an in-flight approach, so one re-plan is
		// normal; repeated failures mean it is simply unreachable and we should go home.
		const int MaxApproachAttempts = 3;

		// Cells. The origin can be occupied by the time we get back, so settle for close by.
		const int HomeNearEnough = 2;

		// Ticks of standing in the aura with no batch arriving before we conclude nothing is
		// coming. Resets on every delivery, so this bounds a stall, not the total refill time.
		// The case it exists for: a provider holding supply it cannot spend on us — a cache with
		// 3 supply left and an RPG pool costing 50 never fills us and never counts as empty.
		const int MaxStalledTicks = 300;

		SupplyHuntState state = SupplyHuntState.Approaching;
		bool childQueued;
		int approachAttempts;
		int stalledTicks;
		int lastAmmoTotal = -1;

		public SeekSuppliesAndReturn(Actor self, Actor provider)
		{
			move = self.Trait<IMove>();
			moveInfo = self.Info.TraitInfo<IMoveInfo>();

			// Only ever queued by AutoSeekSupplies, which requires Rearmable — a provider will not
			// select a unit without it, so there is no meaningful supply run for such a unit.
			pools = self.Trait<Rearmable>().RearmableAmmoPools.ToArray();

			// Captured once, at hunt start — the cell to come back to. Read now rather than on
			// arrival, since by then the unit is standing next to the truck.
			origin = self.Location;

			this.provider = provider;
			providerTrait = provider.Trait<SupplyProvider>();

			// Only ever queued by AutoSeekSupplies; borrowing its cached per-actor lookups keeps the
			// every-tick eligibility check allocation-free.
			seeker = self.Trait<AutoSeekSupplies>();
		}

		/// <summary>
		/// Exactly the test the seeker's scan used to pick this provider, re-asked every tick — so a
		/// truck that pauses, drains, or turns for home releases us immediately instead of stranding
		/// us in the aura until the stall guard expires. Symmetry by construction: one predicate,
		/// both sides. Everything it reads is cached, so this allocates nothing per tick.
		/// </summary>
		bool ProviderUsable()
		{
			return seeker.CanServe(provider, providerTrait);
		}

		bool Replenished()
		{
			return pools.Length == 0 || pools.All(p => p.HasFullAmmo);
		}

		int AmmoTotal()
		{
			var total = 0;
			foreach (var p in pools)
				total += p.CurrentAmmoCount;

			return total;
		}

		public override bool Tick(Actor self)
		{
			if (IsCanceling)
				return true;

			var providerUsable = ProviderUsable();
			var inAura = providerUsable &&
				SupplyProvider.InAuraRange(provider.CenterPosition, self.CenterPosition, providerTrait.Info.Range);

			var next = SupplyHuntMath.NextState(state, providerUsable, inAura, Replenished(), self.Location == origin);
			if (next != state)
			{
				state = next;

				// Each leg plans its own movement, so drop whatever the previous one queued.
				ChildActivity?.Cancel(self);
				childQueued = false;
			}

			// Let a cancelled child from the previous leg unwind before planning this one.
			// QueueChild APPENDS to the child chain rather than replacing it (Activity.cs:220-226),
			// so queuing the new move now would run the stale one first.
			if (!childQueued && ChildActivity != null)
			{
				TickChild(self);
				return false;
			}

			switch (state)
			{
				case SupplyHuntState.Approaching:
					if (!childQueued)
					{
						QueueChild(move.MoveWithinRange(Target.FromActor(provider), providerTrait.Info.Range,
							targetLineColor: moveInfo.GetTargetLineColor()));
						childQueued = true;
						approachAttempts++;
					}

					TickChild(self);

					// The move ended. Either we are in the aura — next tick's state check will see
					// that and switch to Replenishing — or the path was blocked and we are not.
					// Re-plan a bounded number of times (a mobile truck legitimately moves), then
					// give up and walk home rather than retrying against an unreachable target.
					if (ChildActivity == null)
					{
						childQueued = false;
						if (approachAttempts >= MaxApproachAttempts && !inAura)
							state = SupplyHuntState.Returning;
					}

					return false;

				case SupplyHuntState.Replenishing:
					// Stand still; SupplyProvider.Tick pushes the ammo to us. Give up if nothing
					// arrives for a while, so an unspendable residue can't park us at the truck.
					var total = AmmoTotal();
					if (total > lastAmmoTotal)
					{
						lastAmmoTotal = total;
						stalledTicks = 0;
					}
					else if (++stalledTicks > MaxStalledTicks)
					{
						state = SupplyHuntState.Returning;
						childQueued = false;
					}

					return false;

				case SupplyHuntState.Returning:
					if (!childQueued)
					{
						QueueChild(move.MoveTo(origin, HomeNearEnough, targetLineColor: moveInfo.GetTargetLineColor()));
						childQueued = true;
					}

					TickChild(self);

					// Done when the walk home finishes — including the case where the origin cell got
					// taken while we were away and MoveTo settled for a nearby one.
					return ChildActivity == null;

				default:
					return true;
			}
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			// Standard target line, so a human player can see where a unit that walked off on its
			// own is going and why.
			if (state == SupplyHuntState.Returning || state == SupplyHuntState.Done)
				yield return new TargetLineNode(Target.FromCell(self.World, origin), moveInfo.GetTargetLineColor());
			else if (provider != null && !provider.IsDead && provider.IsInWorld)
				yield return new TargetLineNode(Target.FromActor(provider), moveInfo.GetTargetLineColor());
		}
	}
}
