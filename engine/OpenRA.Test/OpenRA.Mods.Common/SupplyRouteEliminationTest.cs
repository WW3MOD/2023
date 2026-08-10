#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// Covers the pure decisions SupplyRouteContestation makes when a team's last active Supply Route
	/// falls: which candidates a team elimination marks Lost (SelectEliminationTargets /
	/// ResolveEliminationOutcome), whether the team already has a victor (TeamHasVictor), and which
	/// survivors are awarded Won (ShouldAwardVictory). These are unit-scoped helpers — the full
	/// World/MissionObjectives propagation is NOT exercised here and is verified by code reasoning.
	/// The autotest harness cannot reach this path at all: ResolveTeamElimination early-returns under
	/// TestMode, and DOCS/recipes/AUTOTEST.md permits only one Playable slot, so no 3-way FFA exists.
	/// Regression guarded: a 2v2 where the winning team denied both enemy Supply Routes ended with all
	/// four players Lost ("mission failed"); (post-first-fix) an FFA elimination over-awarded Won to
	/// every remaining hostile party; and a 3-player skirmish where eliminating one bot defeated the
	/// human and every other survivor slotted after it.
	/// </summary>
	[TestFixture]
	public class SupplyRouteEliminationTest
	{
		static IEnumerable<(bool Allied, WinState State)> Others(params (bool, WinState)[] others)
		{
			return others;
		}

		// --- Eliminated-team marking + simultaneous-overrun race guard ---

		[Test]
		public void EliminatedTeamMemberLoses()
		{
			Assert.That(
				SupplyRouteContestation.ResolveEliminationOutcome(WinState.Undefined, onEliminatedTeam: true),
				Is.EqualTo(WinState.Lost));
		}

		[Test]
		public void AlreadyDecidedPlayersAreLeftUntouched()
		{
			// The anti "everyone loses" invariant: once a player has an outcome, a second
			// elimination event (the other team's last SR falling in the same window) is a no-op.
			// Without this, the winners' Won would be overwritten by the loser's elimination pass —
			// this is what guarantees exactly ONE team wins a 2v2 mutual overrun.
			Assert.That(
				SupplyRouteContestation.ResolveEliminationOutcome(WinState.Won, onEliminatedTeam: true),
				Is.Null);
			Assert.That(
				SupplyRouteContestation.ResolveEliminationOutcome(WinState.Lost, onEliminatedTeam: false),
				Is.Null);
		}

		// --- Survivor win-award (the FFA regression) ---

		[Test]
		public void TwoVsTwo_EnemyTeamEliminated_SurvivorIsAwarded()
		{
			// (a) 2v2: survivor's two enemies are Lost, ally still Undefined → award Won.
			var result = SupplyRouteContestation.ShouldAwardVictory(Others(
				(true, WinState.Undefined),   // ally
				(false, WinState.Lost),       // enemy 1
				(false, WinState.Lost)));     // enemy 2

			Assert.That(result, Is.True);
		}

		[Test]
		public void FreeForAll_OneEliminated_NoAwardYet()
		{
			// (b) 3-player FFA, one eliminated: a hostile is still alive → defer, no win.
			var result = SupplyRouteContestation.ShouldAwardVictory(Others(
				(false, WinState.Lost),        // eliminated rival
				(false, WinState.Undefined))); // still-fighting rival

			Assert.That(result, Is.False);
		}

		[Test]
		public void FreeForAll_LastSurvivor_IsAwarded()
		{
			// (c) FFA down to one survivor: every hostile is Lost → award Won.
			var result = SupplyRouteContestation.ShouldAwardVictory(Others(
				(false, WinState.Lost),
				(false, WinState.Lost)));

			Assert.That(result, Is.True);
		}

		[Test]
		public void LoneSurvivor_NoOtherCombatants_IsAwarded()
		{
			Assert.That(SupplyRouteContestation.ShouldAwardVictory(Others()), Is.True);
		}

		// --- Winning-team guard: a team with a victor is never eliminated ---
		// Regression: 1v2 (human vs allied bots) where one bot clinched the win via
		// ConquestVictoryConditions a tick before the other bot's Supply Route defeat bar filled.
		// The trailing bot was still Undefined and got marked Lost by team-elimination, so the
		// winning team ended with one member shown "Won" and the other "Lost".

		[Test]
		public void TeamWithAWonAlly_IsVictorious()
		{
			// The still-Undefined teammate sees its ally already Won -> team has won, do not eliminate.
			var result = SupplyRouteContestation.TeamHasVictor(Others(
				(true, WinState.Won),          // ally already clinched the win
				(false, WinState.Lost)));      // the defeated enemy

			Assert.That(result, Is.True);
		}

		// --- Elimination must not cascade onto players who are not on the eliminated team ---
		// Regression (high severity, 3-player skirmish): the human destroyed one bot's Supply Route and
		// was himself marked defeated, all his units killed. Phase 1 decided membership with a live
		// IsAlliedWith test inside the very loop that was calling MarkFailed. MarkFailed sets WinState
		// synchronously, a Lost player is instantly Spectating, and RelationshipWith reports every
		// Spectating player as an Ally — so once the eliminated player was marked, every playable
		// player positioned AFTER it in slot order passed the "is my ally" test and was marked Lost too.
		// The contract restored here is that each candidate's verdict depends only on its own
		// (membership, win state) pair, so applying one verdict cannot reclassify any other.

		static bool[] Targets(params (bool OnEliminatedTeam, WinState State)[] candidates)
		{
			return SupplyRouteContestation.SelectEliminationTargets(candidates);
		}

		[Test]
		public void EliminationDoesNotCascadeOntoLaterSlottedSurvivors()
		{
			// h > e: eliminated bot in slot 0, human in slot 1, third player in slot 2.
			// This is the reported case — the human was defeated for killing someone else's SR.
			var targets = Targets(
				(true, WinState.Undefined),     // slot 0: the eliminated player
				(false, WinState.Undefined),    // slot 1: the human, NOT on that team
				(false, WinState.Undefined));   // slot 2: a third party, NOT on that team

			Assert.That(targets, Is.EqualTo(new[] { true, false, false }));
		}

		[Test]
		public void EliminationDoesNotCascadeOntoEarlierSlottedSurvivors()
		{
			// h < e: the opposite sign of the same defect. Here the human survived, but every bot
			// slotted after the eliminated one was wrongly killed and the human was then handed an
			// instant win by AwardDecidedSurvivors.
			var targets = Targets(
				(false, WinState.Undefined),    // slot 0: the human
				(true, WinState.Undefined),     // slot 1: the eliminated player
				(false, WinState.Undefined));   // slot 2: a third party that must keep playing

			Assert.That(targets, Is.EqualTo(new[] { false, true, false }));
		}

		[Test]
		public void MarkingOneMemberDoesNotReclassifyTheOthers()
		{
			// The independence property itself: re-running the decision after the first candidate has
			// actually become Lost must leave every other verdict byte-identical. Under the old live
			// IsAlliedWith test this is exactly where the cascade appeared, because the newly-Lost
			// player turned into everyone's "ally".
			var before = Targets(
				(true, WinState.Undefined),
				(false, WinState.Undefined),
				(false, WinState.Undefined));

			var after = Targets(
				(true, WinState.Lost),          // slot 0 has now been marked
				(false, WinState.Undefined),
				(false, WinState.Undefined));

			Assert.That(after[0], Is.False, "an already-decided player is not marked twice");
			Assert.That(after[1], Is.EqualTo(before[1]));
			Assert.That(after[2], Is.EqualTo(before[2]));
		}

		[Test]
		public void PartialCascade_SurvivorsBothBeforeAndAfterTheVictimAreUntouched()
		{
			// The cascade was always PARTIAL: it only ever reached players slotted after the eliminated
			// one, so the lived experience is "I'm dead, some rivals are still alive" rather than
			// "everybody dies". A mixed list is the case that matters — a fix that is still subtly
			// order-dependent can pass an all-lose test and fail this one.
			var targets = Targets(
				(false, WinState.Undefined),    // slot 0: survivor BEFORE the victim
				(false, WinState.Undefined),    // slot 1: survivor BEFORE the victim
				(true, WinState.Undefined),     // slot 2: the eliminated player
				(false, WinState.Undefined),    // slot 3: survivor AFTER — the old cascade killed this
				(false, WinState.Undefined));   // slot 4: survivor AFTER — and this

			Assert.That(targets, Is.EqualTo(new[] { false, false, true, false, false }));
		}

		[Test]
		public void VictimInFirstSlot_EveryLaterSurvivorUntouched()
		{
			// Worst case: the victim is early enough to catch everyone, which is the only arrangement
			// that produced the "nobody wins at all" end screen.
			Assert.That(
				Targets(
					(true, WinState.Undefined),
					(false, WinState.Undefined),
					(false, WinState.Undefined),
					(false, WinState.Undefined)),
				Is.EqualTo(new[] { true, false, false, false }));
		}

		[Test]
		public void VictimInLastSlot_NoOneIsDownstream()
		{
			// Mildest case: nobody is slotted after the victim, so even the old code behaved. Pinned so
			// the fix is not silently order-sensitive in the other direction.
			Assert.That(
				Targets(
					(false, WinState.Undefined),
					(false, WinState.Undefined),
					(false, WinState.Undefined),
					(true, WinState.Undefined)),
				Is.EqualTo(new[] { false, false, false, true }));
		}

		[Test]
		public void EachVerdictIsIndependentOfItsPositionInTheList()
		{
			// The general statement of the contract: a candidate's verdict inside a mixed list is the
			// same verdict it gets evaluated entirely on its own. Any positional coupling — which is
			// precisely what the live IsAlliedWith test introduced — breaks this.
			var mixed = new[]
			{
				(false, WinState.Undefined),
				(true, WinState.Undefined),
				(false, WinState.Undefined),
				(true, WinState.Lost),
				(false, WinState.Won),
				(true, WinState.Undefined),
			};

			var together = SupplyRouteContestation.SelectEliminationTargets(mixed);

			for (var i = 0; i < mixed.Length; i++)
			{
				var alone = SupplyRouteContestation.SelectEliminationTargets(new[] { mixed[i] });
				Assert.That(together[i], Is.EqualTo(alone[0]), $"candidate {i} was judged differently in company than alone");
			}
		}

		[Test]
		public void WholeEliminatedTeamIsMarked_OpposingTeamUntouched()
		{
			// The fix must not under-mark either: a 2v2 still loses both members of the dead team.
			var targets = Targets(
				(true, WinState.Undefined),
				(true, WinState.Undefined),
				(false, WinState.Undefined),
				(false, WinState.Undefined));

			Assert.That(targets, Is.EqualTo(new[] { true, true, false, false }));
		}

		[Test]
		public void AlreadyDecidedCandidatesAreNeverMarked()
		{
			var targets = Targets(
				(true, WinState.Lost),
				(true, WinState.Won),
				(false, WinState.Won));

			Assert.That(targets, Is.EqualTo(new[] { false, false, false }));
		}

		[Test]
		public void NoWonAlly_TeamNotYetVictorious()
		{
			// A Lost ally or an enemy's Won never counts -- only a living ally's victory saves the team.
			Assert.That(
				SupplyRouteContestation.TeamHasVictor(Others(
					(true, WinState.Undefined),   // ally still fighting
					(false, WinState.Lost))),     // defeated enemy
				Is.False);

			Assert.That(
				SupplyRouteContestation.TeamHasVictor(Others(
					(true, WinState.Lost),         // ally already lost
					(false, WinState.Won))),       // an ENEMY won -- must not save us
				Is.False);
		}
	}
}
