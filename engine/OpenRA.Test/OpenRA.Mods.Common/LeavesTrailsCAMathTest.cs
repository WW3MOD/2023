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

using NUnit.Framework;
using OpenRA.Mods.CA.Traits.Render;

namespace OpenRA.Test
{
	/// <summary>
	/// Pins the off-map trail decision for LeavesTrailsCA. The stock "if (!Contains) return;" guard is
	/// intentionally not restored so V3/ICBM projectiles keep trailing as they arc past the map edge,
	/// but GetTerrainInfo throws IndexOutOfRange off-map — so the terrain read must be skipped there.
	/// This math is the seam that keeps unrestricted trails spawning off-map while terrain-restricted
	/// trails skip (they can never match off-map terrain).
	///
	/// Source of truth: engine/OpenRA.Mods.Common/Traits/LeavesTrailsCA.cs
	/// </summary>
	[TestFixture]
	public class LeavesTrailsCAMathTest
	{
		[Test]
		public void InBoundsNeverSkips()
		{
			// In bounds the terrain is readable, so the trail is never skipped on this account
			// regardless of whether it is terrain-restricted.
			Assert.That(LeavesTrailsCAMath.SkipsOffMapTrail(true, 0), Is.False);
			Assert.That(LeavesTrailsCAMath.SkipsOffMapTrail(true, 3), Is.False);
		}

		[Test]
		public void OffMapUnrestrictedTrailStillSpawns()
		{
			// The V3/ICBM case: no terrain restriction (TerrainTypes empty) ⇒ the trail keeps
			// spawning even off the map edge.
			Assert.That(LeavesTrailsCAMath.SkipsOffMapTrail(false, 0), Is.False);
		}

		[Test]
		public void OffMapRestrictedTrailSkips()
		{
			// A terrain-restricted trail can never match off-map terrain (which cannot be read),
			// so it skips — matching the stock behaviour for those trails.
			Assert.That(LeavesTrailsCAMath.SkipsOffMapTrail(false, 1), Is.True);
			Assert.That(LeavesTrailsCAMath.SkipsOffMapTrail(false, 5), Is.True);
		}
	}
}
