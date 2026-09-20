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
using OpenRA.Graphics;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	// THE MIDDLE TEST, and why it is a RATIO rather than a distance.
	//
	// An absolute radius ("neutral within N cells of the map centre") is wrong on the maps this has to
	// serve: the ten shipped maps run from 66x34 to 130x130, carry two, four and six spawn points, and
	// several are not mirror-symmetric. A radius tuned on woodland-warfare leaves twin-rivers' midfield
	// derrick on the wrong side of it. What "genuinely in the middle" actually means is that NOBODY is
	// meaningfully closer -- which is a comparison between two distances, not a distance.
	//
	// So: the nearest player takes the structure unless the nearest player they are NOT allied with is
	// within MiddleBandPercent of that distance, in which case it stays neutral.
	//
	// WHY THE NEAREST *NON-ALLIED* PLAYER RATHER THAN THE NEAREST OTHER PLAYER. A structure sitting
	// between two teammates is not contested; it is simply deep inside one team's half, and leaving it
	// neutral would be a worse answer than handing it to either of them. On x-lake-ww3 the four
	// edge-midpoint derricks sit at a 0.0% margin between two spawn points that are teammates in the
	// obvious 2v2, and 110 cells from either enemy. In a free-for-all, and in every 1v1 on every
	// shipped map, this clause is inert and the rule reduces to "nearest player, unless it is a tie".
	// THE BORDER RULE, WHICH IS WHAT ACTUALLY DECIDES OWNERSHIP ON EVERY MAP THAT HAS A BORDER.
	//
	// The ratio below is the ORIGINAL rule and is now the FALLBACK. It answers "is anybody
	// meaningfully nearer" with a comparison of two distances, which is the best available answer
	// when all you have is a set of anchors -- but it is an inference about where the middle of the
	// map is, and nine of the ten shipped maps now STATE where the middle is: they author a
	// `DefconWall: RegionCells:` border in their own rules.yaml
	// (WORKSPACE/audit/positioning-borders-260919.md). A stated border beats an inferred one, so
	// where one resolves this trait asks it instead:
	//
	//   - any footprint cell inside the band  -> Neutral. The band is the contested middle, said out
	//     loud by the map rather than derived from a percentage.
	//   - otherwise the structure's side is its location cell's side, and it goes to the NEAREST
	//     contender whose own home is on that same side.
	//   - a side with no contender on it      -> Neutral. Nobody is behind it to own it.
	//   - footprint cells that disagree       -> Neutral. A building straddling the border belongs
	//     to neither half, and this is the case the band test alone misses when the band is thin
	//     enough for a 2x2 to step over it.
	//
	// THE TWO RULES DISAGREE, AND THEY ARE MEANT TO. The ratio hands a structure to whoever is
	// nearest wherever it sits; the border rule refuses to hand anyone a structure standing behind
	// the enemy's half of the line, however close they happen to be to it. The per-map table of
	// every row where they differ is in WORKSPACE/DISCOVERIES.md (2026-09-20).
	public static class PreCapturedOwnership
	{
		/// <summary>
		/// Decides who owns one capturable structure on a map WITH a border: an index into
		/// <paramref name="distances"/>, or -1 for "stays neutral". Pure, integer-only and free of
		/// world state so it can be tested directly.
		/// </summary>
		/// <param name="structureSide">The structure's side id, or negative for "in the band / no side".</param>
		/// <param name="distances">Distance from the structure to each contending player's anchor, in world units.</param>
		/// <param name="contenderSides">Each contender's own side id, in the same order. Negative means no side.</param>
		// NEGATIVE MEANS UNCLASSIFIED ON BOTH BACKENDS, which is the whole contract this borrows
		// from DefconWall's level-independent surface: real side ids are >= 0 there (component ids
		// for a region, 0/1 for a line) and DefconWall.NoSide is -1. So `< 0` is the only test this
		// needs and it never has to know which geometry answered.
		//
		// NO ALLIANCE CLAUSE, unlike Resolve below, and the border is why it is not needed. The
		// ratio's alliance test exists to stop a structure sitting between two TEAMMATES reading as
		// contested; here "contested" is a property of the map, not of the distances, so two allies
		// on one side simply race for it on distance and the nearer one takes it. Allies on OPPOSITE
		// sides each take their own, which is the same answer the ratio gives and is correct: the
		// border does not care who is allied with whom.
		public static int ResolveOnSide(int structureSide, IReadOnlyList<long> distances, IReadOnlyList<int> contenderSides)
		{
			// In the band, off the map, or straddling: nobody owns it. This is the first test rather
			// than a special case because it is the one the feature is FOR.
			if (structureSide < 0)
				return -1;

			if (distances == null || contenderSides == null || distances.Count == 0 ||
				contenderSides.Count != distances.Count)
				return -1;

			// Ties break on the lowest index, which is the player's position in World.Players and is
			// therefore identical on every client -- the same rule Resolve uses, for the same reason.
			// A contender with no side of their own (negative) never equals a non-negative
			// structureSide, so they are excluded here without a second test.
			var winner = -1;
			for (var i = 0; i < distances.Count; i++)
			{
				if (contenderSides[i] != structureSide)
					continue;

				if (winner < 0 || distances[i] < distances[winner])
					winner = i;
			}

			// Nobody lives on that side of the border. Neutral rather than "nearest anyway": handing
			// it to a player who has to cross the border to reach it is exactly what the border rule
			// exists to stop.
			return winner;
		}

		/// <summary>
		/// Decides who owns one capturable structure: an index into <paramref name="distances"/>, or -1
		/// for "stays neutral". Pure, integer-only and free of world state so it can be tested directly.
		/// </summary>
		/// <param name="distances">Distance from the structure to each contending player's anchor, in world units.</param>
		/// <param name="areAllied">Whether two contenders (by index) are allies. Must be symmetric.</param>
		/// <param name="middleBandPercent">How much further the nearest enemy may be and still make this a tie.</param>
		public static int Resolve(IReadOnlyList<long> distances, Func<int, int, bool> areAllied, int middleBandPercent)
		{
			if (distances == null || distances.Count == 0)
				return -1;

			// Ties break on the lowest index, which is the player's position in World.Players and is
			// therefore identical on every client. The brief allows either winner when two ALLIES tie;
			// this picks one deterministically rather than leaving it to enumeration order.
			var winner = 0;
			for (var i = 1; i < distances.Count; i++)
				if (distances[i] < distances[winner])
					winner = i;

			var rival = -1;
			for (var i = 0; i < distances.Count; i++)
			{
				if (i == winner || (areAllied != null && areAllied(winner, i)))
					continue;

				if (rival < 0 || distances[i] < distances[rival])
					rival = i;
			}

			// Nobody to contest it: one player, or a whole map's worth of allies. Not a tie -- a walkover.
			if (rival < 0)
				return winner;

			// distances[rival] <= distances[winner] * (100 + X) / 100, rearranged so there is no division
			// to round. Longs rather than ints with room to spare, not out of necessity: the largest
			// shipped map's diagonal is ~184 cells = ~188000 units and 188000 * 110 is 20.7 million,
			// two orders of magnitude inside int. The widening costs nothing and survives a map ten
			// times the size or a MiddleBandPercent someone sets to 10000.
			if (distances[rival] * 100 <= distances[winner] * (100L + middleBandPercent))
				return -1;

			return winner;
		}
	}

	[TraitLocation(SystemActors.World)]
	[Desc("Hands every neutral capturable structure to the nearest player at world load, leaving the",
		"contested ones neutral. On a map with a " + nameof(DefconWall) + " border, \"contested\" is",
		"the border band and a structure only ever goes to a player on its OWN side of it; on a map",
		"with no border it falls back to a distance ratio. Adds a lobby checkbox, default OFF.",
		"Attach to the world actor.")]
	public class PreCapturedStructuresInfo : TraitInfo, ILobbyOptions
	{
		public const string OptionId = "precapturedstructures";

		[Desc("Descriptive label for the pre-captured structures option in the lobby.")]
		public readonly string CheckboxLabel = "Pre-captured Structures";

		[Desc("Tooltip description for the pre-captured structures option in the lobby.")]
		// SAYS WHAT IT NOW DOES. The old wording ("the nearer player; ones in the middle stay
		// neutral") described the ratio rule, which is now only what happens on a map with no
		// border -- and on nine of the ten shipped maps there IS one, so the old sentence described
		// the exception rather than the rule.
		public readonly string CheckboxDescription =
			"Capturable structures start owned by the nearest player on their side of the border; " +
			"the border zone stays neutral";

		[Desc("Whether the option starts enabled. OFF is the shipped default and is load-bearing: with it",
			"off this trait returns before reading the actor list, the rules or the map, so a Skirmish",
			"nobody touched is the match it was before this option existed.")]
		public readonly bool CheckboxEnabled = false;

		[Desc("Prevent the pre-captured structures option from being changed in the lobby.")]
		public readonly bool CheckboxLocked = false;

		[Desc("Whether to display the pre-captured structures option in the lobby.")]
		public readonly bool CheckboxVisible = true;

		[Desc("Display order for the pre-captured structures option in the lobby.")]
		public readonly int CheckboxDisplayOrder = 8;

		[Desc("FALLBACK ONLY -- read on a map where no " + nameof(DefconWall) + " border resolves.",
			"Nine of the ten shipped maps author one, so on those this field is never consulted; what",
			"reaches it is a map with no region and no derivable line, e.g. a three-way free-for-all",
			"on the derived path.",
			"",
			"A structure stays neutral when the nearest player NOT allied with the nearest player is",
			"within this percentage of the nearest player's distance.",
			"",
			"CALIBRATED, NOT PICKED. Across all ten shipped maps the margins fall into two clumps with an",
			"empty band between 8.4% and 15.2%, so any value in that band produces identical results and",
			"10 sits in the middle of it rather than on an edge. It is also the SMALLEST value that keeps",
			"every symmetric pair symmetric: below 8 the two LOGISTICSCENTERs on woodland-warfare-ww3",
			"(4.5% and 8.0%) and on river-zeta-ww3 (1.0% and 8.4%) split, one captured and its mirror",
			"neutral, which reads as a bug however defensible the arithmetic is. Re-check that property",
			"first if this is ever retuned:",
			"`python tools/precaptured-calibration/precaptured_calibration.py <X>`.",
			"",
			"Note that no value satisfies 'the woodland-warfare reactor is neutral but every derrick is",
			"captured': the reactor sits at a 2.1% margin and the derrick at 64,31 at 0.1%, so the derrick",
			"is the more central of the two.")]
		public readonly int MiddleBandPercent = 10;

		IEnumerable<LobbyOption> ILobbyOptions.LobbyOptions(MapPreview map)
		{
			yield return new LobbyBooleanOption(OptionId, CheckboxLabel, CheckboxDescription,
				CheckboxVisible, CheckboxDisplayOrder, CheckboxEnabled, CheckboxLocked);
		}

		public override object Create(ActorInitializer init) { return new PreCapturedStructures(this); }
	}

	// IWorldLoaded, NOT INotifyCreated: the world actor's traits are constructed before the world exists
	// (World.cs:252 constructs them, World.cs:334 runs IWorldLoaded), so World.WorldActor is null in a
	// constructor and no map actor has been placed yet. Same reason SpawnStartingUnits is an IWorldLoaded.
	//
	// ORDERING AGAINST SpawnStartingUnits DOES NOT MATTER, which is worth knowing because it looks like
	// it should. That trait creates each player's SUPPLYROUTE, and this one anchors on it -- but its
	// fallback is the spawn cell, and the two are the SAME POINT: the SR is placed at
	// `HomeLocation + BaseActorOffset` with BaseActorOffset (-1,-1) (MapStartingUnits.cs:37, not
	// overridden in world.yaml), and a 3x3 building's CenterOffset is (+1,+1) cells
	// (Building.cs:207-211). The offsets cancel exactly. The fallback is what makes this work in the
	// autotest scenarios, which routinely carry `-SpawnStartingUnits:`.
	public class PreCapturedStructures : IWorldLoaded
	{
		readonly PreCapturedStructuresInfo info;

		public PreCapturedStructures(PreCapturedStructuresInfo info)
		{
			this.info = info;
		}

		public void WorldLoaded(World world, WorldRenderer wr)
		{
			if (!world.LobbyInfo.GlobalSettings.OptionOrDefault(PreCapturedStructuresInfo.OptionId, info.CheckboxEnabled))
				return;

			// CONTENDERS ARE COMBATANTS WITH AN ANCHOR, AND DELIBERATELY NOT "PLAYABLE PLAYERS".
			//
			// `Playable` is a statement about LOBBY SLOTS, not about who is in the match.
			// CreateMapPlayers builds a Player for every non-playable map player and then one per
			// OCCUPIED slot, skipping empty ones outright (CreateMapPlayers.cs:93-121) -- so a
			// scripted or map-authored side is a real combatant that owns actors and fights, and is
			// `Playable: false`. Filtering on Playable dropped exactly those, which on a two-sided
			// autotest scenario left ONE contender, made every structure a walkover, and would have
			// made the "middle stays neutral" arm silently vacuous.
			//
			// NO EFFECT ON ANY SHIPPED MAP, checked rather than assumed: across all ten, every
			// non-playable player is `Neutral` (OwnsWorld + NonCombatant) or `Creeps` (NonCombatant),
			// so `!NonCombatant` selects exactly the set `Playable && !NonCombatant` used to. It also
			// still cannot pick up an EMPTY slot, because an empty slot has no Player object at all.
			// NonCombatant additionally excludes the synthetic all-seeing "Everyone" player that
			// CreateMapPlayers.cs:124-132 appends.
			var contenders = new List<Player>();
			var anchors = new List<WPos>();
			foreach (var p in world.Players)
			{
				if (p.NonCombatant)
					continue;

				var anchor = AnchorFor(world, p);
				if (anchor == null)
					continue;

				contenders.Add(p);
				anchors.Add(anchor.Value);
			}

			// One line per match, at world load, and only when the option is on. This is the only
			// way to tell "the trait decided nobody was near enough" from "the trait never saw that
			// player", which cost a scenario pair one run to establish.
			Log.Write("debug", "PreCapturedStructures: contenders = " + (contenders.Count == 0 ? "(none)" :
				string.Join(", ", contenders.Select((p, i) => $"{p.InternalName}@{world.Map.CellContaining(anchors[i])}"))));

			if (contenders.Count == 0)
				return;

			// THE BORDER, AND WHY ASKING FOR IT HERE IS SAFE DESPITE THE TRAIT ORDER.
			//
			// This trait is declared at world.yaml:688 and DefconWall at :971, so OUR IWorldLoaded
			// runs FIRST and DefconWall has not resolved its border yet when we ask. That is fine
			// rather than merely tolerable: DefconWall.ResolveBorder is lazy and idempotent, and was
			// made so precisely because a trait declared earlier gets there first (SpawnStartingUnits
			// at :674 already does). Asking here BUILDS the border; DefconWall's own WorldLoaded then
			// finds it already built and does nothing. Every input ResolveBorder reads -- the map,
			// its terrain, the players' HomeLocations -- exists before the first IWorldLoaded.
			//
			// HasBorder IS NOT GATED ON THE DEFCON LEVEL, which is the point of that surface: the
			// DEFCON-3 gate governs BLOCKING, and the border's geometry resolves in every mode. This
			// therefore works in Skirmish, where the wall will never stand. Nothing is mutated --
			// no CustomTerrain byte is written until DefconWall raises the wall, which Skirmish never
			// does.
			var wall = world.WorldActor.TraitOrDefault<DefconWall>();
			var useBorder = wall != null && wall.HasBorder;

			// EACH CONTENDER'S SIDE COMES FROM THEIR ANCHOR, NOT FROM Player.HomeLocation, and the
			// two are the same point on every shipped map: SpawnStartingUnits places the Supply Route
			// at `HomeLocation + (-1,-1)` and a 3x3 building's CenterOffset is (+1,+1) cells, so the
			// SR's CenterPosition IS CenterOfCell(HomeLocation) (MapStartingUnits.cs:37,
			// Building.cs:207-211). Where they differ is a scenario: HomeLocation is CPos.Zero for a
			// map player, and also for a lobby player on any map that strips MapStartingLocations
			// (Player.cs:213 falls back to the PlayerReference when there is no IAssignSpawnPoints),
			// which is every autotest scenario in this tree. Reading HomeLocation there would put
			// both sides off the map at (0,0), read Unlabelled for both, and leave every structure
			// neutral. The anchor is already the answer to "where does this player live" that
			// AnchorFor spent its own comment getting right.
			//
			// Note for anyone auditing this against the border audit: that document records SUPPLY
			// ROUTES landing outside Bounds on six shipped maps. That is the actor's LOCATION (its
			// top-left cell at x=0 for a spawn at x=1), not its CenterPosition, which is the spawn
			// cell centre and is safely inside Bounds. The sides read here are the spawns' own.
			var contenderSides = new int[contenders.Count];
			if (useBorder)
				for (var i = 0; i < contenders.Count; i++)
					contenderSides[i] = wall.SideOf(anchors[i]);

			Log.Write("debug", "PreCapturedStructures: border = " + (!useBorder
				? "(none -- falling back to the " + info.MiddleBandPercent + "% ratio rule)"
				: string.Join(", ", contenders.Select((p, i) => $"{p.InternalName}:side{contenderSides[i]}"))));

			// WHY THESE THREE FILTERS. `Capturable` is the engine's own answer to "can this be taken"
			// -- most of the neutral scenery on a map inherits ^BasicBuilding and would be swept up by a
			// looser test, and the three families that opt out (^CivBuilding, GTWR/PBOX/HBOX, and every
			// ^Wall/^Tree/^Rock descendant) all do it by stripping these traits. `Building` is the
			// user's word "structures": vehicles.yaml:108 declares one capturable VEHICLE and it is
			// deliberately out of scope. `Selectable` excludes BARL and BRL3 -- explosive barrels
			// inherit ^TechBuilding (civilian.yaml:772,793) and are therefore genuinely capturable, but
			// they carry `-Selectable:` and sit in the map editor's Decoration category. Fifteen of them
			// are Neutral on siberian-pass-ww3 and seventh-woods-ww3, so this is not a hypothetical.
			//
			// Owner test is OwnsWorld rather than NonCombatant: `Creeps` is also non-combatant but is a
			// hostile third party, and handing its buildings out at world load is not what "neutral"
			// means to the player.
			//
			// AND THAT DISTINCTION IS LOAD-BEARING ON A SHIPPED MAP, which this comment used to deny.
			// nuclear-winter-ww3's map.yaml:1146-1148 places `Actor436: mslo` owned by **Creeps** at
			// 50,35, and MSLO passes all three filters below -- it carries Capturable, Building and
			// Selectable. A NonCombatant owner test would therefore hand a Missile Silo to whichever
			// player is nearest, on the one shipped map that has one. Corrected 2026-09-20; it is the
			// only non-Neutral capturable structure on any of the ten (WORKSPACE/DISCOVERIES.md).
			var candidates = world.Actors.Where(a =>
				a.Owner.PlayerReference != null && a.Owner.PlayerReference.OwnsWorld &&
				a.Info.HasTraitInfo<CapturableInfo>() &&
				a.Info.HasTraitInfo<BuildingInfo>() &&
				a.Info.HasTraitInfo<SelectableInfo>()).ToList();

			var distances = new long[contenders.Count];
			foreach (var a in candidates)
			{
				for (var i = 0; i < contenders.Count; i++)
					distances[i] = (a.CenterPosition - anchors[i]).HorizontalLength;

				// SAME DISTANCES, SAME TIE RULE, DIFFERENT QUESTION. The border rule filters the
				// contenders down to the ones on the structure's own side and then picks the nearest
				// of those, so a structure deep in one half cannot be claimed from the other half by
				// a player who merely happens to be closer to it.
				var winner = useBorder
					? PreCapturedOwnership.ResolveOnSide(SideOfFootprint(wall, a), distances, contenderSides)
					: PreCapturedOwnership.Resolve(distances, (x, y) => contenders[x].IsAlliedWith(contenders[y]), info.MiddleBandPercent);

				if (winner < 0)
					continue;

				// ChangeOwner, not ChangeOwnerSync: the sync form is documented as callable only from
				// inside an existing FrameEndTask (Actor.cs:565-568) and WorldLoaded is not one. The
				// queued form runs INotifyOwnerChanged through the engine's own path, so CashTrickler,
				// prerequisites, bot bookkeeping and the derrick counter all see a normal handover --
				// they just see it at the end of the first tick rather than during world load.
				a.ChangeOwner(contenders[winner]);
			}
		}

		/// <summary>
		/// The side of the border a structure is on: <see cref="DefconWall.NoSide"/> when any of its
		/// footprint cells is inside the band, when its footprint cells disagree, or when the cell it
		/// stands on has no side at all.
		/// </summary>
		// THE WHOLE FOOTPRINT, NOT THE LOCATION CELL, AND THE TWO REALLY DO DIFFER. Every capturable
		// structure on a shipped map is 2x2 or larger, and the authored bands are three cells thick
		// (WORKSPACE/audit/positioning-borders-260919.md), so a building can have one corner in the
		// band with its location cell clear of it -- and on a bend, one corner on each side with no
		// cell in the band at all. A location-cell test would hand that building to one half of the
		// map. Both cases collapse to Neutral here, which is the only defensible answer for a
		// structure the border runs through.
		//
		// BuildingInfo.Tiles is the same footprint the engine occupies the world with, so a building
		// whose Footprint declares Empty cells is judged on the cells it really covers.
		static int SideOfFootprint(DefconWall wall, Actor a)
		{
			var side = wall.SideOf(a.Location);

			foreach (var cell in a.Info.TraitInfo<BuildingInfo>().Tiles(a.Location))
			{
				if (wall.IsInBand(cell))
					return DefconWall.NoSide;

				if (wall.SideOf(cell) != side)
					return DefconWall.NoSide;
			}

			return side;
		}

		// The player's Supply Route if they have one, else their spawn cell, else NOTHING -- they are
		// not a contender at all. BaseBuilding is the marker because SUPPLYROUTE is the only actor in
		// the shipped ruleset carrying it (structures.yaml:370). Ordered by ActorID so a player
		// holding two would still resolve identically on every client; "one per player" is the shipped
		// reality but the ordering costs nothing and does not assume it.
		static WPos? AnchorFor(World world, Player p)
		{
			var supplyRoute = world.Actors
				.Where(a => a.Owner == p && a.Info.HasTraitInfo<BaseBuildingInfo>())
				.OrderBy(a => a.ActorID)
				.FirstOrDefault();

			if (supplyRoute != null)
				return supplyRoute.CenterPosition;

			// RETURNING null RATHER THAN THE MAP'S TOP-LEFT CORNER. CPos.Zero is the FIELD DEFAULT of
			// PlayerReference.HomeLocation (PlayerReference.cs:41), so a player who occupies no lobby
			// slot and holds no Supply Route reads (0,0) -- a coordinate default masquerading as "their
			// own side". Anchoring there would hand them every capturable structure in that corner, and
			// on a map whose Bounds start at 1,1 it is not even a cell anyone can stand on. This is the
			// same trap conventions.md records under "A() ?? B in a decision path": the fallback must
			// answer the same question as the primary, and "where is nothing" is not an answer.
			if (p.HomeLocation == CPos.Zero)
				return null;

			return world.Map.CenterOfCell(p.HomeLocation);
		}
	}
}
