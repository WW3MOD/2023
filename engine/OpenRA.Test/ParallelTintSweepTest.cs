#region Copyright & License Information
/*
 * WW3MOD: the parallel terrain relight writes exactly what the serial one wrote.
 *
 * TerrainLighting.NotifyCells may split a light's footprint into contiguous bands of vertex rows and run them
 * on the thread pool. The claim that buys is "changes no output, only who computes it", and that claim is worth
 * nothing as an argument -- a memo shared between workers, a torn 48-byte Vertex or a row the partitioner
 * dropped would all produce a plausible-looking picture that is quietly wrong, and none of them would throw.
 *
 * So the whole of this fixture is one assertion made several ways: run the same sweep serial and parallel over
 * the same synthetic light set, and the resulting vertex arrays must be EQUAL FIELD FOR FIELD, not merely close.
 *
 * WHAT IS REAL HERE AND WHAT IS SYNTHETIC. Real: CellRowSweep (the partitioner), SweepMemo (the four-slot
 * per-thread memo), TerrainSpriteLayer.ApplyCellTint (the per-cell vertex write), SpatiallyPartitioned (the
 * light lookup every TintAt makes) and TerrainLighting.ApplyFalloff (the curve). Synthetic: the Map, which
 * stands in as a plain rectangular grid -- TerrainSpriteLayer needs a Sheet and a GPU vertex buffer and Map
 * needs a ModData, so neither can be stood up in NUnit, and the addressing they contribute
 * (offset = vertexRowStride * V + 4 * U) is reproduced here exactly rather than mocked away.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Lighting;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;

namespace OpenRA.Test
{
	[TestFixture]
	public class ParallelTintSweepTest
	{
		const int MapWidth = 128;
		const int MapHeight = 128;
		const int VertexRowStride = 4 * MapWidth;

		sealed class Light
		{
			public WPos Pos;
			public int Range;
			public float Intensity;
			public float3 Tint;
		}

		/// <summary>
		/// The shape of TerrainLighting.Tint: sum every source whose bin contains the sample point and whose
		/// radius reaches it, through the real falloff curve. Deliberately NOT commutative-safe by accident --
		/// float addition is order-dependent, so if the parallel sweep ever visited a cell's lights in a
		/// different order this would show up as a non-identical result rather than a rounding difference.
		/// </summary>
		static float3 Tint(SpatiallyPartitioned<Light> partition, WPos pos)
		{
			var intensity = 1f;
			var tint = new float3(1f, 1f, 1f);
			foreach (var source in partition.At(new int2(pos.X, pos.Y)))
			{
				var distance = (source.Pos - pos).Length;
				if (distance > source.Range)
					continue;

				var falloff = TerrainLighting.ApplyFalloff(
					LightFalloff.InverseSquare, (source.Range - distance) * 1f / source.Range);

				intensity += falloff * source.Intensity;
				tint += falloff * source.Tint;
			}

			return intensity * tint;
		}

		/// <summary>
		/// One notified cell, doing what a TerrainSpriteLayer does: four corner samples through the memo, then
		/// the real ApplyCellTint over the four vertices this cell owns.
		/// </summary>
		static void NotifyCell(SpatiallyPartitioned<Light> partition, Vertex[] vertices, int u, int v)
		{
			var pos = new WPos(1024 * u + 512, 1024 * v + 512, 0);
			const int Step = 512;

			Span<float3> weights = stackalloc float3[4];
			ReadOnlySpan<WVec> corners = stackalloc WVec[4]
			{
				new WVec(-Step, -Step, 0), new WVec(Step, -Step, 0),
				new WVec(Step, Step, 0), new WVec(-Step, Step, 0)
			};

			// Through the memo, exactly as TintAt does: a hit must return bit-for-bit what a recompute would.
			for (var i = 0; i < 4; i++)
			{
				var sample = pos + corners[i];
				if (!SweepMemo.TryGet(sample, out var tint))
				{
					tint = Tint(partition, sample);
					SweepMemo.Store(sample, tint);
				}

				weights[i] = tint;
			}

			TerrainSpriteLayer.ApplyCellTint(vertices, VertexRowStride * v + 4 * u, weights);
		}

		/// <summary>
		/// The sweep under test, driven through the real CellRowSweep. <paramref name="layers"/> stands in for
		/// the 13-20 TerrainSpriteLayers a WW3MOD world carries: they are what makes the memo pay, and running
		/// several of them back to back per cell is the ordering the memo depends on.
		/// </summary>
		static Vertex[][] Sweep(SpatiallyPartitioned<Light> partition, WPos centre, int searchRadius, int layers, bool parallel)
		{
			var vertices = new Vertex[layers][];
			for (var l = 0; l < layers; l++)
				vertices[l] = SeedVertices(l);

			var searchSq = (long)searchRadius * searchRadius;
			var topLeft = new int2((centre.X - searchRadius) / 1024, (centre.Y - searchRadius) / 1024);
			var bottomRight = new int2((centre.X + searchRadius) / 1024, (centre.Y + searchRadius) / 1024);

			var minU = Math.Max(0, topLeft.X);
			var maxU = Math.Min(MapWidth - 1, bottomRight.X);
			var minV = Math.Max(0, topLeft.Y);
			var maxV = Math.Min(MapHeight - 1, bottomRight.Y);

			CellRowSweep.Run(minV, maxV + 1, maxU - minU + 1, parallel, (fromRow, toRow) =>
			{
				SweepMemo.Begin();
				try
				{
					for (var v = fromRow; v < toRow; v++)
					{
						for (var u = minU; u <= maxU; u++)
						{
							var delta = new WPos(1024 * u + 512, 1024 * v + 512, 0) - centre;
							if ((long)delta.X * delta.X + (long)delta.Y * delta.Y > searchSq)
								continue;

							SweepMemo.NextCell();

							// Every layer for this cell, back to back, before the sweep moves on: the memo's
							// hit rate is (layers-1)/layers and depends on exactly this ordering.
							for (var l = 0; l < layers; l++)
								NotifyCell(partition, vertices[l], u, v);
						}
					}
				}
				finally
				{
					SweepMemo.End();
				}
			});

			return vertices;
		}

		/// <summary>Distinct non-trivial starting vertices, so a lost write cannot hide behind a zero.</summary>
		static Vertex[] SeedVertices(int layer)
		{
			var vertices = new Vertex[VertexRowStride * MapHeight];
			for (var i = 0; i < vertices.Length; i++)
			{
				var f = (i % 977) / 977f;
				vertices[i] = new Vertex(i, i + 1, i + 2, f, 1 - f, f * 2, f * 3, (uint)(i + layer),
					0.25f + f, 0.5f - 0.25f * f, 0.75f * f, 0.3f + 0.6f * f);
			}

			return vertices;
		}

		static SpatiallyPartitioned<Light> BuildLights(int count, int seed)
		{
			// The bin size TerrainLightingInfo ships (10 cells), so the number of sources per bin -- and
			// therefore the concurrent-read pattern over At() -- matches the shipped one.
			var partition = new SpatiallyPartitioned<Light>((MapWidth + 1) * 1024, (MapHeight + 1) * 1024, 10 * 1024);
			var random = new Random(seed);
			for (var i = 0; i < count; i++)
			{
				var light = new Light
				{
					Pos = new WPos(random.Next(0, MapWidth * 1024), random.Next(0, MapHeight * 1024), 0),
					Range = random.Next(8, 70) * 1024,
					Intensity = 0.5f + (float)random.NextDouble() * 6f,
					Tint = new float3(
						(float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble())
				};

				partition.Add(light, new Rectangle(
					light.Pos.X - light.Range, light.Pos.Y - light.Range, 2 * light.Range, 2 * light.Range));
			}

			return partition;
		}

		static void AssertIdentical(Vertex[][] expected, Vertex[][] actual)
		{
			Assert.That(actual.Length, Is.EqualTo(expected.Length));
			for (var l = 0; l < expected.Length; l++)
			{
				var a = expected[l];
				var b = actual[l];
				Assert.That(b.Length, Is.EqualTo(a.Length));
				for (var i = 0; i < a.Length; i++)
				{
					// Field for field and BITWISE, via the raw float bits: Is.EqualTo on a float would let
					// a NaN-vs-NaN pair through as unequal and, worse, would pass a -0.0 against a +0.0.
					// Nothing here should be producing either, which is the point of checking.
					if (Bits(a[i]) != Bits(b[i]))
						Assert.Fail(
							$"Layer {l}, vertex {i} (cell u={i / 4 % MapWidth}, v={i / VertexRowStride}) differs: " +
							$"serial RGBA ({a[i].R}, {a[i].G}, {a[i].B}, {a[i].A}) vs " +
							$"parallel RGBA ({b[i].R}, {b[i].G}, {b[i].B}, {b[i].A}).");
				}
			}
		}

		static (int R, int G, int B, int A, float X, float Y, float Z, uint C) Bits(in Vertex v)
		{
			return (BitConverter.SingleToInt32Bits(v.R), BitConverter.SingleToInt32Bits(v.G),
				BitConverter.SingleToInt32Bits(v.B), BitConverter.SingleToInt32Bits(v.A),
				v.X, v.Y, v.Z, v.C);
		}

		[Test]
		public void ParallelSweepWritesByteIdenticalVertices()
		{
			// THE HEADLINE. Six large overlapping sources on a 128x128 grid is the shape the change was built
			// for -- a six-RV Sarmat salvo late in its life, where every refresh walks nearly every cell and
			// several fireballs are in range of each sample.
			var partition = BuildLights(6, 20260920);
			var centre = new WPos(64 * 1024, 64 * 1024, 0);

			var serial = Sweep(partition, centre, 100 * 1024, 20, false);
			var parallel = Sweep(partition, centre, 100 * 1024, 20, true);

			AssertIdentical(serial, parallel);
		}

		[Test]
		public void ParallelSweepIsIdenticalAcrossRepeatedRuns()
		{
			// A race that loses one write in a thousand passes a single comparison and fails in front of a
			// player. Repeating the parallel arm against one serial baseline is what gives the scheduler
			// enough chances to interleave differently.
			var partition = BuildLights(9, 7);
			var centre = new WPos(60 * 1024, 70 * 1024, 0);
			var serial = Sweep(partition, centre, 90 * 1024, 8, false);

			for (var i = 0; i < 5; i++)
				AssertIdentical(serial, Sweep(partition, centre, 90 * 1024, 8, true));
		}

		[Test]
		public void ParallelSweepIsIdenticalWithOneLayer()
		{
			// The memo's hit rate is (layers-1)/layers, so at one layer it never hits and every corner is
			// recomputed. That is the arm in which a broken memo CANNOT hide the bug, and the arm in which a
			// broken PARTITION still shows: any row the chunking dropped or doubled lands here.
			var partition = BuildLights(4, 99);
			var centre = new WPos(64 * 1024, 64 * 1024, 0);

			AssertIdentical(
				Sweep(partition, centre, 100 * 1024, 1, false),
				Sweep(partition, centre, 100 * 1024, 1, true));
		}

		[Test]
		public void ParallelSweepIsIdenticalForALightSmallerThanTheParallelThreshold()
		{
			// Below MinCellsForParallel the parallel flag is refused and the body runs inline. The output must
			// be identical there too -- which it is trivially, and asserting it is how a future change to the
			// threshold stays honest rather than silently taking a different path for small lights.
			var partition = BuildLights(3, 4242);
			var centre = new WPos(40 * 1024, 40 * 1024, 0);

			AssertIdentical(
				Sweep(partition, centre, 6 * 1024, 20, false),
				Sweep(partition, centre, 6 * 1024, 20, true));
		}

		[Test]
		public void EveryRowIsSweptExactlyOnceInBothModes()
		{
			// The partitioner's own contract, independent of anything lighting-related. A chunking bug that
			// dropped the tail row would leave a stale stripe of terrain at the bottom of a blast, and a bug
			// that doubled one would be invisible in the picture but is a double write to shared bytes.
			foreach (var parallel in new[] { false, true })
			{
				foreach (var rows in new[] { 1, 2, 7, 63, 64, 65, 128, 1000 })
				{
					var visits = new int[rows];
					CellRowSweep.Run(0, rows, 64, parallel, (from, to) =>
					{
						for (var r = from; r < to; r++)
							Interlocked.Increment(ref visits[r]);
					});

					for (var r = 0; r < rows; r++)
						Assert.That(visits[r], Is.EqualTo(1), $"row {r} of {rows}, parallel={parallel}");
				}
			}
		}

		[Test]
		public void RowBandsHandedToTheBodyAreContiguousAndInBounds()
		{
			// Contiguity is not cosmetic: a chunk is one span of a row-major vertex array, and a partitioner
			// that handed out strided rows would scatter each worker's writes across the whole buffer.
			var ranges = new List<(int From, int To)>();
			CellRowSweep.Run(17, 17 + 500, 64, true, (from, to) =>
			{
				lock (ranges)
					ranges.Add((from, to));
			});

			ranges.Sort((a, b) => a.From.CompareTo(b.From));
			Assert.That(ranges[0].From, Is.EqualTo(17));
			Assert.That(ranges[^1].To, Is.EqualTo(517));
			for (var i = 1; i < ranges.Count; i++)
				Assert.That(ranges[i].From, Is.EqualTo(ranges[i - 1].To), "bands must meet without a gap or an overlap");
		}

		[Test]
		public void ParallelIsRefusedForABandTooSmallToPayForIt()
		{
			Assert.That(CellRowSweep.ShouldParallelize(1, 100000), Is.False, "a single row cannot be split");
			Assert.That(CellRowSweep.ShouldParallelize(16, 16), Is.False, "256 cells is under the threshold");
			Assert.That(CellRowSweep.ShouldParallelize(32, 32), Is.True, "1024 cells is the threshold");
			Assert.That(CellRowSweep.ShouldParallelize(200, 200), Is.True);
		}

		[Test]
		public void ChunkSizeAimsAtSeveralChunksPerWorker()
		{
			// The load-balancing choice, pinned. Rows in a light's bounding box are not equal work -- a row's
			// cost is the chord of the light's circle -- so one chunk per worker leaves the worker holding the
			// middle band on the critical path. See CellRowSweep.ChunksPerWorker.
			Assert.That(CellRowSweep.ChunkRows(128, 16), Is.EqualTo(2), "128 rows over 64 chunks");
			Assert.That(CellRowSweep.ChunkRows(1024, 16), Is.EqualTo(16));

			// Never zero, whatever the shape.
			Assert.That(CellRowSweep.ChunkRows(1, 16), Is.EqualTo(1));
			Assert.That(CellRowSweep.ChunkRows(3, 64), Is.EqualTo(1));
			Assert.That(CellRowSweep.ChunkRows(0, 16), Is.EqualTo(1));
		}

		[Test]
		public void TheMemoIsPrivateToItsThread()
		{
			// The property that makes a per-partition memo possible at all. If these fields were on the trait
			// instead of [ThreadStatic], the store on one thread would be visible to the other and this would
			// return the wrong tint rather than a miss.
			var pos = new WPos(1, 2, 3);
			var mine = new float3(9f, 9f, 9f);

			SweepMemo.Begin();
			try
			{
				SweepMemo.Store(pos, mine);
				Assert.That(SweepMemo.TryGet(pos, out var hit), Is.True);
				Assert.That(hit, Is.EqualTo(mine));

				var sawSweeping = true;
				var leaked = true;
				var other = new Thread(() =>
				{
					sawSweeping = SweepMemo.Sweeping;
					leaked = SweepMemo.TryGet(pos, out _);
				});

				other.Start();
				other.Join();

				Assert.That(sawSweeping, Is.False, "another thread must not see this thread's sweep");
				Assert.That(leaked, Is.False, "another thread must not see this thread's memoised tint");
			}
			finally
			{
				SweepMemo.End();
			}

			Assert.That(SweepMemo.Sweeping, Is.False);
			Assert.That(SweepMemo.TryGet(pos, out _), Is.False, "the memo must be dead outside a sweep");
		}

		[Test]
		public void SweepsMayNotNest()
		{
			// A nested Begin would silently share the outer sweep's four slots, which is the wrong-colours
			// failure one level up. It throws instead.
			SweepMemo.Begin();
			try
			{
				Assert.Throws<InvalidOperationException>(() => SweepMemo.Begin());
			}
			finally
			{
				SweepMemo.End();
			}
		}
	}
}
