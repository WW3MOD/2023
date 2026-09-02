#region Copyright & License Information
/*
 * WW3MOD — where a counter-battery radar should stand (pure math).
 *
 * Engine-free so it can be pinned in NUnit without mounting a world, same seam as BotTerrain /
 * FiresStandoffMath. The plumbing that consumes it is CounterBatteryRadarBotModule.
 */
#endregion

using System;

namespace OpenRA.Mods.Common.Traits
{
	public static class CounterBatteryRadarMath
	{
		/// <summary>How far forward of its own Supply Route the radar should sit, in cells, given the
		/// distance from that SR to the ground it wants to watch.
		///
		/// <para>TWO CONSTRAINTS, AND THE SMALLER ONE WINS. The radar is an unarmoured 1600-cost vehicle
		/// that is immobile once deployed, so it must stay in the rear —
		/// <paramref name="rearFractionPercent"/> of the way to the front is the hard cap. But a radar
		/// that covers nothing is not worth buying, so it also wants to be at least
		/// (distance - <paramref name="radarCoverageCells"/>) forward, which is the nearest point whose
		/// disc still reaches the front. Taking the MINIMUM means the coverage term can pull it forward
		/// only within the rear band, and never past it.</para>
		///
		/// <para>The three regimes, with the shipped 33% / 42 cells: at distance 30 the SR itself is
		/// already inside coverage so the answer is 0 and the radar deploys where it stands; at 60 the
		/// coverage term (18) binds under the rear cap (20), so it advances just far enough to reach the
		/// front; at 120 the rear cap (40) binds under the coverage term (78), so it takes the furthest
		/// forward position the rear rule allows and covers the near half of the contested band rather
		/// than all of it. PARTIAL COVERAGE IS THE DELIBERATE ANSWER in that third case — the alternative
		/// is parking a specialist in the contested band, where it is a free kill.</para>
		///
		/// <para>Never negative, and 0 is a legitimate answer meaning "deploy at the anchor".</para></summary>
		public static int ForwardOffsetCells(int frontDistanceCells, int rearFractionPercent, int radarCoverageCells)
		{
			if (frontDistanceCells <= 0)
				return 0;

			var rearCap = frontDistanceCells * Math.Max(0, rearFractionPercent) / 100;
			var forCoverage = Math.Max(0, frontDistanceCells - Math.Max(0, radarCoverageCells));

			return Math.Max(0, Math.Min(rearCap, forCoverage));
		}
	}
}
