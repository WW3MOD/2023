#region Copyright & License Information
/*
 * The yield band table, the release gate, and the cumulative band conditions the two drive.
 *
 * WHAT THIS FIXTURE USED TO BE, AND WHY IT SHRANK. It tested decision 06's shared pressure ladder:
 * the doubling (0 -> 1 -> 3 -> 7 -> 15), the rung recovered from the low set bits, the host ceiling,
 * saturation, and "both sides always read the same rung". All of that is DELETED rather than moved,
 * because the user's ruling of 2026-09-13 (manager-2b944571 decision 01) replaced the shared counter
 * with a per-SIDE exchange in which firing arms the OTHER side. The state machine that replaced it
 * has its own fixture: NuclearExchangeStateTest.
 *
 * What survives here is the part every game mode shares -- how big a warhead is, and when nuclear
 * weapons exist at all -- and both still live in plain classes with no world dependency, for the
 * reason DefconEscalationState's header gives: nothing in OpenRA.Test can construct a World, so
 * arithmetic inside a trait method is arithmetic verified by READING.
 *
 * THE LOAD-BEARING TEST HERE IS EveryShippedYieldLandsOnTheRungTheLadderWasDrawnWith. A yield read
 * off a power's NAME rather than out of its weapon file lands a warhead one rung from where it
 * belongs, and under the exchange that is now worse than it was: the band decides not only what the
 * firer may fire but what the VICTIM is armed with.
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

		[Test]
		public void EveryShippedYieldLandsOnTheRungTheLadderWasDrawnWith()
		{
			// HOLD -> 1 kt -> 20 kt -> 50 kt -> 100 kt -> 200 kt+, checked against the real yields.
			// The 50 kt and 100 kt rungs were ONE rung until the ruling of 2026-09-10.
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

			// A non-nuclear power carries no yield at all, and must read as HOLD rather than as the
			// bottom band -- MissileStrikePower.Activate tests `NuclearYieldTons > 0` before it
			// reports anything, and this is the floor under that test.
			Assert.That(NuclearReleaseLadder.RungForYield(0), Is.EqualTo((int)NuclearRung.Hold));
			Assert.That(NuclearReleaseLadder.RungForYield(-1), Is.EqualTo((int)NuclearRung.Hold));
		}

		[Test]
		public void AnEscalationMatchIsAtHoldUntilDefconOne()
		{
			// THE RULING OF 2026-09-10, and the half of it that changes what a match feels like:
			// nuclear weapons do not exist until the shooting war does. Ticked at DEFCON 3 and 2 for
			// far longer than any shipped pace, with a ZERO delay -- so the only thing keeping the
			// gate shut here is the LEVEL, not the clock.
			var gate = new NuclearReleaseGate(DefconGameMode.Escalation, 0);

			for (var i = 0; i < 20000; i++)
			{
				Assert.That(gate.Tick(DefconEscalationState.Ceiling), Is.False, $"DEFCON 3 opened the gate on tick {i}");
				Assert.That(gate.Tick(2), Is.False, $"DEFCON 2 opened the gate on tick {i}");
			}

			Assert.That(gate.ReleaseOpen, Is.False);

			// And DEFCON 1 opens it. THIS ASSERTION IS THE POINT OF THE WHOLE RULING: the gate is the
			// only thing that can take a side off HOLD, because the exchange is armed by being fired
			// at and nothing can be fired from HOLD.
			Assert.That(gate.Tick(DefconEscalationState.Floor), Is.True, "DEFCON 1 did not open the gate");
			Assert.That(gate.ReleaseOpen, Is.True);
		}

		[Test]
		public void TheReleaseDelayIsTicksSpentAtDefconOneAndZeroIsLegal()
		{
			const int Delay = 600;

			var gate = new NuclearReleaseGate(DefconGameMode.Escalation, Delay);

			Assert.That(gate.TicksUntilRelease, Is.EqualTo(Delay), "the countdown did not start at the delay");

			// TIME ABOVE DEFCON 1 IS NOT ON THE CLOCK. A match that takes a long time to reach the
			// bottom level still owes the full delay when it gets there, which is what makes this a
			// countdown to release rather than a match timer.
			for (var i = 0; i < 5000; i++)
				gate.Tick(DefconEscalationState.Ceiling);

			Assert.That(gate.TicksUntilRelease, Is.EqualTo(Delay), "the countdown ran while above DEFCON 1");

			// Opens on the delay'th tick at DEFCON 1: not one early, not one late.
			for (var i = 0; i < Delay - 1; i++)
				Assert.That(gate.Tick(DefconEscalationState.Floor), Is.False,
					$"the gate opened on tick {i + 1} of {Delay}");

			Assert.That(gate.ReleaseOpen, Is.False, "the gate opened before its last tick");
			Assert.That(gate.Tick(DefconEscalationState.Floor), Is.True, "the gate did not open on the delay's last tick");
			Assert.That(gate.TicksUntilRelease, Is.EqualTo(0));

			// Idempotent afterwards -- Tick returns true exactly once, so a caller can log or notify
			// on it without latching anything of its own.
			for (var i = 0; i < 100; i++)
				Assert.That(gate.Tick(DefconEscalationState.Floor), Is.False, "the gate opened twice");

			// ZERO IS A LEGAL DELAY AND MEANS "IMMEDIATELY", which is an explicit part of the ruling.
			var immediate = new NuclearReleaseGate(DefconGameMode.Escalation, 0);
			Assert.That(immediate.Tick(DefconEscalationState.Floor), Is.True,
				"a delay of 0 must open the gate on the tick DEFCON 1 is reached");

			// A negative delay is clamped to 0 rather than read as an unbounded countdown. The Info
			// field refuses one in RulesetLoaded; this is the class's own floor under that.
			var negative = new NuclearReleaseGate(DefconGameMode.Escalation, -5000);
			Assert.That(negative.TicksUntilRelease, Is.EqualTo(0));
			Assert.That(negative.Tick(DefconEscalationState.Floor), Is.True);
		}

		[Test]
		public void TheGateNeverOpensOutsideEscalation()
		{
			// Skirmish and Sandbox do not have a release gate at all -- their bands come from
			// NuclearUnlockClock's purchase schedule, which no DEFCON level moves. Skirmish is the
			// SHIPPED DEFAULT game mode and the user tests from main, so this is the assertion that
			// keeps this whole feature invisible there.
			foreach (var mode in new[] { DefconGameMode.Skirmish, DefconGameMode.Sandbox })
			{
				var gate = new NuclearReleaseGate(mode, 0);
				for (var i = 0; i < 1000; i++)
					Assert.That(gate.Tick(DefconEscalationState.Floor), Is.False, $"{mode} ticked the release gate");

				Assert.That(gate.ReleaseOpen, Is.False, $"{mode} opened the release gate");
			}
		}

		[Test]
		public void TheBandConditionsAreCumulativeAndOrdered()
		{
			// The YAML face of the bands. Cumulative is the deliberate divergence from
			// GrantConditionOnDefconLevel's exclusive family — see that trait's header and this
			// one's for why five bands and thirteen consumers change the answer.
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
			}), "the top band must hold every band below it, in rung order");

			// FIVE BANDS SINCE THE SPLIT, and this count is what the YAML consumers are keyed on: an
			// entry silently missing from the dictionary leaves its weapons ungranted at every rung,
			// which reads in game as a cameo that never appears rather than as an error anywhere.
			Assert.That(atTop.Length, Is.EqualTo(5), "a yield band lost its condition");
			Assert.That(GrantConditionOnNuclearRelease.ConditionsFor((int)NuclearRung.FiftyKiloton, conditions).ToArray(),
				Is.EqualTo(new[] { "nuclear-release-1kt", "nuclear-release-20kt", "nuclear-release-50kt" }),
				"the 50 kt band must release its own band and everything below it, and NOT 100 kt");

			// Every band's set is a prefix of the next, which is what "cumulative" means and what
			// lets a weapon name only its own band.
			for (var rung = NuclearReleaseLadder.Lowest; rung < NuclearReleaseLadder.Highest; rung++)
			{
				var lower = GrantConditionOnNuclearRelease.ConditionsFor(rung, conditions).ToArray();
				var upper = GrantConditionOnNuclearRelease.ConditionsFor(rung + 1, conditions).ToArray();
				Assert.That(upper.Take(lower.Length), Is.EqualTo(lower),
					$"rung {rung + 1} does not extend rung {rung}; the family is no longer cumulative");
			}

			// The Tsar Bomba's gate is NOT one of the bands, and must never become one.
			Assert.That(conditions.Values, Does.Not.Contain(new GrantConditionOnNuclearReleaseInfo().UnrestrictedCondition),
				"the unrestricted condition became a band, which would make the Tsar Bomba reachable " +
				"by being shot at");
		}
	}
}
