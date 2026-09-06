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

using OpenRA.Primitives;

namespace OpenRA.Support
{
	public static class PerfHistory
	{
		static readonly Color[] Colors =
		{
			Color.Red, Color.Green,
			Color.Orange, Color.Yellow,
			Color.Fuchsia, Color.Lime,
			Color.Cyan, Color.Blue,
			Color.White, Color.Teal,
			Color.Pink, Color.MediumPurple,
			Color.Olive, Color.CornflowerBlue
		};

		static int nextColor;

		public static Cache<string, PerfItem> Items = new(
			s =>
			{
				var x = new PerfItem(s, Colors[nextColor++]);
				if (nextColor >= Colors.Length) nextColor = 0;
				return x;
			});

		/// <summary>
		/// True while something is actually reading these numbers - the perf graph, the perf text overlay,
		/// or benchmark mode. Refreshed once per rendered frame by Game.RenderTick.
		/// </summary>
		// WHY THIS EXISTS. A PerfSample costs two Stopwatch.GetTimestamp() calls plus the string-keyed Cache
		// lookup in Increment below. Benchmarked at 53.5 ns for a sampled call against 3.6 ns for the same call
		// unsampled (best of 7 x 20M iterations, net6.0 Release, standing in for TerrainLighting.TintAt with no
		// light source in range) - so on a per-sprite-per-frame path the instrumentation is 14.8x the cost of
		// the thing being instrumented, all of it wasted whenever nobody is looking. Branching on this costs
		// 1.1 ns. Callers on such a path should do so and skip the sample.
		//
		// This does NOT gate PerfSample globally, deliberately: the other thirteen call sites run once per tick,
		// per frame or per bot tick, where the sample is both wanted and free. Moving the check inside
		// PerfSample itself would fix the whole class in three lines and is the obvious follow-up, but it
		// changes shared instrumentation for every mod in the tree and belongs in its own change, not in a
		// lighting branch.
		public static bool Sampling;

		public static void Increment(string item, double x)
		{
			Items[item].Val += x;
		}

		public static void Tick()
		{
			foreach (var item in Items.Values)
				if (item.HasNormalTick)
					item.Tick();
		}

		public static void Reset()
		{
			foreach (var item in Items.Values)
				item.ResetSamples();
		}
	}
}
