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
 * THIS IS NO LONGER INERT ON THE SHIPPED MAPS, and that is the point of the change that did it.
 * The DEFCON readout tells the player, at DEFCON 3, "The border is closed. Neither side may cross
 * it." While no map authored a line that sentence was simply false, and a readout that lies is
 * worse than no readout. world.yaml now sets DeriveFromSpawns, so the line is DERIVED from where
 * the match's two sides actually start (see WorldLoaded below) rather than drawn per map.
 *
 * WHAT IS STILL INERT, which matters because the user tests from main:
 *   - Skirmish is a strict no-op. DefconEscalation holds NoLevel, ActiveLevels never contains it,
 *     and Skirmish is still the default game mode. A Skirmish match is unchanged.
 *   - A map that authors no line AND leaves DeriveFromSpawns off still gets a degenerate geometry,
 *     so IsActive is false before the level is even consulted. That is the C# default; only
 *     world.yaml turns it on.
 *   - Zero shared-random draws. Nothing here touches World.SharedRandom.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Primitives;
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

		[Desc("Half the thickness of the wall itself, in world units (1024 = one cell). Cells inside",
			"the band are impassable to ground units and forbidden to aircraft REGARDLESS of which",
			"side they belong to, which is what stops a player parking on the line.",
			"",
			"DO NOT LOWER THIS TO 512. A one-cell band seals an AXIS-ALIGNED line and nothing else.",
			"On any other angle the banded cells touch only at their corners and an 8-connected step",
			"goes straight between two of them -- so the wall is drawn, is visible, and does not",
			"divide anything. It was 512 until the derivation landed, and the one worked example",
			"authors a VERTICAL line, which is the single case where 512 works; that is why nothing",
			"caught it. Measured: at 512 every derived line on all ten shipped maps leaked on every",
			"ground locomotor (tools/nav-guard/defcon_wall_audit.py).",
			"",
			"The floor is arithmetic, not taste. Two 8-adjacent cells differ by at most one cell in",
			"each axis, so their perpendicular distances to the line differ by at most sqrt(2) cells;",
			"when they straddle the line those distances SUM to at most sqrt(2), so the nearer one is",
			"within sqrt(2)/2 = 0.707 cells = 724 world units. Any value >= 724 therefore catches one",
			"of every straddling pair at every angle. 1024 is that bound with margin, and is one",
			"whole cell either side.")]
		public readonly WDist HalfWidth = new WDist(1024);

		// ---- THE BORDER AS A REGION RATHER THAN A LINE ------------------------------------------
		// A line is the right border on an open map and is what every shipped map gets. It is the
		// WRONG border on a map whose natural division is a river: the bisector ignores the water and
		// cuts across it at whatever angle the spawns happen to imply. The two fields below let a map
		// author the border as a SET OF CELLS instead -- "every water, river and bridge cell, plus
		// these" -- which is a set and not a function of position.
		//
		// PRECEDENCE, AND IT IS EXPLICIT BECAUSE THREE THINGS NOW COMPETE:
		//   1. A REGION WINS over everything. Either field below being non-empty selects the region
		//      path, and neither Start/End nor DeriveFromSpawns is consulted at all.
		//   2. An authored Start/End beats DeriveFromSpawns, exactly as it always has.
		//   3. DeriveFromSpawns is the fallback, and is what world.yaml switches on.
		// A REGION THAT DIVIDES NOTHING DOES NOT FALL BACK TO A LINE. It logs and the wall stays
		// down. Falling back would hand a map author who believed they had a river border a straight
		// line cutting across it, which is the one outcome worse than no border -- the same ruling
		// DeriveFromSpawns makes for a three-way free-for-all.

		[Desc("Terrain type names that make up the border, e.g. `Water, River, Bridge`. Every cell of",
			"one of these types becomes part of the border while the wall stands, exactly as the band",
			"cells of a line do: impassable to ground units and forbidden to aircraft on both sides.",
			"Resolved against the tileset's own type list, so an unknown name is ignored rather than",
			"throwing -- the four shipped tilesets do not all carry the same types.",
			"",
			"EMPTY BY DEFAULT, which together with " + nameof(RegionCells) + " is what keeps every",
			"shipped map on the line path. Setting either one switches this map to the region path;",
			"see the precedence note above.",
			"",
			"A REGION IS ONLY A BORDER IF IT SEPARATES THE MAP, and terrain alone usually does not:",
			"a river that stops short of the map edge leaves a land bridge round the end, and the",
			"cells that close it have to be authored by hand in " + nameof(RegionCells) + ". Check",
			"with tools/nav-guard/defcon_wall_audit.py --region-terrain, which reports separation per",
			"LOCOMOTOR -- the answer differs between infantry and vehicles, because a vehicle already",
			"cannot ford what infantry can walk round.")]
		public readonly string[] RegionTerrainTypes = Array.Empty<string>();

		[Desc("Individual cells added to the border, on top of whatever " + nameof(RegionTerrainTypes),
			"selected. This is how a terrain feature that nearly divides the map is closed off at the",
			"ends, and how a border is drawn on a map with no such feature at all.",
			"",
			"A flat comma-separated list of X,Y pairs: `44,0, 44,1, 45,1` is three cells.",
			"",
			"DO NOT AUTHOR A ONE-CELL-WIDE DIAGONAL. The same arithmetic that forbids a 512 " +
			nameof(HalfWidth) + " applies here and for the same reason: cells that touch only at their",
			"corners do not seal anything, because an 8-connected step goes straight between two of",
			"them. A hand-drawn diagonal must be two cells wide. This is not theoretical -- a one-cell",
			"band leaked on 131 map/locomotor combinations when the line shipped that way, and the",
			"audit tool reports the same failure for a region.")]
		public readonly CPos[] RegionCells = Array.Empty<CPos>();

		[Desc("Derive the line from where the match's two sides actually start, instead of requiring",
			"every map to author one. The combatants are split into their two alliance groups, each",
			"group's centroid home is taken, and the line is the perpendicular bisector of those two",
			"points -- so it is equidistant from both sides by construction, on any map.",
			"",
			"THIS IS WHY NO SHIPPED MAP NEEDS AN ENTRY. It also handles what an authored line cannot:",
			"on the 4- and 6-spawn maps, which spawns are occupied is not known until the match",
			"starts, so the fair line is not a property of the map at all.",
			"",
			"AN AUTHORED " + nameof(Start) + "/" + nameof(End) + " WINS over this, which is what makes",
			"the whole thing an override list -- a map whose terrain makes the bisector silly draws",
			"its own line and this field is ignored for it.",
			"",
			"EXACTLY TWO ALLIANCE GROUPS OR NO WALL. A three-way free-for-all derives nothing, on the",
			"same reasoning as two coincident spawns: no line is visibly wrong and therefore fixable,",
			"while a line pointing somewhere nobody chose is not.",
			"",
			"FALSE HERE ON PURPOSE. The C# default must leave the trait inert so that a map, scenario",
			"or test that does not ask for a wall cannot grow one; world.yaml is the single place it",
			"is switched on.")]
		public readonly bool DeriveFromSpawns = false;

		[Desc("How far past the midpoint the derived endpoints are pushed, in cells. The line is",
			"treated as INFINITE, so this does not decide how far the wall reaches -- it only fixes",
			"the direction, and a larger value means less angular error from integer truncation.",
			"512 is comfortably past the corner of the largest shipped map (128x128) and is the value",
			"the connectivity audit was run at.")]
		public readonly int DerivedExtendCells = 512;

		// ---- HOW THE BORDER IS DRAWN ------------------------------------------------------------
		// It has to be drawn by this trait, because writing CustomTerrain draws nothing at all.
		// CustomTerrain is read by Map.GetTerrainIndex (Map.cs:1718) -- pathfinding, locomotor cost,
		// GrantConditionOnTerrain -- and by nothing in the terrain draw: TerrainRenderer renders from
		// Map.Tiles and subscribes only to Map.Tiles and Map.Height (TerrainRenderer.cs:92-93, :96).
		// BuildableTerrainOverlay is the proof by contrast: it needs its OWN sprite layer and its own
		// subscription to CustomTerrain.CellEntryChanged (:73) precisely because the change reaches no
		// renderer by itself. So the wall sealed the map while the map's own grass stayed on screen,
		// and the DEFCON readout announced a closed border over an unmarked stretch of field.

		[Desc("Draw the border at all. False leaves the wall enforced but invisible, which is the",
			"state this feature shipped in and is not a state to return to on purpose.")]
		public readonly bool RenderBorder = true;

		[Desc("Fill drawn over the wall band itself.",
			"",
			"IT IS DRAWN OVER UNITS AND THAT COSTS NOTHING, which is worth stating because it looks",
			"like it should: the band is exactly the set of cells no ground unit may occupy, so there",
			"is never a unit underneath it to hide. Alpha is kept low anyway so an aircraft crossing",
			"above it stays readable.")]
		public readonly Color BandColor = Color.FromArgb(70, 255, 96, 48);

		[Desc("Colour of the line along the centre of the band, and of the hatch strokes.",
			"Amber against this mod's greens, greys and blues: the border must read as a RULE rather",
			"than as terrain, and no tileset draws a perfectly straight amber line across a map.")]
		public readonly Color LineColor = Color.FromArgb(235, 255, 190, 40);

		[Desc("Width of the centre line, in PIXELS rather than world units -- so it stays visible when",
			"the map is zoomed out, which a world-space width would not.")]
		public readonly float LineWidth = 2;

		[Desc("Spacing between the perpendicular hatch strokes, in cells. The hatching is what stops",
			"the line reading as a river or a road: a repeated cross-stroke is border notation.")]
		public readonly int HatchSpacing = 3;

		[Desc("Width of the hatch strokes, in pixels.")]
		public readonly float HatchWidth = 1;

		// ---- WHAT A REFUSED ORDER SAYS ----------------------------------------------------------
		// Hovering a band cell already showed move-blocked, because Wall is in no locomotor's
		// TerrainSpeeds and Mobile's targeter flags an unreachable destination (Mobile.cs:1240-1243).
		// The silence was on the OTHER side: ordering a unit to a perfectly legal cell BEYOND the
		// border gave an ordinary cursor, accepted the order, and then nothing moved.

		[NotificationReference("Speech")]
		[Desc("Speech notification when a move order is refused for crossing the border.",
			"Null by default and deliberately unset in world.yaml -- there is no recorded line for it",
			"yet, and naming a sound that does not exist is worse than staying quiet. The hook is",
			"here so adding one is a yaml edit.")]
		public readonly string CrossingRefusedNotification = null;

		[Desc("Transient text notification shown when a move order is refused for crossing the border.")]
		public readonly string CrossingRefusedTextNotification = null;

		[Desc("Minimum ticks between two crossing-refused notifications for one player. Selecting",
			"twenty units and ordering them across is ONE decision and should be one line of feedback,",
			"not twenty. 50 ticks is 3 s at the 60 ms timestep (16.67 ticks/s, NOT 25).")]
		public readonly int CrossingRefusedNotificationInterval = 50;

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
	public class DefconWall : INotifyCreated, IWorldLoaded, ITick, ISync, IRenderAnnotations
	{
		// Last tick each player was told its order was refused, so twenty units ordered across the
		// border produce one line rather than twenty. Not synced: it decides nothing the simulation
		// reads, only whether a client prints a line it has already printed.
		readonly Dictionary<Player, int> lastRefusalTick = new Dictionary<Player, int>();

		readonly DefconWallInfo info;
		readonly World world;

		// NOT readonly: WorldLoaded replaces it with the derived line when no line was authored.
		// Nothing may cache a side before that happens -- see the note in WorldLoaded.
		DefconWallGeometry geometry;

		// THE BORDER AS A SET OF CELLS, or null on every map that uses the line. Non-null means the
		// region path: `geometry` is then never consulted and is left in whatever state the Info
		// authored, which for a region map is degenerate. Exactly one of the two is live at a time,
		// and IsRegion is the single test that decides which -- there is no map with both.
		DefconWallRegion region;

		// Which half-plane each player's home sits in, resolved once on first ask and then cached.
		// A player never changes sides: HomeLocation is fixed at match start.
		readonly Dictionary<Player, int> sides = new Dictionary<Player, int>();

		// Previous CustomTerrain byte for every cell the wall overwrote, so coming down restores
		// exactly what was there rather than assuming byte.MaxValue. Same shape as ChangesTerrain.cs.
		readonly Dictionary<CPos, byte> overwritten = new Dictionary<CPos, byte>();

		DefconEscalation escalation;

		// Resolved alongside `escalation` and for the same reason: the component labelling this trait
		// invalidates is a WORLD-actor trait, so `self` is the only valid handle while Created runs.
		// Null on any world without it, which is every world outside the @experimental/@stable bots.
		CrossingMap crossingMap;

		bool active;

		// The border is built ONCE, and no longer necessarily by our own WorldLoaded -- see
		// ResolveBorder below for why another trait's WorldLoaded may get there first.
		bool borderResolved;

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
			// rather than throw.
			//
			// SELF, NOT self.World.WorldActor -- and this crashed every match until it was.
			// DefconCasualtyObserver reads WorldActor here and is correct to, because it is
			// [TraitLocation(SystemActors.Player)] and player actors are built after the world
			// actor exists. This trait IS a world-actor trait, and World.cs:252 is literally
			// `WorldActor = CreateActor(...)` -- so while our own Created runs, that field is
			// still null and any access through it throws. `self` is the same actor and is
			// always valid. Copying an idiom is only safe once you have checked it was written
			// for the same actor.
			escalation = self.TraitOrDefault<DefconEscalation>();
			crossingMap = self.TraitOrDefault<CrossingMap>();
			Apply();
		}

		/// <summary>
		/// Derive the dividing line from where the two sides actually start, when no map authored one.
		/// </summary>
		// IWorldLoaded RATHER THAN Created, and the ordering is the whole reason. Player.HomeLocation
		// is assigned in the Player constructor (Player.cs:182), and every Player is built in the
		// World constructor (World.cs:63) -- both of which finish before IWorldLoaded is invoked on
		// the world actor's traits (World.cs:334). Deriving in Created would read homes that do not
		// exist yet, which is the same class of mistake as the WorldActor null this trait already
		// shipped once.
		//
		// DETERMINISTIC WITHOUT QUALIFICATION. World.Players is one array built identically on every
		// client, this walks it in order, alliance masks are fixed at construction, and everything
		// downstream is integer. No shared random is drawn, so every client derives the same line.
		void IWorldLoaded.WorldLoaded(World w, OpenRA.Graphics.WorldRenderer wr)
		{
			ResolveBorder();
		}

		/// <summary>
		/// Build the border -- derived line or authored region -- once, on whichever comes first: our
		/// own <see cref="IWorldLoaded"/> or the first caller that asks where the wall will be.
		/// </summary>
		// LAZY BECAUSE IWorldLoaded ORDER IS TRAIT ORDER, AND SOMETHING RUNS BEFORE US.
		// SpawnStartingUnits is declared at world.yaml:638 and this trait at :925, so the starting
		// units are placed while `geometry` is still the degenerate Info default and `region` is
		// still null -- it asked where the border was and was told there wasn't one. Reordering the
		// two yaml blocks would not have been enough either: the CustomTerrain write happens in the
		// first Tick, which is after EVERY IWorldLoaded, so no trait order makes the band readable
		// from the ground during world load. The answer is to make the question answerable early
		// instead, which it always could be: every input here -- the map, its terrain, and the
		// players' HomeLocations (fixed in the Player constructor, World.cs:63) -- exists before the
		// first IWorldLoaded runs.
		//
		// IDEMPOTENT, AND THAT IS WHAT KEEPS BuildRegion's OWN PRECONDITION TRUE: it reads
		// Map.GetTerrainIndex, which is CustomTerrain-aware, so re-running it once the wall stood
		// would read the wall back in as border terrain and grow it. Running EARLIER than it used to
		// is safe for the same reason running at WorldLoaded was -- the wall is still down either
		// way -- and running a second time is now impossible rather than merely unlikely.
		//
		// AND THE PRECONDITION IS NOW LOAD-BEARING AT A NEW PLACE, which is worth stating because it
		// is the one thing this change quietly moved. BuildRegion no longer runs at world.yaml:935;
		// on a region map it runs at :638, when SpawnStartingUnits asks. It still reads the MAP's own
		// terrain, and that was AUDITED rather than assumed: THIS TRAIT IS THE ONLY WRITER OF
		// Map.CustomTerrain ANYWHERE IN world.yaml. The other writers in the engine are
		// CliffBackImpassabilityLayer, ResourceLayer/EditorResourceLayer, Bridge, GroundLevelBridge
		// and ChangesTerrain; none of the world-actor ones is declared by this mod, and the rest are
		// building traits that cannot run before their actor exists. So the window between :638 and
		// :935 is not merely empty today, there is nothing in the mod that could fill it. If a
		// CustomTerrain-writing world trait is ever added there, BuildRegion must read Map.Tiles
		// directly rather than through the CustomTerrain-aware GetTerrainIndex.
		void ResolveBorder()
		{
			if (borderResolved)
				return;

			borderResolved = true;

			var w = world;

			// A REGION WINS OVER BOTH THE AUTHORED LINE AND THE DERIVATION, and returns either way:
			// a region that divides nothing leaves the wall DOWN rather than falling through to a
			// line. See the precedence note on DefconWallInfo.RegionTerrainTypes for why.
			if (info.RegionTerrainTypes.Length > 0 || info.RegionCells.Length > 0)
			{
				BuildRegion(w);
				return;
			}

			// AN AUTHORED LINE WINS. IsDegenerate is exactly "no line was authored", so this is the
			// override list working: a map that drew its own line never reaches the derivation.
			if (!info.DeriveFromSpawns || !geometry.IsDegenerate)
				return;

			var homes = new List<(int Group, CPos Home)>();
			var representatives = new List<Player>();

			foreach (var player in w.Players)
			{
				// Spectators and the world/neutral players have no side to be on. Including them
				// would drag a centroid toward a player who is not in the match.
				//
				// SHARED WITH NuclearExchange RATHER THAN SPELLED OUT, and the two runs that forced
				// that are named in CombatantSides' header. This used to read
				// `player.NonCombatant || player.Spectating` and both of those runtime flags are
				// FALSE for a client-occupied slot however the map authored it (Player.cs's client
				// branch never assigns them), so an Observer counted as a third alliance group here
				// and the wall silently stayed down for the whole of DEFCON 3.
				if (!CombatantSides.CountsAsASide(player))
					continue;

				var group = -1;
				for (var i = 0; i < representatives.Count; i++)
				{
					if (representatives[i].IsAlliedWith(player))
					{
						group = i;
						break;
					}
				}

				if (group < 0)
				{
					representatives.Add(player);
					group = representatives.Count - 1;
				}

				homes.Add((group, player.HomeLocation));
			}

			var (start, end) = DefconWallGeometry.BisectorOfSides(homes, info.DerivedExtendCells);
			if (start == end)
			{
				Log.Write("debug", $"DEFCON wall: no line derived from {homes.Count} combatant " +
					$"home(s) in {representatives.Count} alliance group(s); the wall stays down.");
				return;
			}

			var from = w.Map.CenterOfCell(start);
			var to = w.Map.CenterOfCell(end);
			geometry = new DefconWallGeometry(from.X, from.Y, to.X, to.Y, info.HalfWidth.Length);

			// Defensive: a side cached against the OLD geometry would be a permanently wrong answer
			// for that player. The dictionary is empty here in practice -- nothing can have asked
			// while the geometry was degenerate, because every entry point early-returns on !active
			// -- so this costs nothing and removes the need to re-derive that argument later.
			sides.Clear();

			Log.Write("debug", $"DEFCON wall derived from {homes.Count} home(s) in " +
				$"{representatives.Count} group(s): {start} .. {end}.");
		}

		/// <summary>
		/// Build the border from the authored terrain types and cells. Runs once, at WorldLoaded, for
		/// the same ordering reason the derivation does.
		/// </summary>
		// THE TERRAIN SCAN IS CrossingMap.ComputeWaterCells's, not a second one. Resolve the authored
		// type NAMES against the tileset's own type list and collect the indices, rather than calling
		// GetTerrainIndex(string) per name -- that overload THROWS on a type the tileset does not
		// carry, and the four shipped tilesets do not all carry the same types, so a map authoring
		// `River` would crash on a tileset that has none.
		//
		// IT READS Map.GetTerrainIndex, WHICH IS CustomTerrain-AWARE, AND THAT IS SAFE HERE ONLY
		// BECAUSE OF WHEN THIS RUNS. WorldLoaded is before the wall has ever been raised, so no cell
		// carries a Wall override yet and what this reads is the map's own terrain. Re-running it
		// while the wall stood would read the wall back in as border terrain and grow it every time.
		// It is built ONCE and never rebuilt, which is also what makes the labels stable for the
		// whole match.
		void BuildRegion(World w)
		{
			var cells = new List<CPos>();

			if (info.RegionTerrainTypes.Length > 0)
			{
				var indices = new HashSet<byte>();
				var types = w.Map.Rules.TerrainInfo.TerrainTypes;
				for (var i = 0; i < types.Length; i++)
					if (Array.IndexOf(info.RegionTerrainTypes, types[i].Type) >= 0)
						indices.Add((byte)i);

				if (indices.Count > 0)
					foreach (var cell in w.Map.AllCells)
						if (w.Map.Contains(cell) && indices.Contains(w.Map.GetTerrainIndex(cell)))
							cells.Add(cell);
			}

			// Authored cells are added unconditionally, INCLUDING cells outside Bounds. That is how
			// the border ring gets closed: the river reaches the map edge but the playable Bounds stop
			// one cell short, and a border that stopped at Bounds would leave a one-cell seam.
			foreach (var cell in info.RegionCells)
				cells.Add(cell);

			var bounds = w.Map.Bounds;

			// PASSABILITY HERE IS `Map.Contains` AND NOTHING ELSE, which is a deliberate limit rather
			// than an oversight. Whether a given cell is passable is a property of a LOCOMOTOR, and
			// this is a World-actor trait that would have to pick one arbitrarily -- infantry can walk
			// where a tank cannot, so a component labelling built on either one is wrong for the
			// other. What this labelling therefore answers is "are these cells joined by open map",
			// which is the weakest and safest reading: it never claims a separation the terrain does
			// not provide. Per-locomotor separation is checked statically instead, by
			// tools/nav-guard/defcon_wall_audit.py, which is where per-locomotor truth belongs.
			region = new DefconWallRegion(bounds.Left, bounds.Top, bounds.Width, bounds.Height,
				cells, c => w.Map.Contains(c));

			sides.Clear();

			if (region.IsDegenerate)
			{
				// Same ruling as a three-way free-for-all deriving no line: no border is visibly wrong
				// and therefore fixable, a border that claims to divide the map and does not is not.
				Log.Write("debug", $"DEFCON wall: the authored region covers {region.BlockedCells.Count} " +
					$"cell(s) and leaves the map in {region.ComponentCount} piece(s); it divides nothing, " +
					"so the wall stays down.");
				region = null;
				return;
			}

			Log.Write("debug", $"DEFCON wall region: {region.BlockedCells.Count} border cell(s), " +
				$"{region.ComponentCount} component(s).");
		}

		/// <summary>True on a map that authored a region; false on every map that uses the line.</summary>
		bool IsRegion => region != null;

		void ITick.Tick(Actor self)
		{
			Apply();
		}

		/// <summary>
		/// Does the wall stand at the level the match is at right now? Says nothing about WHERE it
		/// stands -- <see cref="Apply"/> pairs this with a non-degenerate border, and
		/// <see cref="ForbidsPlacement"/> pairs it with the lazily-built one.
		/// </summary>
		// SHARED SO THE TWO CANNOT DRIFT. A placement filter that answered for a wall the tick loop
		// would not raise is a filter that moves units in Skirmish -- where this is false because
		// DefconEscalation holds NoLevel -- and Skirmish has to stay bit-for-bit what it was.
		bool StandsAtThisLevel => escalation != null
			&& escalation.Level != DefconEscalationState.NoLevel
			&& info.ActiveLevels.Contains(escalation.Level);

		void Apply()
		{
			// Polling one int per tick on the World actor, for the same reason
			// GrantConditionOnDefconLevel polls: no creation-order dependency, and no edge to miss.
			var wanted = (IsRegion || !geometry.IsDegenerate) && StandsAtThisLevel;

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

			// THE REGION PATH WALKS ITS OWN CELL SET RATHER THAN THE MAP. `overwritten` was already a
			// free-form cell -> previous-byte dictionary and nothing downstream requires its keys to
			// have come from a line, so both paths converge here and everything after this point --
			// the restore, the renderer, the CrossingMap invalidation -- is shared unchanged.
			if (IsRegion)
			{
				foreach (var cell in region.BlockedCells)
				{
					overwritten[cell] = world.Map.CustomTerrain[cell];
					world.Map.CustomTerrain[cell] = terrainIndex;
				}
			}
			else
			{
				foreach (var cell in world.Map.AllCells)
				{
					var centre = world.Map.CenterOfCell(cell);
					if (!geometry.IsInWallBand(centre.X, centre.Y))
						continue;

					overwritten[cell] = world.Map.CustomTerrain[cell];
					world.Map.CustomTerrain[cell] = terrainIndex;
				}
			}

			Log.Write("debug", $"DEFCON wall raised over {overwritten.Count} cells.");

			// The bot's ground-component labelling is built ONCE and never rebuilt, so it has to be told
			// that what is passable just changed -- in BOTH directions. Raising without this leaves the
			// bot believing in routes the border has just closed; lowering without it leaves every POI
			// across the old line classified Unreachable for the rest of the match. See
			// CrossingMap.Invalidate for the full account and the determinism argument.
			crossingMap?.Invalidate();
		}

		void LowerWall()
		{
			foreach (var kv in overwritten)
				world.Map.CustomTerrain[kv.Key] = kv.Value;

			Log.Write("debug", $"DEFCON wall lowered, {overwritten.Count} cells restored.");
			overwritten.Clear();
			crossingMap?.Invalidate();
		}

		// THE PICTURE IS DRAWN FROM THE SAME DICTIONARY AS THE RULE. `overwritten` holds exactly the
		// cells whose terrain this trait replaced, so a cell is painted if and only if it is actually
		// impassable -- the visual cannot drift from the enforcement, and "appears at DEFCON 3, gone
		// at DEFCON 2" is structural rather than a second condition somebody has to keep in step:
		// `active` is the same flag that raises and lowers the wall.
		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (!active || !info.RenderBorder)
				yield break;

			foreach (var cell in overwritten.Keys)
			{
				// overwritten covers Map.AllCells, which includes the border ring OUTSIDE Bounds.
				// Painting those would draw into the blacked-out margin past the playable area.
				if (!world.Map.Contains(cell))
					continue;

				var origin = world.Map.CenterOfCell(cell) - new WVec(512, 512, 0);
				yield return new FilledQuadAnnotationRenderable(
					new[]
					{
						origin,
						origin + new WVec(1024, 0, 0),
						origin + new WVec(1024, 1024, 0),
						origin + new WVec(0, 1024, 0),
					},
					info.BandColor);
			}

			// A REGION HAS NO CENTRE LINE AND NO NORMAL, so the line and its hatching are skipped and
			// the band fill above is the whole picture. This is a real loss -- the hatching is what
			// makes a line read as a RULE rather than as terrain -- but a region authored from terrain
			// is already drawn as terrain the player recognises (a river reads as a river), and
			// inventing a centre line through a bending border would point somewhere false. Stated
			// rather than silently skipped: this is the one visual difference between the two paths.
			if (IsRegion)
				yield break;

			// The line is infinite and the map is not. Drawing between the authored endpoints would
			// streak an annotation hundreds of cells into the black past the map edge, because the
			// annotation pass runs AFTER the shroud pass and nothing would cover it.
			var bounds = world.Map.Bounds;
			if (!geometry.ClipToRect((long)bounds.Left * 1024, (long)bounds.Top * 1024,
				(long)bounds.Right * 1024, (long)bounds.Bottom * 1024, out var start, out var end))
				yield break;

			yield return new LineAnnotationRenderable(start, end, info.LineWidth, info.LineColor);

			// Cross-strokes at a fixed cell interval. This is the part that makes it read as a rule:
			// a bare line is a road or a river, a line with repeated perpendicular ticks is border
			// notation, and it survives being zoomed out because the widths are in pixels.
			var span = end - start;
			var length = span.Length;
			var spacing = Math.Max(1, info.HatchSpacing) * 1024;
			if (length < spacing)
				yield break;

			var normal = geometry.NormalTowards(1);
			var half = new WVec(
				(int)(normal.X * geometry.HalfWidth / 1024),
				(int)(normal.Y * geometry.HalfWidth / 1024), 0);
			if (half == WVec.Zero)
				yield break;

			// Accumulated rather than computed as span * travelled / length: the latter overflows int
			// on a large map (a 130-cell span is ~133000 units, and multiplying that by a comparable
			// travelled distance passes 2^31 silently).
			var step = span * spacing / length;
			var point = start;
			for (var travelled = 0; travelled < length; travelled += spacing)
			{
				yield return new LineAnnotationRenderable(point - half, point + half, info.HatchWidth, info.LineColor);
				point += step;
			}
		}

		bool IRenderAnnotations.SpatiallyPartitionable => false;

		/// <summary>
		/// Say that a move order was refused for crossing the border. Throttled per player.
		/// </summary>
		// Called from both enforcement halves so ground and air say the same thing: Mobile.ResolveOrder
		// and Aircraft.ResolveOrder. Both run inside synced order resolution, so this executes on every
		// client -- the same seam GrantConditionOnDeploy's refusal notification already uses.
		public void NotifyCrossingRefused(Actor self)
		{
			if (!active)
				return;

			var owner = self.Owner;
			if (lastRefusalTick.TryGetValue(owner, out var last)
				&& world.WorldTick - last < info.CrossingRefusedNotificationInterval)
				return;

			lastRefusalTick[owner] = world.WorldTick;

			Game.Sound.PlayNotification(world.Map.Rules, owner, "Speech",
				info.CrossingRefusedNotification, owner.Faction.InternalName);
			TextNotificationsManager.AddTransientLine(owner, info.CrossingRefusedTextNotification);
		}

		// THE TWO PATHS USE DIFFERENT SENTINELS FOR "no side", and they must not be interchanged:
		// the line's NoSide is 0, the region's Unlabelled is -1, and the region's component ids START
		// at 0. Sharing a sentinel would make the region's first component indistinguishable from a
		// player with no side at all.
		int NoSideValue => IsRegion ? DefconWallRegion.Unlabelled : DefconWallGeometry.NoSide;

		int SideFor(Player player)
		{
			if (player == null)
				return NoSideValue;

			if (sides.TryGetValue(player, out var side))
				return side;

			// The region is labelled per CELL, so the home cell is asked directly -- no round trip
			// through CenterOfCell, which the line needs only because it is world-unit arithmetic.
			if (IsRegion)
				side = region.SideOf(player.HomeLocation);
			else
			{
				var home = world.Map.CenterOfCell(player.HomeLocation);
				side = geometry.SideOf(home.X, home.Y);
			}

			sides.Add(player, side);
			return side;
		}

		// ======================================================================================
		// THE LEVEL-INDEPENDENT GEOMETRY SURFACE
		// ======================================================================================
		//
		// WHO THIS IS FOR, AND WHY IT CANNOT BE ONE OF THE ACCESSORS ABOVE. Every public member
		// declared before this point -- IsBeyondWall, ForbidsPlacement, DepthBeyondWall,
		// NearestPositionOnOwnSide -- is gated on the escalation level, either through `active` or
		// through StandsAtThisLevel. That is correct for all of them: they answer "may this actor be
		// here", and outside DEFCON 3 the answer is always yes because there is no wall standing.
		//
		// THE DEFCON-3 GATE APPLIES TO BLOCKING, NOT TO GEOMETRY. The border itself is a property of
		// the MAP and of where the sides start, and it resolves in every mode: nine of the ten
		// shipped maps author `DefconWall: RegionCells:` in their own rules.yaml
		// (WORKSPACE/audit/positioning-borders-260919.md), and world.yaml sets DeriveFromSpawns for
		// anything that does not. Asking "which half of the map is this cell in" is therefore
		// answerable in a Skirmish match, where DefconEscalation holds NoLevel and the wall will
		// never stand at all.
		//
		// Consumers, all of which run in EVERY mode and none of which blocks anything:
		//   - PreCapturedStructures (world.yaml:688) -- assigns each neutral capturable structure to
		//     the nearest player on ITS OWN side of the border, and leaves the band neutral.
		//   - the final-exchange targeting on a sibling branch, next.
		//
		// NOTHING HERE MUTATES THE WORLD. ResolveBorder builds an in-memory region or line, writes
		// one Log line and touches no CustomTerrain byte -- raising the wall is RaiseWall's job and
		// is still gated on `active`. So a Skirmish match that calls into this surface is otherwise
		// byte-identical to one that does not.
		//
		// ONE SENTINEL, ONE MEANING, ACROSS BOTH BACKENDS. The two geometries disagree natively --
		// the line's NoSide is 0 and its sides are -1/+1, the region's Unlabelled is -1 and its
		// component ids start at 0 -- and NoSideValue above exists precisely because those must not
		// be interchanged. This surface normalises instead: every real side id is >= 0 and
		// <see cref="NoSide"/> is the single "inside the band, off the map, or otherwise
		// unclassified" answer on both paths. Callers may rely on `side < 0` meaning exactly that.

		/// <summary>
		/// The one sentinel of the level-independent surface: this cell or position is inside the
		/// border band, off the map, or otherwise has no side. Distinct from every real side id,
		/// which are non-negative on both backends.
		/// </summary>
		public const int NoSide = -1;

		/// <summary>
		/// Did a real, non-degenerate border resolve for this match -- an authored region that
		/// divides the map, an authored line, or a derived one? False on a map with no border at
		/// all, in which case every side query below answers <see cref="NoSide"/>.
		/// </summary>
		// LAZY, exactly as ForbidsPlacement is, and for the same reason: this may be the FIRST thing
		// to ask where the border is, because a trait declared earlier in world.yaml than this one
		// runs its IWorldLoaded first. ResolveBorder is idempotent and every input it reads (the
		// map, its terrain, the players' HomeLocations) exists before the first IWorldLoaded -- see
		// its own header.
		public bool HasBorder
		{
			get
			{
				ResolveBorder();
				return IsRegion || !geometry.IsDegenerate;
			}
		}

		/// <summary>
		/// Which side of the border this cell is on: a non-negative id that is stable for the whole
		/// match, or <see cref="NoSide"/> for a cell inside the band, off the map, or on a map with
		/// no border. Two cells share a side id if and only if the border does not separate them.
		/// </summary>
		public int SideOf(CPos cell)
		{
			ResolveBorder();

			// The region is labelled per cell and its Unlabelled IS NoSide -- both -1 -- so a border
			// cell, an off-Bounds cell and an unreachable one all fall out with the right answer and
			// no mapping at all. Its component ids are already 0..ComponentCount-1.
			if (IsRegion)
				return region.SideOf(cell);

			if (geometry.IsDegenerate)
				return NoSide;

			var centre = world.Map.CenterOfCell(cell);
			return NormalisedLineSideAt(centre.X, centre.Y);
		}

		/// <summary>
		/// Which side of the border this world position is on. See <see cref="SideOf(CPos)"/>.
		/// </summary>
		public int SideOf(WPos pos)
		{
			ResolveBorder();

			// Map.CellContaining rather than DefconWallRegion.CellContaining, matching what
			// IsBeyondWall(Player, WPos) already does: the map's own projection is the authority on
			// which cell a position is in.
			if (IsRegion)
				return region.SideOf(world.Map.CellContaining(pos));

			if (geometry.IsDegenerate)
				return NoSide;

			return NormalisedLineSideAt(pos.X, pos.Y);
		}

		/// <summary>
		/// Is this cell part of the border itself -- a cell of an authored region, or a cell of the
		/// band a line draws? Always false on a map with no border. An off-map cell is NOT in the
		/// band, but its <see cref="SideOf(CPos)"/> is still <see cref="NoSide"/>.
		/// </summary>
		public bool IsInBand(CPos cell)
		{
			ResolveBorder();

			if (IsRegion)
				return region.IsInWallBand(cell);

			if (geometry.IsDegenerate)
				return false;

			var centre = world.Map.CenterOfCell(cell);
			return geometry.IsInWallBand(centre.X, centre.Y);
		}

		/// <summary>
		/// Which side of the border this player's HOME is on -- the same question
		/// <see cref="SideFor"/> answers for the gated accessors, normalised onto this surface and
		/// available at any escalation level.
		/// </summary>
		// READS Player.HomeLocation, AND A CALLER THAT HAS A BETTER HOME SHOULD PASS THAT INSTEAD.
		// HomeLocation is CPos.Zero -- the FIELD DEFAULT of PlayerReference.HomeLocation
		// (PlayerReference.cs:41) -- for a map player, and also for a lobby player on any map or
		// scenario that strips MapStartingLocations, because Player.cs:213 falls back to the
		// PlayerReference when there is no IAssignSpawnPoints trait to ask. That is a coordinate
		// default masquerading as a position, and on a region map it reads Unlabelled and therefore
		// NoSide. PreCapturedStructures consequently asks SideOf(WPos) about the player's ANCHOR --
		// their Supply Route, whose CenterPosition sits exactly on the centre of their spawn cell on
		// every shipped map -- rather than calling this. This overload is for callers whose players
		// really are lobby players on a map with spawn points.
		public int SideOf(Player player)
		{
			ResolveBorder();

			if (player == null)
				return NoSide;

			if (IsRegion)
				return SideFor(player);

			if (geometry.IsDegenerate)
				return NoSide;

			// The band test the line needs and the region gets for free: a home inside the band has
			// no side on either backend, so the sentinel keeps exactly one meaning.
			if (IsInBand(player.HomeLocation))
				return NoSide;

			return NormalisedLineSide(SideFor(player));
		}

		/// <summary>
		/// Map the line's native -1/0/+1 at a world position onto this surface's non-negative ids,
		/// treating anything inside the band as unclassified.
		/// </summary>
		int NormalisedLineSideAt(long px, long py)
		{
			if (geometry.IsInWallBand(px, py))
				return NoSide;

			return NormalisedLineSide(geometry.SideOf(px, py));
		}

		static int NormalisedLineSide(int nativeSide)
		{
			if (nativeSide == DefconWallGeometry.NoSide)
				return NoSide;

			return nativeSide < 0 ? 0 : 1;
		}

		/// <summary>
		/// May this player's actors be at this position? False whenever the wall is down, so callers
		/// on hot paths can lean on this single test.
		/// </summary>
		public bool IsBeyondWall(Player player, WPos pos)
		{
			if (!active)
				return false;

			if (IsRegion)
				return region.IsBeyond(SideFor(player), world.Map.CellContaining(pos));

			return geometry.IsBeyond(SideFor(player), pos.X, pos.Y);
		}

		public bool IsBeyondWall(Player player, CPos cell)
		{
			if (!active)
				return false;

			if (IsRegion)
				return region.IsBeyond(SideFor(player), cell);

			var centre = world.Map.CenterOfCell(cell);
			return geometry.IsBeyond(SideFor(player), centre.X, centre.Y);
		}

		/// <summary>
		/// Will the wall forbid <paramref name="player"/> from standing on this cell? The same question
		/// <see cref="IsBeyondWall(Player, CPos)"/> answers -- the band itself counts as beyond -- except
		/// that it does not require the wall to have been RAISED yet, so it can be asked during world
		/// load, before the first Tick has written a single cell of CustomTerrain.
		/// </summary>
		// FOR PLACING THINGS, NOT FOR ENFORCEMENT. The three enforcement layers all run long after the
		// wall is up and must keep using IsBeyondWall: asking THIS on a hot path would resolve the
		// border for a match that has not reached DEFCON 3 yet. What needs it is world load -- see
		// SpawnStartingUnits, which chose a cell inside the band on arena-tank-duel because the ground
		// it tested had not been written yet and this trait had no way to be asked.
		//
		// STILL FALSE FOR THE WHOLE OF SKIRMISH, and that is the byte-identity guarantee: the level
		// test comes FIRST, so a Skirmish match never even builds a border to consult.
		public bool ForbidsPlacement(Player player, CPos cell)
		{
			if (!StandsAtThisLevel)
				return false;

			ResolveBorder();

			// No IsDegenerate test: a derivation that produced nothing leaves the geometry degenerate
			// and IsBeyond already answers false for every point on it, which is the right answer --
			// no border was drawn, so no cell is behind one.
			if (IsRegion)
				return region.IsBeyond(SideFor(player), cell);

			var centre = world.Map.CenterOfCell(cell);
			return geometry.IsBeyond(SideFor(player), centre.X, centre.Y);
		}

		/// <summary>
		/// How far past the line this position is, in world units; negative on the player's own side.
		/// The turn-back layer uses the negative range to react BEFORE the line is reached.
		/// </summary>
		// ON THE REGION PATH THIS IS DISTANCE TO THE BORDER, NOT PERPENDICULAR DISTANCE TO A LINE.
		// The sign convention, the units and the "negative at home" contract the turn-back layer's
		// margin arithmetic depends on are all identical; what differs is that the magnitude is an
		// 8-connected cell distance to the nearest border cell, because a region has no perpendicular.
		// DefconWallTurnBack is therefore answered PROPERLY rather than degraded -- see the header of
		// DefconWallRegion for why a distance transform was chosen over failing loudly.
		public long DepthBeyondWall(Player player, WPos pos)
		{
			if (!active)
				return long.MinValue / 4;

			if (IsRegion)
				return region.DepthBeyond(SideFor(player), pos.X, pos.Y);

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

			// THE REGION'S NORMAL DEPENDS ON WHERE YOU ASK FROM, which is the one structural
			// difference between the two paths and is what a border that bends requires: it is the
			// steepest-descent direction of the transform toward the player's own component, i.e. the
			// way home from HERE rather than a constant perpendicular. The travel arithmetic below is
			// shared unchanged, because both normals are scaled to about one cell.
			var normal = IsRegion
				? region.NormalTowards(side, pos.X, pos.Y)
				: geometry.NormalTowards(side);
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
			var depth = IsRegion
				? region.DepthBeyond(side, pos.X, pos.Y)
				: geometry.DepthBeyond(side, pos.X, pos.Y);
			var travel = Math.Max(0L, depth + clearance.Length);

			return pos + new WVec((int)(normal.X * travel / 1024), (int)(normal.Y * travel / 1024), 0);
		}
	}
}
