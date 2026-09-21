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
 * DOOMSDAY statistics freeze, and the salvo's arrival ordering.
 *
 * The user's requirement: "Statistics should stop counting at the point the nukes come in", and the
 * winner is the score "before the nukes fall". The failure mode this guards is the superficial fix —
 * snapshotting what the end-of-match panel displays while the underlying counters keep running, so
 * that anything reading them later (the tournament scorers, the composition telemetry, the victory
 * comparison itself) sees post-apocalypse totals.
 *
 * PlayerExperience is testable directly — PlayerExperienceInfo.Create takes no arguments — and it is
 * the trait that matters most, because ConquestVictoryConditions picks the winner by comparing
 * PlayerStatistics.Experience, which forwards to it. PlayerStatistics itself needs an Actor to
 * construct, so its freeze is verified by inspection and by the build rather than here; that
 * limitation is stated in the report rather than papered over.
 */

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class DoomsdayStatsFreezeTest
	{
		[Test]
		public void ExperienceStopsAccruingAtTheFreeze()
		{
			var xp = new PlayerExperience();
			xp.GiveExperience(100);
			xp.GiveExperience(50);
			Assert.That(xp.Experience, Is.EqualTo(150));
			Assert.That(xp.Frozen, Is.False);

			xp.Freeze();
			Assert.That(xp.Frozen, Is.True);

			// Every one of these is a real path in the engine: kills through GivesExperience, captures
			// through CaptureActor, donations, infiltration rewards, repair rewards, Lua score writes.
			// They all funnel through GiveExperience, which is why one guard covers them.
			xp.GiveExperience(1000);
			xp.GiveExperience(-1000);
			xp.GiveExperience(1);
			Assert.That(xp.Experience, Is.EqualTo(150), "Score moved after the freeze.");
		}

		[Test]
		public void FreezeIsIdempotent()
		{
			// DoomsdayStrike freezes once, but Resolve re-raises NotifyTimerExpired and a scenario could
			// in principle trip the trigger path twice. Re-freezing must not resurrect the counter.
			var xp = new PlayerExperience();
			xp.GiveExperience(7);
			xp.Freeze();
			xp.Freeze();
			xp.GiveExperience(7);
			Assert.That(xp.Experience, Is.EqualTo(7));
		}

		[Test]
		public void TheNukesOwnKillsCreditNobody()
		{
			// The point of the user's sentence. Two players, frozen at the expiry tick; the annihilation
			// then "kills" everything on the map. Whoever the salvo happens to kill first must not move
			// the standing.
			var a = new PlayerExperience();
			var b = new PlayerExperience();
			a.GiveExperience(900);
			b.GiveExperience(1200);

			a.Freeze();
			b.Freeze();

			var beforeA = a.Experience;
			var beforeB = b.Experience;

			// The sweep destroys 400 actors, in whatever order the geometry produced.
			for (var i = 0; i < 400; i++)
			{
				a.GiveExperience(25);
				b.GiveExperience(25);
			}

			Assert.That(a.Experience, Is.EqualTo(beforeA));
			Assert.That(b.Experience, Is.EqualTo(beforeB));
			Assert.That(b.Experience, Is.GreaterThan(a.Experience), "The winner must still be the one who led at the freeze.");
		}

		[Test]
		public void WinnerIsDecidedOnTheFrozenScoreNotTheFinalOne()
		{
			// The regression that matters: without the freeze, a trailing player whose units happen to die
			// last — or who lands a kill during the annihilation — could overtake. Model both orderings
			// and assert they agree.
			var scores = new[] { ("north", 4200), ("south", 4350) };

			var frozen = scores.Select(s =>
			{
				var xp = new PlayerExperience();
				xp.GiveExperience(s.Item2);
				xp.Freeze();
				return (s.Item1, Xp: xp);
			}).ToList();

			var atFreeze = frozen.OrderByDescending(p => p.Xp.Experience).First().Item1;

			// The salvo now runs. "north" is credited for a great deal of destruction it did not do.
			foreach (var p in frozen)
				if (p.Item1 == "north")
					p.Xp.GiveExperience(100000);

			var afterSalvo = frozen.OrderByDescending(p => p.Xp.Experience).First().Item1;

			Assert.That(atFreeze, Is.EqualTo("south"));
			Assert.That(afterSalvo, Is.EqualTo(atFreeze), "The annihilation changed who won.");
		}

		[Test]
		public void ScoreTiesResolveByStableSeatOrder()
		{
			// Simultaneous elimination is not a case in Doomsday — every player dies on the same tick and
			// none has a WinState, because the victory checks are suspended for the whole salvo. So the
			// only ordering decision is this one, and on a genuine score tie it must be deterministic.
			// ConquestVictoryConditions uses OrderByDescending, which is a STABLE sort in .NET, so the
			// earliest player in world.Players order wins — identically on every client.
			var players = new List<(string Name, int Score)>
			{
				("seat0", 1000),
				("seat1", 1000),
				("seat2", 999),
			};

			var first = players.OrderByDescending(p => p.Score).First().Name;
			Assert.That(first, Is.EqualTo("seat0"));

			// Re-running the same comparison must give the same answer; a hash-ordered container here
			// would be the classic desync.
			for (var i = 0; i < 50; i++)
				Assert.That(players.OrderByDescending(p => p.Score).First().Name, Is.EqualTo(first));
		}

		// THE TWO SALVO-SCHEDULE TESTS THAT SAT HERE WERE DELETED ON 2026-09-20 WITH THEIR SUBJECT.
		// SalvoArrivesSmallFirstThenCitiesThenFill and AnEmptyOutlierWaveDoesNotLeaveDeadAirBefore-
		// TheCities pinned DoomsdayMath.BuildSchedule, which laid out the map-wide Dead Hand salvo in
		// an outlier wave, a pause and a city wave. There is no salvo: the ending is now two per-side
		// packages on one evenly-spaced cascade, and the property that replaced "the waves must read
		// as separate events" is "every warhead of both packages lands inside one bounded span",
		// which FinalExchangeCascadeTest asserts. Recoverable from git if the wave shape ever returns.

		[Test]
		public void IsqrtIsExactAtAndAroundPerfectSquares()
		{
			// Isqrt walks Math.Sqrt's result onto the exact integer floor, because the coverage margin is
			// compared in whole cells and an off-by-one from FPU rounding would silently widen or narrow
			// the guarantee.
			for (var n = 0; n < 2000; n++)
			{
				var sq = (long)n * n;
				Assert.That(DoomsdayMath.Isqrt(sq), Is.EqualTo(n));
				if (n > 0)
				{
					Assert.That(DoomsdayMath.Isqrt(sq - 1), Is.EqualTo(n - 1));
					Assert.That(DoomsdayMath.Isqrt(sq + 1), Is.EqualTo(n));
				}
			}

			Assert.That(DoomsdayMath.Isqrt(0), Is.EqualTo(0));
			Assert.That(DoomsdayMath.Isqrt(-5), Is.EqualTo(0));
		}
	}
}
