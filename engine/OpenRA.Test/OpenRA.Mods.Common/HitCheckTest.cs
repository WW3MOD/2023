#region Copyright & License Information
/*
 * Pins the HitCheck anomaly detector.
 *
 * The value of this detector is entirely in what it does NOT report, so most of what follows is
 * false-positive suppression rather than detection. Each silent case below is a real weapon/target
 * pairing from the shipped ruleset that an earlier draft of the predicate fired on.
 *
 * Every "delivered" figure is computed by the shipped DamageWarhead.ApplyPenetration rather than
 * hardcoded, so a change to the armour arithmetic breaks these tests instead of quietly
 * invalidating them.
 *
 * Pure arithmetic; no World, no Actor, no game run.
 */
#endregion

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

		// --- (1) it catches the bug it exists for --------------------------------------------

		[Test]
		public void IskanderWithUnsetPenetrationIsFlagged()
		{
			// The reported defect: IskanderExplosion wrote Damage 54000 and no Penetration, so the
			// engine default of 1 divided it down to 192 against a T-90. Fixed in-tree on 260827;
			// this is the state the detector has to catch if it ever comes back.
			var delivered = Delivered(54000, 1, T90Thickness);

			Assert.That(delivered, Is.EqualTo(192), "guard: the arithmetic under test has moved");
			Assert.That(HitCheck.IsUnderPerforming(54000, delivered, T90Thickness, T90Hp), Is.True,
				"a 54000-damage warhead arriving as 192 against a 24000 HP tank must be flagged");
		}

		[Test]
		public void IskanderAsShippedIsSilent()
		{
			// Penetration: 2500 clears every armour value in the game.
			var delivered = Delivered(54000, 2500, T90Thickness);

			Assert.That(delivered, Is.EqualTo(54000));
			Assert.That(HitCheck.IsUnderPerforming(54000, delivered, T90Thickness, T90Hp), Is.False);
		}

		[Test]
		public void HimarsPayloadAsShippedIsSilent()
		{
			// The HIMARS is the standing false-positive trap: its ARMAMENT weapon says Damage 50
			// while the payload it spawns says 36000. The detector reads the warhead's own written
			// damage, so the payload is what it sees -- and that payload penetrates.
			var delivered = Delivered(36000, 2500, AbramsFrontal);

			Assert.That(delivered, Is.EqualTo(36000));
			Assert.That(HitCheck.IsUnderPerforming(36000, delivered, AbramsFrontal, AbramsHp), Is.False);
		}

		// --- (2) the false-positive classes, each one measured ---------------------------------

		[Test]
		public void UnarmouredVictimsNeverFire()
		{
			// Thickness defaults to 0 and no infantry sets one, so InflictDamage skips the armour
			// branch entirely and delivered == raw. This is why the ~109 Penetration-less warheads
			// aimed at infantry are not bugs and must never appear on this log.
			var delivered = Delivered(54000, 1, 0);

			Assert.That(delivered, Is.EqualTo(54000));
			Assert.That(HitCheck.IsUnderPerforming(54000, delivered, 0, 200), Is.False);
			Assert.That(HitCheck.IsUnderPerformingAgainstThinArmour(54000, delivered, 0, 200), Is.False);
		}

		[Test]
		public void TopAttackWeaponsAreJudgedAgainstTheRoofTheyActuallyHit()
		{
			// The ATGM's Penetration 100 against 700mm reads as a sevenfold under-penetration and is
			// correctly sized: TopAttack takes Distribution[3], so the number it must beat is 70.
			// Feeding the detector raw Thickness instead of effective thickness would report every
			// top-attack weapon in the mod as broken.
			var throughRoof = Delivered(10000, 100, AbramsRoof);

			Assert.That(throughRoof, Is.EqualTo(10000), "Penetration 100 clears an Abrams roof of 70");
			Assert.That(HitCheck.IsUnderPerforming(10000, throughRoof, AbramsRoof, AbramsHp), Is.False);
		}

		[Test]
		public void AShotThatKillsAnywayIsSilentHoweverMuchItLost()
		{
			// The severity axis is the change in OUTCOME, not the damage lost. An earlier draft used
			// "damage lost >= victim max HP" and fired on victims the shot destroys several times
			// over -- large absolute loss, nothing about the result changed.
			//
			// This case is chosen to DISCRIMINATE between the two axes, which is harder than it
			// looks: the obvious example (a 50 HP drone) is already excluded by the ratio gate, so a
			// test built on it passes the rejected implementation and proves nothing. The first
			// version of this test did exactly that. Here the ratio gate is satisfied (10% delivered)
			// and only the outcome axis separates them:
			//
			//   damage lost  = 10000 - 1000 = 9000 >= 600  -> the REJECTED axis fires
			//   outcome      = delivered 1000 >= 600 HP    -> it kills anyway, so this axis is silent
			//
			// Real pairing: mandibleheavy / cratenuke (Damage 10000, no Penetration) against a
			// transport helicopter.
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
			// An anti-armour missile pairs a penetrating Warhead@Target with a small Warhead@Spread
			// that carries no Penetration. The ATGM's spread delivers 28 against an Abrams. That is
			// the designed shape of a spread warhead, and it stays quiet because 2000 was never
			// going to kill a 28000 HP tank -- the outcome test, not a magnitude threshold, is what
			// excludes it.
			var delivered = Delivered(2000, 1, AbramsRoof);

			Assert.That(delivered, Is.EqualTo(28));
			Assert.That(HitCheck.IsUnderPerforming(2000, delivered, AbramsRoof, AbramsHp), Is.False);
		}

		[Test]
		public void ArmourMerelyShavingAShotIsSilent()
		{
			// RPG frontally into an Abrams: Penetration 500 against 700 delivers 4285 of 6000. The
			// armour model working as designed must not read as a defect.
			var delivered = Delivered(6000, 500, AbramsFrontal);

			Assert.That(delivered, Is.EqualTo(4285));
			Assert.That(HitCheck.IsUnderPerforming(6000, delivered, AbramsFrontal, AbramsHp), Is.False);
		}

		[Test]
		public void SplashAgainstAnAirframeGoesToTheQuietChannel()
		{
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
			// No input may light both markers -- a firing belongs to exactly one channel, so a grep
			// of the loud marker can never double-count.
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

		// --- (3) the tuning constants ----------------------------------------------------------

		[Test]
		public void ThresholdsAreWhatTheMeasurementChose()
		{
			// Pinned so a retune is a deliberate act with a visible diff. ArmourFloor 50 separates
			// two populations that were measured, not guessed: every benign firing sat at effective
			// thickness <= 20, every armour value a designer sizes penetration against is >= 150.
			Assert.That(HitCheck.ArmourFloor, Is.EqualTo(50));
			Assert.That(HitCheck.MaxDeliveredPercent, Is.EqualTo(25));
		}
	}
}
