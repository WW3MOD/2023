#region Copyright & License Information
/*
 * The Skirmish nuclear unlock clock's arithmetic: when each yield band comes up for sale.
 *
 * NuclearUnlockSchedule is a plain class for the reason its own header gives -- nothing in
 * OpenRA.Test can construct a World -- and this fixture is what makes that split worth having.
 *
 * THE LOAD-BEARING TESTS HERE ARE TheDefaultScheduleIsWhatTheLobbyBarDraws AND
 * GameEndersAreUnreachableAtEveryCapAndEveryElapsedTime. The first because the entire point of the
 * feature is that the bar stops lying, so a schedule that disagreed with the drawn band would be
 * the original defect with extra steps. The second because it is a user ruling (decision 17.3) with
 * no derivation behind it, chosen over a laxer recommendation, and therefore exactly the kind of
 * value a later reader "corrects".
 *
 * SCOPE, HONESTLY. This is the schedule's arithmetic and nothing else. It does NOT prove that
 * NuclearUnlockClock reads the lobby options correctly, that Sandbox suspends the clock, or that a
 * revoked band condition really removes a shop entry -- none of those are reachable without a World.
 * The first two are checked by reading the trait; the third is asserted in
 * PowerPurchaseWiringTest by reading the YAML links, and rests on SupportPowerInstance.Permitted
 * folding in instancesEnabled (SupportPowerManager.cs:160-166).
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class NuclearUnlockScheduleTest
	{
		// The mod's timestep. 60 ms, i.e. 16.67 ticks/s -- NOT 25 and NOT 40. This repo has assumed
		// the wrong rate at eleven sites and the 1.5x error looks plausible every time.
		const int Timestep = 60;
		const int TenMinutes = 10000;

		const int Cap = NuclearUnlockSchedule.HighestPurchasableRung;

		[Test]
		public void MinutesConvertExactlyRatherThanThroughATruncatedTickRate()
		{
			// THE BUG THIS ASSERTION EXISTS TO PREVENT, which is live in TimeLimitManager today:
			// `1000 / 60` is integer division and gives 16 ticks/s, so `minutes * 60 * 16` makes ten
			// minutes 9600 ticks = 576 s. Four per cent short, and short by more the longer the
			// interval. Invisible in a time limit; NOT invisible here, because this number is what the
			// lobby bar draws as a band boundary.
			Assert.That(NuclearUnlockSchedule.TicksForMinutes(10, Timestep), Is.EqualTo(TenMinutes),
				"ten minutes must be exactly 10000 ticks at a 60 ms timestep; 9600 means the " +
				"conversion divided before multiplying and the bar now draws a boundary the match " +
				"reaches 24 seconds early");

			Assert.That(NuclearUnlockSchedule.TicksForMinutes(5, Timestep), Is.EqualTo(5000));
			Assert.That(NuclearUnlockSchedule.TicksForMinutes(7, Timestep), Is.EqualTo(7000));
			Assert.That(NuclearUnlockSchedule.TicksForMinutes(20, Timestep), Is.EqualTo(20000));

			// Degenerate inputs yield 0 -- i.e. "no clock" -- rather than dividing by zero.
			Assert.That(NuclearUnlockSchedule.TicksForMinutes(0, Timestep), Is.EqualTo(0));
			Assert.That(NuclearUnlockSchedule.TicksForMinutes(-5, Timestep), Is.EqualTo(0));
			Assert.That(NuclearUnlockSchedule.TicksForMinutes(10, 0), Is.EqualTo(0));
		}

		[Test]
		public void TheDefaultScheduleIsWhatTheLobbyBarDraws()
		{
			// THE ACCEPTANCE TEST FOR THE WHOLE FEATURE, stated as the four times a host reads off the
			// bar. Decision 16: "low yield purchasable after ten minutes, the next tier after twenty,
			// and so on." Decision 22 fixes the default at ten so the shipped bar is true.
			Assert.That(NuclearUnlockSchedule.RungAt(0, TenMinutes, Cap),
				Is.EqualTo(NuclearUnlockSchedule.NothingReleased), "a match opened with nukes on sale");

			Assert.That(NuclearUnlockSchedule.RungAt(TenMinutes - 1, TenMinutes, Cap),
				Is.EqualTo(NuclearUnlockSchedule.NothingReleased), "the shop opened one tick early");

			// Inclusive at the bottom: AT ten minutes the 1 kt band is on sale, which is what makes the
			// marker the tick the shop opens rather than the tick before it.
			Assert.That(NuclearUnlockSchedule.RungAt(TenMinutes, TenMinutes, Cap),
				Is.EqualTo((int)NuclearRung.Kiloton), "the 1 kt band was not on sale at ten minutes");

			Assert.That(NuclearUnlockSchedule.RungAt(2 * TenMinutes, TenMinutes, Cap),
				Is.EqualTo((int)NuclearRung.TwentyKiloton));
			Assert.That(NuclearUnlockSchedule.RungAt(3 * TenMinutes, TenMinutes, Cap),
				Is.EqualTo((int)NuclearRung.FiftyKiloton));
			Assert.That(NuclearUnlockSchedule.RungAt(4 * TenMinutes, TenMinutes, Cap),
				Is.EqualTo((int)NuclearRung.HundredKiloton));

			// 40:00 is the top, and the number decision 16 flagged as the open tuning question: it is
			// most of a match. Recorded here so the arithmetic behind that question is checkable
			// without re-deriving it -- but the ANSWER is the user's and is not this fixture's to fix.
			Assert.That(NuclearUnlockSchedule.TicksUntilRung((int)NuclearRung.HundredKiloton, TenMinutes),
				Is.EqualTo(40 * 60 * 1000 / Timestep), "the top tier is no longer at 40:00 on the default");
		}

		[Test]
		public void TicksUntilRungIsTheExactInverseOfRungAt()
		{
			// The bar positions boundaries with TicksUntilRung and the match gates with RungAt. If the
			// two ever disagree the timeline draws a line the game does not honour, which is the
			// original defect in a new place and would be invisible in every other test here.
			foreach (var interval in new[] { 5000, 7000, TenMinutes, 20000 })
			{
				for (var rung = NuclearUnlockSchedule.LowestRung; rung <= Cap; rung++)
				{
					var at = NuclearUnlockSchedule.TicksUntilRung(rung, interval);

					Assert.That(NuclearUnlockSchedule.RungAt(at, interval, Cap), Is.EqualTo(rung),
						$"rung {rung} is not on sale at the tick the bar draws it ({at}, interval {interval})");
					Assert.That(NuclearUnlockSchedule.RungAt(at - 1, interval, Cap), Is.EqualTo(rung - 1),
						$"rung {rung} was already on sale one tick before the bar draws it (interval {interval})");
				}
			}
		}

		[Test]
		public void GameEndersAreUnreachableAtEveryCapAndEveryElapsedTime()
		{
			// DECISION 17.3, in the user's words as recorded: "Game-enders are NEVER purchasable in
			// Skirmish", with no host override -- chosen over the agent's recommendation to offer them
			// capped-below-by-default, and stricter than it. Checked against a cap ABOVE the ceiling
			// and a match run far past the top rung, because "never" has to mean unreachable rather
			// than merely not-yet.
			foreach (var cap in new[] { (int)NuclearRung.GameEnder, NuclearReleaseLadder.Highest, 99 })
				foreach (var elapsed in new[] { 0, TenMinutes, 40 * TenMinutes, int.MaxValue / 2 })
					Assert.That(NuclearUnlockSchedule.RungAt(elapsed, TenMinutes, cap),
						Is.LessThan((int)NuclearRung.GameEnder),
						$"a cap of {cap} put the game-ender band on sale at tick {elapsed}");

			// And with the wait switched off, which is the one path that returns the cap directly.
			Assert.That(NuclearUnlockSchedule.RungAt(0, 0, (int)NuclearRung.GameEnder),
				Is.EqualTo(Cap), "the no-wait opt-out bypassed the game-ender ceiling");

			Assert.That(NuclearUnlockSchedule.ClampCap((int)NuclearRung.GameEnder), Is.EqualTo(Cap));
			Assert.That(Cap, Is.EqualTo((int)NuclearRung.HundredKiloton),
				"the purchasable ceiling moved off 100 kt; decision 17.3 says it is the top of the " +
				"Skirmish schedule and that game-enders keep their meaning by being out of the shop");
		}

		[Test]
		public void AnIntervalOfZeroIsTheNoWaitOptOutAndStillHonoursTheCap()
		{
			// The host's opt-out, and the behaviour every Skirmish match had before this feature: on
			// sale from the first tick. It is NOT "an infinitely long interval" and it is NOT a
			// bypass -- the cap still applies, which the game-ender assertion above also covers.
			Assert.That(NuclearUnlockSchedule.RungAt(0, 0, Cap), Is.EqualTo(Cap));
			Assert.That(NuclearUnlockSchedule.RungAt(0, -5000, Cap), Is.EqualTo(Cap),
				"a negative interval must read as no-wait rather than as an unbounded countdown");

			Assert.That(NuclearUnlockSchedule.RungAt(0, 0, (int)NuclearRung.Kiloton),
				Is.EqualTo((int)NuclearRung.Kiloton), "no-wait ignored a 1 kt cap");
		}

		[Test]
		public void TheCapPinsTheScheduleHoweverLongTheMatchRuns()
		{
			foreach (var cap in new[] { NuclearRung.Kiloton, NuclearRung.TwentyKiloton, NuclearRung.FiftyKiloton, NuclearRung.HundredKiloton })
			{
				Assert.That(NuclearUnlockSchedule.RungAt(100 * TenMinutes, TenMinutes, (int)cap),
					Is.EqualTo((int)cap), $"a hundred intervals climbed past the {cap} cap");

				// And the rung below the cap is still reached on schedule, so a cap slows nothing down
				// on its way up -- it only stops the top.
				if (cap > NuclearRung.Kiloton)
					Assert.That(NuclearUnlockSchedule.RungAt(((int)cap - 1) * TenMinutes, TenMinutes, (int)cap),
						Is.EqualTo((int)cap - 1));
			}

			// A cap below the lowest band is raised to it rather than shutting the shop entirely.
			// "No nuclear weapons at all" is the `nuclear-arsenal` checkbox's job and is deliberately
			// not a second control here -- two controls for one outcome is one too many.
			Assert.That(NuclearUnlockSchedule.ClampCap((int)NuclearRung.Hold),
				Is.EqualTo(NuclearUnlockSchedule.LowestRung));
			Assert.That(NuclearUnlockSchedule.ClampCap(-3), Is.EqualTo(NuclearUnlockSchedule.LowestRung));
		}

		[Test]
		public void TheScheduleIndexesTheLadderSOwnBandTableRatherThanASecondOne()
		{
			// The rungs this clock sells are NuclearRung values and the conditions they release are
			// GrantConditionOnNuclearReleaseInfo.Conditions -- the same five names Escalation grants.
			// That is what stops the two modes drifting: a weapon names its band once, in YAML, and
			// each mode's clock decides only how far up that ONE table it has got.
			var conditions = new GrantConditionOnNuclearReleaseInfo().Conditions;

			for (var rung = NuclearUnlockSchedule.LowestRung; rung <= Cap; rung++)
				Assert.That(conditions.ContainsKey(rung), Is.True,
					$"rung {rung} is on the Skirmish schedule but has no band condition, so crossing " +
					"its interval would unlock nothing and the tier would be dead");

			// Every band the clock can sell, at the top of its schedule, and NOT the game-ender one.
			var atTop = System.Linq.Enumerable.ToArray(
				GrantConditionOnNuclearRelease.ConditionsFor(Cap, conditions));

			Assert.That(atTop, Is.EqualTo(new[]
			{
				"nuclear-release-1kt", "nuclear-release-20kt", "nuclear-release-50kt", "nuclear-release-100kt"
			}), "the Skirmish schedule's top rung no longer releases exactly the four buy bands");

			Assert.That(atTop, Has.No.Member("nuclear-release-gameender"),
				"the Skirmish schedule reached the game-ender band; see decision 17.3");
		}
	}
}
