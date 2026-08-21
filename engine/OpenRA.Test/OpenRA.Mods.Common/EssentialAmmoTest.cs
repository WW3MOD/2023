#region Copyright & License Information
/*
 * WW3MOD essential-ammo seeking predicate — contract + the default-off safety pin.
 *
 * USER REPORT, 2026-08-21: "a rifleman without bullets should basically be considered 'out of ammo'
 * even if they have one AT round left." AmmoPool.AllPoolsEmpty requires EVERY pool empty, so ^E3
 * (primary-ammo 100, secondary-ammo 1) never counted as dry while that single AT round was unspent —
 * and both all-empty dispatch paths were dead for him for the rest of the match.
 *
 * USER RULING on the shape of the fix: an explicit per-pool Essential flag that DEFAULTS TO FALSE.
 * No name-based default, even though 40 pools in this mod are called primary-ammo, because the guess
 * is wrong on the first unit anyone reaches for: the tunguska's primary-ammo is its cannon and its
 * secondary-ammo is its missiles, and a tunguska out of missiles is in more trouble than one out of
 * bullets. NoEssentialPoolsAuthored_MatchesAllPoolsEmpty is the pin that makes default-off mean what
 * it says — with nothing authored, this feature is invisible.
 *
 * TWO CONSTRAINTS THIS FILE GUARDS, both of which were identified as ways the obvious fix breaks:
 *
 *  1. The predicate must NOT reach AmmoPool.CannotFight, which gates COMBAT at seven call sites and
 *     means "stop trying to shoot". An AT specialist down to his last round must still fire it.
 *     CannotFightIgnoresEssential is that boundary.
 *
 *  2. Dispatch and the errand's EXIT test must be the SAME function. SeekSupplyProvider's
 *     ErrandIsPointless returns `dispatchedBecauseDry && !<predicate>`. Widening dispatch while the
 *     exit still read AllPoolsEmpty would make a partially-dry unit's errand pointless on its FIRST
 *     tick — AllPoolsEmpty is already false when such a unit sets off — so it would take one step
 *     and stop. TheErrandExitTestCannotBeSatisfiedAtDispatch is that trap, written as the arithmetic
 *     the two sites actually perform.
 *
 * NOTE ON RED. Constraint 2 is the only one here with a genuine RED: run it against
 * `!AmmoPool.AllPoolsEmpty(pools)` as the exit and it fails with the stated message. The rest pin a
 * new contract and are green by construction — they are NOT evidence that a partially-dry unit walks
 * to a truck in a running game. That needs the scenario named in the report.
 */
#endregion

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class EssentialAmmoTest
	{
		static AmmoPool Pool(int capacity, int initial, bool essential = false)
		{
			// The Info fields are readonly, which is what FieldLoader exists to populate.
			var info = new AmmoPoolInfo();
			FieldLoader.LoadField(info, "Ammo", capacity.ToString());
			FieldLoader.LoadField(info, "InitialAmmo", initial.ToString());
			FieldLoader.LoadField(info, "Essential", essential ? "true" : "false");
			return new AmmoPool(info);
		}

		// ^E3 as shipped: 100 rifle rounds, 1 AT round. The motivating unit, spelled out so the
		// numbers in these tests are traceable to infantry.yaml rather than invented.
		static AmmoPool[] RiflemanOutOfBullets(bool essential)
		{
			return new[] { Pool(100, 0, essential), Pool(1, 1) };
		}

		[Test]
		public void NoEssentialPoolsAuthored_MatchesAllPoolsEmpty()
		{
			// THE SAFETY PIN, and the reason default-off was chosen. Every shipped pool in the mod is
			// currently unflagged, so this equivalence is what guarantees the feature changes nothing
			// until someone authors it. If this ever fails, the feature has stopped being opt-in and
			// every actor in the game just changed behaviour.
			var cases = new[]
			{
				new AmmoPool[0],                                 // no pools at all (medic, technician)
				new[] { Pool(100, 0) },                          // single pool, spent
				new[] { Pool(100, 100) },                        // single pool, full
				new[] { Pool(100, 0), Pool(3, 0) },              // both spent
				new[] { Pool(100, 100), Pool(3, 0) },            // ^E6: SMG full, C4 spent
				new[] { Pool(100, 0), Pool(1, 1) },              // ^E3: rifle spent, AT round left
				new[] { Pool(900, 0), Pool(8, 8) },              // bradley: cannon spent, 8 TOWs
			};

			foreach (var pools in cases)
				Assert.That(AmmoPool.OutOfEssentialAmmo(pools), Is.EqualTo(AmmoPool.AllPoolsEmpty(pools)),
					"with no pool marked Essential, OutOfEssentialAmmo must be indistinguishable from " +
					"AllPoolsEmpty. Default-off is the whole safety story of this feature: it ships inert " +
					"and turning it on is a per-pool authoring act.");
		}

		[Test]
		public void AnEssentialPoolSpentIsDryEvenWithRoundsElsewhere()
		{
			// The user's sentence, as an assertion. Rifle spent, AT round in hand, rifle marked
			// essential: he is out of ammo for seeking purposes.
			Assert.That(AmmoPool.OutOfEssentialAmmo(RiflemanOutOfBullets(essential: true)), Is.True);

			// And the contrast that makes it a real change rather than a rename.
			Assert.That(AmmoPool.AllPoolsEmpty(RiflemanOutOfBullets(essential: true)), Is.False,
				"AllPoolsEmpty must keep its old meaning — other callers depend on it");
		}

		[Test]
		public void AnEssentialPoolWithRoundsIsNotDryHoweverEmptyTheRest()
		{
			// The tunguska case that killed the name-based default: missiles are what matter, so a
			// tunguska with missiles left is not dry no matter how many cannon rounds it has burned.
			var missilesLeft = new[] { Pool(180, 0), Pool(8, 8, essential: true) };
			Assert.That(AmmoPool.OutOfEssentialAmmo(missilesLeft), Is.False);

			var missilesSpent = new[] { Pool(180, 180), Pool(8, 0, essential: true) };
			Assert.That(AmmoPool.OutOfEssentialAmmo(missilesSpent), Is.True,
				"which pool is essential is an authoring decision, not a function of its name — the " +
				"secondary pool being the essential one must work exactly as well as the primary");
		}

		[Test]
		public void EveryEssentialPoolMustBeSpent()
		{
			// "Every essential pool empty", not "any". A unit holding one of two essential weapons is
			// still in the fight; pulling it out would be a far more aggressive rule than the user asked
			// for and would empty the line for partial shortages.
			var oneOfTwoLeft = new[] { Pool(100, 0, essential: true), Pool(8, 8, essential: true) };
			Assert.That(AmmoPool.OutOfEssentialAmmo(oneOfTwoLeft), Is.False);

			var bothSpent = new[] { Pool(100, 0, essential: true), Pool(8, 0, essential: true) };
			Assert.That(AmmoPool.OutOfEssentialAmmo(bothSpent), Is.True);
		}

		[Test]
		public void OrderingOfPoolsDoesNotChangeTheAnswer()
		{
			// INotifyBecomingIdle delivers to each pool in turn and trait order is not a guarantee any
			// caller should lean on. Both orderings, both answers.
			Assert.That(AmmoPool.OutOfEssentialAmmo(new[] { Pool(100, 0, true), Pool(1, 1) }), Is.True);
			Assert.That(AmmoPool.OutOfEssentialAmmo(new[] { Pool(1, 1), Pool(100, 0, true) }), Is.True);
			Assert.That(AmmoPool.OutOfEssentialAmmo(new[] { Pool(100, 100, true), Pool(1, 0) }), Is.False);
			Assert.That(AmmoPool.OutOfEssentialAmmo(new[] { Pool(1, 0), Pool(100, 100, true) }), Is.False);
		}

		[Test]
		public void NoPoolsIsNeverDry()
		{
			// Matches AllPoolsEmpty's own `any` guard. An unarmed class must never read as out of ammo,
			// or every medic on the map walks off to find a truck it has no use for.
			Assert.That(AmmoPool.OutOfEssentialAmmo(new List<AmmoPool>()), Is.False);
		}

		[Test]
		public void TheErrandExitTestCannotBeSatisfiedAtDispatch()
		{
			// THE TRAP, as arithmetic. SeekSupplyProvider.ErrandIsPointless and Resupply's mid-route
			// re-check both compute `dispatchedBecauseDry && !<predicate>`. Whatever predicate dispatches
			// the unit must therefore be the one they negate, or the errand ends on the tick it begins.
			//
			// Swap OutOfEssentialAmmo for AllPoolsEmpty on the exit line and this test goes RED — which
			// is exactly the bug it exists to prevent.
			var pools = RiflemanOutOfBullets(essential: true);

			Assert.That(AmmoPool.OutOfEssentialAmmo(pools), Is.True, "precondition: this unit is dispatched");

			// Calls the SHIPPED exit function that both activities now use, rather than restating its
			// expression here — a test that reimplements the rule it checks agrees with itself whatever
			// the real code does, which is precisely the failure mode this test is about.
			Assert.That(AmmoPool.SelfAssignedErrandIsOver(true, pools), Is.False,
				"the errand must not be pointless at the moment of dispatch. If the exit test reads " +
				"AllPoolsEmpty while dispatch reads OutOfEssentialAmmo, a partially-dry unit is " +
				"dispatched and then immediately told its errand is over — it takes one step and stops. " +
				"Dispatch and exit must name the same function.");

			// The player's explicit Resupply order is a destination order: it never expires on ammo
			// state, however full the unit gets on the way.
			Assert.That(AmmoPool.SelfAssignedErrandIsOver(false, pools), Is.False);
			Assert.That(AmmoPool.SelfAssignedErrandIsOver(false, new[] { Pool(100, 100, true) }), Is.False,
				"an ordered errand is not self-assigned and does not end just because the unit is loaded");

			// And the errand DOES end once the reason lapses — otherwise this test would pass against a
			// function hardcoded to false.
			Assert.That(AmmoPool.SelfAssignedErrandIsOver(true, new[] { Pool(100, 100, true), Pool(1, 1) }), Is.True);
		}

		[Test]
		public void CannotFightIgnoresEssential()
		{
			// THE OTHER BOUNDARY. CannotFight gates combat at seven sites (Attack, AttackFollow,
			// AttackBase x2, AttackMove, SmartMoveActivity, AttackMoveActivity) and means "stop trying to
			// shoot". It reads AllPoolsEmpty and must keep doing so: feed it the seeking predicate and a
			// rifleman marked essential-dry stops attacking while still holding the AT round he is
			// supposed to fire.
			//
			// Asserted at the AllPoolsEmpty level because CannotFight itself needs an Actor for the
			// aircraft carve-out; the ammunition half of it is this, and it is the half at risk.
			var pools = RiflemanOutOfBullets(essential: true);

			Assert.That(AmmoPool.OutOfEssentialAmmo(pools), Is.True, "seeking says dry");
			Assert.That(AmmoPool.AllPoolsEmpty(pools), Is.False,
				"combat must NOT say dry — these two predicates answer different questions and the only " +
				"reason they agree today is that nothing is authored Essential yet");
		}

		[Test]
		public void ThePartialLeashIsShorterThanTheFullyDryOne()
		{
			// The tier only means something if it is the weaker impulse the user asked for: "there is a
			// truck right here", not "go find one". Relative, not absolute, so retuning either number
			// keeps the relationship under test.
			var info = new AmmoPoolInfo();
			Assert.That(info.EssentialDryLeashCells, Is.LessThan(info.DryRearmLeashCells),
				"a unit that can still fire something must not roam further than one that cannot fire " +
				"at all — that inversion would make partial dryness the STRONGER pull");
		}

		[Test]
		public void TheLeashTierFollowsHowDryTheActorActuallyIs()
		{
			// ResolveSeekLeash picks the budget; these call the shipped method rather than restating it.
			var info = new AmmoPoolInfo();

			var partiallyDry = RiflemanOutOfBullets(essential: true);
			Assert.That(AmmoPool.ResolveSeekLeash(partiallyDry), Is.EqualTo(info.EssentialDryLeashCells),
				"can still fire the AT round -> short leash");

			var whollyDry = new[] { Pool(100, 0, essential: true), Pool(1, 0) };
			Assert.That(AmmoPool.ResolveSeekLeash(whollyDry), Is.EqualTo(info.DryRearmLeashCells),
				"cannot fire anything -> the full dry leash");

			// Tightest wins, same rule as ResolveDryRearmLeash, and it must not depend on which pool asks.
			var tightened = new AmmoPool[] { Pool(100, 0, true), Pool(1, 1) };
			FieldLoader.LoadField(tightened[1].Info, "EssentialDryLeashCells", "4");
			Assert.That(AmmoPool.ResolveSeekLeash(tightened), Is.EqualTo(4));
		}

		[Test]
		public void AnEvacuatingUnitNeverTravelsBackwardsToRearm()
		{
			// USER RULING: in Evacuate, detour only if the resupply is closer than the exit. The unit was
			// told to leave; fetching ammunition must not take it deeper into what it is leaving.
			// Offsets are from the unit, both chessboard, both from the same origin.

			// Truck 3 cells one way, exit 12 the other: worth the detour.
			Assert.That(SupplyHuntMath.ResupplyBeatsExit(3, 0, -12, 0), Is.True);

			// Truck 12 cells BEHIND, exit 3 ahead: keep leaving.
			Assert.That(SupplyHuntMath.ResupplyBeatsExit(-12, 0, 3, 0), Is.False);

			// Equidistant: the evacuation is an order that was actually expressed, the detour is the
			// unit's own idea, and an unordered errand does not win a coin flip against an ordered one.
			Assert.That(SupplyHuntMath.ResupplyBeatsExit(7, 0, 7, 0), Is.False,
				"ties must go to evacuating");

			// Chessboard on both sides: a diagonal at 7 is 7 away, not 9.9, so it must lose to a
			// straight-line exit at 8 rather than beating it.
			Assert.That(SupplyHuntMath.ResupplyBeatsExit(7, 7, 8, 0), Is.True);
			Assert.That(SupplyHuntMath.ChessboardCells(7, 7), Is.EqualTo(7));
		}
	}
}
