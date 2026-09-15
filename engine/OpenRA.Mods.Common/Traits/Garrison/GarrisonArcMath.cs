#region Copyright & License Information
/*
 * WW3MOD garrison firing-port arc — the one test for "is this direction inside this port's cone".
 *
 * There were two hand-written copies of this, and they did not agree. GarrisonManager
 * .IsTargetInPortArc decides who a port may SHOOT and added the building's own facing to the port
 * yaw; GarrisonPortOccupant.TargetableBy decides who may shoot the man AT that port and used the
 * port yaw raw, with no facing term at all. Same geometry, two answers — so a garrisonable actor
 * with a non-zero facing would have let a soldier fire into one arc while being shootable from a
 * different one.
 *
 * IT WAS INERT WHEN FOUND, and that is the only reason this is a refactor rather than a bug fix: no
 * garrisonable actor carries an IFacing trait today (none of the 38 civilian actors has Mobile,
 * Turreted, Aircraft or Husk, and of the defences only CRAM, AGUN, SAM, HSAM and GUN are Turreted,
 * none of which is garrisonable), so bodyYaw was WAngle.Zero on both paths and the two agreed by
 * accident. Giving any garrison building a facing would have split them silently.
 *
 * The two normalisations were equivalent and the min-of-turns form is kept: WAngle.Angle is always
 * in [0, 1024) (WAngle.cs:28-33), so the smaller of the two turns between the vectors IS the
 * absolute angular distance, and it needs no signed wraparound branch.
 *
 * UNITS. WAngle is 1024 = 360 degrees, and Cone is a HALF-angle each side of the port yaw
 * (GarrisonPortInfo.Cone, whose default of WAngle(512) is a 180-degree half-angle, i.e.
 * omnidirectional). The shipped civilian value of 140 is therefore 49.2 degrees either side, not
 * the ~12 degrees an audit once recorded by reading 1024 as 90 degrees.
 */
#endregion

using System;

namespace OpenRA.Mods.Common.Traits
{
	public static class GarrisonArcMath
	{
		/// <summary>
		/// True when <paramref name="targetYaw"/> lies within <paramref name="cone"/> of the port, where
		/// the port faces <paramref name="bodyYaw"/> + <paramref name="portYaw"/>. All angles are
		/// absolute world yaws; cone is a half-angle each side.
		/// </summary>
		public static bool IsWithinArc(WAngle bodyYaw, WAngle portYaw, WAngle cone, WAngle targetYaw)
		{
			var facing = bodyYaw + portYaw;
			var leftTurn = (facing - targetYaw).Angle;
			var rightTurn = (targetYaw - facing).Angle;

			return Math.Min(leftTurn, rightTurn) <= cone.Angle;
		}
	}
}
