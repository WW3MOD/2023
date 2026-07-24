#region Copyright & License Information
/*
 * WW3MOD Phase-4 role-model guard.
 *
 * Asserts UnitRoleResolver's DERIVED-role table against the actually loaded ww3mod
 * ruleset. The NUnit UnitRoleResolverTest covers the pure Classify cascade with
 * hand-encoded UnitRoleFacts; this pass closes the gap the phase-4a merge review
 * flagged: ExtractFacts run over the REAL ActorInfos (with resolved weapon data),
 * so a YAML edit that silently reclassifies a key combat unit — dropping an
 * artillery MinRange below the IndirectFire floor, adding weight-bearing Cargo to a
 * tank, removing an AA weapon's Air target — fails `make test` (--check-yaml) rather
 * than only surfacing as a subtle in-game AI regression once UseUnitRoles is on.
 *
 * Runs automatically: CheckYaml discovers every ILintRulesPass and runs it against
 * modData.DefaultRules (design WORKSPACE/plans/260722_unit_role_resolver_DESIGN.md §5).
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common.Lint
{
	public class CheckUnitRoleTable : ILintRulesPass
	{
		// Key combat units → expected derived role, verified against the ww3mod ruleset.
		// Actor keys are lowercased (ActorInfo.Name); faction variants included where the AI
		// actually fields the variant rather than the base template.
		static readonly (string Actor, UnitRole Role)[] Expected =
		{
			// Tanks — direct-fire main gun, range below the indirect floor.
			("abrams", UnitRole.MainBattle),
			("t90", UnitRole.MainBattle),

			// IFVs — their Cargo would derive TransportLift; pinned MainBattle by AIUnitRole override.
			("bradley", UnitRole.MainBattle),
			("bmp2", UnitRole.MainBattle),

			// Artillery — long MinRange (tube/mortar) or very long reach (rocket/missile).
			("m109", UnitRole.IndirectFire),
			("giatsint", UnitRole.IndirectFire),
			("grad", UnitRole.IndirectFire),
			("tos", UnitRole.IndirectFire),
			("m270", UnitRole.IndirectFire),
			("mt", UnitRole.IndirectFire),

			// SHORAD / MANPADS — Mobile + a weapon whose ValidTargets include Air.
			("strykershorad", UnitRole.ShortRangeAD),
			("tunguska", UnitRole.ShortRangeAD),
			("aa", UnitRole.ShortRangeAD),
			("aa.america", UnitRole.ShortRangeAD),
			("aa.russia", UnitRole.ShortRangeAD),

			// Neutral-tech capturers (capture TYPE building-neutral, not mere Captures presence).
			("tecn", UnitRole.CaptureSpecialist),
			("tecn.america", UnitRole.CaptureSpecialist),
			("tecn.russia", UnitRole.CaptureSpecialist),

			// Special Forces / Drone operator — armed, targets enemies, no override → fall through to
			// MainBattle. NOTE (F1): both are Prerequisites: ~disabled and absent from every AI reserve
			// list, so role mode cannot currently task them — this row pins the classification so a
			// future decision to field them is a conscious one, not a silent MainBattle leak.
			("sf", UnitRole.MainBattle),
			("sf.america", UnitRole.MainBattle),
			("dr", UnitRole.MainBattle),
			("dr.america", UnitRole.MainBattle),

			// Recon — fast, armed, light weapons only (carries Cargo but combat roles win first).
			("humvee", UnitRole.Recon),
			("btr", UnitRole.Recon),

			// Pure carrier — slow, MG-only self defence: the Cargo fall-through.
			("m113", UnitRole.TransportLift),

			// Logistics — supply provider (truk), heal-only (medi), lethal-armed engineer by override (e6).
			("truk", UnitRole.Logistics),
			("e6", UnitRole.Logistics),
			("medi", UnitRole.Logistics),

			// Line / screen infantry — direct fire, no qualifying MinRange, infantry speed.
			("e3", UnitRole.MainBattle),
			("ar", UnitRole.MainBattle),
			("at", UnitRole.MainBattle),
			("sn", UnitRole.MainBattle),
			("tl", UnitRole.MainBattle),
			("e2", UnitRole.MainBattle),
			("e4", UnitRole.MainBattle),

			// Aircraft — coarse role mapped from AIHelicopterRole.
			("littlebird", UnitRole.Recon),     // Scout
			("heli", UnitRole.AttackAir),        // AttackHeavy
			("hind", UnitRole.AttackAir),        // AttackHeavy
			("tran", UnitRole.TransportLift),    // Transport

			// Fixed-wing strike — aircraft + armament, no AIHelicopterRole → AttackAir. The experimental
			// fixed-wing SquadManager (Phase 4b) selects its Air squad by role (Buildable AttackAir,
			// non-heli); pinning these fails the build if a YAML edit drops them out of AttackAir and
			// silently empties the role-mode air squad.
			("a10", UnitRole.AttackAir),
			("f16", UnitRole.AttackAir),
			("mig", UnitRole.AttackAir),
			("frog", UnitRole.AttackAir),

			// Supply Route beachhead — a building: not mobile, not armed.
			("supplyroute", UnitRole.None),
		};

		// F3 real-data coverage of the CargoInfo bridge: MainBattle-classified carriers must be flagged
		// by the aligned IsTroopCarrier predicate (so LayeredDefence/PoiOffensive/PoiGarrison hold them
		// back and MountedTransport keeps them), while plain MainBattle tanks must NOT be.
		static readonly string[] MainBattleCarriers = { "bradley", "bmp2" };
		static readonly string[] MainBattleNonCarriers = { "abrams", "t90" };

		void ILintRulesPass.Run(Action<string> emitError, Action<string> emitWarning, ModData modData, Ruleset rules)
		{
			// Thresholds come from the resolver actually registered on the world actor, so the lint
			// tracks live config rather than a copy of the defaults. Absent (e.g. a minimal map that
			// strips the trait) → nothing to assert.
			var resolverInfo = rules.Actors.Values
				.Select(a => a.TraitInfoOrDefault<UnitRoleResolverInfo>())
				.FirstOrDefault(i => i != null);

			if (resolverInfo == null)
				return;

			var t = new UnitRoleThresholds(resolverInfo.IndirectMinRange, resolverInfo.IndirectRangeFloor,
				resolverInfo.ReconMaxWeaponRange, resolverInfo.ReconSpeedFloor, resolverInfo.NeutralCaptureType);

			UnitRole RoleOf(ActorInfo ai) => UnitRoleResolver.Classify(UnitRoleResolver.ExtractFacts(ai, t), t);

			foreach (var (actor, expected) in Expected)
			{
				if (!rules.Actors.TryGetValue(actor, out var ai))
				{
					emitError($"CheckUnitRoleTable: expected key unit `{actor}` not found in the ruleset.");
					continue;
				}

				var role = RoleOf(ai);
				if (role != expected)
					emitError($"CheckUnitRoleTable: `{actor}` derived role {role}, expected {expected}.");
			}

			foreach (var actor in MainBattleCarriers)
			{
				if (!rules.Actors.TryGetValue(actor, out var ai))
					continue;

				if (!UnitRoleResolver.IsTroopCarrier(ai))
					emitError($"CheckUnitRoleTable: `{actor}` is a MainBattle carrier but IsTroopCarrier is false — it would leak onto the role-mode line and starve MountedTransport.");

				if (RoleOf(ai) != UnitRole.MainBattle)
					emitError($"CheckUnitRoleTable: `{actor}` expected MainBattle (the held-back-carrier case).");
			}

			foreach (var actor in MainBattleNonCarriers)
			{
				if (!rules.Actors.TryGetValue(actor, out var ai))
					continue;

				if (UnitRoleResolver.IsTroopCarrier(ai))
					emitError($"CheckUnitRoleTable: `{actor}` is not a troop carrier but IsTroopCarrier is true — it would be wrongly held back from the line.");
			}

			// Optional eyeball dump of the full derived-role table (design §5). Opt-in and printed
			// once per process so it never spams the per-map lint runs: DUMP_UNIT_ROLES=1 make test.
			DumpTableOnce(rules, t);
		}

		static bool dumped;

		static void DumpTableOnce(Ruleset rules, UnitRoleThresholds t)
		{
			if (dumped || Environment.GetEnvironmentVariable("DUMP_UNIT_ROLES") != "1")
				return;

			dumped = true;
			var rows = rules.Actors.Values
				.Where(a => !a.Name.StartsWith("^", StringComparison.Ordinal))
				.Select(a => (a.Name, Role: UnitRoleResolver.Classify(UnitRoleResolver.ExtractFacts(a, t), t)))
				.OrderBy(r => r.Role)
				.ThenBy(r => r.Name, StringComparer.Ordinal)
				.ToList();

			Console.WriteLine("=== UnitRoleResolver derived-role table ===");
			foreach (var (name, role) in rows)
				Console.WriteLine($"  {role,-18} {name}");
		}
	}
}
