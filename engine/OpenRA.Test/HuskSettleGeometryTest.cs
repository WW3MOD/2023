#region Copyright & License Information
/*
 * WW3MOD HuskSettleGeometry tests — which way a wreck points while it finishes the move that killed it.
 *
 * A vehicle killed mid-move is replaced by a husk that keeps sliding to the centre of the cell the living unit had
 * already reserved (Mobile.TopLeft is ToCell, not the cell the body is standing in). That slide is wanted. What is
 * not wanted is the body keeping the facing it died with: on a corner the dying unit is part-way through an arc
 * whose tangent points at the cell it is leaving, while the husk's drag runs straight to the cell it was heading
 * for. Those two directions differ, and the difference renders as a wreck crabbing sideways across the ground.
 *
 * Every assertion here is stated RELATIVE to travel.Yaw rather than against a literal angle. That is deliberate:
 * WAngle is counterclockwise with north at -Y, and a test that hard-coded bearings would be pinning my arithmetic
 * rather than the property, and would read as "correct" if I had the sign backwards in both places.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class HuskSettleGeometryTest
	{
		// A cell-space step. The specific direction is irrelevant — every expectation below is derived from it.
		static readonly WVec Travel = new WVec(1024, -1024, 0);

		[Test]
		public void FacingAlreadyAlongTravel_IsLeftAlone()
		{
			// The common case: a unit dies on a straight leg, where Move has already turned it to face the cell it
			// is entering. There is no crab to correct, so the fix must be a no-op here or it would introduce a
			// visible facing pop at the moment of death on every ordinary kill.
			var deathFacing = Travel.Yaw;
			Assert.That(HuskSettleGeometry.SettleFacing(Travel, deathFacing), Is.EqualTo(Travel.Yaw));
		}

		[Test]
		public void FacingOffTravel_TurnsToFaceTravel()
		{
			// The reported bug. 128 raw units is 45 degrees, the mid-arc offset a cornering vehicle carries.
			var deathFacing = new WAngle(Travel.Yaw.Angle + 128);
			Assert.That(HuskSettleGeometry.SettleFacing(Travel, deathFacing), Is.EqualTo(Travel.Yaw),
				"a wreck part-way through a corner must point along the line it is actually sliding down, not along the arc tangent it died on");
		}

		[Test]
		public void FacingOppositeTravel_StaysReversed()
		{
			// Vehicles here really do drive backwards (^WheeledVehicle sets CanMoveBackward), and a reversing unit's
			// facing is legitimately 180 degrees off its travel. Snapping that to the travel direction would spin the
			// wreck around on death — a new artifact in place of the one being fixed.
			var deathFacing = new WAngle(Travel.Yaw.Angle + 512);
			Assert.That(HuskSettleGeometry.SettleFacing(Travel, deathFacing), Is.EqualTo(new WAngle(Travel.Yaw.Angle + 512)),
				"a wreck that died reversing must keep pointing away from its travel, not rotate 180 degrees");
		}

		[Test]
		public void ExactlyPerpendicular_PrefersForward()
		{
			// A dead tie between forward and reverse. The user's rule is "always only move forward", so forward wins.
			var deathFacing = new WAngle(Travel.Yaw.Angle + 256);
			Assert.That(HuskSettleGeometry.SettleFacing(Travel, deathFacing), Is.EqualTo(Travel.Yaw));
		}

		[Test]
		public void NoTravel_KeepsDeathFacing()
		{
			// A husk with nowhere to drag has no direction of travel to derive a facing from. Deriving one anyway
			// would rotate stationary wrecks to an arbitrary bearing.
			var deathFacing = new WAngle(300);
			Assert.That(HuskSettleGeometry.SettleFacing(WVec.Zero, deathFacing), Is.EqualTo(deathFacing));
		}
	}
}
