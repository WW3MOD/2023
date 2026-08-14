#region Copyright & License Information
/*
 * WW3MOD composition-plan dump — what the @experimental bot actually buys, offline.
 *
 * Re-derives UnitBuilderBotModule.ChooseByDeficit WITHOUT a game session, by calling the production
 * ForceCompositionMath functions against the real ruleset (resolved ValuedInfo.Cost, the real
 * UnitTargetShares / UnitLimits / gate flags read off the actual trait info). The purchase lane's only
 * instrumentation is AIUtils.BotDebug, which is default-off AND chat-only — it never reaches debug.log —
 * so before this command the composition of a bot's army could not be measured at all without watching
 * a match by eye.
 *
 * Run with:
 *   ENGINE_DIR=<repo>/engine \
 *   MOD_SEARCH_PATHS=<repo>/mods,<repo>/engine/mods \
 *   dotnet engine/bin/OpenRA.Utility.dll ww3mod --composition-plan [--faction america] [--cycles 200] [--verbose]
 *
 * WHAT THE REPLAY MODELS, stated plainly so the numbers are not over-read:
 *   * budget is UNLIMITED, and by default NOTHING DIES. That default is deliberately the case most
 *     FAVOURABLE to a small target share: with no losses the army converges toward the target vector,
 *     which is the only regime in which a 9-per-mille slot's deficit can ever be the argmax. A type
 *     starved there is starved a fortiori in a real match.
 *   * --attrition N adds deterministic PROPORTIONAL-HAZARD losses: every standing unit accrues one
 *     hazard point per cycle and dies at N points, so each unit carries the same per-cycle death rate
 *     and no type is singled out. Zero RNG. This is a model, not a measurement of the real loss
 *     process — it is here to show which conclusions survive losses at all, not to predict a match.
 *   * the believed-enemy vector is ZERO, so ApplyCounterBias is an identity-plus-renormalise pass. The
 *     counter bias can only move a target by +/-CounterBiasMaxPct, which cannot rescue a slot whose
 *     problem is that one unit of it costs more share than the target allows.
 *   * ScaleAntiAirToThreat is evaluated at zero observed enemy air (the opening state).
 * Everything else — ordinal slot order, target apportionment, ceiling eligibility, the argmax and its
 * tie-break, UnitLimits, the supply-fleet pre-empt — is the shipped code path, called directly.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common.UtilityCommands
{
	public sealed class DumpCompositionPlanCommand : IUtilityCommand
	{
		string IUtilityCommand.Name => "--composition-plan";

		bool IUtilityCommand.ValidateArguments(string[] args) => true;

		[Desc("[--faction america|russia] [--cycles N] [--start <class>] [--verbose]",
			"Re-derive the @experimental bot's composition-directed buy sequence from YAML, no game run.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			// HACK: engine code assumes Game.ModData is set.
			Game.ModData = utility.ModData;

			var verbose = args.Any(a => a == "--verbose");
			var faction = ArgValue(args, "--faction") ?? "america";
			var startClass = ArgValue(args, "--start") ?? "platoon";
			var cycles = int.TryParse(ArgValue(args, "--cycles"), out var c) ? c : 200;
			var attrition = int.TryParse(ArgValue(args, "--attrition"), out var at) ? at : 0;

			var rules = utility.ModData.DefaultRules;

			var info = FindCompositionTrait(rules, faction);
			if (info == null)
			{
				Console.WriteLine($"[composition] no CompositionDirected UnitBuilderBotModule found for faction '{faction}'");
				return;
			}

			// Mirrors InitializeComposition: ordinal sort by actor name, then apportion the targets once.
			var types = info.UnitTargetShares.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
			var baseTargets = ForceCompositionMath.SharesPerMille(types.Select(t => info.UnitTargetShares[t]).ToArray());

			var cost = new int[types.Length];
			for (var i = 0; i < types.Length; i++)
				cost[i] = rules.Actors.TryGetValue(types[i], out var ai)
					? ai.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 0
					: 0;

			var limit = new int[types.Length];
			for (var i = 0; i < types.Length; i++)
				limit[i] = info.UnitLimits != null && info.UnitLimits.TryGetValue(types[i], out var l) ? l : int.MaxValue;

			var floor = new int[types.Length];
			for (var i = 0; i < types.Length; i++)
				floor[i] = info.UnitFloors != null && info.UnitFloors.TryGetValue(types[i], out var f) ? f : 0;

			// UnitDelays are in TICKS; the buy block runs once per UnitBuilderBotModule.FeedbackTime ticks, so
			// cycle c is tick 30*(c+1). Modelled because an AA floor is deliberately delayed and an instrument
			// that ignores the delay would report the floor firing at cycle 0 and quietly overstate it.
			var delayCycle = new int[types.Length];
			for (var i = 0; i < types.Length; i++)
				delayCycle[i] = info.UnitDelays != null && info.UnitDelays.TryGetValue(types[i], out var d)
					? (d + UnitBuilderBotModule.FeedbackTime - 1) / UnitBuilderBotModule.FeedbackTime
					: 0;

			// Gate flags evaluated at the OPENING state (no observed enemy air, nobody starving yet).
			var aaGated = new bool[types.Length];
			for (var i = 0; i < types.Length; i++)
				aaGated[i] = info.ScaleAntiAirToThreat && info.AntiAirUnitTypes.Contains(types[i]);

			var openingAaAllowed = AntiAirDemand.MaxAllowed(0, info.AntiAirBaseline, info.AntiAirPerObservedAir);
			var openingTrucks = SupplyFleetMath.DesiredTrucks(0, info.SupplyCustomersPerTruck,
				info.SupplyDemandOvercompensationPercent, info.SupplyTruckFloor, info.SupplyTruckCeiling);

			var start = StartingCounts(rules, types, faction, startClass);

			Console.WriteLine($"[composition] faction={faction} start={startClass} cycles={cycles} "
				+ $"slots={types.Length} ceiling={info.CompositionEnforceTargetCeiling} "
				+ $"supply-demand-sizing={info.SupplyDemandSizing}");
			Console.WriteLine($"[composition] opening supply fleet floor={info.SupplyTruckFloor} "
				+ $"=> desired at zero starving customers = {openingTrucks} truck(s) "
				+ $"= {openingTrucks * cost[Array.IndexOf(types, info.ResupplyUnitTypes.FirstOrDefault() ?? "truk")]} budget");
			Console.WriteLine($"[composition] gated AA allowed at zero observed enemy air = {openingAaAllowed} "
				+ $"(baseline={info.AntiAirBaseline}, per-observed={info.AntiAirPerObservedAir})");
			Console.WriteLine();

			// ===== The reachability table. V_fit is the whole medic argument in one column. =====
			// V_fit = the smallest total army VALUE at which ONE unit of this type sits at or under its
			// target share. Below it, owning a single one already puts the slot strictly over target, so
			// ApplyCeilingEligibility strikes the slot and no second one is ever bought.
			Console.WriteLine("slot  type                  cost  target‰   limit   V_fit  units@20k  note");
			for (var i = 0; i < types.Length; i++)
			{
				var t = baseTargets[i];
				var vfit = t > 0 ? (long)cost[i] * ForceCompositionMath.Total / t : 0;
				var at20k = t > 0 && cost[i] > 0 ? 20000L * t / (ForceCompositionMath.Total * (long)cost[i]) : 0;
				var note = floor[i] > 0 ? $"FLOOR {floor[i]}"
					: aaGated[i] && openingAaAllowed == 0 ? "GATED OFF until enemy air observed"
					: limit[i] != int.MaxValue ? $"UnitLimit {limit[i]}"
					: "";
				Console.WriteLine($"{i,4}  {types[i],-20} {cost[i],5}  {t,7}  {(limit[i] == int.MaxValue ? "-" : limit[i].ToString()),5}  "
					+ $"{vfit,6}  {at20k,9}  {note}");
			}

			Console.WriteLine();

			// ===== Replay the shipped argmax. =====
			var counts = (int[])start.Clone();
			var bought = new int[types.Length];
			var lost = new int[types.Length];
			var hazard = new int[types.Length];
			var declines = 0;
			var firstBuy = Enumerable.Repeat(-1, types.Length).ToArray();

			// Supply pre-empt is modelled explicitly: it runs AHEAD of the deficit pick and is sized by
			// customers, not by share. At zero starving customers the floor is what it returns.
			var truckSlots = types.Select((t, i) => (t, i)).Where(x => info.ResupplyUnitTypes.Contains(x.t))
				.Select(x => x.i).ToArray();

			for (var cycle = 0; cycle < cycles; cycle++)
			{
				// Proportional-hazard attrition, applied BEFORE the buy so the cycle sees the losses it is
				// replacing. Every standing unit accrues one point per cycle; a slot sheds one unit each time
				// its accumulator passes N. Equal per-unit death rate, fully deterministic, no type favoured.
				if (attrition > 0)
				{
					for (var i = 0; i < types.Length; i++)
					{
						if (counts[i] <= 0)
							continue;

						hazard[i] += counts[i];
						while (hazard[i] >= attrition && counts[i] > 0)
						{
							hazard[i] -= attrition;
							counts[i]--;
							lost[i]++;
						}
					}
				}

				var values = new int[types.Length];
				for (var i = 0; i < types.Length; i++)
					values[i] = counts[i] * cost[i];

				var census = ForceCompositionMath.SharesPerMille(values);
				var targets = ForceCompositionMath.ApplyCounterBias(baseTargets, new int[4], null,
					info.CounterBiasMaxPct, info.ThreatDeadbandPerMille);

				// Supply-fleet pre-empt (ahead of the deficit pick, exactly as ChooseByDeficit orders it).
				var preempt = -1;
				if (info.SupplyDemandSizing)
					foreach (var s in truckSlots)
						if (counts[s] < limit[s] && counts[s] < openingTrucks)
						{
							preempt = s;
							break;
						}

				// Standing-population floor pre-empt, second (after the supply fleet), ordinal order — the
				// same order ChooseBelowFloor walks. Exempt from the ceiling and the AA threat gate by
				// construction, exactly as the module's early return is.
				if (preempt < 0)
					for (var i = 0; i < types.Length; i++)
						if (counts[i] < floor[i] && counts[i] < limit[i] && cycle >= delayCycle[i])
						{
							preempt = i;
							break;
						}

				int idx;
				if (preempt >= 0)
					idx = preempt;
				else
				{
					var eligible = new bool[types.Length];
					for (var i = 0; i < types.Length; i++)
						eligible[i] = counts[i] < limit[i] && cycle >= delayCycle[i]
							&& !(aaGated[i] && counts[i] >= openingAaAllowed);

					if (info.CompositionEnforceTargetCeiling)
						eligible = ForceCompositionMath.ApplyCeilingEligibility(targets, census, eligible);

					idx = ForceCompositionMath.SelectDeficit(targets, census, eligible);
				}

				if (idx < 0)
				{
					declines++;
					continue;
				}

				counts[idx]++;
				bought[idx]++;
				if (firstBuy[idx] < 0)
					firstBuy[idx] = cycle;

				if (verbose)
					Console.WriteLine($"[composition] cycle {cycle,4} buy {types[idx]}");
			}

			var totalValue = types.Select((t, i) => (long)counts[i] * cost[i]).Sum();
			var finalCensus = ForceCompositionMath.SharesPerMille(
				types.Select((t, i) => counts[i] * cost[i]).ToArray());

			Console.WriteLine($"[composition] after {cycles} cycles: {declines} declined, army value {totalValue}"
				+ (attrition > 0 ? $", attrition 1-in-{attrition} per unit per cycle ({lost.Sum()} lost)" : ", no losses"));
			Console.WriteLine();
			Console.WriteLine("type                  start  bought    lost  standing  census‰  target‰  first-buy");
			for (var i = 0; i < types.Length; i++)
				Console.WriteLine($"{types[i],-20} {start[i],6}  {bought[i],6}  {lost[i],6}  {counts[i],8}  "
					+ $"{finalCensus[i],7}  {baseTargets[i],7}  {(firstBuy[i] < 0 ? "NEVER" : firstBuy[i].ToString()),9}");

			Console.WriteLine();
			var never = types.Where((t, i) => bought[i] == 0).ToArray();
			Console.WriteLine($"[composition] NEVER BOUGHT in {cycles} cycles: "
				+ (never.Length > 0 ? string.Join(", ", never) : "(none)"));
		}

		static string ArgValue(string[] args, string name)
		{
			var i = Array.IndexOf(args, name);
			return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
		}

		// The CompositionDirected UnitBuilder twin for this faction. Matched on the trait's own
		// RequiresCondition text so the command tracks the YAML rather than a hard-coded trait name.
		static UnitBuilderBotModuleInfo FindCompositionTrait(Ruleset rules, string faction)
		{
			if (!rules.Actors.TryGetValue("player", out var player))
				return null;

			return player.TraitInfos<UnitBuilderBotModuleInfo>()
				.FirstOrDefault(t => t.CompositionDirected && t.UnitTargetShares != null
					&& t.UnitTargetShares.Keys.Any(k => k.EndsWith("." + faction, StringComparison.Ordinal)));
		}

		// Starting-units package for this faction, bucketed into the composition slots. This is what the
		// census reads at t=0 — and every package ships exactly one MEDI, which is load-bearing for the
		// medic question: the medic slot is NOT empty when the first buy cycle runs.
		static int[] StartingCounts(Ruleset rules, string[] types, string faction, string startClass)
		{
			var counts = new int[types.Length];
			if (!rules.Actors.TryGetValue("world", out var world))
				return counts;

			var pkg = world.TraitInfos<StartingUnitsInfo>()
				.FirstOrDefault(s => string.Equals(s.Class, startClass, StringComparison.OrdinalIgnoreCase)
					&& s.Factions != null && s.Factions.Contains(faction));

			if (pkg?.SupportActors == null)
				return counts;

			foreach (var a in pkg.SupportActors)
			{
				var idx = Array.IndexOf(types, a.ToLowerInvariant());
				if (idx >= 0)
					counts[idx]++;
			}

			return counts;
		}
	}
}
