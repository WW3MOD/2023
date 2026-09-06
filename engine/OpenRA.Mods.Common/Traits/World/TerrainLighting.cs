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
			}
		}

		/// <summary>Moves a live source, for a light carried by something that moves.</summary>
		public void MoveLightSource(int token, WPos pos)
		{
			if (!lightSources.TryGetValue(token, out var source) || source.Pos == pos)
				return;

			source.Pos = pos;
			partitionedLightSources.Update(source, Bounds(source));
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
			if (CellChanged == null)
				return;

			// TerrainSpriteLayer samples a cell's lighting at its four CORNERS, half a tile out from the centre in
			// each direction. Widening the search by the half-diagonal of a cell (724 = 1024 / sqrt 2) guarantees
			// we notify every cell with a lit corner, not just every cell with a lit centre.
			var search = range.Length + 724;
			var searchSq = (long)search * search;

			var topLeft = map.CellContaining(pos - new WVec(search, search, 0));
			var bottomRight = map.CellContaining(pos + new WVec(search, search, 0));

			for (var y = topLeft.Y; y <= bottomRight.Y; y++)
			{
				for (var x = topLeft.X; x <= bottomRight.X; x++)
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

					CellChanged(uv);
				}
			}
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
			if (!PerfHistory.Sampling)
				return Tint(pos);

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
