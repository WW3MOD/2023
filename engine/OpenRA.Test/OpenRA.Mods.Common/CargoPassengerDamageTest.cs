#region Copyright & License Information
/*
 * Verifies how much of a hit on a transport reaches the men riding inside:
 * Cargo.PassengerDamageFromTransportHit (hits the hull survives) and
 * Cargo.PassengerDamageFromTransportDeath (the blow that destroys it).
 * Pure-math test; no Actor / World.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Test
{
	[TestFixture]
	public class CargoPassengerDamageTest
	{
		// A bradley (14000 HP) carrying ^Infantry (200 HP) is the reference case:
		// it is what the mod actually drives infantry around in. Threshold 25% of
		// the hull = a 3500 hit before anything is felt in the back.
		const int Transport = 14000;
		const int Passenger = 200;
		const int Threshold = 25;

		// The caller rolls SharedRandom.Next(passengerMaxHP / VarianceDivisor),
		// which is exclusive of its bound — so the worst roll is 39, not 40.
		const int MaxRoll = (Passenger / 5) - 1;

		// Shipped default, and the pre-change curve it replaced.
		const int Half = 50;
		const int Raw = 100;

		static int Hit(int damage, int share, int roll = 0) =>
			Cargo.PassengerDamageFromTransportHit(Passenger, Transport, damage, Threshold, share, roll);

		static int Death(int damage, int roll = 0) =>
			Cargo.PassengerDamageFromTransportDeath(Passenger, Transport, damage, roll);

		[Test]
		public void ChipDamagePassesNothingThrough()
		{
			// Small-arms and autocannon fire grinds the hull down without ever
			// being felt inside it.
			Assert.AreEqual(0, Hit(100, Raw));
			Assert.AreEqual(0, Hit(1000, Raw));
			Assert.AreEqual(0, Hit(3499, Raw));

			// Exactly on the threshold is still nothing — the check is strict.
			Assert.AreEqual(0, Hit(3500, Raw));
			Assert.AreEqual(0, Hit(3500, Half));
		}

		[Test]
		public void ThresholdIsUnchangedByTheShareCut()
		{
			// Halving the share must not move where damage starts being felt,
			// otherwise "chip damage does nothing" quietly changes meaning.
			for (var damage = 0; damage <= 3500; damage += 250)
				Assert.AreEqual(0, Hit(damage, Half), $"a {damage} hit should pass nothing through");

			Assert.That(Hit(4000, Raw), Is.GreaterThan(0));
		}

		[Test]
		public void MiddleBandIsRoughlyHalved()
		{
			// The band the user complained about: hits the hull survives.
			// An ATGM (10000) against a full-health bradley is the worst case.
			Assert.AreEqual(92, Hit(10000, Raw));
			Assert.AreEqual(46, Hit(10000, Half));

			// A tank round that does not quite finish the job.
			Assert.AreEqual(50, Hit(7000, Raw));
			Assert.AreEqual(25, Hit(7000, Half));

			// Just over the threshold stays negligible either way.
			Assert.AreEqual(7, Hit(4000, Raw));
			Assert.AreEqual(3, Hit(4000, Half));
		}

		[Test]
		public void VarianceIsScaledWithTheShareNotOnTopOfIt()
		{
			// The roll is added before the cut, so the curve is halved end-to-end.
			// Applied afterwards instead, a 10000 hit at the top of the variance
			// band would land at 46+39=85 rather than 65 and the "roughly half"
			// would not hold at the edges — which is where survival is decided.
			Assert.AreEqual(131, Hit(10000, Raw, MaxRoll));
			Assert.AreEqual(65, Hit(10000, Half, MaxRoll));

			// Unluckiest halved roll still beats the luckiest raw roll.
			Assert.That(Hit(10000, Half, MaxRoll), Is.LessThan(Hit(10000, Raw)));
		}

		[Test]
		public void OneAtgmIsTheWholeExposureBecauseTheSameHitAlsoTriggersTheBail()
		{
			// An earlier version of this test asserted that two ATGMs are survivable.
			// That situation cannot occur on this branch: a single 10000 hit takes a
			// full-health bradley to 4000 (28.5%), which is Heavy, and Heavy is the
			// bail threshold — so the squad is on the ground before a second missile
			// arrives. The exposure from a hull hit is therefore ONE hit, not a
			// sequence, and the two changes compound.
			Assert.That(Cargo.ShouldEmergencyBail(DamageState.Heavy, DamageState.Heavy), Is.True);

			// One hit, unluckiest roll, is what a passenger actually pays.
			Assert.That(Hit(10000, Half, MaxRoll), Is.LessThan(Passenger / 2));

			// The halving still does real work despite that, because it governs the
			// band above the bail line — hits the hull takes while the squad is still
			// aboard and NOT yet leaving. A 7000 hit leaves a bradley at 50%, which is
			// Medium: nobody bails, and this is the damage they keep.
			Assert.That(Cargo.ShouldEmergencyBail(DamageState.Medium, DamageState.Heavy), Is.False);
			Assert.AreEqual(89, Hit(7000, Raw, MaxRoll));
			Assert.AreEqual(44, Hit(7000, Half, MaxRoll));
		}

		[Test]
		public void ADeadTransportNeverBailsSoEjectOnDeathStillRuns()
		{
			// REGRESSION PIN. Health clamps HP to 0 and evaluates DamageState before
			// notifying Damaged, so the killing blow reaches Cargo.Damaged already
			// reading Dead — and Dead (32) is numerically ABOVE Heavy (8), so a naive
			// `>=` lets it through. When it did, the bail emptied the hold synchronously
			// and INotifyKilled.Killed then iterated an empty list: EjectOnDeath never
			// ran and a one-shot kill on a loaded transport left the whole squad alive.
			Assert.That(Cargo.ShouldEmergencyBail(DamageState.Dead, DamageState.Heavy), Is.False);
			Assert.That(Cargo.ShouldEmergencyBail(DamageState.Dead, DamageState.Critical), Is.False);
		}

		[Test]
		public void BailFiresFromItsThresholdUpToButNotIncludingDeath()
		{
			Assert.That(Cargo.ShouldEmergencyBail(DamageState.Undamaged, DamageState.Heavy), Is.False);
			Assert.That(Cargo.ShouldEmergencyBail(DamageState.Light, DamageState.Heavy), Is.False);
			Assert.That(Cargo.ShouldEmergencyBail(DamageState.Medium, DamageState.Heavy), Is.False);
			Assert.That(Cargo.ShouldEmergencyBail(DamageState.Heavy, DamageState.Heavy), Is.True);
			Assert.That(Cargo.ShouldEmergencyBail(DamageState.Critical, DamageState.Heavy), Is.True);

			// Airborne transports are held at the old Critical threshold, so a
			// helicopter at 40% HP keeps flying with its troops aboard.
			Assert.That(Cargo.ShouldEmergencyBail(DamageState.Heavy, DamageState.Critical), Is.False);
			Assert.That(Cargo.ShouldEmergencyBail(DamageState.Critical, DamageState.Critical), Is.True);
		}

		[Test]
		public void TheHoistedThresholdMatchesTheOneInsideTheCurve()
		{
			// The caller now tests the threshold before looping so the RNG is only
			// consumed by hits that can produce damage. If these two ever disagree,
			// either the roll comes back on every scratch or a band of real hits stops
			// reaching the passengers entirely.
			var threshold = Cargo.PassengerDamageThreshold(Transport, Threshold);
			Assert.AreEqual(3500, threshold);

			Assert.AreEqual(0, Hit(threshold, Raw, MaxRoll));
			Assert.That(Hit(threshold + 1, Raw, MaxRoll), Is.GreaterThan(0));
		}

		[Test]
		public void AOneShotKillStillKills()
		{
			// The invariant the halving must not break. A blow that writes the hull
			// off in one go goes down the death path, which is not halved, and
			// carries overkill — so it lands at or past the passenger's whole bar.
			Assert.That(Death(Transport), Is.GreaterThanOrEqualTo(Passenger));
			Assert.That(Death(20000), Is.GreaterThanOrEqualTo(Passenger));

			// And the death path is untouched by the share cut applied to survivable hits.
			Assert.AreEqual(200, Death(Transport));
			Assert.AreEqual(285, Death(20000));
		}

		[Test]
		public void FinishingOffACrippledTransportIsSurvivable()
		{
			// The other half of the asymmetry: a small hit that happens to be the
			// last one is not a catastrophic kill, so the men in the back can walk.
			Assert.That(Death(2000, MaxRoll), Is.LessThan(Passenger));
			Assert.That(Death(4000, MaxRoll), Is.LessThan(Passenger));
		}

		[Test]
		public void DegenerateInputsAreInert()
		{
			Assert.AreEqual(0, Cargo.PassengerDamageFromTransportHit(Passenger, 0, 10000, Threshold, Half, 0));
			Assert.AreEqual(0, Cargo.PassengerDamageFromTransportHit(Passenger, Transport, 0, Threshold, Half, 0));
			Assert.AreEqual(0, Cargo.PassengerDamageFromTransportDeath(Passenger, 0, 10000, 0));

			// Healing arrives on the same notification carrying a negative value.
			Assert.AreEqual(0, Cargo.PassengerDamageFromTransportHit(Passenger, Transport, -5000, Threshold, Half, 0));
			Assert.AreEqual(0, Cargo.PassengerDamageFromTransportDeath(Passenger, Transport, -5000, 0));
		}

		[Test]
		public void SharePercentOfZeroMakesPassengersImmuneToSurvivableHits()
		{
			// The knob has to reach both ends, so it can be dialled to taste from YAML.
			Assert.AreEqual(0, Hit(10000, 0, MaxRoll));
			Assert.AreEqual(0, Hit(20000, 0));

			// but never at the cost of the catastrophic-kill invariant.
			Assert.That(Death(Transport), Is.GreaterThanOrEqualTo(Passenger));
		}

		[Test]
		public void LargeHitsDoNotOverflow()
		{
			// The intermediate product is passengerMaxHP * damage; a superweapon
			// (200000) against an abrams hull would be tight if it were not widened.
			Assert.That(Cargo.PassengerDamageFromTransportHit(Passenger, 28000, 200000, Threshold, Half, 0),
				Is.GreaterThan(Passenger));
			Assert.That(Cargo.PassengerDamageFromTransportDeath(Passenger, 28000, 200000, 0),
				Is.GreaterThan(Passenger));
		}
	}
}
