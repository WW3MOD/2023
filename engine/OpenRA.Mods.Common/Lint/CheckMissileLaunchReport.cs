#region Copyright & License Information
/*
 * WW3MOD lint rule.
 *
 * A missile launcher fires a dummy weapon whose only job is to spawn the missile actor; the missile
 * then erects on the rail for BallisticMissile.PreLaunchTicks before the motor lights. The weapon's
 * `Report` plays at armament-fire — i.e. at the START of that erection — so on an erecting launcher a
 * launch report lands PreLaunchTicks early. The Iskander shipped that way: 80 ticks, 4.8s at the
 * mod's Timestep 60, which is long enough that the sound reads as belonging to the tilt rather than
 * to the launch. The report belongs on BallisticMissile.IgnitionSound, which fires once when the
 * motor actually lights.
 *
 * Launchers whose missiles fly straight from the tube (PreLaunchTicks == 0, e.g. HIMARS) are correct
 * with a weapon Report and are deliberately not flagged.
 */
#endregion

using System;
using System.Linq;
using OpenRA.Mods.CA.Traits;
using OpenRA.Mods.Common.Traits;
using OpenRA.Server;

namespace OpenRA.Mods.Common.Lint
{
	sealed class CheckMissileLaunchReport : ILintRulesPass, ILintServerMapPass
	{
		void ILintRulesPass.Run(Action<string> emitError, Action<string> emitWarning, ModData modData, Ruleset rules)
		{
			Run(emitError, rules);
		}

		void ILintServerMapPass.Run(Action<string> emitError, Action<string> emitWarning, ModData modData, MapPreview map, Ruleset mapRules)
		{
			Run(emitError, mapRules);
		}

		static void Run(Action<string> emitError, Ruleset rules)
		{
			foreach (var actorInfo in rules.Actors)
			{
				foreach (var spawner in actorInfo.Value.TraitInfos<MissileSpawnerMasterInfo>())
				{
					if (spawner.Actors == null)
						continue;

					// The slowest slave sets the floor: any one of them erecting is enough to mistime
					// a report shared by the whole armament.
					var preLaunchTicks = spawner.Actors
						.Select(a => rules.Actors.TryGetValue(a.ToLowerInvariant(), out var slave) ? slave : null)
						.Where(slave => slave != null)
						.SelectMany(slave => slave.TraitInfos<BallisticMissileInfo>())
						.Select(bm => bm.PreLaunchTicks)
						.DefaultIfEmpty(0)
						.Max();

					if (preLaunchTicks == 0)
						continue;

					foreach (var armament in actorInfo.Value.TraitInfos<ArmamentInfo>())
					{
						if (!spawner.ArmamentNames.Contains(armament.Name))
							continue;

						if (!rules.Weapons.TryGetValue(armament.Weapon.ToLowerInvariant(), out var weapon))
							continue;

						if (weapon.Report == null || weapon.Report.Length == 0)
							continue;

						emitError($"Actor `{actorInfo.Key}`: armament `{armament.Name}` fires weapon `{armament.Weapon}`, "
							+ $"whose `Report` plays {preLaunchTicks} ticks before the missile it spawns ignites. "
							+ "Move the launch sound to BallisticMissile.IgnitionSound on the spawned missile.");
					}
				}
			}
		}
	}
}
