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
using OpenRA.Graphics;
using OpenRA.Mods.Common.Lighting;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Graphics
{
	/// <summary>
	/// WW3MOD: puts a light on the ground OUTSIDE the cell grid, where there is no ground.
	/// <para>A light lands on the map through <see cref="TerrainLighting"/>, which is a tint written into the
	/// terrain vertices, and <see cref="FogPiercingLightRenderable"/>, which adds back whatever the fog quads
	/// took off those vertices. Both are per-CELL, and both stop in the same place, because there are no cells
	/// past MapSize: TerrainLighting.NotifyCells skips anything map.Tiles does not contain (the vertex buffer
	/// has no geometry for it) and FogPiercingLightRenderable skips anything map.Height does not contain. Past
	/// the grid WorldRenderer.DrawBeyondMapFog fills the screen with opaque black instead, and until this
	/// renderable existed nothing ever drew light on that black -- so a nuclear flash lit the map and stopped
	/// dead at a rectangle while its own fire sprites went on burning outside it. That was a deliberate rule
	/// ("nothing may put light on it") and the user has overridden it.</para>
	/// <para>THIS IS THE COMPLEMENT OF <see cref="FogPiercingLightRenderable"/>, not a replacement: that one
	/// draws where map.Height.Contains is true, this one draws where it is false, so no pixel is written by
	/// both and no light is doubled anywhere. The tessellation is the same cell lattice continued past the grid
	/// edge -- Map.CenterOfCell is arithmetic and answers for an out-of-range cell -- and the corner sampling is
	/// the same, so the quads out here share their corner POSITIONS, and therefore their colours, with the
	/// on-grid quads along the seam. That shared corner is the whole reason the join is invisible rather than
	/// merely close.</para>
	/// <para>DRAWN FROM THE ABOVE-SHROUD SLOT, and it has to be. WorldRenderer.DrawBeyondMapActorFog lays
	/// per-border-cell fog strips over this same region AFTER the above-fog renderables, to dim actor sprites
	/// hanging off the map edge. A glow drawn in the above-fog slot would be dimmed by those strips while the
	/// on-grid half of the same light is not -- FogPiercingLightRenderable has already put the fog's share back
	/// -- so the light would step down by the fog transmission, under a seventh at the shipped FogDarkness,
	/// exactly along the grid edge. That is the same rectangle, fainter. Drawing after the strips instead means
	/// the value written here IS the final value, so it is made equal to the on-grid net directly: see
	/// <see cref="SurvivalByVisibility"/>.</para>
	/// <para>NO VISION IS LEAKED. The region past the cell grid holds no terrain, no resources and no cells, so
	/// there is nothing out there to reveal; and the colour of every quad below is a function of the light alone
	/// (position, radius, intensity, tint) plus the render player's OWN visibility, and of nothing else -- no
	/// actor, no ownership, no terrain is consulted. Actors CAN be out here (aircraft leaving the map, missiles
	/// at altitude), and they are unaffected: they are drawn long before this, an additive quad cannot uncover a
	/// sprite the fog strips have already blacked out, and brightening a region uniformly adds no silhouette.
	/// The visibility that gates the light is read through
	/// <see cref="ShroudRenderer.ClampToPlayable(Map, PPos)"/>, the same authority that chooses which fog quad a
	/// ring cell receives and which strip DrawBeyondMapActorFog lays down out here, so a quad whose nearest
	/// playable cell is unexplored stays black -- which is what the shroud draws there anyway.</para>
	/// </summary>
	public class BeyondMapLightRenderable : IRenderable, IFinalizedRenderable
	{
		/// <summary>Below this the quad is invisible at 8 bits per channel and not worth submitting.</summary>
		const float MinimumChannel = 0.5f / 255f;

		readonly WPos center;
		readonly WDist range;
		readonly float intensity;
		readonly float3 tint;
		readonly LightFalloff falloff;
		readonly float fogDarkness;
		readonly bool glowAboveFog;
		readonly int zOffset;

		public BeyondMapLightRenderable(WPos center, WDist range, float intensity, in float3 tint,
			LightFalloff falloff, float fogDarkness, bool glowAboveFog, int zOffset = 0)
		{
			this.center = center;
			this.range = range;
			this.intensity = intensity;
			this.tint = tint;
			this.falloff = falloff;
			this.fogDarkness = fogDarkness;
			this.glowAboveFog = glowAboveFog;
			this.zOffset = zOffset;
		}

		public WPos Pos => center;
		public int ZOffset => zOffset;
		public bool IsDecoration => true;

		public IRenderable WithZOffset(int newOffset)
		{
			return new BeyondMapLightRenderable(center, range, intensity, tint, falloff, fogDarkness, glowAboveFog, newOffset);
		}

		public IRenderable OffsetBy(in WVec vec)
		{
			return new BeyondMapLightRenderable(center + vec, range, intensity, tint, falloff, fogDarkness, glowAboveFog, zOffset);
		}

		public IRenderable AsDecoration() { return this; }

		public IFinalizedRenderable PrepareRender(WorldRenderer wr) { return this; }

		/// <summary>
		/// How much of a light's contribution is still on screen INSIDE the grid once the whole shroud stack has
		/// been drawn, per visibility level. This is the number an off-grid quad has to reproduce, and
		/// reproducing it is what makes the grid edge stop being visible.
		/// <para>With GlowAboveFog the on-grid net is the full contribution at every explored level: the fog
		/// transmits <c>t</c> of it and FogPiercingLightRenderable adds back <c>1 - t</c>. Without it the fog
		/// keeps its share and the net is <c>t</c>. Visibility 0 is zero either way -- the opaque unexplored
		/// layer erases rather than darkens, so nothing of an on-grid light survives there and nothing of this
		/// one may either. That zero is the no-leak boundary, and it is the one entry that must never be
		/// interpolated or rounded up.</para>
		/// </summary>
		public static float SurvivalByVisibility(int visibility, bool glowAboveFog, float fogDarkness)
		{
			if (visibility <= 0)
				return 0f;

			var transmission = ShroudRenderer.CompositeTransmission(visibility, fogDarkness);
			return glowAboveFog ? 1f : transmission;
		}

		public void Render(WorldRenderer wr)
		{
			var rangeLength = range.Length;
			if (rangeLength <= 0 || intensity == 0f)
				return;

			var world = wr.World;
			var map = world.Map;
			var cr = Game.Renderer.WorldRgbaColorRenderer;

			// AllVisibleCells is deliberately NOT clamped to the map (Viewport.CalculateVisibleCells passes
			// insideBounds false), so its corners run past the grid exactly as far as the screen does. That is
			// what lets a viewport-bounded sweep reach cells which do not exist.
			var visible = wr.Viewport.AllVisibleCells;
			var reach = rangeLength + 1024;
			var lightTL = map.CellContaining(center - new WVec(reach, reach, 0)).ToMPos(map);
			var lightBR = map.CellContaining(center + new WVec(reach, reach, 0)).ToMPos(map);

			var minU = Math.Max(visible.TopLeft.U, lightTL.U);
			var maxU = Math.Min(visible.BottomRight.U, lightBR.U);
			var minV = Math.Max(visible.TopLeft.V, lightTL.V);
			var maxV = Math.Min(visible.BottomRight.V, lightBR.V);
			if (minU > maxU || minV > maxV)
				return;

			// No render player means no shroud and no fog were drawn for anyone -- the shellmap, observers and
			// TestMode. DrawBeyondMapActorFog returns early in exactly that case too, so nothing dims this
			// region afterwards and the light reaches the screen whole.
			var mapLayers = world.RenderPlayer?.MapLayers;

			// One entry per visibility level, so the composite fog curve is walked ten times per light rather
			// than once per quad.
			var survives = new float[MapLayers.VisionLayers];
			for (var i = 0; i < MapLayers.VisionLayers; i++)
				survives[i] = SurvivalByVisibility(i, glowAboveFog, fogDarkness);

			for (var v = minV; v <= maxV; v++)
			{
				for (var u = minU; u <= maxU; u++)
				{
					var uv = new MPos(u, v);

					// The half of the split. A cell the grid HAS is lit by TerrainLighting and corrected by
					// FogPiercingLightRenderable; drawing here as well would add the light to it a second time.
					if (map.Height.Contains(uv))
						continue;

					var mask = mapLayers == null
						? 1f
						: survives[mapLayers.GetVisibility(ShroudRenderer.ClampToPlayable(map, (PPos)uv))];
					if (mask <= 0f)
						continue;

					var cellCenter = map.CenterOfCell(uv.ToCPos(map));

					// Sampled at the four corners and interpolated across the quad, not filled flat from the
					// centre. Flat fill is what made the on-grid half of this same light resolve into a visible
					// grid of tiles, and the cure is the same one: the corner POSITIONS are used twice each, once
					// as the point the light is sampled at and once as the vertex the quad is drawn to, so a
					// neighbouring quad derives the corner it shares from its own centre to the very same WPos
					// and the two agree on the colour all along their common edge. The on-grid quad across the
					// seam derives it the same way, which is why the seam itself disappears.
					var tlPos = cellCenter + new WVec(-512, -512, 0);
					var trPos = cellCenter + new WVec(512, -512, 0);
					var brPos = cellCenter + new WVec(512, 512, 0);
					var blPos = cellCenter + new WVec(-512, 512, 0);

					// The mask stays per-quad and multiplies all four corners equally. It is NOT interpolated and
					// must not be: it is 0 where the governing playable cell is unexplored, and smearing a
					// neighbour's non-zero value into that corner would put light where the shroud says none.
					// Smooth light, hard mask -- the mask is the half carrying the no-leak guarantee.
					var cornerTL = mask * ContributionAt(tlPos);
					var cornerTR = mask * ContributionAt(trPos);
					var cornerBR = mask * ContributionAt(brPos);
					var cornerBL = mask * ContributionAt(blPos);

					// All four, not the centre: a quad straddling the light's outer edge has dark corners and a
					// bright one, and testing the centre alone drops it -- cutting the glow off with exactly the
					// kind of hard edge this renderable exists to remove.
					if (BelowMinimum(cornerTL) && BelowMinimum(cornerTR) && BelowMinimum(cornerBR) && BelowMinimum(cornerBL))
						continue;

					cr.FillRect(
						wr.Screen3DPxPosition(tlPos), wr.Screen3DPxPosition(trPos),
						wr.Screen3DPxPosition(brPos), wr.Screen3DPxPosition(blPos),
						ToColor(cornerTL), ToColor(cornerTR), ToColor(cornerBR), ToColor(cornerBL), BlendMode.Additive);
				}
			}
		}

		/// <summary>
		/// The light's own contribution at a position. Shared with <see cref="FogPiercingLightRenderable"/>
		/// rather than re-derived, so the off-grid quads and the on-grid ones are the same function of position
		/// and meet at the seam by construction instead of by agreement.
		/// </summary>
		float3 ContributionAt(WPos pos)
		{
			return FogPiercingLightRenderable.ContributionAt(center, range, intensity, tint, falloff, pos);
		}

		/// <summary>True when a contribution is too dark to show at 8 bits per channel.</summary>
		static bool BelowMinimum(in float3 c)
		{
			return c.X < MinimumChannel && c.Y < MinimumChannel && c.Z < MinimumChannel;
		}

		/// <summary>
		/// Packs a contribution into a premultiplied additive colour. Alpha is 255 and carries no transparency:
		/// BlendMode.Additive is GL_ONE / GL_ONE and RgbaColorRenderer premultiplies, so alpha 255 is what makes
		/// the RGB reach the framebuffer at face value.
		/// </summary>
		static Color ToColor(in float3 c)
		{
			return Color.FromArgb(255, Channel(c.X), Channel(c.Y), Channel(c.Z));
		}

		static int Channel(float v)
		{
			if (v <= 0f)
				return 0;

			var b = (int)(v * 255f + 0.5f);
			return b > 255 ? 255 : b;
		}

		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr) { return Rectangle.Empty; }
	}
}
