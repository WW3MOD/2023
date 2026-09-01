#region Copyright & License Information
/*
 * WW3MOD — E6 combat-engineer employment for the @experimental bot.
 *
 * The gap this closes: the bot BUYS engineers and then parks them. e6 carries UnitsToBuild 20,
 * UnitTargetShares 8 and UnitLimits 2 on both faction UnitBuilder twins, but ^E6 also carries
 * `AIUnitRole: Role: Logistics`, and every free-pool module sets UseUnitRoles: true — so the role
 * filter excludes him from assault, ambush, garrison and line duty alike. The single thing that ever
 * moved an engineer before this module was EngineerRouteOpenBotModule's bridge trigger, which fires
 * only when a repairable crossing happens to sit in the believed-weakest enemy sector. Everywhere
 * else he stands at the Supply Route for the whole match holding three C4 charges, a repair
 * armament and a mine detector.
 *
 * WHY A MODULE AND NOT A YAML EDIT. Two shortcuts look available and neither works. Removing e6 from
 * LayeredDefence's ExcludedActorTypes does nothing, because that instance sets UseUnitRoles: true and
 * the exclusion list is inert under it. Changing the AIUnitRole would work — by pushing a 250-cost
 * unarmoured specialist onto the assault line, which is not a use of an engineer, it is a way to lose
 * one. The employments below are also not orders any existing module knows how to issue: "C4" is a
 * Demolition order with an Enter-derived activity behind it, and the repair and mine armaments need
 * the engineer PARKED within one cell of the thing he is servicing.
 *
 * FOG LEGALITY IS THIS MODULE'S OWN BURDEN AND NOTHING DOWNSTREAM SHARES IT. Breach targets are read
 * from BeliefStore.Contacts(player) — never world.Actors — but the engine will not enforce that:
 * TargetExtensions.Recalculate returns an Actor target UNMODIFIED with targetIsHiddenActor false
 * whenever viewer.IsBot, so a bot may freely demolish something it has never seen. See
 * EngineerTaskingMath.IsBreachViable for why the freshness window is the guard rather than a knob.
 *
 * @stable: there is NO @stable twin. A wholly new module instanced only under
 * enable-ai-experimental cannot move the benchmark control, which sidesteps the shared-trait
 * default-to-baseline rule entirely (DOCS/reference/architecture.md §"Adding a behavioural field to a
 * trait shared by both bot profiles"). Zero RNG: the evaluation stagger is a deterministic countdown
 * and every walk over a dictionary is either order-free or sorted by ActorID before it decides
 * anything.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.Player)]
	[Desc("WW3MOD experimental AI: employs E6 combat engineers, which the role filter otherwise leaves",
		"parked at the Supply Route for the whole match. Three employments in a fixed priority — plant C4",
		"on a believed enemy static our own troops are already engaged against, park next to damaged",
		"friendly armour so Armament@Repair auto-targets it, or screen the forward group so the mine",
		"detector and Armament@ClearMines cover its advance. Claims its engineers through the shared",
		"PoiGoalGuard ledger (objective engineer:<id>) and YIELDS to an existing claim, so",
		"EngineerRouteOpenBotModule's bridge repair always wins. Gate enable-ai-experimental.")]
	public class EngineerOperatorBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Ticks between employment evaluations. Faster than the drone module's because an engineer",
			"has no sortie cycle to respect — the cost of evaluating is a scan, and the cost of being",
			"late is a damaged vehicle that drove away. Re-ordering is separately damped by",
			"OrderSettleTicks, so this does not control how often he is actually disturbed.")]
		public readonly int ReevaluateInterval = 100;

		[Desc("Actor types treated as combat engineers. Named explicitly rather than detected from the",
			"Demolition trait so this module can never adopt some future demolition unit by accident —",
			"^SF also carries Demolition and is a 600-cost infiltrator that must not be spent this way.")]
		public readonly HashSet<string> OperatorActorTypes = new() { "e6.america", "e6.russia" };

		[Desc("Ticks a ledger commitment is held for. Must exceed ReevaluateInterval by enough that a",
			"commitment cannot lapse between two evaluations — a lapsed claim is an engineer another",
			"module is free to recruit mid-walk.")]
		public readonly int CommitmentTicks = 300;

		[Desc("Ticks a standing order is held before this module will replace it, even when a better job",
			"has appeared. Every order here is unqueued and an unqueued order CANCELS the current",
			"activity, so a shorter window throws away a Demolish walk without spending the charge and",
			"the module simply re-walks — see EngineerTaskingMath.ShouldRetask.")]
		public readonly int OrderSettleTicks = 200;

		[Desc("The ammo pool the C4 charges live in. ^E6 spends these through the Demolition trait's",
			"UseAmmo rather than through any Armament, so an armament pause state cannot answer",
			"'has he got a charge left' and the pool must be read directly.")]
		public readonly string DemolitionAmmoPoolName = "secondary-ammo";

		[Desc("Furthest a believed enemy static may be, in cells, and still be worth walking an engineer",
			"to. He moves at infantry speed through contested ground with no armour.")]
		public readonly int BreachMaxDistanceCells = 30;

		[Desc("How many of OUR OWN units must be near a believed static before it counts as something an",
			"axis is stalled against. This is the legally-measurable stand-in for 'stalled': a defence",
			"with none of our troops near it is not blocking anything yet.")]
		public readonly int BreachMinFriendlyNearby = 2;

		[Desc("Radius in cells over which the friendly-pressure term above is counted.")]
		public readonly int BreachFriendlyRadiusCells = 8;

		[Desc("Maximum age, in ticks, of the belief sighting that authorises a breach. THIS IS THE FOG",
			"GUARD, NOT A TUNING KNOB — read EngineerTaskingMath.IsBreachViable before changing it.",
			"Must span more than one BeliefStore recompute (25 ticks) or a continuously-watched",
			"structure flickers out of eligibility between passes.")]
		public readonly int BreachContactFreshnessTicks = 50;

		[Desc("Furthest a damaged friendly may be, in cells, and still pull an engineer to it.")]
		public readonly int RepairMaxDistanceCells = 25;

		[Desc("Cells from our own Supply Route inside which a friendly unit does NOT count toward the",
			"screen anchor. The SR is where everything spawns, so without this the anchor is always the",
			"beachhead and the engineer never leaves it — which is the exact behaviour this module",
			"exists to end.")]
		public readonly int ScreenHomeExclusionCells = 12;

		[Desc("Cells the screen or repair anchor must move before the engineer is re-ordered to follow.",
			"Damping, not precision: re-ordering on every centroid twitch cancels the repair armament's",
			"auto-acquired target every cycle, so the group under fire is the one whose engineer never",
			"completes a repair burst.")]
		public readonly int AnchorShiftCells = 4;

		[Desc("Actor types of the bot's own Supply Route, used for the screen exclusion above.")]
		public readonly HashSet<string> SupplyRouteTypes = new() { "supplyroute" };

		[Desc("Ammo term, per mille — see StarvingRecruitGate. A dry engineer is released and left IDLE",
			"rather than ordered, which is the only way he rearms: ^E6 sets AutoSeekSupplies",
			"ReturnWhenEmpty false, so the periodic empty-check dispatch is OFF for him and only the",
			"INotifyBecomingIdle path remains. Issuing him any order here would suppress it.")]
		public readonly int StarvingRecruitThresholdPerMille = 250;

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			base.RulesetLoaded(rules, ai);

			// Case-harden actor-name config (see ActorNameCase). Without this the module matches only
			// the exact spelling used here, and `E6.america` — the spelling used throughout
			// infantry-america.yaml — would silently match nothing.
			ActorNameCase.NormalizeInPlace(OperatorActorTypes);
			ActorNameCase.NormalizeInPlace(SupplyRouteTypes);
		}

		public override object Create(ActorInitializer init) { return new EngineerOperatorBotModule(init.Self, this); }
	}

	public class EngineerOperatorBotModule : ConditionalTrait<EngineerOperatorBotModuleInfo>, IBotTick
	{
		// The two target types Armament@Repair services, spelled exactly as the YAML spells them
		// (infantry.yaml AutoTargetPriority@Repair / weapons-other.yaml Repair.ValidTargets). Read off
		// the live actor through GetAllTargetTypes rather than re-derived from Health, so this cannot
		// drift out of agreement with the conditional Targetable@VehicleRepair / @BuildingRepair traits
		// that actually decide whether the armament would fire.
		static readonly BitSet<TargetableType> RepairTargetTypes = new("VehicleRepair", "BuildingRepair");

		readonly World world;
		readonly Player player;

		PoiGoalGuard goalGuard;
		bool goalGuardResolved;
		BeliefStore beliefStore;
		bool beliefStoreResolved;

		readonly StarvingRecruitGate ammoGate = new("engineer");

		// Engineers this module currently holds a ledger claim on, so the claim can be dropped precisely
		// when the engineer goes dry, dies, or the module shuts down.
		readonly HashSet<Actor> claimed = new();

		// The order standing against each engineer. Read only through the ordinal engineer walk, never
		// enumerated, so its ordering reaches no decision.
		sealed class Assignment
		{
			public EngineerEmployment Employment;
			public CPos TargetCell;
			public uint TargetKey;
			public int OrderedTick;
		}

		readonly Dictionary<Actor, Assignment> assignments = new();

		// Targets spent this evaluation. Engineers are walked in ActorID order, so the second engineer
		// cannot pile onto the building the first one just left to blow — the "do not send both
		// engineers to the same place" rule, enforced without any randomisation.
		readonly HashSet<uint> claimedTargets = new();

		// Scratch, reused across evaluations so a per-tick scan allocates nothing. Sorted by ActorID
		// before anything reads it.
		readonly List<BeliefContact> staticContacts = new();

		int reevalCountdown;

		public EngineerOperatorBotModule(Actor self, EngineerOperatorBotModuleInfo info)
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

		protected override void TraitDisabled(Actor self)
		{
			ReleaseAll();
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (player.WinState != WinState.Undefined)
				return;

			// REFRESH THE CLAIM EVERY TICK, EVALUATE ONLY ON THE CADENCE. These are split because a
			// third party can delete this module's claim between two evaluations:
			// StancePositioningExecutor.CommitManagement overwrites the ledger entry with `tacpos:` and
			// ReleaseManagement calls Ledger.Release(self) unconditionally, without ever reading
			// IsCommitted. On a 100-tick evaluation cadence that leaves the engineer unclaimed — and so
			// recruitable by anything that walks the free pool — for up to 100 ticks. A dictionary write
			// per tick is far cheaper than that exposure. The hazard is general, not engineer-specific:
			// GoalGuardLedger.Release is keyed on the ACTOR, not the objective, so it deletes whatever
			// claim the actor holds regardless of who wrote it (documented at
			// DroneOperatorBotModule.cs:253-263, where the same window was measured).
			ResolveTraits();
			RefreshClaims();

			if (--reevalCountdown > 0)
				return;

			reevalCountdown = Info.ReevaluateInterval;
			Reevaluate(bot);
		}

		void ResolveTraits()
		{
			if (!goalGuardResolved)
			{
				goalGuard = player.PlayerActor.TraitOrDefault<PoiGoalGuard>();
				goalGuardResolved = true;
			}

			if (!beliefStoreResolved)
			{
				beliefStore = world.WorldActor.TraitOrDefault<BeliefStore>();
				beliefStoreResolved = true;
			}
		}

		void RefreshClaims()
		{
			if (goalGuard == null || claimed.Count == 0)
				return;

			var tick = world.WorldTick;
			foreach (var op in claimed)
				if (!op.IsDead && op.IsInWorld)
					goalGuard.Ledger.Commit(op, ObjectiveKey(op), tick, Info.CommitmentTicks);
		}

		static string ObjectiveKey(Actor op) => "engineer:" + op.ActorID.ToString();

		void ReleaseAll()
		{
			if (goalGuard != null)
				foreach (var a in claimed)
					goalGuard.Ledger.Release(a);

			claimed.Clear();
			assignments.Clear();
			claimedTargets.Clear();
			staticContacts.Clear();
		}

		void Reevaluate(IBot bot)
		{
			var tick = world.WorldTick;

			// Drop claims on engineers that died or left.
			claimed.RemoveWhere(a =>
			{
				if (!a.IsDead && a.IsInWorld)
					return false;

				goalGuard?.Ledger.Release(a);
				assignments.Remove(a);
				return true;
			});

			// A dead engineer's standing order dies with it; anything left here would keep a stale
			// assignment alive and suppress a re-task for a replacement that reused the reference.
			if (assignments.Count > 0)
				foreach (var a in assignments.Keys.Where(a => a.IsDead || !a.IsInWorld).ToList())
					assignments.Remove(a);

			var engineers = world.Actors
				.Where(a => a.Owner == player
					&& !a.IsDead
					&& a.IsInWorld
					&& Info.OperatorActorTypes.Contains(a.Info.Name))
				.OrderBy(a => a.ActorID)
				.ToList();

			if (engineers.Count == 0)
			{
				if (claimed.Count > 0)
					ReleaseAll();

				return;
			}

			// Per evaluation: last cycle's target claims must not suppress this cycle's choices.
			claimedTargets.Clear();
			BuildStaticContacts();

			var screenAnchor = FindScreenAnchor();

			foreach (var op in engineers)
				TaskEngineer(bot, op, tick, screenAnchor);
		}

		// Snapshot the believed STATIC contacts once per evaluation, SORTED BY KEY so the argmax below
		// has a fixed iteration order regardless of the belief store's dictionary layout — the
		// determinism the influence stack requires, obtained by construction rather than by hoping
		// enumeration is stable.
		void BuildStaticContacts()
		{
			staticContacts.Clear();
			if (beliefStore == null)
				return;

			foreach (var c in beliefStore.Contacts(player))
				if (c.IsStatic)
					staticContacts.Add(c);

			staticContacts.Sort((a, b) => a.Key.CompareTo(b.Key));
		}

		void TaskEngineer(IBot bot, Actor op, int tick, CPos? screenAnchor)
		{
			// RELEASE WHEN DRY AND ISSUE NOTHING, WHICH IS NOT THE SAME RULE THE DRONE MODULE USES AND
			// THE DIFFERENCE IS ONE YAML FIELD. ^DR self-dispatches to a truck because AutoSeekSupplies
			// runs its periodic empty-check; ^E6 sets ReturnWhenEmpty: false, so that path is gated off
			// for him entirely (AutoSeekSupplies.cs:230) and the ONLY route back to a supply source is
			// the INotifyBecomingIdle handoff. That fires when he goes idle — so the correct action here
			// is to leave him alone with no order at all. Issuing him a parking move instead would keep
			// him permanently non-idle and permanently empty.
			if (ammoGate.Withhold(op, Info.StarvingRecruitThresholdPerMille))
			{
				if (claimed.Remove(op))
					goalGuard?.Ledger.Release(op);

				assignments.Remove(op);
				return;
			}

			// If something else already owns this engineer, DO NOT FIGHT OVER IT. The other claimant is
			// EngineerRouteOpenBotModule holding `bridge-repair:<hutId>`, and that mission manufactures
			// a whole new land axis — it outranks anything here by a wide margin, and there is exactly
			// one engineer-consuming module on each side of this check, so yielding cannot deadlock.
			// The claim IS taken back once that mission releases it.
			if (goalGuard != null && !claimed.Contains(op) && goalGuard.Ledger.IsCommitted(op, tick))
				return;

			// Re-commit EVERY cycle. Commitments expire, and a lapsed claim is an engineer the offence
			// pool is free to walk into an assault wave.
			goalGuard?.Ledger.Commit(op, ObjectiveKey(op), tick, Info.CommitmentTicks);
			claimed.Add(op);

			var canDemolish = HasDemolitionCharge(op);
			var breachKey = 0u;
			var breach = canDemolish ? FindBreachTarget(op, tick, out breachKey) : null;
			var repair = FindRepairTarget(op);

			var employment = EngineerTaskingMath.ChooseEmployment(
				canDemolish, breach != null, repair != null, screenAnchor.HasValue);

			if (employment == EngineerEmployment.None)
				return;

			// SPEND THE TARGET ONLY ONCE THE EMPLOYMENT HAS BEEN CHOSEN, not inside the finders. Both
			// finders run for every engineer, so claiming as they searched marked targets that the
			// engineer then did not take: an engineer who chose breach still burned the repair target it
			// had merely CONSIDERED, and the second engineer in the ActorID walk found no repair work on
			// a map that had some. The finders are pure searches; this is the only place a claim is made.
			switch (employment)
			{
				case EngineerEmployment.Breach:
					claimedTargets.Add(breachKey);
					IssueBreach(bot, op, breach, tick);
					break;

				case EngineerEmployment.Repair:
					claimedTargets.Add(repair.ActorID);
					IssuePark(bot, op, EngineerEmployment.Repair, repair.Location, repair.ActorID, tick);
					break;

				case EngineerEmployment.Screen:
					// Not claimed: the screen anchor is a centroid rather than a unit, and every engineer
					// with nothing better to do belongs with the forward group.
					IssuePark(bot, op, EngineerEmployment.Screen, screenAnchor.Value, 0, tick);
					break;
			}
		}

		// THE CHARGE COUNT, READ FROM THE POOL RATHER THAN FROM AN ARMAMENT PAUSE STATE. ^E6 has no
		// Armament named `secondary` at all — the Demolition and Minelayer traits spend secondary-ammo
		// through TakeAmmo directly (Demolish.cs:79, LayMines.cs:210) — so the armament-pause trick the
		// drone module uses has nothing to read here. This mirrors Demolish.TryStartEnter's own check
		// (Demolish.cs:56), which is what will actually abort the walk if it disagrees.
		bool HasDemolitionCharge(Actor op)
		{
			foreach (var pool in op.TraitsImplementing<AmmoPool>())
				if (pool.Info.Name == Info.DemolitionAmmoPoolName)
					return pool.HasAmmo;

			return false;
		}

		// The believed enemy static most worth three charges, or null. FOG-LEGAL BY CONSTRUCTION: the
		// candidate set is BeliefStore.Contacts(player) and nothing else, the freshness window is what
		// makes the actor lookup reveal nothing (see EngineerTaskingMath.IsBreachViable), and every
		// distance is measured against the CONTACT's remembered cell rather than against the resolved
		// actor's live Location.
		Actor FindBreachTarget(Actor op, int tick, out uint bestKey)
		{
			Actor best = null;
			var bestScore = long.MinValue;
			bestKey = 0u;

			foreach (var c in staticContacts)
			{
				// Spent by an earlier engineer in this evaluation's ActorID walk.
				if (claimedTargets.Contains(c.Key))
					continue;

				var distance = (c.Cell - op.Location).Length;
				var friendly = CountFriendliesNear(c.Cell, Info.BreachFriendlyRadiusCells);

				if (!EngineerTaskingMath.IsBreachViable(
					friendly, Info.BreachMinFriendlyNearby,
					distance, Info.BreachMaxDistanceCells,
					tick - c.LastSeenTick, Info.BreachContactFreshnessTicks))
					continue;

				var score = EngineerTaskingMath.BreachScore(friendly, distance, Info.BreachMaxDistanceCells);

				// Strict >, over a list already sorted by key, so ties resolve to the lowest ActorID on
				// every run rather than depending on iteration order.
				if (score <= bestScore)
					continue;

				// Only NOW resolve the belief record to a live actor, and only to BUILD THE ORDER —
				// Demolish derives from Enter and Enter only ever enters a TargetType.Actor, so a cell
				// or frozen target would walk the engineer there and quietly give up. Nothing read off
				// this actor reaches the choice above.
				var actor = world.GetActorById(c.Key);
				if (actor == null || actor.IsDead || !actor.IsInWorld)
					continue;

				// The same validity test Demolition.ResolveOrder runs (Demolition.cs:97-99). Asking it
				// here rather than mirroring its conditions is what stops the two drifting apart: an
				// order that fails this check is silently dropped, and a silently dropped order is
				// indistinguishable in a match from a module that never fired.
				if (!actor.TraitsImplementing<IDemolishable>().Any(d => d.IsValidTarget(actor, op)))
					continue;

				bestScore = score;
				best = actor;
				bestKey = c.Key;
			}

			return best;
		}

		// Our own units near a cell. OWN units only, so this is legal without any belief indirection —
		// it is the legally-measurable stand-in for "an axis is stalled here".
		int CountFriendliesNear(CPos cell, int radiusCells)
		{
			var n = 0;
			foreach (var a in world.Actors)
			{
				if (a.Owner != player || a.IsDead || !a.IsInWorld || a.OccupiesSpace == null)
					continue;

				// Armed units only: a supply truck parked near a bunker is not pressure on it.
				if (!a.Info.HasTraitInfo<AttackBaseInfo>())
					continue;

				if ((a.Location - cell).Length <= radiusCells)
					n++;
			}

			return n;
		}

		// The nearest damaged friendly the repair armament would actually service, or null.
		//
		// GetAllTargetTypes RATHER THAN A HEALTH COMPARISON, DELIBERATELY. Targetable@VehicleRepair and
		// @BuildingRepair are both conditional on `damaged`, so asking the actor which target types it
		// currently presents answers the real question — "would Armament@Repair fire at this right
		// now" — while a Health.HP < MaxHP test would be a second, independently-drifting definition of
		// damaged sitting next to the one the engine uses.
		Actor FindRepairTarget(Actor op)
		{
			Actor best = null;
			var bestDistance = int.MaxValue;

			foreach (var a in world.Actors)
			{
				if (a.Owner != player || a.IsDead || !a.IsInWorld || a.OccupiesSpace == null)
					continue;

				if (a == op)
					continue;

				if (claimedTargets.Contains(a.ActorID))
					continue;

				if (!a.GetAllTargetTypes().Overlaps(RepairTargetTypes))
					continue;

				var distance = (a.Location - op.Location).Length;
				if (distance > Info.RepairMaxDistanceCells)
					continue;

				// Strict <, then lowest ActorID, so the walk over world.Actors cannot decide a tie.
				if (distance < bestDistance || (distance == bestDistance && (best == null || a.ActorID < best.ActorID)))
				{
					bestDistance = distance;
					best = a;
				}
			}

			return best;
		}

		// Where the forward friendly group is, or null when nothing is deployed. The SR exclusion is
		// what makes this a FORWARD anchor rather than the beachhead: everything spawns at the Supply
		// Route, so without it the centroid is always the SR and the engineer never leaves it — the
		// exact behaviour this module exists to end.
		CPos? FindScreenAnchor()
		{
			var homes = world.Actors
				.Where(a => a.Owner == player && !a.IsDead && a.IsInWorld && a.OccupiesSpace != null
					&& Info.SupplyRouteTypes.Contains(a.Info.Name))
				.Select(a => a.Location)
				.ToList();

			var sumX = 0;
			var sumY = 0;
			var count = 0;

			foreach (var a in world.Actors)
			{
				if (a.Owner != player || a.IsDead || !a.IsInWorld || a.OccupiesSpace == null)
					continue;

				if (!a.Info.HasTraitInfo<AttackBaseInfo>() || !a.Info.HasTraitInfo<MobileInfo>())
					continue;

				if (homes.Any(h => (a.Location - h).Length <= Info.ScreenHomeExclusionCells))
					continue;

				sumX += a.Location.X;
				sumY += a.Location.Y;
				count++;
			}

			if (count == 0)
				return null;

			// Summation is order-independent, so the unordered walk above reaches no ordering-dependent
			// decision; the rounding is what keeps a still group from reading as a moving one (see
			// EngineerTaskingMath.CentroidAxis).
			return new CPos(
				EngineerTaskingMath.CentroidAxis(sumX, count),
				EngineerTaskingMath.CentroidAxis(sumY, count));
		}

		void IssueBreach(IBot bot, Actor op, Actor target, int tick)
		{
			assignments.TryGetValue(op, out var standing);
			var sameEmployment = standing != null && standing.Employment == EngineerEmployment.Breach;
			var sameTarget = standing != null && standing.TargetKey == target.ActorID;
			var sinceOrder = standing != null ? tick - standing.OrderedTick : int.MaxValue;

			if (!EngineerTaskingMath.ShouldRetask(standing != null, sameEmployment, sameTarget,
				sinceOrder, Info.OrderSettleTicks))
				return;

			// Unqueued deliberately: a demolition queued behind a parking move is a demolition that
			// starts from an unknown position at an unknown time, and the charge count may have changed
			// by then.
			if (!bot.QueueOrder(new Order("C4", op, Target.FromActor(target), false)))
				return;

			// THE VERDICT LINE FOR THIS MODULE. "The bot uses its engineers" and "the bot bought
			// engineers and I told a story about them" are the same trace without it. Count distinct
			// `target=` values per `eng=` over a match: the employment is working when a breach line is
			// followed by the target's death rather than by another breach line naming the same target,
			// which is the signature of a walk that keeps being cancelled.
			Log.Write("debug",
				$"[engineer] player={player.PlayerName} eng={op.ActorID} breach target={target.ActorID} "
				+ $"type={target.Info.Name} cell={target.Location.X},{target.Location.Y} "
				+ $"engcell={op.Location.X},{op.Location.Y} "
				+ $"dist={(target.Location - op.Location).Length} statics={staticContacts.Count} "
				+ $"retask={(standing != null ? "yes" : "first")} tick={tick}");

			Remember(op, standing, EngineerEmployment.Breach, target.Location, target.ActorID, tick);
		}

		void IssuePark(IBot bot, Actor op, EngineerEmployment employment, CPos anchor, uint targetKey, int tick)
		{
			assignments.TryGetValue(op, out var standing);
			var sameEmployment = standing != null && standing.Employment == employment;

			// A parking employment's "same target" is a DISPLACEMENT question, not an equality one: the
			// anchor is a centroid (screen) or a moving vehicle (repair) and shifts a cell whenever
			// anyone takes a step. Treating any shift as a new target re-orders him every cycle, which
			// cancels the repair armament's auto-acquired target on the way out — so the group under
			// fire, which is the group that needs repairs, is the one whose engineer never completes a
			// burst.
			var shift = standing != null ? (anchor - standing.TargetCell).Length : int.MaxValue;
			var sameTarget = sameEmployment
				&& standing != null
				&& !EngineerTaskingMath.AnchorMovedMaterially(shift, Info.AnchorShiftCells);

			var sinceOrder = standing != null ? tick - standing.OrderedTick : int.MaxValue;

			if (!EngineerTaskingMath.ShouldRetask(standing != null, sameEmployment, sameTarget,
				sinceOrder, Info.OrderSettleTicks))
				return;

			// Clamp to a cell the engineer can actually stand on. Both order paths a bot uses relocate
			// the destination through Mobile.NearestMoveableCell anyway, silently and without telling
			// the bot — clamping here makes the cell this module remembers and the cell the engine
			// picks the same cell, which is what the shift comparison above depends on being true.
			if (!BotTerrain.TryNearestStandable(anchor, BotTerrain.EngineRelocationCells,
					world.Map.Contains, BotTerrain.PassableFor(op), out var cell))
				return;

			// Move, not AttackMove: both weapons that do this job (Repair 1c0, ClearMines 1c0) are
			// auto-targeted by AutoTargetPriority@Repair / @Mine once he is parked, and an assault move
			// would instead walk a 250-cost unarmoured specialist into contact with his MP5.
			if (!bot.QueueOrder(new Order("Move", op, Target.FromCell(world, cell), false)))
				return;

			Log.Write("debug",
				$"[engineer] player={player.PlayerName} eng={op.ActorID} {employment.ToString().ToLowerInvariant()} "
				+ $"cell={cell.X},{cell.Y} anchor={anchor.X},{anchor.Y} target={targetKey} "
				+ $"engcell={op.Location.X},{op.Location.Y} shift={(shift == int.MaxValue ? -1 : shift)} "
				+ $"retask={(standing != null ? "yes" : "first")} tick={tick}");

			Remember(op, standing, employment, cell, targetKey, tick);
		}

		void Remember(Actor op, Assignment standing, EngineerEmployment employment, CPos cell, uint targetKey, int tick)
		{
			if (standing == null)
				assignments[op] = standing = new Assignment();

			standing.Employment = employment;
			standing.TargetCell = cell;
			standing.TargetKey = targetKey;
			standing.OrderedTick = tick;
		}
	}
}
