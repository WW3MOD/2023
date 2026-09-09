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

using System.Collections.Generic;

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>
	/// Where a forward-deployed force stands and which way it looks. Pure integer geometry over cell
	/// coordinates and world vectors, so <c>ForwardDeploymentGeometryTest</c> can pin it without a map,
	/// a running game or a human looking at a frame — the same reason RangeCircleGeometry is split out.
	/// </summary>
	public static class ForwardDeploymentGeometry
	{
		/// <summary>
		/// How far toward the enemy the force stands, as a percentage of the distance between the two spawn
		/// points. 35% on the ten shipped maps puts every centre on ground whose support annulus offers at
		/// least 14 spawnable cells to every actor type in every package, on both sides of every spawn pair.
		/// </summary>
		public const int DefaultAdvancePercent = 35;

		/// <summary>
		/// Number of shorter advances tried when the ground at the full advance is too thin. Step 0 is the
		/// player's own spawn point, which reproduces the home deployment exactly — so the worst case of the
		/// search is today's behaviour, never a force that fails to spawn.
		/// </summary>
		public const int DefaultRetreatSteps = 5;

		/// <summary>
		/// Valid cells the annulus must offer EVERY support actor type before an advance is accepted.
		/// 12 is a third of the tightest shipped annulus (squad/platoon place into the single ring at
		/// distance 6, 32 cells) and engages the retreat on exactly the two twin-rivers spawn pairs that
		/// face each other down the same map edge across a cliff line.
		/// </summary>
		public const int DefaultMinValidCells = 12;

		/// <summary>
		/// The point <paramref name="advancePercent"/> of the way from <paramref name="home"/> to
		/// <paramref name="enemyHome"/>. Integer division truncates toward zero, so the result is always a
		/// convex combination of two in-bounds cells and cannot leave the map.
		/// </summary>
		public static CPos AdvancedCenter(CPos home, CPos enemyHome, int advancePercent)
		{
			return home + (enemyHome - home) * advancePercent / 100;
		}

		/// <summary>Advance percentage at <paramref name="step"/> of <paramref name="steps"/>; step 0 is home.</summary>
		public static int AdvanceAtStep(int advancePercent, int steps, int step)
		{
			return advancePercent * step / steps;
		}

		/// <summary>
		/// Facing that looks from <paramref name="from"/> toward <paramref name="to"/>. Takes world positions
		/// rather than cells because cell space and world space only agree on a rectangular grid, and this
		/// lives in the engine. WAngle runs COUNTERCLOCKWISE from north (DOCS/reference/conventions.md), which
		/// is what WVec.Yaw already encodes — hand-rolling the trigonometry here is how the units end up
		/// facing their own rear.
		/// </summary>
		public static WAngle BearingToward(WPos from, WPos to)
		{
			var delta = to - from;

			// Yaw only guards LengthSquared == 0, which a purely vertical delta does not satisfy; a zero
			// horizontal delta would otherwise come back as 768 (east) rather than an honest "no bearing".
			if (delta.X == 0 && delta.Y == 0)
				return WAngle.Zero;

			return new WVec(delta.X, delta.Y, 0).Yaw;
		}

		/// <summary>
		/// Nearest of <paramref name="candidates"/> to <paramref name="home"/>. Ties go to the earlier entry,
		/// so the caller's enumeration order is the tiebreak and the choice is identical on every client.
		/// </summary>
		public static bool TryFindNearest(CPos home, IEnumerable<CPos> candidates, out CPos nearest)
		{
			nearest = home;
			var found = false;
			var best = long.MaxValue;

			foreach (var c in candidates)
			{
				var d = c - home;
				var distanceSquared = (long)d.X * d.X + (long)d.Y * d.Y;
				if (found && distanceSquared >= best)
					continue;

				best = distanceSquared;
				nearest = c;
				found = true;
			}

			return found;
		}
	}
}
