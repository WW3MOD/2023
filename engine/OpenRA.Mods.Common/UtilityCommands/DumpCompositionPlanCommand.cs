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
 * TUNING A FLOOR RATIO: read the run-integrated `over the run` lines, never the end-of-run `standing`
 * column or the final census‰ — for a type that is continuously replaced those are close to a coin flip on
 * whether one happened to be alive at the last cycle, and they have now mis-tuned two ratios (the supply
 * truck, then the medic). `--floor-per N` sweeps every configured UnitFloorPer without editing YAML; the
 * `engaged` count is what catches a ratio whose denominator collapses out from under it.
 *
 * CASH IS OPT-IN, via --cash. WITHOUT IT THE BUDGET IS UNLIMITED exactly as before, so every figure ever
 * published from this tool reproduces byte-for-byte — the same rule the mod applies to a new trait field,
 * applied to an instrument. WITH IT the replay carries a balance: the ruleset's own DefaultCash, the
 * ruleset's own PassiveIncome converted to a per-cycle rate, minus what each cycle actually buys. The three
 * affordability filters (pre-empt, floor, deficit eligibility) and the banking gate then evaluate for real,
 * through the SHIPPED SupplyPrecedenceMath / CompositionNeedMath functions rather than a second copy.
 *
 * WHY IT WAS BUILT: SupplyPrecedenceStallCycles was believed inert on the user's profile because a 1000-cost
 * truck is trivially affordable against 20,000 starting cash. That reads the OPENING balance and stops. The
 * question that decides the matter is what the balance does over a match, and no instrument could see it —
 * the replay had no cash term at all. Now it does, so the claim is measurable instead of arguable.
 *
 * READ THE SWEEP, NOT A POINT ESTIMATE. Per-cycle income is the one genuinely free parameter here, so a
 * single run at one income is exactly the kind of number this project has twice had to re-derive. Sweep
 * --income and read where the behaviour CHANGES; the crossover is robust in a way a point value is not.
 *
 * WHAT THE REPLAY MODELS, stated plainly so the numbers are not over-read:
 *   * budget is UNLIMITED unless --cash is given, and by default NOTHING DIES. That default is deliberately the case most
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
 *   * THE AMMO GATE IS NOT MODELLED AT ALL, and it skews the truck numbers in BOTH directions — state it
 *     that way, because "the replay understates trucks" is the intuitive reading and it is half wrong.
 *     GateResupplyOnAmmoNeed / AnyFieldedUnitNeedsResupply are absent, so on the DEFICIT path `truk` is
 *     unconditionally eligible for the argmax and the replay OVERSTATES it: at --attrition 40 this reports
 *     `truk bought 5, first-buy cycle 7` on a path the live game gates behind somebody actually being dry.
 *     Meanwhile the supply PRE-EMPT is pinned at zero starving customers, so it only ever reflects
 *     SupplyTruckFloor and never fires on demand — there the replay UNDERSTATES. Net: this tool cannot
 *     answer "how many trucks will the bot field". Read the [composition] census line from a real match for
 *     that; it now carries starving / trucks-desired / ammo-need for exactly this reason.
 *   * WITH --cash, TWO UNMODELLED TERMS PULL IN OPPOSITE DIRECTIONS and neither is small enough to wave at.
 *     SPEND IS UNDERSTATED: the replay buys at most ONE unit per cycle, while the live BotTick drains a
 *     priority request, a FIFO request AND one pick per queue in Info.UnitQueues — so the real bot spends
 *     several times faster from the same treasury. INCOME IS ALSO UNDERSTATED: evacuation refunds
 *     (RotateToEdge) are absent, and a bot that rotates spent artillery and drained trucks off the map
 *     recovers real cash. The spend gap is structural and multiplies with queue count; the refund gap is
 *     conditional and partial (scaled by HP/MaxHP and net of missing ammo). Expect the modelled bot to be
 *     RICHER than the live one, but do not treat that as proven — it is an argument about magnitudes.
 * Everything else — ordinal slot order, target apportionment, ceiling eligibility, the argmax and its
 * tie-break, UnitLimits, UnitDelays, UnitFloors and their UnitFloorPer scaling, the supply-fleet pre-empt,
 * and with --cash the affordability filters and the banking gate — is the shipped code path,
 * called directly.
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

		[Desc("[--faction america|russia] [--cycles N] [--start <class>] [--attrition N] [--floor-per N]",
			"[--cash N] [--income N] [--no-bank] [--supply-floor-per N] [--verbose]",
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

			// Economy, read off the ruleset rather than typed in here, so the modelled bot is the shipped bot
			// and a lobby-option change cannot leave this command quoting a stale number.
			var (defaultCash, defaultIncome) = EconomyDefaults(rules);

			// --cash is what switches the balance on at all. Absent ⇒ unlimited budget, no banking, and the
			// output is identical to every run published before this existed.
			var modelCash = args.Contains("--cash");
			var startCash = int.TryParse(ArgValue(args, "--cash"), out var sc) ? sc : defaultCash;
			var incomePerCycle = int.TryParse(ArgValue(args, "--income"), out var ic) ? ic : defaultIncome;

			// The paired control: model the same economy with the precedence gate forced off, so a
			// before/after is one flag rather than a YAML edit and two runs that differed in more than the gate.
			var noBank = args.Any(a => a == "--no-bank");

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

			var flatFloor = new int[types.Length];
			for (var i = 0; i < types.Length; i++)
				flatFloor[i] = info.UnitFloors != null && info.UnitFloors.TryGetValue(types[i], out var f) ? f : 0;

			// UnitFloorPer turns a flat floor into one that phases in with the force it supports, so the floor
			// is no longer a constant and has to be recomputed every cycle inside the loop. Modelled for the
			// same reason the AA delay is: an instrument that ignored it would report the opening medic buy
			// that this scaling exists to remove.
			var floorPer = new int[types.Length];
			for (var i = 0; i < types.Length; i++)
				floorPer[i] = info.UnitFloorPer != null && info.UnitFloorPer.TryGetValue(types[i], out var p) ? p : 0;

			// --floor-per N sweeps the ratio without editing YAML, so a nine-value sweep cannot leave an edited
			// config behind or silently compare two runs that differed in more than the ratio. It rewrites only
			// slots that ALREADY carry a ratio: the override can retune a configured floor, never invent one on
			// a type the designer left flat, which would measure a config that does not exist.
			var floorPerOverride = int.TryParse(ArgValue(args, "--floor-per"), out var fpo) ? fpo : 0;
			if (floorPerOverride > 0)
				for (var i = 0; i < types.Length; i++)
					if (floorPer[i] > 0)
						floorPer[i] = floorPerOverride;

			// --supply-floor-per is the TRUCK's ratio, and it is a separate flag because SupplyTruckFloorPer is a
			// separate field on a separate lane: --floor-per rewrites UnitFloorPer, which drives ChooseBelowFloor
			// (the medic), while the truck's standing floor is read straight off info.SupplyTruckFloorPer by the
			// demand pre-empt. They are not interchangeable and the names invite believing they are — a sweep of
			// --floor-per moves the truck's `dry` line only indirectly, via the medic's effect on army
			// composition, which reads as a weak-but-real response and is nothing of the kind. The shipped
			// [Desc] on SupplyTruckFloorPer says to tune it on the `dry N/200` line; before this flag there was
			// no way to move it without editing YAML.
			var supplyFloorPerOverride = int.TryParse(ArgValue(args, "--supply-floor-per"), out var sfp) ? sfp : 0;
			var supplyFloorPer = supplyFloorPerOverride > 0 && info.SupplyTruckFloorPer > 0
				? supplyFloorPerOverride
				: info.SupplyTruckFloorPer;

			var isSupported = new bool[types.Length];
			for (var i = 0; i < types.Length; i++)
				isSupported[i] = info.UnitFloorSupportedTypes.Contains(types[i]);

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

			// The truck's standing floor is scaled by SupplyTruckFloorPer against the units a truck can
			// actually rearm, so — exactly like UnitFloorPer — it is no longer a constant and has to be
			// recomputed every cycle inside the loop. The denominator is read from the REAL Rearmable trait
			// rather than a type list, mirroring the module's CountResupplyCapableUnits: a unit is a customer
			// iff its RearmActors overlaps ResupplyUnitTypes. On this ruleset that resolves to infantry only.
			var isTruckCustomer = new bool[types.Length];
			for (var i = 0; i < types.Length; i++)
				isTruckCustomer[i] = rules.Actors.TryGetValue(types[i], out var customerInfo)
					&& customerInfo.TraitInfoOrDefault<RearmableInfo>()?.RearmActors.Overlaps(info.ResupplyUnitTypes) == true;

			var openingFloor = SupportFloorMath.EffectiveFloor(info.SupplyTruckFloor, supplyFloorPer, 0);
			var openingTrucks = SupplyFleetMath.DesiredTrucks(0, info.SupplyCustomersPerTruck,
				info.SupplyDemandOvercompensationPercent, openingFloor, info.SupplyTruckCeiling);

			var start = StartingCounts(rules, types, faction, startClass);

			Console.WriteLine($"[composition] faction={faction} start={startClass} cycles={cycles} "
				+ $"slots={types.Length} ceiling={info.CompositionEnforceTargetCeiling} "
				+ $"supply-demand-sizing={info.SupplyDemandSizing}");
			Console.WriteLine($"[composition] supply fleet floor cap={info.SupplyTruckFloor} "
				+ $"per={supplyFloorPer} customer(s) => floor at ZERO customers = {openingFloor}, "
				+ $"desired at zero starving customers = {openingTrucks} truck(s) "
				+ $"= {openingTrucks * cost[Array.IndexOf(types, info.ResupplyUnitTypes.FirstOrDefault() ?? "truk")]} budget");
			Console.WriteLine($"[composition] gated AA allowed at zero observed enemy air = {openingAaAllowed} "
				+ $"(baseline={info.AntiAirBaseline}, per-observed={info.AntiAirPerObservedAir})");

			if (floorPerOverride > 0)
				Console.WriteLine($"[composition] *** UnitFloorPer OVERRIDDEN to {floorPerOverride} for every "
					+ "ratio-floored slot (--floor-per) — this is NOT the shipped config ***");

			if (modelCash)
				Console.WriteLine($"[composition] economy MODELLED: start {startCash}, income {incomePerCycle}/cycle "
					+ $"(ruleset default {defaultCash} / {defaultIncome} per cycle), precedence gate "
					+ $"{(noBank ? "FORCED OFF (--no-bank)" : "stall-cycles " + info.SupplyPrecedenceStallCycles)}");
			else
				Console.WriteLine($"[composition] economy NOT modelled: budget unlimited, banking gate never "
					+ $"evaluated. Pass --cash {defaultCash} for the shipped lobby economy.");

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
				var note = flatFloor[i] > 0 && floorPer[i] > 0 ? $"FLOOR {flatFloor[i]} @ 1 per {floorPer[i]} supported"
					: flatFloor[i] > 0 ? $"FLOOR {flatFloor[i]} (flat)"
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

			// ===== Fleet AVAILABILITY over the whole run, not just at the final cycle =====
			// The end-of-run `standing` column is a SNAPSHOT, and for a type that is continuously replaced it
			// is close to a coin flip: at a 1-in-15 loss rate the truck is bought and killed ~12 times in 200
			// cycles, so whether the last cycle happens to catch one alive says almost nothing about whether
			// the army had supply. Tuning the floor ratio against that snapshot tunes against the instrument.
			// These three integrate the whole trajectory instead, and `dry` — cycles with NO truck at all — is
			// the one that answers the user's complaint ("soldiers out of ammo are useless"), because it is
			// the fraction of the match during which nobody could be resupplied.
			var truckDryCycles = 0;
			var truckCycleSum = 0;
			var truckMin = int.MaxValue;
			var truckMax = 0;

			// ===== The same run-integrated reading, for every type with a UnitFloorPer =====
			// The truck block above exists because the final-cycle snapshot could not tune SupplyTruckFloorPer.
			// A UnitFloorPer type has exactly the same problem for exactly the same reason — it is continuously
			// replaced under losses — so it gets the same instrument rather than a second one.
			//
			// ENGAGED and SHORT are the pair that locates the CLIFF, and they answer different questions.
			// A ratio against a denominator that COLLAPSES under attrition has a threshold above which it stops
			// firing at all: measured on the truck, per 12 gave 30% dry and per 14 gave 86% — the pre-fix
			// behaviour exactly — because the thinned army no longer carries enough customers to clear the
			// denominator. `engaged` is the cycles on which the effective floor was >= 1 at all, i.e. the ratio
			// cleared its denominator; 0/N is a floor that is INERT, not a floor that is satisfied. `short` is
			// the cycles on which the floor was both non-zero AND unmet, i.e. the pre-empt would actually fire.
			// Reading only `standing` or only `dry` cannot tell those two apart: a type with no floor engaged
			// and a type with a floor fully met both show zero pre-empts.
			var slotDry = new int[types.Length];
			var slotSum = new int[types.Length];
			var slotMin = new int[types.Length];
			var slotMax = new int[types.Length];
			var slotEngaged = new int[types.Length];
			var slotShort = new int[types.Length];
			for (var i = 0; i < types.Length; i++)
				slotMin[i] = int.MaxValue;

			// The denominator's own trajectory. Without this the sweep can report THAT a ratio stopped engaging
			// but not WHERE the cliff is, which is the number that decides whether the shipped value has margin.
			var supportedSum = 0;
			var supportedMin = int.MaxValue;
			var supportedMax = 0;

			// ===== The balance, and the banking spell it drives =====
			// bankBest / bankStall are the module's own spell trail (supplyBankBestCash / supplyBankStalled),
			// stepped through the shipped SupplyPrecedenceMath so the offline answer cannot drift from the live
			// one. `broke` is the poverty metric that actually matters: not "cash is low" but "cash cannot buy
			// the CHEAPEST thing this bot would consider", which is the state in which the argmax stops being a
			// choice at all.
			long cash = startCash;
			long bankBest = 0;
			var bankStall = 0;
			var bankedCycles = 0;
			var brokeCycles = 0;
			var firstBrokeCycle = -1;
			long cashSum = 0;
			var cashMin = long.MaxValue;
			long cashMax = 0;
			var stallCycles = noBank ? 0 : info.SupplyPrecedenceStallCycles;

			var cheapest = int.MaxValue;
			for (var i = 0; i < types.Length; i++)
				if (cost[i] > 0 && cost[i] < cheapest)
					cheapest = cost[i];

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

				// Income lands before the decision, so this cycle can spend what it just earned — the live
				// PassiveIncome tick is on its own interval and is not synchronised to the build cycle, so
				// either side of the pick is equally defensible and this is the one that flatters the bot.
				// NOT long.MaxValue: CompositionNeedMath.Affordable computes `budget * 100`, which overflows
				// silently at that value and wraps NEGATIVE — so an "unlimited" budget expressed the obvious
				// way makes every slot unaffordable and the replay declines all 200 cycles. A billion is
				// unreachable by any unit cost in the ruleset and leaves three orders of magnitude of headroom
				// under the multiply.
				var budget = 1_000_000_000L;
				if (modelCash)
				{
					cash += incomePerCycle;
					budget = cash;

					cashSum += cash;
					if (cash < cashMin)
						cashMin = cash;

					if (cash > cashMax)
						cashMax = cash;

					if (cheapest != int.MaxValue && !CompositionNeedMath.Affordable(cash, cheapest, 100))
					{
						brokeCycles++;
						if (firstBrokeCycle < 0)
							firstBrokeCycle = cycle;
					}
				}

				// Sampled AFTER attrition and BEFORE this cycle's buy: this is the fleet the army actually had
				// while it needed supply, which is the quantity the user's complaint is about. Sampling here
				// also keeps it unaffected by the `continue` on a declined cycle.
				var truckAlive = truckSlots.Sum(s => counts[s]);
				truckCycleSum += truckAlive;
				if (truckAlive == 0)
					truckDryCycles++;

				if (truckAlive < truckMin)
					truckMin = truckAlive;

				if (truckAlive > truckMax)
					truckMax = truckAlive;

				// The UnitFloorPer denominator: the STANDING count of supported types, matching
				// CountSupportedForce (alive units, pending excluded). Sampled here, after attrition and before
				// this cycle's buy, so the floor pre-empt below and the run-integrated reading above are
				// computed from ONE value and cannot disagree about what the denominator was.
				var supported = 0;
				for (var i = 0; i < types.Length; i++)
					if (isSupported[i])
						supported += counts[i];

				supportedSum += supported;
				if (supported < supportedMin)
					supportedMin = supported;

				if (supported > supportedMax)
					supportedMax = supported;

				for (var i = 0; i < types.Length; i++)
				{
					if (floorPer[i] <= 0 || flatFloor[i] <= 0)
						continue;

					var held = counts[i];
					slotSum[i] += held;
					if (held == 0)
						slotDry[i]++;

					if (held < slotMin[i])
						slotMin[i] = held;

					if (held > slotMax[i])
						slotMax[i] = held;

					var effNow = SupportFloorMath.EffectiveFloor(flatFloor[i], floorPer[i], supported);
					if (effNow > 0)
					{
						slotEngaged[i]++;
						if (held < effNow)
							slotShort[i]++;
					}
				}

				var values = new int[types.Length];
				for (var i = 0; i < types.Length; i++)
					values[i] = counts[i] * cost[i];

				var census = ForceCompositionMath.SharesPerMille(values);
				var targets = ForceCompositionMath.ApplyCounterBias(baseTargets, new int[4], null,
					info.CounterBiasMaxPct, info.ThreatDeadbandPerMille);

				// Supply-fleet pre-empt (ahead of the deficit pick, exactly as ChooseByDeficit orders it).
				//
				// The floor is re-derived from the STANDING customer population every cycle, matching
				// SupplyFleetUnderDesired. Starving customers stay pinned at zero here (the replay has no ammo
				// model), so what this measures is precisely the STANDING reserve the floor guarantees —
				// which is the quantity under test. Live behaviour can only be at or above it, since real
				// starvation only ever raises DesiredTrucks.
				var truckCustomers = 0;
				for (var i = 0; i < types.Length; i++)
					if (isTruckCustomer[i])
						truckCustomers += counts[i];

				var cycleFloor = SupportFloorMath.EffectiveFloor(info.SupplyTruckFloor, supplyFloorPer, truckCustomers);
				var cycleTrucks = SupplyFleetMath.DesiredTrucks(0, info.SupplyCustomersPerTruck,
					info.SupplyDemandOvercompensationPercent, cycleFloor, info.SupplyTruckCeiling);

				// ===== The banking gate, mirroring ShouldBankForSupply =====
				// Same two inputs the module derives (is the fleet short of what demand wants, and can we
				// afford the truck yet), fed to the same SupplyPrecedenceMath functions. Inert without --cash,
				// because without a balance `truckAffordable` is unconditionally true and ShouldBankCycle
				// declines — which is the very property under test, so it is left to fall out of the arithmetic
				// rather than special-cased.
				var fleetShort = false;
				var truckAffordable = false;
				if (info.SupplyDemandSizing)
					foreach (var s in truckSlots)
					{
						if (counts[s] >= limit[s] || counts[s] >= cycleTrucks)
							continue;

						fleetShort = true;
						if (CompositionNeedMath.Affordable(budget, cost[s], 100))
						{
							truckAffordable = true;
							break;
						}
					}

				if (modelCash && stallCycles > 0)
				{
					bankStall = SupplyPrecedenceMath.UpdateStall(cash, bankBest, bankStall);
					if (cash > bankBest)
						bankBest = cash;

					if (SupplyPrecedenceMath.ShouldBankCycle(fleetShort, truckAffordable, bankStall, stallCycles))
					{
						bankedCycles++;
						continue;
					}

					// EndBankingSpell: the module clears the trail on every cycle that reaches the pick, so a
					// high-water mark from a richer moment cannot make the next spell look stalled from birth.
					bankBest = 0;
					bankStall = 0;
				}

				var preempt = -1;
				if (info.SupplyDemandSizing)
					foreach (var s in truckSlots)
						if (counts[s] < limit[s] && counts[s] < cycleTrucks
							&& CompositionNeedMath.Affordable(budget, cost[s], 100))
						{
							preempt = s;
							break;
						}

				// Standing-population floor pre-empt, second (after the supply fleet), ordinal order — the
				// same order ChooseBelowFloor walks. Exempt from the ceiling and the AA threat gate by
				// construction, exactly as the module's early return is.
				//
				// `supported` is the denominator sampled above. At cycle 0 with no starting units it is 0, so
				// every scaled floor is 0 and nothing is pre-empted — which is the behaviour under test.
				if (preempt < 0)
					for (var i = 0; i < types.Length; i++)
					{
						var eff = SupportFloorMath.EffectiveFloor(flatFloor[i], floorPer[i], supported);
						if (counts[i] < eff && counts[i] < limit[i] && cycle >= delayCycle[i]
							&& CompositionNeedMath.Affordable(budget, cost[i], 100))
						{
							preempt = i;
							break;
						}
					}

				int idx;
				if (preempt >= 0)
					idx = preempt;
				else
				{
					var eligible = new bool[types.Length];
					for (var i = 0; i < types.Length; i++)
						eligible[i] = counts[i] < limit[i] && cycle >= delayCycle[i]
							&& !(aaGated[i] && counts[i] >= openingAaAllowed)
							&& CompositionNeedMath.Affordable(budget, cost[i], 100);

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
				if (modelCash)
					cash -= cost[idx];

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

			// The balance, and how much of the run the precedence gate was reachable at all. `broke` is the
			// headline: cycles on which the bot could not afford even the cheapest slot it composes, i.e. the
			// regime the whole precedence argument is about. `banked` counts the cycles the gate actually
			// silenced — 0 there with a non-zero SupplyPrecedenceStallCycles is the gate proven inert AT THIS
			// ECONOMY, which is a much narrower claim than "inert", and the difference is the point.
			if (modelCash)
			{
				Console.WriteLine($"[composition] cash over the run: mean {cashSum / (double)cycles:0}, "
					+ $"min {(cashMin == long.MaxValue ? 0 : cashMin)}, max {cashMax}, final {cash}");
				Console.WriteLine($"[composition] precedence gate: banked {bankedCycles}/{cycles} cycles "
					+ $"({100 * bankedCycles / cycles}%), broke {brokeCycles}/{cycles} "
					+ $"({100 * brokeCycles / cycles}%, first at cycle "
					+ $"{(firstBrokeCycle < 0 ? "NEVER" : firstBrokeCycle.ToString())}), "
					+ $"cheapest slot {(cheapest == int.MaxValue ? 0 : cheapest)}"
					+ (stallCycles <= 0 ? "  <-- GATE OFF" : bankedCycles == 0 ? "  <-- INERT at this economy" : ""));
			}

			// Read THIS line, not the `standing` column, when tuning the supply fleet: `dry` is the number of
			// cycles the army had no truck at all, which is the thing the user complains about, and unlike the
			// final-cycle snapshot it does not turn on whether a truck happened to die on cycle 199.
			if (truckSlots.Length > 0)
				Console.WriteLine($"[composition] supply fleet over the run: dry {truckDryCycles}/{cycles} cycles "
					+ $"({100 * truckDryCycles / cycles}%), mean {truckCycleSum / (double)cycles:0.00}, "
					+ $"min {(truckMin == int.MaxValue ? 0 : truckMin)}, max {truckMax}");

			// Same reading for the ratio-floored types, plus the denominator that decides whether their ratio
			// engages at all. Tune a UnitFloorPer on THESE lines, never on the `standing` column below.
			if (floorPer.Any(p => p > 0))
			{
				Console.WriteLine($"[composition] UnitFloorPer denominator over the run: mean "
					+ $"{supportedSum / (double)cycles:0.00} supported, min {(supportedMin == int.MaxValue ? 0 : supportedMin)}, "
					+ $"max {supportedMax}");

				for (var i = 0; i < types.Length; i++)
				{
					if (floorPer[i] <= 0 || flatFloor[i] <= 0)
						continue;

					Console.WriteLine($"[composition] {types[i]} over the run: dry {slotDry[i]}/{cycles} cycles "
						+ $"({100 * slotDry[i] / cycles}%), mean {slotSum[i] / (double)cycles:0.00}, "
						+ $"min {(slotMin[i] == int.MaxValue ? 0 : slotMin[i])}, max {slotMax[i]}");
					Console.WriteLine($"[composition] {types[i]} floor (cap {flatFloor[i]} @ 1 per {floorPer[i]}): "
						+ $"engaged {slotEngaged[i]}/{cycles} cycles ({100 * slotEngaged[i] / cycles}%), "
						+ $"short {slotShort[i]}/{cycles}"
						+ (slotEngaged[i] == 0 ? "  <-- INERT: denominator never reached " + floorPer[i] : ""));
				}
			}

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

		// The shipped economy, as the ruleset states it. PassiveIncome is quoted per PassiveIncomeInterval
		// TICKS and the replay steps in BUILD CYCLES, so it is converted once here — FeedbackTime (30) ticks
		// per cycle against a 50-tick interval turns the engine's 100 into 60 a cycle. Integer division is
		// deliberate: it rounds the modelled bot's income DOWN, i.e. toward poverty, and a poverty claim
		// should never rest on a rounding that flattered it.
		static (int Cash, int IncomePerCycle) EconomyDefaults(Ruleset rules)
		{
			if (!rules.Actors.TryGetValue("player", out var player))
				return (0, 0);

			var res = player.TraitInfoOrDefault<PlayerResourcesInfo>();
			if (res == null)
				return (0, 0);

			var perCycle = res.PassiveIncomeInterval > 0
				? res.PassiveIncome * UnitBuilderBotModule.FeedbackTime / res.PassiveIncomeInterval
				: 0;

			return (res.DefaultCash, perCycle);
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
