#region Copyright & License Information
/*
 * WW3MOD influence stack — Stage D helicopter danger-consumer navigation test.
 *
 * Pins the three decisions the Stage-D consumer turns the anti-air danger field into:
 *   (1) PATH EXPOSURE — a flight path that crosses a believed SAM reads hot; one that skirts
 *       it reads safe (the input to "route around").
 *   (2) LEASH — the engage cell snaps to the AA-safe edge nearest a target buried in anti-air,
 *       and is left untouched when the target is already safe (the "fire from the envelope edge,
 *       don't dive in" rule).
 *   (3) DETOUR — a straight approach through a SAM yields a lateral waypoint that lowers the
 *       worst-case exposure; a clear approach yields none (fly direct).
 * Plus the air-aware retreat picking the least-covered ring cell, and a determinism guard.
 * Pure math over a synthetic air-danger sampler; no world mounted.
 */
#endregion

using System;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits.BotModules.Squads;

namespace OpenRA.Test
{
	[TestFixture]
	public class HeliDangerNavTest
	{
		// A radial anti-air envelope: full intensity at the centre, linear falloff to 0 at the edge,
		// 0 beyond — the same shape DangerFieldLayer stamps. Off-envelope cells read a clean 0.
		static Func<CPos, int> Envelope(CPos centre, int radius, int coreIntensity)
		{
			return c =>
			{
				var dx = c.X - centre.X;
				var dy = c.Y - centre.Y;
				var d = Exts.ISqrt(dx * dx + dy * dy);
				if (d > radius)
					return 0;

				return coreIntensity * (radius - d + 1) / (radius + 1);
			};
		}

		[Test]
		public void PathThroughEnvelopeReadsHot_PathAroundReadsSafe()
		{
			var air = Envelope(new CPos(10, 10), 5, 100);

			// Straight across the centre — crosses the hot core.
			var through = HeliDangerNav.PathMaxAirDanger(new CPos(0, 10), new CPos(20, 10), air);
			Assert.That(through, Is.GreaterThan(0), "a path through the SAM is exposed");

			// A lane well clear of the envelope — never enters it.
			var around = HeliDangerNav.PathMaxAirDanger(new CPos(0, 30), new CPos(20, 30), air);
			Assert.That(around, Is.EqualTo(0), "a path clear of the SAM is safe");
		}

		[Test]
		public void LeashSnapsToSafeEdge_ForTargetInsideEnvelope()
		{
			var centre = new CPos(10, 10);
			var air = Envelope(centre, 5, 100);

			var engage = HeliDangerNav.LeashedEngageCell(centre, leashCells: 8, safeThreshold: 0, air);

			Assert.Multiple(() =>
			{
				Assert.That(engage, Is.Not.EqualTo(centre), "must not engage from inside the AA envelope");
				Assert.That(air(engage), Is.LessThanOrEqualTo(0), "leash cell is AA-safe");
				// Within the leash radius (Chebyshev) of the target.
				Assert.That(Math.Max(Math.Abs(engage.X - centre.X), Math.Abs(engage.Y - centre.Y)),
					Is.LessThanOrEqualTo(8), "leash cell stays within reach of the target");
			});
		}

		[Test]
		public void LeashLeavesSafeTargetUntouched()
		{
			var air = Envelope(new CPos(10, 10), 5, 100);
			var safeTarget = new CPos(40, 40);
			Assert.That(HeliDangerNav.LeashedEngageCell(safeTarget, 8, 0, air), Is.EqualTo(safeTarget));
		}

		[Test]
		public void LeashFallsBackToTargetWhenNoSafeCellInReach()
		{
			// A huge envelope with a tiny leash radius: no safe cell reachable, so we return the target
			// unchanged and let the withdraw-on-spike / hot-target guards take over.
			var centre = new CPos(10, 10);
			var air = Envelope(centre, 30, 100);
			Assert.That(HeliDangerNav.LeashedEngageCell(centre, leashCells: 2, safeThreshold: 0, air), Is.EqualTo(centre));
		}

		[Test]
		public void DetourReturnedWhenDirectPathCrossesAa()
		{
			var air = Envelope(new CPos(10, 10), 5, 100);
			var from = new CPos(0, 10);
			var to = new CPos(20, 10);

			var direct = HeliDangerNav.PathMaxAirDanger(from, to, air);
			var wp = HeliDangerNav.DetourWaypoint(from, to, lateralCells: 8, safeThreshold: 0, air);

			Assert.That(wp, Is.Not.Null, "a straight path through the SAM must yield a detour");

			var rerouted = Math.Max(
				HeliDangerNav.PathMaxAirDanger(from, wp.Value, air),
				HeliDangerNav.PathMaxAirDanger(wp.Value, to, air));
			Assert.That(rerouted, Is.LessThan(direct), "the detour lowers worst-case exposure");
		}

		[Test]
		public void NoDetourWhenDirectPathIsSafe()
		{
			var air = Envelope(new CPos(10, 10), 5, 100);
			// A path that never enters the envelope needs no detour.
			var wp = HeliDangerNav.DetourWaypoint(new CPos(0, 30), new CPos(20, 30), 8, 0, air);
			Assert.That(wp, Is.Null);
		}

		[Test]
		public void RetreatPicksLeastCoveredRingCell()
		{
			// Envelope sits to the +X side of the origin; the safest ring cell must lean away from it.
			var origin = new CPos(10, 10);
			var air = Envelope(new CPos(16, 10), 5, 100);

			var retreat = HeliDangerNav.SafestAirCellOnRing(origin, ringCells: 4, air);

			Assert.Multiple(() =>
			{
				Assert.That(air(retreat), Is.EqualTo(0), "retreat heads to an AA-clear cell");
				Assert.That(retreat.X, Is.LessThanOrEqualTo(origin.X), "retreat leans away from the SAM");
			});
		}

		[Test]
		public void DecisionsAreDeterministic()
		{
			var air = Envelope(new CPos(10, 10), 5, 100);
			var from = new CPos(0, 10);
			var to = new CPos(20, 10);

			Assert.Multiple(() =>
			{
				Assert.That(HeliDangerNav.DetourWaypoint(from, to, 8, 0, air),
					Is.EqualTo(HeliDangerNav.DetourWaypoint(from, to, 8, 0, air)));
				Assert.That(HeliDangerNav.LeashedEngageCell(new CPos(10, 10), 8, 0, air),
					Is.EqualTo(HeliDangerNav.LeashedEngageCell(new CPos(10, 10), 8, 0, air)));
				Assert.That(HeliDangerNav.SafestAirCellOnRing(from, 4, air),
					Is.EqualTo(HeliDangerNav.SafestAirCellOnRing(from, 4, air)));
			});
		}

		// ---------- degenerate / boundary inputs ----------

		[Test]
		public void PathMaxOfZeroLengthFlight_IsTheSingleCellSample()
		{
			// from == to: steps = 0, so the reading is exactly the air-danger at that one cell.
			var air = Envelope(new CPos(10, 10), 5, 100);
			Assert.That(HeliDangerNav.PathMaxAirDanger(new CPos(10, 10), new CPos(10, 10), air), Is.EqualTo(100));
			Assert.That(HeliDangerNav.PathMaxAirDanger(new CPos(99, 99), new CPos(99, 99), air), Is.EqualTo(0));
		}

		[Test]
		public void DetourWithZeroLengthAxis_ReturnsNull_EvenWhenHot()
		{
			// from == to inside the SAM: passes the danger gate but has no axis (axisLen == 0) ⇒ null,
			// never a divide-by-zero.
			var air = Envelope(new CPos(10, 10), 5, 100);
			Assert.That(HeliDangerNav.DetourWaypoint(new CPos(10, 10), new CPos(10, 10), 8, 0, air), Is.Null);
		}

		[Test]
		public void LeashZeroRadius_UnsafeTarget_FallsBackToTarget()
		{
			// leashCells == 0: the expanding-ring loop never runs, so an unsafe target is returned unchanged
			// (the withdraw guards handle it) — pins the empty-search fallback.
			var centre = new CPos(10, 10);
			var air = Envelope(centre, 5, 100);
			Assert.That(HeliDangerNav.LeashedEngageCell(centre, leashCells: 0, safeThreshold: 0, air), Is.EqualTo(centre));
		}

		[Test]
		public void LeashTieBreak_PicksTopLeftNeighbourFirst()
		{
			// Target hot, every neighbour safe: the fixed dy-then-dx scan at r=1 reaches (X-1, Y-1) first, so
			// that corner is the deterministic winner among the 8 equally-safe cells. Pins WHICH cell wins.
			var target = new CPos(10, 10);
			Func<CPos, int> air = c => c == target ? 100 : 0;
			Assert.That(HeliDangerNav.LeashedEngageCell(target, leashCells: 4, safeThreshold: 0, air),
				Is.EqualTo(new CPos(9, 9)));
		}

		[Test]
		public void SafestRingBelowRadiusOne_ReturnsOrigin()
		{
			// ringCells < 1 has no ring to scan ⇒ the origin is returned (the caller stays put).
			var air = Envelope(new CPos(10, 10), 5, 100);
			Assert.That(HeliDangerNav.SafestAirCellOnRing(new CPos(3, 3), 0, air), Is.EqualTo(new CPos(3, 3)));
			Assert.That(HeliDangerNav.SafestAirCellOnRing(new CPos(3, 3), -2, air), Is.EqualTo(new CPos(3, 3)));
		}

		[Test]
		public void SafestRingTieBreak_PicksFirstScannedCellOnUniformField()
		{
			// A uniform field (every ring cell equal) resolves to the FIRST cell in scan order — dy=-r then
			// dx=-r, i.e. the (X-r, Y-r) corner — because only a strictly-lower reading displaces it. Pins the
			// tie-break so a uniform retreat is deterministic rather than arbitrary.
			Func<CPos, int> flat = _ => 50;
			var origin = new CPos(10, 10);
			Assert.That(HeliDangerNav.SafestAirCellOnRing(origin, ringCells: 2, flat), Is.EqualTo(new CPos(8, 8)));
		}
	}
}
