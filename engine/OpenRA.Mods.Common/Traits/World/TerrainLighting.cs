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
using System.Diagnostics;
using OpenRA.Mods.Common.Lighting;
using OpenRA.Primitives;
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World | SystemActors.EditorWorld)]
	[Desc("Add to the world actor to apply a global lighting tint and allow actors using the TerrainLightSource",
		"or LightEventManager to add localised lighting.")]
	public class TerrainLightingInfo : TraitInfo, ILobbyCustomRulesIgnore
	{
		[Desc("Ambient intensity. 1 with neutral tints is an exact no-op: TintAt returns (1,1,1), which is",
			"the identity for both consumers (SpriteRenderable multiplies by it; TerrainSpriteLayer writes",
			"alpha * (1,1,1), the same vertex colour Util.FastCreateQuad writes without this trait).")]
		public readonly float Intensity = 1;

		public readonly float HeightStep = 0;
		public readonly float RedTint = 1;
		public readonly float GreenTint = 1;
		public readonly float BlueTint = 1;

		[Desc("Size of light source partition bins (cells)")]
		public readonly int BinSize = 10;

		[Desc("Spread a terrain relight across the thread pool, one contiguous band of vertex rows per worker.",
			"CHANGES NO OUTPUT, only who computes it: the same cells are notified in the same per-cell order",
			"and every vertex ends up with the tint the serial sweep would have written, bit for bit. It is a",
			"switch so that a suspected rendering defect can be bisected from YAML without a rebuild; set it",
			"false and the sweep runs inline on the calling thread exactly as it did before.",
			"Ignored on isometric grids and while the perf overlay is sampling - see NotifyCells.")]
		public readonly bool ParallelSweep = true;

		public override object Create(ActorInitializer init) { return new TerrainLighting(init.World, this); }
	}

	public sealed class TerrainLighting : ITerrainLighting
	{
		// Windowed inverse square: w(u) = (1/(1+k u^2) - 1/(1+k)) * (1+k)/k, with u the normalised distance
		// from the centre. w(0) == 1 and w(1) == 0 exactly, so the light still ends cleanly at its radius
		// instead of needing a separate cutoff. k sets how hard the core is; 24 puts the half-brightness
		// point at about 20% of the radius, which reads as a point source rather than a disc.
		const float InverseSquareK = 24f;
		const float InverseSquareEdge = 1f / (1f + InverseSquareK);
		const float InverseSquareScale = 1f / (1f - InverseSquareEdge);

		sealed class LightSource
		{
			public readonly LightFalloff Falloff;
			public readonly LightBlend Blend;

			// Mutable so a time-varying event can be updated in place. Rebuilding the source instead would
			// mean Remove + Add, and both of those raise CellChanged over the whole radius - see the cost
			// note on NotifyCells for why doing that every tick is not an option.
			public WPos Pos;
			public WDist Range;
			public float Intensity;
			public float3 Tint;

			public LightSource(WPos pos, WDist range, float intensity, in float3 tint, LightFalloff falloff, LightBlend blend)
			{
				Pos = pos;
				Range = range;
				Intensity = intensity;
				Tint = tint;
				Falloff = falloff;
				Blend = blend;
			}
		}

		readonly TerrainLightingInfo info;
		readonly Map map;
		readonly Dictionary<int, LightSource> lightSources = new();
		readonly SpatiallyPartitioned<LightSource> partitionedLightSources;
		readonly float3 globalTint;
		int nextLightSourceToken = 1;

		// ==== THE SWEEP MEMO ====
		// CellChanged is a MULTICAST delegate raised ONCE PER CELL, so every subscriber runs for
		// cell A before the sweep moves to cell B -- and each subscriber is a TerrainSpriteLayer
		// whose UpdateTint samples the SAME FOUR corner positions of that cell. WW3MOD's world.yaml
		// carries ~20 of those layers (TerrainRenderer, ShroudRenderer x2, seven SmudgeLayers at
		// two layers each, ResourceRenderer x2, BuildableTerrainOverlay), so one notified cell costs
		// ~80 TintAt calls over four distinct arguments: the first layer computes the four, and the
		// other nineteen recompute the identical numbers.
		//
		// Four slots is therefore exactly the working set, and the hit rate is (layers-1)/layers.
		// What is avoided per hit is the real work in Tint: a SpatiallyPartitioned query at the
		// sample point plus a falloff evaluation and a blend per light in range -- and during a
		// nuclear salvo "per light in range" is up to six overlapping fireballs.
		//
		// CORRECTNESS. The memo is live ONLY between the entry and exit of the per-partition body in
		// NotifyCells, which is why it needs no invalidation hook on the six methods that mutate
		// lightSources: none of them can run inside that window. Within one sweep nothing a Tint
		// result depends on moves -- CellChanged subscribers write vertices and nothing else -- so a
		// hit returns bit-for-bit what a recompute would have returned. OUTSIDE the sweep TintAt is
		// untouched, which is what keeps the per-sprite-per-frame render path
		// (SpriteRenderable.cs:116) exactly as it was.
		//
		// PER-PARTITION, NOT PER-TRAIT, and that is why the state lives in SweepMemo's [ThreadStatic]
		// fields rather than in fields here. The memo's contents are only valid for the one cell a
		// sweep is currently on; a row-partitioned sweep has one such cell PER WORKER, and fields on
		// this trait would have every worker clobbering every other worker's four slots. That failure
		// would not be a crash but a hit returning another cell's tint -- silently wrong colours.
		//
		// THIS IS ALSO STRICTLY SAFER THAN THE FIELDS IT REPLACES: a thread that never called
		// SweepMemo.Begin sees Sweeping == false and takes the ordinary path, so no caller outside the
		// sweep can observe a half-written memo. The old shared fields only forbade that by comment.

		// Bumped by every mutation of lightSources or of its partition. Snapshotted around a sweep so a
		// Debug build can prove the no-mutation invariant the concurrent partition reads depend on,
		// rather than leaving it as an assertion in a comment. See AssertSourcesUnchanged.
		int lightSourceVersion;

		public event Action<MPos> CellChanged = null;

		public TerrainLighting(World world, TerrainLightingInfo info)
		{
			this.info = info;
			map = world.Map;
			globalTint = new float3(info.RedTint, info.GreenTint, info.BlueTint);

			var tileScale = map.Grid.TileScale;
			partitionedLightSources = new SpatiallyPartitioned<LightSource>(
				(map.MapSize.X + 1) * tileScale,
				(map.MapSize.Y + 1) * tileScale,
				info.BinSize * tileScale);
		}

		static Rectangle Bounds(LightSource source)
		{
			var c = source.Pos;
			var r = source.Range.Length;
			return new Rectangle(c.X - r, c.Y - r, 2 * r, 2 * r);
		}

		public int AddLightSource(WPos pos, WDist range, float intensity, in float3 tint)
		{
			return AddLightSource(pos, range, intensity, tint, LightFalloff.Linear, LightBlend.Legacy, true);
		}

		public int AddLightSource(WPos pos, WDist range, float intensity, in float3 tint,
			LightFalloff falloff, LightBlend blend, bool notifyTerrain)
		{
			// PITFALL: SpatiallyPartitioned.Add throws on a zero-width Rectangle, so a zero range is fatal here.
			if (range.Length <= 0)
				throw new ArgumentOutOfRangeException(nameof(range), "A light source needs a range greater than zero.");

			var token = nextLightSourceToken++;
			var source = new LightSource(pos, range, intensity, tint, falloff, blend);
			lightSources.Add(token, source);
			partitionedLightSources.Add(source, Bounds(source));
			lightSourceVersion++;

			if (notifyTerrain)
				NotifyCells(pos, range);

			return token;
		}

		public void RemoveLightSource(int token)
		{
			RemoveLightSource(token, true);
		}

		public void RemoveLightSource(int token, bool notifyTerrain)
		{
			if (!lightSources.TryGetValue(token, out var source))
				return;

			lightSources.Remove(token);
			partitionedLightSources.Remove(source);
			lightSourceVersion++;

			if (notifyTerrain)
				NotifyCells(source.Pos, source.Range);
		}

		/// <summary>
		/// Rewrites a live source's intensity, colour and radius WITHOUT touching the terrain. Sprites pick the
		/// new values up on the next frame for free, because SpriteRenderable.Render calls TintAt per sprite per
		/// frame. Call <see cref="RefreshTerrain"/> separately, and less often, to push the change to the ground.
		/// </summary>
		public void UpdateLightSource(int token, WDist range, float intensity, in float3 tint)
		{
			if (!lightSources.TryGetValue(token, out var source))
				return;

			source.Intensity = intensity;
			source.Tint = tint;

			if (source.Range != range && range.Length > 0)
			{
				source.Range = range;
				partitionedLightSources.Update(source, Bounds(source));
				lightSourceVersion++;
			}
		}

		/// <summary>Moves a live source, for a light carried by something that moves.</summary>
		public void MoveLightSource(int token, WPos pos)
		{
			if (!lightSources.TryGetValue(token, out var source) || source.Pos == pos)
				return;

			source.Pos = pos;
			partitionedLightSources.Update(source, Bounds(source));
			lightSourceVersion++;
		}

		/// <summary>Current radius of a live source, or WDist.Zero if the token is not live.</summary>
		public WDist RangeOf(int token)
		{
			return lightSources.TryGetValue(token, out var source) ? source.Range : WDist.Zero;
		}

		/// <summary>
		/// Marks the terrain under a live source dirty. <paramref name="previousRange"/> lets a caller that has
		/// shrunk the light also clean up the ring it no longer covers; pass WDist.Zero if the radius has not
		/// gone down. This is the expensive half of the system - see NotifyCells.
		/// </summary>
		public void RefreshTerrain(int token, WDist previousRange)
		{
			if (!lightSources.TryGetValue(token, out var source))
				return;

			NotifyCells(source.Pos, previousRange.Length > source.Range.Length ? previousRange : source.Range);
		}

		// PITFALL: this used to be map.FindTilesInCircle(cell, ceil(range / 1024)), and that was a CRASH.
		// FindTilesInAnnulus THROWS above MapGrid.MaximumTileSearchRange (56 cells, MapGrid.cs:113) rather than
		// clamping (Map.cs:1994), so any light with a radius over 56 cells took the game down at the moment it was
		// created. A nuclear fireball wants a radius several times that. The rectangular sweep below has no such
		// ceiling, is a strict superset of the cells the old call returned, and is cheaper - it does not walk the
		// precomputed TilesByDistance offset lists or allocate their enumerators, and it needs no distance ordering.
		//
		// COST: O(cells in the bounding box). For a 100-cell radius that is a 200x200 sweep, of which up to the whole
		// map passes the bounds check, and every passing cell costs the subscriber four TintAt calls plus a vertex
		// row marked dirty for re-upload. Do NOT call this every tick for a large light; throttle it, which is what
		// LightEventDefinition.TerrainRefreshInterval exists for.

		/// <summary>Raises CellChanged for every map cell the light can reach.</summary>
		public void NotifyCells(WPos pos, WDist range)
		{
			// Snapshotted rather than read twice: the field is a multicast delegate and a subscriber could
			// in principle be added between the null check and the invoke.
			var changed = CellChanged;
			if (changed == null)
				return;

			// TerrainSpriteLayer samples a cell's lighting at its four CORNERS, half a tile out from the centre in
			// each direction. Widening the search by the half-diagonal of a cell (724 = 1024 / sqrt 2) guarantees
			// we notify every cell with a lit corner, not just every cell with a lit centre.
			var search = range.Length + 724;
			var searchSq = (long)search * search;

			var topLeft = map.CellContaining(pos - new WVec(search, search, 0));
			var bottomRight = map.CellContaining(pos + new WVec(search, search, 0));

			var cols = bottomRight.X - topLeft.X + 1;
			if (cols <= 0)
				return;

			// ==== WHY THIS MAY BE SPLIT ACROSS THREADS, in four parts ====
			//
			// (1) THE WRITES ARE DISJOINT BY ROW. The only thing a subscriber does is write the four
			//     vertices at TerrainSpriteLayer's `vertexRowStride * V + 4 * U` and set a flag for row V.
			//     Two cells touch the same bytes only if they share a V, so a partition into contiguous
			//     bands of V needs no synchronisation at all. A Vertex is 48 bytes and a concurrent store
			//     to one TEARS, so this is the property the whole change rests on -- hence the grid check
			//     below, which is the one place it can fail.
			//
			// (2) THE PER-LAYER SCRATCH IS GONE. UpdateTint's four corner weights were a field on the
			//     layer, shared by every cell it was asked about; they are now stackalloc'd per call.
			//
			// (3) THE MEMO IS PER THREAD rather than per trait. See its note above.
			//
			// (4) THE PARTITION IS READ-ONLY FOR THE DURATION. SpatiallyPartitioned.At is a pure iterator
			//     over one bin -- it reads, yields and mutates nothing (SpatiallyPartitioned.cs:100-107) --
			//     so any number of threads may walk it at once PROVIDED no light source is added, removed,
			//     moved or resized while they do. That invariant is not new: the memo has always depended
			//     on it, because a source appearing mid-sweep would make an already-memoised corner stale.
			//     It holds because the mutating methods are all reached from the simulation tick on the
			//     main thread, which is the thread sitting inside this call. AssertSourcesUnchanged checks
			//     it for real in Debug rather than leaving it as a promise.
			//
			// ROWS OF WHAT, exactly: (1) is about MPos.V, the VERTEX row, and the loop below walks CPos.Y.
			// Those are the same number only on a Rectangular grid, where CPos.ToMPos is the identity
			// (CPos.cs:77-78). On RectangularIsometric it is `v = X + Y` (CPos.cs:91), so one CPos row is a
			// DIAGONAL across vertex rows and two different CPos rows share vertex rows -- a Y partition
			// there would put two threads on one Vertex and tear it. WW3MOD is Rectangular
			// (mods/ww3mod/mod.yaml:380) and so is stock RA; TS and D2K are not. Rather than rewrite the
			// sweep in MPos space for grids this branch cannot test, isometric keeps the serial path.
			//
			// AND NOT WHILE THE PERF OVERLAY IS SAMPLING. A sampled TintAt goes through PerfSample, whose
			// Dispose calls PerfHistory.Increment -> `Items[item].Val += x` (PerfHistory.cs:59) -- a
			// string-keyed Cache lookup that can INSERT, over a plain Dictionary, plus a non-atomic double
			// accumulate. Concurrent inserts corrupt a Dictionary rather than merely losing a count.
			// Falling back to serial keeps that instrument exactly as it was, which is also what the
			// nuke-perf rig's Profile B is for: it answers "is it the lighting?", while Profile A
			// (--hidden, unsampled) answers "how much did that cost" and is where this change is measured.
			var parallel = info.ParallelSweep
				&& map.Grid.Type == MapGridType.Rectangular
				&& !PerfHistory.Sampling;

			var version = lightSourceVersion;

			CellRowSweep.Run(topLeft.Y, bottomRight.Y + 1, cols, parallel,
				(fromRow, toRow) => SweepRows(changed, pos, searchSq, topLeft.X, bottomRight.X, fromRow, toRow));

			AssertSourcesUnchanged(version);
		}

		/// <summary>One partition of <see cref="NotifyCells"/>: the rows in [fromRow, toRow).</summary>
		void SweepRows(Action<MPos> changed, WPos pos, long searchSq, int minX, int maxX, int fromRow, int toRow)
		{
			// The memo is armed for the duration of this partition and for nothing else. See its
			// declaration. try/finally rather than a bare assignment because a subscriber is arbitrary
			// code: one that throws must not leave TintAt memoising forever on this thread, which would
			// freeze the terrain's lighting at whatever four samples were in the buffer.
			SweepMemo.Begin();
			try
			{
				for (var y = fromRow; y < toRow; y++)
				{
					for (var x = minX; x <= maxX; x++)
					{
						var cell = new CPos(x, y);
						var uv = cell.ToMPos(map);

						// The vertex buffer only has geometry for cells inside MapSize, and TerrainSpriteLayer.UpdateTint
						// indexes it without a bounds check of its own.
						if (!map.Tiles.Contains(uv))
							continue;

						// Ground cells are lit by horizontal distance; the light's altitude is irrelevant to which of
						// them it reaches, and including Z here would shrink the footprint of an airburst to nothing.
						var delta = map.CenterOfCell(cell) - pos;
						if ((long)delta.X * delta.X + (long)delta.Y * delta.Y > searchSq)
							continue;

						// Each cell brings its own four sample points, so the previous cell's entries are
						// dead the moment this one starts. Dropping them costs nothing and keeps the
						// lookup a scan of at most four live slots rather than four stale ones.
						SweepMemo.NextCell();

						changed(uv);
					}
				}
			}
			finally
			{
				SweepMemo.End();
			}
		}

		/// <summary>
		/// Debug-only detector for part (4) of the argument in <see cref="NotifyCells"/>: nothing may add,
		/// remove, move or resize a light source while a sweep is reading the partition.
		/// </summary>
		// After the fact rather than in the way, because it is a DETECTOR and not a guard: checking per
		// cell is exactly the kind of cost this branch exists to remove, and a violation is a programming
		// error to be found in a Debug run, not a condition to recover from at runtime. ConditionalAttribute
		// compiles it out of Release entirely, so it is free in the build that ships.
		[Conditional("DEBUG")]
		void AssertSourcesUnchanged(int version)
		{
			if (lightSourceVersion != version)
				throw new InvalidOperationException(
					"A light source was added, removed, moved or resized during a terrain relight. " +
					"The sweep memo and the parallel partition both read the light set without locking " +
					"and require it to be frozen for the duration of NotifyCells.");
		}

		/// <summary>Maps a normalised falloff in [0,1] (1 at the centre, 0 at the edge) through the requested curve.</summary>
		public static float ApplyFalloff(LightFalloff falloff, float f)
		{
			switch (falloff)
			{
				case LightFalloff.Quadratic:
					return f * f;
				case LightFalloff.InverseQuadratic:
					return f * (2f - f);
				case LightFalloff.Smoothstep:
					return f * f * (3f - 2f * f);
				case LightFalloff.InverseSquare:
					var u = 1f - f;
					return (1f / (1f + InverseSquareK * u * u) - InverseSquareEdge) * InverseSquareScale;
				default:
					return f;
			}
		}

		// PERF: this runs PER SPRITE PER FRAME (SpriteRenderable.cs:116) and, on the terrain path, four times
		// per dirtied cell. The PerfSample that used to wrap the whole body unconditionally costs two
		// Stopwatch.GetTimestamp() calls and a string-keyed Cache lookup - benchmarked at 53.5 ns sampled
		// against 3.6 ns unsampled, so 14.8x the work it was measuring, spent whether or not anything was
		// reading the number. The branch below costs 1.1 ns. That was free before WW3MOD enabled this trait, because WorldRenderer.
		// TerrainLighting was null and TintAt was never called at all; enabling it mod-wide turned it into a
		// permanent global tax. PerfHistory.Sampling is true only while the perf graph, the perf text overlay
		// or benchmark mode is live, so the `terrain_lighting` trace still reports exactly when someone is
		// reading it, and costs ~1 ns otherwise.
		float3 ITerrainLighting.TintAt(WPos pos)
		{
			// The sweep memo, checked ahead of the sampling branch on purpose: a hit is the cheapest
			// thing this method can do and there is nothing worth measuring about it. A MISS is still
			// sampled exactly as before, so `terrain_lighting` keeps reporting real Tint work -- the
			// trace gets smaller because the work got smaller, not because it stopped being counted.
			if (SweepMemo.TryGet(pos, out var memoised))
				return memoised;

			var tint = PerfHistory.Sampling ? Sampled(pos) : Tint(pos);

			// Only ever four distinct sample points are live at once (the four corners of the cell
			// currently being notified), and the memo is reset per cell, so this cannot overflow in the
			// shape NotifyCells produces. Store's own guard is for any future caller that samples a
			// fifth point inside a sweep: such a point is simply not memoised, which is correct but
			// slow, rather than silently evicting one that is.
			SweepMemo.Store(pos, tint);

			return tint;
		}

		float3 Sampled(WPos pos)
		{
			using (new PerfSample("terrain_lighting"))
				return Tint(pos);
		}

		/// <summary>The body of TintAt, split out so the sampled and unsampled paths share one copy of it.</summary>
		float3 Tint(WPos pos)
		{
			var uv = map.CellContaining(pos).ToMPos(map);
			var tint = globalTint;
			if (!map.Height.Contains(uv))
				return tint;

			var intensity = info.Intensity + info.HeightStep * map.Height[uv];
			if (lightSources.Count == 0)
				return intensity * tint;

			var additive = float3.Zero;
			foreach (var source in partitionedLightSources.At(new int2(pos.X, pos.Y)))
			{
				var range = source.Range.Length;
				var distance = (source.Pos - pos).Length;
				if (distance > range)
					continue;

				var falloff = ApplyFalloff(source.Falloff, (range - distance) * 1f / range);
				if (source.Blend == LightBlend.Additive)
					additive += falloff * source.Intensity * source.Tint;
				else
				{
					intensity += falloff * source.Intensity;
					tint += falloff * source.Tint;
				}
			}

			// With no Additive sources this is exactly the historical `intensity * tint`: adding a zero float3
			// is exact in IEEE754, so the vanilla TerrainLightSource path is unchanged to the bit.
			return intensity * tint + additive;
		}
	}
}
