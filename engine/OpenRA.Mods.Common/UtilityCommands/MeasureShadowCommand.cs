#region Copyright & License Information
/*
 * WW3MOD MEASUREMENT HARNESS - throwaway, wt/shadow-relight (2026-09-08).
 *
 * Answers "can MapShadowLayer be updated incrementally at runtime, and at what
 * unit of work" with numbers rather than estimates. Not intended to ship: it
 * exists so the recon's figures are reproducible. It only READS the sim layers
 * (plus a scratch DensityLayer mutation it restores), launches nothing, and
 * writes nothing to disk.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OpenRA.FileSystem;

namespace OpenRA.Mods.Common.UtilityCommands
{
	sealed class MeasureShadowCommand : IUtilityCommand
	{
		string IUtilityCommand.Name => "--measure-shadow";

		bool IUtilityCommand.ValidateArguments(string[] args) { return args.Length >= 2; }

		[Desc("MAP", "Measure MapShadowLayer incremental-update costs. Throwaway harness.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			var modData = Game.ModData = utility.ModData;
			using (var package = new Folder(Platform.EngineDir).OpenPackage(args[1], modData.ModFiles))
			{
				var map = new Map(modData, package);
				Run(map);
			}
		}

		static void Run(Map map)
		{
			var w = map.MapSize.X;
			var h = map.MapSize.Y;
			var cells = map.AllCells.MapCoords.ToArray();
			Console.WriteLine($"### MAP {map.Title} {w}x{h} = {w * h} cells, grid {map.Grid.Type}");
			Console.WriteLine($"SlotsPerCell (allocated window) = {map.ShadowLayer.SlotsPerCell}");

			// ---- (0) annulus size, the multiplier everything scales by -------------------
			long annulusTotal = 0;
			var annulusMax = 0;
			foreach (var c in cells)
			{
				var n = map.FindTilesInAnnulus(c.ToCPos(map), 2, 32, true).Count();
				annulusTotal += n;
				annulusMax = Math.Max(annulusMax, n);
			}

			Console.WriteLine($"Annulus to-cells per from-cell: mean {annulusTotal / (double)cells.Length:F1}, max {annulusMax}");
			Console.WriteLine($"Total written pairs = {annulusTotal}");

			// ---- (1) cost of ONE RecomputeShadowFrom -------------------------------------
			// Interior cells (full unclipped annulus) are the honest per-call number.
			var interior = cells.Where(c => c.U > 34 && c.U < w - 35 && c.V > 34 && c.V < h - 35).ToArray();
			var sample = (interior.Length > 0 ? interior : cells).Take(400).ToArray();

			foreach (var c in sample.Take(50))
				map.RecomputeShadowFrom(c); // warm up JIT + caches

			var sw = Stopwatch.StartNew();
			foreach (var c in sample)
				map.RecomputeShadowFrom(c);
			sw.Stop();
			var perCallUs = sw.Elapsed.TotalMilliseconds * 1000.0 / sample.Length;
			Console.WriteLine($"RecomputeShadowFrom: {sample.Length} interior calls in {sw.Elapsed.TotalMilliseconds:F1} ms => {perCallUs:F1} us/call");

			// ---- (2) full serial bake, measured ------------------------------------------
			sw.Restart();
			foreach (var c in cells)
				map.RecomputeShadowFrom(c);
			sw.Stop();
			Console.WriteLine($"FULL SERIAL BAKE (all {cells.Length} from-cells): {sw.Elapsed.TotalMilliseconds:F0} ms");

			sw.Restart();
			System.Threading.Tasks.Parallel.ForEach(cells, map.RecomputeShadowFrom);
			sw.Stop();
			Console.WriteLine($"FULL PARALLEL BAKE: {sw.Elapsed.TotalMilliseconds:F0} ms");

			// ---- (3) THE CRUX: blast radius of one changed cell ---------------------------
			// Which (from,to) pairs actually have X strictly between them?
			var x = PickDensestCell(map, out var xDensity);
			Console.WriteLine($"### CHANGED CELL X = {x} (DensityLayer = {xDensity})");

			var affectedFrom = 0;
			long affectedPairs = 0;
			var maxPairsPerFrom = 0;
			long naiveFromCells = 0;
			long naivePairs = 0;

			var crossSw = Stopwatch.StartNew();
			foreach (var f in map.FindTilesInAnnulus(x.ToCPos(map), 0, 32, true))
			{
				if (!map.ShadowLayer.Contains(f))
					continue;

				var fUV = f.ToMPos(map);
				naiveFromCells++;
				var here = 0;
				foreach (var t in map.FindTilesInAnnulus(f, 2, 32, true))
				{
					if (!map.ShadowLayer.Contains(t))
						continue;

					naivePairs++;
					var tUV = t.ToMPos(map);
					foreach (var tile in map.DensityLayer.TilesIntersectingLine(fUV, tUV))
					{
						if (tile == fUV || tile == tUV)
							continue;
						if (tile == x)
						{
							here++;
							break;
						}
					}
				}

				if (here > 0)
					affectedFrom++;
				affectedPairs += here;
				maxPairsPerFrom = Math.Max(maxPairsPerFrom, here);
			}

			crossSw.Stop();

			Console.WriteLine($"NAIVE (UpdateShadowForCells) : from-cells {naiveFromCells}, pairs recomputed {naivePairs}");
			Console.WriteLine($"EXACT (rays crossing X)      : from-cells {affectedFrom}, pairs actually wrong {affectedPairs}");
			Console.WriteLine($"  pairs-per-affected-from: mean {affectedPairs / (double)Math.Max(1, affectedFrom):F1}, max {maxPairsPerFrom}");
			Console.WriteLine($"  WASTE FACTOR naive/exact = {naivePairs / (double)Math.Max(1, affectedPairs):F1}x");
			Console.WriteLine($"  COST OF DISCOVERING the exact set by brute force: {crossSw.Elapsed.TotalMilliseconds:F0} ms");

			// ---- (4) how much shadow is actually at stake --------------------------------
			// Snapshot the affected pairs, zero the density at X, recompute, diff.
			var before = new List<((MPos F, MPos T) Pair, (byte G, byte A) V)>();
			foreach (var f in map.FindTilesInAnnulus(x.ToCPos(map), 0, 32, true))
			{
				if (!map.ShadowLayer.Contains(f))
					continue;

				var fUV = f.ToMPos(map);
				foreach (var t in map.FindTilesInAnnulus(f, 2, 32, true))
				{
					if (!map.ShadowLayer.Contains(t))
						continue;

					var tUV = t.ToMPos(map);
					before.Add(((fUV, tUV), map.ShadowLayer[fUV, tUV]));
				}
			}

			var saved = map.DensityLayer[x];
			map.DensityLayer[x] = 0;
			foreach (var f in map.FindTilesInAnnulus(x.ToCPos(map), 0, 32, true))
				if (map.ShadowLayer.Contains(f))
					map.RecomputeShadowFrom(f.ToMPos(map));

			var groundChanged = 0;
			var airChanged = 0;
			var groundDelta = new Dictionary<int, int>();
			var airDelta = new Dictionary<int, int>();
			foreach (var (pair, v) in before)
			{
				var now = map.ShadowLayer[pair.F, pair.T];
				if (now.GroundShadow != v.G)
				{
					groundChanged++;
					var d = v.G - now.GroundShadow;
					groundDelta[d] = groundDelta.GetValueOrDefault(d) + 1;
				}

				if (now.AirborneShadow != v.A)
				{
					airChanged++;
					var d = v.A - now.AirborneShadow;
					airDelta[d] = airDelta.GetValueOrDefault(d) + 1;
				}
			}

			Console.WriteLine($"### SHADOW AT STAKE for removing density {saved} at one cell");
			Console.WriteLine($"pairs whose GROUND shadow changed  : {groundChanged} (of {before.Count} in the radius-32 from-set)");
			Console.WriteLine($"pairs whose AIRBORNE shadow changed: {airChanged}");
			Console.WriteLine("  ground delta histogram: " + string.Join(", ", groundDelta.OrderByDescending(kv => kv.Key).Select(kv => $"-{kv.Key}:{kv.Value}")));
			Console.WriteLine("  airborne delta histogram: " + string.Join(", ", airDelta.OrderByDescending(kv => kv.Key).Select(kv => $"-{kv.Key}:{kv.Value}")));

			map.DensityLayer[x] = saved;
			foreach (var f in map.FindTilesInAnnulus(x.ToCPos(map), 0, 32, true))
				if (map.ShadowLayer.Contains(f))
					map.RecomputeShadowFrom(f.ToMPos(map));

			// ---- (5) crater-scale from-set (checks the DISCOVERIES Tsar Bomba claim) ------
			foreach (var craterRadius in new[] { 2, 8, 15 })
			{
				var fromSet = new HashSet<MPos>();
				foreach (var cc in map.FindTilesInAnnulus(x.ToCPos(map), 0, craterRadius, true))
					foreach (var f in map.FindTilesInAnnulus(cc, 0, 32, true))
						if (map.ShadowLayer.Contains(f))
							fromSet.Add(f.ToMPos(map));

				var pct = 100.0 * fromSet.Count / cells.Length;
				Console.WriteLine($"CRATER r={craterRadius}c: naive affected from-cells = {fromSet.Count} ({pct:F1}% of map), " +
					$"serial cost = {fromSet.Count * perCallUs / 1000.0:F0} ms");
			}

			// ---- (6) density census: how much of the map is tree at all -------------------
			var nonZero = cells.Count(c => map.DensityLayer[c] > 0);
			Console.WriteLine($"### DENSITY CENSUS: {nonZero} of {cells.Length} cells carry density ({100.0 * nonZero / cells.Length:F1}%)");
		}

		static MPos PickDensestCell(Map map, out int density)
		{
			var best = map.AllCells.MapCoords.First();
			var bestD = -1;

			// Prefer a cell with the most OTHER density around it, so the sample is a forest
			// interior rather than a lone tree in a field.
			foreach (var c in map.AllCells.MapCoords)
			{
				if (map.DensityLayer[c] == 0)
					continue;

				var around = 0;
				foreach (var n in map.FindTilesInAnnulus(c.ToCPos(map), 0, 6, false))
					around += map.DensityLayer[n.ToMPos(map)];

				if (around > bestD)
				{
					bestD = around;
					best = c;
				}
			}

			density = map.DensityLayer[best];
			return best;
		}
	}
}
