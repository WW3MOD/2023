#region Copyright & License Information
/*
 * WW3MOD MountedTransportBotModule pure-geometry test (@experimental transport-shuttle improvement).
 *
 * Pins the two decisions the fog-legal shuttle turns believed data into, without a game run:
 *   (1) PICKUP CORRIDOR — a passenger walking the SR→drop lane is in-corridor; one off to the side, or
 *       past the endpoints, or when the corridor is disabled, is not. This is the "catch the mid-walk
 *       infantry" widen from the 14-cell reserve bubble.
 *   (2) STANDOFF INDEX — given believed anti-ground danger sampled from the intended drop back toward our
 *       SR, choose the drop cell just OUTSIDE the believed envelope (+ margin): keep an already-safe
 *       target, back off to first-safe+margin through a hot envelope, and fall back to the furthest-back
 *       cell when nothing sampled is safe.
 * Plus a determinism guard. Pure integer math; no world mounted, zero RNG.
 */
#endregion

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class MountedTransportMathTest
	{
		// ---------- Pickup corridor ----------

		[TestCase(0, 5, TestName = "corridor: on-lane midpoint is in")]      // p directly on the lane
		public void OnLaneIsInCorridor(int _, int halfWidth)
		{
			var a = new CPos(0, 0);
			var b = new CPos(20, 0);
			var p = new CPos(10, 0);
			Assert.That(MountedTransportMath.InCorridor(a, b, p, halfWidth), Is.True);
		}

		[Test]
		public void WithinHalfWidthIsIn_BeyondIsOut()
		{
			var a = new CPos(0, 0);
			var b = new CPos(20, 0);

			// 3 cells off a horizontal lane, half-width 4 → in; half-width 2 → out.
			var p = new CPos(10, 3);
			Assert.That(MountedTransportMath.InCorridor(a, b, p, 4), Is.True, "3 off, width 4 should be in");
			Assert.That(MountedTransportMath.InCorridor(a, b, p, 2), Is.False, "3 off, width 2 should be out");
		}

		[Test]
		public void PastTheEndpointsIsOut()
		{
			var a = new CPos(0, 0);
			var b = new CPos(20, 0);

			// On the lane's infinite line but beyond b, and behind a — both outside the SPAN.
			Assert.That(MountedTransportMath.InCorridor(a, b, new CPos(25, 0), 4), Is.False, "beyond b");
			Assert.That(MountedTransportMath.InCorridor(a, b, new CPos(-5, 0), 4), Is.False, "behind a");
		}

		[Test]
		public void DisabledOrDegenerateIsAlwaysOut()
		{
			var a = new CPos(0, 0);
			var b = new CPos(20, 0);
			var p = new CPos(10, 0);
			Assert.That(MountedTransportMath.InCorridor(a, b, p, 0), Is.False, "width 0 = off");
			Assert.That(MountedTransportMath.InCorridor(a, b, p, -1), Is.False, "negative width = off");

			// Degenerate zero-length lane: never in-corridor (no direction to project onto).
			Assert.That(MountedTransportMath.InCorridor(a, a, a, 4), Is.False, "zero-length lane");
		}

		[Test]
		public void CorridorWorksOnADiagonalLane()
		{
			var a = new CPos(0, 0);
			var b = new CPos(20, 20);

			// A cell just off the 45° lane: perpendicular distance ≈ sqrt(2) ≈ 1.41 cells.
			var p = new CPos(11, 9);
			Assert.That(MountedTransportMath.InCorridor(a, b, p, 2), Is.True, "≈1.4 off, width 2 in");
			Assert.That(MountedTransportMath.InCorridor(a, b, p, 1), Is.False, "≈1.4 off, width 1 out");
		}

		// ---------- Standoff index ----------

		[Test]
		public void AlreadySafeTargetIsKept()
		{
			// dangers[0] (the intended drop) is at/below threshold → no back-off, keep index 0.
			var dangers = new List<int> { 0, 0, 0 };
			Assert.That(MountedTransportMath.ChooseStandoffIndex(dangers, 0, 2), Is.EqualTo(0));
		}

		[Test]
		public void BacksOffToFirstSafePlusMargin()
		{
			// Hot at the drop and one cell back, clears at index 2. threshold 0, margin 2 → 2+2 = 4.
			var dangers = new List<int> { 90, 40, 0, 0, 0, 0 };
			Assert.That(MountedTransportMath.ChooseStandoffIndex(dangers, 0, 2), Is.EqualTo(4));
		}

		[Test]
		public void MarginClampsToSampledRange()
		{
			// First safe at index 3, margin 5 would overshoot; clamp to last index (5).
			var dangers = new List<int> { 90, 90, 90, 0, 0, 0 };
			Assert.That(MountedTransportMath.ChooseStandoffIndex(dangers, 0, 5), Is.EqualTo(5));
		}

		[Test]
		public void ThresholdIsInclusiveAndTunable()
		{
			// With threshold 40, the index-1 cell (danger 40) already counts as safe → first-safe 1 + margin 0.
			var dangers = new List<int> { 90, 40, 10 };
			Assert.That(MountedTransportMath.ChooseStandoffIndex(dangers, 40, 0), Is.EqualTo(1));
		}

		[Test]
		public void NoSafeCellFallsBackToFurthestBack()
		{
			// Everything sampled is hot → use the furthest-back cell (closest to our SR), never the hot drop.
			var dangers = new List<int> { 200, 180, 160, 150 };
			Assert.That(MountedTransportMath.ChooseStandoffIndex(dangers, 0, 2), Is.EqualTo(3));
		}

		[Test]
		public void EmptyOrNullIsIndexZero()
		{
			Assert.That(MountedTransportMath.ChooseStandoffIndex(new List<int>(), 0, 2), Is.EqualTo(0));
			Assert.That(MountedTransportMath.ChooseStandoffIndex(null, 0, 2), Is.EqualTo(0));
		}

		[Test]
		public void IsDeterministic()
		{
			var dangers = new List<int> { 90, 40, 0, 0, 0 };
			var a = MountedTransportMath.ChooseStandoffIndex(dangers, 0, 2);
			var b = MountedTransportMath.ChooseStandoffIndex(dangers, 0, 2);
			Assert.That(a, Is.EqualTo(b));

			var c = MountedTransportMath.InCorridor(new CPos(0, 0), new CPos(20, 0), new CPos(10, 3), 4);
			var d = MountedTransportMath.InCorridor(new CPos(0, 0), new CPos(20, 0), new CPos(10, 3), 4);
			Assert.That(c, Is.EqualTo(d));
		}
	}
}
