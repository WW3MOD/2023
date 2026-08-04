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
using OpenRA.Mods.Common.Activities;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("When this unit goes idle low on ammo, walk to the nearest friendly supply source whose",
		"push aura can actually replenish it, top up, then walk back to where it was standing.",
		"A tactical behaviour, not a bot behaviour: it runs for human-owned and bot-owned units",
		"alike, and is gated purely on the unit's own stances.",
		"Does nothing on a unit without Rearmable — no supply provider will select such a unit,",
		"so the walk could never pay off.",
		"The unit is on an activity for the whole trip and so is not idle, which means AutoTarget's",
		"idle scan does not run and retaliation (gated on !IsIdle) does not fire: a unit on a supply",
		"run is combat-inert until it gets home. That is the intended trade — it is out of ammo, and",
		"the alternative is standing on the line unable to shoot anyway.")]
	// Deliberately not Requires<AmmoPoolInfo>: this sits on the shared ^Soldier template, which
	// also covers unarmed classes (medic, engineer, technician). They simply have no pool to trip.
	public class AutoSeekSuppliesInfo : TraitInfo
	{
		[Desc("Master switch. Ships OFF — flip this one line to true to enable auto-seek.")]
		public readonly bool Enabled = false;

		[Desc("Seek supplies once a rearmable pool drops below this many parts per thousand of its",
			"capacity (250 = 25%).")]
		public readonly int AutoSeekAmmoThresholdPerMille = 250;

		[Desc("Furthest a supply source can be, in cells, and still be worth walking to. No source",
			"inside the leash means the unit stays put and retries the next time it goes idle.",
			"This is STRAIGHT-LINE distance, not path length: a source 20 cells away across a river",
			"passes the leash and is only rejected later, when the approach fails to reach it.")]
		public readonly int SupplyHuntLeashCells = 20;

		[Desc("Ticks between idle re-checks. INotifyIdle fires every tick an actor stays idle, so",
			"this throttles the provider scan; the phase is staggered per actor deterministically.")]
		public readonly int ScanInterval = 40;

		public override object Create(ActorInitializer init) { return new AutoSeekSupplies(init.Self, this); }
	}

	public class AutoSeekSupplies : INotifyCreated, INotifyIdle
	{
		readonly AutoSeekSuppliesInfo info;

		Rearmable rearmable;
		IMove move;
		AutoTarget autoTarget;
		int scanTicks;

		public AutoSeekSupplies(Actor self, AutoSeekSuppliesInfo info)
		{
			this.info = info;

			// Deterministic per-actor phase so a squad that goes idle together does not scan on the
			// same tick. Must NOT come from World.SharedRandom: this trait loads for every profile,
			// so drawing from the synced stream would shift it for control games too (conventions.md).
			scanTicks = info.ScanInterval > 0 ? (int)(self.ActorID % (uint)info.ScanInterval) : 0;
		}

		void INotifyCreated.Created(Actor self)
		{
			rearmable = self.TraitOrDefault<Rearmable>();
			move = self.TraitOrDefault<IMove>();
			autoTarget = self.TraitOrDefault<AutoTarget>();
		}

		void INotifyIdle.TickIdle(Actor self)
		{
			// No Rearmable means no provider can ever select us (SupplyProvider.IsValidTarget
			// requires it), so the whole errand is futile — the combat engineer carries an AmmoPool
			// and this trait via ^Soldier, but no Rearmable, and would otherwise walk to a truck,
			// wait out the stall guard, walk home, and repeat on every idle cycle.
			if (!info.Enabled || move == null || rearmable == null)
				return;

			if (--scanTicks > 0)
				return;

			scanTicks = info.ScanInterval;

			if (!StancesPermit() || !NeedsSupplies())
				return;

			var provider = FindNearestUsableProvider(self);
			if (provider == null)
				return;

			self.QueueActivity(false, new SeekSuppliesAndReturn(self, provider));
			self.ShowTargetLines();
		}

		bool StancesPermit()
		{
			// A unit without AutoTarget has no stances to consult; treat it as fully permissive,
			// which is the same fallback AmmoPool.AutoRearmIfAllEmpty applies.
			var fire = autoTarget?.Stance ?? UnitStance.FireAtWill;
			var engagement = autoTarget?.EngagementStanceValue ?? EngagementStance.Defensive;
			var resupply = autoTarget?.ResupplyBehaviorValue ?? ResupplyBehavior.Auto;

			return SupplyHuntMath.StancesPermitHunt(fire, engagement, resupply);
		}

		bool NeedsSupplies()
		{
			foreach (var pool in rearmable.RearmableAmmoPools)
				if (SupplyHuntMath.BelowSeekThreshold(pool.CurrentAmmoCount, pool.Info.Ammo, info.AutoSeekAmmoThresholdPerMille))
					return true;

			return false;
		}

		/// <summary>
		/// Nearest provider inside the leash whose push would actually reach this unit.
		/// </summary>
		Actor FindNearestUsableProvider(Actor self)
		{
			var candidates = new List<SupplyHuntMath.Candidate>();
			var actors = new List<Actor>();

			foreach (var a in self.World.ActorsHavingTrait<SupplyProvider>())
			{
				if (!CanServe(self, a))
					continue;

				var distanceSquared = (a.CenterPosition - self.CenterPosition).HorizontalLengthSquared;
				if (!SupplyHuntMath.WithinLeash(distanceSquared, info.SupplyHuntLeashCells))
					continue;

				candidates.Add(new SupplyHuntMath.Candidate(distanceSquared, a.ActorID));
				actors.Add(a);
			}

			var best = SupplyHuntMath.SelectNearest(candidates);
			return best < 0 ? null : actors[best];
		}

		/// <summary>
		/// The ONE eligibility test — used both to pick a provider and, by SeekSuppliesAndReturn,
		/// to decide every tick whether the one it is walking to is still worth reaching. Sharing it
		/// is the point: a provider we would not walk to must also be one we stop walking to, and two
		/// separate copies of this rule drifted apart the moment one of them gained a clause.
		///
		/// Mirrors the gates SupplyProvider applies from the other side, so a unit never walks to a
		/// source that would refuse it on arrival.
		/// </summary>
		public static bool CanServe(Actor seeker, Actor providerActor)
		{
			if (providerActor == null || providerActor.IsDead || !providerActor.IsInWorld || providerActor == seeker)
				return false;

			var provider = providerActor.TraitOrDefault<SupplyProvider>();
			if (provider == null)
				return false;

			// No Rearmable, no selection: SupplyProvider.IsValidTarget rejects such a unit outright.
			var rearmable = seeker.TraitOrDefault<Rearmable>();
			if (rearmable == null)
				return false;

			// Drained, paused, disabled, mid-restock, or about to reserve its remainder and drive
			// home — CanServeNow is the provider's own Tick-level serving ladder, asked rather than
			// reproduced (it reads private restock state).
			if (provider.CountsAsEmpty || !provider.CanServeNow)
				return false;

			if (!provider.Info.ValidRelationships.HasRelationship(providerActor.Owner.RelationshipWith(seeker.Owner)))
				return false;

			// A docking-gated provider (the Logistics Center's unit.docked) does not resupply by
			// proximity — reaching it is the Rearmable/Resupply dock path's job, which AmmoPool
			// already drives. Walking into its aura would achieve nothing, so skip it here.
			if (!string.IsNullOrEmpty(provider.Info.DockedCondition))
				return false;

			// The recipient-side gate: the provider only pushes to units carrying its RearmCondition
			// (replenish-soldiers, which only infantry hold). This is what makes "would this aura
			// actually replenish THIS unit" a real question rather than a proximity check.
			if (!string.IsNullOrEmpty(provider.Info.RearmCondition))
			{
				var granted = seeker.TraitsImplementing<ExternalCondition>()
					.Any(e => e.Info.Condition == provider.Info.RearmCondition);
				if (!granted)
					return false;
			}

			// Must be able to afford at least one batch of something we are short of, or the walk
			// buys us nothing.
			return rearmable.RearmableAmmoPools.Any(p => !p.HasFullAmmo && provider.CurrentSupply >= p.Info.SupplyValue);
		}
	}
}
