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

		[TestCase(ApacheFlee, ApacheReEngage)]
		[TestCase(ChinookFlee, ChinookReEngage)]
		[TestCase(ScoutFlee, ScoutReEngage)]
		public void WithARepairHostTheRecoveryBarIsUnchanged(int fleeBar, int recoveryBar)
		{
			Assert.That(AirframeReadiness.CommitHealthBar(true, recoveryBar, fleeBar), Is.EqualTo(recoveryBar));
		}

		[TestCase(ApacheFlee, ApacheReEngage)]
		[TestCase(ChinookFlee, ChinookReEngage)]
		[TestCase(ScoutFlee, ScoutReEngage)]
		public void WithNoRepairHostTheFleeBarApplies(int fleeBar, int recoveryBar)
		{
			Assert.That(AirframeReadiness.CommitHealthBar(false, recoveryBar, fleeBar), Is.EqualTo(fleeBar));
		}

		/// <summary>
		/// The defect itself, at the real numbers. An Apache that has taken 300 of its 800 HP is at
		/// 62% — below ReEngageHealthPercent 75, above FleeHealthPercent 35. Under the recovery bar
		/// it is unlaunchable, and with nothing able to repair it, it stays that way for the match
		/// while still being too healthy for any flee path to retire it.
		/// </summary>
		[Test]
		public void TheDeadBandBetweenFleeAndRecoveryIsWhereAirframesUsedToStrand()
		{
			var hp = HealthPercent(ApacheMaxHp - 300, ApacheMaxHp);
			Assert.That(hp, Is.EqualTo(62));

			Assert.That(hp, Is.LessThan(AirframeReadiness.CommitHealthBar(true, ApacheReEngage, ApacheFlee)));
			Assert.That(hp, Is.GreaterThanOrEqualTo(AirframeReadiness.CommitHealthBar(false, ApacheReEngage, ApacheFlee)));
		}

		/// <summary>
		/// The transport band is wider still: Chinook flees at 60 and re-engages at 90, so a single
		/// 100 HP hit out of 600 (83%) parks it permanently.
		/// </summary>
		[Test]
		public void OneChipOfDamageStrandsATransportUnderTheRecoveryBar()
		{
			var hp = HealthPercent(ChinookMaxHp - 100, ChinookMaxHp);
			Assert.That(hp, Is.EqualTo(83));

			Assert.That(hp, Is.LessThan(AirframeReadiness.CommitHealthBar(true, ChinookReEngage, ChinookFlee)));
			Assert.That(hp, Is.GreaterThanOrEqualTo(AirframeReadiness.CommitHealthBar(false, ChinookReEngage, ChinookFlee)));
		}

		/// <summary>
		/// Below the flee bar the airframe is not committed either way — the fix opens the dead band,
		/// it does not remove the floor.
		/// </summary>
		[Test]
		public void BelowTheFleeBarTheAirframeIsStillNotCommitted()
		{
			var hp = HealthPercent(240, ApacheMaxHp);
			Assert.That(hp, Is.EqualTo(30));
			Assert.That(hp, Is.LessThan(AirframeReadiness.CommitHealthBar(false, ApacheReEngage, ApacheFlee)));
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
