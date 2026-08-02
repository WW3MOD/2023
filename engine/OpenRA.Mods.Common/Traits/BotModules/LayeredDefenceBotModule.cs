#region Copyright & License Information
/*
 * WW3MOD LayeredDefenceBotModule — Stage B.1 of the doctrine roadmap.
 *
 * RESERVE-DRIVEN line filling + emergent flanking. Reads
 * InfluenceMap.GetFrontline(perspective) every N ticks. For each
 * RESERVE unit (= idle, AND not already on the line):
 *
 *   1. Score every contested cell as a candidate slot. Score favours
 *      cells where BOTH our line is thin (low friendly influence) AND
 *      the enemy is weak (low enemy influence). Lowest-density cell
 *      wins — that's a gap to fill AND a weak point to flank.
 *
 *   2. Send the unit to that slot. SCREEN units (light infantry) go
 *      to the slot directly. MAIN-LINE units (vehicles + heavy inf +
 *      artillery + AA) go to a standoff position shifted along the
 *      vector from slot -> own SR.
 *
 * Crucial detail per doctrine: units ALREADY on the engagement line
 * do NOT get re-tasked. Filling and flanking comes from the reserves
 * behind them. A unit is "on the line" if it sits within
 * OnLineRadiusCells of any contested cell. As the front shifts, units
 * naturally re-enter the reserve pool when they fall behind it.
 *
 * Doctrine: WORKSPACE/ai/doctrine.md. Stage spec:
 * WORKSPACE/ai/stage_b_layered_defence.md.
 *
 * When the frontline is empty (no contact), this module does nothing —
 * existing SquadManagerBotModule handles opening play.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.Player)]
	[Desc("WW3MOD experimental: assigns idle units to screen / main-line positions along the InfluenceMap frontline.")]
	public class LayeredDefenceBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Ticks between assignment passes.")]
		public readonly int ScanInterval = 75;

		[Desc("Minimum ticks between successive orders to the same unit. Prevents thrashing when",
			"a unit completes its move and goes idle again.")]
		public readonly int AssignCooldownTicks = 250;

		[Desc("Actor types eligible for the SCREEN (Layer 1). Sparse light infantry that anchors",
			"the contested edge. Examples: e3, ar, at, sn, tl, e2, medi (+ faction variants).")]
		public readonly HashSet<string> ScreenUnitTypes = new();

		[Desc("Actor types eligible for the MAIN LINE (Layer 2). The full combined-arms mix:",
			"tanks, IFVs, heavy infantry, ATGM, artillery, AA.")]
		public readonly HashSet<string> MainLineUnitTypes = new();

		[Desc("Standoff distance (cells) from the contested edge for main-line positioning.")]
		public readonly int MainLineStandoffCells = 6;

		[Desc("Map-cell radius around a contested cell that counts as 'on the line'.",
			"Units within this radius are NOT re-tasked — only true reserves (further back)",
			"get reassigned to fill gaps or flank weak enemy points.")]
		public readonly int OnLineRadiusCells = 8;

		[Desc("Weight applied to friendly influence when scoring candidate slots.",
			"Higher = stronger preference for cells where OUR line has a gap (spread units evenly).")]
		public readonly int FriendlyGapWeight = 2;

		[Desc("Weight applied to enemy influence when scoring candidate slots.",
			"Higher = stronger preference for cells where the ENEMY is weak (flanking).",
			"With both weights ~equal, units distribute evenly AND naturally avoid enemy concentrations.")]
		public readonly int EnemyWeaknessWeight = 1;

		[Desc("Maximum number of slot assignments per scan pass. Higher = quicker fill,",
			"but more orders/tick.")]
		public readonly int MaxAssignsPerScan = 4;

		[Desc("Actor types of the bot's home Supply Route — used to compute the 'behind' direction.")]
		public readonly HashSet<string> SupplyRouteTypes = new() { "supplyroute" };

		[Desc("Actor types EXCLUDED from layered defence dispatch. These are owned by other",
			"modules: tecn (capture coordinator), e6 (repair specialist), truk (supply follower),",
			"humvee/btr (scouts), bradley/bmp2/m113 (mounted transport — they ferry infantry,",
			"not stand the line). Aircraft are handled by their own SquadManagerBotModule.")]
		// PITFALL (2026-05): excluding carriers (bradley/bmp2/m113) is REQUIRED for
		// MountedTransportBotModule (B.4) to work. If LayeredDefence pulls them forward
		// they engage at standoff via AutoTarget → !IsIdle → never qualify as transport
		// candidates → carriers-candidate=0 forever. See WORKSPACE/ai/handoff_260513.md.
		public readonly HashSet<string> ExcludedActorTypes = new()
		{
			"tecn", "tecn.america", "tecn.russia",
			"e6", "e6.america", "e6.russia",
			"truk",
			"humvee", "btr",
			"bradley", "bmp2", "m113"
		};

		[Desc("Skip units whose AmmoPool(s) are ALL empty. Out-of-ammo units shouldn't be sent",
			"into the spearhead. A future rearm/retreat module will actively route them to",
			"supply; for now we just don't pull them forward.")]
		public readonly bool SkipOutOfAmmoUnits = true;

		[Desc("Terrain types that count as COVER for screen units. Screen-eligible reserves",
			"snap to the nearest cell of one of these types within CoverSearchRadiusCells of",
			"their assigned slot, so infantry takes treeline/rough-ground cover rather than",
			"standing in the open.")]
		public readonly HashSet<string> CoverTerrainTypes = new() { "Tree", "Rough", "Field" };

		[Desc("Search radius (map cells) around an assigned slot for cover. 0 disables cover snap.")]
		public readonly int CoverSearchRadiusCells = 6;

		[Desc("EXPERIMENTAL mission-commitment interop: before pulling a reserve onto the line, skip any unit",
			"that is currently COMMITTED in the shared PoiGoalGuard ledger (an offense axis / capture / garrison",
			"already owns it). Without this, LayeredDefence is a ledger-BLIND second writer: an offense unit that",
			"idles for an instant at its objective between offense evals gets yanked back to a line slot, then",
			"offense re-grabs it next eval — the forward/back dithering loop. Honouring the ledger (like the",
			"transport-reservation check already does) makes LayeredDefence cooperate with the shared claim.",
			"Default false so @stable/Normal/legacy twins stay byte-identical (they never consult the ledger);",
			"only LayeredDefenceBotModule@experimental turns it on. Inert if no PoiGoalGuard exists on the player.")]
		public readonly bool RespectCommitmentLedger = false;

		[Desc("Phase 2 commit-on-order audit (§4): also COMMIT each line/screen assignment to the shared",
			"PoiGoalGuard ledger (key defend-line:<cell>). RespectCommitmentLedger landed only the READ side —",
			"LayeredDefence skips units offense committed, but never WRITES its own, so offense's BuildFreePool",
			"still strips an idle line unit (the reverse steal channel N6 documents). Committing the assignment",
			"closes it. TTL is the assignment cooldown (AssignCooldownTicks), so the ledger claim and this module's",
			"own assignedAtTick anti-thrash cooldown expire together — the line re-flows on the same clock while",
			"other writers defer for the cooldown window. Requires a resolved ledger (implies RespectCommitmentLedger",
			"on the same twin). Default false ⇒ no write ⇒ byte-identical @stable/legacy.")]
		public readonly bool CommitLineAssignments = false;

		[Desc("EXPERIMENTAL: derive line eligibility from UnitRoleResolver (only role==MainBattle holds",
			"either layer) instead of the ScreenUnitTypes/MainLineUnitTypes/ExcludedActorTypes name lists.",
			"Cures the ai.yaml:349 artillery/SHORAD/MANPADS-on-the-line defect — those roles drop out by",
			"class. Cargo carriers (bradley/bmp2/m113) are still excluded so MountedTransportBotModule keeps",
			"them (the IFVs classify MainBattle by override, but this partial migration leaves the transport",
			"module on its name list). The screen/main-line partition still reads ScreenUnitTypes. Default",
			"false = frozen list behaviour, so @stable/legacy twins stay byte-identical.")]
		public readonly bool UseUnitRoles = false;

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			base.RulesetLoaded(rules, ai);

			// Case-harden actor-name config (see ActorNameCase). SupplyRouteTypes is a hardcoded
			// lowercase default and CoverTerrainTypes holds terrain tokens (Tree/Rough/Field), NOT
			// actor names — both stay ordinal.
			ActorNameCase.NormalizeInPlace(ScreenUnitTypes);
			ActorNameCase.NormalizeInPlace(MainLineUnitTypes);
			ActorNameCase.NormalizeInPlace(ExcludedActorTypes);
		}

		public override object Create(ActorInitializer init) { return new LayeredDefenceBotModule(init.Self, this); }
	}

	public class LayeredDefenceBotModule : ConditionalTrait<LayeredDefenceBotModuleInfo>, IBotTick
	{
		readonly World world;
		readonly Player player;

		// Per-unit last assignment tick. Stale entries cleaned in the cooldown gate.
		readonly Dictionary<Actor, int> assignedAtTick = new();

		int scanCountdown;

		InfluenceMap influenceMap;
		UnitRoleResolver resolver;
		PoiGoalGuard goalGuard;

		// Phase 2 commit-on-order (§4): ledger key for a line/screen slot. The slot is a CELL, not an actor,
		// so the grammar (defend-line:<x>,<y>) is disjoint from every actor-keyed executor (capture:/defend:/
		// garrison:/transport:/offense:) — audit requirement (d).
		static string LineObjectiveKey(CPos slot) => "defend-line:" + slot.X + "," + slot.Y;

		public LayeredDefenceBotModule(Actor self, LayeredDefenceBotModuleInfo info)
			: base(info)
		{
			world = self.World;
			player = self.Owner;
		}

		protected override void TraitEnabled(Actor self)
		{
			scanCountdown = world.LocalRandom.Next(0, Info.ScanInterval);
			influenceMap = world.WorldActor.TraitOrDefault<InfluenceMap>();
			resolver = world.WorldActor.TraitOrDefault<UnitRoleResolver>();

			// Shared commitment ledger (experimental interop): only consulted when RespectCommitmentLedger
			// is on, so @stable/legacy never look it up ⇒ byte-identical. Null when the player has no
			// PoiGoalGuard (every non-@experimental profile) ⇒ the check below is inert.
			goalGuard = Info.RespectCommitmentLedger || Info.CommitLineAssignments
				? player.PlayerActor.TraitOrDefault<PoiGoalGuard>() : null;

			TextNotificationsManager.AddSystemLine(
				$"[exp-layered-defence] enabled for {player.PlayerName} ({player.Faction.Name})");
			Log.Write("debug",
				$"[exp-layered-defence] TraitEnabled — player={player.PlayerName} screen-types={Info.ScreenUnitTypes.Count} mainline-types={Info.MainLineUnitTypes.Count} excluded-types={Info.ExcludedActorTypes.Count}");
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (player.WinState != WinState.Undefined || influenceMap == null)
				return;

			if (--scanCountdown > 0)
				return;
			scanCountdown = Info.ScanInterval;

			// PHASE-0 DIAGNOSTIC — death-ball root-cause confirm. Runs BEFORE the
			// frontline gate in AssignPositions so the log captures the "pooling"
			// phase (no contact, contested=0) as well as the "flow to contact"
			// phase. See WORKSPACE/plans/260719_experimental_ai_poi_strategy.md §0.
			LogPoiDispersionDiagnostic();

			AssignPositions(bot);
		}

		// One-line-per-scan dispersion metric for the experimental combat ground pool.
		// A death-ball reads as: clumpRadiusCells stays small while pool grows,
		// centroid sitting near the SR pre-contact then marching to the single
		// contested band. A spread army has a large clumpRadius. Log channel
		// [exp-poi]; read once from the debug log after a skirmish (no sweep).
		void LogPoiDispersionDiagnostic()
		{
			var contested = CollectContestedCells(influenceMap.GetFrontline(player)).Count;

			var pool = world.Actors.Where(a =>
				a.Owner == player && !a.IsDead && a.IsInWorld && IsCombatPoolMember(a))
				.ToList();

			if (pool.Count == 0)
			{
				Log.Write("debug",
					$"[exp-poi] disperse player={player.PlayerName} pool=0 contested={contested} tick={world.WorldTick}");
				return;
			}

			long sx = 0, sy = 0;
			foreach (var a in pool)
			{
				sx += a.Location.X;
				sy += a.Location.Y;
			}

			var cx = (int)(sx / pool.Count);
			var cy = (int)(sy / pool.Count);

			long distSum = 0;
			foreach (var a in pool)
			{
				var dx = a.Location.X - cx;
				var dy = a.Location.Y - cy;
				distSum += Exts.ISqrt((long)dx * dx + (long)dy * dy);
			}

			var clumpRadius = distSum / pool.Count;

			var ownSR = world.Actors.FirstOrDefault(a =>
				a.Owner == player && !a.IsDead && a.IsInWorld
				&& Info.SupplyRouteTypes.Contains(a.Info.Name));
			var srDist = -1L;
			if (ownSR != null)
			{
				var dx = cx - ownSR.Location.X;
				var dy = cy - ownSR.Location.Y;
				srDist = Exts.ISqrt((long)dx * dx + (long)dy * dy);
			}

			Log.Write("debug",
				$"[exp-poi] disperse player={player.PlayerName} pool={pool.Count} centroid=({cx},{cy}) " +
				$"clumpRadiusCells={clumpRadius} centroidDistFromSRCells={srDist} contested={contested} tick={world.WorldTick}");
		}

		void AssignPositions(IBot bot)
		{
			// Pull the contested grid. If no contact yet, hand off to existing logic.
			var frontline = influenceMap.GetFrontline(player);
			var contestedCells = CollectContestedCells(frontline);
			if (contestedCells.Count == 0)
				return;

			// Own SR — first one found. Used to compute the "behind" vector.
			var ownSR = world.Actors.FirstOrDefault(a =>
				a.Owner == player && !a.IsDead && a.IsInWorld
				&& Info.SupplyRouteTypes.Contains(a.Info.Name));
			if (ownSR == null)
				return;
			var srCell = ownSR.Location;

			// Per-perspective influence layers — used for slot scoring.
			var friendlyInf = influenceMap.GetFriendlyInfluence(player);
			var enemyInf = influenceMap.GetEnemyInfluence(player);

			// Gather reserve units (idle, eligible, NOT on the line, cooldown elapsed).
			// On-line units stay put — line-filling and flanking happens from the rear.
			// We also defer to MountedTransportBotModule's reservation set so we don't
			// override an EnterTransport with an AttackMove.
			var onLineRadiusSq = (long)Info.OnLineRadiusCells * Info.OnLineRadiusCells;
			var cooldownExpiresBefore = world.WorldTick - Info.AssignCooldownTicks;
			// MountedTransportBotModule is split into @stable + @experimental twins (both
			// instances exist on the player actor, one disabled), so TraitOrDefault would throw
			// on "multiple traits". Pick the enabled one.
			var transport = player.PlayerActor.TraitsImplementing<MountedTransportBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);
			var reserves = new List<(Actor Actor, bool IsScreen)>();
			foreach (var actor in world.Actors)
			{
				if (actor.Owner != player || actor.IsDead || !actor.IsInWorld || !actor.IsIdle)
					continue;

				var name = actor.Info.Name.ToLowerInvariant();

				bool isScreen;
				if (Info.UseUnitRoles && resolver != null)
				{
					// Role-model eligibility: only MainBattle ground combatants hold the line.
					// Artillery/SHORAD/MANPADS/scouts/capturers/logistics/carriers drop out by class.
					if (!IsLineEligibleByRole(actor))
						continue;

					// Screen/main-line partition stays list-based (design §2.1): screen-listed
					// MainBattle units screen, every other MainBattle unit forms the main line.
					isScreen = Info.ScreenUnitTypes.Contains(name);
				}
				else
				{
					// Hard exclusion (owned by other modules: capture/repair/supply/scout).
					if (Info.ExcludedActorTypes.Contains(name))
						continue;

					isScreen = Info.ScreenUnitTypes.Contains(name);
					var isMainLine = Info.MainLineUnitTypes.Contains(name);
					if (!isScreen && !isMainLine)
						continue;
				}

				if (assignedAtTick.TryGetValue(actor, out var lastTick) && lastTick > cooldownExpiresBefore)
					continue;
				if (!actor.Info.HasTraitInfo<IPositionableInfo>())
					continue;

				// Out-of-ammo guard: don't push empty units forward as cannon fodder.
				// A future rearm/retreat module will actively route them; for now we just skip.
				if (Info.SkipOutOfAmmoUnits && IsOutOfAmmo(actor))
					continue;

				// Transport reservation: if MountedTransportBotModule has earmarked this
				// actor as a passenger, leave it alone — overriding with AttackMove here
				// would cancel its EnterTransport.
				if (transport != null && transport.IsPassengerReserved(actor))
					continue;

				// Mission-commitment interop (experimental, default off): a unit COMMITTED in the shared
				// PoiGoalGuard ledger belongs to an offense axis / capture / garrison. Skip it so we don't
				// yank a briefly-idle committed unit back to the line and start the forward/back loop. Off ⇒
				// goalGuard is null ⇒ inert (byte-identical). Mirrors PoiOffensiveBotModule's free-pool gate.
				if (goalGuard != null && goalGuard.Ledger.IsCommitted(actor, world.WorldTick))
					continue;

				// On-the-line check: skip if any contested cell is within OnLineRadiusCells.
				var actorCell = actor.Location;
				var onLine = false;
				foreach (var c in contestedCells)
				{
					var dx = c.X - actorCell.X;
					var dy = c.Y - actorCell.Y;
					if ((long)dx * dx + (long)dy * dy <= onLineRadiusSq)
					{
						onLine = true;
						break;
					}
				}

				if (onLine)
					continue;

				reserves.Add((actor, isScreen));
			}

			if (reserves.Count == 0)
				return;

			// Score every contested cell as a candidate slot. Lower combined density
			// (friendly gap + enemy weakness) → higher score. Cells already assigned
			// this tick get a heavy penalty so we spread across the line.
			var assignedSlots = new HashSet<CPos>();
			var assignsThisPass = 0;

			// Send reserves closest to the line first — they arrive faster and feel
			// more responsive.
			reserves.Sort((a, b) =>
			{
				var da = MinSqDistTo(a.Actor.Location, contestedCells);
				var db = MinSqDistTo(b.Actor.Location, contestedCells);
				return da.CompareTo(db);
			});

			foreach (var (actor, isScreen) in reserves)
			{
				if (assignsThisPass >= Info.MaxAssignsPerScan)
					break;

				CPos bestSlot = default;
				var bestScore = long.MinValue;
				var found = false;

				foreach (var c in contestedCells)
				{
					if (assignedSlots.Contains(c))
						continue;

					var (gx, gy) = influenceMap.MapCellToGridCell(c);
					if (gx < 0 || gx >= friendlyInf.GetLength(0) || gy < 0 || gy >= friendlyInf.GetLength(1))
						continue;

					// Lower density on BOTH sides = higher score (gap to fill AND weak enemy = flank).
					// Both weights tunable; with equal weights, units spread evenly along the line
					// and naturally pull toward enemy weak points.
					var score = -(long)Info.FriendlyGapWeight * friendlyInf[gx, gy]
								- (long)Info.EnemyWeaknessWeight * enemyInf[gx, gy];

					if (score > bestScore)
					{
						bestScore = score;
						bestSlot = c;
						found = true;
					}
				}

				if (!found)
					break;

				// Screen units sit AT the slot, but prefer nearby treeline/cover.
				// Main-line units shift behind, toward our SR.
				CPos targetCell;
				if (isScreen)
				{
					targetCell = Info.CoverSearchRadiusCells > 0
						? FindCoverNear(bestSlot, Info.CoverSearchRadiusCells) ?? bestSlot
						: bestSlot;
				}
				else
				{
					targetCell = ShiftToward(bestSlot, srCell, Info.MainLineStandoffCells);
				}

				if (!world.Map.Contains(targetCell))
					continue;

				bot.QueueOrder(new Order("AttackMove", actor, Target.FromCell(world, targetCell), false));
				assignedAtTick[actor] = world.WorldTick;

				// Phase 2 commit-on-order (§4): stake the line assignment in the shared ledger so offense's
				// BuildFreePool defers (closes the reverse steal). TTL = the assignment cooldown, so the claim
				// lapses exactly when this module would itself re-consider the unit. Off ⇒ no write ⇒ frozen.
				if (CommitOnOrderMath.ShouldCommit(Info.CommitLineAssignments, goalGuard != null && !goalGuard.IsTraitDisabled))
					goalGuard.Ledger.Commit(actor, LineObjectiveKey(bestSlot), world.WorldTick, Info.AssignCooldownTicks);

				assignedSlots.Add(bestSlot);
				assignsThisPass++;

				AIUtils.BotDebug("AI ({0}): layered-defence — {1} ({2}) → {3} (slot {4} score {5})",
					player.ClientIndex, actor.Info.Name, isScreen ? "SCREEN" : "MAIN", targetCell, bestSlot, bestScore);
			}

			// Drop dead-actor entries so the dictionary doesn't grow.
			var deadKeys = assignedAtTick.Keys.Where(a => a.IsDead || !a.IsInWorld).ToList();
			foreach (var k in deadKeys)
				assignedAtTick.Remove(k);
		}

		static long MinSqDistTo(CPos from, List<CPos> cells)
		{
			var best = long.MaxValue;
			foreach (var c in cells)
			{
				var dx = c.X - from.X;
				var dy = c.Y - from.Y;
				var d = (long)dx * dx + (long)dy * dy;
				if (d < best)
					best = d;
			}

			return best;
		}

		// Role-model line eligibility (UseUnitRoles): a MainBattle ground combatant that is NOT a
		// passenger carrier. Cargo carriers (bradley/bmp2/m113) are owned by MountedTransportBotModule;
		// the IFVs classify MainBattle by AIUnitRole override, so this partial migration excludes any
		// cargo-carrier by trait to keep the ferry hand-off intact until MountedTransport is itself
		// migrated. See WORKSPACE/DISCOVERIES.md (2026-07-22) and the PITFALL at line 86.
		bool IsLineEligibleByRole(Actor a)
		{
			return resolver.GetRole(a) == UnitRole.MainBattle
				&& !UnitRoleResolver.IsTroopCarrier(a.Info);
		}

		// The "combat ground pool" for the dispersion diagnostic: role-based when UseUnitRoles is on,
		// otherwise the ScreenUnitTypes/MainLineUnitTypes lists (log-only, no sim effect either way).
		bool IsCombatPoolMember(Actor a)
		{
			if (Info.UseUnitRoles && resolver != null)
				return IsLineEligibleByRole(a);

			var name = a.Info.Name.ToLowerInvariant();
			return Info.ScreenUnitTypes.Contains(name) || Info.MainLineUnitTypes.Contains(name);
		}

		List<CPos> CollectContestedCells(bool[,] frontline)
		{
			var result = new List<CPos>();
			if (frontline == null)
				return result;

			var cellSize = influenceMap.Info.CellSize;
			var w = frontline.GetLength(0);
			var h = frontline.GetLength(1);

			for (var x = 0; x < w; x++)
			{
				for (var y = 0; y < h; y++)
				{
					if (!frontline[x, y])
						continue;

					// Use the grid cell's centre map cell as the representative.
					var mapCell = new CPos(x * cellSize + cellSize / 2, y * cellSize + cellSize / 2);
					if (world.Map.Contains(mapCell))
						result.Add(mapCell);
				}
			}

			return result;
		}

		// Find a nearby cover cell (terrain type ∈ Info.CoverTerrainTypes) within
		// `radius` map cells of `centre`. Returns the closest one, or null if no
		// cover is available. Cover snap is what makes the screen DOCTRINE-correct:
		// hidden in treelines / rough ground, not standing in the open.
		CPos? FindCoverNear(CPos centre, int radius)
		{
			CPos? best = null;
			var bestDistSq = long.MaxValue;

			for (var dx = -radius; dx <= radius; dx++)
			{
				for (var dy = -radius; dy <= radius; dy++)
				{
					var cell = new CPos(centre.X + dx, centre.Y + dy);
					if (!world.Map.Contains(cell))
						continue;

					var terrain = world.Map.GetTerrainInfo(cell);
					if (terrain == null || !Info.CoverTerrainTypes.Contains(terrain.Type))
						continue;

					var distSq = (long)dx * dx + (long)dy * dy;
					if (distSq < bestDistSq)
					{
						bestDistSq = distSq;
						best = cell;
					}
				}
			}

			return best;
		}

		// "Out of ammo" = the unit has AmmoPool traits AND every pool is empty.
		// Units with no AmmoPool (e.g. tanks with infinite shells) always return false.
		// Partial-ammo units (one pool empty, another full) return false — still useful.
		static bool IsOutOfAmmo(Actor actor)
		{
			var pools = actor.TraitsImplementing<AmmoPool>().ToList();
			if (pools.Count == 0)
				return false;
			return pools.All(p => p.CurrentAmmoCount == 0);
		}

		// Shift `from` toward `toward` by `cells` map cells. If the points are
		// nearly coincident (degenerate map layout), return `from` unchanged.
		static CPos ShiftToward(CPos from, CPos toward, int cells)
		{
			var dx = toward.X - from.X;
			var dy = toward.Y - from.Y;
			var len = System.Math.Sqrt(dx * dx + dy * dy);
			if (len < 1)
				return from;

			var sx = (int)System.Math.Round(dx / len * cells);
			var sy = (int)System.Math.Round(dy / len * cells);
			return new CPos(from.X + sx, from.Y + sy);
		}
	}
}
