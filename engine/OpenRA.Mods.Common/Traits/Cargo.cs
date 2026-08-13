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
using OpenRA.Effects;
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
			"passenger per tick.",
			"Note the two readings of 'the same spacing as before': literally, that is 1 tick, ",
			"because the unload loop ran once per tick. At the 60ms timestep 1 and 3 ticks are ",
			"0.06s and 0.18s, which is below what anyone can see, so the pairing would be invisible ",
			"and the point of the change lost. 4 is chosen instead so the 3x gap between groups has ",
			"something visible to be three times larger than. Set this to 1 for the literal reading.")]
		public readonly int IntraGroupUnloadDelay = 4;

		[Desc("The pause between groups, as a multiple of IntraGroupUnloadDelay. 3 = the gap between ",
			"pairs is three times the gap inside a pair; that ratio is what gives the groups visible ",
			"separation. Raise it to spread a dismount out further, set it to 1 for an even cadence.")]
		public readonly int InterGroupUnloadDelayMultiplier = 3;

		[Desc("Damage state at which passengers bail out on their own, without an unload order and ",
			"without waiting for the transport to stop. Heavy (HP <50%) is the point the hull starts ",
			"burning down for good and VehicleCrew bails the crew, so the men in the back leave on the ",
			"same cue rather than a damage state later. NOTE this is the damage STATE, not the ",
			"`critical-damage` condition this mod grants below 25% — the bail reads that condition ",
			"nowhere. The men leave one at a time on the group pacing above, so a burning transport ",
			"streams its squad out at the same rhythm as an ordered dismount; what the emergency path ",
			"drops are the drill delays, BeforeUnloadDelay and AfterUnloadDelay, and the wait for the ",
			"hull to stop. The first man is out on the tick the threshold is crossed.",
			"Applies to ground transports only; airborne ones use ",
			"AircraftEmergencyBailDamageState below.")]
		public readonly DamageState EmergencyBailDamageState = DamageState.Heavy;

		[Desc("As EmergencyBailDamageState, but for transports with an Aircraft trait. Held at ",
			"Critical — the value in use before ground transports were moved to Heavy — because ",
			"dumping troops out of an airborne transport a whole damage state earlier is a separate ",
			"question from getting them out of a burning APC, and it has not been asked. Airborne ",
			"transports also do not take the direct-placement path at all: they keep the ordered ",
			"unload, which lands first.")]
		public readonly DamageState AircraftEmergencyBailDamageState = DamageState.Critical;

		[Desc("Ticks to wait after the transport reaches EmergencyBailDamageState before the ",
			"passengers actually leave. 0 (default) means they leave on the same tick the threshold ",
			"is crossed, which puts them on the ground BEFORE the crew — VehicleCrew waits for the ",
			"hull to roll to a stop (StopTimeout) and then PostStopDelay again, roughly 45 ticks in ",
			"total. Set this to 45 to have passengers and crew leave together instead. This is the ",
			"knob for that judgement; it is deliberately not pre-decided here.")]
		public readonly int EmergencyBailDelay = 0;

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
		// Eight compass headings a bailing passenger can run in to clear the hull.
		static readonly CVec[] BailScatterDirections =
		{
			new CVec(0, -1), new CVec(1, -1), new CVec(1, 0), new CVec(1, 1),
			new CVec(0, 1), new CVec(-1, 1), new CVec(-1, 0), new CVec(-1, -1),
		};

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

		// Latched once the passengers have bailed under EmergencyBailDamageState, so
		// the following hits (including the ChangesHealth bleed) don't re-run it.
		bool bailedOut;

		// A scheduled-but-not-yet-fired bail (EmergencyBailDelay > 0). Tracked apart
		// from bailedOut because a repair inside the delay window clears that one.
		bool bailPending;

		// True from the moment the first man leaves until the stagger stops. The bail
		// no longer completes in one tick, so hits landing mid-stagger must not start
		// a second chain running alongside the first. bailedOut cannot carry this on
		// its own: it is cleared whenever the hull is repaired back below the
		// threshold, which can happen while the stagger is still going.
		bool bailStaggerRunning;

		// Men this bail has already put on the ground, feeding the shared group
		// cadence. Reset per bail, not per passenger, so a transport that is hit,
		// repaired and hit again starts its rhythm over rather than mid-pair.
		int bailUnloaded;

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

			// Fresh passengers have not bailed out of anything. Without this a
			// transport that emptied itself under EmergencyBailDamageState and was
			// then reloaded would hold the latch shut and strand the new stick — and
			// the damage-state reset below cannot cover it, since a transport that is
			// still burning never climbs back above the line.
			bailedOut = false;

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

		/// <summary>The single-hit size below which nothing reaches the passengers at all.
		/// Exposed separately so the caller can test it BEFORE looping and rolling: the roll has to
		/// stay inside the same branch it was in before, or the shared RNG stream advances on ticks
		/// where main's does not and replay/benchmark byte-identity breaks for no reason anyone
		/// asked for.</summary>
		public static int PassengerDamageThreshold(int transportMaxHP, int thresholdPercent)
		{
			return (int)((long)transportMaxHP * thresholdPercent / 100);
		}

		/// <summary>Whether the transport's damage state calls for an unordered bail-out.
		/// Dead is deliberately excluded, and that exclusion is the whole point of this being a
		/// named predicate. Health clamps HP to 0 and evaluates DamageState BEFORE it notifies
		/// Damaged (Health.cs:189-200), so the killing blow arrives here already reading Dead — and
		/// Dead is numerically ABOVE Heavy, so a naive `>=` passes it. Bailing there would empty the
		/// hold synchronously and leave INotifyKilled's EjectOnDeath iterating an empty list, so a
		/// one-shot kill on a loaded transport would let the entire squad walk away unhurt. Once the
		/// hull is dead the cargo belongs to Killed.</summary>
		public static bool ShouldEmergencyBail(DamageState current, DamageState bailAt)
		{
			return current >= bailAt && current < DamageState.Dead;
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
			var threshold = PassengerDamageThreshold(transportMaxHP, thresholdPercent);
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

		/// <summary>Ticks to hold before the next passenger steps out, given how many have left
		/// already. Passengers leave in groups of <paramref name="groupSize"/> back-to-back, then the
		/// gap widens by <paramref name="interMultiplier"/> before the next group starts — so a stick
		/// of four reads as two-pause-two, not as a single spill. 0 means pacing is off, which puts
		/// one passenger per tick.
		/// This is the single definition of the dismount rhythm: both the ordered unload and the
		/// emergency bail drive off it, so a burning transport empties in the same cadence as one
		/// that was told to. Do not add a second timer beside it.</summary>
		public static int NextUnloadDelay(int unloaded, int groupSize, int intraDelay, int interMultiplier)
		{
			if (groupSize <= 0 || intraDelay <= 0)
				return 0;

			return unloaded % groupSize == 0
				? intraDelay * Math.Max(1, interMultiplier)
				: intraDelay;
		}

		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
			if (IsEmpty())
				return;

			// Skip legacy damage forwarding when GarrisonProtection handles it
			if (self.Info.HasTraitInfo<GarrisonProtectionInfo>())
				return;

			var healthTrait = self.Trait<Health>();
			var damageDealt = e.Damage.Value;

			// Threshold tested here rather than inside the loop so the RNG is only
			// touched by hits that can actually produce damage — see
			// PassengerDamageThreshold. It also keeps the ToList allocation off the
			// path taken by every scratch and every ChangesHealth bleed tick.
			if (damageDealt > PassengerDamageThreshold(healthTrait.MaxHP, Info.PassengerDamageThresholdPercent))
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

			// Airborne transports bail on their own threshold and never take the
			// direct-placement path below.
			//
			// VOCABULARY, and it decides the trigger: when this project's user says a
			// vehicle is "in critical damage" they mean DamageState.Heavy — HP under
			// 50%, the point the hull catches fire and is doomed. They do NOT mean the
			// `critical-damage` CONDITION, which this mod grants under 25%
			// (defaults.yaml, GrantConditionOnDamageState@CriticalDamage) and which
			// drives smoke, accuracy penalties and pips only. The bail is deliberately
			// keyed on the damage STATE below and reads that condition nowhere. An
			// implementer who reaches for the condition will fire this a whole damage
			// state too late.
			var bailAt = aircraft != null ? Info.AircraftEmergencyBailDamageState : Info.EmergencyBailDamageState;

			if (!ShouldEmergencyBail(healthTrait.DamageState, bailAt))
			{
				// Re-arm on the way back up. Repairs arrive through this same
				// notification carrying a negative value. Note this is deliberately
				// NOT reached when the hull is Dead: the latch is irrelevant then and
				// Killed owns the cargo.
				if (healthTrait.DamageState < bailAt)
					bailedOut = false;

				return;
			}

			// An airborne transport cannot simply open the doors — the passengers
			// would be stepping out at altitude. Those stay on the ordered path,
			// which lands first. This is retried per hit rather than latched
			// because the landing can be interrupted.
			if (aircraft != null)
			{
				var currentActivityType = self.CurrentActivity?.GetType();
				if (CanUnload() && (currentActivityType == null || currentActivityType.Name != "UnloadCargo"))
					self.QueueActivity(false, new UnloadCargo(self, Info.LoadRange));

				return;
			}

			if (bailedOut || bailStaggerRunning)
				return;

			if (Info.EmergencyBailDelay <= 0)
			{
				BeginEmergencyBail(self);
				return;
			}

			// One pending action at a time. `bailedOut` alone cannot carry this: a
			// repair inside the window drops back below the threshold and clears it,
			// so a later hit would queue a second countdown alongside the first.
			if (bailPending)
				return;

			// Latch immediately so the wait is not restarted by every bleed tick.
			bailPending = true;
			bailedOut = true;

			self.World.AddFrameEndTask(w => w.Add(new DelayedAction(Info.EmergencyBailDelay, () =>
			{
				bailPending = false;

				// The hull may have died while we waited, in which case Killed has
				// already dealt with everyone aboard.
				if (self.IsDead || IsEmpty())
					return;

				// Re-check the condition we were scheduled on. A transport repaired
				// back above the line during the window is not bailing out any more,
				// and must not dump its squad on the ground because a countdown
				// started under conditions that no longer hold. Re-open the latch so
				// a later hit schedules a fresh one.
				if (!ShouldEmergencyBail(self.Trait<Health>().DamageState, bailAt))
				{
					bailedOut = false;
					return;
				}

				BeginEmergencyBail(self);
			})));
		}

		/// <summary>Start the bail. The men leave one at a time on the same cadence an ordered
		/// dismount uses, so a burning transport streams its squad out rather than making the whole
		/// stick appear in one frame. Everything else about the emergency path is unchanged and
		/// deliberate: no unload order, no Move to a drop cell, and no waiting for the hull to roll
		/// to a stop — the first man is on the ground on the tick the threshold is crossed, while
		/// the vehicle is still rolling.</summary>
		void BeginEmergencyBail(Actor self)
		{
			bailStaggerRunning = true;
			bailUnloaded = 0;
			EmergencyBailStep(self);
		}

		/// <summary>One man out, then schedule the next. Ends the chain when the hold is empty or
		/// when nobody left aboard can be placed.</summary>
		void EmergencyBailStep(Actor self)
		{
			// The hull can die between steps — it is already burning, so this is the
			// common ending rather than an edge case. Killed owns the cargo from that
			// point and EjectOnDeath puts whoever had not reached the door yet on the
			// ground, so stopping here strands nobody. Note it is Killed that runs,
			// not this path: Damaged's ShouldEmergencyBail guard excludes Dead, which
			// is what keeps a killing blow from being swallowed as a bail.
			if (self.IsDead || self.Disposed)
			{
				bailStaggerRunning = false;
				return;
			}

			// Re-check the condition the chain was scheduled on, for the same reason
			// the EmergencyBailDelay path re-checks its countdown: a transport repaired
			// back above the line mid-stagger is not bailing out any more, and must not
			// keep dumping the rest of the squad because a chain started under
			// conditions that no longer hold. Abort rather than run to completion —
			// otherwise an engineer healing a burning APC leaves it calmly ejecting men
			// one at a time while no longer on fire.
			// Ordering matters: the dead guard above comes first. On a killing blow
			// DamageState is Dead and this predicate is false too, but that case belongs
			// to Killed, not here.
			// bailedOut is deliberately left false so a later hit starts a fresh chain.
			var bailAt = aircraft != null ? Info.AircraftEmergencyBailDamageState : Info.EmergencyBailDamageState;
			if (!ShouldEmergencyBail(self.Trait<Health>().DamageState, bailAt))
			{
				bailStaggerRunning = false;
				return;
			}

			var placed = EmergencyBailOut(self);
			if (!placed || IsEmpty())
			{
				// Nobody could be placed: every remaining man is boxed in, or the
				// transport is amphibious and still over water. Drop the latch so the
				// next hit retries, exactly as the unpaced bail did.
				bailStaggerRunning = false;
				bailedOut = IsEmpty();
				return;
			}

			bailUnloaded++;

			var delay = NextUnloadDelay(bailUnloaded, Info.UnloadGroupSize,
				Info.IntraGroupUnloadDelay, Info.InterGroupUnloadDelayMultiplier);

			self.World.AddFrameEndTask(w => w.Add(new DelayedAction(delay, () => EmergencyBailStep(self))));
		}

		/// <summary>Put ONE passenger on the ground now. He takes a free adjacent cell, or the hull's
		/// own cell if none is free — but every candidate is checked for passability first, so nobody
		/// is placed on ground he cannot stand on.
		/// Returns whether a man actually left. False means every remaining passenger is boxed in,
		/// which ends the stagger and leaves the caller's latch open so a transport that was boxed
		/// in, or amphibious and still over water, retries on the next hit instead of stranding the
		/// men it could not place.</summary>
		bool EmergencyBailOut(Actor self)
		{
			if (checkTerrainType && !Info.UnloadTerrainTypes.Contains(self.World.Map.GetTerrainInfo(self.Location).Type))
				return false;

			// Centre position of the hull: the man is placed on his exit cell but
			// drawn at the hull for one frame, so he visually spills out of it.
			var spawn = self.CenterPosition;
			var husk = self.Location;

			// No claimed-subcell bookkeeping here, unlike the unpaced bail this
			// replaced. Steps are at least a tick apart, so each man is genuinely in
			// the world — added by the frame-end task below — before the next one
			// picks, and GetAvailableSubCell sees him for real.
			foreach (var passenger in Passengers.ToList())
			{
				if (passenger.IsDead)
					continue;

				var positionable = passenger.TraitOrDefault<IPositionable>();
				if (positionable == null)
					continue;

				// Adjacent cells first, then the hull's own cell as the hatch-emerge
				// fallback — but the fallback is CHECKED, never assumed. Passing self
				// as ignoreActor lets the hull's cell qualify while the transport is
				// still standing on it, while GetAvailableSubCell still rejects ground
				// the passenger cannot stand on and subcells already taken. Placing
				// blind here put the entire rifle squad of an amphibious m113 or btr
				// onto open water when it was hit mid-river, then handed each man a
				// move order out of a cell his locomotor cannot path from.
				(CPos Cell, SubCell SubCell)? exit = null;
				foreach (var candidate in CurrentAdjacentCells.Shuffle(self.World.SharedRandom).Append(husk))
				{
					var subCellHere = positionable.GetAvailableSubCell(candidate, SubCell.Any, self);
					if (subCellHere == SubCell.Invalid)
						continue;

					exit = (candidate, subCellHere);
					break;
				}

				// Nowhere to put this man: he rides it out rather than being dropped
				// somewhere he cannot stand, and the man behind him is tried instead.
				// If nobody at all can be placed the caller drops its latch, so an
				// amphibious transport that reaches land — or one that is simply boxed
				// in for a moment — retries on the next hit; and if the hull dies
				// first, Killed's EjectOnDeath decides his fate with the same
				// passability check this one mirrors.
				if (exit == null)
					continue;

				// Drop any pre-queued rally point, as the ordered unload does. Nobody
				// walks to a rally point out of a burning vehicle, and leaving the
				// entry behind leaks it for the rest of the transport's life.
				ClearEjectRally(passenger.ActorID);

				Unload(self, passenger);

				var actor = passenger;
				var cell = exit.Value.Cell;
				var subCell = exit.Value.SubCell;

				self.World.AddFrameEndTask(w =>
				{
					if (actor.Disposed)
						return;

					positionable.SetPosition(actor, cell, subCell);
					positionable.SetCenterPosition(actor, spawn);

					actor.CancelActivity();
					w.Add(actor);

					// Get clear of the cookoff. ^CrewedVehicle2/3 detonate a
					// VehicleCookoff on death with a single-tile radius, so standing
					// on the husk is what kills a man who otherwise got out in time.
					var mobile = actor.TraitOrDefault<Mobile>();
					if (mobile != null && !actor.IsDead)
					{
						var dir = BailScatterDirections[w.SharedRandom.Next(BailScatterDirections.Length)];
						var dist = 2 + w.SharedRandom.Next(2);
						actor.QueueActivity(false, mobile.MoveTo(husk + new CVec(dir.X * dist, dir.Y * dist), 0, null, true));
					}

					foreach (var nbm in actor.TraitsImplementing<INotifyBlockingMove>())
						nbm.OnNotifyBlockingMove(actor, actor);
				});

				// One man per step — the caller schedules the next off the shared
				// dismount cadence. Passengers skipped above (dead, or with nowhere to
				// stand) are simply passed over, so one boxed-in man does not hold up
				// the man behind him.
				return true;
			}

			return false;
		}

		void INotifyKilled.Killed(Actor self, AttackInfo e)
		{
			if (Info.EjectOnDeath)
			{
				// KNOWN HAZARD, introduced by the staggered bail and not yet addressed.
				// This loop stops the moment no exit is free, and men the stagger
				// already dropped are standing in the adjacent cells with queued
				// scatter orders. That intermediate state was impossible while the bail
				// was atomic: earlier bailers can now block the exits this loop needs,
				// and whoever is left falls out still in cargo, to be Dispose()d by
				// Disposing with no corpse and no kill credit. It takes a genuine choke
				// — a bridge, a treeline gap — to bite, so it is recorded rather than
				// papered over; fixing it means giving these men the same passability
				// search EmergencyBailOut uses instead of a single CanUnload gate.
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
