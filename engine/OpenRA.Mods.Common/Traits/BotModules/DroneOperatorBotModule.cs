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

		[Desc("Ticks-since-verified above which a ControlField grid square counts as UNOBSERVED for",
			"scoring. This no longer gates the hover cell (which can never be stale — see",
			"DroneTaskingMath.ScoreCandidate); it decides which squares count toward revealed area.")]
		public readonly int MinStalenessTicks = 500;

		[Desc("The drone's own verifying vision radius in CELLS, used to size the revealed-area box.",
			"28 because ^StandardVision's bands down to strength 2 reach 28c0, and the strength-1 band",
			"(28c0-32c0) does NOT verify: ControlField.GridCellVisible tests IsVisible(cell, 1) and",
			"that comparison is strict (MapLayers.cs:579).")]
		public readonly int DroneVisionCells = 28;

		[Desc("Minimum unobserved grid squares a hover cell must reveal to be worth a 60s sortie.",
			"Counted in COARSE ControlField grid squares, not map cells.")]
		public readonly int MinRevealedSquares = 12;

		[Desc("Maximum distance from a known POI, in cells. This is the unreachable-corner guard: the",
			"stalest square on a map is usually one nothing can reach.")]
		public readonly int MaxPoiDistanceCells = 40;

		[Desc("Maximum AIR danger tolerated at the cell the drone will hover over. The drone is unarmed",
			"and dies to a single hit from real AA.")]
		public readonly int MaxAirDanger = 100;

		[Desc("Maximum GROUND danger tolerated at the cell the operator stands on to launch.")]
		public readonly int MaxLaunchCellDanger = 60;

		[Desc("Worth of a contact we saw and then LOST INTO FOG, in the same unit as revealed area:",
			"coarse grid squares. The strongest tasking signal the module has — a unit that disappeared",
			"is a unit whose current position matters and is not known. PROVISIONAL: no drone had ever",
			"flown when this was set, so it is a reasoned estimate and not a measurement.",
			"MUST BE >= AreaIntelSquares. The lost tier decays from this value DOWN to that floor, so",
			"setting it lower inverts the decay — a freshly-vanished contact would score less than a",
			"two-minute-old one. This is NOT the knob to zero in order to disable the feature: it leaves",
			"the visible and static tiers fully active. Use IntelSampleInterval for that.")]
		public readonly int LostTrackIntelSquares = 250;

		[Desc("Worth of ground where an enemy is believed to be but is NOT an open question — currently",
			"visible, or a contact whose trail has gone cold. Also the floor the lost-track value decays",
			"to, which turns a stale record into a weak area preference rather than into nothing.")]
		public readonly int AreaIntelSquares = 60;

		[Desc("Worth of a believed STATIC contact (structure/defence). Low: it is not going anywhere, so",
			"its position is not the question a sortie would answer.")]
		public readonly int StaticIntelSquares = 20;

		[Desc("Ticks a lost contact is remembered after the last actual sighting, decaying throughout.",
			"MUST EXCEED ONE SORTIE CYCLE (~1333 ticks: 3s FireDelay + 60s loiter + 9s rearm + 12s burst",
			"wait at 16.667 ticks/s). An operator is airborne or rearming for most of its life, so a",
			"shorter horizon expires the record before any operator is free to act on it — which",
			"reproduces the very gap this exists to close.")]
		public readonly int IntelMemoryTicks = 2000;

		[Desc("Age below which a contact counts as still under observation rather than lost. Must span",
			"more than one BeliefStore recompute (25 ticks) or a continuously-watched unit flickers into",
			"the lost tier between samples.")]
		public readonly int FreshSightingTicks = 50;

		[Desc("Ticks between belief-store samples. The store forgets a mobile contact 175 ticks after the",
			"last sighting (7 decay passes of 25 ticks), so this must stay well under that or a vanishing",
			"unit is erased before the module ever records where it was.",
			"THIS IS ALSO THE FEATURE'S OFF SWITCH, and the only single knob that is. Set beyond the",
			"length of a match and the contact table is never written, so every candidate scores",
			"intelSquares 0 and tasking falls back to pure revealed-area staleness — the pre-change",
			"behaviour, up to the old contact bonus that was worth two squares and could not decide",
			"anything. Used as the control arm when A/B-ing this feature.")]
		public readonly int IntelSampleInterval = 25;

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
		// BeliefContact.TypeName is ActorInfo.Name, which is lowercased by the rules parser.
		const string SupplyRouteActorType = "supplyroute";

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

		// WHERE THE ENEMY WAS LAST SEEN, HELD LONGER THAN THE BELIEF STORE HOLDS IT.
		//
		// THIS IS NOT A DUPLICATE OF BeliefStore AND THE REASON IS ARITHMETIC. A mobile contact that
		// goes unobserved decays 100 -> 75 -> 56 -> 42 -> 31 -> 23 -> 17 -> 12 and is dropped below
		// MinConfidence (15) on the 7th unrefreshed pass; passes are UpdateInterval (25) ticks apart, so
		// the store ERASES a vanished mobile 175 ticks after the last sighting. This module evaluates
		// every 200 ticks and can only act when an operator is docked with ammo — a narrow window inside
		// a ~1333-tick sortie cycle. Reading Contacts() at tasking time therefore samples a signal that
		// has usually already expired, which is the "unsatisfiable by construction" shape that cost this
		// module two whole matches once before. The fix is to sample the store on ITS cadence and keep
		// the record on OURS.
		//
		// Widening BeliefStore's own decay was the alternative and is rejected: those constants feed the
		// Stage-B danger fields for every participant, @stable and humans included, so they are not this
		// module's to move.
		sealed class IntelRecord
		{
			public CPos Cell;
			public int LastSeenTick;
			public bool IsStatic;
		}

		readonly Dictionary<uint, IntelRecord> intel = new();
		readonly HashSet<uint> intelPresent = new();
		readonly List<uint> intelDrop = new();

		// One operator's shortlist, rebuilt per operator and SORTED BY KEY so that the argmax below has a
		// fixed iteration order regardless of Dictionary layout — the determinism the influence stack
		// requires, obtained by construction rather than by hoping enumeration is stable.
		readonly struct IntelCandidate
		{
			public readonly uint Key;
			public readonly CPos Cell;
			public readonly int Squares;

			public IntelCandidate(uint key, CPos cell, int squares) { Key = key; Cell = cell; Squares = squares; }
		}

		readonly List<IntelCandidate> nearbyIntel = new();

		// Records already spent this evaluation. Operators are walked in ActorID order, so the second
		// operator cannot pile onto the contact the first one just left for — the "do not send every
		// drone to the same place" rule, enforced without any randomisation.
		readonly HashSet<uint> claimedIntel = new();

		int reevalCountdown;
		int intelCountdown;

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
			intelCountdown = Info.IntelSampleInterval;
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
			// far cheaper than that exposure. It narrows the window rather than closing it, which is
			// why the executor is also gated off for BOT-owned operators in YAML (^DR narrows its
			// RequiresCondition to the human token); this is the second layer, not the fix. The hazard
			// is general, not drone-specific: GoalGuardLedger.Release is keyed on the ACTOR, not the
			// objective, so it deletes whatever claim the actor holds regardless of who wrote it.
			ResolveTraits();
			RefreshClaims();

			// SAMPLING RUNS ON ITS OWN CADENCE AND ABOVE THE EVALUATION RETURN, DELIBERATELY. The belief
			// store forgets a vanished mobile in 175 ticks; the evaluation cadence is 200. Sampling from
			// inside Reevaluate would therefore miss the majority of vanish events outright — and any
			// early return placed above this would silently take the memory with it, which is exactly
			// how a previous cost gate here took out the ledger claim and the diagnostics along with the
			// work it meant to skip.
			if (--intelCountdown <= 0)
			{
				intelCountdown = Info.IntelSampleInterval;
				SampleIntel();
			}

			if (--reevalCountdown > 0)
				return;

			reevalCountdown = Info.ReevaluateInterval;
			Reevaluate(bot);
		}

		// SNAPSHOT THE BELIEF STORE, AGE ON OUR OWN CLOCK.
		//
		// Fog legality is inherited rather than re-derived: every field copied here came from
		// BeliefStore, which is built strictly from the player's own vision and FrozenActorLayer.
		// Remembering a legally-obtained sighting for longer is still the commander's own memory — no
		// ground truth is read at any point.
		//
		// LastSeenTick is copied rather than stamped with "now", so age is measured from the actual
		// sighting and survives the store dropping the contact: the record does not restart its clock
		// at the moment we notice it is gone.
		void SampleIntel()
		{
			if (beliefStore == null)
				return;

			var tick = world.WorldTick;
			intelPresent.Clear();

			foreach (var c in beliefStore.Contacts(player))
			{
				// The enemy SR is fixed, indestructible and non-capturable, so it is never worth a
				// sortie. Excluded at the source so it cannot pull a candidate from vision range either —
				// the cell-radius refusal further down only covers cells close to it.
				if (c.TypeName == SupplyRouteActorType)
					continue;

				intelPresent.Add(c.Key);

				if (!intel.TryGetValue(c.Key, out var r))
					intel[c.Key] = r = new IntelRecord();

				r.Cell = c.Cell;
				r.LastSeenTick = c.LastSeenTick;
				r.IsStatic = c.IsStatic;
			}

			intelDrop.Clear();
			foreach (var kv in intel)
			{
				if (intelPresent.Contains(kv.Key))
					continue;

				// Past the horizon the cell no longer locates anything — see IntelSquares.
				if (tick - kv.Value.LastSeenTick >= Info.IntelMemoryTicks)
				{
					intelDrop.Add(kv.Key);
					continue;
				}

				// VERIFIED-CLEAR, mirroring BeliefStore.ResolveUnobserved. The contact is no longer in
				// the store and we can currently see the cell, so it is not there: it died, or it moved
				// and was re-observed elsewhere (which updates this record under the same key). Either
				// way the memory is spent, and without this a drone would keep being drawn back to
				// ground it has already disproved. The `1` threshold is strict and matches the store's.
				if (player.MapLayers != null && player.MapLayers.IsVisible(kv.Value.Cell, 1))
					intelDrop.Add(kv.Key);
			}

			// Enumeration above only COLLECTS keys and the removal is set-based, so the resulting table
			// does not depend on Dictionary iteration order.
			foreach (var key in intelDrop)
				intel.Remove(key);
		}

		void RefreshClaims()
		{
			if (goalGuard == null || claimed.Count == 0)
				return;

			var tick = world.WorldTick;
			evalTick = tick;
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

			// A disabled module holding a contact table is leaked state; ages would be meaningless by
			// the time it came back anyway.
			intel.Clear();
			claimedIntel.Clear();
			nearbyIntel.Clear();
		}

		// Hoisted out of Reevaluate because the intel sampler needs the belief store on a faster cadence
		// than the evaluation runs on. Each resolution is still one-shot and order-independent.
		void ResolveTraits()
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
		}

		void Reevaluate(IBot bot)
		{
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

				// THE LINE THAT SEPARATES TWO HYPOTHESES WITH ONE SYMPTOM. "Every sortie is
				// retask=first" is compatible with "the retask path is broken" and with "the retask
				// path works but no operator ever lives long enough to use it", and the ordinary log
				// cannot tell them apart: an operator with an airborne drone is silent at BOTH other
				// log sites, so a short-lived one leaves nothing behind but its launch. Recording the
				// removal of an operator that still had a standing order closes that gap permanently,
				// and costs one line on a path that runs at most twice per match.
				if (sorties.TryGetValue(a, out var lost))
					Log.Write("debug",
						$"[drone] player={player.PlayerName} op={a.ActorID} operator-lost "
						+ $"dead={a.IsDead} inworld={a.IsInWorld} standingcell={lost.OrderedCell.X},{lost.OrderedCell.Y} "
						+ $"orderedtick={lost.OrderedTick} tick={tick}");

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

			// TaskOperator runs for EVERY operator, unconditionally. An earlier cut gated this on the
			// summed-area table having been built, which looked like a pure cost saving and was not:
			// the work ABOVE the launch check in TaskOperator — releasing a dry operator's claim,
			// clearing its sortie, and above all re-committing the ledger every cycle — is not
			// optional. CanLaunchNow is false precisely while a drone is airborne, i.e. the SUCCESS
			// case, so gating here skipped the claim refresh for the whole ~1000-tick sortie against a
			// 500-tick CommitmentTicks: the claim lapsed mid-flight and the offence module was free to
			// walk the operator away, which recalls the drone. With both operators airborne — the
			// intended steady state at UnitLimits 2 — nothing ran at all and the diagnostic log went
			// silent too. The table is now built lazily at first use inside ChooseTargetCell, which
			// keeps the saving without coupling it to anything else.
			satValidTick = -1;

			// Per evaluation: last cycle's claims must not suppress this cycle's choices.
			claimedIntel.Clear();

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

		// THE SINGLE DEFINITION OF "this operator could launch right now", used by BOTH the
		// table-build pre-check and the tasking path itself.
		//
		// It exists as a helper rather than as two agreeing copies because the copies WOULD drift: the
		// next person editing the launch preconditions has no reason to know a second mirror of them
		// governs whether the summed-area table gets built, and a comment saying so is not a
		// countermeasure — that failure has already happened three times in this codebase.
		//
		// Returns the resolved carrier and armament so the caller does not look them up twice.
		static bool CanLaunchNow(Actor op, out CarrierMaster carrier, out Armament armament)
		{
			armament = null;
			carrier = op.TraitOrDefault<CarrierMaster>();
			if (carrier == null)
				return false;

			// A sortie in progress is a sortie to leave alone: the operator must stay parked for the
			// full loiter, and the retarget branch is unreachable for ^DR anyway.
			if (carrier.SlaveEntries.Any(e => e.IsLaunched && e.IsValid))
				return false;

			armament = op.TraitsImplementing<Armament>().FirstOrDefault(a => a.Info.Name == "primary");
			if (armament == null)
				return false;

			// The armament's own pause state IS the launch precondition ("!loaded || !ammo-primary"),
			// so this cannot drift out of agreement with the YAML gate. It also catches the state that
			// looks like success: after a kill the quadcopter respawns and re-grants `loaded`, but
			// ammo-primary is 0, so the operator visibly has a drone it cannot launch.
			return !armament.IsTraitDisabled && !armament.IsTraitPaused;
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

			// Same predicate the table-build pre-check uses — see CanLaunchNow. A sortie in progress is
			// left strictly alone: the operator must stay parked for the full 60s loiter, because
			// CarrierMaster is PauseOnCondition "moving" and TraitPaused calls SetConnection(false)
			// AND Recall(), so any order that moves it here throws the sortie away.
			if (!CanLaunchNow(op, out _, out var armament))
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
			BuildNearbyIntel(opCell, maxHover, tick);
			var target = ChooseTargetCell(opCell, maxHover, contacts, pois);

			// TWO DIFFERENT BUGS SHARE ONE SYMPTOM — "no second sortie" — and the live match cannot
			// tell them apart without this. Either no cell was eligible at all (every square in the
			// disc fresher than MinStalenessTicks, or beyond MaxPoiDistanceCells from any POI, or over
			// MaxAirDanger), or a cell WAS chosen and ShouldRetask declined to re-order it. The first
			// is the one to suspect: a suppression cannot cause a missing second sortie, because at
			// the first evaluation inside the docked window the drone has just spent the whole loiter
			// on the previous cell, so that cell is far fresher than MinStalenessTicks and stays
			// retired — it cannot win the argmax and so cannot be the cell that gets suppressed.
			// Bounded output by construction: at most UnitLimits (2) lines per ReevaluateInterval.
			if (target == null)
			{
				// WHICH gate refused, not merely that one did. Without this split, "no eligible cell"
				// is compatible with three different defects that need three different fixes.
				Log.Write("debug",
					$"[drone] player={player.PlayerName} op={op.ActorID} no-eligible-cell "
					+ $"hover={maxHover} scored={considered} reveal={refusedReveal} poi={refusedPoi} "
					+ $"danger={refusedDanger} sr={refusedSr} covered={refusedCovered} "
					+ $"offmap={refusedOffMap} bestreveal={bestReveal} minreveal={Info.MinRevealedSquares} "
					+ $"minstale={Info.MinStalenessTicks} "
					// Intel side of the same question: were there records to steer by at all, and did any
					// of them reach a candidate? records=0 with contacts on the map means the sampler or
					// the reach filter is wrong; records>0 with bestintel=0 means they were all further
					// than DroneVisionCells from every candidate.
					+ $"records={intel.Count} nearby={nearbyIntel.Count} bestintel={bestIntel} "
					+ $"border={bestBorder} tick={tick}");

				return;
			}

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

			// armamentReady and noDroneAirborne are both already established by CanLaunchNow above; the
			// pure function still takes them because it is the tested contract for "may we launch now",
			// and collapsing it to the terms this one call site has left would untest the other two.
			if (!DroneTaskingMath.CanLaunch(true, true, stationary, distance, maxHover))
				return;

			// Re-order only when it changes something. The engine re-fires the held activity by itself
			// each time `loaded` returns, so an untouched operator keeps flying to the same cell — a
			// fixed post rather than a sweep. Ordering a DIFFERENT cell is what moves the post, and an
			// unqueued order clears the held activity on the way in (Actor.QueueActivity(false, …)).
			// THIS IS A LATCH, AND LATCHES ARE WHAT FIX 1 WAS ABOUT — so its limit is written down.
			// `sorties[op]` is cleared only when the operator dies/leaves or goes dry. If a third party
			// destroys the Attack activity without either happening — a "Stop" from elsewhere, a human
			// taking control, or ControlAllUnitsManager skipping the order at ModularBot.cs:259-260
			// AFTER QueueOrder already returned true — this module still believes its old cell is a
			// standing order. It cannot cause a MISSING second sortie (that cell is retired in `covered`
			// and cannot win the argmax while it is fresh), but once it retires and later re-wins,
			// sameCell would suppress a re-order for an order that is not actually running. The window
			// is narrow now that the positioning layer is gated off for bot-owned ^DR, and left as a
			// known latch rather than fixed with an activity-liveness check bolted on late.
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
				// THE SETTLING OBSERVATION. The one thing that decides whether the retask path works is
				// the number of DISTINCT target cells issued per operator over a match: one means the
				// module launched once and the engine re-flew that cell forever; two or more means
				// re-tasking works. That cannot be read from a unit test — the defect this replaced
				// lived between the pure math and the static order chain, invisible to both — and it
				// cannot be read from the null-case line below either, so the issue side is logged too.
				// Count distinct `cell=` values per `op=` to settle it.
				// opcell IS A DISCRIMINATOR, NOT DECORATION. Measured clustering — several operators
				// launching into the same narrow lane — has two candidate causes that produce an
				// identical signature and want opposite fixes:
				//   (a) INSUFFICIENT DISCOUNTING: `covered` retires only the exact flown cell, and an
				//       adjacent grid centre is 2 cells away against 28-cell vision, so the next
				//       operator scores near-identical revealed area and re-picks the same ground.
				//   (b) GEOMETRIC CONSTRAINT: operators spawn at the Supply Route, which is a fixed
				//       beachhead near a map edge, so ~half of every candidate disc is clamped
				//       off-grid (measured: 42-53%) and only one direction offers unclamped unobserved
				//       ground. Every operator would then pick the same lane INDEPENDENTLY.
				// Logging where the operator itself stood separates them: different operator positions
				// converging on one lane favours (b); operators sitting near each other leaves the two
				// confounded, which is a result to report rather than a coin to flip.
				// intel/intelkey ARE THE VERDICT LINE FOR THIS FEATURE. "Drones prefer contacts" and
				// "drones fly somewhere and I told a story about why" are the same trace without them:
				// intel>0 with a key names the specific believed contact that won the cell, so a
				// positional trace can be checked against the record that caused it rather than against
				// an intention. intel=0 on every launch means the module is still purely exploring.
				//
				// artefact IS THE CALIBRATION LINE, and it is here because the exploration term it
				// competes with is inflated by a known amount. ControlField's grid spans Map.MapSize
				// while Map.Contains tests the smaller playable Bounds, and TicksSinceVerified returns
				// int.MaxValue for any square never verified — so the non-playable border counts as
				// permanently unobserved and is summed into `reveal` as ground the drone will never
				// actually reveal. Operators launch from the Supply Route, a fixed beachhead near a map
				// edge, so this lands hardest exactly where it is used. reveal minus artefact is the
				// real exploration signal, and it is the number LostTrackIntelSquares must be judged
				// against — not `reveal` itself.
				Log.Write("debug",
					$"[drone] player={player.PlayerName} op={op.ActorID} launch cell={targetCell.X},{targetCell.Y} "
					+ $"opcell={opCell.X},{opCell.Y} dist={distance} clamped={refusedOffMap} "
					+ $"reveal={bestReveal} border={bestBorder} intel={chosenIntel} "
					+ $"intelkey={(chosenIntel > 0 ? chosenIntelKey.ToString() : "none")} "
					+ $"bestintel={bestIntel} "
					+ $"bestintelcell={(bestIntel > 0 ? $"{bestIntelCell.X},{bestIntelCell.Y}" : "none")} "
					+ $"bestintelreveal={bestIntelReveal} "
					+ $"records={intel.Count} nearby={nearbyIntel.Count} "
					+ $"retask={(sortie != null ? "yes" : "first")} tick={tick}");

				// Spend the record that won this cell so the next operator in the ActorID walk has to
				// find its own contact. Claimed only on a SUCCESSFUL order: a refused launch leaves the
				// contact available, because nobody is going.
				if (chosenIntel > 0)
					claimedIntel.Add(chosenIntelKey);

				covered.Add(targetCell);

				if (sortie == null)
					sorties[op] = sortie = new Sortie();

				sortie.OrderedCell = targetCell;
				sortie.OrderedTick = tick;
			}
		}

		// Reads the same INPUT as the `moving` condition, with deliberately different semantics —
		// this is NOT a re-implementation of that condition, and describing it as one would be wrong.
		// GrantConditionOnMovement tests Info.ValidMovementTypes.Contains(types) — SET MEMBERSHIP on
		// the whole flags value (GrantConditionOnMovement.cs:65) — so with the default
		// { Horizontal, Vertical } it grants `moving` only when CurrentMovementTypes is EXACTLY
		// Horizontal or EXACTLY Vertical. Mobile.UpdateMovement ORs Turn in whenever facing changed on
		// the same tick (Mobile.cs:383-395), so a walking-and-turning operator is Horizontal|Turn,
		// which is NOT in that set and therefore does NOT grant `moving`.
		// The bitmask below calls that case moving; the condition does not. THE DIVERGENCE IS ONE-WAY
		// AND DELIBERATELY ON THE SAFE SIDE: we occasionally decline a launch window that would in fact
		// have been legal, and we never launch into a state where CarrierMaster is about to pause and
		// Recall(). A missed window costs one evaluation cycle; a launch into a recall costs the
		// sortie. Turn-only (a stationary operator rotating onto its target) is correctly reported
		// stationary by both.
		static bool IsStationary(Actor op)
		{
			var move = op.TraitOrDefault<IMove>();
			if (move == null)
				return true;

			return (move.CurrentMovementTypes & (MovementType.Horizontal | MovementType.Vertical)) == 0;
		}

		// Per-gate refusal tally for the last ChooseTargetCell call. Diagnostic only.
		int refusedReveal, refusedPoi, refusedDanger, refusedSr, refusedCovered, refusedOffMap, considered;

		// Best revealed-area seen this scan, whether or not it cleared the threshold. THIS IS THE LINE
		// THAT TELLS "threshold too high" FROM "still broken" WITHOUT A SECOND MATCH: if a scan ends with
		// no launch and bestReveal is 0, nothing was revealable and the model is still wrong; if
		// bestReveal sits just under MinRevealedSquares, the threshold is the only thing in the way.
		int bestReveal;

		// Best intel value seen anywhere this scan, and the value/record that won the CHOSEN cell.
		// bestIntel answers "was there anything to steer by"; chosenIntel answers "did it decide this".
		int bestIntel;
		int chosenIntel;
		uint chosenIntelKey;

		// WHERE the best intel sat, and what exploration was on offer THERE. This pair is what sizes
		// LostTrackIntelSquares, and without it the question cannot be answered from a match at all.
		// `reveal` on the launch line is bestReveal — the maximum anywhere in the disc — NOT the
		// winner's own revealed area, so a launch that does not go to the best-intel cell says only
		// that intel lost, never by how much. The deficit it would have had to cover is exactly
		// bestReveal - bestIntelReveal; its displacement power is bestIntel minus the intel at the
		// reveal argmax. Both become readable off one launch line, so the constant can be DERIVED
		// rather than tuned upward until the scenario passes. Diagnostic only — no decision reads it.
		int bestIntelReveal;
		CPos bestIntelCell;

		// Non-playable border squares inside the winning candidate's vision box — diagnostic only, and
		// the amount by which bestReveal overstates real ground. See the launch log.
		int bestBorder;

		// Inclusive-prefix-sum table over the control grid: staleSat[x+1,y+1] is the number of
		// unobserved grid squares in the rectangle (0,0)-(x,y). Rebuilt once per evaluation and shared
		// by every operator, which is what keeps the revealed-area score affordable — see BuildStaleSat.
		int[,] staleSat;
		int satGw, satGh;

		// Diagnostic twin of staleSat over non-playable squares. Read only by the log lines.
		int[,] borderSat;

		// Evaluation tick the table was last built for; -1 forces a rebuild on the next use.
		int satValidTick = -1;
		int evalTick;

		// COST, IN THE SAME TERMS THE PREVIOUS MODEL WAS JUSTIFIED IN.
		// Old model: ~1520 map cells scored per operator per evaluation, one O(1) staleness read each.
		// The obvious form of the new model — count unobserved squares inside the drone's vision for
		// each candidate — is ~1520 candidates x ~615 squares = ~935,000 reads per operator, which is
		// correct and unshippable. Two changes bring it back under the old cost:
		//   1. CANDIDATES AT GRID RESOLUTION. A hover cell only needs to be distinct to the control
		//      grid, so iterate grid squares rather than map cells: ~1520/CellSize^2 = ~380 candidates.
		//   2. SUMMED-AREA TABLE. Built once per evaluation over the whole grid (gridW x gridH, e.g.
		//      ~4,096 squares on a 128x128 map at CellSize 2) and shared by all operators, after which
		//      each candidate's revealed count is FOUR array reads regardless of the drone's vision
		//      radius.
		// Net per evaluation: O(gridW x gridH) once, plus ~380 O(1) queries per operator. For the two
		// operators this module may own, ~4,100 + 760 against the old ~3,040 — the same order, now
		// independent of DroneVisionCells, and it replaces a scan that produced nothing at all.
		// The table is skipped entirely when no operator is launch-ready.
		void BuildStaleSat()
		{
			var gw = controlField.GridWidth;
			var gh = controlField.GridHeight;
			if (staleSat == null || satGw != gw || satGh != gh)
			{
				staleSat = new int[gw + 1, gh + 1];
				satGw = gw;
				satGh = gh;
			}

			// Reusing the array across evaluations is safe without clearing: every interior entry is
			// overwritten below, and the zero border is never written at all.
			DroneTaskingMath.BuildSummedArea(staleSat, gw, gh,
				(gx, gy) => controlField.TicksSinceVerified(player, gx, gy) >= Info.MinStalenessTicks);

			// DIAGNOSTIC TWIN — NO DECISION READS THIS. It measures how much of the table above is the
			// non-playable border rather than real unobserved ground, which is the correction the
			// exploration term needs before it can be compared against the intel term. Deliberately NOT
			// subtracted from the live score: that would be a second behavioural change riding along
			// unmeasured with this one. Measure first, then decide, in that order.
			if (borderSat == null || borderSat.GetLength(0) != gw + 1 || borderSat.GetLength(1) != gh + 1)
				borderSat = new int[gw + 1, gh + 1];

			DroneTaskingMath.BuildSummedArea(borderSat, gw, gh,
				(gx, gy) => !world.Map.Contains(controlField.GridCellToMapCell(gx, gy)));
		}

		CPos? ChooseTargetCell(CPos opCell, int maxHover, List<BeliefContact> contacts, List<ScoredPoi> pois)
		{
			// Lazy, once per evaluation, shared by every operator that reaches this point.
			if (satValidTick != evalTick)
			{
				BuildStaleSat();
				satValidTick = evalTick;
			}

			CPos? best = null;
			var bestScore = DroneTaskingMath.Ineligible;
			refusedReveal = refusedPoi = refusedDanger = refusedSr = refusedCovered = refusedOffMap = considered = 0;
			bestReveal = 0;
			bestIntel = 0;
			bestIntelReveal = 0;
			bestIntelCell = CPos.Zero;
			chosenIntel = 0;
			chosenIntelKey = 0;
			bestBorder = 0;

			// Candidates are GRID squares, converted to their centre map cell only to issue the order.
			// GridCellToMapCell is deliberately not the inverse of MapCellToGridCell, so the centre is
			// used as the representative cell rather than round-tripped.
			var lo = controlField.MapCellToGridCell(new CPos(opCell.X - maxHover, opCell.Y - maxHover));
			var hi = controlField.MapCellToGridCell(new CPos(opCell.X + maxHover, opCell.Y + maxHover));

			for (var gx = lo.X; gx <= hi.X; gx++)
			{
				for (var gy = lo.Y; gy <= hi.Y; gy++)
				{
					var cell = controlField.GridCellToMapCell(gx, gy);

					// The disc, not the bounding box.
					if ((cell - opCell).Length > maxHover)
						continue;

					if (!world.Map.Contains(cell))
					{
						refusedOffMap++;
						continue;
					}

					if (covered.Contains(cell))
					{
						refusedCovered++;
						continue;
					}

					// THE SUPPLY ROUTE IS NOT WORTH A SORTIE. It is a fixed, indestructible,
					// non-buildable beachhead: nothing is ever built there and it cannot change hands
					// (SUPPLYROUTE carries no Capturable). Belief-gated, so it cannot fire before the
					// enemy SR has actually been discovered.
					if (NearEnemySupplyRoute(cell, contacts))
					{
						refusedSr++;
						continue;
					}

					// What this hover cell would BUY: unobserved squares inside the drone's own vision
					// once it is parked there. Ground already inside the operator's bubble is not stale
					// and so contributes nothing, which is why no explicit exclusion is needed.
					var vlo = controlField.MapCellToGridCell(new CPos(cell.X - Info.DroneVisionCells, cell.Y - Info.DroneVisionCells));
					var vhi = controlField.MapCellToGridCell(new CPos(cell.X + Info.DroneVisionCells, cell.Y + Info.DroneVisionCells));
					var revealed = DroneTaskingMath.SumInclusive(staleSat, satGw, satGh, vlo.X, vlo.Y, vhi.X, vhi.Y);

					if (revealed > bestReveal)
						bestReveal = revealed;

					var poiDistance = NearestPoiDistance(cell, pois);
					var airDanger = dangerField != null ? dangerField.AirDanger(player, cell) : 0;
					var intelSquares = BestIntelAt(cell, out var intelKey);

					if (intelSquares > bestIntel)
					{
						bestIntel = intelSquares;
						bestIntelReveal = revealed;
						bestIntelCell = cell;
					}

					considered++;
					var score = DroneTaskingMath.ScoreCandidate(
						revealed, intelSquares, Info.MinRevealedSquares,
						poiDistance, Info.MaxPoiDistanceCells,
						airDanger, Info.MaxAirDanger,
						out var refusal);

					if (score == DroneTaskingMath.Ineligible)
					{
						switch (refusal)
						{
							case DroneRefusal.TooLittleRevealed: refusedReveal++; break;
							case DroneRefusal.TooFarFromPoi: refusedPoi++; break;
							case DroneRefusal.TooDangerous: refusedDanger++; break;
						}

						continue;
					}

					// Strict >, and the scan order is a fixed nested loop, so ties resolve to the same
					// cell on every run rather than depending on iteration order.
					if (score > bestScore)
					{
						bestScore = score;
						best = cell;
						chosenIntel = intelSquares;
						chosenIntelKey = intelKey;
						bestBorder = DroneTaskingMath.SumInclusive(borderSat, satGw, satGh, vlo.X, vlo.Y, vhi.X, vhi.Y);
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
				if (c.TypeName == SupplyRouteActorType && (c.Cell - cell).Length <= Info.SupplyRouteExclusionCells)
					return true;

			return false;
		}

		// Shortlist the records that could reach ANY candidate for this operator, once, before the
		// ~380-candidate scan runs. Without it every candidate would walk the whole table; with it the
		// scan walks a handful. The reach bound is exact rather than generous: a candidate sits at most
		// maxHover from the operator and a record contributes out to DroneVisionCells from a candidate,
		// so nothing beyond the sum can matter.
		void BuildNearbyIntel(CPos opCell, int maxHover, int tick)
		{
			nearbyIntel.Clear();
			var reach = maxHover + Info.DroneVisionCells;

			foreach (var kv in intel)
			{
				// Already spent by an earlier operator this evaluation.
				if (claimedIntel.Contains(kv.Key))
					continue;

				var r = kv.Value;
				if ((r.Cell - opCell).Length > reach)
					continue;

				var squares = DroneTaskingMath.IntelSquares(
					tick - r.LastSeenTick, r.IsStatic,
					Info.LostTrackIntelSquares, Info.AreaIntelSquares, Info.StaticIntelSquares,
					Info.FreshSightingTicks, Info.IntelMemoryTicks);

				if (squares <= 0)
					continue;

				nearbyIntel.Add(new IntelCandidate(kv.Key, r.Cell, squares));
			}

			// THE DETERMINISM STEP. Dictionary enumeration order is not a contract, so the list is put
			// into ActorID order here; every downstream read walks it in that order and the argmax's
			// tie-break therefore resolves the same way on every run and every replay. Sorting a
			// shortlist once per operator is cheaper than defending the invariant anywhere else.
			nearbyIntel.Sort((a, b) => a.Key.CompareTo(b.Key));
		}

		// The single best believed contact this hover cell would look at. MAX, NOT SUM, deliberately:
		// summing would let a cluster of five fading records outbid any amount of exploration and would
		// silently make the tuning far more aggressive than the one configured number claims. The value
		// of a cell is the best question it answers, not the number of questions near it.
		int BestIntelAt(CPos cell, out uint key)
		{
			key = 0;
			var best = 0;

			foreach (var ic in nearbyIntel)
			{
				var value = DroneTaskingMath.IntelFalloff(ic.Squares, (ic.Cell - cell).Length, Info.DroneVisionCells);
				if (value > best)
				{
					best = value;
					key = ic.Key;
				}
			}

			return best;
		}
	}
}
