#region Copyright & License Information
/*
 * THE NUCLEAR EXCHANGE'S STATE MACHINE -- permanent parity plus a retaliation window, as the user
 * ruled it on 2026-09-13 (manager-2b944571 decision 01).
 *
 * Every rule lives in NuclearExchangeState, a plain class with no world dependency, for the reason
 * DefconEscalationState's header gives: nothing in OpenRA.Test can construct a World, so arithmetic
 * inside a trait method is arithmetic verified by READING. This fixture is what makes that split
 * worth having, and it is the whole verification of the model -- NuclearExchange (the trait) is a
 * thin shell over this and can only be checked by playing a match.
 *
 * THE LOAD-BEARING TESTS ARE FiringSmallDoesNotEscalateMuch AND GameEndersAreReachableOnlyThroughAWindow.
 * The first is the user's entire reason for judging the model sound -- "the winner's correct play is
 * restraint" only holds if firing at your own level arms the loser at that level and no higher. The
 * second is what stops a held apocalypse existing at all: nothing ever writes GameEnder into a
 * permanent level, so there is no draw card to keep.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class NuclearExchangeStateTest
	{
		// Two sides, which is the whole design (decision 15). The keys are arbitrary ints here; the
		// trait derives them from lobby teams.
		const int America = 1;
		const int Russia = 2;

		const int Window = 3000;  // 3:00 at the mod's 60 ms timestep, the shipped default.

		const int B61LowTons = 300;          // 0.3 kt -> Kiloton
		const int AtomicTons = 20000;        // 20 kt  -> TwentyKiloton
		const int B61MaxTons = 50000;        // 50 kt  -> FiftyKiloton
		const int W76Tons = 100000;          // 100 kt -> HundredKiloton
		const int SarmatRvTons = 750000;     // 750 kt -> GameEnder
		const int TsarBombaTons = 50000000;  // 50 Mt  -> above SandboxOnlyAboveTons

		// EVERY TEST THAT IS NOT ABOUT THE GATE STARTS RELEASED, because an Escalation match ships
		// SHUT: nothing is permitted until DEFCON 1 plus the first-warheads delay. A fixture that
		// skipped this step would assert "nothing is permitted" over and over instead of testing the
		// exchange each test is actually about.
		static NuclearExchangeState Released(int windowTicks = Window)
		{
			var state = new NuclearExchangeState(DefconGameMode.Escalation, windowTicks);
			state.RegisterSide(America);
			state.RegisterSide(Russia);

			Assert.That(state.Release(), Is.True, "the fixture failed to open the release gate");
			return state;
		}

		[Test]
		public void ReleaseGivesBothSidesTheLowestBandPermanently()
		{
			var state = new NuclearExchangeState(DefconGameMode.Escalation, Window);
			state.RegisterSide(America);
			state.RegisterSide(Russia);

			// BEFORE RELEASE, NOTHING. Not "the bottom band" -- HOLD, which grants no condition at
			// all, so every nuclear cameo is dark.
			foreach (var side in new[] { America, Russia })
				Assert.That(state.ReleasedLevelFor(side), Is.EqualTo((int)NuclearRung.Hold),
					$"side {side} held a band before release");

			Assert.That(state.Release(), Is.True);

			foreach (var side in new[] { America, Russia })
				Assert.That(state.PermanentLevelFor(side), Is.EqualTo((int)NuclearRung.Kiloton),
					$"side {side} did not get the 1 kt band at release");

			// Release is once. A second call changes nothing, so the trait may poll
			// DefconEscalation.NuclearReleaseOpen every tick rather than latch an edge of its own.
			Assert.That(state.Release(), Is.False);
			Assert.That(state.Released, Is.True);

			// AND IT DOES NOT OPEN A WINDOW. Release is parity, not a provocation; a window that
			// opened here would hand both sides the 20 kt band for three minutes having been fired
			// at by nobody.
			foreach (var side in new[] { America, Russia })
				Assert.That(state.WindowTicksRemainingFor(side), Is.EqualTo(0),
					$"release opened a retaliation window for side {side}");
		}

		[Test]
		public void AnyLaunchArmsTheOtherSideInKindAndOneBandAbove()
		{
			var state = Released();

			var outcome = state.ReportLaunch(America, AtomicTons);
			Assert.That(outcome.Counted, Is.True);
			Assert.That(outcome.Band, Is.EqualTo((int)NuclearRung.TwentyKiloton));
			Assert.That(outcome.FinalExchange, Is.False);

			// PARITY IN KIND: Russia now holds 20 kt permanently, wherever the warhead landed. There
			// is no damage attribution in this model -- the ruling rejected decision 14's 10 % rule.
			Assert.That(state.PermanentLevelFor(Russia), Is.EqualTo((int)NuclearRung.TwentyKiloton));

			// AND ONE BAND ABOVE IT, for a window.
			Assert.That(state.WindowLevelFor(Russia), Is.EqualTo((int)NuclearRung.FiftyKiloton));
			Assert.That(state.WindowTicksRemainingFor(Russia), Is.EqualTo(Window));
			Assert.That(state.ReleasedLevelFor(Russia), Is.EqualTo((int)NuclearRung.FiftyKiloton));

			// THE FIRER GAINS NOTHING. This is the inversion of decision 06's shared ladder, where
			// going first was free and both sides climbed together. Firing is now a cost.
			Assert.That(state.PermanentLevelFor(America), Is.EqualTo((int)NuclearRung.Kiloton),
				"the firer climbed by firing; that is the shared ladder, not the exchange");
			Assert.That(state.WindowTicksRemainingFor(America), Is.EqualTo(0));
		}

		[Test]
		public void TheWindowLapsesAndTheGrantIsGone()
		{
			var state = Released();
			state.ReportLaunch(America, AtomicTons);

			// Open for exactly Window ticks: not one short, not one long.
			for (var i = 0; i < Window - 1; i++)
			{
				var lapsed = state.TickWindows();
				Assert.That(lapsed, Is.Null, $"the window lapsed on tick {i + 1} of {Window}");
				Assert.That(state.ReleasedLevelFor(Russia), Is.EqualTo((int)NuclearRung.FiftyKiloton));
			}

			var last = state.TickWindows();
			Assert.That(last, Is.Not.Null.And.EqualTo(new[] { Russia }), "the window did not lapse on its last tick");

			// THE GRANT IS GONE AND THE PARITY IS NOT. "If the window lapses unused, B stays at Y."
			Assert.That(state.WindowTicksRemainingFor(Russia), Is.EqualTo(0));
			Assert.That(state.WindowLevelFor(Russia), Is.EqualTo((int)NuclearRung.Hold));
			Assert.That(state.ReleasedLevelFor(Russia), Is.EqualTo((int)NuclearRung.TwentyKiloton));
			Assert.That(state.PermanentLevelFor(Russia), Is.EqualTo((int)NuclearRung.TwentyKiloton));

			// And it stays gone. There is no indefinite grant anywhere in this model, which is what
			// stops a held apocalypse stalling the match.
			for (var i = 0; i < 10000; i++)
				Assert.That(state.TickWindows(), Is.Null, "a lapsed window lapsed again");
		}

		[Test]
		public void EveryHitRestartsTheWindowAndABiggerHitRaisesIt()
		{
			var state = Released();

			state.ReportLaunch(America, AtomicTons);
			for (var i = 0; i < Window / 2; i++)
				state.TickWindows();

			Assert.That(state.WindowTicksRemainingFor(Russia), Is.EqualTo(Window - (Window / 2)));

			// A SECOND HIT AT THE SAME BAND RESTARTS THE CLOCK. Being shot at again is what re-opens
			// the reply; that is also what makes repeated small strikes a real cost to the firer.
			var serialBefore = state.For(Russia).WindowSerial;
			state.ReportLaunch(America, AtomicTons);
			Assert.That(state.WindowTicksRemainingFor(Russia), Is.EqualTo(Window));
			Assert.That(state.WindowLevelFor(Russia), Is.EqualTo((int)NuclearRung.FiftyKiloton));
			Assert.That(state.For(Russia).WindowSerial, Is.EqualTo(serialBefore + 1),
				"a restart must be observable; the trait makes the granted tier fire-ready off this");

			// A BIGGER HIT RAISES THE BAND.
			state.ReportLaunch(America, W76Tons);
			Assert.That(state.PermanentLevelFor(Russia), Is.EqualTo((int)NuclearRung.HundredKiloton));
			Assert.That(state.WindowLevelFor(Russia), Is.EqualTo((int)NuclearRung.GameEnder));

			// AND A SMALLER ONE AFTERWARDS DOES NOT CUT IT DOWN. "A hit at a higher band raises the
			// window's band to the max" -- max, so a 0.3 kt shot cannot disarm a window already open
			// at the top, which would otherwise be a way to defuse a reply by firing something tiny.
			state.ReportLaunch(America, B61LowTons);
			Assert.That(state.WindowLevelFor(Russia), Is.EqualTo((int)NuclearRung.GameEnder),
				"a small hit cut down a window that was already open higher");
			Assert.That(state.PermanentLevelFor(Russia), Is.EqualTo((int)NuclearRung.HundredKiloton),
				"a permanent level fell");
		}

		[Test]
		public void FiringSmallDoesNotEscalateMuch()
		{
			// THE USER'S WHOLE ARGUMENT FOR THE MODEL, as a test. The winner holds at the loser's
			// level and absorbs small strikes; the loser attrits at its permanent level. Neither
			// side climbs unless somebody chooses to, and this is what "chooses to" means
			// arithmetically.
			var state = Released();

			// A hundred exchanges at the bottom band. Both sides keep firing 0.3 kt at each other and
			// NEITHER permanent level moves past 1 kt.
			for (var i = 0; i < 100; i++)
			{
				state.ReportLaunch(America, B61LowTons);
				state.ReportLaunch(Russia, B61LowTons);
			}

			foreach (var side in new[] { America, Russia })
			{
				Assert.That(state.PermanentLevelFor(side), Is.EqualTo((int)NuclearRung.Kiloton),
					$"side {side} climbed on small shots alone; restraint is no longer the winner's play");
				Assert.That(state.WindowLevelFor(side), Is.EqualTo((int)NuclearRung.TwentyKiloton),
					$"side {side}'s window is not one band up");
			}

			// TAKING THE WINDOW IS WHAT CLIMBS. Russia answers at 20 kt, which is the band its window
			// granted, and America is armed at 20 kt permanently with a 50 kt window in turn.
			state.ReportLaunch(Russia, AtomicTons);
			Assert.That(state.PermanentLevelFor(America), Is.EqualTo((int)NuclearRung.TwentyKiloton));
			Assert.That(state.WindowLevelFor(America), Is.EqualTo((int)NuclearRung.FiftyKiloton));

			// And Russia is still at 1 kt permanently. Firing the window did not buy the band.
			Assert.That(state.PermanentLevelFor(Russia), Is.EqualTo((int)NuclearRung.Kiloton),
				"firing inside the window raised the FIRER's permanent level");
		}

		[Test]
		public void GameEndersAreReachableOnlyThroughAWindow()
		{
			var state = Released();

			// Walk the ladder to the top the only way it can be walked: a chain of deliberate replies.
			state.ReportLaunch(America, W76Tons);
			Assert.That(state.WindowLevelFor(Russia), Is.EqualTo((int)NuclearRung.GameEnder),
				"a 100 kt hit must open a game-ender window; that is the only route to the top band");

			// NOTHING EVER WRITES GameEnder INTO A PERMANENT LEVEL. The permanent level is capped at
			// the band FIRED, and the top band is unreachable without a window, so there is no way to
			// end up holding a game-ender indefinitely.
			Assert.That(state.PermanentLevelFor(Russia), Is.EqualTo((int)NuclearRung.HundredKiloton));

			var outcome = state.ReportLaunch(Russia, SarmatRvTons);
			Assert.That(outcome.Counted, Is.True);
			Assert.That(outcome.FinalExchange, Is.True, "firing a game-ender must begin the final exchange");
			Assert.That(outcome.Band, Is.EqualTo((int)NuclearRung.GameEnder));

			// America's window is CLAMPED at the top rather than running off the end of the enum.
			Assert.That(state.WindowLevelFor(America), Is.EqualTo((int)NuclearRung.GameEnder));
			// THE CAP THAT IS AN EXPLICIT RULE RATHER THAN A CONSEQUENCE. Firing a game-ender is
			// still a launch, so rule 2 would hand America parity in kind -- a PERMANENT game-ender,
			// which is the indefinite draw card the ruling rules out. It does not matter in play
			// because the match is ending; it matters here because otherwise the invariant rests on
			// DoomsdayStrike being wired and reaching every side in time.
			Assert.That(state.PermanentLevelFor(America), Is.EqualTo((int)NuclearRung.HundredKiloton),
				"a game-ender hit wrote GameEnder into a permanent level; the draw card is back");

			// And letting the window lapse takes it away rather than banking it.
			for (var i = 0; i < Window; i++)
				state.TickWindows();

			Assert.That(state.ReleasedLevelFor(America), Is.EqualTo((int)NuclearRung.HundredKiloton));
		}

		[Test]
		public void NothingIsArmedBeforeReleaseOrOutsideEscalation()
		{
			// A launch before the gate opens is DROPPED WHOLE. Nothing a player can click reaches
			// here -- every nuclear power is gated on a band condition that is not granted -- but a
			// Lua scenario or a bot can, and arming a side off the back of one would let a match
			// arrive at release already escalated.
			var shut = new NuclearExchangeState(DefconGameMode.Escalation, Window);
			shut.RegisterSide(America);
			shut.RegisterSide(Russia);

			Assert.That(shut.ReportLaunch(America, W76Tons).Counted, Is.False);
			Assert.That(shut.ReleasedLevelFor(Russia), Is.EqualTo((int)NuclearRung.Hold));

			// SKIRMISH AND SANDBOX ARE STRICT NO-OPS. Skirmish is the shipped default game mode, the
			// user tests from main, and every nuclear demo scenario under tools/autotest/scenarios
			// runs in one of the two. Their bands come from NuclearUnlockClock instead.
			foreach (var mode in new[] { DefconGameMode.Skirmish, DefconGameMode.Sandbox })
			{
				var state = new NuclearExchangeState(mode, Window);
				state.RegisterSide(America);
				state.RegisterSide(Russia);

				Assert.That(state.Release(), Is.False, $"{mode} released");
				Assert.That(state.ReportLaunch(America, W76Tons).Counted, Is.False, $"{mode} counted a launch");
				Assert.That(state.ReleasedLevelFor(Russia), Is.EqualTo((int)NuclearRung.Hold));
			}
		}

		[Test]
		public void TheTsarBombaArmsNobody()
		{
			// Decision 04: "kept in code but cannot be used in game for now (keep it for sandbox)."
			// It is unreachable in Escalation through the CONDITION -- the power is gated on
			// nuclear-release-unrestricted, which is never granted there -- and this is the second
			// statement of that rule, the one a YAML edit cannot reach. Without it a Lua scenario
			// firing one would arm the other side with a game-ender and end the match.
			var state = Released();

			var outcome = state.ReportLaunch(America, TsarBombaTons);
			Assert.That(outcome.Counted, Is.False, "a 50 Mt warhead armed the other side");
			Assert.That(outcome.FinalExchange, Is.False);
			Assert.That(state.ReleasedLevelFor(Russia), Is.EqualTo((int)NuclearRung.Kiloton));

			// The boundary is exclusive: exactly SandboxOnlyAboveTons is still in play, which is what
			// makes this a statement about the GAP between 6 Mt and 50 Mt rather than a tuned edge.
			Assert.That(state.ReportLaunch(America, NuclearReleaseLadder.SandboxOnlyAboveTons).Counted, Is.True);
		}

		[Test]
		public void MoreThanTwoSidesArmsEveryOtherSide()
		{
			// NOT ENFORCEMENT, BY INSTRUCTION. The design is two sides (decision 15); a lobby that
			// produces three gets a warning from the trait and this arithmetic, which is the honest
			// reading of "the other side" when there is more than one of them.
			const int China = 3;

			var state = new NuclearExchangeState(DefconGameMode.Escalation, Window);
			state.RegisterSide(America);
			state.RegisterSide(Russia);
			state.RegisterSide(China);
			state.Release();

			state.ReportLaunch(America, AtomicTons);

			foreach (var side in new[] { Russia, China })
				Assert.That(state.ReleasedLevelFor(side), Is.EqualTo((int)NuclearRung.FiftyKiloton),
					$"side {side} was not armed by a launch from a third side");

			Assert.That(state.PermanentLevelFor(America), Is.EqualTo((int)NuclearRung.Kiloton));
		}

		[Test]
		public void RegisteringASideIsIdempotentAndUnknownSidesReadAsHold()
		{
			var state = Released();

			state.RegisterSide(America);
			Assert.That(state.Sides.Count, Is.EqualTo(2), "a side was registered twice");

			// A player the trait could not map to a side -- a spectator, or Neutral -- reads HOLD
			// rather than throwing. The trait's SideOf returns 0 for exactly that case.
			Assert.That(state.ReleasedLevelFor(0), Is.EqualTo((int)NuclearRung.Hold));
			Assert.That(state.WindowTicksRemainingFor(0), Is.EqualTo(0));
			Assert.That(state.For(0), Is.Null);

			// And a launch attributed to no side still arms everybody, because `key == firerSide` is
			// false for every real side. That is the safe direction: an unattributable launch must
			// not silently arm nobody.
			state.ReportLaunch(0, AtomicTons);
			foreach (var side in new[] { America, Russia })
				Assert.That(state.ReleasedLevelFor(side), Is.EqualTo((int)NuclearRung.FiftyKiloton));
		}

		[Test]
		public void ASideIsAnyCombatantAndNotOnlyALobbySlot()
		{
			// A REGRESSION TEST WITH A RUN BEHIND IT. This rule read `!NonCombatant && Playable` and
			// dropped Russia from test-nuclear-exchange, because that scenario's map.yaml wrote
			// `Playable: True` on USA and not on Russia -- and PlayerReference.Playable defaults to
			// FALSE (PlayerReference.cs:24). The run logged "NUCLEAR RELEASE: all 1 sides", every
			// Russian nuclear power stayed dark for the whole match, and DefconWall logged "derived
			// from 2 home(s) in 2 group(s)" off the same players on the same tick.
			//
			// So the assertion that matters is the NEGATIVE one: a player who is not a lobby slot is
			// still a side. Everything else here is the boundary around it.
			Assert.That(NuclearExchangeState.CountsAsASide(false, false), Is.True,
				"an ordinary combatant is a side");

			// Neutral, Creeps and the world owner. Arming them would put a phantom third side into
			// the count and make every launch warn about a lobby nobody configured.
			Assert.That(NuclearExchangeState.CountsAsASide(true, false), Is.False,
				"a non-combatant became a side");

			// A spectator has no side to be on, which is DefconWall's reasoning verbatim
			// (DefconWall.cs:294-297) -- including them would arm somebody who cannot fire.
			Assert.That(NuclearExchangeState.CountsAsASide(false, true), Is.False,
				"a spectator became a side");

			Assert.That(NuclearExchangeState.CountsAsASide(true, true), Is.False);
		}

		[Test]
		public void ThePostureScalesChargeIntervalsAndNothingElse()
		{
			// UNTUNED PLACEHOLDERS (150 / 100 / 60 %). They are pinned here because they are a brief
			// rather than a measurement, which is exactly the kind of value that gets quietly
			// "corrected" later by someone who assumes it was derived.
			Assert.That(NuclearPostureScale.Percent(NuclearPosture.Limited), Is.EqualTo(150));
			Assert.That(NuclearPostureScale.Percent(NuclearPosture.Flexible), Is.EqualTo(100));
			Assert.That(NuclearPostureScale.Percent(NuclearPosture.Massive), Is.EqualTo(60));

			// FLEXIBLE IS THE IDENTITY, not "approximately the shipped value". It is the default, so
			// any drift here would move every nuclear cooldown in the mod without anyone choosing to.
			foreach (var ticks in new[] { 1, 7, 100, 3000, 10000 })
				Assert.That(NuclearPostureScale.Apply(ticks, NuclearPosture.Flexible), Is.EqualTo(ticks));

			Assert.That(NuclearPostureScale.Apply(10000, NuclearPosture.Limited), Is.EqualTo(15000));
			Assert.That(NuclearPostureScale.Apply(10000, NuclearPosture.Massive), Is.EqualTo(6000));

			// MULTIPLY BEFORE DIVIDE. Written `ticks * (60 / 100)` the parenthesis evaluates to 0 in
			// integer arithmetic and every interval collapses to nothing -- which is not a rounding
			// error but a total loss of the value. NuclearUnlockSchedule.TicksForMinutes carries the
			// same idiom for the same reason.
			Assert.That(NuclearPostureScale.Apply(1, NuclearPosture.Massive), Is.EqualTo(0),
				"one tick at 60 % truncates to zero, which is the floor this idiom has");
			Assert.That(NuclearPostureScale.Apply(7, NuclearPosture.Massive), Is.EqualTo(4));

			// A power with no interval stays with no interval, whatever the posture.
			foreach (var posture in new[] { NuclearPosture.Limited, NuclearPosture.Flexible, NuclearPosture.Massive })
			{
				Assert.That(NuclearPostureScale.Apply(0, posture), Is.EqualTo(0));
				Assert.That(NuclearPostureScale.Apply(-5, posture), Is.EqualTo(0));
			}
		}

		[Test]
		public void TheRetaliationWindowConvertsMinutesExactly()
		{
			// The lobby offers MINUTES and the state machine counts TICKS, and the conversion is
			// NuclearUnlockSchedule.TicksForMinutes -- multiply before divide, so three minutes at the
			// mod's 60 ms timestep is exactly 3000 ticks and not the 2880 that `minutes * 60 *
			// (1000 / timestep)` produces. A window drawn as 3:00 that expires at 2:53 is the class
			// of lie this whole branch exists to remove.
			const int Timestep = 60;

			Assert.That(NuclearUnlockSchedule.TicksForMinutes(1, Timestep), Is.EqualTo(1000));
			Assert.That(NuclearUnlockSchedule.TicksForMinutes(2, Timestep), Is.EqualTo(2000));
			Assert.That(NuclearUnlockSchedule.TicksForMinutes(3, Timestep), Is.EqualTo(3000));
			Assert.That(NuclearUnlockSchedule.TicksForMinutes(5, Timestep), Is.EqualTo(5000));
			Assert.That(NuclearUnlockSchedule.TicksForMinutes(10, Timestep), Is.EqualTo(10000));

			// Every offered value converts, and the default is one of them. A value the option does
			// not define throws KeyNotFoundException on the next CLIENT JOIN, so the host sees a
			// working lobby and the next player to connect is thrown out.
			var info = new NuclearExchangeInfo();
			Assert.That(info.RetaliationWindowOptions, Is.EqualTo(new[] { 1, 2, 3, 5, 10 }));
			Assert.That(info.RetaliationWindowOptions, Does.Contain(info.RetaliationWindowDefault));
			Assert.That(info.RetaliationWindowValues().ContainsKey(info.RetaliationWindowDefault.ToString()), Is.True);

			// And the state machine honours whatever it is handed, including a one-minute window.
			var state = Released(NuclearUnlockSchedule.TicksForMinutes(1, Timestep));
			state.ReportLaunch(America, AtomicTons);
			Assert.That(state.WindowTicksRemainingFor(Russia), Is.EqualTo(1000));
		}
	}
}
