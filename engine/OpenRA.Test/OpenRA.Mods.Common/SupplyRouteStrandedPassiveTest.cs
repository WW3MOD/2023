#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// The stranded passive player. A full defeat bar makes its owner PASSIVE while a teammate still
	/// holds an active Supply Route. If that teammate then leaves by a route SupplyRouteContestation
	/// never sees — surrender, ConcedeOnDisconnect, an objective failed by another win condition —
	/// nothing re-asked the question, and the passive owner (holding an untargetable MustBeDestroyed
	/// SR that ConquestVictoryConditions can never fail) stayed Undefined forever: the match did not end.
	///
	/// Pinned here: the whole full-bar decision (DecideDefeatBarFull over HasRescuer, the same
	/// predicate the first-time fork uses) and that OnDefeatBarFull calls it. NOT pinned here, and not
	/// pinnable by these tools: a guard restored ABOVE that call (`if (isPassive) return;`) reads only a
	/// field, so neither the truth table nor the IL call scan can see it. That regression is caught by
	/// the test-contestation-stranded-passive scenario, whose RED arm is exactly that guard.
	/// </summary>
	[TestFixture]
	public class SupplyRouteStrandedPassiveTest
	{
		static IEnumerable<(bool SameTeam, WinState State, bool IsPassive)> SRs(
			params (bool, WinState, bool)[] supplyRoutes)
		{
			return supplyRoutes;
		}

		const bool Resolves = true;

		static DefeatBarFullAction Decide(WinState state, bool passive, bool rescuable, bool resolves)
		{
			return SupplyRouteContestation.DecideDefeatBarFull(state, passive, rescuable, resolves);
		}

		// A passive owner, bar held full, in a resolving session: what do these other SRs make it do?
		static bool Stranded(params (bool, WinState, bool)[] otherSupplyRoutes)
		{
			return Decide(WinState.Undefined, true, SupplyRouteContestation.HasRescuer(SRs(otherSupplyRoutes)), Resolves)
				== DefeatBarFullAction.Eliminate;
		}

		[Test]
		public void PassiveWithNoRescuerLeft_IsEliminated()
		{
			// THE ROW THE BUG LIVED IN. The old `if (isPassive || ...) return;` answered None here.
			Assert.That(Decide(WinState.Undefined, passive: true, rescuable: false, resolves: true), Is.EqualTo(DefeatBarFullAction.Eliminate));
		}

		[Test]
		public void TruthTable()
		{
			foreach (var resolves in new[] { true, false })
			{
				// First time the bar fills. Eliminate even in a declined session: ResolveTeamElimination
				// owns that decline and logs it once, and the owner still goes passive (unchanged).
				Assert.That(Decide(WinState.Undefined, false, true, resolves), Is.EqualTo(DefeatBarFullAction.Passivate));
				Assert.That(Decide(WinState.Undefined, false, false, resolves), Is.EqualTo(DefeatBarFullAction.Eliminate));

				// Already passive, rescuer still there: hold.
				Assert.That(Decide(WinState.Undefined, true, true, resolves), Is.EqualTo(DefeatBarFullAction.None));

				// Already decided: never touched, whatever else is true.
				foreach (var decided in new[] { WinState.Won, WinState.Lost })
					foreach (var passive in new[] { true, false })
						foreach (var rescuable in new[] { true, false })
							Assert.That(Decide(decided, passive, rescuable, resolves), Is.EqualTo(DefeatBarFullAction.None),
								$"a {decided} owner was acted on (passive={passive} rescuable={rescuable} resolves={resolves})");
			}

			Assert.That(Decide(WinState.Undefined, true, false, resolves: false), Is.EqualTo(DefeatBarFullAction.None),
				"A declined session (TestMode without RunInTestMode) must not re-enter ResolveTeamElimination every tick: " +
				"its decline writes a debug.log line, so this would flood the log on every passive tick.");
		}

		[Test]
		public void TeammateStillHoldingItsSupplyRoute_StaysPassive()
		{
			Assert.That(Stranded(
				(true, WinState.Undefined, false),    // the rescuer
				(false, WinState.Undefined, false)),  // the enemy sitting on us
				Is.False);
		}

		[Test]
		public void LastRescuerSurrendered_IsEliminated()
		{
			// THE REPORTED CASE. Surrender, disconnect and every other non-contestation defeat all
			// reduce to the same input: the teammate's WinState went Lost and its SR is still in the world.
			Assert.That(Stranded(
				(true, WinState.Lost, false),
				(false, WinState.Undefined, false)),
				Is.True);
		}

		[Test]
		public void RemainingTeammateIsItselfPassive_IsEliminated()
		{
			// Two passive teammates relieve nobody. Their last active teammate left, so the first of
			// the two to tick takes the whole team down — the same answer the first-time fork gives.
			Assert.That(Stranded(
				(true, WinState.Lost, false),
				(true, WinState.Undefined, true),
				(false, WinState.Undefined, false)),
				Is.True);
		}

		[Test]
		public void OneOfTwoRescuersLeft_StaysPassive()
		{
			Assert.That(Stranded(
				(true, WinState.Lost, false),
				(true, WinState.Undefined, false),
				(false, WinState.Undefined, false)),
				Is.False);
		}

		[Test]
		public void TeammateWon_StaysPassive()
		{
			// A teammate that has Won means the team has won; the passive member is awarded, never
			// eliminated (HasRescuer's Won limb). Leaving the game by winning is not abandonment.
			Assert.That(Stranded(
				(true, WinState.Won, false),
				(false, WinState.Lost, false)),
				Is.False);
		}

		[Test]
		public void OnDefeatBarFullDelegatesToTheDecision()
		{
			// Catches the call being deleted or replaced by an inlined copy. It does NOT catch a field-only
			// guard restored above the call — see the class summary; the scenario owns that.
			var method = typeof(SupplyRouteContestation).GetMethod("OnDefeatBarFull", BindingFlags.NonPublic | BindingFlags.Instance);
			Assert.That(method, Is.Not.Null, "SupplyRouteContestation.OnDefeatBarFull is gone.");

			var decide = typeof(SupplyRouteContestation).GetMethod(
				nameof(SupplyRouteContestation.DecideDefeatBarFull), BindingFlags.Public | BindingFlags.Static);

			var scan = IlScan.Scan(method);
			Assert.That(scan.ResolvedCalls, Is.GreaterThan(1),
				$"IL scan resolved only {scan.ResolvedCalls} tokens — the scanner is broken, not the code clean.");
			Assert.That(scan.Callees.Any(c => c.MetadataToken == decide.MetadataToken && c.Module == decide.Module), Is.True,
				"OnDefeatBarFull no longer asks DecideDefeatBarFull: the passive re-check can silently disappear again.");
		}
	}
}
