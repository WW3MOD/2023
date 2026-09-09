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

namespace OpenRA.Mods.Common.Graphics
{
	/// <summary>
	/// Angles and directions of the dashes that make up a range circle. The angles are exact integers and the
	/// directions are exact floats: a dash occupies the same arc of the same world circle however the camera is
	/// placed, which is both the property the eye is meant to see and the reason NUnit can pin the whole pattern
	/// without a screen.
	/// </summary>
	public static class RangeCircleGeometry
	{
		public const int FullCircleAngle = 1024;

		/// <summary>
		/// Number of dash+gap steps around the circle. MUST divide <see cref="FullCircleAngle"/> exactly.
		/// This was 96, which does not divide it: the start angles were computed as `i * 1024 / 96` and truncated,
		/// so consecutive dashes started 10 or 11 angle units apart while the dash itself was a fixed 8, leaving
		/// gaps that alternated between 2 and 3 units the whole way round. On a 28c0 circle that is an 8px gap
		/// next to a 12px one — the "breaks at inconsistent angles rather than at a regular dash interval" half
		/// of the reported symptom.
		/// </summary>
		public const int Segments = 128;

		/// <summary>Angle from one dash's start to the next. Exact, by the constraint on <see cref="Segments"/>.</summary>
		public const int Step = FullCircleAngle / Segments;

		/// <summary>Each dash covers 75% of its step, leaving the remaining 25% as the gap.</summary>
		public const int DashArc = Step * 3 / 4;

		public static int DashStartAngle(int i) { return i * Step; }
		public static int DashEndAngle(int i) { return i * Step + DashArc; }

		/// <summary>
		/// Midpoint of the DASH, not of the step. This is the point sampled to decide whether a dash is interior to
		/// a peer circle, and it is the dash — not the gap beside it — that carries the resulting colour.
		/// </summary>
		public static int DashMidAngle(int i) { return i * Step + DashArc / 2; }

		/// <summary>
		/// Unit direction of a <see cref="WAngle"/>, at float precision. Matches what the engine's integer path
		/// approximates — <c>new WVec(r, 0, 0).Rotate(WRot.FromYaw(a))</c> — to within that path's own rounding,
		/// which <c>RangeCircleGeometryTest</c> pins. Note the negated sine: WAngle runs COUNTERCLOCKWISE over
		/// [0, 1024) with 0 pointing north (DOCS/reference/conventions.md), and screen Y runs downwards.
		/// </summary>
		public static float2 Direction(int wangle)
		{
			var radians = 2 * Math.PI * wangle / FullCircleAngle;
			return new float2((float)Math.Cos(radians), -(float)Math.Sin(radians));
		}

		public static readonly float2[] DashStartDir = Exts.MakeArray(Segments, i => Direction(DashStartAngle(i)));
		public static readonly float2[] DashEndDir = Exts.MakeArray(Segments, i => Direction(DashEndAngle(i)));

		// Integer yaw rotations, kept for the interior sample point only. Drawing goes through the float
		// directions above: WRot.AsMatrix builds its matrix from a quaternion whose components are quantised to
		// ten bits, and whose squared length is "theoretically 1024 squared, but may differ slightly due to
		// rounding" (WRot.cs:156), so the rotation is not exactly length-preserving. On a 28c0 circle that put
		// endpoints up to 0.67px off the true radius — a fifth of the 3px line width, and visible as a ring that
		// does not sit round. The interior test does not care: it decides a boolean over a 2.1 degree arc, where
		// a few world units moves a colour boundary by a fraction of one dash.
		static readonly Int32Matrix4x4[] DashMid = Exts.MakeArray(Segments,
			i => new WRot(WAngle.Zero, WAngle.Zero, new WAngle(DashMidAngle(i))).AsMatrix());

		/// <summary>World position sampled by <see cref="IsInterior"/>.</summary>
		public static WPos MidPos(WPos center, int radius, int i)
		{
			return center + new WVec(radius, 0, 0).Rotate(ref DashMid[i]);
		}

		/// <summary>
		/// True when a dash's midpoint falls inside one of the peer circles, meaning this dash is not on the group's
		/// outer envelope and is drawn de-emphasised. Squared distances throughout, in world units.
		/// </summary>
		public static bool IsInterior(WPos mid, (WPos Center, long RadiusSq)[] others)
		{
			for (var j = 0; j < others.Length; j++)
			{
				var dx = (long)(mid.X - others[j].Center.X);
				var dy = (long)(mid.Y - others[j].Center.Y);
				if (dx * dx + dy * dy < others[j].RadiusSq)
					return true;
			}

			return false;
		}
	}
}
