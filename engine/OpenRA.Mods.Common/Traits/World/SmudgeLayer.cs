#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Effects;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public struct MapSmudge
	{
		public string Type;
		public int Depth;
	}

	[TraitLocation(SystemActors.World)]
	[Desc("Attach this to the world actor.", "Order of the layers defines the Z sorting.")]
	public class SmudgeLayerInfo : TraitInfo
	{
		public readonly string Type = "Scorch";

		[Desc("Sprite sequence name")]
		public readonly string Sequence = "scorch";

		[Desc("Chance of smoke rising from the ground")]
		public readonly int SmokeChance = 0;

		[Desc("By how much (in each direction) can the smoke appearance offset stray from the center of the cell?",
			"Note: Limit this to half a cell for square and 1/3 a cell for isometric cells to avoid straying into neighbour cells.")]
		public readonly WDist MaxSmokeOffsetDistance = WDist.Zero;

		[Desc("Smoke sprite image name")]
		public readonly string SmokeImage = null;

		[SequenceReference(nameof(SmokeImage), allowNullImage: true)]
		[Desc("Smoke sprite sequences randomly chosen from")]
		public readonly string[] SmokeSequences = Array.Empty<string>();

		[PaletteReference]
		public readonly string SmokePalette = "effect";

		[PaletteReference]
		public readonly string Palette = TileSet.TerrainPaletteInternalName;

		[Desc("Fade this layer out over N cells as it approaches terrain that does not accept it,",
			"so a blast disc eases off at a shoreline instead of stopping on a cell edge.",
			"0 (the default) disables the fade and reproduces stock alpha exactly.")]
		public readonly int ShoreFadeCells = 0;

		[Desc("Draw this layer a SECOND time after actors have been drawn, on cells occupied only by",
			"cosmetic ground cover (`Passable.GroundCover` -- the crop fields, `^CivField`).",
			"Smudges are a terrain-pass decal and actors are drawn in a later pass, so a scar on a cell",
			"holding a crop field is placed, is present, and is completely hidden by the field sprite on",
			"top of it. That reads as farmland being immune to a nuclear blast. This draws the same",
			"sprite at the same alpha again, past the actor pass, for those cells only.",
			"The restriction is `GroundCover` and it is exact: a cell qualifies only when it holds at",
			"least one actor and EVERY actor in it is ground cover, so a scar can never appear on top of",
			"a tank, a soldier or a building. An empty cell is deliberately excluded -- the terrain pass",
			"already drew it, and drawing it twice would composite the sprite onto itself and make bare",
			"ground darker than farmland.",
			"Defaults to false, so every layer that does not opt in draws exactly as it did before.")]
		public readonly bool GroundCoverOverlay = false;

		[FieldLoader.LoadUsing(nameof(LoadInitialSmudges))]
		public readonly Dictionary<CPos, MapSmudge> InitialSmudges;

		public static object LoadInitialSmudges(MiniYaml yaml)
		{
			var smudges = new Dictionary<CPos, MapSmudge>();
			var smudgeYaml = yaml.NodeWithKeyOrDefault("InitialSmudges");
			if (smudgeYaml != null)
			{
				foreach (var node in smudgeYaml.Value.Nodes)
				{
					try
					{
						var cell = FieldLoader.GetValue<CPos>("key", node.Key);
						var parts = node.Value.Value.Split(',');
						var type = parts[0];
						var depth = FieldLoader.GetValue<int>("depth", parts[1]);
						smudges.Add(cell, new MapSmudge { Type = type, Depth = depth });
					}
					catch { }
				}
			}

			return smudges;
		}

		public override object Create(ActorInitializer init) { return new SmudgeLayer(init.Self, this); }
	}

	public class SmudgeLayer : IRenderOverlay, IRenderAboveWorld, IWorldLoaded, ITickRender, INotifyActorDisposing
	{
		struct Smudge
		{
			public string Type;
			public int Depth;
			public ISpriteSequence Sequence;
		}

		public readonly SmudgeLayerInfo Info;
		readonly Dictionary<CPos, Smudge> tiles = new();
		readonly Dictionary<CPos, Smudge> dirty = new();
		readonly Dictionary<string, ISpriteSequence> smudges = new();
		readonly Dictionary<CPos, float> shoreAlpha = new();
		readonly World world;
		readonly bool hasSmoke;

		TerrainSpriteLayer render;
		PaletteReference paletteReference;
		bool disposed;

		// ---- the over-actors pass, Info.GroundCoverOverlay ------------------------------------
		// A second TerrainSpriteLayer holding ONLY the cells whose every occupant is cosmetic ground
		// cover, drawn from IRenderAboveWorld so it lands after the actor pass rather than inside the
		// terrain pass. IRenderOverlay is called by TerrainRenderer.RenderTerrain (TerrainRenderer.cs:112),
		// which WorldRenderer.Draw runs at :378 -- before the renderable loop at :388. IRenderAboveWorld
		// is invoked at :396, after it. Those are two sequential passes, not one sorted list, so this is
		// the only seam at which a terrain decal can be put over an actor sprite at all.
		//
		// ALLOCATED LAZILY, and that is load-bearing rather than tidiness: TerrainSpriteLayer eagerly
		// allocates 4 Vertex (48 bytes each) per map cell -- ~1.5 MB on river-zeta's 98x82 -- plus a GPU
		// vertex buffer, in its constructor. Nine of the ten shipped maps carry no crop field at all, so
		// on those this stays null forever and the whole feature costs one bool.
		TerrainSpriteLayer overlayRender;
		WorldRenderer worldRenderer;
		Sprite overlayEmptySprite;
		BlendMode overlayBlendMode;

		// PERF: cached so the per-cell classification below does not allocate a delegate per call.
		static readonly Func<Actor, bool> ActorIsGroundCover = a => a.IsGroundCover();

		public SmudgeLayer(Actor self, SmudgeLayerInfo info)
		{
			Info = info;
			world = self.World;
			hasSmoke = !string.IsNullOrEmpty(info.SmokeImage) && info.SmokeSequences.Length > 0;

			var sequences = world.Map.Sequences;
			var types = sequences.Sequences(Info.Sequence);
			foreach (var t in types)
				smudges.Add(t, sequences.GetSequence(Info.Sequence, t));
		}

		public void WorldLoaded(World w, WorldRenderer wr)
		{
			var sprites = smudges.Values.SelectMany(v => Exts.MakeArray(v.Length, x => v.GetSprite(x))).ToList();
			var sheet = sprites[0].Sheet;
			var blendMode = sprites[0].BlendMode;
			var emptySprite = new Sprite(sheet, Rectangle.Empty, TextureChannel.Alpha);

			if (sprites.Any(s => s.BlendMode != blendMode))
				throw new InvalidDataException("Smudges specify different blend modes. "
					+ "Try using different smudge types for smudges that use different blend modes.");

			paletteReference = wr.Palette(Info.Palette);
			render = new TerrainSpriteLayer(w, wr, emptySprite, blendMode, w.Type != WorldType.Editor);

			if (Info.GroundCoverOverlay)
			{
				worldRenderer = wr;
				overlayEmptySprite = emptySprite;
				overlayBlendMode = blendMode;

				// Subscribed only when the feature is on, so a layer that has not opted in adds no
				// handler to an event that fires on every cell every moving actor enters or leaves.
				// The event is already live regardless -- Locomotor (Locomotor.cs:453) and
				// HierarchicalPathFinder (:258) both subscribe unconditionally -- so this is one extra
				// handler on a hot event, not the switching-on of a dormant path. The handler itself is
				// a single dictionary lookup that returns immediately for any cell with no smudge.
				w.ActorMap.CellUpdated += ActorsChanged;
			}

			// Add map smudges
			foreach (var kv in Info.InitialSmudges)
			{
				var s = kv.Value;
				if (!smudges.ContainsKey(s.Type))
					continue;

				var seq = smudges[s.Type];
				var smudge = new Smudge
				{
					Type = s.Type,
					Depth = s.Depth,
					Sequence = seq
				};

				tiles.Add(kv.Key, smudge);
				Draw(kv.Key, smudge);
			}
		}

		public void AddSmudge(CPos loc)
		{
			if (!world.Map.Contains(loc))
				return;

			if (hasSmoke && Game.CosmeticRandom.Next(0, 100) <= Info.SmokeChance)
			{
				var position = world.Map.CenterOfCell(loc);
				var maxOffsetDistance = Info.MaxSmokeOffsetDistance.Length;
				if (maxOffsetDistance != 0)
				{
					position += new WVec(Game.CosmeticRandom.Next(-maxOffsetDistance, maxOffsetDistance), Game.CosmeticRandom.Next(-maxOffsetDistance, maxOffsetDistance), 0);
					position = new WPos(position.X, position.Y, position.Z - world.Map.DistanceAboveTerrain(position).Length);
				}

				world.AddFrameEndTask(w => w.Add(new SpriteEffect(
					position, w, Info.SmokeImage, Info.SmokeSequences.Random(Game.CosmeticRandom), Info.SmokePalette)));
			}

			// A null Sequence indicates a deleted smudge.
			if ((!dirty.ContainsKey(loc) || dirty[loc].Sequence == null) && !tiles.ContainsKey(loc))
			{
				// No smudge; create a new one
				var st = smudges.Keys.Random(Game.CosmeticRandom);
				dirty[loc] = new Smudge { Type = st, Depth = 0, Sequence = smudges[st] };
			}
			else
			{
				// Existing smudge; make it deeper
				// A null Sequence indicates a deleted smudge.
				var tile = dirty.TryGetValue(loc, out var d) && d.Sequence != null ? d : tiles[loc];
				var maxDepth = smudges[tile.Type].Length;
				if (tile.Depth < maxDepth - 1)
					tile.Depth++;

				dirty[loc] = tile;
			}
		}

		/// <summary>Draws one smudge, scaled by <see cref="ShoreAlpha"/>.
		///
		/// <para>This is the four-argument <c>TerrainSpriteLayer.Update</c> overload
		/// (<c>TerrainSpriteLayer.cs:91-94</c>) written out longhand so the alpha can be multiplied.
		/// `Scale` and `IgnoreWorldTint` are passed through unchanged, so with
		/// <see cref="SmudgeLayerInfo.ShoreFadeCells"/> at its default of 0 this is the identical call
		/// the layer made before.</para></summary>
		void Draw(CPos cell, Smudge smudge)
		{
			var seq = smudge.Sequence;
			var alpha = seq.GetAlpha(smudge.Depth) * ShoreAlpha(cell);
			render.Update(cell, seq.GetSprite(smudge.Depth), paletteReference, seq.Scale, alpha, seq.IgnoreWorldTint);

			if (Info.GroundCoverOverlay)
				DrawOverGroundCover(cell, seq, smudge.Depth, alpha);
		}

		// Writes this cell into the over-actors layer, or clears it out of it. Same sprite, same depth,
		// same alpha as the terrain-pass draw above -- this is the identical decal drawn a second time,
		// not a second decal, so a field cell and a bare cell in the same band read as one continuous
		// blast rather than two shades of one.
		//
		// No double-darkening, despite the sprite being composited twice on a field cell: the terrain-pass
		// copy is drawn onto the terrain and then the OPAQUE field sprite is drawn over it, hiding it
		// outright, so exactly one copy is visible either way. That depends on the field sprite being
		// opaque and covering its cell; see the note in the test fixture.
		void DrawOverGroundCover(CPos cell, ISpriteSequence seq, int depth, float alpha)
		{
			var over = IsGroundCoverOnly(world.ActorMap.GetActorsAt(cell), ActorIsGroundCover);

			if (overlayRender == null)
			{
				// The overwhelmingly common case on a map with no farmland: nothing to draw and nothing
				// allocated. Do not build the layer just to write a null into it.
				if (!over)
					return;

				overlayRender = new TerrainSpriteLayer(world, worldRenderer, overlayEmptySprite, overlayBlendMode,
					world.Type != WorldType.Editor);
			}

			overlayRender.Update(cell, over ? seq.GetSprite(depth) : null, paletteReference, seq.Scale, alpha, seq.IgnoreWorldTint);
		}

		/// <summary>Whether a cell's occupants are all cosmetic ground cover, so a decal may be drawn over them.</summary>
		// Generic over the occupant type, with the ground-cover test passed in, PURELY so it is testable:
		// nothing in OpenRA.Test can construct an Actor, let alone a World, so a rule expressed directly
		// against Actor is a rule verified by reading. Same split, and the same reason, as ShoreAlphaAt
		// below. The trait calls it with the real ActorMap and Actor.IsGroundCover; the fixture calls it
		// with plain bools.
		//
		// EMPTY IS FALSE, deliberately and load-bearingly. An empty cell was already drawn by the terrain
		// pass; drawing it again composites the sprite onto itself and leaves bare ground darker than the
		// farmland beside it, which is a new artefact rather than a fix. "At least one occupant, and every
		// occupant is ground cover" is the whole rule.
		public static bool IsGroundCoverOnly<T>(IEnumerable<T> occupants, Func<T, bool> isGroundCover)
		{
			var any = false;
			foreach (var occupant in occupants)
			{
				if (!isGroundCover(occupant))
					return false;

				any = true;
			}

			return any;
		}

		// An actor entered or left this cell, so its classification may have changed -- a tank driving
		// onto scarred farmland must take the over-drawn scar with it, and give it back when it leaves.
		//
		// Re-queues through `dirty` rather than redrawing here, which buys three things from the existing
		// commit path for free: the fog gate (TickRender only commits cells the local player can see),
		// batching (many transitions in one tick collapse to one redraw), and a single code path for the
		// under-layer and the over-layer. Re-adding a tile to `dirty` at its CURRENT depth is exactly what
		// AddSmudge's "existing smudge" branch does, so it cannot disturb depth accounting.
		void ActorsChanged(CPos cell)
		{
			if (tiles.TryGetValue(cell, out var smudge) && !dirty.ContainsKey(cell))
				dirty[cell] = smudge;
		}

		/// <summary>How strongly this cell should draw, given how close it is to terrain this layer
		/// cannot be drawn on.
		///
		/// <para>WHY THIS EXISTS. A smudge is one opaque sprite per cell in a terrain layer, and
		/// <c>LeaveSmudgeWarhead</c> drops any cell whose terrain type does not list the smudge type in
		/// <c>AcceptsSmudgeType</c>. So a blast that reaches a river ends on a cell edge at full
		/// strength: a near-solid core-band cell butts straight against untouched water. There is no
		/// sub-cell land/water information anywhere in the engine to feather against --
		/// <c>TerrainTileInfo</c> is one terrain type per tile and nothing reads the tile art back --
		/// so the only lever is to ramp the whole cell down as the boundary approaches.</para>
		///
		/// <para>Distance is Chebyshev, which matches the square cell grid: a cell diagonally touching
		/// water fades the same as one orthogonally touching it, so the ramp follows the shoreline
		/// rather than bulging at diagonals. Off-map cells deliberately do NOT trigger the fade -- the
		/// map border is not a shoreline and a ring of half-strength scar around the edge of the world
		/// would be a new artefact, not a fix.</para>
		///
		/// <para>Cached because a single high-yield strike asks about ~7000 cells and the answer is
		/// static for the life of the map.</para></summary>
		float ShoreAlpha(CPos cell)
		{
			if (Info.ShoreFadeCells <= 0)
				return 1f;

			if (shoreAlpha.TryGetValue(cell, out var cached))
				return cached;

			var alpha = ShoreAlphaAt(cell, Info.ShoreFadeCells,
				c => world.Map.Contains(c) && !world.Map.GetTerrainInfo(c).AcceptsSmudgeType.Contains(Info.Type));

			shoreAlpha[cell] = alpha;
			return alpha;
		}

		/// <summary>The fade ramp itself, split out from the map lookup so it can be tested without a World.
		///
		/// <para><paramref name="isBoundary"/> answers "is this cell one this layer cannot draw on" — which
		/// is deliberately FALSE for off-map cells: the edge of the world is not a shoreline, and a ring of
		/// half-strength scar around the map border would be a new artefact rather than a fix.</para>
		///
		/// <para>Returns <c>d / (fadeCells + 1)</c> clamped to 1, where <c>d</c> is the Chebyshev distance to
		/// the nearest boundary cell. Chebyshev, not Euclidean, because the smudge grid is square: a cell
		/// touching water at a corner should fade like one touching it edge-on, so the ramp traces the
		/// shoreline instead of bulging on diagonals. A cell that IS a boundary cell scores 0 and draws
		/// nothing, which never happens in practice — the warhead already refused to place a smudge
		/// there.</para></summary>
		public static float ShoreAlphaAt(CPos cell, int fadeCells, Func<CPos, bool> isBoundary)
		{
			if (fadeCells <= 0)
				return 1f;

			var distance = fadeCells + 1;
			for (var dy = -fadeCells; dy <= fadeCells; dy++)
			{
				for (var dx = -fadeCells; dx <= fadeCells; dx++)
				{
					var d = Math.Max(Math.Abs(dx), Math.Abs(dy));
					if (d >= distance || !isBoundary(cell + new CVec(dx, dy)))
						continue;

					distance = d;
				}
			}

			return Math.Min(1f, distance / (float)(fadeCells + 1));
		}

		public void RemoveSmudge(CPos loc)
		{
			if (!world.Map.Contains(loc))
				return;

			var tile = dirty.TryGetValue(loc, out var d) ? d : default;

			// Setting Sequence to null to indicate a deleted smudge.
			tile.Sequence = null;
			dirty[loc] = tile;
		}

		void ITickRender.TickRender(WorldRenderer wr, Actor self)
		{
			var remove = new List<CPos>();
			foreach (var kv in dirty)
			{
				if (!world.FogObscures(kv.Key))
				{
					// A null Sequence
					if (kv.Value.Sequence == null)
					{
						tiles.Remove(kv.Key);
						render.Clear(kv.Key);
						overlayRender?.Clear(kv.Key);
					}
					else
					{
						var smudge = kv.Value;
						tiles[kv.Key] = smudge;
						Draw(kv.Key, smudge);
					}

					remove.Add(kv.Key);
				}
			}

			foreach (var r in remove)
				dirty.Remove(r);
		}

		void IRenderOverlay.Render(WorldRenderer wr)
		{
			render.Draw(wr.Viewport);
		}

		// The over-actors half. Null on every map that has never had a scar land on ground cover, which
		// is nine of the ten shipped maps -- so this is a null check per layer per frame there.
		//
		// Z ORDER IS PRESERVED ACROSS THE TWO PASSES. world.yaml lists the five scar bands lightest-first
		// so the darkest draws last and wins (SmudgeLayerInfo's own [Desc]: "Order of the layers defines
		// the Z sorting"), and that ordering holds here for the same reason it holds for IRenderOverlay:
		// TraitDictionary.TraitContainer.Add inserts at BinarySearchMany(ActorID + 1) (TraitDictionary.cs:153),
		// i.e. appends after the existing entries for the same actor, so traits on the world actor are
		// visited in creation -- therefore YAML declaration -- order by both iterations.
		void IRenderAboveWorld.RenderAboveWorld(Actor self, WorldRenderer wr)
		{
			overlayRender?.Draw(wr.Viewport);
		}

		void INotifyActorDisposing.Disposing(Actor self)
		{
			if (disposed)
				return;

			if (Info.GroundCoverOverlay)
				world.ActorMap.CellUpdated -= ActorsChanged;

			render.Dispose();
			overlayRender?.Dispose();
			disposed = true;
		}
	}
}
