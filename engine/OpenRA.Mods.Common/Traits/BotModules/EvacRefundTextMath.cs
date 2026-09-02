#region Copyright & License Information
/*
 * WW3MOD evacuation refund indicator — where the "+$N" floats and for how long (pure math).
 *
 * WHY THIS EXISTS: the refund tick for an evacuated unit was spawned at the actor's CenterPosition at the moment of
 * sale, and a successful evacuation ends OUTSIDE the playable area by construction — a ground unit is dragged
 * GroundOffMapCells past the boundary and an aircraft flies AircraftOffMapCells past it (RotateToEdge). Both of
 * FloatingText's gates resolve that position through MapLayers, and both answer "hidden" for anything out of bounds:
 * IsExplored(MPos) returns false when !map.Contains(uv) (MapLayers.cs:504-505), and IsVisible(PPos) returns
 * map.Contains(puv) outright when fog is off (MapLayers.cs:576-577). So the indicator was suppressed for the
 * COMPLETED evacuation and survived only on the fallback paths that sell in place — which is the reported
 * "usually there is a $1000, but sometimes I get nothing", with the two cases the other way round from how it reads.
 *
 * WHY CLAMPING RATHER THAN MOVING THE SPAWN: the clamp is the IDENTITY for every position already inside the
 * bounds, so no currently-visible tick moves by a single world unit — only the ones that were invisible are
 * rescued. Anchoring instead to, say, the order position would have shifted the working cases too.
 *
 * DETERMINISM: pure integer arithmetic, no random draws, no collection iteration. Presentational either way — the
 * refund amount and the sale itself are decided in RotateToEdge.DoSell and are not touched here.
 */
#endregion

using System;

namespace OpenRA.Mods.Common.Traits
{
	public static class EvacRefundTextMath
	{
		/// <summary>Lifetime of the evacuation refund tick, in ticks. At the default game speed
		/// (<c>Timestep: 60</c> ms, mod.yaml:358 + :382 ⇒ 16.67 ticks/s) this is 4.5 s, up from the shared
		/// 30-tick default's 1.8 s — the "animate it a bit slower/longer so it is easier to read" ask.</summary>
		public const int TickLifetime = 75;

		/// <summary>Rise per tick for that longer lifetime, in world units. Chosen so the text drifts the same
		/// TOTAL distance as the 30-tick default did, just 2.5x more slowly: a tick that lived 2.5x longer at the
		/// unchanged 86/tick would climb ~6.3 cells and walk off the top of the viewport, which at the map edge
		/// (where every evacuation ends) is where the viewport is most likely to be clipped already.</summary>
		public const int RiseRate = 34;

		/// <summary>The 30-tick, 86-per-tick default every other <c>FloatingText</c> caller still uses. Kept here
		/// only so the drift-parity intent above is checkable in a test rather than asserted in a comment.</summary>
		public const int DefaultTickLifetime = 30;

		/// <summary>Total world units risen over a tick's whole life.</summary>
		public static int TotalRise(int ticks, int riseRate)
		{
			return ticks * riseRate;
		}

		/// <summary><para>Clamp a map cell's (U, V) into the playable bounds, so a position that ended up outside
		/// them resolves to the nearest cell the shroud and fog layers can actually answer for.</para>
		///
		/// <para><paramref name="right"/> and <paramref name="bottom"/> are EXCLUSIVE, matching
		/// <c>Rectangle.Right</c>/<c>Bottom</c> and therefore <c>Map.Bounds</c>, so the last legal cell is one less
		/// than each. Degenerate bounds (zero or negative width/height, reachable only from a malformed map) collapse
		/// to the top-left corner instead of throwing, because the caller is mid-sale and must not fault.</para></summary>
		public static (int U, int V) ClampToBounds(int u, int v, int left, int top, int right, int bottom)
		{
			var maxU = Math.Max(left, right - 1);
			var maxV = Math.Max(top, bottom - 1);
			return (Math.Clamp(u, left, maxU), Math.Clamp(v, top, maxV));
		}
	}
}
