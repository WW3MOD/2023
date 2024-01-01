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
		[Desc("Bitfield of shroud directions for each frame. Lower four bits are",
			"corners clockwise from TL; upper four are edges clockwise from top")]
		public readonly int[] Index = { 12, 9, 8, 3, 1, 6, 4, 2, 13, 11, 7, 14 };

		[Desc("Use the upper four bits when calculating frame")]
		public readonly bool UseExtendedIndex = false;

		[SequenceReference(nameof(Sequence))]
		[Desc("Override for source art that doesn't define a fully shrouded tile")]
		public readonly string OverrideFullShroud = null;

		public readonly int OverrideShroudIndex = 15;

		[SequenceReference(nameof(Sequence))]
		[Desc("Override for source art that doesn't define a fully fogged tile")]
		public readonly string OverrideFullFog = null;

		public readonly int OverrideFogIndex = 15;

		public readonly BlendMode ShroudBlend = BlendMode.Alpha;
		public override object Create(ActorInitializer init) { return new ShroudRenderer(init.World, this); }
	}

	public sealed class ShroudRenderer : IRenderShroud, IWorldLoaded, INotifyActorDisposing
	{
		[Flags]
		enum Edges : byte
		{
			None = 0,
			TopLeft = 0x01,
			TopRight = 0x02,
			BottomRight = 0x04,
			BottomLeft = 0x08,
			AllCorners = TopLeft | TopRight | BottomRight | BottomLeft,
			TopSide = 0x10,
			RightSide = 0x20,
			BottomSide = 0x40,
			LeftSide = 0x80,
			AllSides = TopSide | RightSide | BottomSide | LeftSide,
			Top = TopSide | TopLeft | TopRight,
			Right = RightSide | TopRight | BottomRight,
			Bottom = BottomSide | BottomRight | BottomLeft,
			Left = LeftSide | TopLeft | BottomLeft,
			All = Top | Right | Bottom | Left
		}

		// Index into neighbors array.
		enum Neighbor
		{
			Top = 0,
			Right,
			Bottom,
			Left,
			TopLeft,
			TopRight,
			BottomRight,
			BottomLeft
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
		readonly Edges notVisibleEdges;
		readonly byte variantStride;
		readonly byte[] edgesToSpriteIndexOffset;

		// PERF: Allocate once.
		readonly byte[] neighbors = new byte[8];

		readonly CellLayer<TileInfo> tileInfos;
		readonly CellLayer<bool> cellsDirty;
		bool anyCellDirty;
		readonly (Sprite Sprite, float Scale, float Alpha)[] fogSprites, shroudSprites;

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

			if ((info.OverrideFullFog == null) ^ (info.OverrideFullShroud == null))
				throw new ArgumentException("ShroudRenderer cannot define overrides for only one of shroud or fog!", nameof(info));

			if (info.ShroudVariants.Length > byte.MaxValue)
				throw new ArgumentException("ShroudRenderer cannot define this many shroud and fog variants.", nameof(info));

			if (info.Index.Length >= byte.MaxValue)
				throw new ArgumentException("ShroudRenderer cannot define this many indexes for shroud directions.", nameof(info));

			this.info = info;
			this.world = world;
			map = world.Map;

			tileInfos = new CellLayer<TileInfo>(map);

			cellsDirty = new CellLayer<bool>(map);
			anyCellDirty = true;

			// Load sprite variants
			var variantCount = info.ShroudVariants.Length;
			variantStride = (byte)(info.Index.Length + (info.OverrideFullShroud != null ? 1 : 0));
			shroudSprites = new (Sprite, float, float)[variantCount * variantStride];
			fogSprites = new (Sprite, float, float)[variantCount * variantStride];

			var sequences = map.Sequences;
			for (var j = 0; j < variantCount; j++)
			{
				var shroudSequence = sequences.GetSequence(info.Sequence, info.ShroudVariants[j]);
				var fogSequence = sequences.GetSequence(info.Sequence, info.FogVariants[j]);
				for (var i = 0; i < info.Index.Length; i++)
				{
					shroudSprites[j * variantStride + i] = (shroudSequence.GetSprite(i), shroudSequence.Scale, shroudSequence.GetAlpha(i));
					fogSprites[j * variantStride + i] = (fogSequence.GetSprite(i), fogSequence.Scale, fogSequence.GetAlpha(i));
				}

				if (info.OverrideFullShroud != null)
				{
					var i = (j + 1) * variantStride - 1;
					shroudSequence = sequences.GetSequence(info.Sequence, info.OverrideFullShroud);
					fogSequence = sequences.GetSequence(info.Sequence, info.OverrideFullFog);
					shroudSprites[i] = (shroudSequence.GetSprite(0), shroudSequence.Scale, shroudSequence.GetAlpha(0));
					fogSprites[i] = (fogSequence.GetSprite(0), fogSequence.Scale, fogSequence.GetAlpha(0));
				}
			}

			for (var i = 0; i < MapLayers.VisionLayers - 1; i++)
				layers[i] = new Layer();

			int spriteCount;
			if (info.UseExtendedIndex)
			{
				notVisibleEdges = Edges.AllSides;
				spriteCount = (int)Edges.All;
			}
			else
			{
				notVisibleEdges = Edges.AllCorners;
				spriteCount = (int)Edges.AllCorners;
			}

			// Mapping of shrouded directions -> sprite index
			edgesToSpriteIndexOffset = new byte[spriteCount + 1];
			for (var i = 0; i < info.Index.Length; i++)
				edgesToSpriteIndexOffset[info.Index[i]] = (byte)i;

			if (info.OverrideFullShroud != null)
				edgesToSpriteIndexOffset[info.OverrideShroudIndex] = (byte)(variantStride - 1);

			world.RenderPlayerChanged += WorldOnRenderPlayerChanged;
		}

		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			// Initialize tile cache
			// This includes the region outside the visible area to cover any sprites peeking outside the map
			foreach (var uv in w.Map.AllCells.MapCoords)
			{
				var pos = w.Map.CenterOfCell(uv.ToCPos(map));
				var screen = wr.Screen3DPosition(pos - new WVec(0, 0, pos.Z));
				var variant = (byte)Game.CosmeticRandom.Next(info.ShroudVariants.Length);
				tileInfos[uv] = new TileInfo(screen, variant);
			}

			// All tiles are visible in the editor
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

			neighbors[(int)Neighbor.TopLeft] = cellVisibility((PPos)(cell + new CVec(-1, -1)).ToMPos(map));
			neighbors[(int)Neighbor.TopRight] = cellVisibility((PPos)(cell + new CVec(1, -1)).ToMPos(map));
			neighbors[(int)Neighbor.BottomRight] = cellVisibility((PPos)(cell + new CVec(1, 1)).ToMPos(map));
			neighbors[(int)Neighbor.BottomLeft] = cellVisibility((PPos)(cell + new CVec(-1, 1)).ToMPos(map));

			return neighbors;
		}

		Edges GetEdges(byte[] neighbors, byte cellVisibility)
		{
			// If a side is shrouded then we also count the corners.
			var edges = Edges.None;
			if (neighbors[(int)Neighbor.Top] < cellVisibility) edges |= Edges.Top;
			if (neighbors[(int)Neighbor.Right] < cellVisibility) edges |= Edges.Right;
			if (neighbors[(int)Neighbor.Bottom] < cellVisibility) edges |= Edges.Bottom;
			if (neighbors[(int)Neighbor.Left] < cellVisibility) edges |= Edges.Left;

			var ucorner = edges & Edges.AllCorners;
			if (neighbors[(int)Neighbor.TopLeft] < cellVisibility) edges |= Edges.TopLeft;
			if (neighbors[(int)Neighbor.TopRight] < cellVisibility) edges |= Edges.TopRight;
			if (neighbors[(int)Neighbor.BottomRight] < cellVisibility) edges |= Edges.BottomRight;
			if (neighbors[(int)Neighbor.BottomLeft] < cellVisibility) edges |= Edges.BottomLeft;

			// RA provides a set of frames for tiles with shrouded
			// corners but unshrouded edges. We want to detect this
			// situation without breaking the edge -> corner enabling
			// in other combinations. The XOR turns off the corner
			// bits that are enabled twice, which gives the sprite offset
			// we want here.
			return info.UseExtendedIndex ? edges ^ ucorner : edges & Edges.AllCorners;
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
					// Visible under shroud: Explored. Visible under fog: Visible.
					cellVisibility = puv => (byte)(map.Contains(puv) ? 1 : 0);
				}

				shroud = newShroud;
			}

			// Dirty the full projected space so the cells outside
			// the map bounds can be initialized as fully shrouded.
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

					var cv = cellVisibility(puv);
					var tileInfo = tileInfos[uv];
					var pos = tileInfo.ScreenPosition;

					for (var i = MapLayers.VisionLayers - 2; i >= 0; i--)
					{
						if (cv <= i)
							UpdateLayer(true, false, Alpha(i), layers[i].TerrainSpriteLayer, uv, puv, pos, layers[i].PaletteReference, tileInfo.Variant, layers[i].Sprites);

						// Dont render over tiles with higher visibility, reset tile for layer
						else if (cv >= i + 2)
							UpdateLayer(false, true, Alpha(i), layers[i].TerrainSpriteLayer, uv, puv, pos, layers[i].PaletteReference, tileInfo.Variant, layers[i].Sprites);

						// Handle edges
						else if (cv == i + 1)
						{
							// Use Shroud if it is on the edge
							if (cv == 1 && i == 0)
							{
								UpdateLayer(false, false, Alpha(i), layers[i].TerrainSpriteLayer, uv, puv, pos, layers[i].PaletteReference, tileInfo.Variant, layers[i].Sprites);
							}
							else
								UpdateLayer(false, false, Alpha(i), layers[i].TerrainSpriteLayer, uv, puv, pos, layers[i].PaletteReference, tileInfo.Variant, layers[i].Sprites);
						}
						else
						{
							var ncv = GetNeighborsVisbility(puv);

							if (ncv.All(n => n >= i))
							{
								UpdateLayer(true, false, Alpha(i), layers[i].TerrainSpriteLayer, uv, puv, pos, layers[i].PaletteReference, tileInfo.Variant, layers[i].Sprites);
							}
							else
							{
								UpdateLayer(false, false, Alpha(i > 0 ? i - 1 : i), layers[i].TerrainSpriteLayer, uv, puv, pos, layers[i].PaletteReference, tileInfo.Variant, layers[i].Sprites);
							}
						}
					}
				}
			}

			anyCellDirty = false;
		}

		void UpdateLayer(bool allEdges, bool reset, float alpha, TerrainSpriteLayer terrainSpriteLayer, MPos uv, PPos puv, float3 pos, PaletteReference paletteReference, byte tileVariant, (Sprite, float, float)[] sprites)
		{
			var cv = cellVisibility(puv);

			Sprite sprite;
			if (reset)
				sprite = null;
			else
			{
				var gotSprite = GetSprite(sprites, allEdges ? notVisibleEdges : GetEdges(GetNeighborsVisbility(puv), cv), tileVariant);

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
			if (edges == Edges.None)
				return (null, 1f, 1f);

			return sprites[variant * variantStride + edgesToSpriteIndexOffset[(byte)edges]];
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
