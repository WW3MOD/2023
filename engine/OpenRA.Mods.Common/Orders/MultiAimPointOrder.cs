#region Copyright & License Information
/*
 * WW3MOD — wire format for a support power aimed at SEVERAL points in one order.
 *
 * THE PROBLEM. An Order carries exactly one Target (Order.cs:63), and the user's MIRV needs six aim
 * points issued as ONE order — six separate orders would be six separate SupportPowerInstance
 * activations, and the second one would find the magazine empty.
 *
 * THE ANSWER, and it is not a new one: ride in Order.TargetString, which is a first-class wire
 * field (the flag is set whenever the string is non-null, Order.cs:388-389; written at 462-463;
 * read back at 179). PatrolOrder already does exactly this for a route of N cells, so this file is
 * deliberately the same shape as PatrolOrder.SerializeWaypoints/DeserializeWaypoints rather than a
 * cleverer one. It is a SEPARATE class rather than a call into PatrolOrder because the two
 * contracts differ at the receiving end — a patrol clamps and walks, a strike clamps and TRUNCATES
 * to the power's warhead count — and a shared decoder would have to grow a mode flag to say which.
 *
 * DETERMINISM. Integers only, no floats, no culture: ToStringInvariant out, TryParseInt32Invariant
 * back (Exts.cs:561, 581). A CPos list encodes and decodes byte-identically on every client
 * regardless of locale, which is the property that lets this ride the synced order path at all.
 */
#endregion

using System.Collections.Generic;
using System.Text;

namespace OpenRA.Mods.Common.Orders
{
	/// <summary>
	/// Encodes and decodes the list of aim points a multi-target support power order carries in
	/// <see cref="Order.TargetString"/>.
	/// </summary>
	public static class MultiAimPointOrder
	{
		/// <summary>Encodes aim points as "x,y,x,y,…".</summary>
		public static string Serialize(IReadOnlyList<CPos> points)
		{
			if (points == null || points.Count == 0)
				return null;

			var sb = new StringBuilder();
			for (var i = 0; i < points.Count; i++)
			{
				if (i > 0)
					sb.Append(',');

				sb.Append(points[i].X.ToStringInvariant());
				sb.Append(',');
				sb.Append(points[i].Y.ToStringInvariant());
			}

			return sb.ToString();
		}

		/// <summary>
		/// Decodes an aim point list, or returns null when the string is absent or malformed.
		/// </summary>
		/// <remarks>
		/// Null is the SINGLE-TARGET signal, not an error to report. A bot issues this power's order
		/// with no TargetString at all (SupportPowerBotModule.cs:114-116), and so does any Lua
		/// binding; the caller falls back to Order.Target in that case rather than dropping the
		/// order. Malformed input reaches the same place for the same reason — a strike that lands
		/// on the one point everybody agrees on beats a strike that silently does not happen.
		/// </remarks>
		public static CPos[] Deserialize(string encoded)
		{
			if (string.IsNullOrEmpty(encoded))
				return null;

			var parts = encoded.Split(',');
			if (parts.Length < 2 || parts.Length % 2 != 0)
				return null;

			var points = new CPos[parts.Length / 2];
			for (var i = 0; i < points.Length; i++)
			{
				if (!Exts.TryParseInt32Invariant(parts[2 * i], out var x) ||
					!Exts.TryParseInt32Invariant(parts[2 * i + 1], out var y))
					return null;

				points[i] = new CPos(x, y);
			}

			return points;
		}

		/// <summary>
		/// Offsets from a single aim point for a power that delivers <paramref name="count"/>
		/// warheads but received a one-point order. Index 0 is always zero — the first warhead lands
		/// exactly where the sender pointed — and the rest sit on a ring of radius
		/// <paramref name="spread"/> at even angles.
		/// </summary>
		/// <remarks>
		/// <para>WHY IT EXISTS. A bot picks one location and knows nothing about placement mode
		/// (SupportPowerBotModule.cs:114-116), and so does a Lua binding. Without this a bot's
		/// six-warhead MIRV would put all six on one cell — which is the exact complaint the
		/// player-aimed mode was built to fix, so leaving the bot on that path would fix the
		/// symptom for one side of the match only.</para>
		///
		/// <para>INTEGER ONLY, which is what lets it run on the synced activation path. WAngle is
		/// 1024 per quarter turn, so <c>i * 4096 / ring</c> divides the circle into equal arcs with
		/// integer division for any ring size; <c>WVec.Rotate</c> is fixed-point. No trigonometry,
		/// no floating point, no RNG — the same inputs give byte-identical offsets on every machine
		/// and in every replay.</para>
		/// </remarks>
		public static WVec[] FallbackRingOffsets(int count, WDist spread)
		{
			if (count < 1)
				count = 1;

			var offsets = new WVec[count];
			offsets[0] = WVec.Zero;

			if (count == 1 || spread.Length <= 0)
				return offsets;

			var ring = count - 1;
			for (var i = 0; i < ring; i++)
				offsets[i + 1] = new WVec(spread.Length, 0, 0).Rotate(WRot.FromYaw(new WAngle(i * 4096 / ring)));

			return offsets;
		}
	}
}
