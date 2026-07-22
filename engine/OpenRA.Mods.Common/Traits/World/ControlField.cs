#region Copyright & License Information
/*
 * WW3MOD influence stack — Stage C: control field (per-player believed territory).
 *
 * The full-map ownership layer (design §2C, WORKSPACE/plans/260722_influence_stack_design.md).
 * Per participating player, every coarse grid cell carries a believed-ownership SCORE from that
 * player's point of view:
 *   score  > 0  → believed OURS  (magnitude = how firmly held)
 *   score  < 0  → believed ENEMY
 *   |score| small (within a gray band) → CONTESTED / grayzone
 *
 * Lifecycle (commander's-view, mirrors the belief store's usefulness-over-purity rule):
 *   - SEED: Voronoi by proximity to each player's home beachhead at first recompute — every
 *     cell is coloured from tick 0 (nearest home wins; equidistant reads contested midline).
 *   - PRESENCE: own/allied combat units paint ownership toward +; believed enemy contacts erode
 *     it toward − and eventually FLIP the sign (capture). A cell with BOTH present is contested.
 *   - PERSISTENCE: when a cell has no evidence (units left into fog) ownership LINGERS, decaying
 *     slowly so the front does not flicker.
 *   - VERIFIED-CLEAR: a cell currently observed and empty relaxes to grayzone immediately (a
 *     commander does not claim ground he can see is empty) — the same rule the belief store uses.
 *   - ANCHORS: home beachheads (public spawn positions) and owned/believed-enemy site structures
 *     (Supply Routes, derricks) re-assert a floor of ownership every recompute, so territory
 *     stays pinned to ground and roaming armies cannot drag the whole map with them.
 *
 * VERIFICATION MEMORY: alongside the score, each cell records the tick it was last observed. The
 * overlay's "verified safe" (green) reading is perishable — it relaxes to gray after a staleness
 * window — and this is where that timestamp lives. Grid-coarse, sim-side, sync-safe.
 *
 * DETERMINISM / FOG: pure integer math; enemy evidence comes only from the fog-legal belief store;
 * own units are always legally known; home/anchor positions are public map facts. Never reads
 * LocalRandom or RenderPlayer — SharedRandom only staggers the first tick.
 *
 * PERF (§6): coarse grid (InfluenceMap granularity) + round-robin per-player recompute via
 * InfluenceStack — one participant per sub-slot, never the whole map on one tick.
 *
 * INERT IN STAGE C: pure data + a render-only overlay. NOTHING consumes the field for behaviour;
 * @experimental strategy (Stage D+) and the DangerFieldLayer territory-baseline seam read it.
 * Control bots (Normal/Rush/Turtle) and @stable never touch this path — byte-identical for them.
 */
#endregion

using System;
using System.Collections.Generic;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public enum ControlOwner { Own, Enemy, Contested }

	// Per-cell evidence gathered for one recompute. Pure input to the ownership update.
	public readonly struct ControlEvidence
	{
		public readonly bool SelfPresent;
		public readonly bool EnemyPresent;
		public readonly bool VerifiedClear;   // currently observed AND empty of both.

		public ControlEvidence(bool selfPresent, bool enemyPresent, bool verifiedClear)
		{
			SelfPresent = selfPresent;
			EnemyPresent = enemyPresent;
			VerifiedClear = verifiedClear;
		}
	}

	// Tunables snapshotted from the Info so the pure math needs no trait handle (mirrors
	// DangerKernelParams / UnitRoleThresholds).
	public readonly struct ControlParams
	{
		public readonly int SeedStrength;
		public readonly int MaxScore;
		public readonly int PresenceGain;
		public readonly int ContestErodePercent;
		public readonly int VerifiedClearErodePercent;
		public readonly int PersistDecayPercent;
		public readonly int GrayBand;

		public ControlParams(int seedStrength, int maxScore, int presenceGain, int contestErodePercent,
			int verifiedClearErodePercent, int persistDecayPercent, int grayBand)
		{
			SeedStrength = seedStrength;
			MaxScore = maxScore;
			PresenceGain = presenceGain;
			ContestErodePercent = contestErodePercent;
			VerifiedClearErodePercent = verifiedClearErodePercent;
			PersistDecayPercent = persistDecayPercent;
			GrayBand = grayBand;
		}
	}

	// The pure, engine-free ownership math. Split from the trait (mirroring PoiScoring /
	// DangerKernelMath / UnitRoleResolver.Classify) so the lifecycle the NUnit table pins —
	// seeding, capture-flip, grayzone, anchors — is unit-testable without mounting a world.
	public static class ControlFieldMath
	{
		/// <summary>Voronoi seed score for one cell from its nearest self/enemy home (squared
		/// grid distances; use int.MaxValue for "no such seed"). Nearer home owns it; a tie is
		/// the contested midline (0).</summary>
		public static int SeedScore(int nearestSelfDistSq, int nearestEnemyDistSq, int seedStrength)
		{
			if (nearestSelfDistSq == int.MaxValue && nearestEnemyDistSq == int.MaxValue)
				return 0;

			if (nearestSelfDistSq < nearestEnemyDistSq)
				return seedStrength;
			if (nearestEnemyDistSq < nearestSelfDistSq)
				return -seedStrength;

			return 0;
		}

		/// <summary>Advance one cell's ownership by a recompute's evidence.</summary>
		public static int UpdateScore(int current, ControlEvidence ev, in ControlParams p)
		{
			// Both sides present: actively contested — bleed toward the grayzone.
			if (ev.SelfPresent && ev.EnemyPresent)
				return Decay(current, p.ContestErodePercent);

			// Own units hold/paint the ground.
			if (ev.SelfPresent)
				return Clamp(current + p.PresenceGain, -p.MaxScore, p.MaxScore);

			// Believed enemy erodes and eventually flips the sign (capture).
			if (ev.EnemyPresent)
				return Clamp(current - p.PresenceGain, -p.MaxScore, p.MaxScore);

			// Observed empty: a commander does not claim ground he can see is clear ⇒ gray now.
			if (ev.VerifiedClear)
				return Decay(current, p.VerifiedClearErodePercent);

			// No evidence (units left into fog): ownership lingers, fading slowly — no flicker.
			return Decay(current, p.PersistDecayPercent);
		}

		/// <summary>Re-assert a site/home anchor's ownership floor over a cell. Self anchors floor
		/// the score at +strength, enemy anchors cap it at −strength — so an anchored cell never
		/// fades or flips from mere presence/decay unless the anchor itself is lost.</summary>
		public static int ApplyAnchor(int current, int anchorStrength, bool self)
		{
			if (anchorStrength <= 0)
				return current;

			return self ? Math.Max(current, anchorStrength) : Math.Min(current, -anchorStrength);
		}

		/// <summary>Bucket a score into the tri-state ownership read.</summary>
		public static ControlOwner Classify(int score, int grayBand)
		{
			if (score > grayBand)
				return ControlOwner.Own;
			if (score < -grayBand)
				return ControlOwner.Enemy;

			return ControlOwner.Contested;
		}

		// Decay magnitude toward zero by `percent` (integer, truncates toward zero for both signs).
		static int Decay(int v, int percent)
		{
			if (percent <= 0)
				return v;
			if (percent >= 100)
				return 0;

			return v * (100 - percent) / 100;
		}

		static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
	}

	[TraitLocation(SystemActors.World)]
	[Desc("WW3MOD influence stack Stage C: per-player believed-territory (control) field.",
		"Voronoi-seeded full-map ownership with capture/persistence/grayzone semantics + site anchors.",
		"Pure data — read by the danger-field territory baseline + the Stage-C overlay (render-only).")]
	public class ControlFieldInfo : TraitInfo
	{
		[Desc("Map cells per coarse grid cell (matches InfluenceMap granularity, §6).")]
		public readonly int CellSize = 2;

		[Desc("Ticks between full per-player refreshes. Recomputes are round-robin-staggered",
			"across participants so no single tick rebuilds every field (§6).")]
		public readonly int UpdateInterval = 25;

		[Desc("Ownership magnitude assigned to the nearer side by the tick-0 Voronoi seed.")]
		public readonly int SeedStrength = 500;

		[Desc("Ownership score clamp (both signs). Presence gains saturate here.")]
		public readonly int MaxScore = 1000;

		[Desc("Ownership pushed toward the present side each recompute a cell is held/contested.")]
		public readonly int PresenceGain = 250;

		[Desc("Percent of ownership bled toward gray each recompute a cell is actively contested",
			"(both sides present).")]
		public readonly int ContestErodePercent = 40;

		[Desc("Percent of ownership shed when a cell is observed empty (verified-clear ⇒ grayzone).",
			"100 = relax to contested immediately, per the commander's-view rule.")]
		public readonly int VerifiedClearErodePercent = 100;

		[Desc("Percent of ownership lost each recompute a cell has no evidence (units left into fog).",
			"Small ⇒ territory lingers without flicker.")]
		public readonly int PersistDecayPercent = 8;

		[Desc("|score| at or below this reads CONTESTED (grayzone) rather than owned.")]
		public readonly int GrayBand = 150;

		[Desc("Ownership floor a home/site anchor re-asserts at its centre each recompute.")]
		public readonly int AnchorStrength = 800;

		[Desc("Radius (grid cells) an anchor's ownership floor spreads over, tapering linearly.")]
		public readonly int AnchorRadiusCells = 4;

		[Desc("Ticks after which a cell's 'verified' observation goes stale — the overlay's green",
			"(verified-safe) reading relaxes to gray past this window. Perishable intel.")]
		public readonly int StalenessWindow = 500;

		public override object Create(ActorInitializer init) { return new ControlField(init.Self, this); }
	}

	public class ControlField : ITick, IWorldLoaded
	{
		sealed class PlayerControl
		{
			public readonly int[,] Score;
			public readonly int[,] LastVerified;   // ControlField tick a cell was last observed; 0 = never.
			public bool Seeded;

			public PlayerControl(int w, int h)
			{
				Score = new int[w, h];
				LastVerified = new int[w, h];
			}
		}

		public readonly ControlFieldInfo Info;
		readonly World world;
		readonly ControlParams p;
		readonly Dictionary<Player, PlayerControl> fields = new();
		readonly List<Player> participants = new();
		readonly HashSet<long> selfCells = new();
		readonly HashSet<long> enemyCells = new();
		HashSet<string> siteAnchorTypes;

		BeliefStore beliefStore;
		int gridWidth, gridHeight;
		int subCountdown;
		int cursor = -1;
		int tick;

		public ControlField(Actor self, ControlFieldInfo info)
		{
			Info = info;
			world = self.World;
			p = new ControlParams(info.SeedStrength, info.MaxScore, info.PresenceGain,
				info.ContestErodePercent, info.VerifiedClearErodePercent, info.PersistDecayPercent, info.GrayBand);
		}

		void IWorldLoaded.WorldLoaded(World w, OpenRA.Graphics.WorldRenderer wr)
		{
			var map = w.Map;
			gridWidth = (map.MapSize.X + Info.CellSize - 1) / Info.CellSize;
			gridHeight = (map.MapSize.Y + Info.CellSize - 1) / Info.CellSize;

			beliefStore = w.WorldActor.TraitOrDefault<BeliefStore>();

			// Site anchors: non-mobile structures that hold ground — Supply Routes (beachheads)
			// and capturable income/utility (derricks). Cached by name so belief contacts (which
			// carry only the type name) can be tested the same way as live own actors.
			siteAnchorTypes = new HashSet<string>();
			foreach (var ai in w.Map.Rules.Actors.Values)
			{
				if (ai.Name.StartsWith("^", StringComparison.Ordinal))
					continue;
				if (IsSiteAnchor(ai))
					siteAnchorTypes.Add(ai.Name);
			}

			// DETERMINISTIC stagger — NOT a SharedRandom draw. These always-on grids tick for every
			// profile, so any synced draw at load shifts the RNG stream for @stable/controls too and
			// breaks replay/benchmark byte-identity (see WORKSPACE/DISCOVERIES.md 2026-07-22). The
			// whole stack uses distinct fixed offsets instead — BeliefStore=0, DangerFieldLayer=
			// Interval/3, ControlField=Interval/2+1 — keeping the anti-collision stagger, zero RNG.
			subCountdown = Info.UpdateInterval / 2 + 1;
		}

		void ITick.Tick(Actor self)
		{
			tick++;
			if (--subCountdown > 0)
				return;

			InfluenceStack.GatherParticipants(world, participants);
			subCountdown = InfluenceStack.SubInterval(Info.UpdateInterval, participants.Count);
			if (participants.Count == 0)
				return;

			// Round-robin: one participant per sub-slot, so the whole participant set is refreshed
			// once per UpdateInterval without any tick rebuilding every field (§6).
			cursor = (cursor + 1) % participants.Count;
			RecomputePlayer(participants[cursor]);
		}

		void RecomputePlayer(Player player)
		{
			// Verified-clear + seeding both need the player's own vision map.
			if (player.MapLayers == null)
				return;

			if (!fields.TryGetValue(player, out var pc))
				fields[player] = pc = new PlayerControl(gridWidth, gridHeight);

			if (!pc.Seeded)
			{
				SeedVoronoi(player, pc);
				pc.Seeded = true;
			}

			GatherPresence(player);

			for (var gx = 0; gx < gridWidth; gx++)
			{
				for (var gy = 0; gy < gridHeight; gy++)
				{
					var key = Key(gx, gy);
					var self = selfCells.Contains(key);
					var enemy = enemyCells.Contains(key);
					var visible = GridCellVisible(player, gx, gy);

					// Any currently-observed cell is freshly "verified" for staleness; only fogged
					// cells age toward gray. Verified-CLEAR (the grayzone trigger) additionally
					// requires the cell be empty of both sides.
					if (visible)
						pc.LastVerified[gx, gy] = tick;

					pc.Score[gx, gy] = ControlFieldMath.UpdateScore(pc.Score[gx, gy],
						new ControlEvidence(self, enemy, visible && !self && !enemy), p);
				}
			}

			ApplyAnchors(player, pc);
		}

		// --- Seeding ------------------------------------------------------------------

		// Voronoi over the public home beachheads: nearest home (self/ally vs enemy) owns each cell.
		// Home positions are fixed spawn beachheads — public map facts, so seeding is not a fog leak.
		void SeedVoronoi(Player player, PlayerControl pc)
		{
			var selfHomes = new List<(int gx, int gy)>();
			var enemyHomes = new List<(int gx, int gy)>();
			foreach (var q in world.Players)
			{
				if (q.NonCombatant || q.Spectating)
					continue;

				var (hx, hy) = MapCellToGridCell(q.HomeLocation);
				if (q == player || player.RelationshipWith(q) == PlayerRelationship.Ally)
					selfHomes.Add((hx, hy));
				else if (player.RelationshipWith(q) == PlayerRelationship.Enemy)
					enemyHomes.Add((hx, hy));
			}

			for (var gx = 0; gx < gridWidth; gx++)
				for (var gy = 0; gy < gridHeight; gy++)
					pc.Score[gx, gy] = ControlFieldMath.SeedScore(
						NearestDistSq(gx, gy, selfHomes), NearestDistSq(gx, gy, enemyHomes), Info.SeedStrength);
		}

		static int NearestDistSq(int gx, int gy, List<(int gx, int gy)> homes)
		{
			var best = int.MaxValue;
			foreach (var (hx, hy) in homes)
			{
				var dx = gx - hx;
				var dy = gy - hy;
				var d = dx * dx + dy * dy;
				if (d < best)
					best = d;
			}

			return best;
		}

		// --- Presence gathering -------------------------------------------------------

		void GatherPresence(Player player)
		{
			selfCells.Clear();
			enemyCells.Clear();

			// Own + allied fighting units paint ownership (armed-only, mirroring InfluenceMap —
			// trucks/civilians do not hold ground).
			foreach (var actor in world.Actors)
			{
				if (actor.IsDead || !actor.IsInWorld || actor.Owner == null)
					continue;

				var rel = player.RelationshipWith(actor.Owner);
				if (actor.Owner != player && rel != PlayerRelationship.Ally)
					continue;

				if (!actor.Info.HasTraitInfo<AttackBaseInfo>() && !actor.Info.HasTraitInfo<AutoTargetInfo>())
					continue;

				var (gx, gy) = MapCellToGridCell(actor.Location);
				if (InGrid(gx, gy))
					selfCells.Add(Key(gx, gy));
			}

			// Enemy presence comes ONLY from the fog-legal belief store (assumed-still-there memory).
			if (beliefStore != null)
			{
				foreach (var contact in beliefStore.Contacts(player))
				{
					var (gx, gy) = MapCellToGridCell(contact.Cell);
					if (InGrid(gx, gy))
						enemyCells.Add(Key(gx, gy));
				}
			}
		}

		// --- Anchors ------------------------------------------------------------------

		void ApplyAnchors(Player player, PlayerControl pc)
		{
			// Home beachheads (public spawn positions) — permanent ownership pins.
			foreach (var q in world.Players)
			{
				if (q.NonCombatant || q.Spectating)
					continue;

				var self = q == player || player.RelationshipWith(q) == PlayerRelationship.Ally;
				var enemy = player.RelationshipWith(q) == PlayerRelationship.Enemy;
				if (!self && !enemy)
					continue;

				var (gx, gy) = MapCellToGridCell(q.HomeLocation);
				StampAnchor(pc, gx, gy, self);
			}

			// Own/allied site structures (Supply Routes, held derricks) — self anchors.
			foreach (var actor in world.Actors)
			{
				if (actor.IsDead || !actor.IsInWorld || actor.Owner == null)
					continue;
				if (actor.Owner != player && player.RelationshipWith(actor.Owner) != PlayerRelationship.Ally)
					continue;
				if (!siteAnchorTypes.Contains(actor.Info.Name))
					continue;

				var (gx, gy) = MapCellToGridCell(actor.Location);
				StampAnchor(pc, gx, gy, self: true);
			}

			// Believed-enemy site structures (fog-legal, from the belief store) — enemy anchors.
			if (beliefStore != null)
			{
				foreach (var contact in beliefStore.Contacts(player))
				{
					if (!contact.IsStatic || !siteAnchorTypes.Contains(contact.TypeName))
						continue;

					var (gx, gy) = MapCellToGridCell(contact.Cell);
					StampAnchor(pc, gx, gy, self: false);
				}
			}
		}

		void StampAnchor(PlayerControl pc, int cx, int cy, bool self)
		{
			var r = Info.AnchorRadiusCells;
			for (var dy = -r; dy <= r; dy++)
			{
				for (var dx = -r; dx <= r; dx++)
				{
					var d = Exts.ISqrt(dx * dx + dy * dy);
					if (d > r)
						continue;

					var gx = cx + dx;
					var gy = cy + dy;
					if (!InGrid(gx, gy))
						continue;

					var strength = Info.AnchorStrength * (r - d + 1) / (r + 1);
					pc.Score[gx, gy] = ControlFieldMath.ApplyAnchor(pc.Score[gx, gy], strength, self);
				}
			}
		}

		// --- Helpers ------------------------------------------------------------------

		static bool IsSiteAnchor(ActorInfo info)
		{
			if (info.HasTraitInfo<MobileInfo>() || info.HasTraitInfo<AircraftInfo>())
				return false;

			// Beachheads (SupplyProvider) and capturable income/utility structures (derricks) hold
			// ground; both carry a CaptureManager or provide supply.
			return info.HasTraitInfo<SupplyProviderInfo>() || info.HasTraitInfo<CaptureManagerInfo>();
		}

		bool GridCellVisible(Player player, int gx, int gy)
		{
			var cell = GridCellToMapCell(gx, gy);
			return world.Map.Contains(cell) && player.MapLayers.IsVisible(cell, 1);
		}

		bool InGrid(int gx, int gy) => gx >= 0 && gx < gridWidth && gy >= 0 && gy < gridHeight;

		long Key(int gx, int gy) => ((long)gx << 32) | (uint)gy;

		/// <summary>Grid cell containing the given map cell.</summary>
		public (int X, int Y) MapCellToGridCell(CPos mapCell) => (mapCell.X / Info.CellSize, mapCell.Y / Info.CellSize);

		/// <summary>Map cell at the centre of the given grid cell.</summary>
		public CPos GridCellToMapCell(int gx, int gy)
			=> new(gx * Info.CellSize + Info.CellSize / 2, gy * Info.CellSize + Info.CellSize / 2);

		public int GridWidth => gridWidth;
		public int GridHeight => gridHeight;

		// ---------- Public query API (overlay / Stage-C+ consumer seam) ----------

		PlayerControl FieldOrNull(Player player)
			=> player != null && fields.TryGetValue(player, out var f) ? f : null;

		public bool HasField(Player player) => FieldOrNull(player) != null;

		/// <summary>Believed ownership score at a grid cell (+ ours, − enemy). 0 when no field.</summary>
		public int ScoreAt(Player player, int gx, int gy)
		{
			var f = FieldOrNull(player);
			return f != null && InGrid(gx, gy) ? f.Score[gx, gy] : 0;
		}

		/// <summary>Tri-state believed ownership at a grid cell.</summary>
		public ControlOwner OwnerAt(Player player, int gx, int gy)
			=> ControlFieldMath.Classify(ScoreAt(player, gx, gy), Info.GrayBand);

		/// <summary>Was this grid cell observed recently enough to still count as "verified"?
		/// Perishable — false once the observation ages past StalenessWindow (the overlay's
		/// green→gray relax).</summary>
		public bool IsVerifiedFresh(Player player, int gx, int gy)
		{
			var f = FieldOrNull(player);
			if (f == null || !InGrid(gx, gy))
				return false;

			var last = f.LastVerified[gx, gy];
			return last > 0 && tick - last <= Info.StalenessWindow;
		}
	}
}
