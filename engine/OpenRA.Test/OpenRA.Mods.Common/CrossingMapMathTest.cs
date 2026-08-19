#region Copyright & License Information
/*
 * WW3MOD crossing-map math test — frontline-influence Phase 0.
 *
 * Pins the pure terrain/reachability model on a synthetic River-Zeta-shaped fixture:
 *   - a river (rows 4-5) splits the land into a NORTH bank and a SOUTH bank ⇒ 2 ground components;
 *   - 2 central bridges enumerated as INTACT crossings (fold the banks into one effective set);
 *   - 2 flank bridges enumerated as REPAIRABLE (destroyed) crossings (do NOT fold — potential only);
 *   - the amphibious locomotor traverses water too ⇒ 1 amphibious component spanning both banks.
 *
 * Pins: component counts per class, crossing classification + status, amphibious-crossable detection,
 * effective-set union over intact crossings, and the end-to-end GroundReach classification.
 */
#endregion

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class CrossingMapMathTest
	{
		const int Width = 12;
		const int Height = 10;

		// Ground: land everywhere except the river band (rows 4-5) which is impassable.
		static bool GroundPassable(int x, int y) => y < 4 || y > 5;

		// Amphibious: passable everywhere (land + water).
		static bool AmphibiousPassable(int x, int y) => true;

		static int[,] GroundLabels(out int count)
		{
			var labels = new int[Width, Height];
			count = CrossingMapMath.LabelComponents(Width, Height, GroundPassable, labels);
			return labels;
		}

		// The 4 fixture crossings, banks straddling the river (north row 3 ↔ south row 6).
		static List<GroundCrossing> Crossings(int[,] ground, CrossingStatus centralStatus, CrossingStatus flankStatus)
		{
			GroundCrossing At(int col, CrossingStatus status)
				=> CrossingMapMath.ClassifyCrossing(ground, Width, Height, col, 3, col, 6, status, col, 5);

			return new List<GroundCrossing>
			{
				At(0, flankStatus),    // west flank
				At(5, centralStatus),  // central
				At(6, centralStatus),  // central
				At(11, flankStatus),   // east flank
			};
		}

		[Test]
		public void GroundSplitsIntoTwoComponentsAmphibiousIntoOne()
		{
			var ground = GroundLabels(out var groundCount);
			var amphib = new int[Width, Height];
			var amphibCount = CrossingMapMath.LabelComponents(Width, Height, AmphibiousPassable, amphib);

			Assert.Multiple(() =>
			{
				Assert.That(groundCount, Is.EqualTo(2), "river splits land into north + south");
				Assert.That(amphibCount, Is.EqualTo(1), "amphibious spans the water — one component");

				// North bank and south bank are DIFFERENT ground components.
				var north = CrossingMapMath.LabelAt(ground, Width, Height, 5, 3);
				var south = CrossingMapMath.LabelAt(ground, Width, Height, 5, 6);
				Assert.That(north, Is.GreaterThanOrEqualTo(0));
				Assert.That(south, Is.GreaterThanOrEqualTo(0));
				Assert.That(north, Is.Not.EqualTo(south), "banks are disconnected on the ground");

				// River cells are impassable for ground.
				Assert.That(CrossingMapMath.LabelAt(ground, Width, Height, 5, 4), Is.EqualTo(CrossingMapMath.Impassable));

				// Amphibious sees both banks as the same component.
				var an = CrossingMapMath.LabelAt(amphib, Width, Height, 5, 3);
				var asth = CrossingMapMath.LabelAt(amphib, Width, Height, 5, 6);
				Assert.That(an, Is.EqualTo(asth), "amphibious reaches both banks");
			});
		}

		[Test]
		public void CrossingsClassifyBanksAndStatus()
		{
			var ground = GroundLabels(out _);
			var crossings = Crossings(ground, CrossingStatus.Intact, CrossingStatus.Repairable);

			Assert.Multiple(() =>
			{
				Assert.That(crossings.Count, Is.EqualTo(4));
				foreach (var c in crossings)
					Assert.That(c.JoinsDistinctComponents, Is.True, "every crossing bridges the two banks");

				// 2 central intact, 2 flank repairable.
				var intact = crossings.FindAll(c => c.Status == CrossingStatus.Intact);
				var repairable = crossings.FindAll(c => c.Status == CrossingStatus.Repairable);
				Assert.That(intact.Count, Is.EqualTo(2), "2 central bridges intact");
				Assert.That(repairable.Count, Is.EqualTo(2), "2 flank bridges destroyed-but-repairable");
			});
		}

		[Test]
		public void AmphibiousCrossablePairsFindsTheBankPair()
		{
			var ground = GroundLabels(out var groundCount);
			var amphib = new int[Width, Height];
			CrossingMapMath.LabelComponents(Width, Height, AmphibiousPassable, amphib);

			var pairs = CrossingMapMath.AmphibiousCrossablePairs(ground, groundCount, amphib, Width, Height);

			Assert.Multiple(() =>
			{
				Assert.That(pairs.Count, Is.EqualTo(1), "one amphibious-crossable ground-component pair");
				Assert.That(pairs[0], Is.EqualTo((0, 1)), "the two banks (ascending ids)");
				Assert.That(CrossingMapMath.AmphibiousConnects(pairs, 1, 0), Is.True, "order-insensitive");
				Assert.That(CrossingMapMath.AmphibiousConnects(pairs, 0, 0), Is.False, "same component is not a crossing");
			});
		}

		[Test]
		public void IntactCrossingsUnionBanksRepairableDoNot()
		{
			var ground = GroundLabels(out var groundCount);

			var withIntact = Crossings(ground, CrossingStatus.Intact, CrossingStatus.Repairable);
			var intactSets = CrossingMapMath.EffectiveGroundSets(groundCount, withIntact);
			Assert.That(CrossingMapMath.SameEffectiveSet(intactSets, 0, 1), Is.True,
				"an intact central bridge folds the banks into one effective ground set");

			var allRepairable = Crossings(ground, CrossingStatus.Repairable, CrossingStatus.Repairable);
			var repairSets = CrossingMapMath.EffectiveGroundSets(groundCount, allRepairable);
			Assert.That(CrossingMapMath.SameEffectiveSet(repairSets, 0, 1), Is.False,
				"a destroyed bridge is only a POTENTIAL connection — not yet walkable");
		}

		[Test]
		public void ClassifyGroundReachCoversEveryCase()
		{
			var ground = GroundLabels(out var groundCount);
			var amphib = new int[Width, Height];
			CrossingMapMath.LabelComponents(Width, Height, AmphibiousPassable, amphib);
			var amphibPairs = CrossingMapMath.AmphibiousCrossablePairs(ground, groundCount, amphib, Width, Height);

			var intact = Crossings(ground, CrossingStatus.Intact, CrossingStatus.Repairable);
			var intactSets = CrossingMapMath.EffectiveGroundSets(groundCount, intact);

			var onlyRepairable = Crossings(ground, CrossingStatus.Repairable, CrossingStatus.Repairable);
			var repairSets = CrossingMapMath.EffectiveGroundSets(groundCount, onlyRepairable);

			var noCrossings = new List<GroundCrossing>();
			var noSets = CrossingMapMath.EffectiveGroundSets(groundCount, noCrossings);

			Assert.Multiple(() =>
			{
				// Same component.
				Assert.That(CrossingMapMath.Classify(0, 0, intactSets, intact, amphibPairs),
					Is.EqualTo(GroundReach.Same));

				// Different component, joined by an intact central bridge.
				Assert.That(CrossingMapMath.Classify(0, 1, intactSets, intact, amphibPairs),
					Is.EqualTo(GroundReach.IntactCrossing));

				// Different component, only a repairable flank bridge connects them.
				Assert.That(CrossingMapMath.Classify(0, 1, repairSets, onlyRepairable, amphibPairs),
					Is.EqualTo(GroundReach.RepairableCrossing));

				// No bridge at all — only the amphibious locomotor connects the banks.
				Assert.That(CrossingMapMath.Classify(0, 1, noSets, noCrossings, amphibPairs),
					Is.EqualTo(GroundReach.AmphibiousOnly));

				// No bridge and no amphibious route — unreachable.
				Assert.That(CrossingMapMath.Classify(0, 1, noSets, noCrossings, new List<(int, int)>()),
					Is.EqualTo(GroundReach.Unreachable));

				// Invalid (off-map / water) component reads unreachable.
				Assert.That(CrossingMapMath.Classify(-1, 1, intactSets, intact, amphibPairs),
					Is.EqualTo(GroundReach.Unreachable));
			});
		}

		[Test]
		public void AnyOnLineDetectsAWaterCrossingAndExcludesEndpoints()
		{
			// Water is a vertical strip at x==5, y in [3,7].
			static bool Water(int x, int y) => x == 5 && y >= 3 && y <= 7;

			Assert.Multiple(() =>
			{
				// Horizontal line through the strip crosses water at (5,5) — an intermediate cell.
				Assert.That(CrossingMapMath.AnyOnLine(0, 5, 10, 5, Water), Is.True, "line crosses the river");

				// A line entirely on the clear side never touches water.
				Assert.That(CrossingMapMath.AnyOnLine(0, 0, 4, 0, Water), Is.False, "clear line, no barrier");

				// The water cell is the ENDPOINT — endpoints are excluded, so no crossing is reported.
				Assert.That(CrossingMapMath.AnyOnLine(0, 5, 5, 5, Water), Is.False, "endpoint water excluded");

				// Symmetric: start on the water endpoint.
				Assert.That(CrossingMapMath.AnyOnLine(5, 5, 10, 5, Water), Is.False, "start endpoint water excluded");
			});
		}

		[Test]
		public void IsCrossingSpanFindsLandBetweenWater()
		{
			// Water at (5,3) and (5,5); everything else is land.
			bool Water(int x, int y) => x == 5 && (y == 3 || y == 5);
			bool Land(int x, int y) => !Water(x, y);

			Assert.Multiple(() =>
			{
				// Land cell (5,4) has water N and S ⇒ a crossing span.
				Assert.That(CrossingMapMath.IsCrossingSpan(5, 4, Land, Water), Is.True, "land spanning water N&S");

				// A water cell itself is never a crossing (not land).
				Assert.That(CrossingMapMath.IsCrossingSpan(5, 3, Land, Water), Is.False, "water cell is not a span");

				// Open land with no flanking water is not a crossing.
				Assert.That(CrossingMapMath.IsCrossingSpan(0, 0, Land, Water), Is.False, "open land is not a span");
			});
		}

		[Test]
		public void ComponentLabellingIsDeterministicRowMajor()
		{
			var a = GroundLabels(out _);
			var b = GroundLabels(out _);
			for (var x = 0; x < Width; x++)
				for (var y = 0; y < Height; y++)
					Assert.That(a[x, y], Is.EqualTo(b[x, y]), $"labels stable at ({x},{y})");

			// Row-major seed order ⇒ the north bank (scanned first) is component 0.
			Assert.That(CrossingMapMath.LabelAt(a, Width, Height, 0, 0), Is.EqualTo(0), "north bank labelled first");
		}
	}
}
