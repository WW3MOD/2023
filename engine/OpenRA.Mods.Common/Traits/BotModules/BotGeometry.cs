#region Copyright & License Information
/*
 * WW3MOD — cell geometry shared by the strategic bot modules.
 *
 * WHY THIS EXISTS AS ONE FUNCTION. ShiftToward was written out twice — privately in
 * EngineerRouteOpenBotModule and again in LayeredDefenceBotModule — with byte-identical bodies, and a
 * third module now needs it. Two identical copies is the state just before the divergence, not evidence
 * that copies stay in agreement: the phantom-anchor class in this codebase was three copies of a grid
 * descent, two of them wrong, found only after it had shipped. The bodies were identical when this was
 * extracted (2026-09-02), so the extraction is provably behaviour-neutral, and keeping them identical is
 * not something a comment can enforce.
 *
 * Same seam as BotTerrain: engine-free, so it can be pinned in NUnit without mounting a world.
 */
#endregion

using System;

namespace OpenRA.Mods.Common.Traits
{
	public static class BotGeometry
	{
		/// <summary>Shift <paramref name="from"/> toward <paramref name="toward"/> by
		/// <paramref name="cells"/> map cells. Nearly-coincident points (a degenerate map layout, or a
		/// unit already standing on its reference) return <paramref name="from"/> unchanged rather than
		/// dividing by a zero length.
		///
		/// <para>The result is a RAW vector cell: it is neither bounds-tested nor terrain-tested, so every
		/// caller must put it through <see cref="BotTerrain.TryNearestStandable"/> before ordering a unit
		/// to it.</para></summary>
		public static CPos ShiftToward(CPos from, CPos toward, int cells)
		{
			var dx = toward.X - from.X;
			var dy = toward.Y - from.Y;
			var len = Math.Sqrt(dx * dx + dy * dy);
			if (len < 1)
				return from;

			var sx = (int)Math.Round(dx / len * cells);
			var sy = (int)Math.Round(dy / len * cells);
			return new CPos(from.X + sx, from.Y + sy);
		}
	}
}
