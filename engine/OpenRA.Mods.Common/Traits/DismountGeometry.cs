#region Copyright & License Information
/*
 * WW3MOD dismount geometry — where a man goes when he gets out of a vehicle (pure math).
 *
 * USER REQUEST (2026-09-01): "Can we make exiting a vehicle happen from the rear of a vehicle, so it actually
 * looks like a dismounting from a real vehicle? [...] I would like them to exit and spread out as fast as
 * possible, some going left, some going right, some going forward (from the direction they are exiting, which
 * is behind the vehicle (at least by default))."
 *
 * Two separate jobs, both keyed off the hull's facing:
 *   - RANKING adjacent cells so the ones behind the hull are picked first (ordered unload, emergency bail).
 *   - FANNING a walk-away bearing per man so the stick splits back / left / right instead of following one
 *     another to the same cell (crew ejection, emergency bail).
 *
 * WHY THIS IS A SEPARATE CLASS. The three dismount paths (VehicleCrew.PlaceEjectedCrew,
 * Cargo.EmergencyBailOut, UnloadCargo.ChooseExitSubCell) already carried two byte-identical copies of an
 * eight-compass offset table that had drifted into different orders in different files. More to the point,
 * this is WAngle arithmetic, and WAngle is COUNTERCLOCKWISE in OpenRA (0 = North, 256 = WEST, 512 = South,
 * 768 = EAST — see DOCS/reference/conventions.md). A sign error here does not crash and does not look wrong
 * in a diff: it silently puts the squad out through the FRONT of the vehicle, which is the exact failure the
 * user asked to have fixed. Isolating it lets NUnit pin it without launching the game.
 *
 * CROSS-CHECKED AGAINST THE ENGINE, NOT AGAINST THIS COMMENT. CellStep resolves a bearing by integer sector
 * arithmetic rather than trigonometry, because WVec.FromSpeedAndAngle rounds through a 1024-scaled cosine
 * table and a component that ought to be exactly zero on a cardinal heading is not guaranteed to be — one
 * stray unit there turns "straight back" into a diagonal. DismountGeometryTest pins the sector table against
 * WVec.FromSpeedAndAngle at all eight headings, so if the counterclockwise reasoning above is backwards the
 * ENGINE fails the test rather than this file agreeing with itself.
 *
 * DETERMINISM (influence-stack invariant): zero RNG, pure integer arithmetic, no collection iteration. Callers
 * supply their own SharedRandom rolls (walk distance, tie-breaking shuffle) outside this class.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>Facing-relative placement math shared by every path that puts a man on the ground next to the
	/// vehicle he just left. Pure: no world, no actor, no RNG.</summary>
	public static class DismountGeometry
	{
		/// <summary>Half a turn in WAngle units. Added to a hull facing to get the bearing out of its back.</summary>
		public const int HalfTurn = 512;

		/// <summary>One eighth of a turn in WAngle units — the width of one compass sector.</summary>
		public const int Octant = 128;

		/// <summary>Unit cell steps for the eight compass sectors, indexed COUNTERCLOCKWISE from North to match
		/// WAngle. Sector 1 is therefore North-WEST, not North-east; that inversion relative to a clockwise
		/// table is the whole hazard this class exists to contain. OpenRA screen space is north = -Y, east = +X.</summary>
		static readonly CVec[] SectorSteps =
		{
			new CVec(0, -1),   // 0   North
			new CVec(-1, -1),  // 128 North-west
			new CVec(-1, 0),   // 256 West
			new CVec(-1, 1),   // 384 South-west
			new CVec(0, 1),    // 512 South
			new CVec(1, 1),    // 640 South-east
			new CVec(1, 0),    // 768 East
			new CVec(1, -1),   // 896 North-east
		};

		/// <summary>Angular offsets from the exit bearing, applied per dismounting man in order.
		/// <para>Straight back first, then the two flanks, then the two back-diagonals. For a three-man tank crew
		/// that is literally the user's "some going left, some going right, some going forward"; for a longer
		/// stick it cycles, and the caller's random walk distance keeps the repeats from overlapping.</para>
		/// <para>Bounded to ±90° on purpose. A wider fan would send the tail of a full transport out PAST the
		/// hull's shoulders and around its nose, which is the look the rear-exit change exists to remove.</para></summary>
		static readonly int[] FanOffsets = { 0, 256, -256, 128, -128 };

		/// <summary>Number of distinct fan bearings before the pattern repeats.</summary>
		public static int FanCount => FanOffsets.Length;

		/// <summary>Bearing pointing out of the back of a hull with the given facing.</summary>
		public static WAngle RearBearing(WAngle hullFacing)
		{
			return hullFacing + new WAngle(HalfTurn);
		}

		/// <summary>The <paramref name="index"/>'th fan bearing around <paramref name="exitBearing"/>.
		/// Negative indices are accepted and wrap the same way positive ones do.</summary>
		public static WAngle FanBearing(WAngle exitBearing, int index)
		{
			var i = index % FanOffsets.Length;
			if (i < 0)
				i += FanOffsets.Length;

			return exitBearing + FanOffsets[i];
		}

		/// <summary>Unit cell step (one of the eight compass neighbours) for a bearing.
		/// <para>Snapped by sector rather than by trigonometry: the +Octant/2 bias centres each sector on its
		/// compass point, so a bearing of exactly South yields (0, +1) and cannot round into a diagonal.</para></summary>
		public static CVec CellStep(WAngle bearing)
		{
			// WAngle's constructor has already normalised to [0, 1023], so this cannot go negative.
			var sector = ((bearing.Angle + Octant / 2) % 1024) / Octant;
			return SectorSteps[sector];
		}

		/// <summary>Cell step the <paramref name="index"/>'th man out of a hull should walk along: behind the
		/// hull, fanned. This is the one call the dismount paths make.</summary>
		public static CVec FanStep(WAngle hullFacing, int index)
		{
			return CellStep(FanBearing(RearBearing(hullFacing), index));
		}

		/// <summary>Sort key ranking an adjacent-cell offset by how far behind the hull it sits: 0 for dead
		/// astern, 512 for dead ahead. Feed to a STABLE OrderBy so a caller's prior shuffle still breaks ties
		/// among equally-rearward cells.
		/// <para>A ranking, deliberately not a filter. Restricting a dismount to the three rear cells would
		/// make a full transport queue up waiting for them; ordering costs nothing when the rear is clear and
		/// degrades to the old any-free-cell behaviour when it is not.</para></summary>
		public static int RearPreference(WAngle hullFacing, CVec cellOffset)
		{
			if (cellOffset == CVec.Zero)
				return HalfTurn;

			// Cell space is scaled to world space (CVec's own explicit WVec cast) so Yaw's ArcTan sees the same
			// north = -Y convention the rest of the engine uses; going through Yaw rather than a hand-rolled
			// atan2 is what conventions.md prescribes for exactly this conversion.
			var bearing = ((WVec)cellOffset).Yaw;
			return WAngle.AngleDiff(bearing, RearBearing(hullFacing)).Angle;
		}

		/// <summary>Fallback for a hull with no IFacing at all: the plain eight-compass step the dismount paths
		/// used before rear-exit existed. Kept so a caller can degrade cleanly rather than branching on null
		/// with bespoke offsets of its own.</summary>
		public static CVec CompassStep(int sector)
		{
			var i = sector % SectorSteps.Length;
			if (i < 0)
				i += SectorSteps.Length;

			return SectorSteps[i];
		}

		/// <summary>Number of compass sectors — the exclusive upper bound a caller should roll against when it
		/// falls back to <see cref="CompassStep"/>.</summary>
		public static int CompassCount => SectorSteps.Length;

		/// <summary>Shortest angular separation between two bearings, in WAngle units [0, 512]. Exposed so the
		/// tests can assert the sector table against the engine's own trigonometry.</summary>
		public static int Separation(WAngle a, WAngle b)
		{
			return WAngle.AngleDiff(a, b).Angle;
		}

	}
}
