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
using System.Collections.Generic;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common
{
	/// <summary>
	/// <para>The pure half of AutoCaptureNearby: how far a technician will venture for a structure, and
	/// which structure it picks. Kept separate from the trait so both questions can be pinned by NUnit —
	/// nothing in OpenRA.Test can build a World, so anything left inside the trait is untestable.</para>
	///
	/// <para>Zero RNG by construction. Every tie is broken by ActorID, so the choice is identical on every
	/// client without consulting the synced random stream — which this must not do, since the trait ships
	/// enabled and would otherwise shift the stream for control games (conventions.md).</para>
	/// </summary>
	public static class AutoCaptureMath
	{
		/// <summary>A structure the scan is considering. Distance is in world units, not cells.</summary>
		public readonly struct Candidate
		{
			public readonly int Distance;
			public readonly int Value;
			public readonly uint ActorId;

			public Candidate(int distance, int value, uint actorId)
			{
				Distance = distance;
				Value = value;
				ActorId = actorId;
			}
		}

		public const int NoTarget = -1;

		/// <summary>
		/// <para>Whether the unit's FIRE stance permits acting on its own initiative at all.</para>
		///
		/// <para>HoldFire is the player saying "do nothing unless I tell you", and it is the per-unit
		/// off switch for this behaviour — deliberately an existing control the player already
		/// understands rather than a new one. Ambush still captures: it means "do not give away my
		/// position by shooting first", which is a statement about opening fire, and a technician has
		/// nothing to open fire with that matters.</para>
		/// </summary>
		public static bool StancePermitsAutoCapture(UnitStance stance)
		{
			return stance != UnitStance.HoldFire;
		}

		/// <summary>
		/// <para>How far the unit will travel for a structure, graded by ENGAGEMENT stance — the axis that
		/// already means "how far do you roam", orthogonal to the fire stance above.</para>
		///
		/// <para>This is the answer to "Fire at will, or Hunt?": they are not alternatives. UnitStance
		/// (HoldFire/Ambush/FireAtWill) and EngagementStance (HoldPosition/Defensive/Hunt) are separate
		/// enums on AutoTarget, so the fire stance can gate the behaviour while the engagement stance
		/// sizes it. A fresh unit is FireAtWill + Defensive (AutoTarget.cs:75,167), which is why the
		/// behaviour is on by default at the conservative radius.</para>
		///
		/// <para>HoldPosition returns 0 — no radius, ever. "Stay put" has to mean stay put, or the stance
		/// stops being trustworthy for the one job players use it for. AutoFollowAlly takes the same
		/// reading of it.</para>
		/// </summary>
		public static int RadiusCellsForStance(EngagementStance stance, int defensiveCells, int huntCells)
		{
			switch (stance)
			{
				case EngagementStance.HoldPosition:
					return 0;
				case EngagementStance.Hunt:
					return huntCells;
				default:
					return defensiveCells;
			}
		}

		/// <summary>
		/// True when a straight-line distance in world units is inside a radius given in cells. A radius
		/// of 0 or less admits nothing, which is what makes HoldPosition above a real off switch rather
		/// than a very small leash.
		/// </summary>
		public static bool WithinRadius(int distance, int radiusCells)
		{
			if (radiusCells <= 0)
				return false;

			return distance <= radiusCells * 1024;
		}

		/// <summary>
		/// <para>Pick a structure: NEAREST, with VALUE breaking ties when the distances are close enough
		/// not to matter. Returns an index into <paramref name="candidates"/>, or NoTarget when empty.</para>
		///
		/// <para><paramref name="tieBand"/> is what "close enough" means, in world units. Every candidate
		/// within that band of the nearest one is treated as equally near, and the most valuable of them
		/// wins. A band of 0 degrades to pure nearest-first, which is a legitimate configuration and is
		/// why the band is a field rather than a constant.</para>
		///
		/// <para>The band is measured from the NEAREST candidate, not pairwise, because pairwise
		/// "closeness" is not transitive: a chain of structures each within a band of the next would drag
		/// the whole map into one tie group and turn this into pure highest-value, which is precisely the
		/// behaviour the user asked not to have ("not that they go far to find them").</para>
		/// </summary>
		public static int SelectBest(IReadOnlyList<Candidate> candidates, int tieBand)
		{
			ArgumentNullException.ThrowIfNull(candidates);

			if (candidates.Count == 0)
				return NoTarget;

			var nearest = int.MaxValue;
			for (var i = 0; i < candidates.Count; i++)
				if (candidates[i].Distance < nearest)
					nearest = candidates[i].Distance;

			var cutoff = tieBand > 0 ? nearest + tieBand : nearest;

			var best = NoTarget;
			for (var i = 0; i < candidates.Count; i++)
			{
				var c = candidates[i];
				if (c.Distance > cutoff)
					continue;

				if (best == NoTarget)
				{
					best = i;
					continue;
				}

				var b = candidates[best];

				// Value first inside the band, then ActorID. ActorID is the only total tie-break
				// available that does not touch the random stream, so it is what makes the pick
				// identical on every client.
				if (c.Value > b.Value || (c.Value == b.Value && c.ActorId < b.ActorId))
					best = i;
			}

			return best;
		}
	}
}
