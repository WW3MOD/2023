#region Copyright & License Information
/*
 * WW3MOD fires doctrine Phase 1 (gap G1) — continuous bombardment decision test.
 *
 * Pins the pure ContinuousBombardMath so the standing fire-mission assignment can't silently regress:
 *   (1) CELL DISTANCE  — Chebyshev (Rectangular grid), symmetric.
 *   (2) IN-REACH        — the Chebyshev ≤ MaxRangeCells gate; non-positive range never reaches.
 *   (3) WORTHWHILE      — rocket prices the CLUMP numerator, tube prices the SINGLE value, both via the
 *                         shared FiresEconMath.FireWorthy gate (margin + free-weapon short circuit).
 *   (4) COMPARE         — nearer → higher value → lower cell (row-major) → lower ActorID; a total order.
 *   (5) SELECT: nearest-worthwhile-in-reach is chosen; unworthy/out-of-reach earn no mission.
 *   (6) CAP             — per-target pile-on cap; a committed piece keeps its own slot past the cap.
 *   (7) HYSTERESIS      — a committed piece holds its target unless another is closer by > hysteresis, and
 *                         re-tasks when the current target goes invalid.
 *   (8) EV INTERACTION  — a rocket holds on a lone cheap static (clump doesn't repay); a tube fires on it.
 *   (9) DETERMINISM     — reordered pieces/targets give the identical assignment set.
 * Pure math over synthetic inputs; no world mounted.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class ContinuousBombardMathTest
	{
		static ContinuousBombardMath.StaticTarget Target(uint id, int x, int y, int value, int clumpValue)
			=> new(id, x, y, value, clumpValue);

		static ContinuousBombardMath.FiresPiece Piece(uint id, int x, int y, int rangeCells,
			bool rocket = false, int salvoCost = 0, uint currentTargetId = 0)
			=> new(id, x, y, rangeCells, rocket, salvoCost, currentTargetId);

		static ContinuousBombardMath.Assignment For(IReadOnlyList<ContinuousBombardMath.Assignment> list, uint pieceId)
			=> list.First(a => a.PieceId == pieceId);

		// ---- (1) CellDistance -----------------------------------------------------------------------------

		[Test]
		public void CellDistance_IsChebyshevAndSymmetric()
		{
			Assert.That(ContinuousBombardMath.CellDistance(0, 0, 3, 4), Is.EqualTo(4), "max-norm, not Euclidean 5");
			Assert.That(ContinuousBombardMath.CellDistance(3, 4, 0, 0), Is.EqualTo(4), "symmetric");
			Assert.That(ContinuousBombardMath.CellDistance(2, 2, 2, 2), Is.EqualTo(0), "same cell");
			Assert.That(ContinuousBombardMath.CellDistance(-2, 0, 2, 0), Is.EqualTo(4), "negative coords");
		}

		// ---- (2) InReach ----------------------------------------------------------------------------------

		[Test]
		public void InReach_GateAndDegenerateRange()
		{
			var t = Target(1, 5, 0, 100, 100);
			Assert.That(ContinuousBombardMath.InReach(Piece(10, 0, 0, 5), t), Is.True, "exactly at range");
			Assert.That(ContinuousBombardMath.InReach(Piece(10, 0, 0, 4), t), Is.False, "one cell short");
			Assert.That(ContinuousBombardMath.InReach(Piece(10, 0, 0, 0), t), Is.False, "zero range never reaches");
		}

		// ---- (3) Worthwhile -------------------------------------------------------------------------------

		[Test]
		public void Worthwhile_RocketPricesClump_TubePricesSingle()
		{
			// Value (tube numerator) = 100, ClumpValue (rocket numerator) = 300; salvo cost 250, margin 100.
			var t = Target(1, 0, 0, value: 100, clumpValue: 300);

			var rocket = Piece(10, 0, 0, 5, rocket: true, salvoCost: 250);
			var tube = Piece(11, 0, 0, 5, rocket: false, salvoCost: 250);

			// Rocket: 300 >= 250 ⇒ worthy. Tube: 100 < 250 ⇒ NOT worthy at a full salvo cost.
			Assert.That(ContinuousBombardMath.Worthwhile(rocket, t, 100), Is.True, "rocket clump repays");
			Assert.That(ContinuousBombardMath.Worthwhile(tube, t, 100), Is.False, "tube single doesn't repay a 250 salvo");

			// A cheaper tube salvo (cost 80) DOES let a single 100-value static repay — the tube-may-take-singles split.
			var cheapTube = Piece(12, 0, 0, 5, rocket: false, salvoCost: 80);
			Assert.That(ContinuousBombardMath.Worthwhile(cheapTube, t, 100), Is.True, "cheap tube salvo repays on a single");
		}

		[Test]
		public void Worthwhile_FreeWeaponAlwaysWorthy()
		{
			var t = Target(1, 0, 0, value: 1, clumpValue: 1);
			Assert.That(ContinuousBombardMath.Worthwhile(Piece(10, 0, 0, 5, salvoCost: 0), t, 100), Is.True,
				"unpriced ammo ⇒ always worthy (no gate)");
		}

		[Test]
		public void Worthwhile_MarginDemandsSurplus()
		{
			var t = Target(1, 0, 0, value: 250, clumpValue: 250);
			var tube = Piece(10, 0, 0, 5, salvoCost: 250);
			Assert.That(ContinuousBombardMath.Worthwhile(tube, t, 100), Is.True, "value == cost passes at margin 100");
			Assert.That(ContinuousBombardMath.Worthwhile(tube, t, 150), Is.False, "margin 150 demands a 1.5x surplus");
		}

		// ---- (4) CompareCandidate -------------------------------------------------------------------------

		[Test]
		public void CompareCandidate_TieBreakOrder()
		{
			var piece = Piece(10, 0, 0, 20);

			// Nearer wins over farther regardless of value.
			var near = Target(1, 2, 0, value: 1, clumpValue: 1);
			var far = Target(2, 5, 0, value: 999, clumpValue: 999);
			Assert.That(ContinuousBombardMath.CompareCandidate(piece, near, far), Is.LessThan(0), "nearer preferred");

			// Equal distance ⇒ higher value wins.
			var lowVal = Target(3, 0, 3, value: 10, clumpValue: 10);
			var highVal = Target(4, 3, 0, value: 20, clumpValue: 20);
			Assert.That(ContinuousBombardMath.CompareCandidate(piece, highVal, lowVal), Is.LessThan(0), "higher value preferred");

			// Equal distance + value ⇒ lower cell row-major (Y then X).
			var lowerY = Target(5, 3, 1, value: 10, clumpValue: 10);
			var higherY = Target(6, 1, 3, value: 10, clumpValue: 10);
			Assert.That(ContinuousBombardMath.CompareCandidate(piece, lowerY, higherY), Is.LessThan(0), "lower Y preferred");

			// Equal everything but id ⇒ lower id (the deterministic final tie-break).
			var a = Target(7, 3, 0, value: 10, clumpValue: 10);
			var b = Target(8, 3, 0, value: 10, clumpValue: 10);
			Assert.That(ContinuousBombardMath.CompareCandidate(piece, a, b), Is.LessThan(0), "lower id preferred");
			Assert.That(ContinuousBombardMath.CompareCandidate(piece, a, a), Is.EqualTo(0), "identical ⇒ 0");
		}

		// ---- (5) SelectAssignments: nearest worthwhile in reach -------------------------------------------

		[Test]
		public void Select_NearestWorthwhileInReach()
		{
			var pieces = new[] { Piece(10, 0, 0, 6) };
			var targets = new[]
			{
				Target(1, 5, 0, value: 100, clumpValue: 100), // in reach, farther
				Target(2, 2, 0, value: 100, clumpValue: 100), // in reach, NEAREST
				Target(3, 20, 0, value: 999, clumpValue: 999), // out of reach (ignored despite value)
			};

			var result = ContinuousBombardMath.SelectAssignments(pieces, targets, 100, 2, 4);
			var asn = For(result, 10);
			Assert.That(asn.HasTarget, Is.True);
			Assert.That(asn.TargetId, Is.EqualTo(2u), "picks the nearest in-reach worthwhile static");
		}

		[Test]
		public void Select_NoWorthwhileTarget_NoMission()
		{
			// Single lone cheap static; rocket salvo cost 250 ⇒ clump (100) never repays ⇒ no assignment.
			var pieces = new[] { Piece(10, 0, 0, 6, rocket: true, salvoCost: 250) };
			var targets = new[] { Target(1, 2, 0, value: 100, clumpValue: 100) };

			var asn = For(ContinuousBombardMath.SelectAssignments(pieces, targets, 100, 2, 4), 10);
			Assert.That(asn.HasTarget, Is.False, "rocket holds — nothing worthy in reach");
		}

		[Test]
		public void Select_OutOfReach_NoMission()
		{
			var pieces = new[] { Piece(10, 0, 0, 3) };
			var targets = new[] { Target(1, 10, 0, value: 100, clumpValue: 100) };
			Assert.That(For(ContinuousBombardMath.SelectAssignments(pieces, targets, 100, 2, 4), 10).HasTarget, Is.False);
		}

		[Test]
		public void Select_EmptyInputs()
		{
			// Any empty side short-circuits to an empty assignment list (no work, nothing to task).
			var t = new[] { Target(1, 0, 0, 100, 100) };
			var p = new[] { Piece(10, 0, 0, 5) };
			Assert.That(ContinuousBombardMath.SelectAssignments(null, t, 100, 2, 4), Is.Empty, "null pieces");
			Assert.That(ContinuousBombardMath.SelectAssignments(p, null, 100, 2, 4), Is.Empty, "null targets");
			Assert.That(ContinuousBombardMath.SelectAssignments(p, new ContinuousBombardMath.StaticTarget[0], 100, 2, 4),
				Is.Empty, "empty targets");
		}

		// ---- (6) Per-target cap ---------------------------------------------------------------------------

		[Test]
		public void Select_CapLimitsNewPileOn()
		{
			// Three idle pieces, ONE worthy target, cap 2 ⇒ the third piece finds no candidate under the cap.
			var pieces = new[]
			{
				Piece(10, 0, 0, 6),
				Piece(11, 0, 1, 6),
				Piece(12, 0, 2, 6),
			};
			var targets = new[] { Target(1, 3, 0, value: 100, clumpValue: 100) };

			var result = ContinuousBombardMath.SelectAssignments(pieces, targets, 100, maxPiecesPerTarget: 2, retargetHysteresisCells: 4);
			Assert.That(For(result, 10).HasTarget, Is.True, "first fills a slot");
			Assert.That(For(result, 11).HasTarget, Is.True, "second fills the last slot");
			Assert.That(For(result, 12).HasTarget, Is.False, "cap reached ⇒ third gets no mission");
		}

		[Test]
		public void Select_CommittedPieceKeepsSlotPastCap()
		{
			// Two pieces already on target 1 (cap 1). Both keep their slots even though that exceeds the pile-on cap.
			var pieces = new[]
			{
				Piece(10, 0, 0, 6, currentTargetId: 1),
				Piece(11, 0, 1, 6, currentTargetId: 1),
			};
			var targets = new[] { Target(1, 3, 0, value: 100, clumpValue: 100) };

			var result = ContinuousBombardMath.SelectAssignments(pieces, targets, 100, maxPiecesPerTarget: 1, retargetHysteresisCells: 4);
			Assert.That(For(result, 10).TargetId, Is.EqualTo(1u));
			Assert.That(For(result, 11).TargetId, Is.EqualTo(1u), "both committed pieces keep firing past the cap");
		}

		// ---- (7) Re-target hysteresis ---------------------------------------------------------------------

		[Test]
		public void Select_HysteresisHoldsCurrentAgainstMarginallyCloser()
		{
			// Piece is on target 1 (dist 5). Target 2 (dist 3) is closer by 2, hysteresis 4 ⇒ NOT material ⇒ hold.
			var pieces = new[] { Piece(10, 0, 0, 10, currentTargetId: 1) };
			var targets = new[]
			{
				Target(1, 5, 0, value: 100, clumpValue: 100),
				Target(2, 3, 0, value: 100, clumpValue: 100),
			};

			var asn = For(ContinuousBombardMath.SelectAssignments(pieces, targets, 100, 2, retargetHysteresisCells: 4), 10);
			Assert.That(asn.TargetId, Is.EqualTo(1u), "marginally-closer target does not steal a committed piece");
		}

		[Test]
		public void Select_HysteresisSwitchesWhenMateriallyCloser()
		{
			// Piece on target 1 (dist 9). Target 2 (dist 3) is closer by 6 > hysteresis 4 ⇒ switch.
			var pieces = new[] { Piece(10, 0, 0, 12, currentTargetId: 1) };
			var targets = new[]
			{
				Target(1, 9, 0, value: 100, clumpValue: 100),
				Target(2, 3, 0, value: 100, clumpValue: 100),
			};

			var asn = For(ContinuousBombardMath.SelectAssignments(pieces, targets, 100, 2, retargetHysteresisCells: 4), 10);
			Assert.That(asn.TargetId, Is.EqualTo(2u), "materially-closer target re-tasks the piece");
		}

		[Test]
		public void Select_ReTasksWhenCurrentInvalid()
		{
			// Piece was on target 99 (no longer in the belief set / out of reach). Only target 1 remains ⇒ switch to it.
			var pieces = new[] { Piece(10, 0, 0, 6, currentTargetId: 99) };
			var targets = new[] { Target(1, 3, 0, value: 100, clumpValue: 100) };

			var asn = For(ContinuousBombardMath.SelectAssignments(pieces, targets, 100, 2, 4), 10);
			Assert.That(asn.TargetId, Is.EqualTo(1u), "a gone current target re-tasks to the best available");
		}

		// ---- (8) EV interaction: rocket holds, tube fires on the same lone static -------------------------

		[Test]
		public void Select_RocketHolds_TubeFires_OnLoneCheapStatic()
		{
			var targets = new[] { Target(1, 3, 0, value: 100, clumpValue: 100) };

			var rocket = For(ContinuousBombardMath.SelectAssignments(
				new[] { Piece(10, 0, 0, 6, rocket: true, salvoCost: 250) }, targets, 100, 2, 4), 10);
			var tube = For(ContinuousBombardMath.SelectAssignments(
				new[] { Piece(11, 0, 0, 6, rocket: false, salvoCost: 80) }, targets, 100, 2, 4), 11);

			Assert.That(rocket.HasTarget, Is.False, "rocket holds ammo — a lone cheap static isn't a worthy clump");
			Assert.That(tube.HasTarget, Is.True, "tube may shell the single static");
		}

		// ---- (9) Determinism ------------------------------------------------------------------------------

		[Test]
		public void Select_IsOrderIndependent()
		{
			var targets = new[]
			{
				Target(1, 5, 0, value: 100, clumpValue: 100),
				Target(2, 2, 0, value: 100, clumpValue: 100),
				Target(3, 3, 3, value: 100, clumpValue: 100),
			};
			var pieces = new[] { Piece(10, 0, 0, 8), Piece(11, 4, 4, 8) };

			var a = ContinuousBombardMath.SelectAssignments(pieces, targets, 100, 2, 4);
			var b = ContinuousBombardMath.SelectAssignments(
				pieces.Reverse().ToArray(), targets.Reverse().ToArray(), 100, 2, 4);

			foreach (var pid in new uint[] { 10, 11 })
				Assert.That(For(b, pid).TargetId, Is.EqualTo(For(a, pid).TargetId), $"piece {pid} assignment stable under reorder");
		}
	}
}
