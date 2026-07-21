#region Copyright & License Information
/*
 * WW3MOD strategic/tactical split — Phase 1, §3a.
 *
 * Per-player, FOG-RESPECTING sighting/threat layer. This is the "where has the
 * enemy been seen" field the tactical (L3) layer will consume in Phase 2 to
 * position units toward/away from known enemy direction.
 *
 * Unlike InfluenceMap / ThreatMapManager (both omniscient — they scan every
 * actor regardless of visibility), this layer is built STRICTLY from
 * per-player-legal, synced sources:
 *   - the player's own current vision (Actor.CanBeViewedByPlayer), and
 *   - the player's FrozenActorLayer (fog-correct last-seen snapshots).
 * So a human and a bot with the same vision get identical information — no
 * cheating, by construction.
 *
 * Data model: a decaying-memory field per player. Each recompute multiplies the
 * existing field down (DecayPercent) and re-injects fresh sightings, so recent
 * contacts dominate and stale ones fade. Two channels per cell:
 *   - EnemyIntensity  (+ a summed direction vector toward the sightings) —
 *     surfaced as ThreatIntensity / ThreatDirection.
 *   - FriendlyIntensity (own + visible allied combat units) — used only to
 *     derive the Phase-1 balance-of-power overlay (§3d). Always legal: you
 *     always know where your own units are.
 *
 * DETERMINISM (survey Q6): pure integer cell math, no LocalRandom (it is NOT in
 * the sync hash — a divergent read desyncs silently), never reads
 * RenderPlayer/LocalPlayer. SharedRandom is used only to stagger the initial
 * recompute tick (synced). All accumulation is additive ⇒ iteration order over
 * actors / frozen actors does not affect the result.
 *
 * PERF: CellLayer<T> + staggered N-tick recompute (the established cheap
 * pattern). Decay walks only an active-cell list, never the full map.
 *
 * Phase 1 is pure data: NOTHING consumes this for behavior yet. See
 * WORKSPACE/plans/260722_strategic_tactical_split_SPEC.md.
 */
#endregion

using System.Collections.Generic;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("WW3MOD Phase 1 (§3a): per-player, fog-respecting sighting/threat field.",
		"Built from own vision + FrozenActorLayer. Exposes ThreatIntensity/ThreatDirection.",
		"Pure data — no consumer yet (Phase 2 positioning executor reads it).")]
	public class SightingThreatLayerInfo : TraitInfo
	{
		[Desc("Ticks between recomputations. Staggered against other world grids.")]
		public readonly int UpdateInterval = 25;

		[Desc("Radius (in map cells) a single sighting spreads over, with linear falloff.")]
		public readonly int ContributionRadius = 4;

		[Desc("Intensity injected by an enemy unit the player can currently see.")]
		public readonly int FreshWeight = 100;

		[Desc("Intensity injected by a fog-frozen (last-seen) enemy snapshot.",
			"Lower than FreshWeight: remembered contact is weaker than a live one.")]
		public readonly int FrozenWeight = 60;

		[Desc("Percent of the previous field carried over each recompute (temporal decay).",
			"75 ⇒ a cell with no fresh sighting keeps 75% of its value each cycle.")]
		public readonly int DecayPercent = 75;

		[Desc("Cells whose intensity falls below this after decay are culled to zero",
			"and dropped from the active set.")]
		public readonly int MinIntensity = 8;

		[Desc("Only actors with an attack/auto-target trait contribute (mirrors InfluenceMap).",
			"Keeps the field a combat-threat signal, not a census of every crate and civilian.")]
		public readonly bool ArmedOnly = true;

		public override object Create(ActorInitializer init) { return new SightingThreatLayer(init.Self, this); }
	}

	public class SightingThreatLayer : ITick, IWorldLoaded
	{
		public struct SightingCell
		{
			public int EnemyIntensity;
			public int FriendlyIntensity;
			public int DirX;
			public int DirY;
		}

		sealed class PlayerField
		{
			public readonly CellLayer<SightingCell> Cells;
			public readonly List<CPos> ActiveCells = new();
			public readonly HashSet<CPos> ActiveSet = new();

			public PlayerField(Map map)
			{
				Cells = new CellLayer<SightingCell>(map);
			}

			public void MarkActive(CPos cell)
			{
				if (ActiveSet.Add(cell))
					ActiveCells.Add(cell);
			}
		}

		public readonly SightingThreatLayerInfo Info;
		readonly World world;

		readonly Dictionary<Player, PlayerField> fields = new();
		int updateCountdown;

		public SightingThreatLayer(Actor self, SightingThreatLayerInfo info)
		{
			Info = info;
			world = self.World;
		}

		void IWorldLoaded.WorldLoaded(World w, OpenRA.Graphics.WorldRenderer wr)
		{
			// Stagger the first fire so this doesn't recompute on the same tick as
			// InfluenceMap / ThreatMapManager. SharedRandom is synced.
			updateCountdown = w.SharedRandom.Next(0, Info.UpdateInterval);
		}

		void ITick.Tick(Actor self)
		{
			if (--updateCountdown > 0)
				return;

			updateCountdown = Info.UpdateInterval;
			Recompute();
		}

		void Recompute()
		{
			foreach (var player in world.Players)
			{
				if (player.NonCombatant || player.Spectating)
					continue;

				// Fog-correct last-seen memory requires a FrozenActorLayer + MapLayers.
				if (player.FrozenActorLayer == null || player.MapLayers == null)
					continue;

				if (!fields.TryGetValue(player, out var field))
				{
					field = new PlayerField(world.Map);
					fields[player] = field;
				}

				DecayField(field);
				InjectSightings(player, field);
			}
		}

		void DecayField(PlayerField field)
		{
			// Walk only the active cells, decay in place, and rebuild the active
			// set from the survivors — never a full-map scan.
			var survivors = new List<CPos>(field.ActiveCells.Count);
			field.ActiveSet.Clear();

			foreach (var cell in field.ActiveCells)
			{
				var data = field.Cells[cell];
				data.EnemyIntensity = data.EnemyIntensity * Info.DecayPercent / 100;
				data.FriendlyIntensity = data.FriendlyIntensity * Info.DecayPercent / 100;
				data.DirX = data.DirX * Info.DecayPercent / 100;
				data.DirY = data.DirY * Info.DecayPercent / 100;

				if (data.EnemyIntensity < Info.MinIntensity && data.FriendlyIntensity < Info.MinIntensity)
				{
					field.Cells[cell] = default;
					continue;
				}

				field.Cells[cell] = data;
				survivors.Add(cell);
				field.ActiveSet.Add(cell);
			}

			field.ActiveCells.Clear();
			field.ActiveCells.AddRange(survivors);
		}

		void InjectSightings(Player player, PlayerField field)
		{
			// Fresh: enemy combat actors the player can currently, legally see.
			foreach (var actor in world.Actors)
			{
				if (actor.IsDead || !actor.IsInWorld || actor.Owner == null)
					continue;

				var rel = player.RelationshipWith(actor.Owner);
				var isEnemy = rel == PlayerRelationship.Enemy;
				var isFriendly = actor.Owner == player || rel == PlayerRelationship.Ally;
				if (!isEnemy && !isFriendly)
					continue;

				if (Info.ArmedOnly && !actor.Info.HasTraitInfo<AttackBaseInfo>() && !actor.Info.HasTraitInfo<AutoTargetInfo>())
					continue;

				if (isEnemy)
				{
					// Enemy: only if currently visible under the player's own fog.
					if (!actor.CanBeViewedByPlayer(player))
						continue;

					Spread(field, actor.Location, Info.FreshWeight, enemy: true);
				}
				else
				{
					// Friendly: own units are always known; allied only if visible.
					if (actor.Owner != player && !actor.CanBeViewedByPlayer(player))
						continue;

					Spread(field, actor.Location, Info.FreshWeight, enemy: false);
				}
			}

			// Remembered: fog-frozen enemy snapshots (last-seen while under fog).
			// FrozenActorsInRegion(onlyVisible:true) returns exactly the stale copies
			// currently rendered under fog — the "was here, now hidden" sightings.
			foreach (var fa in player.FrozenActorLayer.FrozenActorsInRegion(world.Map.AllCells, onlyVisible: true))
			{
				if (!fa.IsValid || fa.Owner == null)
					continue;

				if (player.RelationshipWith(fa.Owner) != PlayerRelationship.Enemy)
					continue;

				if (Info.ArmedOnly && !fa.Info.HasTraitInfo<AttackBaseInfo>() && !fa.Info.HasTraitInfo<AutoTargetInfo>())
					continue;

				Spread(field, world.Map.CellContaining(fa.CenterPosition), Info.FrozenWeight, enemy: true);
			}
		}

		void Spread(PlayerField field, CPos origin, int weight, bool enemy)
		{
			var r = Info.ContributionRadius;
			for (var dy = -r; dy <= r; dy++)
			{
				for (var dx = -r; dx <= r; dx++)
				{
					var dist = System.Math.Abs(dx) + System.Math.Abs(dy);
					if (dist > r)
						continue;

					var cell = new CPos(origin.X + dx, origin.Y + dy);
					if (!field.Cells.Contains(cell))
						continue;

					// Linear falloff: full at origin, 1/(r+1) at the edge.
					var contribution = weight * (r - dist + 1) / (r + 1);
					if (contribution <= 0)
						continue;

					var data = field.Cells[cell];
					if (enemy)
					{
						data.EnemyIntensity += contribution;

						// Vector from this cell toward the sighting = origin - cell = (-dx, -dy),
						// weighted by contribution so nearer/stronger sightings dominate the bearing.
						data.DirX += -dx * contribution;
						data.DirY += -dy * contribution;
					}
					else
						data.FriendlyIntensity += contribution;

					field.Cells[cell] = data;
					field.MarkActive(cell);
				}
			}
		}

		// ---------- Public query API ----------

		PlayerField FieldOrNull(Player player)
		{
			return player != null && fields.TryGetValue(player, out var f) ? f : null;
		}

		/// <summary>Decaying enemy-sighting intensity at a cell, from the player's own intel.</summary>
		public int ThreatIntensity(Player player, CPos cell)
		{
			var field = FieldOrNull(player);
			if (field == null || !field.Cells.Contains(cell))
				return 0;

			return field.Cells[cell].EnemyIntensity;
		}

		/// <summary>Dominant bearing from a cell toward recent enemy sightings.
		/// WAngle.Zero when there is no recorded direction.</summary>
		public WAngle ThreatDirection(Player player, CPos cell)
		{
			var field = FieldOrNull(player);
			if (field == null || !field.Cells.Contains(cell))
				return WAngle.Zero;

			var data = field.Cells[cell];
			if (data.DirX == 0 && data.DirY == 0)
				return WAngle.Zero;

			return new WVec(data.DirX, data.DirY, 0).Yaw;
		}

		/// <summary>Friendly (own + visible allied) combat intensity at a cell.</summary>
		public int FriendlyIntensity(Player player, CPos cell)
		{
			var field = FieldOrNull(player);
			if (field == null || !field.Cells.Contains(cell))
				return 0;

			return field.Cells[cell].FriendlyIntensity;
		}

		public bool HasData(Player player, CPos cell)
		{
			var field = FieldOrNull(player);
			if (field == null || !field.Cells.Contains(cell))
				return false;

			var data = field.Cells[cell];
			return data.EnemyIntensity > 0 || data.FriendlyIntensity > 0;
		}

		/// <summary>Cells with any recorded intensity for this player. Empty when the
		/// player has no field yet. Intended for the §3d overlay and Phase-2 scans —
		/// avoids a full-map walk.</summary>
		public IReadOnlyList<CPos> ActiveCells(Player player)
		{
			var field = FieldOrNull(player);
			return field != null ? field.ActiveCells : System.Array.Empty<CPos>();
		}
	}
}
