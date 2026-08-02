#region Copyright & License Information
/*
 * WW3MOD influence stack — Stage E ground danger-consumer navigation test.
 *
 * Pins the decisions the Stage-E consumer turns the anti-GROUND danger field into:
 *   (1) PATH EXPOSURE — a route that crosses a believed strongpoint reads hot; one that skirts
 *       it reads safe (the input to "flow around").
 *   (2) DETOUR — a straight approach through a defended core yields a lateral waypoint that lowers
 *       the worst-case exposure; a clear approach yields none (go direct).
 *   (3) REAR-BIAS EMERGENCE — the flagship Stage-E property: against a danger GRADIENT (deep enemy
 *       ground expensive, friendly rear ~0, exactly the Stage-C territory baseline) the detour picks
 *       the SAFER side on strict merit — the rear-lateral route is not scripted, it falls out of cost.
 *   (4) DEPTH BUDGET — a larger MaxSteps lets a high-value mover route deeper into safety when the
 *       near lateral lane is still inside the danger.
 * Plus a determinism guard. Pure math over a synthetic ground-danger sampler; no world mounted.
 */
#endregion

using System;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits.BotModules.Squads;

namespace OpenRA.Test
{
	[TestFixture]
	public class GroundDangerNavTest
	{
		// A radial danger envelope: full intensity at the centre, linear falloff to 0 at the edge, 0
		// beyond — the same shape DangerFieldLayer stamps. Off-envelope cells read a clean 0.
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

		static int WorstOf(CPos from, CPos via, CPos to, Func<CPos, int> g)
		{
			return Math.Max(
				GroundDangerNav.PathMaxGroundDanger(from, via, g),
				GroundDangerNav.PathMaxGroundDanger(via, to, g));
		}

		// Default passability: every cell is standable (the mover-terrain check is exercised separately).
		static readonly Func<CPos, bool> AllPassable = _ => true;

		[Test]
		public void PathThroughStrongpointReadsHot_PathAroundReadsSafe()
		{
			var ground = Envelope(new CPos(10, 10), 5, 100);

			var through = GroundDangerNav.PathMaxGroundDanger(new CPos(0, 10), new CPos(20, 10), ground);
			Assert.That(through, Is.GreaterThan(0), "a route through the strongpoint is exposed");

			var around = GroundDangerNav.PathMaxGroundDanger(new CPos(0, 30), new CPos(20, 30), ground);
			Assert.That(around, Is.EqualTo(0), "a route clear of the strongpoint is safe");
		}

		[Test]
		public void DetourReturnedWhenDirectPathCrossesStrongpoint()
		{
			var ground = Envelope(new CPos(10, 10), 5, 100);
			var from = new CPos(0, 10);
			var to = new CPos(20, 10);

			var direct = GroundDangerNav.PathMaxGroundDanger(from, to, ground);
			var wp = GroundDangerNav.DetourWaypoint(from, to, lateralCells: 8, maxSteps: 2, safeThreshold: 0, ground, AllPassable);

			Assert.That(wp, Is.Not.Null, "a straight path through the strongpoint must yield a detour");
			Assert.That(WorstOf(from, wp.Value, to, ground), Is.LessThan(direct), "the detour lowers worst-case exposure");
		}

		[Test]
		public void NoDetourWhenDirectPathIsSafe()
		{
			var ground = Envelope(new CPos(10, 10), 5, 100);
			var wp = GroundDangerNav.DetourWaypoint(new CPos(0, 30), new CPos(20, 30), 8, 2, 0, ground, AllPassable);
			Assert.That(wp, Is.Null);
		}

		[Test]
		public void DetourPrefersSaferSide_RearRouteEmergesFromGradient()
		{
			// A strongpoint blocks the direct east-west approach, and a territory-baseline GRADIENT makes
			// the +Y side (deep enemy ground) progressively expensive while the -Y side (friendly rear)
			// stays cheap. The detour must therefore lean into -Y — the rear-lateral route is a CONSEQUENCE
			// of the cost, never a scripted heading.
			var strongpoint = Envelope(new CPos(10, 10), 3, 100);
			Func<CPos, int> ground = c =>
			{
				var baseline = c.Y > 10 ? (c.Y - 10) * 8 : 0; // deeper +Y => costlier
				return strongpoint(c) + baseline;
			};

			var from = new CPos(0, 10);
			var to = new CPos(20, 10);
			var wp = GroundDangerNav.DetourWaypoint(from, to, lateralCells: 6, maxSteps: 2, safeThreshold: 0, ground, AllPassable);

			Assert.That(wp, Is.Not.Null, "the blocked approach must detour");
			Assert.That(wp.Value.Y, Is.LessThan(10), "the safer (rear, -Y) side is chosen on merit");
		}

		[Test]
		public void DeeperStepBudgetRoutesFurtherIntoSafety()
		{
			// A WIDE strongpoint: a shallow one-step lateral lane is still inside the danger, so only a
			// deeper budget can find a route that skirts the core — the high-value-mover depth knob.
			var ground = Envelope(new CPos(10, 10), 6, 100);
			var from = new CPos(0, 10);
			var to = new CPos(20, 10);

			var direct = GroundDangerNav.PathMaxGroundDanger(from, to, ground);
			var shallow = GroundDangerNav.DetourWaypoint(from, to, lateralCells: 2, maxSteps: 1, safeThreshold: 0, ground, AllPassable);
			var deep = GroundDangerNav.DetourWaypoint(from, to, lateralCells: 2, maxSteps: 6, safeThreshold: 0, ground, AllPassable);

			var shallowExposure = shallow.HasValue ? WorstOf(from, shallow.Value, to, ground) : direct;
			var deepExposure = deep.HasValue ? WorstOf(from, deep.Value, to, ground) : direct;

			Assert.Multiple(() =>
			{
				Assert.That(deep, Is.Not.Null, "the deeper budget finds a route past the wide strongpoint");
				Assert.That(deepExposure, Is.LessThan(direct), "the deep detour lowers worst-case exposure");
				Assert.That(deepExposure, Is.LessThanOrEqualTo(shallowExposure), "a deeper budget is never worse");
			});
		}

		[Test]
		public void ImpassableCandidateWaypointIsRejected()
		{
			// A strongpoint blocks the direct approach and a gradient makes the -Y (rear) side safer, so
			// unfiltered the detour prefers -Y. Marking -Y ON-MAP IMPASSABLE (water) must force the chosen
			// waypoint onto a standable cell — never the falsely-"safe" (danger 0) water it would prefer.
			var strongpoint = Envelope(new CPos(10, 10), 3, 100);
			Func<CPos, int> ground = c =>
			{
				var baseline = c.Y > 10 ? (c.Y - 10) * 8 : 0; // deeper +Y costlier => -Y is the safer rear
				return strongpoint(c) + baseline;
			};
			var from = new CPos(0, 10);
			var to = new CPos(20, 10);

			var openWp = GroundDangerNav.DetourWaypoint(from, to, 6, 2, 0, ground, AllPassable);
			Assert.That(openWp, Is.Not.Null);
			Assert.That(openWp.Value.Y, Is.LessThan(10), "unfiltered, the safer rear (-Y) side is chosen");

			// Rear (-Y) is now on-map-impassable; any chosen waypoint must be standable.
			Func<CPos, bool> passable = c => c.Y >= 10;
			var wp = GroundDangerNav.DetourWaypoint(from, to, 6, 2, 0, ground, passable);
			Assert.That(wp == null || passable(wp.Value), Is.True, "an on-map-impassable candidate is never chosen");

			// And when EVERY candidate is impassable, there is no lane at all — go direct (null).
			var none = GroundDangerNav.DetourWaypoint(from, to, 6, 2, 0, ground, _ => false);
			Assert.That(none, Is.Null, "no standable candidate => null (caller goes direct)");
		}

		[Test]
		public void NoSafeLaneReturnsNull_CallerGoesDirect()
		{
			// Uniform danger above the threshold: the direct route is exposed but NO lateral candidate
			// lowers the worst-case (every cell is equally hot), so the router gives up and returns null
			// rather than inventing a pointless dogleg — the caller then goes direct.
			Func<CPos, int> ground = _ => 100;
			var wp = GroundDangerNav.DetourWaypoint(new CPos(0, 10), new CPos(20, 10), 8, 3, 0, ground, AllPassable);
			Assert.That(wp, Is.Null);
		}

		[Test]
		public void DecisionsAreDeterministic()
		{
			var ground = Envelope(new CPos(10, 10), 5, 100);
			var from = new CPos(0, 10);
			var to = new CPos(20, 10);

			Assert.Multiple(() =>
			{
				Assert.That(GroundDangerNav.DetourWaypoint(from, to, 8, 2, 0, ground, AllPassable),
					Is.EqualTo(GroundDangerNav.DetourWaypoint(from, to, 8, 2, 0, ground, AllPassable)));
				Assert.That(GroundDangerNav.PathMaxGroundDanger(from, to, ground),
					Is.EqualTo(GroundDangerNav.PathMaxGroundDanger(from, to, ground)));
			});
		}

		// ---------- degenerate / boundary inputs ----------

		[Test]
		public void PathMaxOfZeroLengthPath_IsTheSingleCellSample()
		{
			// from == to: steps = 0, the loop never runs, so the reading is exactly the danger at that one cell.
			var ground = Envelope(new CPos(5, 5), 5, 100);
			Assert.That(GroundDangerNav.PathMaxGroundDanger(new CPos(5, 5), new CPos(5, 5), ground), Is.EqualTo(100));
			Assert.That(GroundDangerNav.PathMaxGroundDanger(new CPos(99, 99), new CPos(99, 99), ground), Is.EqualTo(0));
		}

		[Test]
		public void DetourWithZeroLengthAxis_ReturnsNull_EvenWhenHot()
		{
			// A degenerate from == to inside a hot cell passes the danger>threshold gate but has no axis to
			// offset from (axisLen == 0) ⇒ null (go direct), never a divide-by-zero.
			var ground = Envelope(new CPos(5, 5), 5, 100);
			Assert.That(GroundDangerNav.DetourWaypoint(new CPos(5, 5), new CPos(5, 5), 8, 2, 0, ground, AllPassable),
				Is.Null);
		}

		[Test]
		public void MaxStepsBelowOne_ClampedToOne()
		{
			// maxSteps <= 0 is clamped up to 1, so a mis-configured 0 behaves exactly like a single-step budget
			// rather than emitting no candidates at all.
			var ground = Envelope(new CPos(10, 10), 5, 100);
			var from = new CPos(0, 10);
			var to = new CPos(20, 10);
			Assert.That(GroundDangerNav.DetourWaypoint(from, to, 8, 0, 0, ground, AllPassable),
				Is.EqualTo(GroundDangerNav.DetourWaypoint(from, to, 8, 1, 0, ground, AllPassable)));
		}
	}
}
