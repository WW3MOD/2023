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
	[Desc("WW3MOD v2: assigns idle units to screen / main-line positions along the InfluenceMap frontline.")]
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

			TextNotificationsManager.AddSystemLine(
				$"[v2-layered-defence] enabled for {player.PlayerName} ({player.Faction.Name})");
			Log.Write("debug",
				$"[v2-layered-defence] TraitEnabled — player={player.PlayerName} screen-types={Info.ScreenUnitTypes.Count} mainline-types={Info.MainLineUnitTypes.Count} excluded-types={Info.ExcludedActorTypes.Count}");
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (player.WinState != WinState.Undefined || influenceMap == null)
				return;

			if (--scanCountdown > 0)
				return;
			scanCountdown = Info.ScanInterval;

			AssignPositions(bot);
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
			var transport = player.PlayerActor.TraitOrDefault<MountedTransportBotModule>();
			var reserves = new List<(Actor Actor, bool IsScreen)>();
			foreach (var actor in world.Actors)
			{
				if (actor.Owner != player || actor.IsDead || !actor.IsInWorld || !actor.IsIdle)
					continue;

				var name = actor.Info.Name.ToLowerInvariant();

				// Hard exclusion (owned by other modules: capture/repair/supply/scout).
				if (Info.ExcludedActorTypes.Contains(name))
					continue;

				var isScreen = Info.ScreenUnitTypes.Contains(name);
				var isMainLine = Info.MainLineUnitTypes.Contains(name);
				if (!isScreen && !isMainLine)
					continue;

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
