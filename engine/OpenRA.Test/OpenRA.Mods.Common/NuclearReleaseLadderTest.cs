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

		static NuclearReleaseLadder Escalation(NuclearRung ceiling = NuclearRung.GameEnder)
		{
			return new NuclearReleaseLadder(DefconGameMode.Escalation, (int)ceiling);
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
		public void TheRungIsTheDetonationCountAndPressureRecoversItExactly()
		{
			// Pressure is 2^n - 1, so the rung is recoverable from it without a logarithm or any
			// rounding. This is why the +1 is in the recurrence.
			var ladder = Escalation();
			for (var n = 1; n <= NuclearReleaseLadder.Highest; n++)
			{
				ladder.ReportDetonation(Firer, AtomicTons);
				Assert.That(ladder.Pressure, Is.EqualTo((1 << n) - 1));
				Assert.That(ladder.RungFor(Firer), Is.EqualTo(n));
			}
		}

		[Test]
		public void BothSidesAlwaysReadTheSameRung()
		{
			// THE WHOLE OF DECISION 06: "both sides always read the same rung. Whoever fires, both
			// climb together and are permitted exactly the same yields." The firer is the only one
			// detonating here and the victim must still be released just as far.
			var ladder = Escalation();

			for (var i = 0; i < 3; i++)
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
			Assert.That(ladder.RungFor(Victim), Is.EqualTo((int)NuclearRung.HundredKiloton),
				"three detonations should release the 50-100 kt band to a player who fired none of them");
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
			foreach (var ceiling in new[] { NuclearRung.Hold, NuclearRung.Kiloton, NuclearRung.TwentyKiloton, NuclearRung.HundredKiloton, NuclearRung.GameEnder })
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
			// HOLD -> 1 kt -> 20 kt -> 50-100 kt -> 200 kt+, checked against the real yields. This is
			// the table most likely to be quietly wrong, because a yield read off a power's NAME
			// rather than out of its weapon file lands a warhead one rung from where it belongs.
			var expected = new (int Tons, NuclearRung Rung)[]
			{
				(B61LowTons, NuclearRung.Kiloton),
				(Ru9M729Tons, NuclearRung.Kiloton),
				(B61MidTons, NuclearRung.TwentyKiloton),
				(AtomicTons, NuclearRung.TwentyKiloton),
				(B61MaxTons, NuclearRung.HundredKiloton),
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
			// bottom rung with the 0.3 kt B61 rather than one above it, and the 100 kt W76 with the
			// 50 kt B61Max. Both pairings are the ladder as drawn.
			Assert.That(NuclearReleaseLadder.RungForYield(NuclearReleaseLadder.KilotonBandCeilingTons),
				Is.EqualTo((int)NuclearRung.Kiloton));
			Assert.That(NuclearReleaseLadder.RungForYield(NuclearReleaseLadder.KilotonBandCeilingTons + 1),
				Is.EqualTo((int)NuclearRung.TwentyKiloton));
			Assert.That(NuclearReleaseLadder.RungForYield(NuclearReleaseLadder.HundredKilotonBandCeilingTons),
				Is.EqualTo((int)NuclearRung.HundredKiloton));
			Assert.That(NuclearReleaseLadder.RungForYield(NuclearReleaseLadder.HundredKilotonBandCeilingTons + 1),
				Is.EqualTo((int)NuclearRung.GameEnder));
		}

		[Test]
		public void TheLadderOpensAtHoldAndFirstReleasesTheKilotonBand()
		{
			// The opening state of an Escalation match: nothing nuclear is permitted until the first
			// detonation, and the first one opens the bottom rung only.
			//
			// NOTE WHAT THIS PINS AND WHAT IT DOES NOT. It pins the SHAPE — rung 0 permits nothing,
			// rung 1 permits 1 kt and not 20 kt. It does NOT settle what OPENS the ladder, because
			// neither decision 04 nor 06 says: as built, a detonation is the only thing that moves
			// it, so at rung 0 nothing on the ladder can fire and the ladder cannot be climbed from
			// inside itself. HOLD is therefore usable as a CEILING ("no nuclear weapons this match")
			// and the opening question is called out in the report rather than guessed at here.
			var ladder = Escalation();

			Assert.That(ladder.RungFor(Firer), Is.EqualTo((int)NuclearRung.Hold));
			Assert.That(ladder.Permits(Firer, B61LowTons), Is.False, "HOLD released a warhead");

			ladder.ReportDetonation(Firer, B61LowTons);
			Assert.That(ladder.Permits(Firer, B61LowTons), Is.True);
			Assert.That(ladder.Permits(Firer, B61MidTons), Is.False,
				"one detonation released the 20 kt band; the ladder must climb one rung at a time");
		}

		[Test]
		public void TheCeilingPinsTheLadderAndHoldMeansNoNuclearWeaponsAtAll()
		{
			foreach (var ceiling in new[] { NuclearRung.Hold, NuclearRung.Kiloton, NuclearRung.TwentyKiloton, NuclearRung.HundredKiloton })
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
				"nuclear-release-1kt", "nuclear-release-20kt", "nuclear-release-100kt", "nuclear-release-gameender"
			}), "the top rung must hold every band below it, in rung order");

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
