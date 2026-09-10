#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

/*
 * THE DEFCON 3 DIVIDING WALL -- a line neither side may cross, aircraft included.
 *
 * This trait owns the line and answers "may this player be here". It does NOT enforce anything by
 * itself; three separate layers do that, and they all ask this one object:
 *
 *   1. Aircraft.ResolveOrder + AircraftMoveOrderTargeter -- refuse the order, paint a blocked cursor.
 *   2. DefconWallTurnBack                                -- turn back on approach, ticked per airframe.
 *   3. Aircraft.SetPosition                              -- refuse the write. The hard guarantee.
 *
 * THE LINE IS DRAWN ONCE, NOT TWICE, and this is the finding the whole design rests on.
 * GrantConditionOnTerrain.Tick reads `self.Location` with NO ALTITUDE GATE (Conditions/
 * GrantConditionOnTerrain.cs:48), and for an aircraft `self.Location` is Aircraft.TopLeft, i.e.
 * `Map.CellContaining(CenterPosition)` (Air/Aircraft.cs:287) -- the cell under the airframe's centre,
 * whatever its altitude. That resolves through Map.GetTerrainInfo -> GetTerrainIndex -> CustomTerrain
 * (Map.cs:1725-1727, :1703-1719). So the CustomTerrain write below, which is what makes the wall
 * solid to GROUND units, is already visible to an aircraft flying over it with no new engine code.
 * There is no second geometry to author and no second data path to drift.
 *
 * The three enforcement layers nonetheless ask DefconWallGeometry directly rather than reading the
 * terrain type back out. That is not a second geometry -- it is the SAME line, one step earlier in
 * the pipeline, before it is flattened into a byte per cell. Reading it back through the terrain
 * would lose the sign (which side) and the distance (how far), and both layers 1 and 2 need those.
 *
 * WHAT MAKES THIS INERT, which matters because the user tests from main:
 *   - Skirmish is a strict no-op. DefconEscalation holds NoLevel, ActiveLevels never contains it.
 *   - No shipped map authors a line, so Start == End, so the geometry is degenerate, so IsActive is
 *     false before the level is even consulted.
 *   - Zero shared-random draws. Nothing here touches World.SharedRandom.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("A dividing line that neither side may cross, aircraft included, while the match sits at",
		"one of " + nameof(DefconWallInfo.ActiveLevels) + ". Attach to the World actor. Requires",
		nameof(DefconEscalation) + " on the same actor; without it this trait does nothing at all.",
		"Inert until a map authors " + nameof(DefconWallInfo.Start) + " and " + nameof(DefconWallInfo.End) + ".")]
	public class DefconWallInfo : TraitInfo
	{
		[Desc("One end of the dividing line, in cells. Leave equal to " + nameof(End) + " -- the default --",
			"for no wall at all, which is what every shipped map does today.",
			"THE LINE IS TREATED AS INFINITE: these two cells fix its position and direction only, and",
			"the half-planes either side extend past the map edge. A line that stops short would be a",
			"line an aircraft flies around.")]
		public readonly CPos Start = CPos.Zero;

		[Desc("The other end of the dividing line, in cells. See " + nameof(Start) + ".")]
		public readonly CPos End = CPos.Zero;

		[Desc("Half the thickness of the wall itself, in world units (1024 = one cell). The default",
			"gives a band one cell wide, centred on the line. Cells inside the band are impassable to",
			"ground units and forbidden to aircraft REGARDLESS of which side they belong to, which is",
			"what stops a player parking on the line.")]
		public readonly WDist HalfWidth = new WDist(512);

		[Desc("Terrain type written into Map.CustomTerrain for every cell of the wall band while the",
			"wall is up, and reverted cell-by-cell to its previous value when it comes down.",
			"`Wall` is deliberate and is not a new terrain type: it already exists in all four shipped",
			"tilesets, it is named by no locomotor's TerrainSpeeds (and 'Leave out entries for",
			"impassable terrain' -- Locomotor.cs:84, :181-182 -- so absence IS impassability), and it",
			"already carries an AcceptsSmudgeType list so the smudge gate stays green. Introducing a",
			"bespoke type would mean four tileset edits and a smudge-coverage entry for no gain.")]
		public readonly string TerrainType = "Wall";

		[Desc("DEFCON levels at which the wall stands. 3 is the positioning phase and is the only",
			"level the design puts a wall at: DEFCON 2 is hold-fire and DEFCON 1 is open war, and in",
			"both of those the line is gone.")]
		public readonly int[] ActiveLevels = { 3 };

		public override object Create(ActorInitializer init) { return new DefconWall(init.Self, this); }
	}

	// ISync for the same reason DefconEscalation carries it: Actor.cs:206 hashes a trait only when it
	// `is ISync`, so without the interface the [Sync] member below would be inert and this trait would
	// be missing from every sync report. `active` is the only mutable state that changes what the
	// simulation does, and it is projected to an int because the runtime hasher cannot hash a bool.
	public class DefconWall : INotifyCreated, ITick, ISync
	{
		readonly DefconWallInfo info;
		readonly World world;
		readonly DefconWallGeometry geometry;

		// Which half-plane each player's home sits in, resolved once on first ask and then cached.
		// A player never changes sides: HomeLocation is fixed at match start.
		readonly Dictionary<Player, int> sides = new Dictionary<Player, int>();

		// Previous CustomTerrain byte for every cell the wall overwrote, so coming down restores
		// exactly what was there rather than assuming byte.MaxValue. Same shape as ChangesTerrain.cs.
		readonly Dictionary<CPos, byte> overwritten = new Dictionary<CPos, byte>();

		DefconEscalation escalation;
		bool active;

		[Sync]
		int SyncActive => active ? 1 : 0;

		public DefconWall(Actor self, DefconWallInfo info)
		{
			this.info = info;
			world = self.World;

			var start = self.World.Map.CenterOfCell(info.Start);
			var end = self.World.Map.CenterOfCell(info.End);
			geometry = new DefconWallGeometry(start.X, start.Y, end.X, end.Y, info.HalfWidth.Length);
		}

		/// <summary>
		/// The one gate every enforcement layer checks first, and the reason none of them cost anything
		/// on a shipped map: false whenever no line is authored, whenever the mode is Skirmish, and
		/// whenever the level is not one the wall stands at.
		/// </summary>
		public bool IsActive => active;

		void INotifyCreated.Created(Actor self)
		{
			// TraitOrDefault, not Trait: a map that strips DefconEscalation must leave this inert
			// rather than throw. Mirrors DefconCasualtyObserver.Created.
			escalation = self.World.WorldActor.TraitOrDefault<DefconEscalation>();
			Apply();
		}

		void ITick.Tick(Actor self)
		{
			Apply();
		}

		void Apply()
		{
			// Polling one int per tick on the World actor, for the same reason
			// GrantConditionOnDefconLevel polls: no creation-order dependency, and no edge to miss.
			var wanted = !geometry.IsDegenerate
				&& escalation != null
				&& escalation.Level != DefconEscalationState.NoLevel
				&& info.ActiveLevels.Contains(escalation.Level);

			if (wanted == active)
				return;

			active = wanted;
			if (active)
				RaiseWall();
			else
				LowerWall();
		}

		void RaiseWall()
		{
			var terrainIndex = world.Map.Rules.TerrainInfo.GetTerrainIndex(info.TerrainType);

			foreach (var cell in world.Map.AllCells)
			{
				var centre = world.Map.CenterOfCell(cell);
				if (!geometry.IsInWallBand(centre.X, centre.Y))
					continue;

				overwritten[cell] = world.Map.CustomTerrain[cell];
				world.Map.CustomTerrain[cell] = terrainIndex;
			}

			Log.Write("debug", $"DEFCON wall raised over {overwritten.Count} cells.");
		}

		void LowerWall()
		{
			foreach (var kv in overwritten)
				world.Map.CustomTerrain[kv.Key] = kv.Value;

			Log.Write("debug", $"DEFCON wall lowered, {overwritten.Count} cells restored.");
			overwritten.Clear();
		}

		int SideFor(Player player)
		{
			if (player == null)
				return DefconWallGeometry.NoSide;

			if (sides.TryGetValue(player, out var side))
				return side;

			var home = world.Map.CenterOfCell(player.HomeLocation);
			side = geometry.SideOf(home.X, home.Y);
			sides.Add(player, side);
			return side;
		}

		/// <summary>
		/// May this player's actors be at this position? False whenever the wall is down, so callers
		/// on hot paths can lean on this single test.
		/// </summary>
		public bool IsBeyondWall(Player player, WPos pos)
		{
			if (!active)
				return false;

			return geometry.IsBeyond(SideFor(player), pos.X, pos.Y);
		}

		public bool IsBeyondWall(Player player, CPos cell)
		{
			if (!active)
				return false;

			var centre = world.Map.CenterOfCell(cell);
			return geometry.IsBeyond(SideFor(player), centre.X, centre.Y);
		}

		/// <summary>
		/// How far past the line this position is, in world units; negative on the player's own side.
		/// The turn-back layer uses the negative range to react BEFORE the line is reached.
		/// </summary>
		public long DepthBeyondWall(Player player, WPos pos)
		{
			if (!active)
				return long.MinValue / 4;

			return geometry.DepthBeyond(SideFor(player), pos.X, pos.Y);
		}

		/// <summary>
		/// A position on the player's own side of the line, <paramref name="clearance"/> world units
		/// back from it, straight along the normal from where the actor is now. This is deliberately
		/// the shortest way home rather than a retreat to base: it un-does the violation and nothing
		/// more, so the player's own positioning is disturbed as little as the rule allows.
		/// </summary>
		public WPos NearestPositionOnOwnSide(Player player, WPos pos, WDist clearance)
		{
			var side = SideFor(player);
			var normal = geometry.NormalTowards(side);
			if (normal.X == 0 && normal.Y == 0)
				return pos;

			// SIGNED, deliberately. Depth is negative on the actor's own side, so adding it lands the
			// actor exactly `clearance` from the LINE rather than `clearance` from wherever it happened
			// to be standing -- which is what the summary above promises and is the only version that
			// gives every turned-back airframe the same standoff.
			//
			// The clamp cannot bite in practice: DefconWallTurnBack only calls this once depth has
			// reached -Margin, and its Info requires Clearance to exceed Margin, so the sum is positive.
			// It is kept so that a mis-authored Clearance/Margin pair cannot push an airframe BACKWARDS
			// across the line it is retreating from, which would be the one failure worse than no
			// turn-back at all.
			var depth = geometry.DepthBeyond(side, pos.X, pos.Y);
			var travel = Math.Max(0L, depth + clearance.Length);

			return pos + new WVec((int)(normal.X * travel / 1024), (int)(normal.Y * travel / 1024), 0);
		}
	}
}
