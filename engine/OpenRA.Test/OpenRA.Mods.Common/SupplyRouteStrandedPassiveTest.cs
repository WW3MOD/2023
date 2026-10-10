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
	/// Pinned here: the decision (ShouldEliminateStrandedPassive over HasRescuer, the same predicate
	/// the first-time fork uses) and that OnDefeatBarFull still asks it. The end-to-end half — a
	/// teammate's defeat actually resolving the passive owner Lost — is the
	/// test-contestation-stranded-passive scenario.
	/// </summary>
	[TestFixture]
	public class SupplyRouteStrandedPassiveTest
	{
		static IEnumerable<(bool SameTeam, WinState State, bool IsPassive)> SRs(
			params (bool, WinState, bool)[] supplyRoutes)
		{
			return supplyRoutes;
		}

		static bool Stranded(params (bool, WinState, bool)[] otherSupplyRoutes)
		{
			return SupplyRouteContestation.ShouldEliminateStrandedPassive(
				SupplyRouteContestation.HasRescuer(SRs(otherSupplyRoutes)), resolvesInSession: true);
		}

		[Test]
		public void TruthTable()
		{
			Assert.That(SupplyRouteContestation.ShouldEliminateStrandedPassive(rescuable: true, resolvesInSession: true), Is.False);
			Assert.That(SupplyRouteContestation.ShouldEliminateStrandedPassive(rescuable: false, resolvesInSession: true), Is.True);
			Assert.That(SupplyRouteContestation.ShouldEliminateStrandedPassive(rescuable: true, resolvesInSession: false), Is.False);
			Assert.That(SupplyRouteContestation.ShouldEliminateStrandedPassive(rescuable: false, resolvesInSession: false), Is.False,
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
		public void OnDefeatBarFullAsksTheStrandedQuestion()
		{
			// The bug was not a wrong answer but a question never asked: OnDefeatBarFull returned on
			// `isPassive` before evaluating anything. Pin that it now consults both halves, so a refactor
			// that restores the early return fails here and not only in a scenario that times out.
			var method = typeof(SupplyRouteContestation).GetMethod("OnDefeatBarFull", BindingFlags.NonPublic | BindingFlags.Instance);
			Assert.That(method, Is.Not.Null, "SupplyRouteContestation.OnDefeatBarFull is gone.");

			var decide = typeof(SupplyRouteContestation).GetMethod(
				nameof(SupplyRouteContestation.ShouldEliminateStrandedPassive), BindingFlags.Public | BindingFlags.Static);
			var rescuer = typeof(SupplyRouteContestation).GetMethod("HasActiveTeamSupplyRoute", BindingFlags.NonPublic | BindingFlags.Instance);
			Assert.That(rescuer, Is.Not.Null, "SupplyRouteContestation.HasActiveTeamSupplyRoute is gone.");

			var scan = IlScan.Scan(method);
			Assert.That(scan.ResolvedCalls, Is.GreaterThan(1),
				$"IL scan resolved only {scan.ResolvedCalls} tokens — the scanner is broken, not the code clean.");
			Assert.That(scan.Callees.Any(c => c.MetadataToken == decide.MetadataToken && c.Module == decide.Module), Is.True,
				"OnDefeatBarFull no longer consults ShouldEliminateStrandedPassive: a passive owner whose last rescuer leaves " +
				"is stranded in an unending match again.");
			Assert.That(scan.Callees.Count(c => c.MetadataToken == rescuer.MetadataToken && c.Module == rescuer.Module), Is.GreaterThanOrEqualTo(2),
				"OnDefeatBarFull must ask HasActiveTeamSupplyRoute on both paths: first-time (passive vs eliminated) and passive re-check.");
		}
	}
}
