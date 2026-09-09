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
		/// Whether <paramref name="candidate"/> lies inside the salvo's permitted footprint: a disc
		/// of radius <paramref name="maxSpread"/> centred on <paramref name="anchor"/>, which is the
		/// FIRST aim point of the salvo. A zero or negative radius means unbounded.
		/// </summary>
		/// <remarks>
		/// THE ONE DEFINITION OF "TOO FAR", shared by the client that refuses the click and by the
		/// simulation that clamps the order. Two implementations of this predicate would be two
		/// definitions, and they would drift the first time either was tuned; the order generator
		/// asks this question about a cell centre and <see cref="MissileStrikePower"/> asks it about
		/// the same cell centre, so a click the player was allowed to make is never clamped.
		/// <para>EXACT AND INTEGER: the comparison is between squared lengths accumulated in long,
		/// so it neither takes a square root nor overflows. Height is ignored on purpose — the
		/// constraint is a footprint on the ground, and an aim point on a cliff is not further away
		/// for being higher.</para>
		/// </remarks>
		public static bool IsWithinSpread(WPos anchor, WPos candidate, WDist maxSpread)
		{
			if (maxSpread.Length <= 0)
				return true;

			return (candidate - anchor).HorizontalLengthSquared <= (long)maxSpread.Length * maxSpread.Length;
		}

		/// <summary>
		/// <paramref name="candidate"/> pulled back onto the edge of the permitted footprint when it
		/// lies outside it, and returned untouched when it does not.
		/// </summary>
		/// <remarks>
		/// <para>THIS IS THE AUTHORITATIVE HALF. The order generator that refuses the click is
		/// client-local by construction, so an order carrying six aim points spread across the whole
		/// map can still arrive — from a modified client, from a Lua binding, or from a replay
		/// recorded before the bound existed. Clamping rather than rejecting is deliberate: the
		/// shot has already been consumed by the time the order resolves, so dropping the salvo
		/// would spend 18,000 credits on nothing. A warhead thrown as far as the bus can throw it is
		/// the honest degradation.</para>
		///
		/// <para>DETERMINISM. Integer throughout — one ISqrt and one multiply-divide accumulated in
		/// long, no floating point and no RNG — so every client clamps to the same position on the
		/// same tick. The scale-down truncates, so the result can sit up to one world unit inside or
		/// outside the exact radius; callers snap it to a cell afterwards, which is coarser than
		/// that by three orders of magnitude. It is a BOUND, not a precise projection.</para>
		/// </remarks>
		public static WPos ClampToSpread(WPos anchor, WPos candidate, WDist maxSpread)
		{
			if (IsWithinSpread(anchor, candidate, maxSpread))
				return candidate;

			var delta = candidate - anchor;
			var length = delta.HorizontalLength;
			if (length <= 0)
				return candidate;

			var max = maxSpread.Length;
			return anchor + new WVec(
				(int)((long)delta.X * max / length),
				(int)((long)delta.Y * max / length),
				delta.Z);
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
		/// <para>INTEGER ONLY, which is what lets it run on the synced activation path.
		/// <c>i * 1024 / ring</c> divides the circle into equal arcs with integer division for any
		/// ring size; <c>WVec.Rotate</c> is fixed-point. No trigonometry, no floating point, no RNG
		/// — the same inputs give byte-identical offsets on every machine and in every replay.</para>
		///
		/// <para>THE DIVIDEND IS 1024 AND NOT 4096, corrected 2026-09-09. WAngle is 1024 units to
		/// the WHOLE TURN (WAngle.cs), not to the quarter turn as the comment here used to claim, so
		/// <c>i * 4096 / ring</c> stepped FOUR full turns around the ring instead of one. It
		/// survived only because the one shipped value is coprime with its ring: at
		/// <c>count</c> 6 the ring is 5, and 4 and 5 share no factor, so the five bearings came out
		/// distinct — in reverse order, but distinct. At <c>count</c> 3 (ring 2) and <c>count</c> 5
		/// (ring 4) every bearing collapsed onto zero and the whole ring stacked on ONE point, and
		/// at <c>count</c> 7 it collapsed to three bearings in pairs. Both shipped powers use
		/// <c>AimPoints: 6</c>, so the set of positions this returns for them is unchanged; only the
		/// direction the index walks the ring reverses, which is visible solely as the rotational
		/// order of a staggered salvo's arrivals.</para>
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
				offsets[i + 1] = new WVec(spread.Length, 0, 0).Rotate(WRot.FromYaw(new WAngle(i * 1024 / ring)));

			return offsets;
		}
	}
}
