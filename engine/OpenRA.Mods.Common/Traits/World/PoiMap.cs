#region Copyright & License Information
/*
 * WW3MOD PoiMap — v2 AI, POI-strategy Phase 2.
 *
 * A world-level POINT-OF-INTEREST layer: it discovers the map locations worth
 * reasoning about strategically — money-granting capturables (oil derrick,
 * expansion post, reactor...), neutral AND enemy Supply Routes (as DENY targets
 * per decision #2: capturing an enemy SR flips it Neutral, cutting their
 * reinforcement lane — never a lane for us), and the enemy base anchor — then
 * SCORES each from a given player's perspective by value x distance x threat.
 *
 * The scored list is the single source of "what to go for next". Consumers:
 *   - CaptureCoordinatorBotModule reads GetCaptureTargets() for capture ordering
 *     (replacing its own per-target scan).
 *   - Phase 3's offensive/axis module will read GetScoredPois() for spread
 *     offense — the interface is shaped so it can consume this WITHOUT rework
 *     (each POI already carries a suggested PoiAction + score).
 *
 * DESIGN INTENT (v3-portable, same pattern as GoalGuardLedger / InfluenceMapMath):
 * all scoring math lives in the pure, engine-free PoiScoring class so it ports
 * VERBATIM into a future v3 brain; only the discovery/plumbing (this trait) is
 * OpenRA-specific. Threat is sourced from the existing InfluenceMap world trait
 * (with a FindActorsInCircle fallback), not a bespoke scan.
 *
 * Gated for the v2 bot at the CONSUMER (ai.yaml wires the trait, but only v2
 * modules query it). Normal / Rush / Turtle never read it, so their behaviour
 * is unchanged even though the trait exists on the world.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public enum PoiKind
	{
		IncomeStructure,
		UtilityStructure,
		SupplyRoute,
	}

	// Suggested action for a POI from the querying player's perspective. Capture
	// consumers filter to Capture/DenyCapture; offense (Phase 3) reads Pressure/
	// Attack; defense reads Defend.
	public enum PoiAction
	{
		Capture,      // neutral/enemy income or utility structure — take it
		DenyCapture,  // neutral/enemy Supply Route — capture-to-neutralize (deny)
		Defend,       // a POI we already own — garrison target
		Pressure,     // enemy Supply Route circle — park units to slow production (Phase 3)
	}

	public readonly struct ScoredPoi
	{
		public readonly Actor Actor;
		public readonly CPos Location;
		public readonly WPos CenterPosition;
		public readonly PoiKind Kind;
		public readonly PoiAction Action;
		public readonly int Value;            // base value weight (income $ / SR deny weight)
		public readonly int DistanceCells;    // from the perspective player's own SR
		public readonly int EnemyInfluence;   // sampled at the POI's cell
		public readonly long Score;

		public ScoredPoi(Actor actor, PoiKind kind, PoiAction action, int value,
			int distanceCells, int enemyInfluence, long score)
		{
			Actor = actor;
			Location = actor.Location;
			CenterPosition = actor.CenterPosition;
			Kind = kind;
			Action = action;
			Value = value;
			DistanceCells = distanceCells;
			EnemyInfluence = enemyInfluence;
			Score = score;
		}
	}

	[Desc("WW3MOD v2 AI: discovers + scores strategic points of interest (money capturables,",
		"neutral/enemy Supply Routes as deny targets, enemy base). Perspective-scored by",
		"value x distance x threat. Queried by CaptureCoordinatorBotModule (capture ordering)",
		"and the Phase 3 offense module. Pure math lives in PoiScoring for v3 portability.")]
	public class PoiMapInfo : TraitInfo
	{
		[Desc("Per-actor-type value weights (lowercased actor name). Income structures use their",
			"CashTrickler-equivalent weight; only listed types are discovered as income POIs.")]
		public readonly Dictionary<string, int> IncomeWeights = new();

		[Desc("Value weight for a Supply Route POI (deny target). Cutting an enemy reinforcement",
			"lane is high-value; tuned between FCOM (100) and BIO (150).")]
		public readonly int SupplyRouteDenyValue = 120;

		[Desc("Lowercased actor name of the Supply Route type, discovered as a deny POI.")]
		public readonly string SupplyRouteActorType = "supplyroute";

		[Desc("Cells over which the distance score halves (closer = higher). Measured from the",
			"perspective player's own SR — units walk in from the edge near it.")]
		public readonly int DistanceHalfLifeCells = 20;

		[Desc("Enemy-influence value at/below which a POI counts as MILD threat (above 0). At or",
			"below 0 is SAFE; above this is HOSTILE. Sampled from InfluenceMap enemy layer.")]
		public readonly int ThreatMildThreshold = 20;

		[Desc("Threat multiplier (x100) when no enemy influence at the POI.")]
		public readonly int ThreatSafeMultiplier = 100;

		[Desc("Threat multiplier (x100) at mild enemy influence.")]
		public readonly int ThreatMildMultiplier = 40;

		[Desc("Threat multiplier (x100) at hostile enemy influence.")]
		public readonly int ThreatHostileMultiplier = 10;

		[Desc("Ownership multiplier (x100) for a NEUTRAL income/utility POI (undefended → prefer).")]
		public readonly int OwnershipNeutralIncomeMultiplier = 100;

		[Desc("Ownership multiplier (x100) for an ENEMY-owned income/utility POI (defended → lower).")]
		public readonly int OwnershipEnemyIncomeMultiplier = 70;

		[Desc("Ownership multiplier (x100) for an ENEMY Supply Route (the prize — cuts their lane).")]
		public readonly int OwnershipEnemySupplyRouteMultiplier = 100;

		[Desc("Ownership multiplier (x100) for a NEUTRAL Supply Route (forward hold, lower urgency).")]
		public readonly int OwnershipNeutralSupplyRouteMultiplier = 70;

		[Desc("Radius (cells) used by the FindActorsInCircle threat fallback when InfluenceMap is",
			"unavailable — each nearby enemy contributes 10 influence.")]
		public readonly int ThreatFallbackRadiusCells = 6;

		[Desc("Ticks between POI discovery refreshes. Scoring is recomputed per query on the",
			"cached candidate set, so this only bounds how often the actor scan runs.")]
		public readonly int DiscoveryInterval = 50;

		public override object Create(ActorInitializer init) { return new PoiMap(init.Self, this); }
	}

	public class PoiMap : ITick, IWorldLoaded
	{
		public readonly PoiMapInfo Info;
		readonly World world;

		// Discovered candidates (owner-agnostic): rebuilt every DiscoveryInterval.
		readonly List<Actor> candidates = new();
		int discoveryCountdown;

		InfluenceMap influenceMap;
		bool influenceResolved;

		public PoiMap(Actor self, PoiMapInfo info)
		{
			Info = info;
			world = self.World;
		}

		void IWorldLoaded.WorldLoaded(World w, OpenRA.Graphics.WorldRenderer wr)
		{
			discoveryCountdown = w.SharedRandom.Next(0, Math.Max(1, Info.DiscoveryInterval));
			Discover();
		}

		void ITick.Tick(Actor self)
		{
			if (--discoveryCountdown > 0)
				return;

			discoveryCountdown = Math.Max(1, Info.DiscoveryInterval);
			Discover();
		}

		// Owner-agnostic discovery: which actors on the map are POIs at all. The
		// per-perspective owner/action/score is derived later in ScoreFor so a
		// captured POI flips role (Capture → Defend) without a re-scan.
		void Discover()
		{
			candidates.Clear();
			foreach (var actor in world.Actors)
			{
				if (actor.IsDead || !actor.IsInWorld || actor.Owner == null)
					continue;

				var name = actor.Info.Name.ToLowerInvariant();
				var isIncome = Info.IncomeWeights.ContainsKey(name);
				var isSupplyRoute = name == Info.SupplyRouteActorType;
				if (!isIncome && !isSupplyRoute)
					continue;

				// Income/utility POIs must be capturable to be actionable by the
				// capture layer. Supply Routes are strategic POIs (deny / pressure)
				// even though the SUPPLYROUTE actor currently has no CaptureManager
				// — they're still discovered + scored so the offense/pressure layer
				// (Phase 3) can consume them; the capture consumer harmlessly skips
				// a non-capturable target. See DOCS/reference/supply-route.md.
				if (isIncome && !actor.Info.HasTraitInfo<CaptureManagerInfo>())
					continue;

				candidates.Add(actor);
			}
		}

		// ---------- Public query API ----------

		/// <summary>All POIs scored from `perspective`, best first. Includes own-owned POIs
		/// tagged Defend so a defense/garrison consumer can read the same list.</summary>
		public List<ScoredPoi> GetScoredPois(Player perspective)
		{
			ResolveInfluence();

			var ownSr = FindOwnSupplyRoute(perspective);
			var enemyLayer = influenceMap?.GetEnemyInfluence(perspective);

			var result = new List<ScoredPoi>(candidates.Count);
			foreach (var actor in candidates)
			{
				if (actor.IsDead || !actor.IsInWorld)
					continue;

				if (TryScore(actor, perspective, ownSr, enemyLayer, out var scored))
					result.Add(scored);
			}

			result.Sort(CompareScoredPoi);
			return result;
		}

		/// <summary>Capture-actionable POIs only (Capture + DenyCapture), best first.
		/// This is what CaptureCoordinatorBotModule iterates for target ordering.</summary>
		public List<ScoredPoi> GetCaptureTargets(Player perspective)
			=> GetScoredPois(perspective)
				.Where(p => p.Action == PoiAction.Capture || p.Action == PoiAction.DenyCapture)
				.ToList();

		// Deterministic ordering: score desc, then nearer, then lower ActorID.
		static int CompareScoredPoi(ScoredPoi a, ScoredPoi b)
			=> PoiScoring.CompareForOrder(a.Score, a.DistanceCells, a.Actor.ActorID,
				b.Score, b.DistanceCells, b.Actor.ActorID);

		bool TryScore(Actor actor, Player perspective, Actor ownSr, int[,] enemyLayer, out ScoredPoi scored)
		{
			scored = default;

			var name = actor.Info.Name.ToLowerInvariant();
			var isSupplyRoute = name == Info.SupplyRouteActorType;
			var kind = isSupplyRoute ? PoiKind.SupplyRoute : PoiKind.IncomeStructure;
			var rel = perspective.RelationshipWith(actor.Owner);

			// Decide the action + skip cases.
			PoiAction action;
			if (rel == PlayerRelationship.Ally || actor.Owner == perspective)
			{
				// Own SR is existential-defense (handled elsewhere) and never a POI
				// target for the capture/offense layers — drop it. Own income
				// structures ARE defend targets.
				if (isSupplyRoute)
					return false;

				action = PoiAction.Defend;
			}
			else if (isSupplyRoute)
			{
				action = PoiAction.DenyCapture;
			}
			else
			{
				action = PoiAction.Capture;
			}

			var value = isSupplyRoute
				? Info.SupplyRouteDenyValue
				: (Info.IncomeWeights.TryGetValue(name, out var w) ? w : 0);
			if (value <= 0)
				return false;

			var distCells = ownSr != null
				? (actor.CenterPosition - ownSr.CenterPosition).Length / 1024
				: 0;
			var distFactor = PoiScoring.DistanceFactor(distCells, Info.DistanceHalfLifeCells);

			var enemyInfluence = SampleThreat(actor, perspective, enemyLayer);
			var threatFactor = PoiScoring.ThreatFactor(enemyInfluence, Info.ThreatMildThreshold,
				Info.ThreatSafeMultiplier, Info.ThreatMildMultiplier, Info.ThreatHostileMultiplier);

			var ownershipMul = PoiScoring.OwnershipMultiplier(kind, rel,
				Info.OwnershipNeutralIncomeMultiplier, Info.OwnershipEnemyIncomeMultiplier,
				Info.OwnershipNeutralSupplyRouteMultiplier, Info.OwnershipEnemySupplyRouteMultiplier);

			// Own POIs (Defend) keep their raw value ordering but sidestep threat/ownership
			// gating — they're not a "go capture" decision. Give them a neutral factor so
			// they sort by value/distance for a defense consumer.
			var score = action == PoiAction.Defend
				? PoiScoring.Score(value, distFactor, Info.ThreatSafeMultiplier, Info.OwnershipNeutralIncomeMultiplier)
				: PoiScoring.Score(value, distFactor, threatFactor, ownershipMul);

			scored = new ScoredPoi(actor, kind, action, value, distCells, enemyInfluence, score);
			return true;
		}

		int SampleThreat(Actor actor, Player perspective, int[,] enemyLayer)
		{
			if (enemyLayer != null && influenceMap != null)
			{
				var (gx, gy) = influenceMap.MapCellToGridCell(actor.Location);
				if (gx >= 0 && gx < influenceMap.GridWidth && gy >= 0 && gy < influenceMap.GridHeight)
					return enemyLayer[gx, gy];
				return 0;
			}

			// Fallback: count nearby enemies, each worth 10 influence.
			var radius = WDist.FromCells(Info.ThreatFallbackRadiusCells);
			var enemies = world.FindActorsInCircle(actor.CenterPosition, radius)
				.Count(a => !a.IsDead && a.IsInWorld
					&& perspective.RelationshipWith(a.Owner) == PlayerRelationship.Enemy
					&& a.Info.HasTraitInfo<ITargetableInfo>());
			return enemies * 10;
		}

		Actor FindOwnSupplyRoute(Player perspective)
		{
			Actor best = null;
			foreach (var actor in world.Actors)
			{
				if (actor.IsDead || !actor.IsInWorld || actor.Owner != perspective)
					continue;
				if (actor.Info.Name.ToLowerInvariant() != Info.SupplyRouteActorType)
					continue;

				best = actor;
				break;
			}

			return best;
		}

		void ResolveInfluence()
		{
			if (influenceResolved)
				return;

			influenceMap = world.WorldActor.TraitOrDefault<InfluenceMap>();
			influenceResolved = true;
		}
	}

	// ============================================================
	// Pure scoring — engine-free, unit-tested (PoiMapTest). Ports verbatim to v3.
	// ============================================================
	public static class PoiScoring
	{
		/// <summary>Distance decay in (0,100]: 100 at distance 0, 50 at halfLife, →0 far out.</summary>
		public static int DistanceFactor(int distCells, int halfLifeCells)
		{
			var hl = Math.Max(1, halfLifeCells);
			var d = Math.Max(0, distCells);
			return hl * 100 / (hl + d);
		}

		/// <summary>Threat bucket on enemy influence at the POI cell: safe (≤0), mild
		/// (≤mildThreshold), hostile (above). Returns the corresponding x100 multiplier.</summary>
		public static int ThreatFactor(int enemyInfluence, int mildThreshold,
			int safeMul, int mildMul, int hostileMul)
		{
			if (enemyInfluence <= 0)
				return safeMul;
			if (enemyInfluence <= mildThreshold)
				return mildMul;
			return hostileMul;
		}

		/// <summary>Ownership preference. Neutral income &gt; enemy income (enemy is defended);
		/// enemy SR &gt; neutral SR (capturing the enemy's SR cuts their reinforcement lane).</summary>
		public static int OwnershipMultiplier(PoiKind kind, PlayerRelationship rel,
			int neutralIncomeMul, int enemyIncomeMul, int neutralSrMul, int enemySrMul)
		{
			var enemy = rel == PlayerRelationship.Enemy;
			if (kind == PoiKind.SupplyRoute)
				return enemy ? enemySrMul : neutralSrMul;

			return enemy ? enemyIncomeMul : neutralIncomeMul;
		}

		/// <summary>Combined POI score. Long keeps headroom on big maps.</summary>
		public static long Score(int value, int distanceFactor, int threatFactor, int ownershipMul)
			=> (long)value * distanceFactor * threatFactor * ownershipMul;

		/// <summary>Deterministic POI ordering used for the scored list: higher score first,
		/// then nearer, then lower id. Pure so the tie-break is unit-testable + v3-portable.</summary>
		public static int CompareForOrder(long scoreA, int distA, uint idA, long scoreB, int distB, uint idB)
		{
			var c = scoreB.CompareTo(scoreA);
			if (c != 0)
				return c;

			c = distA.CompareTo(distB);
			if (c != 0)
				return c;

			return idA.CompareTo(idB);
		}
	}
}
