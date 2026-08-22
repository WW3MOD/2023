using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World | SystemActors.EditorWorld)]
	public class ShroudRendererInfo : TraitInfo
	{
		public readonly string Sequence = "shroud";
		[SequenceReference(nameof(Sequence))]
		public readonly string[] ShroudVariants = { "shroud" };
		[SequenceReference(nameof(Sequence))]
		public readonly string[] FogVariants = { "shroud" };
		[PaletteReference]
		public readonly string ShroudPalette = "shroud";
		[PaletteReference]
		public readonly string FogPalette = "fog";
		public readonly BlendMode ShroudBlend = BlendMode.Alpha;
		public override object Create(ActorInitializer init) { return new ShroudRenderer(init.World, this); }
	}

	public sealed class ShroudRenderer : IRenderShroud, IWorldLoaded, INotifyActorDisposing
	{
		[Flags]
		enum Edges : byte
		{
			None = 0,
			Top = 0x01,
			Right = 0x02,
			Bottom = 0x04,
			Left = 0x08,
			AllSides = Top | Right | Bottom | Left // 0x0F (15)
		}

		enum Neighbor
		{
			Top = 0,
			Right,
			Bottom,
			Left
		}

		readonly struct TileInfo
		{
			public readonly float3 ScreenPosition;
			public readonly byte Variant;
			public TileInfo(in float3 screenPosition, byte variant)
			{
				ScreenPosition = screenPosition;
				Variant = variant;
			}
		}

		readonly ShroudRendererInfo info;
		readonly World world;
		readonly Map map;
		readonly byte frameCount;
		readonly (Sprite Sprite, float Scale, float Alpha)[] fogSprites, shroudSprites;
		readonly byte[] neighbors = new byte[4];
		readonly CellLayer<TileInfo> tileInfos;
		readonly CellLayer<bool> cellsDirty;
		bool anyCellDirty;
		MapLayers shroud;
		Func<PPos, byte> cellVisibility;

		// The whole MapSize, not just the playable Bounds. RenderShroud used to walk
		// map.ProjectedCells, which is Bounds-derived (Map.cs:1600-1624), so cells in
		// the unplayable ring could be marked dirty but were never actually visited.
		PPos[] allProjectedCells;
		readonly Layer[] layers = new Layer[MapLayers.VisionLayers];

		class Layer
		{
			public (Sprite, float, float)[] Sprites;
			public TerrainSpriteLayer TerrainSpriteLayer;
			public PaletteReference PaletteReference;
			public Layer() { }
		}

		bool disposed;

		public ShroudRenderer(World world, ShroudRendererInfo info)
		{
			if (info.ShroudVariants.Length != info.FogVariants.Length)
				throw new ArgumentException("ShroudRenderer must define the same number of shroud and fog variants!", nameof(info));

			this.info = info;
			this.world = world;
			map = world.Map;
			tileInfos = new CellLayer<TileInfo>(map);
			cellsDirty = new CellLayer<bool>(map);
			anyCellDirty = true;

			var variantCount = info.ShroudVariants.Length;
			frameCount = 16;
			shroudSprites = new (Sprite, float, float)[variantCount * frameCount];
			fogSprites = new (Sprite, float, float)[variantCount * frameCount];

			var sequenceProvider = map.Sequences;
			for (var j = 0; j < variantCount; j++)
			{
				var shroudSequence = sequenceProvider.GetSequence(info.Sequence, info.ShroudVariants[j]);
				var fogSequence = sequenceProvider.GetSequence(info.Sequence, info.FogVariants[j]);
				if (shroudSequence.Length < frameCount || fogSequence.Length < frameCount)
					throw new InvalidOperationException($"Sequence {info.ShroudVariants[j]} or {info.FogVariants[j]} has fewer than {frameCount} frames.");

				for (var i = 0; i < frameCount; i++)
				{
					var index = j * frameCount + i;
					shroudSprites[index] = (shroudSequence.GetSprite(i), shroudSequence.Scale, shroudSequence.GetAlpha(i));
					fogSprites[index] = (fogSequence.GetSprite(i), fogSequence.Scale, fogSequence.GetAlpha(i));
				}
			}

			for (var i = 0; i < MapLayers.VisionLayers - 1; i++)
				layers[i] = new Layer();

			world.RenderPlayerChanged += WorldOnRenderPlayerChanged;
		}

		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			allProjectedCells = new ProjectedCellRegion(map, new PPos(0, 0), new PPos(map.MapSize.X - 1, map.MapSize.Y - 1)).ToArray();

			foreach (var uv in w.Map.AllCells.MapCoords)
			{
				var pos = w.Map.CenterOfCell(uv.ToCPos(map));
				var screen = wr.Screen3DPosition(pos - new WVec(0, 0, pos.Z));
				var variant = (byte)Game.CosmeticRandom.Next(info.ShroudVariants.Length);
				tileInfos[uv] = new TileInfo(screen, variant);
			}

			if (w.Type == WorldType.Editor)
				cellVisibility = puv => (byte)(map.Contains(puv) ? 10 : 0);
			else
				cellVisibility = puv => world.RenderPlayer.MapLayers.GetVisibility(puv);

			var shroudBlend = shroudSprites[0].Sprite.BlendMode;
			if (shroudSprites.Any(s => s.Sprite.BlendMode != shroudBlend))
				throw new InvalidDataException("Shroud sprites must all use the same blend mode.");

			var fogBlend = fogSprites[0].Sprite.BlendMode;
			if (fogSprites.Any(s => s.Sprite.BlendMode != fogBlend))
				throw new InvalidDataException("Fog sprites must all use the same blend mode.");

			var emptySprite = new Sprite(shroudSprites[0].Sprite.Sheet, Rectangle.Empty, TextureChannel.Alpha);

			for (var i = 0; i < MapLayers.VisionLayers - 1; i++)
			{
				if (i == 0)
				{
					layers[i].TerrainSpriteLayer = new TerrainSpriteLayer(w, wr, emptySprite, shroudBlend, false);
					layers[i].PaletteReference = wr.Palette(info.ShroudPalette);
					layers[i].Sprites = shroudSprites;
				}
				else
				{
					layers[i].TerrainSpriteLayer = new TerrainSpriteLayer(w, wr, emptySprite, fogBlend, false);
					layers[i].PaletteReference = wr.Palette(info.FogPalette);
					layers[i].Sprites = fogSprites;
				}
			}

			WorldOnRenderPlayerChanged(world.RenderPlayer);
		}

		// WW3MOD: every map ships a one-cell unplayable ring between Bounds and MapSize
		// that holds real terrain and authored scenery. Each of MapLayers' visibility
		// sources is gated on Map.Contains — which is Bounds.Contains — so a ring cell
		// resolves to 0 and the shroud paints it opaque black, three draw calls after
		// DrawBeyondMapFog handed it its ground back. Sample the nearest playable cell
		// instead, so a ring cell shows whatever its neighbour shows.
		//
		// This is deliberately confined to the renderer. MapLayers.GetVisibility also
		// feeds FrozenActorLayer, and through it targeting, autotarget acquisition,
		// BeliefStore and SightingThreatLayer — changing what it returns would be a
		// simulation and determinism change, not a render fix.
		//
		// Clamping is exact for a one-cell ring, O(1) as this per-cell path requires,
		// and the identity inside Bounds, so mid-map shroud drawing is untouched.
		PPos ClampToPlayable(PPos puv)
		{
			var b = map.Bounds;
			var u = puv.U < b.Left ? b.Left : (puv.U > b.Right - 1 ? b.Right - 1 : puv.U);
			var v = puv.V < b.Top ? b.Top : (puv.V > b.Bottom - 1 ? b.Bottom - 1 : puv.V);

			return u == puv.U && v == puv.V ? puv : new PPos(u, v);
		}

		byte CellVisibility(PPos puv)
		{
			return cellVisibility(ClampToPlayable(puv));
		}

		byte[] GetNeighborsVisbility(PPos puv)
		{
			var cell = ((MPos)puv).ToCPos(map);

			// WW3MOD: a neighbour off the playable area is clamped back onto it rather
			// than counted as 0 (shroud), which would paint a false shroud gradient down
			// every map border. For a playable edge cell the clamp lands on the cell
			// itself, so this keeps the previous "use the cell's own visibility" result
			// exactly; for a ring cell it lands on the playable cell the ring abuts.
			var topPos = (PPos)(cell + new CVec(0, -1)).ToMPos(map);
			var rightPos = (PPos)(cell + new CVec(1, 0)).ToMPos(map);
			var bottomPos = (PPos)(cell + new CVec(0, 1)).ToMPos(map);
			var leftPos = (PPos)(cell + new CVec(-1, 0)).ToMPos(map);

			neighbors[(int)Neighbor.Top] = CellVisibility(topPos);
			neighbors[(int)Neighbor.Right] = CellVisibility(rightPos);
			neighbors[(int)Neighbor.Bottom] = CellVisibility(bottomPos);
			neighbors[(int)Neighbor.Left] = CellVisibility(leftPos);

			return neighbors;
		}

		Edges GetEdges(byte[] neighbors, byte cellVisibility, byte max)
		{
			var edges = Edges.None;

			if (cellVisibility > neighbors[(int)Neighbor.Top] && neighbors[(int)Neighbor.Top] <= max)
				edges |= Edges.Top;
			if (cellVisibility > neighbors[(int)Neighbor.Right] && neighbors[(int)Neighbor.Right] <= max)
				edges |= Edges.Right;
			if (cellVisibility > neighbors[(int)Neighbor.Bottom] && neighbors[(int)Neighbor.Bottom] <= max)
				edges |= Edges.Bottom;
			if (cellVisibility > neighbors[(int)Neighbor.Left] && neighbors[(int)Neighbor.Left] <= max)
				edges |= Edges.Left;
			/*
				if ((byte)edges > (byte)Edges.AllSides)
					edges = Edges.None;
			*/

			return edges;
		}

		void WorldOnRenderPlayerChanged(Player player)
		{
			var newShroud = player?.MapLayers;

			if (shroud != newShroud)
			{
				if (shroud != null)
					shroud.OnShroudChanged -= UpdateShroudCell;

				if (newShroud != null)
				{
					cellVisibility = puv => newShroud.GetVisibility(puv);
					newShroud.OnShroudChanged += UpdateShroudCell;
				}
				else
				{
					cellVisibility = puv => (byte)(map.Contains(puv) ? 1 : 0);
				}

				shroud = newShroud;
			}

			cellsDirty.Clear(true);
			anyCellDirty = true;
			UpdateShroud(new ProjectedCellRegion(map, new PPos(0, 0), new PPos(map.MapSize.X - 1, map.MapSize.Y - 1)));
		}

		static float Alpha(int index)
		{
			var alpha = 1f;

			if (index > 1)
				alpha -= (index - 1) * (1f / 12);

			if (index > 0)
				alpha /= 3;

			return alpha;
		}

		void UpdateShroud(IEnumerable<PPos> region)
		{
			if (!anyCellDirty)
				return;

			// RenderPlayer == null is the render-side "world view" (observers, the
			// DevCinematicView cheat, and TestMode's full-map viewer). In that mode every
			// dirty cell's shroud/fog sprites are cleared and nothing is repainted, so the
			// whole map shows. Without this clear, switching to world view from a real
			// player left the previously-drawn shroud sprites stuck on screen (black map).
			// Player MapLayers are never touched here — only sprites — so AI perception and
			// the test verdict stay byte-identical.
			var renderPlayerActive = world.RenderPlayer != null;

			foreach (var puv in region)
			{
				var uv = (MPos)puv;
				if (!cellsDirty[uv] || !tileInfos.Contains(uv))
					continue;

				cellsDirty[uv] = false;

				for (var vLayerIndex = MapLayers.VisionLayers - 2; vLayerIndex >= 0; vLayerIndex--)
					layers[vLayerIndex].TerrainSpriteLayer.Clear(uv.ToCPos(map));

				if (!renderPlayerActive)
					continue;

				var cellVisibility = CellVisibility(puv);
				var tileInfo = tileInfos[uv];
				var pos = tileInfo.ScreenPosition;

				for (var vLayerIndex = MapLayers.VisionLayers - 2; vLayerIndex >= 0; vLayerIndex--)
				{
					if (cellVisibility <= vLayerIndex)
					{
						UpdateLayer(true, false, Alpha(vLayerIndex), layers[vLayerIndex].TerrainSpriteLayer, uv, puv, pos, layers[vLayerIndex].PaletteReference, tileInfo.Variant, layers[vLayerIndex].Sprites, (byte)vLayerIndex);
					}
					else
					{
						var neighbors = GetNeighborsVisbility(puv);
						var neighborsCheck = false;
						for (var i = 0; i <= 3; i++)
						{
							if (neighbors[i] <= vLayerIndex)
							{
								neighborsCheck = true;
								break;
							}
						}

						if (neighborsCheck)
						{
							UpdateLayer(false, false, Alpha(vLayerIndex), layers[vLayerIndex].TerrainSpriteLayer, uv, puv, pos, layers[vLayerIndex].PaletteReference, tileInfo.Variant, layers[vLayerIndex].Sprites, (byte)vLayerIndex);
						}
					}
				}
			}

			anyCellDirty = false;
		}

		void UpdateLayer(bool allEdges, bool reset, float alpha, TerrainSpriteLayer terrainSpriteLayer, MPos uv, PPos puv, float3 pos, PaletteReference paletteReference, byte tileVariant, (Sprite, float, float)[] sprites, byte visionLayerIndex)
		{
			var cv = CellVisibility(puv);

			Sprite sprite;
			if (reset)
				sprite = null;
			else
			{
				var edges = allEdges ? Edges.None : GetEdges(GetNeighborsVisbility(puv), cv, visionLayerIndex);
				var gotSprite = GetSprite(sprites, edges, tileVariant);

				if (gotSprite.Sprite != null) pos += gotSprite.Sprite.Offset - 0.5f * gotSprite.Sprite.Size;

				sprite = gotSprite.Sprite;
			}

			terrainSpriteLayer.Update(uv, sprite, paletteReference, pos, 1f, alpha, true);
		}

		void IRenderShroud.RenderShroud(WorldRenderer wr)
		{
			UpdateShroud(allProjectedCells);

			for (var i = MapLayers.VisionLayers - 2; i >= 0; i--)
			{
				layers[i].TerrainSpriteLayer.Draw(wr.Viewport);
			}
		}

		void UpdateShroudCell(PPos puv)
		{
			var uv = (MPos)puv;
			cellsDirty[uv] = true;
			anyCellDirty = true;
			var cell = uv.ToCPos(map);
			foreach (var direction in CVec.Directions)
			{
				// Gate on the sprite layer's own extent (the whole MapSize), not on
				// Map.Contains (the playable Bounds). MapLayers only reports changes for
				// playable cells, so a ring cell is repainted solely by this spread from
				// the playable cell it abuts — the Bounds gate here left it stuck on
				// whatever it was painted at world load.
				var neighbor = (cell + direction).ToMPos(map);
				if (cellsDirty.Contains(neighbor))
					cellsDirty[neighbor] = true;
			}
		}

		(Sprite Sprite, float Scale, float Alpha) GetSprite((Sprite, float, float)[] sprites, Edges edges, int variant)
		{
			var edgeIndex = (byte)edges;
			return sprites[edgeIndex];
		}

		void INotifyActorDisposing.Disposing(Actor self)
		{
			if (disposed)
				return;

			for (var i = MapLayers.VisionLayers - 2; i >= 0; i--)
				layers[i].TerrainSpriteLayer.Dispose();

			disposed = true;
		}
	}
}
