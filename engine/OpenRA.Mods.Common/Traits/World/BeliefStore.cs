#region Copyright & License Information
/*
 * WW3MOD influence stack — Stage A: belief store (per-player contact memory).
 *
 * The substrate the whole influence stack reads. A per-player table of BELIEVED
 * enemy contacts — the commander's memory of where the enemy has been seen, not
 * ground truth. Generalises the engine's FrozenActorLayer (which persists
 * structures under fog) to mobile units, with confidence semantics on top.
 *
 * Commander's-view lifecycle (design §2A, WORKSPACE/plans/260722_influence_stack_design.md):
 *   - Sighting an enemy: record/update a contact (position, type, confidence=Fresh).
 *   - Losing visual on a MOBILE: contact persists at last-seen cell, confidence
 *     decays each recompute — "assumed still there, fading".
 *   - Losing visual on a STATIC (defence/structure): no decay — persists until
 *     verified gone. While a frozen ghost of it remains visible under fog the
 *     contact is refreshed at the Frozen confidence ceiling.
 *   - Observing the contact's cell and finding it empty (currently visible, no
 *     live actor, no frozen ghost): contact cleared — verified-clear ⇒ the danger
 *     stack reads that cell as gray immediately.
 *   - Seen moving: the live pass re-observes it at the new cell (same ActorID),
 *     so the vacated cell simply stops being covered.
 *
 * FOG DISCIPLINE (mirrors SightingThreatLayer): built STRICTLY from per-player-legal,
 * synced sources — the player's own current vision (Actor.CanBeViewedByPlayer) and
 * their FrozenActorLayer (fog-correct last-seen snapshots). A human and a bot with
 * the same vision get identical beliefs; no cheating, by construction.
 *
 * DETERMINISM: pure integer confidence math, keyed by synced ActorID; no LocalRandom,
 * never reads RenderPlayer/LocalPlayer. SharedRandom only staggers the first tick.
 * Upserts are last-write-same-value and removals are set-based, so iteration order
 * over actors / frozen actors does not affect the stored result.
 *
 * INERT IN STAGES A/B: this is pure data. NOTHING consumes it for behaviour. The
 * danger fields (Stage B) read it; @experimental strategy and the human overlay
 * (Stages C+) read those. Control bots (Normal/Rush/Turtle) and @stable never touch
 * this code path, so registering the trait is behaviour-inert for every profile.
 */
#endregion

using System.Collections.Generic;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	// A single believed enemy contact. Mutable — updated in place across recomputes.
	public sealed class BeliefContact
	{
		public readonly uint Key;         // synced enemy ActorID — stable identity across ticks.
		public string TypeName;           // ActorInfo.Name — the danger field maps this to cached kernel facts.
		public CPos Cell;                 // last-seen cell.
		public bool IsStatic;             // structure / immobile defence: exempt from decay.
		public int Confidence;            // 0..100. Fresh sighting = 100, frozen ghost = the Frozen ceiling.
		public int LastSeenTick;

		public BeliefContact(uint key) { Key = key; }
	}

	// The pure, engine-independent contact table for one player. Split out from the
	// trait (mirroring UnitRoleResolver's facts/Classify split) so the lifecycle —
	// the part the NUnit table pins — is unit-testable without mounting a world.
	public sealed class PlayerBeliefContacts
	{
		readonly Dictionary<uint, BeliefContact> byKey = new();
		readonly HashSet<uint> refreshedThisPass = new();
		readonly List<uint> removalScratch = new();

		public int Count => byKey.Count;
		public IReadOnlyCollection<BeliefContact> Contacts => byKey.Values;

		public bool TryGet(uint key, out BeliefContact contact) => byKey.TryGetValue(key, out contact);
		public bool IsRefreshed(uint key) => refreshedThisPass.Contains(key);

		// Start a recompute pass: forget which contacts were touched last time.
		public void BeginPass() => refreshedThisPass.Clear();

		// Record or refresh a contact. Caller supplies the confidence tier (Fresh for a
		// live sighting, Frozen for a remembered ghost). Marks the contact refreshed so
		// DecayUnrefreshed / verified-clear skip it this pass.
		public void Observe(uint key, CPos cell, string typeName, bool isStatic, int confidence, int tick)
		{
			if (!byKey.TryGetValue(key, out var c))
				byKey[key] = c = new BeliefContact(key);

			c.Cell = cell;
			c.TypeName = typeName;
			c.IsStatic = isStatic;
			c.Confidence = confidence;
			c.LastSeenTick = tick;
			refreshedThisPass.Add(key);
		}

		// Explicit removal — verified-clear (cell observed empty) or observed death.
		public void Remove(uint key) => byKey.Remove(key);

		// Decay every contact NOT refreshed this pass. Static contacts are exempt
		// (no decay until verified gone); mobiles fade by decayPercent and are dropped
		// once they fall below minConfidence.
		public void DecayUnrefreshed(int decayPercent, int minConfidence)
		{
			removalScratch.Clear();
			foreach (var c in byKey.Values)
			{
				if (refreshedThisPass.Contains(c.Key) || c.IsStatic)
					continue;

				c.Confidence = c.Confidence * decayPercent / 100;
				if (c.Confidence < minConfidence)
					removalScratch.Add(c.Key);
			}

			foreach (var key in removalScratch)
				byKey.Remove(key);
		}
	}

	[TraitLocation(SystemActors.World)]
	[Desc("WW3MOD influence stack Stage A: per-player believed-enemy-contact memory.",
		"Built from own vision + FrozenActorLayer; generalises frozen structures to mobile units",
		"with decaying confidence. Pure data — no behaviour consumer (Stage B danger fields read it).")]
	public class BeliefStoreInfo : TraitInfo
	{
		[Desc("Ticks between recomputes. Staggered against the other world grids.")]
		public readonly int UpdateInterval = 25;

		[Desc("Confidence assigned to a contact the player can currently, legally see.")]
		public readonly int FreshConfidence = 100;

		[Desc("Confidence ceiling for a contact known only via a frozen (last-seen) ghost.",
			"Lower than Fresh: a remembered contact is weaker evidence than a live one.",
			"Applied to STATIC contacts only — mobiles rely on decay, not the ghost, so their",
			"belief fades per §2A even if the engine keeps a frozen sprite.")]
		public readonly int FrozenConfidence = 60;

		[Desc("Percent of confidence a mobile contact keeps each recompute while unobserved.",
			"75 ⇒ a lost mobile contact keeps 75% of its confidence each cycle. Statics never decay.")]
		public readonly int MobileDecayPercent = 75;

		[Desc("A mobile contact whose confidence falls below this is dropped from the store.")]
		public readonly int MinConfidence = 15;

		public override object Create(ActorInitializer init) { return new BeliefStore(init.Self, this); }
	}

	public class BeliefStore : ITick, IWorldLoaded
	{
		public readonly BeliefStoreInfo Info;
		readonly World world;
		readonly Dictionary<Player, PlayerBeliefContacts> stores = new();

		int updateCountdown;
		int tick;

		public BeliefStore(Actor self, BeliefStoreInfo info)
		{
			Info = info;
			world = self.World;
		}

		void IWorldLoaded.WorldLoaded(World w, OpenRA.Graphics.WorldRenderer wr)
		{
			// Stagger the first fire so this doesn't recompute on the same tick as the
			// other world grids. SharedRandom is synced.
			updateCountdown = w.SharedRandom.Next(0, Info.UpdateInterval);
		}

		void ITick.Tick(Actor self)
		{
			tick++;
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

				if (!stores.TryGetValue(player, out var store))
					stores[player] = store = new PlayerBeliefContacts();

				store.BeginPass();
				InjectLive(player, store);
				InjectFrozenStatics(player, store);
				ResolveUnobserved(player, store);
			}
		}

		// Live sightings: enemy actors the player can currently, legally see.
		void InjectLive(Player player, PlayerBeliefContacts store)
		{
			foreach (var actor in world.Actors)
			{
				if (actor.IsDead || !actor.IsInWorld || actor.Owner == null)
					continue;

				if (player.RelationshipWith(actor.Owner) != PlayerRelationship.Enemy)
					continue;

				// Real destructible actors only — skips crates, shroud effects, and other props.
				if (!actor.Info.HasTraitInfo<HealthInfo>())
					continue;

				if (!actor.CanBeViewedByPlayer(player))
					continue;

				store.Observe(actor.ActorID, actor.Location, actor.Info.Name,
					IsStatic(actor.Info), Info.FreshConfidence, tick);
			}
		}

		// Remembered STATIC contacts: fog-frozen enemy structure/defence snapshots.
		// Mobile ghosts are deliberately ignored here so mobile belief decays (§2A);
		// static belief persists at the Frozen ceiling while the ghost is visible.
		void InjectFrozenStatics(Player player, PlayerBeliefContacts store)
		{
			foreach (var fa in player.FrozenActorLayer.FrozenActorsInRegion(world.Map.AllCells, onlyVisible: true))
			{
				if (!fa.IsValid || fa.Owner == null)
					continue;

				if (player.RelationshipWith(fa.Owner) != PlayerRelationship.Enemy)
					continue;

				if (!fa.Info.HasTraitInfo<HealthInfo>() || !IsStatic(fa.Info))
					continue;

				// A live sighting this pass already refreshed the contact at full confidence.
				if (store.IsRefreshed(fa.ID))
					continue;

				store.Observe(fa.ID, world.Map.CellContaining(fa.CenterPosition), fa.Info.Name,
					isStatic: true, Info.FrozenConfidence, tick);
			}
		}

		// Verified-clear + decay for contacts untouched this pass. A contact whose cell
		// is currently visible (we looked, nothing live there, no frozen ghost) is empty
		// ⇒ removed immediately. The rest (cell still under fog) decay per class.
		void ResolveUnobserved(Player player, PlayerBeliefContacts store)
		{
			var toClear = new List<uint>();
			foreach (var c in store.Contacts)
			{
				if (store.IsRefreshed(c.Key))
					continue;

				if (player.MapLayers.IsVisible(c.Cell, 1))
					toClear.Add(c.Key);
			}

			foreach (var key in toClear)
				store.Remove(key);

			store.DecayUnrefreshed(Info.MobileDecayPercent, Info.MinConfidence);
		}

		static bool IsStatic(ActorInfo info)
		{
			// Anything that can move under its own power is a mobile contact; everything
			// else (defences, garrisoned structures) is static and exempt from decay.
			return !info.HasTraitInfo<MobileInfo>() && !info.HasTraitInfo<AircraftInfo>();
		}

		// ---------- Public query API (Stage-B / consumer seam) ----------

		PlayerBeliefContacts StoreOrNull(Player player)
		{
			return player != null && stores.TryGetValue(player, out var s) ? s : null;
		}

		/// <summary>Believed enemy contacts for a player. Empty when the player has no
		/// store yet. This is the substrate the Stage-B danger fields stamp from.</summary>
		public IReadOnlyCollection<BeliefContact> Contacts(Player player)
		{
			var store = StoreOrNull(player);
			return store != null ? store.Contacts : System.Array.Empty<BeliefContact>();
		}

		public int ContactCount(Player player)
		{
			var store = StoreOrNull(player);
			return store?.Count ?? 0;
		}
	}
}
