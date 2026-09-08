#region Copyright & License Information
/*
 * WW3MOD MEASUREMENT HARNESS - throwaway, wt/shadow-relight (2026-09-08).
 *
 * Companion to --measure-shadow. Answers the two questions that decide whether a
 * per-ray incremental shadow update is affordable:
 *
 *   (a) does the 162x naive/exact waste factor SURVIVE a crater-sized change, or
 *       does a contiguous blob of removed density make almost every ray wrong?
 *   (b) is the accumulation SUBTRACTABLE - i.e. can a stored raw sum be adjusted
 *       by the delta at one cell and produce the byte-identical result a full
 *       re-walk would, for BOTH the integer ground term and the float airborne one?
 *
 * Reads the sim layers and restores anything it mutates. Launches nothing.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OpenRA.FileSystem;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.UtilityCommands
{
	sealed class MeasureShadowCraterCommand : IUtilityCommand
	{
		string IUtilityCommand.Name => "--measure-shadow-crater";

		bool IUtilityCommand.ValidateArguments(string[] args) { return args.Length >= 2; }

		[Desc("MAP", "Crater-scale + subtractability measurements for MapShadowLayer. Throwaway harness.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			var modData = Game.ModData = utility.ModData;
			using (var package = new Folder(Platform.EngineDir).OpenPackage(args[1], modData.ModFiles))
			{
				var map = new Map(modData, package);
				Run(map, modData);
			}
		}

		static void Run(Map map, ModData modData)
		{
			var cells = map.AllCells.MapCoords.ToArray();
			Console.WriteLine($"### MAP {map.Title} {map.MapSize.X}x{map.MapSize.Y} = {cells.Length} cells");

			// ---- (A) are all authored densities multiples of 5? --------------------------
			// This is what makes `density / 5f` exact-in-binary, and therefore what decides
			// whether the FLOAT airborne accumulator can be adjusted by subtraction without
			// diverging from a fresh re-walk. Same fragile invariant already documented for
			// ForestGroundShadow's below-knee path.
			var allValues = new SortedSet<int>();
			foreach (var actor in modData.DefaultRules.Actors.Values)
			{
				var d = actor.TraitInfoOrDefault<IDensityInfo>();
				if (d == null)
					continue;

				foreach (var kv in d.Density())
					allValues.Add(kv.Value);
			}

			var nonMultiple = allValues.Where(v => v % 5 != 0).ToArray();
			Console.WriteLine($"Authored density values in the mod: {string.Join(", ", allValues)}");
			Console.WriteLine($"NOT multiples of 5: {(nonMultiple.Length == 0 ? "NONE - float term is exact" : string.Join(", ", nonMultiple))}");

			// Stamped layer values can exceed authored ones where footprints overlap.
			var maxStamped = cells.Max(c => (int)map.DensityLayer[c]);
			var stampedNonMultiple = cells.Count(c => map.DensityLayer[c] % 5 != 0);
			Console.WriteLine($"Max STAMPED DensityLayer value: {maxStamped}; stamped cells not a multiple of 5: {stampedNonMultiple}");

			// ---- (B) subtractability: does re-walk == subtract, byte for byte? -----------
			VerifySubtractability(map);

			// ---- (C) crater-scale exact vs naive ----------------------------------------
			var centre = PickDensestCell(map);
			Console.WriteLine($"### CRATER CENTRE = {centre}");

			foreach (var r in new[] { 0, 2, 8, 15 })
				CraterCensus(map, cells.Length, centre, r);
		}

		static void CraterCensus(Map map, int mapCells, MPos centre, int craterRadius)
		{
			var crater = new HashSet<MPos>();
			foreach (var c in map.FindTilesInAnnulus(centre.ToCPos(map), 0, craterRadius, true))
				if (map.ShadowLayer.Contains(c))
					crater.Add(c.ToMPos(map));

			// Every from-cell that the naive UpdateShadowForCells path would touch.
			var naiveFrom = new HashSet<MPos>();
			foreach (var cc in crater)
				foreach (var f in map.FindTilesInAnnulus(cc.ToCPos(map), 0, 32, true))
					if (map.ShadowLayer.Contains(f))
						naiveFrom.Add(f.ToMPos(map));

			long naivePairs = 0;
			long exactPairs = 0;
			var exactFrom = 0;

			var sw = Stopwatch.StartNew();
			foreach (var f in naiveFrom)
			{
				var here = 0;
				foreach (var t in map.FindTilesInAnnulus(f.ToCPos(map), 2, 32, true))
				{
					if (!map.ShadowLayer.Contains(t))
						continue;

					naivePairs++;
					var tUV = t.ToMPos(map);
					foreach (var tile in map.DensityLayer.TilesIntersectingLine(f, tUV))
					{
						if (tile == f || tile == tUV)
							continue;
						if (crater.Contains(tile))
						{
							here++;
							break;
						}
					}
				}

				if (here > 0)
					exactFrom++;
				exactPairs += here;
			}

			sw.Stop();

			var pct = 100.0 * naiveFrom.Count / mapCells;
			Console.WriteLine($"CRATER r={craterRadius}c ({crater.Count} cells):");
			Console.WriteLine($"   naive from-cells {naiveFrom.Count} ({pct:F1}% of map), naive pairs {naivePairs}");
			Console.WriteLine($"   exact from-cells {exactFrom}, exact wrong pairs {exactPairs}");
			Console.WriteLine($"   WASTE FACTOR naive/exact = {naivePairs / (double)Math.Max(1, exactPairs):F1}x" +
				$"   (exact is {100.0 * exactPairs / Math.Max(1, naivePairs):F2}% of naive)");
			Console.WriteLine($"   brute-force discovery cost for this crater: {sw.Elapsed.TotalMilliseconds:F0} ms");
		}

		/// <summary>
		/// For a sample of (from, to) pairs whose ray crosses X, compare:
		///   REBUILD  - re-walk the ray with DensityLayer[X] set to the new value
		///   SUBTRACT - take the ORIGINAL raw accumulators and adjust them by the delta
		/// If these agree on every pair, the accumulation is separable and a stored raw sum
		/// can be patched in O(1) per affected pair instead of O(ray length).
		/// </summary>
		static void VerifySubtractability(Map map)
		{
			var x = PickDensestCell(map);
			var oldD = map.DensityLayer[x];
			const byte NewD = 0;

			var checkedPairs = 0;
			var groundMismatch = 0;
			var airMismatch = 0;

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

					// Raw accumulators with the ORIGINAL density.
					if (!Trace(map, fUV, tUV, x, out var g0, out var a0, out var crosses))
						continue;
					if (!crosses)
						continue;

					// REBUILD: walk again with the new density in place.
					map.DensityLayer[x] = NewD;
					Trace(map, fUV, tUV, x, out var gRebuild, out var aRebuild, out _);
					map.DensityLayer[x] = oldD;

					// SUBTRACT: patch the original accumulators by the delta at X.
					// The airborne term is CONDITIONAL on X's own zLos test, so the patch must
					// re-evaluate that one test (O(1), no ray walk) rather than subtract blindly.
					var gSub = g0 - oldD + NewD;
					var aSub = a0;
					if (AirborneCounts(map, fUV, tUV, x))
						aSub = a0 - (oldD / Map.ShadowAirborneDivisor) + (NewD / Map.ShadowAirborneDivisor);

					// Compare the FINAL BYTES, which is what the layer stores and the sim reads.
					var gA = (byte)Math.Min(Map.ForestGroundShadow(gRebuild), (int)byte.MaxValue);
					var gB = (byte)Math.Min(Map.ForestGroundShadow(gSub), (int)byte.MaxValue);
					var aA = (byte)Math.Min(Math.Ceiling(aRebuild), byte.MaxValue);
					var aB = (byte)Math.Min(Math.Ceiling(aSub), byte.MaxValue);

					if (gA != gB)
						groundMismatch++;
					if (aA != aB)
						airMismatch++;

					// Also require BIT equality of the float itself, not just the ceil.
					if (aRebuild != aSub)
						airMismatch++;

					checkedPairs++;
					if (checkedPairs >= 200000)
						goto done;
				}
			}

		done:
			Console.WriteLine($"### SUBTRACTABILITY over {checkedPairs} crossing pairs");
			Console.WriteLine($"   ground  rebuild-vs-subtract mismatches: {groundMismatch}");
			Console.WriteLine($"   airborne rebuild-vs-subtract mismatches (incl. raw float bit compare): {airMismatch}");
		}

		/// <summary>The zLos gate that decides whether tile <paramref name="probe"/> contributes to
		/// the AIRBORNE accumulator on the ray from -> to. Pure geometry; no density involved.</summary>
		static bool AirborneCounts(Map map, MPos fromUV, MPos toUV, MPos probe)
		{
			var zA = Map.ShadowEyeHeight;
			var fromCenter = map.CenterOfCell(fromUV.ToCPos(map));
			var toCenter = map.CenterOfCell(toUV.ToCPos(map));
			var p0 = new WPos(fromCenter.X, fromCenter.Y, zA);
			var p1 = new WPos(toCenter.X, toCenter.Y, 0);
			var delta = p1 - p0;

			var tileCenter = map.CenterOfCell(probe.ToCPos(map));
			var vecToTile = new WVec(tileCenter.X - p0.X, tileCenter.Y - p0.Y, 0);
			var dot = (vecToTile.X * delta.X) + (vecToTile.Y * delta.Y);
			var deltaLengthSquared = (delta.X * delta.X) + (delta.Y * delta.Y);
			var t = dot / (float)deltaLengthSquared;
			t = Math.Max(0, Math.Min(1, t));
			return Map.ShadowObstacleHeight > zA * (1 - t);
		}

		/// <summary>Re-implements RecomputeShadowFrom's inner trace, exposing the raw accumulators.</summary>
		static bool Trace(Map map, MPos fromUV, MPos toUV, MPos probe, out int ground, out float airborne, out bool crossesProbe)
		{
			ground = 0;
			airborne = 0f;
			crossesProbe = false;

			var zA = Map.ShadowEyeHeight;
			var fromCenter = map.CenterOfCell(fromUV.ToCPos(map));
			var toCenter = map.CenterOfCell(toUV.ToCPos(map));
			var p0 = new WPos(fromCenter.X, fromCenter.Y, zA);
			var p1 = new WPos(toCenter.X, toCenter.Y, 0);
			var delta = p1 - p0;

			foreach (var tile in map.DensityLayer.TilesIntersectingLine(fromUV, toUV))
			{
				if (tile == fromUV || tile == toUV)
					continue;

				if (tile == probe)
					crossesProbe = true;

				ground += map.DensityLayer[tile];

				var tileCenter = map.CenterOfCell(tile.ToCPos(map));
				var vecToTile = new WVec(tileCenter.X - p0.X, tileCenter.Y - p0.Y, 0);
				var dot = (vecToTile.X * delta.X) + (vecToTile.Y * delta.Y);
				var deltaLengthSquared = (delta.X * delta.X) + (delta.Y * delta.Y);
				var t = dot / (float)deltaLengthSquared;
				t = Math.Max(0, Math.Min(1, t));
				var zLos = zA * (1 - t);

				if (Map.ShadowObstacleHeight > zLos)
					airborne += map.DensityLayer[tile] / Map.ShadowAirborneDivisor;
			}

			return true;
		}

		static MPos PickDensestCell(Map map)
		{
			var best = map.AllCells.MapCoords.First();
			var bestD = -1;
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

			return best;
		}
	}
}
