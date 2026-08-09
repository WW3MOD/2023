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

namespace OpenRA.Test
{
	/// <summary>
	/// Covers the two pure readiness decisions that used to presuppose a rearm/repair host existed:
	/// which health bar an airframe must clear to be committed (CommitHealthBar) and whether its
	/// ammunition state clears the launch / stay-in-the-fight bars (AmmoReadyToLaunch /
	/// AmmoReadyToFight).
	///
	/// Regression guarded: WW3MOD's aircraft name only `hpad` / `afld` as rearm and repair hosts, and
	/// both are unbuildable (`Buildable.Prerequisites: ~disabled`, `structures.yaml:432`, `:500` —
	/// nothing provides `disabled`) and placed on none of the ten shipped maps. With no host, every
	/// "restore to X first" bar can be crossed exactly once, downward, so one chip of damage or one
	/// spent missile benched an airframe for the whole match.
	///
	/// EVERY fixture number below is a real shipped value, not a scaled-down stand-in:
	///   Apache  (HELI,  `aircraft-america.yaml:273-276`, `:300`, `:330`, `:359`)
	///           Role AttackHeavy, FleeHealthPercent 35, ReEngageHealthPercent 75, HP 800,
	///           primary-ammo Ammo 200, secondary-ammo (Hellfire) Ammo 8.
	///   Chinook (TRAN,  `aircraft-america.yaml:7-9`, `:36`)
	///           Role Transport, FleeHealthPercent 60, ReEngageHealthPercent 90, HP 600.
	///   Littlebird (`aircraft-america.yaml:110-113`) Role Scout, Flee 50, ReEngage 80.
	/// The host-existence lookups themselves (AirframeReadiness.HasRearmHost / HasRepairHost) need a
	/// World and are NOT exercised here.
	/// </summary>
	[TestFixture]
	public class AirframeReadinessTest
	{
		const int ApacheFlee = 35;
		const int ApacheReEngage = 75;
		const int ApacheMaxHp = 800;

		const int ChinookFlee = 60;
		const int ChinookReEngage = 90;
		const int ChinookMaxHp = 600;

		const int ScoutFlee = 50;
		const int ScoutReEngage = 80;

		// Apache pools: primary-ammo (200 rounds) and secondary-ammo (8 Hellfires).
		const int ApachePools = 2;

		static int HealthPercent(int hp, int maxHp)
		{
			return hp * 100 / maxHp;
		}

		/// <summary>
		/// The commit floor is the flee bar and does not move when a host appears. This is the
		/// monotonicity rule: a captured host must add the option of repairing, never withdraw
		/// permission to fly. If someone re-keys the commit bar off host existence, capturing a
		/// logistics center snaps the Apache bar 35 -> 75 and the helicopters get more timid for
		/// having taken ground.
		/// </summary>
		[TestCase(ApacheFlee, ApacheReEngage)]
		[TestCase(ChinookFlee, ChinookReEngage)]
		[TestCase(ScoutFlee, ScoutReEngage)]
		public void TheRepairRoutingBarRisesWithAHostAndCollapsesToFleeWithout(int fleeBar, int recoveryBar)
		{
			Assert.That(AirframeReadiness.RepairRoutingBar(true, recoveryBar, fleeBar), Is.EqualTo(recoveryBar));
			Assert.That(AirframeReadiness.RepairRoutingBar(false, recoveryBar, fleeBar), Is.EqualTo(fleeBar));

			// Monotone: gaining a host may only pull an airframe out EARLIER to be repaired, never
			// later — it is an added option, so the routing bar can only go up.
			Assert.That(AirframeReadiness.RepairRoutingBar(true, recoveryBar, fleeBar),
				Is.GreaterThanOrEqualTo(AirframeReadiness.RepairRoutingBar(false, recoveryBar, fleeBar)));
		}

		/// <summary>
		/// The defect itself, at the real numbers. An Apache that has taken 300 of its 800 HP is at
		/// 62% — below ReEngageHealthPercent 75, above FleeHealthPercent 35. Under the old recovery
		/// bar it was unlaunchable, and with nothing able to repair it, it stayed that way for the
		/// match while still being too healthy for any flee path to retire it.
		/// </summary>
		[Test]
		public void TheDeadBandBetweenFleeAndRecoveryIsWhereAirframesUsedToStrand()
		{
			var hp = HealthPercent(ApacheMaxHp - 300, ApacheMaxHp);
			Assert.That(hp, Is.EqualTo(62));
			Assert.That(hp, Is.LessThan(ApacheReEngage));
			Assert.That(hp, Is.GreaterThanOrEqualTo(ApacheFlee));
		}

		/// <summary>
		/// The transport band is wider still: Chinook flees at 60 and re-engages at 90, so a single
		/// 100 HP hit out of 600 (83%) used to park it permanently.
		/// </summary>
		[Test]
		public void OneChipOfDamageStrandsATransportUnderTheRecoveryBar()
		{
			var hp = HealthPercent(ChinookMaxHp - 100, ChinookMaxHp);
			Assert.That(hp, Is.EqualTo(83));
			Assert.That(hp, Is.LessThan(ChinookReEngage));
			Assert.That(hp, Is.GreaterThanOrEqualTo(ChinookFlee));
		}

		[Test]
		public void HostedLaunchStillDemandsEveryPoolFull()
		{
			// Both pools full.
			Assert.That(AirframeReadiness.AmmoReadyToLaunch(true, ApachePools, 2, 2), Is.True);

			// Hellfires part-spent: loaded but not full.
			Assert.That(AirframeReadiness.AmmoReadyToLaunch(true, ApachePools, 2, 1), Is.False);
		}

		/// <summary>
		/// The Apache carries 8 Hellfires and cannot refill them. Once it has fired them the pool is
		/// empty for the match, so a full-ammo launch bar retires the airframe after one sortie even
		/// though its 200-round minigun is untouched.
		/// </summary>
		[Test]
		public void UnhostedLaunchOnlyAsksWhetherTheAirframeCanStillShoot()
		{
			// Hellfires dry, minigun loaded: 1 of 2 pools loaded, 1 of 2 full.
			Assert.That(AirframeReadiness.AmmoReadyToLaunch(true, ApachePools, 1, 1), Is.False);
			Assert.That(AirframeReadiness.AmmoReadyToLaunch(false, ApachePools, 1, 1), Is.True);
		}

		[Test]
		public void AnAirframeWithEveryPoolDryIsNeverLaunched()
		{
			Assert.That(AirframeReadiness.AmmoReadyToLaunch(false, ApachePools, 0, 0), Is.False);
			Assert.That(AirframeReadiness.AmmoReadyToLaunch(true, ApachePools, 0, 0), Is.False);
		}

		/// <summary>
		/// REGRESSION, and the one that matters most here because it is unreachable in play today.
		///
		/// SquadHasAmmo asks "does ANY member still shoot?". The inherited loop answered it by
		/// SKIPPING every member whose pools are all covered by a Rearmable, then reporting the squad
		/// dry because nothing survived the skip. Apache's Rearmable covers both of its pools
		/// (`aircraft-america.yaml:377`), so every attack-heli squad in this mod is all-covered and
		/// reported dry at full ammo.
		///
		/// The trap: making that skip conditional on a rearm host being present LOOKS like the fix
		/// and is a strict regression — it moves the never-launches bug from "always" to "whenever
		/// someone places a pad", i.e. onto the maps nobody can test today. HPAD carries both
		/// Reservable and RepairsUnits, so a single placed pad flips it. These two cases must give the
		/// same answer as their allPoolsRearmable=false twins.
		/// </summary>
		[TestCase(true)]
		[TestCase(false)]
		public void SquadAmmoNeverDependsOnWhetherAHostWouldCoverThePools(bool hasRearmHost)
		{
			// A full-ammo Apache counts toward its squad's ammo whether or not its pools are
			// host-covered, and whether or not a host exists.
			Assert.That(AirframeReadiness.MemberStillShoots(hasRearmHost, true, ApachePools, 2), Is.True);
			Assert.That(AirframeReadiness.MemberStillShoots(hasRearmHost, false, ApachePools, 2), Is.True);

			// ...and a bone-dry one does not, on either branch.
			Assert.That(AirframeReadiness.MemberStillShoots(hasRearmHost, true, ApachePools, 0), Is.False);
			Assert.That(AirframeReadiness.MemberStillShoots(hasRearmHost, false, ApachePools, 0), Is.False);
		}

		/// <summary>
		/// The coverage fact must not change the answer for ANY reachable combination — stated as a
		/// sweep so that re-introducing the skip in any branch fails here rather than in a live match.
		/// </summary>
		[Test]
		public void CoverageIsIgnoredAcrossEveryHostAndLoadCombination()
		{
			foreach (var hasHost in new[] { true, false })
				for (var loaded = 0; loaded <= ApachePools; loaded++)
					Assert.That(
						AirframeReadiness.MemberStillShoots(hasHost, true, ApachePools, loaded),
						Is.EqualTo(AirframeReadiness.MemberStillShoots(hasHost, false, ApachePools, loaded)),
						$"coverage changed the answer at hasHost={hasHost}, loaded={loaded}");
		}

		[Test]
		public void HostedStayInFightStillDemandsEveryPoolLoaded()
		{
			Assert.That(AirframeReadiness.AmmoReadyToFight(true, ApachePools, 2), Is.True);
			Assert.That(AirframeReadiness.AmmoReadyToFight(true, ApachePools, 1), Is.False);
		}

		[Test]
		public void UnhostedStayInFightAcceptsAPartlyDryAirframe()
		{
			Assert.That(AirframeReadiness.AmmoReadyToFight(false, ApachePools, 1), Is.True);
			Assert.That(AirframeReadiness.AmmoReadyToFight(false, ApachePools, 0), Is.False);
		}

		/// <summary>
		/// An actor with no ammo pools at all (a transport) is vacuously ready — matching the
		/// inherited HasAmmo/FullAmmo helpers, which return true over an empty pool set. Getting this
		/// backwards would bench every Chinook and Halo in the game.
		/// </summary>
		[Test]
		public void AnAirframeWithNoAmmoPoolsIsVacuouslyReady()
		{
			Assert.That(AirframeReadiness.AmmoReadyToLaunch(false, 0, 0, 0), Is.True);
			Assert.That(AirframeReadiness.AmmoReadyToLaunch(true, 0, 0, 0), Is.True);
			Assert.That(AirframeReadiness.AmmoReadyToFight(false, 0, 0), Is.True);
			Assert.That(AirframeReadiness.AmmoReadyToFight(true, 0, 0), Is.True);
		}
	}
}
