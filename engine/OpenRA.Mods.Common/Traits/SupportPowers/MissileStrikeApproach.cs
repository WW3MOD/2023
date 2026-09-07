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

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>
	/// The approach geometry of ONE missile-strike salvo: a single azimuth that every warhead in
	/// the salvo flies down, and the distance each one is born back along it.
	/// </summary>
	/// <remarks>
	/// <para>ONE BEARING PER SALVO, DERIVED FROM THE CENTROID. Until 2026-09-07 each warhead of a
	/// multi-aim-point strike took its own bearing, from a single shared spawn cell on the owner's
	/// map edge to its own aim point -- so a six-warhead MIRV was born at one point and FANNED
	/// outward to six. Reentry vehicles from one launch cross a battlefield-scale map on tracks
	/// parallel to within a rounding error. Computing the bearing per warhead rebuilds the fan with
	/// extra steps, which is why <see cref="Centroid"/> exists and why it is called once.</para>
	///
	/// <para>THE BEARING STAYS PLAYER-SITUATED. It runs from the owner's own HomeLocation toward the
	/// salvo centroid, so the property the old map-edge rule protected survives: the player chooses
	/// WHERE the warheads land and never WHICH WAY they come in. There is deliberately no faction
	/// constant and no per-map bearing here -- a Russian player striking west is approached from the
	/// east only because that is where they are sitting.</para>
	///
	/// <para>DETERMINISM. Every step is integer: a long-accumulated centroid with one integer
	/// division, <see cref="WVec.Yaw"/> (an ArcTan table lookup), integer ISqrt for the map
	/// diagonal, and <see cref="WVec.Rotate"/>'s Int32Matrix4x4. No float, no RNG, no reads of
	/// anything client-local. This runs on the synced order-resolution path and identical inputs
	/// produce byte-identical positions on every client.</para>
	///
	/// <para>BOUNDED, and the bound is not decoration. <c>CPos</c> packs each axis into 12 signed
	/// bits and WRAPS outside -2048..2047 in its constructor, so a spawn far enough off-map decodes
	/// as a cell on the far side of the world -- and the missile really does convert
	/// (<c>BallisticMissile.TopLeft</c> is <c>Map.CellContaining(CenterPosition)</c>).
	/// <see cref="MaxStandoff"/> caps the walk-back at 1024 cells; on the largest shipped map
	/// (x-lake, 130x130) the standoff is ~200 cells and the worst spawn cell coordinate is about
	/// -200, so the cap is a guard rather than a live constraint. It also keeps every WVec component
	/// under 1,048,576, half of the 2,097,151 ceiling above which <see cref="WVec.Yaw"/> overflows
	/// int and silently returns a plausible wrong angle (DOCS/reference/conventions.md).</para>
	/// </remarks>
	public readonly struct MissileStrikeApproach
	{
		/// <summary>Hard ceiling on <see cref="Standoff"/>, in world units. See the CPos note above.</summary>
		public const int MaxStandoff = 1024 * 1024;

		/// <summary>
		/// Shortest standoff worth flying. BallisticMissileFly divides by the horizontal distance and
		/// completes on its first tick at zero, which would teleport the warhead onto its aim point
		/// and detonate it there (the same trap DoomsdayStrikeInfo.ApproachDistance documents).
		/// </summary>
		public const int MinStandoff = 1024;

		/// <summary>The shared azimuth: the direction of travel every warhead in the salvo flies.</summary>
		public readonly WAngle Facing;

		/// <summary>Horizontal distance back up <see cref="Facing"/> at which each warhead is born.</summary>
		public readonly int Standoff;

		public MissileStrikeApproach(WAngle facing, int standoff)
		{
			Facing = facing;
			Standoff = Math.Clamp(standoff, MinStandoff, MaxStandoff);
		}

		/// <summary>
		/// The whole composition, in one World-free call: centroid, then bearing, then standoff.
		/// </summary>
		/// <remarks>
		/// This exists so the geometry a salvo actually flies can be asserted without standing up a
		/// World -- the trait method that calls it does nothing but look up HomeLocation, the map
		/// centre and the map size. Splitting it any further would leave the ORDER of these three
		/// steps (centroid FIRST, bearing SECOND) untested, and that order is the entire fix.
		/// </remarks>
		public static MissileStrikeApproach For(WPos home, WPos mapCenter, int mapCellsX, int mapCellsY,
			int margin, WPos[] aimPoints)
		{
			return new MissileStrikeApproach(
				Bearing(home, Centroid(aimPoints), mapCenter),
				StandoffFor(mapCellsX, mapCellsY, margin));
		}

		/// <summary>
		/// The salvo's centre of aim. THE WHOLE POINT of the shared-bearing fix: take the bearing
		/// from here once, not from each aim point in turn.
		/// </summary>
		public static WPos Centroid(WPos[] aimPoints)
		{
			if (aimPoints.Length == 1)
				return aimPoints[0];

			// Accumulated in long so a salvo spread across a large map cannot overflow, then ONE
			// integer division. C# integer division truncates toward zero on every platform, so this
			// is exact and identical everywhere rather than merely close.
			long x = 0, y = 0, z = 0;
			foreach (var p in aimPoints)
			{
				x += p.X;
				y += p.Y;
				z += p.Z;
			}

			var n = aimPoints.Length;
			return new WPos((int)(x / n), (int)(y / n), (int)(z / n));
		}

		/// <summary>
		/// The direction the salvo travels: from where the launching player sits, toward what they
		/// aimed at.
		/// </summary>
		/// <param name="home">Centre of the owner's HomeLocation cell.</param>
		/// <param name="centroid">The salvo centroid from <see cref="Centroid"/>.</param>
		/// <param name="mapCenter">Fallback reference, used only in the degenerate case below.</param>
		public static WAngle Bearing(WPos home, WPos centroid, WPos mapCenter)
		{
			var delta = centroid - home;
			if (delta.HorizontalLengthSquared != 0)
				return delta.Yaw;

			// DEGENERATE: the player aimed the whole salvo at their own home cell. There is no
			// bearing to read, so fall back to the one that still means "in over my own back line" --
			// from outside the player's own side, inward. WAngle.Zero is the last resort for a home
			// that is also the map centre, and it is a direction rather than a failure.
			var inward = mapCenter - home;
			return inward.HorizontalLengthSquared != 0 ? inward.Yaw : WAngle.Zero;
		}

		/// <summary>
		/// How far back up the approach a warhead is born, so that the spawn is off-map from EVERY
		/// aim point rather than off-map from the ones that happen to be far from the boundary.
		/// </summary>
		/// <remarks>
		/// The map diagonal is the largest distance between any two points inside the map, so a
		/// walk-back of diagonal + margin lands at least <paramref name="margin"/> outside it,
		/// whatever the aim point and whatever the bearing. That is a proof rather than a tuned
		/// number, which matters because a standoff that is merely usually big enough puts the
		/// missile's birth in plain view on the maps where it is not.
		/// </remarks>
		public static int StandoffFor(int mapCellsX, int mapCellsY, int margin)
		{
			// HorizontalLengthSquared is long and ISqrt is integer, so this neither overflows on a
			// large map nor introduces a float.
			var diagonal = new WVec(1024 * mapCellsX, 1024 * mapCellsY, 0).HorizontalLength;
			return Math.Clamp(diagonal + margin, MinStandoff, MaxStandoff);
		}

		/// <summary>
		/// Where the warhead aimed at <paramref name="aimPoint"/> is born: one standoff BACK up the
		/// shared approach.
		/// </summary>
		/// <remarks>
		/// <c>WVec(0, -d, 0)</c> rotated by a yaw is d units ALONG that yaw -- the construction
		/// <see cref="WVec.FromSpeedAndAngle"/> and <c>BallisticMissile.GetVector</c> both use to
		/// turn a facing into a velocity. SUBTRACTING it therefore walks backward up the approach.
		/// Writing it this way rather than with Cos/Sin is what keeps the counterclockwise WAngle
		/// convention from being got wrong by hand a third time.
		///
		/// Z is zero because that is what <c>Map.CenterOfCell</c> returns on the Rectangular grid
		/// this mod uses, which is exactly what the old map-edge spawn fed in; the caller adds
		/// SpawnAltitude on top, unchanged.
		/// </remarks>
		public WPos SpawnPosition(WPos aimPoint)
		{
			var alongApproach = new WVec(0, -Standoff, 0).Rotate(WRot.FromYaw(Facing));
			return new WPos(aimPoint.X - alongApproach.X, aimPoint.Y - alongApproach.Y, 0);
		}
	}
}
