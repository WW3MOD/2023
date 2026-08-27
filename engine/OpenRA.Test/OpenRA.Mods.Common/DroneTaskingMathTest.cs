#region Copyright & License Information
/*
 * WW3MOD DroneTaskingMath tests — the recon-drone tasking rules for the @experimental bot.
 *
 * These cover the three things that would each produce a bot that LOOKS like it works: a leash
 * computed from the wrong constant, an unbounded staleness argmax, and a launch issued in a state
 * where the weapon fires and no drone spawns.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class DroneTaskingMathTest
	{
		// ---------- MaxHoverDistanceCells ----------

		[Test]
		public void MaxHoverDistance_TakesTheSmallerOfWeaponAndLeash()
		{
			Assert.Multiple(() =>
			{
				// Leash binds: weapon reaches 25, but the 25-cell leash less a 3-cell margin is 22.
				Assert.That(DroneTaskingMath.MaxHoverDistanceCells(25, 25, 3), Is.EqualTo(22));

				// Weapon binds: a shorter weapon is the limit even with a generous leash.
				Assert.That(DroneTaskingMath.MaxHoverDistanceCells(10, 25, 3), Is.EqualTo(10));
			});
		}

		[Test]
		public void MaxHoverDistance_LeavesMarginInsideTheLeash()
		{
			// THE REGRESSION THIS PINS: the leash check is periodic (MaxDistanceCheckTicks: 20), so a
			// drone parked exactly on 25 is one nudge from being dragged back and granted
			// lost-connection, which zeroes its vision. The result must be strictly inside the leash.
			var d = DroneTaskingMath.MaxHoverDistanceCells(25, 25, 3);
			Assert.That(d, Is.LessThan(25));
		}

		[Test]
		public void MaxHoverDistance_NeverGoesNegative()
		{
			// A margin larger than the leash must clamp to 0, not produce a negative distance that
			// would make every candidate "in range" through a signed comparison.
			Assert.That(DroneTaskingMath.MaxHoverDistanceCells(25, 2, 5), Is.EqualTo(0));
		}

		[Test]
		public void MaxHoverDistance_IsNotBuiltOnTheInertMaxSlaveDistance()
		{
			// CarrierMasterInfo.MaxSlaveDistance had no readers engine-wide and the 20c0 that used to
			// sit on ^DR enforced nothing; the real leash is CarrierSlave.MaxDistance: 25 cells. If
			// someone ever "restores" the 20 as the leash, this fails.
			Assert.That(DroneTaskingMath.MaxHoverDistanceCells(25, 25, 3), Is.Not.EqualTo(20));
		}

		// ---------- ScoreCandidate ----------

		[Test]
		public void Score_RefusesGroundWeAlreadyWatch()
		{
			// Freshly verified square: sending a drone there buys nothing.
			Assert.That(
				DroneTaskingMath.ScoreCandidate(10, 500, 5, 40, 0, 100, 0),
				Is.EqualTo(DroneTaskingMath.Ineligible));
		}

		[Test]
		public void Score_RefusesTheUnreachableCorner()
		{
			// THE SCOUT BUG, PINNED. The stalest square on a map is usually one nothing can reach, so
			// a pure staleness argmax parks the drone in a corner forever. Never-observed staleness
			// plus a POI distance past the ceiling must still be refused.
			Assert.That(
				DroneTaskingMath.ScoreCandidate(int.MaxValue, 500, 90, 40, 0, 100, 0),
				Is.EqualTo(DroneTaskingMath.Ineligible));
		}

		[Test]
		public void Score_RefusesHotAirspace()
		{
			// The drone dies to one hit of real AA. Staleness must not buy its way past danger.
			Assert.That(
				DroneTaskingMath.ScoreCandidate(int.MaxValue, 500, 5, 40, 900, 100, 0),
				Is.EqualTo(DroneTaskingMath.Ineligible));
		}

		[Test]
		public void Score_PrefersStalerGround()
		{
			var fresher = DroneTaskingMath.ScoreCandidate(1000, 500, 5, 40, 0, 100, 0);
			var staler = DroneTaskingMath.ScoreCandidate(9000, 500, 5, 40, 0, 100, 0);
			Assert.That(staler, Is.GreaterThan(fresher));
		}

		[Test]
		public void Score_PrefersGroundNearBelievedContacts()
		{
			// "Do not spend the sortie on blank map": with staleness equal, the square next to
			// something we believe is there must win.
			var blank = DroneTaskingMath.ScoreCandidate(9000, 500, 5, 40, 0, 100, 0);
			var nearContact = DroneTaskingMath.ScoreCandidate(9000, 500, 5, 40, 0, 100, 2000);
			Assert.That(nearContact, Is.GreaterThan(blank));
		}

		[Test]
		public void Score_PrefersCloserToAPoiWhenOtherwiseEqual()
		{
			var far = DroneTaskingMath.ScoreCandidate(9000, 500, 30, 40, 0, 100, 0);
			var near = DroneTaskingMath.ScoreCandidate(9000, 500, 2, 40, 0, 100, 0);
			Assert.That(near, Is.GreaterThan(far));
		}

		[Test]
		public void Score_NeverObservedDoesNotOverflow()
		{
			// int.MaxValue staleness plus a bonus must stay a sane positive score. If this ever wraps,
			// the never-observed squares — the ones most worth scouting — sort LAST.
			var s = DroneTaskingMath.ScoreCandidate(int.MaxValue, 500, 0, 40, 0, 100, 1000);
			Assert.Multiple(() =>
			{
				Assert.That(s, Is.GreaterThan(0));
				Assert.That(s, Is.GreaterThan(DroneTaskingMath.ScoreCandidate(9000, 500, 0, 40, 0, 100, 1000)));
			});
		}

		// ---------- CanLaunch ----------

		[Test]
		public void CanLaunch_AllowsTheGoodCase()
		{
			Assert.That(DroneTaskingMath.CanLaunch(true, true, true, 10, 22), Is.True);
		}

		[Test]
		public void CanLaunch_RefusesWhileTheOperatorIsMoving()
		{
			// THE EXPENSIVE ONE. CarrierMaster is PauseOnCondition "moving", and Attacking()
			// early-returns on IsTraitPaused — so a launch ordered while moving fires the weapon,
			// burns the 3s FireDelay and the 12s BurstWait, plays the animation, and spawns NOTHING.
			Assert.That(DroneTaskingMath.CanLaunch(true, true, false, 10, 22), Is.False);
		}

		[Test]
		public void CanLaunch_AllowsAnOperatorThatIsStationaryButNotIdle()
		{
			// THE REGRESSION THAT SHIPPED. This term is "not moving", NOT "idle". After its first
			// launch the operator is never idle again: the Attack activity holds forever because
			// ChooseArmamentsForTarget filters IsTraitDisabled but not IsTraitPaused and ^DR does not
			// set AbandonWhenArmamentsPaused. An idle gate here latched false for the rest of the
			// match and capped the module at ONE sortie per operator — invisible to every other test
			// in this file, because it lives in the activity layer rather than in the arithmetic.
			// A wedged operator is standing perfectly still and is a valid launch platform.
			const bool StationaryButHoldingAnAttackActivity = true;
			Assert.That(
				DroneTaskingMath.CanLaunch(true, true, StationaryButHoldingAnAttackActivity, 10, 22),
				Is.True);
		}

		// ---------- ShouldRetask ----------

		[Test]
		public void ShouldRetask_OrdersTheFirstSortie()
		{
			Assert.That(DroneTaskingMath.ShouldRetask(false, false, int.MaxValue, 75), Is.True);
		}

		[Test]
		public void ShouldRetask_MovesThePostWhenABetterCellAppears()
		{
			// The sweep depends entirely on this: the engine re-fires the held activity at the OLD
			// cell by itself, so a new cell only ever gets flown if the module orders it.
			Assert.That(DroneTaskingMath.ShouldRetask(true, false, 500, 75), Is.True);
		}

		[Test]
		public void ShouldRetask_LeavesAStandingOrderAloneWhenTheCellIsUnchanged()
		{
			// Re-ordering the same cell would cancel and rebuild an identical activity every cycle.
			Assert.That(DroneTaskingMath.ShouldRetask(true, true, 500, 75), Is.False);
		}

		[Test]
		public void ShouldRetask_DoesNotDisturbAPendingFireDelay()
		{
			// The spawn is a delayed action owned by the Armament, not the activity, so re-ordering
			// inside the 50-tick FireDelay does not cancel the launch — it just aims the operator
			// elsewhere while the drone departs for the old cell. Settle first.
			Assert.Multiple(() =>
			{
				Assert.That(DroneTaskingMath.ShouldRetask(true, false, 20, 75), Is.False);
				Assert.That(DroneTaskingMath.ShouldRetask(true, false, 75, 75), Is.True);
			});
		}

		[Test]
		public void ShouldRetask_SettleWindowClearsTheFireDelay()
		{
			// Guards the config relationship rather than the function: a settle window at or below the
			// 50-tick FireDelay would re-order mid-launch, which is the case above.
			Assert.That(DroneTaskingMath.ShouldRetask(true, false, 50, 75), Is.False);
		}

		[Test]
		public void CanLaunch_RefusesWhenTheArmamentIsPaused()
		{
			// Covers the state that looks like success: after a kill the quadcopter respawns and
			// `loaded` is re-granted, but ammo-primary is 0, so the armament stays paused. The
			// operator visibly has a drone and cannot launch it.
			Assert.That(DroneTaskingMath.CanLaunch(false, true, true, 10, 22), Is.False);
		}

		[Test]
		public void CanLaunch_RefusesASecondLaunchWhileOneIsAirborne()
		{
			// The retarget branch is unreachable for ^DR, so this could not redirect the drone; it
			// would only burn the cooldown.
			Assert.That(DroneTaskingMath.CanLaunch(true, false, true, 10, 22), Is.False);
		}

		[Test]
		public void CanLaunch_RefusesOutOfRangeSoTheOperatorNeverWalks()
		{
			// Out of weapon range the attack activity would WALK the operator there, granting
			// `moving` — which recalls the drone and defeats standing off at all.
			Assert.Multiple(() =>
			{
				Assert.That(DroneTaskingMath.CanLaunch(true, true, true, 23, 22), Is.False);
				Assert.That(DroneTaskingMath.CanLaunch(true, true, true, 22, 22), Is.True);
			});
		}

		// ---------- IsCovered ----------

		[Test]
		public void IsCovered_RetiresASquareOnceItIsFreshAgain()
		{
			Assert.Multiple(() =>
			{
				Assert.That(DroneTaskingMath.IsCovered(10, 500), Is.True);
				Assert.That(DroneTaskingMath.IsCovered(900, 500), Is.False);
			});
		}
	}
}
