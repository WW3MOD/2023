#region Copyright & License Information
/*
 * WW3MOD EvacRefundTextMath tests — the "+$N" indicator shown when a unit is evacuated.
 *
 * Pure-logic pins for two numbers that are otherwise only checkable by watching the screen, which is exactly why
 * they were wrong: the POSITION the refund tick is drawn at, and how LONG it stays readable.
 *
 * The load-bearing property pinned here is that the clamp is the IDENTITY INSIDE THE BOUNDS. The defect it fixes
 * was invisible text at an out-of-bounds position, and the obvious over-correction — moving the spawn point — would
 * have relocated every refund tick in the game, including the ones that were working. A clamp that is identity for
 * in-bounds inputs cannot regress the working cases, and that is asserted rather than assumed.
 *
 * The drift-parity pin is the other half: lengthening a tick's life without slowing its rise makes it climb 2.5x
 * further and leave the viewport, so the two constants are only correct TOGETHER. A future edit to one of them
 * fails here rather than in a game nobody replayed.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Effects;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class EvacRefundTextMathTest
	{
		// A stand-in for Map.Bounds: right/bottom are EXCLUSIVE, so the last legal cell is (127, 95).
		const int Left = 0;
		const int Top = 0;
		const int Right = 128;
		const int Bottom = 96;

		// ---------- ClampToBounds ----------

		[Test]
		public void ClampToBounds_IsIdentityInsideTheBounds()
		{
			Assert.Multiple(() =>
			{
				// The whole safety argument for this change: a refund that was already visible does not move.
				Assert.That(EvacRefundTextMath.ClampToBounds(64, 48, Left, Top, Right, Bottom), Is.EqualTo((64, 48)),
					"a mid-map sell — the fallback path that always worked — must be untouched");
				Assert.That(EvacRefundTextMath.ClampToBounds(Left, Top, Left, Top, Right, Bottom), Is.EqualTo((Left, Top)));
				Assert.That(EvacRefundTextMath.ClampToBounds(Right - 1, Bottom - 1, Left, Top, Right, Bottom),
					Is.EqualTo((Right - 1, Bottom - 1)),
					"the last IN-bounds cell is Right-1/Bottom-1, not Right/Bottom");
			});
		}

		[Test]
		public void ClampToBounds_PullsAnOffMapPositionBackToTheNearestLegalCell()
		{
			Assert.Multiple(() =>
			{
				// A ground unit is dragged GroundOffMapCells (2) past the boundary before it sells, an aircraft
				// AircraftOffMapCells (5). Both land outside, where the shroud and fog layers report "hidden".
				Assert.That(EvacRefundTextMath.ClampToBounds(-2, 48, Left, Top, Right, Bottom), Is.EqualTo((0, 48)),
					"ground unit driven 2 cells off the left edge");
				Assert.That(EvacRefundTextMath.ClampToBounds(Right + 4, 48, Left, Top, Right, Bottom),
					Is.EqualTo((Right - 1, 48)), "aircraft 5 cells past the right edge");
				Assert.That(EvacRefundTextMath.ClampToBounds(64, -5, Left, Top, Right, Bottom), Is.EqualTo((64, 0)));
				Assert.That(EvacRefundTextMath.ClampToBounds(64, Bottom + 5, Left, Top, Right, Bottom),
					Is.EqualTo((64, Bottom - 1)));

				// A corner exit is out on both axes at once.
				Assert.That(EvacRefundTextMath.ClampToBounds(-3, -3, Left, Top, Right, Bottom), Is.EqualTo((0, 0)));
			});
		}

		[Test]
		public void ClampToBounds_HonoursANonZeroOrigin()
		{
			// Map.Bounds is inset from MapSize on any map with an authored border, so Left/Top are routinely
			// non-zero and clamping to 0 would still be out of bounds.
			Assert.That(EvacRefundTextMath.ClampToBounds(3, 200, 8, 8, 120, 120), Is.EqualTo((8, 119)));
		}

		[Test]
		public void ClampToBounds_DegenerateBoundsDoNotThrow()
		{
			Assert.Multiple(() =>
			{
				// Math.Clamp throws when min > max, and this runs mid-sale where a fault would strand the actor.
				Assert.That(EvacRefundTextMath.ClampToBounds(5, 5, 0, 0, 0, 0), Is.EqualTo((0, 0)),
					"zero-area bounds collapse to the origin rather than throwing");
				Assert.That(EvacRefundTextMath.ClampToBounds(5, 5, 10, 10, 4, 4), Is.EqualTo((10, 10)),
					"inverted bounds collapse to the origin rather than throwing");
			});
		}

		// ---------- Lifetime and rise ----------

		[Test]
		public void RefundTickIsLongerLivedThanTheSharedDefault()
		{
			Assert.Multiple(() =>
			{
				Assert.That(EvacRefundTextMath.DefaultTickLifetime, Is.EqualTo(30),
					"the value every other FloatingText caller passes; the comparison below is meaningless if it moved");
				Assert.That(EvacRefundTextMath.TickLifetime, Is.GreaterThan(EvacRefundTextMath.DefaultTickLifetime),
					"the user asked for longer, so this must never regress to the default");

				// 75 ticks x 60 ms = 4500 ms. Derived from ticks rather than from an assumed tick rate: the
				// default speed is Timestep 60 ms (mod.yaml:382), i.e. 16.67/s, NOT the RA-era 25.
				Assert.That(EvacRefundTextMath.TickLifetime * 60, Is.EqualTo(4500),
					"4.5s at the default game speed, up from 1.8s");
			});
		}

		[Test]
		public void SlowerRiseKeepsTheTotalDriftUnchanged()
		{
			var oldDrift = EvacRefundTextMath.TotalRise(EvacRefundTextMath.DefaultTickLifetime, FloatingText.DefaultRiseRate);
			var newDrift = EvacRefundTextMath.TotalRise(EvacRefundTextMath.TickLifetime, EvacRefundTextMath.RiseRate);

			Assert.Multiple(() =>
			{
				Assert.That(oldDrift, Is.EqualTo(2580), "30 ticks x 86 = 2580 world units, about 2.5 cells");

				// Within a tenth of a cell of the old total. Left as a real tolerance rather than an exact equality
				// because 2580 does not divide by 75 — the point is the DRIFT, not a magic number.
				Assert.That(newDrift, Is.EqualTo(oldDrift).Within(102),
					"a longer life at the unchanged 86/tick would climb ~6.3 cells and leave the viewport");

				Assert.That(EvacRefundTextMath.RiseRate, Is.LessThan(FloatingText.DefaultRiseRate),
					"'animate it a bit slower' is the ask; a faster rise would fail it while still passing the drift check");
			});
		}
	}
}
