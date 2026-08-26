#region Copyright & License Information
/*
 * WW3MOD anti-overkill timing pins for AA missiles.
 *
 * Reported from playtest 260826: "Tunguska fires two missiles at the same target ... I am not sure
 * if the second missile fired at the crashing helicopter, it should not do that."
 *
 * There are two candidate explanations and they call for opposite fixes, so the numbers that
 * separate them are pinned here rather than argued in prose.
 *
 *   FLIGHT WINDOW — how long missile one can still be airborne. 9M311's BurstWait was raised to 58
 *   in 9be6f8e0 because 58 is the missile's MAXIMUM POSSIBLE LIFETIME (the RangeLimit cull), so a
 *   second missile can never leave the rail while the first is unresolved. That holds.
 *
 *   CRASH WINDOW — how long a helicopter that has ALREADY been killed stays visible in the air.
 *   If this were longer than the burst gap, "more ticks" would be the fix. It is not: a crash
 *   descent is 26 ticks against a 58-tick gap, and a killed helicopter is replaced by a husk
 *   (TargetTypes: NoAutoTarget, AirborneActor, Husk) that 9M311's `ValidTargets: Air` cannot match
 *   anyway. So the wasted missile is NOT spent on a dead helicopter.
 *
 * What is left is a helicopter that is ALIVE at Critical damage: HeliEmergencyLanding puts it into
 * an uncontrolled crash descent from which it cannot recover, while it keeps Targetable@Helicopter
 * and stays a perfectly ordinary target. AutoTargetInfo.BreakOffCondition exists for exactly that
 * and did not reach it — see AttackFollow.Tick, which had the guard on its opportunity target and
 * not on its requested target.
 *
 * Pure arithmetic over verbatim shipped values, no World, following MissileLaunchTimingTest.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class AaOverkillWindowTest
	{
		// mods/ww3mod/mod.yaml — Timestep: 60, i.e. 16.67 ticks/second. NOT 25.
		const int TimestepMilliseconds = 60;

		// Copied verbatim from mods/ww3mod/rules/weapons/weapons-missiles.yaml.
		// Stinger (:535-541) supplies the projectile; 9M311 (:599-631) inherits it untouched and
		// overrides only BurstWait. 9M311 sets no Burst, so WeaponInfo's default of 1 applies — which
		// is what puts every shot through Armament.UpdateBurst's `--Burst < 1` branch and makes
		// BurstWait, not BurstDelays, the whole gap between consecutive missiles.
		const int MissileRangeLimit = 30 * 1024;    // RangeLimit: 30c0
		const int MissileSpeed = 600;
		const int MissileAcceleration = 35;
		const int MissileMaximumLaunchSpeed = 50;
		const int NineM311Burst = 1;
		const int NineM311BurstWait = 58;

		// Copied verbatim from mods/ww3mod/rules/ingame/aircraft.yaml ^Helicopter (:200-206).
		const int CrashDescentRate = 50;
		const int AutorotationDescentRate = 20;

		/// <summary>Ticks a Missile survives before the RangeLimit cull detonates it, re-derived as a
		/// plain counting loop. Missile.cs:538 adds Acceleration per tick clamped to Speed, starting
		/// from MaximumLaunchSpeed; Missile.cs:1159,1164 detonate the tick distanceCovered passes
		/// RangeLimit. ExplodeWhenEmpty is true on Stinger, so the cull really applies.</summary>
		static int MaximumMissileLifetime(int rangeLimit, int speed, int acceleration, int launchSpeed)
		{
			var covered = 0;
			var v = launchSpeed;
			for (var tick = 1; tick < 10000; tick++)
			{
				v = System.Math.Min(v + acceleration, speed);
				covered += v;
				if (covered > rangeLimit)
					return tick;
			}

			return -1;
		}

		/// <summary>Ticks for a FallToEarth-style constant-rate descent to reach the ground, rounded up:
		/// the activity only stops once DistanceAboveTerrain has actually reached zero.</summary>
		static int DescentTicks(int altitude, int ratePerTick)
		{
			return (altitude + ratePerTick - 1) / ratePerTick;
		}

		static int CruiseAltitude()
		{
			// Helicopters do not override CruiseAltitude anywhere in the mod, so the AircraftInfo
			// default is the live value. Read it off the real trait rather than restating it.
			return new AircraftInfo().CruiseAltitude.Length;
		}

		[Test]
		public void TheBurstGapCoversTheWholeMissileFlight()
		{
			var lifetime = MaximumMissileLifetime(
				MissileRangeLimit, MissileSpeed, MissileAcceleration, MissileMaximumLaunchSpeed);

			Assert.That(lifetime, Is.EqualTo(58),
				"the derivation BurstWait: 58 was sized on — if the projectile is retuned this moves");

			Assert.That(NineM311BurstWait, Is.GreaterThanOrEqualTo(lifetime),
				"a second 9M311 must never leave the rail while the first is still airborne");

			// The user's own wording: "the first missile hits before the second is launched".
			Assert.That(NineM311BurstWait * TimestepMilliseconds, Is.EqualTo(3480));
		}

		[Test]
		public void BurstWaitIsTheGapBecauseTheWeaponFiresSingleShotBursts()
		{
			// With Burst: 1 the `--Burst < 1` branch in Armament.UpdateBurst is taken on EVERY shot,
			// so BurstDelays is unreachable and BurstWait alone spaces consecutive missiles. A future
			// edit that gives 9M311 a Burst > 1 silently moves the gap onto BurstDelays and voids the
			// guarantee above without touching BurstWait.
			Assert.That(NineM311Burst, Is.EqualTo(1));
		}

		[Test]
		public void ADeadHelicopterIsOnTheGroundLongBeforeTheNextMissileIsDue()
		{
			// This is the number that rules OUT "just add more ticks". If a killed helicopter lingered
			// in the air past the burst gap, a longer gap would be the fix. It does not.
			var crash = DescentTicks(CruiseAltitude(), CrashDescentRate);

			Assert.That(crash, Is.EqualTo(26));
			Assert.That(crash, Is.LessThan(NineM311BurstWait),
				"the crash window is already shorter than the existing burst gap, so no increase to " +
				"BurstWait can be the fix for a missile spent on a helicopter that is on its way down");
		}

		[Test]
		public void AutorotationOutlastsTheBurstGapButIsNotTheBreakOffState()
		{
			// Stated so the asymmetry is deliberate rather than forgotten. Autorotation begins at
			// HEAVY damage and is survivable — the helicopter can still land. It grants
			// `autorotation`, not `critical-damage`, so BreakOffCondition does not and should not
			// reach it, even though its descent is longer than one burst gap.
			var autorotation = DescentTicks(CruiseAltitude(), AutorotationDescentRate);

			Assert.That(autorotation, Is.EqualTo(64));
			Assert.That(autorotation, Is.GreaterThan(NineM311BurstWait));
		}

		// The scope of BreakOffApplies — which sources the AttackFollow guard may fire for — is pinned
		// once, in BreakOffScopeTest. Deliberately not restated here: two files asserting one predicate
		// drift apart, and that file is the older and more complete of the two.
	}
}
