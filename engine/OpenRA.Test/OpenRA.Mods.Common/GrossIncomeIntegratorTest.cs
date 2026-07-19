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

using NUnit.Framework;
using OpenRA.Mods.Common.Tournament;

namespace OpenRA.Test
{
	/// <summary>
	/// Covers the S1 economy metric accumulator (BotVsBotMatchWatcher, verdict_version 3).
	/// GrossIncomeIntegrator integrates PlayerResources.TotalBuildingIncome — paid once
	/// per PassiveIncomeInterval ticks — into cumulative GROSS cash granted, ignoring
	/// upkeep. Pure math: no World/Actor setup needed.
	/// </summary>
	[TestFixture]
	public class GrossIncomeIntegratorTest
	{
		[Test]
		public void FreshIntegratorIsZero()
		{
			var g = new GrossIncomeIntegrator();
			Assert.That(g.Value, Is.EqualTo(0L));
		}

		[Test]
		public void NoIncomeAccumulatesNothing()
		{
			// A player who never captures a derrick (TotalBuildingIncome stays 0) — e.g. the
			// Normal control — must read gross 0 across the whole match.
			var g = new GrossIncomeIntegrator();
			for (var t = 0; t < 7500; t++)
				g.Tick(0f, 50);
			Assert.That(g.Value, Is.EqualTo(0L));
		}

		[Test]
		public void OneDerrickOverIntervalGrantsAmount()
		{
			// $50 income paid every 50-tick interval → exactly $50 accrued over one interval.
			var g = new GrossIncomeIntegrator();
			for (var t = 0; t < 50; t++)
				g.Tick(50f, 50);
			Assert.That(g.Value, Is.EqualTo(50L));
		}

		[Test]
		public void CapturedDerrickHeldToMatchEndMatchesSmokeExpectation()
		{
			// Mirrors the S1 smoke: capture ~t1550, hold a $50/interval (50-tick) derrick to
			// the 7500-tick limit → ~$50 per interval for the remaining ~5950 ticks ≈ $5950.
			var g = new GrossIncomeIntegrator();
			for (var t = 0; t < 7500; t++)
			{
				var income = t >= 1550 ? 50f : 0f;
				g.Tick(income, 50);
			}

			Assert.That(g.Value, Is.EqualTo((7500 - 1550) * 50L / 50L)); // 5950
		}

		[Test]
		public void RateIsIndependentOfIntervalLength()
		{
			// Same $/tick rate reached two ways: $50 over 50 ticks vs $100 over 100 ticks.
			var a = new GrossIncomeIntegrator();
			var b = new GrossIncomeIntegrator();
			for (var t = 0; t < 1000; t++)
			{
				a.Tick(50f, 50);
				b.Tick(100f, 100);
			}

			Assert.That(a.Value, Is.EqualTo(1000L));
			Assert.That(b.Value, Is.EqualTo(1000L));
		}

		[Test]
		public void NonPositiveIntervalContributesNothing()
		{
			var g = new GrossIncomeIntegrator();
			g.Tick(50f, 0);
			g.Tick(50f, -10);
			Assert.That(g.Value, Is.EqualTo(0L));
		}

		[Test]
		public void OwnershipLossStopsAccrual()
		{
			// Derrick captured then lost back: gross freezes when TotalBuildingIncome drops to 0,
			// exercising the robustness-to-ownership-changes property.
			var g = new GrossIncomeIntegrator();
			for (var t = 0; t < 100; t++)
				g.Tick(50f, 50);   // owned: +$100
			for (var t = 0; t < 500; t++)
				g.Tick(0f, 50);    // lost: no further accrual
			Assert.That(g.Value, Is.EqualTo(100L));
		}
	}
}
