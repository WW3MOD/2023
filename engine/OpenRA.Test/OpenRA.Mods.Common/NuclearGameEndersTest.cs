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
 * AND A THIRD RULE ARRIVED THE SAME DAY: "NATIONAL ENDER ONLY" (user ruling, 2026-09-14). Each side
 * gets exactly one END cameo, so a top-rung power that names NO owner -- the 6 Mt
 * MissileStrikePower@HighYieldNuke, `powers.event` and nothing else -- is armed by nobody, in the
 * final exchange as well as in the window. NamesAnOwner is where that rule lives and it is
 * deliberately a separate public member: it is the one part of ArmableBy a test can drive in BOTH
 * directions without a World, which is what keeps this fixture from being all-negative.
 *
 * WHAT IS OUT OF REACH HERE. TechTree cannot be constructed without a World, so the HELD case --
 * "America holds player.america, so its B83 is armed" -- is not assertable from OpenRA.Test and is
 * covered by tools/autotest/scenarios/test-nuclear-ender-window instead. What IS assertable of
 * ArmableBy, and is the half that fails DANGEROUSLY, is everything that must return FALSE: a null
 * tech tree, an unheld faction name, an unattributed power, a yield off the top rung. Failing
 * closed is the property with teeth, so the asymmetry of THAT half is deliberate rather than a gap
 * -- and NamesAnOwner beside it is what stops `ArmableBy => false` passing the whole file.
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
		public void TheTwoNationalEndersNameAnOwner()
		{
			// THE POSITIVE HALF OF THE 2026-09-14 RULING, and the reason NamesAnOwner is public:
			// every ArmableBy assertion below is a FALSE, so without this pair the whole fixture
			// would pass against `ArmableBy => false`.
			Assert.That(NuclearGameEnders.NamesAnOwner(Power(1200000, "powers.event, player.america"), EventTier),
				Is.True, "the B83 names America as its owner, beside the licensed event tier");
			Assert.That(NuclearGameEnders.NamesAnOwner(Power(750000, "powers.event, player.russia"), EventTier),
				Is.True, "the Sarmat names Russia");
		}

		[Test]
		public void TheShippedEndersOwnerRequirementIsTheFactionIdentityAlone()
		{
			// ==== THE VISIBILITY GATE OF THE 2026-09-16 ENDGAME DEFECT ====
			// The user reached the final exchange as RUSSIA and reported that no cameo appeared at
			// all. Read forward from here and that is exactly what a wrong answer on this line
			// produces: ArmableBy ends in `techTree.HasPrerequisites(OwnerPrerequisites(...))`, and
			// on false DoomsdayStrike.ArmGameEnders skips the power entirely -- no MakeReady, so
			// prereqsAvailable stays false, so SupportPowerInstance.Permitted is false, so
			// `Disabled => !bank.IconVisible(Permitted)` is TRUE and SupportPowersWidget.RefreshIcons
			// (which filters on exactly that, every tick) draws NO ICON. A silent, total failure with
			// nothing in the log.
			//
			// NamesAnOwner above already pins "non-empty". THIS pins WHICH, and the difference is the
			// failure mode worth having a test for: an override list that grew `player.russia` -- the
			// hazard both OverriddenPrerequisites fields warn about in capitals -- would empty this
			// list and read as "unattributed", which fails CLOSED to no cameo rather than open to the
			// wrong warhead. That is the safe direction and it is still the reported bug.
			//
			// THE OTHER HALF IS NOT REACHABLE FROM HERE and is verified by reading: `player.russia`
			// and `player.america` are provided by ProvidesPrerequisite with a `Factions:` filter and
			// by nothing else in the mod (player.yaml:1109-1124), so a Russia player holds
			// `player.russia`. A fixture cannot build a TechTree to assert it.
			Assert.That(NuclearGameEnders.OwnerPrerequisites(Power(750000, "powers.event, player.russia"), EventTier),
				Is.EqualTo(new[] { "player.russia" }),
				"the Sarmat's arming must turn on Russia's faction identity and nothing else");

			Assert.That(NuclearGameEnders.OwnerPrerequisites(Power(1200000, "powers.event, player.america"), EventTier),
				Is.EqualTo(new[] { "player.america" }),
				"the B83's arming must turn on America's faction identity and nothing else");

			// AND THE HAZARD ITSELF, stated as a test rather than only as a comment: licensing the
			// faction name empties the list, and an ender nobody owns is armed by nobody.
			Assert.That(NuclearGameEnders.OwnerPrerequisites(
					Power(750000, "powers.event, player.russia"), new[] { "powers.event", "player.russia" }),
				Is.Empty,
				"licensing the faction name left an owner behind, so the override would hand the "
				+ "Sarmat to an America player instead of failing closed");
		}

		[Test]
		public void AnUnattributedEnderNamesNobody()
		{
			// "NATIONAL ENDER ONLY" (user ruling, 2026-09-14). The 6 Mt strategic strike declares
			// `powers.event` and nothing else (player.yaml:842), so once the event tier is licensed
			// there is no owner left -- and an ender nobody owns is armed by nobody. Before the
			// ruling this same shape read as "owned by everyone" and put a second END cameo in both
			// columns. THE POLARITY OF THE EMPTY CASE IS THE WHOLE CHANGE.
			var strategic = Power(6000000, "powers.event");
			Assert.That(NuclearGameEnders.NamesAnOwner(strategic, EventTier), Is.False,
				"licensing `powers.event` must leave this power with no owner at all");
			Assert.That(NuclearGameEnders.ArmableBy(null, strategic, EventTier), Is.False,
				"and an ender with no owner must be armed by nobody, tech tree or not");
		}

		[Test]
		public void APowerWithNoPrerequisitesIsArmableByNobody()
		{
			// The degenerate case of the same rule, and it must not short-circuit to `true` the way
			// an ordinary "does this player hold everything" test would.
			Assert.That(NuclearGameEnders.NamesAnOwner(Power(1200000), EventTier), Is.False,
				"a power that declares nothing names no owner");
			Assert.That(NuclearGameEnders.ArmableBy(null, Power(1200000), EventTier), Is.False,
				"a power that declares nothing is armed by nobody");
		}

		[Test]
		public void TheFactionHalfSurvivesTheOverride()
		{
			// THE LOAD-BEARING TEST. The B83 declares `powers.event, player.america`; the override
			// licenses the first and the second must still be held. With no tech tree to hold it,
			// the answer is NO -- a blanket bypass would answer YES here and would be how a Russian
			// player ends up looking at an American warhead.
			var b83 = Power(1200000, "powers.event, player.america");
			Assert.That(NuclearGameEnders.ArmableBy(null, b83, EventTier), Is.False,
				"overriding `powers.event` must not also override the faction beside it");

			var sarmat = Power(750000, "powers.event, player.russia");
			Assert.That(NuclearGameEnders.ArmableBy(null, sarmat, EventTier), Is.False,
				"the Sarmat's half of the same rule");
		}

		[Test]
		public void AnEmptyOverrideListIsTheStrictSetting()
		{
			// Emptying the field must make the caller respect every prerequisite rather than none.
			// It is the documented revert path for both traits, so the polarity is worth pinning:
			// read the other way round, clearing the field would arm everybody.
			var strategic = Power(6000000, "powers.event");
			Assert.That(NuclearGameEnders.NamesAnOwner(strategic, NothingOverridden), Is.True,
				"with nothing licensed, `powers.event` is itself an owner name that must be held");
			Assert.That(NuclearGameEnders.ArmableBy(null, strategic, NothingOverridden), Is.False,
				"an empty override list must license nothing, not everything");
			Assert.That(NuclearGameEnders.ArmableBy(null, strategic, null), Is.False,
				"and a null one must behave the same as an empty one");
		}

		[Test]
		public void NothingBelowTheTopRungIsArmableHoweverWellAttributed()
		{
			// ArmableBy asks Is() first, so a perfectly attributed 100 kt warhead is still not
			// something either path may hand out. Without this the yield rule could be dropped from
			// ArmableBy and every other test here would stay green.
			Assert.That(NuclearGameEnders.NamesAnOwner(Power(100000, "powers.america"), EventTier), Is.True,
				"the premise: the W76 does name an owner");
			Assert.That(NuclearGameEnders.ArmableBy(null, Power(100000, "powers.america"), EventTier), Is.False,
				"but it is band 4, and only game-enders are handed out");
		}
	}
}
