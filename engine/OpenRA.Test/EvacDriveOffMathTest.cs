#region Copyright & License Information
/*
 * WW3MOD EvacDriveOffMath tests — PIPELINE item 38, the ground evacuation drive-off leg.
 *
 * Pure-logic pins for the two numbers that decide whether an evacuating vehicle reads as "drove off the
 * battlefield" or as "glitched out of existence": how long the off-map leg takes, and when the unit counts as
 * clear of the playable area.
 *
 * The load-bearing property pinned here is TERMINATION. This math sits inside an activity that replaced an
 * unconditional sell, so a duration of zero, a negative duration, or an unbounded one is not a cosmetic bug — it
 * is a unit that never sells and never dies, parked outside the map. Every degenerate input a ruleset can hand it
 * (zero speed, zero distance, an overflowing span) is pinned to a finite, positive tick count.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class EvacDriveOffMathTest
	{
		// ---------- DriveOffTicks ----------

		[Test]
		public void DriveOffTicks_TravelsAtTheUnitsOwnPace()
		{
			Assert.Multiple(() =>
			{
				// One cell (1024 world units) at infantry speed 25 ⇒ ~41 ticks, about 1.6s. The point of the whole
				// change: the leg takes real time rather than snapping.
				Assert.That(EvacDriveOffMath.DriveOffTicks(1024, 25), Is.EqualTo(41));

				// Same span at a tank's pace is proportionally quicker.
				Assert.That(EvacDriveOffMath.DriveOffTicks(1024, 128), Is.EqualTo(8));
			});
		}

		[Test]
		public void DriveOffTicks_RoundsUpSoAShortLegStillTakesATick()
		{
			Assert.Multiple(() =>
			{
				Assert.That(EvacDriveOffMath.DriveOffTicks(1, 25), Is.EqualTo(1),
					"a sub-tick span must not truncate to 0 — Drag would divide by zero on its own lerp");
				Assert.That(EvacDriveOffMath.DriveOffTicks(100, 25), Is.EqualTo(4), "ceiling, not floor (100/25 = 4)");
				Assert.That(EvacDriveOffMath.DriveOffTicks(101, 25), Is.EqualTo(5), "ceiling rounds the remainder up");
			});
		}

		[Test]
		public void DriveOffTicks_DegenerateInputsAreFiniteAndPositive()
		{
			Assert.Multiple(() =>
			{
				Assert.That(EvacDriveOffMath.DriveOffTicks(1024, 0), Is.EqualTo(1),
					"speed 0 must not divide by zero — an immobilised unit gets the floor, and the caller's deadline sells it");
				Assert.That(EvacDriveOffMath.DriveOffTicks(1024, -5), Is.EqualTo(1), "negative speed reads as immobile");
				Assert.That(EvacDriveOffMath.DriveOffTicks(0, 25), Is.EqualTo(1), "already at the destination");
				Assert.That(EvacDriveOffMath.DriveOffTicks(-1024, 25), Is.EqualTo(1), "negative span reads as arrived");
			});
		}

		[Test]
		public void DriveOffTicks_IsCapped()
		{
			Assert.Multiple(() =>
			{
				Assert.That(EvacDriveOffMath.DriveOffTicks(int.MaxValue, 1),
					Is.EqualTo(EvacDriveOffMath.MaxDriveOffTicks),
					"an enormous span at crawling speed is capped, not left to run for minutes");

				// The long-widening matters: distance + speed - 1 overflows int32 here, and an overflowed
				// intermediate would wrap negative and come back as the 1-tick floor — i.e. a teleport, silently.
				Assert.That(EvacDriveOffMath.DriveOffTicks(int.MaxValue, 2),
					Is.EqualTo(EvacDriveOffMath.MaxDriveOffTicks));
			});
		}

		// ---------- IsClearOfBounds ----------
		// Bounds used below are LTRB 10,10 .. 90,90 with left/top INCLUSIVE and right/bottom EXCLUSIVE, matching
		// Map.Bounds (Map.cs:1590). So the playable columns are 10..89.

		[Test]
		public void IsClearOfBounds_InsideIsNeverClear()
		{
			Assert.Multiple(() =>
			{
				Assert.That(EvacDriveOffMath.IsClearOfBounds(50, 50, 10, 10, 90, 90, 2), Is.False, "map centre");
				Assert.That(EvacDriveOffMath.IsClearOfBounds(10, 50, 10, 10, 90, 90, 2), Is.False,
					"first playable column — on the boundary is not past it");
				Assert.That(EvacDriveOffMath.IsClearOfBounds(89, 50, 10, 10, 90, 90, 2), Is.False,
					"last playable column (right is exclusive)");
			});
		}

		[Test]
		public void IsClearOfBounds_ClearsAtExactlyTheMargin()
		{
			Assert.Multiple(() =>
			{
				// Left edge: playable starts at 10, so u == 8 is two cells outside.
				Assert.That(EvacDriveOffMath.IsClearOfBounds(9, 50, 10, 10, 90, 90, 2), Is.False, "one cell out, margin 2");
				Assert.That(EvacDriveOffMath.IsClearOfBounds(8, 50, 10, 10, 90, 90, 2), Is.False, "at the margin, not past it");
				Assert.That(EvacDriveOffMath.IsClearOfBounds(7, 50, 10, 10, 90, 90, 2), Is.True, "past the margin");

				// Right edge: first non-playable column is 90, so 90 + 2 == 92 is the first that counts.
				Assert.That(EvacDriveOffMath.IsClearOfBounds(91, 50, 10, 10, 90, 90, 2), Is.False);
				Assert.That(EvacDriveOffMath.IsClearOfBounds(92, 50, 10, 10, 90, 90, 2), Is.True);
			});
		}

		[Test]
		public void IsClearOfBounds_AnySingleSideCounts()
		{
			// The property that makes the predicate usable at all: a unit leaving due west of a side edge clears on
			// U alone while V stays mid-map. Requiring both axes would mean only corner-bound units ever sold.
			Assert.Multiple(() =>
			{
				Assert.That(EvacDriveOffMath.IsClearOfBounds(7, 50, 10, 10, 90, 90, 2), Is.True, "west only");
				Assert.That(EvacDriveOffMath.IsClearOfBounds(50, 7, 10, 10, 90, 90, 2), Is.True, "north only");
				Assert.That(EvacDriveOffMath.IsClearOfBounds(92, 50, 10, 10, 90, 90, 2), Is.True, "east only");
				Assert.That(EvacDriveOffMath.IsClearOfBounds(50, 92, 10, 10, 90, 90, 2), Is.True, "south only");
			});
		}

		[Test]
		public void IsClearOfBounds_ZeroAndNegativeMargin()
		{
			Assert.Multiple(() =>
			{
				Assert.That(EvacDriveOffMath.IsClearOfBounds(9, 50, 10, 10, 90, 90, 0), Is.True,
					"margin 0 ⇒ one cell outside the playable area is already clear");
				Assert.That(EvacDriveOffMath.IsClearOfBounds(10, 50, 10, 10, 90, 90, 0), Is.False,
					"margin 0 ⇒ the boundary cell itself is still inside");
				Assert.That(EvacDriveOffMath.IsClearOfBounds(9, 50, 10, 10, 90, 90, -3), Is.True,
					"a negative margin clamps to 0 rather than reaching back INSIDE the map");
			});
		}
	}
}
