#region Copyright & License Information
/*
 * WW3MOD patrol-order wire-format test — multiplayer determinism.
 *
 * The Patrol order carries its waypoint route in Order.TargetString because an Order holds only a
 * single Target. Every client decodes that string and queues a PatrolActivity from it, so a decode
 * that differs between clients IS the desync this order exists to prevent. These cases pin the
 * round-trip and pin that malformed input is rejected wholesale rather than silently yielding a
 * partial route (a half-decoded route would diverge instead of failing).
 */
#endregion

using System.Globalization;
using System.Threading;
using NUnit.Framework;
using OpenRA.Mods.Common.Orders;

namespace OpenRA.Test
{
	[TestFixture]
	public class PatrolOrderTest
	{
		[Test]
		public void RouteSurvivesRoundTrip()
		{
			var waypoints = new[] { new CPos(3, 4), new CPos(0, 0), new CPos(127, 96) };
			var decoded = PatrolOrder.DeserializeWaypoints(PatrolOrder.SerializeWaypoints(waypoints));

			Assert.That(decoded, Is.EqualTo(waypoints));
		}

		[Test]
		public void DecodeIsCultureInvariant()
		{
			var waypoints = new[] { new CPos(12, 34), new CPos(56, 78) };
			var encoded = PatrolOrder.SerializeWaypoints(waypoints);

			var original = Thread.CurrentThread.CurrentCulture;
			try
			{
				// A client running a culture with a different digit/sign shape must decode the same
				// route as everyone else, or the two clients patrol to different cells.
				Thread.CurrentThread.CurrentCulture = new CultureInfo("ar-SA");
				Assert.That(PatrolOrder.SerializeWaypoints(waypoints), Is.EqualTo(encoded));
				Assert.That(PatrolOrder.DeserializeWaypoints(encoded), Is.EqualTo(waypoints));
			}
			finally
			{
				Thread.CurrentThread.CurrentCulture = original;
			}
		}

		[TestCase(null, TestName = "Malformed route rejected (null)")]
		[TestCase("", TestName = "Malformed route rejected (empty)")]
		[TestCase("5", TestName = "Malformed route rejected (lone coordinate)")]
		[TestCase("1,2,3", TestName = "Malformed route rejected (odd coordinate count)")]
		[TestCase("1,2,x,4", TestName = "Malformed route rejected (non-numeric)")]
		[TestCase("1,2,,4", TestName = "Malformed route rejected (empty coordinate)")]
		public void MalformedRouteIsRejected(string encoded)
		{
			Assert.That(PatrolOrder.DeserializeWaypoints(encoded), Is.Null);
		}

		[Test]
		public void SingleWaypointDecodesButIsNotAPatrol()
		{
			// The generator refuses to issue below two waypoints; the decoder still returns the
			// pair it was given, and Resolve applies the same floor on the receiving side.
			Assert.That(PatrolOrder.DeserializeWaypoints("7,8"), Is.EqualTo(new[] { new CPos(7, 8) }));
		}
	}
}
