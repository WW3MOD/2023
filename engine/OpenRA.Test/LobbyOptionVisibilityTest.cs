#region Copyright & License Information
/*
 * WHICH LOBBY ROWS THE GAME MODE IS ALLOWED TO HIDE.
 *
 * User, 2026-09-15: "we can enable/disable specific nukes even in Escalation mode, which is not
 * necessary to even show in Escalation." The fix is a filter in LobbyOptionsLogic.RebuildOptions,
 * and the whole of its judgement is one pure predicate -- which is what this fixture pins.
 *
 * IT IS A PREDICATE ONLY, AND THAT IS THE POINT. Hiding a row must never write an option: every id
 * below stays registered by LobbyCommands.LoadMapSettings with its shipped default whatever this
 * says, so a host who flips to Escalation and back finds their Skirmish settings exactly as they
 * left them. A test that asserted a VALUE had changed would be pinning the bug.
 *
 * THE DIRECTION OF THE UNKNOWN-MODE CASE IS LOAD-BEARING. An unrecognised mode shows everything
 * rather than hiding everything: a lobby that cannot tell which mode it is in must not silently
 * withhold a control the host needs. The last two cases below are what stop that inverting.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets.Logic;

namespace OpenRA.Test
{
	[TestFixture]
	public class LobbyOptionVisibilityTest
	{
		const string Escalation = "escalation";
		const string Skirmish = "skirmish";
		const string Sandbox = "sandbox";

		// The four tier checkboxes plus the clock that decides when they come up for sale. Named by
		// constant, not by literal, so a renamed id cannot leave this fixture green against an option
		// the lobby no longer has.
		static readonly string[] SkirmishShop =
		{
			NuclearUnlockClockInfo.IntervalOptionId,
			NuclearUnlockClockInfo.KilotonPurchasableOptionId,
			NuclearUnlockClockInfo.TwentyKilotonPurchasableOptionId,
			NuclearUnlockClockInfo.FiftyKilotonPurchasableOptionId,
			NuclearUnlockClockInfo.HundredKilotonPurchasableOptionId,
		};

		static readonly string[] WeaponGates = { "tactical-nuke", "high-yield-nuke", "nuclear-arsenal" };

		[Test]
		public void EscalationHidesTheSkirmishNuclearShop()
		{
			// NuclearUnlockClock.cs:319 switches the clock off outright in Escalation
			// (`mode != DefconGameMode.Escalation`), and the four checkboxes say so in their own
			// generated description: "Ignored in Escalation, where nothing nuclear is purchasable".
			foreach (var id in SkirmishShop)
				Assert.That(LobbyOptionsLogic.OptionVisibleInMode(id, Escalation), Is.False,
					$"{id} governs nothing in Escalation and must not draw a row there.");
		}

		[Test]
		public void EscalationHidesThePerWeaponGates()
		{
			foreach (var id in WeaponGates)
				Assert.That(LobbyOptionsLogic.OptionVisibleInMode(id, Escalation), Is.False,
					$"{id} gates a powers.event weapon no faction provides; it is noise in Escalation.");
		}

		[Test]
		public void SkirmishAndSandboxKeepTheShopAndTheGates()
		{
			foreach (var mode in new[] { Skirmish, Sandbox })
			{
				foreach (var id in SkirmishShop)
					Assert.That(LobbyOptionsLogic.OptionVisibleInMode(id, mode), Is.True,
						$"{id} is live in {mode} -- NuclearUnlockClock.Active only excludes Escalation.");

				foreach (var id in WeaponGates)
					Assert.That(LobbyOptionsLogic.OptionVisibleInMode(id, mode), Is.True,
						$"{id} is the sandbox's own switch and must stay reachable in {mode}.");
			}
		}

		[Test]
		public void TheEscalationPhaseControlsAreHiddenOutsideEscalation()
		{
			// NuclearReleaseLadder.cs:199 returns immediately when the mode is not Escalation, and
			// NuclearExchange's own Desc calls itself a "STRICT NO-OP OUTSIDE Escalation" -- so these
			// four are inert in BOTH of the other modes, not merely in Skirmish.
			var escalationOnly = new[]
			{
				DefconEscalationInfo.NoRushOptionId,
				DefconEscalationInfo.FirstWarheadsOptionId,
				NuclearExchangeInfo.PostureOptionId,
				NuclearExchangeInfo.RetaliationWindowOptionId,
			};

			foreach (var id in escalationOnly)
			{
				Assert.That(LobbyOptionsLogic.OptionVisibleInMode(id, Escalation), Is.True,
					$"{id} is an Escalation control and must draw in Escalation.");

				foreach (var mode in new[] { Skirmish, Sandbox })
					Assert.That(LobbyOptionsLogic.OptionVisibleInMode(id, mode), Is.False,
						$"{id} is inert in {mode} and must not draw a row there.");
			}
		}

		[Test]
		public void TheOpeningPhaseSurvivesSandboxButNotSkirmish()
		{
			// THE ONE ROW THAT IS NOT SIMPLY "ESCALATION OR NOT". Sandbox is documented as "pinned at
			// the configured level" (DefconEscalationState.cs), so the opening phase is the level
			// Sandbox holds for the whole match and DefconWall reads it. Skirmish forces
			// Level = NoLevel whatever this dropdown says, so there it governs nothing.
			var id = DefconEscalationInfo.StartOptionId;
			Assert.That(LobbyOptionsLogic.OptionVisibleInMode(id, Escalation), Is.True);
			Assert.That(LobbyOptionsLogic.OptionVisibleInMode(id, Sandbox), Is.True,
				"Sandbox pins the match at this level, so it is the one setting Sandbox still reads.");
			Assert.That(LobbyOptionsLogic.OptionVisibleInMode(id, Skirmish), Is.False,
				"Skirmish forces NoLevel, so the opening phase governs nothing there.");
		}

		[Test]
		public void TheModeDropdownItselfIsNeverHidden()
		{
			// Hiding the control that chose the mode would make the choice irreversible in the lobby.
			foreach (var mode in new[] { Escalation, Skirmish, Sandbox })
				Assert.That(LobbyOptionsLogic.OptionVisibleInMode(DefconEscalationInfo.ModeOptionId, mode), Is.True,
					$"the game mode dropdown must stay reachable in {mode}.");
		}

		[Test]
		public void OptionsWithNoModeOpinionAlwaysDraw()
		{
			var neutral = new[] { "timelimit", DoomsdayStrikeInfo.DoomsdayOptionId, "powers-sandbox", "startingcash" };
			foreach (var mode in new[] { Escalation, Skirmish, Sandbox })
				foreach (var id in neutral)
					Assert.That(LobbyOptionsLogic.OptionVisibleInMode(id, mode), Is.True,
						$"{id} is meaningful in every mode and must draw in {mode}.");
		}

		[Test]
		public void TheModeIsMatchedCaseInsensitively()
		{
			// The wire value is lowercased at the source (DefconEscalation.cs builds its keys with
			// ToLowerInvariant) but nothing enforces that on the way back in -- a scenario's rules.yaml
			// may set it in any case, and an accidental case-sensitive compare would read as "unknown
			// mode" and quietly show every row in Escalation.
			foreach (var spelling in new[] { "Escalation", "ESCALATION", "escalation" })
				Assert.That(LobbyOptionsLogic.OptionVisibleInMode(NuclearUnlockClockInfo.IntervalOptionId, spelling), Is.False,
					$"'{spelling}' is Escalation and must hide the Skirmish shop.");
		}

		[Test]
		public void AnUnknownOrAbsentModeHidesNothing()
		{
			// The safe direction: a lobby that cannot tell which mode it is in shows the host
			// everything rather than withholding a control they may need.
			foreach (var mode in new[] { null, "", "co-op", "Escalatioon" })
			{
				Assert.That(LobbyOptionsLogic.OptionVisibleInMode(NuclearUnlockClockInfo.IntervalOptionId, mode), Is.True,
					"an unrecognised mode must not hide the Skirmish shop.");
				Assert.That(LobbyOptionsLogic.OptionVisibleInMode(DefconEscalationInfo.NoRushOptionId, mode), Is.True,
					"an unrecognised mode must not hide the Escalation clocks either.");
			}
		}

		[Test]
		public void ANullOptionIdIsVisibleRatherThanThrowing()
		{
			// RebuildOptions feeds this straight off LobbyOption.Id. Nothing is expected to yield a
			// null id, but this predicate runs over every option every rebuild and must not be the
			// thing that takes the lobby down if one ever does.
			Assert.That(LobbyOptionsLogic.OptionVisibleInMode(null, Escalation), Is.True);
		}
	}
}
