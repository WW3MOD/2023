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
	/// Pins the pure lane geometry for the Stage-4 bot lane-ambush consumer (PIPELINE item 8). The
	/// world-touching parts (finding the friendly/enemy anchors, snapping to a passable cell, claiming
	/// units, granting the gate) live in <see cref="LaneAmbushBotModule"/>; the geometry + viability
	/// decisions are <see cref="AmbushLaneMath"/> so they can be exercised here with no simulation harness.
	///
	/// Source of truth: engine/OpenRA.Mods.Common/Traits/BotModules/LaneAmbushBotModule.cs
	/// </summary>
	[TestFixture]
	public class AmbushLaneMathTest
	{
		// ── PostPosition: interpolate friendly -> enemy by percent ──

		[Test]
		public void PostAtZeroPercentIsTheFriendlyAnchor()
		{
			var f = new WPos(1024, 2048, 0);
			var e = new WPos(9024, 2048, 0);
			Assert.That(AmbushLaneMath.PostPosition(f, e, 0), Is.EqualTo(f));
		}

		[Test]
		public void PostAtHundredPercentIsTheEnemyAnchor()
		{
			var f = new WPos(1024, 2048, 0);
			var e = new WPos(9024, 6048, 0);
			Assert.That(AmbushLaneMath.PostPosition(f, e, 100), Is.EqualTo(e));
		}

		[Test]
		public void PostAtFiftyPercentIsTheMidpoint()
		{
			var f = new WPos(0, 0, 0);
			var e = new WPos(8000, 4000, 0);
			Assert.That(AmbushLaneMath.PostPosition(f, e, 50), Is.EqualTo(new WPos(4000, 2000, 0)));
		}

		[Test]
		public void DefaultFortyPercentStaysOnOurSideOfTheMidline()
		{
			// The shipped default (PostFractionPct = 40): the post sits nearer our beachhead than the
			// enemy's — concealed in our own territory, on the corridor attackers commit down.
			var f = new WPos(0, 0, 0);
			var e = new WPos(10000, 0, 0);
			var p = AmbushLaneMath.PostPosition(f, e, 40);
			Assert.That(p, Is.EqualTo(new WPos(4000, 0, 0)));

			var distToFriendly = (p - f).HorizontalLength;
			var distToEnemy = (p - e).HorizontalLength;
			Assert.That(distToFriendly, Is.LessThan(distToEnemy), "post must be closer to our own beachhead");
		}

		[Test]
		public void PostInterpolatesAllThreeAxes()
		{
			var f = new WPos(100, 200, 300);
			var e = new WPos(1100, 1200, 1300);
			Assert.That(AmbushLaneMath.PostPosition(f, e, 50), Is.EqualTo(new WPos(600, 700, 800)));
		}

		[Test]
		public void PostFractionIsClampedToRange()
		{
			var f = new WPos(0, 0, 0);
			var e = new WPos(1000, 0, 0);

			// Negative clamps to 0 (the friendly anchor); over 100 clamps to 100 (the enemy anchor).
			Assert.That(AmbushLaneMath.PostPosition(f, e, -30), Is.EqualTo(f));
			Assert.That(AmbushLaneMath.PostPosition(f, e, 250), Is.EqualTo(e));
		}

		[Test]
		public void PostHandlesNegativeDeltaDirection()
		{
			// Enemy is "behind" us on an axis (negative delta): interpolation is still linear + deterministic.
			var f = new WPos(10000, 0, 0);
			var e = new WPos(0, 0, 0);
			Assert.That(AmbushLaneMath.PostPosition(f, e, 40), Is.EqualTo(new WPos(6000, 0, 0)));
		}

		[Test]
		public void PostSurvivesLargeMapDeltaWithoutOverflow()
		{
			// The (long) cast guards (delta × percent) from 32-bit overflow on a large map. Here
			// delta = 50,000,000 and pct = 50 ⇒ delta*pct = 2.5e9, which overflows int32 (max 2.147e9);
			// the long path yields 25,000,000 exactly. A regression to a bare int multiply would wrap here.
			var f = new WPos(0, 0, 0);
			var e = new WPos(50_000_000, 0, 0);
			Assert.That(AmbushLaneMath.PostPosition(f, e, 50), Is.EqualTo(new WPos(25_000_000, 0, 0)));
		}

		[Test]
		public void PostTruncatesTowardZeroSymmetrically()
		{
			// Integer division truncates toward zero (C# semantics), so a positive and the mirrored negative
			// delta round the SAME way (both drop the .3), keeping interpolation direction-symmetric.
			var origin = new WPos(0, 0, 0);
			Assert.That(AmbushLaneMath.PostPosition(origin, new WPos(10, 0, 0), 33), Is.EqualTo(new WPos(3, 0, 0)));
			Assert.That(AmbushLaneMath.PostPosition(origin, new WPos(-10, 0, 0), 33), Is.EqualTo(new WPos(-3, 0, 0)));
		}

		[Test]
		public void LaneViableWhenSeparationAndThresholdBothZero()
		{
			// The zero/zero boundary: separation 0 with a 0 floor is viable (>= is inclusive).
			Assert.That(AmbushLaneMath.LaneIsViable(0, 0), Is.True);
		}

		// ── LaneIsViable: reject degenerate near-adjacent beachheads ──

		[Test]
		public void LaneViableAtOrAboveMinSeparation()
		{
			Assert.That(AmbushLaneMath.LaneIsViable(12, 12), Is.True);   // exactly at the floor
			Assert.That(AmbushLaneMath.LaneIsViable(40, 12), Is.True);   // well separated
		}

		[Test]
		public void LaneNotViableBelowMinSeparation()
		{
			// Beachheads basically adjacent ⇒ the interpolated post would sit on our own base ⇒ reject.
			Assert.That(AmbushLaneMath.LaneIsViable(5, 12), Is.False);
			Assert.That(AmbushLaneMath.LaneIsViable(0, 12), Is.False);
		}

		[Test]
		public void LaneViabilityFloorsNegativeThresholdAtZero()
		{
			// A negative threshold is floored to 0, so any non-negative separation is viable.
			Assert.That(AmbushLaneMath.LaneIsViable(0, -5), Is.True);
			Assert.That(AmbushLaneMath.LaneIsViable(3, -5), Is.True);
		}

		// ==================================================================================
		// LaneMayPost — MINIMUM MANNING (PIPELINE item 64).
		//
		// Measured twice, at tick 100 of two independent runs: this module claimed the very
		// first reinforcement of the match and posted it ALONE, 40% of the way to the enemy
		// beachhead, before PoiOffensiveBotModule's free pool had ever seen it. The lone unit
		// the user reported is this module's.
		// ==================================================================================

		[Test]
		public void MinManningOfZeroIsByteIdenticalToTheUngatedModule()
		{
			// The C# default, and what @stable read before this batch. Must never withhold —
			// including at zero units, where the caller's own empty-lane retire is what applies.
			for (var units = 0; units <= 8; units++)
				Assert.That(AmbushLaneMath.LaneMayPost(units, 0), Is.True, $"min=0 must never withhold (units={units})");

			Assert.That(AmbushLaneMath.LaneMayPost(1, -2), Is.True, "negative is OFF, not always-hold");
		}

		[Test]
		public void AHalfMannedLanePostsNobody()
		{
			// The shipped setting: min 2, equal to UnitsPerAmbush — post a full lane or none.
			Assert.Multiple(() =>
			{
				Assert.That(AmbushLaneMath.LaneMayPost(1, 2), Is.False,
					"one unit available — this is the lone tank at tick 100 of runs 260905_180211 and _183118");
				Assert.That(AmbushLaneMath.LaneMayPost(2, 2), Is.True, "at the minimum is enough; the gate is >=");
				Assert.That(AmbushLaneMath.LaneMayPost(5, 2), Is.True);
			});
		}

		[Test]
		public void TheGateIsMonotonicSoAFillingLaneCannotOscillate()
		{
			// Same anti-churn property the offensive free-pool gate carries: the answer depends only
			// on the count, so a lane that gains a unit can never re-enter the refusal. Combined with
			// the caller refusing at the RECRUIT step (nothing committed, granted or ordered), an
			// under-manned lane costs exactly zero orders per eval rather than a claim/release cycle.
			for (var min = 0; min <= 5; min++)
			{
				var posted = false;
				for (var units = 0; units <= 10; units++)
				{
					if (AmbushLaneMath.LaneMayPost(units, min))
						posted = true;
					else
						Assert.That(posted, Is.False,
							$"min={min}: lane grew to {units} and the gate CLOSED again — not monotonic");
				}
			}
		}

		// ── ReserveAllowance: the army-share reserve (PIPELINE item 86) ──
		//
		// The measured defect: three eligible units, offense floor 2, no axis. Unbounded the lane takes two of
		// them (the army's only MBT among them) and leaves offense at one — below its own floor, which it then
		// correctly refuses to advance. The reserve is the arithmetic that stops the split.

		[Test]
		public void ReserveIsUnboundedWhenDisabled()
		{
			// The C# default. Every term below is the failing opening; only `enabled` differs.
			Assert.That(
				AmbushLaneMath.ReserveAllowance(false, true, true, 3, 2, false),
				Is.EqualTo(int.MaxValue));
		}

		[Test]
		public void ReserveIsUnboundedWithNoOffensiveModule()
		{
			// Nothing to reserve FOR — a profile running ambush without the offensive stager is unaffected.
			Assert.That(
				AmbushLaneMath.ReserveAllowance(true, false, false, 0, 0, false),
				Is.EqualTo(int.MaxValue));
		}

		[Test]
		public void ReserveIsUnboundedWhenOffenseAppliesNoFloor()
		{
			// min <= 0 is FreePoolMayAdvance's own off-switch, and it is also what EffectiveFreePoolMinAdvance-
			// Units reports when forward staging is off. Checked BEFORE the snapshot term on purpose: this is
			// configuration, knowable without a pool count, and must not fall into the withhold-on-unknown path.
			Assert.That(AmbushLaneMath.ReserveAllowance(true, true, true, 3, 0, false), Is.EqualTo(int.MaxValue));
			Assert.That(AmbushLaneMath.ReserveAllowance(true, true, false, 0, 0, false), Is.EqualTo(int.MaxValue));
			Assert.That(AmbushLaneMath.ReserveAllowance(true, true, true, 3, -1, false), Is.EqualTo(int.MaxValue));
		}

		[Test]
		public void ReserveIsUnboundedWhileAnAxisIsLive()
		{
			// FreePoolMayAdvance waives the floor whenever an axis exists, so there is no floor to be left
			// below. This is what keeps the reserve aimed at the opening rather than at every reinforcement.
			Assert.That(
				AmbushLaneMath.ReserveAllowance(true, true, true, 3, 2, true),
				Is.EqualTo(int.MaxValue));
		}

		[Test]
		public void ReserveWithholdsEverythingWhenTheSnapshotIsUnknown()
		{
			// Offense has a floor but has not computed a pool, so whether a take would breach it is unknowable.
			// Fails toward offense, like MinUnitsPerAmbush.
			Assert.That(AmbushLaneMath.ReserveAllowance(true, true, false, 0, 2, false), Is.EqualTo(0));
		}

		[Test]
		public void ReserveAboveTheFloorAllowsOnlyTheSurplus()
		{
			// Five free, floor 2 ⇒ three are spare. Taking the fourth would put offense at 1.
			Assert.That(AmbushLaneMath.ReserveAllowance(true, true, true, 5, 2, false), Is.EqualTo(3));
		}

		[Test]
		public void ReserveAtTheFloorAllowsNothing()
		{
			// Offense sits exactly on its floor: every remaining unit is load-bearing.
			Assert.That(AmbushLaneMath.ReserveAllowance(true, true, true, 2, 2, false), Is.EqualTo(0));
		}

		[Test]
		public void ReserveBelowTheFloorAllowsNothingAndNeverGoesNegative()
		{
			// The state the measured run was ALREADY in at t226 ([exp-ledger] free=1, min=2). A negative
			// allowance would read as "owed" and, min'd against a need, would hand the lane units.
			Assert.That(AmbushLaneMath.ReserveAllowance(true, true, true, 1, 2, false), Is.EqualTo(0));
			Assert.That(AmbushLaneMath.ReserveAllowance(true, true, true, 0, 2, false), Is.EqualTo(0));
		}

		[Test]
		public void ReserveOnTheMeasuredOpeningRefusesTheSecondUnitAndSoTheWholeLane()
		{
			// Run 260906_091912 verbatim: three eligible units, offense floor 2, axes=0. The lane wanted
			// UnitsPerAmbush=2 and got an allowance of ONE — and since MinUnitsPerAmbush is also 2, a lane that
			// can be filled to only one is refused by minimum manning, so NOBODY is posted. That composition is
			// the fix: the reserve caps availability and manning then refuses the part-lane.
			var allowance = AmbushLaneMath.ReserveAllowance(true, true, true, 3, 2, false);
			Assert.That(allowance, Is.EqualTo(1));
			Assert.That(AmbushLaneMath.LaneMayPost(0 + allowance, 2), Is.False);
		}

		[Test]
		public void ReserveLeavesALargePoolUntouchedForAFullLane()
		{
			// The unchanged case, and the reason this is aimed at the opening: a twelve-unit pool clears the
			// floor with room to spare, so both lanes fill and the module behaves as it always did.
			var allowance = AmbushLaneMath.ReserveAllowance(true, true, true, 12, 2, false);
			Assert.That(allowance, Is.EqualTo(10));
			Assert.That(AmbushLaneMath.LaneMayPost(0 + System.Math.Min(2, allowance), 2), Is.True);
		}
	}
}
