#region Copyright & License Information
/*
 * WHO MAY BE HANDED A GAME-ENDER -- the predicate the final exchange and the retaliation window
 * both ask, pinned here because getting it wrong in either direction is silent in play.
 *
 * ==== WHY THIS FIXTURE EXISTS AT ALL, AND WHAT IT CANNOT REACH ====
 * The defect it was written for was a CATCH-22 rather than a wrong number: NuclearExchange gated
 * its grant on SupportPowerInstance.Permitted, Permitted ANDs prereqsAvailable, and both shipped
 * national game-enders declare `powers.event` -- a prerequisite no faction provides. So the one
 * call that could clear the flag was behind the flag, the END band drew no cameo in a normal match,
 * and every band below it worked because each faction owns its own warhead there outright. Nothing
 * in the tree could see it: the only scenario that climbs the ladder sets PowersSandboxCheckboxEnabled,
 * which provides `powers.event` and makes the defect invisible.
 *
 * TWO HALVES OF THE RULE PULL IN OPPOSITE DIRECTIONS and that is what these tests are really for:
 * the TIER (`powers.event`) is what the override exists to bypass, and the FACTION
 * (`player.america` / `player.russia`) must survive it -- c8cadc8a, 2026-09-14. A fix written as a
 * blanket bypass passes "the ender is reachable" and hands an America player Russia's Sarmat.
 *
 * WHAT IS OUT OF REACH HERE. TechTree cannot be constructed without a World, so the HELD case --
 * "America holds player.america, so its B83 is armed" -- is not assertable from OpenRA.Test and is
 * covered by tools/autotest/scenarios/test-nuclear-ender-window instead. What IS assertable, and is
 * the half that fails DANGEROUSLY, is everything that must return FALSE: a null tech tree, an
 * unheld faction name, a yield off the top rung. Failing closed is the property with teeth, so the
 * asymmetry of this fixture is deliberate rather than a gap.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class NuclearGameEndersTest
	{
		// The one name both paths are licensed to ignore; the shipped default of
		// NuclearExchangeInfo.OverriddenPrerequisites and DoomsdayStrikeInfo.OverriddenPrerequisites.
		static readonly string[] EventTier = { "powers.event" };
		static readonly string[] NothingOverridden = System.Array.Empty<string>();

		// MissileActor carries [FieldLoader.Require], so every info below names one even when the
		// test is about the yield. `sarmatmissile` is the real actor; the value is never dereferenced.
		static MissileStrikePowerInfo Power(int tons, string prerequisites = null)
		{
			var yaml = "MissileStrikePower:\n\tMissileActor: sarmatmissile\n\tNuclearYieldTons: " + tons;
			if (prerequisites != null)
				yaml += "\n\tPrerequisites: " + prerequisites;

			var info = new MissileStrikePowerInfo();
			FieldLoader.Load(info, MiniYaml.FromString(yaml, nameof(NuclearGameEndersTest))[0].Value);
			return info;
		}

		// ---- WHICH WARHEADS ARE GAME-ENDERS ----

		[Test]
		public void TheTwoNationalEndersAreGameEnders()
		{
			// 750 kt (RS-28 Sarmat, one re-entry vehicle) and 1.2 Mt (B83-1). Both above the
			// 100 kt band ceiling, both below SandboxOnlyAboveTons.
			Assert.That(NuclearGameEnders.Is(Power(750000)), Is.True, "the Sarmat is a game-ender");
			Assert.That(NuclearGameEnders.Is(Power(1200000)), Is.True, "the B83 is a game-ender");
		}

		[Test]
		public void TheTsarBombaIsNotAGameEnder()
		{
			// 50 Mt, above NuclearReleaseLadder.SandboxOnlyAboveTons. RungForYield still calls it
			// GameEnder -- the band table has nowhere higher to put it -- so a predicate that only
			// asked the rung would hand a 50 Mt warhead out of a retaliation window and undo
			// decision 04. THIS IS THE TEST THAT SEPARATES THE TWO QUESTIONS.
			Assert.That(NuclearReleaseLadder.RungForYield(50000000), Is.EqualTo((int)NuclearRung.GameEnder),
				"the premise of this test has changed: the ladder no longer puts 50 Mt on the top rung");
			Assert.That(NuclearGameEnders.Is(Power(50000000)), Is.False,
				"the Tsar Bomba must be excluded by SandboxOnlyAboveTons, not merely by its condition");
		}

		[Test]
		public void NothingBelowTheTopRungIsAGameEnder()
		{
			// 100 kt is the HundredKiloton band's ceiling and is the largest weapon that is NOT one.
			Assert.That(NuclearGameEnders.Is(Power(100000)), Is.False, "100 kt is the band below");
			Assert.That(NuclearGameEnders.Is(Power(20000)), Is.False, "20 kt is the tactical nuke");
			Assert.That(NuclearGameEnders.Is(Power(300)), Is.False, "0.3 kt is the bottom rung");
		}

		[Test]
		public void AConventionalPowerIsNotAGameEnder()
		{
			// NuclearYieldTons defaults to 0, which is how every conventional strike in the mod
			// declares itself non-nuclear. A predicate reading the rung alone would map 0 to Hold
			// rather than to GameEnder and pass by luck; this pins the explicit guard.
			Assert.That(NuclearGameEnders.Is(Power(0)), Is.False, "a zero yield is not nuclear at all");
		}

		// ---- WHO OWNS ONE ----

		[Test]
		public void AnOverriddenPrerequisiteIsTheOnlyOneThatMayBeMissing()
		{
			// The shipped shape of the unowned 6 Mt strategic strike: `powers.event` and nothing
			// else. Everything it requires is licensed, so it is owned by every side -- which is
			// what DoomsdayStrike.ArmGameEnders already does in the final exchange, and now what
			// the END window does too. A NULL TechTree is deliberate: this must not need one.
			var strategic = Power(6000000, "powers.event");
			Assert.That(NuclearGameEnders.OwnedByFaction(null, strategic, EventTier), Is.True,
				"a power whose every prerequisite is overridden needs no tech tree to own it");
		}

		[Test]
		public void TheFactionHalfSurvivesTheOverride()
		{
			// THE LOAD-BEARING TEST. The B83 declares `powers.event, player.america`; the override
			// licenses the first and the second must still be held. With no tech tree to hold it,
			// the answer is NO -- a blanket bypass would answer YES here and would be how a Russian
			// player ends up looking at an American warhead.
			var b83 = Power(1200000, "powers.event, player.america");
			Assert.That(NuclearGameEnders.OwnedByFaction(null, b83, EventTier), Is.False,
				"overriding `powers.event` must not also override the faction beside it");

			var sarmat = Power(750000, "powers.event, player.russia");
			Assert.That(NuclearGameEnders.OwnedByFaction(null, sarmat, EventTier), Is.False,
				"the Sarmat's half of the same rule");
		}

		[Test]
		public void AnEmptyOverrideListIsTheStrictSetting()
		{
			// Emptying the field must make the caller respect every prerequisite rather than none.
			// It is the documented revert path for both traits, so the polarity is worth pinning:
			// read the other way round, clearing the field would arm everybody.
			var strategic = Power(6000000, "powers.event");
			Assert.That(NuclearGameEnders.OwnedByFaction(null, strategic, NothingOverridden), Is.False,
				"an empty override list must license nothing, not everything");
			Assert.That(NuclearGameEnders.OwnedByFaction(null, strategic, null), Is.False,
				"and a null one must behave the same as an empty one");
		}

		[Test]
		public void APowerWithNoPrerequisitesIsOwnedByEveryone()
		{
			// Nothing to hold, so nothing to check, and no tech tree needed to say so.
			Assert.That(NuclearGameEnders.OwnedByFaction(null, Power(1200000), EventTier), Is.True,
				"a power that declares no prerequisites is owned outright");
		}
	}
}
