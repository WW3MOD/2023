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
	/// <para>THE BEARING STAYS PLAYER-SITUATED, and since 2026-09-08 it is ANCHORED to the player
	/// rather than merely derived from them: the base direction is the owner's own corridor, from
	/// outside their HomeLocation inward, and the salvo centroid leans it by at most
	/// <see cref="MaxCorridorDeviation"/>. Reading the bearing off home-to-centroid alone was
	/// player-situated for aim points in front of the launcher and silently inverted for aim points
	/// behind it -- see <see cref="Bearing"/>. The property the old map-edge rule protected survives
	/// either way: the player chooses WHERE the warheads land and never WHICH WAY they come in.
	/// There is deliberately no faction constant and no per-map bearing here -- a Russian player
	/// striking west is approached from the east only because that is where they are sitting.</para>
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

		/// <summary>
		/// Widest angle the salvo's approach may sit from the launching player's own back-line
		/// corridor (the direction from their HomeLocation toward the map centre). 170 raw WAngle
		/// units, of 1024 to the turn, is 59.8 degrees.
		/// </summary>
		/// <remarks>
		/// ANY VALUE UNDER 90 DEGREES MAKES THE INVARIANT HOLD, which is worth knowing before anyone
		/// tunes this. Writing S for the standoff, i for the home-to-centroid vector and o for the
		/// unit outward direction (home minus map centre), the spawn's outward offset from home is
		/// at worst (S - |i|)cos(d) for this limit d -- positive for every d below 90 degrees,
		/// because S is the map diagonal plus a margin and |i| cannot exceed the diagonal. So the
		/// choice between 60 degrees and, say, 80 is not a choice about correctness; it is a choice
		/// about how far round the side of its launcher a salvo may swing before the player stops
		/// reading it as coming in over their own back line.
		/// <para>60 is chosen so that it never engages in ordinary play: a corner home's most extreme
		/// in-map aim point sits at exactly 45 degrees off its corridor, and the shipped maps put
		/// every Supply Route near a corner or an edge. It is one line to retune, and the reading it
		/// governs is how side-on a strike behind one's own back line is allowed to look.</para>
		/// </remarks>
		public static readonly WAngle MaxCorridorDeviation = new(170);

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
		/// The direction the salvo travels: down the launching player's own approach corridor,
		/// leaned toward what they aimed at by at most <see cref="MaxCorridorDeviation"/>.
		/// </summary>
		/// <remarks>
		/// <para>THE CORRIDOR IS THE BASE, NOT THE AIM POINT, and that inversion is the 2026-09-08
		/// fix. This used to return <c>(centroid - home).Yaw</c> outright. Because
		/// <see cref="SpawnPosition"/> walks BACKWARD up the bearing by a standoff longer than the
		/// map diagonal, the spawn always lands on the opposite side of <paramref name="home"/> from
		/// the aim point -- which is "behind my own Supply Route" only while the aim point is inward
		/// of it. Aim at a cell on the OUTWARD side of your own SR, which is a routine thing to do
		/// when the fighting is on your doorstep, and home-to-centroid points off the map; the
		/// backward walk then puts the spawn deep on the far side of the world and the missile
		/// crosses the entire board to hit a target ten cells from its own launcher. That is the bug
		/// the player reported as "the missile comes in from the other side".</para>
		///
		/// <para>The old <c>DEGENERATE</c> branch already knew the right answer -- <c>mapCenter -
		/// home</c>, "from outside the player's own side, inward" -- but reached for it only on EXACT
		/// coincidence of aim point and home cell, which is the one case in the whole failing family
		/// a player will essentially never hit. It is the base case now, and the aim point perturbs
		/// it rather than defining it. A player's approach corridor is a property of where they sit.
		/// They still choose WHERE the warheads land and still never choose WHICH WAY they come in;
		/// that was always the intent, and only now is it true everywhere on the map.</para>
		///
		/// <para>WHY A LEAN RATHER THAN THE CORRIDOR ALONE. Under <see cref="MaxCorridorDeviation"/>
		/// this returns the aim bearing UNCHANGED, so every ordinary strike -- anything out in front
		/// of the launcher, which on the shipped maps is every aim point a corner home can reach --
		/// flies exactly the geometry it flew before. The lean exists so a strike on the enemy's left
		/// comes in over the player's own left shoulder rather than dead down the middle, and it is
		/// inert until the bearing would otherwise swing round past the launcher.</para>
		///
		/// <para>AND WHY IT TAPERS BACK TO ZERO INSTEAD OF SATURATING, which is the part that is easy
		/// to get backwards -- an earlier draft of this fix got it backwards. It LOOKS as though a
		/// target outward of the launcher cannot be reached from behind the launcher, on the grounds
		/// that "behind the launcher" is then past the target. That is false, and the standoff is why:
		/// the spawn has to sit behind the TARGET by a whole map diagonal plus a margin, so it clears
		/// the launcher as well with room to spare. Flying straight down the corridor at a target six
		/// cells behind one's own Supply Route puts the birth point ninety cells further out on the
		/// same line -- dead astern, the best possible answer -- where holding the lean at
		/// <see cref="MaxCorridorDeviation"/> would have brought it in over the launcher's shoulder at
		/// nearly sixty degrees, which is what the player would read as "from the side".</para>
		///
		/// <para>So the lean rises with the aim bearing while the target is in front, is capped at
		/// <see cref="MaxCorridorDeviation"/>, and then falls linearly back to zero as the aim bearing
		/// swings round to directly astern. Both endpoints are the ones the player would draw by hand:
		/// a target in front is approached from behind and to its own side, and a target directly
		/// behind is approached from directly behind.</para>
		/// </remarks>
		/// <param name="home">Centre of the owner's HomeLocation cell.</param>
		/// <param name="centroid">The salvo centroid from <see cref="Centroid"/>.</param>
		/// <param name="mapCenter">Inward reference defining the corridor: the salvo comes in from
		/// outside the player's own side, toward here.</param>
		public static WAngle Bearing(WPos home, WPos centroid, WPos mapCenter)
		{
			var corridor = mapCenter - home;
			var intent = centroid - home;

			// A home ON the map centre has no inward direction to lean away from. Nothing shipped
			// puts a Supply Route there, and the aim bearing is as good as anything when it happens.
			if (corridor.HorizontalLengthSquared == 0)
				return intent.HorizontalLengthSquared != 0 ? intent.Yaw : WAngle.Zero;

			var corridorYaw = corridor.Yaw;

			// The whole salvo aimed at the launcher's own cell: no aim bearing exists, so fly the
			// corridor itself. This is what the old DEGENERATE branch did, unchanged.
			if (intent.HorizontalLengthSquared == 0)
				return corridorYaw;

			// SIGNED deviation, in raw WAngle units (1024 to the turn). The subtraction operator
			// normalises to 0..1023, so the half above 512 is the same rotation taken the short way
			// round in the negative direction -- and reading it as negative is what holds the
			// approach on the side of the corridor the player actually aimed at rather than on its
			// mirror. Integer throughout; no float and no trigonometry.
			var deviation = (intent.Yaw - corridorYaw).Angle;
			if (deviation > 512)
				deviation -= 1024;

			var limit = MaxCorridorDeviation.Angle;
			var magnitude = Math.Abs(deviation);

			// In front of the launcher: fly what the player aimed, untouched.
			if (magnitude <= limit)
				return corridorYaw + new WAngle(deviation);

			// Round past it: taper the lean linearly from the limit back to zero as the aim bearing
			// swings on to directly astern (512 raw units is 180 degrees). Continuous at the limit --
			// substituting magnitude = limit gives the limit back -- so there is no step for a player
			// walking an aim point across the boundary, and exact at astern, where it gives the bare
			// corridor. Integer division truncates toward zero on every platform, which is a lean one
			// unit shy at worst and never one unit over.
			var tapered = limit * (512 - magnitude) / (512 - limit);
			return corridorYaw + new WAngle(deviation < 0 ? -tapered : tapered);
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
