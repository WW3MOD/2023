#region Copyright & License Information
/*
 * The nuclear release ladder: the doubling, the rung boundaries, the ceiling and the band table.
 *
 * Every one of those lives in NuclearReleaseLadder, a plain class with no world dependency, for the
 * same reason DefconEscalationState is one and is stated in its header: nothing in OpenRA.Test can
 * construct a World, so arithmetic inside a trait method is arithmetic verified by READING. This
 * fixture is what makes that split worth having.
 *
 * THE LOAD-BEARING TESTS HERE ARE SkirmishIsAStrictNoOp AND TheTsarBombaIsUnreachableInNormalPlay.
 * The first because Skirmish is the shipped default game mode, the user tests from main, and every
 * nuclear demo scenario under tools/autotest/scenarios runs in it — a restrictive default would
 * have switched six weapons off in demo-nuke-arsenal and reported it as nothing but a demo that
 * stopped firing. The second because it is a user ruling (decision 04) with no derivation behind
 * it, which is exactly the kind of value that gets quietly "corrected" later.
 */
#endregion

using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class NuclearReleaseLadderTest
	{
		const string Firer = "america";
		const string Victim = "russia";

		// EVERY TEST THAT IS NOT ABOUT THE GATE STARTS WITH THE GATE ALREADY OPEN, because since the
		// ruling of 2026-09-10 the ladder ships SHUT: an Escalation match sits at HOLD until DEFCON 1
		// plus a delay. A fixture that skipped this step would be asserting "the gate is closed"
		// over and over instead of testing the rung arithmetic each test is actually about.
		//
		// The zero delay is a legal shipped setting rather than a test-only door -- there is no
		// constructor that bypasses the gate, so opening it here means ticking it at DEFCON 1 exactly
		// as DefconEscalation.Tick does.
		static NuclearReleaseLadder Escalation(NuclearRung ceiling = NuclearRung.GameEnder)
		{
			var ladder = new NuclearReleaseLadder(DefconGameMode.Escalation, (int)ceiling);

			Assert.That(ladder.Tick(DefconEscalationState.Floor), Is.True,
				"a zero delay must open the ladder on the first tick at DEFCON 1");
			Assert.That(ladder.ReleaseOpen, Is.True, "the fixture failed to open the release gate");

			return ladder;
		}

		// The yields actually shipped, in tons, read out of the weapon files rather than assumed
		// from the power names. Sources are cited on each power in the two rules files; the
		// kilotons also appear in NuclearYieldTest.AllNukes, which is an independent transcription
		// of the same figures.
		const int B61LowTons = 300;          // 0.3 kt
		const int Ru9M729Tons = 1000;        // 1 kt
		const int B61MidTons = 10000;        // 10 kt
		const int AtomicTons = 20000;        // 20 kt
		const int B61MaxTons = 50000;        // 50 kt
		const int W76Tons = 100000;          // 100 kt
		const int SarmatRvTons = 750000;     // 750 kt
		const int B83Tons = 1200000;         // 1.2 Mt
		const int HighYieldTons = 6000000;   // 6 Mt
		const int TsarBombaTons = 50000000;  // 50 Mt

		[Test]
		public void PressureDoublesPerDetonation()
		{
			// THE RULE, from decision 06: "Every detonation adds to one pressure value, doubling per
			// use." Written out longhand rather than as a formula, because the sequence IS the
			// claim: each detonation is worth exactly as much as everything before it put together.
			var ladder = Escalation();
			Assert.That(ladder.Pressure, Is.EqualTo(0), "the match opens with pressure on the clock");

			var expected = new[] { 1, 3, 7, 15 };
			for (var i = 0; i < expected.Length; i++)
			{
				ladder.ReportDetonation(Firer, AtomicTons);
				Assert.That(ladder.Pressure, Is.EqualTo(expected[i]),
					$"detonation {i + 1} put the pressure at {ladder.Pressure} rather than {expected[i]}; " +
					"the increment must equal the entire running total, which is what doubling means");
			}
		}

		[Test]
		public void TheRungIsTheStartPlusTheDetonationCountAndPressureRecoversItExactly()
		{
			// Pressure is 2^n - 1, so the detonation count is recoverable from it without a
			// logarithm or any rounding. This is why the +1 is in the recurrence.
			var ladder = Escalation();
			Assert.That(ladder.StartRung, Is.EqualTo((int)NuclearRung.Kiloton));

			for (var n = 1; n <= NuclearReleaseLadder.Highest; n++)
			{
				ladder.ReportDetonation(Firer, AtomicTons);
				Assert.That(ladder.Pressure, Is.EqualTo((1 << n) - 1));
				Assert.That(ladder.Detonations, Is.EqualTo(n));

				var uncapped = ladder.StartRung + n;
				Assert.That(ladder.RungFor(Firer),
					Is.EqualTo(uncapped > NuclearReleaseLadder.Highest ? NuclearReleaseLadder.Highest : uncapped));
			}
		}

		[Test]
		public void BothSidesAlwaysReadTheSameRung()
		{
			// THE WHOLE OF DECISION 06: "both sides always read the same rung. Whoever fires, both
			// climb together and are permitted exactly the same yields." The firer is the only one
			// detonating here and the victim must still be released just as far.
			var ladder = Escalation();

			// FOUR, not three, since the 50/100 kt split: the ladder opens at 1 kt and the top rung
			// is now five, so four detonations is what pins it at the top.
			for (var i = 0; i < 4; i++)
			{
				ladder.ReportDetonation(Firer, AtomicTons);
				Assert.That(ladder.RungFor(Victim), Is.EqualTo(ladder.RungFor(Firer)),
					"the victim reads a different rung from the firer — this is the ASYMMETRIC ladder " +
					"that decision 06 rejected, and rejected on 'one number on the HUD instead of two'");
			}

			// And the accepted cost, pinned so that nobody reads it as a bug: GOING FIRST IS FREE.
			// The firer is released exactly as far as the player they just nuked. Decision 06 argued
			// and accepted this; it is the user's call, not a defect.
			Assert.That(ladder.DetonationsBy(Victim), Is.EqualTo(0), "the victim has fired nothing");
			Assert.That(ladder.RungFor(Victim), Is.EqualTo((int)NuclearRung.GameEnder),
				"opening at the 1 kt rung plus four detonations is the top of the ladder, and a " +
				"player who fired none of them must be released exactly as far as the one who did");
		}

		[Test]
		public void TheFirerIsRecordedSoTheAsymmetricVariantStaysLayerable()
		{
			// Decision 06 keeps the rejected alternative alive and requires it to remain reachable
			// "without redoing this work". The shared ladder never reads this breakdown — the test
			// above pins that it does not — but it is RECORDED, so the asymmetric variant is a
			// change to the body of RungFor and to no call site, no trait and no YAML.
			var ladder = Escalation();

			ladder.ReportDetonation(Firer, AtomicTons);
			ladder.ReportDetonation(Firer, AtomicTons);
			ladder.ReportDetonation(Victim, AtomicTons);

			Assert.That(ladder.DetonationsBy(Firer), Is.EqualTo(2));
			Assert.That(ladder.DetonationsBy(Victim), Is.EqualTo(1));
			Assert.That(ladder.Detonations, Is.EqualTo(3), "the shared counter is the total");
		}

		[Test]
		public void SkirmishIsAStrictNoOp()
		{
			// THE LOAD-BEARING ONE. Skirmish is the shipped default game mode and every nuclear demo
			// and test scenario runs in it, so a Skirmish match must permit exactly what it
			// permitted before this ladder existed: everything the lobby allows, including the Tsar
			// Bomba, from the first tick, forever.
			var ladder = new NuclearReleaseLadder(DefconGameMode.Skirmish, (int)NuclearRung.Hold);

			foreach (var tons in new[] { B61LowTons, AtomicTons, HighYieldTons, TsarBombaTons })
				Assert.That(ladder.Permits(Firer, tons), Is.True,
					$"Skirmish refused a {tons} t warhead. Every nuke demo scenario runs in Skirmish; " +
					"the symptom of this regressing is a demo that silently stops firing.");

			// Even the HOLD ceiling above cannot bite, and detonations never move anything.
			for (var i = 0; i < 50; i++)
				Assert.That(ladder.ReportDetonation(Firer, HighYieldTons), Is.False,
					"a detonation moved the ladder in Skirmish");

			Assert.That(ladder.Pressure, Is.EqualTo(0), "Skirmish accumulated pressure");
			Assert.That(ladder.RungFor(Firer), Is.EqualTo(NuclearReleaseLadder.Highest));
		}

		[Test]
		public void TheTsarBombaIsUnreachableInNormalPlayAtEveryCeiling()
		{
			// Decision 04, in the user's words: "We keep it in code but it should be disabled and
			// cannot be used in game for now (keep it for sandbox)." Checked at EVERY ceiling and
			// after enough detonations to pin the ladder at its top, because "out of normal play"
			// has to mean unreachable rather than merely expensive.
			foreach (var ceiling in new[] { NuclearRung.Hold, NuclearRung.Kiloton, NuclearRung.TwentyKiloton, NuclearRung.FiftyKiloton, NuclearRung.HundredKiloton, NuclearRung.GameEnder })
			{
				var ladder = Escalation(ceiling);
				for (var i = 0; i < 20; i++)
					ladder.ReportDetonation(Firer, HighYieldTons);

				Assert.That(ladder.Permits(Firer, TsarBombaTons), Is.False,
					$"a ceiling of {ceiling} released the Tsar Bomba after twenty detonations");
			}

			// Sandbox is the one place it lives, which is the other half of the same ruling.
			var sandbox = new NuclearReleaseLadder(DefconGameMode.Sandbox, (int)NuclearRung.GameEnder);
			Assert.That(sandbox.Permits(Firer, TsarBombaTons), Is.True, "Sandbox must still reach it");

			var skirmish = new NuclearReleaseLadder(DefconGameMode.Skirmish, (int)NuclearRung.GameEnder);
			Assert.That(skirmish.Permits(Firer, TsarBombaTons), Is.True,
				"demo-nuke-arsenal fires the Tsar Bomba and runs in Skirmish");
		}

		[Test]
		public void EveryShippedYieldLandsOnTheRungTheLadderWasDrawnWith()
		{
			// HOLD -> 1 kt -> 20 kt -> 50 kt -> 100 kt -> 200 kt+, checked against the real yields.
			// The 50 kt and 100 kt rungs were ONE rung until the ruling of 2026-09-10. This is
			// the table most likely to be quietly wrong, because a yield read off a power's NAME
			// rather than out of its weapon file lands a warhead one rung from where it belongs.
			var expected = new (int Tons, NuclearRung Rung)[]
			{
				(B61LowTons, NuclearRung.Kiloton),
				(Ru9M729Tons, NuclearRung.Kiloton),
				(B61MidTons, NuclearRung.TwentyKiloton),
				(AtomicTons, NuclearRung.TwentyKiloton),
				(B61MaxTons, NuclearRung.FiftyKiloton),
				(W76Tons, NuclearRung.HundredKiloton),
				(SarmatRvTons, NuclearRung.GameEnder),
				(B83Tons, NuclearRung.GameEnder),
				(HighYieldTons, NuclearRung.GameEnder),
			};

			foreach (var (tons, rung) in expected)
				Assert.That(NuclearReleaseLadder.RungForYield(tons), Is.EqualTo((int)rung),
					$"a {tons} t ({tons / 1000.0:0.#} kt) warhead resolved to rung " +
					$"{NuclearReleaseLadder.RungForYield(tons)} rather than {rung}");

			// The band edges are INCLUSIVE at the top, which is what puts the 1 kt 9M729 on the
			// bottom rung with the 0.3 kt B61 rather than one above it.
			Assert.That(NuclearReleaseLadder.RungForYield(NuclearReleaseLadder.KilotonBandCeilingTons),
				Is.EqualTo((int)NuclearRung.Kiloton));
			Assert.That(NuclearReleaseLadder.RungForYield(NuclearReleaseLadder.KilotonBandCeilingTons + 1),
				Is.EqualTo((int)NuclearRung.TwentyKiloton));

			// THE EDGE THE SPLIT CREATED, and the one most likely to be got wrong by a reader who
			// remembers the old combined band: 50 kt exactly is the TOP of its own rung, and 50 kt
			// plus one ton is the 100 kt rung. Before 2026-09-10 both answers were the same rung.
			Assert.That(NuclearReleaseLadder.RungForYield(NuclearReleaseLadder.FiftyKilotonBandCeilingTons),
				Is.EqualTo((int)NuclearRung.FiftyKiloton));
			Assert.That(NuclearReleaseLadder.RungForYield(NuclearReleaseLadder.FiftyKilotonBandCeilingTons + 1),
				Is.EqualTo((int)NuclearRung.HundredKiloton));

			Assert.That(NuclearReleaseLadder.RungForYield(NuclearReleaseLadder.HundredKilotonBandCeilingTons),
				Is.EqualTo((int)NuclearRung.HundredKiloton));
			Assert.That(NuclearReleaseLadder.RungForYield(NuclearReleaseLadder.HundredKilotonBandCeilingTons + 1),
				Is.EqualTo((int)NuclearRung.GameEnder));

			// The two 50 kt weapons and the two 100 kt weapons are now on DIFFERENT rungs, which is
			// the whole of the split. Asserted as a pair rather than individually because the failure
			// mode is them collapsing back onto one rung, not either one moving alone.
			Assert.That(NuclearReleaseLadder.RungForYield(B61MaxTons),
				Is.Not.EqualTo(NuclearReleaseLadder.RungForYield(W76Tons)),
				"the 50 kt and 100 kt weapons share a rung again; the 2026-09-10 split is undone");
		}

		[Test]
		public void OnceOpenTheLadderStartsAtOneKilotonAndClimbsOneRungPerDetonation()
		{
			// WHY IT DOES NOT OPEN AT HOLD, which looks like the obvious reading of
			// "HOLD -> 1 kt -> ...": the only thing that CLIMBS this ladder is a detonation, so a
			// ladder sitting at HOLD with the gate already open would permit no warhead, and no
			// detonation could occur, and the rung would never move. The gate is what leaves HOLD
			// (it is a clock), and StartRung is where it leaves HOLD to.
			//
			// Decision 06 settles the direction rather than leaving it to taste: its accepted cost
			// is that "going first is free", which presumes going first is possible.
			var ladder = Escalation();

			Assert.That(ladder.RungFor(Firer), Is.EqualTo((int)NuclearRung.Kiloton),
				"an opened Escalation ladder must sit somewhere a player can actually fire from");
			Assert.That(ladder.Permits(Firer, B61LowTons), Is.True, "the opening rung released nothing");
			Assert.That(ladder.Permits(Firer, Ru9M729Tons), Is.True);
			Assert.That(ladder.Permits(Firer, B61MidTons), Is.False,
				"the 20 kt band is open before anyone has fired; the ladder must be climbed for it");

			// One rung at a time, so the whole ladder is FIVE deep and four detonations from the
			// bottom to the top. It was four deep and three detonations before the 50/100 kt split.
			ladder.ReportDetonation(Firer, B61LowTons);
			Assert.That(ladder.Permits(Firer, B61MidTons), Is.True);
			Assert.That(ladder.Permits(Firer, B61MaxTons), Is.False, "the 50 kt band opened a rung early");

			// THE RUNG THE SPLIT ADDED. 50 kt is released here and 100 kt is not, which is a state
			// the ladder could not previously be in: these two came out together.
			ladder.ReportDetonation(Firer, B61MidTons);
			Assert.That(ladder.RungFor(Firer), Is.EqualTo((int)NuclearRung.FiftyKiloton));
			Assert.That(ladder.Permits(Firer, B61MaxTons), Is.True);
			Assert.That(ladder.Permits(Firer, W76Tons), Is.False,
				"100 kt came out with 50 kt; that is the pre-split ladder");

			ladder.ReportDetonation(Firer, B61MaxTons);
			Assert.That(ladder.Permits(Firer, W76Tons), Is.True);
			Assert.That(ladder.Permits(Firer, HighYieldTons), Is.False);

			ladder.ReportDetonation(Firer, W76Tons);
			Assert.That(ladder.Permits(Firer, HighYieldTons), Is.True, "the game-ender rung is unreachable");
		}

		[Test]
		public void AnEscalationMatchIsAtHoldUntilDefconOne()
		{
			// THE RULING OF 2026-09-10, and the half of it that changes what a match feels like:
			// nuclear weapons do not exist until the shooting war does. Ticked at DEFCON 3 and 2 for
			// far longer than any shipped pace, with a ZERO delay -- so the only thing keeping the
			// ladder shut here is the LEVEL, not the clock.
			var ladder = new NuclearReleaseLadder(DefconGameMode.Escalation, (int)NuclearRung.GameEnder);

			for (var i = 0; i < 20000; i++)
			{
				Assert.That(ladder.Tick(DefconEscalationState.Ceiling), Is.False, $"DEFCON 3 opened the ladder on tick {i}");
				Assert.That(ladder.Tick(2), Is.False, $"DEFCON 2 opened the ladder on tick {i}");
			}

			Assert.That(ladder.ReleaseOpen, Is.False);
			Assert.That(ladder.RungFor(Firer), Is.EqualTo((int)NuclearRung.Hold),
				"a match above DEFCON 1 must read HOLD, whatever the ceiling is");

			foreach (var tons in new[] { B61LowTons, Ru9M729Tons, AtomicTons, B61MaxTons, W76Tons, HighYieldTons })
				Assert.That(ladder.Permits(Firer, tons), Is.False,
					$"a {tons} t warhead was permitted before DEFCON 1; the ladder is supposed to be shut");

			// And DEFCON 1 opens it, which is what stops HOLD being the absorbing state it would
			// otherwise be. THIS ASSERTION IS THE POINT OF THE WHOLE RULING: without it the ladder
			// as drawn is unclimbable and nuclear weapons are unreachable for the entire match.
			Assert.That(ladder.Tick(DefconEscalationState.Floor), Is.True, "DEFCON 1 did not open the ladder");
			Assert.That(ladder.ReleaseOpen, Is.True);
			Assert.That(ladder.Permits(Firer, B61LowTons), Is.True);
		}

		[Test]
		public void TheReleaseDelayIsTicksSpentAtDefconOneAndZeroIsLegal()
		{
			const int Delay = 600;

			var ladder = new NuclearReleaseLadder(DefconGameMode.Escalation, (int)NuclearRung.GameEnder,
				(int)NuclearRung.Kiloton, Delay);

			Assert.That(ladder.TicksUntilRelease, Is.EqualTo(Delay), "the countdown did not start at the delay");

			// TIME ABOVE DEFCON 1 IS NOT ON THE CLOCK. A match that takes a long time to reach the
			// bottom level still owes the full delay when it gets there, which is what makes this a
			// countdown to release rather than a match timer.
			for (var i = 0; i < 5000; i++)
				ladder.Tick(DefconEscalationState.Ceiling);

			Assert.That(ladder.TicksUntilRelease, Is.EqualTo(Delay), "the countdown ran while above DEFCON 1");

			// Opens on the delay'th tick at DEFCON 1: not one early, not one late.
			for (var i = 0; i < Delay - 1; i++)
				Assert.That(ladder.Tick(DefconEscalationState.Floor), Is.False,
					$"the ladder opened on tick {i + 1} of {Delay}");

			Assert.That(ladder.ReleaseOpen, Is.False, "the ladder opened before its last tick");
			Assert.That(ladder.Tick(DefconEscalationState.Floor), Is.True, "the ladder did not open on the delay's last tick");
			Assert.That(ladder.TicksUntilRelease, Is.EqualTo(0));

			// Idempotent afterwards -- Tick returns true exactly once, so a caller can log or notify
			// on it without latching anything of its own.
			for (var i = 0; i < 100; i++)
				Assert.That(ladder.Tick(DefconEscalationState.Floor), Is.False, "the ladder opened twice");

			// ZERO IS A LEGAL DELAY AND MEANS "IMMEDIATELY", which is an explicit part of the ruling.
			var immediate = new NuclearReleaseLadder(DefconGameMode.Escalation, (int)NuclearRung.GameEnder,
				(int)NuclearRung.Kiloton, 0);

			Assert.That(immediate.Tick(DefconEscalationState.Floor), Is.True,
				"a delay of 0 must open the ladder on the tick DEFCON 1 is reached");
			Assert.That(immediate.Permits(Firer, B61LowTons), Is.True);

			// A negative delay is clamped to 0 rather than read as an unbounded countdown. The Info
			// field refuses one in RulesetLoaded; this is the class's own floor under that.
			var negative = new NuclearReleaseLadder(DefconGameMode.Escalation, (int)NuclearRung.GameEnder,
				(int)NuclearRung.Kiloton, -5000);

			Assert.That(negative.TicksUntilRelease, Is.EqualTo(0));
			Assert.That(negative.Tick(DefconEscalationState.Floor), Is.True);
		}

		[Test]
		public void ADetonationBeforeTheGateOpensDoesNotPreClimbTheLadder()
		{
			// Nothing a player can click reaches ReportDetonation while the ladder is shut, because
			// every nuclear power is gated on a band condition that is not granted. A Lua scenario or
			// a bot calling DefconEscalation.ReportNuclearRelease directly CAN, and banking those
			// would let a match arrive at DEFCON 1 with the ladder already part-climbed -- i.e. would
			// hand the first mover the top of the ladder for free at the moment it opens.
			var ladder = new NuclearReleaseLadder(DefconGameMode.Escalation, (int)NuclearRung.GameEnder);

			for (var i = 0; i < 20; i++)
				Assert.That(ladder.ReportDetonation(Firer, HighYieldTons), Is.False,
					"a detonation moved a ladder that has not opened");

			Assert.That(ladder.Pressure, Is.EqualTo(0), "pressure accumulated before the gate opened");
			Assert.That(ladder.Detonations, Is.EqualTo(0));
			Assert.That(ladder.DetonationsBy(Firer), Is.EqualTo(0), "the per-firer tally banked a pre-gate shot");

			ladder.Tick(DefconEscalationState.Floor);

			Assert.That(ladder.RungFor(Firer), Is.EqualTo((int)NuclearRung.Kiloton),
				"the ladder opened above its starting rung; twenty pre-gate detonations were banked");
			Assert.That(ladder.Permits(Firer, B61MidTons), Is.False);
		}

		[Test]
		public void TheCeilingPinsTheLadderAndHoldMeansNoNuclearWeaponsAtAll()
		{
			foreach (var ceiling in new[] { NuclearRung.Hold, NuclearRung.Kiloton, NuclearRung.TwentyKiloton, NuclearRung.FiftyKiloton, NuclearRung.HundredKiloton })
			{
				var ladder = Escalation(ceiling);
				for (var i = 0; i < 20; i++)
					ladder.ReportDetonation(Firer, B61LowTons);

				Assert.That(ladder.RungFor(Firer), Is.EqualTo((int)ceiling),
					$"twenty detonations climbed past the {ceiling} ceiling");
			}

			// HOLD is the "no nuclear weapons this match" setting, and it has to hold against the
			// smallest warhead in the arsenal as well as the largest.
			var held = Escalation(NuclearRung.Hold);
			for (var i = 0; i < 20; i++)
				held.ReportDetonation(Firer, B61LowTons);

			foreach (var tons in new[] { B61LowTons, AtomicTons, HighYieldTons, TsarBombaTons })
				Assert.That(held.Permits(Firer, tons), Is.False, $"a HOLD ceiling released a {tons} t warhead");
		}

		[Test]
		public void PressureSaturatesRatherThanOverflowing()
		{
			// A long Sandbox session or a scripted scenario can call this an unbounded number of
			// times. Doubling an int forever is how that becomes a negative pressure and a rung
			// nobody can explain.
			var ladder = Escalation();
			for (var i = 0; i < 10000; i++)
				ladder.ReportDetonation(Firer, HighYieldTons);

			Assert.That(ladder.Pressure, Is.GreaterThan(0), "the pressure went negative — it overflowed");
			Assert.That(ladder.RungFor(Firer), Is.EqualTo(NuclearReleaseLadder.Highest));
			Assert.That(ladder.Permits(Firer, TsarBombaTons), Is.False,
				"saturation must not become a back door to the one weapon that is out of play");
		}

		[Test]
		public void SandboxNeverClimbsBecauseItIsAlreadyOpen()
		{
			var ladder = new NuclearReleaseLadder(DefconGameMode.Sandbox, (int)NuclearRung.Hold);

			foreach (var tons in new[] { B61LowTons, AtomicTons, HighYieldTons, TsarBombaTons })
				Assert.That(ladder.Permits(Firer, tons), Is.True, $"Sandbox refused a {tons} t warhead");

			for (var i = 0; i < 50; i++)
				Assert.That(ladder.ReportDetonation(Firer, HighYieldTons), Is.False);

			Assert.That(ladder.Pressure, Is.EqualTo(0));
		}

		[Test]
		public void TheBandConditionsAreCumulativeAndOrdered()
		{
			// The YAML face of the ladder. Cumulative is the deliberate divergence from
			// GrantConditionOnDefconLevel's exclusive family — see that trait's header and this
			// one's for why five rungs and thirteen consumers change the answer.
			var conditions = new GrantConditionOnNuclearReleaseInfo().Conditions;

			Assert.That(GrantConditionOnNuclearRelease.ConditionsFor((int)NuclearRung.Hold, conditions),
				Is.Empty, "HOLD released a band");

			var atKiloton = GrantConditionOnNuclearRelease.ConditionsFor((int)NuclearRung.Kiloton, conditions).ToArray();
			Assert.That(atKiloton, Is.EqualTo(new[] { "nuclear-release-1kt" }));

			var atTop = GrantConditionOnNuclearRelease.ConditionsFor(NuclearReleaseLadder.Highest, conditions).ToArray();
			Assert.That(atTop, Is.EqualTo(new[]
			{
				"nuclear-release-1kt", "nuclear-release-20kt", "nuclear-release-50kt",
				"nuclear-release-100kt", "nuclear-release-gameender"
			}), "the top rung must hold every band below it, in rung order");

			// FIVE BANDS SINCE THE SPLIT, and this count is what the YAML consumers are keyed on: an
			// entry silently missing from the dictionary leaves its weapons ungranted at every rung,
			// which reads in game as a cameo that never appears rather than as an error anywhere.
			Assert.That(atTop.Length, Is.EqualTo(5), "a yield band lost its condition");
			Assert.That(GrantConditionOnNuclearRelease.ConditionsFor((int)NuclearRung.FiftyKiloton, conditions).ToArray(),
				Is.EqualTo(new[] { "nuclear-release-1kt", "nuclear-release-20kt", "nuclear-release-50kt" }),
				"the 50 kt rung must release its own band and everything below it, and NOT 100 kt");

			// Every rung's set is a prefix of the next, which is what "cumulative" means and what
			// lets a weapon name only its own band.
			for (var rung = NuclearReleaseLadder.Lowest; rung < NuclearReleaseLadder.Highest; rung++)
			{
				var lower = GrantConditionOnNuclearRelease.ConditionsFor(rung, conditions).ToArray();
				var upper = GrantConditionOnNuclearRelease.ConditionsFor(rung + 1, conditions).ToArray();
				Assert.That(upper.Take(lower.Length), Is.EqualTo(lower),
					$"rung {rung + 1} does not extend rung {rung}; the family is no longer cumulative");
			}

			// The Tsar Bomba's gate is NOT one of the rungs, and must never become one.
			Assert.That(conditions.Values, Does.Not.Contain(new GrantConditionOnNuclearReleaseInfo().UnrestrictedCondition),
				"the unrestricted condition became a ladder rung, which would make the Tsar Bomba " +
				"reachable by climbing");
		}
	}
}
