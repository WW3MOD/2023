#region Copyright & License Information
/*
 * WW3MOD — counter-battery radar siting for the @experimental bot.
 *
 * MSAR is the mod's ONLY counter-battery vision source (Detectable.cs:36-37, MapLayers.cs:539-549) and
 * every artillery piece carries `Detectable: CounterBatteryRadar: 1` gated on `firing`. Until this
 * module existed the bot had never bought one and no bot module anywhere issued the
 * "GrantConditionOnDeploy" order, so the whole mechanic was human-only.
 *
 * Production is the sibling YAML change (a UnitFloors/UnitLimits/UnitDelays entry on both @experimental
 * UnitBuilder twins). This module is the other half: an MSAR that is never deployed grants nothing at
 * all, because Radar and CounterBatteryRadar both carry `RequiresCondition: deployed`.
 *
 * THE PAYOFF IS INDIRECT AND THAT IS THE DESIGN. Nothing AI-side reads CounterBatteryRadarCover today.
 * What the coverage buys is that a firing enemy artillery piece becomes a LEGAL BeliefStore contact
 * under fog, which feeds ContinuousBombardment and AdaptiveProduction's enemy-composition scan for
 * free. This module delivers the coverage and deliberately builds no consumer for it.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.Player)]
	[Desc("WW3MOD experimental AI: parks the counter-battery radar in its own rear and deploys it.",
		"Picks a cell on the bearing from its own Supply Route toward the contested ground, far enough",
		"forward that the 42c0 radar disc reaches the front but never past the rear band, then routes",
		"that cell through BotTerrain.TryNearestStandable with the DEPLOY terrain whitelist folded into",
		"the passability oracle — the locomotor is wider than AllowedTerrainTypes, so a locomotor-only",
		"clamp lands it where deploy is silently refused. Claims the radar through the shared PoiGoalGuard",
		"ledger (objective cbradar:<id>) so no other module can move it, because UndeployOnMove: true",
		"means any Move order at all revokes the coverage. Gate enable-ai-experimental.")]
	public class CounterBatteryRadarBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Ticks between siting evaluations. The radar is bought once, driven once and then never",
			"touched again, so this only has to be fast enough to notice it arriving.")]
		public readonly int ReevaluateInterval = 100;

		[Desc("Actor types treated as counter-battery radars. Named explicitly rather than detected from",
			"the CounterBatteryRadar trait so this module can never adopt some future radar by accident.")]
		public readonly HashSet<string> RadarActorTypes = new() { "msar" };

		[Desc("Actor types treated as this player's own Supply Route — the rear anchor the site is",
			"measured from. The SR is a fixed, indestructible, non-buildable beachhead, which is exactly",
			"what makes it a usable definition of 'our rear'.")]
		public readonly HashSet<string> SupplyRouteTypes = new() { "supplyroute" };

		[Desc("Ticks a ledger commitment is held for. Must exceed ReevaluateInterval by enough that a",
			"commitment cannot lapse between two evaluations — a lapsed claim is a radar the offence FSM",
			"is free to recruit, and recruiting it means moving it, and moving it undeploys it.")]
		public readonly int CommitmentTicks = 300;

		[Desc("The radar's own coverage radius in CELLS, matching CounterBatteryRadar.Range (42c0) on",
			"MSAR. Used only to decide how far forward the site has to be; the trait's own range is what",
			"actually applies once deployed.")]
		public readonly int RadarCoverageCells = 42;

		[Desc("How far toward the front the site may sit, as a percentage of the SR-to-front distance.",
			"The rear-band cap — see CounterBatteryRadarMath.ForwardOffsetCells.")]
		public readonly int RearFractionPercent = 33;

		[Desc("Maximum GROUND danger tolerated at the deploy cell. The radar is unarmoured, unarmed and",
			"immobile once deployed, so a cell that is merely quiet right now is not good enough — this",
			"is the whole reason the site is capped to the rear band as well.")]
		public readonly int MaxSiteGroundDanger = 60;

		[Desc("How far the site search may wander from the ideal cell, in cells. Defaults to the engine's",
			"own silent relocation budget (BotTerrain.EngineRelocationCells): clamping SHORTER gives up on",
			"sites the engine would have reached, clamping FURTHER picks a cell the engine would not have",
			"chosen, and the two then disagree in the other direction.")]
		public readonly int SiteSearchCells = BotTerrain.EngineRelocationCells;

		[Desc("How close to the committed site counts as ARRIVED, in cells. Defaults to the engine's",
			"relocation budget for a specific reason: Mobile.NearestMoveableCell may silently park the",
			"radar up to 10 cells from the cell we asked for, and a stricter test would re-issue the same",
			"Move forever and never deploy — the MountedTransport drop-cell trap, exactly.")]
		public readonly int ArrivalRadiusCells = BotTerrain.EngineRelocationCells;

		[Desc("Ticks before an unaccepted Move is re-offered. Bounds the re-order beat.")]
		public readonly int MoveSettleTicks = 150;

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			base.RulesetLoaded(rules, ai);

			// Case-harden actor-name config (see ActorNameCase). Without this the module matches only the
			// exact spelling used here, and `MSAR` — the spelling used throughout vehicles.yaml — would
			// silently match nothing.
			ActorNameCase.NormalizeInPlace(RadarActorTypes);
			ActorNameCase.NormalizeInPlace(SupplyRouteTypes);
		}

		public override object Create(ActorInitializer init) { return new CounterBatteryRadarBotModule(init.Self, this); }
	}

	public class CounterBatteryRadarBotModule : ConditionalTrait<CounterBatteryRadarBotModuleInfo>, IBotTick
	{
		readonly World world;
		readonly Player player;

		PoiGoalGuard goalGuard;
		bool goalGuardResolved;
		DangerFieldLayer dangerField;
		bool dangerFieldResolved;
		InfluenceMap influenceMap;
		bool influenceMapResolved;
		BeliefStore beliefStore;
		bool beliefStoreResolved;

		// Radars this module holds a ledger claim on, so the claim can be dropped precisely when the
		// radar dies or the module shuts down.
		readonly HashSet<Actor> claimed = new();

		// The Move standing against each radar: where it was sent and when. Read only through the ordinal
		// radar walk, never enumerated, so its ordering reaches no decision.
		sealed class Errand
		{
			public CPos Cell;
			public int OrderedTick;
		}

		readonly Dictionary<Actor, Errand> errands = new();

		int reevalCountdown;

		public CounterBatteryRadarBotModule(Actor self, CounterBatteryRadarBotModuleInfo info)
			: base(info)
		{
			world = self.World;
			player = self.Owner;
		}

		protected override void TraitEnabled(Actor self)
		{
			// Deterministic initial offset, NOT a LocalRandom draw — a control game that never
			// instantiates this module must keep its random stream untouched.
			reevalCountdown = Info.ReevaluateInterval;
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (player.WinState != WinState.Undefined)
				return;

			// REFRESH THE CLAIM EVERY TICK, EVALUATE ONLY ON THE CADENCE — the split DroneOperator
			// documents at :253-263, and it matters more here than it does there. A third party can delete
			// this module's claim between two evaluations (GoalGuardLedger.Release is keyed on the ACTOR,
			// not the objective, so it drops whatever claim the actor holds regardless of who wrote it),
			// and on a 100-tick cadence that leaves the radar recruitable for up to 100 ticks. For a drone
			// operator that costs a sortie; here it costs the coverage outright, because every module that
			// would recruit it does so by issuing a Move and UndeployOnMove: true revokes `deployed` on
			// the spot. A dictionary write per tick is far cheaper than that exposure.
			ResolveTraits();
			RefreshClaims();

			if (--reevalCountdown > 0)
				return;

			reevalCountdown = Info.ReevaluateInterval;
			Reevaluate(bot);
		}

		void ResolveTraits()
		{
			if (!goalGuardResolved)
			{
				goalGuard = player.PlayerActor.TraitOrDefault<PoiGoalGuard>();
				goalGuardResolved = true;
			}

			if (!dangerFieldResolved)
			{
				dangerField = world.WorldActor.TraitOrDefault<DangerFieldLayer>();
				dangerFieldResolved = true;
			}

			if (!influenceMapResolved)
			{
				influenceMap = world.WorldActor.TraitOrDefault<InfluenceMap>();
				influenceMapResolved = true;
			}

			if (!beliefStoreResolved)
			{
				beliefStore = world.WorldActor.TraitOrDefault<BeliefStore>();
				beliefStoreResolved = true;
			}
		}

		void RefreshClaims()
		{
			if (goalGuard == null || claimed.Count == 0)
				return;

			var tick = world.WorldTick;
			foreach (var r in claimed)
				if (!r.IsDead && r.IsInWorld)
					goalGuard.Ledger.Commit(r, ObjectiveKey(r), tick, Info.CommitmentTicks);
		}

		static string ObjectiveKey(Actor radar) => "cbradar:" + radar.ActorID.ToString();

		// A disabled module must not leave units committed behind it, or the offence FSM sees a radar that
		// is permanently spoken for by nobody.
		protected override void TraitDisabled(Actor self)
		{
			ReleaseAll();
		}

		void ReleaseAll()
		{
			if (goalGuard != null)
				foreach (var r in claimed)
					goalGuard.Ledger.Release(r);

			claimed.Clear();
			errands.Clear();
		}

		void Reevaluate(IBot bot)
		{
			var tick = world.WorldTick;

			// Drop claims on radars that died or left. Ordered by nothing — the predicate is per-actor and
			// order-independent.
			claimed.RemoveWhere(a =>
			{
				if (!a.IsDead && a.IsInWorld)
					return false;

				goalGuard?.Ledger.Release(a);
				errands.Remove(a);
				return true;
			});

			foreach (var a in errands.Keys.Where(a => a.IsDead || !a.IsInWorld).ToList())
				errands.Remove(a);

			var radars = world.Actors
				.Where(a => a.Owner == player
					&& !a.IsDead
					&& a.IsInWorld
					&& Info.RadarActorTypes.Contains(a.Info.Name))
				.OrderBy(a => a.ActorID)
				.ToList();

			if (radars.Count == 0)
			{
				if (claimed.Count > 0)
					ReleaseAll();

				return;
			}

			// The rear anchor. Ordered by ActorID so a player with more than one SR — not a shipped state,
			// but the contestation design anticipates it — resolves the same way on every run.
			var sr = world.Actors
				.Where(a => a.Owner == player
					&& !a.IsDead
					&& a.IsInWorld
					&& Info.SupplyRouteTypes.Contains(a.Info.Name))
				.OrderBy(a => a.ActorID)
				.FirstOrDefault();

			if (sr == null)
				return;

			foreach (var radar in radars)
				TaskRadar(bot, radar, sr.Location, tick);
		}

		void TaskRadar(IBot bot, Actor radar, CPos srCell, int tick)
		{
			var deploy = radar.TraitOrDefault<GrantConditionOnDeploy>();
			if (deploy == null)
				return;

			// If something else already owns this unit, do not fight over it — but DO take it back once
			// that claim lapses.
			if (goalGuard != null && !claimed.Contains(radar)
				&& goalGuard.Ledger.IsCommitted(radar, tick))
				return;

			goalGuard?.Ledger.Commit(radar, ObjectiveKey(radar), tick, Info.CommitmentTicks);
			claimed.Add(radar);

			// TERMINAL STATE. Once deployed this module issues NO further orders against this radar, ever
			// — and that is the point of the whole module rather than an optimisation. UndeployOnMove is
			// true, so any Move at all revokes `deployed` and with it the coverage; the claim above is what
			// stops anyone ELSE issuing one, and this return is what stops US. The errand is dropped so a
			// stale destination cannot outlive the drive.
			if (deploy.DeployState == DeployState.Deployed)
			{
				errands.Remove(radar);
				return;
			}

			// Mid-animation. `deployed` is granted at the END of the make-animation, so a radar that has
			// been ordered to deploy spends several ticks here; re-ordering it would restart the sequence.
			if (deploy.DeployState != DeployState.Undeployed)
				return;

			// Still driving. Leave it alone — re-issuing a Move mid-drive only resets the path.
			if (!IsStationary(radar))
				return;

			// WHERE TO MEASURE FROM. If we already sent it somewhere and it has stopped within the engine's
			// own relocation budget of that cell, it has ARRIVED — even though it may be standing up to 10
			// cells from the cell we named, because Mobile.NearestMoveableCell relocates silently. Anchor
			// the final search at where it actually IS, so the deploy is judged against real ground rather
			// than against the cell we asked for. Measuring against the requested cell instead is the
			// MountedTransport drop-cell trap: the arrival test never passes and the Move is re-issued
			// forever.
			errands.TryGetValue(radar, out var errand);
			var arrived = errand != null && (radar.Location - errand.Cell).Length <= Info.ArrivalRadiusCells;
			var anchor = arrived ? radar.Location : ChooseIdealCell(radar, srCell);

			// The site search carries the DEPLOY whitelist, not just the locomotor — see DeployableCell.
			if (!TryDeployableCell(radar, deploy, anchor, out var site))
			{
				// Nothing legal within the search budget. Two very different causes share this line — no
				// standable ground near the ideal, or standable ground that is all too hot — and the danger
				// reading at the anchor separates them without a second match.
				AIUtils.BotDebug("AI ({0}): cb-radar {1} no deployable site near {2} (danger {3} / max {4})",
					player.ClientIndex, radar.ActorID, anchor,
					dangerField != null ? dangerField.GroundDanger(player, anchor) : 0, Info.MaxSiteGroundDanger);

				// Drop the errand so the next evaluation re-derives the ideal from the SR rather than
				// re-anchoring forever on a spot that has nothing legal around it.
				errands.Remove(radar);
				return;
			}

			if (site == radar.Location)
			{
				// THE ONE ORDER THAT DEPLOYS IT. Unqueued: a queued deploy is judged at whatever cell the
				// radar reaches, not the one it is standing on now, which is the trap NoDeployNotification
				// on MSAR exists to make audible. Protected damping (the default) deliberately — this is a
				// terminal transition, so a dropped order is the entire failure mode rather than a delay.
				if (bot.QueueOrder(new Order("GrantConditionOnDeploy", radar, false)))
				{
					errands.Remove(radar);

					Log.Write("debug",
						$"[cbradar] player={player.PlayerName} radar={radar.ActorID} deploy cell={site.X},{site.Y} "
						+ $"sr={srCell.X},{srCell.Y} tick={tick}");
				}

				return;
			}

			// Re-offer bounded by MoveSettleTicks so a radar that cannot path to its site does not get a
			// fresh order every evaluation.
			if (errand != null && errand.Cell == site && tick - errand.OrderedTick < Info.MoveSettleTicks)
				return;

			// Recurring: this module re-offers the drive on its own cadence and the state write below is
			// guarded on acceptance, which is what that damping level asserts.
			if (!bot.QueueOrder(new Order("Move", radar, Target.FromCell(world, site), false), BotOrderDamping.Recurring))
				return;

			if (errand == null)
				errands[radar] = errand = new Errand();

			errand.Cell = site;
			errand.OrderedTick = tick;

			AIUtils.BotDebug("AI ({0}): cb-radar {1} → {2} (from {3}, sr {4})",
				player.ClientIndex, radar.ActorID, site, radar.Location, srCell);
		}

		// The ideal, UNTESTED site: on the bearing from our own SR toward the ground worth watching, far
		// enough forward that the radar disc reaches it without leaving the rear band. Raw vector
		// arithmetic — the caller must clamp it, and does.
		CPos ChooseIdealCell(Actor radar, CPos srCell)
		{
			var toward = ChooseBearingTarget(radar, srCell);
			var distance = (toward - srCell).Length;
			var forward = CounterBatteryRadarMath.ForwardOffsetCells(
				distance, Info.RearFractionPercent, Info.RadarCoverageCells);

			return BotGeometry.ShiftToward(srCell, toward, forward);
		}

		// WHAT THE RADAR SHOULD FACE, IN DESCENDING ORDER OF HOW MUCH IT ACTUALLY KNOWS. Every rung is
		// fog-legal: the influence frontline and the belief store are both built from what this player can
		// legally see, and the map centre is map geometry rather than enemy state.
		CPos ChooseBearingTarget(Actor radar, CPos srCell)
		{
			if (TryFrontlineCentroid(out var centroid))
				return centroid;

			// No contact yet. A believed enemy SR is the next best bearing — it is fixed and
			// indestructible, so a sighting of it never goes stale, and the fighting will happen between
			// the two beachheads.
			if (beliefStore != null)
			{
				var srType = Info.SupplyRouteTypes.FirstOrDefault();
				if (srType != null)
				{
					// Ordered by Key (the synced enemy ActorID) so a two-SR sighting resolves identically
					// on every client and every replay.
					var enemySr = beliefStore.Contacts(player)
						.Where(c => c.TypeName == srType)
						.OrderBy(c => c.Key)
						.Select(c => (CPos?)c.Cell)
						.FirstOrDefault();

					if (enemySr.HasValue)
						return enemySr.Value;
				}
			}

			// Nothing believed at all. The map centre is a poor bearing but it is never wrong in the way a
			// guess about the enemy would be, and it is what makes the module reach a deployed state on a
			// map where contact has not happened yet — which is most of the opening.
			var bounds = world.Map.Bounds;
			var centre = new CPos(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);

			// A radar that spawned closer to the centre than its own SR is already forward of the anchor;
			// nothing sensible to do with that here, and the clamp below handles the degenerate case.
			return centre == srCell ? radar.Location : centre;
		}

		// The centroid of the contested band, in map cells, or false when there is no contact anywhere.
		// Summed over a fixed nested loop, so world/dictionary iteration order reaches no decision.
		bool TryFrontlineCentroid(out CPos centroid)
		{
			centroid = CPos.Zero;
			if (influenceMap == null)
				return false;

			var frontline = influenceMap.GetFrontline(player);
			if (frontline == null)
				return false;

			var w = frontline.GetLength(0);
			var h = frontline.GetLength(1);

			long sx = 0, sy = 0;
			var n = 0;

			for (var gx = 0; gx < w; gx++)
			{
				for (var gy = 0; gy < h; gy++)
				{
					if (!frontline[gx, gy])
						continue;

					var cell = influenceMap.GridCellToMapCell(gx, gy);
					sx += cell.X;
					sy += cell.Y;
					n++;
				}
			}

			if (n == 0)
				return false;

			centroid = new CPos((int)(sx / n), (int)(sy / n));
			return true;
		}

		// THE CELL TO ACTUALLY ORDER, and the reason this is not a plain BotTerrain call with
		// PassableFor alone.
		//
		// MSAR's GrantConditionOnDeploy carries `AllowedTerrainTypes: Clear, Road, Rough`, which is
		// NARROWER than what its wheeled locomotor will drive over. A locomotor-only clamp therefore
		// returns cells the radar can happily stand on and then silently refuses to deploy on — the
		// refusal is a notification to the owning player and nothing the bot can observe. Folding the
		// deploy test into the passability oracle makes the search return only cells that satisfy BOTH,
		// so the cell we drive to is a cell we can deploy on.
		//
		// IsValidTerrain is CALLED, never re-derived: it is public on the trait and covers the ramp test
		// as well as the whitelist (CanDeployOnRamps is false on MSAR). A second copy of that predicate
		// here would be one more instance of the class of defect BotTerrain itself exists to prevent.
		bool TryDeployableCell(Actor radar, GrantConditionOnDeploy deploy, CPos ideal, out CPos cell)
		{
			var passable = BotTerrain.PassableFor(radar);

			bool Deployable(CPos c)
			{
				if (!passable(c) || !deploy.IsValidTerrain(c))
					return false;

				return dangerField == null || dangerField.GroundDanger(player, c) <= Info.MaxSiteGroundDanger;
			}

			return BotTerrain.TryNearestStandable(ideal, Info.SiteSearchCells, world.Map.Contains, Deployable, out cell);
		}

		// Same shape and the same one-way conservatism as DroneOperatorBotModule.IsStationary — read the
		// note there. A radar that is turning-while-driving reports moving here and does not report
		// `moving` to the condition system; declining an order for one evaluation costs 100 ticks, whereas
		// issuing a Move to a radar that is already under way only resets its path.
		static bool IsStationary(Actor radar)
		{
			var move = radar.TraitOrDefault<IMove>();
			if (move == null)
				return true;

			return (move.CurrentMovementTypes & (MovementType.Horizontal | MovementType.Vertical)) == 0;
		}
	}
}
