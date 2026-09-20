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

using System;

namespace OpenRA.Mods.Common.Lighting
{
	/// <summary>
	/// The four-slot memo that collapses one notified cell's repeated corner samples, held per thread so a
	/// row-partitioned sweep can use it. See TerrainLighting for what it memoises and why four slots is the
	/// whole working set.
	/// </summary>
	/// <remarks>
	/// <para>WHY PER-THREAD RATHER THAN PER-TRAIT. The memo's contents are only valid for the ONE cell a
	/// sweep is currently notifying, and a parallel sweep has as many "current cells" as it has workers. A
	/// field on TerrainLighting would have every worker overwriting every other worker's four slots, and
	/// the failure would not be a crash -- it would be a hit returning the tint of some other cell, i.e.
	/// silently wrong colours. [ThreadStatic] makes "the cell this thread is on" the natural scope.</para>
	/// <para>IT IS ALSO STRICTLY SAFER THAN THE FIELD IT REPLACES. Any thread that has not called
	/// <see cref="Begin"/> reads Sweeping == false and takes the ordinary uncached path, so a render thread
	/// sampling TintAt while a sweep is in flight can no longer see a half-written memo -- which the shared
	/// field allowed in principle and only a comment forbade.</para>
	/// <para>NESTING IS NOT SUPPORTED, and cannot arise: a sweep is armed and disarmed inside one
	/// NotifyCells call, and nothing NotifyCells invokes calls back into it. <see cref="Begin"/> throws if
	/// it finds the memo already armed on this thread rather than silently sharing slots with an outer
	/// sweep, which would be the same wrong-colours failure one level up.</para>
	/// </remarks>
	public static class SweepMemo
	{
		/// <summary>
		/// Slots held per thread. TerrainSpriteLayer samples a cell at its four corners, so four is exactly
		/// the live working set and a fifth distinct point inside one cell is simply not memoised.
		/// </summary>
		public const int Slots = 4;

		[ThreadStatic]
		static bool sweeping;

		[ThreadStatic]
		static int live;

		[ThreadStatic]
		static WPos[] keys;

		[ThreadStatic]
		static float3[] values;

		/// <summary>Is this thread inside a sweep?</summary>
		public static bool Sweeping => sweeping;

		/// <summary>Arms the memo for the calling thread. Pair with <see cref="End"/> in a finally.</summary>
		public static void Begin()
		{
			if (sweeping)
				throw new InvalidOperationException("The sweep memo is already armed on this thread; sweeps must not nest.");

			// One allocation per thread for the life of the process: thread-pool threads are reused across
			// refreshes, so after the first sweep this is a null check.
			keys ??= new WPos[Slots];
			values ??= new float3[Slots];

			sweeping = true;
			live = 0;
		}

		/// <summary>Disarms the memo for the calling thread.</summary>
		public static void End()
		{
			sweeping = false;
			live = 0;
		}

		/// <summary>
		/// Drops the entries for the cell just finished. Each cell brings its own four sample points, so the
		/// previous cell's are dead the moment this one starts.
		/// </summary>
		public static void NextCell()
		{
			live = 0;
		}

		/// <summary>Returns a memoised tint for <paramref name="pos"/>, if this thread has one.</summary>
		public static bool TryGet(WPos pos, out float3 tint)
		{
			if (sweeping)
			{
				var k = keys;
				for (var i = 0; i < live; i++)
				{
					if (k[i] == pos)
					{
						tint = values[i];
						return true;
					}
				}
			}

			tint = float3.Zero;
			return false;
		}

		/// <summary>Offers a freshly computed tint to the memo. A no-op outside a sweep, or once full.</summary>
		public static void Store(WPos pos, in float3 tint)
		{
			if (!sweeping || live >= Slots)
				return;

			keys[live] = pos;
			values[live] = tint;
			live++;
		}
	}
}
