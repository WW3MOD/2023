#region Copyright & License Information
/*
 * WW3MOD — the bot's nuclear decision-making for Escalation.
 *
 * ==== NOTHING IN THIS MOD USED A SUPPORT POWER BEFORE THIS FILE ====
 * The engine ships a generic SupportPowerBotModule and mods/ww3mod INSTANTIATES IT NOWHERE, so up
 * to now every bot in every mode has sat on a full nuclear bin and never clicked it. That is the
 * gap this closes, and it is why this is a new module rather than a decision table bolted onto the
 * generic one:
 *
 *   1. THE GENERIC MODULE'S UNIT OF CONFIGURATION IS THE POWER. SupportPowerDecision is keyed on
 *      OrderName, so expressing "the highest band that is ready" would mean thirteen decision
 *      blocks that have to agree with each other about a ladder none of them can see. The
 *      exchange's unit is the BAND (NuclearRung), and a band is a property of the weapon's yield,
 *      not of its order name.
 *   2. IT HAS NO NOTION OF WHETHER TO FIRE AT ALL. Its whole policy is "ready and a target scores
 *      above MinimumAttractiveness". The user's rule is the opposite shape — "It only fires if it
 *      is losing, so it never escalates unnecessarily" — and there is no attractiveness threshold
 *      that encodes it.
 *   3. IT DRAWS RNG. FindCoarseAttackLocationToSupportPower ends in
 *      `suitableLocations.Shuffle(world.LocalRandom)`. The influence stack's standing invariant is
 *      zero draws anywhere in the strategic layer (influence-stack.md §Determinism), and a nuclear
 *      aim point is the last decision in the mod that should be a coin flip.
 *
 * ==== WHAT IT DOES, IN ONE PARAGRAPH ====
 * Every EvaluationInterval it asks whether this side is losing (army ratio OR its own Supply Route
 * being contested), keeps a streak so a single bad trade is not a rout, and if it has been losing
 * for LosingStreakRequired evaluations it fires THE HIGHEST BAND ITS SIDE'S LEVEL ALLOWS that has a
 * warhead ready. It aims at the point maximising believed enemy value inside the warhead's radius.
 * It never fires when it is winning, and it reaches a game-ender only at level 5 with
 * MayFireGameEnder set — or through a final exchange somebody else started, where declining would
 * forfeit its aim points to Dead Hand and save nothing.
 *
 * THE COOLDOWN NEEDS NO CODE HERE, and that is worth saying because its absence looks like an
 * omission. NuclearExchange writes a side's cooldown onto every one of that side's nuclear support
 * powers, so while it runs BuildReadyBands finds nothing ready and the policy answers NoReadyBand on
 * its own. A second check against the state would be a copy of rule 2 free to disagree with the mask
 * the module actually measured.
 *
 * The arithmetic is all in NuclearPolicyMath, world-free and under NUnit. This file is the part
 * that needs a world: which powers exist, what is ready, who can legally be seen, and the order.
 *
 * ==== FOG ====
 * Targets come from BeliefStore — the established fog-legal seam and the one both fog-respecting
 * profiles already build (influence-stack.md §Stage A). Its contacts are live sightings the player
 * may legally see plus its own FrozenActorLayer ghosts, so nothing here reads through fog. The
 * fallback for a profile that does not participate in the influence stack is a direct
 * Actor.CanBeViewedByPlayer scan, which is the SAME predicate BeliefStore.InjectLive uses — so the
 * fallback is narrower than the belief store, never wider.
 *
 * THE EVALUATION CADENCE IS BOUNDED BY THE BELIEF STORE'S DECAY, not chosen freely. An unrefreshed
 * mobile contact is erased after 175 ticks (influence-stack.md §Stage A: 100 -> 12 over seven
 * passes at UpdateInterval 25), so a module that re-decided more slowly than that would find the
 * list already empty of anything that moved. EvaluationInterval's default of 50 is well inside it;
 * raising it past 175 in yaml would quietly reduce this to a structures-only targeter.
 *
 * ==== DETERMINISM ====
 * Bot logic is host-only and reaches the simulation ONLY through IBot.QueueOrder
 * (architecture.md §"All bot orders funnel through ModularBot.QueueOrder"). Nothing here writes
 * synced state and nothing here draws from SharedRandom or LocalRandom. The power walk is sorted by
 * ordinal key rather than taken in Dictionary order, and every target tie breaks on the enemy
 * actor's own ActorID, so two hosts handed the same world produce the same order.
 */
#endregion

using System;
using System.Collections.Generic;
using OpenRA.Mods.Common.Orders;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.Player)]
	[Desc("WW3MOD: decides when a bot fires a nuclear weapon in Escalation, and where.",
		"Fires ONLY when losing — army value below LosingArmyRatioPercent of the strongest enemy, or",
		"its own Supply Route's control bar below LosingContestationPercent — and only after",
		"LosingStreakRequired consecutive evaluations agree, so one bad trade is not a rout. When it",
		"does fire it takes the biggest band it may: the highest band at or below its SIDE's level",
		"with a warhead ready. Game-enders are reachable only at level 5 (and only when",
		"MayFireGameEnder is set) or through a final exchange somebody else began.",
		"Targets the point maximising believed enemy value inside the warhead's radius, fog-legally,",
		"via BeliefStore. Inert outside DefconGameMode.Escalation.")]
	public class NuclearBotModuleInfo : ConditionalTraitInfo, Requires<SupportPowerManagerInfo>
	{
		[Desc("Ticks between losing evaluations, and therefore between launch decisions.",
			"",
			"BOUNDED ABOVE BY THE BELIEF STORE, not free. An unrefreshed mobile contact is erased after",
			"175 ticks, so a value past that leaves this module targeting remembered structures only.",
			"The final-exchange placement is checked every tick regardless of this — that window is",
			"250 ticks wide and forfeiting it to Dead Hand for a beat would cost the bot its aim points.")]
		public readonly int EvaluationInterval = 50;

		[Desc("UNTUNED PLACEHOLDER. Own ArmyValue below this percentage of the STRONGEST enemy's counts",
			"as losing. The user's brief is a ratio of 0.6; the engine counts in whole credits, so it is",
			"written as the percentage the integer comparison actually uses.",
			"",
			"100 would mean 'losing whenever slightly behind' and 0 would disable this half entirely.")]
		public readonly int LosingArmyRatioPercent = 60;

		[Desc("UNTUNED PLACEHOLDER. Own Supply Route control bar below this percentage counts as losing.",
			"The user's brief is 0.4; SupplyRouteContestation.ControlBarFraction ALREADY returns 0..100",
			"(SupplyRouteContestation.cs:258), so this is that number and not a fraction of it.",
			"",
			"WHY THIS HALF EXISTS AT ALL: contestation, not attrition, is the mod's win condition — at",
			"100 % the trait's own [Desc] says the player is passive if a teammate can relieve them and",
			"DEFEATED if nobody can. A bot reading army value alone cannot see itself losing that way.",
			"Set to 0 to disable this half and decide on army value only.")]
		public readonly int LosingContestationPercent = 40;

		[Desc("UNTUNED PLACEHOLDER. Consecutive losing evaluations required before the bot will fire.",
			"ArmyValue moves on every kill, so a bare threshold flaps across any ordinary engagement and",
			"a bot without this fires on the first unlucky trade. The streak RESETS TO ZERO on a single",
			"not-losing evaluation — climbing out is instant, climbing in takes this many.",
			"",
			"At the default EvaluationInterval of 50 this is 150 ticks of continuously losing.",
			"1 disables the hysteresis; 0 is read as 1.")]
		public readonly int LosingStreakRequired = 3;

		[Desc("UNTUNED PLACEHOLDER. Minimum ticks between two launches from this module.",
			"Bounds the bot's own escalation rate independently of what its cooldowns permit: the side",
			"cooldown is a lobby-scaled lever (Nuclear Posture) and a bot on Massive Retaliation would",
			"otherwise fire on the first tick of every recovery.",
			"",
			"MOSTLY REDUNDANT SINCE EXCHANGE v2 AND KEPT ANYWAY. The side cooldown is at least 3000",
			"ticks at the fastest posture, which already exceeds this default -- so this only binds if",
			"a host shortens the cooldowns below it, or if a future band is cheaper than 900 ticks.",
			"",
			"NOT APPLIED TO THE FINAL EXCHANGE, which is placement rather than escalation.")]
		public readonly int MinTicksBetweenLaunches = 900;

		[Desc("May the bot START an apocalypse — fire a game-ender once its side has reached level 5?",
			"",
			"DEFAULT TRUE, because the ruling's model is that the top rung is reached only by being hit",
			"with a 100 kt, and a loser who will not take the weapon that hit bought them is a loser",
			"who cannot use its own deterrent. Set FALSE for bots that must never end the game; such a",
			"bot falls back to the highest band below END rather than holding its fire.",
			"",
			"IT DOES NOT COVER PARTICIPATING IN ONE ALREADY BEGUN. Once DoomsdayStrike has opened the",
			"final exchange the match is ending whatever this says — Dead Hand places for anyone who",
			"does not — so declining there would forfeit the bot's aim points and save nothing.")]
		public readonly bool MayFireGameEnder = true;

		[Desc("UNTUNED PLACEHOLDER. Effect radius used for aim-point scoring, in CELLS, one entry per",
			"band in ascending order: 1 kt, 20 kt, 50 kt, 100 kt, game-ender. A band past the end of the",
			"list takes the last entry, matching NuclearExchangeState.CooldownTicksFor's convention.",
			"",
			"TAKEN FROM THE ARSENAL'S OWN CameraRange PER BAND (nuclear-arsenal.yaml :120, :167, :208,",
			"between :285 and :326, :397) because that is the mod's existing per-yield statement of how",
			"far a warhead matters, and a targeting radius invented from nothing would be worse. It is",
			"still a SCORING radius and not the weapon's lethal radius: too small clusters the aim point",
			"onto one unit, too large flattens the score across the whole map and makes every cell look",
			"alike.")]
		public readonly int[] StrikeRadiusCellsPerBand = { 4, 10, 16, 21, 30 };

		[Desc("UNTUNED PLACEHOLDER. Percentage applied to an aim point whose radius covers the enemy",
			"Supply Route. 100 is inert; below 100 avoids one.",
			"",
			"The SR itself takes NO DAMAGE from any of this — SUPPLYROUTE carries",
			"`Targetable.TargetTypes: NoAutoTarget`, a target type no weapon in the mod lists, so every",
			"warhead is rejected before damage is computed. What the bonus buys is the GARRISON and the",
			"contesting force standing around it, which is exactly where a losing side's problem is.")]
		public readonly int SupplyRouteBonusPercent = 150;

		[Desc("Apply SupplyRouteBonusPercent only while the bot's OWN Supply Route is contested below",
			"LosingContestationPercent, rather than on every launch.",
			"",
			"TRUE (the default) reads the brief's 'preferring the enemy Supply Route area when",
			"contested' as 'when we are the ones being contested' — hit back at the source. FALSE makes",
			"the preference unconditional. Both readings are live because the brief's wording does not",
			"settle which side 'contested' refers to.")]
		public readonly bool SupplyRouteBonusOnlyWhenContested = true;

		[Desc("Actor types treated as a Supply Route when scoring aim points. Named explicitly rather",
			"than detected from the trait so a future structure cannot silently inherit the bonus.")]
		public readonly HashSet<string> SupplyRouteTypes = new() { "supplyroute" };

		[Desc("Minimum separation between two aim points of one MULTI-WARHEAD salvo, in cells, as a",
			"percentage of that band's StrikeRadiusCellsPerBand.",
			"",
			"Without it the six highest-scoring discs over any cluster overlap almost completely, which",
			"is six warheads doing slightly more than one. 100 means 'no two aim points inside each",
			"other's radius'; 0 disables the spreading and stacks the salvo.")]
		public readonly int MultiAimSeparationPercent = 100;

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			base.RulesetLoaded(rules, ai);

			// Case-harden actor-name config (see ActorNameCase). Actor.Info.Name is lowercased at load
			// (Ruleset.cs:126), so `SUPPLYROUTE` here would match nothing while looking correct.
			ActorNameCase.NormalizeInPlace(SupplyRouteTypes);
		}

		public override object Create(ActorInitializer init) { return new NuclearBotModule(init.Self, this); }
	}

	public class NuclearBotModule : ConditionalTrait<NuclearBotModuleInfo>, IBotTick
	{
		readonly World world;
		readonly Player player;

		SupportPowerManager supportPowerManager;

		NuclearExchange exchange;
		bool exchangeResolved;
		DoomsdayStrike doomsday;
		bool doomsdayResolved;
		BeliefStore beliefStore;
		bool beliefStoreResolved;
		PlayerStatistics ownStatistics;
		bool ownStatisticsResolved;

		int evaluationDelay;

		// Reused across evaluations so a 50-tick beat allocates nothing steady-state.
		readonly List<NuclearTargetCandidate> candidates = new();
		readonly List<string> orderedPowerKeys = new();
		readonly Dictionary<int, string> readyKeyForBand = new();
		readonly List<CPos> aimCells = new();

		// -1 rather than 0 so a bot that is already losing at world load is not rate-limited by a
		// launch that never happened. MinTicksBetweenLaunches away from tick 0 would be, with 0.
		int lastLaunchTick = int.MinValue / 2;

		bool placedInFinalExchange;

		/// <summary>How many launch orders this module has queued. Read by the autotest binding.</summary>
		public int LaunchCount { get; private set; }

		/// <summary>Consecutive losing evaluations. Reset to 0 by a single not-losing one.</summary>
		public int LosingStreak { get; private set; }

		/// <summary>Whether the streak has reached <c>LosingStreakRequired</c>.</summary>
		public bool IsLosingCommitted => NuclearPolicyMath.IsCommitted(LosingStreak, Info.LosingStreakRequired);

		/// <summary>
		/// <para>The most recent decision, fired or not. LIVE: overwritten every evaluation.</para>
		/// </summary>
		// IT DOES NOT SURVIVE THE LAUNCH IT DESCRIBES, and that is why LastLaunchReason exists beside
		// it. The evaluation after a launch reads RateLimited -- correctly -- so a caller asking "why
		// did it fire" a few hundred ticks later gets the reason it is NOT firing now.
		public NuclearBotReason LastReason { get; private set; } = NuclearBotReason.NotReleased;

		/// <summary>Why the LAST LAUNCH happened. Sticky; see <see cref="LastReason"/>.</summary>
		public NuclearBotReason LastLaunchReason { get; private set; } = NuclearBotReason.NotLosing;

		/// <summary>The band of the last launch, or Hold if there has not been one.</summary>
		public int LastFiredBand { get; private set; } = (int)NuclearRung.Hold;

		/// <summary>The tick of the last launch, or int.MinValue/2 if there has not been one.</summary>
		public int LastLaunchTick => lastLaunchTick;

		public NuclearBotModule(Actor self, NuclearBotModuleInfo info)
			: base(info)
		{
			world = self.World;
			player = self.Owner;
		}

		protected override void Created(Actor self)
		{
			supportPowerManager = self.Owner.PlayerActor.Trait<SupportPowerManager>();
		}

		protected override void TraitEnabled(Actor self)
		{
			// Staggered by nothing: the offset would have to come from somewhere, and the only sources
			// on hand are the RNGs the influence stack's determinism invariant forbids this layer from
			// touching (influence-stack.md §Determinism, which gives each always-on layer a FIXED
			// offset for exactly this reason). One evaluation per EvaluationInterval per bot is cheap
			// enough that the alignment does not matter.
			evaluationDelay = Info.EvaluationInterval;
		}

		void IBotTick.BotTick(IBot bot)
		{
			var nuclearExchange = Exchange();
			if (nuclearExchange == null || nuclearExchange.Mode != DefconGameMode.Escalation)
				return;

			// THE FINAL EXCHANGE IS CHECKED ON EVERY TICK, ahead of the evaluation beat. The window is
			// DoomsdayStrikeInfo.FinalExchangeWindowTicks wide (500 shipped, raised from 250 on 2026-09-16)
			// and a bot that waited for
			// its 50-tick beat would still make it — but placing late is strictly worse than placing
			// early and there is nothing to gain by the wait.
			var strike = Doomsday();
			if (strike != null && strike.FinalExchangeOpen)
			{
				if (!placedInFinalExchange)
					Evaluate(bot, nuclearExchange, finalExchangeOpen: true);

				return;
			}

			// Reset OUTSIDE the window so a second exchange — which cannot happen today, BeginFinalExchange
			// being idempotent — would not find this latched.
			placedInFinalExchange = false;

			if (--evaluationDelay > 0)
				return;

			evaluationDelay = Info.EvaluationInterval;
			Evaluate(bot, nuclearExchange, finalExchangeOpen: false);
		}

		void Evaluate(IBot bot, NuclearExchange nuclearExchange, bool finalExchangeOpen)
		{
			var contestationPercent = OwnSupplyRouteControlPercent(out var haveSupplyRoute);
			var losingNow = NuclearPolicyMath.IsLosingNow(
				OwnArmyValue(), StrongestEnemyArmyValue(), Info.LosingArmyRatioPercent,
				haveSupplyRoute, contestationPercent, Info.LosingContestationPercent);

			// The streak is advanced on the EVALUATION BEAT ONLY. A final-exchange tick reads the streak
			// but must not advance it, or the hysteresis count would be reached in one window's worth of
			// ticks rather than in LosingStreakRequired evaluations.
			if (!finalExchangeOpen)
				LosingStreak = NuclearPolicyMath.AdvanceStreak(LosingStreak, losingNow);

			var readyMask = BuildReadyBands();

			var decision = NuclearPolicyMath.Choose(
				nuclearExchange.Released,
				IsLosingCommitted,
				finalExchangeOpen,
				nuclearExchange.LevelFor(player),
				readyMask,
				world.WorldTick - lastLaunchTick,
				Info.MinTicksBetweenLaunches,
				Info.MayFireGameEnder);

			LastReason = decision.Reason;
			if (!decision.Fire)
				return;

			// BOTH FAILURES BELOW OVERWRITE THE REASON, and that is the point of NoTarget. Leaving
			// `HighestAllowed` standing over a launch count that never moved reports a decision where
			// there was an execution failure, and the two want opposite investigations.
			if (!readyKeyForBand.TryGetValue(decision.Band, out var key))
			{
				// Unreachable as written -- the mask was built from this dictionary in the same pass --
				// so this is the belt-and-braces the file's neighbours use rather than a live branch.
				LastReason = NuclearBotReason.NoReadyBand;
				return;
			}

			var contested = haveSupplyRoute && contestationPercent < Info.LosingContestationPercent;
			if (!Fire(bot, key, decision.Band, contested))
			{
				LastReason = NuclearBotReason.NoTarget;
				return;
			}

			lastLaunchTick = world.WorldTick;
			LastFiredBand = decision.Band;
			LastLaunchReason = decision.Reason;
			LaunchCount++;
			if (finalExchangeOpen)
				placedInFinalExchange = true;

			AIUtils.BotDebug("{0}: nuclear launch {1} band {2} (reason {3}, streak {4}, army {5} vs {6}, SR {7}%)",
				player.ResolvedPlayerName, key, decision.Band, decision.Reason, LosingStreak,
				OwnArmyValue(), StrongestEnemyArmyValue(), haveSupplyRoute ? contestationPercent : -1);
		}

		/// <summary>
		/// <para>Bands with a warhead ready RIGHT NOW, as a bitmask, and the order key to fire each one
		/// with.</para>
		///
		/// <para>SORTED BY ORDINAL KEY, not taken in Dictionary order: two bands can share a rung — the
		/// B61's mid dial and the Iskander are both TwentyKiloton — and which of them a side fires must
		/// not depend on hash iteration. In practice a player holds only its own faction's powers, so
		/// this matters for a shared side rather than for a 1v1.</para>
		///
		/// <para>THE TSAR BOMBA IS EXCLUDED HERE TOO. It is already unreachable in Escalation (its
		/// power is gated on a condition granted only outside the mode, decision 04) so this is the
		/// second statement of one rule — the same belt-and-braces NuclearReleaseLadder's
		/// SandboxOnlyAboveTons constant is.</para>
		/// </summary>
		int BuildReadyBands()
		{
			readyKeyForBand.Clear();
			orderedPowerKeys.Clear();
			foreach (var key in supportPowerManager.Powers.Keys)
				orderedPowerKeys.Add(key);

			orderedPowerKeys.Sort(StringComparer.Ordinal);

			var mask = 0;
			foreach (var key in orderedPowerKeys)
			{
				var power = supportPowerManager.Powers[key];
				if (power.Info is not MissileStrikePowerInfo missile || missile.NuclearYieldTons <= 0)
					continue;

				if (missile.NuclearYieldTons > NuclearReleaseLadder.SandboxOnlyAboveTons)
					continue;

				if (!power.Ready)
					continue;

				var band = NuclearReleaseLadder.RungForYield(missile.NuclearYieldTons);
				mask = NuclearPolicyMath.WithBand(mask, band);

				// FIRST KEY WINS, and the walk is sorted, so the choice is stable across hosts.
				readyKeyForBand.TryAdd(band, key);
			}

			return mask;
		}

		/// <summary>
		/// Aim and queue the order. Returns false when there is nothing legally visible to aim at — the
		/// bot then keeps the warhead rather than dropping it on empty ground.
		/// </summary>
		bool Fire(IBot bot, string key, int band, bool ownSupplyRouteContested)
		{
			var power = supportPowerManager.Powers[key];
			var missile = (MissileStrikePowerInfo)power.Info;

			CollectCandidates();
			if (candidates.Count == 0)
				return false;

			var radiusCells = RadiusCellsFor(band);
			var bonus = Info.SupplyRouteBonusOnlyWhenContested && !ownSupplyRouteContested
				? 100
				: Info.SupplyRouteBonusPercent;

			// MissileStrikePower.AimPointsFor, NOT missile.AimPoints, AND THE DIFFERENCE WAS A
			// SILENTLY SHORT PACKAGE. A game-ender's warhead count is map-derived
			// (DoomsdayStrike.PackageSize) and the raw YAML field is inert for one -- so asking the
			// field meant asking for the Sarmat's 6 on a map whose package is 4, getting 3 back from
			// PickAimIndices after separation filtering, and having MissileStrikePower truncate to 3.
			// Run 260920_140551 read `warheads=7` where two full packages of 4 were due.
			//
			// IT ALSO FIXES A FACTION ASYMMETRY THAT SURVIVED THE REDESIGN. The B83 left AimPoints at
			// its default 1, so the America bot took the single-cell branch below and its whole
			// package was laid on a BLIND RING around one target, while the Russia bot got ranked
			// aim points for each warhead. Both nations now rank every point they fire.
			var aimPoints = Math.Max(1, MissileStrikePower.AimPointsFor(world, missile));
			aimCells.Clear();

			if (aimPoints == 1)
			{
				var index = NuclearPolicyMath.PickAimIndex(candidates, radiusCells, bonus);
				if (index < 0)
					return false;

				aimCells.Add(candidates[index].Cell);
			}
			else
			{
				// MaxAimPointSpread is in WDist and the arithmetic here is in cells, so it is converted
				// once rather than compared in two units. 0 is that field's own "unbounded" sentinel and
				// survives the conversion as 0.
				var maxSpreadCells = missile.MaxAimPointSpread.Length > 0
					? missile.MaxAimPointSpread.Length / 1024
					: 0;

				var indices = NuclearPolicyMath.PickAimIndices(
					candidates, radiusCells, bonus, aimPoints,
					radiusCells * Info.MultiAimSeparationPercent / 100, maxSpreadCells);

				if (indices.Length == 0)
					return false;

				foreach (var i in indices)
					aimCells.Add(candidates[i].Cell);
			}

			// THE SAME ORDER SHAPE SelectMultiPowerTarget EMITS (SelectMultiPowerTarget.cs:168-172):
			// Target carries the first aim point for the target line and for SupportPowerInstance's own
			// snap, TargetString carries the authoritative list. A receiver reading Target alone gets a
			// valid single-point strike rather than a malformed one — which is exactly what a
			// single-aim-point power wants, so the same construction covers both cases.
			//
			// ExtraData is uint.MaxValue for SelectDirectionalTarget's reason, copied from the engine's
			// own SupportPowerBotModule: it is the sentinel for "the player did not pick a direction".
			var order = new Order(key, supportPowerManager.Self, Target.FromCell(world, aimCells[0]), false)
			{
				SuppressVisualFeedback = true,
				ExtraData = uint.MaxValue,
			};

			if (aimCells.Count > 1)
				order.TargetString = MultiAimPointOrder.Serialize(aimCells);

			// CHECKED EVEN THOUGH A Protected ORDER CANNOT BE DROPPED. BotOrderGate only discards a
			// Recurring order (TraitsInterfaces.cs:437-449), and this is Protected — but the counter and
			// the rate-limit tick below are exactly the kind of "memory advanced on a silent drop" that
			// the IBot.QueueOrder contract warns about, so they are advanced on true and not before.
			return bot.QueueOrder(order);
		}

		int RadiusCellsFor(int band)
		{
			var table = Info.StrikeRadiusCellsPerBand;
			if (table == null || table.Length == 0)
				return 0;

			var index = band - (int)NuclearRung.Kiloton;
			if (index < 0)
				index = 0;

			if (index >= table.Length)
				index = table.Length - 1;

			return Math.Max(0, table[index]);
		}

		/// <summary>
		/// <para>Everything this player may legally aim at. BELIEF STORE FIRST — the fog-legal seam both
		/// fog-respecting profiles already build — falling back to a direct visibility scan for a
		/// profile that does not participate in the influence stack.</para>
		///
		/// <para>THE FALLBACK IS NARROWER THAN THE BELIEF STORE, NEVER WIDER. It uses
		/// Actor.CanBeViewedByPlayer, which is the same predicate BeliefStore.InjectLive uses for live
		/// sightings (BeliefStore.cs:216); what it lacks is the remembered ghosts, so a non-participating
		/// profile aims only at what it can see this instant. That is the conservative direction.</para>
		/// </summary>
		void CollectCandidates()
		{
			candidates.Clear();

			var store = Belief();
			if (store != null)
			{
				foreach (var contact in store.Contacts(player))
				{
					if (contact.Confidence <= 0)
						continue;

					var value = ValueOf(contact.TypeName);
					if (value <= 0)
						continue;

					// SCALED BY CONFIDENCE, so a half-forgotten ghost is worth half a live sighting. The
					// belief store's own consumers weight their kernels the same way (influence-stack.md
					// §Stage B: intensity scales with confidence).
					candidates.Add(new NuclearTargetCandidate(
						contact.Key, contact.Cell, value * contact.Confidence / 100,
						Info.SupplyRouteTypes.Contains(contact.TypeName)));
				}

				// A participating profile whose store is simply empty has seen nothing, which is a real
				// answer: do not fall through to the scan and silently widen the fog surface.
				return;
			}

			foreach (var actor in world.Actors)
			{
				if (actor.Owner == null || actor.IsDead || !actor.IsInWorld)
					continue;

				if (player.RelationshipWith(actor.Owner) != PlayerRelationship.Enemy)
					continue;

				if (!actor.CanBeViewedByPlayer(player))
					continue;

				var value = ValueOf(actor.Info.Name);
				if (value <= 0)
					continue;

				candidates.Add(new NuclearTargetCandidate(
					actor.ActorID, actor.Location, value, Info.SupplyRouteTypes.Contains(actor.Info.Name)));
			}
		}

		int ValueOf(string actorType)
		{
			if (string.IsNullOrEmpty(actorType))
				return 0;

			if (!world.Map.Rules.Actors.TryGetValue(actorType, out var actorInfo))
				return 0;

			// A Supply Route is not Valued — it is never bought — so a floor keeps it in the candidate
			// list at all. Without one the single most important thing on the map scores zero and the
			// SupplyRouteBonusPercent below has nothing to apply to.
			var cost = actorInfo.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 0;
			if (cost <= 0 && Info.SupplyRouteTypes.Contains(actorType))
				return 1;

			return cost;
		}

		int OwnArmyValue()
		{
			var stats = OwnStatistics();
			return stats?.ArmyValue ?? 0;
		}

		/// <summary>
		/// The largest ArmyValue among this player's enemies. world.Players order, and a max rather
		/// than a sum: "losing" against a coalition means being outgunned by its strongest member,
		/// which is the reading that does not make every 1vN read as a rout on tick one.
		/// </summary>
		int StrongestEnemyArmyValue()
		{
			var best = 0;
			foreach (var p in world.Players)
			{
				if (p.NonCombatant || p == player)
					continue;

				if (player.RelationshipWith(p) != PlayerRelationship.Enemy)
					continue;

				var stats = p.PlayerActor.TraitOrDefault<PlayerStatistics>();
				if (stats != null && stats.ArmyValue > best)
					best = stats.ArmyValue;
			}

			return best;
		}

		/// <summary>
		/// The LOWEST control percentage among this player's own Supply Routes, and whether it has any.
		/// The lowest rather than an average: one SR about to fall is the emergency, and averaging it
		/// against a quiet one is how a bot fails to notice.
		/// </summary>
		int OwnSupplyRouteControlPercent(out bool haveSupplyRoute)
		{
			haveSupplyRoute = false;
			var lowest = 100;

			foreach (var pair in world.ActorsWithTrait<SupplyRouteContestation>())
			{
				if (pair.Actor.Owner != player || pair.Actor.IsDead || !pair.Actor.IsInWorld)
					continue;

				haveSupplyRoute = true;
				var fraction = pair.Trait.ControlBarFraction;
				if (fraction < lowest)
					lowest = fraction;
			}

			return lowest;
		}

		NuclearExchange Exchange()
		{
			if (!exchangeResolved)
			{
				exchange = world.WorldActor.TraitOrDefault<NuclearExchange>();
				exchangeResolved = true;
			}

			return exchange;
		}

		DoomsdayStrike Doomsday()
		{
			if (!doomsdayResolved)
			{
				doomsday = world.WorldActor.TraitOrDefault<DoomsdayStrike>();
				doomsdayResolved = true;
			}

			return doomsday;
		}

		BeliefStore Belief()
		{
			if (!beliefStoreResolved)
			{
				beliefStore = world.WorldActor.TraitOrDefault<BeliefStore>();
				beliefStoreResolved = true;
			}

			return beliefStore;
		}

		PlayerStatistics OwnStatistics()
		{
			if (!ownStatisticsResolved)
			{
				ownStatistics = player.PlayerActor.TraitOrDefault<PlayerStatistics>();
				ownStatisticsResolved = true;
			}

			return ownStatistics;
		}
	}
}
