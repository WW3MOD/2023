#region Copyright & License Information
/*
 * WW3MOD — recon-drone tasking for the @experimental bot.
 *
 * The user's report: "I want the experimental bot to use drones ... but now they make none."
 * Production is the sibling commit (a UnitFloors/UnitFloorPer entry on both experimental
 * UnitBuilder twins). This module is the other half: having bought a drone operator, the bot has to
 * USE it, and using it correctly is not something any existing module can be pointed at.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.Player)]
	[Desc("WW3MOD experimental AI: flies recon drones from drone operators.",
		"Picks the stalest ground worth looking at from the FOG-LEGAL ControlField staleness field,",
		"parks an operator inside weapon range of it, and force-fires the cell — which is the only",
		"thing that launches a drone, because CarrierMaster.Attacking early-returns on any target",
		"that is not Terrain. Claims its operators through the shared PoiGoalGuard ledger (objective",
		"drone:<id>) so the offence FSM and the ambush module cannot take a 150-cost unarmoured",
		"specialist for assault or ambush work. Gate enable-ai-experimental.")]
	public class DroneOperatorBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Ticks between tasking evaluations. Deliberately slow: a sortie is ~80s end to end",
			"(3s FireDelay + 60s loiter + 9s rearm + 12s burst wait), so evaluating faster buys",
			"nothing and only risks re-ordering a parked operator.")]
		public readonly int ReevaluateInterval = 200;

		[Desc("Actor types treated as drone operators. Named explicitly rather than detected from the",
			"CarrierMaster trait so this module can never adopt some future carrier by accident.")]
		public readonly HashSet<string> OperatorActorTypes = new() { "dr.america", "dr.russia" };

		[Desc("Ticks a ledger commitment is held for. Must exceed ReevaluateInterval by enough that a",
			"commitment cannot lapse between two evaluations — a lapsed claim is an operator the",
			"offence module is free to recruit mid-sortie.")]
		public readonly int CommitmentTicks = 500;

		[Desc("The enforced leash, in CELLS: CarrierSlave.MaxDistance on quadcopterdrone.",
			"NOT CarrierMasterInfo.MaxSlaveDistance, which has no readers engine-wide.")]
		public readonly int LeashCells = 25;

		[Desc("Cells of margin kept inside the leash. The leash check is periodic",
			"(MaxDistanceCheckTicks: 20), so a drone parked exactly on the boundary is one nudge from",
			"being dragged back and granted lost-connection, which zeroes its vision.")]
		public readonly int LeashMarginCells = 3;

		[Desc("Minimum ticks-since-verified before a square is worth a sortie at all.")]
		public readonly int MinStalenessTicks = 500;

		[Desc("Maximum distance from a known POI, in cells. This is the unreachable-corner guard: the",
			"stalest square on a map is usually one nothing can reach.")]
		public readonly int MaxPoiDistanceCells = 40;

		[Desc("Maximum AIR danger tolerated at the cell the drone will hover over. The drone is unarmed",
			"and dies to a single hit from real AA.")]
		public readonly int MaxAirDanger = 100;

		[Desc("Maximum GROUND danger tolerated at the cell the operator stands on to launch.")]
		public readonly int MaxLaunchCellDanger = 60;

		[Desc("Score bonus for a candidate cell adjacent to a believed enemy contact. Prefers ground",
			"someone is actually on over blank map.")]
		public readonly int ContactBonus = 2000;

		[Desc("How near a believed contact counts as 'adjacent', in cells.")]
		public readonly int ContactRadiusCells = 6;

		[Desc("Cells around a believed enemy Supply Route that are refused as observation targets.",
			"The SR is fixed, indestructible and non-capturable, so there is nothing there to learn.")]
		public readonly int SupplyRouteExclusionCells = 8;

		[Desc("Ammo term, per mille — see StarvingRecruitGate. An operator below this is left alone so",
			"AutoSeekSupplies can walk it to a truck.")]
		public readonly int StarvingRecruitThresholdPerMille = 250;

		[Desc("Ticks after issuing a launch order before the module will re-order the same operator.",
			"Must exceed the weapon's FireDelay (50): the spawn is a delayed action owned by the",
			"Armament, so re-ordering inside that window aims the operator at a new cell while the",
			"drone still departs for the old one.")]
		public readonly int LaunchSettleTicks = 75;

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			base.RulesetLoaded(rules, ai);

			// Case-harden actor-name config (see ActorNameCase). Without this the module matches only
			// the exact spelling used here, and `DR.america` — the spelling used throughout
			// infantry-america.yaml — would silently match nothing.
			ActorNameCase.NormalizeInPlace(OperatorActorTypes);
		}

		public override object Create(ActorInitializer init) { return new DroneOperatorBotModule(init.Self, this); }
	}

	public class DroneOperatorBotModule : ConditionalTrait<DroneOperatorBotModuleInfo>, IBotTick
	{
		readonly World world;
		readonly Player player;

		PoiGoalGuard goalGuard;
		bool goalGuardResolved;
		ControlField controlField;
		bool controlFieldResolved;
		DangerFieldLayer dangerField;
		bool dangerFieldResolved;
		BeliefStore beliefStore;
		bool beliefStoreResolved;
		PoiMap poiMap;
		bool poiMapResolved;

		// The ammo term, matching the other POI-family modules.
		readonly StarvingRecruitGate ammoGate = new("drone");

		// Squares already looked at, so the module does not re-pick one hot square every cycle while
		// the rest of the map goes stale. Retired back into the pool once they go stale again.
		readonly HashSet<CPos> covered = new();

		// Operators this module currently holds a ledger claim on, so the claim can be dropped
		// precisely when the operator goes dry or the module shuts down.
		readonly HashSet<Actor> claimed = new();

		// The launch order standing against each operator: which cell it was aimed at and when. Read
		// only through the ordinal operator walk, never enumerated, so its ordering reaches no decision.
		sealed class Sortie
		{
			public CPos OrderedCell;
			public int OrderedTick;
		}

		readonly Dictionary<Actor, Sortie> sorties = new();

		int reevalCountdown;

		public DroneOperatorBotModule(Actor self, DroneOperatorBotModuleInfo info)
			: base(info)
		{
			world = self.World;
			player = self.Owner;
		}

		protected override void TraitEnabled(Actor self)
		{
			// Deterministic initial offset, NOT a LocalRandom draw — a control game that never
			// instantiates this module must keep its random stream untouched.
			reevalCountdown = Info.ReevaluateInterval;
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (player.WinState != WinState.Undefined)
				return;

			// REFRESH THE CLAIM EVERY TICK, EVALUATE ONLY ON THE CADENCE. These are split because a
			// third party can delete this module's claim between two evaluations:
			// StancePositioningExecutor.CommitManagement overwrites the ledger entry with `tacpos:`
			// and ReleaseManagement calls Ledger.Release(self) unconditionally, without ever reading
			// IsCommitted. On a 200-tick evaluation cadence that leaves the operator unclaimed — and
			// so recruitable by the offence FSM — for up to 200 ticks. A dictionary write per tick is
			// far cheaper than that exposure. It does NOT make the claim safe on its own, which is why
			// the executor is also removed from ^DR in YAML; this is the second layer, not the fix.
			RefreshClaims(bot);

			if (--reevalCountdown > 0)
				return;

			reevalCountdown = Info.ReevaluateInterval;
			Reevaluate(bot);
		}

		void RefreshClaims(IBot bot)
		{
			if (goalGuard == null || claimed.Count == 0)
				return;

			var tick = world.WorldTick;
			foreach (var op in claimed)
				if (!op.IsDead && op.IsInWorld)
					goalGuard.Ledger.Commit(op, "drone:" + op.ActorID.ToString(), tick, Info.CommitmentTicks);
		}

		// A disabled module must not leave units committed behind it, or the offence FSM sees an
		// operator that is permanently spoken for by nobody.
		protected override void TraitDisabled(Actor self)
		{
			ReleaseAll();
		}

		void ReleaseAll()
		{
			if (goalGuard != null)
				foreach (var a in claimed)
					goalGuard.Ledger.Release(a);

			claimed.Clear();
			sorties.Clear();
		}

		void Reevaluate(IBot bot)
		{
			if (!goalGuardResolved)
			{
				goalGuard = player.PlayerActor.TraitOrDefault<PoiGoalGuard>();
				goalGuardResolved = true;
			}

			if (!controlFieldResolved)
			{
				controlField = world.WorldActor.TraitOrDefault<ControlField>();
				controlFieldResolved = true;
			}

			if (!dangerFieldResolved)
			{
				dangerField = world.WorldActor.TraitOrDefault<DangerFieldLayer>();
				dangerFieldResolved = true;
			}

			if (!beliefStoreResolved)
			{
				beliefStore = world.WorldActor.TraitOrDefault<BeliefStore>();
				beliefStoreResolved = true;
			}

			if (!poiMapResolved)
			{
				poiMap = world.WorldActor.TraitOrDefault<PoiMap>();
				poiMapResolved = true;
			}

			// Staleness IS the signal; with no ControlField there is nothing fog-legal to steer by and
			// the module must do nothing rather than fall back on an omniscient source.
			// HasField is checked as well as null, and that is not belt-and-braces: with a live trait
			// but no field for this player, TicksSinceVerified returns int.MaxValue for EVERY cell
			// (ControlField.cs:921-928), so the staleness term saturates flat and the choice collapses
			// to contact adjacency and POI distance alone. Bounded and deterministic, but it is no
			// longer staleness-driven, and silently degrading is exactly what this module must not do.
			if (controlField == null || !controlField.HasField(player))
				return;

			var tick = world.WorldTick;

			// Drop claims on operators that died or left. Ordered by ActorID so the walk is
			// deterministic.
			claimed.RemoveWhere(a =>
			{
				if (!a.IsDead && a.IsInWorld)
					return false;

				goalGuard?.Ledger.Release(a);
				sorties.Remove(a);
				return true;
			});

			// A dead operator's standing order dies with it; anything left here would keep a stale cell
			// alive and suppress a re-task for a replacement that reused the reference.
			if (sorties.Count > 0)
				foreach (var a in sorties.Keys.Where(a => a.IsDead || !a.IsInWorld).ToList())
					sorties.Remove(a);

			var operators = world.Actors
				.Where(a => a.Owner == player
					&& !a.IsDead
					&& a.IsInWorld
					&& Info.OperatorActorTypes.Contains(a.Info.Name))
				.OrderBy(a => a.ActorID)
				.ToList();

			if (operators.Count == 0)
			{
				if (claimed.Count > 0)
					ReleaseAll();

				return;
			}

			RetireCoveredSquares();

			// Static contacts are kept: a believed structure is a perfectly good reason to look at the
			// ground around it. The one exception is handled in ChooseTargetCell.
			var contacts = beliefStore != null
				? beliefStore.Contacts(player).ToList()
				: new List<BeliefContact>();

			var pois = poiMap != null
				// suppressOmniscientThreat: the default threat term walks the world unfiltered, which
				// would make this module's target choice depend on units it cannot legally see.
				? poiMap.GetScoredPois(player, true)
				: new List<ScoredPoi>();

			foreach (var op in operators)
				TaskOperator(bot, op, tick, contacts, pois);
		}

		// A square stops being retired once it has gone stale again, so coverage decays rather than
		// permanently shrinking the candidate set.
		void RetireCoveredSquares()
		{
			covered.RemoveWhere(c =>
			{
				var (gx, gy) = controlField.MapCellToGridCell(c);
				return !DroneTaskingMath.IsCovered(controlField.TicksSinceVerified(player, gx, gy), Info.MinStalenessTicks);
			});
		}

		void TaskOperator(IBot bot, Actor op, int tick, List<BeliefContact> contacts, List<ScoredPoi> pois)
		{
			// RELEASE WHEN DRY AND LET THE ENGINE DRIVE. ^DR inherits AutoSeekSupplies with
			// ReturnWhenEmpty, and primary-ammo is its only pool, so an empty operator self-dispatches
			// to the nearest truk/supplycache/logisticscenter. Re-implementing that here would fight
			// it. Holding the claim would be worse: a committed unit is excluded from the modules that
			// would otherwise leave it alone, but the claim does nothing to help it rearm.
			if (ammoGate.Withhold(op, Info.StarvingRecruitThresholdPerMille))
			{
				if (claimed.Remove(op))
					goalGuard?.Ledger.Release(op);

				sorties.Remove(op);
				return;
			}

			// If something else already owns this unit, do not fight over it — but DO take it back
			// once that claim lapses.
			if (goalGuard != null && !claimed.Contains(op)
				&& goalGuard.Ledger.IsCommitted(op, tick))
				return;

			// Re-commit EVERY cycle. Commitments expire, and a lapsed claim is an operator the offence
			// module is free to walk into an assault wave.
			goalGuard?.Ledger.Commit(op, "drone:" + op.ActorID.ToString(), tick, Info.CommitmentTicks);
			claimed.Add(op);

			var carrier = op.TraitOrDefault<CarrierMaster>();
			if (carrier == null)
				return;

			var droneAirborne = carrier.SlaveEntries.Any(e => e.IsLaunched && e.IsValid);

			// A SORTIE IN PROGRESS IS A SORTIE TO LEAVE ALONE. The operator must stay parked for the
			// full 60s loiter: CarrierMaster is PauseOnCondition "moving", and TraitPaused calls
			// SetConnection(false) AND Recall(). Any order that moves it here throws the sortie away.
			if (droneAirborne)
				return;

			var armament = op.TraitsImplementing<Armament>()
				.FirstOrDefault(a => a.Info.Name == "primary");

			if (armament == null)
				return;

			// The armament's own pause state IS the launch precondition ("!loaded || !ammo-primary"),
			// so this cannot drift out of agreement with the YAML gate.
			var armamentReady = !armament.IsTraitDisabled && !armament.IsTraitPaused;
			if (!armamentReady)
				return;

			// AttackBase.ResolveOrder early-returns on this for an unqueued order, so a launch issued
			// here would be silently dropped.
			if (AmmoPool.CannotFight(op))
				return;

			var weaponRangeCells = armament.MaxRange().Length / 1024;
			var maxHover = DroneTaskingMath.MaxHoverDistanceCells(weaponRangeCells, Info.LeashCells, Info.LeashMarginCells);
			if (maxHover <= 0)
				return;

			var opCell = op.Location;
			var target = ChooseTargetCell(opCell, maxHover, contacts, pois);
			if (target == null)
				return;

			var targetCell = target.Value;
			var distance = (targetCell - opCell).Length;

			// NOT op.IsIdle. After the first launch this operator is never idle again — the Attack
			// activity holds indefinitely because ChooseArmamentsForTarget filters IsTraitDisabled but
			// not IsTraitPaused and ^DR does not set AbandonWhenArmamentsPaused, so with the sole
			// armament paused the activity still reports Attacking every tick (Attack.cs:243-256). An
			// idle gate here latched false forever and capped the module at ONE sortie per operator.
			// What actually has to be excluded is a MOVING operator, because `moving` pauses
			// CarrierMaster and Recall()s the drone — so ask that directly.
			var stationary = IsStationary(op);

			if (!DroneTaskingMath.CanLaunch(armamentReady, true, stationary, distance, maxHover))
				return;

			// Re-order only when it changes something. The engine re-fires the held activity by itself
			// each time `loaded` returns, so an untouched operator keeps flying to the same cell — a
			// fixed post rather than a sweep. Ordering a DIFFERENT cell is what moves the post, and an
			// unqueued order clears the held activity on the way in (Actor.QueueActivity(false, …)).
			sorties.TryGetValue(op, out var sortie);
			var sameCell = sortie != null && sortie.OrderedCell == targetCell;
			var sinceOrder = sortie != null ? tick - sortie.OrderedTick : int.MaxValue;

			if (!DroneTaskingMath.ShouldRetask(sortie != null, sameCell, sinceOrder, Info.LaunchSettleTicks))
				return;

			// Do not launch from a cell that is itself hot — the operator is unarmoured.
			if (dangerField != null && dangerField.GroundDanger(player, opCell) > Info.MaxLaunchCellDanger)
				return;

			// THE ONE ORDER THAT LAUNCHES A DRONE, AND WHY IT IS SHAPED LIKE THIS.
			// Target.FromCell gives TargetType.Terrain. CarrierMaster.Attacking opens with
			// `if (target.Type != TargetType.Terrain) return;` — so a force-fire aimed at an ACTOR
			// still fires the weapon (DroneTargeter's ValidTargets match every ground actor), burns
			// the 3s FireDelay, starts the 12s BurstWait, plays the animation and spawns NOTHING.
			// Unqueued deliberately: AttackBase.ResolveOrder's ammo guard is scoped to unqueued
			// orders, and a launch queued behind something else is a launch from an unknown position.
			if (bot.QueueOrder(new Order("ForceAttack", op, Target.FromCell(world, targetCell), false)))
			{
				covered.Add(targetCell);

				if (sortie == null)
					sorties[op] = sortie = new Sortie();

				sortie.OrderedCell = targetCell;
				sortie.OrderedTick = tick;
			}
		}

		// The `moving` condition, read from the same source that grants it:
		// GrantConditionOnMovement matches ValidMovementTypes, which defaults to
		// { Horizontal, Vertical } and deliberately EXCLUDES Turn — so an operator rotating onto its
		// target is stationary for our purposes and does not pause CarrierMaster.
		static bool IsStationary(Actor op)
		{
			var move = op.TraitOrDefault<IMove>();
			if (move == null)
				return true;

			return (move.CurrentMovementTypes & (MovementType.Horizontal | MovementType.Vertical)) == 0;
		}

		CPos? ChooseTargetCell(CPos opCell, int maxHover, List<BeliefContact> contacts, List<ScoredPoi> pois)
		{
			CPos? best = null;
			var bestScore = DroneTaskingMath.Ineligible;

			// Only cells the drone could actually reach and hold are candidates, so the search is a
			// disc around the operator rather than the whole map.
			for (var dy = -maxHover; dy <= maxHover; dy++)
			{
				for (var dx = -maxHover; dx <= maxHover; dx++)
				{
					var cell = opCell + new CVec(dx, dy);
					if ((cell - opCell).Length > maxHover)
						continue;

					if (!world.Map.Contains(cell) || covered.Contains(cell))
						continue;

					// THE SUPPLY ROUTE IS NOT WORTH A SORTIE. It is a fixed, indestructible,
					// non-buildable beachhead: nothing is ever built there and it cannot change hands
					// (SUPPLYROUTE carries no Capturable). Watching it tells the bot only what it
					// already knows, and the ground around it is the enemy's own back line.
					if (NearEnemySupplyRoute(cell, contacts))
						continue;

					var (gx, gy) = controlField.MapCellToGridCell(cell);
					var staleness = controlField.TicksSinceVerified(player, gx, gy);

					var poiDistance = NearestPoiDistance(cell, pois);
					var airDanger = dangerField != null ? dangerField.AirDanger(player, cell) : 0;
					var bonus = NearContact(cell, contacts) ? Info.ContactBonus : 0;

					var score = DroneTaskingMath.ScoreCandidate(
						staleness, Info.MinStalenessTicks,
						poiDistance, Info.MaxPoiDistanceCells,
						airDanger, Info.MaxAirDanger,
						bonus);

					if (score == DroneTaskingMath.Ineligible)
						continue;

					// Strict >, and the scan order is a fixed nested loop, so ties resolve to the same
					// cell on every run rather than depending on iteration order.
					if (score > bestScore)
					{
						bestScore = score;
						best = cell;
					}
				}
			}

			return best;
		}

		int NearestPoiDistance(CPos cell, List<ScoredPoi> pois)
		{
			if (pois.Count == 0)
				return 0;

			var nearest = int.MaxValue;
			foreach (var p in pois)
			{
				var d = (p.Location - cell).Length;
				if (d < nearest)
					nearest = d;
			}

			return nearest;
		}

		bool NearEnemySupplyRoute(CPos cell, List<BeliefContact> contacts)
		{
			foreach (var c in contacts)
				if (c.TypeName == "supplyroute" && (c.Cell - cell).Length <= Info.SupplyRouteExclusionCells)
					return true;

			return false;
		}

		bool NearContact(CPos cell, List<BeliefContact> contacts)
		{
			foreach (var c in contacts)
				if ((c.Cell - cell).Length <= Info.ContactRadiusCells)
					return true;

			return false;
		}
	}
}
