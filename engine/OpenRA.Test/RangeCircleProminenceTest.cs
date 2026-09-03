#region Copyright & License Information
/*
 * WW3MOD grouped range circle tests — how the outer envelope of a multi-unit selection is separated from the
 * interior arcs, and when that separate styling is used at all.
 *
 * Range circles are annotations: WorldRenderer.DrawAnnotations runs inside Renderer.BeginUI (Game.cs:910), after
 * the world framebuffer is finished, with no depth test, no shroud and no post-processing. Every circle segment
 * therefore reaches the screen through the same premultiplied BlendMode.Alpha quad, and the ONLY thing that
 * differs between a segment drawn over the map and one drawn over the black beyond-map band is the destination
 * pixel. Blending is dst + a*(src - dst), so an alpha gap of g between envelope and interior lands as
 * g * |src - dst| per channel: maximal against black, and scaled down to nothing against lit terrain.
 *
 * That is the bug these tests pin. Prominence used to be the raw configured alpha (35 by default) against a
 * quarter of it (8), a gap of 27/255 that reads plainly on black and vanishes on ground.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Test
{
	[TestFixture]
	public class RangeCircleProminenceTest
	{
		// RenderRangeCircleInfo.Alpha and WithRangeCircleInfo.Alpha both default to 35, and no shipped actor
		// overrides it for the artillery `explosive` circles where the symptom was reported.
		const int ShippedAlpha = 35;

		[Test]
		public void EnvelopeClearsTheFloorTheConfiguredAlphaDoesNot()
		{
			var (prominent, dim) = RangeCircleGrouping.Prominence(Color.FromArgb(ShippedAlpha, Color.Red));

			// The regression: the gap the eye has to find against terrain used to be 35 - 8.
			Assert.That(prominent.A, Is.EqualTo(120));
			Assert.That(dim.A, Is.EqualTo(8));
			Assert.That(prominent.A - dim.A, Is.GreaterThan(ShippedAlpha));
		}

		[Test]
		public void InteriorArcsAreUntouchedSoTheOffMapLookIsPreserved()
		{
			// Off-map the old rendering was already correct, and it was correct because the interior sat at a
			// quarter alpha against black. Only the envelope moves; anything that changed the interior would
			// change the one view the player already reads correctly.
			foreach (var alpha in new[] { 25, ShippedAlpha, 50, 80 })
			{
				var (_, dim) = RangeCircleGrouping.Prominence(Color.FromArgb(alpha, Color.Red));
				Assert.That(dim.A, Is.EqualTo(alpha / 4), $"interior alpha moved for configured alpha {alpha}");
			}
		}

		[Test]
		public void AConfiguredAlphaAboveTheFloorIsNotPulledDownToIt()
		{
			// The floor lifts a too-quiet circle; it must never quieten a mod author who asked for a loud one.
			var (prominent, _) = RangeCircleGrouping.Prominence(Color.FromArgb(200, Color.Red));
			Assert.That(prominent.A, Is.EqualTo(200));

			var (opaque, _) = RangeCircleGrouping.Prominence(Color.FromArgb(255, Color.Red));
			Assert.That(opaque.A, Is.EqualTo(255));
		}

		[Test]
		public void OnlyAlphaMovesAndTheInteriorNeverReachesZero()
		{
			var color = Color.FromArgb(ShippedAlpha, 12, 34, 56);
			var (prominent, dim) = RangeCircleGrouping.Prominence(color);

			foreach (var c in new[] { prominent, dim })
			{
				Assert.That(c.R, Is.EqualTo(color.R));
				Assert.That(c.G, Is.EqualTo(color.G));
				Assert.That(c.B, Is.EqualTo(color.B));
			}

			// A quarter of a very low alpha rounds to nothing, which would erase the interior rather than dim it.
			var (faintProminent, faintDim) = RangeCircleGrouping.Prominence(Color.FromArgb(1, Color.Red));
			Assert.That(faintDim.A, Is.GreaterThan(0));
			Assert.That(faintDim.A, Is.LessThan(faintProminent.A));
		}

		[Test]
		public void OnlyPeersCloseEnoughToActuallyDimSomethingCount()
		{
			const int Radius = 1000;

			// Reach is our radius plus a peer's dim radius (its own radius plus the 3% boundary margin).
			Assert.That(RangeCircleGrouping.DimRadius(Radius), Is.EqualTo(1030));

			var self = new WPos(0, 0, 0);
			Assert.That(RangeCircleGrouping.CanDim(self, new WPos(2029, 0, 0), Radius), Is.True);
			Assert.That(RangeCircleGrouping.CanDim(self, new WPos(2030, 0, 0), Radius), Is.False);

			// Stacked on the same cell: everything of ours is interior.
			Assert.That(RangeCircleGrouping.CanDim(self, self, Radius), Is.True);
		}

		[Test]
		public void CirclesThatDoNotTouchAreNotAGroup()
		{
			// Two same-range units parked far apart share a RangeCircleType and would otherwise be collected as
			// peers, promoting both rings to the louder envelope alpha while dimming nothing. Selecting a second
			// unit on the far side of the map must not change how the first one's ring looks.
			const int Radius = 1000;
			var self = new WPos(0, 0, 0);
			var farAway = new WPos(0, 8000, 0);

			Assert.That(RangeCircleGrouping.CanDim(self, farAway, Radius), Is.False);
		}

		[Test]
		public void ReachIsMeasuredInTheGroundPlaneOnly()
		{
			// Circles are drawn flat at the actor's radius; a peer sitting on a cliff is the same circle as one
			// beside it on flat ground, so height must not push a genuine overlap out of reach.
			const int Radius = 1000;
			var self = new WPos(0, 0, 0);

			Assert.That(RangeCircleGrouping.CanDim(self, new WPos(1500, 0, 2048), Radius), Is.True);
			Assert.That(RangeCircleGrouping.CanDim(self, new WPos(1500, 0, 0), Radius), Is.True);
		}
	}
}
