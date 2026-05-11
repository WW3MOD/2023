#region Copyright & License Information
/*
 * WW3MOD balance-data dump utility.
 *
 * Emits every combat-relevant actor + every weapon as a normalized JSON
 * blob to stdout. Used by tools/combat-sim to keep its stat tables in
 * lockstep with the live YAML — no more "sim says X dmg, game says Y dmg"
 * drift.
 *
 * Run with:
 *   ENGINE_DIR=<repo>/engine \
 *   MOD_SEARCH_PATHS=<repo>/mods,<repo>/engine/mods \
 *   dotnet engine/bin/OpenRA.Utility.dll ww3mod --dump-balance-json
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using OpenRA.GameRules;
using OpenRA.Mods.Common.Projectiles;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Warheads;

namespace OpenRA.Mods.Common.UtilityCommands
{
	public sealed class DumpBalanceJsonCommand : IUtilityCommand
	{
		string IUtilityCommand.Name => "--dump-balance-json";

		bool IUtilityCommand.ValidateArguments(string[] args) => true;

		[Desc("Emit every combat-relevant actor + every weapon as JSON. " +
			"Used by tools/combat-sim to stay in sync with live YAML.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			// HACK: engine code assumes Game.ModData is set.
			Game.ModData = utility.ModData;

			var rules = utility.ModData.DefaultRules;

			var actors = new Dictionary<string, object>();
			foreach (var kv in rules.Actors)
			{
				var entry = ExtractActor(kv.Value);
				if (entry != null)
					actors[kv.Key] = entry;
			}

			var weapons = new Dictionary<string, object>();
			foreach (var kv in rules.Weapons)
				weapons[kv.Key] = ExtractWeapon(kv.Value);

			var output = new
			{
				_meta = new
				{
					mod = utility.ModData.Manifest.Id,
					version = utility.ModData.Manifest.Metadata.Version,
					generated_at = DateTime.UtcNow.ToString("o"),
					note = "Generated from live YAML by --dump-balance-json. " +
						"Do not edit by hand; re-run the dump after rules changes.",
				},
				actors,
				weapons,
			};

			Console.WriteLine(JsonConvert.SerializeObject(output, Formatting.Indented));
		}

		static object ExtractActor(ActorInfo ai)
		{
			var valued = ai.TraitInfoOrDefault<ValuedInfo>();
			var health = ai.TraitInfoOrDefault<HealthInfo>();
			var armor = ai.TraitInfoOrDefault<ArmorInfo>();
			var mobile = ai.TraitInfoOrDefault<MobileInfo>();
			var aircraft = ai.TraitInfoOrDefault<AircraftInfo>();
			var cargo = ai.TraitInfoOrDefault<CargoInfo>();
			var armaments = ai.TraitInfos<ArmamentInfo>();
			var tooltip = ai.TraitInfoOrDefault<TooltipInfo>();
			var buildable = ai.TraitInfoOrDefault<BuildableInfo>();

			// Skip clearly non-combat / non-buildable junk: anything with
			// no Health AND no Cost AND no Armament. Civilians, decorations,
			// projectiles, husks, etc. fall through this filter.
			if (health == null && valued == null && armaments.Count == 0)
				return null;

			return new
			{
				name = tooltip?.Name,
				cost = valued?.Cost,
				hp = health?.HP,
				armor = armor == null ? null : new
				{
					type = armor.Type,
					thickness = armor.Thickness,
					distribution = armor.Distribution,
				},
				speed = mobile?.Speed ?? aircraft?.Speed,
				mover = mobile != null ? "ground" : aircraft != null ? "air" : null,
				cargo_max_weight = cargo?.MaxWeight,
				prerequisites = buildable?.Prerequisites,
				disabled = buildable?.Prerequisites?.Any(p => p == "~disabled") ?? false,
				armaments = armaments.Select(arm => new
				{
					name = arm.Name,
					weapon = arm.Weapon,
					aiming_delay = arm.AimingDelay,
					fire_delay = arm.FireDelay,
					ammo_usage = arm.AmmoUsage,
				}).ToArray(),
			};
		}

		static object ExtractWeapon(WeaponInfo w)
		{
			var damageWarheads = w.Warheads
				.OfType<DamageWarhead>()
				.Select(wh => new
				{
					type = wh.GetType().Name,
					damage = wh.Damage,
					penetration = wh.Penetration,
					damage_at_max_range = wh.DamageAtMaxRange,
					damage_percent = wh.DamagePercent,
					random_damage_addition = wh.RandomDamageAddition,
					random_damage_subtraction = wh.RandomDamageSubtraction,
					spread = wh is SpreadDamageWarhead sd ? (int?)sd.Spread.Length : null,
					falloff = wh is SpreadDamageWarhead sd2 ? sd2.Falloff : null,
					versus = wh.Versus,
					valid_targets = wh.ValidTargets.Select(t => t.ToString()).ToArray(),
					invalid_targets = wh.InvalidTargets.Select(t => t.ToString()).ToArray(),
				})
				.ToArray();

			// Inaccuracy + speed live on the projectile, not the weapon. Pull
			// what we can for the common projectile types.
			int? inaccuracy = null;
			string inaccuracy_type = null;
			int? projectile_speed = null;
			string projectile_kind = null;
			switch (w.Projectile)
			{
				case BulletInfo b:
					inaccuracy = b.Inaccuracy.Length;
					inaccuracy_type = b.InaccuracyType.ToString();
					projectile_speed = b.Speed?.Length > 0 ? b.Speed[0].Length : (int?)null;
					projectile_kind = "Bullet";
					break;
				case MissileInfo m:
					inaccuracy = m.Inaccuracy.Length;
					inaccuracy_type = m.InaccuracyType.ToString();
					projectile_speed = m.Speed.Length;
					projectile_kind = "Missile";
					break;
				case InstantHitInfo ih:
					inaccuracy = ih.Inaccuracy.Length;
					inaccuracy_type = ih.InaccuracyType.ToString();
					projectile_kind = "InstantHit";
					break;
				case AreaBeamInfo ab:
					inaccuracy = ab.Inaccuracy.Length;
					inaccuracy_type = ab.InaccuracyType.ToString();
					projectile_kind = "AreaBeam";
					break;
				case LaserZapInfo lz:
					inaccuracy = lz.Inaccuracy.Length;
					inaccuracy_type = lz.InaccuracyType.ToString();
					projectile_kind = "LaserZap";
					break;
				default:
					projectile_kind = w.Projectile?.GetType().Name;
					break;
			}

			return new
			{
				range = w.Range.Length,
				min_range = w.MinRange.Length,
				burst = w.Burst,
				burst_delays = w.BurstDelays,
				burst_wait = w.BurstWait,
				magazine = w.Magazine,
				reload_delay = w.ReloadDelay,
				top_attack = w.TopAttack,
				bottom_attack = w.BottomAttack,
				clear_sight_threshold = w.ClearSightThreshold,
				free_line_density = w.FreeLineDensity,
				miss_chance_per_density = w.MissChancePerDensity,
				valid_targets = w.ValidTargets.Select(t => t.ToString()).ToArray(),
				invalid_targets = w.InvalidTargets.Select(t => t.ToString()).ToArray(),
				projectile_kind,
				projectile_speed,
				inaccuracy,
				inaccuracy_type,
				warheads = damageWarheads,
			};
		}
	}
}
