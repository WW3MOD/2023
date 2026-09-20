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

/*
 * ONE IMPACT CASCADE, and the defect it closes stated as a bound.
 *
 * THE BUG, IN NUMBERS. Dead Hand's first warhead was scheduled at LeadInTicks 30 plus a 44-tick
 * flight, i.e. open+74. A Sarmat ordered on the FIRST tick of the window carried MissileDelay 500
 * plus a ~92-tick flight and landed at open+592. So the machine's warheads landed ~8.5 s before the
 * player's own, on every map, every time. Neither number was wrong; they were two unrelated clocks.
 *
 * THE PROPERTY THAT REPLACES THEM, and it is what the main test below sweeps:
 *   for ANY responder placement tick inside the window, and for EITHER missile's flight time,
 *   every impact of both packages lands inside [anchor, anchor + (2N-1) * spacing]
 *   AND no warhead is ever asked to launch in the past.
 *
 * The second half matters as much as the first. A schedule can satisfy the bound on paper and still
 * be unreachable -- "arrive at tick T" is impossible when T minus the flight is already behind you,
 * and LaunchDelayFor clamps to zero, which puts that warhead OUTSIDE the bound in practice. The
 * anchor floor (FinalExchangeFlightTicks) is what makes it reachable, so the sweep asserts both.
 */

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class FinalExchangeCascadeTest
	{
		// The shipped configuration (mods/ww3mod/rules/world.yaml, DoomsdayStrike).
		const int Spacing = 15;
		const int WindowTicks = 250;
		const int FlightFloorTicks = 800;

		// MissileDelay on both game-enders (rules/ingame/nuclear-arsenal.yaml).
		const int MissileDelay = 500;

		// Flight = PreLaunchTicks + EstimateArcTicks(standoff). sarmatmissile is Speed 1600 and
		// b83missile is Speed 700 over the same standoff, so the B83 is the slow one by better than
		// a factor of two. The sweep runs the whole plausible band rather than two points, because
		// the standoff is the MAP DIAGONAL and therefore differs on every map.
		static readonly int[] Flights = { 60, 100, 126, 200, 290, 350 };

		[Test]
		public void BothPackagesLandInsideOneBoundedSpanFromAnyPlacementTime()
		{
			const int Open = 4000;
			const int Close = Open + WindowTicks;

			for (var n = 2; n <= 6; n++)
			{
				foreach (var triggerFlight in Flights)
				{
					foreach (var responderFlight in Flights)
					{
						for (var placedAt = Open; placedAt <= Close; placedAt++)
						{
							var cascade = new FinalExchangeCascade(Spacing);
							cascade.SetFloor(Close + FlightFloorTicks);

							var impacts = new List<(int Impact, int LaunchedAt, int Flight)>();

							// The trigger fires on the tick the window opens: one order, N warheads,
							// reserved in launch order.
							for (var i = 0; i < n; i++)
								impacts.Add((cascade.Reserve(Open + MissileDelay + triggerFlight), Open, triggerFlight));

							// The responder places somewhere inside the window -- or, at placedAt ==
							// Close, is auto-fired on the closing tick, which is the same arithmetic.
							for (var i = 0; i < n; i++)
								impacts.Add((cascade.Reserve(placedAt + MissileDelay + responderFlight), placedAt, responderFlight));

							var anchor = cascade.AnchorTick;
							var span = (2 * n) - 1;

							Assert.That(cascade.SlotsIssued, Is.EqualTo(2 * n));

							// ASSERTED BY HAND RATHER THAN THROUGH Assert.That PER WARHEAD, and that is
							// about cost rather than style: this sweep runs a quarter of a million
							// checks and an interpolated message argument is built EAGERLY on every
							// passing call. Building it only on failure keeps the whole fixture under
							// a second.
							foreach (var (impact, launchedAt, flight) in impacts)
							{
								if (impact < anchor || impact > anchor + (span * Spacing))
									Assert.Fail($"impact {impact} outside [{anchor}, {anchor + (span * Spacing)}] " +
										$"for N={n} triggerFlight={triggerFlight} responderFlight={responderFlight} placedAt={placedAt}");

								// REACHABLE, not merely in range: the launch delay solved for this
								// impact must be non-negative, or the warhead is late and the bound
								// above is a fiction.
								if (impact - launchedAt - flight < 0)
									Assert.Fail($"a warhead launched at {launchedAt} cannot reach {impact} on a {flight}-tick flight "
										+ $"(N={n}, placedAt={placedAt})");
							}
						}
					}
				}
			}
		}

		[Test]
		public void TheFirstWarheadLandsExactlyOnTheAnchor()
		{
			var cascade = new FinalExchangeCascade(Spacing);
			cascade.SetFloor(1000);

			Assert.That(cascade.AnchorTick, Is.EqualTo(-1), "The anchor does not exist until something is reserved.");
			Assert.That(cascade.Reserve(2000), Is.EqualTo(2000));
			Assert.That(cascade.AnchorTick, Is.EqualTo(2000));
		}

		[Test]
		public void SlotsAreSequentialAndEvenlySpacedInLaunchOrder()
		{
			var cascade = new FinalExchangeCascade(Spacing);
			cascade.SetFloor(0);

			// Every reservation after the first IGNORES its own natural tick -- that is the whole
			// point. A warhead's flight decides when it launches, never when it arrives.
			Assert.That(cascade.Reserve(1000), Is.EqualTo(1000));
			Assert.That(cascade.Reserve(1), Is.EqualTo(1000 + Spacing));
			Assert.That(cascade.Reserve(999999), Is.EqualTo(1000 + (2 * Spacing)));
			Assert.That(cascade.LastImpactTick, Is.EqualTo(1000 + (2 * Spacing)));
		}

		[Test]
		public void TheFloorRaisesAnEarlyAnchorAndNeverLowersALateOne()
		{
			// Door (b), the time limit: nobody has fired, so the first reservation is a player's own
			// placement and its natural impact is inside the window. The floor pushes the whole
			// cascade out past the close.
			var early = new FinalExchangeCascade(Spacing);
			early.SetFloor(1050);
			Assert.That(early.Reserve(600), Is.EqualTo(1050));

			// Door (a) on a long window or a slow bomb: the trigger's own first impact is already
			// later than the floor and the cascade starts THERE, which is the ruling as written.
			var late = new FinalExchangeCascade(Spacing);
			late.SetFloor(1050);
			Assert.That(late.Reserve(1400), Is.EqualTo(1400));
		}

		[Test]
		public void RaisingTheFloorAfterTheAnchorIsFixedChangesNothing()
		{
			// Warheads already scheduled cannot be moved -- they are in the air, or are a
			// SpawnActorEffect counting down. A late SetFloor must not retroactively shift them.
			var cascade = new FinalExchangeCascade(Spacing);
			cascade.SetFloor(1000);
			var first = cascade.Reserve(1200);

			cascade.SetFloor(99999);
			Assert.That(cascade.AnchorTick, Is.EqualTo(first));
			Assert.That(cascade.Reserve(0), Is.EqualTo(first + Spacing));
		}

		[Test]
		public void ASpacingBelowOneIsClampedRatherThanStackingTheExchangeOnOneTick()
		{
			// Zero spacing is the pre-cascade failure mode in miniature: twelve detonations on one
			// tick. YAML can say it; this cannot honour it.
			foreach (var bad in new[] { 0, -1, -100 })
			{
				var cascade = new FinalExchangeCascade(bad);
				cascade.Reserve(500);
				Assert.That(cascade.Reserve(500), Is.EqualTo(501), $"spacing {bad}");
			}
		}

		[Test]
		public void ALateWarheadLaunchesImmediatelyRatherThanNotAtAll()
		{
			// The clamp exists for the case the floor is supposed to prevent -- a scenario that set
			// FinalExchangeFlightTicks too low, or a placement the window should not have allowed.
			// Arriving late beats not flying.
			Assert.That(FinalExchangeCascade.LaunchDelayFor(1000, 1500, 200), Is.EqualTo(300));
			Assert.That(FinalExchangeCascade.LaunchDelayFor(1000, 1200, 200), Is.EqualTo(0));
			Assert.That(FinalExchangeCascade.LaunchDelayFor(1000, 900, 200), Is.EqualTo(0));
		}

		[Test]
		public void TheShippedFloorCoversTheSlowestGameEnderOnTheLargestMap()
		{
			// THE ARITHMETIC THE YAML COMMENT CLAIMS, checked rather than asserted in prose. A warhead
			// ordered on the LAST tick of the window must still be able to reach the anchor.
			//
			// The flight term is the b83missile's crossing of x-lake's standoff: the 128x128 diagonal
			// (about 185,363 WDist) plus ApproachMargin 16c0, at Speed 700, is roughly 290 ticks.
			const int SlowestFlight = 290;
			Assert.That(FlightFloorTicks, Is.GreaterThanOrEqualTo(MissileDelay + SlowestFlight),
				"FinalExchangeFlightTicks must cover MissileDelay plus the slowest game-ender's flight, "
				+ "or a warhead placed on the window's last tick is scheduled into the past.");
		}
	}
}
