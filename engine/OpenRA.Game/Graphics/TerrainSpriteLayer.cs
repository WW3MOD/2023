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
using System.IO;
using System.Runtime.CompilerServices;

namespace OpenRA.Graphics
{
	public sealed class TerrainSpriteLayer : IDisposable
	{
		// PERF: we can reuse the IndexBuffer as all layers have the same size.
		static readonly ConditionalWeakTable<World, IndexBufferRc> IndexBuffers = new();
		readonly IndexBufferRc indexBufferWrapper;

		public readonly BlendMode BlendMode;

		readonly Sheet[] sheets;
		readonly Sprite emptySprite;

		readonly IVertexBuffer<Vertex> vertexBuffer;
		readonly Vertex[] vertices;
		readonly bool[] ignoreTint;

		// PERF / THREAD SAFETY: a write-only flag per vertex row, replacing a HashSet<int>.
		//
		// A row-partitioned parallel sweep (TerrainLighting.NotifyCells) has several threads inside
		// UpdateTint at once, and HashSet.Add is not safe under that -- two concurrent Adds can corrupt
		// the bucket chain outright, not merely lose an entry. A bool[] indexed by row has no such
		// structure to corrupt: every writer stores the same value (true) into a distinct-or-identical
		// byte, which is an atomic store on every architecture .NET supports, so the race is benign by
		// construction rather than by scheduling luck.
		//
		// VISIBILITY: workers write these during the sweep and Draw reads them later on the main thread.
		// Parallel.ForEach joins every worker before it returns, and that join is a full barrier, so
		// every flag set inside the sweep is visible to Draw without any explicit fence here.
		//
		// SIZED ONE PAST THE LAST ROW: Draw's flush loop runs `row <= lastRow` with lastRow clamped to
		// MapSize.Y, so it probes one row past the end. That row is never marked -- every writer below
		// goes through a map.Tiles.Contains check first -- so the extra slot is only there to keep the
		// probe in bounds, exactly as HashSet.Remove tolerated it.
		readonly bool[] dirtyRows;
		readonly int indexRowStride;
		readonly int vertexRowStride;
		readonly bool restrictToBounds;

		readonly WorldRenderer worldRenderer;
		readonly Map map;

		// PERF: UpdateTint's four corner samples live in a stackalloc'd Span, not on the heap and not
		// in a field. It used to be `var weights = new[] { ... }` INSIDE UpdateTint, i.e. one float3[4]
		// on the heap per cell PER LAYER per refresh -- and a nuclear fireball is the case that makes
		// that matter: its light radius exceeds every shipped map, so every refresh notifies every
		// in-bounds cell, and WW3MOD's world.yaml carries 13 to 20 of these layers (TerrainRenderer,
		// ShroudRenderer x2, seven SmudgeLayers at one or two each, ResourceRenderer x2,
		// BuildableTerrainOverlay). Order 16k cells x 20 layers x ~60 refreshes per warhead is millions
		// of short-lived arrays whose only job is to be indexed four times and dropped.
		//
		// IT WAS A FIELD AND COULD NOT STAY ONE. Hoisting the buffer out of the method removed the
		// allocation but made it shared mutable state, safe only while CellChanged was raised from one
		// thread. A row-partitioned sweep has several threads in UpdateTint at once, all four of them
		// writing those same four slots. stackalloc gives each call its own four, which is both
		// allocation-free and thread-safe without a comment having to promise anything about callers.

		// PERF: does this cell hold a real sprite on THIS layer? Indexed like ignoreTint, by the
		// cell's first vertex offset.
		//
		// WHY IT PAYS. Between 13 and 20 TerrainSpriteLayers subscribe to CellChanged in WW3MOD, and
		// 7 to 14 of them are SmudgeLayers (SmudgeLayer.cs:334 always, :500 when it has an overlay).
		// A smudge layer is EMPTY in almost every cell -- scorch marks exist only where something
		// burned -- yet every one of those empty cells still paid four TintAt calls, four vertex
		// rewrites and a dirtyRows.Add on every relight. A nuclear fireball's light radius exceeds
		// every shipped map, so "every relight" means every in-bounds cell, ~60 times per warhead.
		//
		// INVISIBLE BY CONSTRUCTION, on both halves of the question. A cell with no sprite was given
		// `emptySprite` and alpha 0 by Update below, and UpdateTint writes `v.A * weights[i]` -- so
		// the value it computes for an empty cell is zero whatever the tint is, and skipping the
		// write leaves the identical zero behind. And a sprite ARRIVING later is not a stale-tint
		// hazard either, because Update applies the tint itself on the same call that sets this flag
		// (see Update below): the new sprite is tinted at the moment it appears, not at the next relight.
		readonly bool[] hasSprite;

		readonly PaletteReference[] palettes;

		public TerrainSpriteLayer(World world, WorldRenderer wr, Sprite emptySprite, BlendMode blendMode, bool restrictToBounds)
		{
			worldRenderer = wr;
			this.restrictToBounds = restrictToBounds;
			this.emptySprite = emptySprite;
			sheets = new Sheet[SpriteRenderer.SheetCount];
			BlendMode = blendMode;
			map = world.Map;

			vertexRowStride = 4 * map.MapSize.X;
			vertices = new Vertex[vertexRowStride * map.MapSize.Y];
			vertexBuffer = Game.Renderer.Context.CreateVertexBuffer<Vertex>(vertices.Length);

			indexRowStride = 6 * map.MapSize.X;
			dirtyRows = new bool[map.MapSize.Y + 1];
			lock (IndexBuffers)
			{
				indexBufferWrapper = IndexBuffers.GetValue(world, world => new IndexBufferRc(world));
				indexBufferWrapper.AddRef();
			}

			palettes = new PaletteReference[map.MapSize.X * map.MapSize.Y];
			wr.PaletteInvalidated += UpdatePaletteIndices;

			if (wr.TerrainLighting != null)
			{
				ignoreTint = new bool[vertexRowStride * map.MapSize.Y];
				hasSprite = new bool[vertexRowStride * map.MapSize.Y];
				wr.TerrainLighting.CellChanged += UpdateTint;
			}
		}

		void UpdatePaletteIndices()
		{
			for (var i = 0; i < vertices.Length; i++)
			{
				var v = vertices[i];
				var p = palettes[i / 4]?.TextureIndex ?? 0;
				var c = (uint)((p & 0xFFFF) << 16) | (v.C & 0xFFFF);
				vertices[i] = new Vertex(v.X, v.Y, v.Z, v.S, v.T, v.U, v.V, c, v.R, v.G, v.B, v.A);
			}

			for (var row = 0; row < map.MapSize.Y; row++)
				dirtyRows[row] = true;
		}

		public void Clear(CPos cell)
		{
			Update(cell, null, null, 1f, 1f, true);
		}

		public void Update(CPos cell, ISpriteSequence sequence, PaletteReference palette, int frame)
		{
			Update(cell, sequence.GetSprite(frame), palette, sequence.Scale, sequence.GetAlpha(frame), sequence.IgnoreWorldTint);
		}

		public void Update(CPos cell, Sprite sprite, PaletteReference palette, float scale = 1f, float alpha = 1f, bool ignoreTint = false)
		{
			var xyz = float3.Zero;
			if (sprite != null)
			{
				var cellOrigin = map.CenterOfCell(cell) - new WVec(0, 0, map.Grid.Ramps[map.Ramp[cell]].CenterHeightOffset);
				xyz = worldRenderer.Screen3DPosition(cellOrigin) + scale * (sprite.Offset - 0.5f * sprite.Size);
			}

			Update(cell.ToMPos(map.Grid.Type), sprite, palette, xyz, scale, alpha, ignoreTint);
		}

		void UpdateTint(MPos uv)
		{
			var offset = vertexRowStride * uv.V + 4 * uv.U;

			// Nothing drawn here on this layer: the vertices are the empty quad at alpha 0, so every
			// tint resolves to the zero already stored. See the hasSprite declaration.
			if (!hasSprite[offset])
				return;

			if (ignoreTint[offset])
			{
				for (var i = 0; i < 4; i++)
				{
					var v = vertices[offset + i];
					vertices[offset + i] = new Vertex(v.X, v.Y, v.Z, v.S, v.T, v.U, v.V, v.C, v.A * float3.Ones, v.A);
				}

				return;
			}

			// Allow the terrain tint to vary linearly across the cell to smooth out the staircase effect
			// This is done by sampling the lighting the corners of the sprite, even though those pixels are
			// transparent for isometric tiles
			var tl = worldRenderer.TerrainLighting;
			var pos = map.CenterOfCell(uv.ToCPos(map));
			var step = map.Grid.TileScale / 2;

			// Same four samples, same order, same values -- in a per-call buffer. See the note above.
			Span<float3> tintWeights = stackalloc float3[4];
			tintWeights[0] = tl.TintAt(pos + new WVec(-step, -step, 0));
			tintWeights[1] = tl.TintAt(pos + new WVec(step, -step, 0));
			tintWeights[2] = tl.TintAt(pos + new WVec(step, step, 0));
			tintWeights[3] = tl.TintAt(pos + new WVec(-step, step, 0));

			ApplyCellTint(vertices, offset, tintWeights);

			dirtyRows[uv.V] = true;
		}

		/// <summary>
		/// Multiplies a cell's four vertices by its four corner weights, in place. The whole of the
		/// per-cell tint write, split out so the parallel sweep's byte-identity can be tested against the
		/// serial one without a Sheet or a vertex buffer to stand up. See ParallelTintSweepTest.
		/// </summary>
		/// <remarks>
		/// <para>THREAD SAFETY, and it is the whole reason a row partition is the only legal one: this
		/// writes four CONSECUTIVE vertices starting at <paramref name="offset"/>, and offset is
		/// vertexRowStride * V + 4 * U. Two cells collide here only if they share a V, so partitioning the
		/// sweep by vertex row makes the writes disjoint and no synchronisation is needed. Partitioning it
		/// any other way does not -- a Vertex is 48 bytes and a concurrent store to one TEARS.</para>
		/// </remarks>
		public static void ApplyCellTint(Vertex[] vertices, int offset, ReadOnlySpan<float3> weights)
		{
			// Apply tint directly to the underlying vertices
			// This saves us from having to re-query the sprite information, which has not changed
			for (var i = 0; i < 4; i++)
			{
				var v = vertices[offset + i];
				vertices[offset + i] = new Vertex(v.X, v.Y, v.Z, v.S, v.T, v.U, v.V, v.C, v.A * weights[i], v.A);
			}
		}

		int GetOrAddSheetIndex(Sheet sheet)
		{
			if (sheet == null)
				return 0;

			for (var i = 0; i < sheets.Length; i++)
			{
				if (sheets[i] == sheet)
					return i;

				if (sheets[i] == null)
				{
					sheets[i] = sheet;
					return i;
				}
			}

			throw new InvalidDataException("Sheet overflow");
		}

		public void Update(MPos uv, Sprite sprite, PaletteReference palette, in float3 pos, float scale, float alpha, bool ignoreTint)
		{
			// Captured BEFORE the emptySprite substitution below, which would otherwise make every
			// cell look occupied.
			var cellHasSprite = sprite != null;

			int2 samplers;
			if (sprite != null)
			{
				if (sprite.BlendMode != BlendMode)
					throw new InvalidDataException("Attempted to add sprite with a different blend mode");

				samplers = new int2(GetOrAddSheetIndex(sprite.Sheet), GetOrAddSheetIndex((sprite as SpriteWithSecondaryData)?.SecondarySheet));

				// PERF: Remove useless palette assignments for RGBA sprites
				// HACK: This is working around the limitation that palettes are defined on traits rather than on sequences,
				// and can be removed once this has been fixed
				if (sprite.Channel == TextureChannel.RGBA && !(palette?.HasColorShift ?? false))
					palette = null;
			}
			else
			{
				sprite = emptySprite;
				samplers = int2.Zero;
			}

			// The vertex buffer does not have geometry for cells outside the map
			if (!map.Tiles.Contains(uv))
				return;

			var offset = vertexRowStride * uv.V + 4 * uv.U;
			Util.FastCreateQuad(vertices, pos, sprite, samplers, palette?.TextureIndex ?? 0, offset, scale * sprite.Size, alpha * float3.Ones, alpha);
			palettes[uv.ToCellIndex(map)] = palette;

			if (worldRenderer.TerrainLighting != null)
			{
				this.ignoreTint[offset] = ignoreTint;
				hasSprite[offset] = cellHasSprite;

				// Applies the CURRENT tint to a sprite that has just arrived, which is what makes
				// the skip in UpdateTint safe rather than a deferred-tint bug.
				UpdateTint(uv);
			}

			dirtyRows[uv.V] = true;
		}

		public void Draw(Viewport viewport)
		{
			var cells = restrictToBounds ? viewport.VisibleCellsInsideBounds : viewport.AllVisibleCells;

			// Only draw the rows that are visible.
			var firstRow = cells.CandidateMapCoords.TopLeft.V.Clamp(0, map.MapSize.Y);
			var lastRow = (cells.CandidateMapCoords.BottomRight.V + 1).Clamp(firstRow, map.MapSize.Y);

			Game.Renderer.Flush();

			// Flush any visible changes to the GPU
			for (var row = firstRow; row <= lastRow; row++)
			{
				if (!dirtyRows[row])
					continue;

				dirtyRows[row] = false;

				var rowOffset = vertexRowStride * row;
				vertexBuffer.SetData(vertices, rowOffset, rowOffset, vertexRowStride);
			}

			Game.Renderer.WorldSpriteRenderer.DrawVertexBuffer(
				vertexBuffer, indexBufferWrapper.Buffer, indexRowStride * firstRow,
				indexRowStride * (lastRow - firstRow), sheets, BlendMode);

			Game.Renderer.Flush();
		}

		public void Dispose()
		{
			worldRenderer.PaletteInvalidated -= UpdatePaletteIndices;
			if (worldRenderer.TerrainLighting != null)
				worldRenderer.TerrainLighting.CellChanged -= UpdateTint;

			vertexBuffer.Dispose();

			lock (IndexBuffers)
				indexBufferWrapper.Dispose();
		}

		sealed class IndexBufferRc : IDisposable
		{
			public IIndexBuffer Buffer;
			int count;

			public IndexBufferRc(World world)
			{
				Buffer = Game.Renderer.Context.CreateIndexBuffer(Util.CreateQuadIndices(world.Map.MapSize.X * world.Map.MapSize.Y));
			}

			public void AddRef() { count++; }

			public void Dispose()
			{
				count--;
				if (count == 0)
					Buffer.Dispose();
			}
		}
	}
}
