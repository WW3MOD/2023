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

		// ConditionToken is minted per-actor: Actor.nextConditionToken starts at 1 on every actor
		// (Actor.cs:104), so two soldiers in one building routinely hold the same integer. A port
		// left holding a token minted on the OTHER soldier is therefore not inert — RevokePortCondition
		// (:440) asks the wrong actor whether that integer is valid, very often gets yes, and revokes
		// whatever unrelated condition happens to carry that id. Meanwhile the token actually granted
		// to the soldier at :352 is tracked by no port and can never be revoked, so that soldier keeps
		// `garrisoned-at-port` for the rest of the match: sprite hidden, movement disabled, forever.
		[Test]
		public void NeitherPortIsLeftHoldingTheOtherSoldiersToken()
		{
			var north = Port(null, RiflemanArmaments, TokenA);
			var south = Port(null, AtSoldierArmaments, TokenB);

			GarrisonManager.SwapPortOccupants(north, south);

			Assert.That(north.ConditionToken, Is.EqualTo(TokenB),
				"north took the other soldier but did not take their condition token");

			Assert.That(south.ConditionToken, Is.EqualTo(TokenA),
				"condition-token leak: south is still holding token " + TokenB + ", which north now " +
				"holds too, so one soldier's token is recorded at two ports and the other soldier's " +
				"token (" + TokenA + ") is recorded nowhere and can never be revoked — that soldier " +
				"stays garrisoned-at-port permanently");

			Assert.That(north.ConditionToken, Is.Not.EqualTo(south.ConditionToken),
				"both ports hold the same token: tokens are per-actor counters, so revoking through " +
				"one of these ports will revoke an unrelated condition on the wrong soldier");
		}

		// The other shape the port→port path hits: destination empty. The hand-rolled version in
		// AssignGarrisonPort cleared the source slot to InvalidConditionToken and only then copied the
		// source's token to the destination, so the moved soldier's token was zeroed in transit.
		// RevokePortCondition silently no-ops on InvalidConditionToken (:440), so the leak is total
		// and completely quiet.
		[Test]
		public void TokenTravelsWithTheSoldierIntoAnEmptyPort()
		{
			var manned = Port(null, RiflemanArmaments, TokenA);
			var empty = Port(null, null, Actor.InvalidConditionToken);

			GarrisonManager.SwapPortOccupants(manned, empty);

			Assert.That(empty.ConditionToken, Is.EqualTo(TokenA),
				"the port that just received the soldier records no condition token: the token granted " +
				"at GarrisonManager.cs:352 is now tracked nowhere, RevokePortCondition no-ops on it, and " +
				"the soldier can never shed garrisoned-at-port");

			Assert.That(manned.ConditionToken, Is.EqualTo(Actor.InvalidConditionToken),
				"the vacated port still records a token for a soldier that has left it — a later recall " +
				"here would revoke a condition on whoever occupies this port next");
		}

		// PlayerOverride marks CurrentTarget as player-chosen. The helper invalidates CurrentTarget,
		// so leaving the flag set hands the incoming occupant a stale claim to a target that is gone.
		// Every other occupant-changing site clears it (:280, :379, :410, :604, :1372, :1410).
		[Test]
		public void SwapClearsPlayerOverrideOnBothPorts()
		{
			var north = Port(null, RiflemanArmaments, TokenA);
			var south = Port(null, AtSoldierArmaments, TokenB);
			north.PlayerOverride = true;
			south.PlayerOverride = true;

			GarrisonManager.SwapPortOccupants(north, south);

			Assert.That(north.PlayerOverride, Is.False,
				"the new occupant inherited the previous occupant's player-override claim");
			Assert.That(south.PlayerOverride, Is.False,
				"the new occupant inherited the previous occupant's player-override claim");
		}

		// Both ports are seated with a REAL target before the swap. Terrain targets are the only
		// kind constructible here — Target.FromActor needs an Actor and FromCell needs a World —
		// but they are enough: Target.Type returns Terrain unconditionally for them (Target.cs:104),
		// so the port must actually be overwritten for these assertions to pass. Seating them is not
		// optional dressing: TargetType.Invalid is the zero value of a byte enum and CurrentTarget is
		// a plain field, so an unseated port reads Invalid before the helper is ever called and the
		// assertion cannot fail. The two positions differ so a helper that SWAPS targets instead of
		// invalidating them also fails, and says which port kept which.
		[Test]
		public void SwapInvalidatesTargetingOnBothPorts()
		{
			var northTarget = Target.FromPos(new WPos(1024, 0, 0));
			var southTarget = Target.FromPos(new WPos(0, 2048, 0));

			var north = Port(null, RiflemanArmaments, TokenA);
			var south = Port(null, AtSoldierArmaments, TokenB);
			north.CurrentTarget = northTarget;
			south.CurrentTarget = southTarget;
			north.TargetLockTicks = 25;
			south.TargetLockTicks = 25;

			Assert.That(north.CurrentTarget.Type, Is.EqualTo(TargetType.Terrain),
				"test setup is broken: north must hold a live target before the swap or the " +
				"post-swap assertion below passes against a port nobody ever aimed");
			Assert.That(south.CurrentTarget.Type, Is.EqualTo(TargetType.Terrain),
				"test setup is broken: south must hold a live target before the swap or the " +
				"post-swap assertion below passes against a port nobody ever aimed");

			GarrisonManager.SwapPortOccupants(north, south);

			Assert.That(north.CurrentTarget.Type, Is.EqualTo(TargetType.Invalid),
				"north still holds a target chosen for the soldier who just left it: the incoming " +
				"occupant opens fire on the previous occupant's pick, selected against a weapon " +
				"profile it does not have");
			Assert.That(south.CurrentTarget.Type, Is.EqualTo(TargetType.Invalid),
				"south still holds a target chosen for the soldier who just left it: the incoming " +
				"occupant opens fire on the previous occupant's pick, selected against a weapon " +
				"profile it does not have");
			Assert.That(north.TargetLockTicks, Is.Zero,
				"the lock must clear or the new occupant is held to the old occupant's target until it expires");
			Assert.That(south.TargetLockTicks, Is.Zero,
				"the lock must clear or the new occupant is held to the old occupant's target until it expires");
		}
	}
}
