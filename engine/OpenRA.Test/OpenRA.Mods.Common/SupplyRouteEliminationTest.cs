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

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// Locks the team-victory decision that SupplyRouteContestation applies when a team's last
	/// active Supply Route falls. The regression this guards: a 2v2 where the winning team denied
	/// both enemy Supply Routes ended with ALL four players marked Lost ("mission failed"), because
	/// the win was only ever inferred by ConquestVictoryConditions and a near-simultaneous mutual
	/// overrun resolved both teams as losers before that inference could run.
	/// </summary>
	[TestFixture]
	public class SupplyRouteEliminationTest
	{
		[Test]
		public void EliminatedTeamMemberLoses()
		{
			Assert.That(
				SupplyRouteContestation.ResolveEliminationOutcome(WinState.Undefined, onEliminatedTeam: true),
				Is.EqualTo(WinState.Lost));
		}

		[Test]
		public void SurvivorIsAwardedTheWin()
		{
			// The core fix: survivors are credited Won explicitly, not left Undefined/Lost.
			Assert.That(
				SupplyRouteContestation.ResolveEliminationOutcome(WinState.Undefined, onEliminatedTeam: false),
				Is.EqualTo(WinState.Won));
		}

		[Test]
		public void AlreadyDecidedPlayersAreLeftUntouched()
		{
			// The anti "everyone loses" invariant: once a player has an outcome, a second
			// elimination event (the other team's last SR falling in the same window) is a no-op.
			// Without this, the winners' Won would be overwritten by the loser's DefeatTeam pass.
			Assert.That(
				SupplyRouteContestation.ResolveEliminationOutcome(WinState.Won, onEliminatedTeam: true),
				Is.Null);
			Assert.That(
				SupplyRouteContestation.ResolveEliminationOutcome(WinState.Won, onEliminatedTeam: false),
				Is.Null);
			Assert.That(
				SupplyRouteContestation.ResolveEliminationOutcome(WinState.Lost, onEliminatedTeam: true),
				Is.Null);
			Assert.That(
				SupplyRouteContestation.ResolveEliminationOutcome(WinState.Lost, onEliminatedTeam: false),
				Is.Null);
		}
	}
}
