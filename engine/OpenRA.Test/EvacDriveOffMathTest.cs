#region Copyright & License Information
/*
 * WW3MOD EvacDriveOffMath tests — PIPELINE item 38, the ground evacuation drive-off leg.
 *
 * Pure-logic pins for the number that decides whether an evacuating vehicle reads as "drove off the battlefield"
 * or as "glitched out of existence": how long the off-map leg takes.
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
	}
}
