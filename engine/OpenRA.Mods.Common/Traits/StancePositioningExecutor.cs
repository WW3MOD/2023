#region Copyright & License Information
/*
 * WW3MOD strategic/tactical split — Phase 2 (§4 under the §2 contract).
 *
 * The STANCE-CONDITIONED POSITIONING EXECUTOR. This is the first L3 (tactical)
 * unit trait: it reads a unit's Engagement stance and the Phase-1 map layers
 * (SightingThreatLayer + TerrainAffordanceLayer) and, when the unit is IDLE,
 * nudges it to a threat-facing cover-edge cell within a bounded leash.
 *
 *   Hunt        — take the forward cover edge toward the enemy (step +1 beyond
 *                 the treeline); may creep forward, always leashed.
 *   Defensive   — take the threat-facing edge cell itself (hull-down, concealed,
 *                 static line of fire toward the enemy).
 *   HoldPosition — no autonomous repositioning at all.
 *
 * Design is the hardened brief in WORKSPACE/plans/260722_phase2_redteam.md
 * ("Hardened implementation brief") + the SPEC §7 Phase-2 amendment. The rules
 * that shape every branch below (why they exist, not just what they do):
 *
 *   S5  IDLE-ONLY. Evaluate only in TickIdle with CurrentActivity == null and a
 *       cooldown. NEVER touch a moving unit; no in-transit detours in v1. A fresh
 *       explicit order therefore aborts us for free (it replaces the queue).
 *   B1  LEDGER. Bot-owned units commit a "tacpos:<actorId>" claim in the owner's
 *       PoiGoalGuard.Ledger so the Poi stack (and the GroundStates re-fire filter)
 *       leaves them alone mid-adjustment. Humans skip the ledger — no bot layer
 *       contests them.
 *   B2  SLOT OWNERSHIP. On repositioning we re-Assign the unit's CohesionSlotMemory
 *       slot to our chosen cell, so return-to-slot REINFORCES our choice instead
 *       of a 750-tick tug-of-war. Cleared on abort.
 *   B3  THREAT BEARING. Facing comes from an AGGREGATE scan over ActiveCells near
 *       the anchor (never a single-cell read — opposite-axis cancellation reads
 *       identically to no-data), gated on MinThreatIntensity + a direction-
 *       ambiguity ratio, with a fallback chain (last accepted bearing → toward
 *       commanded destination → stay put).
 *   S1  STANCE re-read every evaluation, never cached — L2 legitimately rewrites it.
 *   S4  SUPPRESSION gate: never issue a move above the prone threshold (a move
 *       stands the unit up out of prone and crawls it at up to -90% speed).
 *   S7  LEASH pinned: anchor = last commanded destination (or the assigned cohesion
 *       slot for grouped orders), captured ONCE per idle episode; our own moves
 *       never re-anchor (Hunt creep stays bounded).
 *   S8  DETERMINISM: this trait queues activities directly on every client, with no
 *       bot-order laundering, so it must NEVER call LocalRandom. SharedRandom is
 *       used only for a one-time stagger; every choice among equals resolves by an
 *       integer total-order tie-break (CoverQuality desc, angular error asc,
 *       (Y,X) asc, ActorID asc). Aggregates are additive so iteration order is moot.
 *   N4  Ops-layer surface: public AdjustmentState + CurrentTarget, and the reserved
 *       "tacpos:" ledger key grammar, so a future operations layer can inspect and
 *       preempt tactical claims without a retrofit.
 *
 * Gated `enable-tactical-positioning || enable-ai-experimental`: default-off
 * everywhere except experimental bots (the former is granted by nothing in Phase 2;
 * humans get it in Phase 3). @stable/@normal/humans are byte-identical.
 */
#endregion

using System.Collections.Generic;
using OpenRA.Mods.Common.Activities;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("WW3MOD Phase 2 (§4): stance-conditioned idle repositioning toward threat-facing cover.",
		"Reads SightingThreatLayer + TerrainAffordanceLayer; acts only in TickIdle. Bot-owned units",
		"claim a `tacpos:` commitment in PoiGoalGuard.Ledger. Default-off (experimental bots only).")]
	public class StancePositioningExecutorInfo : ConditionalTraitInfo, Requires<MobileInfo>
	{
		[Desc("Max cells the chosen cell may be from the leash ANCHOR (last commanded destination /",
			"assigned cohesion slot). Bounds Hunt's creep. Keep <= the Phase-0 formation footprint.")]
		public readonly int LeashRadius = 4;

		[Desc("Ticks between idle re-evaluations. Coprime-ish with the 25/50/75/100 bot cadences",
			"so evaluations don't phase-lock onto a re-fire.")]
		public readonly int EvaluateCooldown = 30;

		[Desc("How far (cells) around the anchor to aggregate the sighting field for the threat",
			"bearing. Should cover the leash plus the sighting ContributionRadius.")]
		public readonly int ThreatScanRadius = 8;

		[Desc("B3 gate: minimum summed enemy sighting intensity in the scan before a bearing is",
			"trusted. Below this we fall back (last bearing / no move) rather than face noise.")]
		public readonly int MinThreatIntensity = 40;

		[Desc("B3 direction-ambiguity gate numerator. A bearing is trusted only when",
			"|dir|^2 * DirectionAmbiguityDen >= sumIntensity^2 * DirectionAmbiguityNum, i.e. the",
			"summed direction vector is coherent, not near-cancelled on opposite axes.")]
		public readonly int DirectionAmbiguityNum = 1;

		[Desc("B3 direction-ambiguity gate denominator (see DirectionAmbiguityNum).")]
		public readonly int DirectionAmbiguityDen = 4;

		[Desc("Ticks a last-accepted bearing stays usable as a fallback when the live scan is",
			"below threshold (B3 fallback (a)).")]
		public readonly int BearingMemoryTicks = 150;

		[Desc("S4 gate: skip repositioning while this suppression value is exceeded (a move breaks",
			"prone and crawls the unit at up to -90% speed). Matches the prone trigger.")]
		public readonly int MaxSuppressionToMove = 30;

		[Desc("Name of the variable-condition that carries the unit's suppression level (S4).")]
		public readonly string SuppressionVariable = "suppressed";

		[Desc("Phase-3 opt-out (S2): name of the variable-condition set while the unit is deployed.",
			"A deployed unit expresses a stronger positional intent than a move order, so the executor",
			"never repositions it. Read via a variable observer (not RequiresCondition) so units that",
			"never grant it are simply inert — no per-actor grantor lint burden.")]
		public readonly string DeployedVariable = "deployed";

		[Desc("S2 edge-facing tolerance in WAngle units (1024 = full circle; 256 = 90 degrees).",
			"An edge cell counts as threat-facing when its OutwardFacing is within this of the bearing.")]
		public readonly int FacingToleranceAngle = 256;

		[Desc("TTL (ticks) of the `tacpos:` ledger claim for bot-owned units (B1). Re-committed each",
			"evaluation while managing, so it never lapses under us; released on abort/disengage.")]
		public readonly int ClaimTicks = 150;

		public override object Create(ActorInitializer init) { return new StancePositioningExecutor(init.Self, this); }
	}

	public class StancePositioningExecutor : ConditionalTrait<StancePositioningExecutorInfo>, INotifyIdle, ITick, ISync
	{
		// N4: queryable per-unit state for the future operations layer / event bus.
		public enum AdjustmentState { None, Adjusting, Arrived, Aborted }

		readonly Actor self;
		readonly Mobile mobile;

		AutoTarget autoTarget;
		CohesionSlotMemory slotMemory;
		SightingThreatLayer threatLayer;
		TerrainAffordanceLayer affordanceLayer;

		[Sync]
		int nextEvalTick;

		[Sync]
		int currentSuppression;

		[Sync]
		int currentDeployed;

		// S6: the ledger we committed a `tacpos:` claim to, cached so Release reaches the ORIGINAL
		// owner's ledger even after a bot→human capture flips self.Owner (the claim would otherwise
		// leak until TTL). Null for human-owned units — they never commit.
		PoiGoalGuard committedGuard;

		[Sync]
		WAngle lastAcceptedBearing;
		int lastBearingTick = int.MinValue;
		bool hasBearingMemory;

		[Sync]
		CPos anchor;

		[Sync]
		bool hasAnchor;

		[Sync]
		CPos currentTarget;

		[Sync]
		bool hasTarget;

		[Sync]
		bool claimed;

		public AdjustmentState State { get; private set; } = AdjustmentState.None;

		// OpenRA's Sync hasher rejects enum types (Sync.cs:71 — only int/bool/registered structs),
		// so sync the int projection rather than the enum property itself.
		[Sync]
		int SyncState => (int)State;

		public CPos? CurrentTarget => hasTarget ? currentTarget : (CPos?)null;

		public StancePositioningExecutor(Actor self, StancePositioningExecutorInfo info)
			: base(info)
		{
			this.self = self;
			mobile = self.Trait<Mobile>();
		}

		protected override void Created(Actor self)
		{
			base.Created(self);

			autoTarget = self.TraitOrDefault<AutoTarget>();
			slotMemory = self.TraitOrDefault<CohesionSlotMemory>();
			threatLayer = self.World.WorldActor.TraitOrDefault<SightingThreatLayer>();
			affordanceLayer = self.World.WorldActor.TraitOrDefault<TerrainAffordanceLayer>();

			// S8: one-time stagger via SharedRandom (synced). Spreads first evaluations so a wave
			// of freshly-idle units doesn't all evaluate on the same tick. No LocalRandom, ever.
			nextEvalTick = self.World.WorldTick + self.World.SharedRandom.Next(0, Info.EvaluateCooldown);
		}

		public override IEnumerable<VariableObserver> GetVariableObservers()
		{
			foreach (var o in base.GetVariableObservers())
				yield return o;

			yield return new VariableObserver(SuppressionChanged, new[] { Info.SuppressionVariable });
			yield return new VariableObserver(DeployedChanged, new[] { Info.DeployedVariable });
		}

		void SuppressionChanged(Actor self, IReadOnlyDictionary<string, int> conditions)
		{
			conditions.TryGetValue(Info.SuppressionVariable, out currentSuppression);
		}

		void DeployedChanged(Actor self, IReadOnlyDictionary<string, int> conditions)
		{
			conditions.TryGetValue(Info.DeployedVariable, out currentDeployed);
		}

		protected override void TraitDisabled(Actor self)
		{
			// Gate lifted mid-adjustment (profile change / lost condition) — let go cleanly.
			ReleaseManagement();
			State = AdjustmentState.None;
		}

		void ITick.Tick(Actor self)
		{
			// B1 (return-to-slot vector): catch a non-executor relocation the MOMENT it crosses the
			// leash — mid-move, before the unit next idles. CohesionSlotMemory is declared before this
			// trait, so on the unit's next idle its return-to-slot fires FIRST and would drag the unit
			// back to the executor-assigned cover slot; clearing the slot here (during the move) beats
			// it. The idle-time guard in TickIdle is a backstop. Skipped while Adjusting (our own
			// leashed move keeps the unit inside the leash).
			// KNOWN GAP (merge review, e2208d42): a player redirect issued WHILE Adjusting is not
			// caught here — CohesionSlotMemory can drag the unit back to the old slot ONCE before the
			// slot goes stale / the next redirect is handled. Bounded and self-healing; filed in
			// WORKSPACE/bugs/discovered.md. A fix needs an Adjusting-aware leash margin to avoid
			// false-aborting the executor's own pathing excursions.
			if (IsTraitDisabled || State == AdjustmentState.Adjusting)
				return;

			if (hasAnchor && !WithinLeash(self.Location))
			{
				ReleaseManagement();
				State = AdjustmentState.None;
			}
		}

		void INotifyIdle.TickIdle(Actor self)
		{
			if (IsTraitDisabled)
				return;

			// S6: a peer INotifyIdle handler (CohesionSlotMemory return-to-slot, AutoTarget scan)
			// may have queued this same tick. Never stack a second activity on top.
			if (self.CurrentActivity != null)
				return;

			// S4: don't reposition suppressed infantry — a move order stands them out of prone.
			if (currentSuppression > Info.MaxSuppressionToMove)
				return;

			if (autoTarget == null)
				return;

			// Phase-3 opt-out (S2/option-b): a deployed unit has expressed a stronger positional
			// intent than a move order — never reposition it (a move would force an undeploy). Reads
			// the live SYNCED `deployed` condition, not the unsynced per-type defaults file, so the
			// opt-out is desync-safe. Relinquish any prior management, same as HoldPosition.
			if (currentDeployed > 0)
			{
				ReleaseManagement();
				State = AdjustmentState.None;
				return;
			}

			// S1: re-read stance every evaluation; never cache across evaluations.
			var stance = autoTarget.EngagementStanceValue;
			if (stance == EngagementStance.HoldPosition)
			{
				// §4: stand exactly where placed. Relinquish any prior management.
				ReleaseManagement();
				State = AdjustmentState.None;
				return;
			}

			var tick = self.World.WorldTick;
			if (tick < nextEvalTick)
				return;

			nextEvalTick = tick + Info.EvaluateCooldown;

			ResolveArrivalOrAbort();

			// B1: anchor invalidation. Any non-executor relocation that leaves the unit outside the
			// leash of a settled (non-Adjusting) anchor has fossilized that anchor — a stale anchor
			// must never out-live the player's last move, or the next threat scan drags the unit back
			// toward its abandoned position. Release management here; the block below re-anchors to the
			// current location this same tick. (Adjusting is handled by ResolveArrivalOrAbort above.)
			if (hasAnchor && State != AdjustmentState.Adjusting && !WithinLeash(self.Location))
			{
				ReleaseManagement();
				State = AdjustmentState.None;
			}

			// S7: capture the leash anchor ONCE per idle episode. Prefer the assigned cohesion
			// slot (kept fresh by grouped orders) so grouped and single orders share one anchor;
			// fall back to where the unit actually is. Our own moves never re-anchor (see B2 note).
			if (!hasAnchor)
			{
				var slot = slotMemory?.AssignedSlot;
				anchor = slot ?? self.Location;
				hasAnchor = true;
			}

			var bearing = ComputeThreatBearing(tick);
			if (bearing == null)
			{
				// No usable threat direction. If we're already holding a cover cell, STAY: re-commit
				// the claim + slot on our current cell so protection never lapses under us (deviation
				// 3 invariant — Arrived keeps managing). Otherwise stop managing so the unit re-enters
				// the strategic pool.
				if (State == AdjustmentState.Arrived)
					CommitManagement(self.Location, tick);
				else
					ReleaseManagement();
				return;
			}

			var target = ChooseTarget(bearing.Value, stance);
			if (target == null)
			{
				// No valid cover cell. Same rule: a holding unit keeps its claim/slot on its current
				// cell; a non-holding unit disengages.
				if (State == AdjustmentState.Arrived)
					CommitManagement(self.Location, tick);
				else
					ReleaseManagement();
				return;
			}

			var dest = target.Value;

			// Already on — or within the S3 arrival tolerance of — the chosen cover cell: HOLD.
			// Commit the claim + slot to our CURRENT cell (not `dest`) so neither this executor NOR
			// the declared-earlier CohesionSlotMemory return-to-slot re-dispatches a Move toward a
			// cell we are already effectively at, and issue NO move.
			//
			// The old exact-equality `self.Location == dest` was a per-interval re-order loop (same
			// bug class as the fires-standoff re-issue gate, 3aca99a1): cell contention lands the
			// unit one cell off `dest`, ResolveArrivalOrAbort marks it Arrived under the 1-cell
			// tolerance, yet this check then re-issued Move(dest) every EvaluateCooldown — a
			// perpetual nudge. Those repeated blocked moves also shove peers past their leash edge,
			// re-anchoring them one step forward each time; because Defensive/Hunt always bias the
			// chosen cover cell toward the threat, that ratchet walks the squad toward the frontline.
			// Matching the hold tolerance to the arrival tolerance kills both symptoms at the source.
			if (WithinOneCell(self.Location, dest))
			{
				CommitManagement(self.Location, tick);
				State = AdjustmentState.Arrived;
				return;
			}

			CommitManagement(dest, tick);
			State = AdjustmentState.Adjusting;
			self.QueueActivity(new Move(self, dest));
		}

		// Idle + we had a target: did we arrive where we intended, or did something else move us?
		void ResolveArrivalOrAbort()
		{
			if (State != AdjustmentState.Adjusting || !hasTarget)
				return;

			// S3: accept arrival within 1 cell of the intended target. Cell contention (two units
			// racing for the same edge cell) shoves the loser one cell over; reading that as a player
			// interrupt churns the anchor. A 1-cell tolerance treats it as arrival, not abort.
			if (WithinOneCell(self.Location, currentTarget))
			{
				State = AdjustmentState.Arrived;
			}
			else
			{
				// Idle, >1 cell from our target, not moving ⇒ a fresh external order replaced our Move
				// (§2 pt 4 abort). Drop the claim/slot; ReleaseManagement re-arms the "anchor born on
				// next idle tick" invariant, so the block below re-anchors to wherever we ended up.
				ReleaseManagement();
				State = AdjustmentState.Aborted;
			}
		}

		// B3: intensity-weighted aggregate bearing from ActiveCells near the anchor, with gates and
		// the fallback chain. Returns null ⇒ "no usable bearing, do not reposition".
		WAngle? ComputeThreatBearing(int tick)
		{
			var player = self.Owner;
			if (threatLayer == null || player == null)
				return BearingFallback(tick);

			long sumIntensity = 0;
			long dirX = 0;
			long dirY = 0;

			var r = Info.ThreatScanRadius;
			foreach (var cell in threatLayer.ActiveCells(player))
			{
				var dx = cell.X - anchor.X;
				var dy = cell.Y - anchor.Y;
				if (System.Math.Abs(dx) > r || System.Math.Abs(dy) > r)
					continue;

				var intensity = threatLayer.ThreatIntensity(player, cell);
				if (intensity <= 0)
					continue;

				sumIntensity += intensity;
				dirX += (long)dx * intensity;
				dirY += (long)dy * intensity;
			}

			if (sumIntensity < Info.MinThreatIntensity)
				return BearingFallback(tick);

			// Ambiguity gate: reject near-cancelled bearings (surrounded ≡ blind otherwise).
			var magSq = dirX * dirX + dirY * dirY;
			if (magSq * Info.DirectionAmbiguityDen < sumIntensity * sumIntensity * Info.DirectionAmbiguityNum)
				return BearingFallback(tick);

			// Scale the weighted vector down into WVec range while preserving the angle.
			var vx = (int)(dirX * 256 / sumIntensity);
			var vy = (int)(dirY * 256 / sumIntensity);
			if (vx == 0 && vy == 0)
				return BearingFallback(tick);

			var bearing = new WVec(vx, vy, 0).Yaw;
			lastAcceptedBearing = bearing;
			lastBearingTick = tick;
			hasBearingMemory = true;
			return bearing;
		}

		WAngle? BearingFallback(int tick)
		{
			// (a) last accepted bearing within its TTL.
			if (hasBearingMemory && tick - lastBearingTick <= Info.BearingMemoryTicks)
				return lastAcceptedBearing;

			// (b) bearing toward the commanded destination, if we are somewhere else.
			if (hasAnchor && anchor != self.Location)
			{
				var dx = anchor.X - self.Location.X;
				var dy = anchor.Y - self.Location.Y;
				return new WVec(dx, dy, 0).Yaw;
			}

			// (c) no repositioning.
			return null;
		}

		// S2: pick the best threat-facing edge cell within the leash. Defensive takes the edge cell;
		// Hunt steps one cell forward toward the threat. Deterministic total-order tie-break (S8).
		CPos? ChooseTarget(WAngle bearing, EngagementStance stance)
		{
			if (affordanceLayer == null)
				return null;

			var hunt = stance == EngagementStance.Hunt;

			var haveBest = false;
			CPos bestTarget = default;
			var bestCover = int.MinValue;
			var bestAngErr = int.MaxValue;
			var bestKey = 0; // (Y,X) packed for the final tie-break

			// Forward step (Hunt): move one cell along the threat bearing. Accepted candidates have
			// OutwardFacing within FacingTolerance of the bearing, so the bearing direction ≈ the
			// local outward normal. FromSpeedAndAngle is the exact inverse of WVec.Yaw (the bearing
			// was built from a cell-space WVec), so its X/Y signs give the cell-space step with no
			// hand-rolled WAngle→cell conversion (WAngle is counterclockwise — easy to sign wrong).
			var stepDir = WVec.FromSpeedAndAngle(1024, bearing);
			var stepX = System.Math.Sign(stepDir.X);
			var stepY = System.Math.Sign(stepDir.Y);

			var lr = Info.LeashRadius;
			for (var dy = -lr; dy <= lr; dy++)
			{
				for (var dx = -lr; dx <= lr; dx++)
				{
					if (System.Math.Abs(dx) + System.Math.Abs(dy) > lr)
						continue;

					var edge = new CPos(anchor.X + dx, anchor.Y + dy);
					if (!affordanceLayer.IsCoverEdge(edge))
						continue;

					var angErr = AngleDelta(affordanceLayer.OutwardFacing(edge), bearing);
					if (angErr > Info.FacingToleranceAngle)
						continue;

					var target = edge;
					if (hunt)
					{
						var stepped = new CPos(edge.X + stepX, edge.Y + stepY);
						if (WithinLeash(stepped) && IsUsableCell(stepped))
							target = stepped;
					}

					if (!IsUsableCell(target))
						continue;

					var cover = affordanceLayer.CoverQuality(edge);
					var key = (target.Y << 16) ^ (target.X & 0xFFFF);

					// Tie-break: CoverQuality desc, angular error asc, (Y,X) asc.
					var better = !haveBest
						|| cover > bestCover
						|| (cover == bestCover && angErr < bestAngErr)
						|| (cover == bestCover && angErr == bestAngErr && key < bestKey);

					if (better)
					{
						haveBest = true;
						bestTarget = target;
						bestCover = cover;
						bestAngErr = angErr;
						bestKey = key;
					}
				}
			}

			return haveBest ? bestTarget : (CPos?)null;
		}

		bool WithinLeash(CPos cell)
		{
			return System.Math.Abs(cell.X - anchor.X) + System.Math.Abs(cell.Y - anchor.Y) <= Info.LeashRadius;
		}

		// S3: Chebyshev "within one cell" (the 8-neighbourhood plus the cell itself).
		static bool WithinOneCell(CPos a, CPos b)
		{
			return System.Math.Abs(a.X - b.X) <= 1 && System.Math.Abs(a.Y - b.Y) <= 1;
		}

		// S3: never trust the static affordance layer alone — validate passability/occupancy now.
		bool IsUsableCell(CPos cell)
		{
			if (self.Location == cell)
				return true;

			return mobile.CanStayInCell(cell) && mobile.CanEnterCell(cell);
		}

		// Shortest absolute angular distance between two WAngles, 0..512 (512 = 180 degrees).
		static int AngleDelta(WAngle a, WAngle b)
		{
			var d = (a.Angle - b.Angle) & 1023;
			return d > 512 ? 1024 - d : d;
		}

		// B1 + B2: take ownership so nothing contests the adjustment.
		void CommitManagement(CPos dest, int tick)
		{
			currentTarget = dest;
			hasTarget = true;

			// B2: return-to-slot now reinforces our cell instead of dragging back to a stale slot.
			slotMemory?.Assign(dest, tick);

			// B1: bot-owned units register a `tacpos:` claim so the Poi stack / GroundStates filter
			// leaves them alone. Humans have no bot layer contesting them — skip the ledger. Cache the
			// guard we committed to so Release (S6) can reach the same ledger after an owner change.
			if (self.Owner != null && self.Owner.IsBot)
			{
				committedGuard = self.Owner.PlayerActor?.TraitOrDefault<PoiGoalGuard>();
				committedGuard?.Ledger.Commit(self, "tacpos:" + self.ActorID, tick, Info.ClaimTicks);
				claimed = committedGuard != null;
			}
		}

		// Relinquish the claim + slot override (abort / disengage / disable).
		void ReleaseManagement()
		{
			// S6: release from the ledger we actually committed to, regardless of the CURRENT owner.
			// A bot→human capture must not leak the claim until TTL; release of an unheld claim is a
			// no-op. committedGuard is null for units that never committed, so this is inert for them.
			committedGuard?.Ledger.Release(self);
			committedGuard = null;
			claimed = false;
			slotMemory?.Clear();
			hasTarget = false;

			// B1: every release restores the "anchor born on next idle tick" invariant, so a stale
			// anchor can never out-live the unit's last non-executor movement. TraitDisabled, abort,
			// disengage, HoldPosition/deploy opt-out and anchor-invalidation all route through here.
			hasAnchor = false;
			anchor = default;
		}
	}
}
