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

		[Desc("Break off a LIVE order once every ammo pool is empty and walk to the nearest rearm actor.",
			"The seek above is idle-triggered, and a soldier marching under an attack-move order is never",
			"idle (Actor.IsIdle is CurrentActivity == null) — so a man who empties on the advance keeps",
			"advancing with nothing to shoot. AmmoPool's own dispatcher has the same blind spot: it fires",
			"from INotifyAttack on the shot that empties the pool and from INotifyBecomingIdle, and",
			"AmmoPool is not ITick, so nobody ever asks again. This is the missing periodic ask.",
			"Ships OFF so the trait's behaviour is unchanged until a mod opts in.")]
		public readonly bool ReturnWhenEmpty = false;

		[Desc("Ticks between empty-state re-checks for ReturnWhenEmpty. Shorter than ScanInterval: this",
			"one is racing a unit that is walking into a fight it cannot answer.")]
		public readonly int EmptyScanInterval = 25;

		[Desc("Furthest a rearm actor can be, in CHESSBOARD cells, and still be worth breaking off for.",
			"AmmoPool.ChooseResupplier picks the closest host ignoring path and does not check that a",
			"route exists (economy.md: \"a resupplier exists is the engine's whole reachability test\"), so",
			"an unleashed order can march a soldier at a depot across an unfordable river for the rest of",
			"the match. A budget is the same cheap proxy PoiOffensiveBotModule uses for dry vehicles",
			"(OutOfAmmoRearmSeekRadiusCells); beyond it the unit holds and is flagged NeedsResupply so a",
			"Hunt-stance truck can come to it instead.")]
		public readonly int ReturnWhenEmptyLeashCells = 30;

		[GrantedConditionReference]
		[Desc("Condition read to tell whether we are already evacuating (RotateToEdge grants it). Leave",
			"empty to skip the check.")]
		public readonly string EvacuatingCondition = "evacuating";

		public override object Create(ActorInitializer init) { return new AutoSeekSupplies(init.Self, this); }
	}

	public class AutoSeekSupplies : INotifyCreated, INotifyIdle, ITick
	{
		readonly AutoSeekSuppliesInfo info;

		// The actor this trait belongs to. CanServe reads per-actor state cached below, so it can
		// only ever answer for this actor — taking a seeker parameter implied otherwise.
		readonly Actor self;

		Rearmable rearmable;
		IMove move;
		AutoTarget autoTarget;
		int scanTicks;
		int emptyScanTicks;

		// Per-actor constants, cached so the eligibility test is allocation-free in the steady
		// state — SeekSuppliesAndReturn re-asks it every tick for the whole trip.
		ExternalCondition[] externalConditions;

		public AutoSeekSupplies(Actor self, AutoSeekSuppliesInfo info)
		{
			this.info = info;
			this.self = self;

			// Deterministic per-actor phase so a squad that goes idle together does not scan on the
			// same tick. Must NOT come from World.SharedRandom: this trait loads for every profile,
			// so drawing from the synced stream would shift it for control games too (conventions.md).
			scanTicks = info.ScanInterval > 0 ? (int)(self.ActorID % (uint)info.ScanInterval) : 0;
			emptyScanTicks = info.EmptyScanInterval > 0 ? (int)(self.ActorID % (uint)info.EmptyScanInterval) : 0;
		}

		// The interface hands us the actor these run for, which is always the one cached in `self` — the
		// parameters only shadowed the field, so they are discarded and every site reads the field.
		void INotifyCreated.Created(Actor _)
		{
			rearmable = self.TraitOrDefault<Rearmable>();
			move = self.TraitOrDefault<IMove>();
			autoTarget = self.TraitOrDefault<AutoTarget>();
			externalConditions = self.TraitsImplementing<ExternalCondition>().ToArray();
		}

		void INotifyIdle.TickIdle(Actor _)
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

			var provider = FindNearestUsableProvider();
			if (provider == null)
				return;

			// EDGE, TestMode only. This trait moves a combat unit OFF ITS POSITION and was, until now,
			// completely silent — so when an autotest measured a platoon drifting rearward there was no way
			// to tell whether this walked them or a bot module did, and two rounds of analysis attributed
			// the drift by assumption. One line per errand, at the moment the unit decides to leave.
			// Normal play is unaffected: nothing is logged outside a test.
			if (TestMode.IsActive)
				Log.Write("debug",
					$"[seek] leave unit={self.ActorID}@{self.Location} owner={self.Owner.PlayerName} "
					+ $"provider={provider.Info.Name}@{provider.Location} "
					+ $"dist={(provider.CenterPosition - self.CenterPosition).HorizontalLength / 1024}c "
					+ $"leash={info.SupplyHuntLeashCells}c");

			self.QueueActivity(false, new SeekSuppliesAndReturn(self, provider));
			self.ShowTargetLines();
		}

		/// <summary>
		/// The empty-pool half, which cannot be idle-triggered: the whole complaint is a soldier who is
		/// BUSY — walking an attack-move onto the line — with nothing left to fire. Unlike the idle seek
		/// this one interrupts, because the order it interrupts is the problem.
		///
		/// Deliberately narrower than the idle seek in two ways. It requires EVERY pool empty, not merely
		/// low, so a rifleman still holding his RPG round keeps fighting; and it hands off to
		/// AmmoPool.AutoRearm rather than SeekSuppliesAndReturn, so a Logistics Centre counts as a
		/// destination (the proximity errand skips docking-gated hosts, and when a man is dry the dock is
		/// often the only source on the map).
		/// </summary>
		void ITick.Tick(Actor self)
		{
			if (!info.Enabled || !info.ReturnWhenEmpty || move == null || rearmable == null)
				return;

			if (--emptyScanTicks > 0)
				return;

			emptyScanTicks = info.EmptyScanInterval;

			if (!self.IsInWorld || self.IsDead || !AllRearmablePoolsEmpty())
				return;

			// Already on the errand — re-issuing would cancel it and restart the walk from here, forever.
			if (AmmoPool.IsSeekingRearm(self))
				return;

			// Already leaving the map for a refund; that disposition outranks a local errand and cancelling
			// it would strand the unit at the front with nothing banked. RotateToEdge grants the condition
			// and is otherwise only declared as a bare ExternalCondition, so reading the condition is the
			// honest test — inferring it from "all pools empty" would be the cause, not the state
			// (conventions.md, Actor.GetConditionCount).
			if (!string.IsNullOrEmpty(info.EvacuatingCondition) && self.GetConditionCount(info.EvacuatingCondition) > 0)
				return;

			// Same stance contract as the idle seek: Hold means "stay put, a truck will come to me",
			// Ambush means "do not stand up", HoldPosition means "do not roam". Evacuate is not ours
			// either — AmmoPool.AutoRearmIfAllEmpty owns that disposition and rotates the unit out.
			if (!StancesPermit())
				return;

			// ChooseResupplier filters on ownership, RearmActors membership and remaining supply only — it
			// checks neither IsInWorld nor a path (economy.md). The IsInWorld hole is real: a host loaded
			// into a carryall is out of the world with a stale CenterPosition, so it would read as a
			// perfectly good destination at wherever it was picked up.
			var host = AmmoPool.ChooseResupplier(self);
			if (host == null || !host.IsInWorld || !WithinReturnLeash(host))
			{
				// Nothing worth walking to. Raise the flag the Hunt-stance provider scan reads
				// (SupplyProvider.FindNeedsResupplyTarget) so the supply side can come to us instead, and
				// leave the unit's current order alone — an unreachable errand is worse than none.
				foreach (var pool in rearmable.RearmableAmmoPools)
					pool.NeedsResupply = true;

				return;
			}

			if (TestMode.IsActive)
				Log.Write("debug",
					$"[seek] dry unit={self.ActorID}@{self.Location} owner={self.Owner.PlayerName} "
					+ $"host={host.Info.Name}@{host.Location} leash={info.ReturnWhenEmptyLeashCells}c");

			// QueueActivity(false, …) inside — the forward order is cancelled, which is the point.
			AmmoPool.AutoRearm(self);
			self.ShowTargetLines();
		}

		bool AllRearmablePoolsEmpty()
		{
			if (rearmable.RearmableAmmoPools.Length == 0)
				return false;

			foreach (var pool in rearmable.RearmableAmmoPools)
				if (pool.CurrentAmmoCount > 0)
					return false;

			return true;
		}

		bool WithinReturnLeash(Actor host)
		{
			return SupplyHuntMath.WithinCellBudget(
				host.Location.X - self.Location.X,
				host.Location.Y - self.Location.Y,
				info.ReturnWhenEmptyLeashCells);
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
		/// Nearest provider inside the leash whose push would actually reach this unit. Answers only for
		/// this trait's own actor (like <see cref="CanServe"/>), so it takes no seeker argument.
		/// </summary>
		Actor FindNearestUsableProvider()
		{
			var candidates = new List<SupplyHuntMath.Candidate>();
			var actors = new List<Actor>();

			foreach (var a in self.World.ActorsHavingTrait<SupplyProvider>())
			{
				var provider = a.TraitOrDefault<SupplyProvider>();
				if (provider == null || !CanServe(a, provider))
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
		///
		/// An instance method reading cached per-actor traits, so the every-tick call from the
		/// activity allocates nothing — and answering only for its OWN actor, which is why it takes
		/// no seeker argument. The provider trait is passed in rather than looked up: the activity
		/// already holds it, and the scan resolves it once per candidate anyway.
		/// </summary>
		public bool CanServe(Actor providerActor, SupplyProvider provider)
		{
			if (providerActor == null || providerActor.IsDead || !providerActor.IsInWorld || providerActor == self)
				return false;

			if (provider == null)
				return false;

			// No Rearmable, no selection: SupplyProvider.IsValidTarget rejects such a unit outright.
			if (rearmable == null)
				return false;

			// Drained, paused, disabled, mid-restock, or about to reserve its remainder and drive
			// home — CanServeNow is the provider's own Tick-level serving ladder, asked rather than
			// reproduced (it reads private restock state).
			if (provider.CountsAsEmpty || !provider.CanServeNow)
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
			if (!string.IsNullOrEmpty(provider.Info.RearmCondition) && !HasExternalCondition(provider.Info.RearmCondition))
				return false;

			// Must be able to afford at least one batch of something we are short of, or the walk
			// buys us nothing.
			foreach (var p in rearmable.RearmableAmmoPools)
				if (!p.HasFullAmmo && provider.CurrentSupply >= p.Info.SupplyValue)
					return true;

			return false;
		}

		/// <summary>Cached-array lookup — no closure, so the per-tick call stays allocation-free.</summary>
		bool HasExternalCondition(string condition)
		{
			foreach (var e in externalConditions)
				if (e.Info.Condition == condition)
					return true;

			return false;
		}
	}
}
