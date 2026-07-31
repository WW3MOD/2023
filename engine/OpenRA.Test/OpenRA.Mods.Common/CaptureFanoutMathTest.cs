#region Copyright & License Information
/*
 * WW3MOD capture fan-out (@experimental) — distinct-target selection test.
 *
 * Pins the fan-out invariant CaptureCoordinatorBotModule.QueueCaptureOrdersFromPoiMap relies on so N free
 * capturers spread onto N DISTINCT neutral oilbs instead of clustering (the measured 2-TECN→1-oilb waste,
 * WORKSPACE/recon/260731):
 *   (1) an empty in-flight set reproduces "take the top-K distinct in ranked order" (frozen assignment);
 *   (2) targets already claimed by an in-flight capturer are skipped, so a newly-free capturer picks the next
 *       best UNCLAIMED derrick;
 *   (3) the ranked order is preserved and the count is capped at the number of capturers;
 *   (4) duplicate ids collapse to one (defensive — real target lists are already distinct actors).
 * Pure ordered walk; only set membership is queried, so no hash enumeration order feeds the decision.
 */
#endregion

using System.Collections.Generic;
using OpenRA.Mods.Common.Traits;
using NUnit.Framework;

namespace OpenRA.Test
{
	[TestFixture]
	public class CaptureFanoutMathTest
	{
		static List<uint> Select(IReadOnlyList<uint> ordered, IEnumerable<uint> inFlight, int capturers)
			=> CaptureFanoutMath.SelectDistinctTargets(ordered, new HashSet<uint>(inFlight), capturers);

		[Test]
		public void EmptyInFlight_TakesTopKDistinct_InRankedOrder()
		{
			// Frozen assignment: no in-flight claims ⇒ the top-K ranked targets, order preserved.
			var chosen = Select(new uint[] { 10, 20, 30, 40 }, new uint[0], 2);
			Assert.That(chosen, Is.EqualTo(new List<uint> { 10, 20 }));
		}

		[Test]
		public void SkipsTargetsAlreadyClaimedByInFlightCapturers()
		{
			// 10 and 20 are being captured already ⇒ the two free capturers take the next best UNCLAIMED, 30 and 40.
			var chosen = Select(new uint[] { 10, 20, 30, 40, 50 }, new uint[] { 10, 20 }, 2);
			Assert.That(chosen, Is.EqualTo(new List<uint> { 30, 40 }));
		}

		[Test]
		public void CapsAtCapturerCount()
		{
			// Five ranked targets but only three capturers ⇒ the three best, no more.
			var chosen = Select(new uint[] { 1, 2, 3, 4, 5 }, new uint[0], 3);
			Assert.That(chosen, Is.EqualTo(new List<uint> { 1, 2, 3 }));
		}

		[Test]
		public void ZeroOrNegativeCapturers_SelectsNothing()
		{
			Assert.That(Select(new uint[] { 1, 2, 3 }, new uint[0], 0), Is.Empty);
			Assert.That(Select(new uint[] { 1, 2, 3 }, new uint[0], -1), Is.Empty);
		}

		[Test]
		public void CollapsesDuplicateIds()
		{
			// Defensive: a repeated id is chosen once, so two capturers never target the same derrick.
			var chosen = Select(new uint[] { 7, 7, 8 }, new uint[0], 3);
			Assert.That(chosen, Is.EqualTo(new List<uint> { 7, 8 }));
		}

		[Test]
		public void AllTargetsInFlight_SelectsNothing()
		{
			// Every candidate is already being captured ⇒ nothing to fan out to this scan.
			Assert.That(Select(new uint[] { 1, 2, 3 }, new uint[] { 1, 2, 3 }, 4), Is.Empty);
		}
	}
}
