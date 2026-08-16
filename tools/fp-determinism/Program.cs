using System;
using System.Globalization;
using System.Runtime.InteropServices;
using OpenRA.Mods.Common.Traits;

namespace FpDeterminism
{
	/// <summary>
	/// Cross-runtime determinism probe for CohesionIntentMath — the double-precision formation
	/// classifier that runs on the SYNCED order-resolution path (CohesionMoveModifier.ModifyGroupOrder,
	/// invoked from UnitOrders on every client) and picks each actor's destination cell.
	///
	/// Two players desynced on 2026-08-16 running .NET CLR 8.0.27 and 10.0.10. If this arithmetic
	/// differs between runtimes, that alone explains a single actor walking to a different cell on
	/// each machine with the shared RNG stream untouched.
	///
	/// Run the SAME assembly under each runtime and diff stdout.
	/// </summary>
	public static class Program
	{
		// The shipped CohesionMoveModifierInfo defaults.
		const int OpenDensityThreshold = 15;
		const float TreelineMinSpreadSq = 2f;
		const float TreelineAnisotropyRatio = 2.5f;
		const int EdgeOffsetThresholdCellsSq = 9;
		const int SampleRadius = 4;

		static string Bits(double d) => BitConverter.DoubleToInt64Bits(d).ToString("X16", CultureInfo.InvariantCulture);

		/// <summary>
		/// Hand-rolled xorshift64*. Deliberately NOT System.Random: its algorithm is an
		/// implementation detail that may differ between runtime versions, which would make the
		/// harness measure itself instead of the code under test.
		/// </summary>
		sealed class Rng
		{
			ulong s;
			public Rng(ulong seed) { s = seed == 0 ? 0x9E3779B97F4A7C15UL : seed; }
			public ulong Next()
			{
				s ^= s >> 12; s ^= s << 25; s ^= s >> 27;
				return s * 0x2545F4914F6CDD1DUL;
			}
			public int Next(int maxExclusive) => (int)(Next() % (ulong)maxExclusive);
		}

		/// <summary>Walks a density window exactly as CohesionMoveModifier.ClassifyIntent does.</summary>
		static (int Total, int Wx, int Wy, long Sxx, long Syy, long Sxy) Moments(int[,] density)
		{
			var total = 0; var wx = 0; var wy = 0;
			long sxx = 0, syy = 0, sxy = 0;
			for (var dy = -SampleRadius; dy <= SampleRadius; dy++)
			{
				for (var dx = -SampleRadius; dx <= SampleRadius; dx++)
				{
					var d = density[dx + SampleRadius, dy + SampleRadius];
					if (d == 0)
						continue;

					total += d;
					wx += dx * d;
					wy += dy * d;
					sxx += (long)dx * dx * d;
					syy += (long)dy * dy * d;
					sxy += (long)dx * dy * d;
				}
			}

			return (total, wx, wy, sxx, syy, sxy);
		}

		static CohesionIntentResult Run(int[,] density)
		{
			var m = Moments(density);
			return CohesionIntentMath.Classify(m.Total, m.Wx, m.Wy, m.Sxx, m.Syy, m.Sxy,
				OpenDensityThreshold, TreelineMinSpreadSq, TreelineAnisotropyRatio, EdgeOffsetThresholdCellsSq);
		}

		static void Detail(string name, int[,] density)
		{
			var m = Moments(density);
			var r = Run(density);
			Console.WriteLine($"CASE {name}");
			Console.WriteLine($"  in    total={m.Total} wx={m.Wx} wy={m.Wy} sxx={m.Sxx} syy={m.Syy} sxy={m.Sxy}");
			Console.WriteLine($"  out   intent={r.Intent} cdx={r.CentroidDxCells} cdy={r.CentroidDyCells} lax={r.LineAlongX} lay={r.LineAlongY}");
			Console.WriteLine($"  bits  cxx={Bits(r.Cxx)} cyy={Bits(r.Cyy)} cxy={Bits(r.Cxy)}");
			Console.WriteLine($"  bits  disc={Bits(r.Disc)} l1={Bits(r.Lambda1)} l2={Bits(r.Lambda2)}");
		}

		static int[,] Empty() => new int[2 * SampleRadius + 1, 2 * SampleRadius + 1];

		static int[,] Treeline(int len, int weight, int rowOffset)
		{
			var g = Empty();
			for (var i = -len; i <= len; i++)
			{
				var x = i + SampleRadius;
				var y = rowOffset + SampleRadius;
				if (x >= 0 && x < 2 * SampleRadius + 1 && y >= 0 && y < 2 * SampleRadius + 1)
					g[x, y] = weight;
			}

			return g;
		}

		static int[,] Blob(int radius, int weight)
		{
			var g = Empty();
			for (var dy = -radius; dy <= radius; dy++)
				for (var dx = -radius; dx <= radius; dx++)
					if (dx * dx + dy * dy <= radius * radius)
						g[dx + SampleRadius, dy + SampleRadius] = weight;

			return g;
		}

		public static int Main(string[] args)
		{
			var fx = RuntimeInformation.FrameworkDescription;
			Console.WriteLine($"FrameworkDescription: {fx}");
			Console.WriteLine($"ProcessArchitecture:  {RuntimeInformation.ProcessArchitecture}");
			Console.WriteLine($"OSDescription:        {RuntimeInformation.OSDescription}");

			// A harness that silently ran on the wrong runtime measures nothing. Refuse to
			// produce output that could be mistaken for a result.
			if (args.Length > 0)
			{
				var want = args[0];
				if (!fx.Contains(want, StringComparison.Ordinal))
				{
					Console.Error.WriteLine($"FATAL: expected runtime containing '{want}' but got '{fx}'.");
					return 2;
				}

				Console.WriteLine($"Runtime assertion OK (contains '{want}')");
			}

			Console.WriteLine();

			// 1. Early-return path — below OpenDensityThreshold, no floating point executes at all.
			Detail("open-empty", Empty());
			Detail("open-below-threshold", Treeline(1, 4, 0));

			// 2. Cover-aware paths.
			Detail("treeline-horizontal", Treeline(4, 10, 0));
			Detail("treeline-offset-row", Treeline(4, 10, 2));
			Detail("blob-symmetric", Blob(3, 10));
			Detail("blob-tight", Blob(1, 40));

			Console.WriteLine();

			// 3. Boundary hunt: sweep weights/lengths so lambda1 lands near TreelineMinSpreadSq (2.0)
			//    and near the anisotropy ratio, where a one-ULP difference flips the whole strategy.
			var nearBoundary = 0;
			for (var len = 1; len <= 4; len++)
			{
				for (var w = 4; w <= 60; w++)
				{
					var r = Run(Treeline(len, w, 0));
					var d1 = Math.Abs(r.Lambda1 - TreelineMinSpreadSq);
					var d2 = Math.Abs(r.Lambda1 - TreelineAnisotropyRatio * Math.Max(r.Lambda2, 0.0));
					if (d1 < 0.5 || d2 < 0.5)
					{
						nearBoundary++;
						Console.WriteLine($"BOUNDARY len={len} w={w} intent={r.Intent} l1={Bits(r.Lambda1)} l2={Bits(r.Lambda2)} disc={Bits(r.Disc)}");
					}
				}
			}

			Console.WriteLine($"boundary cases printed: {nearBoundary}");
			Console.WriteLine();

			// 4. Randomised sweep, folded into one digest. A single differing case anywhere in the
			//    sweep changes the digest, so this is the sensitive instrument; the detail above is
			//    only for reading off WHERE a difference is once the digest disagrees.

			// Echo argv. This is not debug residue: zsh does NOT word-split an unquoted "$var", so
			// `for p in "--perturb 0"; do run $p; done` passes ONE glued argument and the flag below
			// silently never parses — which made the sensitivity check pass while measuring nothing.
			// Print what actually arrived.
			Console.WriteLine($"ARGC={args.Length} ARGS=[{string.Join("|", args)}]");

			// Sensitivity self-test: --perturb N nudges exactly ONE case's Lambda1 by a single ULP.
			// If that does not move the digest, the instrument cannot detect what it exists to
			// detect and a matching digest across runtimes would be worthless. Run it before
			// believing any negative result.
			var perturbCase = -1;
			for (var i = 0; i < args.Length; i++)
				if (args[i] == "--perturb" && i + 1 < args.Length)
					perturbCase = int.Parse(args[i + 1], CultureInfo.InvariantCulture);

			var rng = new Rng(0xC0FFEE1234567890UL);
			ulong digest = 1469598103934665603UL;

			void Fold(ulong v)
			{
				digest ^= v;
				digest *= 1099511628211UL;
			}

			var intents = new int[4];
			const int Sweeps = 200000;
			for (var i = 0; i < Sweeps; i++)
			{
				var g = Empty();
				var cells = 1 + rng.Next(30);
				for (var c = 0; c < cells; c++)
				{
					var x = rng.Next(2 * SampleRadius + 1);
					var y = rng.Next(2 * SampleRadius + 1);
					g[x, y] += 1 + rng.Next(25);
				}

				var r = Run(g);
				intents[(int)r.Intent]++;
				Fold((ulong)(int)r.Intent);
				Fold((ulong)r.CentroidDxCells);
				Fold((ulong)r.CentroidDyCells);
				Fold((ulong)r.LineAlongX);
				Fold((ulong)r.LineAlongY);
				Fold((ulong)BitConverter.DoubleToInt64Bits(r.Cxx));
				Fold((ulong)BitConverter.DoubleToInt64Bits(r.Cyy));
				Fold((ulong)BitConverter.DoubleToInt64Bits(r.Cxy));
				Fold((ulong)BitConverter.DoubleToInt64Bits(r.Disc));
				Fold((ulong)BitConverter.DoubleToInt64Bits(i == perturbCase ? Math.BitIncrement(r.Lambda1) : r.Lambda1));
				Fold((ulong)BitConverter.DoubleToInt64Bits(r.Lambda2));
			}

			Console.WriteLine($"perturbCase: {perturbCase}");
			Console.WriteLine($"sweep cases: {Sweeps}");
			Console.WriteLine($"intent histogram: Open={intents[0]} SpreadInside={intents[1]} EdgeLine={intents[2]} Approach={intents[3]}");
			Console.WriteLine($"SWEEP DIGEST: {digest:X16}");
			Console.WriteLine();

			LayoutProbe(args);
			return 0;
		}

		/// <summary>
		/// Second probe: CohesionLayoutMath — the kernels that emit the actual destination CELLS.
		/// Nearer the observed symptom (a differing Mobile.ToCell) than the classifier is, because
		/// these are what a cell coordinate is finally rounded out of.
		/// </summary>
		static void LayoutProbe(string[] args)
		{
			var perturb = -1;
			for (var i = 0; i < args.Length; i++)
				if (args[i] == "--perturb-layout" && i + 1 < args.Length)
					perturb = int.Parse(args[i + 1], CultureInfo.InvariantCulture);

			Console.WriteLine("=== LAYOUT KERNELS ===");
			Console.WriteLine($"perturbLayout: {perturb}");

			// Detail cases, raw bits of every intermediate double.
			for (var n = 1; n <= 6; n++)
				Console.WriteLine($"  boxColumns n={n} -> {CohesionLayoutMath.BoxColumns(n)}");

			foreach (var (ax, ay) in new[] { (4, 0), (0, 4), (3, 3), (-4, 1), (1, -4), (2, -3) })
			{
				var f = CohesionLayoutMath.TreelineForward(ax, ay);
				Console.WriteLine($"  treelineForward({ax},{ay}) fx={Bits(f.ForwardX)} fy={Bits(f.ForwardY)} len={Bits(f.AlongLen)}");
			}

			foreach (var (gx, gy, pct) in new[] { (3, 0, 100), (2, 2, 100), (5, -3, 75), (-1, 4, 120), (7, 7, 50) })
			{
				var e = CohesionLayoutMath.EdgeAnchorOffset(gx, gy, pct);
				Console.WriteLine($"  edgeAnchor({gx},{gy},{pct}) dx={e.AnchorDx} dy={e.AnchorDy} ux={Bits(e.UnitX)} uy={Bits(e.UnitY)} len={Bits(e.GradLen)} adv={Bits(e.Advance)}");
			}

			foreach (var (fx, fy, i2, n2, sp) in new[] { (1.0, 0.0, 3, 8, 1024), (0.0, 1.0, 0, 5, 1536), (0.7071067811865476, 0.7071067811865476, 4, 9, 2048) })
			{
				var o = CohesionLayoutMath.LineSlotOffset(fx, fy, i2, n2, sp);
				Console.WriteLine($"  lineSlot(f=({fx},{fy}),i={i2},n={n2},sp={sp}) dx={o.Dx} dy={o.Dy} t={Bits(o.T)}");
			}

			foreach (var (dx, dy) in new[] { (10, 0), (7, 7), (-13, 5), (1, 1) })
			{
				var w = CohesionLayoutMath.ApproachWalk(dx, dy);
				Console.WriteLine($"  approachWalk({dx},{dy}) dist={Bits(w.DistCells)} steps={w.MaxSteps} ux={Bits(w.UnitX)} uy={Bits(w.UnitY)}");
			}

			Console.WriteLine();

			// Boundary hunt: (int)Math.Round flips when the product lands on a half-integer. Those
			// are the inputs where a one-ULP disagreement becomes a different CELL, which is the
			// whole failure mode. Report how close each candidate gets.
			var nearHalf = 0;
			for (var gx = -8; gx <= 8; gx++)
			{
				for (var gy = -8; gy <= 8; gy++)
				{
					if (gx == 0 && gy == 0)
						continue;

					for (var pct = 25; pct <= 200; pct += 25)
					{
						var e = CohesionLayoutMath.EdgeAnchorOffset(gx, gy, pct);
						var px = e.UnitX * e.Advance;
						var py = e.UnitY * e.Advance;
						var fx2 = Math.Abs(px - Math.Floor(px) - 0.5);
						var fy2 = Math.Abs(py - Math.Floor(py) - 0.5);
						if (fx2 < 1e-9 || fy2 < 1e-9)
						{
							nearHalf++;
							Console.WriteLine($"  HALF-INT edgeAnchor({gx},{gy},{pct}) px={Bits(px)} py={Bits(py)} -> dx={e.AnchorDx} dy={e.AnchorDy}");
						}
					}
				}
			}

			Console.WriteLine($"  half-integer boundary hits: {nearHalf}");

			// Randomised sweep over every kernel, folded into one digest.
			var rng = new Rng(0x5EEDF00DBAADF00DUL);
			ulong d2 = 1469598103934665603UL;
			void Fold2(ulong v) { d2 ^= v; d2 *= 1099511628211UL; }

			const int LayoutSweeps = 200000;
			for (var k = 0; k < LayoutSweeps; k++)
			{
				var gx = rng.Next(33) - 16;
				var gy = rng.Next(33) - 16;
				var pct = 10 + rng.Next(200);
				var n2 = 1 + rng.Next(40);
				var i2 = rng.Next(n2);
				var sp = 256 + rng.Next(4096);

				Fold2((ulong)CohesionLayoutMath.BoxColumns(n2));

				var f = CohesionLayoutMath.TreelineForward(gx == 0 && gy == 0 ? 1 : gx, gy);
				Fold2((ulong)BitConverter.DoubleToInt64Bits(f.ForwardX));
				Fold2((ulong)BitConverter.DoubleToInt64Bits(f.ForwardY));
				Fold2((ulong)BitConverter.DoubleToInt64Bits(f.AlongLen));

				if (gx != 0 || gy != 0)
				{
					var e = CohesionLayoutMath.EdgeAnchorOffset(gx, gy, pct);
					Fold2((ulong)e.AnchorDx);
					Fold2((ulong)e.AnchorDy);
					Fold2((ulong)BitConverter.DoubleToInt64Bits(k == perturb ? Math.BitIncrement(e.UnitX) : e.UnitX));
					Fold2((ulong)BitConverter.DoubleToInt64Bits(e.UnitY));
					Fold2((ulong)BitConverter.DoubleToInt64Bits(e.GradLen));
					Fold2((ulong)BitConverter.DoubleToInt64Bits(e.Advance));

					var w = CohesionLayoutMath.ApproachWalk(gx, gy);
					Fold2((ulong)w.MaxSteps);
					Fold2((ulong)BitConverter.DoubleToInt64Bits(w.DistCells));
					Fold2((ulong)BitConverter.DoubleToInt64Bits(w.UnitX));
					Fold2((ulong)BitConverter.DoubleToInt64Bits(w.UnitY));

					for (var step = 0; step <= 3; step++)
					{
						var a = CohesionLayoutMath.ApproachStepOffset(w.UnitX, w.UnitY, step);
						Fold2((ulong)a.Dx);
						Fold2((ulong)a.Dy);
						var nu = CohesionLayoutMath.NudgeOffset(f.ForwardX, f.ForwardY, step);
						Fold2((ulong)nu.Dx);
						Fold2((ulong)nu.Dy);
					}
				}

				var o = CohesionLayoutMath.LineSlotOffset(f.ForwardX, f.ForwardY, i2, n2, sp);
				Fold2((ulong)o.Dx);
				Fold2((ulong)o.Dy);
				Fold2((ulong)BitConverter.DoubleToInt64Bits(o.T));

				var ol = CohesionLayoutMath.OpenLineOffset(i2, n2, sp);
				Fold2((ulong)ol.Dx);
				Fold2((ulong)BitConverter.DoubleToInt64Bits(ol.T));
			}

			Console.WriteLine($"  layout sweep cases: {LayoutSweeps}");
			Console.WriteLine($"LAYOUT DIGEST: {d2:X16}");
		}
	}
}
