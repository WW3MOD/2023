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
	/// Covers the two pure decisions behind the Supply Route warnings: whether production is actually
	/// being slowed at a given bar level (IsProductionSlowed), and whether the bar has recovered far
	/// enough to let the warnings fire again (ShouldRearmWarning).
	///
	/// Neither notification itself is observable from the autotest harness — a transient text line
	/// goes to TextNotificationsManager and the Lua bindings expose no reader for it — so these
	/// fixtures pin the decisions instead of the announcements. What they cannot see is that the
	/// call sites are wired to them; see RED-ARM.md for what a human would look and listen for.
	///
	/// The invariant this fixture exists to protect is RearmDefaultIsTodaysBehaviour: this trait is
	/// shared by ModularBot@stable, the benchmark control, so the default RearmThresholdPercent must
	/// reproduce the shipped `controlBar >= BarMax` reset exactly rather than approximately.
	/// </summary>
	[TestFixture]
	public class SupplyRouteWarningTest
	{
		// The shipped SUPPLYROUTE values (mods/ww3mod/rules/ingame/structures.yaml).
		const int BarMax = 100000;
		const int SlowdownThreshold = 50;

		// The C# default on SupplyRouteContestationInfo. No YAML sets it.
		const int DefaultRearm = 100;

		// --- IsProductionSlowed mirrors GetProductionSpeedModifier ---

		[Test]
		public void FullBarIsNotSlowed()
		{
			Assert.That(SupplyRouteContestation.IsProductionSlowed(BarMax, BarMax, SlowdownThreshold), Is.False);
		}

		[Test]
		public void ThresholdItselfIsStillFullSpeed()
		{
			// GetProductionSpeedModifier returns 100 on `barPercent >= SlowdownThreshold`, so 50% is
			// the last full-speed level and the call-out must NOT fire there. Announcing a slowdown
			// the player is not experiencing is worse than the silence this replaces.
			Assert.That(SupplyRouteContestation.IsProductionSlowed(BarMax / 2, BarMax, SlowdownThreshold), Is.False,
				"50% is exactly full speed; the call-out fires one bar unit later.");

			Assert.That(SupplyRouteContestation.IsProductionSlowed((BarMax / 2) - 1, BarMax, SlowdownThreshold), Is.True,
				"One unit below the threshold is the first slowed tick.");
		}

		[Test]
		public void EmptyBarIsSlowed()
		{
			// Hard lockout: GetProductionSpeedModifier returns 0 on controlBar <= 0. That is past
			// slowed, not short of it.
			Assert.That(SupplyRouteContestation.IsProductionSlowed(0, BarMax, SlowdownThreshold), Is.True);
			Assert.That(SupplyRouteContestation.IsProductionSlowed(-1, BarMax, SlowdownThreshold), Is.True);
		}

		[Test]
		public void SlowedAgreesWithTheProductionModifierAcrossTheWholeBar()
		{
			// Non-vacuity plus the real claim: the predicate is "the modifier is below 100" at every
			// level, not just at the crossing. Reproduces the modifier's arithmetic independently.
			for (var bar = 0; bar <= BarMax; bar += 250)
			{
				var modifier = bar <= 0
					? 0
					: (bar * 100 / BarMax >= SlowdownThreshold ? 100 : bar * 100 / BarMax * 100 / SlowdownThreshold);

				Assert.That(SupplyRouteContestation.IsProductionSlowed(bar, BarMax, SlowdownThreshold),
					Is.EqualTo(modifier < 100), $"Disagreed with the production modifier at bar={bar}.");
			}
		}

		// --- ShouldRearmWarning ---

		[Test]
		public void RearmDefaultIsTodaysBehaviour()
		{
			// The shipped reset was `if (controlBar >= info.BarMax) wasContested = false;`. At the
			// default the predicate must be that expression and nothing looser — one unit short of
			// full must not re-arm.
			Assert.That(SupplyRouteContestation.ShouldRearmWarning(BarMax, BarMax, DefaultRearm), Is.True);
			Assert.That(SupplyRouteContestation.ShouldRearmWarning(BarMax - 1, BarMax, DefaultRearm), Is.False,
				"99.999% is not full recovery; @stable must keep warning exactly once per match.");

			// The oscillation the item is about: a bar cycling between 40% and 95% never re-arms.
			Assert.That(SupplyRouteContestation.ShouldRearmWarning(BarMax * 95 / 100, BarMax, DefaultRearm), Is.False);
			Assert.That(SupplyRouteContestation.ShouldRearmWarning(BarMax * 40 / 100, BarMax, DefaultRearm), Is.False);
		}

		[Test]
		public void LoweredBandRearmsOnTheBand()
		{
			Assert.That(SupplyRouteContestation.ShouldRearmWarning(BarMax * 90 / 100, BarMax, 90), Is.True);
			Assert.That(SupplyRouteContestation.ShouldRearmWarning((BarMax * 90 / 100) - 1, BarMax, 90), Is.False);

			// The 40-95 oscillation now re-arms at the top of its swing, which is the whole point.
			Assert.That(SupplyRouteContestation.ShouldRearmWarning(BarMax * 95 / 100, BarMax, 90), Is.True);
		}

		[Test]
		public void BandAlwaysLeavesHysteresisAboveTheSlowdownCallOut()
		{
			// Both latches share this field, so the slowdown call-out's hysteresis is
			// (band - SlowdownThreshold). Any band at or below the threshold would let it chatter:
			// re-arm and re-fire at the same bar level. Nothing enforces band > threshold, so this
			// records the relationship a tuner has to respect rather than asserting a guard exists.
			for (var band = SlowdownThreshold + 1; band <= 100; band += 1)
			{
				var rearmAt = band * BarMax / 100;
				Assert.That(SupplyRouteContestation.ShouldRearmWarning(rearmAt, BarMax, band), Is.True);
				Assert.That(SupplyRouteContestation.IsProductionSlowed(rearmAt, BarMax, SlowdownThreshold), Is.False,
					$"A band of {band} re-arms at a level that is already slowed — it would chatter.");
			}
		}

		[Test]
		public void DegenerateInputsDoNotDivideByZero()
		{
			Assert.That(SupplyRouteContestation.ShouldRearmWarning(0, 0, DefaultRearm), Is.True);
			Assert.That(SupplyRouteContestation.IsProductionSlowed(1, 0, SlowdownThreshold), Is.False);

			// Out-of-range bands clamp rather than throwing or inverting.
			Assert.That(SupplyRouteContestation.ShouldRearmWarning(BarMax, BarMax, 250), Is.True);
			Assert.That(SupplyRouteContestation.ShouldRearmWarning(0, BarMax, -5), Is.True);
		}
	}
}
