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
	/// Covers the two pure decisions SupplyRouteContestation makes when a team's last active Supply
	/// Route falls: which eliminated-team members are marked Lost (ResolveEliminationOutcome) and
	/// which survivors are awarded Won (ShouldAwardVictory). These are unit-scoped helpers — the full
	/// World/MissionObjectives propagation is NOT exercised here and is verified by code reasoning.
	/// Regression guarded: a 2v2 where the winning team denied both enemy Supply Routes ended with all
	/// four players Lost ("mission failed"), and (post-first-fix) an FFA elimination over-awarded Won
	/// to every remaining hostile party.
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
	}
}
