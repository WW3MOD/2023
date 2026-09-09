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
using System.Linq;
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

		// ---------- the maximum-spread footprint ----------

		static WPos Cell(int x, int y) => new(1024 * x + 512, 1024 * y + 512, 0);

		[Test]
		public void AnUnboundedPowerAcceptsEveryAimPoint()
		{
			// WDist.Zero is the default on MissileStrikePowerInfo.MaxAimPointSpread, and it is what
			// the shipped Sarmat leaves it at. A power that never opted in must be untouched by all
			// of this -- no cell refused, no position moved.
			var anchor = Cell(0, 0);
			var faraway = Cell(400, 400);

			Assert.That(MultiAimPointOrder.IsWithinSpread(anchor, faraway, WDist.Zero), Is.True);
			Assert.That(MultiAimPointOrder.ClampToSpread(anchor, faraway, WDist.Zero), Is.EqualTo(faraway));
		}

		[Test]
		public void APointInsideTheFootprintIsAcceptedAndNotMoved()
		{
			var anchor = Cell(50, 50);
			var spread = new WDist(10 * 1024);

			// Nine cells out on one axis, and the exact diagonal that still fits (7,7 -> 9.9 cells).
			foreach (var inside in new[] { Cell(59, 50), Cell(50, 41), Cell(57, 57), anchor })
			{
				Assert.That(MultiAimPointOrder.IsWithinSpread(anchor, inside, spread), Is.True,
					$"{inside} is inside a 10-cell footprint and must be accepted");
				Assert.That(MultiAimPointOrder.ClampToSpread(anchor, inside, spread), Is.EqualTo(inside),
					"an accepted point must not be moved");
			}
		}

		[Test]
		public void APointOutsideTheFootprintIsRefused()
		{
			var anchor = Cell(50, 50);
			var spread = new WDist(10 * 1024);

			foreach (var outside in new[] { Cell(61, 50), Cell(50, 38), Cell(58, 58), Cell(120, 120) })
				Assert.That(MultiAimPointOrder.IsWithinSpread(anchor, outside, spread), Is.False,
					$"{outside} is outside a 10-cell footprint and must be refused");
		}

		[Test]
		public void TheBoundaryItselfIsInside()
		{
			// Exactly on the circle. The predicate is `<=`, so a player who places a point at the
			// drawn edge of the footprint gets it -- the circle they can see is inclusive.
			var anchor = Cell(50, 50);
			var spread = new WDist(10 * 1024);

			Assert.That(MultiAimPointOrder.IsWithinSpread(anchor, Cell(60, 50), spread), Is.True);
		}

		[Test]
		public void ClampingPullsAnOutOfRangePointBackOntoTheFootprint()
		{
			// THE HOSTILE-ORDER PATH. A modified client can put any six cells on the wire; this is
			// what stops them being six strikes across the map.
			var anchor = Cell(50, 50);
			var spread = new WDist(10 * 1024);
			var clamped = MultiAimPointOrder.ClampToSpread(anchor, Cell(120, 90), spread);

			Assert.That((clamped - anchor).HorizontalLength, Is.EqualTo(spread.Length).Within(2),
				"a clamped point must land on the edge of the footprint");
		}

		[Test]
		public void ClampingKeepsTheBearingOfTheOriginalPoint()
		{
			// Pulled straight back toward the anchor, not relocated. The attacker still chooses the
			// DIRECTION each warhead goes; only the distance is bounded.
			var anchor = Cell(50, 50);
			var spread = new WDist(10 * 1024);
			var far = Cell(150, 50);
			var clamped = MultiAimPointOrder.ClampToSpread(anchor, far, spread);

			Assert.That(clamped.Y, Is.EqualTo(anchor.Y));
			Assert.That(clamped.X, Is.GreaterThan(anchor.X));
		}

		[Test]
		public void ClampingIsRepeatableToTheUnit()
		{
			// Same argument as FallbackIsRepeatableToTheUnit: this runs on the synced order path, so
			// identical inputs must give a byte-identical answer on every client.
			var anchor = Cell(50, 50);
			var spread = new WDist(10 * 1024);

			Assert.That(
				MultiAimPointOrder.ClampToSpread(anchor, Cell(123, 97), spread),
				Is.EqualTo(MultiAimPointOrder.ClampToSpread(anchor, Cell(123, 97), spread)));
		}

		[Test]
		public void HeightDoesNotCountTowardTheSpread()
		{
			// The bound is a footprint on the ground. An aim point on a cliff is not further away
			// for being higher, and a map with tall terrain must not shrink the weapon.
			var anchor = new WPos(512, 512, 0);
			var high = new WPos(512 + (9 * 1024), 512, 20000);

			Assert.That(MultiAimPointOrder.IsWithinSpread(anchor, high, new WDist(10 * 1024)), Is.True);
		}

		[Test]
		public void TheFallbackRingUsesEveryBearingAtEveryRingSize()
		{
			// REGRESSION for the 4096/1024 dividend. WAngle is 1024 to the TURN, so `i * 4096 / ring`
			// walked four turns instead of one and collapsed the ring wherever `ring` shared a factor
			// with 4: at count 3 and count 5 every offset stacked on ONE point, and at count 7 they
			// paired up. Only count 6 (ring 5, coprime with 4) escaped, which is why the one shipped
			// value hid it. Distinctness at every size is the property that was actually wanted.
			var spread = new WDist(24576);
			for (var count = 2; count <= 12; count++)
			{
				var offsets = MultiAimPointOrder.FallbackRingOffsets(count, spread);
				var distinct = offsets.Distinct().Count();

				Assert.That(distinct, Is.EqualTo(count),
					$"a {count}-warhead fallback ring put two warheads on the same point");
			}
		}

		[Test]
		public void TheFallbackRingStillLandsTheShippedSalvoOnTheSameSixPoints()
		{
			// BOTH SHIPPED POWERS USE AimPoints: 6, and the 4096 -> 1024 correction must not move
			// where their bot-fired salvo lands. It does not: 4 and 5 are coprime, so `i * 4096 / 5`
			// and `i * 1024 / 5` generate the SAME five bearings -- the index walks the ring the
			// other way round, which is visible only as the rotational order of the arrivals.
			var spread = new WDist(24576);
			var offsets = MultiAimPointOrder.FallbackRingOffsets(6, spread);
			var expected = new[] { 0, 204, 409, 614, 819 }
				.Select(a => new WVec(spread.Length, 0, 0).Rotate(WRot.FromYaw(new WAngle(a))))
				.ToHashSet();

			Assert.That(offsets[0], Is.EqualTo(WVec.Zero));
			Assert.That(offsets.Skip(1).ToHashSet(), Is.EquivalentTo(expected));
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
