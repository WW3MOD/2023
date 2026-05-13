#region Copyright & License Information
/*
 * WW3MOD LayeredDefenceBotModule — Stage B.1 of the doctrine roadmap.
 *
 * Reads InfluenceMap.GetFrontline(perspective) every N ticks. For each
 * IDLE unit:
 *
 *   - SCREEN-eligible (light infantry): move to the nearest contested
 *     cell. The screen sits AT the contested edge; B.2 adds treeline
 *     and garrison preference for cover.
 *
 *   - MAIN-LINE-eligible (heavy infantry + vehicles + AA + artillery):
 *     move to a standoff position behind the frontline — frontline cell
 *     shifted by MainLineStandoffCells along the vector from cell ->
 *     own SR.
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

		[Desc("Actor types of the bot's home Supply Route — used to compute the 'behind' direction.")]
		public readonly HashSet<string> SupplyRouteTypes = new() { "supplyroute" };

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
			var cooldownExpiresBefore = world.WorldTick - Info.AssignCooldownTicks;

			// Walk idle units. Cheap filter first (player ownership, idle, alive), then
			// classify into screen / main-line.
			foreach (var actor in world.Actors)
			{
				if (actor.Owner != player || actor.IsDead || !actor.IsInWorld || !actor.IsIdle)
					continue;

				var name = actor.Info.Name;
				var isScreen = Info.ScreenUnitTypes.Contains(name);
				var isMainLine = Info.MainLineUnitTypes.Contains(name);
				if (!isScreen && !isMainLine)
					continue;

				// Cooldown gate. Keep the actor entry around so the dictionary tracks history.
				if (assignedAtTick.TryGetValue(actor, out var lastTick) && lastTick > cooldownExpiresBefore)
					continue;

				if (!actor.Info.HasTraitInfo<IPositionableInfo>())
					continue;

				// Nearest contested cell (Manhattan distance in CPos).
				var actorCell = actor.Location;
				var nearestContested = NearestCell(contestedCells, actorCell);

				CPos targetCell;
				if (isScreen)
				{
					// Screen sits AT the contested edge.
					targetCell = nearestContested;
				}
				else
				{
					// Main line: shift from contested cell toward own SR by MainLineStandoffCells.
					targetCell = ShiftToward(nearestContested, srCell, Info.MainLineStandoffCells);
				}

				if (!world.Map.Contains(targetCell))
					continue;

				bot.QueueOrder(new Order("AttackMove", actor, Target.FromCell(world, targetCell), false));
				assignedAtTick[actor] = world.WorldTick;

				AIUtils.BotDebug("AI ({0}): layered-defence — {1} ({2}) → {3} (contested at {4})",
					player.ClientIndex, name, isScreen ? "SCREEN" : "MAIN", targetCell, nearestContested);
			}

			// Drop entries for actors that have died, so the dictionary doesn't grow.
			var deadKeys = assignedAtTick.Keys.Where(a => a.IsDead || !a.IsInWorld).ToList();
			foreach (var k in deadKeys)
				assignedAtTick.Remove(k);
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

		static CPos NearestCell(List<CPos> cells, CPos from)
		{
			var best = cells[0];
			var bestDist = (best - from).LengthSquared;
			for (var i = 1; i < cells.Count; i++)
			{
				var d = (cells[i] - from).LengthSquared;
				if (d < bestDist)
				{
					bestDist = d;
					best = cells[i];
				}
			}

			return best;
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
