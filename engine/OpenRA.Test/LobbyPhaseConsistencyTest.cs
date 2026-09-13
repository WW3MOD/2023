#region Copyright & License Information
/*
 * THE ONE CONSISTENCY RULE LEFT BETWEEN THE LOBBY'S PHASE CLOCKS.
 *
 * Worth a fixture of its own rather than a corner of the timeline's, because it is read from TWO
 * places that cannot see each other -- the bar's ChromeLogic and LobbyOptionsLogic's tooltip
 * delegates -- and the failure it guards is silent in both. A host who sets a 10-minute time limit
 * against a 15-minute no-rush period gets a match that ends without the border ever opening, and
 * neither dropdown is wrong on its own.
 *
 * IT IS A PREDICATE ONLY. Nothing here writes an option, and that is the user's ruling rather than
 * an omission: "a window that would collapse to zero is flagged, never silently clamped". A test
 * that asserted a value had been corrected would be pinning the behaviour that was rejected.
 */
#endregion

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Network;

namespace OpenRA.Test
{
	[TestFixture]
	public class LobbyPhaseConsistencyTest
	{
		static Session.Global Lobby(params (string Id, string Value)[] options)
		{
			var global = new Session.Global { LobbyOptions = new Dictionary<string, Session.LobbyOptionState>() };
			foreach (var (id, value) in options)
				global.LobbyOptions[id] = new Session.LobbyOptionState { Value = value };

			return global;
		}

		static Session.Global Escalation(string noRushMinutes, string limitMinutes)
		{
			return Lobby(
				(LobbyPhaseConsistency.ModeOptionId, "escalation"),
				(LobbyPhaseConsistency.NoRushOptionId, noRushMinutes),
				(LobbyPhaseConsistency.TimeLimitOptionId, limitMinutes));
		}

		static Session.Global Skirmish(string intervalMinutes, string limitMinutes)
		{
			return Lobby(
				(LobbyPhaseConsistency.ModeOptionId, "skirmish"),
				(LobbyPhaseConsistency.UnlockIntervalOptionId, intervalMinutes),
				(LobbyPhaseConsistency.TimeLimitOptionId, limitMinutes));
		}

		[Test]
		public void NoTimeLimitCanNeverCollideWithAnything()
		{
			// "No limit" is the shipped default and is the wire value "0". A match that does not end
			// on a clock cannot end during a phase, in either mode.
			Assert.That(LobbyPhaseConsistency.Warning(Escalation("15", "0")), Is.Null);
			Assert.That(LobbyPhaseConsistency.Warning(Skirmish("20", "0")), Is.Null);
		}

		[Test]
		public void ATimeLimitInsideTheNoRushPeriodIsFlagged()
		{
			// Reachable from the shipped sets: `timelimit` offers 10 and `no-rush-period` offers 15.
			Assert.That(LobbyPhaseConsistency.Warning(Escalation("15", "10")),
				Is.EqualTo(LobbyPhaseConsistency.MatchEndsDuringNoRushText));

			// EQUAL COUNTS. A limit exactly at the end of the no-rush period leaves a match with no
			// second of playable war in it, which is the same defect as one inside.
			Assert.That(LobbyPhaseConsistency.Warning(Escalation("10", "10")),
				Is.EqualTo(LobbyPhaseConsistency.MatchEndsDuringNoRushText));

			// One minute clear is fine, and the default pair is fine.
			Assert.That(LobbyPhaseConsistency.Warning(Escalation("10", "20")), Is.Null);
			Assert.That(LobbyPhaseConsistency.Warning(Escalation("5", "0")), Is.Null);
		}

		[Test]
		public void TheWarheadClockIsNotPartOfTheRuleBecauseItHasNoOrderingToBreak()
		{
			// The first-warheads clock runs from the FIRST KILL, and the peace phase before it ends
			// on an event with no duration -- so there is no ordering between it and either of the
			// other two clocks. The old T1 < T2 < T3 chain does not survive the ruling, and a test
			// that reintroduced it would be pinning a rule the user removed.
			var late = Lobby(
				(LobbyPhaseConsistency.ModeOptionId, "escalation"),
				(LobbyPhaseConsistency.NoRushOptionId, "2"),
				(LobbyPhaseConsistency.FirstWarheadsOptionId, "20"),
				(LobbyPhaseConsistency.TimeLimitOptionId, "10"));

			Assert.That(LobbyPhaseConsistency.Warning(late), Is.Null,
				"warheads arriving after the time limit is a legal match, not an inconsistency");
		}

		[Test]
		public void ASkirmishShopThatNeverOpensIsFlaggedAndSaysSoInItsOwnWords()
		{
			Assert.That(LobbyPhaseConsistency.Warning(Skirmish("20", "20")),
				Is.EqualTo(LobbyPhaseConsistency.NukesNeverUnlockText));
			Assert.That(LobbyPhaseConsistency.Warning(Skirmish("20", "10")),
				Is.EqualTo(LobbyPhaseConsistency.NukesNeverUnlockText));

			// The two warnings are DIFFERENT SENTENCES. A host who has hit one needs to know which
			// clock is the problem, and merging them would produce a line that names neither.
			Assert.That(LobbyPhaseConsistency.NukesNeverUnlockText,
				Is.Not.EqualTo(LobbyPhaseConsistency.MatchEndsDuringNoRushText));

			// The no-wait opt-out puts everything on sale from the first second, so the shop is never
			// late however short the match is.
			Assert.That(LobbyPhaseConsistency.Warning(Skirmish("0", "10")), Is.Null);
			Assert.That(LobbyPhaseConsistency.Warning(Skirmish("10", "20")), Is.Null);
		}

		[Test]
		public void TheEscalationRuleDoesNotFireInSkirmishAndViceVersa()
		{
			// The no-rush period is a DEFCON clock and DefconEscalation is a strict no-op in Skirmish,
			// so a Skirmish lobby carrying a long no-rush value has nothing wrong with it.
			var skirmishWithNoRush = Lobby(
				(LobbyPhaseConsistency.ModeOptionId, "skirmish"),
				(LobbyPhaseConsistency.NoRushOptionId, "15"),
				(LobbyPhaseConsistency.TimeLimitOptionId, "10"));

			Assert.That(LobbyPhaseConsistency.Warning(skirmishWithNoRush), Is.Null);

			// And the unlock clock is suspended in Escalation -- nothing is purchasable there at all.
			var escalationWithInterval = Lobby(
				(LobbyPhaseConsistency.ModeOptionId, "escalation"),
				(LobbyPhaseConsistency.UnlockIntervalOptionId, "20"),
				(LobbyPhaseConsistency.NoRushOptionId, "5"),
				(LobbyPhaseConsistency.TimeLimitOptionId, "10"));

			Assert.That(LobbyPhaseConsistency.Warning(escalationWithInterval), Is.Null);
		}

		[Test]
		public void OnlyTheOffendingPairGetsTheTooltip()
		{
			// Hanging the warning on every dropdown would make it wallpaper and the host would stop
			// reading the one place it means something.
			var lobby = Escalation("15", "10");

			Assert.That(LobbyPhaseConsistency.WarningAppliesTo(lobby, LobbyPhaseConsistency.NoRushOptionId), Is.True);
			Assert.That(LobbyPhaseConsistency.WarningAppliesTo(lobby, LobbyPhaseConsistency.TimeLimitOptionId), Is.True);
			Assert.That(LobbyPhaseConsistency.WarningAppliesTo(lobby, LobbyPhaseConsistency.FirstWarheadsOptionId), Is.False);
			Assert.That(LobbyPhaseConsistency.WarningAppliesTo(lobby, "startingcash"), Is.False);

			var skirmish = Skirmish("20", "10");
			Assert.That(LobbyPhaseConsistency.WarningAppliesTo(skirmish, LobbyPhaseConsistency.UnlockIntervalOptionId), Is.True);
			Assert.That(LobbyPhaseConsistency.WarningAppliesTo(skirmish, LobbyPhaseConsistency.NoRushOptionId), Is.False);

			// A clean lobby hangs it on nothing at all, including the time limit.
			Assert.That(LobbyPhaseConsistency.WarningAppliesTo(Escalation("5", "30"), LobbyPhaseConsistency.TimeLimitOptionId), Is.False);
		}

		[Test]
		public void EveryWarningHasADetailAndACleanLobbyHasNeither()
		{
			Assert.That(LobbyPhaseConsistency.WarningDetail(Escalation("15", "10")),
				Is.EqualTo(LobbyPhaseConsistency.MatchEndsDuringNoRushDetail));
			Assert.That(LobbyPhaseConsistency.WarningDetail(Skirmish("20", "10")),
				Is.EqualTo(LobbyPhaseConsistency.NukesNeverUnlockDetail));
			Assert.That(LobbyPhaseConsistency.WarningDetail(Escalation("5", "30")), Is.Null);
		}

		[Test]
		public void MissingUnparsableAndNullStateFallBackRatherThanThrowing()
		{
			// A lobby that has not registered an option yet, or a value that is not a number, must
			// not take the panel down. Every caller runs this inside a Draw or a tooltip delegate.
			Assert.That(LobbyPhaseConsistency.Minutes(null, "anything", 7), Is.EqualTo(7));
			Assert.That(LobbyPhaseConsistency.Minutes(Lobby(), "absent", 7), Is.EqualTo(7));
			Assert.That(LobbyPhaseConsistency.Minutes(Lobby(("x", "not-a-number")), "x", 7), Is.EqualTo(7));
			Assert.That(LobbyPhaseConsistency.Minutes(Lobby(("x", "12")), "x", 7), Is.EqualTo(12));

			Assert.That(LobbyPhaseConsistency.Warning(null), Is.Null);
			Assert.That(LobbyPhaseConsistency.Warning(Lobby()), Is.Null);
			Assert.That(LobbyPhaseConsistency.WarningAppliesTo(null, LobbyPhaseConsistency.TimeLimitOptionId), Is.False);
		}

		// THE IDS ARE WIRE-VISIBLE STATE. Renaming one silently discards every stored value set to
		// it, and the server validates against the option's own Values dictionary -- so a constant
		// here that drifts from the trait's would flag on an id nothing publishes, forever silently.
		[Test]
		public void TheIdsMatchTheTraitsThatPublishThem()
		{
			Assert.That(LobbyPhaseConsistency.NoRushOptionId, Is.EqualTo(DefconEscalationInfo.NoRushOptionId));
			Assert.That(LobbyPhaseConsistency.FirstWarheadsOptionId, Is.EqualTo(DefconEscalationInfo.FirstWarheadsOptionId));
			Assert.That(LobbyPhaseConsistency.ModeOptionId, Is.EqualTo(DefconEscalationInfo.ModeOptionId));
			Assert.That(LobbyPhaseConsistency.UnlockIntervalOptionId, Is.EqualTo(NuclearUnlockClockInfo.IntervalOptionId));
		}
	}
}
