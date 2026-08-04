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
		"alike, and is gated purely on the unit's own stances.")]
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
			"inside the leash means the unit stays put and retries the next time it goes idle.")]
		public readonly int SupplyHuntLeashCells = 20;

		[Desc("Ticks between idle re-checks. INotifyIdle fires every tick an actor stays idle, so",
			"this throttles the provider scan; the phase is staggered per actor deterministically.")]
		public readonly int ScanInterval = 40;

		public override object Create(ActorInitializer init) { return new AutoSeekSupplies(init.Self, this); }
	}

	public class AutoSeekSupplies : INotifyCreated, INotifyIdle
	{
		readonly AutoSeekSuppliesInfo info;

		AmmoPool[] pools;
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
			pools = self.TraitsImplementing<AmmoPool>().ToArray();
			rearmable = self.TraitOrDefault<Rearmable>();
			move = self.TraitOrDefault<IMove>();
			autoTarget = self.TraitOrDefault<AutoTarget>();
		}

		void INotifyIdle.TickIdle(Actor self)
		{
			if (!info.Enabled || move == null)
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
			foreach (var pool in RearmablePools())
				if (SupplyHuntMath.BelowSeekThreshold(pool.CurrentAmmoCount, pool.Info.Ammo, info.AutoSeekAmmoThresholdPerMille))
					return true;

			return false;
		}

		/// <summary>
		/// The pools a provider would actually refill. Rearmable names the subset that resupply
		/// touches; without it, every pool counts.
		/// </summary>
		IEnumerable<AmmoPool> RearmablePools()
		{
			return rearmable != null ? rearmable.RearmableAmmoPools : pools;
		}

		/// <summary>
		/// Nearest provider inside the leash whose push would actually reach this unit. Mirrors the
		/// gates SupplyProvider.IsValidTarget applies from the other side, so we never walk to a
		/// source that would refuse us on arrival.
		/// </summary>
		Actor FindNearestUsableProvider(Actor self)
		{
			var candidates = new List<SupplyHuntMath.Candidate>();
			var actors = new List<Actor>();

			foreach (var a in self.World.ActorsHavingTrait<SupplyProvider>())
			{
				if (a.IsDead || !a.IsInWorld || a == self)
					continue;

				var provider = a.TraitOrDefault<SupplyProvider>();
				if (provider == null || !CanServeUs(self, a, provider))
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

		bool CanServeUs(Actor self, Actor providerActor, SupplyProvider provider)
		{
			if (provider.IsTraitDisabled || provider.IsTraitPaused || provider.CountsAsEmpty)
				return false;

			if (!provider.Info.ValidRelationships.HasRelationship(providerActor.Owner.RelationshipWith(self.Owner)))
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
				var granted = self.TraitsImplementing<ExternalCondition>()
					.Any(e => e.Info.Condition == provider.Info.RearmCondition);
				if (!granted)
					return false;
			}

			// Must be able to afford at least one batch of something we are short of, or the walk
			// buys us nothing.
			return RearmablePools().Any(p => !p.HasFullAmmo && provider.CurrentSupply >= p.Info.SupplyValue);
		}
	}
}
