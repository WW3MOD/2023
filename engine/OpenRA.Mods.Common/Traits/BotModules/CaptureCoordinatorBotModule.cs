#region Copyright & License Information
/*
 * WW3MOD CaptureCoordinatorBotModule — v2 AI.
 *
 * Replaces CaptureManagerBotModule for v2 bots. Three behaviours over the
 * legacy module:
 *
 *  1. Target scoring is INCOME-WEIGHTED (OILB=50, FCOM=100, BIO=150)
 *     rather than the legacy sell-value sort. MISS/HOSP (no income)
 *     score lower.
 *  2. Each capture dispatch also pulls K nearby idle friendlies and
 *     attack-moves them to the target as ESCORT. Engineer no longer
 *     walks alone.
 *  3. Defense pass: every DefenseScanInterval ticks, for each own
 *     capturable structure under threat (enemy army value > friendly
 *     army value in the neighbourhood), summon defenders.
 *
 * Coexists with the legacy CaptureManagerBotModule — v2 YAML gates the
 * legacy ones to enable-ai-legacy-only so they don't double-fire.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.Player)]
	[Desc("WW3MOD v2 AI: coordinates capture of income structures with escort + defense.")]
	public class CaptureCoordinatorBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Actor types that can capture other actors (via `Captures`). Empty = disabled.")]
		public readonly HashSet<string> CapturingActorTypes = new();

		[Desc("Actor types that can be targeted for capturing. Empty = all eligible.")]
		public readonly HashSet<string> CapturableActorTypes = new();

		[Desc("Tick budget for the capture scan + dispatch pass.")]
		public readonly int ScanInterval = 75;

		[Desc("Tick budget for the defense scan over own captured structures.")]
		public readonly int DefenseScanInterval = 150;

		[Desc("Max number of candidate targets considered each scan.")]
		public readonly int MaximumCaptureTargetOptions = 10;

		[Desc("Whether to filter targets by fog visibility. WW3MOD bots are typically omniscient; default false.")]
		public readonly bool CheckCaptureTargetsForVisibility = false;

		[Desc("Player relationships eligible as capture targets.")]
		public readonly PlayerRelationship CapturableRelationships = PlayerRelationship.Enemy | PlayerRelationship.Neutral;

		[Desc("Per-actor-type income weights. Lookup by lowercased actor name. ",
			"Unlisted types get DefaultIncomeWeight.")]
		public readonly Dictionary<string, int> IncomeWeights = new();

		[Desc("Income weight used when a target type is not listed in IncomeWeights.")]
		public readonly int DefaultIncomeWeight = 10;

		[Desc("Number of cells over which target-distance score halves (rough decay scale).")]
		public readonly int DistanceHalfLifeCells = 20;

		[Desc("Radius (cells) around a target inside which enemy presence reduces its safety score.")]
		public readonly int SafetyEnemyScanRadiusCells = 6;

		[Desc("Safety multiplier (x100) when no enemies near target.")]
		public readonly int SafetyMultiplierSafe = 100;

		[Desc("Safety multiplier (x100) when 1-2 enemies near target.")]
		public readonly int SafetyMultiplierMild = 40;

		[Desc("Safety multiplier (x100) when 3+ enemies near target.")]
		public readonly int SafetyMultiplierHostile = 10;

		[Desc("Actor types that may be pulled in as escorts for captures and defenders for own structures.",
			"Empty = any idle friendly mobile unit except the capturers themselves.")]
		public readonly HashSet<string> SupportingUnitTypes = new();

		[Desc("Number of escort units to attach to each capture dispatch.")]
		public readonly int EscortSize = 2;

		[Desc("Max recruit radius (cells) when searching for idle escort/defender units around the capturer or threatened structure.")]
		public readonly int SupportRecruitRadiusCells = 40;

		[Desc("Radius (cells) inside which enemy army value is counted when evaluating threat to own structures.")]
		public readonly int DefenseEnemyScanRadiusCells = 12;

		[Desc("Radius (cells) inside which friendly army value is counted when evaluating defense.")]
		public readonly int DefenseFriendlyScanRadiusCells = 6;

		[Desc("Number of defenders to summon to a threatened structure per defense tick.")]
		public readonly int DefenseSummonCount = 3;

		[Desc("Minimum enemy army value (engine $) within DefenseEnemyScanRadius to trigger a defense summon.")]
		public readonly int DefenseEnemyValueThreshold = 200;

		public override object Create(ActorInitializer init) { return new CaptureCoordinatorBotModule(init.Self, this); }
	}

	public class CaptureCoordinatorBotModule : ConditionalTrait<CaptureCoordinatorBotModuleInfo>, IBotTick, INotifyActorDisposing
	{
		readonly World world;
		readonly Player player;
		readonly Predicate<Actor> unitCannotBeOrderedOrIsIdle;
		readonly int maximumCaptureTargetOptions;

		// Capturers we've already issued orders to; cleaned when they become idle again.
		readonly List<Actor> activeCapturers = new();

		// Defender bookings — actor → tick they were summoned. Stale entries removed on tick.
		readonly Dictionary<Actor, int> defenderBookings = new();

		readonly ActorIndex.OwnerAndNamesAndTrait<CapturesInfo> capturingActors;

		int captureScanCountdown;
		int defenseScanCountdown;

		public CaptureCoordinatorBotModule(Actor self, CaptureCoordinatorBotModuleInfo info)
			: base(info)
		{
			world = self.World;
			player = self.Owner;

			if (world.Type == WorldType.Editor)
				return;

			unitCannotBeOrderedOrIsIdle = a => a.Owner != player || a.IsDead || !a.IsInWorld || a.IsIdle;
			maximumCaptureTargetOptions = Math.Max(1, Info.MaximumCaptureTargetOptions);

			capturingActors = new ActorIndex.OwnerAndNamesAndTrait<CapturesInfo>(world, Info.CapturingActorTypes, player);
		}

		protected override void TraitEnabled(Actor self)
		{
			// Stagger initial fire so all AIs don't tick the heavy scans on the same frame.
			captureScanCountdown = world.LocalRandom.Next(0, Info.ScanInterval);
			defenseScanCountdown = world.LocalRandom.Next(0, Info.DefenseScanInterval);
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (player.WinState != WinState.Undefined)
				return;

			// Bookings expire after one defense interval — that's enough time for the
			// defender to walk in, engage, and either die or pop back to idle. Keeps
			// the same actor from being booked again every single tick.
			var staleBookingTick = world.WorldTick - Info.DefenseScanInterval;
			var staleKeys = defenderBookings
				.Where(kv => kv.Value < staleBookingTick || kv.Key.IsDead || !kv.Key.IsInWorld)
				.Select(kv => kv.Key)
				.ToList();
			foreach (var k in staleKeys)
				defenderBookings.Remove(k);

			if (--captureScanCountdown <= 0)
			{
				captureScanCountdown = Info.ScanInterval;
				QueueCaptureOrders(bot);
			}

			if (--defenseScanCountdown <= 0)
			{
				defenseScanCountdown = Info.DefenseScanInterval;
				QueueDefenseOrders(bot);
			}
		}

		// ============================================================
		// CAPTURE PASS
		// ============================================================

		void QueueCaptureOrders(IBot bot)
		{
			if (Info.CapturingActorTypes.Count == 0)
				return;

			activeCapturers.RemoveAll(unitCannotBeOrderedOrIsIdle);

			var idleCapturers = capturingActors.Actors
				.Where(a => a.IsIdle && a.Info.HasTraitInfo<IPositionableInfo>() && !activeCapturers.Contains(a))
				.Select(a => new TraitPair<CaptureManager>(a, a.TraitOrDefault<CaptureManager>()))
				.Where(tp => tp.Trait != null)
				.ToArray();

			if (idleCapturers.Length == 0)
				return;

			// Collect all targetable candidates across all eligible owners.
			var candidates = new List<Actor>();
			foreach (var otherPlayer in world.Players)
			{
				if (otherPlayer.Spectating)
					continue;
				if (!Info.CapturableRelationships.HasRelationship(player.RelationshipWith(otherPlayer)))
					continue;

				var actorPool = Info.CheckCaptureTargetsForVisibility
					? GetVisibleActorsBelongingToPlayer(otherPlayer)
					: GetActorsThatCanBeOrderedByPlayer(otherPlayer);

				foreach (var actor in actorPool)
				{
					if (Info.CapturableActorTypes.Count > 0
						&& !Info.CapturableActorTypes.Contains(actor.Info.Name.ToLowerInvariant()))
						continue;

					var cm = actor.TraitOrDefault<CaptureManager>();
					if (cm == null)
						continue;

					if (!idleCapturers.Any(tp => tp.Trait.CanTarget(cm)))
						continue;

					candidates.Add(actor);
				}
			}

			if (candidates.Count == 0)
				return;

			// Score every (capturer, candidate) pair; assign greedily by score.
			// We keep the per-capturer top-N candidates to avoid an N×M blow-up on big maps.
			var availableCapturers = new List<TraitPair<CaptureManager>>(idleCapturers);
			var alreadyTargetedThisTick = new HashSet<Actor>();

			// Track escorts already recruited THIS TICK so a second capturer doesn't
			// re-pick them (their AttackMove order is queued but hasn't applied yet,
			// so IsIdle is still true for the rest of this tick).
			var escortsRecruitedThisTick = new HashSet<Actor>();

			while (availableCapturers.Count > 0)
			{
				var capturer = availableCapturers[0];

				Actor bestTarget = null;
				long bestScore = long.MinValue;

				var considered = 0;
				foreach (var target in candidates.OrderByDescending(a => GetIncomeWeight(a)).Take(maximumCaptureTargetOptions))
				{
					if (alreadyTargetedThisTick.Contains(target))
						continue;

					var s = ScoreTarget(capturer.Actor, target);
					if (s > bestScore)
					{
						bestScore = s;
						bestTarget = target;
					}

					if (++considered >= maximumCaptureTargetOptions)
						break;
				}

				if (bestTarget == null)
					break;

				// Issue capture order.
				bot.QueueOrder(new Order("CaptureActor", capturer.Actor, Target.FromActor(bestTarget), true));
				activeCapturers.Add(capturer.Actor);
				alreadyTargetedThisTick.Add(bestTarget);

				// Recruit escort — fire-and-forget; if no escort available, capture proceeds alone.
				DispatchEscort(bot, capturer.Actor, bestTarget, escortsRecruitedThisTick);

				AIUtils.BotDebug("AI ({0}): v2-capture — {1} → {2} (score={3})",
					player.ClientIndex, capturer.Actor.Info.Name, bestTarget.Info.Name, bestScore);

				availableCapturers.RemoveAt(0);
			}
		}

		long ScoreTarget(Actor capturer, Actor target)
		{
			// Income value — flat lookup, baseline from YAML.
			var income = GetIncomeWeight(target);

			// Distance decay. distFactor in [10, 1000]; closer = higher.
			var distCells = Math.Max(1, (target.CenterPosition - capturer.CenterPosition).Length / 1024);
			var halfLife = Math.Max(1, Info.DistanceHalfLifeCells);
			// distFactor = halfLife * 100 / (halfLife + distCells)  →  at distCells=halfLife: 50; at distCells=0: 100.
			var distFactor = halfLife * 100 / (halfLife + distCells);

			// Safety — count enemies near the target.
			var safetyRadius = WDist.FromCells(Info.SafetyEnemyScanRadiusCells);
			var nearbyEnemies = world.FindActorsInCircle(target.CenterPosition, safetyRadius)
				.Count(a => !a.IsDead && a.IsInWorld
					&& player.RelationshipWith(a.Owner) == PlayerRelationship.Enemy
					&& a.Info.HasTraitInfo<ITargetableInfo>());

			int safetyFactor;
			if (nearbyEnemies == 0)
				safetyFactor = Info.SafetyMultiplierSafe;
			else if (nearbyEnemies <= 2)
				safetyFactor = Info.SafetyMultiplierMild;
			else
				safetyFactor = Info.SafetyMultiplierHostile;

			// Combined long score keeps headroom for big maps.
			return (long)income * distFactor * safetyFactor;
		}

		int GetIncomeWeight(Actor target)
		{
			var name = target.Info.Name.ToLowerInvariant();
			return Info.IncomeWeights.TryGetValue(name, out var v) ? v : Info.DefaultIncomeWeight;
		}

		void DispatchEscort(IBot bot, Actor capturer, Actor target, HashSet<Actor> alreadyRecruited)
		{
			if (Info.EscortSize <= 0)
				return;

			var recruits = FindIdleSupportersNear(capturer.CenterPosition, Info.EscortSize, alreadyRecruited);
			if (recruits.Length == 0)
				return;

			bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(world, target.Location), false, groupedActors: recruits));

			foreach (var r in recruits)
				alreadyRecruited.Add(r);

			AIUtils.BotDebug("AI ({0}): v2-capture — escort dispatched ({1} units → {2})",
				player.ClientIndex, recruits.Length, target.Info.Name);
		}

		// ============================================================
		// DEFENSE PASS
		// ============================================================

		void QueueDefenseOrders(IBot bot)
		{
			// Own capturables (must have CaptureManager AND a relevant income role).
			var owned = world.Actors
				.Where(a => a.Owner == player && !a.IsDead && a.IsInWorld
					&& (Info.CapturableActorTypes.Count == 0
						|| Info.CapturableActorTypes.Contains(a.Info.Name.ToLowerInvariant()))
					&& a.Info.HasTraitInfo<CaptureManagerInfo>())
				.ToList();

			if (owned.Count == 0)
				return;

			var enemyRadius = WDist.FromCells(Info.DefenseEnemyScanRadiusCells);
			var friendlyRadius = WDist.FromCells(Info.DefenseFriendlyScanRadiusCells);

			foreach (var structure in owned)
			{
				var enemies = world.FindActorsInCircle(structure.CenterPosition, enemyRadius)
					.Where(a => !a.IsDead && a.IsInWorld
						&& player.RelationshipWith(a.Owner) == PlayerRelationship.Enemy)
					.ToList();

				if (enemies.Count == 0)
					continue;

				var enemyValue = enemies.Sum(a => a.GetSellValue());
				if (enemyValue < Info.DefenseEnemyValueThreshold)
					continue;

				var friendlies = world.FindActorsInCircle(structure.CenterPosition, friendlyRadius)
					.Where(a => !a.IsDead && a.IsInWorld
						&& a.Owner == player
						&& !Info.CapturingActorTypes.Contains(a.Info.Name))
					.ToList();
				var friendlyValue = friendlies.Sum(a => a.GetSellValue());

				if (friendlyValue >= enemyValue)
					continue;

				var defenders = FindIdleSupportersNear(structure.CenterPosition, Info.DefenseSummonCount);
				if (defenders.Length == 0)
					continue;

				bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(world, structure.Location), false, groupedActors: defenders));
				foreach (var d in defenders)
					defenderBookings[d] = world.WorldTick;

				AIUtils.BotDebug("AI ({0}): v2-capture — defense summoned ({1} units → {2}, enemyVal={3})",
					player.ClientIndex, defenders.Length, structure.Info.Name, enemyValue);
			}
		}

		// ============================================================
		// SHARED HELPERS
		// ============================================================

		Actor[] FindIdleSupportersNear(WPos around, int wantCount, HashSet<Actor> exclude = null)
		{
			if (wantCount <= 0)
				return Array.Empty<Actor>();

			var recruitRadius = WDist.FromCells(Info.SupportRecruitRadiusCells);

			IEnumerable<Actor> pool = world.FindActorsInCircle(around, recruitRadius)
				.Where(a => !a.IsDead && a.IsInWorld
					&& a.Owner == player
					&& a.IsIdle
					&& !defenderBookings.ContainsKey(a)
					&& (exclude == null || !exclude.Contains(a))
					&& !Info.CapturingActorTypes.Contains(a.Info.Name)
					&& a.Info.HasTraitInfo<IPositionableInfo>()
					&& a.Info.HasTraitInfo<AttackBaseInfo>());

			if (Info.SupportingUnitTypes.Count > 0)
				pool = pool.Where(a => Info.SupportingUnitTypes.Contains(a.Info.Name));

			return pool
				.OrderBy(a => (a.CenterPosition - around).LengthSquared)
				.Take(wantCount)
				.ToArray();
		}

		IEnumerable<Actor> GetVisibleActorsBelongingToPlayer(Player owner)
		{
			foreach (var actor in GetActorsThatCanBeOrderedByPlayer(owner))
				if (actor.CanBeViewedByPlayer(player))
					yield return actor;
		}

		IEnumerable<Actor> GetActorsThatCanBeOrderedByPlayer(Player owner)
		{
			foreach (var actor in world.Actors)
				if (actor.Owner == owner && !actor.IsDead && actor.IsInWorld)
					yield return actor;
		}

		void INotifyActorDisposing.Disposing(Actor self)
		{
			capturingActors.Dispose();
		}
	}
}
