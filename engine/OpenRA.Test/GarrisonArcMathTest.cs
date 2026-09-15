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

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// The firing-port arc test, which until 2026-09-15 existed as two hand-written copies that did not
	/// agree: GarrisonManager.IsTargetInPortArc (who a port may SHOOT) added the building's facing to
	/// the port yaw, and GarrisonPortOccupant.TargetableBy (who may shoot the man AT that port) used
	/// the port yaw raw. Inert when found — no garrisonable actor carries an IFacing trait — but it
	/// would have split silently the moment one did.
	///
	/// <para>WAngle is 1024 = 360 degrees and Cone is a HALF-angle each side, so the shipped civilian
	/// Cone of 140 is 49.2 degrees either side, not the ~12 an earlier audit computed by reading 1024
	/// as 90 degrees. The BodyFacingRotatesTheWholeArc case is the one that would have failed before
	/// the two were merged.</para>
	/// </summary>
	[TestFixture]
	public class GarrisonArcMathTest
	{
		static readonly WAngle Cone = new WAngle(140);

		static bool Within(int bodyYaw, int portYaw, int targetYaw)
		{
			return GarrisonArcMath.IsWithinArc(new WAngle(bodyYaw), new WAngle(portYaw), Cone, new WAngle(targetYaw));
		}

		[Test]
		public void DeadAheadIsInside()
		{
			Assert.That(Within(0, 896, 896), Is.True, "a target exactly on the port yaw is outside its own arc.");
		}

		[Test]
		public void TheConeIsAHalfAngleEachSide()
		{
			Assert.That(Within(0, 896, 896 + 140), Is.True, "the far edge of the cone is excluded; Cone is inclusive.");
			Assert.That(Within(0, 896, 896 - 140), Is.True, "the near edge of the cone is excluded.");
			Assert.That(Within(0, 896, 896 + 141), Is.False, "one unit past the cone is still admitted.");
			Assert.That(Within(0, 896, 896 - 141), Is.False, "one unit before the cone is still admitted.");
		}

		[Test]
		public void TheOppositeSideIsOutside()
		{
			// Due south (512) against a north-east port (896): 384 units off a cone of 140. This is the
			// exact geometry test-garrison-port-arc-highpriority is built on.
			Assert.That(Within(0, 896, 512), Is.False,
				"a target 384 units off a 140 cone was admitted — the arc is not narrowing anything, and " +
				"the port-arc scenario's central assertion cannot fail.");
		}

		[Test]
		public void TheArcWrapsThroughZero()
		{
			// A port facing 0 must admit 1010, which is 14 units to its left the short way round — not
			// 1010 units to its right. The signed-wraparound branch this replaced got this right too;
			// the case is here because it is the one a min-of-turns rewrite could plausibly break.
			Assert.That(Within(0, 0, 1010), Is.True, "the arc did not wrap through zero: a target 14 units " +
				"anticlockwise of a port facing 0 was treated as 1010 units away.");

			Assert.That(Within(0, 1010, 0), Is.True, "the same wrap fails in the mirrored direction.");
		}

		[Test]
		public void BodyFacingRotatesTheWholeArc()
		{
			// THE CASE THE TWO COPIES DISAGREED ABOUT. With the building rotated 256 (90 degrees), a port
			// declared at 896 actually faces 896 + 256 = 1152 = 128. GarrisonPortOccupant's copy ignored
			// bodyYaw entirely and would have answered both of these the other way round.
			Assert.That(Within(256, 896, 128), Is.True,
				"the building's facing is not being added to the port yaw, so the arc stays where the YAML " +
				"declared it instead of rotating with the actor. This is the divergence GarrisonArcMath " +
				"was extracted to close.");

			Assert.That(Within(256, 896, 896), Is.False,
				"the unrotated port direction is still being admitted on a rotated building, so bodyYaw is " +
				"being ignored or added with the wrong sign.");
		}

		[Test]
		public void AnOmnidirectionalConeAdmitsEverything()
		{
			// GarrisonPortInfo.Cone defaults to WAngle(512) precisely so an unconfigured port is
			// omnidirectional rather than blind. Guards a rewrite that makes 512 a half-circle exclusive.
			var omni = new WAngle(512);
			for (var yaw = 0; yaw < 1024; yaw += 64)
				Assert.That(GarrisonArcMath.IsWithinArc(WAngle.Zero, WAngle.Zero, omni, new WAngle(yaw)), Is.True,
					$"a 512 half-angle cone rejected yaw {yaw}; the trait default is supposed to mean omni.");
		}
	}
}
