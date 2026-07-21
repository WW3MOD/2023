#region Copyright & License Information
/*
 * WW3MOD unit-role resolver — strategic/tactical split, Phase 3 (data-only track).
 *
 * A single closed taxonomy (UnitRole) plus a world-level classifier that assigns
 * every actor a coarse doctrinal role ONCE at map load, cached for O(1) queries.
 * It replaces the hand-maintained YAML type-name lists each bot module keeps
 * today ("which units are artillery / SHORAD / carriers / scouts?") with one
 * derivation, and cures the ai.yaml:349 artillery+SHORAD-as-mainline conflation.
 *
 * Hybrid model (mandate DOCS/design/ai-realism.md §4): a YAML-facing AIUnitRole
 * override wins outright; otherwise the role is derived from the unit's traits
 * and weapon stats. Derivation is a PURE function of the ruleset — no RNG, no
 * per-player state, no tick input — so it is identical on every client by
 * construction and cannot desync.
 *
 * INERT IN THIS PHASE: nothing reads the resolver yet (Phase-4 consumers gate
 * behind their own per-module UseUnitRoles flag, default-off). Registering this
 * trait and annotating units with AIUnitRole is behaviour-inert for every
 * profile — @stable stays byte-identical.
 *
 * Design + audit: WORKSPACE/plans/260722_unit_role_resolver_DESIGN.md,
 * WORKSPACE/plans/260722_phase3_redteam.md (finding B3 — the cascade order below
 * is the reordered, corrected form: capture/logistics/AD/indirect/recon are all
 * tested BEFORE the Cargo→TransportLift catch, or armed IFVs and AA vehicles
 * would be miscategorised as troop carriers).
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public enum UnitRole
	{
		None,               // buildings, husks, dummies, unclassifiable
		MainBattle,         // holds/advances the line (tanks, line infantry, ATGM teams)
		IndirectFire,       // standoff fires: suppressive during assault, continuous bombardment
		ShortRangeAD,       // air-defence overwatch of the force; threat-proportionate purchase
		Recon,              // ahead of the force; screens; Phase-4 stale-intel tasking
		TransportLift,      // ferries infantry; mounted-doctrine executor owns them
		CaptureSpecialist,  // neutral-tech capture tasking + ferry priority (tecn)
		Logistics,          // resupply/repair/medical: follows the force, never line duty
		AttackAir           // sortie-cycle air assets, owned by the air squad modules
	}

	[Desc("Overrides the AI role derived by UnitRoleResolver for this actor. ",
		"Mirror of the AIHelicopterRole precedent — an explicit, YAML-facing role hint.")]
	public class AIUnitRoleInfo : TraitInfo
	{
		[Desc("AI role for this actor. Absolute — wins over derivation (hybrid model per the mandate).")]
		public readonly UnitRole Role = UnitRole.None;

		public override object Create(ActorInitializer init) { return new AIUnitRole(this); }
	}

	public class AIUnitRole
	{
		public readonly AIUnitRoleInfo Info;
		public AIUnitRole(AIUnitRoleInfo info) { Info = info; }
	}

	// Tunable thresholds, snapshotted from the Info so the pure classifier needs no trait handle.
	public readonly struct UnitRoleThresholds
	{
		public readonly WDist IndirectMinRange;
		public readonly WDist IndirectRangeFloor;
		public readonly WDist ReconMaxWeaponRange;
		public readonly int ReconSpeedFloor;
		public readonly string NeutralCaptureType;

		public UnitRoleThresholds(WDist indirectMinRange, WDist indirectRangeFloor,
			WDist reconMaxWeaponRange, int reconSpeedFloor, string neutralCaptureType)
		{
			IndirectMinRange = indirectMinRange;
			IndirectRangeFloor = indirectRangeFloor;
			ReconMaxWeaponRange = reconMaxWeaponRange;
			ReconSpeedFloor = reconSpeedFloor;
			NeutralCaptureType = neutralCaptureType;
		}
	}

	// The set of ruleset facts the cascade reads, extracted once per actor. Splitting
	// extraction (ruleset-bound) from Classify (pure) lets the priority cascade — the
	// part the B3 reorder fix protects — be unit-tested without mounting a full mod.
	public readonly struct UnitRoleFacts
	{
		public readonly bool HasOverride;
		public readonly UnitRole OverrideRole;
		public readonly bool IsAircraft;
		public readonly bool HasHeliRole;
		public readonly HelicopterAIRole HeliRole;
		public readonly bool CapturesNeutral;
		public readonly bool HasSupplyProvider;
		public readonly bool HasArmament;
		public readonly bool AnyArmamentTargetsEnemy;
		public readonly bool HasAirWeapon;
		public readonly WDist MaxWeaponRange;
		public readonly WDist MaxWeaponMinRange;
		public readonly bool HasCargo;
		public readonly bool IsMobile;
		public readonly int Speed;

		public UnitRoleFacts(bool hasOverride, UnitRole overrideRole, bool isAircraft,
			bool hasHeliRole, HelicopterAIRole heliRole, bool capturesNeutral, bool hasSupplyProvider,
			bool hasArmament, bool anyArmamentTargetsEnemy, bool hasAirWeapon,
			WDist maxWeaponRange, WDist maxWeaponMinRange, bool hasCargo, bool isMobile, int speed)
		{
			HasOverride = hasOverride;
			OverrideRole = overrideRole;
			IsAircraft = isAircraft;
			HasHeliRole = hasHeliRole;
			HeliRole = heliRole;
			CapturesNeutral = capturesNeutral;
			HasSupplyProvider = hasSupplyProvider;
			HasArmament = hasArmament;
			AnyArmamentTargetsEnemy = anyArmamentTargetsEnemy;
			HasAirWeapon = hasAirWeapon;
			MaxWeaponRange = maxWeaponRange;
			MaxWeaponMinRange = maxWeaponMinRange;
			HasCargo = hasCargo;
			IsMobile = isMobile;
			Speed = speed;
		}
	}

	[TraitLocation(SystemActors.World)]
	[Desc("Classifies every actor into a coarse UnitRole once at map load; O(1) queries thereafter. ",
		"Data-only: issues no orders, ticks nothing, mutates no sim state. No consumers in this phase.")]
	public class UnitRoleResolverInfo : TraitInfo
	{
		[Desc("A weapon whose MinRange is at least this classifies its owner as IndirectFire (mortars, tube/rocket arty).")]
		public readonly WDist IndirectMinRange = WDist.FromCells(4);

		[Desc("A weapon whose Range is at least this classifies its owner as IndirectFire (long-reach missile arty).")]
		public readonly WDist IndirectRangeFloor = new WDist(35 * 1024);

		[Desc("Recon candidates must have every weapon at or below this range (screens carry light armament only).")]
		public readonly WDist ReconMaxWeaponRange = new WDist(16 * 1024);

		[Desc("Recon candidates must move at least this fast (Mobile.Speed).")]
		public readonly int ReconSpeedFloor = 110;

		[Desc("Capture type that marks a CaptureSpecialist (neutral-tech capture). Matched against Captures.CaptureTypes.")]
		public readonly string NeutralCaptureType = "building-neutral";

		public override object Create(ActorInitializer init) { return new UnitRoleResolver(this); }
	}

	public class UnitRoleResolver : IWorldLoaded
	{
		readonly UnitRoleThresholds thresholds;
		readonly Dictionary<string, UnitRole> rolesByName = new();
		readonly Dictionary<UnitRole, List<string>> namesByRole = new();

		public UnitRoleResolver(UnitRoleResolverInfo info)
		{
			thresholds = new UnitRoleThresholds(info.IndirectMinRange, info.IndirectRangeFloor,
				info.ReconMaxWeaponRange, info.ReconSpeedFloor, info.NeutralCaptureType);
		}

		void IWorldLoaded.WorldLoaded(World w, OpenRA.Graphics.WorldRenderer wr)
		{
			// One pass over the ruleset actually in effect for this world (map rule overrides
			// included — the ruleset is per-map, so this cache must be per-world, not per-mod).
			foreach (var ai in w.Map.Rules.Actors.Values)
			{
				// Abstract templates (^-prefixed) are not spawnable actors; skip them.
				if (ai.Name.StartsWith("^", System.StringComparison.Ordinal))
					continue;

				var role = Classify(ExtractFacts(ai, thresholds), thresholds);
				rolesByName[ai.Name] = role;

				if (!namesByRole.TryGetValue(role, out var list))
					namesByRole[role] = list = new List<string>();
				list.Add(ai.Name);
			}
		}

		public UnitRole GetRole(ActorInfo info)
		{
			return rolesByName.TryGetValue(info.Name, out var role) ? role : UnitRole.None;
		}

		public UnitRole GetRole(Actor a) { return GetRole(a.Info); }

		public IReadOnlyCollection<string> NamesWithRole(UnitRole role)
		{
			return namesByRole.TryGetValue(role, out var list)
				? (IReadOnlyCollection<string>)list
				: System.Array.Empty<string>();
		}

		// Reads the flattened trait list + resolved weapon data. Weapon references are
		// resolved by WorldLoaded (ArmamentInfo.WeaponInfo is populated at RulesetLoaded).
		public static UnitRoleFacts ExtractFacts(ActorInfo info, in UnitRoleThresholds t)
		{
			var overrideInfo = info.TraitInfoOrDefault<AIUnitRoleInfo>();
			var heli = info.TraitInfoOrDefault<AIHelicopterRoleInfo>();
			var mobile = info.TraitInfoOrDefault<MobileInfo>();
			var cargo = info.TraitInfoOrDefault<CargoInfo>();

			var neutralCaptureType = t.NeutralCaptureType;
			var capturesNeutral = info.TraitInfos<CapturesInfo>()
				.Any(c => c.CaptureTypes.Contains(neutralCaptureType));

			var hasArmament = false;
			var anyTargetsEnemy = false;
			var hasAirWeapon = false;
			var maxRange = WDist.Zero;
			var maxMinRange = WDist.Zero;
			foreach (var arm in info.TraitInfos<ArmamentInfo>())
			{
				hasArmament = true;

				if (arm.TargetRelationships.HasFlag(PlayerRelationship.Enemy))
					anyTargetsEnemy = true;

				var weapon = arm.WeaponInfo;
				if (weapon == null)
					continue;

				// "Air" specifically, NOT "Helicopter": machine guns list Helicopter as a
				// valid target but are not air-defence assets; only Stinger/9M311/MANPAD-class
				// weapons list Air. Keying on Air under-matches safely (finding B3).
				if (weapon.ValidTargets.Contains("Air"))
					hasAirWeapon = true;

				if (weapon.Range > maxRange)
					maxRange = weapon.Range;

				if (weapon.MinRange > maxMinRange)
					maxMinRange = weapon.MinRange;
			}

			return new UnitRoleFacts(
				overrideInfo != null, overrideInfo?.Role ?? UnitRole.None,
				info.HasTraitInfo<AircraftInfo>(),
				heli != null, heli?.Role ?? default,
				capturesNeutral,
				info.HasTraitInfo<SupplyProviderInfo>(),
				hasArmament, anyTargetsEnemy, hasAirWeapon,
				maxRange, maxMinRange,
				cargo != null && cargo.MaxWeight > 0,
				mobile != null, mobile?.Speed ?? 0);
		}

		// Deterministic first-match priority cascade. Order is the finding-B3 corrected form:
		// override -> air -> capture -> logistics -> short-range AD -> indirect fire -> recon
		// -> transport lift -> main battle -> none.
		public static UnitRole Classify(in UnitRoleFacts f, in UnitRoleThresholds t)
		{
			// 1. Explicit override — absolute.
			if (f.HasOverride)
				return f.OverrideRole;

			// 2. Air. Fine-grained air behaviour stays on AIHelicopterRole; map it to the coarse role.
			if (f.IsAircraft)
			{
				if (f.HasHeliRole)
				{
					switch (f.HeliRole)
					{
						case HelicopterAIRole.Scout: return UnitRole.Recon;
						case HelicopterAIRole.Transport: return UnitRole.TransportLift;
						default: return UnitRole.AttackAir;
					}
				}

				if (f.HasArmament)
					return UnitRole.AttackAir;

				if (f.HasCargo)
					return UnitRole.TransportLift;

				return UnitRole.None;
			}

			// 3. CaptureSpecialist — neutral-tech capture TYPE (not mere Captures presence:
			// line infantry also carry Captures for occupied buildings).
			if (f.CapturesNeutral)
				return UnitRole.CaptureSpecialist;

			// 4. Logistics — supply providers, and support units whose only armament is
			// heal/repair (never targets an enemy). Armed combat units are excluded here even
			// if they also carry a repair tool, because at least one armament targets enemies.
			if (f.HasSupplyProvider || (f.HasArmament && !f.AnyArmamentTargetsEnemy))
				return UnitRole.Logistics;

			// 5. ShortRangeAD — maneuver AD only (Mobile guard: air-defence STRUCTURES are not
			// part of the maneuver taxonomy and fall through to None).
			if (f.IsMobile && f.HasAirWeapon)
				return UnitRole.ShortRangeAD;

			// 6. IndirectFire — a long minimum-range (mortars/arty) or very long reach (missile arty).
			if (f.MaxWeaponMinRange >= t.IndirectMinRange || f.MaxWeaponRange >= t.IndirectRangeFloor)
				return UnitRole.IndirectFire;

			// 7. Recon — fast, armed, light-weapon-only. Ordered after AD/indirect so an armed
			// scout that is really an AD or arty piece cannot be mistaken for a screen.
			if (f.IsMobile && f.Speed >= t.ReconSpeedFloor
				&& f.HasArmament && f.MaxWeaponRange <= t.ReconMaxWeaponRange)
				return UnitRole.Recon;

			// 8. TransportLift — anything that can carry passengers and was not already claimed
			// by a combat role above (finding B3: this MUST come after AD/indirect/recon).
			if (f.HasCargo)
				return UnitRole.TransportLift;

			// 9. MainBattle — an armed, mobile ground combatant with no more specialised role.
			if (f.HasArmament && f.IsMobile)
				return UnitRole.MainBattle;

			// 10. None — buildings, husks, dummy actors, unarmed non-mobile support.
			return UnitRole.None;
		}
	}
}
