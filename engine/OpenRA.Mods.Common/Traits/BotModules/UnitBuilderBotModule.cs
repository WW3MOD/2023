#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Controls AI unit production.")]
	public class UnitBuilderBotModuleInfo : ConditionalTraitInfo
	{
		// TODO: Investigate whether this might the (or at least one) reason why bots occasionally get into a state of doing nothing.
		// Reason: If this is less than SquadSize, the bot might get stuck between not producing more units due to this,
		// but also not creating squads since there aren't enough idle units.
		[Desc("Only produce units as long as there are less than this amount of units idling inside the base.")]
		public readonly int IdleBaseUnitsMaximum = 12;

		[Desc("Production queues AI uses for producing units.")]
		public readonly HashSet<string> UnitQueues = new HashSet<string> { "Vehicle", "Infantry", "Plane", "Ship", "Aircraft" };

		[Desc("What units to the AI should build.", "What relative share of the total army must be this type of unit.")]
		public readonly Dictionary<string, int> UnitsToBuild = null;

		[Desc("What units should the AI have a maximum limit to train.")]
		public readonly Dictionary<string, int> UnitLimits = null;

		[Desc("EXPERIMENTAL: minimum STANDING population per type, held regardless of composition share.",
			"The mirror of UnitLimits, and the answer to a measured hole: a per-mille-of-army-VALUE target",
			"cannot hold a small type on the map at all. One unit of a 9-per-mille type is already over its",
			"target in any army below 1000*cost/target of value, so the ceiling strikes the slot; and under",
			"losses the large slots stay permanently in deficit, so the argmax never descends far enough to",
			"replace a lost specialist. Offline replay (--composition-plan) of the shipped argmax: with no",
			"losses medics ARE bought (4 in 200 cycles), but at a 1-in-40-per-cycle loss rate medic, sniper",
			"and engineer are bought ZERO times in 200 cycles. Supply trucks were exempt from this only",
			"because SupplyTruckFloor gave them a floor nothing else had.",
			"The engineer (e6) shows the same zero and is deliberately NOT floored: nothing anywhere buys one",
			"today — EngineerRouteOpenBotModule does not implement IBotRequestUnitProduction — so a floor",
			"would be papering over a separate defect rather than fixing it. Filed in bugs/discovered.md.",
			"A type under its floor PRE-EMPTS the deficit pick, so the floor deliberately outranks the target",
			"ceiling and the ScaleAntiAirToThreat gate — a floor a threat gate can refuse is not a floor, and",
			"holding AA before enemy air is seen is the entire point of an AA floor. UnitLimits still applies",
			"on top as the absolute backstop, and the count includes pending call-ins so the floor cannot",
			"order the same unit every cycle while the first walks in from the map edge.",
			"Rides the composition pick, so it needs CompositionDirected. Empty default ⇒ byte-identical for",
			"normal/rush/turtle/@stable.")]
		public readonly Dictionary<string, int> UnitFloors = null;

		[Desc("EXPERIMENTAL: scale a UnitFloors entry to the force it supports — 'one of these per N units of",
			"UnitFloorSupportedTypes'. The floor becomes min(UnitFloors[type], supported / N) and the UnitFloors",
			"value is reinterpreted as the CAP on it.",
			"THIS FIXES A GENERAL DEFECT, NOT A UNIT. A bare UnitFloors entry is a standing minimum with no",
			"denominator, and ChooseBelowFloor pre-empts the deficit argmax, the target ceiling AND the demand",
			"gates to satisfy it. At t=0 every census is zero, so every floor is maximally unmet at exactly the",
			"moment its need is lowest, and floored support types are cheap — so they clear first and become the",
			"opening call-in. Measured on the shipped default (--composition-plan --start none): cycles 0 and 1",
			"both buy medi, on BOTH factions; with --start platoon the medic is still the very first buy. The",
			"user has reported this same shape twice, first as two supply trucks and then as two medics.",
			"WITH A ZERO DENOMINATOR THE FLOOR IS ZERO — a support unit with nothing to support has no floor and",
			"is left to the ordinary argmax. That is the whole fix; see SupportFloorMath.",
			"Only meaningful for types whose value is PROPORTIONAL to the force they serve. A type keyed to",
			"something else (an AA soldier is keyed to enemy AIR, not to own army size) does not belong here —",
			"that is what ScaleAntiAirToThreat and UnitDelays are for. A floored type with NEITHER a ratio here",
			"nor a threat gate nor a delay WILL be an opening buy; that is a property of the pre-empt, not an",
			"accident.",
			"Empty default ⇒ every floor keeps its flat behaviour, so @stable and the frozen profiles are",
			"unchanged.")]
		public readonly Dictionary<string, int> UnitFloorPer = null;

		[Desc("EXPERIMENTAL: the denominator population for UnitFloorPer — the units a support type exists to",
			"serve. Counted as owned + IN-CARGO, matching OwnedOrPending's cargo credit, because boarding",
			"REMOVES a passenger from world.Actors outright and a world-only count would collapse the",
			"denominator every time a transport picked the squad up.",
			"PENDING call-ins are deliberately EXCLUDED here, which is an asymmetry with the numerator and is",
			"the point rather than an oversight: the numerator counts pending so the floor cannot re-order the",
			"same medic every cycle while the first walks in, but a unit still crossing the map from the edge is",
			"not yet a squad that needs supporting. The floor should follow the force that EXISTS.",
			"Unset ⇒ the denominator is zero, which makes every UnitFloorPer ratio hold its floor at 0. Set the",
			"ratio and this together or neither.")]
		public readonly HashSet<string> UnitFloorSupportedTypes = new();

		[Desc("When should the AI start train specific units.")]
		public readonly Dictionary<string, int> UnitDelays = null;

		[Desc("Bot-tick interval between unconditional [composition] census lines in debug.log. 0 disables.",
			"Defaults to 0 and is opted into per-profile by the two @experimental blocks. It is ALREADY inert",
			"without CompositionDirected (no composition slots exist to report), so a non-zero default would",
			"also have been harmless — but this trait is shared with @stable, and the standing rule for a",
			"shared trait is that a new field defaults to the baseline and is turned on in YAML. Costs nothing",
			"to obey literally, and leaves no judgement call for the next reader to re-derive.")]
		public readonly int CensusLogInterval = 0;

		[Desc("If true, skip the rearm building capacity check for aircraft.",
			"Use this when aircraft are produced from a generic production building (e.g. Supply Route)",
			"and don't require a dedicated pad/airfield to be built first.")]
		public readonly bool SkipRearmBuildingCheck = false;

		[Desc("EXPERIMENTAL (early-econ behaviour 1): don't call in resupply units (supply trucks) while",
			"no fielded unit a truck can rearm has meaningful ammo need. A truck bought while every unit",
			"is full just sits as a target. Simple CURRENT-need gate — reads the SAME signal SupplyProvider",
			"uses (missing ammo weighted by SupplyValue over capacity, over units whose Rearmable.RearmActors",
			"lists a ResupplyUnitType). Designed so an anticipated-need model can replace the predicate later.",
			"Default false ⇒ frozen production, byte-identical for the normal/rush/turtle/stable profiles.")]
		public readonly bool GateResupplyOnAmmoNeed = false;

		[Desc("Resupply actor types gated by GateResupplyOnAmmoNeed (e.g. supply trucks). Inert unless that flag is set.")]
		public readonly HashSet<string> ResupplyUnitTypes = new HashSet<string>();

		[Desc("Ammo-need fraction (0-1) at/above which a fielded unit counts as needing resupply.",
			"Mirrors SupplyProvider.MinNeedThreshold so a near-full unit (e.g. 499/500) does not trigger a truck.")]
		public readonly float ResupplyNeedThreshold = 0.05f;

		[Desc("EXPERIMENTAL: size the ResupplyUnitTypes fleet from the number of STARVING customers instead of",
			"letting the composition target share decide it. The share cannot size logistics: shares are",
			"per-mille of army VALUE, so a 1000-cost truck at a 40-per-mille target admits one truck per",
			"25,000 value of army — measured at one standing truck for a whole match while infantry starved.",
			"When this is on, a shortfall against SupplyFleetMath.DesiredTrucks pre-empts the deficit pick for",
			"one cycle, and also satisfies GateResupplyOnAmmoNeed so the standing floor can be reached before",
			"anyone is dry. NOTE the pre-emption rides the composition pick, so it needs CompositionDirected",
			"as well — on a lottery profile this flag only relaxes the ammo gate and will NOT grow the fleet.",
			"Default false ⇒ byte-identical for normal/rush/turtle/@stable.")]
		public readonly bool SupplyDemandSizing = false;

		[Desc("A truck-rearmable unit counts as a starving CUSTOMER when any of its truck-rearmable ammo pools",
			"sits below this per-mille of capacity. Matches SupplyFollowerBotModule.HuntStarvingThresholdPerMille",
			"(and defers to the same SupplyHuntMath rule) so procurement and delivery agree on 'starving'.",
			"Inert unless SupplyDemandSizing is set.")]
		public readonly int SupplyStarvingThresholdPerMille = 250;

		[Desc("Starving customers one truck is assumed to service. Derive it from the truck's TotalSupply over",
			"a typical full reload, NOT from how many trucks you want — the over-provision lever is",
			"SupplyDemandOvercompensationPercent. Non-positive reads as 1.")]
		public readonly int SupplyCustomersPerTruck = 6;

		[Desc("Deliberate over-provision on the computed fleet size, in percent. 100 = the honest number,",
			"200 = double it. Trucks are consumable and drive toward the fighting, so a fleet sized to the",
			"exact requirement is short the moment one dies. This is the knob to walk DOWN once the fleet is",
			"observably working — not customersPerTruck.")]
		public readonly int SupplyDemandOvercompensationPercent = 100;

		[Desc("Standing fleet size held even when nothing is starving. A fleet bought only after men are dry",
			"arrives after the fight it was needed for.",
			"REINTERPRETED AS THE CAP when SupplyTruckFloorPer is set, exactly as UnitFloors is by",
			"UnitFloorPer — see that field for why a bare standing floor is an opening buy order.")]
		public readonly int SupplyTruckFloor = 1;

		[Desc("EXPERIMENTAL: give SupplyTruckFloor a DENOMINATOR — 'one standing truck per N units the truck",
			"can actually rearm'. The floor becomes min(SupplyTruckFloor, customers / N) via",
			"SupportFloorMath.EffectiveFloor, the same function UnitFloorPer uses for the medic; this adds no",
			"second floor mechanism, it supplies the missing denominator to the one that already exists.",
			"THE MEASURED DEFECT THIS CLOSES — the composition CEILING, not timing and not affordability.",
			"A type's V_fit (the smallest army VALUE at which one unit sits at or under its target share) is",
			"cost*1000/target; for a 1000-cost truck at 40 per-mille that is 25,000. ApplyCeilingEligibility",
			"strikes any slot strictly OVER target, so below 25,000 army value owning ONE truck already puts",
			"the slot over and the deficit route is closed — the bot can hold at most one, re-bought each time",
			"the last dies. Offline replay (--composition-plan --start none): standing trucks 3 with no losses",
			"(army 57,750), 1 at --attrition 40 (army 14,350), 0 at --attrition 15 (army 7,800). The middle",
			"figure reproduces the verbatim 2026-08-10 LIVE reading of 'one standing truck per player for the",
			"whole game'.",
			"WHY A DENOMINATOR RATHER THAN A RESTORED CONSTANT: there are exactly two ceiling-EXEMPT routes onto",
			"the field, UnitFloors and this fleet pre-empt. SupplyTruckFloor: 2 gave the t=0 trucks the user",
			"complained about first (PIPELINE 57(a)); setting it to 0 deleted the truck's only ceiling-exempt",
			"route below V_fit and produced the opposite complaint (66). A constant can only be one of those two",
			"failures. A ratio is zero at t=0 — no infantry, no floor, so 57(a) cannot return — and non-zero as",
			"soon as there is an army to feed, so 66 cannot return either.",
			"THE DENOMINATOR IS INFANTRY, and that is a fact about the ruleset rather than a modelling choice:",
			"`RearmActors: truk, logisticscenter` appears ONLY on infantry (mods/ww3mod/rules/ingame/",
			"infantry.yaml); vehicles rearm from logisticscenter and aircraft from hpad/afld, so no vehicle or",
			"aircraft is ever a truck customer. It is therefore the same population medics scale against.",
			"Counted from the LIVE Rearmable filter (CountResupplyCapableUnits), not from a hand-maintained YAML",
			"type list, so it cannot drift out of agreement with the ruleset the way UnitFloorSupportedTypes can.",
			"SPARSER THAN THE DEMAND RATE ON PURPOSE. SupplyCustomersPerTruck (6) sizes the SURGE from men who",
			"are actually dry; this sizes the STANDING RESERVE from men who might become dry. A reserve as dense",
			"as the surge rate would be a fleet that never shrinks. Set this well above 6.",
			"0 (default) ⇒ the flat floor verbatim, so every existing profile — including @stable, which sets no",
			"supply flags at all — keeps its current answer exactly.")]
		public readonly int SupplyTruckFloorPer = 0;

		[Desc("Hard upper bound on the demand-sized fleet, so supply can never eat the whole call-in budget",
			"however bad the front gets. UnitLimits still applies on top as the absolute backstop.")]
		public readonly int SupplyTruckCeiling = 4;

		[Desc("EXPERIMENTAL: size the fleet from customers at SupplyProvider's OWN service bar",
			"(ResupplyNeedThreshold) rather than from SupplyStarvingThresholdPerMille.",
			"MEASURED DEFECT this closes: `starving` read 0 at EVERY snapshot of a full match while",
			"`ammo-need` read True continuously from tick 1240 — two predicates for one fact with different",
			"bars, and the stricter one was sizing the fleet, so DesiredTrucks returned 0 all match and the",
			"pre-empt never fired. AnyFieldedUnitNeedsResupply (which sets ammo-need) mirrors",
			"SupplyProvider.MinNeedThreshold, i.e. the bar at which a customer is actually SERVED; sizing the",
			"fleet from anything stricter asks for trucks later than the supply system admits it needs them.",
			"WHY NOT JUST LOWER SupplyStarvingThresholdPerMille — corrected 2026-08-15, the earlier answer here",
			"was FALSE and would have stopped the next reader trying the simpler thing. It claimed that value",
			"is also the truck's seek threshold. It is not: SupplyStarvingThresholdPerMille is declared on THIS",
			"trait and read at exactly one site (CountStarvingCustomers), while the truck's seek threshold is",
			"SupplyFollowerBotModuleInfo.HuntStarvingThresholdPerMille — a different field on a different trait,",
			"set independently in ai.yaml. They merely share the value 250 and a helper family. Lowering the",
			"procurement one would NOT have retargeted delivery.",
			"The real reason to size from the service bar is that it is the bar at which a customer is SERVED,",
			"so it cannot drift out of agreement with the supply system the way a second, independently-tuned",
			"number can — which is exactly how the two came to disagree in the first place.",
			"Taking the max of the two counts keeps the switch one-directional — it can only raise the fleet.",
			"Default false ⇒ the starving count keeps sizing the fleet, unchanged.")]
		public readonly bool SupplySizeFromNeed = false;

		[Desc("EXPERIMENTAL: how many CONSECUTIVE build cycles the banked balance may fail to set a new high",
			"before the bot gives up saving for a supply truck and resumes ordinary buying.",
			"USER RULING 2026-08-15: 'soldiers out of ammo are useless. That should be the first priority to",
			"solve at all times.' That is a PRECEDENCE, and nothing in this path could express one: a pre-empt",
			"that merely SKIPS when it cannot afford the item is not a priority, because the cycle then falls",
			"through and buys a rifleman with the very cash the truck was waiting for.",
			"THE BOUND IS ON PROGRESS, NOT ON TIME, and that is a correction from review rather than a tuning",
			"choice. A cycle-count bound was tried and is the WRONG SHAPE three ways: it does not terminate",
			"(the counter resets on fall-through, so the steady state for a poor player is N silent cycles,",
			"one buy, N silent cycles, forever, and the truck is never bought); its fall-through purchase is",
			"PRICED BY the savings, because composition eligibility is affordability-filtered and a fat balance",
			"promotes expensive slots — measured, a spell banked to 819 against a 1000 truck and immediately",
			"bought a 450 humvee; and a cycle count cannot encode a per-map, per-player economy rate.",
			"Banking on progress terminates by construction: it continues only while cash sets new highs, and a",
			"balance that keeps setting new highs reaches any fixed price in finite time.",
			"It also ABSORBS a defect it does not fix: this bank silences only the composition lane, while the",
			"request drains (CaptureCoordinator, AdaptiveProduction) and the separate .heli UnitBuilder",
			"instances keep spending the same treasury. Rather than override other modules' guarantees, the",
			"progress test NOTICES the drain — no new high, stall climbs, spell ends — instead of holding",
			"production silent against a treasury it does not control.",
			"Tolerance rather than 1 because income arrives in lumps: a measured healthy spell ran",
			"92/92/158/224/224/290/.../554/521/521/587, with two flat cycles and a dip while climbing overall.",
			"This is NOT a restored SupplyTruckFloor: it is gated on live measured demand, so with a full-ammo",
			"army — the t=0 case the user complained about first — it is inert.",
			"0 (default) ⇒ off, the cycle falls through exactly as before.")]
		public readonly int SupplyPrecedenceStallCycles = 0;

		[Desc("EXPERIMENTAL (early-econ behaviour 2): cap gated AA call-ins (the expensive vehicle SHORAD/",
			"Tunguska) to the OBSERVED enemy air threat. Cheap AA infantry stay ungated as a baseline picket.",
			"observedAir is fog-legal — only enemy aircraft the player can currently see. Prevents fielding",
			"multiple vehicle AA at game start when no air has been seen. Default false ⇒ frozen, byte-identical",
			"for the normal/rush/turtle/stable profiles.")]
		public readonly bool ScaleAntiAirToThreat = false;

		[Desc("Vehicle-AA actor types gated by ScaleAntiAirToThreat. Inert unless that flag is set.")]
		public readonly HashSet<string> AntiAirUnitTypes = new HashSet<string>();

		[Desc("Gated AA units permitted with ZERO observed enemy air (a small standing picket; 0 = none until air is seen).")]
		public readonly int AntiAirBaseline = 0;

		[Desc("Extra gated AA units permitted per observed enemy aircraft.")]
		public readonly int AntiAirPerObservedAir = 1;

		[Desc("EXPERIMENTAL (early-econ behaviour 3): don't call in a TRANSPORT (a carrier with no weapon,",
			"e.g. the tran/halo transport helicopter) unless there is real LIFT DEMAND — infantry actually",
			"available and waiting for a ride — and no idle transport we already own can serve it. The frozen",
			"path buys transports on a flat lottery weight with no demand test at all, so they are called in",
			"during the opening and then park at the Supply Route for the whole match (River Zeta issue 4).",
			"NOTE — 'idle transport we already own' is an UNOCCUPIED-AIRFRAME test, NOT a launchability test: a",
			"chip-damaged transport the squad launcher can never pick still counts as spare capacity here and",
			"so defers a replacement buy. That is bounded, not permanent — the squad module's use-or-evac",
			"(EvacuateIdleTransports) retires the unlaunchable airframe at its idle window, owned drops, and",
			"this gate then authorises the replacement. Without that counterpart flag the deferral IS permanent.",
			"Composes with CompositionDirected rather than fighting it: helicopter pools stay deliberately",
			"absent from UnitTargetShares (deferred to their own builder twins) and this gate is applied at",
			"BOTH post-pick sites, so it behaves identically whether the pick came from the deficit picker or",
			"the legacy lottery. Default false ⇒ frozen production, byte-identical for normal/rush/turtle/stable.")]
		public readonly bool GateTransportOnDemand = false;

		[Desc("Transport actor types gated by GateTransportOnDemand (e.g. tran, halo). Inert unless that flag is set.")]
		public readonly HashSet<string> TransportUnitTypes = new HashSet<string>();

		[Desc("Passengers a transport mission needs before lift demand counts as real. Should match the consuming",
			"squad module's TransportMinInfantry — a stray rifleman is not a reason to call in an airframe.",
			"Only used when GateTransportOnDemand is set.")]
		public readonly int TransportMinPassengers = 4;

		[Desc("Radius (map cells) around the bot's own Supply Route inside which infantry count as liftable",
			"RESERVE for the demand gate. MUST match the consuming squad module's LiftReserveZoneRadiusCells:",
			"this count predicts the load HelicopterSquadBotModule will actually assemble, and a disagreement",
			"either buys an airframe the launcher can never fill or starves it of one it needs. 0 or less = no",
			"spatial restriction. Only used when GateTransportOnDemand is set.")]
		public readonly int LiftReserveZoneRadiusCells = 14;

		[Desc("Actor types of the bot's home Supply Route, used to anchor the lift reserve zone.",
			"Only used when GateTransportOnDemand is set.")]
		public readonly HashSet<string> SupplyRouteTypes = new HashSet<string> { "supplyroute" };

		[Desc("Cap on the demand count, matching the consuming squad module's TransportMaxInfantry. Counting to",
			"the airframe's Cargo.MaxWeight instead would report demand no single lift can consume (America's",
			"tran carries 36 against a 4-passenger launch threshold). Effective cap is min(this, Cargo.MaxWeight),",
			"never below TransportMinPassengers. 0 or less falls back to Cargo.MaxWeight — see the HAZARD note on",
			"HelicopterSquadBotModuleInfo.TransportMaxInfantry; keep the two in step. Only used when",
			"GateTransportOnDemand is set.")]
		public readonly int TransportMaxPassengers = 8;

		[Desc("Count only UnitRoleResolver role MainBattle infantry as lift demand — the same restriction the",
			"consuming squad module applies. Without it the capture engineer, medics and MANPADS read as demand",
			"and buy an airframe that will never carry them. Only used when GateTransportOnDemand is set.")]
		public readonly bool RestrictLiftToLineInfantry = true;

		[Desc("EXPERIMENTAL (composition-directed purchasing): replace the ground-unit LOTTERY with a",
			"census-vs-target deficit pick. The frozen path buys uniformly at random (idleUnitCount stays 0",
			"under IgnoreGroundUnits, so buildRandom is always true), which makes the STANDING composition",
			"proportional to each type's LIFETIME — long-lived rear-line mortars accumulate. With this on, the",
			"module measures what it owns (per-mille of army VALUE), compares against UnitTargetShares, and buys",
			"the type furthest below target. Default false ⇒ the legacy ternary runs unchanged, including the",
			"LocalRandom draw count and order, so normal/rush/turtle/@stable are byte-identical.")]
		public readonly bool CompositionDirected = false;

		[Desc("Target share (per-mille of army VALUE) per actor type. These are TARGETS, not the ceilings",
			"UnitsToBuild weights are. Only types listed here are eligible for the composition-directed pick —",
			"an unlisted buyable type is simply never chosen by it (that is how helicopter pools stay deferred).",
			"Inert unless CompositionDirected is set. Values need not sum to 1000; they are apportioned.")]
		public readonly Dictionary<string, int> UnitTargetShares = null;

		[Desc("Own actor type -> role token (frontline/antitank/antiair/mortar/support/...). Used only as the",
			"row/column key for CounterMatrixPct; a type with no role simply receives no counter bias.")]
		public readonly Dictionary<string, string> UnitRoles = null;

		[Desc("Counter bias, keyed \"enemyclass>ownrole\" (e.g. \"armor>antitank: 40\"). Enemy classes are the",
			"existing 3-way believed classification: air, armor, infantry. The value is a percent contribution",
			"scaled by that class's share of the believed enemy force, summed per own-role and clamped to",
			"+/-CounterBiasMaxPct before being applied to the target shares.")]
		public readonly Dictionary<string, int> CounterMatrixPct = null;

		[Desc("Clamp on the summed counter bias, in percent of the base target share. 0 disables the bias.")]
		public readonly int CounterBiasMaxPct = 200;

		[Desc("Integer EMA alpha (percent) for the believed enemy class shares. Lower = smoother/slower.")]
		public readonly int ThreatSmoothingAlphaPct = 20;

		[Desc("Believed enemy class shares (per-mille) strictly below this contribute NO counter bias —",
			"a single scouted unit must not re-plan the army.")]
		public readonly int ThreatDeadbandPerMille = 30;

		[Desc("EXPERIMENTAL (composition ceilings): stop the three lanes that buy PAST UnitTargetShares.",
			"All three effects are inert without this flag:",
			"  * a cycle where composed types are buildable but all priced out or at their UnitLimit DECLINES",
			"    instead of falling back to ChooseRandomUnitToBuild — that fallback is a uniform lottery, i.e.",
			"    exactly the lifetime-proportional drift CompositionDirected exists to remove, so taking it",
			"    whenever the bot is momentarily broke quietly re-admits the bug;",
			"  * the external-request FIFO (counter-composition buys from AdaptiveProductionBotModule) is folded",
			"    under the targets, so a side lane cannot buy past them. The PRIORITY lane is deliberately NOT",
			"    bounded — the capture-supply floor must stay able to out-compete — and types named in",
			"    CompositionCeilingExemptTypes are exempt on the FIFO lane too;",
			"  * the module's OWN deficit pick drops every class STRICTLY over target",
			"    (ForceCompositionMath.ApplyCeilingEligibility), so an over-target class is never bought at all.",
			"That third lane is NOT the no-op it looks like. Restricting the argmax and then falling BACK to the",
			"unrestricted one would be (see ForceCompositionMath.SelectDeficit); restricting it and DECLINING is",
			"not, because eligibility is affordability-filtered. In the low-cash band the only eligible member of",
			"a queue is its CHEAPEST type, so the unrestricted argmax degenerates into 'buy the cheapest thing'",
			"every cycle without limit — measured live as a bot that filled its Supply Route with 10+ humvees",
			"(450, the cheapest composed vehicle) against a 40‰ target while never banking enough for the armour",
			"core. Declining banks the cash instead.",
			"Default false ⇒ the frozen fallback, the unbounded FIFO and the unrestricted argmax all run",
			"unchanged, so normal/rush/turtle/@stable keep their RNG draw count and order.")]
		public readonly bool CompositionEnforceTargetCeiling = false;

		[Desc("Actor types whose EXTERNAL requests bypass the composition ceiling on the FIFO lane. The",
			"capture-supply floor is the intended member: its availability contract ('a capturer must be",
			"obtainable when there is something to capture') outranks the composition shape, and its own",
			"alive-or-pending floor already bounds it. Naming the type here is deliberate rather than relying",
			"on CaptureCoordinatorBotModule.TecnRequestPriority being set — that flag lives in another file and",
			"its FIFO fall-back path (CaptureCoordinatorBotModule.MaintainTecnFloor) would otherwise be",
			"silently ceiling-bounded the moment someone turns it off. Inert unless",
			"CompositionEnforceTargetCeiling is set.")]
		public readonly HashSet<string> CompositionCeilingExemptTypes = new HashSet<string>();

		[Desc("Own units currently granted this condition are EXCLUDED from the composition census.",
			"An evacuating unit is leaving the battlefield, so it is not force-in-being and must not",
			"suppress a replacement buy. Empty ⇒ no exclusion.")]
		public readonly string CensusExcludeCondition = "evacuating";

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			base.RulesetLoaded(rules, ai);

			// Case-harden actor-name config (see ActorNameCase). UnitQueues holds production-queue
			// Type tokens (Vehicle/Infantry/Aircraft), NOT actor names, so it stays ordinal.
			ActorNameCase.NormalizeKeysInPlace(UnitsToBuild);
			ActorNameCase.NormalizeKeysInPlace(UnitLimits);
			ActorNameCase.NormalizeKeysInPlace(UnitFloors);
			ActorNameCase.NormalizeKeysInPlace(UnitFloorPer);
			ActorNameCase.NormalizeKeysInPlace(UnitDelays);
			ActorNameCase.NormalizeInPlace(UnitFloorSupportedTypes);
			ActorNameCase.NormalizeInPlace(ResupplyUnitTypes);
			ActorNameCase.NormalizeInPlace(AntiAirUnitTypes);
			ActorNameCase.NormalizeInPlace(TransportUnitTypes);
			ActorNameCase.NormalizeInPlace(CompositionCeilingExemptTypes);

			// UnitTargetShares is keyed by actor name and compared against Info.Name (always lowercase, see
			// conventions.md) — the same case-mismatch trap the sets above are hardened against. UnitRoles
			// (string values, no ActorNameCase overload) and the CounterMatrixPct "enemyclass>ownrole" tokens
			// are lowercased where the module flattens them in Created.
			ActorNameCase.NormalizeKeysInPlace(UnitTargetShares);
		}

		public override object Create(ActorInitializer init) { return new UnitBuilderBotModule(init.Self, this); }
	}

	public class UnitBuilderBotModule : ConditionalTrait<UnitBuilderBotModuleInfo>, IBotTick, IBotNotifyIdleBaseUnits, IBotRequestUnitProduction, IBotRequestPriorityUnitProduction, IGameSaveTraitData
	{
		public const int FeedbackTime = 30; // ticks; = a bit over 1s. must be >= netlag.

		readonly World world;
		readonly Player player;

		readonly List<string> queuedBuildRequests = new List<string>();

		// WW3MOD @experimental: high-priority requests, drained BEFORE the normal FIFO and the blind lottery so
		// a capture-supply floor out-competes combat buys for the queue slot. Stays EMPTY unless a caller opts in
		// via IBotRequestPriorityUnitProduction — no @experimental capture floor requesting here ⇒ the list is
		// never populated ⇒ BotTick is byte-identical for normal/rush/turtle/@stable. Transient (not persisted):
		// the requester re-issues each scan, so a save/load simply re-derives it.
		readonly List<string> priorityBuildRequests = new List<string>();

		// Ordinal-sorted copy of ResupplyUnitTypes: the fleet pre-emption ITERATES it (the ammo gate only ever
		// probes it with Contains), and a HashSet's enumeration order is not part of the config text.
		string[] supplyFleetTypes = Array.Empty<string>();
		string[] floorTypes = Array.Empty<string>();
		int supplyFleetShortfallTick = -1;
		int supplyFleetStarving;
		int supplyFleetNeedy;
		int supplyFleetOwned;
		int supplyFleetDesired;

		// The standing floor actually in force this tick, after SupplyTruckFloorPer scaling. Held as state
		// rather than recomputed for logging: the diagnosis this subsystem needed for six merges was "which
		// number was the floor at the moment it decided", and a log line that recomputes can disagree with
		// the decision it claims to explain.
		int supplyFleetFloor;

		// Consecutive cycles spent banking toward a truck, and the tick the last one was counted on. The tick
		// guard is load-bearing: ChooseByDeficit runs once PER QUEUE, so an ungated counter would burn two or
		// three of the allowance on a single bot tick and the bound would mean whatever the queue count
		// happened to be. One tick, one banked cycle.
		// Banking-spell state: the best balance seen so far in the current spell, and how many consecutive
		// cycles have failed to beat it. Banking continues only while the balance keeps setting new highs,
		// which is what makes the spell terminate rather than run to a fixed tick count.
		long supplyBankBestCash;
		int supplyBankStalled;
		int supplyBankedCycles;
		int supplyBankedTick = -1;

		// The bank decision is taken once per world tick and reused by every queue in that tick, so the
		// Vehicle and Infantry passes can never reach opposite conclusions about one shared treasury.
		int supplyBankDecisionTick = -1;
		bool supplyBankDecision;

		// Per-instance cache of the treasury-wide hold (see AnySiblingWantsSupplyBank).
		int bankHoldTick = -1;
		bool bankHold;

		IBotRequestPauseUnitProduction[] requestPause;
		int idleUnitCount;

		// Transport demand-gate lookups, resolved once (both are single-instance: PoiGoalGuard is a shared
		// singleton per player, UnitRoleResolver a world trait). Only read on the GateTransportOnDemand path.
		PoiGoalGuard goalGuard;
		UnitRoleResolver roleResolver;

		int ticks;

		// ===== Composition-directed purchasing (@experimental; all null/empty when CompositionDirected is off) =====
		// The Dictionaries are flattened ONCE in Created into ordinal-ordered arrays: the runtime math must never
		// depend on Dictionary enumeration order (it is unspecified and can differ between runs of the same seed).
		// compositionTypes[i] is the actor name of ordinal slot i; every other array is parallel to it.
		string[] compositionTypes;
		int[] compositionTargets;      // designer target share per slot, per-mille (pre-bias).
		int[] compositionRoleIndex;    // slot -> index into compositionRoles, or -1 for "no role".
		string[] compositionRoles;     // ordinal-sorted distinct role tokens.
		int[,] counterMatrix;          // [enemy class, role index] percent, from CounterMatrixPct.
		int[] smoothedThreatShares;    // persisted EMA state, parallel to ThreatClasses.
		BeliefStore beliefStore;
		bool compositionInitialized;
		bool beliefStoreResolved;
		int lastThreatTick = -1;

		// The believed-enemy classification is the existing 3-way split (see AdaptiveProductionBotModule
		// .ClassifyContact); this array fixes both the ordinal slot order and the YAML key spelling.
		static readonly string[] ThreatClasses = { "air", "armor", "infantry" };

		public UnitBuilderBotModule(Actor self, UnitBuilderBotModuleInfo info)
			: base(info)
		{
			world = self.World;
			player = self.Owner;
		}

		protected override void Created(Actor self)
		{
			requestPause = self.Owner.PlayerActor.TraitsImplementing<IBotRequestPauseUnitProduction>().ToArray();
			goalGuard = self.Owner.PlayerActor.TraitOrDefault<PoiGoalGuard>();
			roleResolver = world.WorldActor.TraitOrDefault<UnitRoleResolver>();

			InitializeComposition();

			if (Info.SupplyDemandSizing)
				supplyFleetTypes = Info.ResupplyUnitTypes.OrderBy(t => t, StringComparer.Ordinal).ToArray();

			// Ordinal, like every other flattened config array here: a Dictionary's enumeration order must
			// never reach a decision (influence-stack determinism invariant).
			if (Info.UnitFloors != null && Info.UnitFloors.Count > 0)
				floorTypes = Info.UnitFloors.Keys.OrderBy(t => t, StringComparer.Ordinal).ToArray();
		}

		// Flatten the composition config into ordinal-ordered arrays exactly once. Skipped entirely when the
		// flag is off ⇒ no allocation, no belief-store lookup, nothing to diverge on the frozen path.
		void InitializeComposition()
		{
			if (compositionInitialized)
				return;

			compositionInitialized = true;

			if (!Info.CompositionDirected || Info.UnitTargetShares == null || Info.UnitTargetShares.Count == 0)
				return;

			// An all-zero (or all-negative) share table is a CONFIG ERROR, not a policy. SharesPerMille would
			// apportion it to all zeros, every deficit would tie at 0, and the ordinal tie-break would then buy
			// the lowest-ordinal eligible type forever. Stay inactive (legacy picker) and say so once, instead
			// of silently shipping a degenerate purchase plan.
			if (!Info.UnitTargetShares.Values.Any(v => v > 0))
			{
				AIUtils.BotDebug("{0} CompositionDirected is set but no UnitTargetShares entry is positive — falling back to the legacy picker.", player);
				return;
			}

			// Ordinal sort by actor name: the slot order is then a pure function of the config text.
			compositionTypes = Info.UnitTargetShares.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
			compositionTargets = new int[compositionTypes.Length];
			compositionRoleIndex = new int[compositionTypes.Length];

			// Role token per type (lowercased here — UnitRoles has string values, so ActorNameCase's
			// Dictionary<string,int> key-normalizer does not apply to it).
			var rolesByType = new Dictionary<string, string>();
			if (Info.UnitRoles != null)
				foreach (var kv in Info.UnitRoles)
					rolesByType[kv.Key.ToLowerInvariant()] = kv.Value.ToLowerInvariant();

			compositionRoles = rolesByType.Values.Distinct().OrderBy(r => r, StringComparer.Ordinal).ToArray();

			for (var i = 0; i < compositionTypes.Length; i++)
			{
				compositionTargets[i] = Info.UnitTargetShares[compositionTypes[i]];
				compositionRoleIndex[i] = rolesByType.TryGetValue(compositionTypes[i], out var role)
					? Array.IndexOf(compositionRoles, role)
					: -1;
			}

			// Targets are apportioned once so the runtime compares like with like (census is per-mille too).
			compositionTargets = ForceCompositionMath.SharesPerMille(compositionTargets);

			// The YAML matrix is keyed by ROLE, but the target vector the bias is applied to is indexed by
			// TYPE — so parse into a role matrix and then EXPAND it into type columns here, once. Doing this
			// at flatten time is what keeps the per-buy math a plain array read (and stops a role/type index
			// mix-up from silently disabling the bias: ApplyCounterBias requires column count == targets).
			var roleMatrix = new int[ThreatClasses.Length, compositionRoles.Length];
			if (Info.CounterMatrixPct != null)
			{
				foreach (var kv in Info.CounterMatrixPct)
				{
					var key = kv.Key.ToLowerInvariant();
					var sep = key.IndexOf('>');
					if (sep <= 0 || sep >= key.Length - 1)
						continue;

					var enemyClass = Array.IndexOf(ThreatClasses, key.Substring(0, sep).Trim());
					var ownRole = Array.IndexOf(compositionRoles, key.Substring(sep + 1).Trim());
					if (enemyClass < 0 || ownRole < 0)
						continue;

					roleMatrix[enemyClass, ownRole] = kv.Value;
				}
			}

			counterMatrix = new int[ThreatClasses.Length, compositionTypes.Length];
			for (var i = 0; i < compositionTypes.Length; i++)
			{
				var role = compositionRoleIndex[i];
				if (role < 0)
					continue;

				for (var c = 0; c < ThreatClasses.Length; c++)
					counterMatrix[c, i] = roleMatrix[c, role];
			}

			smoothedThreatShares = new int[ThreatClasses.Length];
		}

		void IBotNotifyIdleBaseUnits.UpdatedIdleBaseUnits(List<Actor> idleUnits)
		{
			idleUnitCount = idleUnits.Count;
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (requestPause.Any(rp => rp.PauseUnitProduction))
				return;

			ticks++;

			if (ticks % FeedbackTime == 0)
			{
				// @experimental priority requests first — before the normal FIFO and the blind lottery below —
				// so a capture-supply floor claims the queue slot ahead of combat buys. Empty for every profile
				// that never opts in ⇒ this block is skipped ⇒ byte-identical.
				// PEEK-DON'T-POP: only remove the head once BuildUnit has ACTUALLY issued the order. When the
				// Infantry queue is busy this cycle BuildUnit returns false and the request STAYS at the head, so
				// it is retried next cycle and claims the NEXT free slot — instead of being silently discarded
				// while combat buys churn the queue (the measured supply-side deadlock). The drain still runs
				// before the FIFO/lottery below, so the surviving priority item pre-empts the next free slot; an
				// in-progress build is never cancelled (BuildUnit only takes an empty queue).
				if (priorityBuildRequests.Count > 0 && BuildUnit(bot, priorityBuildRequests[0]))
					priorityBuildRequests.RemoveAt(0);

				// The FIFO carries counter-composition buys (AdaptiveProductionBotModule). Those ride the
				// single-name BuildUnit overload, which applies NO UnitsToBuild / UnitDelays / UnitLimits and
				// no composition test — so without the ceiling below a counter class is bought without bound.
				// The request is consumed either way (as before): the requester re-issues each of its own
				// cycles, so dropping a refused one keeps the list from growing behind a permanent ceiling.
				var buildRequest = queuedBuildRequests.FirstOrDefault();
				if (buildRequest != null)
				{
					if (!RequestIsOverCompositionCeiling(buildRequest))
						BuildUnit(bot, buildRequest);

					queuedBuildRequests.Remove(buildRequest);
				}

				foreach (var q in Info.UnitQueues)
					BuildUnit(bot, q, idleUnitCount < Info.IdleBaseUnitsMaximum);
			}

			LogCensusSnapshot();
		}

		// UNCONDITIONAL, like the [danger] and [supply] lines and for the same reason: this lane's only other
		// instrumentation is AIUtils.BotDebug, which is default-OFF *and* routes to game chat rather than a log
		// file — so "the bot never bought a medic" and "nobody was recording" were the same silence, and the
		// standing composition could not be measured after a match at all.
		//
		// Reports CONCURRENTLY-ALIVE counts, never distinct identities: a standing cap and a high replacement
		// rate produce the same id count and only concurrency separates them (DISCOVERIES.md 2026-08-12). The
		// world/cargo split is the load-bearing part — a transported or garrisoned soldier is ABSENT from
		// world.Actors (RideTransport removes it) while still alive on the map, so a world-only count reads a
		// full transport as a dead platoon and cannot distinguish "never bought" from "bought and swallowed".
		void LogCensusSnapshot()
		{
			if (compositionTypes == null || Info.CensusLogInterval <= 0 || ticks % Info.CensusLogInterval != 0)
				return;

			var inWorld = new int[compositionTypes.Length];
			foreach (var a in world.Actors)
			{
				if (a.Owner != player || a.IsDead || !a.IsInWorld)
					continue;

				var slot = Array.IndexOf(compositionTypes, a.Info.Name);
				if (slot >= 0)
					inWorld[slot]++;
			}

			var inCargo = new int[compositionTypes.Length];
			foreach (var pair in world.ActorsWithTrait<Cargo>())
			{
				var transport = pair.Actor;
				if (transport.IsDead || !transport.IsInWorld)
					continue;

				if (transport.Owner != player && player.RelationshipWith(transport.Owner) != PlayerRelationship.Ally)
					continue;

				foreach (var p in pair.Trait.Passengers)
				{
					if (p.Owner != player)
						continue;

					var slot = Array.IndexOf(compositionTypes, p.Info.Name);
					if (slot >= 0)
						inCargo[slot]++;
				}
			}

			var census = ForceCompositionMath.SharesPerMille(CensusValues());
			var parts = new List<string>();
			for (var i = 0; i < compositionTypes.Length; i++)
				if (inWorld[i] > 0 || inCargo[i] > 0 || census[i] > 0)
					parts.Add($"{compositionTypes[i]}={inWorld[i]}+{inCargo[i]}/{census[i]}v{compositionTargets[i]}");

			// The two truck terms are on this line ON PURPOSE, and they are the difference between a claim and
			// a guess. With SupplyTruckFloor at 0 these predicates are the ONLY thing that orders a truck, so
			// "no trucks all match" has two completely different readings — "correct, nobody ever went dry"
			// and "the gate never opened" — and without them printed the two are indistinguishable after the
			// fact. That is precisely the mistake this branch banked in DISCOVERIES.md (confirm X was
			// OBSERVABLE before concluding the bot never did X), one layer up; deleting the floor while
			// leaving its replacement unobservable would repeat it. Both values already exist here.
			// Read through SupplyFleetUnderDesired so the tick cache is populated by the SAME path the decision
			// uses — the fields are only refreshed when that runs, so reading them raw would print whatever
			// tick last happened to ask, which is a stale number that looks live. -1 means "not applicable"
			// (demand sizing off), never "zero starving".
			// `needy` joins them for the same reason, and it is the number that settles the 2026-08-15
			// diagnosis: `starving` and `ammo-need` disagreed all match because they measure at different
			// bars, and printing only the stricter one made "desired=0 while men are dry" look like a
			// contradiction rather than the arithmetic it is. With both counts on the line the sizing input
			// is visible, so a future desired=0 can be read off as "genuinely no demand" or "sized from the
			// wrong bar" without another instrumented run.
			var starving = -1;
			var needy = -1;
			var desired = -1;
			if (Info.SupplyDemandSizing && supplyFleetTypes.Length > 0)
			{
				SupplyFleetUnderDesired(supplyFleetTypes[0]);
				starving = supplyFleetStarving;
				needy = Info.SupplySizeFromNeed ? supplyFleetNeedy : -1;
				desired = supplyFleetDesired;
			}

			// `earned`, not `cash`, is what distinguishes a bot spending a live income from one
			// living off a frozen opening allocation — both sit at cash~0. Logging cash alone hid a
			// dead-economy harness for a year (WORKSPACE/DISCOVERIES.md, 2026-08-14).
			var econRes = player.PlayerActor.TraitOrDefault<PlayerResources>();
			var econ = econRes == null ? "econ=none" :
				$"playable={player.Playable} passive={econRes.PassiveIncomeAmount} "
				+ $"bldincome={(int)econRes.TotalBuildingIncome} upkeep={(int)econRes.Upkeep} "
				+ $"net={econRes.NetChange} earned={econRes.Earned} spent={econRes.Spent}";

			Log.Write("debug", $"[composition] census tick={world.WorldTick} player={player.InternalName} "
				+ $"cash={AvailableBudget()} starving={starving} needy={needy} trucks-desired={desired} "
				+ $"bank-spell={supplyBankedCycles} bank-best={supplyBankBestCash} bank-stalled={supplyBankStalled} "
				+ $"ammo-need={AnyFieldedUnitNeedsResupply()} {econ} "
				+ $"(type=inWorld+inCargo/census‰vtarget‰) {string.Join(" ", parts)}");
		}

		void IBotRequestUnitProduction.RequestUnitProduction(IBot bot, string requestedActor)
		{
			queuedBuildRequests.Add(requestedActor);

			// The lane the offline replay CANNOT see: another module ordering through this trait bypasses the
			// composition pick entirely. Tagged so a log can distinguish "the argmax chose this" from "something
			// else asked for it" — the distinction that decides whether a composition-side fix can work at all.
			LogPick("request", requestedActor, $"queued={queuedBuildRequests.Count}");
		}

		bool IBotRequestPriorityUnitProduction.RequestPriorityUnitProduction(IBot bot, string requestedActor)
		{
			// Reject when this twin is condition-disabled: its BotTick never runs (ModularBot ticks only enabled
			// modules), so a request accepted here would never be drained/built — it would only inflate the
			// caller's pending count forever (the measured pending=82 / alive=0 deadlock). The caller routes to
			// the first twin that returns true, guaranteeing the request lands on the enabled UnitBuilder.
			if (IsTraitDisabled)
				return false;

			priorityBuildRequests.Add(requestedActor);
			return true;
		}

		int IBotRequestUnitProduction.RequestedProductionCount(IBot bot, string requestedActor)
		{
			// Count BOTH lists so a requester's alive-or-pending gate sees priority requests in flight too.
			// priorityBuildRequests is empty on every non-opted-in profile ⇒ same result as the frozen path.
			return priorityBuildRequests.Count(r => r == requestedActor)
				+ queuedBuildRequests.Count(r => r == requestedActor);
		}

		void BuildUnit(IBot bot, string category, bool buildRandom)
		{
			// TREASURY-SCOPE BANK HOLD. Checked here rather than inside ChooseByDeficit because the sibling
			// instances this has to silence do NOT set CompositionDirected — the .heli and .fixedwing twins
			// ride the legacy lottery path and never enter ChooseByDeficit at all, so a hold placed there
			// would miss exactly the instances that were draining the savings.
			//
			// Ordering is preserved for the owning instance: ShouldBankForSupply returns false whenever the
			// truck is affordable, so an affordable truck falls through to ChooseByDeficit and is bought by
			// the shortfall pre-empt as before. This only suppresses cycles that would otherwise SPEND the
			// price on something else.
			if (AnySiblingWantsSupplyBank())
			{
				// One line per TICK, not per queue per instance: the hold is a treasury-wide fact, and
				// logging it three times a cycle (ground x2 queues + heli) would triple the volume while
				// saying the same thing. The owning instance carries the counters, so it does the logging.
				if (Info.SupplyDemandSizing && supplyBankedTick != world.WorldTick)
				{
					supplyBankedTick = world.WorldTick;
					supplyBankedCycles++;

					LogPick("supply-bank", "(none)", $"cash={AvailableBudget()} "
						+ $"needy={supplyFleetNeedy} desired={supplyFleetDesired} owned={supplyFleetOwned} "
						+ $"best={supplyBankBestCash} stalled={supplyBankStalled}/{Info.SupplyPrecedenceStallCycles} "
						+ $"spell={supplyBankedCycles}");
				}

				return;
			}

			// Pick a free queue
			var queue = AIUtils.FindQueuesByCategory(player)[category].FirstOrDefault(q => !q.AllQueued().Any());
			if (queue == null)
				return;

			// @experimental composition-directed pick. When the flag is off the legacy ternary below is reached
			// unchanged — same branches, same LocalRandom draws, same order — so the frozen profiles are
			// byte-identical. When it is on, ChooseByDeficit draws ZERO random and falls back to the SAME
			// ternary for a queue it has no opinion about (e.g. a heli-only queue with no target shares).
			var unit = Info.CompositionDirected ?
				ChooseByDeficit(queue, buildRandom) :
				(buildRandom ? ChooseRandomUnitToBuild(queue) : ChooseUnitToBuild(queue));

			if (unit == null)
				return;

			var name = unit.Name;

			if (Info.UnitsToBuild != null && !Info.UnitsToBuild.ContainsKey(name))
				return;

			if (Info.UnitDelays != null &&
				Info.UnitDelays.ContainsKey(name) &&
				Info.UnitDelays[name] > world.WorldTick)
				return;

			if (Info.UnitLimits != null &&
				Info.UnitLimits.ContainsKey(name) &&
				world.Actors.Count(a => a.Owner == player && a.Info.Name == name) >= Info.UnitLimits[name])
				return;

			// EXPERIMENTAL early-econ gates (default-off; only the @experimental UnitBuilder twin enables them,
			// so normal/rush/turtle/stable reach QueueOrder byte-identically). Both draw ZERO random.
			// SupplyFleetUnderDesired satisfies this gate on its own: the standing floor exists precisely to be
			// held while nobody is dry, and this gate is the one thing that would forbid reaching it. False when
			// SupplyDemandSizing is off ⇒ the frozen evaluation order is unchanged.
			// A type under its standing floor is exempt from the demand gates below — otherwise the AA gate
			// would refuse the very AA floor whose purpose is to hold AA BEFORE enemy air is observed, and the
			// floor could never be reached. False whenever UnitFloors is unset (the default), so the frozen
			// evaluation order is unchanged for normal/rush/turtle/@stable.
			// EffectiveFloorFor returns 0 for an unfloored type, so this short-circuits before OwnedOrPending on
			// every profile that sets no UnitFloors — the frozen path does no extra work and reaches QueueOrder
			// byte-identically.
			var floor = EffectiveFloorFor(name);
			var belowFloor = floor > 0 && OwnedOrPending(name) < floor;

			if (!belowFloor && Info.GateResupplyOnAmmoNeed && Info.ResupplyUnitTypes.Contains(name)
				&& !SupplyFleetUnderDesired(name) && !AnyFieldedUnitNeedsResupply())
				return;

			if (!belowFloor && Info.ScaleAntiAirToThreat && Info.AntiAirUnitTypes.Contains(name) && !ShouldBuildMoreAntiAir())
				return;

			if (!belowFloor && Info.GateTransportOnDemand && Info.TransportUnitTypes.Contains(name) && !ShouldBuyTransport(unit))
				return;

			bot.QueueOrder(Order.StartProduction(queue.Actor, name, 1));
		}

		// Every unit this player owns that a truck could serve — INCLUDING units currently loaded in a transport
		// or sheltering in a garrison, which are ABSENT from world.Actors entirely (boarding calls
		// `w.Remove(self)`, it does not merely clear IsInWorld).
		//
		// This is not defensive tidying; the two truck predicates below are wrong without it, and the bug is
		// worst exactly when it matters most. SupplyProvider SERVES loaded/sheltered men — it walks
		// GarrisonManager.ShelterPassengers specifically because they are out of the world
		// (SupplyProvider.cs:547-570) — while a world-only purchase gate cannot see them. Every line and
		// support infantry type is a PassengerType on MountedTransportBotModule (ai.yaml:1483), so the failure
		// is ordinary play: infantry fight, run dry, board a Bradley or duck into a shelter, and demand reads
		// ZERO while they sit there empty. `SupplyTruckFloor: 2` used to paper over this by buying trucks
		// regardless; with the floor at 0 these predicates are the ONLY thing that orders a truck, so the blind
		// spot becomes load-bearing.
		//
		// Cargo is the single authoritative container for both cases — a garrison's shelter soldiers live in
		// the building's own Cargo and ShelterPassengers mirrors it — so one walk covers transports and
		// garrisons, and a soldier deployed to a firing port is re-added to the world and counted by the first
		// loop instead. The two sources are therefore disjoint: no double-count.
		IEnumerable<Actor> OwnedUnitsIncludingCarried()
		{
			foreach (var a in world.Actors)
				if (a.Owner == player && !a.IsDead && a.IsInWorld)
					yield return a;

			foreach (var pair in world.ActorsWithTrait<Cargo>())
			{
				var transport = pair.Actor;
				if (transport.IsDead || !transport.IsInWorld)
					continue;

				if (transport.Owner != player && player.RelationshipWith(transport.Owner) != PlayerRelationship.Ally)
					continue;

				foreach (var p in pair.Trait.Passengers)
					if (p.Owner == player && !p.IsDead)
						yield return p;
			}
		}

		// Behaviour 1: is there meaningful ammo need among fielded units a gated truck can rearm? Mirrors
		// SupplyProvider's own metric (ResupplyDemand.UnitNeed) over each such unit's truck-rearmable pools,
		// short-circuiting on the first needy unit. Pure decision in ResupplyDemand; this only reads trait state.
		bool AnyFieldedUnitNeedsResupply()
		{
			foreach (var a in OwnedUnitsIncludingCarried())
			{
				var rearmable = a.TraitOrDefault<Rearmable>();
				if (rearmable == null || !rearmable.Info.RearmActors.Overlaps(Info.ResupplyUnitTypes))
					continue;

				var pools = rearmable.RearmableAmmoPools;
				if (pools == null || pools.Length == 0)
					continue;

				var need = ResupplyDemand.UnitNeed(pools.Select(p => (p.Info.Ammo, p.CurrentAmmoCount, p.Info.SupplyValue)));
				if (ResupplyDemand.MeetsThreshold(need, Info.ResupplyNeedThreshold))
					return true;
			}

			return false;
		}

		// ===== Demand-sized supply fleet (@experimental) =====
		// Is the truck fleet short of what the measured starving-customer count wants? Cached per world tick:
		// the eligibility loop asks once per composition type, and each miss walks every actor the player owns.
		// world.WorldTick is deterministic and both counts are order-independent sums, so the cache cannot
		// introduce divergence. Returns false immediately when the flag is off ⇒ the callers below keep their
		// frozen short-circuit order.
		bool SupplyFleetUnderDesired(string name)
		{
			if (!Info.SupplyDemandSizing || !Info.ResupplyUnitTypes.Contains(name))
				return false;

			if (supplyFleetShortfallTick != world.WorldTick)
			{
				supplyFleetShortfallTick = world.WorldTick;
				supplyFleetStarving = CountStarvingCustomers();

				// Only walk the actor list a second time when the need bar is actually in use.
				supplyFleetNeedy = Info.SupplySizeFromNeed ? CountResupplyCustomers() : 0;

				supplyFleetOwned = SupplyTrucksOwnedOrPending();

				// The standing floor, scaled to the force it resupplies. Only walk the actor list for the
				// denominator when a ratio is actually configured: with SupplyTruckFloorPer at its 0 default
				// EffectiveFloor returns the flat floor verbatim and the count is not needed at all, so an
				// unconfigured profile pays nothing for this.
				supplyFleetFloor = Info.SupplyTruckFloorPer > 0
					? SupportFloorMath.EffectiveFloor(Info.SupplyTruckFloor, Info.SupplyTruckFloorPer, CountResupplyCapableUnits())
					: Info.SupplyTruckFloor;

				supplyFleetDesired = SupplyFleetMath.DesiredTrucks(
					SupplyPrecedenceMath.SizingCustomers(Info.SupplySizeFromNeed, supplyFleetStarving, supplyFleetNeedy),
					Info.SupplyCustomersPerTruck,
					Info.SupplyDemandOvercompensationPercent, supplyFleetFloor, Info.SupplyTruckCeiling);
			}

			return supplyFleetOwned < supplyFleetDesired;
		}

		// Fielded units that a truck would actually SERVE right now — the counting twin of
		// AnyFieldedUnitNeedsResupply, at the identical bar (ResupplyNeedThreshold, mirroring
		// SupplyProvider.MinNeedThreshold).
		//
		// This exists because the boolean and the count were measuring different things: the boolean said
		// "somebody needs resupply" (True from tick 1240 to the end of the match) while the fleet was sized
		// from CountStarvingCustomers, a strictly tighter bar that read 0 at every snapshot. One fact, two
		// predicates, and the one that could not see the demand was the one holding the purse. Sharing
		// OwnedUnitsIncludingCarried and the same Rearmable filter is what keeps them from drifting again.
		//
		// Counts UNITS, not pools, exactly as the starving count does: a soldier with two dry weapons is one
		// customer for one truck.
		int CountResupplyCustomers()
		{
			var count = 0;
			foreach (var a in OwnedUnitsIncludingCarried())
			{
				var rearmable = a.TraitOrDefault<Rearmable>();
				if (rearmable == null || !rearmable.Info.RearmActors.Overlaps(Info.ResupplyUnitTypes))
					continue;

				var pools = rearmable.RearmableAmmoPools;
				if (pools == null || pools.Length == 0)
					continue;

				if (ResupplyDemand.MeetsThreshold(
					ResupplyDemand.UnitNeed(pools.Select(p => (p.Info.Ammo, p.CurrentAmmoCount, p.Info.SupplyValue))),
					Info.ResupplyNeedThreshold))
					count++;
			}

			return count;
		}

		// Every fielded unit a truck COULD rearm, regardless of how full it currently is — the denominator for
		// the standing floor (SupplyTruckFloorPer).
		//
		// This is deliberately the only one of the three customer counts with NO ammo test. The other two ask
		// "who needs a truck right now" and size the surge; this asks "how big is the force a truck exists to
		// serve", which is what a STANDING reserve must be proportional to. Sizing the reserve from current
		// need would reproduce the defect it exists to fix — a full-ammo army would carry no reserve, and the
		// fleet would once again only be bought after the men were already dry.
		//
		// Shares OwnedUnitsIncludingCarried and the same Rearmable filter as the other two so all three agree
		// on who a customer is; on this ruleset that resolves to infantry only, since `RearmActors` naming
		// `truk` appears nowhere else.
		int CountResupplyCapableUnits()
		{
			var count = 0;
			foreach (var a in OwnedUnitsIncludingCarried())
			{
				var rearmable = a.TraitOrDefault<Rearmable>();
				if (rearmable == null || !rearmable.Info.RearmActors.Overlaps(Info.ResupplyUnitTypes))
					continue;

				var pools = rearmable.RearmableAmmoPools;
				if (pools == null || pools.Length == 0)
					continue;

				count++;
			}

			return count;
		}

		// Fielded units with at least one truck-rearmable pool below the starving bar. Deliberately counts
		// UNITS, not pools: a soldier with two dry weapons is still one customer for one truck.
		int CountStarvingCustomers()
		{
			var count = 0;
			foreach (var a in OwnedUnitsIncludingCarried())
			{
				var rearmable = a.TraitOrDefault<Rearmable>();
				if (rearmable == null || !rearmable.Info.RearmActors.Overlaps(Info.ResupplyUnitTypes))
					continue;

				var pools = rearmable.RearmableAmmoPools;
				if (pools == null)
					continue;

				foreach (var p in pools)
				{
					if (SupplyHuntMath.BelowSeekThreshold(p.CurrentAmmoCount, p.Info.Ammo, Info.SupplyStarvingThresholdPerMille))
					{
						count++;
						break;
					}
				}
			}

			return count;
		}

		// Pending call-ins count: a reinforcement walks in from the map edge long after the 30-tick purchase
		// cycle that ordered it, so counting only live trucks would re-order the whole fleet every cycle until
		// the first one arrived. Same argument CensusValues makes for its pending credit.
		//
		// SPENT TRUCKS DO NOT COUNT, and this is load-bearing rather than tidy. TRUK restocks only at a
		// logisticscenter (vehicles.yaml:548), which is `Prerequisites: ~disabled` — capture-only, so a bot
		// that captures none can NEVER refill a truck. Measured over the user's match: 14 trucks, every one
		// adopted at supply=750 and released `reason=low-supply`, not one restock. A truck is therefore a
		// CONSUMABLE carrying one 750 load, and counting drained hulls as fleet would let six dead-weight
		// trucks satisfy the target while the front starved — the same failure in a new costume.
		int SupplyTrucksOwnedOrPending()
		{
			var count = world.Actors.Count(a => a.Owner == player && !a.IsDead && a.IsInWorld
				&& Info.ResupplyUnitTypes.Contains(a.Info.Name)
				&& a.TraitOrDefault<SupplyProvider>()?.CountsAsEmpty != true);

			var queues = AIUtils.FindQueuesByCategory(player);
			foreach (var category in Info.UnitQueues)
				foreach (var q in queues[category])
					foreach (var item in q.AllQueued())
						if (Info.ResupplyUnitTypes.Contains(item.Item))
							count++;

			count += priorityBuildRequests.Count(r => Info.ResupplyUnitTypes.Contains(r));
			count += queuedBuildRequests.Count(r => Info.ResupplyUnitTypes.Contains(r));

			return count;
		}

		// The pre-emption itself: the first buildable, affordable, under-limit resupply type the fleet is short
		// of. Returning null falls through to the ordinary deficit pick, so a cycle where the truck is priced
		// out or at its UnitLimit still buys something rather than being wasted.
		ActorInfo ChooseSupplyFleetShortfall(HashSet<string> buildableNames, long budget)
		{
			foreach (var name in supplyFleetTypes)
			{
				if (!buildableNames.Contains(name) || !world.Map.Rules.Actors.TryGetValue(name, out var actorInfo))
					continue;

				if (Info.UnitLimits != null && Info.UnitLimits.TryGetValue(name, out var limit) &&
					world.Actors.Count(a => a.Owner == player && a.Info.Name == name) >= limit)
					continue;

				if (!CompositionNeedMath.Affordable(budget, UnitCost(actorInfo), 100))
					continue;

				if (!SupplyFleetUnderDesired(name))
					continue;

				AIUtils.BotDebug("{0} supply fleet SHORT: {1} owned+pending {2} < desired {3} ({4} starving / {5} per truck at {6}%, floor {7} (cap {8} per {9}), ceiling {10})",
					player, name, supplyFleetOwned, supplyFleetDesired, supplyFleetStarving,
					Info.SupplyCustomersPerTruck, Info.SupplyDemandOvercompensationPercent,
					supplyFleetFloor, Info.SupplyTruckFloor, Info.SupplyTruckFloorPer, Info.SupplyTruckCeiling);

				return actorInfo;
			}

			return null;
		}

		// Does ANY UnitBuilder instance on this player want to bank right now?
		//
		// THE SCOPE ARGUMENT, which is this branch's own rule applied where it had not yet been applied: a
		// decision about a shared resource must be taken at the scope of that resource. The resource is the
		// TREASURY. One trait instance is not its scope, and neither is one queue — that was the first
		// version of this bug, fixed a layer down. Under @experimental a player runs THREE live
		// UnitBuilderBotModule instances (the ground twin, .fixedwing, and the experimental .heli twin), all
		// spending one balance, and only the ground twin carries the supply flags. Measured consequence: a
		// player banked 29 consecutive cycles, reached cash 935 against a 1000 truck, and was drained back to
		// 477 by its siblings — 1,340 spent during the silence.
		//
		// DELIBERATELY NARROW: siblings of THIS TRAIT only. It is not extended to other module types even
		// though they also spend, because CaptureCoordinatorBotModule's capturer floor and
		// AdaptiveProductionBotModule's counter-buys are those modules' own correctness contracts, and
		// suppressing them from here would trade a scope bug for a cross-module dependency. Silencing a
		// sibling instance of the SAME trait overrides nobody's invariant — it makes one mechanism behave
		// consistently with itself.
		//
		// Cached per world tick. The cache is per-instance but cannot disagree across instances, because the
		// ANSWER is computed by the owning instance's own per-tick-cached ShouldBankForSupply — so the stall
		// bookkeeping runs exactly once per tick no matter which sibling asks first. That is genuinely shared
		// state derived from one owner, not N caches that happen to agree.
		bool AnySiblingWantsSupplyBank()
		{
			if (bankHoldTick == world.WorldTick)
				return bankHold;

			bankHoldTick = world.WorldTick;
			bankHold = false;

			foreach (var sibling in player.PlayerActor.TraitsImplementing<UnitBuilderBotModule>())
			{
				if (sibling.WantsSupplyBank())
				{
					bankHold = true;
					break;
				}
			}

			return bankHold;
		}

		// This instance's own banking decision, with no sibling consultation — that separation is what stops
		// AnySiblingWantsSupplyBank recursing. Only an ENABLED instance that actually owns the supply flags
		// can want to bank; a condition-disabled twin never ticks, so letting it vote would let a dormant
		// config silence a live one.
		internal bool WantsSupplyBank()
		{
			if (IsTraitDisabled || !Info.SupplyDemandSizing || Info.SupplyPrecedenceStallCycles <= 0)
				return false;

			return ShouldBankForSupply(AvailableBudget());
		}

		// Clear the banking-spell trail. Called both when a truck is bought and when a spell is abandoned, so
		// the high-water mark can never leak across spells: a stale `best` from a richer moment would make
		// every later cycle look stalled and suppress banking that was in fact making progress.
		void EndBankingSpell()
		{
			supplyBankBestCash = 0;
			supplyBankStalled = 0;
			supplyBankedCycles = 0;
		}

		// Should this cycle buy NOTHING and bank toward a truck? True only when the fleet is short and the
		// truck is genuinely unaffordable — if we could afford it, ChooseSupplyFleetShortfall would already
		// have bought it and we would never be asked.
		//
		// DELIBERATELY QUEUE-INDEPENDENT, and this is the whole correctness argument rather than a detail.
		// ChooseByDeficit runs once PER QUEUE, and `truk` is buildable only from the Vehicle queue. A version
		// of this that tested the CALLING queue's buildable set banked the Vehicle queue while the Infantry
		// queue happily went on spending — on the same shared treasury the truck was saving from. Measured
		// exactly that way: 208 bank decisions, `banked` never rising above 1 because the Infantry queue
		// reset it every cycle, and cash still sawtoothing 92/158/124/190/256/166 without ever reaching
		// 1000. Banking one queue is not banking. The budget is global, so the decision must be too: this
		// asks whether ANY queue this module drives could produce a needed truck, and when the answer is yes
		// EVERY queue declines.
		//
		// Cached per world tick so the two queue calls in one bot tick cannot disagree (and so the buildable
		// scan runs once). world.WorldTick is deterministic and every input is an order-independent sum.
		//
		// The decision itself is in SupplyPrecedenceMath.ShouldBankCycle (pure, NUnit-pinned); this only
		// samples the world state it needs.
		bool ShouldBankForSupply(long budget)
		{
			if (Info.SupplyPrecedenceStallCycles <= 0)
				return false;

			if (supplyBankDecisionTick == world.WorldTick)
				return supplyBankDecision;

			supplyBankDecisionTick = world.WorldTick;
			supplyBankDecision = false;

			// Progress bookkeeping, once per tick and BEFORE the decision, so a spell that has stopped
			// advancing is caught on the same cycle it stalls.
			supplyBankStalled = SupplyPrecedenceMath.UpdateStall(budget, supplyBankBestCash, supplyBankStalled);
			if (budget > supplyBankBestCash)
				supplyBankBestCash = budget;

			var fleetShort = false;
			var affordable = false;
			foreach (var name in supplyFleetTypes)
			{
				if (!world.Map.Rules.Actors.TryGetValue(name, out var actorInfo))
					continue;

				if (Info.UnitLimits != null && Info.UnitLimits.TryGetValue(name, out var limit) &&
					world.Actors.Count(a => a.Owner == player && a.Info.Name == name) >= limit)
					continue;

				// Never stall production saving for something no queue can actually deliver.
				if (!BuildableFromAnyQueue(name))
					continue;

				if (!SupplyFleetUnderDesired(name))
					continue;

				fleetShort = true;
				if (CompositionNeedMath.Affordable(budget, UnitCost(actorInfo), 100))
				{
					affordable = true;
					break;
				}
			}

			supplyBankDecision = SupplyPrecedenceMath.ShouldBankCycle(fleetShort, affordable,
				supplyBankStalled, Info.SupplyPrecedenceStallCycles);

			return supplyBankDecision;
		}

		// Can any queue this module drives currently produce this type? The per-queue buildable set is what
		// ChooseByDeficit works from, but the treasury is shared across queues, so a decision about MONEY has
		// to be asked across all of them.
		bool BuildableFromAnyQueue(string name)
		{
			var queues = AIUtils.FindQueuesByCategory(player);
			foreach (var category in Info.UnitQueues)
				foreach (var q in queues[category])
					foreach (var item in q.BuildableItems())
						if (item.Name == name)
							return true;

			return false;
		}

		// The floor pre-empt: the first type (ordinal) whose standing population is under its UnitFloors entry
		// and which we can actually call in. Returning null falls through to the ordinary deficit pick.
		//
		// This deliberately does NOT consult IsCompositionCandidateEligible. The target ceiling and the
		// ScaleAntiAirToThreat gate are precisely what a floor has to outrank — a floor a threat gate can
		// refuse is not a floor, and an AA floor whose whole purpose is to hold AA BEFORE enemy air is seen
		// would never fire. UnitLimits, buildability, UnitsToBuild membership, UnitDelays and affordability
		// are all still honoured: a floor is a priority, not a licence to buy what does not exist or to
		// overdraw. A floor above its own UnitLimit is bounded by the limit, not an error.
		ActorInfo ChooseBelowFloor(HashSet<string> buildableNames, long budget)
		{
			foreach (var name in floorTypes)
			{
				if (!buildableNames.Contains(name) || !world.Map.Rules.Actors.TryGetValue(name, out var actorInfo))
					continue;

				if (Info.UnitsToBuild != null && !Info.UnitsToBuild.ContainsKey(name))
					continue;

				if (Info.UnitDelays != null && Info.UnitDelays.TryGetValue(name, out var delay) && delay > world.WorldTick)
					continue;

				if (Info.UnitLimits != null && Info.UnitLimits.TryGetValue(name, out var limit)
					&& OwnedOrPending(name) >= limit)
					continue;

				if (!CompositionNeedMath.Affordable(budget, UnitCost(actorInfo), 100))
					continue;

				// Counting PENDING call-ins is load-bearing, not defensive: a reinforcement walks in from the
				// map edge over many purchase cycles, so an owned-only count would re-order the same unit every
				// cycle until the first one arrives and blow straight past the floor.
				//
				// The floor is the EFFECTIVE one, which for a support type is scaled to the force it serves and
				// is therefore ZERO before that force exists. That is what stops this pre-empt — which outranks
				// the ceiling and every demand gate — from spending the opening call-ins on support units.
				var effectiveFloor = EffectiveFloorFor(name);
				if (OwnedOrPending(name) >= effectiveFloor)
					continue;

				AIUtils.BotDebug("{0} standing floor SHORT: {1} owned+pending {2} < floor {3} (flat {4})",
					player, name, OwnedOrPending(name), effectiveFloor, Info.UnitFloors[name]);

				return actorInfo;
			}

			return null;
		}

		// The floor actually in force for a type right now — the ONE place both decision sites read, so the
		// pre-empt (ChooseBelowFloor) and the demand-gate exemption (BuildUnit) can never disagree about what
		// the floor is. They did not disagree before because both inlined the same raw lookup; a scaled floor
		// makes that duplication a real hazard, hence the shared accessor.
		//
		// Returns 0 for any type with no UnitFloors entry, which is every type on every profile that does not
		// opt in — so this reduces to the frozen answer without touching the frozen path.
		int EffectiveFloorFor(string name)
		{
			if (Info.UnitFloors == null || !Info.UnitFloors.TryGetValue(name, out var flatFloor))
				return 0;

			var per = 0;
			if (Info.UnitFloorPer != null)
				Info.UnitFloorPer.TryGetValue(name, out per);

			// Only pay for the denominator walk when a ratio is actually configured for this type.
			return SupportFloorMath.EffectiveFloor(flatFloor, per, per > 0 ? CountSupportedForce() : 0);
		}

		// The denominator for UnitFloorPer: how much of the force that support types serve actually EXISTS.
		//
		// Owned + in-cargo, matching OwnedOrPending's cargo credit — boarding REMOVES a passenger from
		// world.Actors outright, so a world-only walk would collapse this count to near zero every time the
		// squad boarded a transport and hand the floor straight back to zero mid-match.
		//
		// Pending is EXCLUDED, unlike the numerator. A unit still walking in from the map edge is not yet a
		// squad that needs a medic, and counting it would let a queue full of infantry pull the support buy
		// forward to exactly the t=0 moment this scaling exists to prevent.
		//
		// Order-independent sum over a membership filter, so world iteration order cannot leak into the
		// decision (the determinism invariant).
		int CountSupportedForce()
		{
			if (Info.UnitFloorSupportedTypes.Count == 0)
				return 0;

			var count = 0;
			foreach (var a in OwnedUnitsIncludingCarried())
				if (Info.UnitFloorSupportedTypes.Contains(a.Info.Name))
					count++;

			return count;
		}

		// Live actors — INCLUDING transported and garrisoned ones — plus everything already ordered for this
		// type across both request lanes and every queue this module drives. Order-independent sums only.
		//
		// The cargo term is not defensive bookkeeping, it is what stops the floor becoming the disease it was
		// meant to cure. Both floored types (aa.*, medi.*) are listed as PassengerTypes on
		// MountedTransportBotModule and are recruited by the garrison modules, and boarding REMOVES the
		// passenger from world.Actors outright (RideTransport `w.Remove(self)`) rather than clearing
		// IsInWorld. A world-only count would therefore read every loaded AA soldier as dead and buy
		// replacements without bound for as long as the transports kept swallowing them — an unbounded spend
		// driven by units that are alive and well. CensusValues already credits cargo for exactly this reason
		// (CreditTransportedUnits); the floor has to agree with it or the two lanes fight.
		int OwnedOrPending(string name)
		{
			var count = world.Actors.Count(a => a.Owner == player && !a.IsDead && a.IsInWorld
				&& a.Info.Name == name);

			foreach (var pair in world.ActorsWithTrait<Cargo>())
			{
				var transport = pair.Actor;
				if (transport.IsDead || !transport.IsInWorld)
					continue;

				if (transport.Owner != player && player.RelationshipWith(transport.Owner) != PlayerRelationship.Ally)
					continue;

				foreach (var p in pair.Trait.Passengers)
					if (p.Owner == player && p.Info.Name == name)
						count++;
			}

			var queues = AIUtils.FindQueuesByCategory(player);
			foreach (var category in Info.UnitQueues)
				foreach (var q in queues[category])
					foreach (var item in q.AllQueued())
						if (item.Item == name)
							count++;

			count += priorityBuildRequests.Count(r => r == name);
			count += queuedBuildRequests.Count(r => r == name);

			return count;
		}

		// Behaviour 2: allow another gated AA unit only while owned count is under the observed-air cap.
		bool ShouldBuildMoreAntiAir()
		{
			var owned = world.Actors.Count(a => a.Owner == player && !a.IsDead && a.IsInWorld
				&& Info.AntiAirUnitTypes.Contains(a.Info.Name));

			return AntiAirDemand.ShouldBuildMore(owned, CountObservedEnemyAir(),
				Info.AntiAirBaseline, Info.AntiAirPerObservedAir);
		}

		// Behaviour 3: is calling in one more transport justified? Refuses unless there is a full minimum
		// load of infantry actually waiting AND no idle transport we already own could carry them
		// (transports-first). The decision itself lives in TransportEmploymentMath; this only samples world
		// state. Both counts are ORDER-INDEPENDENT (plain sums over a filter, and the candidate tally is
		// capped at the airframe's own MaxWeight), so world.Actors' iteration order cannot leak into the
		// decision — the determinism invariant the byte-identity argument rests on.
		bool ShouldBuyTransport(ActorInfo transportInfo)
		{
			// A gated type that carries nothing has no lift-demand model — leave the frozen decision alone
			// rather than silently banning it.
			var cargo = transportInfo.TraitInfoOrDefault<CargoInfo>();
			if (cargo == null)
				return true;

			// Reserve-bubble anchor. Falls back to the player's spawn when the SR is dead or captured — a
			// missing anchor must not silently remove the spatial gate and make the whole map liftable.
			// MULTI-SR: lowest-ActorID owned SR, matching HelicopterSquadBotModule.FindOwnSupplyRoute. That the
			// two AGREE is what matters here, since this count has to predict the load that module assembles; a
			// CAPTURED second SR gets no reserve bubble of its own on either side.
			var srCell = player.HomeLocation;
			foreach (var a in world.Actors)
			{
				if (a.Owner == player && !a.IsDead && a.IsInWorld && Info.SupplyRouteTypes.Contains(a.Info.Name))
				{
					srCell = a.Location;
					break;
				}
			}

			var owned = 0;
			var idle = 0;
			foreach (var a in world.Actors)
			{
				if (a.Owner != player || a.IsDead || !a.IsInWorld)
					continue;

				if (!Info.TransportUnitTypes.Contains(a.Info.Name))
					continue;

				owned++;

				// Spare capacity for the transports-first test. IsUnoccupiedAirframe, not Actor.IsIdle: a
				// transport hovering at the SR carries FlyIdle forever, so the old test counted ZERO idle
				// transports always — the branch could never fire and only UnitLimits capped the buy.
				// KNOWN OVERCOUNT: a transport whose passengers are still walking has not been reserved yet, so
				// it is still on FlyIdle and counts as spare here. The error is in the SAFE direction (defer a
				// purchase we could have made) and it self-clears within a load window.
				if (AIUtils.IsUnoccupiedAirframe(a))
					idle++;
			}

			// Lift demand. Must agree with the consuming squad module's CountLiftCandidates or the two halves of
			// the transport policy contradict each other (buy an airframe the launcher will never load, or refuse
			// one it is starving for). Same predicate: infantry of a compatible cargo type inside the SR reserve
			// bubble and not claimed by another module. NOT Actor.IsIdle — infantry on the line engage through
			// AutoTarget and are never idle, so the old world-wide idle scan almost never reached
			// TransportMinPassengers and the demand gate refused essentially every call-in.
			// Cap at the same number the squad module will actually ORDER aboard, not the airframe's physical
			// capacity — counting to tran's MaxWeight of 36 would report demand no lift can consume.
			var loadCap = TransportEmploymentMath.LoadCap(Info.TransportMaxPassengers, cargo.MaxWeight, Info.TransportMinPassengers);
			var candidates = 0;
			foreach (var a in world.Actors)
			{
				if (a.Owner != player || a.IsDead || !a.IsInWorld)
					continue;

				if (!a.Info.HasTraitInfo<Render.WithInfantryBodyInfo>() || !a.Info.HasTraitInfo<MobileInfo>())
					continue;

				if (!cargo.Types.Overlaps(a.GetAllTargetTypes()))
					continue;

				// Same role gate the squad module applies, or this over-counts: without it the capture
				// engineer, medics and MANPADS all read as lift demand and buy an airframe that will never
				// carry them. Fails closed with no resolver, exactly as the squad module does.
				if (Info.RestrictLiftToLineInfantry
					&& (roleResolver == null || roleResolver.GetRole(a) != UnitRole.MainBattle))
					continue;

				if (goalGuard != null && !goalGuard.IsTraitDisabled && goalGuard.Ledger.IsCommitted(a, world.WorldTick))
					continue;

				if (!TransportEmploymentMath.InReserveZone((a.Location - srCell).LengthSquared, Info.LiftReserveZoneRadiusCells))
					continue;

				if (++candidates >= loadCap)
					break;
			}

			// Reuse the configured per-type ceiling as the transport cap. NOTE what this does and does NOT buy:
			// UnitLimits counts WORLD ACTORS only, so a call-in still sitting in the production queue is
			// invisible to it — and this cap inherits exactly that blind spot. It is therefore not what stops a
			// concurrent second call-in. UnitDelays does not either: it is an ABSOLUTE world-tick opening
			// threshold, not a repeat cooldown. The real serializer is BuildUnit's empty-queue precondition
			// (it only picks a queue with nothing already queued), and with one Aircraft queue per Supply Route
			// that admits no second simultaneous call-in; once ProductionFromMapEdge spawns the unit into the
			// world, UnitLimits bounds the rest. The residual is multi-SR (a second captured SR = a second
			// queue), and it is bounded by UnitLimits at spawn. 0 ⇒ no ceiling configured.
			var cap = 0;
			if (Info.UnitLimits != null && Info.UnitLimits.TryGetValue(transportInfo.Name, out var limit))
				cap = limit;

			return TransportEmploymentMath.ShouldBuy(owned, cap, idle, candidates, Info.TransportMinPassengers);
		}

		// Fog-legal enemy-air count: only aircraft the player can currently VIEW (no omniscient read).
		// Mirrors AdaptiveProductionBotModule.ScanEnemyComposition's aircraft branch.
		int CountObservedEnemyAir()
		{
			var count = 0;
			foreach (var actor in world.Actors)
			{
				if (actor.IsDead || !actor.IsInWorld || actor.Owner == null)
					continue;

				if (player.RelationshipWith(actor.Owner) != PlayerRelationship.Enemy)
					continue;

				if (!actor.CanBeViewedByPlayer(player))
					continue;

				if (actor.Info.HasTraitInfo<AircraftInfo>())
					count++;
			}

			return count;
		}

		// In cases where we want to build a specific unit but don't know the queue name (because there's more than one possibility).
		// Returns true iff an order was actually issued (a matching queue was free). The priority-drain caller uses
		// this to peek-don't-pop: a request that can't be placed this cycle (busy queue) stays queued for retry.
		bool BuildUnit(IBot bot, string name)
		{
			var actorInfo = world.Map.Rules.Actors[name];
			if (actorInfo == null)
				return false;

			var buildableInfo = actorInfo.TraitInfoOrDefault<BuildableInfo>();
			if (buildableInfo == null)
				return false;

			ProductionQueue queue = null;
			foreach (var pq in buildableInfo.Queue)
			{
				queue = AIUtils.FindQueuesByCategory(player)[pq].FirstOrDefault(q => !q.AllQueued().Any());
				if (queue != null)
					break;
			}

			if (queue != null)
			{
				bot.QueueOrder(Order.StartProduction(queue.Actor, name, 1));
				AIUtils.BotDebug("{0} decided to build {1} (external request)", queue.Actor.Owner, name);
				return true;
			}

			return false;
		}

		ActorInfo ChooseRandomUnitToBuild(ProductionQueue queue)
		{
			var buildableThings = queue.BuildableItems();
			if (!buildableThings.Any())
				return null;

			var unit = buildableThings.Random(world.LocalRandom);
			return HasAdequateAirUnitReloadBuildings(unit) ? unit : null;
		}

		ActorInfo ChooseUnitToBuild(ProductionQueue queue)
		{
			var buildableThings = queue.BuildableItems();
			if (!buildableThings.Any())
				return null;

			var myUnits = player.World
				.ActorsHavingTrait<IPositionable>()
				.Where(a => a.Owner == player)
				.Select(a => a.Info.Name).ToList();

			foreach (var unit in Info.UnitsToBuild.Shuffle(world.LocalRandom))
				if (buildableThings.Any(b => b.Name == unit.Key))
					if (myUnits.Count(a => a == unit.Key) * 100 < unit.Value * myUnits.Count)
						if (HasAdequateAirUnitReloadBuildings(world.Map.Rules.Actors[unit.Key]))
							return world.Map.Rules.Actors[unit.Key];

			return null;
		}

		// ===== Composition-directed pick (@experimental) =====
		// Buy the type the army is furthest BELOW its target share, instead of drawing uniformly at random.
		// Zero random draws. Returns null only when the legacy fallback also declines; a queue this pass has no
		// opinion about (nothing eligible — e.g. a pool with no UnitTargetShares entries at all) falls back to
		// the legacy ternary so purchase VOLUME is never reduced by the flag.
		ActorInfo ChooseByDeficit(ProductionQueue queue, bool buildRandom)
		{
			if (compositionTypes == null || compositionTypes.Length == 0)
				return buildRandom ? ChooseRandomUnitToBuild(queue) : ChooseUnitToBuild(queue);

			var buildableThings = queue.BuildableItems();
			if (!buildableThings.Any())
				return null;

			var census = ForceCompositionMath.SharesPerMille(CensusValues());
			var targets = ForceCompositionMath.ApplyCounterBias(compositionTargets, UpdateThreatShares(),
				counterMatrix, Info.CounterBiasMaxPct, Info.ThreatDeadbandPerMille);

			// Materialize once: BuildableItems() is a lazy query and eligibility probes it per slot.
			var buildableNames = new HashSet<string>(buildableThings.Select(b => b.Name));

			var budget = AvailableBudget();

			// SUPPLY FLEET FLOOR — ahead of the deficit pick, because the deficit pick is exactly what cannot
			// size this fleet. Target shares are per-mille of army VALUE, so at truk's 40 per-mille a 1000-cost
			// truck is admitted once per 25,000 value of army; CompositionEnforceTargetCeiling then strikes the
			// slot the moment the first truck pushes the census over that, and the second truck is never bought.
			// Measured over a 30-minute match: one standing truck per player while infantry starved. Logistics
			// is sized by CUSTOMERS, not by what fraction of the budget the trucks happen to represent — so
			// demand pre-empts, bounded by SupplyTruckCeiling and by UnitLimits above it.
			if (Info.SupplyDemandSizing)
			{
				var shortfall = ChooseSupplyFleetShortfall(buildableNames, budget);
				if (shortfall != null)
				{
					// Bought one: this spell did its job, so the progress trail starts fresh.
					EndBankingSpell();
					LogPick("supply-fleet", shortfall.Name, $"queue={queue.Info.Type} "
						+ $"starving={supplyFleetStarving} needy={supplyFleetNeedy} desired={supplyFleetDesired} "
						+ $"floor={supplyFleetFloor}");
					return shortfall;
				}

				// The bank HOLD itself now lives at the top of BuildUnit, at treasury scope, so that it also
				// silences the sibling .heli/.fixedwing instances that never enter this method. Reaching here
				// therefore means we are NOT banking this cycle — demand met, truck affordable, or the
				// balance stopped advancing — so the spell ends and its progress trail is cleared, leaving
				// the next spell to be judged on its own merits rather than against a high-water mark from a
				// richer moment.
				EndBankingSpell();
			}

			// STANDING-POPULATION FLOOR — same reason the supply fleet pre-empts, generalised. A per-mille-of-
			// VALUE target cannot hold a small type on the map: one unit of a 9-per-mille type is over target in
			// any army below 1000*cost/target, and under losses the big slots stay in deficit so the argmax never
			// descends to a specialist slot at all. Second, so an explicit fleet size still wins a tie with a
			// floor on the same type.
			var belowFloor = ChooseBelowFloor(buildableNames, budget);
			if (belowFloor != null)
			{
				LogPick("floor", belowFloor.Name, $"queue={queue.Info.Type} owned+pending={OwnedOrPending(belowFloor.Name)} "
					+ $"floor={EffectiveFloorFor(belowFloor.Name)} supported={CountSupportedForce()}");
				return belowFloor;
			}

			var eligible = new bool[compositionTypes.Length];
			var anyComposedTypeInQueue = false;
			for (var i = 0; i < compositionTypes.Length; i++)
			{
				eligible[i] = IsCompositionCandidateEligible(compositionTypes[i], buildableNames, budget);
				anyComposedTypeInQueue |= buildableNames.Contains(compositionTypes[i]);
			}

			// CEILING on our OWN pick (experimental). Without this the affordability filter above turns the
			// argmax into "buy the cheapest composed type in this queue" whenever cash is in the low band, no
			// matter how far over target that type already sits — see ApplyCeilingEligibility. anyComposedTypeInQueue
			// is measured before this, so stripping the over-target slots routes into the decline path below
			// (bank the cash) rather than the uniform-lottery fallback.
			if (Info.CompositionEnforceTargetCeiling)
				eligible = ForceCompositionMath.ApplyCeilingEligibility(targets, census, eligible);

			var idx = ForceCompositionMath.SelectDeficit(targets, census, eligible);
			if (idx < 0)
			{
				// Nothing eligible — but for two very different reasons, and the frozen path conflates them.
				// "No composed type is buildable from this queue at all" is genuinely no opinion (a heli-only
				// pool) and must fall back so purchase volume is unchanged. "Composed types ARE buildable but
				// every one is priced out or at its UnitLimit" is a DECISION not to buy: falling back there
				// draws the uniform lottery, which buys by lifetime and rebuilds the very drift this lane
				// exists to remove — and since a bot spends to zero routinely, the affordability case alone
				// makes that fallback frequent enough to dominate the outcome.
				if (ForceCompositionMath.ShouldDeclineCycle(Info.CompositionEnforceTargetCeiling, false, anyComposedTypeInQueue))
				{
					// Invisible buys were the original diagnostic problem; don't replace them with invisible
					// non-buys. One line per declined cycle, on the same channel as the selection log.
					AIUtils.BotDebug("{0} composition-directed DECLINE: {1} composed types buildable, none eligible (priced out or at UnitLimit)",
						player, queue.Info.Type);
					return null;
				}

				return buildRandom ? ChooseRandomUnitToBuild(queue) : ChooseUnitToBuild(queue);
			}

			LogCompositionChoice(compositionTypes[idx], ForceCompositionMath.DeficitAt(targets, census, idx), census, targets);
			LogPick("deficit", compositionTypes[idx], $"queue={queue.Info.Type} "
				+ $"deficit={ForceCompositionMath.DeficitAt(targets, census, idx)}");

			return world.Map.Rules.Actors[compositionTypes[idx]];
		}

		// Is an externally requested call-in ALREADY at or over its composition target? Only the FIFO drain
		// asks; the priority drain (capture-supply floor) is exempt by construction, and named types are
		// additionally exempt on this lane via CompositionCeilingExemptTypes. A type with no target share —
		// helicopters, MCVs, harvesters — has no composition opinion and is never refused here.
		//
		// The request under test is STILL on queuedBuildRequests when this is called (BotTick removes it after),
		// and CensusValues credits both request lists, so its own cost is in the census and has to come back out
		// — otherwise the rule reads "would be over AFTER this buy" and refuses a class still legitimately short
		// by less than one unit's worth of share. Consequence worth stating plainly: this predicate and the
		// deficit pick therefore evaluate DIFFERENT census bases by exactly one candidate's cost. That is
		// deliberate, not an inconsistency — the pick is choosing what to add, this is judging what already
		// stands.
		bool RequestIsOverCompositionCeiling(string name)
		{
			if (!Info.CompositionEnforceTargetCeiling || compositionTypes == null)
				return false;

			if (Info.CompositionCeilingExemptTypes.Contains(name))
				return false;

			var slot = Array.IndexOf(compositionTypes, name);
			if (slot < 0)
				return false;

			if (!world.Map.Rules.Actors.TryGetValue(name, out var actorInfo))
				return false;

			var targets = ForceCompositionMath.ApplyCounterBias(compositionTargets, UpdateThreatShares(),
				counterMatrix, Info.CounterBiasMaxPct, Info.ThreatDeadbandPerMille);

			var over = ForceCompositionMath.RequestExceedsCeiling(CensusValues(), slot, UnitCost(actorInfo), targets);
			if (over)
				AIUtils.BotDebug("{0} composition ceiling REFUSED external request: {1} is already at or over target",
					player, name);

			return over;
		}

		// Own-force census in VALUE (ValuedInfo.Cost), bucketed into the ordinal slots. Two deliberate details:
		//   * EVACUATING units are excluded — a unit flying/driving off the map to refund its cost is not
		//     force-in-being, and counting it would suppress the very replacement buy it should trigger.
		//   * PENDING call-ins are credited at FULL cost. The purchase cycle is 30 ticks but a reinforcement
		//     takes far longer to walk in from the map edge, so without this the bot would re-pick the same
		//     deficit type every cycle and badly overshoot it before the first one ever arrives.
		// The accumulation is additive into fixed slots, so it is order-independent by construction (same
		// argument AdaptiveProductionBotModule uses for its believed-value sums) — no sort needed for determinism.
		int[] CensusValues()
		{
			var values = new int[compositionTypes.Length];
			var excludeCondition = Info.CensusExcludeCondition;
			var hasExclude = !string.IsNullOrEmpty(excludeCondition);

			foreach (var a in world.Actors)
			{
				if (a.Owner != player || a.IsDead || !a.IsInWorld)
					continue;

				var slot = Array.IndexOf(compositionTypes, a.Info.Name);
				if (slot < 0)
					continue;

				if (hasExclude && a.GetConditionCount(excludeCondition) > 0)
					continue;

				values[slot] += UnitCost(a.Info);
			}

			// Pending credit: everything already queued/in-progress on the module's own queues, plus both
			// request lists. The queue BuildUnit selected is empty by construction (it filters on
			// !AllQueued().Any()), so crediting only "this queue" would always be a no-op — the credit has to
			// span the module's queues to actually damp the overshoot it exists to prevent.
			var queues = AIUtils.FindQueuesByCategory(player);
			foreach (var category in Info.UnitQueues)
			{
				foreach (var q in queues[category])
				{
					foreach (var item in q.AllQueued())
					{
						var slot = Array.IndexOf(compositionTypes, item.Item);
						if (slot >= 0 && world.Map.Rules.Actors.TryGetValue(item.Item, out var pendingInfo))
							values[slot] += UnitCost(pendingInfo);
					}
				}
			}

			CreditTransportedUnits(values, excludeCondition, hasExclude);

			CreditRequests(values, priorityBuildRequests);
			CreditRequests(values, queuedBuildRequests);

			return values;
		}

		// TRANSPORTED / GARRISONED units are ABSENT from world.Actors entirely, so the loop above cannot see
		// them — boarding REMOVES the passenger from the world dictionary (RideTransport.cs:85 `w.Remove(self)`,
		// right after Cargo.Load), it does not merely clear IsInWorld. Both loaders are live on the profile that
		// enables this: MountedTransportBotModule loads infantry into bradley/bmp2/m113, and GarrisonBotModule
		// garrisons infantry into buildings. Without this pass every loaded unit reads as a PERMANENT deficit and
		// the bot re-buys it forever — self-reinforcing, and able to re-create the exact mortar pile-up this lane
		// exists to fix (mortar infantry is transportable).
		//
		// Cargo is the single authoritative container for both cases: a garrison's shelter soldiers live in the
		// building's own Cargo (GarrisonManager.cs:185/:1209 — shelterPassengers mirrors it), and a soldier
		// DEPLOYED to a firing port is `cargo.Unload`ed and re-added to the world (:341/:372), so it is already
		// counted by the main loop. The two sources are therefore disjoint: no double-count against world.Actors,
		// and none against the pending-queue/request credit either (that counts things not yet delivered).
		void CreditTransportedUnits(int[] values, string excludeCondition, bool hasExclude)
		{
			foreach (var pair in world.ActorsWithTrait<Cargo>())
			{
				var transport = pair.Actor;
				if (transport.IsDead || !transport.IsInWorld)
					continue;

				// Look inside our own and allied containers only. This misses nothing, because a container
				// holding THIS player's soldiers is always player- or ally-owned: a garrison claims its
				// building for the entering soldier's owner on the way in (GarrisonManager.OnPassengerEntered
				// -> ChangeOwnerInPlace, :253-262) and reverts/transfers only once occupants leave (:320-332),
				// and vehicle transports are loaded by MountedTransportBotModule pairing the bot's own infantry
				// with its own carriers. (Cargo.CanLoad itself does NOT gate on ownership — it tests only
				// LoadingBlocked, the ICargoCanLoadFilter hooks, and space, Cargo.cs:279-289.) Restricting the
				// scan also keeps the census off enemy cargo — the fog-legality constraint is that this pass
				// reads the player's OWN force, not hidden enemy state.
				if (transport.Owner != player && player.RelationshipWith(transport.Owner) != PlayerRelationship.Ally)
					continue;

				// NOTE: passengers are deliberately NOT IsInWorld-checked — being out of the world is precisely
				// what makes them invisible to the main census loop.
				foreach (var passenger in pair.Trait.Passengers)
				{
					if (passenger.Owner != player || passenger.IsDead)
						continue;

					var slot = Array.IndexOf(compositionTypes, passenger.Info.Name);
					if (slot < 0)
						continue;

					if (hasExclude && passenger.GetConditionCount(excludeCondition) > 0)
						continue;

					values[slot] += UnitCost(passenger.Info);
				}
			}
		}

		void CreditRequests(int[] values, List<string> requests)
		{
			foreach (var request in requests)
			{
				var slot = Array.IndexOf(compositionTypes, request);
				if (slot >= 0 && world.Map.Rules.Actors.TryGetValue(request, out var requestInfo))
					values[slot] += UnitCost(requestInfo);
			}
		}

		// Believed enemy value per class -> per-mille shares -> integer EMA. FOG-LEGAL: the belief store is the
		// player's own memory of what it has legally seen (never ground truth). Recomputed at most once per world
		// tick so the two BuildUnit calls in one BotTick cycle share one EMA step instead of double-advancing it.
		int[] UpdateThreatShares()
		{
			if (!beliefStoreResolved)
			{
				beliefStoreResolved = true;
				beliefStore = world.WorldActor.TraitOrDefault<BeliefStore>();
			}

			if (beliefStore == null || world.WorldTick == lastThreatTick)
				return smoothedThreatShares;

			lastThreatTick = world.WorldTick;

			var values = new int[ThreatClasses.Length];
			foreach (var c in beliefStore.Contacts(player))
			{
				if (!world.Map.Rules.Actors.TryGetValue(c.TypeName, out var ai))
					continue;

				var value = UnitCost(ai) * c.Confidence / 100;
				if (value <= 0)
					continue;

				var cls = ClassifyBelievedContact(ai);
				if (cls >= 0)
					values[cls] += value;
			}

			smoothedThreatShares = ForceCompositionMath.SmoothShares(smoothedThreatShares,
				ForceCompositionMath.SharesPerMille(values), Info.ThreatSmoothingAlphaPct);

			return smoothedThreatShares;
		}

		// Index into ThreatClasses, or -1 for "not a mobile threat". Classify by the ENEMY unit's own type, not
		// by what its weapon can target — so an attack helicopter is AIR (answered by AA), never ground.
		// Mirrors AdaptiveProductionBotModule.ClassifyContact (private there; the two must stay in step).
		static int ClassifyBelievedContact(ActorInfo ai)
		{
			if (ai.HasTraitInfo<AircraftInfo>())
				return 0; // air

			if (!ai.HasTraitInfo<MobileInfo>())
				return -1; // structures / immobile

			return ai.HasTraitInfo<Render.WithInfantryBodyInfo>() ? 2 : 1; // infantry : armor
		}

		// A slot is eligible only if the buy would ACTUALLY go through — every gate BuildUnit applies after the
		// pick is checked here too, so an ineligible type is skipped rather than silently wasting the cycle
		// (BuildUnit's post-pick gates `return` without buying anything).
		bool IsCompositionCandidateEligible(string name, HashSet<string> buildableNames, long budget)
		{
			if (!buildableNames.Contains(name))
				return false;

			if (!world.Map.Rules.Actors.TryGetValue(name, out var actorInfo))
				return false;

			if (Info.UnitsToBuild != null && !Info.UnitsToBuild.ContainsKey(name))
				return false;

			if (Info.UnitDelays != null && Info.UnitDelays.TryGetValue(name, out var delay) && delay > world.WorldTick)
				return false;

			if (Info.UnitLimits != null && Info.UnitLimits.TryGetValue(name, out var limit) &&
				world.Actors.Count(a => a.Owner == player && a.Info.Name == name) >= limit)
				return false;

			if (Info.GateResupplyOnAmmoNeed && Info.ResupplyUnitTypes.Contains(name)
				&& !SupplyFleetUnderDesired(name) && !AnyFieldedUnitNeedsResupply())
				return false;

			if (Info.ScaleAntiAirToThreat && Info.AntiAirUnitTypes.Contains(name) && !ShouldBuildMoreAntiAir())
				return false;

			if (Info.GateTransportOnDemand && Info.TransportUnitTypes.Contains(name) && !ShouldBuyTransport(actorInfo))
				return false;

			if (!CompositionNeedMath.Affordable(budget, UnitCost(actorInfo), 100))
				return false;

			return HasAdequateAirUnitReloadBuildings(actorInfo);
		}

		// Observability channel: one line per composition-directed selection, so autotest log-mining can chart
		// the standing composition against its targets without a benchmark run. Top 3 by census share — the
		// over-accumulating types are exactly the ones that need to show up in the chart.
		void LogCompositionChoice(string chosen, int deficit, int[] census, int[] targets)
		{
			var top = Enumerable.Range(0, compositionTypes.Length)
				.OrderByDescending(i => census[i])
				.ThenBy(i => i)
				.Take(3)
				.Select(i => compositionTypes[i] + " " + census[i] + "/" + targets[i]);

			AIUtils.BotDebug("{0} composition-directed buy: {1} (deficit {2}) [top {3}]",
				player, chosen, deficit, string.Join(", ", top));
		}

		// WHICH LANE ORDERED THIS, on the same unconditional channel as the census.
		//
		// The census answers "what does the bot own"; it CANNOT answer "why did it buy that", and the two
		// questions have different answers when several lanes can order the same type. Every existing pick log
		// is AIUtils.BotDebug, which is default-off AND routes to game chat rather than debug.log — so the
		// selection itself has never been observable in a log at all. That gap cost this branch a wrong
		// conclusion: the offline replay showed the medic opening fixed while a live match still opened with
		// medics, and with no lane tag there was no way to tell which of the procurement lanes disagreed with
		// the replay.
		//
		// Gated on CensusLogInterval so it shares the census's opt-in and stays silent for @stable.
		void LogPick(string lane, string type, string detail)
		{
			if (Info.CensusLogInterval <= 0)
				return;

			Log.Write("debug", $"[composition] pick tick={world.WorldTick} player={player.InternalName} "
				+ $"lane={lane} type={type} {detail}");
		}

		long AvailableBudget()
		{
			var res = player.PlayerActor.TraitOrDefault<PlayerResources>();
			return res != null ? (long)res.Cash + res.Resources : 0;
		}

		static int UnitCost(ActorInfo ai)
		{
			var valued = ai.TraitInfoOrDefault<ValuedInfo>();
			return valued?.Cost ?? 0;
		}

		// For mods like RA (number of RearmActors must match the number of aircraft).
		// WW3MOD: Aircraft are called in via Supply Route and don't need a dedicated
		// pad to be produced — SkipRearmBuildingCheck bypasses this for reinforcement-model mods.
		bool HasAdequateAirUnitReloadBuildings(ActorInfo actorInfo)
		{
			if (Info.SkipRearmBuildingCheck)
				return true;

			var aircraftInfo = actorInfo.TraitInfoOrDefault<AircraftInfo>();
			if (aircraftInfo == null)
				return true;

			// If actor isn't Rearmable, it doesn't need a RearmActor to reload
			var rearmableInfo = actorInfo.TraitInfoOrDefault<RearmableInfo>();
			if (rearmableInfo == null)
				return true;

			var countOwnAir = AIUtils.CountActorsWithNameAndTrait<IPositionable>(actorInfo.Name, player);
			var countBuildings = rearmableInfo.RearmActors.Sum(b => AIUtils.CountActorsWithNameAndTrait<Building>(b, player));
			if (countOwnAir >= countBuildings)
				return false;

			return true;
		}

		List<MiniYamlNode> IGameSaveTraitData.IssueTraitData(Actor self)
		{
			if (IsTraitDisabled)
				return null;

			var data = new List<MiniYamlNode>()
			{
				new MiniYamlNode("QueuedBuildRequests", FieldSaver.FormatValue(queuedBuildRequests.ToArray())),
				new MiniYamlNode("IdleUnitCount", FieldSaver.FormatValue(idleUnitCount))
			};

			// Composition-directed EMA state. Only emitted when the flag is on, so a frozen-profile save is
			// byte-identical to before. Reloading without it would restart the threat smoothing from zero and
			// briefly un-bias the targets.
			if (smoothedThreatShares != null)
				data.Add(new MiniYamlNode("SmoothedThreatShares", FieldSaver.FormatValue(smoothedThreatShares)));

			return data;
		}

		void IGameSaveTraitData.ResolveTraitData(Actor self, MiniYaml data)
		{
			if (self.World.IsReplay)
				return;

			var queuedBuildRequestsNode = data.Nodes.FirstOrDefault(n => n.Key == "QueuedBuildRequests");
			if (queuedBuildRequestsNode != null)
			{
				queuedBuildRequests.Clear();
				queuedBuildRequests.AddRange(FieldLoader.GetValue<string[]>("QueuedBuildRequests", queuedBuildRequestsNode.Value.Value));
			}

			var idleUnitCountNode = data.Nodes.FirstOrDefault(n => n.Key == "IdleUnitCount");
			if (idleUnitCountNode != null)
				idleUnitCount = FieldLoader.GetValue<int>("IdleUnitCount", idleUnitCountNode.Value.Value);

			// Restore only into a live composition array — a save written with the flag on must not resurrect
			// state on a profile that has it off (the arrays stay null there and nothing reads them anyway).
			var threatSharesNode = data.Nodes.FirstOrDefault(n => n.Key == "SmoothedThreatShares");
			if (threatSharesNode != null && smoothedThreatShares != null)
			{
				var restored = FieldLoader.GetValue<int[]>("SmoothedThreatShares", threatSharesNode.Value.Value);
				if (restored != null && restored.Length == smoothedThreatShares.Length)
					smoothedThreatShares = restored;
			}
		}
	}
}
