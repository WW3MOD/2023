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

		[Desc("Break off a LIVE order once the unit has run dry — AmmoPool.OutOfEssentialAmmo, i.e. every",
			"pool marked Essential is empty, or (nothing marked, the shipped default) every pool at all —",
			"and walk to the nearest rearm actor.",
			"The LEASH is tiered by how dry: wholly dry earns ReturnWhenEmptyLeashCells below, merely",
			"essential-dry earns the shorter AmmoPoolInfo.EssentialDryLeashCells, because a unit that can",
			"still fire something should not abandon a live order to cross the map.",
			"The seek above is idle-triggered, and a soldier marching under an attack-move order is never",
			"idle (Actor.IsIdle is CurrentActivity == null) — so a man who empties on the advance keeps",
			"advancing with nothing to shoot. AmmoPool's own dispatcher has the same blind spot: it fires",
			"from INotifyAttack on the shot that empties the pool and from INotifyBecomingIdle, and",
			"AmmoPool is not ITick, so nobody ever asks again. This is the missing periodic ask.",
			"NOTE this is a different pool set from the idle seek above, on purpose. That one asks \"is",
			"there anything a host could top up for me\" and so reads Rearmable.AmmoPools; this one asks",
			"\"can I still fight\" and so reads every pool. Reading the rearmable set here calls the combat",
			"engineer empty when his three C4 charges are spent and his SMG magazine is full, and pulls",
			"him off a capture walk to fetch ammunition he is not short of.",
			"Ships OFF so the trait's behaviour is unchanged until a mod opts in. Turning it on in YAML",
			"affects human- and bot-owned units alike, both bot profiles included — there is no",
			"owner-side split in this trait.")]
		public readonly bool ReturnWhenEmpty = false;

		[Desc("Ticks between empty-state re-checks for ReturnWhenEmpty. Shorter than ScanInterval: this",
			"one is racing a unit that is walking into a fight it cannot answer.")]
		public readonly int EmptyScanInterval = 25;

		[Desc("Furthest a rearm actor can be, in CHESSBOARD cells, and still be worth breaking off for.",
			"AmmoPool.ChooseResupplier picks the closest host ignoring path and does not check that a",
			"route exists (economy.md: \"a resupplier exists is the engine's whole reachability test\"), so",
			"an unleashed order can march a soldier at a depot across an unfordable river for the rest of",
			"the match. This is a cheap travel-cost proxy, not a reachability test.",
			"0 or less DISABLES the break-off entirely (SupplyHuntMath.WithinCellBudget admits nothing at",
			"a non-positive budget). Do NOT read that off PoiOffensiveBotModule.OutOfAmmoRearmSeekRadiusCells,",
			"whose 0 means UNLIMITED — the two fields solve the same problem with opposite zero-semantics.",
			"Beyond the budget this tick declines to interrupt and flags NeedsResupply so a Hunt-stance",
			"truck can come to us. That flag holds only until the unit next goes idle: AmmoPool's own",
			"INotifyBecomingIdle path then clears it and dispatches unleashed (AmmoPool.cs:184-188). That",
			"is pre-existing behaviour and is deliberately not changed here — the budget bounds what THIS",
			"trait will interrupt a live order for, not what the unit does once it has run out of orders.")]
		public readonly int ReturnWhenEmptyLeashCells = 30;

		[Desc("Abandon a break-off errand that has made no progress for this many ticks: no ammo gained",
			"AND no cell moved. Needed because a Resupply toward an unreachable host never terminates —",
			"Mobile.MoveResult is declared and read but never ASSIGNED anywhere in the engine",
			"(Mobile.cs:265), so it is permanently InProgress and both of MoveCooldownHelper's exits",
			"(CompleteDestinationReached / CompleteCanceled) are unreachable; it just repaths every 20-31",
			"ticks forever. Without a guard the unit stands still, combat-inert, IsSeekingRearm stays true",
			"so this tick never retries, and every bot module withholds it permanently — a unit deleted",
			"from the game in all but name. Mirrors SeekSuppliesAndReturn.MaxStalledTicks. 0 disables.")]
		public readonly int ReturnErrandStallTicks = 300;

		[Desc("After abandoning a stalled errand, wait this many ticks before this trait may dispatch",
			"another. Without it the next scan re-issues the same doomed walk to the same host.")]
		public readonly int ReturnErrandRetryTicks = 500;

		[ConsumedConditionReference]
		[Desc("Condition read to tell whether we are already evacuating (RotateToEdge grants it). Leave",
			"empty to skip the check. Read, never granted, so this is a consumed reference — tagging it",
			"granted misinforms the condition lint that runs in `make test`.")]
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

		// Stall tracking for the break-off errand. Progress is "ammo went up OR we changed cell"; the
		// counter is in SCANS, not ticks, so it is compared against a tick budget scaled by the scan
		// interval rather than incremented every tick for a check that only runs periodically.
		int stalledTicks;
		int retryCooldownTicks;
		int lastAmmoCount;
		CPos lastErrandCell;
		bool onErrand;

		// Per-actor constants, cached so the eligibility test is allocation-free in the steady
		// state — SeekSuppliesAndReturn re-asks it every tick for the whole trip.
		ExternalCondition[] externalConditions;

		// Every pool, not Rearmable.RearmableAmmoPools — see ReturnWhenEmpty's Desc and
		// AmmoPool.AllPoolsEmpty for why the two sets are different questions.
		AmmoPool[] allPools;

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
			allPools = self.TraitsImplementing<AmmoPool>().ToArray();
		}

		void INotifyIdle.TickIdle(Actor _)
		{
			// No Rearmable means no provider can ever select us (SupplyProvider.IsValidTarget requires
			// it), so the whole errand is futile: such a unit would walk to a truck, wait out the stall
			// guard, walk home, and repeat on every idle cycle. The guard covers the unarmed classes
			// that inherit this trait from ^Soldier without any ammunition of their own (medic,
			// technician). It previously named the combat engineer as the example, which is wrong —
			// ^E6 does declare Rearmable (infantry.yaml); every armed soldier class in ww3mod does.
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
					$"[seek] leave tick={self.World.WorldTick} unit={self.ActorID}@{self.Location} owner={self.Owner.PlayerName} "
					+ $"provider={provider.Info.Name}@{provider.Location} "
					+ $"dist={(provider.CenterPosition - self.CenterPosition).HorizontalLength / 1024}c "
					+ $"leash={info.SupplyHuntLeashCells}c");

			self.QueueActivity(false, new SeekSuppliesAndReturn(self, provider));
			self.ShowTargetLines();
		}

		/// <summary>
		/// <para>The empty-pool half, which cannot be idle-triggered: the whole complaint is a soldier who is
		/// BUSY — walking an attack-move onto the line — with nothing left to fire. Unlike the idle seek
		/// this one interrupts, because the order it interrupts is the problem.</para>
		///
		/// <para>Deliberately narrower than the idle seek in two ways. It requires EVERY pool empty
		/// (AmmoPool.AllPoolsEmpty — every pool on the actor, NOT the Rearmable-filtered subset the idle
		/// seek reads), so a rifleman still holding his RPG round keeps fighting; and it hands off to
		/// AmmoPool.AutoRearm rather than SeekSuppliesAndReturn, so a Logistics Centre counts as a
		/// destination (the proximity errand skips docking-gated hosts, and when a man is dry the dock is
		/// often the only source on the map).</para>
		///
		/// <para>INVARIANT worth preserving: "breaks off" IMPLIES "is deprioritised in selection". Every pool
		/// empty revokes every AmmoCondition, and Armament.Created grants weapon-&lt;name&gt;
		/// unconditionally (Armament.cs:260) with every armed soldier class carrying an armament named
		/// primary — so the ^AmmoDecoration expression that drives the empty-ammo pip and the
		/// SelectionPriorityModifier is necessarily true whenever this fires. A man who walks away is
		/// therefore always visibly dry and always deprioritised; the reverse is deliberately NOT
		/// guaranteed (the engineer with a dry SMG and C4 left is deprioritised but keeps his order),
		/// and that is the harmless direction. If you widen this trigger, re-check the implication —
		/// getting it backwards means soldiers leaving the line looking perfectly healthy.</para>
		/// </summary>
		void ITick.Tick(Actor _)
		{
			if (!info.Enabled || !info.ReturnWhenEmpty || move == null || rearmable == null)
				return;

			if (--emptyScanTicks > 0)
				return;

			emptyScanTicks = info.EmptyScanInterval;

			if (retryCooldownTicks > 0)
				retryCooldownTicks -= info.EmptyScanInterval;

			if (!self.IsInWorld || self.IsDead)
				return;

			// The errand is watched before anything else, because the case that matters most is one where
			// every other gate below reads "nothing to do": a unit standing still on an errand it can
			// never finish still has empty pools, still has a host, and still passes the stances.
			if (onErrand)
			{
				TickErrand();
				return;
			}

			if (!AmmoPool.OutOfEssentialAmmo(allPools))
				return;

			// A resupply activity is already running or QUEUED — do not issue a second one. Note this
			// asks about the whole queue, not the head: see AmmoPool.IsSeekingRearm.
			// If it is not one WE dispatched (a player order, or AmmoPool's own idle dispatch) we also
			// do not stall-guard it. Cancelling an order the player gave, however stuck it looks from
			// here, is not this trait's call to make.
			if (AmmoPool.IsSeekingRearm(self))
				return;

			if (retryCooldownTicks > 0)
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
			// either — AmmoPool.AutoRearmIfDry owns that disposition and rotates the unit out.
			if (!StancesPermit())
				return;

			// The nearest AFFORDABLE host, matching AmmoPool.AutoRearmIfDry's Auto arm. Neither chooser
			// checks IsInWorld or a path (economy.md), which is why the IsInWorld test stays below: a host
			// loaded into a carryall is out of the world with a stale CenterPosition and would otherwise
			// read as a perfectly good destination at wherever it was picked up.
			//
			// THIS USED TO BE ChooseResupplier, WHICH FILTERS ON CurrentSupply > 0, and a comment here
			// forbade changing it. That comment's premise was that a rearm at a DOCKING host is free —
			// "Rearmable.RearmTick hands out ammunition with no supply consulted" — so gating the trip on
			// affordability would withhold a trip that would have worked. It is no longer true, and was
			// already being falsified as it was written: the comment landed in 291ba846 and f8b424f6
			// metered the dock path 72 minutes later the same afternoon. Rearmable.RearmTick now skips
			// any pool the provider cannot pay for (Rearmable.cs:106), exactly as TryServeBatch does on
			// the push side, so BOTH host kinds charge and affordability is the right question for both.
			//
			// What the old test cost, measured in test-dry-seeks-affordable-cache: a mortar whose batch
			// costs 40, standing 7 cells from a cache holding 39 and 23 cells from one holding 45, was
			// dispatched to the cache that could never pay him, parked inside its aura, and was still dry
			// 1750 polls later. A cache with 1..39 in it is a legal destination under "> 0" and cannot
			// serve anybody; under ">= one batch" it is correctly passed over.
			var host = AmmoPool.ChooseAffordableResupplier(self, allPools);
			if (host == null || !host.IsInWorld || !WithinBreakOffLeash(host))
			{
				// Nothing worth walking to. Raise the flag the Hunt-stance provider scan reads
				// (SupplyProvider.FindNeedsResupplyTarget) so the supply side can come to us instead, and
				// leave the unit's current order alone — an unreachable errand is worse than none.
				FlagNeedsResupply();
				return;
			}

			if (TestMode.IsActive)
				Log.Write("debug",
					$"[seek] dry tick={self.World.WorldTick} unit={self.ActorID}@{self.Location} owner={self.Owner.PlayerName} "
					+ $"host={host.Info.Name}@{host.Location} leash={info.ReturnWhenEmptyLeashCells}c");

			// QueueActivity(false, …) inside — the forward order is cancelled, which is the point.
			//
			// `host` is PASSED, never left to be re-picked. AutoRearm's null-host path falls back to
			// ChooseResupplier (AmmoPool.cs:890), so omitting it re-introduces the nearest-merely-stocked
			// pick one call deeper and throws away the choice made immediately above — the precise trap
			// that parameter's own doc comment was written to prevent.
			AmmoPool.AutoRearm(self, true, host);
			self.ShowTargetLines();
			BeginWatching();
		}

		void BeginWatching()
		{
			onErrand = true;
			stalledTicks = 0;
			lastAmmoCount = TotalAmmo();
			lastErrandCell = self.Location;
		}

		/// <summary>
		/// Watch a running errand and abandon it if it is going nowhere. The failure this exists for is
		/// not slowness — it is a Resupply toward a host with no route, which repaths on a cooldown
		/// forever and can never self-terminate (see ReturnErrandStallTicks). Progress is deliberately
		/// generous: ammo arriving OR the unit changing cell either one resets the clock, so a long walk
		/// round a lake is never mistaken for a stall.
		/// </summary>
		void TickErrand()
		{
			if (!AmmoPool.IsSeekingRearm(self))
			{
				// Finished, cancelled, or replaced by a player order. Either way it is no longer ours.
				onErrand = false;
				return;
			}

			var ammo = TotalAmmo();
			var cell = self.Location;
			if (ammo > lastAmmoCount || cell != lastErrandCell)
			{
				stalledTicks = 0;
				lastAmmoCount = ammo;
				lastErrandCell = cell;
				return;
			}

			if (info.ReturnErrandStallTicks <= 0)
				return;

			stalledTicks += info.EmptyScanInterval;
			if (stalledTicks < info.ReturnErrandStallTicks)
				return;

			// Give up and rejoin the pool. Cancelling is what releases the unit: StarvingRecruitGate
			// withholds anything IsSeekingRearm reports, so a unit left wedged here is withheld forever —
			// worse than one fighting with an empty gun, which can at least still take ground.
			if (TestMode.IsActive)
				Log.Write("debug",
					$"[seek] stalled tick={self.World.WorldTick} unit={self.ActorID}@{self.Location} owner={self.Owner.PlayerName} "
					+ $"stalled={stalledTicks}t retry={info.ReturnErrandRetryTicks}t");

			self.CancelActivity();
			FlagNeedsResupply();
			onErrand = false;
			retryCooldownTicks = info.ReturnErrandRetryTicks;
		}

		int TotalAmmo()
		{
			var total = 0;
			foreach (var pool in allPools)
				total += pool.CurrentAmmoCount;

			return total;
		}

		void FlagNeedsResupply()
		{
			foreach (var pool in rearmable.RearmableAmmoPools)
				pool.NeedsResupply = true;
		}

		/// <summary>
		/// <para>The break-off budget, tiered by HOW dry the unit is. Wholly dry — it cannot shoot
		/// anything — earns this trait's own <see cref="AutoSeekSuppliesInfo.ReturnWhenEmptyLeashCells"/>.
		/// Merely ESSENTIAL-dry, still able to fire something, earns the shorter
		/// <see cref="AmmoPoolInfo.EssentialDryLeashCells"/>, so a unit that can still contribute does not
		/// abandon a live order to cross the map.</para>
		///
		/// <para>The partial budget is read off the POOLS rather than added to this trait's Info, and that
		/// asymmetry is deliberate: AmmoPool's own dispatcher needs the identical number on actors that do
		/// not carry this trait at all (every vehicle), so the field has to live where both can reach it.
		/// Reading down into AmmoPoolInfo is safe; the reverse direction is the mistake
		/// DryRearmLeashCells' own comment records.</para>
		/// </summary>
		bool WithinBreakOffLeash(Actor host)
		{
			var budget = AmmoPool.AllPoolsEmpty(allPools)
				? info.ReturnWhenEmptyLeashCells
				: AmmoPool.ResolveSeekLeash(allPools);

			return SupplyHuntMath.WithinCellBudget(
				host.Location.X - self.Location.X,
				host.Location.Y - self.Location.Y,
				budget);
		}

		bool StancesPermit()
		{
			// A unit without AutoTarget has no stances to consult; treat it as fully permissive,
			// which is the same fallback AmmoPool.AutoRearmIfDry applies.
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
		/// <para>The ONE eligibility test — used both to pick a provider and, by SeekSuppliesAndReturn,
		/// to decide every tick whether the one it is walking to is still worth reaching. Sharing it
		/// is the point: a provider we would not walk to must also be one we stop walking to, and two
		/// separate copies of this rule drifted apart the moment one of them gained a clause.</para>
		///
		/// <para>Mirrors the gates SupplyProvider applies from the other side, so a unit never walks to a
		/// source that would refuse it on arrival.</para>
		///
		/// <para>An instance method reading cached per-actor traits, so the every-tick call from the
		/// activity allocates nothing — and answering only for its OWN actor, which is why it takes
		/// no seeker argument. The provider trait is passed in rather than looked up: the activity
		/// already holds it, and the scan resolves it once per candidate anyway.</para>
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
			// buys us nothing. Shared with every other site that asks it rather than restated here —
			// this was the FOURTH copy, and the house rule (SupplyProvider.AcceptClient) is that a
			// subtle predicate never gets duplicated, because prose is not the countermeasure.
			//
			// THE POOL SET IS THE CALLER'S CHOICE, and it is the axis on which these copies can still
			// disagree. This site passes the REARMABLE subset, which is what the push side's canonical
			// rule uses (SupplyProvider.AcceptClient reads rearmable.RearmableAmmoPools). The dispatch
			// sites pass every pool instead — AmmoPool's Auto arm and evacuate detour, and
			// SeekSupplyProvider. Across the shipped ruleset all 46 actors carrying both traits have
			// identical sets, so the two readings coincide today and nothing observable turns on it.
			// If an actor is ever given an AmmoPool its Rearmable does not name, they part company and
			// THIS is where to look: the rearmable subset is the correct set for "can a host serve me",
			// because a pool no host is allowed to fill cannot make a trip worth taking.
			return AmmoPool.HostCanAffordSomethingWeNeed(providerActor, rearmable.RearmableAmmoPools);
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
