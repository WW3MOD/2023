#region Copyright & License Information
/*
 * WW3MOD — pins the two halves of the support-power aim-point snap that can be tested without a
 * World: the candidate ranking, and the damage arithmetic that makes the snap worth doing.
 *
 * WHAT LIVES HERE AND WHAT CANNOT. SupportPowerAimPoint.Resolve needs a World, an ActorMap and a
 * placed building, so the resolution itself is a scenario assertion (test-power-aims-at-center),
 * not a unit test. What IS testable here is the ranking predicate that decides which of several
 * actors on one cell wins, and — more valuable — the shipped RectangleShape arithmetic that turns a
 * corner-cell click into a third of the damage. The second fixture is the reason the feature was
 * filed as a bug fix rather than a convenience.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.HitShapes;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class SupportPowerAimPointTest
	{
		// LOGISTICSCENTER as shipped (mods/ww3mod/rules/ingame/structures.yaml:400-410):
		// HitShape Rectangle TopLeft -1536,-1536 / BottomRight 1536,1536, Building Dimensions 3,3.
		const int HitShapeHalfExtent = 1536;

		// BuildingInfo.CenterOffset for a 3x3 is (CenterOfCell(3,3) - CenterOfCell(1,1)) / 2 =
		// (1024, 1024) — a full cell diagonally (Building.cs:207-210). That is exactly the offset
		// from a corner footprint cell's centre to the building's own centre.
		const int CornerCellOffset = 1024;

		// IskanderExplosion Warhead@Target Damage, the Kinzhal's payload
		// (mods/ww3mod/rules/weapons/weapons-explosions.yaml:522-524).
		const int IskanderTargetDamage = 54000;

		// LOGISTICSCENTER Health.HP (structures.yaml:448-449).
		const int LogisticsCenterHp = 60000;

		static RectangleShape LogisticsCenterShape()
		{
			var shape = new RectangleShape(
				new int2(-HitShapeHalfExtent, -HitShapeHalfExtent),
				new int2(HitShapeHalfExtent, HitShapeHalfExtent));
			shape.Initialize();
			return shape;
		}

		static int ProximityPercentAt(WVec offsetFromCenter)
		{
			return LogisticsCenterShape().CenterProximityPercent(
				new WPos(offsetFromCenter.X, offsetFromCenter.Y, 0), WPos.Zero, WRot.None);
		}

		[TestCase(TestName = "A hit on the actor's centre scales TargetDamage to full")]
		public void CenterHitIsFullDamage()
		{
			Assert.That(ProximityPercentAt(WVec.Zero), Is.EqualTo(100),
				"The aim point the snap produces is the actor's own CenterPosition, which is the " +
				"origin CenterProximityPercent measures from — so it must read 100.");
		}

		[TestCase(TestName = "A hit on a corner footprint cell scales TargetDamage to a third")]
		public void CornerCellHitIsAThird()
		{
			// half-diagonal = |(1536, 1536)| = 2172; offset = |(1024, 1024)| = 1448;
			// 100 * (2172 - 1448) / 2172 = 33.
			var proximity = ProximityPercentAt(new WVec(CornerCellOffset, CornerCellOffset, 0));
			Assert.That(proximity, Is.EqualTo(33));

			// This is the defect in one line: the SAME warhead, on the SAME building, delivers
			// either a kill or well under half depending only on which of nine cells was clicked.
			var cornerDamage = IskanderTargetDamage * proximity / 100;
			Assert.That(IskanderTargetDamage, Is.GreaterThan(LogisticsCenterHp - 7000),
				"A centred Kinzhal is meant to be within its supporting warheads' reach of a kill.");
			Assert.That(cornerDamage, Is.LessThan(LogisticsCenterHp / 2),
				"A corner-cell Kinzhal is not.");
		}

		[TestCase(TestName = "A hit on a mid-edge footprint cell is between the two")]
		public void EdgeCellHitIsBetween()
		{
			// Worth pinning because it disproves the tempting summary "clicking a building's own
			// cell gives 33%". Only the four CORNER cells do; the four edge-midpoints give 52.
			Assert.That(ProximityPercentAt(new WVec(CornerCellOffset, 0, 0)), Is.EqualTo(52));
		}

		[TestCase(TestName = "The bigger footprint wins the cell")]
		public void FootprintDominatesRanking()
		{
			// A 9-cell building beats a 1-cell unit standing on the same cell even when the unit is
			// nearer the click, because the building is what the player was aiming at.
			Assert.That(SupportPowerAimPoint.IsBetterCandidate(9, 2097152, 20, 1, 0, 10), Is.True);
			Assert.That(SupportPowerAimPoint.IsBetterCandidate(1, 0, 10, 9, 2097152, 20), Is.False);
		}

		[TestCase(TestName = "Equal footprints go to the actor nearest the click")]
		public void DistanceBreaksFootprintTies()
		{
			Assert.That(SupportPowerAimPoint.IsBetterCandidate(1, 100, 20, 1, 200, 10), Is.True);
			Assert.That(SupportPowerAimPoint.IsBetterCandidate(1, 200, 10, 1, 100, 20), Is.False);
		}

		[TestCase(TestName = "A full tie goes to the lowest ActorID, not to enumeration order")]
		public void ActorIdBreaksFullTies()
		{
			// ActorMap.GetActorsAt walks an insertion-ordered linked list. Two infantry sharing a
			// cell at equal distance would otherwise be decided by that order, which is a worse
			// thing to depend on than an arbitrary-but-stable rule.
			Assert.That(SupportPowerAimPoint.IsBetterCandidate(1, 100, 5, 1, 100, 9), Is.True);
			Assert.That(SupportPowerAimPoint.IsBetterCandidate(1, 100, 9, 1, 100, 5), Is.False);
		}

		[TestCase(TestName = "A candidate does not beat itself")]
		public void RankingIsStrict()
		{
			// Guards the loop in Resolve: a non-strict predicate would reassign `best` on every
			// equal candidate and hand the result back to enumeration order after all.
			Assert.That(SupportPowerAimPoint.IsBetterCandidate(4, 512, 7, 4, 512, 7), Is.False);
		}
	}
}
