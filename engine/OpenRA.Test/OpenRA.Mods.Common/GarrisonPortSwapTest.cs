#region Copyright & License Information
/*
 * WW3MOD garrison port-swap tests.
 *
 * A port slot holds three fields that together are one soldier's identity at that port:
 * DeployedSoldier, CachedArmaments and ConditionToken. They must move together.
 *
 * CachedArmaments is the one that is easy to forget, because it is not what the shot is fired
 * from — AttackGarrisoned.DoGarrisonedAttack reads the soldier's live armaments
 * (AttackGarrisoned.cs:292), so a stale cache never fires a wrong weapon. It is read by
 * GarrisonManager.ScanForTarget (:858) to size the port's scan circle and to filter candidates
 * by weapon validity. A slot caching another soldier's armaments therefore CHOOSES targets on
 * the wrong weapon profile and then hands them to a firing loop that re-checks range (:302) and
 * validity (:298) against the real occupant — so the port locks onto something it cannot shoot
 * and goes quiet, rather than misfiring visibly. That silence is why this needs a pin and not
 * an eyeball.
 *
 * These are pinned on the pure helper because the order handler around it needs an Actor, a
 * World and a loaded ruleset; Actor has no accessible constructor from this assembly.
 * DeployedSoldier is consequently null in every case below and the ConditionToken doubles as
 * the soldier's identity — what is under test is that the triple travels as a unit.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class GarrisonPortSwapTest
	{
		const int TokenA = 111;
		const int TokenB = 222;

		// Distinguished by length so a failure message says which soldier's weapons landed where.
		static readonly Armament[] RiflemanArmaments = new Armament[1];
		static readonly Armament[] AtSoldierArmaments = new Armament[3];

		static PortState Port(Actor soldier, Armament[] armaments, int token)
		{
			return new PortState(new GarrisonPortInfo())
			{
				DeployedSoldier = soldier,
				CachedArmaments = armaments,
				ConditionToken = token
			};
		}

		[Test]
		public void ArmamentsTravelWithTheSoldierTheyBelongTo()
		{
			var north = Port(null, RiflemanArmaments, TokenA);
			var south = Port(null, AtSoldierArmaments, TokenB);

			GarrisonManager.SwapPortOccupants(north, south);

			Assert.That(north.ConditionToken, Is.EqualTo(TokenB),
				"the swap must move the condition token; if this fails the helper is not swapping at all");

			Assert.That(north.CachedArmaments, Is.SameAs(AtSoldierArmaments),
				"north port took the other soldier (token " + TokenB + ") but kept its previous occupant's " +
				"cached armaments: ScanForTarget sizes the scan circle and filters weapon validity from " +
				"this array, so the port picks targets on a weapon profile its actual occupant does not have");

			Assert.That(south.CachedArmaments, Is.SameAs(RiflemanArmaments),
				"south port took the other soldier (token " + TokenA + ") but kept its previous occupant's " +
				"cached armaments");
		}

		// The sharper half: every other write site keeps DeployedSoldier and CachedArmaments in
		// lockstep (:375-376, :1456-1458, :1478-1479), so ScanForTarget reads the array at :859
		// behind nothing but a null-soldier check at :856. A slot left holding a soldier and a null
		// cache is a NullReferenceException on the next scan tick, not a mis-aimed port.
		[Test]
		public void SwappingIntoAnEmptyPortDoesNotLeaveANullCacheBehindAnOccupiedSlot()
		{
			var manned = Port(null, RiflemanArmaments, TokenA);
			var empty = Port(null, null, Actor.InvalidConditionToken);

			GarrisonManager.SwapPortOccupants(manned, empty);

			Assert.That(empty.CachedArmaments, Is.SameAs(RiflemanArmaments),
				"the port that just received the soldier still caches null armaments: ScanForTarget " +
				"reaches the foreach at GarrisonManager.cs:859 guarded only by the null-soldier check at " +
				":856, so this throws NullReferenceException on the next scan tick");

			Assert.That(manned.CachedArmaments, Is.Null,
				"the vacated port must not retain armaments for a soldier that has left it");
		}

		[Test]
		public void SwapInvalidatesTargetingOnBothPorts()
		{
			var north = Port(null, RiflemanArmaments, TokenA);
			var south = Port(null, AtSoldierArmaments, TokenB);
			north.TargetLockTicks = 25;
			south.TargetLockTicks = 25;

			GarrisonManager.SwapPortOccupants(north, south);

			Assert.That(north.CurrentTarget.Type, Is.EqualTo(TargetType.Invalid),
				"a target chosen for the previous occupant must not survive the swap");
			Assert.That(south.CurrentTarget.Type, Is.EqualTo(TargetType.Invalid),
				"a target chosen for the previous occupant must not survive the swap");
			Assert.That(north.TargetLockTicks, Is.Zero,
				"the lock must clear or the new occupant is held to the old occupant's target until it expires");
			Assert.That(south.TargetLockTicks, Is.Zero,
				"the lock must clear or the new occupant is held to the old occupant's target until it expires");
		}
	}
}
