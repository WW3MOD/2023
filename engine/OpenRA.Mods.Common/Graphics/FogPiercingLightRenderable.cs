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
	/// WW3MOD: puts back the part of a light that the fog took away.
	/// <para>A light reaches the screen through <see cref="TerrainLighting"/>, which is a TINT applied
	/// inside the ordinary sprite and terrain-vertex draw. All of that happens before ShroudRenderer
	/// paints its fog quads over the finished world, so a light under fog is not hidden -- it is
	/// ATTENUATED, by exactly the factor the fog stack transmits. At the shipped FogDarkness that
	/// factor is about an eighth over fully-fogged ground, and zero over never-explored ground, which
	/// is why a nuclear flash used to stop dead at the edge of vision.</para>
	/// <para>Drawn from the above-fog slot (after the fog layers, before the opaque unexplored layer),
	/// this adds back contribution * (1 - transmission) per cell, so a fully-fogged cell ends up as
	/// bright as it would have been with no fog over it, and a fully-visible cell is left alone
	/// because nothing was taken from it. Never-explored cells are skipped entirely.</para>
	/// <para>The glow is drawn as one quad per cell, and that is forced: the fog factor it multiplies
	/// by is per-cell data and nothing finer exists. But the LIGHT inside a quad is a smooth function
	/// of position, so it is sampled at the quad's four CORNERS and interpolated across it rather than
	/// filled flat from the centre. Filling flat is what made a nuclear flash resolve into a visible
	/// grid of tiles: adjacent cell centres of the shipped fireball differ by 63 8-bit levels halfway
	/// out and never by fewer than 13 anywhere in the falloff, against an 8-bit quantum of 1. Corners
	/// are shared with the neighbouring cell by construction -- both compute the same WPos from their
	/// own centre -- so the interpolation is continuous across every cell boundary and there is no edge
	/// left to see. This is the same thing TerrainSpriteLayer.UpdateTint already does for the below-fog
	/// half of the light (TerrainSpriteLayer.cs:124-134); only this path was still flat.</para>
	/// <para>NO VISION IS LEAKED, and the reason is not that this draw is careful -- it is that there
	/// is nothing here to leak. The colour of every quad below is a function of the light (position,
	/// radius, intensity, tint) and of the render player's OWN fog level, and of nothing else: no
	/// actor, no ownership and no terrain is consulted. An actor the render player may not see
	/// contributes no renderables at all -- Detectable.ModifyRender and FrozenUnderFog.ModifyRender
	/// both return SpriteRenderable.None -- so it is absent from the framebuffer rather than painted
	/// over, and adding light on top of where it is not cannot bring it back.</para>
	/// <para>The sweep runs over the whole CELL GRID, not the playable Bounds, because that is the
	/// region the fog it undoes is drawn over: terrain and both shroud layers all pass
	/// restrictToBounds false. The one-cell unplayable ring between Bounds and MapSize therefore
	/// carries real terrain, a real light tint and a real fog quad, and the quad it carries is the one
	/// <see cref="ShroudRenderer.ClampToPlayable(Map, PPos)"/> chose for it -- so the mask is read
	/// through that same clamp. Reading GetVisibility raw out there returns 0 (every visibility source
	/// is gated on Bounds) and would leave the ring at bare transmission: a one-cell band roughly seven
	/// times darker than the cell beside it, which is what the user reported. The clamp cannot widen
	/// the guarantee above, because it can only ever return a cell INSIDE Bounds: an unexplored
	/// neighbour still hands back 0.</para>
	/// </summary>
	public class FogPiercingLightRenderable : IRenderable, IFinalizedRenderable
	{
		/// <summary>Below this the quad is invisible at 8 bits per channel and not worth submitting.</summary>
		const float MinimumChannel = 0.5f / 255f;

		readonly WPos center;
		readonly WDist range;
		readonly float intensity;
		readonly float3 tint;
		readonly LightFalloff falloff;
		readonly float fogDarkness;
		readonly int zOffset;

		public FogPiercingLightRenderable(WPos center, WDist range, float intensity, in float3 tint,
			LightFalloff falloff, float fogDarkness, int zOffset = 0)
		{
			this.center = center;
			this.range = range;
			this.intensity = intensity;
			this.tint = tint;
			this.falloff = falloff;
			this.fogDarkness = fogDarkness;
			this.zOffset = zOffset;
		}

		public WPos Pos => center;
		public int ZOffset => zOffset;
		public bool IsDecoration => true;

		public IRenderable WithZOffset(int newOffset)
		{
			return new FogPiercingLightRenderable(center, range, intensity, tint, falloff, fogDarkness, newOffset);
		}

		public IRenderable OffsetBy(in WVec vec)
		{
			return new FogPiercingLightRenderable(center + vec, range, intensity, tint, falloff, fogDarkness, zOffset);
		}

		public IRenderable AsDecoration() { return this; }

		public IFinalizedRenderable PrepareRender(WorldRenderer wr) { return this; }

		public void Render(WorldRenderer wr)
		{
			var world = wr.World;
			var renderPlayer = world.RenderPlayer;

			// No render player, or fog switched off, means nothing was subtracted -- so there is
			// nothing to add back, and drawing here would double the light instead of restoring it.
			var mapLayers = renderPlayer?.MapLayers;
			if (mapLayers == null || !mapLayers.FogEnabled)
				return;

			var rangeLength = range.Length;
			if (rangeLength <= 0 || intensity == 0f)
				return;

			var map = world.Map;
			var cr = Game.Renderer.WorldRgbaColorRenderer;

			// Bound the sweep by the viewport as well as by the light. A 124-cell strategic fireball
			// covers ~61k cells, almost none of them on screen; the visible region is a couple of
			// thousand at most, and is exactly the region the fog itself is drawn over.
			//
			// AllVisibleCells, not VisibleCellsInsideBounds: the region the fog is drawn over is the
			// whole cell grid, not the playable Bounds. TerrainRenderer (TerrainRenderer.cs:88) and
			// both ShroudRenderer layers (ShroudRenderer.cs:164,170) all pass restrictToBounds false,
			// so terrain, shroud and fog are every one of them drawn across the one-cell unplayable
			// ring that sits between Bounds and MapSize. Clipping the restoration to Bounds while the
			// fog it compensates ran a cell wider is what drew a one-cell band down the map edge.
			// The grid itself still bounds the sweep, via the map.Height.Contains guard below.
			var visible = wr.Viewport.AllVisibleCells;
			var reach = rangeLength + 1024;
			var lightTL = map.CellContaining(center - new WVec(reach, reach, 0)).ToMPos(map);
			var lightBR = map.CellContaining(center + new WVec(reach, reach, 0)).ToMPos(map);

			var minU = Math.Max(visible.TopLeft.U, lightTL.U);
			var maxU = Math.Min(visible.BottomRight.U, lightBR.U);
			var minV = Math.Max(visible.TopLeft.V, lightTL.V);
			var maxV = Math.Min(visible.BottomRight.V, lightBR.V);

			// One entry per visibility level, so the composite curve is walked ten times per light
			// rather than once per cell.
			var restore = new float[MapLayers.VisionLayers];
			for (var v = 0; v < MapLayers.VisionLayers; v++)
				restore[v] = v == 0 ? 0f : 1f - ShroudRenderer.CompositeTransmission(v, fogDarkness);

			for (var v = minV; v <= maxV; v++)
			{
				for (var u = minU; u <= maxU; u++)
				{
					var uv = new MPos(u, v);
					if (!map.Height.Contains(uv))
						continue;

					// Unexplored (0) is skipped by the zero in `restore`: that layer is opaque black
					// and erases rather than darkens, and a fireball floating on never-scouted black
					// is what the alternative looks like. Full visibility restores 1 - 1 = 0, because
					// TerrainLighting already put the whole light on screen there.
					//
					// The visibility is read through ShroudRenderer's own ClampToPlayable, and it has
					// to be: GetVisibility answers for the SIMULATION, where every source is gated on
					// Map.Contains (= Bounds), so it returns 0 for every ring cell and widening the
					// sweep alone would restore exactly nothing out there. The fog quad actually
					// PAINTED on a ring cell is the one its nearest playable cell earned
					// (ShroudRenderer.cs:193-206), so that is the quad this has to undo. Asking the
					// same question of the same authority is what makes the two agree.
					//
					// The no-leak guarantee survives this unchanged, and gets no weaker: a ring cell
					// borrows its mask from ONE specific playable cell, and if that cell is
					// unexplored the clamp returns its 0 and the ring stays black -- which is also
					// exactly what the shroud drew there. Light never lands on a ring cell whose
					// governing playable cell has not been scouted.
					var lost = restore[mapLayers.GetVisibility(ShroudRenderer.ClampToPlayable(map, (PPos)uv))];
					if (lost <= 0f)
						continue;

					var cellCenter = map.CenterOfCell(uv.ToCPos(map));

					// The four corner POSITIONS are used twice each: once as the point the light is sampled
					// at, and once as the vertex the quad is drawn to. That they are the same point is what
					// makes the interpolation exact rather than approximate, and the neighbouring cell derives
					// the corners it shares from its own centre to the very same WPos -- so the two quads agree
					// on the colour all along the edge they have in common, and the edge stops being visible.
					var tlPos = cellCenter + new WVec(-512, -512, 0);
					var trPos = cellCenter + new WVec(512, -512, 0);
					var brPos = cellCenter + new WVec(512, 512, 0);
					var blPos = cellCenter + new WVec(-512, 512, 0);

					// The fog factor stays per-CELL and multiplies all four corners equally. It is NOT
					// interpolated and must not be: `lost` is 0 over never-explored ground, and smearing a
					// neighbour's non-zero value into that corner would put light on ground nobody has scouted.
					// Smooth light, hard mask -- the mask is the half carrying the no-leak guarantee, so it is
					// the half that stays quantised.
					var cornerTL = lost * ContributionAt(tlPos);
					var cornerTR = lost * ContributionAt(trPos);
					var cornerBR = lost * ContributionAt(brPos);
					var cornerBL = lost * ContributionAt(blPos);

					// All four, not the centre: a cell straddling the light's outer edge has dark corners and a
					// bright one, and testing the centre alone dropped it -- cutting the glow off with exactly
					// the kind of hard edge this change exists to remove.
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
		/// The light's own contribution at a position, in the same units and by the same arithmetic as
		/// the Additive branch of TerrainLighting's Tint: the distance is the full 3D one and the
		/// falloff curve is the shared one, so the glow drawn here is the same shape as the tint it is
		/// compensating for rather than an approximation of it.
		/// </summary>
		float3 ContributionAt(WPos pos)
		{
			return ContributionAt(center, range, intensity, tint, falloff, pos);
		}

		/// <summary>
		/// The static body of <see cref="ContributionAt(WPos)"/>, exposed so the corner-continuity property
		/// the smooth glow rests on can be asserted directly against the shipped fireball's parameters,
		/// rather than inferred from a screenshot. See FogPiercingLightTest.
		/// </summary>
		public static float3 ContributionAt(in WPos center, WDist range, float intensity, in float3 tint,
			LightFalloff falloff, in WPos pos)
		{
			var distance = (center - pos).Length;
			if (distance > range.Length)
				return float3.Zero;

			var f = TerrainLighting.ApplyFalloff(falloff, (range.Length - distance) * 1f / range.Length);
			return f * intensity * tint;
		}

		/// <summary>True when a contribution is too dark to show at 8 bits per channel.</summary>
		static bool BelowMinimum(in float3 c)
		{
			return c.X < MinimumChannel && c.Y < MinimumChannel && c.Z < MinimumChannel;
		}

		/// <summary>
		/// Packs a contribution into a premultiplied additive colour. Alpha is 255 and carries no
		/// transparency: BlendMode.Additive is GL_ONE / GL_ONE and RgbaColorRenderer premultiplies,
		/// so alpha 255 is what makes the RGB reach the framebuffer at face value.
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
