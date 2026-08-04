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
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// Contract for SupplyHuntMath — the decision layer behind infantry auto-seek-supplies.
	/// Everything the live trait decides (does the ammo level justify the errand, do the stances
	/// permit it, is the source close enough, which source, and where the unit is in its run) is
	/// pinned here, so the behaviour is regression-tested without launching a game.
	/// </summary>
	[TestFixture]
	public class SupplyHuntMathTest
	{
		const int DefaultThreshold = 250;   // per mille, 25%

		[Test]
		public void ThresholdTripsStrictlyBelow()
		{
			// 24.9% trips, 25.0% does not — a unit sitting exactly on the boundary stays put so a
			// batch-at-a-time top-up cannot oscillate it in and out of a supply run.
			Assert.That(SupplyHuntMath.BelowSeekThreshold(249, 1000, DefaultThreshold), Is.True);
			Assert.That(SupplyHuntMath.BelowSeekThreshold(250, 1000, DefaultThreshold), Is.False);
			Assert.That(SupplyHuntMath.BelowSeekThreshold(251, 1000, DefaultThreshold), Is.False);
		}

		[Test]
		public void FullAndEmptyPools()
		{
			Assert.That(SupplyHuntMath.BelowSeekThreshold(900, 900, DefaultThreshold), Is.False);
			Assert.That(SupplyHuntMath.BelowSeekThreshold(0, 900, DefaultThreshold), Is.True);
		}

		[Test]
		public void SmallPoolsUseTheSameRealPercentageAsLargeOnes()
		{
			// A 3-missile ATGM pool: 0 of 3 trips, 1 of 3 (33%) does not. Cross-multiplying rather
			// than dividing is what keeps this consistent with a 900-round pool instead of falling
			// off an integer-truncation cliff.
			Assert.That(SupplyHuntMath.BelowSeekThreshold(0, 3, DefaultThreshold), Is.True);
			Assert.That(SupplyHuntMath.BelowSeekThreshold(1, 3, DefaultThreshold), Is.False);

			// 224/900 = 24.9% trips; 225/900 = exactly 25% does not — same rule, 300x the pool.
			Assert.That(SupplyHuntMath.BelowSeekThreshold(224, 900, DefaultThreshold), Is.True);
			Assert.That(SupplyHuntMath.BelowSeekThreshold(225, 900, DefaultThreshold), Is.False);
		}

		[Test]
		public void DegenerateThresholdConfigNeverSeeks()
		{
			Assert.That(SupplyHuntMath.BelowSeekThreshold(0, 0, DefaultThreshold), Is.False);
			Assert.That(SupplyHuntMath.BelowSeekThreshold(0, 900, 0), Is.False);
			Assert.That(SupplyHuntMath.BelowSeekThreshold(0, 900, -1), Is.False);
		}

		[Test]
		public void DefaultStancesPermitTheRun()
		{
			// The ordinary line-infantry configuration.
			Assert.That(
				SupplyHuntMath.StancesPermitHunt(UnitStance.FireAtWill, EngagementStance.Defensive, ResupplyBehavior.Auto),
				Is.True);
		}

		[Test]
		public void AmbushSuppressesTheRun()
		{
			// An ambusher walking to a truck reveals the position it was placed to conceal.
			Assert.That(
				SupplyHuntMath.StancesPermitHunt(UnitStance.Ambush, EngagementStance.Defensive, ResupplyBehavior.Auto),
				Is.False);
		}

		[Test]
		public void HoldPositionSuppressesTheRun()
		{
			Assert.That(
				SupplyHuntMath.StancesPermitHunt(UnitStance.FireAtWill, EngagementStance.HoldPosition, ResupplyBehavior.Auto),
				Is.False);
		}

		[Test]
		public void ResupplyHoldSuppressesTheRun()
		{
			// Hold means "stay put, a truck will come to me" — the opposite of this behaviour.
			Assert.That(
				SupplyHuntMath.StancesPermitHunt(UnitStance.FireAtWill, EngagementStance.Defensive, ResupplyBehavior.Hold),
				Is.False);
		}

		[Test]
		public void ResupplyEvacuateSuppressesTheRun()
		{
			// Evacuate is owned by the out-of-ammo evac path and takes precedence.
			Assert.That(
				SupplyHuntMath.StancesPermitHunt(UnitStance.FireAtWill, EngagementStance.Defensive, ResupplyBehavior.Evacuate),
				Is.False);
		}

		[Test]
		public void StanceGateMatrixIsExhaustive()
		{
			// Every combination: permitted iff Auto AND not HoldPosition AND not Ambush. Locks the
			// gate as a conjunction, so adding a stance value cannot silently open a hole.
			foreach (var fire in new[] { UnitStance.HoldFire, UnitStance.Ambush, UnitStance.FireAtWill })
			{
				foreach (var engagement in new[] { EngagementStance.HoldPosition, EngagementStance.Defensive, EngagementStance.Hunt })
				{
					foreach (var resupply in new[] { ResupplyBehavior.Hold, ResupplyBehavior.Auto, ResupplyBehavior.Evacuate })
					{
						var expected = resupply == ResupplyBehavior.Auto
							&& engagement != EngagementStance.HoldPosition
							&& fire != UnitStance.Ambush;

						Assert.That(
							SupplyHuntMath.StancesPermitHunt(fire, engagement, resupply), Is.EqualTo(expected),
							$"fire={fire} engagement={engagement} resupply={resupply}");
					}
				}
			}
		}

		[Test]
		public void HoldFireStillPermitsTheRun()
		{
			// HoldFire is "don't shoot", not "don't move" — only Ambush refuses to walk.
			Assert.That(
				SupplyHuntMath.StancesPermitHunt(UnitStance.HoldFire, EngagementStance.Defensive, ResupplyBehavior.Auto),
				Is.True);
		}

		[Test]
		public void LeashIncludesItsOwnBoundary()
		{
			var exactly20Cells = (long)(20 * 1024) * (20 * 1024);
			Assert.That(SupplyHuntMath.WithinLeash(exactly20Cells, 20), Is.True);
			Assert.That(SupplyHuntMath.WithinLeash(exactly20Cells + 1, 20), Is.False);
		}

		[Test]
		public void LeashExcludesADistantSource()
		{
			var thirtyCells = (long)(30 * 1024) * (30 * 1024);
			Assert.That(SupplyHuntMath.WithinLeash(thirtyCells, 20), Is.False);
		}

		[Test]
		public void LeashIsEuclideanNotChebyshev()
		{
			// A source 20 cells out on BOTH axes is ~28.3 cells away, so it is outside a 20-cell
			// leash even though a player counting squares on the minimap would call it "20 across".
			var diagonal = (long)(20 * 1024) * (20 * 1024) * 2;
			Assert.That(SupplyHuntMath.WithinLeash(diagonal, 20), Is.False);
		}

		[Test]
		public void DegenerateLeashExcludesEverythingButZeroDistance()
		{
			Assert.That(SupplyHuntMath.WithinLeash(0, 0), Is.True);
			Assert.That(SupplyHuntMath.WithinLeash(1, 0), Is.False);
			Assert.That(SupplyHuntMath.WithinLeash(1, -5), Is.False);
		}

		[Test]
		public void NearestSourceWins()
		{
			var candidates = new List<SupplyHuntMath.Candidate>
			{
				new SupplyHuntMath.Candidate(9000, 11),
				new SupplyHuntMath.Candidate(400, 12),
				new SupplyHuntMath.Candidate(2500, 13),
			};

			Assert.That(SupplyHuntMath.SelectNearest(candidates), Is.EqualTo(1));
		}

		[Test]
		public void EquidistantSourcesBreakOnLowerActorId()
		{
			// Two trucks the same distance away must not be chosen by enumeration order — the pick
			// has to be a total order or two clients can diverge.
			var forward = new List<SupplyHuntMath.Candidate>
			{
				new SupplyHuntMath.Candidate(2500, 42),
				new SupplyHuntMath.Candidate(2500, 7),
			};

			var reversed = new List<SupplyHuntMath.Candidate>
			{
				new SupplyHuntMath.Candidate(2500, 7),
				new SupplyHuntMath.Candidate(2500, 42),
			};

			Assert.That(forward[SupplyHuntMath.SelectNearest(forward)].ActorId, Is.EqualTo(7u));
			Assert.That(reversed[SupplyHuntMath.SelectNearest(reversed)].ActorId, Is.EqualTo(7u));
		}

		[Test]
		public void NoCandidatesSelectsNothing()
		{
			Assert.That(SupplyHuntMath.SelectNearest(new List<SupplyHuntMath.Candidate>()), Is.EqualTo(-1));
		}

		[Test]
		public void RunGoesOutWaitsThenComesHome()
		{
			// The nominal round trip.
			var s = SupplyHuntState.Approaching;

			s = SupplyHuntMath.NextState(s, providerUsable: true, inAura: false, replenished: false, atOrigin: false);
			Assert.That(s, Is.EqualTo(SupplyHuntState.Approaching));

			s = SupplyHuntMath.NextState(s, providerUsable: true, inAura: true, replenished: false, atOrigin: false);
			Assert.That(s, Is.EqualTo(SupplyHuntState.Replenishing));

			s = SupplyHuntMath.NextState(s, providerUsable: true, inAura: true, replenished: false, atOrigin: false);
			Assert.That(s, Is.EqualTo(SupplyHuntState.Replenishing));

			s = SupplyHuntMath.NextState(s, providerUsable: true, inAura: true, replenished: true, atOrigin: false);
			Assert.That(s, Is.EqualTo(SupplyHuntState.Returning));

			s = SupplyHuntMath.NextState(s, providerUsable: true, inAura: false, replenished: true, atOrigin: false);
			Assert.That(s, Is.EqualTo(SupplyHuntState.Returning));

			s = SupplyHuntMath.NextState(s, providerUsable: true, inAura: false, replenished: true, atOrigin: true);
			Assert.That(s, Is.EqualTo(SupplyHuntState.Done));
		}

		[Test]
		public void LosingTheProviderSendsTheUnitHomeNotNowhere()
		{
			// Truck dies, drains, or drives off mid-run. The unit must walk back to the line it
			// left, not strand itself wherever it happened to be standing.
			Assert.That(
				SupplyHuntMath.NextState(SupplyHuntState.Approaching, providerUsable: false, inAura: false, replenished: false, atOrigin: false),
				Is.EqualTo(SupplyHuntState.Returning));

			Assert.That(
				SupplyHuntMath.NextState(SupplyHuntState.Replenishing, providerUsable: false, inAura: true, replenished: false, atOrigin: false),
				Is.EqualTo(SupplyHuntState.Returning));
		}

		[Test]
		public void BeingRefilledEnRouteTurnsTheUnitAround()
		{
			// Another provider (or a passing truck's aura) topped us up before we arrived — no
			// reason to finish the walk.
			Assert.That(
				SupplyHuntMath.NextState(SupplyHuntState.Approaching, providerUsable: true, inAura: false, replenished: true, atOrigin: false),
				Is.EqualTo(SupplyHuntState.Returning));
		}

		[Test]
		public void DriftingOutOfTheAuraReApproaches()
		{
			// The provider is mobile: if the truck drives off while we are topping up, resume the
			// approach rather than abandoning the run.
			Assert.That(
				SupplyHuntMath.NextState(SupplyHuntState.Replenishing, providerUsable: true, inAura: false, replenished: false, atOrigin: false),
				Is.EqualTo(SupplyHuntState.Approaching));
		}

		[Test]
		public void ReturningIgnoresAmmoAndProviderState()
		{
			// Once heading home, nothing about the provider pulls the unit back — otherwise a unit
			// that could not be fully refilled would ping-pong.
			Assert.That(
				SupplyHuntMath.NextState(SupplyHuntState.Returning, providerUsable: true, inAura: true, replenished: false, atOrigin: false),
				Is.EqualTo(SupplyHuntState.Returning));
		}

		[Test]
		public void DoneIsTerminal()
		{
			Assert.That(
				SupplyHuntMath.NextState(SupplyHuntState.Done, providerUsable: true, inAura: true, replenished: false, atOrigin: false),
				Is.EqualTo(SupplyHuntState.Done));
		}
	}
}
