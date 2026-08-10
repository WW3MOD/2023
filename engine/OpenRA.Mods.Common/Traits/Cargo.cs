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
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("This actor can transport Passenger actors.")]
	public class CargoInfo : TraitInfo, Requires<IOccupySpaceInfo>
	{
		[Desc("Should this actor turn nutral when not loaded? For civilian buildings.")]
		public readonly bool Neutral = false;

		[Desc("The maximum sum of Passenger.Weight that this actor can support.")]
		public readonly int MaxWeight = 0;

		[Desc("`Passenger.CargoType`s that can be loaded into this actor.")]
		public readonly HashSet<string> Types = new HashSet<string>();

		[Desc("A list of actor types that are initially spawned into this actor.")]
		public readonly string[] InitialUnits = Array.Empty<string>();

		[Desc("When this actor is sold should all of its passengers be unloaded?")]
		public readonly bool EjectOnSell = true;

		[Desc("When this actor dies should all of its passengers be unloaded?")]
		public readonly bool EjectOnDeath = true;

		[Desc("Terrain types that this actor is allowed to eject actors onto. Leave empty for all terrain types.")]
		public readonly HashSet<string> UnloadTerrainTypes = new HashSet<string>();

		[VoiceReference]
		[Desc("Voice to play when ordered to unload the passengers.")]
		public readonly string UnloadVoice = "Action";

		[Desc("Radius to search for a load/unload location if the ordered cell is blocked.")]
		public readonly WDist LoadRange = WDist.FromCells(5);

		[Desc("Which direction the passenger will face (relative to the transport) when unloading.")]
		public readonly WAngle PassengerFacing = new WAngle(512);

		[Desc("Delay (in ticks) before continuing after loading a passenger.")]
		public readonly int AfterLoadDelay = 8;

		[Desc("Delay (in ticks) before unloading the first passenger.")]
		public readonly int BeforeUnloadDelay = 8;

		[Desc("Delay (in ticks) before continuing after unloading a passenger.")]
		public readonly int AfterUnloadDelay = 25;

		[Desc("How many passengers leave back-to-back before the longer inter-group pause. 2 = they ",
			"come out in pairs, which is what reads as a squad dismounting rather than a clown car. ",
			"1 puts the long pause between every passenger; 0 or less disables pacing entirely.")]
		public readonly int UnloadGroupSize = 2;

		[Desc("Ticks between two passengers inside the same group. Deliberately small — the pair is ",
			"meant to look like it left together. 0 restores the pre-pacing behaviour of one ",
			"passenger per tick.")]
		public readonly int IntraGroupUnloadDelay = 4;

		[Desc("The pause between groups, as a multiple of IntraGroupUnloadDelay. 3 = the gap between ",
			"pairs is three times the gap inside a pair; that ratio is what gives the groups visible ",
			"separation. Raise it to spread a dismount out further, set it to 1 for an even cadence.")]
		public readonly int InterGroupUnloadDelayMultiplier = 3;

		[Desc("Fraction of the transport's MaxHP that a single hit must exceed before any of it is ",
			"felt by the passengers, as a percentage. Fire that merely grinds the hull down passes ",
			"nothing through; only a hit big enough to matter does.")]
		public readonly int PassengerDamageThresholdPercent = 25;

		[Desc("Percentage applied to the passenger's share of a hit once it clears the threshold. ",
			"The raw share is the overkill above the threshold expressed against the transport's ",
			"MaxHP and scaled by the passenger's own. 100 = raw, 50 = half. This only touches hits ",
			"the transport SURVIVES — a hit that destroys it outright is handled by the separate ",
			"EjectOnDeath path, which stays lethal, so lowering this makes mounted infantry ride out ",
			"hull hits without making them immortal in a wreck.")]
		public readonly int PassengerDamageSharePercent = 50;

		[Desc("Random spread added to the passenger's share before PassengerDamageSharePercent is ",
			"applied, as a fraction (1/N) of the passenger's MaxHP. 5 = up to a fifth of their ",
			"health. 0 disables the roll and makes the share deterministic.")]
		public readonly int PassengerDamageVarianceDivisor = 5;

		[CursorReference]
		[Desc("Cursor to display when able to unload the passengers.")]
		public readonly string UnloadCursor = "deploy";

		[CursorReference]
		[Desc("Cursor to display when unable to unload the passengers.")]
		public readonly string UnloadBlockedCursor = "deploy-blocked";

		[GrantedConditionReference]
		[Desc("The condition to grant to self while waiting for cargo to load.")]
		public readonly string LoadingCondition = null;

		[GrantedConditionReference]
		[Desc("The condition to grant to self while passengers are loaded.",
			"Condition can stack with multiple passengers.")]
		public readonly string LoadedCondition = null;

		[ActorReference(dictionaryReference: LintDictionaryReference.Keys)]
		[Desc("Conditions to grant when specified actors are loaded inside the transport.",
			"A dictionary of [actor name]: [condition].")]
		public readonly Dictionary<string, string> PassengerConditions = new Dictionary<string, string>();

		[GrantedConditionReference]
		public IEnumerable<string> LinterPassengerConditions => PassengerConditions.Values;

		public override object Create(ActorInitializer init) { return new Cargo(init, this); }
	}

	public class Cargo : IIssueOrder, IResolveOrder, IOrderVoice, INotifyCreated, INotifyKilled, INotifyDamage,
		INotifyOwnerChanged, INotifySold, INotifyActorDisposing, IIssueDeployOrder,
		ITransformActorInitModifier
	{
		public readonly CargoInfo Info;
		readonly Actor self;
		readonly List<Actor> cargo = new List<Actor>();

		/// <summary>When true, cargo loading is blocked entirely (e.g., crash-disabled helicopter).
		/// Affects both targeting UI (cursor shows blocked) and actual loading.</summary>
		public bool LoadingBlocked { get; set; }
		readonly HashSet<Actor> reserves = new HashSet<Actor>();
		readonly Dictionary<string, Stack<int>> passengerTokens = new Dictionary<string, Stack<int>>();
		readonly Lazy<IFacing> facing;
		readonly bool checkTerrainType;

		int totalWeight = 0;
		int reservedWeight = 0;
		Aircraft aircraft;

		// Pre-queued rally points: passengers will move/interact here on ejection
		readonly Dictionary<uint, Target> ejectRallyPoints = new Dictionary<uint, Target>();
		ICargoCanLoadFilter[] loadFilters;
		int loadingToken = Actor.InvalidConditionToken;
		readonly Stack<int> loadedTokens = new Stack<int>();
		bool takeOffAfterLoad;
		bool initialised;

		readonly CachedTransform<CPos, IEnumerable<CPos>> currentAdjacentCells;

		public IEnumerable<CPos> CurrentAdjacentCells => currentAdjacentCells.Update(self.Location);

		public IEnumerable<Actor> Passengers => cargo;
		public int PassengerCount => cargo.Count;

		enum State { Free, Locked }
		State state = State.Free;

		public Cargo(ActorInitializer init, CargoInfo info)
		{
			self = init.Self;
			Info = info;
			checkTerrainType = info.UnloadTerrainTypes.Count > 0;

			currentAdjacentCells = new CachedTransform<CPos, IEnumerable<CPos>>(loc =>
			{
				return Util.AdjacentCells(self.World, Target.FromActor(self)).Where(c => loc != c);
			});

			var runtimeCargoInit = init.GetOrDefault<RuntimeCargoInit>(info);
			var cargoInit = init.GetOrDefault<CargoInit>(info);
			if (runtimeCargoInit != null)
			{
				cargo = runtimeCargoInit.Value.ToList();
				totalWeight = cargo.Sum(c => GetWeight(c));
			}
			else if (cargoInit != null)
			{
				foreach (var u in cargoInit.Value)
				{
					var unit = self.World.CreateActor(false, u.ToLowerInvariant(),
						new TypeDictionary { new OwnerInit(self.Owner) });

					cargo.Add(unit);
				}

				totalWeight = cargo.Sum(c => GetWeight(c));
			}
			else
			{
				foreach (var u in info.InitialUnits)
				{
					var unit = self.World.CreateActor(false, u.ToLowerInvariant(),
						new TypeDictionary { new OwnerInit(self.Owner) });

					cargo.Add(unit);
				}

				totalWeight = cargo.Sum(c => GetWeight(c));
			}

			facing = Exts.Lazy(self.TraitOrDefault<IFacing>);
		}

		/* // Request the closest actorst that are cargoable to enter the transport
		bool PickUpClosestActors(Actor self)
		{
			// Find the closest actors to self
			// This method finds the closest actors that can be picked up by the transport
			// It will attempt to pick up the closest available actors that match the cargo criteria
		} */

		void INotifyCreated.Created(Actor self)
		{
			aircraft = self.TraitOrDefault<Aircraft>();
			loadFilters = self.TraitsImplementing<ICargoCanLoadFilter>().ToArray();

			if (cargo.Count > 0)
			{
				foreach (var c in cargo)
					if (Info.PassengerConditions.TryGetValue(c.Info.Name, out var passengerCondition))
						passengerTokens.GetOrAdd(c.Info.Name).Push(self.GrantCondition(passengerCondition));

				if (!string.IsNullOrEmpty(Info.LoadedCondition))
					loadedTokens.Push(self.GrantCondition(Info.LoadedCondition));
			}

			// Defer notifications until we are certain all traits on the transport are initialised
			self.World.AddFrameEndTask(w =>
			{
				foreach (var c in cargo)
				{
					c.Trait<Passenger>().Transport = self;

					foreach (var nec in c.TraitsImplementing<INotifyEnteredCargo>())
						nec.OnEnteredCargo(c, self);

					foreach (var npe in self.TraitsImplementing<INotifyPassengerEntered>())
						npe.OnPassengerEntered(self, c);
				}

				initialised = true;
			});
		}

		static int GetWeight(Actor a) { return a.Info.TraitInfo<PassengerInfo>().Weight; }

		public IEnumerable<IOrderTargeter> Orders
		{
			get
			{
				if (!IsEmpty())
					yield return new DeployOrderTargeter("Unload", 10,
						() => CanUnload() ? Info.UnloadCursor : Info.UnloadBlockedCursor);
			}
		}

		public Order IssueOrder(Actor self, IOrderTargeter order, in Target target, bool queued)
		{
			if (order.OrderID == "Unload")
				return new Order(order.OrderID, self, queued);

			return null;
		}

		Order IIssueDeployOrder.IssueDeployOrder(Actor self, bool queued)
		{
			return new Order("Unload", self, queued);
		}

		bool IIssueDeployOrder.CanIssueDeployOrder(Actor self, bool queued) { return !IsEmpty(); }

		public void ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "Unload")
			{
				if (!order.Queued && !CanUnload())
					return;

				self.QueueActivity(order.Queued, new UnloadCargo(self, Info.LoadRange));
			}
			else if (order.OrderString == "UnloadCargoPassenger")
			{
				var passenger = self.World.GetActorById(order.ExtraData);
				if (passenger == null || !cargo.Contains(passenger))
					return;

				self.QueueActivity(order.Queued, new UnloadCargo(self, Info.LoadRange, passenger));
			}
		}

		public bool CanUnload(BlockedByActor check = BlockedByActor.None)
		{
			if (checkTerrainType)
			{
				var terrainType = self.World.Map.GetTerrainInfo(self.Location).Type;

				if (!Info.UnloadTerrainTypes.Contains(terrainType))
					return false;
			}

			return !IsEmpty() && (aircraft == null || aircraft.CanLand(self.Location, blockedByMobile: false))
				&& CurrentAdjacentCells != null && CurrentAdjacentCells.Any(c => Passengers.Any(p => !p.IsDead && p.Trait<IPositionable>().CanEnterCell(c, null, check)));
		}

		public bool CanLoad(Actor a)
		{
			if (LoadingBlocked)
				return false;

			if (loadFilters != null)
				foreach (var f in loadFilters)
					if (!f.CanLoadPassenger(self, a))
						return false;

			return reserves.Contains(a) || HasSpace(GetWeight(a));
		}

		internal bool ReserveSpace(Actor a)
		{
			if (LoadingBlocked)
				return false;

			if (reserves.Contains(a))
				return true;

			if (loadFilters != null)
				foreach (var f in loadFilters)
					if (!f.CanLoadPassenger(self, a))
						return false;

			var w = GetWeight(a);
			if (!HasSpace(w))
				return false;

			if (loadingToken == Actor.InvalidConditionToken)
				loadingToken = self.GrantCondition(Info.LoadingCondition);

			reserves.Add(a);
			reservedWeight += w;
			LockForPickup(self);

			return true;
		}

		internal void UnreserveSpace(Actor a)
		{
			if (!reserves.Contains(a) || self.IsDead)
				return;

			reservedWeight -= GetWeight(a);
			reserves.Remove(a);
			ReleaseLock(self);

			if (loadingToken != Actor.InvalidConditionToken)
				loadingToken = self.RevokeCondition(loadingToken);
		}

		// Prepare for transport pickup
		void LockForPickup(Actor self)
		{
			if (state == State.Locked)
				return;

			state = State.Locked;

			self.CancelActivity();

			var air = self.TraitOrDefault<Aircraft>();
			if (air != null && !air.AtLandAltitude)
			{
				takeOffAfterLoad = true;
				self.QueueActivity(new Land(self));
			}

			self.QueueActivity(new WaitFor(() => state != State.Locked, false));
		}

		void ReleaseLock(Actor self)
		{
			if (reservedWeight != 0)
				return;

			state = State.Free;

			self.QueueActivity(new Wait(Info.AfterLoadDelay, false));
			if (takeOffAfterLoad)
				self.QueueActivity(new TakeOff(self));

			takeOffAfterLoad = false;
		}

		public string VoicePhraseForOrder(Actor self, Order order)
		{
			if (order.OrderString != "Unload" || IsEmpty() || !self.HasVoice(Info.UnloadVoice))
				return null;

			return Info.UnloadVoice;
		}

		public bool HasSpace(int weight)
		{
			if (loadFilters != null)
				foreach (var f in loadFilters)
					if (!f.CanLoadPassenger(self, null))
						return false;

			return totalWeight + reservedWeight + weight <= Info.MaxWeight;
		}

		/// <summary>Available cargo weight after passengers and reservations.</summary>
		public int AvailableWeight => Info.MaxWeight - totalWeight - reservedWeight;

		/// <summary>Set a rally point for a passenger to execute on ejection.</summary>
		public void SetEjectRally(uint passengerActorId, Target target)
		{
			ejectRallyPoints[passengerActorId] = target;
		}

		/// <summary>Clear the rally point for a passenger.</summary>
		public void ClearEjectRally(uint passengerActorId)
		{
			ejectRallyPoints.Remove(passengerActorId);
		}

		/// <summary>Get the rally point for a passenger, if any.</summary>
		public Target GetEjectRally(uint passengerActorId)
		{
			return ejectRallyPoints.TryGetValue(passengerActorId, out var target) ? target : Target.Invalid;
		}

		/// <summary>Check if a passenger has a rally point assigned.</summary>
		public bool HasEjectRally(uint passengerActorId) => ejectRallyPoints.ContainsKey(passengerActorId);

		public bool IsEmpty() { return cargo.Count == 0; }

		public Actor Peek() { return cargo.Last(); }

		public Actor Unload(Actor self, Actor passenger = null)
		{
			passenger = passenger ?? cargo.Last();
			if (!cargo.Remove(passenger))
				throw new ArgumentException("Attempted to unload an actor that is not a passenger.");

			totalWeight -= GetWeight(passenger);

			SetPassengerFacing(passenger);

			foreach (var npe in self.TraitsImplementing<INotifyPassengerExited>())
				npe.OnPassengerExited(self, passenger);

			foreach (var nec in passenger.TraitsImplementing<INotifyExitedCargo>())
				nec.OnExitedCargo(passenger, self);

			var p = passenger.Trait<Passenger>();
			p.Transport = null;

			if (passengerTokens.TryGetValue(passenger.Info.Name, out var passengerToken) && passengerToken.Count > 0)
				self.RevokeCondition(passengerToken.Pop());

			if (loadedTokens.Count > 0)
				self.RevokeCondition(loadedTokens.Pop());

			return passenger;
		}

		void SetPassengerFacing(Actor passenger)
		{
			if (facing.Value == null)
				return;

			var passengerFacing = passenger.TraitOrDefault<IFacing>();
			if (passengerFacing != null)
				passengerFacing.Facing = facing.Value.Facing + Info.PassengerFacing;
		}

		public void Load(Actor cargoActor, Actor passengerActor)
		{
			// Skip ownership change when GarrisonManager handles it via DynamicOwnership
			// The ChangeOwnerSync here triggers expensive World.Remove/Add + shroud recalc
			if (cargoActor.Owner != passengerActor.Owner && !cargoActor.Info.HasTraitInfo<GarrisonManagerInfo>())
				cargoActor.ChangeOwnerSync(passengerActor.Owner, false);

			cargo.Add(passengerActor);
			var w = GetWeight(passengerActor);
			totalWeight += w;
			if (reserves.Contains(passengerActor))
			{
				reservedWeight -= w;
				reserves.Remove(passengerActor);
				ReleaseLock(cargoActor);

				if (loadingToken != Actor.InvalidConditionToken)
					loadingToken = cargoActor.RevokeCondition(loadingToken);
			}

			// Don't initialise (effectively twice) if this runs before the FrameEndTask from Created
			if (initialised)
			{
				passengerActor.Trait<Passenger>().Transport = cargoActor;

				foreach (var nec in passengerActor.TraitsImplementing<INotifyEnteredCargo>())
					nec.OnEnteredCargo(passengerActor, cargoActor);

				foreach (var npe in cargoActor.TraitsImplementing<INotifyPassengerEntered>())
					npe.OnPassengerEntered(cargoActor, passengerActor);
			}

			if (Info.PassengerConditions.TryGetValue(passengerActor.Info.Name, out var passengerCondition))
				passengerTokens.GetOrAdd(passengerActor.Info.Name).Push(cargoActor.GrantCondition(passengerCondition));

			if (!string.IsNullOrEmpty(Info.LoadedCondition))
				loadedTokens.Push(cargoActor.GrantCondition(Info.LoadedCondition));
		}

		/// <summary>How much of a hit on the transport is felt by one passenger.
		/// Split out as a pure function so the curve can be tuned and tested without a World.
		/// <paramref name="varianceRoll"/> is the pre-rolled random spread, so callers own the RNG.</summary>
		public static int PassengerDamageFromTransportHit(int passengerMaxHP, int transportMaxHP,
			int damage, int thresholdPercent, int sharePercent, int varianceRoll)
		{
			if (transportMaxHP <= 0 || damage <= 0)
				return 0;

			// Below the threshold nothing reaches the crew compartment at all. This
			// is what keeps a long grind of small-arms fire from bleeding the men
			// inside: only a single blow big enough to matter against the hull does.
			var threshold = (int)((long)transportMaxHP * thresholdPercent / 100);
			if (damage <= threshold)
				return 0;

			// Only the overkill above the threshold is shared, expressed as a
			// fraction of the hull and scaled onto the passenger's own health bar.
			var share = (int)((long)passengerMaxHP * (damage - threshold) / transportMaxHP);

			// The roll is folded in BEFORE the cut so the curve is scaled end to
			// end. Applied afterwards it would dominate the halved share and the
			// unlucky rolls — the ones that actually decide who lives — would barely
			// move.
			return (share + varianceRoll) * sharePercent / 100;
		}

		/// <summary>How much of the killing blow is felt by a passenger when the transport is
		/// destroyed under them. Deliberately NOT reduced by PassengerDamageSharePercent and
		/// deliberately not thresholded: a blow that writes the hull off in one go is the case that
		/// has to stay lethal, and it stays lethal because overkill is carried through here — a
		/// 20000 tank round on a 14000 hull reports 20000, not 14000. Finishing off an
		/// already-crippled transport with a small hit is correspondingly survivable, which is the
		/// same asymmetry the crew get.</summary>
		public static int PassengerDamageFromTransportDeath(int passengerMaxHP, int transportMaxHP,
			int damage, int varianceRoll)
		{
			if (transportMaxHP <= 0 || damage <= 0)
				return 0;

			return (int)((long)passengerMaxHP * damage / transportMaxHP) + varianceRoll;
		}

		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
			if (!IsEmpty())
			{
				// Skip legacy damage forwarding when GarrisonProtection handles it
				if (self.Info.HasTraitInfo<GarrisonProtectionInfo>())
					return;

				var healthTrait = self.Trait<Health>();
				var damageDealt = e.Damage.Value;
				if (damageDealt > 0)
				{
					// Copy first — InflictDamage below can kill a passenger and reenter.
					foreach (var passenger in Passengers.ToList())
					{
						if (passenger.IsDead)
							continue;

						var passengerMaxHP = passenger.Trait<Health>().MaxHP;
						var varianceBand = Info.PassengerDamageVarianceDivisor > 0
							? passengerMaxHP / Info.PassengerDamageVarianceDivisor
							: 0;
						var varianceRoll = varianceBand > 0 ? self.World.SharedRandom.Next(varianceBand) : 0;

						var damageToDeal = PassengerDamageFromTransportHit(passengerMaxHP, healthTrait.MaxHP,
							damageDealt, Info.PassengerDamageThresholdPercent, Info.PassengerDamageSharePercent,
							varianceRoll);

						if (damageToDeal > 0)
							passenger.InflictDamage(e.Attacker, new Damage(damageToDeal));
					}
				}

				// Unload when low health
				if (healthTrait.DamageState == DamageState.Critical)
				{
					var currentActivityType = self.CurrentActivity?.GetType();

					if (CanUnload() && (currentActivityType == null || currentActivityType.Name != "UnloadCargo"))
						self.QueueActivity(false, new UnloadCargo(self, Info.LoadRange));
				}
			}
		}

		void INotifyKilled.Killed(Actor self, AttackInfo e)
		{
			if (Info.EjectOnDeath)
			{
				while (!IsEmpty() && CanUnload(BlockedByActor.All))
				{
					var passenger = Unload(self);
					var passengerMaxHP = passenger.Trait<Health>().MaxHP;
					var varianceBand = Info.PassengerDamageVarianceDivisor > 0
						? passengerMaxHP / Info.PassengerDamageVarianceDivisor
						: 0;
					var random = varianceBand > 0 ? self.World.SharedRandom.Next(varianceBand) : 0;

					var damageToDeal = PassengerDamageFromTransportDeath(passengerMaxHP,
						self.Trait<Health>().MaxHP, e.Damage.Value, random);

					if (damageToDeal > 0)
						passenger.InflictDamage(e.Attacker, new Damage(damageToDeal));

					if (!passenger.IsDead)
					{
						var cp = self.CenterPosition;
						var inAir = self.World.Map.DistanceAboveTerrain(cp).Length != 0;
						var positionable = passenger.Trait<IPositionable>();
						positionable.SetPosition(passenger, self.Location);

						if (!inAir && positionable.CanEnterCell(self.Location, self, BlockedByActor.None))
						{
							self.World.AddFrameEndTask(w => w.Add(passenger));
							var nbms = passenger.TraitsImplementing<INotifyBlockingMove>();
							foreach (var nbm in nbms)
								nbm.OnNotifyBlockingMove(passenger, passenger);
						}
						else
							passenger.Kill(e.Attacker);
					}
				}
			}
			else
			{
				foreach (var c in cargo)
					c.Kill(e.Attacker);

				cargo.Clear();
			}
		}

		void INotifyActorDisposing.Disposing(Actor self)
		{
			foreach (var c in cargo)
				c.Dispose();

			cargo.Clear();
		}

		void INotifySold.Selling(Actor self) { }
		void INotifySold.Sold(Actor self)
		{
			if (!Info.EjectOnSell || cargo == null)
				return;

			while (!IsEmpty())
				SpawnPassenger(Unload(self));
		}

		void SpawnPassenger(Actor passenger)
		{
			self.World.AddFrameEndTask(w =>
			{
				w.Add(passenger);
				passenger.Trait<IPositionable>().SetPosition(passenger, self.Location);

				// TODO: this won't work well for >1 actor as they should move towards the next enterable (sub) cell instead
			});
		}

		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner)
		{
			if (cargo == null)
				return;

			foreach (var p in Passengers)
				p.ChangeOwner(newOwner);
		}

		void ITransformActorInitModifier.ModifyTransformActorInit(Actor self, TypeDictionary init)
		{
			init.Add(new RuntimeCargoInit(Info, Passengers.ToArray()));
		}
	}

	public class RuntimeCargoInit : ValueActorInit<Actor[]>, ISuppressInitExport
	{
		public RuntimeCargoInit(TraitInfo info, Actor[] value)
			: base(info, value) { }
	}

	public class CargoInit : ValueActorInit<string[]>
	{
		public CargoInit(TraitInfo info, string[] value)
			: base(info, value) { }
	}
}
