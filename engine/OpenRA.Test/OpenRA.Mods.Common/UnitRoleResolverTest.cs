#region Copyright & License Information
/*
 * WW3MOD unit-role resolver test — strategic/tactical split, Phase 3.
 */
#endregion

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class UnitRoleResolverTest
	{
		// Mirrors the UnitRoleResolverInfo defaults (last arg = RocketSalvoBurstFloor).
		static readonly UnitRoleThresholds Defaults = new(
			WDist.FromCells(4), new WDist(35 * 1024), new WDist(16 * 1024), 110, "building-neutral", 8);

		// Builds the ruleset facts for one actor. Named/optional args keep each row readable;
		// weapon ranges are given in whole cells, MinRange in raw WDist units (1c512 = 1536).
		static UnitRoleFacts Facts(
			bool mobile = false, int speed = 0, bool armed = false, bool targetsEnemy = false,
			bool airWeapon = false, int maxRangeCells = 0, int maxMinRangeUnits = 0, bool cargo = false,
			bool supply = false, bool capturesNeutral = false, bool aircraft = false,
			bool heliRole = false, HelicopterAIRole heli = default,
			bool hasOverride = false, UnitRole overrideRole = UnitRole.None, int burst = 0)
		{
			return new UnitRoleFacts(
				hasOverride, overrideRole, aircraft, heliRole, heli,
				capturesNeutral, supply, armed, targetsEnemy, airWeapon,
				new WDist(maxRangeCells * 1024), new WDist(maxMinRangeUnits), cargo, mobile, speed, burst);
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

			// --- Phase-4b air consumer coverage ---
			// Fixed-wing strike: aircraft + armament, NO AIHelicopterRole -> AttackAir. The experimental
			// fixed-wing SquadManager selects its Air squad from these (Buildable AttackAir, non-heli).
			("a10",           Facts(aircraft: true, armed: true), UnitRole.AttackAir),
			("f16",           Facts(aircraft: true, armed: true), UnitRole.AttackAir),
			("mig",           Facts(aircraft: true, armed: true), UnitRole.AttackAir),
			("frog",          Facts(aircraft: true, armed: true), UnitRole.AttackAir),
			// Attack helicopter maps AttackHeavy -> AttackAir; it carries AIHelicopterRole, so the air gate
			// below excludes it by trait (owned by HelicopterSquadBotModule, never the fixed-wing manager).
			("hind",          Facts(aircraft: true, armed: true, heliRole: true, heli: HelicopterAIRole.AttackHeavy), UnitRole.AttackAir),
			// Transport helicopter: no armament, has Cargo, Transport role -> TransportLift.
			("halo",          Facts(aircraft: true, cargo: true, heliRole: true, heli: HelicopterAIRole.Transport), UnitRole.TransportLift),

			// --- Phase-4 consumer coverage: the LayeredDefence / PoiOffensive / PoiGarrison roster ---
			// Line + screen infantry (base Mobile.Speed 25, direct-fire, no MinRange) -> MainBattle.
			// These MUST stay MainBattle so the migrated line/offense filters keep them eligible;
			// only artillery/SHORAD/MANPADS drop out (the ai.yaml:349 defect cure), never the infantry.
			("e3",            Facts(mobile: true, speed: 25, armed: true, targetsEnemy: true, maxRangeCells: 8), UnitRole.MainBattle),
			("ar",            Facts(mobile: true, speed: 25, armed: true, targetsEnemy: true, maxRangeCells: 8), UnitRole.MainBattle),
			// AT specialist: ATGM is Range 20c0 / MinRange 3c0 — MinRange below the 4c0 IndirectFire
			// floor and range below 35c0, so it is a line combatant, NOT indirect fire.
			("at",            Facts(mobile: true, speed: 25, armed: true, targetsEnemy: true, maxRangeCells: 20, maxMinRangeUnits: 3 * 1024), UnitRole.MainBattle),
			// Sniper: 7.62mm.Sniper Range 20c0 at infantry speed 25 (< Recon floor 110) -> MainBattle.
			("sn",            Facts(mobile: true, speed: 25, armed: true, targetsEnemy: true, maxRangeCells: 20), UnitRole.MainBattle),
			// Team leader: DMR 15c0 + GrenadeLauncher 12c0/MinRange 1c512 (below floor) -> MainBattle.
			("tl",            Facts(mobile: true, speed: 25, armed: true, targetsEnemy: true, maxRangeCells: 15, maxMinRangeUnits: 1536), UnitRole.MainBattle),
			// Grenadier: GrenadeLauncher 12c0 / MinRange 1c512 -> MainBattle (design §8 worked example).
			("e2",            Facts(mobile: true, speed: 25, armed: true, targetsEnemy: true, maxRangeCells: 12, maxMinRangeUnits: 1536), UnitRole.MainBattle),
			// Flamethrower: Flamespray 6c0, short-range direct fire -> MainBattle.
			("e4",            Facts(mobile: true, speed: 25, armed: true, targetsEnemy: true, maxRangeCells: 6), UnitRole.MainBattle),
			// Special Forces: 5.56mm.DMR.silencer 11c0 at speed 32 (< Recon floor), no MinRange, no
			// Cargo, no override -> MainBattle. Armed + MainBattle-classified + absent from every old
			// hard-coded list, so role mode would task them where the lists never did (F1 — see the
			// reserve-pool finding in the branch report/DISCOVERIES).
			("sf",            Facts(mobile: true, speed: 32, armed: true, targetsEnemy: true, maxRangeCells: 11), UnitRole.MainBattle),
			// Drone operator: DroneTargeter 25c0 + DroneJammer 20c0 (both < 35c0 floor, MinRange 0),
			// CarrierMaster (drone slave) is NOT CargoInfo, so no TransportLift; infantry speed -> MainBattle.
			("dr",            Facts(mobile: true, speed: 25, armed: true, targetsEnemy: true, maxRangeCells: 25), UnitRole.MainBattle),

			// Paladin: tube artillery, ^ArtilleryRound 40c0 / MinRange 10c0 -> IndirectFire.
			("paladin",       Facts(mobile: true, speed: 80, armed: true, targetsEnemy: true, maxRangeCells: 40, maxMinRangeUnits: 10 * 1024), UnitRole.IndirectFire),

			// Russian IFV — like bradley, pinned MainBattle by AIUnitRole override (would derive
			// TransportLift from its Cargo). Consumers additionally exclude it by CargoInfo trait so
			// MountedTransportBotModule keeps ferrying it (see the module comments / DISCOVERIES).
			("bmp2",          Facts(mobile: true, speed: 100, armed: true, targetsEnemy: true, maxRangeCells: 16, cargo: true, hasOverride: true, overrideRole: UnitRole.MainBattle), UnitRole.MainBattle),

			// Supply Route (indestructible beachhead): a building — not mobile, not armed -> None.
			("supplyroute",   Facts(), UnitRole.None),
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

		// Fires economics (PIPELINE item 19): tube vs rocket, keyed off max weapon Burst against the
		// RocketSalvoBurstFloor (8). Real WW3MOD salvo sizes: tube Giatsint 1 / Paladin 3; rocket M270 12 /
		// TOS 24 / Grad 40. Only the IndirectFire role is sub-classified; everything else is NotIndirect.
		[Test]
		public void IndirectFireKind_TubeVsRocket_ByBurst()
		{
			Assert.Multiple(() =>
			{
				(string Name, int Burst, IndirectFireKind Expected)[] arty =
				{
					("giatsint", 1, IndirectFireKind.Tube),
					("paladin", 3, IndirectFireKind.Tube),
					("m270", 12, IndirectFireKind.Rocket),
					("tos", 24, IndirectFireKind.Rocket),
					("grad", 40, IndirectFireKind.Rocket),
				};

				foreach (var a in arty)
				{
					// An IndirectFire piece (long MinRange) carrying the given salvo size.
					var facts = Facts(mobile: true, speed: 80, armed: true, targetsEnemy: true,
						maxRangeCells: 40, maxMinRangeUnits: 10 * 1024, burst: a.Burst);
					var role = UnitRoleResolver.Classify(facts, Defaults);
					Assert.That(role, Is.EqualTo(UnitRole.IndirectFire), $"{a.Name} must classify IndirectFire first");

					var kind = UnitRoleResolver.ClassifyIndirectKind(role, facts, Defaults);
					Assert.That(kind, Is.EqualTo(a.Expected), $"{a.Name} (burst {a.Burst}) kind {kind}, expected {a.Expected}");
				}

				// Exactly at the floor counts as rocket (>=), one below is tube.
				var atFloor = Facts(mobile: true, speed: 80, armed: true, targetsEnemy: true,
					maxRangeCells: 40, maxMinRangeUnits: 10 * 1024, burst: 8);
				Assert.That(UnitRoleResolver.ClassifyIndirectKind(UnitRole.IndirectFire, atFloor, Defaults),
					Is.EqualTo(IndirectFireKind.Rocket), "burst == floor is rocket");

				// A non-IndirectFire role is never sub-classified, whatever its burst.
				var tank = Facts(mobile: true, speed: 90, armed: true, targetsEnemy: true, maxRangeCells: 25, burst: 40);
				Assert.That(UnitRoleResolver.ClassifyIndirectKind(UnitRole.MainBattle, tank, Defaults),
					Is.EqualTo(IndirectFireKind.NotIndirect), "a tank is NotIndirect regardless of burst");
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

		// --- F3: module-level line/offense eligibility across the CargoInfo bridge ---
		// Mirrors LayeredDefenceBotModule.IsLineEligibleByRole / Poi{Offensive,Garrison}
		// IsEligibleCombatUnit for a MainBattle unit: role == MainBattle && !IsTroopCarrier(info).
		// The CargoInfo bridge is the aligned IsTroopCarrier predicate (MaxWeight > 0), the single
		// source of truth shared with the resolver cascade.
		static bool RoleModeLineEligible(ActorInfo info)
		{
			var role = UnitRoleResolver.Classify(UnitRoleResolver.ExtractFacts(info, Defaults), Defaults);
			return role == UnitRole.MainBattle && !UnitRoleResolver.IsTroopCarrier(info);
		}

		// A MainBattle unit (pinned by AIUnitRole override, exactly like bradley/bmp2) carrying a
		// Cargo of the given MaxWeight. maxWeight < 0 means "no CargoInfo at all" (a plain tank).
		static ActorInfo MainBattleActor(string name, int maxWeight)
		{
			var role = new AIUnitRoleInfo();
			SetReadonly(role, nameof(AIUnitRoleInfo.Role), UnitRole.MainBattle);

			if (maxWeight < 0)
				return new ActorInfo(name, role);

			var cargo = new CargoInfo();
			SetReadonly(cargo, nameof(CargoInfo.MaxWeight), maxWeight);
			return new ActorInfo(name, role, cargo);
		}

		static void SetReadonly(object info, string field, object value)
		{
			info.GetType().GetField(field).SetValue(info, value);
		}

		[Test]
		public void RoleModeHoldsBackCargoCarriers()
		{
			// bradley/bmp2 shape: MainBattle by override, real carrier (MaxWeight 12) -> held back so
			// MountedTransportBotModule keeps ferrying them.
			var bradley = MainBattleActor("bradley", 12);
			var bmp2 = MainBattleActor("bmp2", 12);

			// abrams/t90 shape: MainBattle, no Cargo at all -> holds the line.
			var abrams = MainBattleActor("abrams", -1);

			// ^CargoPips shape: a weight-0 Cargo (garrison-pip decoration only) is NOT a carrier, so it
			// must NOT be held back. This is the latent divergence Item 3 aligned: the old bridge used
			// !HasTraitInfo<CargoInfo>() and would have wrongly benched this unit.
			var pipsOnly = MainBattleActor("pips-decorated-tank", 0);

			Assert.Multiple(() =>
			{
				Assert.That(UnitRoleResolver.IsTroopCarrier(bradley), Is.True, "bradley is a real carrier");
				Assert.That(UnitRoleResolver.IsTroopCarrier(bmp2), Is.True, "bmp2 is a real carrier");
				Assert.That(UnitRoleResolver.IsTroopCarrier(abrams), Is.False, "abrams has no Cargo");
				Assert.That(UnitRoleResolver.IsTroopCarrier(pipsOnly), Is.False, "weight-0 Cargo is not a carrier");

				Assert.That(RoleModeLineEligible(bradley), Is.False, "bradley held back from role-mode line");
				Assert.That(RoleModeLineEligible(bmp2), Is.False, "bmp2 held back from role-mode line");
				Assert.That(RoleModeLineEligible(abrams), Is.True, "plain MainBattle tank passes");
				Assert.That(RoleModeLineEligible(pipsOnly), Is.True, "weight-0-Cargo MainBattle unit passes");
			});
		}

		// --- Phase-4b consumer coverage ---

		// Builds an aircraft ActorInfo for the air-squad gate test. AircraftInfo + ArmamentInfo makes an
		// armed airframe (ExtractFacts sees HasArmament even with an unresolved weapon); BuildableInfo and
		// AIHelicopterRoleInfo toggle the two trait guards the SquadManager gate reads.
		static ActorInfo Aircraft(string name, bool armed, bool buildable, bool heliRole, HelicopterAIRole heli = default)
		{
			var traits = new List<TraitInfo> { new AircraftInfo() };
			if (armed)
				traits.Add(new ArmamentInfo());
			if (buildable)
				traits.Add(new BuildableInfo());
			if (heliRole)
			{
				var h = new AIHelicopterRoleInfo();
				SetReadonly(h, nameof(AIHelicopterRoleInfo.Role), heli);
				traits.Add(h);
			}

			return new ActorInfo(name, traits.ToArray());
		}

		// Mirror of SquadManagerBotModule.IsAirSquadUnit's role branch: a Buildable AttackAir airframe
		// that is NOT a helicopter (attack helis stay owned by HelicopterSquadBotModule; -Buildable
		// airstrike-power spawns are never squad-managed).
		static bool RoleModeAirSquadEligible(ActorInfo info)
		{
			var role = UnitRoleResolver.Classify(UnitRoleResolver.ExtractFacts(info, Defaults), Defaults);
			return role == UnitRole.AttackAir
				&& info.HasTraitInfo<BuildableInfo>()
				&& !info.HasTraitInfo<AIHelicopterRoleInfo>();
		}

		[Test]
		public void AirSquadRoleGateSelectsBuildableFixedWingStrike()
		{
			var fixedWing = Aircraft("a10", armed: true, buildable: true, heliRole: false);
			var attackHeli = Aircraft("hind", armed: true, buildable: true, heliRole: true, HelicopterAIRole.AttackHeavy);
			var airstrike = Aircraft("a10.airstrike", armed: true, buildable: false, heliRole: false);

			Assert.Multiple(() =>
			{
				Assert.That(RoleModeAirSquadEligible(fixedWing), Is.True, "buildable fixed-wing strike joins the Air squad");
				Assert.That(RoleModeAirSquadEligible(attackHeli), Is.False, "attack helis stay with HelicopterSquadBotModule");
				Assert.That(RoleModeAirSquadEligible(airstrike), Is.False, "airstrike-power spawns are never squad-managed");
			});
		}

		[Test]
		public void CaptureRoleSelectsOnlyNeutralCapturers()
		{
			// CaptureCoordinator's role-mode pool is the CaptureSpecialist class (Captures targeting the
			// neutral-tech type). A line-infantry — which may carry Captures for occupied buildings but not
			// the neutral type — is NOT selected: the 'wrong unit sent to capture' cure by class.
			var tecn = UnitRoleResolver.Classify(Facts(mobile: true, speed: 63, capturesNeutral: true), Defaults);
			var lineInfantry = UnitRoleResolver.Classify(
				Facts(mobile: true, speed: 25, armed: true, targetsEnemy: true, maxRangeCells: 8), Defaults);

			Assert.Multiple(() =>
			{
				Assert.That(tecn, Is.EqualTo(UnitRole.CaptureSpecialist));
				Assert.That(lineInfantry, Is.Not.EqualTo(UnitRole.CaptureSpecialist));
			});
		}

		[Test]
		public void AdaptiveProductionRoleFiltersMatchCounterCategories()
		{
			// Mirror of AdaptiveProductionBotModule's per-category allowed-role sets.
			var antiAir = new[] { UnitRole.ShortRangeAD };
			var antiVehicle = new[] { UnitRole.MainBattle, UnitRole.IndirectFire };
			var antiInfantry = new[] { UnitRole.MainBattle, UnitRole.IndirectFire, UnitRole.Recon };

			UnitRole R(UnitRoleFacts f) => UnitRoleResolver.Classify(f, Defaults);

			// Representative pool members (facts mirror the ww3mod stats used elsewhere in this file).
			var shorad = R(Facts(mobile: true, speed: 120, armed: true, targetsEnemy: true, airWeapon: true, maxRangeCells: 28, cargo: true)); // strykershorad
			var tank = R(Facts(mobile: true, speed: 90, armed: true, targetsEnemy: true, maxRangeCells: 25, maxMinRangeUnits: 1536)); // abrams
			var arty = R(Facts(mobile: true, speed: 80, armed: true, targetsEnemy: true, maxRangeCells: 40, maxMinRangeUnits: 10 * 1024)); // m109
			var scout = R(Facts(mobile: true, speed: 150, armed: true, targetsEnemy: true, maxRangeCells: 15, cargo: true)); // humvee

			Assert.Multiple(() =>
			{
				// Anti-air admits only SHORAD; a tank mistakenly listed there would be pruned.
				Assert.That(antiAir, Does.Contain(shorad));
				Assert.That(antiAir, Does.Not.Contain(tank));

				// Anti-vehicle admits line combatants + artillery, not SHORAD or scouts.
				Assert.That(antiVehicle, Does.Contain(tank));
				Assert.That(antiVehicle, Does.Contain(arty));
				Assert.That(antiVehicle, Does.Not.Contain(shorad));
				Assert.That(antiVehicle, Does.Not.Contain(scout));

				// Anti-infantry additionally admits Recon (light wheeled scouts like humvee/btr).
				Assert.That(antiInfantry, Does.Contain(tank));
				Assert.That(antiInfantry, Does.Contain(scout));
				Assert.That(antiInfantry, Does.Not.Contain(shorad));
			});
		}
	}
}
