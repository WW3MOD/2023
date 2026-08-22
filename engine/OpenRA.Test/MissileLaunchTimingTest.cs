#region Copyright & License Information
/*
 * WW3MOD missile launch-timing tests.
 *
 * WHAT IS BEING PINNED: the gap between "the armament fired" and "the missile left the tube".
 *
 * A missile launcher fires a dummy weapon whose only effect is to spawn the missile actor
 * (MissileSpawnerMaster.Attacking). The missile then sits on the rail erecting for
 * BallisticMissile.PreLaunchTicks before BallisticMissileFly reaches Phase 2 and calls Ignite().
 * The weapon's own `Report` is played by Armament.FireBarrel in the SAME tick as the spawn
 * (Armament.cs:621-628 — the Report and the INotifyAttack.Attacking loop that spawns the missile
 * are consecutive statements in one delayed action). So on an erecting launcher a weapon `Report`
 * is early by exactly PreLaunchTicks, which the user heard as "the launch sound comes at the start
 * of the tilt animation".
 *
 * These tests take no World. They exercise the pure arithmetic through the real FieldLoader, using
 * the shipped YAML copied verbatim (the house convention — see FactionDescriptionSplitTest).
 * The paired guard against the mod DATA drifting back is the CheckMissileLaunchReport lint rule,
 * which reads the actual rules tree and is what `make test` runs.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class MissileLaunchTimingTest
	{
		// mods/ww3mod/mod.yaml — the default GameSpeed entry the mod ships on.
		const int TimestepMilliseconds = 60;

		static BallisticMissileInfo Load(string yaml)
		{
			var info = new BallisticMissileInfo();
			FieldLoader.Load(info, MiniYaml.FromString(yaml, nameof(MissileLaunchTimingTest))[0].Value);
			return info;
		}

		/// <summary>
		/// The Phase 1 guard from BallisticMissileFly.Tick, re-derived as a plain counting loop rather
		/// than by calling PreLaunchTicks — so agreement between the two is evidence, not a tautology.
		/// Returns the value of the activity's `ticks` counter on the tick Ignite() is first reached.
		/// </summary>
		static int IgnitionTick(BallisticMissileInfo info)
		{
			var ticks = 0;
			while (info.LaunchRiseTicks > 0 && ticks < info.LaunchRiseTicks + info.PostErectionWaitTicks)
				ticks++;

			return ticks;
		}

		// Copied verbatim from mods/ww3mod/rules/ingame/vehicles-russia.yaml (IskanderMissile,
		// BallisticMissile), comments stripped.
		const string IskanderMissile = @"
BallisticMissile:
	LaunchAngle: 110
	Speed: 600
	Acceleration: 3
	InitialSpeedPercent: 0
	LaunchRiseTicks: 60
	LaunchRiseErect: true
	LaunchRiseErectVisualOffset: -300, 0, 200
	PostErectionWaitTicks: 20
	IgnitionCondition: ignited
	IgnitionSound: vv3latta.aud, vv3lattb.aud
	VisualPitchMultiplier: 47
	TerminalSpeed: 600
	TerminalAcceleration: 10
";

		// Copied verbatim from mods/ww3mod/rules/ingame/vehicles-america.yaml (HIMARSMissile,
		// BallisticMissile), comments stripped. Note the absence of LaunchRiseTicks.
		const string HimarsMissile = @"
BallisticMissile:
	LaunchAngle: 80
	Speed: 500
	Acceleration: 4
	InitialSpeedPercent: 3
	VisualPitchMultiplier: 42
	TerminalSpeed: 550
	TerminalAcceleration: 7
";

		[Test]
		public void IskanderSitsOnTheRailForEightyTicksBeforeIgniting()
		{
			var info = Load(IskanderMissile);

			// 60 ticks of erection + 20 ticks held at full elevation.
			Assert.That(info.PreLaunchTicks, Is.EqualTo(80));
			Assert.That(IgnitionTick(info), Is.EqualTo(80),
				"the activity's Phase 1 guard must run out on exactly the tick PreLaunchTicks names");

			// This is the size of the defect: a weapon Report fired at spawn was heard this
			// long before the motor lit.
			Assert.That(info.PreLaunchTicks * TimestepMilliseconds, Is.EqualTo(4800));
		}

		[Test]
		public void IskanderCarriesItsLaunchReportOnIgnitionNotOnTheWeapon()
		{
			var info = Load(IskanderMissile);

			Assert.That(info.IgnitionSound, Is.EqualTo(new[] { "vv3latta.aud", "vv3lattb.aud" }),
				"the launch report belongs to the moment the motor lights, not to the armament firing");
		}

		[Test]
		public void HimarsFiresStraightFromTheTubeSoAWeaponReportIsCorrect()
		{
			var info = Load(HimarsMissile);

			// No pre-launch phase at all, so Ignite() lands on the missile's first tick in the
			// world and the report on HIMARSTargeter is right where it belongs.
			Assert.That(info.PreLaunchTicks, Is.Zero);
			Assert.That(IgnitionTick(info), Is.Zero);
			Assert.That(info.IgnitionSound, Is.Empty);
		}

		[Test]
		public void PostErectionWaitAloneBuysNoPreLaunchPhase()
		{
			// BallisticMissileFly gates the whole of Phase 1 on LaunchRiseTicks > 0, so a hold
			// configured without a rise is dead config. PreLaunchTicks must agree, or the lint
			// rule would flag a launcher whose missile does in fact ignite immediately.
			var info = Load("BallisticMissile:\n\tPostErectionWaitTicks: 20\n");

			Assert.That(info.PreLaunchTicks, Is.Zero);
			Assert.That(IgnitionTick(info), Is.Zero);
		}
	}
}
