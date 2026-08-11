#region Copyright & License Information
/*
 * WW3MOD danger-field reference dump.
 *
 * Reproduces the `[danger] reference` log line WITHOUT a game session, by calling the
 * production DangerFieldLayer.ExtractKernelFacts / DangerKernelMath.ReferenceIntensity
 * against the default ruleset. The danger unit is a ruleset-wide median resolved at world
 * load, so every threshold expressed in danger units silently re-scales when the damage
 * table, the durability weight or the contributing population moves — and before this
 * command the only way to observe that was to start a match and read the log.
 *
 * Run with:
 *   ENGINE_DIR=<repo>/engine \
 *   MOD_SEARCH_PATHS=<repo>/mods,<repo>/engine/mods \
 *   dotnet engine/bin/OpenRA.Utility.dll ww3mod --danger-reference
 *
 * Pass --verbose for the per-type contributing table (name, intensity, weight, throughput).
 *
 * Caveat, stated: this reads Rules.Actors from the DEFAULT ruleset. The live layer reads
 * w.Map.Rules.Actors, so a map that overrides armaments/health/cost shifts its own reference
 * away from this number. Every WW3MOD map today inherits the default combat rules.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common.UtilityCommands
{
	public sealed class DumpDangerReferenceCommand : IUtilityCommand
	{
		string IUtilityCommand.Name => "--danger-reference";

		bool IUtilityCommand.ValidateArguments(string[] args) => true;

		[Desc("[--verbose]", "Re-derive the danger-field reference intensity from YAML, no game run.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			// HACK: engine code assumes Game.ModData is set.
			Game.ModData = utility.ModData;

			var verbose = args.Any(a => a == "--verbose");
			var rules = utility.ModData.DefaultRules;

			// Mirrors DangerFieldLayer.WorldLoaded: skip abstract ^Templates, keep everything else.
			var info = new DangerFieldLayerInfo();
			var kernelParams = new DangerKernelParams(info.RangeBufferCells, info.MaxRadiusCells,
				info.DurabilityBase, info.HealthDivisor, info.CostDivisor);

			var (armorTypes, anyUnprovableArmor) = DangerFieldLayer.RulesetArmorTypes(rules.Actors.Values);

			var facts = new List<(string Name, DangerKernelFacts Facts)>();
			foreach (var ai in rules.Actors.Values)
			{
				if (ai.Name.StartsWith("^", StringComparison.Ordinal))
					continue;

				facts.Add((ai.Name, DangerFieldLayer.ExtractKernelFacts(ai, info.ThroughputWindow,
					armorTypes, anyUnprovableArmor)));
			}

			var ground = DangerKernelMath.ReferenceIntensity(facts.Select(f => f.Facts), DangerChannel.Ground, kernelParams);
			var air = DangerKernelMath.ReferenceIntensity(facts.Select(f => f.Facts), DangerChannel.Air, kernelParams);

			var contributing = facts
				.Select(f => (f.Name, f.Facts, Kernel: DangerKernelMath.Compute(f.Facts, DangerChannel.Ground, 100, kernelParams)))
				.Where(f => f.Kernel.Contributes)
				.OrderBy(f => f.Kernel.Intensity)
				.ToArray();

			var min = contributing.Length > 0 ? contributing[0].Kernel.Intensity : 0;
			var max = contributing.Length > 0 ? contributing[^1].Kernel.Intensity : 0;

			Console.WriteLine($"[danger] reference ground={ground} air={air} "
				+ "(100 danger units = one reference contact at point-blank) "
				+ $"ground-types={contributing.Length}/{facts.Count} "
				+ $"min={min} max={max}");

			// The AIR channel's own spread. The `[danger] reference` line above reports only the ground
			// population, which is enough to convert a ground threshold but says nothing about the air one —
			// and the two references move by DIFFERENT factors under the same tuning change, so an air
			// threshold can never be re-derived from a ground measurement.
			var airContributing = facts
				.Select(f => DangerKernelMath.Compute(f.Facts, DangerChannel.Air, 100, kernelParams))
				.Where(k => k.Contributes)
				.OrderBy(k => k.Intensity)
				.ToArray();

			var airMin = airContributing.Length > 0 ? airContributing[0].Intensity : 0;
			var airMax = airContributing.Length > 0 ? airContributing[^1].Intensity : 0;

			Console.WriteLine($"[danger] air-types={airContributing.Length}/{facts.Count} "
				+ $"min={airMin} max={airMax}");

			// The largest `throughput x durabilityWeight` any type in the ruleset produces — the first
			// multiply in DangerKernelMath.Compute, and the one that once overflowed int. Intensity at full
			// confidence IS that product divided by DurabilityBase, so the worst case is recoverable from the
			// spreads without re-deriving it. Printed because the headroom claim in Compute's comment is a
			// statement about the RULESET, and a ruleset changes.
			var worstMultiply = (long)Math.Max(max, airMax) * info.DurabilityBase;
			Console.WriteLine($"[danger] worst-first-multiply={worstMultiply} "
				+ $"({int.MaxValue / worstMultiply}x below int.MaxValue)");

			Console.WriteLine($"[danger] armor-types-in-ruleset={armorTypes.Count} "
				+ $"({string.Join(", ", armorTypes.OrderBy(t => t, StringComparer.Ordinal))}) "
				+ $"any-unprovable-armor={anyUnprovableArmor}");

			// Every weapon whose damage the harmless test drops, and every one it deliberately keeps
			// despite a Versus table that zeroes SOME classes — the second list is where a reader checks
			// that omission was not mistaken for a zero.
			foreach (var kv in rules.Weapons.OrderBy(k => k.Key, StringComparer.Ordinal))
			{
				foreach (var wh in kv.Value.Warheads.OfType<Warheads.DamageWarhead>())
				{
					if (wh.Damage <= 0 || wh.Versus.Count == 0)
						continue;

					var harmless = DangerFieldLayer.WarheadIsHarmless(wh, armorTypes, anyUnprovableArmor);
					var missing = armorTypes.Where(t => !wh.Versus.ContainsKey(t)).OrderBy(t => t, StringComparer.Ordinal).ToArray();
					var allListedZero = wh.Versus.Values.All(v => v <= 0);
					if (!harmless && !allListedZero)
						continue;

					Console.WriteLine($"[danger] versus {kv.Key} damage={wh.Damage} "
						+ $"harmless={harmless} listed-all-zero={allListedZero} "
						+ $"unlisted-armor-classes=[{string.Join(",", missing)}]");
				}
			}

			var weights = contributing
				.Select(f => info.DurabilityBase + (f.Facts.Health / info.HealthDivisor) + (f.Facts.Cost / info.CostDivisor))
				.ToArray();
			if (weights.Length > 0)
			{
				var sorted = weights.OrderBy(w => w).ToArray();
				Console.WriteLine("[danger] durability-weight over contributing ground types: "
					+ $"min={Weight(sorted[0])} median={Weight(sorted[sorted.Length / 2])} max={Weight(sorted[^1])}");
			}

			if (!verbose)
				return;

			Console.WriteLine();
			Console.WriteLine("name                 intensity   weight  throughput  range  hp     cost");
			foreach (var f in contributing)
			{
				var w = info.DurabilityBase + (f.Facts.Health / info.HealthDivisor) + (f.Facts.Cost / info.CostDivisor);
				Console.WriteLine($"{f.Name,-20} {f.Kernel.Intensity,9}  {Weight(w),7}  {f.Facts.GroundThroughput,10}  "
					+ $"{f.Facts.GroundRange,5}  {f.Facts.Health,-6} {f.Facts.Cost}");
			}
		}

		static string Weight(int weight) => $"{weight / 100}.{weight % 100:D2}x";
	}
}
