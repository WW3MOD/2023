#region Copyright & License Information
/*
 * WW3MOD LaneAmbushBotModule — experimental AI, PIPELINE item 8 Stage 4 (bot lane-ambush consumer).
 *
 * The strategic CONSUMER of the shipped widened-ambush machinery (Stages 1-3, gate condition
 * `enable-ambush-tactics`). Earlier stages made a HUMAN- or map-granted Ambush unit hide-and-spring;
 * this module is what makes the @experimental bot actually USE that machinery: it posts a small number
 * of suitable units into concealed Ambush positions on the corridor between our beachhead and the
 * enemy's — the lane their reinforcements/attackers march down — and grants each posted unit the
 * `enable-ambush-tactics` gate so the Stage-2 halt-before-contact + Stage-3 stationary state machine run.
 *
 * WHY IT LOOKS LIKE PoiGarrisonBotModule: the plumbing is the same score-floating, ledger-arbitrated
 * pattern (build a free pool of uncommitted units, claim a few through the shared PoiGoalGuard ledger,
 * order them, leave them alone until released). Deliberately kept SMALL (MaxAmbushes x UnitsPerAmbush)
 * so it never starves PoiOffensiveBotModule — the same "a handful of units, the rest stay with offense"
 * budget as the garrison. The competition between offense / garrison / capture / ambush is emergent
 * through the ONE shared ledger, not a priority ladder.
 *
 * THREE things this module MUST get right (all carried OBS from the Stage 1-3 ships):
 *   * OBS-1 — the halt/spring gate is wired only on the ^AutoTarget family. The ^AutoTargetGround*
 *     family (AA IFVs via ^AutoTargetAAIFV, all assault-move ground vehicles) has a SEPARATE AutoTarget
 *     block with NO AmbushTacticsCondition and NO ambushtactics ExternalCondition seam, so granting the
 *     gate does nothing for them. CanHostAmbush() filters on "the unit's AutoTargetInfo carries a
 *     non-empty AmbushTacticsCondition AND it has a grantable ExternalCondition for that token", which
 *     excludes that whole family automatically (and self-heals if someone wires the gate onto them later).
 *   * OBS-2 — a bot-owned granted unit WITHOUT a ledger commit oscillates on the ~75-tick squad re-issue
 *     cadence. Every posted unit is committed "ambush:<anchorId>" in PoiGoalGuard.Ledger, so the offense
 *     FSM treats it as taken and never stomps the posting. THIS is the fix Stage 4 is required to carry.
 *   * OBS-3 — a shift-queued follow-up order runs on halt and walks the unit out of ambush. We only ever
 *     issue a single queued:false AttackMove (plus an immediate SetUnitStance) — never a shift-queue.
 *
 * SPRUNG is terminal until stance reset (design §5.2): once a posted unit fires, AutoTarget.AmbushSprung
 * latches true and it will never re-conceal on its own. Each re-eval releases every sprung unit back to
 * normal tasking (revoke the gate, reset it to FireAtWill — which also clears the latch — and drop the
 * ledger commit) so offense reclaims a fresh, un-latched unit. Nothing is left latched forever.
 *
 * DETERMINISM: zero RNG. The initial re-eval offset is a fixed constant (NOT a LocalRandom draw), every
 * actor iteration is ordered by ActorID, and the only per-unit state read from the sim (AmbushSprung) is
 * the Stage-3 latch, which evolves by pure integer/bool math over synced world state (deterministic
 * across clients). Lane geometry is integer WPos interpolation in the pure AmbushLaneMath helper.
 *
 * BYTE-IDENTITY: gated `enable-ai-experimental` with NO @stable twin — @stable / Normal / Rush / Turtle /
 * humans never instantiate it, so they never commit to a ledger, grant a condition, or issue an order
 * from here. The `enable-ambush-tactics` gate is granted ONLY by this module, so on every non-experimental
 * profile GetConditionCount stays 0 and the Stage-2/3 machinery is the same dead code it was at ship.
 * Gated enable-ai-experimental in ai.yaml — Normal / Rush / Turtle / Stable never see it.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Pathfinder;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.Player)]
	[Desc("WW3MOD experimental AI: posts a few suitable units into concealed Ambush positions on the",
		"corridor between our beachhead and the enemy's (the reinforcement/approach lane) and grants them",
		"the enable-ambush-tactics gate so the shipped Stage-2/3 ambush machinery runs. Claims units through",
		"the shared PoiGoalGuard ledger (objective ambush:<id>) so the offense FSM never stomps a posting;",
		"releases sprung units back to normal tasking. Deliberately small so it never starves offense.",
		"Gate enable-ai-experimental.")]
	public class LaneAmbushBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Ticks between ambush re-evaluations. Slow cadence + the per-unit commitment TTL give",
			"hysteresis so ambushers aren't reshuffled every scan.")]
		public readonly int ReevaluateInterval = 100;

		[Desc("Hard cap on how many ambush lanes are run concurrently (one per top enemy anchor). Bounds",
			"the units this module ties up = MaxAmbushes x UnitsPerAmbush; keep small so offense keeps the rest.")]
		public readonly int MaxAmbushes = 2;

		[Desc("Units posted per ambush lane. Small — a lane ambush is a few pieces, not the main army.")]
		public readonly int UnitsPerAmbush = 2;

		[Desc("Where along the friendly-SR -> enemy-SR line to post the ambush, as a percent of the way",
			"from OUR beachhead toward the enemy's. Below 50 keeps the post on our side of the midline —",
			"concealed in our own territory, on the corridor attackers commit down. Clamped [0,100].")]
		public readonly int PostFractionPct = 40;

		[Desc("Minimum separation (cells) between our beachhead and an enemy anchor for a lane to be viable.",
			"Guards the degenerate case where the two beachheads are basically adjacent and the post cell",
			"would sit on top of our own base.")]
		public readonly int MinLaneSeparationCells = 12;

		[Desc("Chebyshev search radius (cells) for snapping the interpolated post position to a cell the",
			"ambusher can actually stand on (reuses FiresStandoffMath.NearestPassableCell).")]
		public readonly int PostCellClampCells = 6;

		[Desc("Commitment lifetime (ticks) for a unit posted to an ambush. While committed the unit holds",
			"its lane and is invisible to capture/offense/garrison. Refreshed each re-eval, so it must",
			"exceed ReevaluateInterval.")]
		public readonly int AmbushCommitmentTicks = 250;

		[Desc("Re-issue the AttackMove-to-post only if the post cell moved by at least this many cells (or",
			"the unit set changed). Anchors are stationary, so this is mostly set-change only.")]
		public readonly int RepathThresholdCells = 3;

		[Desc("Actor types NEVER posted as ambushers (capturers, supply trucks, IFV carriers — owned by",
			"CaptureCoordinator / SupplyFollower / MountedTransport). Aircraft are excluded automatically",
			"by trait. Mirror PoiOffensiveBotModule's ExcludeUnitTypes.")]
		public readonly HashSet<string> ExcludeUnitTypes = new HashSet<string>();

		[Desc("Derive free-pool eligibility from UnitRoleResolver (role MainBattle or IndirectFire) instead",
			"of the ExcludeUnitTypes name list — same filter as PoiOffensiveBotModule. Default false.")]
		public readonly bool UseUnitRoles = false;

		public override object Create(ActorInitializer init) { return new LaneAmbushBotModule(init.Self, this); }
	}

	public class LaneAmbushBotModule : ConditionalTrait<LaneAmbushBotModuleInfo>, IBotTick
	{
		// A live ambush lane: an enemy anchor, the concealed post cell on the corridor to it, and the units
		// holding it. Persists across re-evals so ambushers aren't reshuffled every scan (hysteresis).
		sealed class Lane
		{
			public uint AnchorId;
			public CPos AnchorCell;
			public CPos PostCell;
			public long Score;
			public string AnchorName;
			public CPos OrderedCell;   // last cell we AttackMoved to (for repath gating)
			public bool HasOrdered;
			public readonly List<Actor> Units = new();
		}

		readonly World world;
		readonly Player player;

		PoiMap poiMap;
		bool poiMapResolved;
		PoiGoalGuard goalGuard;
		bool goalGuardResolved;
		UnitRoleResolver resolver;
		bool resolverResolved;

		readonly List<Lane> lanes = new();

		// The enable-ambush-tactics grant we hold per posted unit, so it can be revoked precisely on release.
		readonly Dictionary<Actor, (ExternalCondition Ec, int Token)> gateGrants = new();

		int reevalCountdown;

		public LaneAmbushBotModule(Actor self, LaneAmbushBotModuleInfo info)
			: base(info)
		{
			world = self.World;
			player = self.Owner;
		}

		protected override void TraitEnabled(Actor self)
		{
			// Deterministic initial offset (NOT a LocalRandom draw — zero RNG, so @stable/control games that
			// never instantiate this module keep their SharedRandom stream untouched, and the initial phase is
			// reproducible). One interval in so units exist to post by the first eval.
			reevalCountdown = Info.ReevaluateInterval;
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (player.WinState != WinState.Undefined)
				return;

			if (--reevalCountdown > 0)
				return;

			reevalCountdown = Info.ReevaluateInterval;
			Reevaluate(bot);
		}

		void Reevaluate(IBot bot)
		{
			if (!poiMapResolved)
			{
				poiMap = world.WorldActor.TraitOrDefault<PoiMap>();
				poiMapResolved = true;
			}

			if (poiMap == null)
				return;

			if (!goalGuardResolved)
			{
				goalGuard = player.PlayerActor.TraitOrDefault<PoiGoalGuard>();
				goalGuardResolved = true;
			}

			if (!resolverResolved)
			{
				resolver = world.WorldActor.TraitOrDefault<UnitRoleResolver>();
				resolverResolved = true;
			}

			var tick = world.WorldTick;

			// 1. Drop dead/lost/reclaimed AND already-SPRUNG units from lanes; sweep orphan ambush commitments.
			PruneLanes(bot);
			if (goalGuard != null)
				goalGuard.Ledger.Prune(tick, a => !a.IsDead && a.IsInWorld && a.Owner == player);

			// 2. Friendly anchor (our beachhead). No home ⇒ nothing to draw a lane from.
			var ownSr = poiMap.OwnSupplyRoute(player);
			if (ownSr == null)
			{
				RetireAll(bot, "no-home");
				return;
			}

			var homePos = ownSr.CenterPosition;

			// 3. Enemy anchors = the reinforcement sources. Prefer the enemy Supply Route(s) (Pressure); fall
			//    back to enemy income/utility (Attack) when no SR is an offensive target yet. Already score-sorted.
			var offensive = poiMap.GetOffensiveTargets(player);
			var anchors = offensive.Where(p => p.Action == PoiAction.Pressure).ToList();
			if (anchors.Count == 0)
				anchors = offensive.Where(p => p.Action == PoiAction.Attack).ToList();

			// Only keep anchors that yield a VIABLE lane (far enough from home), best first, capped.
			var viable = new List<ScoredPoi>();
			foreach (var a in anchors)
			{
				var sepCells = (a.CenterPosition - homePos).Length / 1024;
				if (AmbushLaneMath.LaneIsViable(sepCells, Info.MinLaneSeparationCells))
					viable.Add(a);
				if (viable.Count >= Info.MaxAmbushes)
					break;
			}

			if (viable.Count == 0)
			{
				RetireAll(bot, "no-viable-lane");
				return;
			}

			// 4. Retire lanes whose anchor is no longer a target. The freed units are released (gate revoked,
			//    stance reset, ledger commitment dropped), so BuildFreePool below re-includes them naturally —
			//    do NOT also seed them into `free` here, or a unit could land in `free` twice and be posted to
			//    two lanes in one eval.
			var keepIds = new HashSet<uint>(viable.Select(v => v.Actor.ActorID));
			for (var i = lanes.Count - 1; i >= 0; i--)
			{
				if (!keepIds.Contains(lanes[i].AnchorId))
				{
					ReleaseLane(bot, lanes[i], "anchor-lost");
					lanes.RemoveAt(i);
				}
			}

			// 5. Ensure a lane per anchor; refresh its post cell (interpolate + snap to a passable cell).
			foreach (var v in viable)
			{
				var lane = lanes.FirstOrDefault(x => x.AnchorId == v.Actor.ActorID);
				if (lane == null)
				{
					lane = new Lane { AnchorId = v.Actor.ActorID };
					lanes.Add(lane);
				}

				var postPos = AmbushLaneMath.PostPosition(homePos, v.CenterPosition, Info.PostFractionPct);
				var ideal = world.Map.CellContaining(postPos);
				lane.PostCell = FiresStandoffMath.NearestPassableCell(ideal, Info.PostCellClampCells, PassableForAnyAmbusher());
				lane.AnchorCell = v.Location;
				lane.Score = v.Score;
				lane.AnchorName = v.Actor.Info.Name;
			}

			// 6. Fill each lane to UnitsPerAmbush from the uncommitted pool, nearest the post cell first.
			//    Units shed from a lane below are added back to `free` so a later lane can reclaim them; they
			//    were in a lane at BuildFreePool time so they aren't already in the list (no duplication).
			var free = BuildFreePool();
			var ordered = lanes.OrderByDescending(l => l.Score).ThenBy(l => l.AnchorId).ToList();

			foreach (var lane in ordered)
			{
				// Shed surplus (farthest from the post cell) back to the pool.
				if (lane.Units.Count > Info.UnitsPerAmbush)
				{
					var surplus = lane.Units
						.OrderByDescending(u => (u.CenterPosition - world.Map.CenterOfCell(lane.PostCell)).LengthSquared)
						.ThenByDescending(u => u.ActorID)
						.Take(lane.Units.Count - Info.UnitsPerAmbush)
						.ToList();
					foreach (var u in surplus)
					{
						lane.Units.Remove(u);
						ReleaseUnit(bot, u, resetStance: true);
						free.Add(u);
						lane.HasOrdered = false; // set changed
					}
				}

				var need = Info.UnitsPerAmbush - lane.Units.Count;
				if (need <= 0)
					continue;

				var postPos = world.Map.CenterOfCell(lane.PostCell);
				var recruits = free
					.OrderBy(u => (u.CenterPosition - postPos).LengthSquared)
					.ThenBy(u => u.ActorID)
					.Take(need)
					.ToList();

				foreach (var u in recruits)
				{
					free.Remove(u);
					lane.Units.Add(u);
					lane.HasOrdered = false; // set changed
				}
			}

			// 7. Issue orders + (re)commit + grant the gate. Retire any lane that ended up empty.
			for (var i = lanes.Count - 1; i >= 0; i--)
			{
				var lane = lanes[i];
				if (lane.Units.Count == 0)
				{
					lanes.RemoveAt(i);
					continue;
				}

				CommitAndOrder(bot, lane, tick);
			}

			Log.Write("debug",
				$"[exp-ambush] reeval player={player.PlayerName} anchors={viable.Count} lanes={lanes.Count} free={free.Count} tick={tick}");
			foreach (var lane in lanes)
				Log.Write("debug",
					$"[exp-ambush] lane player={player.PlayerName} anchor={lane.AnchorName}#{lane.AnchorId} post={lane.PostCell} units={lane.Units.Count} tick={tick}");
		}

		// Remove units that died / changed owner / lost their ambush commitment, AND release any unit that
		// has already SPRUNG (fired) — a sprung unit is latched terminal until stance reset, so we hand it
		// back to normal tasking rather than leaving it dangling in the lane forever (design §5.2).
		void PruneLanes(IBot bot)
		{
			foreach (var lane in lanes)
			{
				var key = AmbushObjectiveKey(lane.AnchorId);
				for (var i = lane.Units.Count - 1; i >= 0; i--)
				{
					var u = lane.Units[i];

					if (u.IsDead || !u.IsInWorld || u.Owner != player)
					{
						lane.Units.RemoveAt(i);
						ReleaseUnit(bot, u, resetStance: false); // gone / not ours — just drop our bookkeeping
						lane.HasOrdered = false;
						continue;
					}

					// A committed-but-reclaimed unit (some other module now owns it) leaves quietly.
					if (goalGuard != null
						&& goalGuard.Ledger.TryGetObjective(u, out var obj)
						&& obj != null
						&& obj != key
						&& obj.StartsWith("ambush:", StringComparison.Ordinal))
					{
						lane.Units.RemoveAt(i);
						ReleaseUnit(bot, u, resetStance: false);
						lane.HasOrdered = false;
						continue;
					}

					// SPRUNG: it fired its ambush. Release to normal tasking (revoke gate + FireAtWill resets
					// the terminal latch) so offense reclaims a fresh, un-latched unit.
					var at = u.TraitOrDefault<AutoTarget>();
					if (at != null && at.AmbushSprung)
					{
						lane.Units.RemoveAt(i);
						ReleaseUnit(bot, u, resetStance: true);
						lane.HasOrdered = false;
					}
				}
			}
		}

		void CommitAndOrder(IBot bot, Lane lane, int tick)
		{
			var key = AmbushObjectiveKey(lane.AnchorId);
			foreach (var u in lane.Units)
			{
				// (Re)commit so the shared ledger keeps the unit ours (OBS-2: without this the ~75-tick
				// offense re-issue stomps the posting and the unit oscillates).
				goalGuard?.Ledger.Commit(u, key, tick, Info.AmbushCommitmentTicks);

				// Grant the enable-ambush-tactics gate (once) and set Ambush stance so Stages 2/3 activate.
				EnsureGatedAmbusher(bot, u);
			}

			// Only (re)issue the AttackMove when the unit set changed or the post cell moved (anchors are
			// stationary, so this is essentially set-change only). ONE queued:false order — never a
			// shift-queue past the posting (OBS-3).
			var moved = !lane.HasOrdered
				|| (lane.OrderedCell - lane.PostCell).LengthSquared >= Info.RepathThresholdCells * Info.RepathThresholdCells;
			if (!moved)
				return;

			var units = lane.Units.ToArray();
			bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(world, lane.PostCell), false, groupedActors: units));
			lane.OrderedCell = lane.PostCell;
			lane.HasOrdered = true;

			AIUtils.BotDebug("AI ({0}): exp-ambush — post {1} unit(s) on lane to {2}@{3} (cell {4})",
				player.ClientIndex, units.Length, lane.AnchorName, lane.AnchorCell, lane.PostCell);
		}

		// Grant the enable-ambush-tactics gate to a posted unit (idempotent) and set it to Ambush stance.
		void EnsureGatedAmbusher(IBot bot, Actor u)
		{
			if (!gateGrants.ContainsKey(u))
			{
				var at = u.Info.TraitInfoOrDefault<AutoTargetInfo>();
				var gate = at?.AmbushTacticsCondition;
				if (!string.IsNullOrEmpty(gate))
				{
					var ec = u.TraitsImplementing<ExternalCondition>()
						.FirstOrDefault(e => e.Info.Condition == gate && e.CanGrantCondition(this));
					if (ec != null)
					{
						var token = ec.GrantCondition(u, this);
						if (token != Actor.InvalidConditionToken)
							gateGrants[u] = (ec, token);
					}
				}
			}

			// Put it in Ambush so the (now-gated) Stage-2 halt + Stage-3 machine run. SetUnitStance is applied
			// immediately in AutoTarget.ResolveOrder and touches no activity — safe to co-issue with AttackMove.
			var atTrait = u.TraitOrDefault<AutoTarget>();
			if (atTrait != null && atTrait.Stance != UnitStance.Ambush)
				bot.QueueOrder(new Order("SetUnitStance", u, false) { ExtraData = (uint)UnitStance.Ambush });
		}

		// Release one unit: revoke its gate, drop its ledger commit, and optionally reset it to FireAtWill
		// (which also clears the terminal SPRUNG latch via AutoTarget.ResetAmbushState) so it re-enters the
		// normal offense pool clean.
		void ReleaseUnit(IBot bot, Actor u, bool resetStance)
		{
			if (gateGrants.TryGetValue(u, out var g))
			{
				if (!u.IsDead && u.IsInWorld)
					g.Ec.TryRevokeCondition(u, this, g.Token);
				gateGrants.Remove(u);
			}

			goalGuard?.Ledger.Release(u);

			if (resetStance && !u.IsDead && u.IsInWorld && u.Owner == player)
			{
				var at = u.TraitOrDefault<AutoTarget>();
				if (at != null && at.Stance == UnitStance.Ambush)
					bot.QueueOrder(new Order("SetUnitStance", u, false) { ExtraData = (uint)UnitStance.FireAtWill });
			}
		}

		List<Actor> ReleaseLane(IBot bot, Lane lane, string reason)
		{
			var freed = new List<Actor>(lane.Units);
			foreach (var u in lane.Units)
				ReleaseUnit(bot, u, resetStance: true);
			lane.Units.Clear();

			if (freed.Count > 0)
				Log.Write("debug",
					$"[exp-ambush] retire player={player.PlayerName} anchor={lane.AnchorName} freed={freed.Count} reason={reason} tick={world.WorldTick}");
			return freed;
		}

		void RetireAll(IBot bot, string reason)
		{
			foreach (var lane in lanes)
				ReleaseLane(bot, lane, reason);
			lanes.Clear();
		}

		List<Actor> BuildFreePool()
		{
			var tick = world.WorldTick;
			var claimed = new HashSet<Actor>(lanes.SelectMany(l => l.Units));

			return world.Actors
				.Where(a => IsEligibleAmbusher(a)
					&& !claimed.Contains(a)
					&& (goalGuard == null || !goalGuard.Ledger.IsCommitted(a, tick)))
				.OrderBy(a => a.ActorID)
				.ToList();
		}

		bool IsEligibleAmbusher(Actor a)
		{
			if (a.Owner != player || a.IsDead || !a.IsInWorld)
				return false;
			if (!a.Info.HasTraitInfo<IPositionableInfo>() || !a.Info.HasTraitInfo<AttackBaseInfo>())
				return false;
			if (a.Info.HasTraitInfo<AircraftInfo>())
				return false;

			// The unit must be able to actually HOST the ambush machinery — this is the OBS-1 filter that
			// drops the ^AutoTargetGround* family (AA IFVs + assault-move vehicles) that never halt/spring.
			if (!CanHostAmbush(a))
				return false;

			if (Info.UseUnitRoles && resolver != null)
			{
				var role = resolver.GetRole(a);
				return (role == UnitRole.MainBattle || role == UnitRole.IndirectFire)
					&& !UnitRoleResolver.IsTroopCarrier(a.Info);
			}

			return !Info.ExcludeUnitTypes.Contains(a.Info.Name);
		}

		// OBS-1: a unit can host a widened ambush only if its AutoTargetInfo carries a non-empty
		// AmbushTacticsCondition (the Stage-2/3 gate) AND it has a grantable ExternalCondition seam for that
		// token. The ^AutoTargetGround* family has neither, so it is excluded here rather than posted and
		// silently failing to halt/spring. Self-healing: wire the gate onto a new template and it qualifies.
		bool CanHostAmbush(Actor a)
		{
			var at = a.Info.TraitInfoOrDefault<AutoTargetInfo>();
			var gate = at?.AmbushTacticsCondition;
			if (string.IsNullOrEmpty(gate))
				return false;

			foreach (var ec in a.TraitsImplementing<ExternalCondition>())
				if (ec.Info.Condition == gate && ec.CanGrantCondition(this))
					return true;

			return false;
		}

		// A terrain-passability predicate that accepts a cell if SOME plausible ground ambusher can stand on
		// it (any Mobile actor we own). Used only to snap the interpolated post cell to reachable ground; the
		// individual recruit's own pathfinder does the real routing. Falls back to "all passable" if we have
		// no mover to sample (never over-rejects). Deterministic (ActorID order).
		Func<CPos, bool> PassableForAnyAmbusher()
		{
			var loco = world.Actors
				.Where(a => a.Owner == player && !a.IsDead && a.IsInWorld && a.TraitOrDefault<Mobile>() != null)
				.OrderBy(a => a.ActorID)
				.Select(a => a.TraitOrDefault<Mobile>()?.Locomotor)
				.FirstOrDefault(l => l != null);

			if (loco == null)
				return _ => true;

			return c => loco.MovementCostForCell(c) != PathGraph.MovementCostForUnreachableCell;
		}

		static string AmbushObjectiveKey(uint anchorId) => "ambush:" + anchorId;
	}

	// ============================================================
	// Pure lane geometry — engine-free, unit-tested (AmbushLaneMathTest). Ports to v3.
	// ============================================================
	public static class AmbushLaneMath
	{
		/// <summary>The concealed post position on the corridor between our beachhead and an enemy anchor:
		/// a point <paramref name="fractionPct"/> percent of the way from <paramref name="friendly"/> toward
		/// <paramref name="enemy"/>. Below 50% keeps the post on OUR side of the midline — hidden in our own
		/// territory, on the lane attackers commit down. Integer per-axis interpolation (deterministic
		/// truncation toward zero); the long cast guards the (delta x percent) product against 32-bit
		/// overflow on large maps. fractionPct is clamped to [0,100].</summary>
		public static WPos PostPosition(WPos friendly, WPos enemy, int fractionPct)
		{
			var f = Math.Clamp(fractionPct, 0, 100);
			var x = friendly.X + (int)((long)(enemy.X - friendly.X) * f / 100);
			var y = friendly.Y + (int)((long)(enemy.Y - friendly.Y) * f / 100);
			var z = friendly.Z + (int)((long)(enemy.Z - friendly.Z) * f / 100);
			return new WPos(x, y, z);
		}

		/// <summary>A lane is viable only when our beachhead and the enemy anchor are at least
		/// <paramref name="minSeparationCells"/> apart — otherwise the interpolated post cell sits on top of
		/// our own base and "ambushing" it is meaningless. minSeparationCells is floored at 0.</summary>
		public static bool LaneIsViable(int anchorSeparationCells, int minSeparationCells)
		{
			return anchorSeparationCells >= Math.Max(0, minSeparationCells);
		}
	}
}
