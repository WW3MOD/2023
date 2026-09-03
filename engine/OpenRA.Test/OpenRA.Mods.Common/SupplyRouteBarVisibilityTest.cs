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
	/// Covers SupplyRouteContestation's decision to draw its contestation bar on an UNSELECTED Supply
	/// Route (IAlwaysVisibleBar.ShowBarWithoutSelection). Contesting is not capturing — ownership never
	/// transfers — so this bar is purely an alert that enemies are on someone's beachhead right now.
	/// Regression guarded: a defeated player's Supply Route showed a full red bar forever. Tick returns
	/// early once the owner is out (OwnerStillPlaying), which freezes controlBar at 0 and defeatBar
	/// full; the old predicate was `controlBar &lt; BarMax` alone, so that frozen state read as
	/// "permanently contested" and the dead player's bar never left the screen.
	/// Only the pure predicates are exercised. The renderer side — SelectionDecorationsBase consulting
	/// IAlwaysVisibleBar, and the bar still drawing on selection via ISelectionBar.DisplayWhenEmpty —
	/// is verified by code reasoning, not here.
	/// </summary>
	[TestFixture]
	public class SupplyRouteBarVisibilityTest
	{
		const int BarMax = 100000;

		[Test]
		public void ContestedLivePlayerShowsBarWithoutSelection()
		{
			// The behaviour the bar exists for, and the one this fix must not touch: a live owner
			// under attack gets the alert whether or not anybody has the actor selected.
			Assert.That(
				SupplyRouteContestation.ShouldShowBarWithoutSelection(WinState.Undefined, false, true, BarMax / 2, BarMax),
				Is.True);

			// Fully depleted but not yet resolved — the defeat bar is filling and the owner is still
			// in the game. This is the loudest moment the alert has.
			Assert.That(
				SupplyRouteContestation.ShouldShowBarWithoutSelection(WinState.Undefined, false, true, 0, BarMax),
				Is.True);
		}

		[Test]
		public void UncontestedLivePlayerHidesBarWithoutSelection()
		{
			Assert.That(
				SupplyRouteContestation.ShouldShowBarWithoutSelection(WinState.Undefined, false, true, BarMax, BarMax),
				Is.False);
		}

		[Test]
		public void DefeatedPlayerHidesBarWithoutSelection()
		{
			// The reported bug. controlBar 0 is exactly the state a contestation defeat leaves behind,
			// and it is the state the old `controlBar < BarMax` predicate read as still-contested.
			Assert.That(
				SupplyRouteContestation.ShouldShowBarWithoutSelection(WinState.Lost, false, true, 0, BarMax),
				Is.False,
				"a defeated owner's Supply Route must not draw its bar unless the actor is selected");

			// Defeat by any other route (surrender, all units lost) freezes the bar wherever it stood,
			// which need not be empty. A partially depleted bar is just as stale.
			Assert.That(
				SupplyRouteContestation.ShouldShowBarWithoutSelection(WinState.Lost, false, true, BarMax / 2, BarMax),
				Is.False);
		}

		[Test]
		public void OwnersWhoCanNoLongerActHideBarWithoutSelection()
		{
			// Every terminal or non-participating owner is the same case: Tick has stopped, so the bar
			// can never move again. Won is included because the trait's own tick guard treats it
			// identically — a frozen bar over a winner's beachhead is no more current than over a
			// loser's. NonCombatant/Playable cover the SR being handed to Neutral on defeat, whose
			// WinState is Undefined and which would otherwise slip back through the WinState check.
			Assert.That(
				SupplyRouteContestation.ShouldShowBarWithoutSelection(WinState.Won, false, true, 0, BarMax),
				Is.False, "a winner's frozen bar is as stale as a loser's");

			Assert.That(
				SupplyRouteContestation.ShouldShowBarWithoutSelection(WinState.Undefined, true, true, 0, BarMax),
				Is.False, "a NonCombatant (e.g. Neutral after defeat) owner must not drive the alert");

			Assert.That(
				SupplyRouteContestation.ShouldShowBarWithoutSelection(WinState.Undefined, false, false, 0, BarMax),
				Is.False, "a non-Playable owner must not drive the alert");
		}

		[Test]
		public void OwnerStillPlayingMatchesTheTickGuard()
		{
			// ShowBarWithoutSelection and Tick must agree on "this owner is still in the game", because
			// the whole bug was the bar outliving the simulation that moves it. Only the all-live
			// combination is true.
			Assert.That(SupplyRouteContestation.OwnerStillPlaying(WinState.Undefined, false, true), Is.True);

			foreach (var winState in new[] { WinState.Undefined, WinState.Won, WinState.Lost })
				foreach (var nonCombatant in new[] { true, false })
					foreach (var playable in new[] { true, false })
					{
						var live = winState == WinState.Undefined && !nonCombatant && playable;
						Assert.That(
							SupplyRouteContestation.OwnerStillPlaying(winState, nonCombatant, playable),
							Is.EqualTo(live),
							$"state {winState}, nonCombatant {nonCombatant}, playable {playable}");

						// The composed decision may only ever narrow the tick guard, never widen it.
						Assert.That(
							SupplyRouteContestation.ShouldShowBarWithoutSelection(winState, nonCombatant, playable, 0, BarMax)
								&& !SupplyRouteContestation.OwnerStillPlaying(winState, nonCombatant, playable),
							Is.False,
							"the bar must never show without selection for an owner Tick has abandoned");
					}
		}
	}
}
