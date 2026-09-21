#region Copyright & License Information
/*
 * THE NUCLEAR EXCHANGE'S STATE MACHINE -- a permanent LEVEL ratchet plus one side-wide COOLDOWN, as
 * the user ruled it on 2026-09-15 (manager-2b944571 decision 03, spec 02).
 *
 * Every rule lives in NuclearExchangeState, a plain class with no world dependency, for the reason
 * DefconEscalationState's header gives: nothing in OpenRA.Test can construct a World, so arithmetic
 * inside a trait method is arithmetic verified by READING. This fixture is what makes that split
 * worth having, and it is the whole verification of the model -- NuclearExchange (the trait) is a
 * thin shell over this and can only be checked by playing a match.
 *
 * THE LOAD-BEARING TESTS ARE FiringDoesNotEscalateYourself, LevelsNeverFall AND
 * TheCooldownIsSideWideAndBlocksEveryBand.
 *   The first is the user's entire reason for judging the model sound -- "the aggressor hands the
 *   defender a bigger weapon and never itself", so a side that fires cannot climb by doing it.
 *   The second is the ratchet: a small shot after a big one must not cut the victim back down,
 *   which is the one arithmetic slip that would turn max() into assignment and go unnoticed in play.
 *   The third is the anti-spam rule the whole of v2 exists for -- one shot per side per cooldown,
 *   at EVERY band and for every teammate, which is what "too many nukes in flight" was about.
 *
 * WHAT WAS DELETED WITH v1 AND IS NOT COMING BACK: the retaliation window, its lapse, its restart,
 * its one-shot spend, and the four per-band regeneration clocks. Six tests went with them. See
 * NuclearExchangeState's header for why they were deleted rather than retuned.
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

		// THE SHIPPED COOLDOWNS, WHICH IS WHAT MAKES THESE TESTS ABOUT THE SHIPPED GAME. 5/7/9/12
		// minutes at the mod's 60 ms timestep; the identity is that a tick count divided by 1000 is
		// its length in minutes. Restated rather than read off the Info so a test that expects 7000
		// says 7000, and TheShippedCooldownsAreTheUsersRuling is where the two are tied together.
		static readonly int[] Cooldowns = { 5000, 7000, 9000, 12000 };

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
		static NuclearExchangeState Released(int[] cooldowns = null)
		{
			var state = new NuclearExchangeState(DefconGameMode.Escalation, cooldowns ?? Cooldowns);
			state.RegisterSide(America);
			state.RegisterSide(Russia);

			Assert.That(state.Release(), Is.True, "the fixture failed to open the release gate");
			return state;
		}

		/// <summary>Run <paramref name="ticks"/> ticks of cooldown off the clock.</summary>
		static void Advance(NuclearExchangeState state, int ticks)
		{
			for (var i = 0; i < ticks; i++)
				state.Tick();
		}

		/// <summary>
		/// <para>ONE RELEASE ORDER, END TO END: the launch, and then the escalation its warhead causes
		/// when it lands.</para>
		///
		/// <para>THE TWO ARE SEPARATE CALLS SINCE 2026-09-16 and this helper is what keeps the fixture
		/// about the RULES rather than about the seam. <see cref="NuclearExchangeState.ReportLaunch"/>
		/// charges the firer at the click; <see cref="NuclearExchangeState.ApplyEscalation"/> raises
		/// the victims at the detonation, which in a match is a whole missile flight later (user
		/// ruling: "it should happen when the nuke explodes"). Every test below that is about the
		/// ratchet is about the PAIR, so the pair has a name — and
		/// <see cref="ReportLaunchAloneEscalatesNobody"/> is what pins the seam itself.</para>
		///
		/// <para>A REFUSED LAUNCH ESCALATES NOBODY, here as in the trait, which is why the guard is on
		/// Counted rather than unconditional.</para>
		/// </summary>
		static NuclearLaunchOutcome Fire(NuclearExchangeState state, int side, int tons)
		{
			var outcome = state.ReportLaunch(side, tons);
			if (outcome.Counted)
				state.ApplyEscalation(side, outcome.Band);

			return outcome;
		}

		[Test]
		public void ReleaseGivesEverySideTheLowestBandAndNoCooldown()
		{
			var state = new NuclearExchangeState(DefconGameMode.Escalation, Cooldowns);
			state.RegisterSide(America);
			state.RegisterSide(Russia);

			foreach (var side in new[] { America, Russia })
				Assert.That(state.LevelFor(side), Is.EqualTo((int)NuclearRung.Hold),
					"a side holds nothing before the release gate opens");

			Assert.That(state.Release(), Is.True);
			Assert.That(state.Release(), Is.False, "Release must report the EDGE, so a caller may poll it");

			foreach (var side in new[] { America, Russia })
			{
				Assert.That(state.LevelFor(side), Is.EqualTo((int)NuclearRung.Kiloton),
					"release is simultaneous and symmetric: every side reaches level 1 together");
				Assert.That(state.CooldownFor(side), Is.EqualTo(0),
					"release hands out a loaded warhead, not one that is already reloading");
				Assert.That(state.MayFire(side, (int)NuclearRung.Kiloton), Is.True);
				Assert.That(state.MayFire(side, (int)NuclearRung.TwentyKiloton), Is.False,
					"release opens the LOWEST band only; nothing above it is reachable until somebody fires");
			}
		}

		[Test]
		public void FiringRaisesTheENEMYOneBandAbove()
		{
			var state = Released();

			var outcome = Fire(state, America, B61LowTons);
			Assert.That(outcome.Counted, Is.True);
			Assert.That(outcome.Band, Is.EqualTo((int)NuclearRung.Kiloton));

			// RULE 3, THE HALF THAT IS THE WHOLE DESIGN: b + 1, to the enemy, permanently.
			Assert.That(state.LevelFor(Russia), Is.EqualTo((int)NuclearRung.TwentyKiloton),
				"being hit by 1 kt must raise the victim to 20 kt");
			Assert.That(state.MayFire(Russia, (int)NuclearRung.TwentyKiloton), Is.True,
				"the victim is not on a cooldown -- it did not fire -- so its new band is fireable now");
			Assert.That(state.MayFire(Russia, (int)NuclearRung.FiftyKiloton), Is.False,
				"one band above, not two");
		}

		[Test]
		public void FiringDoesNotEscalateYourself()
		{
			// THE USER'S REASON FOR JUDGING THE MODEL SOUND, and the one assertion that separates it
			// from decision 06's shared pressure ladder, where both sides always read the same rung
			// and "going first is free" was the stated and accepted cost.
			var state = Released();

			Fire(state, America, B61LowTons);
			Assert.That(state.LevelFor(America), Is.EqualTo((int)NuclearRung.Kiloton),
				"THE FIRER CLIMBED BY FIRING. A side can only be escalated by being shot at");

			// AND A BIG SHOT ESCALATES NOBODY BUT THE ENEMY EITHER. America is put at the top rung by
			// hand and fires a 100 kt: Russia goes to END, America stays exactly where it was. A side
			// cannot buy itself a single band, at any price, ever.
			Advance(state, Cooldowns[0]);
			state.For(America).Level = (int)NuclearRung.HundredKiloton;
			Fire(state, America, W76Tons);
			Assert.That(state.LevelFor(America), Is.EqualTo((int)NuclearRung.HundredKiloton),
				"firing the biggest thing it held moved the firer's own level");
			Assert.That(state.LevelFor(Russia), Is.EqualTo((int)NuclearRung.GameEnder));
		}

		[Test]
		public void LevelsNeverFall()
		{
			// THE RATCHET. max(), not assignment -- a side hit by a 100 kt and then by a 1 kt keeps
			// END. Written as a sequence rather than as one call because the slip this catches is in
			// the SECOND launch, and a single-shot test cannot reach it.
			var state = Released();

			// THE LEVEL IS SET BY HAND RATHER THAN CLIMBED TO, and every test in this file that fires
			// above band 1 has to: rule 2 refuses a launch above the firer's own level, so a fixture
			// that just called ReportLaunch with a 100 kt would be measuring the refusal instead of
			// the ratchet. Three tests here were written that way and failed on exactly that.
			state.For(America).Level = (int)NuclearRung.HundredKiloton;

			Fire(state, America, W76Tons);
			Assert.That(state.LevelFor(Russia), Is.EqualTo((int)NuclearRung.GameEnder),
				"being hit by 100 kt reaches END -- min(b + 1, 5)");

			Advance(state, Cooldowns[3]);
			Fire(state, America, B61LowTons);
			Assert.That(state.LevelFor(Russia), Is.EqualTo((int)NuclearRung.GameEnder),
				"A SMALL SHOT CUT THE VICTIM BACK DOWN. Levels are permanent; this is assignment " +
				"where the rule says max()");
		}

		[Test]
		public void TheLadderClimbsOneStepPerExchange()
		{
			// THE WHOLE MATCH, IN ONE TEST, because the interesting property is the SHAPE of the
			// climb rather than any single step: alternating fire walks both sides up, and reaching
			// END takes a 100 kt landing on you. This is the arithmetic test-nuclear-ender-level
			// measures through the support power bin.
			var state = Released();

			Fire(state, America, B61LowTons);                 // b1 -> RU level 2
			Assert.That(state.LevelFor(Russia), Is.EqualTo((int)NuclearRung.TwentyKiloton));

			Fire(state, Russia, AtomicTons);                  // b2 -> US level 3
			Assert.That(state.LevelFor(America), Is.EqualTo((int)NuclearRung.FiftyKiloton));

			Advance(state, Cooldowns[0]);
			Fire(state, America, B61MaxTons);                 // b3 -> RU level 4
			Assert.That(state.LevelFor(Russia), Is.EqualTo((int)NuclearRung.HundredKiloton));

			Advance(state, Cooldowns[1]);
			Fire(state, Russia, W76Tons);                     // b4 -> US level 5
			Assert.That(state.LevelFor(America), Is.EqualTo((int)NuclearRung.GameEnder));

			// AND THE FIRER IS STILL WHERE IT WAS AT EACH STEP. Russia fired the 100 kt that gave
			// America END; Russia's own level is what America's 50 kt gave it and nothing more.
			Assert.That(state.LevelFor(Russia), Is.EqualTo((int)NuclearRung.HundredKiloton),
				"the side that fired the 100 kt escalated itself to END");
		}

		[Test]
		public void TheCooldownIsSideWideAndBlocksEveryBand()
		{
			// THE ANTI-SPAM RULE, AND THE WHOLE POINT OF v2. One shot silences the side's WHOLE
			// arsenal -- not the band that fired, which is what v1 did and what the user played and
			// ruled against ("too many nukes in flight").
			var state = Released();

			// AMERICA IS PUT AT LEVEL 3 BY HAND RATHER THAN BY HAVING RUSSIA FIRE A 20 KT. Both reach
			// the same level, but the second leaves RUSSIA on a 7000-tick cooldown of its own -- and
			// the enemy's cooldown is one of the two things this test is asserting is ZERO. A fixture
			// that produced the state it wanted through a launch would be asserting against its own
			// setup, which is how this test failed the first time it was written.
			state.For(America).Level = (int)NuclearRung.FiftyKiloton;

			var outcome = Fire(state, America, B61LowTons);
			Assert.That(outcome.Counted, Is.True);
			Assert.That(outcome.CooldownTicks, Is.EqualTo(Cooldowns[0]),
				"a 1 kt shot costs the 1 kt band's cooldown");
			Assert.That(state.CooldownFor(America), Is.EqualTo(Cooldowns[0]));

			// EVERY BAND, INCLUDING THE TWO THE SIDE HOLDS AND DID NOT FIRE. Under v1 both of these
			// were still loaded and the side fired three warheads inside one interval.
			foreach (var band in new[] { NuclearRung.Kiloton, NuclearRung.TwentyKiloton, NuclearRung.FiftyKiloton })
				Assert.That(state.MayFire(America, (int)band), Is.False,
					$"{band} was fireable while the side was on cooldown");

			// THE ENEMY IS UNAFFECTED. A cooldown belongs to the side that fired; a launch that
			// silenced both sides would be an entirely different game -- and it is the enemy, not the
			// firer, that is handed the bigger weapon.
			Assert.That(state.CooldownFor(Russia), Is.EqualTo(0));
			Assert.That(state.MayFire(Russia, (int)NuclearRung.TwentyKiloton), Is.True,
				"being shot at must leave the victim able to answer immediately");

			// AND A SECOND LAUNCH INSIDE IT IS REFUSED, LOUDLY. Nothing a player can click reaches
			// this -- the cooldown is on the power's own timer -- so a refusal means the two layers
			// disagree, which is why the reason is on the outcome rather than being a bare false.
			var blocked = Fire(state, America, B61LowTons);
			Assert.That(blocked.Counted, Is.False);
			Assert.That(blocked.Refusal, Is.EqualTo(NuclearLaunchRefusal.OnCooldown));
			Assert.That(blocked.IsAlarming, Is.True,
				"a launch inside a cooldown is a defect, not an ordinary no-op");
			Assert.That(state.LevelFor(Russia), Is.EqualTo((int)NuclearRung.TwentyKiloton),
				"A REFUSED LAUNCH ESCALATED THE ENEMY ANYWAY -- Russia is at 2 from the 1 kt that " +
				"landed, and the blocked second shot must not have moved it to 2 again or beyond");
		}

		[Test]
		public void ALevelRiseLeavesTheVICTIMReadyNowAndAReloadingSideWaitingExactly()
		{
			// ==== THE NUMBER THE GRANT PATH READS, AND THE ONE IT GOT WRONG ON 2026-09-15 ====
			// NuclearExchange.MakeBandsReady arms a newly granted band and then leaves it carrying
			// `state.CooldownFor(side)`. WHICH side is the whole question, and both answers look
			// plausible at the call site: the launch that caused the rise belongs to the FIRER, and
			// the band being armed belongs to the VICTIM. This pins that the victim owes nothing.

			// (a) THE VICTIM IS READY ON THE TICK ITS LEVEL RISES. It did not fire, so it is on no
			// cooldown, and the band it was just handed is fireable immediately -- which is the
			// entire point of being escalated. A victim handed the AGGRESSOR's lockout would be
			// punished for being shot at.
			var state = Released();
			Fire(state, America, B61LowTons);

			Assert.That(state.LevelFor(Russia), Is.EqualTo((int)NuclearRung.TwentyKiloton));
			Assert.That(state.CooldownFor(Russia), Is.EqualTo(0),
				"THE VICTIM WAS HANDED A COOLDOWN BY BEING SHOT AT. A cooldown belongs to the side " +
				"that fired; this is the value a newly granted power is left carrying");
			Assert.That(state.MayFire(Russia, (int)NuclearRung.TwentyKiloton), Is.True,
				"the band a rise just granted must be fireable on the tick it arrives");

			// (b) A SIDE ESCALATED WHILE RELOADING OWES EXACTLY ITS OWN REMAINING COOLDOWN -- not the
			// full interval, not the firer's, and not zero. Zero is the one that matters: it is what
			// MakeReady leaves behind, and a grant that forgot to correct it would hand a reloading
			// side a free shot at a bigger band. Being shot at while reloading is the ORDINARY case
			// in this model, not an edge one.
			const int Elapsed = 120;

			var both = Released();
			Fire(both, Russia, B61LowTons);              // Russia now owes Cooldowns[0]
			Assert.That(both.CooldownFor(Russia), Is.EqualTo(Cooldowns[0]));

			Advance(both, Elapsed);
			Fire(both, America, B61LowTons);             // ...and is escalated mid-cooldown

			Assert.That(both.LevelFor(Russia), Is.EqualTo((int)NuclearRung.TwentyKiloton),
				"the rise must land whether or not the side is reloading");
			Assert.That(both.CooldownFor(Russia), Is.EqualTo(Cooldowns[0] - Elapsed),
				"being escalated RESTARTED or CLEARED the victim's own cooldown. A launch moves the " +
				"other side's LEVEL and nothing else about it");
			Assert.That(both.MayFire(Russia, (int)NuclearRung.TwentyKiloton), Is.False,
				"A SIDE FIRED THE BAND IT WAS JUST GRANTED WHILE STILL RELOADING. The level and the " +
				"cooldown are separate gates and both have to be open");

			// ...and it becomes fireable exactly when its own cooldown ends, not a tick either side.
			Advance(both, Cooldowns[0] - Elapsed - 1);
			Assert.That(both.MayFire(Russia, (int)NuclearRung.TwentyKiloton), Is.False);
			both.Tick();
			Assert.That(both.MayFire(Russia, (int)NuclearRung.TwentyKiloton), Is.True);
		}

		[Test]
		public void AGrantIsFinishedOnlyWhenEVERYPowerInRangeIsArmed()
		{
			// THE PREDICATE THE RETRY BUDGET KEYS OFF, AND THE REGRESSION IT PINS. On 2026-09-15 this
			// was `armed > 0` inside NuclearExchange.MakeBandsReady, and three scenarios failed the
			// same way: the range contained a band the side had held since release, that band armed on
			// the first tick, the request reported itself finished, and the band the rise had actually
			// granted -- still one tick behind its condition -- was never looked at again.
			Assert.That(NuclearExchangeState.GrantSatisfied(2, 1), Is.False,
				"ONE OF TWO IS NOT DONE. This exact call returning true is the 2026-09-15 defect: a " +
				"newly granted cameo left counting down its own interval on a side that fired nothing");

			Assert.That(NuclearExchangeState.GrantSatisfied(2, 2), Is.True);
			Assert.That(NuclearExchangeState.GrantSatisfied(1, 0), Is.False);
			Assert.That(NuclearExchangeState.GrantSatisfied(1, 1), Is.True);

			// NOTHING IN RANGE IS FINISHED, NOT PENDING. A side with no power at the granted band --
			// a stripped arsenal, or a faction that simply has none there -- must not burn the whole
			// retry budget waiting for a power that does not exist.
			Assert.That(NuclearExchangeState.GrantSatisfied(0, 0), Is.True);

			// And it cannot be tripped into false by a caller that over-counts.
			Assert.That(NuclearExchangeState.GrantSatisfied(1, 2), Is.True);
		}

		[Test]
		public void TheCooldownRunsOutAfterExactlyItsOwnLength()
		{
			// N TICKS MEANS N TICKS, not N +/- 1. The same decrement idiom DefconEscalationState and
			// NuclearReleaseLadder both use, and the same off-by-one it is written that way to avoid:
			// a cooldown drawn as 5:00 that ends at 4:59 is the class of lie the readout work removed.
			var state = Released();
			Fire(state, America, B61LowTons);

			Advance(state, Cooldowns[0] - 1);
			Assert.That(state.CooldownFor(America), Is.EqualTo(1));
			Assert.That(state.MayFire(America, (int)NuclearRung.Kiloton), Is.False,
				"one tick short of the end is still inside the cooldown");

			var expired = state.Tick();
			Assert.That(expired, Is.Not.Null, "Tick must REPORT the side whose cooldown just ended");
			Assert.That(expired, Does.Contain(America));
			Assert.That(state.CooldownFor(America), Is.EqualTo(0));
			Assert.That(state.MayFire(America, (int)NuclearRung.Kiloton), Is.True);

			// ONCE, ON THE EDGE. A caller announcing the recovery must not be told about it again on
			// every subsequent tick.
			Assert.That(state.Tick(), Is.Null, "the expiry was reported twice");
		}

		[Test]
		public void ABiggerShotCostsALongerCooldown()
		{
			// THE PRICE RISES WITH THE BAND, which is what makes a 1 kt a cheap probe and a 100 kt a
			// commitment. Asserted through ReportLaunch rather than off the table, because the table
			// is only half the rule -- the other half is that ReportLaunch picks the entry for the
			// band it was HANDED rather than for the side's level.
			foreach (var (tons, expected) in new[]
			{
				(B61LowTons, Cooldowns[0]),
				(AtomicTons, Cooldowns[1]),
				(B61MaxTons, Cooldowns[2]),
				(W76Tons, Cooldowns[3]),
			})
			{
				var state = Released();

				// Hand the firer the level it needs by having the OTHER side fire the band below.
				state.For(America).Level = NuclearReleaseLadder.Highest;

				var outcome = Fire(state, America, tons);
				Assert.That(outcome.Counted, Is.True, $"a {tons} t launch was refused at the top level");
				Assert.That(outcome.CooldownTicks, Is.EqualTo(expected), $"{tons} t bought the wrong cooldown");
				Assert.That(state.CooldownFor(America), Is.EqualTo(expected));
			}
		}

		[Test]
		public void AGameEnderTakesNoCooldownAndOpensTheFinalExchange()
		{
			var state = Released();
			state.For(America).Level = (int)NuclearRung.GameEnder;

			var outcome = Fire(state, America, SarmatRvTons);
			Assert.That(outcome.Counted, Is.True);
			Assert.That(outcome.Band, Is.EqualTo((int)NuclearRung.GameEnder));
			Assert.That(outcome.FinalExchange, Is.True,
				"the trait begins DoomsdayStrike's final exchange off this flag and nothing else");

			// NO COOLDOWN AT THE TOP RUNG. The match ends on this launch, so a lockout would be a
			// number nobody lives to read -- and 0 here is not "ready again next tick" for the same
			// reason. Pinned because the natural implementation is to index the table and get 12000.
			Assert.That(outcome.CooldownTicks, Is.EqualTo(0));
			Assert.That(state.CooldownFor(America), Is.EqualTo(0));

			// The victim is capped at END and cannot go past it.
			Assert.That(state.LevelFor(Russia), Is.EqualTo((int)NuclearRung.GameEnder));
			Assert.That(state.LevelFor(Russia), Is.LessThanOrEqualTo(NuclearReleaseLadder.Highest));
		}

		[Test]
		public void FiringAboveYourLevelIsRefusedLoudly()
		{
			// UNREACHABLE BY CLICKING -- the band condition for a level a side does not hold is never
			// granted, so its cameo does not exist. That is exactly why it is worth refusing rather
			// than trusting: a Lua scenario or a future bot calling the trait directly can reach it,
			// and a warhead that lands and escalates nobody is invisible without this.
			var state = Released();

			var outcome = Fire(state, America, W76Tons);
			Assert.That(outcome.Counted, Is.False);
			Assert.That(outcome.Refusal, Is.EqualTo(NuclearLaunchRefusal.AboveLevel));
			Assert.That(outcome.Band, Is.EqualTo((int)NuclearRung.HundredKiloton),
				"the refusal must name the band that was attempted, or the log cannot say what happened");
			Assert.That(outcome.IsAlarming, Is.True);

			Assert.That(state.LevelFor(Russia), Is.EqualTo((int)NuclearRung.Kiloton),
				"A REFUSED LAUNCH ESCALATED THE ENEMY ANYWAY");
			Assert.That(state.CooldownFor(America), Is.EqualTo(0),
				"A REFUSED LAUNCH SPENT A COOLDOWN. The trait sets the side's arsenal on the " +
				"COUNTED edge only, and this is what that guard rests on");
		}

		[Test]
		public void NothingEscalatesBeforeReleaseOrOutsideEscalation()
		{
			// THE FOUR ORDINARY REFUSALS, which are no-ops rather than alarms: the trait logs the
			// two alarming ones and stays quiet about these.
			var shut = new NuclearExchangeState(DefconGameMode.Escalation, Cooldowns);
			shut.RegisterSide(America);
			shut.RegisterSide(Russia);

			var before = Fire(shut, America, AtomicTons);
			Assert.That(before.Counted, Is.False);
			Assert.That(before.Refusal, Is.EqualTo(NuclearLaunchRefusal.NotReleased));
			Assert.That(before.IsAlarming, Is.False);
			Assert.That(shut.LevelFor(Russia), Is.EqualTo((int)NuclearRung.Hold),
				"a match must not arrive at release already escalated");

			foreach (var mode in new[] { DefconGameMode.Skirmish, DefconGameMode.Sandbox })
			{
				var other = new NuclearExchangeState(mode, Cooldowns);
				other.RegisterSide(America);
				other.RegisterSide(Russia);

				Assert.That(other.Release(), Is.False, "the exchange does not exist outside Escalation");

				var outcome = Fire(other, America, AtomicTons);
				Assert.That(outcome.Counted, Is.False);
				Assert.That(outcome.Refusal, Is.EqualTo(NuclearLaunchRefusal.NotEscalation));
				Assert.That(other.LevelFor(Russia), Is.EqualTo((int)NuclearRung.Hold));
			}

			// A NON-NUCLEAR POWER. MissileStrikePower.Activate already filters on a positive yield,
			// so this is the belt to that braces.
			var state = Released();
			Assert.That(Fire(state, America, 0).Refusal, Is.EqualTo(NuclearLaunchRefusal.NotNuclear));
		}

		[Test]
		public void TheTsarBombaEscalatesNobody()
		{
			// DECISION 04: the 50 Mt warhead is unreachable in normal play, and the gate is checked
			// BEFORE the band so a Lua scenario calling the trait directly cannot hand the other side
			// END by a route that ruling closed.
			var state = Released();
			state.For(America).Level = NuclearReleaseLadder.Highest;

			var outcome = Fire(state, America, TsarBombaTons);
			Assert.That(outcome.Counted, Is.False);
			Assert.That(outcome.Refusal, Is.EqualTo(NuclearLaunchRefusal.AboveSandboxCeiling));
			Assert.That(outcome.FinalExchange, Is.False,
				"a 50 Mt warhead must not open the final exchange either");
			Assert.That(state.LevelFor(Russia), Is.EqualTo((int)NuclearRung.Kiloton));
		}

		[Test]
		public void MoreThanTwoSidesEscalatesEveryOtherSide()
		{
			// NOT ENFORCED, BY INSTRUCTION (decision 15 says two sides; the trait warns and carries
			// on). "Every OTHER side" is the honest reading of "the other side" when there is more
			// than one, and it is asserted so a three-way lobby has a defined behaviour rather than
			// an accidental one.
			const int Third = 3;

			var state = new NuclearExchangeState(DefconGameMode.Escalation, Cooldowns);
			state.RegisterSide(America);
			state.RegisterSide(Russia);
			state.RegisterSide(Third);
			state.Release();

			// Level set by hand so the 20 kt launch is permitted; see LevelsNeverFall.
			state.For(America).Level = (int)NuclearRung.TwentyKiloton;
			Fire(state, America, AtomicTons);

			foreach (var side in new[] { Russia, Third })
				Assert.That(state.LevelFor(side), Is.EqualTo((int)NuclearRung.FiftyKiloton),
					$"side {side} was not escalated by a launch it was not the firer of");

			// THE FIRER IS UNMOVED -- still the level the fixture handed it, not one step further on.
			Assert.That(state.LevelFor(America), Is.EqualTo((int)NuclearRung.TwentyKiloton));

			// AND ONLY THE FIRER PAYS, however many sides it just escalated. A cooldown that spread
			// with the escalation would silence two innocent sides off one shot.
			Assert.That(state.CooldownFor(America), Is.EqualTo(Cooldowns[1]));
			Assert.That(state.CooldownFor(Russia), Is.EqualTo(0));
			Assert.That(state.CooldownFor(Third), Is.EqualTo(0));
		}

		[Test]
		public void TheLevelSerialMarksEveryRiseAndNothingElse()
		{
			// THE ONE THING A BANNER CAN WATCH. It carries no more information than the level does --
			// levels never fall -- and it exists so the widget and NuclearExchange.ReconcileGrants
			// watch the same edge rather than two derivations of it.
			var state = new NuclearExchangeState(DefconGameMode.Escalation, Cooldowns);
			state.RegisterSide(America);
			state.RegisterSide(Russia);

			Assert.That(state.LevelSerialFor(America), Is.EqualTo(0));

			state.Release();
			var afterRelease = state.LevelSerialFor(Russia);
			Assert.That(afterRelease, Is.GreaterThan(0), "release is itself a rise, Hold -> 1 kt");

			Fire(state, America, B61LowTons);
			Assert.That(state.LevelSerialFor(Russia), Is.EqualTo(afterRelease + 1));
			Assert.That(state.LevelSerialFor(America), Is.EqualTo(afterRelease),
				"the FIRER's serial moved; only being shot at raises a level");

			// A HIT THAT CHANGES NOTHING BUMPS NOTHING. v1's window serial was restarted on every hit
			// on purpose -- being shot at again re-opened the reply -- and that is exactly the
			// behaviour v2 must NOT have: a second 1 kt against a side already at 20 kt is not news,
			// and a banner firing on it would announce an escalation that did not happen.
			Advance(state, Cooldowns[0]);
			var beforeRepeat = state.LevelSerialFor(Russia);
			Fire(state, America, B61LowTons);
			Assert.That(state.LevelSerialFor(Russia), Is.EqualTo(beforeRepeat),
				"a hit that raised no level bumped the serial anyway");
		}

		[Test]
		public void RegisteringASideIsIdempotentAndUnknownSidesReadAsHold()
		{
			var state = Released();

			state.RegisterSide(America);
			Assert.That(state.Sides.Count, Is.EqualTo(2), "RegisterSide is not idempotent");

			// AN UNKNOWN SIDE IS NOT A CRASH AND NOT A SIDE. The ledger asks about OpposingSideOf,
			// which is 0 in a one-sided match, and 0 was never registered.
			Assert.That(state.LevelFor(0), Is.EqualTo((int)NuclearRung.Hold));
			Assert.That(state.CooldownFor(0), Is.EqualTo(0));
			Assert.That(state.LevelSerialFor(0), Is.EqualTo(0));
			Assert.That(state.MayFire(0, (int)NuclearRung.Kiloton), Is.False);
			Assert.That(state.For(0), Is.Null);

			// A LAUNCH BY AN UNREGISTERED SIDE IS REFUSED rather than escalating everybody. It cannot
			// happen -- SideOf returns 0 only for a non-combatant, which holds no powers -- but the
			// failure mode if it did is every real side climbing off a phantom.
			var outcome = Fire(state, 0, B61LowTons);
			Assert.That(outcome.Counted, Is.False);
			Assert.That(outcome.Refusal, Is.EqualTo(NuclearLaunchRefusal.AboveLevel));
			foreach (var side in new[] { America, Russia })
				Assert.That(state.LevelFor(side), Is.EqualTo((int)NuclearRung.Kiloton));
		}

		// ==== "IS THIS PLAYER A SIDE" MOVED OUT AT THE 2026-09-15 MERGE, AND ITS TEST WENT WITH IT ====
		// `NuclearExchangeState.CountsAsASide(nonCombatant, spectating)` was deleted by main's
		// `e5a3f629`, which extracted the rule into CombatantSides so DefconWall and this trait could
		// stop disagreeing about who is in the match -- and STRENGTHENED it while doing so, because
		// the runtime Player flags do not always reflect the map's authored PlayerReference (a
		// scenario's `NonCombatant: True` could be dropped on one of Player's two constructor
		// branches, which is how an Observer was keyed as a third nuclear side).
		//
		// The fixture that used to sit here is now CombatantSidesTest, against the four-flag form.
		// It is NOT duplicated here: two fixtures asserting one predicate is how they drift, and the
		// one that moved is the one with the extra coverage.

		[Test]
		public void NothingNuclearIsPurchasableInEscalationAndEverythingElseIsUnchanged()
		{
			// THE MODE-CONDITIONAL BYPASS, which is the whole of decision 02's "in escalation mode we
			// only control the timing, nothing is purchasable". SupportPowerInstance's constructor
			// asks this: true means build the charge bank DISABLED -- which is also what drops the
			// power out of the buy tab, because SupportPowerProductionQueue filters AllItems and
			// BuildableItems on Purchasable, which is `bank.Enabled && permitted`.
			foreach (var tons in new[] { B61LowTons, AtomicTons, B61MaxTons, W76Tons, SarmatRvTons })
				Assert.That(NuclearExchangeState.IsFreeTimerPower(DefconGameMode.Escalation, tons), Is.True,
					$"a {tons} t warhead is still purchased in Escalation");

			// SKIRMISH AND SANDBOX ARE UNTOUCHED, and this is the assertion with teeth rather than the
			// one above. Skirmish is the shipped DEFAULT game mode, the user tests from main, and every
			// nuclear scenario in tools/autotest/scenarios buys its shot through the Powers queue -- so
			// a predicate that answered true here would empty the buy tab in all of them at once, with
			// no error anywhere and no cameo to notice missing.
			foreach (var mode in new[] { DefconGameMode.Skirmish, DefconGameMode.Sandbox })
				foreach (var tons in new[] { B61LowTons, AtomicTons, W76Tons, SarmatRvTons })
					Assert.That(NuclearExchangeState.IsFreeTimerPower(mode, tons), Is.False,
						$"{mode} stopped charging for a {tons} t warhead; the purchase economy must be byte-identical");

			// A CONVENTIONAL POWER IS NEVER FREE, in any mode. NuclearYieldTons is 0 for everything
			// that is not a warhead, and that is the same test MissileStrikePower.Activate uses before
			// reporting a launch at all.
			foreach (var mode in new[] { DefconGameMode.Escalation, DefconGameMode.Skirmish, DefconGameMode.Sandbox })
				Assert.That(NuclearExchangeState.IsFreeTimerPower(mode, 0), Is.False);

			// AND NEITHER IS THE TSAR BOMBA, which falls out of the mode test rather than needing its
			// own: it is unreachable in Escalation (decision 04), so the only modes it exists in are
			// the two that return false. Asserted anyway, because "falls out of" is exactly the kind
			// of reasoning that stops being true after an unrelated edit.
			Assert.That(NuclearExchangeState.IsFreeTimerPower(DefconGameMode.Escalation, TsarBombaTons), Is.False,
				"the 50 Mt warhead became a free power in Escalation");
		}

		[Test]
		public void ASideKeyPrefersTheOwningClientsTeamAndFallsBackToTheMaps()
		{
			// THE REGRESSION THIS PINS, IN ONE LINE: a map-authored player must take the MAP's team.
			// On 2026-09-14 a 2v1 scenario ran as a 3v0 and PASSED -- debug.log read
			// `Volga(1), Enemy(1), USA(1)` with Enemy authored as `Team: 2` -- because the trait
			// asked `ClientWithIndex(p.ClientIndex)` for the team, and Player.cs:191 hands every
			// player with no client of its own the HOST'S client index. Every map player therefore
			// inherited the human's lobby team and this second argument was unreachable.
			//
			// The fix is in the lookup (NuclearExchange.SideKeyFor now asks ClientInSlot), so what
			// this fixture can hold is the contract that lookup has to satisfy: 0 means "no client
			// owns this player", and 0 must yield to the map.
			Assert.That(NuclearExchangeState.SideKeyFor(0, 2, 1), Is.EqualTo(2),
				"a player no lobby client owns must take its PlayerReference team -- this is the " +
				"argument that was unreachable while the lookup returned the host's client");

			Assert.That(NuclearExchangeState.SideKeyFor(0, 1, 0), Is.EqualTo(1));

			// A REAL LOBBY CLIENT WINS, because a human or bot can be moved between teams in the
			// lobby and the map cannot know that. Asserted with the two DISAGREEING so a reading
			// that took the wrong one cannot pass.
			Assert.That(NuclearExchangeState.SideKeyFor(2, 1, 0), Is.EqualTo(2),
				"a client sitting in this player's own slot outranks the map's Team:");

			// NO TEAM ANYWHERE -> THE PLAYER IS ITS OWN SIDE, keyed on a unique negative. Positive
			// keys are team numbers and both sources are positive, so a negative can never collide
			// with one -- which is what makes a teamless free-for-all N sides rather than one.
			Assert.That(NuclearExchangeState.SideKeyFor(0, 0, 0), Is.EqualTo(-1));
			Assert.That(NuclearExchangeState.SideKeyFor(0, 0, 1), Is.EqualTo(-2));
			Assert.That(NuclearExchangeState.SideKeyFor(0, 0, 2), Is.EqualTo(-3),
				"two teamless players must not share a side key");

			// ZERO IS ABSENT, NOT A TEAM, for both sources: lobby team 0 means "no team" and
			// PlayerReference.Team defaults to 0. A negative arriving from either is absent too
			// rather than a side key in its own right -- nothing produces one today, and treating it
			// as a team would let a caller collide with the teamless keys above.
			Assert.That(NuclearExchangeState.SideKeyFor(-5, 0, 0), Is.EqualTo(-1));
			Assert.That(NuclearExchangeState.SideKeyFor(0, -5, 3), Is.EqualTo(-4));
		}

		[Test]
		public void TheShippedCooldownsAreTheUsersRuling()
		{
			// USER-RULED 2026-09-15, NOT MEASURED. 5/7/9/12 minutes -- "nukes become rare
			// punctuation; conventional play dominates" -- and pinned here for the reason every
			// placeholder in this repo is pinned: a value nobody derived is exactly the kind a later
			// reader "corrects" on the assumption that it was.
			//
			// AND THE IDENTITY THE TICK RATE RESTS ON: at the mod's 60 ms timestep a tick count
			// divided by 1000 is its length in minutes. 5000 / 1000 = 5:00. Read as 25 tps these
			// would be 3:20 / 4:40 / 6:00 / 8:00 -- the same 1.5x error this repo has made eleven
			// times, and the one that would make the whole ruling land 40 % short.
			var info = new NuclearExchangeInfo();
			var table = info.CooldownTicks();

			Assert.That(table, Is.EqualTo(Cooldowns));
			Assert.That(NuclearExchangeState.CooldownTicksFor((int)NuclearRung.Kiloton, table), Is.EqualTo(5000));
			Assert.That(NuclearExchangeState.CooldownTicksFor((int)NuclearRung.TwentyKiloton, table), Is.EqualTo(7000));
			Assert.That(NuclearExchangeState.CooldownTicksFor((int)NuclearRung.FiftyKiloton, table), Is.EqualTo(9000));
			Assert.That(NuclearExchangeState.CooldownTicksFor((int)NuclearRung.HundredKiloton, table), Is.EqualTo(12000));

			// BIGGER IS SLOWER, asserted as a shape rather than as four numbers, so a retune that
			// keeps the intent cannot fail this and one that inverts it cannot pass.
			for (var i = 1; i < table.Count; i++)
				Assert.That(table[i], Is.GreaterThan(table[i - 1]),
					"a larger warhead costs a shorter lockout than a smaller one");

			// OUT-OF-RANGE INDEXING IS CLAMPED AT BOTH ENDS. HOLD is not a band anyone fires and the
			// game-ender band takes no cooldown at all (see AGameEnderTakesNoCooldown...), but the
			// lookup must not throw for either -- NuclearBotModuleInfo's radius table documents this
			// same convention and would be the next thing to copy it.
			Assert.That(NuclearExchangeState.CooldownTicksFor((int)NuclearRung.Hold, table), Is.EqualTo(5000));
			Assert.That(NuclearExchangeState.CooldownTicksFor((int)NuclearRung.GameEnder, table), Is.EqualTo(12000));
		}

		[Test]
		public void ThePostureScalesCooldownsAndNothingElse()
		{
			// UNTUNED PLACEHOLDERS (150 / 100 / 60 %). They are pinned here because they are a brief
			// rather than a measurement, which is exactly the kind of value that gets quietly
			// "corrected" later by someone who assumes it was derived.
			Assert.That(NuclearPostureScale.Percent(NuclearPosture.Limited), Is.EqualTo(150));
			Assert.That(NuclearPostureScale.Percent(NuclearPosture.Flexible), Is.EqualTo(100));
			Assert.That(NuclearPostureScale.Percent(NuclearPosture.Massive), Is.EqualTo(60));

			// FLEXIBLE IS THE IDENTITY, not "approximately the shipped value". It is the default, so
			// any drift here would move every nuclear cooldown in the mod without anyone choosing to.
			foreach (var ticks in new[] { 1, 7, 100, 5000, 12000 })
				Assert.That(NuclearPostureScale.Apply(ticks, NuclearPosture.Flexible), Is.EqualTo(ticks));

			// THE SHIPPED TABLE AT BOTH EXTREMES, which is the range a host can actually produce:
			// 7:30 at the slowest 1 kt, 3:00 at the fastest. "At least three minutes between shots,
			// whatever the host picks" is the claim the abuse review rests on.
			Assert.That(NuclearPostureScale.Apply(5000, NuclearPosture.Limited), Is.EqualTo(7500));
			Assert.That(NuclearPostureScale.Apply(5000, NuclearPosture.Massive), Is.EqualTo(3000));
			Assert.That(NuclearPostureScale.Apply(12000, NuclearPosture.Limited), Is.EqualTo(18000));
			Assert.That(NuclearPostureScale.Apply(12000, NuclearPosture.Massive), Is.EqualTo(7200));

			// MULTIPLY BEFORE DIVIDE. Written `ticks * (60 / 100)` the parenthesis evaluates to 0 in
			// integer arithmetic and every cooldown collapses to nothing -- which is not a rounding
			// error but a total loss of the value, and here it would be the spam v2 exists to stop.
			// NuclearUnlockSchedule.TicksForMinutes carries the same idiom for the same reason.
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
		public void TheStateHonoursWhateverCooldownTableItIsHanded()
		{
			// THE POSTURE IS APPLIED ONCE, BY THE TRAIT, BEFORE THE TABLE GETS HERE -- see
			// NuclearExchange.ScaledCooldownTicks. This class must therefore apply nothing further,
			// and a second application would be 225 % at Limited. The only way to pin that from a
			// world-free fixture is to hand it a table and check the number comes back unchanged.
			var state = Released(new[] { 300, 400, 500, 600 });

			Fire(state, America, B61LowTons);
			Assert.That(state.CooldownFor(America), Is.EqualTo(300),
				"the state re-scaled a table that arrived already scaled");

			// A DEGENERATE TABLE MUST NOT THROW. The Info refuses non-positive values in
			// RulesetLoaded, but this class is constructible from a test and from any future caller,
			// and a negative cooldown would read as "ready" on the tick after firing.
			var odd = new NuclearExchangeState(DefconGameMode.Escalation, new[] { -5 });
			odd.RegisterSide(America);
			odd.RegisterSide(Russia);
			odd.Release();
			Assert.That(Fire(odd, America, B61LowTons).CooldownTicks, Is.EqualTo(0));

			var empty = new NuclearExchangeState(DefconGameMode.Escalation, null);
			empty.RegisterSide(America);
			empty.RegisterSide(Russia);
			empty.Release();
			Assert.That(Fire(empty, America, B61LowTons).Counted, Is.True);
		}

		// ==== THE LAUNCH/IMPACT SEAM (user ruling, 2026-09-16) ====================================
		// "when the enemy fires a nuke, we instantly get the level up event, but it should happen when
		// the nuke explodes, so we see the correlation between the explosion, and after only a few
		// seconds perhaps we get the message of escalation."
		//
		// The SCHEDULE lives in the trait, which no fixture can construct. What lives here is the
		// thing that made the schedule possible: that the two halves of rule 3 are separable, that
		// the firer still pays at the click, and that the ratchet is idempotent enough for an RS-28's
		// six re-entry vehicles to be harmless even if the trait's dedup ever failed.

		[Test]
		public void ReportLaunchAloneEscalatesNobody()
		{
			var state = Released();

			// The 1 kt is the only band either side holds at release, which is what an opening shot
			// actually is. The victim's serial is 1 at this point: Release() bumped it once.
			var serialBefore = state.LevelSerialFor(Russia);
			var outcome = state.ReportLaunch(America, B61LowTons);

			Assert.That(outcome.Counted, Is.True);
			Assert.That(outcome.Band, Is.EqualTo((int)NuclearRung.Kiloton));

			// THE FIRER STILL PAYS AT THE CLICK, and that half must NOT have moved with the other one:
			// the cooldown answers "may this side fire again", which is a question about the button
			// being pressed and not about the warhead arriving. A deferred cooldown would let a side
			// empty its arsenal during a flight, which is the exact spam v2 exists to stop.
			Assert.That(state.CooldownFor(America), Is.EqualTo(Cooldowns[0]),
				"the firer must be locked out at the LAUNCH, not at the impact");

			// AND THE VICTIM IS UNTOUCHED UNTIL THE WARHEAD LANDS.
			Assert.That(state.LevelFor(Russia), Is.EqualTo((int)NuclearRung.Kiloton),
				"ReportLaunch escalated the victim; the level-up must wait for the detonation");
			Assert.That(state.LevelSerialFor(Russia), Is.EqualTo(serialBefore),
				"the victim's serial moved, so a banner would have fired at the click");

			// THE DETONATION.
			Assert.That(state.ApplyEscalation(America, outcome.Band), Is.EqualTo(1),
				"ApplyEscalation reported the wrong number of sides raised");
			Assert.That(state.LevelFor(Russia), Is.EqualTo((int)NuclearRung.TwentyKiloton));
			Assert.That(state.LevelSerialFor(Russia), Is.EqualTo(serialBefore + 1));
			Assert.That(state.LevelFor(America), Is.EqualTo((int)NuclearRung.Kiloton),
				"the firer escalated itself");
		}

		[Test]
		public void SixReEntryVehiclesFromOneOrderEscalateOneRung()
		{
			// THE REASON THE ESCALATION USED TO SIT AT LAUNCH TIME, stated as a test. The RS-28 Sarmat
			// flies six independently-aimed RVs and the trait dedups them onto one record; this pins
			// the belt behind that braces -- max() against a level that never falls means even six
			// calls at the same band land one rung, so a future caller that loses the dedup degrades
			// to correct rather than to a six-rung jump.
			var state = Released();

			var serialBefore = state.LevelSerialFor(America);
			var outcome = state.ReportLaunch(Russia, B61LowTons);
			Assert.That(outcome.Counted, Is.True);
			Assert.That(outcome.Band, Is.EqualTo((int)NuclearRung.Kiloton));

			for (var rv = 0; rv < 6; rv++)
				state.ApplyEscalation(Russia, outcome.Band);

			Assert.That(state.LevelFor(America), Is.EqualTo((int)NuclearRung.TwentyKiloton),
				"one release order escalated the enemy by more than one rung");
			Assert.That(state.LevelSerialFor(America), Is.EqualTo(serialBefore + 1),
				"the serial moved more than once, so the banner would have fired six times");
		}

		// ==== DEFECT (a): THE FINAL EXCHANGE MUST CLEAR WHAT IT ARMS ==============================
		// THIS IS THE REGRESSION TEST FOR THE BUG THE USER PLAYED, and it is the one test in this file
		// that is about an interaction rather than about a rule. Recorded match, river-zeta, from the
		// user's own debug.log:
		//
		//     NUCLEAR LAUNCH: Multi0 (side 2) band 4 -> cooldown 12000      (~tick 23500)
		//     NUCLEAR LAUNCH: Multi1 (side -4) band 5 -> cooldown 0; enemy 2 level 4->5
		//     FINAL EXCHANGE opening at tick 30957, trigger Multi1.
		//
		// Side 2 was ~7500 ticks into a 12000-tick lockout when it was handed a game-ender and fifteen
		// seconds to aim it. DoomsdayStrike.ArmGameEnders made the cameo Ready; NuclearExchange
		// serviced the level rise on the next tick and put ~4500 ticks straight back on it, because
		// that is the correct rule everywhere except here. The player could not place, the window
		// expired, Dead Hand fired for them.
		//
		// WHICH SIDE IS ON COOLDOWN AT THAT MOMENT IS PURE TIMING LUCK -- the AI came off its own at
		// tick 30909 and fired 48 ticks later -- so this is not a tuning problem.

		[Test]
		public void OpeningTheFinalExchangeClearsEveryCooldownAndRaisesEverySide()
		{
			var state = Released();

			// THE RECORDED MATCH, CLIMBED THE WAY IT WAS ACTUALLY CLIMBED. Neither side can open above
			// the 1 kt, so getting a side to level 5 takes the four alternating shots the ladder is
			// made of -- and the last of them is what leaves Russia reloading. Each firer is advanced
			// clear of its own lockout before the next shot, so no step is refused.
			Fire(state, America, B61LowTons);                       // b1 -> RU level 2
			Advance(state, Cooldowns[0]);
			Fire(state, Russia, AtomicTons);                        // b2 -> US level 3
			Advance(state, Cooldowns[1]);
			Fire(state, America, B61MaxTons);                       // b3 -> RU level 4
			Advance(state, Cooldowns[2]);

			// Russia fires the 100 kt and is locked out for 12000 ticks, taking America to the top
			// rung with it. Then 7500 ticks pass and America's game-ender comes up.
			Assert.That(Fire(state, Russia, W76Tons).Counted, Is.True,
				"the fixture failed to climb the ladder");
			Advance(state, 7500);

			Assert.That(state.LevelFor(America), Is.EqualTo((int)NuclearRung.GameEnder));
			Assert.That(state.CooldownFor(America), Is.EqualTo(0));
			Assert.That(state.CooldownFor(Russia), Is.EqualTo(Cooldowns[3] - 7500));
			Assert.That(state.MayFire(Russia, (int)NuclearRung.GameEnder), Is.False,
				"the fixture failed to reproduce the reloading side");

			// America fires its game-ender. THE EXCHANGE OPENS.
			var ender = Fire(state, America, SarmatRvTons);
			Assert.That(ender.FinalExchange, Is.True, "the fixture failed to trigger the final exchange");

			Assert.That(state.OpenFinalExchange(), Is.True);

			// THE WHOLE POINT. Both sides, every band, no wait.
			foreach (var side in new[] { America, Russia })
			{
				Assert.That(state.CooldownFor(side), Is.EqualTo(0),
					$"side {side} is still on cooldown inside the final exchange");
				Assert.That(state.LevelFor(side), Is.EqualTo(NuclearReleaseLadder.Highest),
					$"side {side} is below the top rung inside the final exchange");
				Assert.That(state.MayFire(side, (int)NuclearRung.GameEnder), Is.True,
					$"side {side} cannot fire the game-ender it was just handed");
			}

			// AND THE ORDER IS ACTUALLY ACCEPTED, which is the half a level/cooldown assertion does
			// not cover: ReportLaunch is what vetoes a salvo, and both of its alarming refusals --
			// AboveLevel and OnCooldown -- were reachable here before this fix.
			var placed = state.ReportLaunch(Russia, SarmatRvTons);
			Assert.That(placed.Counted, Is.True,
				$"the reloading side's placed game-ender was refused with {placed.Refusal}");
			Assert.That(placed.FinalExchange, Is.True);
		}

		[Test]
		public void TheTimeLimitPathAlsoLicensesTheGameEnder()
		{
			// THE OTHER WAY IN, and the one with no launch anywhere in it: the clock reaches zero with
			// the Nuclear ending checkbox ticked, DoomsdayStrike arms every surviving side, and every
			// side is still at whatever rung it climbed to -- which, in a match where nobody fired, is
			// the 1 kt they were released at. ReportLaunch would then veto the placed warhead with
			// AboveLevel: armed, aimed, clicked, and refused at resolution.
			var state = Released();

			Assert.That(state.LevelFor(America), Is.EqualTo((int)NuclearRung.Kiloton));
			Assert.That(state.ReportLaunch(America, SarmatRvTons).Refusal,
				Is.EqualTo(NuclearLaunchRefusal.AboveLevel),
				"the fixture failed to reproduce the un-escalated side");

			var fresh = Released();
			Assert.That(fresh.OpenFinalExchange(), Is.True);
			Assert.That(fresh.ReportLaunch(America, SarmatRvTons).Counted, Is.True,
				"a side handed a game-ender by the time limit still cannot fire it");
		}

		[Test]
		public void OpeningTheFinalExchangeBeforeReleaseStillLicensesTheGameEnder()
		{
			// THE THIRD REFUSAL, which ReportLaunch tests BEFORE the band: a time limit that expires
			// at DEFCON 3 opens the window with the release gate still shut. NotReleased is a QUIET
			// refusal -- the caller lets the warhead fly -- so this was never fatal, but it would
			// leave the ledger reading Hold while game-enders were in the air, and a final exchange
			// IS a release by definition.
			var shut = new NuclearExchangeState(DefconGameMode.Escalation, Cooldowns);
			shut.RegisterSide(America);
			shut.RegisterSide(Russia);

			Assert.That(shut.Released, Is.False);
			Assert.That(shut.OpenFinalExchange(), Is.True);
			Assert.That(shut.Released, Is.True);
			Assert.That(shut.ReportLaunch(America, SarmatRvTons).Counted, Is.True);
		}

		[Test]
		public void OpeningTheFinalExchangeIsIdempotentAndInertOutsideEscalation()
		{
			var state = Released();

			Assert.That(state.OpenFinalExchange(), Is.True);
			Assert.That(state.OpenFinalExchange(), Is.False,
				"a second call reported a change, so a caller polling it would re-announce the ending");

			// SKIRMISH AND SANDBOX HAVE NO EXCHANGE AT ALL -- the same strict no-op Release() keeps.
			// Their ending is the same salvo with none of this state behind it, and a mode test that
			// silently raised every side to the top rung would put a ladder into a match that has none.
			foreach (var mode in new[] { DefconGameMode.Skirmish, DefconGameMode.Sandbox })
			{
				var other = new NuclearExchangeState(mode, Cooldowns);
				other.RegisterSide(America);
				other.RegisterSide(Russia);

				Assert.That(other.OpenFinalExchange(), Is.False, $"{mode} was given an exchange");
				Assert.That(other.LevelFor(America), Is.EqualTo((int)NuclearRung.Hold));
				Assert.That(other.Released, Is.False);
			}
		}
	}
}
