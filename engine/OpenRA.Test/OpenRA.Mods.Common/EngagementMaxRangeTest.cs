#region Copyright & License Information
/*
 * WW3MOD engagement-range pins.
 *
 * Reported from playtest 2026-08-22: "That same tunguska shot missiles at one helicopter, then when
 * I ordered it to shoot at a second it refused to shoot it with missiles and went closer to it, to
 * shoot with the guns instead, even though it had missiles and should have used them."
 *
 * The cause is NOT the firing-primary condition lockout it looks like. Attack.TickAttack computed
 * `maxRange = armaments.Min(a => a.MaxRange())` — the SHORTEST reach among every armament valid
 * against the target. A Tunguska ordered at a helicopter has two valid armaments: the 30mm AA gun
 * at 18c0 and the 9M311 missile at 28c0. Min picks 18c0, so the unit reads itself as out of range
 * at 25 cells and drives forward until the GUN can fire — with eight missiles still loaded.
 *
 * The load-bearing property: A PLAYER'S ATTACK ORDER MUST NOT BE SILENTLY DOWNGRADED TO A WORSE
 * WEAPON. Where weapons are specialised by target rather than meant to be massed, the unit engages
 * at the range of the best weapon it actually has loaded.
 *
 * The second fixture below is the reason this is not simply Max: a dry armament is PAUSED, never
 * disabled, so it stays in the candidate list forever. Taking a naive Max would strand a Tunguska
 * that had spent its missiles at 28c0, out of its own gun's reach, firing nothing at all.
 *
 * Pure over the helper, following the project idiom (BreakOffScopeTest); no world.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class EngagementMaxRangeTest
	{
		static readonly WDist Gun = WDist.FromCells(18);      // 30mm.Tunguska.AA
		static readonly WDist Missile = WDist.FromCells(28);  // 9M311

		[Test]
		public void ASpecialistUnitEngagesAtItsLongestLoadedWeapon()
		{
			// The user-visible bug, stated as the predicate: both weapons loaded, both valid against
			// the helicopter. The unit must hold at the missile's range instead of closing to the gun's.
			var ranges = new[] { Gun, Missile };
			var paused = new[] { false, false };

			Assert.That(AttackBase.EngagementMaxRange(ranges, paused, true), Is.EqualTo(Missile),
				"a Tunguska with missiles loaded must engage a helicopter at the 9M311's 28c0 — " +
				"closing to the 30mm's 18c0 is the player's order being downgraded to a worse weapon");
		}

		[Test]
		public void ADryLongRangeWeaponDoesNotStrandTheUnitOutOfReach()
		{
			// Eight missiles spent: that armament is PAUSED (!ammo-secondary), not disabled, so it is
			// still a candidate. Holding at 28c0 here would leave the unit firing nothing whatsoever.
			var ranges = new[] { Gun, Missile };
			var paused = new[] { false, true };

			Assert.That(AttackBase.EngagementMaxRange(ranges, paused, true), Is.EqualTo(Gun),
				"once the missiles are dry the unit must close to the gun's 18c0, not hold at 28c0");
		}

		[Test]
		public void EveryWeaponPausedFallsBackToTheLongestRatherThanZero()
		{
			// EMP or heavy damage pauses both. Returning Zero would make the unit try to drive onto
			// the target's own cell; GetMaximumRangeVersusTarget has the same fallback for the same reason.
			var ranges = new[] { Gun, Missile };
			var paused = new[] { true, true };

			Assert.That(AttackBase.EngagementMaxRange(ranges, paused, true), Is.EqualTo(Missile),
				"with everything paused the unit keeps its longest range rather than collapsing to zero");
		}

		[Test]
		public void TheDefaultIsUnchangedFromShippedBehaviour()
		{
			// engageAtLongest=false must stay bit-for-bit the old Min, so every unit that has not opted
			// in — including the @stable bot's whole roster — behaves exactly as before.
			var ranges = new[] { Gun, Missile };

			Assert.That(AttackBase.EngagementMaxRange(ranges, new[] { false, false }, false), Is.EqualTo(Gun),
				"the default must remain the shipped Min so opting out changes nothing");
			Assert.That(AttackBase.EngagementMaxRange(ranges, new[] { false, true }, false), Is.EqualTo(Gun),
				"the shipped Min ignored pause state; the default branch must keep ignoring it");
		}

		[Test]
		public void ASingleArmamentIsUnaffectedEitherWay()
		{
			var ranges = new[] { Gun };

			Assert.That(AttackBase.EngagementMaxRange(ranges, new[] { false }, false), Is.EqualTo(Gun));
			Assert.That(AttackBase.EngagementMaxRange(ranges, new[] { false }, true), Is.EqualTo(Gun),
				"the overwhelming majority of units carry one weapon and must be untouched by this");
		}
	}
}
