#region Copyright & License Information
/*
 * WW3MOD EngineerTaskingMath tests — the E6 employment rules for the @experimental bot.
 *
 * These cover the failures that would each produce a bot that LOOKS like it uses its engineers: an
 * employment order that never reaches the one job nothing else can do, a breach that walks a lone
 * specialist at a bunker nobody is fighting over, a fog-legality guard that admits a contact the bot
 * has not looked at in a minute, and a re-task rule that cancels its own walk every cycle.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class EngineerTaskingMathTest
	{
		// ---------- ChooseEmployment ----------

		[Test]
		public void Employment_BreachOutranksRepairAndScreen()
		{
			// Breach is the only employment with no other provider in the bot: nothing else issues the
			// "C4" order, and the charges die with the engineer. Repair and screen both have fallbacks.
			Assert.That(
				EngineerTaskingMath.ChooseEmployment(true, true, true, true),
				Is.EqualTo(EngineerEmployment.Breach));
		}

		[Test]
		public void Employment_RepairOutranksScreen()
		{
			Assert.That(
				EngineerTaskingMath.ChooseEmployment(false, false, true, true),
				Is.EqualTo(EngineerEmployment.Repair));
		}

		[Test]
		public void Employment_FallsThroughToScreen()
		{
			Assert.That(
				EngineerTaskingMath.ChooseEmployment(false, false, false, true),
				Is.EqualTo(EngineerEmployment.Screen));
		}

		[Test]
		public void Employment_NoneWhenNothingIsAvailable()
		{
			Assert.That(
				EngineerTaskingMath.ChooseEmployment(false, false, false, false),
				Is.EqualTo(EngineerEmployment.None));
		}

		[Test]
		public void Employment_NoChargesFallsBackRatherThanStalling()
		{
			// THE REGRESSION THIS PINS: an engineer who has spent all three charges must keep working.
			// An early implementation that returned None on !canDemolish would park every engineer
			// permanently after his third C4 — and because he is then never re-tasked, never idle, and
			// still ledger-claimed, he would also never be released to go and rearm.
			Assert.Multiple(() =>
			{
				Assert.That(
					EngineerTaskingMath.ChooseEmployment(false, true, true, true),
					Is.EqualTo(EngineerEmployment.Repair));

				Assert.That(
					EngineerTaskingMath.ChooseEmployment(false, true, false, true),
					Is.EqualTo(EngineerEmployment.Screen));
			});
		}

		// ---------- IsBreachViable ----------

		[Test]
		public void Breach_RefusesAStaleSighting()
		{
			// THE FOG-LEGALITY GUARD. The module must hand the engine a real Actor to build the "C4"
			// order (Demolish : Enter only enters TargetType.Actor), and that lookup failing is itself
			// information — it says the believed structure is already dead. Requiring a fresh sighting
			// is what stops the bot learning anything its own eyes have not told it.
			Assert.Multiple(() =>
			{
				// Seen 40 ticks ago against a 50-tick window: still under observation.
				Assert.That(EngineerTaskingMath.IsBreachViable(3, 2, 10, 30, 40, 50), Is.True);

				// Seen 51 ticks ago: the sighting is memory, not observation.
				Assert.That(EngineerTaskingMath.IsBreachViable(3, 2, 10, 30, 51, 50), Is.False);
			});
		}

		[Test]
		public void Breach_FreshnessBoundaryIsInclusive()
		{
			// Exactly at the window is still fresh — the belief store and this module run on independent
			// cadences, so an exclusive bound would drop a contact that was refreshed this very pass.
			Assert.That(EngineerTaskingMath.IsBreachViable(3, 2, 10, 30, 50, 50), Is.True);
		}

		[Test]
		public void Breach_RefusesATargetNobodyIsFightingOver()
		{
			// A defence with none of our troops near it is not blocking anything, and walking a 250-cost
			// unarmoured specialist at it alone is how the charges get donated to the enemy.
			Assert.Multiple(() =>
			{
				Assert.That(EngineerTaskingMath.IsBreachViable(1, 2, 10, 30, 0, 50), Is.False);
				Assert.That(EngineerTaskingMath.IsBreachViable(2, 2, 10, 30, 0, 50), Is.True);
			});
		}

		[Test]
		public void Breach_RefusesATargetBeyondReach()
		{
			Assert.Multiple(() =>
			{
				Assert.That(EngineerTaskingMath.IsBreachViable(3, 2, 30, 30, 0, 50), Is.True);
				Assert.That(EngineerTaskingMath.IsBreachViable(3, 2, 31, 30, 0, 50), Is.False);
			});
		}

		[Test]
		public void Breach_RefusesNegativeAgeAndDistance()
		{
			// A negative age means the contact's LastSeenTick is in the future, which can only be a
			// bookkeeping fault upstream. Failing closed keeps a corrupt record from authorising a walk.
			Assert.Multiple(() =>
			{
				Assert.That(EngineerTaskingMath.IsBreachViable(3, 2, 10, 30, -1, 50), Is.False);
				Assert.That(EngineerTaskingMath.IsBreachViable(3, 2, -1, 30, 0, 50), Is.False);
			});
		}

		// ---------- BreachScore ----------

		[Test]
		public void BreachScore_PressureBeatsProximityAcrossTheWholeRange()
		{
			// THE REGRESSION THIS PINS, AND IT IS AN ARITHMETIC ONE. With a bare `friendly * max`
			// multiplier, one squad at distance 0 scores 30 and two squads at the maximum legal distance
			// 30 also score 30 — a tie at the exact extreme the multiplier exists to separate, resolved
			// by whatever the caller's tie-break happens to be. The +1 span is what makes the ordering
			// hold at the boundary rather than merely in the middle.
			var oneSquadAdjacent = EngineerTaskingMath.BreachScore(1, 0, 30);
			var twoSquadsFarthest = EngineerTaskingMath.BreachScore(2, 30, 30);

			Assert.That(twoSquadsFarthest, Is.GreaterThan(oneSquadAdjacent));
		}

		[Test]
		public void BreachScore_DistanceBreaksTiesAmongEquallyContestedTargets()
		{
			Assert.That(
				EngineerTaskingMath.BreachScore(2, 5, 30),
				Is.GreaterThan(EngineerTaskingMath.BreachScore(2, 12, 30)));
		}

		[Test]
		public void BreachScore_ClampsNegativeInputsRatherThanInvertingTheRanking()
		{
			// A negative distance would otherwise ADD to the score and make a malformed candidate the
			// argmax winner outright.
			Assert.That(
				EngineerTaskingMath.BreachScore(1, -5, 30),
				Is.EqualTo(EngineerTaskingMath.BreachScore(1, 0, 30)));
		}

		// ---------- ShouldRetask ----------

		[Test]
		public void Retask_FirstOrderAlwaysIssues()
		{
			Assert.That(EngineerTaskingMath.ShouldRetask(false, false, false, 0, 200), Is.True);
		}

		[Test]
		public void Retask_HoldsInsideTheSettleWindowEvenWhenTheTargetChanged()
		{
			// THE LIVELOCK THIS PINS. Every order is unqueued and an unqueued order cancels the current
			// activity, so re-targeting an engineer who is most of the way through a Demolish walk throws
			// the walk away WITHOUT spending the charge — the module then re-picks and re-walks, forever,
			// which on screen is an engineer wandering between two buildings and never blowing either.
			Assert.That(EngineerTaskingMath.ShouldRetask(true, false, false, 199, 200), Is.False);
		}

		[Test]
		public void Retask_ReleasesOnceTheWindowElapsesAndSomethingChanged()
		{
			Assert.Multiple(() =>
			{
				// Employment changed.
				Assert.That(EngineerTaskingMath.ShouldRetask(true, false, true, 200, 200), Is.True);

				// Target changed.
				Assert.That(EngineerTaskingMath.ShouldRetask(true, true, false, 200, 200), Is.True);
			});
		}

		[Test]
		public void Retask_StaysSilentWhenNothingChanged()
		{
			// Re-issuing the identical order would cancel and rebuild the same activity for nothing —
			// and for a parking employment it also drops the repair armament's auto-acquired target.
			Assert.That(EngineerTaskingMath.ShouldRetask(true, true, true, 10000, 200), Is.False);
		}

		// ---------- AnchorMovedMaterially ----------

		[Test]
		public void Anchor_IgnoresJitterAndActsOnRealDisplacement()
		{
			Assert.Multiple(() =>
			{
				Assert.That(EngineerTaskingMath.AnchorMovedMaterially(2, 4), Is.False);
				Assert.That(EngineerTaskingMath.AnchorMovedMaterially(4, 4), Is.True);
				Assert.That(EngineerTaskingMath.AnchorMovedMaterially(9, 4), Is.True);
			});
		}

		[Test]
		public void Anchor_ZeroShiftIsNeverMaterialEvenWithDampingOff()
		{
			// With minShiftCells 0 the damping is off, but "the anchor did not move at all" must still
			// not re-order: that is the every-cycle re-issue the damping exists to prevent, and it would
			// come back the moment someone set the knob to zero to disable the FILTERING.
			Assert.That(EngineerTaskingMath.AnchorMovedMaterially(0, 0), Is.False);
		}

		// ---------- CentroidAxis ----------

		[Test]
		public void Centroid_RoundsToNearestRatherThanTruncating()
		{
			Assert.Multiple(() =>
			{
				// 5/2 = 2.5 -> 3, not 2.
				Assert.That(EngineerTaskingMath.CentroidAxis(5, 2), Is.EqualTo(3));

				// 7/3 = 2.33 -> 2.
				Assert.That(EngineerTaskingMath.CentroidAxis(7, 3), Is.EqualTo(2));

				// Exact division is unaffected.
				Assert.That(EngineerTaskingMath.CentroidAxis(8, 4), Is.EqualTo(2));
			});
		}

		[Test]
		public void Centroid_TruncationBiasWouldFakeAnAnchorShift()
		{
			// THE COUPLING THIS PINS, which is why the rounding is not cosmetic. Three units on cells
			// 10, 11, 11 truncate to 10 while the true centroid is 10.67; drop one and 10, 11 truncate
			// to 10 as well — but with rounding the pair reads 11, and the difference between the two
			// readings is what AnchorMovedMaterially would see. Pinning the rounded values is what stops
			// a group standing still from re-tasking its engineer every time a member dies.
			Assert.Multiple(() =>
			{
				Assert.That(EngineerTaskingMath.CentroidAxis(10 + 11 + 11, 3), Is.EqualTo(11));
				Assert.That(EngineerTaskingMath.CentroidAxis(10 + 11, 2), Is.EqualTo(11));
			});
		}

		[Test]
		public void Centroid_EmptySetIsHarmless()
		{
			Assert.That(EngineerTaskingMath.CentroidAxis(0, 0), Is.EqualTo(0));
		}

		[Test]
		public void Centroid_RoundsAwayFromZeroOnBothSidesOfTheOrigin()
		{
			// Symmetry, so the bias cannot flip sign across the origin. Map cells are non-negative in
			// practice, which is exactly why an untested sign convention here would never be noticed.
			Assert.Multiple(() =>
			{
				Assert.That(EngineerTaskingMath.CentroidAxis(5, 2), Is.EqualTo(3));
				Assert.That(EngineerTaskingMath.CentroidAxis(-5, 2), Is.EqualTo(-3));
			});
		}
	}
}
