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
	/// Covers the pure arithmetic behind Supply Route collapse acceleration: how far a defending team
	/// has been ground down (CollapseWeakness) and how fast that makes its control/defeat bar drain
	/// (ContestTicksToFull), plus the rate truncation that turns a requested duration into a realised
	/// one (BarRate).
	///
	/// Durations here are REAL SECONDS at the mod's actual tick rate. mod.yaml:358 selects the
	/// `default` game speed and mod.yaml:381 gives it Timestep: 60ms, i.e. 16.67 ticks/second — not
	/// the 25 tps the trait's own comments claimed until this change, which understated every quoted
	/// duration by 1.5x.
	///
	/// The invariant this fixture exists to protect is NoSpeedupWhileTeamIsHealthy: a contested game
	/// between two live armies must drain byte-identically to the pre-collapse formula. The
	/// acceleration is only allowed to touch a team that has already been beaten.
	/// </summary>
	[TestFixture]
	public class SupplyRouteCollapseTest
	{
		// The shipped SUPPLYROUTE values (mods/ww3mod/rules/ingame/structures.yaml:260-271).
		const int BaseTicks = 1500;
		const int ReferenceValue = 2500;
		const int MinTicks = 500;
		const int CollapseMinTicks = 80;
		const int MaxCollapseSpeedup = 800;
		const int CollapseThreshold = 5000;
		const int BarMax = 100000;

		// mod.yaml:381 — Timestep 60ms.
		const double MsPerTick = 60.0;

		static int Ticks(int enemySurplus, int teamValue)
		{
			return SupplyRouteContestation.ContestTicksToFull(
				enemySurplus,
				SupplyRouteContestation.CollapseWeakness(teamValue, CollapseThreshold),
				BaseTicks, ReferenceValue, MinTicks, CollapseMinTicks, MaxCollapseSpeedup);
		}

		// Seconds to drain one full bar as the game ACTUALLY runs it: the requested tick count is
		// turned into a per-tick rate (truncating), and the bar then needs ceil(BarMax / rate) ticks.
		static double RealSeconds(int enemySurplus, int teamValue)
		{
			var rate = SupplyRouteContestation.BarRate(BarMax, Ticks(enemySurplus, teamValue));
			var ticks = (BarMax + rate - 1) / rate;
			return ticks * MsPerTick / 1000.0;
		}

		// --- Weakness ramp ---

		[Test]
		public void HealthyTeamHasNoWeakness()
		{
			Assert.That(SupplyRouteContestation.CollapseWeakness(CollapseThreshold, CollapseThreshold), Is.EqualTo(0));
			Assert.That(SupplyRouteContestation.CollapseWeakness(40000, CollapseThreshold), Is.EqualTo(0),
				"A team above the threshold must clamp to healthy, never to a negative speedup.");
		}

		[Test]
		public void EmptyTeamIsFullyWeak()
		{
			Assert.That(SupplyRouteContestation.CollapseWeakness(0, CollapseThreshold), Is.EqualTo(100));
		}

		[Test]
		public void WeaknessRampIsLinearAndContinuous()
		{
			Assert.That(SupplyRouteContestation.CollapseWeakness(2500, CollapseThreshold), Is.EqualTo(50));
			Assert.That(SupplyRouteContestation.CollapseWeakness(1000, CollapseThreshold), Is.EqualTo(80));

			// No cliff: one cheap survivor must not be worth a step change in the rate.
			var lone = SupplyRouteContestation.CollapseWeakness(30, CollapseThreshold);
			Assert.That(lone, Is.EqualTo(99));
			Assert.That(SupplyRouteContestation.CollapseWeakness(0, CollapseThreshold) - lone, Is.LessThanOrEqualTo(1));
		}

		[Test]
		public void WeaknessIsMonotonic()
		{
			var previous = 101;
			for (var teamValue = 0; teamValue <= CollapseThreshold * 2; teamValue += 37)
			{
				var w = SupplyRouteContestation.CollapseWeakness(teamValue, CollapseThreshold);
				Assert.That(w, Is.InRange(0, 100));
				Assert.That(w, Is.LessThanOrEqualTo(previous), "Weakness must never rise as the team gets stronger.");
				previous = w;
			}
		}

		// --- The invariant: a contested game between live armies is untouched ---

		[Test]
		public void NoSpeedupWhileTeamIsHealthy()
		{
			foreach (var surplus in new[] { 1, 25, 100, 900, 2500, 7500, 15000, 100000 })
			{
				// The formula as it stood before collapse acceleration existed.
				var legacy = (int)System.Math.Max(MinTicks, (long)BaseTicks * ReferenceValue / surplus);

				Assert.That(Ticks(surplus, CollapseThreshold), Is.EqualTo(legacy),
					$"Surplus {surplus} against a healthy team must be byte-identical to the old rate.");
				Assert.That(Ticks(surplus, 40000), Is.EqualTo(legacy));
			}
		}

		// --- The two endpoints the design was asked for, in real seconds ---

		[Test]
		public void BeatenTeamCollapsesInSeconds()
		{
			// A committed finishing force (3x ReferenceValue) against a team with nothing mobile left.
			Assert.That(RealSeconds(7500, 0), Is.EqualTo(4.8).Within(0.3),
				"'Very fast, not instantly, but in seconds' — the control bar at the collapse floor.");

			// The bar the loser is made to watch afterwards is the defeat bar, and with zero team value
			// the lockout shortcut fills it in LockoutCollapseTicks (17) rather than at this rate.
			Assert.That(17 * MsPerTick / 1000.0, Is.EqualTo(1.0).Within(0.1));
		}

		[Test]
		public void HealthyTeamStillTakesTheFullNinetySeconds()
		{
			Assert.That(RealSeconds(ReferenceValue, CollapseThreshold), Is.EqualTo(91.0).Within(0.5),
				"BaseTicks 1500 at 16.67 tps is 90s (91s realised after rate truncation), not the 60s the old comment claimed.");
			Assert.That(RealSeconds(7500, CollapseThreshold), Is.EqualTo(30.0).Within(0.5),
				"MinTicks 500 at 16.67 tps is 30s, not 20s.");
		}

		// --- Anti-grief: acceleration scales the attacker's rate, it does not replace it ---

		[Test]
		public void LoneScoutCannotSnipeAnEmptyTeam()
		{
			// Every player starts a match at zero army value on the default lobby setting
			// (StartingUnitsClass = "none", SpawnStartingUnits.cs:24),
			// and rotating units out for a refund returns them there voluntarily. If weakness REPLACED
			// the attacker term instead of scaling it, one 100-value scout would end such a player in
			// seconds. Scaled, it still costs minutes.
			Assert.That(RealSeconds(100, 0), Is.GreaterThan(120.0),
				"A single cheap unit must never become a way to win.");
		}

		[Test]
		public void OneSurvivingTruckDelaysButDoesNotBlock()
		{
			var empty = RealSeconds(7500, 0);
			var truck = RealSeconds(7500, 30);

			Assert.That(truck, Is.GreaterThanOrEqualTo(empty),
				"A survivor must be worth something.");
			Assert.That(truck, Is.LessThan(empty + 3.0),
				"…but one worthless leftover must not restore the full 30s stall.");
		}

		[Test]
		public void AccelerationNeverSlowsTheBar()
		{
			foreach (var surplus in new[] { 100, 900, 2500, 7500, 15000 })
			{
				var healthy = Ticks(surplus, CollapseThreshold);
				var previous = healthy;
				for (var teamValue = CollapseThreshold; teamValue >= 0; teamValue -= 50)
				{
					var ticks = Ticks(surplus, teamValue);
					Assert.That(ticks, Is.LessThanOrEqualTo(previous),
						$"Surplus {surplus}: a weaker team must never take longer to overrun.");
					Assert.That(ticks, Is.LessThanOrEqualTo(healthy));
					Assert.That(ticks, Is.GreaterThan(0));
					previous = ticks;
				}
			}
		}

		[Test]
		public void CollapseFloorIsRespected()
		{
			// No attacker, however overwhelming, drops below the floor for a given weakness.
			Assert.That(Ticks(1000000, 0), Is.GreaterThanOrEqualTo(CollapseMinTicks));
			Assert.That(Ticks(1000000, CollapseThreshold), Is.GreaterThanOrEqualTo(MinTicks));
		}

		[Test]
		public void ThresholdOfZeroDisablesAcceleration()
		{
			// Defensive: a mod that tunes the feature off by zeroing the threshold must not divide by it.
			Assert.That(SupplyRouteContestation.CollapseWeakness(0, 0), Is.EqualTo(0));
			Assert.That(SupplyRouteContestation.CollapseWeakness(9999, 0), Is.EqualTo(0));
		}

		/// <summary>
		/// The design table, in real seconds, for one full bar. Two bars (control, then defeat) are
		/// drained per capture, so a full overrun is twice these — except at zero team value, where the
		/// lockout shortcut fills the defeat bar in ~1s instead.
		///
		/// team value | surplus 2500 | surplus 7500
		/// -----------+--------------+-------------
		///      5000+ |      91.0s   |     30.0s     (unchanged — the contested case)
		///       2500 |      20.0s   |     17.5s
		///       1000 |      13.7s   |      9.9s
		///          0 |      11.3s   |      4.8s
		/// </summary>
		[Test]
		public void DesignTableHoldsInRealSeconds()
		{
			Assert.That(RealSeconds(2500, 5000), Is.EqualTo(91.0).Within(0.5));
			Assert.That(RealSeconds(2500, 2500), Is.EqualTo(20.0).Within(0.5));
			Assert.That(RealSeconds(2500, 1000), Is.EqualTo(13.7).Within(0.5));
			Assert.That(RealSeconds(2500, 0), Is.EqualTo(11.3).Within(0.5));

			Assert.That(RealSeconds(7500, 5000), Is.EqualTo(30.0).Within(0.5));
			Assert.That(RealSeconds(7500, 2500), Is.EqualTo(17.4).Within(0.5));
			Assert.That(RealSeconds(7500, 1000), Is.EqualTo(9.9).Within(0.5));
			Assert.That(RealSeconds(7500, 0), Is.EqualTo(4.8).Within(0.5));
		}
	}
}
