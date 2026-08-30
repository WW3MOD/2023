#region Copyright & License Information
/*
 * Pins the HitCheck anomaly detector.
 *
 * EVERY TEST HERE IS THE SOLE CATCHER OF AT LEAST ONE MUTATION, and that was established by running
 * the mutations rather than by reasoning about them. An earlier version of this file had eleven
 * tests; a mutation audit showed four of them never caught anything at all, and two of those could
 * not possibly have caught the defect they were NAMED for, because they passed the value under test
 * in as a parameter. Count is not coverage. The surviving set is smaller and strictly stronger.
 *
 * The audit that produced it (mutation -> the tests that failed):
 *
 *   outcome axis -> rejected "damage lost" form ... AShotThatKillsAnyway            (sole)
 *   outcome axis -> drop "would have killed" ...... CompanionSpreadWarheads         (sole)
 *   outcome axis -> drop "now survives" ........... AShotThatKillsAnyway            (sole)
 *   ratio gate removed ............................ ArmourMerelyShavingAShot        (sole)
 *   MaxDeliveredPercent 25 -> 75 .................. Thresholds                      (sole)
 *   thin channel dead ............................. SplashAgainstAnAirframe         (sole)
 *   thin predicate loses its floor guard .......... TheTwoChannelsArePartitions     (sole)
 *   loud channel always false ..................... IskanderWithUnsetPenetration    (sole)
 *   call site passes raw thickness ................ TheDetectorIsWiredIntoTheDamagePath (sole)
 *   call site passes written Damage ............... TheDetectorIsWiredIntoTheDamagePath (sole)
 *   call site disabled entirely ................... TheDetectorIsWiredIntoTheDamagePath (sole)
 *
 * The last three are why the source scan exists. Arithmetic pins cannot see wiring: with the whole
 * detector call commented out of DamageWarhead, every "this stays silent" assertion here passes
 * trivially and happily. That seam needs a different kind of test, and this repo has hit the same
 * wall before -- see the source-scan guard added alongside the critical-pip blink work.
 *
 * Every "delivered" figure is computed by the shipped DamageWarhead.ApplyPenetration rather than
 * hardcoded, so a change to the armour arithmetic breaks these tests instead of quietly
 * invalidating them.
 *
 * Pure arithmetic plus one file read; no World, no Actor, no game run.
 */
#endregion

using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Warheads;

namespace OpenRA.Test
{
	[TestFixture]
	public class HitCheckTest
	{
		// Real values, read from the shipped ruleset at main @ 5a985337.
		const int T90Thickness = 280;
		const int T90Hp = 24000;
		const int AbramsFrontal = 700;      // vehicles-america.yaml:499, Distribution[0] = 100
		const int AbramsRoof = 70;          // 700 * Distribution[3] (10) / 100
		const int AbramsHp = 28000;
		const int TransportHeliEffective = 10;
		const int TransportHeliHp = 600;

		static int Delivered(int raw, int penetration, int effectiveThickness)
		{
			return DamageWarhead.ApplyPenetration(raw, penetration, effectiveThickness);
		}

		// --- detection ------------------------------------------------------------------------

		[Test]
		public void IskanderWithUnsetPenetrationIsFlagged()
		{
			// The reported defect, and the only test here that asserts the loud channel ever returns
			// TRUE -- so it is the sole catcher of "the detector never fires", which every silence
			// test above would happily pass.
			//
			// IskanderExplosion wrote Damage 54000 and no Penetration, so the engine default of 1
			// divided it to 192 against a T-90. Fixed in-tree 260827; this is the state to catch if
			// it returns.
			var delivered = Delivered(54000, 1, T90Thickness);

			Assert.That(delivered, Is.EqualTo(192), "guard: the arithmetic under test has moved");
			Assert.That(HitCheck.IsUnderPerforming(54000, delivered, T90Thickness, T90Hp), Is.True,
				"a 54000-damage warhead arriving as 192 against a 24000 HP tank must be flagged");
		}

		// --- the false-positive classes, each one the sole catcher of a mutation ----------------

		[Test]
		public void AShotThatKillsAnywayIsSilentHoweverMuchItLost()
		{
			// Sole catcher of both halves of the outcome axis: reverting it to the rejected
			// "damage lost >= max HP" form, and dropping its "now survives" clause.
			//
			// This case is chosen to DISCRIMINATE, which is harder than it looks. The obvious
			// example -- a 50 HP drone -- is already excluded by the ratio gate, so a test built on
			// it passes the rejected implementation and proves nothing. The first version of this
			// test did exactly that. Here the ratio gate is satisfied and only the outcome axis
			// separates them:
			//
			//   damage lost = 10000 - 1000 = 9000 >= 600 -> the REJECTED axis fires
			//   outcome     = delivered 1000 >= 600 HP   -> it kills anyway, so the real axis is silent
			//
			// Real pairing: mandibleheavy / cratenuke (Damage 10000, no Penetration) on a transport.
			var delivered = Delivered(10000, 1, TransportHeliEffective);

			Assert.That(delivered, Is.EqualTo(1000));
			Assert.That(delivered * 100 / 10000, Is.LessThanOrEqualTo(HitCheck.MaxDeliveredPercent),
				"guard: the ratio gate must NOT be what excludes this, or the test proves nothing");
			Assert.That(10000 - delivered, Is.GreaterThanOrEqualTo(TransportHeliHp),
				"guard: the rejected 'damage lost' axis would fire here");
			Assert.That(delivered, Is.GreaterThanOrEqualTo(TransportHeliHp), "guard: the victim still dies");

			Assert.That(HitCheck.IsUnderPerforming(10000, delivered, TransportHeliEffective, TransportHeliHp), Is.False);
			Assert.That(HitCheck.IsUnderPerformingAgainstThinArmour(10000, delivered, TransportHeliEffective, TransportHeliHp), Is.False,
				"a shot that still kills is not an anomaly on either channel");
		}

		[Test]
		public void CompanionSpreadWarheadsAreSilentBecauseTheyWereNeverLethal()
		{
			// Sole catcher of dropping the "would have killed" half of the outcome axis.
			//
			// An anti-armour missile pairs a penetrating Warhead@Target with a small Warhead@Spread
			// carrying no Penetration. The ATGM's spread delivers 28 against an Abrams -- the
			// designed shape of a spread warhead. It stays quiet because 2000 was never going to
			// kill a 28000 HP tank, so it is the lethality clause, not a magnitude threshold, doing
			// the work.
			var delivered = Delivered(2000, 1, AbramsRoof);

			Assert.That(delivered, Is.EqualTo(28));
			Assert.That(delivered * 100 / 2000, Is.LessThanOrEqualTo(HitCheck.MaxDeliveredPercent),
				"guard: the ratio gate must NOT be what excludes this");
			Assert.That(HitCheck.IsUnderPerforming(2000, delivered, AbramsRoof, AbramsHp), Is.False);
		}

		[Test]
		public void ArmourMerelyShavingAShotIsSilent()
		{
			// Sole catcher of removing the ratio gate.
			//
			// REWRITTEN after the mutation audit. The previous version used an RPG frontally into an
			// Abrams (6000 -> 4285) and was decoration: 6000 was never going to kill a 28000 HP tank,
			// so the OUTCOME axis excluded it and deleting the ratio gate entirely changed nothing.
			// A test named for a gate it never reaches is the same defect this file was already
			// caught by once.
			//
			// This case reaches it. A heavy round that armour shaves by only 7%, and that 7% is just
			// enough to leave the tank alive:
			//
			//   raw 30000 >= 28000 HP        -> would have killed
			//   delivered 27857 < 28000 HP   -> now does not, so the outcome axis FIRES
			//   27857/30000 = 92%            -> only the ratio gate says "this is a close fight,
			//                                   not a mis-sized warhead"
			var delivered = Delivered(30000, 650, AbramsFrontal);

			Assert.That(delivered, Is.EqualTo(27857));
			Assert.That(delivered, Is.LessThan(AbramsHp), "guard: the outcome axis DOES fire here");
			Assert.That(delivered * 100 / 30000, Is.GreaterThan(HitCheck.MaxDeliveredPercent),
				"guard: the ratio gate must be the only thing excluding this");

			Assert.That(HitCheck.IsUnderPerforming(30000, delivered, AbramsFrontal, AbramsHp), Is.False,
				"armour deciding a close fight is the model working, not a defect");
		}

		[Test]
		public void SplashAgainstAnAirframeGoesToTheQuietChannel()
		{
			// Sole catcher of the thin channel being dead.
			//
			// Replaying the predicate over the whole ruleset produced 370 firings and ALL 370 were
			// this shape: an area warhead against a lightly-armoured airframe. They are routed to
			// the advisory marker rather than dropped, and rather than allowed to swamp the signal.
			var delivered = Delivered(3000, 1, TransportHeliEffective);

			Assert.That(delivered, Is.EqualTo(300));
			Assert.That(HitCheck.IsUnderPerforming(3000, delivered, TransportHeliEffective, TransportHeliHp), Is.False,
				"must not reach the loud channel");
			Assert.That(HitCheck.IsUnderPerformingAgainstThinArmour(3000, delivered, TransportHeliEffective, TransportHeliHp), Is.True,
				"but must still be recorded somewhere");
		}

		[Test]
		public void TheTwoChannelsArePartitions()
		{
			// Sole catcher of the thin predicate losing its floor guard, which would let a single
			// firing light both markers and make a grep of the loud marker double-count.
			foreach (var eff in new[] { 1, 10, 49, 50, 70, 280, 700, 2000 })
			{
				foreach (var pen in new[] { 1, 100, 500, 2500 })
				{
					var delivered = Delivered(54000, pen, eff);
					var loud = HitCheck.IsUnderPerforming(54000, delivered, eff, T90Hp);
					var thin = HitCheck.IsUnderPerformingAgainstThinArmour(54000, delivered, eff, T90Hp);
					Assert.That(loud && thin, Is.False, $"eff={eff} pen={pen} lit both channels");
				}
			}
		}

		[Test]
		public void ThresholdsAreWhatTheMeasurementChose()
		{
			// Sole catcher of a retune of MaxDeliveredPercent. Pinned so a retune is a deliberate act
			// with a visible diff. ArmourFloor 50 separates two populations that were measured, not
			// guessed: every benign firing sat at effective thickness <= 20, every armour value a
			// designer sizes penetration against is >= 150.
			Assert.That(HitCheck.ArmourFloor, Is.EqualTo(50));
			Assert.That(HitCheck.MaxDeliveredPercent, Is.EqualTo(25));
		}

		// --- the wiring seam, which no arithmetic pin can reach ---------------------------------

		[Test]
		public void TheDetectorIsWiredIntoTheDamagePath()
		{
			// Sole catcher of THREE call-site mutations, every one of which the arithmetic pins above
			// survive without a murmur:
			//
			//   1. passing raw `thickness` instead of `effectiveThickness` -- which would report every
			//      top-attack weapon in the mod as broken, since the ATGM's Penetration 100 clears an
			//      Abrams ROOF of 70 while looking sevenfold under-sized against the frontal 700.
			//   2. passing the warhead's written `Damage` instead of `damageBeforeArmour` -- which
			//      would ignore the random-damage rolls that ran just above it.
			//   3. disabling the call entirely. THIS IS THE ONE THAT MATTERS: with the detector
			//      commented out of DamageWarhead, every "this stays silent" assertion in this file
			//      passes, because a detector that never runs is silent about everything.
			//
			// A source scan is brittle to reformatting, and that is the accepted cost -- a false
			// failure here is loud and instantly diagnosable, whereas the failure it replaces is a
			// detector that has quietly not existed for months. Whitespace is normalised so ordinary
			// re-indentation does not trip it.
			var source = Normalise(File.ReadAllText(FindDamageWarhead()));

			Assert.That(source, Does.Contain("effectiveThickness = thickness * armorPercent / 100;"),
				"DamageWarhead must still compute effective thickness into a local for HitCheck to read");

			Assert.That(source, Does.Contain(
				"if (effectiveThickness > 0 && HitCheck.LostMostOfItsDamage(damageBeforeArmour, damage))"),
				"the cheap int pre-gate must still guard the detector, and must still be reachable -- " +
				"if this call has been removed or short-circuited, HitCheck is dead code and every " +
				"other test in this file is passing vacuously");

			Assert.That(source, Does.Contain(
				"HitCheck.IsUnderPerforming(damageBeforeArmour, damage, effectiveThickness, victimMaxHp)"),
				"the loud predicate must be fed pre-armour damage and EFFECTIVE thickness");

			Assert.That(source, Does.Contain(
				"HitCheck.IsUnderPerformingAgainstThinArmour(damageBeforeArmour, damage, effectiveThickness, victimMaxHp)"),
				"the advisory predicate must be fed the same two values");

			Assert.That(source, Does.Contain("HitCheck.Report("),
				"a flagged hit must still be reported somewhere");
		}

		static string Normalise(string source)
		{
			return Regex.Replace(source, @"\s+", " ");
		}

		static string FindDamageWarhead()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null)
			{
				var candidate = Path.Combine(dir.FullName, "engine", "OpenRA.Mods.Common", "Warheads", "DamageWarhead.cs");
				if (File.Exists(candidate))
					return candidate;

				dir = dir.Parent;
			}

			throw new FileNotFoundException(
				"could not locate DamageWarhead.cs -- if it moved, repoint this test rather than " +
				"deleting it: it is the only guard that HitCheck is wired in at all");
		}
	}
}
