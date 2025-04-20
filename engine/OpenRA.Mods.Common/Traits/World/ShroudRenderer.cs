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

			var sequenceProvider = map.Rules.Sequences;
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

		byte[] GetNeighborsVisbility(PPos puv)
		{
			var cell = ((MPos)puv).ToCPos(map);
			neighbors[(int)Neighbor.Top] = cellVisibility((PPos)(cell + new CVec(0, -1)).ToMPos(map));
			neighbors[(int)Neighbor.Right] = cellVisibility((PPos)(cell + new CVec(1, 0)).ToMPos(map));
			neighbors[(int)Neighbor.Bottom] = cellVisibility((PPos)(cell + new CVec(0, 1)).ToMPos(map));
			neighbors[(int)Neighbor.Left] = cellVisibility((PPos)(cell + new CVec(-1, 0)).ToMPos(map));

			/*
				var cell = ((MPos)puv).ToCPos(map);
				var topPos = (PPos)(cell + new CVec(0, -1)).ToMPos(map);
				var rightPos = (PPos)(cell + new CVec(1, 0)).ToMPos(map);
				var bottomPos = (PPos)(cell + new CVec(0, 1)).ToMPos(map);
				var leftPos = (PPos)(cell + new CVec(-1, 0)).ToMPos(map);

				neighbors[(int)Neighbor.Top] = map.Contains(topPos) ? cellVisibility(topPos) : (byte)5;
				neighbors[(int)Neighbor.Right] = map.Contains(rightPos) ? cellVisibility(rightPos) : (byte)5;
				neighbors[(int)Neighbor.Bottom] = map.Contains(bottomPos) ? cellVisibility(bottomPos) : (byte)5;
				neighbors[(int)Neighbor.Left] = map.Contains(leftPos) ? cellVisibility(leftPos) : (byte)5;
			*/

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

			if (world.RenderPlayer != null)
			{
				foreach (var puv in region)
				{
					var uv = (MPos)puv;
					if (!cellsDirty[uv] || !tileInfos.Contains(uv))
						continue;

					cellsDirty[uv] = false;

					var cellVisibility = this.cellVisibility(puv);
					var tileInfo = tileInfos[uv];
					var pos = tileInfo.ScreenPosition;

					for (var vLayerIndex = MapLayers.VisionLayers - 2; vLayerIndex >= 0; vLayerIndex--)
						layers[vLayerIndex].TerrainSpriteLayer.Clear(uv.ToCPos(map));

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
			}

			anyCellDirty = false;
		}

		void UpdateLayer(bool allEdges, bool reset, float alpha, TerrainSpriteLayer terrainSpriteLayer, MPos uv, PPos puv, float3 pos, PaletteReference paletteReference, byte tileVariant, (Sprite, float, float)[] sprites, byte visionLayerIndex)
		{
			var cv = cellVisibility(puv);

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
			UpdateShroud(map.ProjectedCells);

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
				if (map.Contains((PPos)(cell + direction).ToMPos(map)))
					cellsDirty[cell + direction] = true;
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
