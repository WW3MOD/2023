#region Copyright & License Information
/*
 * WW3MOD unit-role resolver test — strategic/tactical split, Phase 3.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class UnitRoleResolverTest
	{
		// Mirrors the UnitRoleResolverInfo defaults.
		static readonly UnitRoleThresholds Defaults = new(
			WDist.FromCells(4), new WDist(35 * 1024), new WDist(16 * 1024), 110, "building-neutral");

		// Builds the ruleset facts for one actor. Named/optional args keep each row readable;
		// weapon ranges are given in whole cells, MinRange in raw WDist units (1c512 = 1536).
		static UnitRoleFacts Facts(
			bool mobile = false, int speed = 0, bool armed = false, bool targetsEnemy = false,
			bool airWeapon = false, int maxRangeCells = 0, int maxMinRangeUnits = 0, bool cargo = false,
			bool supply = false, bool capturesNeutral = false, bool aircraft = false,
			bool heliRole = false, HelicopterAIRole heli = default,
			bool hasOverride = false, UnitRole overrideRole = UnitRole.None)
		{
			return new UnitRoleFacts(
				hasOverride, overrideRole, aircraft, heliRole, heli,
				capturesNeutral, supply, armed, targetsEnemy, airWeapon,
				new WDist(maxRangeCells * 1024), new WDist(maxMinRangeUnits), cargo, mobile, speed);
		}

		// The audit classification table (WORKSPACE/plans/260722_phase3_redteam.md §6),
		// each row's facts encoding the real, verified WW3MOD YAML stats for that actor.
		static readonly (string Name, UnitRoleFacts Facts, UnitRole Expected)[] Table =
		{
			// Tube / rocket / missile artillery — long MinRange or very long reach -> IndirectFire.
			("m109",          Facts(mobile: true, speed: 80, armed: true, targetsEnemy: true, maxRangeCells: 40, maxMinRangeUnits: 10 * 1024), UnitRole.IndirectFire),
			("giatsint",      Facts(mobile: true, speed: 80, armed: true, targetsEnemy: true, maxRangeCells: 40, maxMinRangeUnits: 10 * 1024), UnitRole.IndirectFire),
			("m270",          Facts(mobile: true, speed: 80, armed: true, targetsEnemy: true, maxRangeCells: 40, maxMinRangeUnits: 12 * 1024), UnitRole.IndirectFire),
			// grad moves at 110 (Recon speed floor) yet must stay IndirectFire — proves rule 6 precedes rule 7.
			("grad",          Facts(mobile: true, speed: 110, armed: true, targetsEnemy: true, maxRangeCells: 40, maxMinRangeUnits: 12 * 1024), UnitRole.IndirectFire),
			// tos reaches only 28c0 (< 35c0 floor) — classified via the MinRange clause alone.
			("tos",           Facts(mobile: true, speed: 80, armed: true, targetsEnemy: true, maxRangeCells: 28, maxMinRangeUnits: 5 * 1024), UnitRole.IndirectFire),
			// Light mortar infantry — MinRange 8c0 trips the same rule.
			("mt",            Facts(mobile: true, speed: 63, armed: true, targetsEnemy: true, maxRangeCells: 25, maxMinRangeUnits: 8 * 1024), UnitRole.IndirectFire),

			// Fast, light, cargo-carrying wheeled scouts — Recon despite carrying passengers
			// (proves rules 5-7 precede the Cargo->TransportLift catch, rule 8).
			("humvee",        Facts(mobile: true, speed: 150, armed: true, targetsEnemy: true, maxRangeCells: 15, cargo: true), UnitRole.Recon),
			("btr",           Facts(mobile: true, speed: 110, armed: true, targetsEnemy: true, maxRangeCells: 16, cargo: true), UnitRole.Recon),

			// Air-defence: a weapon with ValidTargets "Air" + Mobile -> ShortRangeAD.
			// strykershorad also has Cargo — AD must win over TransportLift.
			("strykershorad", Facts(mobile: true, speed: 120, armed: true, targetsEnemy: true, airWeapon: true, maxRangeCells: 28, cargo: true), UnitRole.ShortRangeAD),
			("tunguska",      Facts(mobile: true, speed: 100, armed: true, targetsEnemy: true, airWeapon: true, maxRangeCells: 28, maxMinRangeUnits: 1536), UnitRole.ShortRangeAD),
			("aa",            Facts(mobile: true, speed: 54, armed: true, targetsEnemy: true, airWeapon: true, maxRangeCells: 23), UnitRole.ShortRangeAD),

			// Neutral-tech capturer.
			("tecn",          Facts(mobile: true, speed: 63, capturesNeutral: true), UnitRole.CaptureSpecialist),

			// Logistics: supply provider (unarmed), and via override for the armed engineer.
			("truk",          Facts(mobile: true, speed: 75, supply: true), UnitRole.Logistics),
			("e6",            Facts(mobile: true, speed: 63, armed: true, targetsEnemy: true, maxRangeCells: 10, hasOverride: true, overrideRole: UnitRole.Logistics), UnitRole.Logistics),
			// Medic: only armament is Heal (targets allies, never enemies) -> derived Logistics.
			("medi",          Facts(mobile: true, speed: 63, armed: true, targetsEnemy: false), UnitRole.Logistics),

			// IFVs pinned to the line of battle by override (would otherwise derive TransportLift).
			("bradley",       Facts(mobile: true, speed: 100, armed: true, targetsEnemy: true, maxRangeCells: 30, cargo: true, hasOverride: true, overrideRole: UnitRole.MainBattle), UnitRole.MainBattle),

			// Pure carrier — slow (< Recon floor), no air/indirect weapon -> TransportLift.
			("m113",          Facts(mobile: true, speed: 100, armed: true, targetsEnemy: true, maxRangeCells: 16, cargo: true), UnitRole.TransportLift),

			// Tank hulls — direct-fire main guns (MinRange 1c512, range < floor) -> MainBattle.
			("abrams",        Facts(mobile: true, speed: 90, armed: true, targetsEnemy: true, maxRangeCells: 25, maxMinRangeUnits: 1536), UnitRole.MainBattle),
			("t90",           Facts(mobile: true, speed: 90, armed: true, targetsEnemy: true, maxRangeCells: 24, maxMinRangeUnits: 1536), UnitRole.MainBattle),

			// Aircraft split via AIHelicopterRole (Scout -> Recon, armed).
			("littlebird",    Facts(aircraft: true, armed: true, heliRole: true, heli: HelicopterAIRole.Scout), UnitRole.Recon),
		};

		[Test]
		public void ClassificationTableMatchesAudit()
		{
			Assert.Multiple(() =>
			{
				foreach (var row in Table)
				{
					var actual = UnitRoleResolver.Classify(row.Facts, Defaults);
					Assert.That(actual, Is.EqualTo(row.Expected),
						$"{row.Name} classified {actual}, expected {row.Expected}");
				}
			});
		}

		[Test]
		public void OverrideBeatsDerivation()
		{
			// Facts that would derive TransportLift, overridden to MainBattle.
			var f = Facts(mobile: true, speed: 100, armed: true, targetsEnemy: true, cargo: true,
				hasOverride: true, overrideRole: UnitRole.MainBattle);
			Assert.That(UnitRoleResolver.Classify(f, Defaults), Is.EqualTo(UnitRole.MainBattle));
		}

		[Test]
		public void CargoDoesNotShadowCombatRoles()
		{
			// The finding-B3 regression guard: a cargo-carrying unit that is also AD / fast-light
			// must NOT be swallowed by the Cargo->TransportLift rule.
			var shorad = Facts(mobile: true, speed: 120, armed: true, targetsEnemy: true, airWeapon: true, maxRangeCells: 28, cargo: true);
			var recon = Facts(mobile: true, speed: 150, armed: true, targetsEnemy: true, maxRangeCells: 15, cargo: true);
			Assert.Multiple(() =>
			{
				Assert.That(UnitRoleResolver.Classify(shorad, Defaults), Is.EqualTo(UnitRole.ShortRangeAD));
				Assert.That(UnitRoleResolver.Classify(recon, Defaults), Is.EqualTo(UnitRole.Recon));
			});
		}

		[Test]
		public void UnclassifiableIsNone()
		{
			// A building/husk: no armament, not mobile, no cargo, no capture/supply.
			Assert.That(UnitRoleResolver.Classify(Facts(), Defaults), Is.EqualTo(UnitRole.None));
		}
	}
}
