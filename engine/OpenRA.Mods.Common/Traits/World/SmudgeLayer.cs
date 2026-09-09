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

	public class SmudgeLayer : IRenderOverlay, IWorldLoaded, ITickRender, INotifyActorDisposing
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
		/// so the only lever is to ramp the whole cell down as the boundary approaches.
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

		void INotifyActorDisposing.Disposing(Actor self)
		{
			if (disposed)
				return;

			render.Dispose();
			disposed = true;
		}
	}
}
