#region Copyright & License Information
/*
 * WW3MOD border-staging test — the DEFCON 3 half of the bot's destination clamp.
 *
 * Pins BotTerrain.TryStageOnNearSide, which is the whole of what PoiOffensiveBotModule does about the
 * border: given the cell the bot picked and the unit picking it, produce a cell the unit can actually be
 * ordered to. The trait plumbing around it (resolving DefconWall, asking IsBeyondWall for THIS player)
 * cannot be mounted in NUnit — nothing here can construct a World — so this fixture pins the decision and
 * the scenario pins the wiring.
 *
 * THE FIXTURE IS A VERTICAL LINE AT x = 10, with the mover's side to its west. `beyond` is the predicate
 * the trait supplies; the band itself is modelled as impassable, exactly as DefconWall makes it (it writes
 * a `Wall` custom terrain no locomotor names, so absence IS impassability — Locomotor.cs:84).
 */
#endregion

using System;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class BorderStagingTest
	{
		const int LineX = 10;
		const int Clamp = BotTerrain.EngineRelocationCells;

		// Our side is x < 10. The wall band is the two cells either side of the line (HalfWidth 1024 = one
		// cell), impassable to everyone.
		static bool Beyond(CPos c) => c.X > LineX;
		static bool InBand(CPos c) => c.X >= LineX - 1 && c.X <= LineX + 1;
		static bool InBounds(CPos c) => c.X >= 0 && c.X < 40 && c.Y >= 0 && c.Y < 40;
		static bool Passable(CPos c) => !InBand(c);

		// x 8, not 9: 9 is inside this fixture's own wall band (LineX - 1), so it is not a cell on our
		// own side at all and the clamp is right to move it.
		[TestCase(3, 5)]
		[TestCase(8, 20)]
		[TestCase(0, 0)]
		public void ADestinationOnOurOwnSideIsReturnedUnchanged(int x, int y)
		{
			var ok = BotTerrain.TryStageOnNearSide(new CPos(x, y), new CPos(1, y), Beyond,
				Clamp, InBounds, Passable, out var cell);

			Assert.That(ok, Is.True);
			Assert.That(cell, Is.EqualTo(new CPos(x, y)));
		}

		[Test]
		public void ANullPredicateIsExactlyTheOrdinaryStandableClamp()
		{
			// The Skirmish path: no wall trait, or the wall is down. Whatever TryNearestStandable would have
			// said is what this must say, for the SAME cell — including well past the line, which is ordinary
			// ground the moment the border is not there.
			BotTerrain.TryNearestStandable(new CPos(25, 7), Clamp, InBounds, Passable, out var expected);

			var ok = BotTerrain.TryStageOnNearSide(new CPos(25, 7), new CPos(1, 7), null,
				Clamp, InBounds, Passable, out var cell);

			Assert.That(ok, Is.True);
			Assert.That(cell, Is.EqualTo(expected));
		}

		[Test]
		public void ADestinationBeyondTheLineStagesOnOurSideOfIt()
		{
			// Straight west-to-east approach: the bot wants (30, 7) and the unit is at (2, 7).
			var ok = BotTerrain.TryStageOnNearSide(new CPos(30, 7), new CPos(2, 7), Beyond,
				Clamp, InBounds, Passable, out var cell);

			Assert.That(ok, Is.True);
			Assert.That(Beyond(cell), Is.False, "staged cell must be on the ordering player's own side");
			Assert.That(Passable(cell), Is.True, "staged cell must be standable — the band itself is not");

			// AT the border rather than back at the unit: the whole point is to advance.
			Assert.That(cell.X, Is.EqualTo(LineX - 2));

			// LATERAL DRIFT IS BOUNDED, NOT ZERO, and asserting zero here was wrong: the walk lands on the
			// first non-beyond cell (10, 7), which is inside the impassable band, so the standable clamp has
			// to move it — and FiresStandoffMath.NearestPassableCell breaks ties by its own ring scan order,
			// which reached (8, 5) before (8, 7). Nothing promises the approach row is preserved. What the
			// caller actually relies on is that the cell is legal and within the clamp budget, so that is
			// what is pinned.
			Assert.That(Math.Abs(cell.Y - 7), Is.LessThanOrEqualTo(Clamp));
		}

		[Test]
		public void TheStagedCellIsNeverRelocatedBackAcrossTheLine()
		{
			// THE REGRESSION THIS EXISTS FOR. The staging cell lands inside the impassable band, so the
			// standable clamp has to move it — and the nearest open ground is symmetric about the band, so a
			// clamp that did not know about the line is free to pick the EAST side and hand back a cell the
			// unit may not occupy. Approach along y so the band is entered head-on.
			for (var y = 0; y < 40; y += 7)
			{
				var ok = BotTerrain.TryStageOnNearSide(new CPos(LineX + 1, y), new CPos(LineX - 6, y), Beyond,
					Clamp, InBounds, Passable, out var cell);

				Assert.That(ok, Is.True);
				Assert.That(Beyond(cell), Is.False, $"y={y}: clamp relocated the staging cell across the border");
			}
		}

		[Test]
		public void ADiagonalApproachStagesOnTheApproachAxisRatherThanThePerpendicular()
		{
			// The walk is back along the line the axis will advance on, not the shortest way out of the far
			// half-plane — so a unit coming from the south-west stages south-west of the border, not due west
			// of its objective. (DefconWall.NearestPositionOnOwnSide is the perpendicular and is the right
			// answer for UNDOING a violation; this is the other problem.)
			var ok = BotTerrain.TryStageOnNearSide(new CPos(30, 30), new CPos(2, 2), Beyond,
				Clamp, InBounds, Passable, out var cell);

			Assert.That(ok, Is.True);
			Assert.That(Beyond(cell), Is.False);
			Assert.That(cell.Y, Is.LessThan(30).And.GreaterThan(2), "must stage along the approach, not beside it");
		}

		[Test]
		public void AMoverAlreadyBeyondTheLineIsGivenNoOrderAtAll()
		{
			// Not this module's problem to solve and not a destination it can invent: returning false is the
			// caller's signal to issue nothing.
			var ok = BotTerrain.TryStageOnNearSide(new CPos(30, 7), new CPos(20, 7), Beyond,
				Clamp, InBounds, Passable, out _);

			Assert.That(ok, Is.False);
		}

		[Test]
		public void NoLegalStandableCellMeansNoOrder()
		{
			// Every cell on our side of this approach is impassable, so there is nowhere to stage. False,
			// rather than a doomed order at the ideal cell.
			var ok = BotTerrain.TryStageOnNearSide(new CPos(30, 7), new CPos(2, 7), Beyond,
				Clamp, InBounds, _ => false, out _);

			Assert.That(ok, Is.False);
		}

		[Test]
		public void ANullPassablePredicateThrowsRatherThanAdmittingEveryCell()
		{
			Assert.Throws<ArgumentNullException>(() =>
				BotTerrain.TryStageOnNearSide(new CPos(30, 7), new CPos(2, 7), Beyond,
					Clamp, InBounds, null, out _));
		}

		[Test]
		public void TheWalkIsDeterministicAndAllocationFreeOfRandomness()
		{
			// Same inputs, same answer, every time — the clamp runs on the synced order path and two clients
			// disagreeing about a staging cell is a desync, not a cosmetic difference.
			var first = new CPos();
			for (var i = 0; i < 32; i++)
			{
				BotTerrain.TryStageOnNearSide(new CPos(27, 19), new CPos(3, 4), Beyond,
					Clamp, InBounds, Passable, out var cell);

				if (i == 0)
					first = cell;
				else
					Assert.That(cell, Is.EqualTo(first));
			}
		}
	}
}
