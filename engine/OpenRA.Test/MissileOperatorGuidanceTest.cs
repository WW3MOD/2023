#region Copyright & License Information
/*
 * WW3MOD wire-guided missile operator-guidance tests.
 *
 * WHAT IS BEING PINNED: the moment an in-flight wire-guided missile stops being flown.
 *
 * A missile whose weapon sets `ManualGuidance` is steered by a human operator in the launcher.
 * Until now it went ballistic only when the launcher DIED. It now also goes ballistic once the
 * launcher is crippled — DamageState.Heavy or worse, i.e. below 50% HP — which is the point the
 * mod already treats as the crew abandoning the vehicle: VehicleCrew.EjectionDamageState defaults
 * to Heavy (VehicleCrew.cs:55), and ^EffectsWhenDamagedVehicles ignites the fire ramp at
 * StartFraction 50.
 *
 * THE VOCABULARY TRAP, which is the single most likely thing to be subtly wrong here: "critically
 * damaged" in the user's sense is DamageState.Heavy, spelled `heavy-damage-attained` in YAML
 * (defaults.yaml:256-258, ValidDamageStates: Heavy, Critical). It is NOT the `critical-damage`
 * token (defaults.yaml:259-261, ValidDamageStates: Critical), which is a different marker at 25%.
 * Keying this on `critical-damage` would fire at half the intended HP. HeavyIsTheFiftyPercentLine
 * below pins that distinction against Health's own arithmetic.
 *
 * These tests take no World. Missile.GuidanceLost is a pure function of two synced inputs, and the
 * HP -> DamageState mapping is re-derived here from the thresholds rather than by calling
 * Health.DamageState, so agreement between the two is evidence and not a tautology (the house
 * convention — see MissileLaunchTimingTest). The shipped weapon YAML is loaded verbatim through
 * the real FieldLoader to pin which weapons are in scope.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Projectiles;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class MissileOperatorGuidanceTest
	{
		static MissileInfo Load(string yaml)
		{
			var info = new MissileInfo();
			FieldLoader.Load(info, MiniYaml.FromString(yaml, nameof(MissileOperatorGuidanceTest))[0].Value);
			return info;
		}

		/// <summary>
		/// Health's HP -> DamageState mapping (Health.cs:95-116), re-derived from the threshold
		/// percentages rather than called, so the boundary case below is independent evidence.
		/// </summary>
		static DamageState StateForHealth(int hp, int maxHP)
		{
			if (hp == maxHP)
				return DamageState.Undamaged;

			if (hp <= 0)
				return DamageState.Dead;

			if (hp * 100L < maxHP * 25L)
				return DamageState.Critical;

			if (hp * 100L < maxHP * 50L)
				return DamageState.Heavy;

			if (hp * 100L < maxHP * 75L)
				return DamageState.Medium;

			return DamageState.Light;
		}

		[Test]
		public void HealthyLauncherKeepsFlyingTheMissile()
		{
			Assert.That(Missile.GuidanceLost(true, DamageState.Undamaged), Is.False);
			Assert.That(Missile.GuidanceLost(true, DamageState.Light), Is.False);
			Assert.That(Missile.GuidanceLost(true, DamageState.Medium), Is.False,
				"Medium is 25-50% HP and above the crew-bail line; the operator is still aboard.");
		}

		[Test]
		public void BurningLauncherDropsGuidance()
		{
			Assert.That(Missile.GuidanceLost(true, DamageState.Heavy), Is.True,
				"Heavy is the burning/crew-bailing state the user calls 'critically damaged'.");
			Assert.That(Missile.GuidanceLost(true, DamageState.Critical), Is.True,
				"Critical is past Heavy, so it must also drop guidance.");
		}

		[Test]
		public void DeadLauncherDropsGuidanceAsItAlreadyDid()
		{
			// The pre-existing behaviour, unchanged: this is what `ManualGuidance` meant before.
			Assert.That(Missile.GuidanceLost(true, DamageState.Dead), Is.True);
		}

		[Test]
		public void WeaponsWithoutManualGuidanceAreUntouchedAtEveryDamageState()
		{
			foreach (var ds in new[]
			{
				DamageState.Undamaged, DamageState.Light, DamageState.Medium,
				DamageState.Heavy, DamageState.Critical, DamageState.Dead
			})
				Assert.That(Missile.GuidanceLost(false, ds), Is.False,
					$"ManualGuidance false must stay inert at {ds} — Hellfire rides a laser, not a wire.");
		}

		/// <summary>
		/// The boundary the request turns on: a missile in flight at the exact tick the launcher
		/// crosses the threshold. Health reports Heavy only STRICTLY below 50%, so at exactly half
		/// health the operator is still flying it and one hit point lower he is not.
		/// </summary>
		[Test]
		public void GuidanceIsLostOnTheTickTheLauncherCrossesFiftyPercent()
		{
			const int MaxHP = 100;

			Assert.That(StateForHealth(50, MaxHP), Is.EqualTo(DamageState.Medium));
			Assert.That(Missile.GuidanceLost(true, StateForHealth(50, MaxHP)), Is.False,
				"Exactly 50% HP is Medium, not Heavy — still guided.");

			Assert.That(StateForHealth(49, MaxHP), Is.EqualTo(DamageState.Heavy));
			Assert.That(Missile.GuidanceLost(true, StateForHealth(49, MaxHP)), Is.True,
				"One HP below half is Heavy — guidance drops on that tick.");
		}

		/// <summary>
		/// Guards the vocabulary directly. `critical-damage` is ValidDamageStates: Critical, so a
		/// predicate keyed on it would still be flying the missile through the whole 25-50% band.
		/// This asserts we are NOT that predicate.
		/// </summary>
		[Test]
		public void ThresholdIsHeavyAttainedAndNotTheTwentyFivePercentCriticalToken()
		{
			const int MaxHP = 200;

			// 49% HP: inside `heavy-damage-attained`, outside `critical-damage`.
			var burning = StateForHealth(98, MaxHP);
			Assert.That(burning, Is.EqualTo(DamageState.Heavy));
			Assert.That(Missile.GuidanceLost(true, burning), Is.True,
				"Keying on the `critical-damage` (25%) token instead would return false here.");

			// 24% HP: the band `critical-damage` actually covers.
			Assert.That(StateForHealth(48, MaxHP), Is.EqualTo(DamageState.Critical));
		}

		// Copied verbatim from mods/ww3mod/rules/weapons/weapons-missiles.yaml (WGM, the Bradley and
		// BMP wire-guided ATGM), trimmed to the fields under test.
		const string WireGuidedAtgm = @"
Missile:
	Speed: 300
	RangeLimit: 25c0
	ManualGuidance: true
	OperatorRetargetTicks: 50
";

		// Copied verbatim from the same file (Hellfire — laser-guided, deliberately NOT wire-guided).
		const string LaserGuidedAtgm = @"
Missile:
	Speed: 500
	RangeLimit: 27c0
	OperatorRetargetTicks: 50
";

		[Test]
		public void ShippedWeaponYamlPutsExactlyTheWireGuidedMissilesInScope()
		{
			var wgm = Load(WireGuidedAtgm);
			Assert.That(wgm.ManualGuidance, Is.True);
			Assert.That(Missile.GuidanceLost(wgm.ManualGuidance, DamageState.Heavy), Is.True);

			var hellfire = Load(LaserGuidedAtgm);
			Assert.That(hellfire.ManualGuidance, Is.False,
				"ManualGuidance defaults false; Hellfire never sets it, so it stays out of scope.");
			Assert.That(Missile.GuidanceLost(hellfire.ManualGuidance, DamageState.Heavy), Is.False);
		}
	}
}
