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
using OpenRA.Graphics;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Orders;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Mobile supply transport (TRUK) behaviors: drop a SUPPLYCACHE on deploy,",
		"deliver supply to a friendly LC, drive in to restock when low.",
		"Requires SupplyProvider as the underlying storage.")]
	public class DropsSupplyCacheInfo : TraitInfo, Requires<SupplyProviderInfo>
	{
		[ActorReference]
		[Desc("Actor to create when supply is unloaded onto the ground.")]
		public readonly string SupplyCacheActor = "supplycache";

		[Desc("Relationships of allies whose Logistics Centers we can deliver / restock at.")]
		public readonly PlayerRelationship ValidRelationships = PlayerRelationship.Ally;

		[CursorReference]
		[Desc("Cursor for right-click on a friendly Logistics Center (default restock flow).")]
		public readonly string RestockCursor = "enter";

		[CursorReference]
		[Desc("Cursor for right-click on a friendly ground supply cache the transport can collect.")]
		public readonly string PickupCacheCursor = "enter";

		[CursorReference]
		[Desc("Cursor for the deploy command when supply can be dropped as a SUPPLYCACHE.")]
		public readonly string DropCacheCursor = "deploy";

		[CursorReference]
		[Desc("Cursor for the deploy command when supply cannot be dropped (no supply, or cell blocked).")]
		public readonly string DropCacheBlockedCursor = "deploy-blocked";

		[VoiceReference]
		[Desc("Voice played when ordered to drop a SUPPLYCACHE.")]
		public readonly string DropCacheVoice = "Action";

		[Desc("DropSupplyCacheAt and PickupSupply: how close (cells) the transport must get to the ordered",
			"cell, both as the move's stop tolerance AND as the arrival check that gates the unload/load.",
			"ONE number for both on purpose — the transfer must not run at a cell the move never actually",
			"reached, and two constants would drift. A bot sizing its demand search around the ordered cell",
			"should subtract this, since the crate can land this far off it",
			"(SupplyFollowerBotModule.DropDemandMarginCells).")]
		public readonly int DropAtToleranceCells = 2;

		[Desc("Image whose sprite marks the waypoint a QUEUED deploy will unload on. Defaults to the",
			"cache actor's own artwork, so the marker is a picture of the thing that will be dropped",
			"rather than a glyph the player has to learn. Leave empty to draw no marker.")]
		public readonly string DropMarkerImage = "supplycache";

		[SequenceReference(nameof(DropMarkerImage), allowNullImage: true)]
		[Desc("Sequence within DropMarkerImage to draw.")]
		public readonly string DropMarkerSequence = "idle";

		[Desc("Alpha of that marker. Held below 1 so a queued drop reads as a PLAN — a crate ghosted",
			"over the waypoint — and can never be mistaken for a crate already sitting on the ground.")]
		public readonly float DropMarkerAlpha = 0.6f;

		[Desc("Ticks between empty-state re-checks for a transport that is UNDER ORDERS. The existing",
			"empty-truck return is INotifyBecomingIdle plus an idle ITick re-check, and a truck driving",
			"somewhere is never idle — so a truck that ran dry mid-move just kept driving. This is the",
			"missing periodic ask: the same blind spot, and the same shape of fix, as",
			"AutoSeekSuppliesInfo.EmptyScanInterval on the infantry side.",
			"0 disables the break-off entirely and restores the idle-only behaviour.")]
		public readonly int DryMoveScanInterval = 25;

		[ConsumedConditionReference]
		[Desc("Condition read to tell whether this transport is already evacuating (RotateToEdge grants",
			"it). Cancelling THAT move would strand the truck at the front with nothing banked, so it is",
			"exempt from the dry break-off. Leave empty to skip the check. Read, never granted, so this",
			"is a consumed reference — tagging it granted misinforms the condition lint in `make test`.")]
		public readonly string EvacuatingCondition = "evacuating";

		public override object Create(ActorInitializer init) { return new DropsSupplyCache(init, this); }
	}

	public class DropsSupplyCache : INotifyCreated, INotifyBecomingIdle, ITick, IResolveOrder,
		IIssueOrder, IIssueDeployOrder, IOrderVoice
	{
		public readonly DropsSupplyCacheInfo Info;

		/// <summary>Sprite drawn on the waypoint a queued deploy will unload on. Null if the mod cleared
		/// DropMarkerImage or named a sequence this tileset has no artwork for.</summary>
		public readonly Sprite DropMarker;

		readonly Actor self;
		SupplyProvider supply;

		// Guards against OnBecomingIdle and ITick both firing EvacuateOrRestock on the same
		// idle-transition frame (which would double-queue RotateToEdge).
		int lastEvacuateTick = -1;

		int dryMoveTicks;

		public DropsSupplyCache(ActorInitializer init, DropsSupplyCacheInfo info)
		{
			Info = info;
			self = init.Self;

			var sequences = self.World.Map.Sequences;
			if (!string.IsNullOrEmpty(info.DropMarkerImage) && sequences.HasSequence(info.DropMarkerImage, info.DropMarkerSequence))
				DropMarker = sequences.GetSequence(info.DropMarkerImage, info.DropMarkerSequence).GetSprite(0);

			// Deterministic per-actor phase so trucks called in together do not all scan on the same
			// tick. Must NOT come from World.SharedRandom: this trait loads for every profile, so
			// drawing from the synced stream would shift it for control games too (conventions.md).
			dryMoveTicks = info.DryMoveScanInterval > 0 ? (int)(self.ActorID % (uint)info.DryMoveScanInterval) : 0;
		}

		void INotifyCreated.Created(Actor self)
		{
			supply = self.Trait<SupplyProvider>();
		}

		bool CanDropCache()
		{
			if (supply == null || supply.CurrentSupply <= 0)
				return false;

			// Cell must be clear or already hold a SUPPLYCACHE to merge into.
			return self.World.BlockingActorsAt(self.Location)
				.All(a => a == self || (!a.IsDead && a.Info.Name == Info.SupplyCacheActor));
		}

		/// <summary>Drop all current supply as a SUPPLYCACHE at the transport's cell.
		///
		/// <para>THE PLACEMENT IS LOGGED UNCONDITIONALLY, and it is the one fact the whole forward-delivery
		/// subsystem had no record of. `[supply] drop` on the bot side says an errand was ISSUED, not that a
		/// crate reached the ground — the two are separated by a drive, an arrival test and an occupancy test,
		/// each of which can refuse silently. So a user reporting "I have never once seen a crate" could not be
		/// distinguished from a user who had simply never looked in the right place. One line per drop, and a
		/// drop empties the truck, so the volume is bounded by the number of loads a match delivers.</para></summary>
		public void DropSupplyCacheHere()
		{
			if (supply == null || supply.CurrentSupply <= 0)
				return;

			var amount = supply.CurrentSupply;

			// Merge into an existing cache on this cell, if any.
			var existing = self.World.ActorMap.GetActorsAt(self.Location)
				.FirstOrDefault(a => !a.IsDead && a.Info.Name == Info.SupplyCacheActor);

			if (existing != null)
			{
				var existingProvider = existing.TraitOrDefault<SupplyProvider>();
				if (existingProvider != null)
				{
					existingProvider.AddSupply(amount);
					supply.SetSupply(0);
					Log.Write("debug",
						$"[supply] crate-merged truck={self.ActorID}@{self.Location} owner={self.Owner.PlayerName} "
						+ $"amount={amount} into={existing.ActorID}");
					return;
				}
			}

			// Otherwise spawn a fresh cache initialized with this amount.
			var cacheInfo = self.World.Map.Rules.Actors[Info.SupplyCacheActor]
				.TraitInfoOrDefault<SupplyProviderInfo>();
			if (cacheInfo == null)
			{
				Log.Write("debug",
					$"[supply] crate-refused truck={self.ActorID}@{self.Location} owner={self.Owner.PlayerName} "
					+ $"reason=no-{Info.SupplyCacheActor}-supplyprovider amount={amount}");
				return;
			}

			supply.SetSupply(0);

			// EDGE — unconditional. `[supply] drop` says the errand was issued; THIS says a crate exists.
			Log.Write("debug",
				$"[supply] crate-placed truck={self.ActorID}@{self.Location} owner={self.Owner.PlayerName} "
				+ $"type={Info.SupplyCacheActor} amount={amount}");

			self.World.AddFrameEndTask(w =>
			{
				w.CreateActor(Info.SupplyCacheActor, new TypeDictionary
				{
					new LocationInit(self.Location),
					new OwnerInit(self.Owner),
					new SupplyInit(cacheInfo, amount),
				});
			});
		}

		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			// QUEUE THE DROP; DO NOT PERFORM IT HERE. This used to call DropSupplyCacheHere() inline,
			// which threw `order.Queued` away at the last layer that could still honour it — the
			// targeter, the order constructor and the wire all carry the Shift modifier faithfully,
			// and then the unload happened at resolution time regardless. A shift-queued deploy is the
			// ordinary way to say "drive there, THEN unload", and it dumped the whole load under the
			// truck's wheels at the beachhead instead, which is the one place it is worth nothing.
			// PITFALL: an order handler that does its work inline is invisible to the queued flag no
			// matter how carefully every layer above it passed the flag down.
			if (order.OrderString == "DropSupplyCache")
			{
				self.QueueActivity(order.Queued, new UnloadSupplyCache(self, this, PredictedDropCell(order.Queued)));
				self.ShowTargetLines();
				return;
			}

			// DROP-AND-LEAVE (bot-issued; no targeter, so it never appears on a human's cursor). The WHOLE
			// errand — drive out, unload, then dispose of the emptied chassis — is composed here as ONE
			// activity chain rather than as a sequence of orders, and that is a correctness requirement, not
			// tidiness. Dropping calls SetSupply(0), which makes CountsAsEmpty true, and the ITick below
			// fires EvacuateOrRestock on ANY tick the truck is idle-and-empty: under TRUK's AI default
			// (InitialResupplyBehaviorAI: Evacuate, vehicles.yaml) that means RotateToEdge — driven to the
			// map edge and sold. A caller issuing "drop" and "then restock" as two orders loses that race,
			// because ModularBot meters its queue (MinOrderQuotientPerTick) and can put the two on different
			// ticks. Chaining them means the truck is never idle between the drop and its disposition.
			if (order.OrderString == "DropSupplyCacheAt")
			{
				if (self.TraitOrDefault<IMove>() == null)
					return;

				// ONE named activity for the whole errand. Being a recognisable TYPE is what keeps the
				// serving halt off it: a truck otherwise stops for anyone in its aura who needs a batch,
				// which would leave it standing next to the platoon it was sent to unload NEAR, with the
				// crate still aboard and the danger it was told to leave still around it.
				var dropCell = self.World.Map.CellContaining(order.Target.CenterPosition);
				self.QueueActivity(order.Queued, new PlaceSupplyCache(self, this, dropCell, Info.DropAtToleranceCells));

				// POST-DROP DISPOSITION, decided here so it is a stated design rather than an emergent
				// consequence of the idle path above. If we hold a docking-aware host (a Logistics Centre) the
				// truck runs a real supply shuttle: drive back, refill, and its owner re-adopts it once it is
				// no longer low on supply. Otherwise NOTHING is appended and the existing evacuate path
				// retires the chassis for its sell value — which is not a regression but the status quo made
				// deliberate, since an emptied truck is dropped by SupplyFollowerBotModule and retired that
				// way today regardless of where its supply went. LOGISTICSCENTER is Prerequisites: ~disabled
				// and exists only as a neutral capturable, so the retire arm is the ORDINARY case and the
				// shuttle arm is the reward for having taken one.
				var restockHost = NearestRestockHost();
				if (restockHost != null)
					QueueDriveAndRestock(restockHost, true);

				self.ShowTargetLines();
				return;
			}

			if (order.OrderString == "PickupSupply")
			{
				if (order.Target.Type != TargetType.Actor)
					return;

				var cache = order.Target.Actor;
				if (cache == null || cache.IsDead || !cache.IsInWorld)
					return;

				QueueCollectFromCache(cache, order.Queued);
				self.ShowTargetLines();
				return;
			}

			if (order.OrderString == "Restock")
			{
				if (order.Target.Type != TargetType.Actor)
					return;

				var host = order.Target.Actor;
				if (host == null || host.IsDead || !host.IsInWorld)
					return;

				var hostProvider = host.TraitOrDefault<SupplyProvider>();
				if (hostProvider == null)
					return;

				QueueDriveAndRestock(host);
				self.ShowTargetLines();
				return;
			}

			if (order.OrderString == "DeliverSupply")
			{
				if (order.Target.Type != TargetType.Actor)
					return;

				var host = order.Target.Actor;
				if (host == null || host.IsDead || !host.IsInWorld)
					return;

				if (host.TraitOrDefault<AbsorbsSupplyCache>() == null)
					return;

				var move = self.TraitOrDefault<IMove>();
				if (move == null)
					return;

				// Drive next to the LC and drop the supply on our cell. The LC's
				// AbsorbsSupplyCache pulls the cache in on its next tick.
				var targetCell = self.World.Map.CellContaining(host.CenterPosition);
				self.QueueActivity(order.Queued, move.MoveTo(targetCell, 2));
				self.QueueActivity(true, new CallFunc(() => DropSupplyCacheHere()));
				self.ShowTargetLines();
			}
		}

		/// <summary>Where a deploy queued RIGHT NOW is going to unload — i.e. which waypoint to draw the
		/// marker on. The drop itself happens wherever the truck is standing when the activity comes up,
		/// so this is a prediction; it is read off the last waypoint of the queue as it exists at ISSUE
		/// time, which is exactly the set of orders that will run before ours.
		///
		/// <para>Captured now rather than recomputed at render time ON PURPOSE. Anything the player
		/// appends AFTER the deploy runs after it and must not drag the marker along — recomputing would
		/// walk the icon onto a waypoint the truck will only reach once the crate is already on the
		/// ground. The walk mirrors DrawLineToTarget's own, so the marker lands on the last waypoint the
		/// player can actually see rather than on some cell only the activity graph knows about.</para></summary>
		CPos PredictedDropCell(bool queued)
		{
			// An unqueued deploy cancels whatever is running, so there are no preceding waypoints left.
			if (!queued)
				return self.Location;

			var cell = self.Location;
			for (var a = self.CurrentActivity; a != null; a = a.NextActivity)
				if (!a.IsCanceling)
					foreach (var n in a.TargetLineNodes(self))
						if (n.Tile == null && n.Target.Type != TargetType.Invalid)
							cell = self.World.Map.CellContaining(n.Target.CenterPosition);

			return cell;
		}

		/// <summary>Unload the whole load as a ground cache at the errand's ordered cell, if we actually
		/// got there and the cell is free. Called by <see cref="PlaceSupplyCache"/> once its drive has
		/// finished; lives here rather than in the activity because the drop itself, the merge rule and
		/// the occupancy test are this trait's business.</summary>
		public void TryPlaceCacheAt(CPos dropCell, int toleranceCells)
		{
			// ARRIVAL CHECK — the load-bearing guard, not a formality. A Move to a TERRAIN-impassable
			// cell does not fail: PathFinder bails to NoPath, and Move.Tick treats an empty path as
			// arrival, completing in ~2 ticks at the cell the truck was already on. Without this test
			// the unload would then run THERE — typically the beachhead — dumping the whole load at an
			// arbitrary place while the issuer's redundancy accounting, measured around the ORDERED
			// cell, never sees it. Refusing instead keeps the supply in the truck, which is always
			// recoverable. (The issuer should also refuse to adopt an impassable cell; this is the
			// second line, and it is the one that holds when the cell became unreachable after issue.)
			// BOTH REFUSALS BELOW ARE LOGGED, unconditionally and one line each. They are the two ways
			// an errand that was issued, driven and completed still puts no crate on the ground — and
			// until now both were silent, so from outside they were indistinguishable from a drop that
			// was never decided on at all. Each is bounded by one line per errand.
			var delta = self.Location - dropCell;
			if (!SupplyDropMath.ArrivedAtDropCell(delta.X, delta.Y, toleranceCells))
			{
				Log.Write("debug",
					$"[supply] crate-refused truck={self.ActorID}@{self.Location} owner={self.Owner.PlayerName} "
					+ $"reason=never-arrived ordered={dropCell} tolerance={toleranceCells}c "
					+ $"amount={supply?.CurrentSupply ?? 0}");
				return;
			}

			// Occupancy is only knowable on arrival. A blocked cell means no drop this errand — the
			// truck keeps its load and its owner re-decides, which self-corrects as the blocker moves.
			if (CanDropCache())
			{
				DropSupplyCacheHere();
				return;
			}

			Log.Write("debug",
				$"[supply] crate-refused truck={self.ActorID}@{self.Location} owner={self.Owner.PlayerName} "
				+ $"reason=cell-blocked ordered={dropCell} amount={supply?.CurrentSupply ?? 0}");
		}

		/// <summary>Send the transport to collect a ground cache. The errand itself lives in
		/// <see cref="CollectSupplyCache"/> — a named activity rather than a move with a transfer queued
		/// behind it, so that "this truck is going to fetch supply" is a fact readable off the activity
		/// queue. That matters here specifically: an EMPTY truck sent to collect a crate is the natural
		/// use of this order, and the dry break-off must be able to tell it apart from an empty truck
		/// merely being repositioned.</summary>
		void QueueCollectFromCache(Actor cache, bool queued)
		{
			if (supply == null || self.TraitOrDefault<IMove>() == null)
				return;

			self.QueueActivity(queued, new CollectSupplyCache(self, cache, Info.DropAtToleranceCells));
		}

		/// <summary>Send the transport to a docking-aware host to refill.
		///
		/// <para>ONE named activity, the same <see cref="RestockSupply"/> SupplyProvider's own low-supply
		/// drive uses, rather than the move/wait/CallFunc chain this used to build itself. Sharing the
		/// TYPE is the point, not sharing the code: it is what makes "this truck is refilling" a fact
		/// readable off the activity queue from anywhere. A caller asking whether a truck's current move
		/// is invalidated by the truck being empty would otherwise have to answer "no" for one of these
		/// two restock drives and "yes" for the other, purely because they were built in different
		/// files.</para></summary>
		void QueueDriveAndRestock(Actor host, bool queued = false)
		{
			if (supply == null || self.TraitOrDefault<IMove>() == null)
				return;

			self.QueueActivity(queued, new RestockSupply(self, host, supply.Info.RestockWaitTicks));
		}

		void INotifyBecomingIdle.OnBecomingIdle(Actor self)
		{
			// Empty (or holding an unusable residue) truck with no orders: try to drive
			// back to the nearest friendly LC; if none can host us, evacuate via RotateToEdge.
			if (supply == null || !supply.CountsAsEmpty)
				return;

			EvacuateOrRestock(self);
		}

		void ITick.Tick(Actor self)
		{
			if (supply == null || !supply.CountsAsEmpty || self.IsDead || !self.IsInWorld)
				return;

			// A residue only becomes unusable after the truck has already gone idle at the
			// front, so OnBecomingIdle (which fires on the idle transition) has come and gone.
			// Re-check here: if we're idle and now count as empty, run the same evac/restock
			// flow. RotateToEdge / the restock drive make us non-idle, so this self-limits.
			if (self.IsIdle)
			{
				EvacuateOrRestock(self);
				return;
			}

			TickDryMove(self);
		}

		/// <summary>
		/// A transport that runs dry while UNDER ORDERS breaks off, so the automatic return or
		/// evacuation can happen.
		///
		/// <para>WHY IT HAS TO BE PERIODIC. The empty-truck return is INotifyBecomingIdle plus the idle
		/// re-check above, and a truck driving somewhere is never idle (Actor.IsIdle is
		/// CurrentActivity == null) — so a truck that emptied mid-move simply kept driving, and only
		/// remembered it was empty once it arrived. Exactly the blind spot
		/// AutoSeekSupplies.ReturnWhenEmpty was written to close on the infantry side, and this is the
		/// same shape of fix.</para>
		///
		/// <para>THE RULE, stated once because the exceptions are a consequence of it rather than a list:
		/// <b>cancel a move that is invalidated by being empty; never cancel a move that exists to stop
		/// being empty.</b> The three exemptions below are the only ways a move can be of the second kind
		/// or can already BE the right disposition:</para>
		///
		/// <list type="number">
		/// <item>ON A COMMITTED SUPPLY ERRAND — driving to a host to refill, to a cell to unload, or to a
		/// ground crate to collect it. All three are named activity types for exactly this reason; while
		/// the restock drive was a bare MoveTo the distinction had no way to be made. The crate case is
		/// not hypothetical: sending an EMPTY truck to fetch a crate is the natural use of that order,
		/// and cancelling it would make the order useless precisely when it is most wanted.</item>
		/// <item>ALREADY EVACUATING — RotateToEdge is the disposition this break-off exists to reach, so
		/// cancelling it would strand the truck at the front with nothing banked. Read as a condition
		/// rather than inferred from "is empty", which would be the cause and not the state.</item>
		/// <item>ResupplyBehavior.Hold — "stay put, do not dispose of me". EvacuateOrRestock returns
		/// immediately on Hold, so a cancel here would destroy the standing order and put NOTHING in its
		/// place. That is pure loss, and Hold is the axis that already means this.</item>
		/// </list>
		///
		/// <para>Cancelling is ALL this does: the truck falls idle and the shipped INotifyBecomingIdle
		/// path decides the disposition, so there is no second definition of what an empty truck should
		/// do. It cannot livelock either — every disposition that path can queue is either exempt
		/// (restock, evacuate) or leaves the truck idle (Hold), and re-cancelling an already-cancelling
		/// activity is a no-op.</para>
		/// </summary>
		void TickDryMove(Actor self)
		{
			if (Info.DryMoveScanInterval <= 0)
				return;

			if (--dryMoveTicks > 0)
				return;

			dryMoveTicks = Info.DryMoveScanInterval;

			if (supply.OnSupplyErrand)
				return;

			if (!string.IsNullOrEmpty(Info.EvacuatingCondition) && self.GetConditionCount(Info.EvacuatingCondition) > 0)
				return;

			var behavior = self.TraitOrDefault<AutoTarget>()?.ResupplyBehaviorValue ?? ResupplyBehavior.Auto;
			if (behavior == ResupplyBehavior.Hold)
				return;

			self.CancelActivity();
		}

		void EvacuateOrRestock(Actor self)
		{
			// Only act once per frame — OnBecomingIdle and ITick can both reach here on the
			// same idle-transition tick.
			if (lastEvacuateTick == self.World.WorldTick)
				return;

			lastEvacuateTick = self.World.WorldTick;

			var autoTarget = self.TraitOrDefault<AutoTarget>();
			var behavior = autoTarget?.ResupplyBehaviorValue ?? ResupplyBehavior.Auto;

			switch (behavior)
			{
				case ResupplyBehavior.Hold:
					return;

				case ResupplyBehavior.Auto:
					if (TryQueueRestockAtNearestHost(self))
						return;
					goto case ResupplyBehavior.Evacuate;

				case ResupplyBehavior.Evacuate:
					var amount = self.GetSellValue();
					self.QueueActivity(false, new RotateToEdge(self, true, amount));
					self.ShowTargetLines();
					return;
			}
		}

		/// <summary>The nearest host this truck could actually refill at, or null. Only docking-aware
		/// providers (Logistics Centres) qualify, so an empty truck never tries to "dock" at a ground
		/// SUPPLYCACHE — the crate has no DockedCondition and no arrival gate, and the transfer path assumes
		/// one. Shared by the idle evacuate/restock path and by the drop errand's post-drop disposition so
		/// the two can never disagree about what counts as a host.</summary>
		Actor NearestRestockHost()
		{
			if (self.TraitOrDefault<IMove>() == null || supply == null)
				return null;

			return self.World.ActorsHavingTrait<SupplyProvider>()
				.Where(a => !a.IsDead && a.IsInWorld && a != self
					&& Info.ValidRelationships.HasRelationship(self.Owner.RelationshipWith(a.Owner))
					&& a.Trait<SupplyProvider>().CurrentSupply > 0
					&& !string.IsNullOrEmpty(a.Trait<SupplyProvider>().Info.DockedCondition))
				.ClosestToIgnoringPath(self);
		}

		bool TryQueueRestockAtNearestHost(Actor self)
		{
			var host = NearestRestockHost();
			if (host == null)
				return false;

			QueueDriveAndRestock(host);
			self.ShowTargetLines();
			return true;
		}

		IEnumerable<IOrderTargeter> IIssueOrder.Orders
		{
			get
			{
				yield return new PickupSupplyOrderTargeter(Info);
				yield return new RestockOrderTargeter(Info);
				yield return new DeliverSupplyOrderTargeter(Info);
				yield return new DeployOrderTargeter("DropSupplyCache", 5,
					() => CanDropCache() ? Info.DropCacheCursor : Info.DropCacheBlockedCursor);
			}
		}

		Order IIssueOrder.IssueOrder(Actor self, IOrderTargeter order, in Target target, bool queued)
		{
			if (order.OrderID == "Restock" || order.OrderID == "DeliverSupply" || order.OrderID == "PickupSupply")
				return new Order(order.OrderID, self, target, queued);

			if (order.OrderID == "DropSupplyCache")
				return new Order("DropSupplyCache", self, queued);

			return null;
		}

		Order IIssueDeployOrder.IssueDeployOrder(Actor self, bool queued)
		{
			return new Order("DropSupplyCache", self, queued);
		}

		bool IIssueDeployOrder.CanIssueDeployOrder(Actor self, bool queued)
		{
			// A QUEUED deploy is judged on the supply we hold, not on the cell we happen to be standing
			// on right now: it fires at the END of the queue, at a cell the truck has not reached yet,
			// so testing THIS cell's occupancy would swallow the order — silently, since the command bar
			// simply filters out actors that answer false — for a reason that will not be true when it
			// runs. Occupancy is knowable only on arrival, and DropSupplyCacheHere re-tests it there by
			// merging into whatever cache already holds the cell. Same shape as
			// GrantChargedConditionOnToggle, which likewise relaxes its now-gate for queued orders.
			if (queued)
				return supply != null && supply.CurrentSupply > 0;

			return CanDropCache();
		}

		string IOrderVoice.VoicePhraseForOrder(Actor self, Order order)
		{
			if (order.OrderString == "DropSupplyCache")
				return Info.DropCacheVoice;
			return null;
		}

		/// <summary>Right-click a friendly ground cache to collect it.
		///
		/// <para>Priority 8, above Restock's 7, so the ordering is decided rather than left to a tie —
		/// though the two are in fact DISJOINT and could not both match: Restock demands a non-empty
		/// DockedCondition (the Logistics Centre's unit.docked) and a crate has none, while this one
		/// demands the SupplyCacheActor type and an LC is not that type. Stated because the disjointness
		/// is what makes the priority uninteresting, not because the priority is doing work.</para>
		///
		/// <para>Matching on SupplyCacheActor rather than on "any SupplyProvider without a
		/// DockedCondition" is deliberate: the looser test would also admit ANOTHER TRUCK, quietly
		/// inventing truck-to-truck supply transfer as a side effect of a pickup order. This is the same
		/// discriminator DropSupplyCacheHere merges into and AbsorbsSupplyCache drains, so the drop and
		/// its inverse agree on what a ground cache is.</para></summary>
		sealed class PickupSupplyOrderTargeter : UnitOrderTargeter
		{
			readonly DropsSupplyCacheInfo info;

			public PickupSupplyOrderTargeter(DropsSupplyCacheInfo info)
				: base("PickupSupply", 8, info.PickupCacheCursor, false, true)
			{
				this.info = info;
			}

			public override bool CanTargetActor(Actor self, Actor target, TargetModifiers modifiers, ref string cursor)
			{
				if (target.Info.Name != info.SupplyCacheActor)
					return false;

				if (!self.Owner.IsAlliedWith(target.Owner))
					return false;

				var cacheSupply = target.TraitOrDefault<SupplyProvider>();
				if (cacheSupply == null || cacheSupply.CurrentSupply <= 0)
					return false;

				// No headroom, no point: the transfer would be a no-op and the truck would drive across
				// the map to perform it.
				var truckSupply = self.TraitOrDefault<SupplyProvider>();
				return truckSupply != null && truckSupply.CurrentSupply < truckSupply.Info.TotalSupply;
			}

			public override bool CanTargetFrozenActor(Actor self, FrozenActor target, TargetModifiers modifiers, ref string cursor)
			{
				return false;
			}
		}

		sealed class RestockOrderTargeter : UnitOrderTargeter
		{
			public RestockOrderTargeter(DropsSupplyCacheInfo info)
				: base("Restock", 7, info.RestockCursor, false, true) { }

			public override bool CanTargetActor(Actor self, Actor target, TargetModifiers modifiers, ref string cursor)
			{
				if (!self.Owner.IsAlliedWith(target.Owner))
					return false;

				// Only docking-aware providers (LC), not ground caches.
				var hostProvider = target.TraitOrDefault<SupplyProvider>();
				if (hostProvider == null || string.IsNullOrEmpty(hostProvider.Info.DockedCondition))
					return false;

				var truckSupply = self.TraitOrDefault<SupplyProvider>();
				if (truckSupply == null)
					return false;

				var notFull = truckSupply.CurrentSupply < truckSupply.Info.TotalSupply;
				var damaged = self.TraitOrDefault<IHealth>()?.DamageState > DamageState.Undamaged;
				if (!notFull && !damaged)
					return false;

				return true;
			}

			public override bool CanTargetFrozenActor(Actor self, FrozenActor target, TargetModifiers modifiers, ref string cursor)
			{
				return false;
			}
		}

		sealed class DeliverSupplyOrderTargeter : UnitOrderTargeter
		{
			public DeliverSupplyOrderTargeter(DropsSupplyCacheInfo info)
				: base("DeliverSupply", 6, info.RestockCursor, false, true) { }

			public override bool CanTargetActor(Actor self, Actor target, TargetModifiers modifiers, ref string cursor)
			{
				// Default right-click on an LC goes to Restock (priority 7). Only
				// Ctrl+click (ForceMove) means "deliver my supply to this LC".
				if (!modifiers.HasModifier(TargetModifiers.ForceMove))
					return false;

				if (!self.Owner.IsAlliedWith(target.Owner))
					return false;

				if (target.TraitOrDefault<AbsorbsSupplyCache>() == null)
					return false;

				var truckSupply = self.TraitOrDefault<SupplyProvider>();
				return truckSupply != null && truckSupply.CurrentSupply > 0;
			}

			public override bool CanTargetFrozenActor(Actor self, FrozenActor target, TargetModifiers modifiers, ref string cursor)
			{
				return false;
			}
		}
	}
}
