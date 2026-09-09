#region Copyright & License Information
/*
 * WW3MOD range circle geometry tests — the dash pattern of a range ring, and the precision of the projection
 * that puts it on screen.
 *
 * Range rings were reported as cut off at strange places, breaking up beyond their dash pattern, changing as the
 * view moved, and generally not smooth. Those are one defect, not four: the arc was quantised, at two stages.
 *
 * The dominant stage was the projection. Every dash endpoint went through Viewport.WorldToViewPx, which truncates
 * to whole pixels (float2.ToInt2 is (int)X, (int)Y — Primitives/float2.cs:91). Snapping each of 256 endpoints
 * independently pushes each one up to a pixel off the true circle in each axis, so adjacent dashes sit at
 * different radii and the ring reads ragged; where a dash is only a pixel or two long, both ends snap onto the
 * same pixel and RgbaColorRenderer.DrawLine (RgbaColorRenderer.cs:57) divides the delta by a zero length, writes
 * NaN into all four vertices, and the dash never appears at all. The snap is taken against TopLeft and Zoom
 * rather than against the world, so the whole pattern re-rolls as the camera scrolls or the unit moves.
 *
 * The smaller stage was the world-space arc itself: endpoints came from WRot.AsMatrix, whose quaternion is
 * quantised to ten bits and is not exactly length-preserving (WRot.cs:156), which put them up to 0.67px off the
 * radius on a 28c0 circle. Drawing now uses float directions; the integer path survives only for the interior
 * test, which decides a boolean over a 2.1 degree arc and does not care.
 *
 * A third, independent defect rode along: the segment count was 96, which does not divide the 1024-unit circle,
 * so the start angles truncated and the gaps between dashes alternated between 2 and 3 angle units.
 *
 * These tests are deliberately screen-free. The arc and the projection are pure functions, so neither needs a
 * running game — or a human looking at a frame — to pin.
 */
#endregion

using System;
using System.Linq;
using NUnit.Framework;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;

namespace OpenRA.Test
{
	[TestFixture]
	public class RangeCircleGeometryTest
	{
		// mods/ww3mod/mod.yaml:352-354 sets MapGrid TileSize 24,24 and Type Rectangular, so MapGrid.TileScale is
		// 1024 (MapGrid.cs:137) and WorldRenderer.ScreenPosition (WorldRenderer.cs:805) is TileSize * pos / TileScale.
		// Square tiles, so RangeCircleAnnotationRenderable.ScreenFrame's east and south are (k*r, 0) and (0, k*r).
		const float WorldToScreenPx = 24f / 1024f;

		// Viewport.cs:69-97 — WW3MOD leaves unlockMinZoom set, so the floor in normal play is 0.25, not MinZoom.
		const float ZoomFloor = 0.25f;

		// The smallest and largest range circles any shipped actor draws: ^DetectableRangeCircles runs from
		// Detectable10 at 4c0 to Detectable1 at 28c0 (infantry.yaml:832-931). The smallest radius has the shortest
		// dash and is therefore the first to be erased by snapping.
		const int SmallestShippedRadius = 4096;
		const int LargestShippedRadius = 28672;

		static readonly WPos Center = new WPos(30 * 1024, 22 * 1024, 0);
		static readonly int2 Camera = new int2(137, 91);

		static float2 ScreenPosition(WPos pos)
		{
			return new float2(WorldToScreenPx * pos.X, WorldToScreenPx * pos.Y);
		}

		/// <summary>
		/// Projects a dash endpoint the way the renderer does, through the real Viewport.ProjectToViewPx. With
		/// <paramref name="snap"/> set it additionally applies the whole-pixel truncation that WorldToViewPx
		/// applies, which is the defect under test.
		/// </summary>
		static float2 DashEndpoint(float2 dir, int radius, int2 topLeft, float scale, bool snap)
		{
			var screen = ScreenPosition(Center) + WorldToScreenPx * radius * dir;
			var px = Viewport.ProjectToViewPx(screen, topLeft, scale);

			return snap ? new float2((int)px.X, (int)px.Y) : px;
		}

		static float[] DashLengths(int radius, int2 topLeft, float scale, bool snap)
		{
			var lengths = new float[RangeCircleGeometry.Segments];
			for (var i = 0; i < RangeCircleGeometry.Segments; i++)
			{
				var a = DashEndpoint(RangeCircleGeometry.DashStartDir[i], radius, topLeft, scale, snap);
				var b = DashEndpoint(RangeCircleGeometry.DashEndDir[i], radius, topLeft, scale, snap);
				lengths[i] = (b - a).Length;
			}

			return lengths;
		}

		[Test]
		public void TheFloatDirectionsMatchTheEnginesIntegerRotation()
		{
			// Pins the handedness and the negated sine. WAngle runs counterclockwise with 0 at north, and getting
			// that wrong would mirror or spin the whole ring without disturbing any smoothness property below.
			// Compared as an ANGLE: the integer path's error is tangential, so on a 28c0 circle a discrepancy of
			// one WAngle unit is 176 world units of displacement and says nothing about the convention. Three
			// units of slack still leaves a mirrored ring (which lands 2x the angle away) caught at every dash.
			for (var i = 0; i < RangeCircleGeometry.Segments; i++)
			{
				var dir = RangeCircleGeometry.Direction(RangeCircleGeometry.DashMidAngle(i));
				Assert.That(dir.Length, Is.EqualTo(1f).Within(0.00001f), $"dash {i} direction is not a unit vector");

				var integer = RangeCircleGeometry.MidPos(Center, LargestShippedRadius, i) - Center;
				var expected = 2 * Math.PI * RangeCircleGeometry.DashMidAngle(i) / RangeCircleGeometry.FullCircleAngle;
				var delta = Math.Atan2(-integer.Y, (double)integer.X) - expected;
				if (delta > Math.PI)
					delta -= 2 * Math.PI;
				else if (delta < -Math.PI)
					delta += 2 * Math.PI;

				var wangleUnits = Math.Abs(delta) * RangeCircleGeometry.FullCircleAngle / (2 * Math.PI);
				Assert.That(wangleUnits, Is.LessThan(3),
					$"dash {i} points somewhere else than the engine's own rotation does");
			}
		}

		[Test]
		public void EveryDashCoversTheSameArcAndEveryGapTheSame()
		{
			Assert.That(RangeCircleGeometry.FullCircleAngle % RangeCircleGeometry.Segments, Is.Zero,
				"the segment count must divide the circle exactly, or dash starts land on truncated angles");

			var expectedGap = RangeCircleGeometry.Step - RangeCircleGeometry.DashArc;
			for (var i = 0; i < RangeCircleGeometry.Segments; i++)
			{
				var nextStart = i == RangeCircleGeometry.Segments - 1
					? RangeCircleGeometry.FullCircleAngle
					: RangeCircleGeometry.DashStartAngle(i + 1);

				Assert.That(RangeCircleGeometry.DashEndAngle(i) - RangeCircleGeometry.DashStartAngle(i),
					Is.EqualTo(RangeCircleGeometry.DashArc), $"dash {i} covers a different arc from its neighbours");
				Assert.That(nextStart - RangeCircleGeometry.DashEndAngle(i),
					Is.EqualTo(expectedGap), $"the gap after dash {i} differs from the others");
			}
		}

		[Test]
		public void EveryDashEndpointSitsExactlyOnTheCircle()
		{
			// The smoothness property, stated directly: 256 endpoints, all at the same radius from the centre.
			var origin = Viewport.ProjectToViewPx(ScreenPosition(Center), Camera, 1f);
			var expected = WorldToScreenPx * LargestShippedRadius;

			for (var i = 0; i < RangeCircleGeometry.Segments; i++)
			{
				var a = DashEndpoint(RangeCircleGeometry.DashStartDir[i], LargestShippedRadius, Camera, 1f, false);
				var b = DashEndpoint(RangeCircleGeometry.DashEndDir[i], LargestShippedRadius, Camera, 1f, false);

				Assert.That((a - origin).Length, Is.EqualTo(expected).Within(0.01f), $"dash {i} starts off the circle");
				Assert.That((b - origin).Length, Is.EqualTo(expected).Within(0.01f), $"dash {i} ends off the circle");
			}
		}

		[Test]
		public void SnappingToWholePixelsIsWhatPulledEndpointsOffTheCircle()
		{
			// Kept as the record of the defect: the same ring, snapped the way WorldToViewPx snaps it.
			var origin = Viewport.ProjectToViewPx(ScreenPosition(Center), Camera, 1f);
			var expected = WorldToScreenPx * LargestShippedRadius;

			var worst = 0f;
			for (var i = 0; i < RangeCircleGeometry.Segments; i++)
			{
				var a = DashEndpoint(RangeCircleGeometry.DashStartDir[i], LargestShippedRadius, Camera, 1f, true);
				worst = Math.Max(worst, Math.Abs((a - origin).Length - expected));
			}

			Assert.That(worst, Is.GreaterThan(0.5f),
				"expected whole-pixel snapping to pull endpoints visibly off the circle");
		}

		[Test]
		public void NoDashCollapsesToNothingAtTheZoomFloor()
		{
			// A zero-length dash is not a short dash: DrawLine normalises by the length, so it emits NaN vertices
			// and draws nothing. That is the "gaps beyond the intended dash pattern" half of the report.
			foreach (var length in DashLengths(SmallestShippedRadius, Camera, ZoomFloor, false))
				Assert.That(length, Is.GreaterThan(0));
		}

		[Test]
		public void SnappingToWholePixelsIsWhatErasedDashesAtTheZoomFloor()
		{
			var erased = DashLengths(SmallestShippedRadius, Camera, ZoomFloor, true).Count(l => l == 0);

			Assert.That(erased, Is.GreaterThan(0),
				"expected whole-pixel snapping to erase dashes on the smallest shipped circle");
		}

		[Test]
		public void EveryDashIsDrawnTheSameLengthAroundTheRing()
		{
			var lengths = DashLengths(LargestShippedRadius, Camera, 1f, false);

			Assert.That(lengths.Max() - lengths.Min(), Is.LessThan(0.01f),
				"dashes on one ring must all be drawn the same length");
		}

		[Test]
		public void DashesDoNotChangeAsTheViewportScrolls()
		{
			var reference = DashLengths(SmallestShippedRadius, new int2(0, 0), ZoomFloor, false);

			for (var scroll = 1; scroll <= 16; scroll++)
			{
				var moved = DashLengths(SmallestShippedRadius, new int2(scroll, scroll), ZoomFloor, false);
				for (var i = 0; i < reference.Length; i++)
					Assert.That(moved[i], Is.EqualTo(reference[i]).Within(0.001f),
						$"dash {i} changed length when the camera scrolled {scroll}px");
			}
		}

		[Test]
		public void SnappingToWholePixelsIsWhatMadeTheRingShimmerWhileScrolling()
		{
			var reference = DashLengths(SmallestShippedRadius, new int2(0, 0), ZoomFloor, true);

			var changed = 0;
			for (var scroll = 1; scroll <= 16; scroll++)
			{
				var after = DashLengths(SmallestShippedRadius, new int2(scroll, scroll), ZoomFloor, true);
				for (var i = 0; i < reference.Length; i++)
					if (Math.Abs(after[i] - reference[i]) > 0.001f)
						changed++;
			}

			Assert.That(changed, Is.GreaterThan(0),
				"expected the snapped projection to re-roll the dash pattern as the camera scrolls");
		}

		[Test]
		public void TheSameWorldArcProjectsToTheSameDashesAtEveryZoom()
		{
			var atFloor = DashLengths(LargestShippedRadius, Camera, ZoomFloor, false);

			foreach (var zoom in new[] { 0.5f, 1f, 2f, 4f })
			{
				var scaled = DashLengths(LargestShippedRadius, Camera, zoom, false);
				for (var i = 0; i < atFloor.Length; i++)
					Assert.That(scaled[i] / atFloor[i], Is.EqualTo(zoom / ZoomFloor).Within(0.001f),
						$"dash {i} is not the same world arc at zoom {zoom} as at the zoom floor");
			}
		}

		[Test]
		public void OnePeerCircleDimsExactlyOneContiguousArc()
		{
			// "Only the outermost ring in a direction reads strongly" is an ARC property. With a single overlapping
			// peer, the dashes it hides must form one unbroken run — scattered dimmed dashes are precisely what an
			// arc cut off at a strange place looks like from the drawing side.
			var radius = 8 * 1024;
			var peers = new[] { (Center + new WVec(6 * 1024, 0, 0), (long)radius * radius) };

			var interior = new bool[RangeCircleGeometry.Segments];
			for (var i = 0; i < RangeCircleGeometry.Segments; i++)
				interior[i] = RangeCircleGeometry.IsInterior(RangeCircleGeometry.MidPos(Center, radius, i), peers);

			Assert.That(interior.Any(x => x), Is.True, "the peer should hide part of our ring");
			Assert.That(interior.Any(x => !x), Is.True, "the peer should not hide all of our ring");

			// Around a closed ring, exactly one contiguous run means exactly two transitions.
			var transitions = 0;
			for (var i = 0; i < RangeCircleGeometry.Segments; i++)
				if (interior[i] != interior[(i + 1) % RangeCircleGeometry.Segments])
					transitions++;

			Assert.That(transitions, Is.EqualTo(2),
				"the dimmed dashes must form a single arc, not a scatter of breaks");
		}
	}
}
