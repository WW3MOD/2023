#region Copyright & License Information
/*
 * WW3MOD MultiAimPointOrder tests — the wire format and fallback geometry behind a support power
 * the player aims at SEVERAL points (user, 2026-09-07: "The MIRV lands all nukes really close
 * together... we can click multiple times, and an overlay shows us how many more we can target, and
 * only when we place the last one it is issued").
 *
 * WHY THIS FILE EXISTS. A multi-point strike is the first order in the mod that carries a LIST of
 * simulation-relevant positions, and every client has to rebuild that list identically or the match
 * desyncs on the loudest possible event. The two things that can break that are both here:
 *
 *     the STRING must survive the round trip     -> culture, sign, and malformed input
 *     the RING must be integer geometry          -> repeatability, no float, no RNG
 *
 * SCOPE, HONESTLY. This covers the encoder, the decoder and the fallback ring — the pure functions.
 * It does NOT cover that the order generator issues on the last click, that TargetString reaches the
 * far client, that the aim points are clamped and truncated on receipt, or that six missiles are
 * actually spawned. Those need a World and an order pipeline. Said plainly so a green run here is
 * not mistaken for whole-feature cover; the click sequence in the branch report is what tests those.
 */
#endregion

using System.Globalization;
using System.Threading;
using NUnit.Framework;
using OpenRA.Mods.Common.Orders;

namespace OpenRA.Test
{
	[TestFixture]
	public class MultiAimPointOrderTest
	{
		// ---------- the wire format ----------

		[Test]
		public void RoundTripsAimPointsInOrder()
		{
			var points = new[] { new CPos(12, 34), new CPos(56, 78), new CPos(9, 10) };

			var decoded = MultiAimPointOrder.Deserialize(MultiAimPointOrder.Serialize(points));

			Assert.That(decoded, Is.EqualTo(points), "the aim points must decode identically and in order");
		}

		[Test]
		public void RoundTripsNegativeCoordinates()
		{
			// Map.Clamp runs on the RECEIVING side, so a negative cell is a legal thing to find on
			// the wire. It must decode as the negative it was rather than as a parse failure that
			// silently degrades the strike to a single point.
			var points = new[] { new CPos(-5, -900), new CPos(0, 0) };

			Assert.That(MultiAimPointOrder.Deserialize(MultiAimPointOrder.Serialize(points)), Is.EqualTo(points));
		}

		[Test]
		public void RoundTripsUnderACommaDecimalCulture()
		{
			// THE ONE THAT WOULD ACTUALLY DESYNC A REAL MATCH. A German or French client formats and
			// parses numbers differently by default, and the encoding is comma-separated — so a
			// culture-sensitive implementation would either emit separators it cannot read back or
			// read a different number than the sender wrote.
			var original = Thread.CurrentThread.CurrentCulture;
			try
			{
				Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

				var points = new[] { new CPos(1234, -1678), new CPos(7, 8) };
				var encoded = MultiAimPointOrder.Serialize(points);

				Assert.That(encoded, Is.EqualTo("1234,-1678,7,8"), "the encoding must not pick up culture separators");
				Assert.That(MultiAimPointOrder.Deserialize(encoded), Is.EqualTo(points));
			}
			finally
			{
				Thread.CurrentThread.CurrentCulture = original;
			}
		}

		[Test]
		public void EmptyInputSerializesToNullSoTheOrderFieldStaysUnset()
		{
			// Order.Serialize sets the TargetString wire flag on non-null (Order.cs:388-389), so
			// returning null rather than "" is what keeps a single-target order byte-identical to
			// what it was before this feature existed.
			Assert.That(MultiAimPointOrder.Serialize(null), Is.Null);
			Assert.That(MultiAimPointOrder.Serialize(new CPos[0]), Is.Null);
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("12")]
		[TestCase("12,34,56")]
		[TestCase("12,thirty-four")]
		[TestCase("12,34,,78")]
		public void MalformedInputDecodesToNullRatherThanThrowing(string encoded)
		{
			// Null is the SINGLE-TARGET signal, and the caller falls back to Order.Target. A throw
			// here would land inside order resolution — on every client at once.
			Assert.That(MultiAimPointOrder.Deserialize(encoded), Is.Null);
		}

		[Test]
		public void AnOutOfRangeCoordinateIsBoundedByCPosBeforeItReachesTheMap()
		{
			// NOT A PROPERTY OF THIS ENCODER — a property of CPos, pinned here because it is the
			// first of the two bounds a hostile or corrupt order runs into and it is invisible at
			// the call site. CPos packs X and Y into 12 signed bits each (CPos.cs:21-22, 41), so the
			// constructor wraps anything outside -2048..2047 rather than storing it. Map.Clamp on
			// the receiving side is the second bound; this one is why the first cannot be skipped
			// by feeding a very large number.
			//
			// Found by this test suite: an earlier draft used -5678 as an "ordinary negative" and
			// decoded -1582, which is -5678 + 4096.
			var decoded = MultiAimPointOrder.Deserialize("1234,-5678");

			Assert.That(decoded.Length, Is.EqualTo(1));
			Assert.That(decoded[0].Y, Is.EqualTo(-1582), "CPos wraps Y into 12 signed bits");
			Assert.That(decoded[0].Y, Is.InRange(-2048, 2047));
		}

		// ---------- the fallback ring ----------

		[Test]
		public void FallbackPutsTheFirstWarheadExactlyOnTheSendersPoint()
		{
			var offsets = MultiAimPointOrder.FallbackRingOffsets(6, new WDist(24576));

			Assert.That(offsets.Length, Is.EqualTo(6));
			Assert.That(offsets[0], Is.EqualTo(WVec.Zero), "the first warhead must land where the sender pointed");
		}

		[Test]
		public void FallbackSpreadsTheRestOntoTheRing()
		{
			var spread = new WDist(24576);
			var offsets = MultiAimPointOrder.FallbackRingOffsets(6, spread);

			for (var i = 1; i < offsets.Length; i++)
				Assert.That(offsets[i].HorizontalLength, Is.EqualTo(spread.Length).Within(2),
					$"aim point {i} must sit on the ring");

			// The whole point of the fallback: a bot's salvo must not be six warheads on one cell,
			// which is the complaint that started this work.
			for (var i = 0; i < offsets.Length; i++)
				for (var j = i + 1; j < offsets.Length; j++)
					Assert.That(offsets[i], Is.Not.EqualTo(offsets[j]), $"aim points {i} and {j} coincide");
		}

		[Test]
		public void FallbackIsRepeatableToTheUnit()
		{
			// Stands in for "every client computes the same thing": the function reads nothing but
			// its arguments, so identical arguments must give a byte-identical answer. If this ever
			// fails, something non-deterministic has been introduced into a synced code path.
			var a = MultiAimPointOrder.FallbackRingOffsets(6, new WDist(24576));
			var b = MultiAimPointOrder.FallbackRingOffsets(6, new WDist(24576));

			Assert.That(a, Is.EqualTo(b));
		}

		[Test]
		public void FallbackCollapsesToASinglePointWhenNoSpreadIsConfigured()
		{
			// WDist.Zero is the default on MissileStrikePowerInfo.AimPointFallbackSpread, and a
			// power that never opted in must be untouched by any of this.
			var offsets = MultiAimPointOrder.FallbackRingOffsets(6, WDist.Zero);

			Assert.That(offsets.Length, Is.EqualTo(6));
			foreach (var o in offsets)
				Assert.That(o, Is.EqualTo(WVec.Zero));
		}

		[TestCase(0)]
		[TestCase(1)]
		[TestCase(-3)]
		public void FallbackDegeneratesSafelyForOneOrFewerWarheads(int count)
		{
			var offsets = MultiAimPointOrder.FallbackRingOffsets(count, new WDist(24576));

			Assert.That(offsets.Length, Is.EqualTo(1), "a single-warhead power gets exactly one aim point");
			Assert.That(offsets[0], Is.EqualTo(WVec.Zero));
		}

		[Test]
		public void FallbackHandlesATwoWarheadRing()
		{
			// ring == 1, so the divisor `i * 4096 / ring` is at its smallest. Guards against an
			// off-by-one that would divide by zero for the smallest multi-warhead power.
			var offsets = MultiAimPointOrder.FallbackRingOffsets(2, new WDist(24576));

			Assert.That(offsets.Length, Is.EqualTo(2));
			Assert.That(offsets[0], Is.EqualTo(WVec.Zero));
			Assert.That(offsets[1].HorizontalLength, Is.EqualTo(24576).Within(2));
		}
	}
}
