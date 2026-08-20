#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using NUnit.Framework;
using OpenRA.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// MapLayers.IsDetected is the whole of "can this observer see that unit". Concealment and observer
	/// strength are clamped into the SAME 1..VisionLayers-1 range, so the comparison between them is the
	/// only thing standing between the game and units that nothing can ever see. It was strict, and a
	/// target at the top of the range was therefore undetectable at every range by every observer.
	///
	/// These cases pin the two clauses of the predicate independently: the non-strict compare (a match
	/// detects) and the level-1 floor (a merely-explored cell must never detect, because Tick stamps
	/// level 1 on explored ground whether or not anyone is looking).
	/// </summary>
	[TestFixture]
	public class MapLayersDetectionTest
	{
		// The top of the shared ladder: the strongest ^StandardVision band, and the concealment ceiling.
		static readonly int TopLevel = MapLayers.VisionLayers - 1;

		[Test]
		public void MaximallyConcealedUnitIsDetectedByAnObserverStandingOnIt()
		{
			for (var concealment = TopLevel - 1; concealment <= TopLevel; concealment++)
				Assert.That(MapLayers.IsDetected(TopLevel, concealment), Is.True,
					$"A unit concealed at {concealment} must be detected by an observer at the top vision band " +
					"(^StandardVision strength 10, within 4 cells). If this fails, a unit can stand next to an " +
					"enemy and remain invisible.");
		}

		[Test]
		public void MatchingObserverStrengthDetects()
		{
			// The forest case: shadow subtracts from the OBSERVER (MapLayers.AddSource), so a strength-10
			// observer four dense tree cells away contributes 4 against a stopped rifleman's concealment 4.
			Assert.That(MapLayers.IsDetected(4, 4), Is.True,
				"Equal observer strength and concealment must resolve as detected.");
		}

		[Test]
		public void WeakerObserverDoesNotDetect()
		{
			Assert.That(MapLayers.IsDetected(3, 4), Is.False,
				"An observer weaker than the target's concealment must not detect it.");
		}

		[TestCase(1)]
		[TestCase(2)]
		public void MerelyExploredGroundNeverDetects(int concealment)
		{
			// ResolvedVisibility 1 is written for any EXPLORED cell with no live vision source on it, so
			// admitting it would reveal every unit standing on ground the player has ever walked past.
			Assert.That(MapLayers.IsDetected(1, concealment), Is.False,
				$"Resolved visibility 1 means 'explored, nobody looking' as often as it means 'seen', so it " +
				$"must never detect a unit concealed at {concealment}.");
		}

		[Test]
		public void ShroudNeverDetects()
		{
			Assert.That(MapLayers.IsDetected(0, 1), Is.False,
				"Unexplored cells resolve to 0 and must never detect.");
		}
	}
}
