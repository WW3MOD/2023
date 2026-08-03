#region Copyright & License Information
/*
 * WW3MOD frontline-influence Phase 0 — terrain/reachability model for the @experimental AI.
 *
 * The strategic layers (PoiMap distance, danger kernels, ground-danger nav) are all TERRAIN-BLIND:
 * POI distance is Euclidean crow-flies from the Supply Route, so a far-bank objective behind an
 * uncrossable river scores as if it were adjacent, and the amphibious locomotors / engineer bridge
 * repair the engine already implements are invisible to bot reasoning. This is the deferred "v2
 * terrain-aware flow" (DangerFieldLayer.cs radial-v1 comment; GroundDangerNav line-walk blindness).
 *
 * CrossingMap is the missing substrate: computed from MAP-STATIC facts (terrain passability per
 * locomotor + the starting bridge actors), it models
 *   - CONNECTED COMPONENTS of the passable graph for a small representative set of locomotor classes
 *     (a ground vehicle, infantry, amphibious), derived from the locomotor's own terrain speeds —
 *     the same data the pathfinder uses (speed 0 / cost-unreachable = impassable);
 *   - REPAIRABLE CROSSINGS: destroyed bridges (a LegacyBridgeHut/BridgeHut whose bridge is Dead) that
 *     an engineer with RepairsBridges could restore, joining two otherwise-disconnected ground
 *     components. Intact bridges need no crossing record — their cells are passable, so the two banks
 *     already fold into ONE ground component and reachability handles them for free;
 *   - AMPHIBIOUS-CROSSABLE component pairs: distinct ground components the amphibious locomotor unifies
 *     across water with no bridge.
 *
 * GATING (byte-identity invariant, influence-stack.md §Invariants): the computation is LAZY and only a
 * participating bot (InfluenceStack.Participates) ever triggers it — @stable / normal / human-only games
 * never build it, so they are byte-identical. Zero SharedRandom draws (map-static, not staggered).
 * Determinism: row-major flood fill over a fixed 4-neighbour order; bridge-state changes revalidate the
 * repairable-crossing set on a cheap periodic check (terrain + component fields are immutable).
 *
 * Pure math (component labelling, crossing classification, amphibious pairing) lives in the engine-free
 * CrossingMapMath so it is NUnit-pinned on a synthetic fixture without mounting a world (mirrors
 * PoiScoring / ControlFieldMath / DangerKernelMath), and ports verbatim to a future v3 brain.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Pathfinder;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public enum CrossingStatus { Intact, Repairable }

	// How a POI's ground component relates to the querying player's Supply Route component.
	// Same/IntactCrossing ⇒ a ground force can walk there; RepairableCrossing ⇒ only if an engineer
	// opens the bridge (kept on the radar, reduced); AmphibiousOnly ⇒ only amphibious units cross;
	// Unreachable ⇒ neither. Consumed by the Phase-1 reachability scoring (PoiReachabilityMath).
	public enum GroundReach { Same, IntactCrossing, RepairableCrossing, AmphibiousOnly, Unreachable }

	// A modelled crossing between two ground components. For runtime reachability only REPAIRABLE
	// crossings are recorded (intact bridges fold into a single component); the pure math also
	// classifies Intact for the NUnit fixture / completeness.
	public readonly struct GroundCrossing
	{
		public readonly int ComponentA;
		public readonly int ComponentB;
		public readonly CrossingStatus Status;
		public readonly int CellX;
		public readonly int CellY;

		public GroundCrossing(int componentA, int componentB, CrossingStatus status, int cellX, int cellY)
		{
			ComponentA = componentA;
			ComponentB = componentB;
			Status = status;
			CellX = cellX;
			CellY = cellY;
		}

		// True when it actually bridges two DISTINCT valid components (both banks land, not the same set).
		public bool JoinsDistinctComponents => ComponentA >= 0 && ComponentB >= 0 && ComponentA != ComponentB;
	}

	// ============================================================
	// Pure math — engine-free, NUnit-pinned (CrossingMapMathTest). Ports verbatim to v3.
	// ============================================================
	public static class CrossingMapMath
	{
		public const int Impassable = -1;

		// Fixed 4-neighbour order ⇒ deterministic flood; row-major seed scan ⇒ deterministic labels.
		static readonly (int Dx, int Dy)[] Neighbours = { (1, 0), (-1, 0), (0, 1), (0, -1) };

		/// <summary>Label the connected components of the passable cells (4-connectivity). Writes
		/// <paramref name="labels"/> (must be [width,height]): Impassable (-1) for a non-passable cell,
		/// else a component id in [0, count). Deterministic row-major seed order + fixed neighbour order,
		/// so labels are stable across clients. Returns the component count.</summary>
		public static int LabelComponents(int width, int height, Func<int, int, bool> passable, int[,] labels)
		{
			for (var x = 0; x < width; x++)
				for (var y = 0; y < height; y++)
					labels[x, y] = Impassable;

			var count = 0;
			var stack = new Stack<int>();
			for (var sy = 0; sy < height; sy++)
			{
				for (var sx = 0; sx < width; sx++)
				{
					if (labels[sx, sy] != Impassable || !passable(sx, sy))
						continue;

					var id = count++;
					labels[sx, sy] = id;
					stack.Push((sx << 16) | sy);
					while (stack.Count > 0)
					{
						var packed = stack.Pop();
						var cx = packed >> 16;
						var cy = packed & 0xFFFF;
						foreach (var (dx, dy) in Neighbours)
						{
							var nx = cx + dx;
							var ny = cy + dy;
							if (nx < 0 || ny < 0 || nx >= width || ny >= height)
								continue;
							if (labels[nx, ny] != Impassable || !passable(nx, ny))
								continue;

							labels[nx, ny] = id;
							stack.Push((nx << 16) | ny);
						}
					}
				}
			}

			return count;
		}

		/// <summary>Component label at (x,y), or Impassable for out-of-bounds / non-passable.</summary>
		public static int LabelAt(int[,] labels, int width, int height, int x, int y)
			=> x < 0 || y < 0 || x >= width || y >= height ? Impassable : labels[x, y];

		/// <summary>Classify a crossing candidate: read the ground component of each bank cell and
		/// record the pair it joins plus its status. A repairable (destroyed) bridge's banks are in
		/// DIFFERENT components (the gap splits them); an intact bridge's banks may already share a
		/// component. Location is a representative cell (the hut / bridge centre) for later telemetry.</summary>
		public static GroundCrossing ClassifyCrossing(int[,] labels, int width, int height,
			int bankAx, int bankAy, int bankBx, int bankBy, CrossingStatus status, int cellX, int cellY)
		{
			var a = LabelAt(labels, width, height, bankAx, bankAy);
			var b = LabelAt(labels, width, height, bankBx, bankBy);
			return new GroundCrossing(a, b, status, cellX, cellY);
		}

		/// <summary>Union-find representative array over <paramref name="count"/> components, unioning
		/// any pair joined by an INTACT crossing. Two components are ground-reachable on foot/tracks iff
		/// they resolve to the same representative. Repairable crossings are NOT unioned (they are only
		/// POTENTIAL connections until an engineer opens them). Deterministic (lower-root union).</summary>
		public static int[] EffectiveGroundSets(int count, IReadOnlyList<GroundCrossing> crossings)
		{
			var parent = new int[count];
			for (var i = 0; i < count; i++)
				parent[i] = i;

			if (crossings != null)
				foreach (var c in crossings)
					if (c.Status == CrossingStatus.Intact && c.JoinsDistinctComponents
						&& c.ComponentA < count && c.ComponentB < count)
						Union(parent, c.ComponentA, c.ComponentB);

			// Path-compress to a stable representative.
			for (var i = 0; i < count; i++)
				parent[i] = Find(parent, i);

			return parent;
		}

		static int Find(int[] parent, int i)
		{
			while (parent[i] != i)
				i = parent[i];
			return i;
		}

		static void Union(int[] parent, int a, int b)
		{
			var ra = Find(parent, a);
			var rb = Find(parent, b);
			if (ra == rb)
				return;
			if (ra < rb)
				parent[rb] = ra;
			else
				parent[ra] = rb;
		}

		/// <summary>Two components are in the same effective ground set (walkable via intact crossings).</summary>
		public static bool SameEffectiveSet(int[] sets, int a, int b)
			=> a >= 0 && b >= 0 && a < sets.Length && b < sets.Length && sets[a] == sets[b];

		/// <summary>Enumerate the DISTINCT ground-component pairs the amphibious locomotor unifies: for
		/// every cell, if its ground component and amphibious component are both valid, group ground
		/// components by shared amphibious component; emit each unordered pair of distinct ground
		/// components that share one. Deterministic (ascending (A,B) with A&lt;B). Excludes pairs already
		/// walkable — a caller reads this as "amphibious opens a route ground cannot".</summary>
		public static List<(int A, int B)> AmphibiousCrossablePairs(
			int[,] groundLabels, int groundCount, int[,] amphibiousLabels, int width, int height)
		{
			// amphibious component id -> set of ground components observed within it.
			var byAmphib = new Dictionary<int, SortedSet<int>>();
			for (var y = 0; y < height; y++)
			{
				for (var x = 0; x < width; x++)
				{
					var g = groundLabels[x, y];
					var a = amphibiousLabels[x, y];
					if (g < 0 || a < 0)
						continue;

					if (!byAmphib.TryGetValue(a, out var set))
						byAmphib[a] = set = new SortedSet<int>();
					set.Add(g);
				}
			}

			var pairs = new SortedSet<(int, int)>();
			foreach (var set in byAmphib.Values)
			{
				if (set.Count < 2)
					continue;

				var list = set.ToList();
				for (var i = 0; i < list.Count; i++)
					for (var j = i + 1; j < list.Count; j++)
						pairs.Add((list[i], list[j]));
			}

			return pairs.ToList();
		}

		/// <summary>Are two DISTINCT ground components amphibious-crossable? Order-insensitive.</summary>
		public static bool AmphibiousConnects(IReadOnlyList<(int A, int B)> pairs, int a, int b)
		{
			if (a == b || a < 0 || b < 0)
				return false;

			var lo = Math.Min(a, b);
			var hi = Math.Max(a, b);
			for (var i = 0; i < pairs.Count; i++)
				if (pairs[i].A == lo && pairs[i].B == hi)
					return true;
			return false;
		}

		/// <summary>Classify how a POI's ground component relates to the SR's ground component, given the
		/// effective (intact-crossing-unioned) sets, the repairable-crossing set, and the amphibious pairs.
		/// Pure decision so the Phase-1 reachability scoring is NUnit-pinnable end to end.</summary>
		public static GroundReach Classify(int srComponent, int poiComponent,
			int[] effectiveSets, IReadOnlyList<GroundCrossing> crossings, IReadOnlyList<(int A, int B)> amphibPairs)
		{
			if (srComponent < 0 || poiComponent < 0)
				return GroundReach.Unreachable;

			if (srComponent == poiComponent)
				return GroundReach.Same;

			if (SameEffectiveSet(effectiveSets, srComponent, poiComponent))
				return GroundReach.IntactCrossing;

			if (crossings != null)
				foreach (var c in crossings)
					if (c.Status == CrossingStatus.Repairable && c.JoinsDistinctComponents
						&& ((c.ComponentA == srComponent && c.ComponentB == poiComponent)
							|| (c.ComponentA == poiComponent && c.ComponentB == srComponent)))
						return GroundReach.RepairableCrossing;

			if (amphibPairs != null && AmphibiousConnects(amphibPairs, srComponent, poiComponent))
				return GroundReach.AmphibiousOnly;

			return GroundReach.Unreachable;
		}
	}

	[TraitLocation(SystemActors.World)]
	[Desc("WW3MOD frontline-influence Phase 0: terrain/reachability model for the @experimental AI.",
		"Per-locomotor connected components + repairable-bridge crossings + amphibious-crossable component",
		"pairs, computed lazily from map-static facts on first request from a participating bot. @stable /",
		"normal / human games never build it (byte-identical). Pure math in CrossingMapMath (NUnit-pinned).")]
	public class CrossingMapInfo : TraitInfo
	{
		[Desc("Representative GROUND-VEHICLE locomotor name (world.yaml). Its passability defines the",
			"ground reachability components the Phase-1 POI penalty keys on.")]
		public readonly string GroundLocomotor = "tracked";

		[Desc("Representative INFANTRY locomotor name (world.yaml).")]
		public readonly string InfantryLocomotor = "foot";

		[Desc("Representative AMPHIBIOUS locomotor name (world.yaml). Auto-detected fallback: the first",
			"locomotor that can traverse any WaterTerrainTypes cell. Its components unify banks the ground",
			"locomotor cannot reach, feeding amphibious-typed axis assignment.")]
		public readonly string AmphibiousLocomotor = "tracked-amphibious";

		[Desc("Terrain types that only amphibious locomotors traverse — used to auto-detect the amphibious",
			"class when AmphibiousLocomotor is absent.")]
		public readonly string[] WaterTerrainTypes = { "Water", "River" };

		[Desc("Ticks between revalidations of the repairable-crossing set (bridge destroyed/repaired). The",
			"component fields are map-static and computed once; only crossing STATUS is re-read here.")]
		public readonly int RevalidateInterval = 100;

		public override object Create(ActorInitializer init) { return new CrossingMap(init.Self, this); }
	}

	public class CrossingMap : ITick, IWorldLoaded
	{
		public readonly CrossingMapInfo Info;
		readonly World world;

		int width, height;
		bool built;
		int revalidateCountdown;

		int[,] groundLabels;
		int[,] infantryLabels;
		int[,] amphibiousLabels;
		int groundCount, infantryCount, amphibiousCount;

		Locomotor groundLoco, infantryLoco, amphibiousLoco;

		readonly List<GroundCrossing> crossings = new();
		List<(int A, int B)> amphibiousPairs = new();
		int[] effectiveSets = Array.Empty<int>();

		// Every locomotor name that can traverse water — the set that types a unit as amphibious-capable.
		readonly HashSet<string> amphibiousLocomotorNames = new();

		// Representative crossing points (bridges/fords): land cells that span a water barrier — where a
		// ground force actually crosses the river. Detected geometrically (a foot-passable cell with
		// foot-impassable water on opposite sides), clustered to one representative per crossing.
		readonly List<CPos> crossingCells = new();

		// Bridge huts discovered once at build (map-static actor set); status is re-read on revalidate.
		readonly List<Actor> bridgeHuts = new();

		public CrossingMap(Actor self, CrossingMapInfo info)
		{
			Info = info;
			world = self.World;
		}

		void IWorldLoaded.WorldLoaded(World w, OpenRA.Graphics.WorldRenderer wr)
		{
			width = w.Map.MapSize.X;
			height = w.Map.MapSize.Y;
		}

		void ITick.Tick(Actor self)
		{
			if (!built)
				return;

			if (--revalidateCountdown > 0)
				return;

			revalidateCountdown = Math.Max(1, Info.RevalidateInterval);
			RevalidateCrossings();
		}

		// ---------- Public query API (lazy: builds on first participating-bot request) ----------

		/// <summary>The ground-vehicle component id at a cell (Impassable = -1 for water/off-map). Triggers
		/// the one-time build on first call from a participating bot.</summary>
		public int GroundComponentAt(CPos cell)
		{
			EnsureBuilt();
			return CrossingMapMath.LabelAt(groundLabels, width, height, cell.X, cell.Y);
		}

		/// <summary>The amphibious component id at a cell.</summary>
		public int AmphibiousComponentAt(CPos cell)
		{
			EnsureBuilt();
			return CrossingMapMath.LabelAt(amphibiousLabels, width, height, cell.X, cell.Y);
		}

		public int GroundComponentCount { get { EnsureBuilt(); return groundCount; } }
		public int AmphibiousComponentCount { get { EnsureBuilt(); return amphibiousCount; } }

		/// <summary>Snapshot of the modelled repairable crossings (for telemetry / Phase-6 engineer wiring).</summary>
		public IReadOnlyList<GroundCrossing> Crossings { get { EnsureBuilt(); return crossings; } }

		public IReadOnlyList<(int A, int B)> AmphibiousPairs { get { EnsureBuilt(); return amphibiousPairs; } }

		/// <summary>Is this locomotor (by name) able to cross water — i.e. does a unit on it count as
		/// amphibious-capable for amphibious-typed axis assignment?</summary>
		public bool IsAmphibiousLocomotor(string locomotorName)
		{
			EnsureBuilt();
			return locomotorName != null && amphibiousLocomotorNames.Contains(locomotorName);
		}

		/// <summary>Are two cells in the same amphibious component (an amphibious force can travel between
		/// them)? Used to decide amphibious axis typing independently of the ground classification.</summary>
		public bool AmphibiousReachable(CPos srCell, CPos poiCell)
		{
			EnsureBuilt();
			var a = CrossingMapMath.LabelAt(amphibiousLabels, width, height, srCell.X, srCell.Y);
			var b = CrossingMapMath.LabelAt(amphibiousLabels, width, height, poiCell.X, poiCell.Y);
			return a >= 0 && a == b;
		}

		public int CrossingCellCount { get { EnsureBuilt(); return crossingCells.Count; } }

		/// <summary>Does the straight cell-line from a to b pass through a WATER BARRIER — a cell impassable
		/// for the non-fording infantry locomotor (deep river/water the ground army must detour around, even
		/// where a fording locomotor like `tracked` could slowly cross)? The signal that a POI is "across the
		/// river" so its crow-flies distance understates the real path. Deterministic integer line walk.</summary>
		public bool CrossesGroundBarrier(CPos a, CPos b)
		{
			EnsureBuilt();
			return AnyBarrierOnLine(a.X, a.Y, b.X, b.Y);
		}

		/// <summary>The through-crossing ground distance (cells) from the SR to a POI when the straight path
		/// crosses a water barrier AND a crossing exists — SR→nearest crossing + crossing→POI, so a far-bank
		/// target reads its honest (longer) detour distance instead of the crow-flies line that makes central
		/// crossings look adjacent. Returns null when the segment crosses no barrier (or no crossing exists),
		/// so the caller keeps its exact Euclidean distance ⇒ only barrier-crossing POIs change. The single
		/// Phase-1.5 distance read; pure decision in PoiReachabilityMath.EffectiveDistanceCells.</summary>
		public int? ThroughCrossingDistanceOverride(CPos srCell, CPos poiCell)
		{
			EnsureBuilt();

			var crowFlies = (poiCell - srCell).Length; // Euclidean cells (CVec.Length = ISqrt).
			var crosses = AnyBarrierOnLine(srCell.X, srCell.Y, poiCell.X, poiCell.Y);
			if (!crosses || crossingCells.Count == 0)
				return null;

			// Nearest crossing by the two-leg detour SR→crossing + crossing→POI (deterministic min, ties by
			// row-major crossing order — crossingCells is built row-major).
			var bestLeg1 = 0;
			var bestLeg2 = 0;
			var bestSum = int.MaxValue;
			foreach (var c in crossingCells)
			{
				var leg1 = (c - srCell).Length;
				var leg2 = (poiCell - c).Length;
				var sum = leg1 + leg2;
				if (sum < bestSum)
				{
					bestSum = sum;
					bestLeg1 = leg1;
					bestLeg2 = leg2;
				}
			}

			var eff = PoiReachabilityMath.EffectiveDistanceCells(crowFlies, true, true, bestLeg1, bestLeg2);
			return eff == crowFlies ? (int?)null : eff;
		}

		/// <summary>Classify how a POI cell relates to the SR cell for a GROUND force: same component,
		/// walkable via an intact crossing, only via a repairable bridge, only amphibious, or unreachable.
		/// The single Phase-1 reachability read.</summary>
		public GroundReach ClassifyGroundReach(CPos srCell, CPos poiCell)
		{
			EnsureBuilt();
			var sr = CrossingMapMath.LabelAt(groundLabels, width, height, srCell.X, srCell.Y);
			var poi = CrossingMapMath.LabelAt(groundLabels, width, height, poiCell.X, poiCell.Y);
			return CrossingMapMath.Classify(sr, poi, effectiveSets, crossings, amphibiousPairs);
		}

		void EnsureBuilt()
		{
			if (built)
				return;

			Build();
			built = true;
			revalidateCountdown = Math.Max(1, Info.RevalidateInterval);
		}

		void Build()
		{
			var locos = world.WorldActor.TraitsImplementing<Locomotor>().ToList();
			groundLoco = ResolveLocomotor(locos, Info.GroundLocomotor);
			infantryLoco = ResolveLocomotor(locos, Info.InfantryLocomotor);
			amphibiousLoco = ResolveLocomotor(locos, Info.AmphibiousLocomotor) ?? AutoDetectAmphibious(locos);

			groundLabels = new int[width, height];
			infantryLabels = new int[width, height];
			amphibiousLabels = new int[width, height];

			groundCount = LabelFor(groundLoco, groundLabels);
			infantryCount = LabelFor(infantryLoco, infantryLabels);
			amphibiousCount = LabelFor(amphibiousLoco, amphibiousLabels);

			// Every water-capable locomotor is amphibious for unit-typing purposes (there are several:
			// foot-amphibious, tracked-amphibious, …). Derived from terrain capability, not a name list.
			amphibiousLocomotorNames.Clear();
			foreach (var loco in AmphibiousCapableLocomotors(locos))
				amphibiousLocomotorNames.Add(loco.Info.Name);

			amphibiousPairs = CrossingMapMath.AmphibiousCrossablePairs(
				groundLabels, groundCount, amphibiousLabels, width, height);

			DetectCrossingCells();

			DiscoverBridgeHuts();
			RevalidateCrossings();

			Log.Write("debug", $"[crossingmap] built ground={groundCount} infantry={infantryCount} " +
				$"amphibious={amphibiousCount} amphibPairs={amphibiousPairs.Count} crossingCells={crossingCells.Count} " +
				$"repairableCrossings={crossings.Count} map={width}x{height}");
		}

		// True when the water-barrier (infantry-impassable, in-bounds) is present at (x,y). The non-fording
		// infantry graph treats deep river/water as impassable, so this marks the ground army's real barriers
		// even where the fording `tracked` locomotor could slowly cross.
		bool InfantryWater(int x, int y)
			=> x >= 0 && y >= 0 && x < width && y < height
				&& CrossingMapMath.LabelAt(infantryLabels, width, height, x, y) == CrossingMapMath.Impassable;

		// Integer supercover-ish line walk (Bresenham) from (x0,y0) to (x1,y1): true if any INTERMEDIATE cell
		// is a water barrier. Endpoints (the SR / POI, on land) are excluded. Deterministic, zero alloc.
		bool AnyBarrierOnLine(int x0, int y0, int x1, int y1)
		{
			var dx = Math.Abs(x1 - x0);
			var dy = Math.Abs(y1 - y0);
			var sx = x0 < x1 ? 1 : -1;
			var sy = y0 < y1 ? 1 : -1;
			var err = dx - dy;
			var x = x0;
			var y = y0;
			while (true)
			{
				if (x == x1 && y == y1)
					return false;

				if (!(x == x0 && y == y0) && InfantryWater(x, y))
					return true;

				var e2 = 2 * err;
				if (e2 > -dy) { err -= dy; x += sx; }
				if (e2 < dx) { err += dx; y += sy; }
			}
		}

		// A crossing cell = a foot-passable land cell that SPANS the water barrier (foot-impassable water on
		// opposite sides — N&S or E&W): a bridge/ford narrow enough that the ground army must funnel over it.
		// Adjacent crossing cells are clustered (4-conn) to one representative each (row-major minimum), so a
		// multi-cell bridge yields one crossing point. Deterministic single map scan.
		void DetectCrossingCells()
		{
			crossingCells.Clear();

			var isCrossing = new bool[width, height];
			for (var y = 0; y < height; y++)
			{
				for (var x = 0; x < width; x++)
				{
					// Must itself be foot-passable land (a bridge/ford tile), not water.
					if (CrossingMapMath.LabelAt(infantryLabels, width, height, x, y) == CrossingMapMath.Impassable)
						continue;

					var spanNS = InfantryWater(x, y - 1) && InfantryWater(x, y + 1);
					var spanEW = InfantryWater(x - 1, y) && InfantryWater(x + 1, y);
					if (spanNS || spanEW)
						isCrossing[x, y] = true;
				}
			}

			// Cluster adjacent crossing cells (4-conn), representative = row-major first cell reached.
			var seen = new bool[width, height];
			var stack = new Stack<int>();
			for (var sy = 0; sy < height; sy++)
			{
				for (var sx = 0; sx < width; sx++)
				{
					if (!isCrossing[sx, sy] || seen[sx, sy])
						continue;

					crossingCells.Add(new CPos(sx, sy));
					seen[sx, sy] = true;
					stack.Push((sx << 16) | sy);
					while (stack.Count > 0)
					{
						var packed = stack.Pop();
						var cx = packed >> 16;
						var cy = packed & 0xFFFF;
						foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
						{
							var nx = cx + dx;
							var ny = cy + dy;
							if (nx < 0 || ny < 0 || nx >= width || ny >= height)
								continue;
							if (!isCrossing[nx, ny] || seen[nx, ny])
								continue;

							seen[nx, ny] = true;
							stack.Push((nx << 16) | ny);
						}
					}
				}
			}
		}

		int LabelFor(Locomotor loco, int[,] labels)
		{
			if (loco == null)
			{
				for (var x = 0; x < width; x++)
					for (var y = 0; y < height; y++)
						labels[x, y] = CrossingMapMath.Impassable;
				return 0;
			}

			bool Passable(int x, int y)
			{
				var cell = new CPos(x, y);
				return world.Map.Contains(cell)
					&& loco.MovementCostForCell(cell) != PathGraph.MovementCostForUnreachableCell;
			}

			return CrossingMapMath.LabelComponents(width, height, Passable, labels);
		}

		static Locomotor ResolveLocomotor(List<Locomotor> locos, string name)
			=> string.IsNullOrEmpty(name) ? null : locos.FirstOrDefault(l => l.Info.Name == name);

		// A locomotor that can traverse a WaterTerrainType is amphibious. Deterministic (world-player
		// trait order + fixed terrain-type list).
		Locomotor AutoDetectAmphibious(List<Locomotor> locos)
		{
			foreach (var loco in AmphibiousCapableLocomotors(locos))
				return loco;
			return null;
		}

		// Every locomotor that can enter a WaterTerrainType, in the world's (deterministic) trait order.
		IEnumerable<Locomotor> AmphibiousCapableLocomotors(List<Locomotor> locos)
		{
			var terrainInfo = world.Map.Rules.TerrainInfo;

			// Resolve the water-terrain indices safely (GetTerrainIndex(string) THROWS on an unknown type,
			// so scan the tileset's own type list instead of asking for each name).
			var waterIndices = new List<int>();
			var types = terrainInfo.TerrainTypes;
			for (var i = 0; i < types.Length; i++)
				if (Array.IndexOf(Info.WaterTerrainTypes, types[i].Type) >= 0)
					waterIndices.Add(i);

			foreach (var loco in locos)
				foreach (var idx in waterIndices)
					// The locomotor's MovementClass bit i is set iff terrain index i is passable for it.
					if ((loco.MovementClass & (1u << idx)) != 0)
					{
						yield return loco;
						break;
					}
		}

		void DiscoverBridgeHuts()
		{
			bridgeHuts.Clear();
			foreach (var actor in world.Actors)
			{
				if (actor.IsDead || !actor.IsInWorld)
					continue;
				if (actor.Info.HasTraitInfo<LegacyBridgeHutInfo>() || actor.Info.HasTraitInfo<BridgeHutInfo>())
					bridgeHuts.Add(actor);
			}
		}

		// Re-read each bridge hut's damage state and rebuild the REPAIRABLE-crossing set. Intact bridges
		// are already folded into the ground components (their cells are passable) so they need no record;
		// a Dead bridge splits its banks, so we look up the two distinct ground components adjacent to the
		// hut and record a repairable crossing between them. Cheap: |bridgeHuts| is tiny.
		void RevalidateCrossings()
		{
			crossings.Clear();
			foreach (var hut in bridgeHuts)
			{
				if (hut.IsDead || !hut.IsInWorld)
					continue;

				var dead = IsBridgeDead(hut);
				if (!dead)
					continue; // intact ⇒ folded into a component, no crossing record needed.

				// Find the two largest DISTINCT ground components adjacent to the hut footprint — the banks
				// a repaired bridge would rejoin. Deterministic (ascending component id).
				var banks = NearbyGroundComponents(hut.Location);
				if (banks.Count < 2)
					continue;

				crossings.Add(new GroundCrossing(banks[0], banks[1], CrossingStatus.Repairable,
					hut.Location.X, hut.Location.Y));
			}

			effectiveSets = CrossingMapMath.EffectiveGroundSets(groundCount, crossings);
		}

		static bool IsBridgeDead(Actor hut)
		{
			var legacy = hut.TraitOrDefault<LegacyBridgeHut>();
			if (legacy != null)
				return legacy.BridgeDamageState == DamageState.Dead;

			var modern = hut.TraitOrDefault<BridgeHut>();
			if (modern != null)
				return modern.BridgeDamageState == DamageState.Dead;

			return false;
		}

		// The distinct ground component ids found in a small ring around a cell (ascending). A destroyed
		// bridge sits on water (its own cell is Impassable), so we sample outward to reach both banks.
		List<int> NearbyGroundComponents(CPos centre)
		{
			var found = new SortedSet<int>();
			for (var r = 1; r <= 4 && found.Count < 2; r++)
			{
				for (var dx = -r; dx <= r; dx++)
				{
					for (var dy = -r; dy <= r; dy++)
					{
						if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
							continue;

						var g = CrossingMapMath.LabelAt(groundLabels, width, height, centre.X + dx, centre.Y + dy);
						if (g >= 0)
							found.Add(g);
					}
				}
			}

			return found.ToList();
		}
	}
}
