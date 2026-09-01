#region Copyright & License Information
/*
 * WW3MOD DismountGeometry tests — rear-exit direction and dismount fan.
 *
 * User request (2026-09-01): infantry should leave a vehicle from its REAR and fan out left / right /
 * straight back relative to that exit direction.
 *
 * WHAT THESE EXIST TO CATCH, and why they are the primary verification for this change. WAngle is
 * COUNTERCLOCKWISE (0 = North, 256 = WEST, 512 = South, 768 = EAST — conventions.md), which is the opposite
 * of the convention nearly every reader carries in. Getting the sign backwards produces code that compiles,
 * never throws, and reads correctly in a diff — it just puts the squad out through the FRONT of the tank,
 * i.e. the exact defect the change was made to remove. No unit test of the trait wiring would notice; only
 * arithmetic pinned against the engine's own trigonometry does.
 *
 * So the load-bearing test here is CellStep_AgreesWithEngineTrigonometry. DismountGeometry snaps a bearing to
 * a compass step through an integer sector table (deliberately, because WVec.FromSpeedAndAngle rounds through
 * a 1024-scaled cosine table and can leave a stray unit on a component that ought to be zero). That table is
 * a hand-authored claim about which way WAngle turns. The test re-derives every entry from
 * WVec.FromSpeedAndAngle instead, so if the claim is inverted the ENGINE fails the assertion rather than this
 * file agreeing with the source file that shares its author's mistake.
 */
#endregion

using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class DismountGeometryTest
	{
		// The documented compass, straight out of DOCS/reference/conventions.md. Written as literals rather
		// than derived so a change to conventions has to be made deliberately in two places.
		static readonly WAngle North = new WAngle(0);
		static readonly WAngle West = new WAngle(256);
		static readonly WAngle South = new WAngle(512);
		static readonly WAngle East = new WAngle(768);

		[Test]
		public void CellStep_MatchesTheDocumentedCompass()
		{
			Assert.That(DismountGeometry.CellStep(North), Is.EqualTo(new CVec(0, -1)), "WAngle 0 is North, and OpenRA screen space is north = -Y");
			Assert.That(DismountGeometry.CellStep(West), Is.EqualTo(new CVec(-1, 0)), "WAngle 256 is WEST, not east — this is the counterclockwise trap");
			Assert.That(DismountGeometry.CellStep(South), Is.EqualTo(new CVec(0, 1)), "WAngle 512 is South");
			Assert.That(DismountGeometry.CellStep(East), Is.EqualTo(new CVec(1, 0)), "WAngle 768 is EAST, not west");
		}

		[Test]
		public void CellStep_AgreesWithEngineTrigonometry()
		{
			// Re-derive every compass step from the engine's own bearing→vector conversion. conventions.md
			// names WVec.FromSpeedAndAngle as the exact inverse of WVec.Yaw and the sanctioned way to turn a
			// bearing back into a cell step, so this is the authority the hand-written sector table answers to.
			for (var sector = 0; sector < 8; sector++)
			{
				var bearing = new WAngle(sector * DismountGeometry.Octant);
				var v = WVec.FromSpeedAndAngle(1024, bearing);
				var expected = new CVec(System.Math.Sign(v.X), System.Math.Sign(v.Y));

				Assert.That(DismountGeometry.CellStep(bearing), Is.EqualTo(expected),
					$"sector table disagrees with WVec.FromSpeedAndAngle at bearing {bearing.Angle}; " +
					"if this fails the counterclockwise assumption in DismountGeometry is inverted and every " +
					"dismount is coming out of the front of the vehicle");
			}
		}

		[Test]
		public void CellStep_IsExactOnCardinalsRatherThanRoundingIntoADiagonal()
		{
			// The reason CellStep does not simply call FromSpeedAndAngle: a cardinal heading must produce a
			// cardinal step with a genuinely zero component. A diagonal here would send a man out of the
			// corner of the hull instead of straight astern.
			foreach (var cardinal in new[] { North, West, South, East })
			{
				var step = DismountGeometry.CellStep(cardinal);
				Assert.That(step.X == 0 || step.Y == 0, Is.True,
					$"bearing {cardinal.Angle} is cardinal and must not snap to a diagonal cell step");
			}
		}

		[Test]
		public void RearBearing_PointsOutOfTheBackOfTheHull()
		{
			Assert.That(DismountGeometry.RearBearing(North), Is.EqualTo(South), "a hull facing North dismounts to the South");
			Assert.That(DismountGeometry.RearBearing(East), Is.EqualTo(West), "a hull facing East dismounts to the West");
			Assert.That(DismountGeometry.RearBearing(South), Is.EqualTo(North));
			Assert.That(DismountGeometry.RearBearing(West), Is.EqualTo(East));
		}

		[Test]
		public void RearBearing_IsNeverTheFacingItself()
		{
			// The failure mode with teeth: forgetting the half-turn entirely. That compiles, and puts the
			// squad out through the driver's hatch.
			for (var a = 0; a < 1024; a += 16)
			{
				var facing = new WAngle(a);
				Assert.That(DismountGeometry.Separation(DismountGeometry.RearBearing(facing), facing),
					Is.EqualTo(512), $"rear bearing must be exactly half a turn from facing {a}");
			}
		}

		[Test]
		public void FanStep_FirstManGoesStraightBack()
		{
			Assert.That(DismountGeometry.FanStep(North, 0), Is.EqualTo(new CVec(0, 1)),
				"the first man out of a north-facing hull walks due south, i.e. straight astern");
			Assert.That(DismountGeometry.FanStep(East, 0), Is.EqualTo(new CVec(-1, 0)),
				"a hull facing east (WAngle 768) puts its first man out to the west");
		}

		[Test]
		public void FanStep_SecondAndThirdMenSplitToOppositeFlanks()
		{
			// This is the user's "some going left, some going right, some going forward" for a three-man crew.
			var back = DismountGeometry.FanStep(North, 0);
			var one = DismountGeometry.FanStep(North, 1);
			var two = DismountGeometry.FanStep(North, 2);

			Assert.That(one, Is.Not.EqualTo(two), "men 2 and 3 must not walk the same way");
			Assert.That(one, Is.EqualTo(-two), "men 2 and 3 fan to exactly opposite flanks");
			Assert.That(one, Is.Not.EqualTo(back), "man 2 must not follow man 1 down the same lane");
			Assert.That(new[] { back, one, two }, Is.EquivalentTo(new[] { new CVec(0, 1), new CVec(1, 0), new CVec(-1, 0) }),
				"a north-facing hull fans its first three men due south, due east and due west");
		}

		[Test]
		public void FanStep_NeverSendsAnyoneThroughTheFrontOfTheHull()
		{
			// The whole point of the feature, asserted over every facing and every fan slot: no man's walk
			// direction may have a forward component. Separation from the FACING must stay > 90 degrees.
			for (var a = 0; a < 1024; a += 8)
			{
				var facing = new WAngle(a);
				for (var i = 0; i < DismountGeometry.FanCount; i++)
				{
					var bearing = DismountGeometry.FanBearing(DismountGeometry.RearBearing(facing), i);
					Assert.That(DismountGeometry.Separation(bearing, facing), Is.GreaterThanOrEqualTo(256),
						$"facing {a}, fan slot {i}: dismount bearing is within 90 degrees of the hull's nose, " +
						"so this man is walking out of the FRONT of the vehicle");
				}
			}
		}

		[Test]
		public void FanBearing_CyclesAndAcceptsNegativeIndices()
		{
			var rear = DismountGeometry.RearBearing(North);
			Assert.That(DismountGeometry.FanBearing(rear, DismountGeometry.FanCount), Is.EqualTo(DismountGeometry.FanBearing(rear, 0)),
				"the fan repeats once every man in the pattern has been placed");
			Assert.That(DismountGeometry.FanBearing(rear, -1), Is.EqualTo(DismountGeometry.FanBearing(rear, DismountGeometry.FanCount - 1)),
				"a negative index must wrap rather than throw — callers pass raw ordinals");
		}

		[Test]
		public void FanSlots_AreDistinctSoAFullCrewDoesNotStack()
		{
			var steps = Enumerable.Range(0, DismountGeometry.FanCount)
				.Select(i => DismountGeometry.FanStep(North, i))
				.ToArray();

			Assert.That(steps.Distinct().Count(), Is.EqualTo(DismountGeometry.FanCount),
				"every fan slot must resolve to a different cell step, or two men walk into each other");
		}

		[Test]
		public void RearPreference_RanksAsternCellsAheadOfForwardOnes()
		{
			// A hull facing North: the cell to its SOUTH is directly behind it and must sort first; the cell
			// to its NORTH is directly ahead and must sort last.
			Assert.That(DismountGeometry.RearPreference(North, new CVec(0, 1)), Is.EqualTo(0), "due astern is the best exit cell");
			Assert.That(DismountGeometry.RearPreference(North, new CVec(0, -1)), Is.EqualTo(512), "dead ahead is the worst exit cell");

			var astern = DismountGeometry.RearPreference(North, new CVec(0, 1));
			var quarter = DismountGeometry.RearPreference(North, new CVec(1, 1));
			var beam = DismountGeometry.RearPreference(North, new CVec(1, 0));
			var bow = DismountGeometry.RearPreference(North, new CVec(0, -1));

			Assert.That(astern, Is.LessThan(quarter), "astern beats the rear quarter");
			Assert.That(quarter, Is.LessThan(beam), "the rear quarter beats the beam");
			Assert.That(beam, Is.LessThan(bow), "the beam beats the bow");
		}

		[Test]
		public void RearPreference_IsSymmetricAcrossTheHullsAxis()
		{
			// Left and right must rank identically, or the stick would always favour one flank and the fan
			// would read as a lopsided trickle rather than a dismount.
			foreach (var a in new[] { 0, 128, 256, 384, 512, 640, 768, 896 })
			{
				var facing = new WAngle(a);
				var port = DismountGeometry.RearPreference(facing, DismountGeometry.CellStep(DismountGeometry.RearBearing(facing) + 128));
				var starboard = DismountGeometry.RearPreference(facing, DismountGeometry.CellStep(DismountGeometry.RearBearing(facing) + (-128)));
				Assert.That(port, Is.EqualTo(starboard), $"facing {a}: the two rear quarters must rank equally");
			}
		}

		[Test]
		public void RearPreference_SortsAllEightNeighboursIntoARearFirstOrder()
		{
			// The property UnloadCargo actually depends on: ordering the real adjacency ring by this key puts
			// the three rear cells first, so the first three men out take them and the rest spill sideways.
			var neighbours = new[]
			{
				new CVec(0, -1), new CVec(1, -1), new CVec(1, 0), new CVec(1, 1),
				new CVec(0, 1), new CVec(-1, 1), new CVec(-1, 0), new CVec(-1, -1),
			};

			var ordered = neighbours.OrderBy(c => DismountGeometry.RearPreference(North, c)).ToArray();

			Assert.That(ordered[0], Is.EqualTo(new CVec(0, 1)), "a north-facing hull's first exit cell is due south of it");
			Assert.That(ordered.Take(3), Is.EquivalentTo(new[] { new CVec(0, 1), new CVec(1, 1), new CVec(-1, 1) }),
				"the three cells behind a north-facing hull must be the first three chosen");
			Assert.That(ordered[7], Is.EqualTo(new CVec(0, -1)), "the cell in front of the hull is chosen last");
		}

		[Test]
		public void RearPreference_OfTheHullsOwnCellIsWorstSoItIsNeverPreferred()
		{
			Assert.That(DismountGeometry.RearPreference(North, CVec.Zero), Is.EqualTo(512),
				"the hull's own cell has no bearing; it must rank with the bow so the fallback keeps using it last");
		}

		[Test]
		public void CompassStep_CoversTheWholeRingAndWraps()
		{
			var steps = Enumerable.Range(0, DismountGeometry.CompassCount)
				.Select(DismountGeometry.CompassStep)
				.ToArray();

			Assert.That(steps.Distinct().Count(), Is.EqualTo(8), "the fallback compass must have eight distinct headings");
			Assert.That(DismountGeometry.CompassStep(DismountGeometry.CompassCount), Is.EqualTo(DismountGeometry.CompassStep(0)), "the compass wraps");
			Assert.That(DismountGeometry.CompassStep(-1), Is.EqualTo(DismountGeometry.CompassStep(DismountGeometry.CompassCount - 1)), "negative indices wrap");
		}

		[Test]
		public void SectorTable_TilesAFullTurn()
		{
			Assert.That(DismountGeometry.CompassCount * DismountGeometry.Octant, Is.EqualTo(1024),
				"the sector table must tile a full turn exactly, or CellStep indexes out of range near 1024");
		}
	}
}
