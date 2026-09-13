#region Copyright & License Information
/*
 * WW3MOD crossing-map invalidation test — the reason CrossingMap.Invalidate exists.
 *
 * WHAT IS PINNED HERE, AND WHAT IS NOT. The defect is a LIFETIME one: the ground component labels are built
 * once behind a one-way flag and the periodic revalidate only re-reads bridge huts, so a DEFCON 3 border
 * standing at build time stays in the labelling after it has come down. The trait half of that (DefconWall
 * calling Invalidate from RaiseWall/LowerWall) needs a World and is therefore verified by reading plus the
 * scenario. What IS pinnable without a world is the CONSEQUENCE — that a one-cell-wide impassable band is
 * enough to split the map into two ground components, and that removing it rejoins them into one — which is
 * the fact that makes a stale labelling matter at all. If that were false there would be nothing to fix.
 *
 * The band is modelled exactly as DefconWall makes it: a vertical line with HalfWidth one cell, written into
 * Map.CustomTerrain as `Wall`, a type no locomotor's TerrainSpeeds names (absence IS impassability —
 * Locomotor.cs:84, :181-182).
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class CrossingMapInvalidationTest
	{
		const int Width = 24;
		const int Height = 16;
		const int LineX = 12;

		// The wall band: one cell either side of the line, per DefconWallInfo.HalfWidth = 1024.
		static bool WalledPassable(int x, int y) => x < LineX - 1 || x > LineX + 1;
		static bool OpenPassable(int x, int y) => true;

		[Test]
		public void TheBorderBandSplitsTheGroundIntoTwoComponents()
		{
			var labels = new int[Width, Height];
			var count = CrossingMapMath.LabelComponents(Width, Height, WalledPassable, labels);

			Assert.That(count, Is.EqualTo(2), "a one-cell band either side of the line must divide the map");

			var west = CrossingMapMath.LabelAt(labels, Width, Height, 2, 8);
			var east = CrossingMapMath.LabelAt(labels, Width, Height, Width - 3, 8);

			Assert.That(west, Is.Not.EqualTo(CrossingMapMath.Impassable));
			Assert.That(east, Is.Not.EqualTo(CrossingMapMath.Impassable));
			Assert.That(west, Is.Not.EqualTo(east), "the two banks must not share a component");
		}

		[Test]
		public void RemovingTheBandRejoinsThemIntoOne()
		{
			// THE POINT OF THE INVALIDATION HOOK. Relabelling after the border comes down gives one component
			// and therefore GroundReach.Same across the old line; NOT relabelling leaves the split above, and
			// every POI on the far bank keeps classifying Unreachable for the rest of the match.
			var labels = new int[Width, Height];
			var count = CrossingMapMath.LabelComponents(Width, Height, OpenPassable, labels);

			Assert.That(count, Is.EqualTo(1));

			var west = CrossingMapMath.LabelAt(labels, Width, Height, 2, 8);
			var east = CrossingMapMath.LabelAt(labels, Width, Height, Width - 3, 8);

			Assert.That(west, Is.EqualTo(east));
		}

		[Test]
		public void TheStaleLabellingIsWhatMisclassifiesAFarBankPoi()
		{
			// End-to-end on the same classification PoiOffensiveBotModule consumes: same component ⇒ Same,
			// different components with no crossing ⇒ Unreachable. This is the pair of answers the hook picks
			// between, stated in the vocabulary the POI scorer actually reads.
			var walled = new int[Width, Height];
			var walledCount = CrossingMapMath.LabelComponents(Width, Height, WalledPassable, walled);
			var effective = CrossingMapMath.EffectiveGroundSets(walledCount, new GroundCrossing[0]);

			var srBank = CrossingMapMath.LabelAt(walled, Width, Height, 2, 8);
			var poiBank = CrossingMapMath.LabelAt(walled, Width, Height, Width - 3, 8);

			Assert.That(CrossingMapMath.SameEffectiveSet(effective, srBank, poiBank), Is.False,
				"while the border stands, a far-bank POI is genuinely unreachable on the ground");

			var open = new int[Width, Height];
			var openCount = CrossingMapMath.LabelComponents(Width, Height, OpenPassable, open);
			var openEffective = CrossingMapMath.EffectiveGroundSets(openCount, new GroundCrossing[0]);

			Assert.That(CrossingMapMath.SameEffectiveSet(openEffective,
					CrossingMapMath.LabelAt(open, Width, Height, 2, 8),
					CrossingMapMath.LabelAt(open, Width, Height, Width - 3, 8)), Is.True,
				"once it is down the same POI is reachable — which is only seen if the labels are rebuilt");
		}
	}
}
