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
using OpenRA.Mods.Common.Pathfinder;
using OpenRA.Mods.Common.Traits.BotModules.Squads;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Orders supply trucks to follow attack squads and resupply units in the field.")]
	public class SupplyFollowerBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Actor types that are supply trucks.")]
		public readonly HashSet<string> SupplyTruckTypes = new HashSet<string>();

		[Desc("Delay (in ticks) between supply follow-up scans.")]
		public readonly int ScanInterval = 120;

		[Desc("Maximum distance in cells a truck will travel to follow a squad.")]
		public readonly int MaxFollowDistance = 40;

		[Desc("Minimum number of friendly units near a location to consider it worth following.")]
		public readonly int MinNearbyFriendlies = 3;

		[Desc("Influence stack Stage E: consume the per-player ANTI-GROUND danger field (DangerFieldLayer)",
			"so a supply truck relocating along the front does NOT drive point-to-point through the danger —",
			"it detours toward the safer side, and because the Stage-C territory baseline makes deep enemy",
			"ground expensive while the friendly rear reads ~0, the pull-back / lateral / re-enter path EMERGES",
			"from the cost rather than being scripted. Emits a two-leg Move (safe waypoint, then the follow",
			"cell). This module is enable-ai-ANY, so the reroute is additionally gated on",
			"InfluenceStack.Participates — which admits BOTH fog-respecting profiles (@experimental and,",
			"since the 2026-08-02 parity promotion, @stable); only Normal/Rush/Turtle and legacy are",
			"byte-identical. OFF by default; the @supply instance opts in via YAML.")]
		public readonly bool DangerFieldRouting = false;

		[Desc("Stage-E: path ground-danger above which a truck's relocation is rerouted via safer depth.",
			"Lower than the offensive threshold — a non-combatant should avoid even moderate exposure.")]
		public readonly int GroundDangerSafeThreshold = 15;

		[Desc("Stage-E: lateral offset magnitude (cells) for the truck's rear-lateral detour waypoint.")]
		public readonly int GroundDangerDetourCells = 8;

		[Desc("Stage-E: how many lateral steps (× GroundDangerDetourCells) the detour search may probe —",
			"a larger budget lets a high-value mover route deeper into the safe rear.")]
		public readonly int GroundDangerDetourSteps = 3;

		[Desc("Stage-E deadband (cells): re-issue a truck's two-leg detour only when the recomputed",
			"waypoint shifts by at least this much. Since the detour is recomputed from the MOVING truck",
			"each scan, without this the waypoint recedes and the maneuver restarts before it completes.")]
		public readonly int RepathThresholdCells = 3;

		[Desc("Sector spread: when several trucks are free, greedily assign each to a DISTINCT unit cluster",
			"(neediest first) instead of every truck piling onto the same blob; only double up when trucks",
			"outnumber clusters. This is a shared enable-ai-ANY module, so it is additionally gated on",
			"InfluenceStack.Participates — which admits @stable as well as @experimental. OFF by default; only",
			"Normal/Rush/Turtle and legacy profiles are byte-identical.")]
		public readonly bool SectorSpread = false;

		[Desc("Small-squad coverage: lower the servable-cluster floor to SmallSquadMinNearbyFriendlies (below",
			"MinNearbyFriendlies) so small squads become visible to the follower once the big clusters are",
			"covered. OFF by default; gated on InfluenceStack.Participates (both fog-respecting profiles).")]
		public readonly bool SmallSquadCoverage = false;

		[Desc("Minimum friendlies to form a servable cluster when SmallSquadCoverage is on. Only applied for",
			"participating (fog-respecting bot / human) profiles; capped at MinNearbyFriendlies so it only widens.")]
		public readonly int SmallSquadMinNearbyFriendlies = 2;

		[Desc("Danger evac: when the believed ground danger at the truck (or its target cluster centroid)",
			"reaches EvacDangerThreshold, retreat the truck toward its Supply Route instead of idling in the",
			"fire. Fog-legal — reads DangerFieldLayer only, never an omniscient enemy scan. OFF by default;",
			"gated on InfluenceStack.Participates, which since the 2026-08-02 @stable parity promotion admits",
			"@stable as well as @experimental — so this is NOT @experimental-only, and the damper below is",
			"therefore load-bearing on both fog-respecting profiles.")]
		public readonly bool DangerEvac = false;

		[Desc("Danger-evac: believed ground-danger reading at/above which a truck pulls back. Set ABOVE the",
			"Stage-E reroute threshold — a reroute avoids exposure, an evac abandons a spot already too hot.",
			"Doubles as the SELECTION filter: a cluster whose centroid is at/above this never becomes a follow",
			"target, so the module cannot pick a cluster it is about to refuse to approach.")]
		public readonly int EvacDangerThreshold = 60;

		[Desc("Danger-evac: how far (cells) to pull the truck back toward its Supply Route when evacuating.")]
		public readonly int EvacRetreatCells = 12;

		[Desc("Danger-evac damper: scans an evacuating truck holds the evac decision before the branch may be",
			"re-decided. Sized so the branch is not flipped mid-leg: at TRUK's speed a 12-cell retreat is",
			"~164 ticks plus the acceleration ramp, against a 150-tick scan — so one held scan puts the",
			"re-decision at the first boundary AFTER the leg completes, and a larger value only delays the",
			"truck's return to useful work. (Bounding the retreat DISTANCE is a separate mechanism — see",
			"StepEvac's leg model — so this does not need to cover the drive.) ENTERING an evac is never",
			"delayed by this; only the return to following is. 0 disables the dwell.")]
		public readonly int EvacDwellScans = 1;

		[Desc("Danger-evac: how far below EvacDangerThreshold the danger must fall before an evacuating truck",
			"follows again; also sets the level SELECTION is gated at, so both sides use one number. Clamped",
			"so the release level is never below 1.",
			"HONEST SCOPE — this is NOT what stabilises the loop, despite reading like a classic Schmitt",
			"deadband. The danger field steps by tens to hundreds per cell near a contact (see GroundDangerAt),",
			"so a 15-unit band is narrower than one cell: the truck crosses the whole 45..60 span in a single",
			"step and the band is never dwelt in. What actually damps the oscillation is EvacDwellScans (the",
			"branch cannot be re-decided mid-leg) and the leg model in StepEvac (the retreat is not re-issued",
			"until it has been driven). Tuning this knob will not visibly change stability; it moves the",
			"geometric contour the truck treats as the edge of the contact, which is a different lever.")]
		public readonly int EvacReleaseHysteresis = 15;

		[Desc("Actor types that count as the player's own Supply Route. Read when DangerEvac is on (the safe",
			"rear an evacuating truck pulls back toward) AND when DropAndLeave is on (the seed the forward",
			"supply point's frontier descent starts from).")]
		public readonly HashSet<string> SupplyRouteTypes = new() { "supplyroute" };

		[Desc("TIER 2 (@experimental) IDLE-TRUCK HUNT. The follow path above only tasks a truck that has a",
			"CLUSTER to follow (MinNearbyFriendlies-strong); a truck with none parks and waits for units to",
			"wander into its aura. Tier 1 gave the infantry the legs (AutoSeekSupplies walks a dry soldier to",
			"a truck) — this closes the other half: an unassigned truck drives to the neediest STARVING",
			"soldier inside HuntLeashCells. INFANTRY ONLY, by construction rather than by a name list — the",
			"candidate must carry the truck's own RearmCondition (replenish-soldiers), which only soldiers",
			"hold; vehicles pull from the static Logistics Centre instead (replenish-vehicles, docked). No",
			"in-leash demand ⇒ no order ⇒ the truck stays put, so there is no cross-map wandering. Decision is",
			"the pure SupplyTruckHuntMath (NUnit-pinned), zero RNG. OFF by default; and because this is a",
			"SHARED enable-ai-any module whose Participates gate now admits @stable too, the flag is",
			"additionally confined to the @experimental player by an explicit BotType gate — @stable / Normal",
			"/ legacy take the identical old path and are byte-identical.")]
		public readonly bool IdleTruckHunt = false;

		[Desc("Idle-truck hunt: a soldier whose ammo pool sits below this many parts per thousand of capacity",
			"counts as starving (250 = 25%). Matches AutoSeekSupplies.AutoSeekAmmoThresholdPerMille so the",
			"truck and the soldier agree on who needs help. Only read when IdleTruckHunt is on.")]
		public readonly int HuntStarvingThresholdPerMille = 250;

		[Desc("Idle-truck hunt: furthest (cells, straight-line) a starving soldier can be and still be worth",
			"driving to. This is the bound on the sweep — same leash metric as AutoSeekSupplies, not a",
			"parallel one. Only read when IdleTruckHunt is on.")]
		public readonly int HuntLeashCells = 20;

		[Desc("Idle-truck hunt: shortfall band width in parts per thousand. Needs within one band tie and",
			"DISTANCE decides, so a landed ammo pip can't make the truck re-target across the sector every",
			"scan. 0 or 1 disables banding (raw shortfall order). Only read when IdleTruckHunt is on.")]
		public readonly int HuntNeedBandPerMille = 100;

		[Desc("DROP-AND-LEAVE. A loaded truck drives ONCE to a forward supply point behind the believed",
			"friendly frontier, unloads its whole stock as a SUPPLYCACHE, and leaves; infantry walk to the",
			"cache via AutoSeekSupplies. This is an ADDED MODE, not a replacement — a truck that does not",
			"meet the drop conditions follows exactly as before.",
			"WHY, rather than more damping of the follow/evac cycle: that cycle is a limit cycle by",
			"CONSTRUCTION (the relief valve re-selects the same needy cluster the moment the truck cools), and",
			"the pull side has the mirror defect (AutoSeekSupplies leashes at selection only and then rides an",
			"actor-tracking move, so infantry at speed 25 chase a truck at 75 without bound). Both are the same",
			"shape and both dissolve against a destination that DOES NOT MOVE. Damping bounds an excursion; a",
			"static destination removes it.",
			"SCOPE: gated on InfluenceStack.Participates as well as this flag, because the anchor is walked",
			"down ControlField's frontier-distance field and that field exists only for participating players.",
			"That admits BOTH fog-respecting profiles (@experimental and @stable); Normal/Rush/Turtle and",
			"legacy keep a flat field, the descent returns the Supply Route unchanged, no anchor is",
			"established and the mode is inert — so they stay byte-identical without needing a second gate.")]
		public readonly bool DropAndLeave = false;

		[Desc("Drop-and-leave: how many COARSE control-grid cells short of the believed enemy frontier the",
			"forward supply point sits. This is the knob that manages the risk the SUPPLYCACHE is designed to",
			"carry — the crate is ProximityCapturable by infantry, vehicles and tanks and is shot at unaided,",
			"so a point too far forward gifts the enemy a resupply node. Deliberately LARGER than",
			"PoiOffensiveBotModule's StagingStandoffCells (a supply dump stands off further than a rifle",
			"squad), and deliberately smaller than AutoSeekSupplies' 20-cell selection leash in map cells, or",
			"the soldiers it exists for could never select it.")]
		public readonly int DropStandoffCells = 8;

		[Desc("Drop-and-leave: believed ground danger above which the anchor descent refuses to step into a",
			"cell. Matches PoiOffensiveBotModule's StagingDangerSafeThreshold on purpose: it is the same",
			"primitive doing the same job, and the STANDOFF above — not this number — is the lever that keeps",
			"the supply point further back than a staging area. A negative value disables the guard.")]
		public readonly int DropDangerSafeThreshold = 40;

		[Desc("Drop-and-leave: step budget for the anchor's steepest-descent walk down the frontier-distance",
			"field. Frontier distance strictly decreases per accepted step, so this only bounds work.")]
		public readonly int DropMaxDescentSteps = 64;

		[Desc("Drop-and-leave: MAP-cell Chebyshev hysteresis on the forward supply point. The anchor is",
			"re-derived every scan from a field that is rebuilt every 25 ticks, so without this a one-cell",
			"belief wobble would move the destination — and a destination that moves is the entire defect this",
			"mode exists to remove. Non-positive re-adopts every scan (no hysteresis).")]
		public readonly int DropAnchorHysteresisCells = 3;

		[Desc("Drop-and-leave: radius in MAP cells around the anchor searched for both halves of the decision —",
			"the starving soldiers that justify a drop, and the existing caches that make one redundant. Sized",
			"to AutoSeekSupplies' SupplyHuntLeashCells: a soldier further from the crate than its own selection",
			"leash cannot pick it, so counting him as demand would drop a crate he will never walk to.")]
		public readonly int DropDemandRadiusCells = 20;

		[Desc("Scans between roll-up lines while an SR's frontier descent keeps producing an unreachable cell.",
			"The first rejection and the recovery are always logged; this only bounds the noise in between,",
			"WITHOUT losing the count (the roll-up carries it), because the frequency of bad descents is the",
			"measurement those lines exist to take. 0 disables the roll-up (first + recovery only).")]
		public readonly int AnchorRejectRollupScans = 10;

		[Desc("Drop-and-leave: shrink the DEMAND search by this many cells (the redundancy search keeps the full",
			"radius). The crate lands up to DropsSupplyCache.DropAtToleranceCells off the anchor, so a soldier",
			"counted at exactly the radius could end up beyond his own selection leash from the crate that was",
			"dropped for him — which is the very thing sizing the radius to that leash was meant to prevent.",
			"Asymmetric on purpose: strict about what counts as demand, generous about what counts as already",
			"covered. Keep at/above DropsSupplyCache.DropAtToleranceCells.")]
		public readonly int DropDemandMarginCells = 2;

		[Desc("Drop-and-leave: how many starving soldiers must be within the (margin-reduced) demand radius of",
			"the anchor to justify unloading. Floored at 1 by SupplyDropMath — 0 cannot mean 'no requirement'.")]
		public readonly int DropMinStarvingUnits = 3;

		[Desc("Drop-and-leave: minimum stock a truck must hold to be worth a drop. Below this it keeps serving",
			"from its own aura instead of littering crates that vanish at the cache's RemoveBelowSupply.")]
		public readonly int DropMinSupply = 250;

		[Desc("Drop-and-leave: supply at or above which the demand counts as covered and no further crate is",
			"dropped, counting BOTH crates on the ground within DropDemandRadiusCells AND the loads of trucks",
			"already dispatched to this anchor. The in-flight half is not an optimisation: trucks are evaluated",
			"against unchanged world state in one loop, so without it the whole fleet passes on the same scan",
			"and unloads at one cell. NOTE this gate carries the entire anti-stacking job alone — SUPPLYCACHE",
			"is a Building with Footprint: x (a BLOCKED cell), so a truck can never stand on a cache and",
			"DropSupplyCacheHere's merge branch is unreachable; nothing coalesces crates. Non-positive DISABLES",
			"the gate — it is not floored, because the literal reading of 0 ('any cache supply is redundant')",
			"would be permanently true and silently disable the whole mode, which looks like a broken feature",
			"rather than a config typo.")]
		public readonly int DropRedundantCacheSupply = 100;

		[Desc("Emit the per-scan [supply] diagnostic lines (scan summary, anchor descent, per-cluster",
			"selection, and the reason a drop was declined). Default OFF so an ordinary match does not flood",
			"debug.log. EDGES — truck adopted/released, evac entered/left, a drop issued — are logged",
			"REGARDLESS, because they are rare and they are exactly what this subsystem had no record of.")]
		public readonly bool DebugLogging = false;

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			base.RulesetLoaded(rules, ai);

			// Case-harden actor-name config (see ActorNameCase). SupplyRouteTypes ships a lowercase default
			// but IS overridable from YAML, and a mis-cased override there fails SILENTLY and expensively:
			// FindOwnSupplyRoutes matches nothing, so the whole evac path — damper included — is skipped
			// while the danger gate on selection keeps running.
			ActorNameCase.NormalizeInPlace(SupplyTruckTypes);
			ActorNameCase.NormalizeInPlace(SupplyRouteTypes);

			// CROSS-TRAIT ARITHMETIC THAT NOTHING ELSE ENFORCES. The drop is only useful if a soldier counted
			// as demand can still SELECT the crate dropped for him, and that holds by exactly zero margin:
			// demand is counted at (DropDemandRadiusCells - DropDemandMarginCells), the crate lands up to
			// DropAtToleranceCells off the anchor, and AutoSeekSupplies' leash admits the sum inclusively.
			// At the shipped values 18 + 2 = 20 <= 20 — correct, with nothing to spare.
			//
			// The two knobs live on DIFFERENT TRAITS ON DIFFERENT ACTORS (this module on the player; the
			// tolerance on the truck), so no single YAML block shows the relation and no existing validation
			// spans them. Raising the tolerance to 3 makes it 18 + 3 = 21 > 20 and the mode degrades in the
			// most expensive possible way: it still drops, and nobody collects. Warn rather than clamp — the
			// right value depends on the leash, which lives on a third actor again, so silently "fixing" one
			// side would just move the surprise.
			if (!DropAndLeave)
				return;

			foreach (var truckType in SupplyTruckTypes)
			{
				if (!rules.Actors.TryGetValue(truckType, out var truckInfo))
					continue;

				var dropInfo = truckInfo.TraitInfoOrDefault<DropsSupplyCacheInfo>();
				if (dropInfo == null)
					continue;

				if (DropDemandMarginCells < dropInfo.DropAtToleranceCells)
					Log.Write("debug",
						$"[supply] CONFIG WARNING: DropDemandMarginCells ({DropDemandMarginCells}) is below "
						+ $"{truckType}'s DropAtToleranceCells ({dropInfo.DropAtToleranceCells}). A crate can land "
						+ "further from the anchor than the demand search was shrunk by, so a soldier counted as "
						+ "demand can end up outside his own AutoSeekSupplies leash from the crate dropped for "
						+ "him — the drop still happens and nobody collects it.");
			}
		}

		public override object Create(ActorInitializer init) { return new SupplyFollowerBotModule(init.Self, this); }
	}

	public class SupplyFollowerBotModule : ConditionalTrait<SupplyFollowerBotModuleInfo>, IBotTick, IBotEnabled
	{
		readonly World world;
		readonly Player player;

		IBot bot;
		ThreatMapManager threatMap;
		BotBlackboard blackboard;
		DangerFieldLayer dangerField;

		// Read for its grid GEOMETRY only (GroundDangerAt's lattice de-aliasing), never for control scores.
		ControlField controlField;
		int scanCountdown;
		bool initialized;

		// Cached in Initialize: whether this player reads the influence stack, and whether the Stage-E two-leg
		// reroute is live. NOTE `participates` is NOT an @experimental-only gate — since the 2026-08-02 @stable
		// parity promotion (b8d2e601) InfluenceStack.Participates admits BOTH fog-respecting bot profiles
		// (InfluenceStack.cs:47-48), so a flag gated on it alone reaches @stable too. Only Normal/Rush/Turtle
		// and legacy profiles stay byte-identical. Behaviour that must be @experimental-only needs the explicit
		// BotType gate below instead.
		bool participates;
		bool routeViaDanger;

		// Tier 2 idle-truck hunt. Participates is NOT enough to confine this one: it admits @stable since the
		// 0802 promotion (ai.yaml:1335-1337), and this module is a single shared instance. Cached BotType gate,
		// same seam GarrisonBotModule uses for its shared-instance commit (GarrisonBotModule.cs:102).
		bool isExperimentalBot;

		// Track which trucks are assigned to follow duty
		readonly HashSet<Actor> activeTrucks = new HashSet<Actor>();

		// Stage-E: last detour waypoint ordered per truck (absent = last order went direct). Drives the
		// re-issue deadband so a truck mid-detour isn't restarted every scan as its waypoint recedes.
		readonly Dictionary<Actor, CPos> lastVia = new Dictionary<Actor, CPos>();

		// The follow cell each truck was last SENT to — the deadband memory for the plain follow Move,
		// which until now re-issued a cancelling Move to a recomputed moving centroid every single scan.
		// Voided (not merely overwritten) by every branch that takes the truck away from following, the
		// same way lastVia is, so a stale record can never suppress the re-issue that restarts it.
		readonly Dictionary<Actor, CPos> lastFollow = new Dictionary<Actor, CPos>();

		// Danger-evac damper state per truck: whether it is currently on the evac branch, and how many scans
		// that decision is still committed for. Absent = following, no dwell. Read only for the truck being
		// processed (never enumerated for a decision), so it adds no ordering dependence.
		readonly Dictionary<Actor, EvacState> evacState = new Dictionary<Actor, EvacState>();

		// Drop-and-leave: the last ADOPTED forward supply point per Supply Route, which is what the anchor
		// hysteresis is applied against. Keyed by SR rather than held as one value because a player can hold
		// several (the beachhead plus any captured neutral ones) and each has its own descent toward its own
		// nearest front — one shared slot would thrash between them. Pruned to living SRs each scan.
		//
		// This is the mode's ONLY memory, and note what it is memory OF: it stabilises the DESTINATION, not
		// the decision. The drop decision itself is memoryless and re-derived from scratch every scan (see
		// SupplyDropMath), so there is no "already dropping" latch that could pin a branch while reading a
		// term that cannot respond to it.
		readonly Dictionary<Actor, CPos> dropAnchor = new Dictionary<Actor, CPos>();

		// Consecutive scans each SR's frontier descent has produced an unreachable cell. Purely an
		// instrumentation counter — nothing reads it for a decision, so it adds no ordering dependence.
		// Absent = the last descent for that SR was fine.
		readonly Dictionary<Actor, int> anchorRejectStreak = new Dictionary<Actor, int>();

		// Drop-and-leave: the cell each truck was last DISPATCHED to unload at. This is memory of the ORDER,
		// not of the decision — the same distinction the dropAnchor comment above draws — and it is one map
		// serving two jobs deliberately, so they cannot drift apart:
		//   * SUPPRESS RE-ISSUE. The errand is issued non-queued, so re-issuing cancels the running activity
		//     and destroys its queued unload/restock tail before rebuilding it — a pathfind and up to a cell
		//     of backslide every scan, on a unit whose whole job is to arrive.
		//   * COUNT COMMITTED SUPPLY. Summing the loads of trucks recorded against an anchor is what stops
		//     the entire fleet passing the redundancy gate on the same scan and unloading at one cell.
		// Keyed by TARGET CELL rather than a boolean, so an anchor that moves re-issues by itself and there
		// is no "still valid?" flag to go stale. The caller must still CLEAR the record wherever something
		// else cancels the errand (the evac branch) or the decision is withdrawn (the revoke path), and it is
		// pruned each scan against the freshly-derived eligible-truck list rather than against activeTrucks —
		// a truck that is not eligible this scan cannot have a live errand of ours.
		readonly Dictionary<Actor, CPos> dropTarget = new Dictionary<Actor, CPos>();

		public SupplyFollowerBotModule(Actor self, SupplyFollowerBotModuleInfo info)
			: base(info)
		{
			world = self.World;
			player = self.Owner;
		}

		void IBotEnabled.BotEnabled(IBot bot)
		{
			this.bot = bot;
		}

		void Initialize()
		{
			if (initialized)
				return;

			threatMap = world.WorldActor.TraitOrDefault<ThreatMapManager>();
			blackboard = player.PlayerActor.TraitsImplementing<BotBlackboard>().FirstOrDefault(b => !b.IsTraitDisabled);

			// enable-ai-ANY module: cache the participation gate once. See the field comment — this admits
			// @stable as well as @experimental, so it narrows to "the fog-respecting profiles", not to
			// @experimental alone.
			participates = InfluenceStack.Participates(player);

			// Tier 2 hunt: explicit bot-type gate (see the field comment) — never widened to Participates.
			isExperimentalBot = player.BotType == InfluenceStack.ExperimentalBotType;

			// Fetch the ground danger field if any believed-danger consumer (Stage-E reroute or danger evac) is
			// active. With DangerEvac at its default off, this is exactly the old condition.
			dangerField = participates && (Info.DangerFieldRouting || Info.DangerEvac || Info.DropAndLeave)
				? world.WorldActor.TraitOrDefault<DangerFieldLayer>() : null;

			// Grid geometry for GroundDangerAt's de-aliasing, and — for drop-and-leave — the frontier-distance
			// field the forward supply point is walked down. Null is tolerated (raw single-cell reads; and no
			// anchor, so the drop mode is inert).
			controlField = dangerField != null ? world.WorldActor.TraitOrDefault<ControlField>() : null;

			// The Stage-E two-leg reroute stays the old condition (DangerFieldRouting + a live field), so
			// enabling DangerEvac alone never flips a truck onto the reroute path.
			routeViaDanger = Info.DangerFieldRouting && dangerField != null;

			initialized = true;

			// One line per player per match recording which of this module's modes are actually LIVE. Every
			// flag here is double-gated on a runtime trait lookup, so the YAML alone does not tell you what
			// ran — which is why two days of reasoning about truck behaviour had no way to check its premise.
			Log.Write("debug",
				$"[supply] init player={player.PlayerName} bot={player.BotType} participates={participates} "
				+ $"exp={isExperimentalBot} dangerField={dangerField != null} controlField={controlField != null} "
				+ $"evac={Info.DangerEvac && dangerField != null} reroute={routeViaDanger} "
				+ $"spread={Info.SectorSpread && participates} hunt={SupplyTruckHuntMath.ShouldHunt(Info.IdleTruckHunt, isExperimentalBot)} "
				+ $"drop={Info.DropAndLeave && controlField != null}");
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (--scanCountdown > 0)
				return;

			scanCountdown = Info.ScanInterval;
			Initialize();

			// Clean up dead trucks, and low-supply ones (see IsLowOnSupply — they are released because they
			// have nothing left to give, NOT because a restock is waiting to claim them). The blackboard
			// claim is handed back with them: dropping a truck from the roster while keeping its claim left
			// it alive-and-claimed forever, invisible to every other claim-respecting module (the 2026-08-04
			// entry in WORKSPACE/bugs/discovered.md — the same defect fixed in GarrisonBotModule this pass).
			var dropped = activeTrucks
				.Where(a => a == null || a.IsDead || !a.IsInWorld || IsLowOnSupply(a))
				.ToList();

			foreach (var a in dropped)
			{
				activeTrucks.Remove(a);
				if (a != null && blackboard != null && blackboard.IsUnitClaimedBy(a, "supply-follow"))
					blackboard.ReleaseUnit(a);

				// Lifecycle EDGE — unconditional. "Where did the truck go?" is the question this subsystem
				// could never answer, and a release is the moment it stops being ours: from here
				// DropsSupplyCache owns it, and under TRUK's Evacuate default that means the map edge.
				if (a != null)
					Log.Write("debug",
						$"[supply] release truck={a.ActorID}@{(a.IsInWorld ? a.Location.ToString() : "<out-of-world>")} "
						+ $"reason={(a.IsDead ? "dead" : !a.IsInWorld ? "out-of-world" : "low-supply")} "
						+ $"supply={a.TraitOrDefault<SupplyProvider>()?.CurrentSupply.ToString() ?? "n/a"}");
			}

			// Keep the per-truck deadband / damper memory bounded to trucks still on active follow duty.
			if (lastVia.Count > 0)
			{
				var stale = lastVia.Keys.Where(a => !activeTrucks.Contains(a)).ToList();
				foreach (var a in stale)
					lastVia.Remove(a);
			}

			if (lastFollow.Count > 0)
			{
				var stale = lastFollow.Keys.Where(a => !activeTrucks.Contains(a)).ToList();
				foreach (var a in stale)
					lastFollow.Remove(a);
			}

			if (evacState.Count > 0)
			{
				var stale = evacState.Keys.Where(a => !activeTrucks.Contains(a)).ToList();
				foreach (var a in stale)
					evacState.Remove(a);
			}

			// Find all supply trucks — eligible only if they actually have supplies to give. An empty truck
			// driven forward just arrives at the front with nothing, so filter them out.
			//
			// CORRECTION (2026-08-07): this filter used to be justified by "SupplyProvider auto-restocks by
			// queuing a MoveTo(LC) and a forward Move would cancel it". That is FALSE for AI trucks. Every
			// TryRestock call site is gated on ShouldSelfRestock (SupplyProvider.cs:330-338), which returns
			// false under ResupplyBehavior.Evacuate — and TRUK ships InitialResupplyBehaviorAI: Evacuate
			// (vehicles.yaml:514-516). There is no restock activity to protect here; what actually picks a
			// released truck up is DropsSupplyCache, which drives it to the MAP EDGE and sells it. The filter
			// is still right, but for the plain reason above, not the one it used to claim.
			var trucks = world.ActorsHavingTrait<Mobile>()
				.Where(a => a.Owner == player
					&& !a.IsDead
					&& a.IsInWorld
					&& Info.SupplyTruckTypes.Contains(a.Info.Name)
					&& !IsClaimedByOtherModule(a)
					&& !IsLowOnSupply(a))
				.ToList();

			// Prune the dispatch record against the trucks that are ELIGIBLE this scan, not against
			// activeTrucks. A truck that dropped is low on supply and gone from this list; one claimed by
			// another module is gone too; either way an errand of ours is no longer running, and a record
			// that outlived its errand would suppress the re-issue that should restart it.
			if (dropTarget.Count > 0)
			{
				var staleTargets = dropTarget.Keys.Where(a => !trucks.Contains(a)).ToList();
				foreach (var a in staleTargets)
					dropTarget.Remove(a);
			}

			if (trucks.Count == 0)
				return;

			// Find clusters of friendly combat units that might need supply
			var friendlyUnits = world.ActorsHavingTrait<Mobile>()
				.Where(a => a.Owner == player && !a.IsDead && a.IsInWorld && !Info.SupplyTruckTypes.Contains(a.Info.Name))
				.ToList();

			if (friendlyUnits.Count == 0)
				return;

			// @experimental small-squad coverage widens the servable-cluster floor so small squads (< the
			// default 4) become visible once big clusters are covered. Capped at MinNearbyFriendlies so it only
			// ever widens; off / non-participant profiles use the frozen floor → byte-identical.
			var minFriendlies = Info.SmallSquadCoverage && participates
				? Math.Max(1, Math.Min(Info.MinNearbyFriendlies, Info.SmallSquadMinNearbyFriendlies))
				: Info.MinNearbyFriendlies;

			// Find unit clusters by looking for groups of friendly units away from base
			var clusters = FindUnitClusters(friendlyUnits, minFriendlies);
			var clustersFound = clusters.Count;

			var spread = Info.SectorSpread && participates;
			var evac = Info.DangerEvac && dangerField != null;

			// Drop-and-leave needs the frontier-distance field to place its anchor, so controlField being
			// non-null carries the Participates gate with it (Initialize only resolves it for participants).
			var dropLive = Info.DropAndLeave && controlField != null;

			// Selection is gated at the RELEASE level, not the entry threshold. Gating at the entry threshold
			// while releasing lower leaves the whole band between them as a latch — see the two-levels note in
			// SupplyLogisticsMath's header. Same number on both sides, so the truck is only ever sent somewhere
			// it would not immediately leave.
			var releaseLevel = SupplyLogisticsMath.ReleaseLevel(Info.EvacDangerThreshold, Info.EvacReleaseHysteresis);

			// DECORRELATE SELECTION FROM REJECTION. Cluster choice below is need-descending, and the neediest
			// cluster is the one that has been fighting — i.e. the one deepest in believed danger, which is
			// exactly what the evac rule then refuses to approach. Selecting a cluster the module is about to
			// reject is what turned the evac branch into a limit cycle (SupplyLogisticsMath's EVAC DAMPER note).
			// Gating on danger BEFORE selection makes the two criteria consistent by construction rather than
			// merely less correlated. Chosen over softening the merit (need weighted against danger) because a
			// weighting always leaves inputs where a big enough need still selects a vetoed cluster, so the
			// cycle survives at lower frequency; a gate leaves none.
			if (evac)
			{
				foreach (var c in clusters)
				{
					c.FollowCell = FindSafeFollowPosition(c) ?? c.CenterCell;
					c.Danger = GroundDangerAt(c.FollowCell);
				}

				clusters = SelectServableClusters(clusters, releaseLevel);
			}

			var hunt = SupplyTruckHuntMath.ShouldHunt(Info.IdleTruckHunt, isExperimentalBot);
			var maxFollowLength = WDist.FromCells(Info.MaxFollowDistance).Length;

			// @experimental sector spread: precompute distinct-cluster assignments over a STABLY sorted truck
			// list (ActorID) so the greedy result is enumeration-order-independent and deterministic.
			//
			// THIS SORT IS NOT WHAT MAKES THE DROP GATE DETERMINISTIC — it only runs when the spread is on
			// AND clusters exist, and the drop path needs a stable order unconditionally. What actually
			// supplies it is ambient: world.ActorsHavingTrait<Mobile>() enumerates through TraitDictionary,
			// which keeps its actor list sorted by ActorID and seeks with BinarySearchMany(ActorID + 1)
			// (TraitDictionary.cs:153-155), so `trucks` is already in ActorID order before this line. That
			// matters more than it used to: the redundancy gate makes each truck's decision depend on what
			// EARLIER trucks in the loop claimed this scan, where previously every truck dropped and the
			// order was irrelevant. Same-scan claims resolve lowest-ActorID-first on both clients.
			Dictionary<Actor, UnitCluster> spreadTargets = null;
			var orderedTrucks = trucks;
			if (spread && clusters.Count > 0)
			{
				orderedTrucks = trucks.OrderBy(t => t.ActorID).ToList();
				var truckPositions = orderedTrucks.Select(t => t.CenterPosition).ToList();
				var sectors = clusters.Select(c => new SupplyLogisticsMath.Sector(c.Center, NeedScore(c.AmmoNeed))).ToList();
				var assignment = SupplyLogisticsMath.AssignSectors(truckPositions, sectors, maxFollowLength);

				spreadTargets = new Dictionary<Actor, UnitCluster>();
				for (var i = 0; i < orderedTrucks.Count; i++)
					if (assignment[i] != SupplyLogisticsMath.NoSector)
						spreadTargets[orderedTrucks[i]] = clusters[assignment[i]];
			}

			// Own SRs — the fog-legal safe rear an evacuating truck pulls back toward, and the seed the
			// drop-and-leave anchor descends FROM (our own actors). A player can hold more than one, so the
			// NEAREST is picked per truck inside the loop.
			var supplyRoutes = evac || dropLive ? FindOwnSupplyRoutes() : null;

			// Keep the per-SR anchor hysteresis bounded to SRs we still hold.
			if (dropAnchor.Count > 0)
			{
				var staleAnchors = dropAnchor.Keys.Where(a => a.IsDead || !a.IsInWorld || a.Owner != player).ToList();
				foreach (var a in staleAnchors)
					dropAnchor.Remove(a);
			}

			if (anchorRejectStreak.Count > 0)
			{
				var staleStreaks = anchorRejectStreak.Keys.Where(a => a.IsDead || !a.IsInWorld || a.Owner != player).ToList();
				foreach (var a in staleStreaks)
					anchorRejectStreak.Remove(a);
			}

			if (Info.DebugLogging)
				Log.Write("debug",
					$"[supply] scan player={player.PlayerName} trucks={trucks.Count} friendlies={friendlyUnits.Count} "
					+ $"clusters-found={clustersFound} clusters-selected={clusters.Count} "
					+ $"evac={evac} drop={dropLive} release-level={releaseLevel} srs={supplyRoutes?.Count ?? 0}");

			if (Info.DebugLogging && evac)
				foreach (var c in clusters)
					Log.Write("debug",
						$"[supply] cluster cell={c.CenterCell} follow={c.FollowCell} units={c.UnitCount} "
						+ $"need={NeedScore(c.AmmoNeed)} danger={c.Danger} gated={c.Gated}");

			foreach (var truck in orderedTrucks)
			{
				// Find the best cluster for this truck (closest cluster with ammo need). Null when there are
				// no clusters at all, none in range, or the spread left this truck unassigned.
				UnitCluster bestCluster = null;
				if (clusters.Count > 0)
				{
					if (spread)
					{
						spreadTargets?.TryGetValue(truck, out bestCluster);
					}
					else
					{
						bestCluster = clusters
							.Where(c => (c.Center - truck.CenterPosition).Length < WDist.FromCells(Info.MaxFollowDistance).Length)
							.OrderByDescending(c => c.AmmoNeed)
							.ThenBy(c => (c.Center - truck.CenterPosition).LengthSquared)
							.FirstOrDefault();
					}
				}

				// Danger evac, damped. Deliberately evaluated BEFORE the no-cluster bail and with a possibly
				// null cluster: the relief valve can still leave a truck with no target (nothing needs ammo),
				// and a truck standing in fire must be able to pull back regardless. Pre-damper this case fell
				// through unevacuated.
				var srActor = evac ? NearestSupplyRoute(supplyRoutes, truck.CenterPosition) : null;
				if (srActor != null)
				{
					if (StepEvac(truck, srActor, bestCluster))
						continue;
				}
				else
					evacState.Remove(truck);

				// DROP-AND-LEAVE. Evaluated AFTER the evac branch — deliberately, and the ordering is the
				// safety property: a truck standing in fire pulls back before it considers an errand, so the
				// undamped withdrawal asymmetry that SupplyLogisticsMath's header pins is preserved intact.
				// Evaluated BEFORE the follow branch because the drop REPLACES following for this truck this
				// scan; the two must never both issue a Move.
				//
				// Re-issuing the identical errand every scan is harmless HERE and is why this branch needs no
				// "already dropping" memory: the anchor is static (belief-field descent + Chebyshev
				// hysteresis), so a re-issued MoveTo resumes toward the same cell rather than chasing a
				// receding one. That is the same property that makes the mode work for the infantry walking
				// to the crate, applied to the truck. The one cost is a re-issue that lands in the same tick
				// the arrival CallFunc would have run, which cancels that drop; the truck is already there,
				// so the next scan's errand completes it. Bounded at one scan, self-correcting.
				if (dropLive)
				{
					var dropSr = srActor ?? NearestSupplyRoute(supplyRoutes, truck.CenterPosition);
					if (StepDrop(truck, dropSr))
						continue;
				}

				// Tier 2: an unassigned truck hunts rather than parking. Hunt off ⇒ plain `continue`, the
				// old behaviour for both the no-spread-target and the no-in-range-cluster cases.
				if (bestCluster == null)
				{
					if (hunt)
						HuntStarvingInfantry(truck);

					continue;
				}

				// The follow cell. On the evac path it was already resolved (and danger-gated) above, so reuse
				// it rather than recomputing — the gate must apply to the cell actually ordered.
				var followPos = evac ? (CPos?)bestCluster.FollowCell : FindSafeFollowPosition(bestCluster);

				if (followPos.HasValue)
				{
					if (!routeViaDanger)
					{
						// Flag off / non-participant: a single direct Move toward the cluster, now DAMPED.
						// This used to re-issue every scan and was justified as "byte-identical base
						// behaviour"; that justification was retired with the byte-identity policy (875c93c1)
						// and the defect it preserved is the one the mode exists to remove — the destination
						// is a moving centroid, so an undamped re-issue cancels the drive and restarts the
						// path every 150 ticks, forever, on a unit whose entire job is to arrive.
						//
						// DELIBERATELY NOT BotOrderDamping.Recurring, and this is worth stating so nobody
						// "completes" the set later. The funnel gate cannot suppress a truck follow Move at
						// all: trucks are single-owner (truk is excluded by every other module), they are
						// never ledger-committed, so predicate (a) has no incumbent to find; and this
						// module's own ScanInterval (150 t) strictly exceeds ReorderDwellTicks (120 t), so
						// consecutive standing records for one truck are always further apart than the dwell
						// window and predicate (b) can never fire either. Marking it would assert damping
						// that provably cannot occur, and would put a real stranding hazard on the
						// lastFollow write below for nothing. The oscillation is damped HERE instead, by
						// distance — the right instrument for a destination that moves by construction.
						if (ShouldReissueFollow(truck, followPos.Value))
						{
							bot.QueueOrder(new Order("Move", truck, Target.FromCell(world, followPos.Value), false));
							lastFollow[truck] = followPos.Value;
						}
					}
					else
					{
						// Stage-E: if the straight drive from the truck to its follow cell would cross a
						// ground kill zone, detour via a safer waypoint first (queued: false), then the follow
						// cell (queued: true). Against the territory-baseline gradient the safer side is the
						// rear, so the pull-back-lateral-re-enter path emerges. WaypointPassable rejects a
						// waypoint the truck cannot stand on (rear water reads 0 danger = falsely "safe").
						var ground = GroundDangerSampler();
						var passable = WaypointPassable(truck);
						var via = GroundDangerNav.DetourWaypoint(
							truck.Location, followPos.Value,
							Info.GroundDangerDetourCells, Info.GroundDangerDetourSteps,
							Info.GroundDangerSafeThreshold, ground, passable);

						if (via.HasValue)
						{
							// Deadband: leave an in-flight two-leg maneuver alone unless the recomputed
							// waypoint shifted >= threshold. `from` is the MOVING truck, so re-issuing every
							// scan would make the waypoint recede and restart the detour before it completes.
							var had = lastVia.TryGetValue(truck, out var prev);
							if (!had || (prev - via.Value).LengthSquared >= Info.RepathThresholdCells * Info.RepathThresholdCells)
							{
								// ALL-OR-NOTHING. Leg 1 is the danger-avoiding waypoint and is non-queued, hence
								// suppressible; leg 2 is the direct line and is queued. Issuing leg 2 alone drives the
								// truck along exactly the straight path the detour exists to avoid, and lastVia's
								// deadband would then block the re-issue that could recover it. Refused head ⇒ issue
								// nothing and remember nothing, so the next scan retries the whole maneuver.
								if (bot.QueueOrder(new Order("Move", truck, Target.FromCell(world, via.Value), false)))
								{
									bot.QueueOrder(new Order("Move", truck, Target.FromCell(world, followPos.Value), true));
									lastVia[truck] = via.Value;

									// The two-leg maneuver terminates at followPos, so it IS a follow dispatch and
									// must be recorded as one — otherwise the direct branch would see no record the
									// scan the detour stops being needed and re-issue on top of a running detour.
									// INSIDE the guard: recording a follow dispatch that was never issued is exactly
									// the stranding shape, and lastFollow now feeds ShouldReissueFollow's deadband,
									// so a phantom record would suppress the re-issue that should restart it.
									lastFollow[truck] = followPos.Value;
								}
							}
						}
						else
						{
							// No detour needed — a single direct Move each scan, damped exactly as the
							// flag-off branch above and Protected for the same reason stated there.
							// lastVia.Remove is UNCONDITIONAL, deliberately: no detour is needed this scan, so
							// the detour memory is stale whether or not the deadband let the Move through. (An
							// earlier cut made it conditional on the order being accepted; that only made sense
							// while this order could be refused, which it now cannot.)
							if (ShouldReissueFollow(truck, followPos.Value))
							{
								bot.QueueOrder(new Order("Move", truck, Target.FromCell(world, followPos.Value), false));
								lastFollow[truck] = followPos.Value;
							}

							lastVia.Remove(truck);
						}
					}

					Adopt(truck);
				}
			}
		}

		/// <summary>Is the follow Move worth re-issuing for this truck this scan? Thin engine-side wrapper that
		/// samples the two observables and defers the rule to the pinned pure predicate.</summary>
		bool ShouldReissueFollow(Actor truck, CPos followPos)
		{
			var dispatched = lastFollow.TryGetValue(truck, out var prev);
			return SupplyLogisticsMath.ShouldReissueFollow(
				dispatched, truck.IsIdle, prev.X, prev.Y, followPos.X, followPos.Y, Info.RepathThresholdCells);
		}

		/// <summary>DROP-AND-LEAVE: send one loaded truck to unload its whole stock at the forward supply point.
		/// Returns true when the errand was issued and the caller should skip the follow path this scan.
		///
		/// <para>WHOLE LOAD, NOT A RESERVE — a stated choice. Three reasons, in order of weight. (1) A truck that
		/// held back a remainder would still be dropped from this module's roster the moment that remainder fell
		/// under RestockThreshold, so "keep a reserve" buys a smaller version of the same lifecycle question
		/// rather than avoiding it. (2) A stationary 4-cell cache aura serves better than a mobile 5-cell one:
		/// the pull side terminates against it (MoveWithinRange can actually stop), and a soldier that drifts out
		/// of a parked aura walks back in rather than flipping to a fresh chase. (3) DropSupplyCacheHere is
		/// all-or-nothing today, and splitting it means changing a shipped trait that a human's deploy button and
		/// cargo panel also drive. The supply VALUE is conserved either way — the crate carries SupplyCreditValue
		/// 750, the same as the truck's load — so this is not a write-off.</para>
		///
		/// <para>WHEN THE FRONT MOVES AWAY FROM AN EXISTING CACHE, nothing is done actively, and that is
		/// deliberate: the anchor tracks the frontier, so the NEXT drop lands at the new supply point, while the
		/// stranded crate drains normally (RemoveBelowSupply 1 disposes it) or is captured. Reclaiming a crate
		/// back into a truck does not exist in this codebase (the census's missing crate → truck leg), and
		/// inventing it here would be a second feature hiding inside this one.</para></summary>
		bool StepDrop(Actor truck, Actor srActor)
		{
			var dispatched = dropTarget.TryGetValue(truck, out var sentTo);

			// AN IDLE TRUCK THAT STILL HOLDS ITS LOAD HAS FINISHED THE ERRAND WITHOUT DROPPING — it stopped on
			// a cell already occupied (CanDropCache refused), or the destination went unreachable after issue
			// and the arrival check refused. Clearing the record here is what keeps the re-issue dedup from
			// turning those two SELF-CORRECTING refusals into a truck parked on its anchor forever: the
			// suppression is only safe while an errand is actually running. IsIdle is the responsive term —
			// re-issuing makes it false — and it is the same observable StepEvac's leg model uses to notice a
			// Move that never arrived. A truck that DID drop is at 0 supply and has already left the eligible
			// roster, so it is pruned rather than reaching here.
			// No outer `dispatched &&` guard: the predicate already carries that term, and repeating it here
			// would make the call read as optional decoration at the one site whose whole point is that it is
			// not. The not-dispatched case falls into this block and both statements are no-ops, which is the
			// intended cost of leaving the predicate as the single authority.
			if (!SupplyDropMath.ErrandStillRunning(dispatched, truck.IsIdle))
			{
				dropTarget.Remove(truck);
				dispatched = false;
			}

			var provider = truck.TraitOrDefault<SupplyProvider>();
			var cacheActor = truck.TraitOrDefault<DropsSupplyCache>()?.Info.SupplyCacheActor;
			if (provider == null || string.IsNullOrEmpty(cacheActor))
				return false;

			var anchor = ResolveDropAnchor(srActor, truck);

			var starving = anchor.HasValue ? CountStarvingNear(anchor.Value, provider) : 0;
			var cacheSupply = anchor.HasValue ? CacheSupplyNear(anchor.Value, cacheActor) : 0;
			var inFlight = anchor.HasValue ? InFlightSupplyTo(anchor.Value, truck) : 0;

			var drop = SupplyDropMath.ShouldDrop(anchor.HasValue,
				provider.CurrentSupply, Info.DropMinSupply,
				starving, Info.DropMinStarvingUnits,
				cacheSupply, inFlight, Info.DropRedundantCacheSupply);

			if (!drop)
			{
				// REVOKE. A module that can START an errand must be able to STOP one, and here that is not a
				// nicety: the conditions can go false while a truck is still driving (the demand refilled, a
				// crate landed, the anchor became unreachable), and the errand would otherwise run to
				// completion and unload on a decision that is no longer true. Relying on some LATER branch to
				// cancel it by side effect is what makes this unsafe rather than untidy — the follow and hunt
				// branches happen to issue a cancelling Move, but on @stable IdleTruckHunt is BotType-gated
				// off, so a truck with no cluster falls through to a bare `continue` that issues nothing at
				// all, and drives on to drop. An explicit Stop makes the revocation independent of whatever
				// the rest of the scan happens to do.
				if (dispatched)
				{
					bot.QueueOrder(new Order("Stop", truck, false));
					dropTarget.Remove(truck);

					// The Stop kills whatever is running, and this scan falls through to the follow
					// branch immediately afterwards. Void the follow record too, or that branch reads a
					// truck that is still nominally driving to its old follow cell (IsIdle is not yet
					// true — the Stop has not been drained from the order queue) and suppresses the very
					// Move that is supposed to pick the truck back up, parking it until the next scan.
					lastFollow.Remove(truck);
					Log.Write("debug",
						$"[supply] drop-revoked truck={truck.ActorID}@{truck.Location} was-sent-to={sentTo} "
						+ $"anchor={(anchor.HasValue ? anchor.Value.ToString() : "<none>")} "
						+ $"supply={provider.CurrentSupply} starving={starving} "
						+ $"cache-near={cacheSupply} in-flight={inFlight}");
				}

				// Per-scan LEVEL — gated. The "why did it not drop?" line, carrying every term the decision
				// read so the answer never needs a rebuild to obtain.
				if (Info.DebugLogging)
					Log.Write("debug",
						$"[supply] drop-declined truck={truck.ActorID}@{truck.Location} "
						+ $"anchor={(anchor.HasValue ? anchor.Value.ToString() : "<none>")} "
						+ $"supply={provider.CurrentSupply}/{Info.DropMinSupply} "
						+ $"starving={starving}/{Info.DropMinStarvingUnits} "
						+ $"cache-near={cacheSupply}+in-flight={inFlight}/{Info.DropRedundantCacheSupply}");

				return false;
			}

			// Already on our way to this exact cell: issue NOTHING, but still take the branch — returning
			// false here would drop through to the follow path, whose non-queued Move would cancel the very
			// errand this record exists to protect.
			if (!SupplyDropMath.ShouldIssueDrop(dispatched, sentTo.X, sentTo.Y, anchor.Value.X, anchor.Value.Y))
			{
				if (Info.DebugLogging)
					Log.Write("debug",
						$"[supply] drop-inflight truck={truck.ActorID}@{truck.Location} anchor={anchor.Value} "
						+ $"load={provider.CurrentSupply} activity={truck.CurrentActivity?.GetType().Name ?? "<none>"}");

				return true;
			}

			// EDGE — unconditional. The drop and its chosen cell are the two facts the whole mode turns on.
			Log.Write("debug",
				$"[supply] drop truck={truck.ActorID}@{truck.Location} anchor={anchor.Value} "
				+ $"load={provider.CurrentSupply} starving={starving} cache-near={cacheSupply} in-flight={inFlight} "
				+ $"danger-at-anchor={GroundDangerAt(anchor.Value)} frontier={FrontierDistanceAt(anchor.Value)} "
				+ $"{(dispatched ? $"retargeted-from={sentTo}" : "new")}");

			// Refused ⇒ issue NOTHING and remember nothing, but still take the branch — exactly the idiom
			// used for the in-flight case above. Returning false would fall through to the follow path,
			// whose non-queued Move would cancel whatever the truck is doing; and caching the anchor would
			// make ShouldIssueDrop suppress the retry. The next scan re-issues cleanly.
			if (!bot.QueueOrder(new Order("DropSupplyCacheAt", truck, Target.FromCell(world, anchor.Value), false)))
				return true;

			dropTarget[truck] = anchor.Value;

			// Any in-flight Stage-E detour memory is void — the errand supersedes it. The follow
			// record goes with it: the errand cancels whatever follow Move was running, so a
			// surviving record would claim a drive that no longer exists.
			lastVia.Remove(truck);
			lastFollow.Remove(truck);
			Adopt(truck);
			return true;
		}

		/// <summary>Supply already COMMITTED to this anchor by other trucks — the loads of trucks this module
		/// dispatched here whose errands have not yet landed. Excludes <paramref name="self"/>, which is the
		/// truck being decided about. Order-independent (a sum); no RNG.
		///
		/// <para>Reconstructed from the dispatch map rather than tracked separately, so it cannot disagree
		/// with the record that suppresses re-issue — there is exactly one piece of state and both jobs read
		/// it. A truck that has already unloaded holds 0 and contributes nothing even before its record is
		/// pruned, so the sum degrades gracefully rather than double-counting.</para></summary>
		int InFlightSupplyTo(CPos anchor, Actor self)
		{
			if (dropTarget.Count == 0)
				return 0;

			var total = 0;
			foreach (var kv in dropTarget)
			{
				if (kv.Key == self || kv.Value != anchor)
					continue;

				if (kv.Key.IsDead || !kv.Key.IsInWorld)
					continue;

				total += kv.Key.TraitOrDefault<SupplyProvider>()?.CurrentSupply ?? 0;
			}

			return total;
		}

		/// <summary>The forward supply point for one Supply Route: walk DOWN ControlField's distance-to-enemy-
		/// frontier field from the SR toward the nearest believed front, halting a standoff short of it and
		/// never stepping into a believed weapon envelope. Null when no anchor could be established.
		///
		/// <para>This is <see cref="ForwardStagingMath.StagingCell"/> — the SAME primitive
		/// PoiOffensiveBotModule.ResolveStagingAnchor consumes, at a larger standoff — rather than a second
		/// placement algorithm, and the reuse buys the property the drop mode most needs: the descent already
		/// carries Chebyshev anchor hysteresis, which is exactly the memory the evac decision was missing.</para>
		///
		/// <para>THE INERT FALLBACK IS LOAD-BEARING, NOT A GUARD CLAUSE. A flat field — no believed enemy
		/// anywhere, or a non-participating profile with no field at all — yields no improving neighbour, so the
		/// descent returns the SR unchanged. Treating that as "no anchor" rather than "anchor at the SR" is what
		/// stops the truck unloading its stock at the beachhead, and it is also what makes the whole mode
		/// self-disable on Normal/Rush/Turtle without a second gate.</para>
		///
		/// <para>PASSABILITY IS TESTED HERE, ENGINE-SIDE, BECAUSE THE DESCENT CANNOT DO IT. StagingCell guards
		/// bounds and believed danger only; it has no terrain awareness, and `GridCellToMapCell` picks ONE fixed
		/// cell of each coarse block, so a 20-40 step walk can perfectly well terminate on water, a cliff or
		/// outside the playable area. That is not a harmless mistake downstream: `PathFinder` bails to NoPath on
		/// an inaccessible target and `Move` treats an empty path as arrival, so an unreachable anchor would let
		/// the unload run at whatever cell the truck was standing on. Refusing to ADOPT such a cell is the first
		/// of two lines (the second is the arrival check in the errand itself), and it is tested on BOTH return
		/// paths — a fresh candidate and a hysteresis-HELD one — for the same reason
		/// PoiOffensiveBotModule.ResolveAdvanceAnchor grants on both of its: a caller downstream must be able to
		/// assume the property without re-deriving which path produced the cell.</para></summary>
		CPos? ResolveDropAnchor(Actor srActor, Actor mover)
		{
			if (controlField == null || srActor == null)
				return null;

			var (sgx, sgy) = controlField.MapCellToGridCell(srActor.Location);
			var (agx, agy) = ForwardStagingMath.StagingCell(sgx, sgy,
				Info.DropStandoffCells, Info.DropDangerSafeThreshold, Info.DropMaxDescentSteps,
				(gx, gy) => controlField.FrontierDistanceAt(player, gx, gy),
				(gx, gy) => dangerField != null ? dangerField.GroundDanger(player, controlField.GridCellToMapCell(gx, gy)) : 0,
				(gx, gy) => gx >= 0 && gx < controlField.GridWidth && gy >= 0 && gy < controlField.GridHeight);

			if (agx == sgx && agy == sgy)
			{
				dropAnchor.Remove(srActor);
				if (Info.DebugLogging)
					Log.Write("debug",
						$"[supply] anchor sr={srActor.Location} → <none> (descent stalled at the SR: flat field, or "
						+ $"the front is on top of us) frontier-at-sr={controlField.FrontierDistanceAt(player, sgx, sgy)}");

				return null;
			}

			var candidate = controlField.GridCellToMapCell(agx, agy);

			// Same locomotor-bound predicate the Stage-E detour uses to reject a waypoint that reads "safe"
			// only because unstamped impassable ground carries no danger. The failure mode here is worse than
			// a bad detour, so the same guard is applied to the destination itself.
			var passable = WaypointPassable(mover);
			if (!world.Map.Contains(candidate) || !passable(candidate))
			{
				dropAnchor.Remove(srActor);

				// EDGE — unconditional, because how OFTEN the descent lands on unreachable ground is the open
				// question this mode's usefulness turns on, and it cannot be answered statically.
				//
				// LOGGED ON TRANSITION PLUS A PERIODIC ROLL-UP, NOT DEDUPED FLAT. A persistently-bad descent
				// re-derives every scan — ~10 lines/min/player — and this is now the loudest unconditional
				// line, which matters since DebugLogging went off precisely to control volume. But a flat
				// dedup would delete the very signal the line exists to produce: "rejected once" and
				// "rejected for the whole match" would look identical, and frequency is the measurement.
				// So the streak is COUNTED and reported — first rejection in full, then a rolling count, and
				// a recovery line when it clears (below), which together bound each episode exactly.
				anchorRejectStreak.TryGetValue(srActor, out var streak);
				streak++;
				anchorRejectStreak[srActor] = streak;

				if (streak == 1)
					Log.Write("debug",
						$"[supply] anchor-impassable sr={srActor.Location} → {candidate} "
						+ $"on-map={world.Map.Contains(candidate)} standoff={Info.DropStandoffCells} "
						+ $"frontier={controlField.FrontierDistanceAt(player, agx, agy)} — no anchor this scan");
				else if (Info.AnchorRejectRollupScans > 0 && streak % Info.AnchorRejectRollupScans == 0)
					Log.Write("debug",
						$"[supply] anchor-impassable-continuing sr={srActor.Location} consecutive={streak} "
						+ $"latest={candidate} frontier={controlField.FrontierDistanceAt(player, agx, agy)}");

				return null;
			}

			// Recovery closes the episode, so a reader can compute both how often the descent fails and how
			// long each failure lasts without inferring it from the absence of lines.
			if (anchorRejectStreak.TryGetValue(srActor, out var clearedAfter))
			{
				anchorRejectStreak.Remove(srActor);
				Log.Write("debug",
					$"[supply] anchor-recovered sr={srActor.Location} → {candidate} after={clearedAfter} scans");
			}

			var had = dropAnchor.TryGetValue(srActor, out var prev);
			if (had && !ForwardStagingMath.AnchorShifted(prev.X, prev.Y, candidate.X, candidate.Y, Info.DropAnchorHysteresisCells))
			{
				// A HELD anchor is re-tested rather than trusted: terrain does not change, but a Building
				// placed on the cell does, and the held cell is not the one just granted above.
				if (world.Map.Contains(prev) && passable(prev))
					return prev;

				dropAnchor.Remove(srActor);
				Log.Write("debug", $"[supply] anchor-impassable sr={srActor.Location} held={prev} — dropped, re-deriving next scan");
				return null;
			}

			dropAnchor[srActor] = candidate;
			if (Info.DebugLogging)
				Log.Write("debug",
					$"[supply] anchor sr={srActor.Location} → {candidate} standoff={Info.DropStandoffCells} "
					+ $"frontier={controlField.FrontierDistanceAt(player, agx, agy)} danger={GroundDangerAt(candidate)} "
					+ $"shifted-from={(had ? prev.ToString() : "<new>")}");

			return candidate;
		}

		/// <summary>How many of our soldiers within <see cref="SupplyFollowerBotModuleInfo.DropDemandRadiusCells"/>
		/// of the anchor are starving in a pool this truck could actually afford a batch of. Deliberately the SAME
		/// eligibility shape the Tier-2 hunt uses — the candidate must carry the provider's own RearmCondition
		/// (which only soldiers hold, so a vehicle never registers as demand a truck cannot relieve) and the pool
		/// must be affordable — so the drop decision and the hunt cannot disagree about who counts as needy.
		/// A count, so it is order-independent; no RNG.</summary>
		int CountStarvingNear(CPos anchor, SupplyProvider provider)
		{
			var rearmCondition = provider.Info.RearmCondition;
			if (string.IsNullOrEmpty(rearmCondition))
				return 0;

			// Margin: the crate lands up to the errand's stop tolerance off the anchor, so a soldier counted
			// at exactly the radius could end up beyond his own selection leash from the crate dropped for
			// him — which is precisely what sizing the radius to that leash was meant to prevent.
			//
			// THE WORST CASE IS EXACT, WITH ZERO SLACK, AND IT HOLDS — but only because every comparison in
			// the chain is inclusive, so treat all three of these numbers as load-bearing together:
			//   * demand is counted at <= 18 cells   — FindActorsInCircle: `HorizontalLengthSquared <= r^2`
			//                                          (WorldUtils.cs:83-84), no actor-radius expansion
			//   * the crate lands <= 2 cells off     — SupplyDropMath.ArrivedAtDropCell: `dx^2+dy^2 <= t^2`,
			//                                          Euclidean, so (2,0) is the true maximum and (2,2) is
			//                                          already refused
			//   * the soldier's leash admits <= 20   — SupplyHuntMath.WithinLeash: `distanceSquared <=
			//                                          LeashLengthSquared`, same squared-WPos geometry
			// Collinear worst case is therefore 18 + 2 = 20 EXACTLY, which the leash admits. A strict `<` in
			// any of the three, an actor-radius expansion in the sweep, or a Chebyshev arrival check would
			// each push the boundary soldier outside the leash of the crate dropped for him. Raising this
			// margin to 3 buys a whole cell of slack for one cell of reach if that ever feels too tight.
			var radius = Math.Max(1, Info.DropDemandRadiusCells - Math.Max(0, Info.DropDemandMarginCells));

			var count = 0;
			foreach (var a in world.FindActorsInCircle(world.Map.CenterOfCell(anchor), WDist.FromCells(radius)))
			{
				if (a.Owner != player || a.IsDead || !a.IsInWorld)
					continue;

				if (Info.SupplyTruckTypes.Contains(a.Info.Name))
					continue;

				var rearmable = a.TraitOrDefault<Rearmable>();
				if (rearmable == null)
					continue;

				if (!a.TraitsImplementing<ExternalCondition>().Any(e => e.Info.Condition == rearmCondition))
					continue;

				foreach (var pool in rearmable.RearmableAmmoPools)
				{
					if (provider.CurrentSupply < pool.Info.SupplyValue)
						continue;

					if (!SupplyTruckHuntMath.IsStarving(pool.CurrentAmmoCount, pool.Info.Ammo, Info.HuntStarvingThresholdPerMille))
						continue;

					count++;
					break;
				}
			}

			return count;
		}

		/// <summary>Supply already sitting in our own caches within the demand radius of the anchor. A sum, so it
		/// is order-independent; no RNG. Only OUR caches count — an ally's crate does serve our soldiers, but the
		/// crate is ProximityCapturable and can change hands, and counting a foreign one would let an opponent
		/// suppress our resupply by parking a crate near our front.</summary>
		int CacheSupplyNear(CPos anchor, string cacheActor)
		{
			var total = 0;
			foreach (var a in world.FindActorsInCircle(world.Map.CenterOfCell(anchor), WDist.FromCells(Info.DropDemandRadiusCells)))
			{
				if (a.Owner != player || a.IsDead || !a.IsInWorld || a.Info.Name != cacheActor)
					continue;

				total += a.TraitOrDefault<SupplyProvider>()?.CurrentSupply ?? 0;
			}

			return total;
		}

		/// <summary>Believed distance to the enemy frontier at a map cell, in coarse control-grid cells. Purely
		/// diagnostic — nothing decides on it.</summary>
		int FrontierDistanceAt(CPos cell)
		{
			if (controlField == null)
				return -1;

			var (gx, gy) = controlField.MapCellToGridCell(cell);
			return controlField.FrontierDistanceAt(player, gx, gy);
		}

		/// <summary>Put a truck on follow duty and claim it, logging the EDGE. One place, so every branch that
		/// tasks a truck reports it identically.</summary>
		void Adopt(Actor truck)
		{
			if (activeTrucks.Contains(truck))
				return;

			activeTrucks.Add(truck);
			blackboard?.ClaimUnit(truck, "supply-follow");
			Log.Write("debug",
				$"[supply] adopt truck={truck.ActorID}@{truck.Location} "
				+ $"supply={truck.TraitOrDefault<SupplyProvider>()?.CurrentSupply.ToString() ?? "n/a"}");
		}

		/// <summary>
		/// Tier 2 idle-truck hunt: drive an unassigned truck to the neediest starving soldier inside its
		/// leash. Called only for a truck the follow pass left with nothing to do, and only for the
		/// @experimental bot with IdleTruckHunt on.
		///
		/// Infantry only, and by construction rather than by a name list: the candidate must carry the
		/// truck's OWN RearmCondition — replenish-soldiers for TRUK (vehicles.yaml:546), which only soldiers
		/// HOLD as an ExternalCondition (infantry.yaml:215). LOGISTICSCENTER names the same condition
		/// (structures.yaml:382-386) but as a ProximityExternalCondition GRANTER, which is not an
		/// ExternalCondition subclass — so the TraitsImplementing&lt;ExternalCondition&gt; scan below does not
		/// match it, and the building never reads as demand. A vehicle therefore never appears
		/// as demand here, which is correct: the only provider that serves replenish-vehicles is the static
		/// Logistics Centre (structures.yaml:394), and it is docking-gated, so vehicles PULL and trucks
		/// cannot push to them.
		///
		/// The candidate scan is a leash-radius spatial query, so the bound holds twice over: FindActorsInCircle
		/// applies the identical inclusive squared-distance filter SupplyHuntMath.WithinLeash does, and the
		/// pure selection re-checks it. No candidate ⇒ no order ⇒ the truck stays put.
		/// </summary>
		void HuntStarvingInfantry(Actor truck)
		{
			var provider = truck.TraitOrDefault<SupplyProvider>();

			// CanServeNow is the provider's own serving ladder — a truck that is paused, mid-restock or
			// reserving its remainder for the drive home would arrive with nothing to give.
			if (provider == null || provider.CountsAsEmpty || !provider.CanServeNow)
				return;

			// No recipient-side condition means no way to tell infantry demand from vehicle demand, and a
			// truck without one would push to anything — don't guess.
			var rearmCondition = provider.Info.RearmCondition;
			if (string.IsNullOrEmpty(rearmCondition))
				return;

			var demands = new List<SupplyTruckHuntMath.Demand>();
			var candidates = new List<Actor>();

			foreach (var a in world.FindActorsInCircle(truck.CenterPosition, WDist.FromCells(Info.HuntLeashCells)))
			{
				if (a.Owner != player || a.IsDead || !a.IsInWorld || a == truck)
					continue;

				if (Info.SupplyTruckTypes.Contains(a.Info.Name))
					continue;

				var rearmable = a.TraitOrDefault<Rearmable>();
				if (rearmable == null)
					continue;

				if (!a.TraitsImplementing<ExternalCondition>().Any(e => e.Info.Condition == rearmCondition))
					continue;

				// Worst starving pool we can actually afford a batch of. An unaffordable pool is not demand
				// this truck can relieve, so it must not pull it out of position.
				var shortfall = 0;
				foreach (var pool in rearmable.RearmableAmmoPools)
				{
					if (provider.CurrentSupply < pool.Info.SupplyValue)
						continue;

					if (!SupplyTruckHuntMath.IsStarving(pool.CurrentAmmoCount, pool.Info.Ammo, Info.HuntStarvingThresholdPerMille))
						continue;

					var s = SupplyTruckHuntMath.ShortfallPerMille(pool.CurrentAmmoCount, pool.Info.Ammo);
					if (s > shortfall)
						shortfall = s;
				}

				if (shortfall == 0)
					continue;

				var distanceSquared = (a.CenterPosition - truck.CenterPosition).HorizontalLengthSquared;
				demands.Add(new SupplyTruckHuntMath.Demand(distanceSquared, shortfall, a.ActorID));
				candidates.Add(a);
			}

			var pick = SupplyTruckHuntMath.SelectDemand(demands, Info.HuntLeashCells, Info.HuntNeedBandPerMille);
			if (pick == SupplyTruckHuntMath.NoDemand)
				return;

			// Already covering him: the push is reaching him where the truck stands, so issue nothing rather
			// than nudging a serving truck onto his cell every scan.
			if (!SupplyTruckHuntMath.NeedsApproach(demands[pick].DistanceSquared, provider.Info.Range.LengthSquared))
				return;

			// Stop as soon as he is inside the push aura (less a cell of margin), not on top of him — the
			// last aura's worth of driving buys nothing and this sweep runs precisely where the line has
			// come apart. The margin is also what keeps the order from stalling on cell quantization; the
			// reasoning lives with the constant, in ApproachTarget.
			var target = candidates[pick];
			var stopPosition = SupplyTruckHuntMath.ApproachTarget(truck.CenterPosition, target.CenterPosition, provider.Info.Range.Length);
			bot.QueueOrder(new Order("Move", truck, Target.FromCell(world, world.Map.CellContaining(stopPosition)), false));
			lastVia.Remove(truck);
			lastFollow.Remove(truck);

			if (Info.DebugLogging)
				Log.Write("debug",
					$"[supply] hunt truck={truck.ActorID}@{truck.Location} → {world.Map.CellContaining(stopPosition)} "
					+ $"target={target.ActorID}@{target.Location} shortfall={demands[pick].ShortfallPerMille} candidates={demands.Count}");

			Adopt(truck);
		}

		/// <summary>Narrow the clusters to the ones actually worth sending a truck to, as a RELIEF-VALVED
		/// filter rather than a hard veto. Only reached when evac is live, so non-participating profiles keep
		/// the old candidate set exactly.
		///
		/// <para>THE VALVE IS THE ORDINARY IN-CONTACT PATH, NOT A CORNER CASE — read the two branches as equal
		/// partners. GroundDangerAt lifts every cell to at least its control-block baseline, that baseline
		/// stacks additively past 40 in a dense sector (DOCS/reference/influence-stack.md, Stage B), and the
		/// gate sits at 45 — so anywhere near a contested frontier the servable set is routinely EMPTY before
		/// a single weapon is counted. Add one contact kernel and the relieved cluster is at or above the evac
		/// entry threshold, which is to say: the valve fires precisely when a cluster is in a firefight, which
		/// is precisely when resupply matters most. The resulting design is deliberate and is the whole
		/// contract of this module's danger handling: GATE TO COMFORTABLE CELLS WHERE ANY EXIST, OTHERWISE
		/// APPROACH THE LEAST-BAD ONE AND ABORT ON THE TRUCK'S OWN READING. Treating the second branch as a
		/// rare fallback is how it ends up feeding ungated readings into gates that assume bounded input.</para>
		///
		/// <para>Two things a plain "drop everything over the threshold" gets wrong. First, a hard veto can
		/// empty the set, and an empty set is not a safe default — the truck falls through to a bare
		/// <c>continue</c> and PARKS on every profile except @experimental, because the idle-truck hunt that
		/// would otherwise catch it is behind an explicit BotType gate. Starving the resupply because the
		/// front is hot is a worse failure than approaching carefully, so when nothing is servable the least
		/// dangerous cluster that actually needs ammo is handed back anyway and the (undamped) evac entry test
		/// is left to stop the truck if it genuinely becomes too hot.</para>
		///
		/// <para>Second, FindUnitClusters applies no need gate at all, so a full-ammo rear cluster is a
		/// candidate with AmmoNeed 0. Need-descending ordering hides that while a needy cluster is present —
		/// but veto the needy one and the truck drives, confidently, to units that need nothing. Requiring
		/// real need makes "no servable cluster" mean what it says.</para></summary>
		static List<UnitCluster> SelectServableClusters(List<UnitCluster> clusters, int releaseLevel)
		{
			var needy = clusters.Where(c => NeedScore(c.AmmoNeed) > 0).ToList();
			if (needy.Count == 0)
				return needy;

			var servable = needy.Where(c => c.Danger < releaseLevel).ToList();
			if (servable.Count > 0)
			{
				// Mark AT the gate — this is the only place a cluster becomes trusted, so the flag cannot
				// claim more than the gate actually established.
				foreach (var c in servable)
					c.Gated = true;

				return servable;
			}

			// Relief valve: nothing is comfortably approachable, so fall back to the least dangerous needy
			// cluster(s) rather than abandoning resupply entirely. Ties are kept so the ordinary need-desc
			// selection still decides between equally-safe clusters; min over a list is order-independent.
			//
			// Deliberately NOT marked Gated: these clusters did not pass the gate, so Danger here is unbounded
			// and is routinely at or above the evac entry threshold. StepEvac therefore will not feed it to
			// the evac decision (SupplyLogisticsMath.DestinationDanger) and the truck approaches under its own
			// reading instead, which is exactly the contract in this method's summary. Nothing to set — the
			// default is the safe value.
			var minDanger = needy.Min(c => c.Danger);
			return needy.Where(c => c.Danger == minDanger).ToList();
		}

		List<UnitCluster> FindUnitClusters(List<Actor> units, int minFriendlies)
		{
			var clusters = new List<UnitCluster>();
			var assigned = new HashSet<Actor>();

			foreach (var unit in units)
			{
				if (assigned.Contains(unit))
					continue;

				// Find nearby units to form a cluster
				var nearby = units
					.Where(a => !assigned.Contains(a) && (a.CenterPosition - unit.CenterPosition).Length < WDist.FromCells(10).Length)
					.ToList();

				if (nearby.Count < minFriendlies)
					continue;

				// Calculate cluster center and ammo need
				var center = nearby.Select(a => a.CenterPosition).Average();
				var ammoNeed = 0f;

				foreach (var a in nearby)
				{
					var ammoPools = a.TraitsImplementing<AmmoPool>().ToArray();
					foreach (var pool in ammoPools)
					{
						if (pool.Info.Ammo > 0)
							ammoNeed += 1f - (float)pool.CurrentAmmoCount / pool.Info.Ammo;
					}
				}

				clusters.Add(new UnitCluster
				{
					Center = center,
					CenterCell = world.Map.CellContaining(center),
					UnitCount = nearby.Count,
					AmmoNeed = ammoNeed
				});

				foreach (var a in nearby)
					assigned.Add(a);
			}

			return clusters;
		}

		CPos? FindSafeFollowPosition(UnitCluster cluster)
		{
			if (threatMap == null)
				return cluster.CenterCell;

			// Find the safest cell near the cluster (behind the front line)
			var bestCell = cluster.CenterCell;
			var bestScore = float.MinValue;

			for (var dx = -3; dx <= 3; dx++)
			{
				for (var dy = -3; dy <= 3; dy++)
				{
					var cell = new CPos(cluster.CenterCell.X + dx, cluster.CenterCell.Y + dy);
					if (!world.Map.Contains(cell))
						continue;

					var threat = threatMap.GetThreat(cell, player);
					// Prefer cells with friendly advantage (negative threat) near the cluster
					var score = -threat;

					if (score > bestScore)
					{
						bestScore = score;
						bestCell = cell;
					}
				}
			}

			return bestCell;
		}

		// A ground-danger sampler bound to this player's own anti-ground channel. Off-map cells read
		// Impassable so a detour waypoint never lands off the playable area. Fog-legal by construction.
		Func<CPos, int> GroundDangerSampler()
		{
			var map = world.Map;
			return c => map.Contains(c) ? dangerField.GroundDanger(player, c) : GroundDangerNav.Impassable;
		}

		// A terrain-passability predicate bound to the truck's locomotor: true when it can actually stand
		// on the cell (not on-map water/cliff, not off-map). Rejects detour WAYPOINTS that read "safe"
		// only because unstamped impassable ground carries no danger. All-passable fallback if no Mobile.
		Func<CPos, bool> WaypointPassable(Actor mover)
		{
			var loco = mover.TraitOrDefault<Mobile>()?.Locomotor;
			if (loco == null)
				return _ => true;

			return c => loco.MovementCostForCell(c) != PathGraph.MovementCostForUnreachableCell;
		}

		// The player's own Supply Routes (our own actors — fog-legal to read), the safe rear an evacuating
		// truck pulls back toward. A player can hold SEVERAL (the starting beachhead plus any captured
		// neutral ones, per DOCS/reference/game-model.md), so this returns all of them and the caller picks
		// the nearest per truck — see NearestSupplyRoute. Ordered by ActorID for a deterministic tie-break.
		List<Actor> FindOwnSupplyRoutes()
		{
			return world.Actors
				.Where(a => a.Owner == player && !a.IsDead && a.IsInWorld
					&& Info.SupplyRouteTypes.Contains(a.Info.Name))
				.OrderBy(a => a.ActorID)
				.ToList();
		}

		// The nearest SR to a truck. Taking the FIRST one instead is a correctness bug, not a nicety: with
		// several SRs the first by spawn order can be on the far side of the map, and RetreatTarget steps
		// the truck TOWARD it — i.e. an "evacuation" that drives through or past the front, which the
		// undamped entry test then re-triggers the whole way. Strict `<` keeps the lowest ActorID on a tie,
		// so the choice is deterministic given the caller's ActorID ordering.
		static Actor NearestSupplyRoute(List<Actor> supplyRoutes, WPos from)
		{
			Actor best = null;
			var bestDistanceSquared = 0L;

			foreach (var sr in supplyRoutes)
			{
				var distanceSquared = (sr.CenterPosition - from).HorizontalLengthSquared;
				if (best == null || distanceSquared < bestDistanceSquared)
				{
					best = sr;
					bestDistanceSquared = distanceSquared;
				}
			}

			return best;
		}

		/// <summary>Believed ground danger at a cell, DE-ALIASED against the control-grid lattice. Every
		/// binary gate in this module reads the field through here so entry and exit cannot disagree.
		///
		/// <para>The Stage-B territory baseline is stamped only at each control-grid cell's CENTRE map cell —
		/// <c>ControlField.GridCellToMapCell(gx, gy) = (gx * CellSize + CellSize / 2, ...)</c> — so at the
		/// shipping CellSize of 2 only map cells with BOTH coordinates odd carry any baseline at all, and
		/// three of every four read zero. That baseline is not small: it stamps additively from every frontier
		/// cell and "a dense sector's baseline stacks and can exceed 40 easily"
		/// (DOCS/reference/influence-stack.md, Stage B). So near a contested frontier a single-cell read can
		/// swing by more than 40 on a ONE-CELL move purely on lattice parity — against a 15-unit release
		/// hysteresis, that is quantisation noise nearly 3x the deadband, and a threshold gate reading one
		/// cell decides on parity rather than on danger.</para>
		///
		/// <para>Taking the MAX over the cell and its grid-centre representative recovers the stamped baseline
		/// for every member of the block while keeping the densely-stamped contact kernel at the cell itself.
		/// MIN is simply unsafe — it reports the unstamped member, i.e. it under-reports danger, which is the
		/// wrong direction for a safety gate. A MEAN over the block would NOT reintroduce parity (a mean over
		/// a fixed block is uniform within it); the objection to it is different and is about the other term:
		/// it dilutes the densely-stamped CONTACT kernel across four cells, roughly quartering the local peak
		/// that the gate exists to notice. MAX is the only one of the three that preserves both terms.</para></summary>
		int GroundDangerAt(CPos cell)
		{
			var danger = dangerField.GroundDanger(player, cell);
			if (controlField == null)
				return danger;

			var (gx, gy) = controlField.MapCellToGridCell(cell);
			var representative = controlField.GridCellToMapCell(gx, gy);
			if (representative == cell)
				return danger;

			return Math.Max(danger, dangerField.GroundDanger(player, representative));
		}

		/// <summary>Run the DAMPED danger-evac decision for one truck and, when it is on the evac branch, issue
		/// the retreat. Returns true when the caller should skip the follow path this scan.
		///
		/// Believed ground danger (DangerFieldLayer) at the truck and at its target cluster centroid drives the
		/// decision — fog-legal by construction; dangerField is non-null only for participating profiles.
		///
		/// Two pieces of memory live here, and they are what stop the branch oscillating (the full reasoning is
		/// in SupplyLogisticsMath's EVAC DAMPER note):
		///   * the DWELL latches the branch for EvacDwellScans so a retreat already ordered is not re-decided
		///     while it is still being driven, and the RELEASE DEADBAND then requires danger to fall clear of
		///     the threshold before the truck follows again. Entering an evac is never delayed by either.
		///   * the retreat is issued ONE LEG AT A TIME. RetreatTarget steps EvacRetreatCells from the truck's
		///     OWN position, so recomputing it every scan is a receding target — at ~11 cells covered per
		///     150-tick scan against a 12-cell leg the truck never arrives and simply walks to the SR, which
		///     is the pre-fix failure verbatim. A leg is therefore issued once and then left alone until it
		///     has actually been driven (or the truck went idle because it could not be), at which point the
		///     next one is stepped. The dwell alone does NOT bound this: the counter arms on the entry edge,
		///     so from the scan after it expires every scan would re-issue.
		/// </summary>
		bool StepEvac(Actor truck, Actor srActor, UnitCluster cluster)
		{
			// An entry exists exactly while the truck is on the evac branch (it is removed on release below).
			var wasEvacuating = evacState.TryGetValue(truck, out var state);
			var heldBefore = wasEvacuating ? state.Hold : 0;

			var dangerAtTruck = GroundDangerAt(truck.Location);

			// The cell the truck is being SENT to — but read ONLY when that cell passed the danger gate this
			// scan. No cluster, or a cluster that came through the relief valve, contributes nothing: the
			// term exists to catch the front arriving on a GATED destination, and an ungated reading in the
			// entry test pins the branch true forever regardless of where the truck drives. The reasoning,
			// and why 0 is the valve's contract rather than a fudge, is on DestinationDanger.
			var dangerAtDestination = SupplyLogisticsMath.DestinationDanger(cluster?.Gated ?? false, cluster?.Danger ?? 0);

			var evacNow = SupplyLogisticsMath.EvacuateWithDwell(wasEvacuating, heldBefore,
				dangerAtTruck, dangerAtDestination, Info.EvacDangerThreshold, Info.EvacReleaseHysteresis);

			if (!evacNow)
			{
				// EXIT EDGE — unconditional, and only on the transition. Logging every non-evacuating scan
				// would bury the transitions that matter in a level nobody reads.
				if (wasEvacuating)
					Log.Write("debug",
						$"[supply] evac-exit truck={truck.ActorID}@{truck.Location} danger={dangerAtTruck} "
						+ $"release-level={SupplyLogisticsMath.ReleaseLevel(Info.EvacDangerThreshold, Info.EvacReleaseHysteresis)} "
						+ $"held={heldBefore}");

				evacState.Remove(truck);
				return false;
			}

			var hold = SupplyLogisticsMath.StepEvacDwell(heldBefore, !wasEvacuating, Info.EvacDwellScans);

			// The evac branch pre-empts any drop errand — its retreat Move cancels the activity chain — so the
			// dispatch record must go with it. Leaving it would suppress the re-issue once the truck cools,
			// stranding a truck that believes it is already on its way to a cell it is no longer driving to.
			// This is the one cancellation the drop branch cannot observe for itself, because the evac branch
			// runs first and returns before it.
			dropTarget.Remove(truck);

			// Step a new leg on entry, and thereafter only once the previous one has been driven. The arrival
			// tolerance reuses RepathThresholdCells, the same deadband the Stage-E detour uses one branch
			// over. IsIdle is the second half and is not optional: a truck whose Move failed (blocked cell,
			// no path) never reaches the target, and without it the leg model would strand it in the danger
			// it was trying to leave.
			var retreatCell = state.Retreat;
			var legDriven = !wasEvacuating
				|| truck.IsIdle
				|| (truck.Location - retreatCell).LengthSquared <= Info.RepathThresholdCells * Info.RepathThresholdCells;

			if (legDriven)
			{
				var retreat = SupplyLogisticsMath.RetreatTarget(
					truck.CenterPosition, srActor.CenterPosition, WDist.FromCells(Info.EvacRetreatCells).Length);
				retreatCell = world.Map.CellContaining(retreat);
				// The evacuation Move. Unmarked ⇒ Protected: a truck that cannot leave the danger it was
				// told to leave is far worse than one that dithers.
				bot.QueueOrder(new Order("Move", truck, Target.FromCell(world, retreatCell), false));
				lastVia.Remove(truck);
				lastFollow.Remove(truck);
			}

			// ENTRY EDGE unconditional; subsequent held/legged scans are a level, so they are gated. Both
			// carry `leg`, which is what makes the TWO-LEG retreat (the dwell holds the branch for one scan,
			// and the leg model then observes leg 1 as driven and issues leg 2) visible in a log rather than
			// only derivable on paper — see WORKSPACE/recon/260808-truck-post-fix-behaviour.md §1.3.
			if (!wasEvacuating)
				Log.Write("debug",
					$"[supply] evac-enter truck={truck.ActorID}@{truck.Location} danger={dangerAtTruck} "
					+ $"dest-danger={dangerAtDestination} gated={cluster?.Gated ?? false} "
					+ $"threshold={Info.EvacDangerThreshold} leg={retreatCell} sr={srActor.Location}");
			else if (Info.DebugLogging)
				Log.Write("debug",
					$"[supply] evac-hold truck={truck.ActorID}@{truck.Location} danger={dangerAtTruck} "
					+ $"held={heldBefore}→{hold} leg-driven={legDriven} leg={retreatCell}");

			evacState[truck] = new EvacState(hold, retreatCell);
			Adopt(truck);
			return true;
		}

		// Scale the float AmmoNeed to a stable non-negative integer for the deterministic sector assignment.
		// Only used on the @experimental spread path, so it never touches the byte-identical base ordering.
		static int NeedScore(float ammoNeed)
		{
			var s = (int)(ammoNeed * 1000f);
			return s < 0 ? 0 : s;
		}

		bool IsClaimedByOtherModule(Actor a)
		{
			if (blackboard == null)
				return false;

			var claimant = blackboard.GetUnitClaimant(a);
			return claimant != null && claimant != "supply-follow";
		}

		// A truck below its RestockThreshold — or one holding an unusable residue that counts as empty — has
		// effectively no supplies to give, so don't issue forward orders for it.
		//
		// CORRECTION (2026-08-07): the old comment here claimed "SupplyProvider's restock / the transport's
		// evacuate will route it away if we leave it alone". Only the second half is true for AI trucks. The
		// restock half is inert — ShouldSelfRestock (SupplyProvider.cs:330-338) returns false under
		// ResupplyBehavior.Evacuate, which is TRUK's AI default. Releasing the truck hands it to
		// DropsSupplyCache, which sends it to the map edge; it is NOT going to refill and come back.
		// A NOTE ON residueUnusable, WHICH THIS ALSO READS VIA CountsAsEmpty (SupplyProvider.cs:294). It is a
		// latch that can make a LOADED truck count as empty, which would drop it from the roster mid-errand
		// and hand it to DropsSupplyCache's map-edge evac still holding its load — the loop already recorded
		// separately in WORKSPACE/bugs/discovered.md (see 3effb1d2). It cannot reach the drop path, for two
		// independent reasons, both checked rather than assumed:
		//   * IT CANNOT ARM ON A DISPATCHED TRUCK. The residue latches only when every servable target is
		//     unaffordable, and a truck can only ever target actors holding its RearmCondition
		//     (replenish-soldiers), which vehicles and aircraft do not. The dearest pool on anything else in
		//     the mod costs 65; DropMinSupply is 250, so a truck eligible to be dispatched affords the
		//     costliest batch it could ever be asked for almost four times over. (The 1500-cost pools are on
		//     vehicles-america/russia, which a truck can never serve.)
		//   * AND IF IT DID, THE STALE RECORD COULD NOT BE READ. dropTarget is pruned against the freshly
		//     derived eligible-truck list — which this predicate filters — at the top of the scan, BEFORE
		//     any truck computes in-flight supply, so a latched truck's load cannot be counted as committed.
		// Re-check the first bullet if a soldier-servable pool is ever priced above DropMinSupply.
		static bool IsLowOnSupply(Actor a)
		{
			var sp = a.TraitOrDefault<SupplyProvider>();
			if (sp == null)
				return false;
			return sp.CurrentSupply < sp.Info.RestockThreshold || sp.CountsAsEmpty;
		}

		protected override void TraitDisabled(Actor self)
		{
			if (blackboard != null)
				foreach (var truck in activeTrucks)
					blackboard.ReleaseUnit(truck);

			activeTrucks.Clear();
			lastVia.Clear();
			lastFollow.Clear();
			evacState.Clear();
			dropAnchor.Clear();
			dropTarget.Clear();
			anchorRejectStreak.Clear();
		}

		// Danger-evac damper state for one truck on the evac branch: scans the branch is still committed for
		// (0 = free to re-decide next scan) and the retreat cell its current leg was ordered to. Presence in
		// the dictionary IS the "currently evacuating" flag.
		readonly struct EvacState
		{
			public readonly int Hold;
			public readonly CPos Retreat;

			public EvacState(int hold, CPos retreat)
			{
				Hold = hold;
				Retreat = retreat;
			}
		}

		class UnitCluster
		{
			public WPos Center;
			public CPos CenterCell;
			public int UnitCount;
			public float AmmoNeed;

			// The cell a truck assigned here would actually be SENT to, and the believed ground danger there.
			// Gating on the follow cell rather than the centroid matters: FindSafeFollowPosition scores a
			// +/-3 box by ThreatMapManager.GetThreat (enemyValue - friendlyValue), and MINIMISING that
			// deliberately prefers the friendliest-dense cell — i.e. it walks toward the contact line, up to
			// ~4 cells off the centroid. A centroid reading safe can therefore hand out a follow cell that is
			// not, which would put the veto and the destination on two different quantities.
			public CPos FollowCell;
			public int Danger;

			// True only when this cluster reached selection by PASSING the danger gate, so Danger is known to
			// sit below the gate level. The evac decision may read the destination of such a cluster and no
			// other; see SupplyLogisticsMath.DestinationDanger for why reading an ungated one is a latch.
			//
			// The polarity is deliberate and is the point: default false means "not known to be gated", so a
			// selection path that produces clusters and forgets to set this fails SAFE — the destination term
			// is ignored and the truck decides on its own reading. The opposite spelling (a Relieved flag
			// defaulting to false = trusted) makes forgetting it restore the latch silently, which is exactly
			// how the same defect got through twice. Set it where the gate is applied, nowhere else.
			public bool Gated;
		}
	}
}
