#region Copyright & License Information
/*
 * WW3MOD mounted-transport border-staging test — the DEFCON 3 half of the DELIVERY destination.
 *
 * BorderStagingTest already pins BotTerrain.TryStageOnNearSide itself, and this fixture deliberately does
 * not re-pin it. What is pinned here is the one decision MountedTransportBotModule makes ON TOP of it and
 * that the offensive module does not: WHERE THE WALK IS ANCHORED. The transport anchors it at the delivery
 * lane's fixed origin (the task's Return cell — the Supply Route it set out from) rather than at the
 * carrier's live position, and that choice is the entire churn guard.
 *
 * WHY IT NEEDS A TEST AND NOT A COMMENT. The bug being fixed IS a re-issue loop: the carrier was ordered
 * every scan to a cell Mobile.ResolveOrder refuses (Mobile.cs:1111-1114), and it re-issued forever. A fix
 * that staged against a MOVING anchor would trade that for a quieter loop of the same kind — the crossing
 * point of the segment (objective -> carrier) slides laterally as the carrier converges, so the staged cell
 * changes, so the equality guard in UpdateDeliveryStaging lets another Move through, every scan. The
 * invariance below is what makes that guard bite, and nothing about it is self-evident from reading the
 * call. `TheMovingAnchorTheModuleRejectsWouldNotHaveBeenStable` is the counter-case: it asserts the
 * rejected alternative genuinely drifts, so this fixture fails if someone "simplifies" the anchor back.
 *
 * The trait plumbing (resolving DefconWall, asking IsBeyondWall for THIS player, the Move order itself)
 * cannot be mounted in NUnit — nothing here can construct a World — so the scenario pins the wiring and
 * this pins the decision. Same seam BorderStagingTest states.
 *
 * THE FIXTURE IS A VERTICAL LINE AT x = 10 with our side to its west, matching BorderStagingTest so the
 * two read together. The band is modelled impassable exactly as DefconWall makes it.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class TransportDeliveryStagingTest
	{
		const int LineX = 10;
		const int Clamp = BotTerrain.EngineRelocationCells;

		// The Supply Route the deliveries set out from: the task's Return cell, and the anchor under test.
		static readonly CPos Sr = new CPos(3, 20);

		static bool Beyond(CPos c) => c.X > LineX;
		static bool InBand(CPos c) => c.X >= LineX - 1 && c.X <= LineX + 1;
		static bool InBounds(CPos c) => c.X >= 0 && c.X < 40 && c.Y >= 0 && c.Y < 40;
		static bool Passable(CPos c) => !InBand(c);

		// Exactly what MountedTransportBotModule.TryStageDropForBorder does, with the trait handles the
		// module resolves replaced by this fixture's predicates. `border` null is the wall being down.
		static bool Stage(CPos objective, CPos laneOrigin, bool border, out CPos cell)
		{
			return BotTerrain.TryStageOnNearSide(objective, laneOrigin,
				border ? Beyond : (System.Func<CPos, bool>)null,
				Clamp, InBounds, Passable, out cell);
		}

		[Test]
		public void ADeliveryBeyondTheBorderIsStagedOnOurOwnSideOfIt()
		{
			var objective = new CPos(31, 26);
			Assert.That(Beyond(objective), Is.True, "the fixture's objective must actually be beyond the line");

			var ok = Stage(objective, Sr, true, out var cell);

			Assert.That(ok, Is.True);
			Assert.That(Beyond(cell), Is.False, "a loaded carrier must never be sent through the border");
			Assert.That(Passable(cell), Is.True, "the staged cell must be standable — the band itself is not");
			Assert.That(cell, Is.Not.EqualTo(objective));
		}

		[Test]
		public void TheStagedCellSitsOnTheDeliveryLaneItWillResumeAlong()
		{
			// THE SUBSTANTIVE HALF OF THE ANCHOR CHOICE. Lane-anchored staging does not merely produce SOME
			// legal cell — it produces the cell where the SR -> objective lane meets the border, so the
			// infantry are put down on the line the delivery resumes along the moment the wall lifts, and the
			// carrier's own wandering cannot move it. A carrier-anchored walk satisfies the "not beyond, and
			// standable" assertions above just as well and would pass every other test in this fixture.
			//
			// Bound rather than equality: the walk lands on the first non-beyond cell, which is inside the
			// impassable band, so TryNearestStandable relocates it — laterally, by up to the clamp. What is
			// asserted is that the relocation is a nudge off the lane and not a different lane.
			var objective = new CPos(31, 26);
			Assert.That(Stage(objective, Sr, true, out var cell), Is.True);

			// Where the straight SR -> objective segment crosses the near edge of the band, computed
			// independently of the helper: at x = LineX - 2 the segment has run (8 - 3) / (31 - 3) of its
			// horizontal distance, so y is Sr.Y + 5 * (26 - 20) / 28.
			const int LaneX = LineX - 2;
			var laneY = Sr.Y + ((LaneX - Sr.X) * (objective.Y - Sr.Y) / (objective.X - Sr.X));

			Assert.That(cell.X, Is.EqualTo(LaneX),
				"the staged cell must sit against the border, not back at the Supply Route");
			Assert.That(System.Math.Abs(cell.Y - laneY), Is.LessThanOrEqualTo(Clamp),
				$"staged cell {cell} is off the SR -> objective lane (expected y near {laneY})");
		}

		[Test]
		public void TheMovingAnchorTheModuleRejectsWouldNotHaveBeenStable()
		{
			// The counter-case, and the reason the anchor is a parameter rather than `carrier.Location`.
			// Anchoring the walk at the carrier makes the staged cell the crossing point of a segment with
			// one moving end, and an approach that is not collinear with the objective slides it sideways.
			// If this ever stops drifting the invariance above has become accidental and this fixture is
			// no longer protecting anything — that is what the assertion is for.
			var objective = new CPos(31, 26);
			Assert.That(Stage(objective, Sr, true, out var atSr), Is.True);
			Assert.That(Stage(objective, new CPos(7, 34), true, out var elsewhere), Is.True);

			Assert.That(elsewhere, Is.Not.EqualTo(atSr),
				"a live-position anchor is expected to drift; if it no longer does, re-derive the anchor argument");
		}

		[Test]
		public void TheBorderComingDownRestoresTheObjectiveExactly()
		{
			// THE SELF-HEALING RE-ISSUE, and the whole reason the objective is held apart from the ordered
			// cell. Nothing subscribes to the wall coming down: the staging clamp simply becomes the
			// identity, the recomputed cell differs from the one last ordered, and UpdateDeliveryStaging
			// issues the Move to the real objective by itself on the module's own scan.
			var objective = new CPos(31, 26);
			Assert.That(Stage(objective, Sr, true, out var staged), Is.True);

			Assert.That(Stage(objective, Sr, false, out var opened), Is.True);
			Assert.That(opened, Is.EqualTo(objective), "with the wall down the delivery must resume to its objective");
			Assert.That(opened, Is.Not.EqualTo(staged), "the change is what triggers the re-issue; no change, no resume");
		}

		[Test]
		public void ADeliveryOnOurOwnSideIsUntouchedByTheBorder()
		{
			// The common case even at DEFCON 3: a drop cell that was never beyond the line is ordered
			// exactly as it would have been with no wall at all, so nothing about this fix perturbs a
			// delivery the border was never going to refuse.
			var objective = new CPos(6, 18);
			Assert.That(Beyond(objective), Is.False);

			Assert.That(Stage(objective, Sr, true, out var withWall), Is.True);
			Assert.That(Stage(objective, Sr, false, out var withoutWall), Is.True);

			Assert.That(withWall, Is.EqualTo(objective));
			Assert.That(withoutWall, Is.EqualTo(objective));
		}

		[Test]
		public void TheClampReachesFurtherThanTheArrivalRadiusItIsMeasuredAgainst()
		{
			// Encodes the reasoning PickDropOffCell states and that this fix inherits: the ENGINE relocates
			// a destination by up to EngineRelocationCells, while CarrierState.Delivering only accepts
			// arrival within DropOffArrivalRadius (3 on both shipped twins). Clamping the staged cell with a
			// SHORTER reach than the engine's would hand back a cell the engine then moves the carrier away
			// from, and the arrival test could never pass — which is the original stall, re-entered through
			// the fix. The constant is the guard; if it is ever lowered to the arrival radius, this fails.
			Assert.That(Clamp, Is.GreaterThan(3),
				"the staging clamp must reach at least as far as the engine relocates, or arrival is unmeasurable");
		}
	}
}
