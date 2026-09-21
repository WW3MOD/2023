#region Copyright & License Information
/*
 * WW3MOD pre-captured structure ownership tests — who owns a neutral derrick before a shot is fired.
 *
 * The "Pre-captured Structures" lobby option hands every neutral capturable structure to the nearest player at
 * world load, leaving the contested ones neutral. There are TWO rules and these fixtures cover both.
 *
 * THE BORDER RULE is what runs on nine of the ten shipped maps, which author a DefconWall region in their own
 * rules.yaml. A structure inside the border band stays neutral; otherwise it goes to the nearest contender whose
 * own home is on the structure's side of the border, and a side with nobody living on it keeps its structures
 * neutral. PreCapturedOwnership.ResolveOnSide is that rule and the ResolveOnSide fixtures at the bottom of this
 * file pin it. It has no alliance clause: "contested" is stated by the map, not inferred from two distances.
 *
 * THE RATIO RULE is the FALLBACK, for a map where no border resolves. It is unchanged, and the fixtures for it
 * below are unchanged with it -- they are now tests of the fallback rather than of the shipped path, which is
 * why they still carry the calibration's verdicts. "Meaningfully nearer" is a RATIO against the nearest
 * non-allied player, and the threshold was calibrated on the ten shipped maps rather than chosen — so the thing
 * worth pinning is not the arithmetic in the abstract but the specific verdicts the calibration promised. Every woodland-warfare-ww3 fixture below is the real geometry: the map's own spawn
 * points and actor locations, run through the same cell-centre and building-centre offsets the engine applies.
 *
 * Three properties are load-bearing and each has cost something to get wrong elsewhere in this repo:
 *
 *  - the comparison is integer-only. The largest shipped map's diagonal is ~188000 world units, and multiplying
 *    that by 110 overflows an int at 20.7 million, so the widened arithmetic is the correctness fix rather than
 *    defensive habit.
 *  - ties resolve deterministically, on index into World.Players, which is identical on every client. A tie
 *    broken by enumeration order is a desync waiting for a four-spawn map.
 *  - "nearest non-allied" is not "nearest other". A structure between two teammates is not contested, and the
 *    x-lake-ww3 edge derricks sit at a 0.0% margin between two spawn points that are teammates in the obvious
 *    2v2. Treating that as a tie would leave four derricks neutral in the middle of one team's half.
 *
 * All pure integer arithmetic: no map, no world, no frame.
 */
#endregion

using System;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class PreCapturedOwnershipTest
	{
		const int Band = 10;

		static readonly Func<int, int, bool> AllEnemies = (a, b) => a == b;
		static readonly Func<int, int, bool> AllAllies = (a, b) => true;

		// Map.CenterOfCell on WW3MOD's Rectangular grid (conventions.md §"WW3MOD's map grid is Rectangular").
		static int Cell(int c)
		{
			return 1024 * c + 512;
		}

		// A 2x2 building's centre: CenterOfCell(topLeft) + BuildingInfo.CenterOffset, which for Dimensions 2,2
		// is half a cell on each axis (Building.cs:207-211). OILB, BIO and LOGISTICSCENTER are all 2x2.
		static int Cell2x2(int c)
		{
			return 1024 * c + 1024;
		}

		static long Distance(int x1, int y1, int x2, int y2)
		{
			return new WVec(x1 - x2, y1 - y2, 0).HorizontalLength;
		}

		// woodland-warfare-ww3 spawn points, from its map.yaml. A player's anchor is their Supply Route, which
		// sits exactly on the spawn cell centre — the (-1,-1) BaseActorOffset and a 3x3 building's (+1,+1)
		// CenterOffset cancel.
		static long[] WoodlandDistances(int topLeftX, int topLeftY)
		{
			var x = Cell2x2(topLeftX);
			var y = Cell2x2(topLeftY);
			return new[]
			{
				Distance(x, y, Cell(1), Cell(4)),
				Distance(x, y, Cell(96), Cell(93))
			};
		}

		// The named map fixtures. Expected owner is -1 for "stays neutral", else the spawn index.
		// Margins in the comments are (second / nearest - 1) and are what the calibration table reports.
		[TestCase(48, 47, -1, TestName = "BIO nuclear reactor, dead centre at a 2.1% margin, stays neutral")]
		[TestCase(64, 31, -1, TestName = "OILB on the diagonal bisector at a 0.1% margin, stays neutral")]
		[TestCase(31, 64, -1, TestName = "its mirror at a 4.1% margin, stays neutral — the pair must agree")]
		[TestCase(26, 26, 0, TestName = "OILB in spawn 0's half at a 183% margin")]
		[TestCase(70, 70, 1, TestName = "its mirror in spawn 1's half")]
		[TestCase(4, 18, 0, TestName = "OILB on spawn 0's doorstep")]
		[TestCase(92, 79, 1, TestName = "OILB on spawn 1's doorstep")]
		[TestCase(73, 4, 0, TestName = "OILB at a 25.9% margin is captured, not a tie")]
		[TestCase(23, 91, 1, TestName = "its mirror at a 24.6% margin")]
		public void WoodlandWarfareOwnershipMatchesTheCalibration(int topLeftX, int topLeftY, int expected)
		{
			var d = WoodlandDistances(topLeftX, topLeftY);
			Assert.That(PreCapturedOwnership.Resolve(d, AllEnemies, Band), Is.EqualTo(expected));
		}

		// THE REACTOR IS NOT THE MOST CENTRAL THING ON THAT MAP, which is the single most misreadable fact about
		// this feature: the brief that commissioned it asked for "the reactor neutral, the derricks captured",
		// and no threshold delivers that. Pinned so nobody re-derives the impossibility from a bug report.
		[Test]
		public void TheBisectorDerrickIsMoreCentralThanTheReactor()
		{
			var reactor = WoodlandDistances(48, 47);
			var derrick = WoodlandDistances(64, 31);

			Assert.That(Margin(derrick), Is.LessThan(Margin(reactor)),
				"oilb at 64,31 sits closer to the perpendicular bisector than the reactor does, so any band " +
				"that keeps the reactor neutral keeps that derrick neutral too.");
		}

		static double Margin(long[] d)
		{
			var near = Math.Min(d[0], d[1]);
			var second = Math.Max(d[0], d[1]);
			return (double)second / near;
		}

		// The band edges, from the two shipped maps that sit either side of the empty 8.4%..15.2% gap. These are
		// the fixtures that fail if MiddleBandPercent is retuned, which is the point of having them.
		[TestCase(100000, 108400, -1, TestName = "river-zeta LOGISTICSCENTER at 8.4% is inside the band")]
		[TestCase(100000, 115200, 0, TestName = "twin-rivers OILB at 15.2% is outside it")]
		[TestCase(100000, 110000, -1, TestName = "exactly 10% is inside the band — the comparison is <=")]
		[TestCase(100000, 110001, 0, TestName = "one unit past 10% is outside it")]
		public void TheBandEdgeIsWhereTheCalibrationPutIt(long nearest, long second, int expected)
		{
			Assert.That(PreCapturedOwnership.Resolve(new[] { nearest, second }, AllEnemies, Band), Is.EqualTo(expected));
		}

		[Test]
		public void AnExactTieBetweenEnemiesStaysNeutral()
		{
			Assert.That(PreCapturedOwnership.Resolve(new long[] { 50000, 50000 }, AllEnemies, Band), Is.EqualTo(-1));
		}

		// The alliance clause. Same two distances as the test above — the ONLY difference is that the two
		// contenders are allies — and the verdict flips from neutral to captured.
		[Test]
		public void AnExactTieBetweenAlliesIsCapturedByTheLowerIndex()
		{
			Assert.That(PreCapturedOwnership.Resolve(new long[] { 50000, 50000 }, AllAllies, Band), Is.EqualTo(0));
		}

		// x-lake-ww3's edge-midpoint derricks in a 2v2: equidistant from two teammates on one side, 110 cells
		// from either enemy. Nearest player wins outright; this is not a contested structure.
		[Test]
		public void ADerrickBetweenTeammatesGoesToATeammateNotToNeutral()
		{
			var distances = new long[] { 45000, 45000, 113000, 113000 };
			static bool Allied(int a, int b) => a / 2 == b / 2;

			Assert.That(PreCapturedOwnership.Resolve(distances, Allied, Band), Is.EqualTo(0));
		}

		// The same four distances with no alliances at all: now the two nearest genuinely contest it.
		[Test]
		public void TheSameDerrickIsNeutralWhenThoseTwoAreEnemies()
		{
			Assert.That(PreCapturedOwnership.Resolve(new long[] { 45000, 45000, 113000, 113000 }, AllEnemies, Band),
				Is.EqualTo(-1));
		}

		[Test]
		public void ASolePlayerTakesEverythingUncontested()
		{
			Assert.That(PreCapturedOwnership.Resolve(new long[] { 120000 }, AllEnemies, Band), Is.EqualTo(0));
		}

		// A whole map's worth of allies — a co-op slot layout — is a walkover, not a map full of ties.
		[Test]
		public void AMapOfNothingButAlliesIsAWalkover()
		{
			Assert.That(PreCapturedOwnership.Resolve(new long[] { 60000, 59000, 61000 }, AllAllies, Band), Is.EqualTo(1));
		}

		[Test]
		public void NoContendersMeansNoOwner()
		{
			Assert.That(PreCapturedOwnership.Resolve(Array.Empty<long>(), AllEnemies, Band), Is.EqualTo(-1));
			Assert.That(PreCapturedOwnership.Resolve(null, AllEnemies, Band), Is.EqualTo(-1));
		}

		// The far end of the scale. 188000 units is the diagonal of the largest shipped map, which is as far
		// apart as two anchors can get — a sanity check that the ratio still discriminates there rather than a
		// claim about overflow: 188000 * 110 is 20.7 million and fits an int with two orders of magnitude to
		// spare. The longs in Resolve are headroom, not a fix.
		[Test]
		public void TheRatioStillDiscriminatesAtMapScale()
		{
			const long Far = 188000;
			Assert.That(PreCapturedOwnership.Resolve(new[] { Far, Far * 2 }, AllEnemies, Band), Is.EqualTo(0));
			Assert.That(PreCapturedOwnership.Resolve(new[] { Far, Far + 100 }, AllEnemies, Band), Is.EqualTo(-1));
		}

		// =====================================================================================
		// THE BORDER RULE -- ResolveOnSide, which is what actually decides ownership on the nine
		// shipped maps that author a DefconWall region.
		// =====================================================================================
		//
		// SIDE IDS ARE OPAQUE NON-NEGATIVE INTEGERS AND NEGATIVE MEANS UNCLASSIFIED. That contract
		// is DefconWall's level-independent surface: a region answers with a connected-component id
		// (0, 1, ...), a line with 0 or 1, and DefconWall.NoSide is -1 on both. These fixtures use
		// 0 and 1 for the two halves and -1 for "in the band / off the map / straddling", which is
		// exactly what SideOfFootprint collapses those three cases to.

		const int NoSide = -1;

		// The structure is in the band. This is the case the whole feature is for, and it does not
		// matter how near anybody is: it is the MAP saying this ground is contested, not a ratio.
		[Test]
		public void AStructureInTheBandStaysNeutralHoweverNearSomebodyIs()
		{
			Assert.That(PreCapturedOwnership.ResolveOnSide(NoSide, new long[] { 1000, 90000 }, new[] { 0, 1 }),
				Is.EqualTo(-1));
		}

		// SideOfFootprint returns NoSide for a building whose footprint cells disagree on side --
		// one corner each side of a bending border, with no cell actually in the band. It reaches
		// this function as the same NoSide the band case does, which is why one test covers both.
		[Test]
		public void AStructureStraddlingTheBorderStaysNeutral()
		{
			Assert.That(PreCapturedOwnership.ResolveOnSide(NoSide, new long[] { 40000, 41000 }, new[] { 0, 1 }),
				Is.EqualTo(-1));
		}

		[Test]
		public void TheNearestContenderOnTheStructuresOwnSideTakesIt()
		{
			Assert.That(PreCapturedOwnership.ResolveOnSide(1, new long[] { 10000, 40000, 50000 }, new[] { 0, 1, 1 }),
				Is.EqualTo(1));
		}

		// TWO CONTENDERS ON ONE SIDE: the nearer wins, and the one on the other side is not
		// consulted at all even though it is nearest of the three. This is the row where the border
		// rule and the ratio rule disagree, and the reason the feature was changed: 10000 units is
		// half the distance of the winner, and it is still not that player's structure.
		[Test]
		public void ANearerContenderOnTheWrongSideDoesNotTakeIt()
		{
			var winner = PreCapturedOwnership.ResolveOnSide(1, new long[] { 10000, 40000, 30000 }, new[] { 0, 1, 1 });

			Assert.That(winner, Is.EqualTo(2));
		}

		// A side with no contender on it keeps its structures neutral. Handing them to whoever is
		// nearest would hand them across the border, which is what this rule exists to stop.
		[Test]
		public void ASideWithNobodyLivingOnItKeepsItsStructuresNeutral()
		{
			Assert.That(PreCapturedOwnership.ResolveOnSide(1, new long[] { 10000, 20000 }, new[] { 0, 0 }),
				Is.EqualTo(-1));
		}

		// ALLIES ARE NOT A SPECIAL CASE HERE, unlike in the ratio rule. Two teammates sharing a side
		// simply race on distance; there is nothing to declare contested, because the map already
		// said where contested is.
		[Test]
		public void TwoAlliesOnOneSideRaceOnDistanceAndTheNearerTakesIt()
		{
			Assert.That(PreCapturedOwnership.ResolveOnSide(0, new long[] { 60000, 45000 }, new[] { 0, 0 }),
				Is.EqualTo(1));
		}

		// Ties break on the lowest index -- World.Players order, identical on every client. Same
		// rule as Resolve, and for the same desync reason.
		[Test]
		public void AnExactTieOnOneSideBreaksOnTheLowerIndex()
		{
			Assert.That(PreCapturedOwnership.ResolveOnSide(0, new long[] { 50000, 50000 }, new[] { 0, 0 }),
				Is.EqualTo(0));
		}

		// A contender with no side of their own -- their anchor sits in the band or off the map --
		// contends for nothing. They are excluded by the side comparison itself rather than by a
		// second test, because a negative can never equal a non-negative structure side.
		[Test]
		public void AContenderWithNoSideOfTheirOwnContendsForNothing()
		{
			Assert.That(PreCapturedOwnership.ResolveOnSide(0, new long[] { 1000, 90000 }, new[] { NoSide, 0 }),
				Is.EqualTo(1));
			Assert.That(PreCapturedOwnership.ResolveOnSide(NoSide, new long[] { 1000 }, new[] { NoSide }),
				Is.EqualTo(-1));
		}

		// A region can have more than two components -- DefconWallRegion labels every connected
		// piece, and IsDegenerate only requires two. The rule is an equality test on ids, so a
		// third pocket behaves like any other side rather than like a special case.
		[Test]
		public void AThirdComponentIsJustAnotherSide()
		{
			var distances = new long[] { 10000, 20000, 30000 };
			var sides = new[] { 0, 1, 2 };

			Assert.That(PreCapturedOwnership.ResolveOnSide(2, distances, sides), Is.EqualTo(2));
			Assert.That(PreCapturedOwnership.ResolveOnSide(3, distances, sides), Is.EqualTo(-1));
		}

		[Test]
		public void MalformedInputIsNeutralRatherThanAThrow()
		{
			Assert.That(PreCapturedOwnership.ResolveOnSide(0, null, new[] { 0 }), Is.EqualTo(-1));
			Assert.That(PreCapturedOwnership.ResolveOnSide(0, new long[] { 1000 }, null), Is.EqualTo(-1));
			Assert.That(PreCapturedOwnership.ResolveOnSide(0, Array.Empty<long>(), Array.Empty<int>()), Is.EqualTo(-1));

			// Lengths that disagree are a caller bug; answering neutral is the safe half of it.
			Assert.That(PreCapturedOwnership.ResolveOnSide(0, new long[] { 1000, 2000 }, new[] { 0 }), Is.EqualTo(-1));
		}

		// =====================================================================================
		// The autotest scenario's own arithmetic, so a red here and a red in test-precaptured-
		// structures mean the same thing rather than two different things.
		// =====================================================================================
		// test-precaptured-structures places two Supply Routes at cells 4,16 (USA) and 60,16
		// (Russia) and a two-cell vertical band at x=31,32. Contender order is [Russia, USA] --
		// map players precede slot players in World.Players -- so Russia is index 0, and Russia
		// lives east (side 1) with USA west (side 0). Distances are in world units, from the
		// map.yaml table: 1024 units to the cell.
		static readonly int[] ScenarioSides = { 1, 0 };

		[Test]
		public void TheScenarioWestDerrickGoesToUsa()
		{
			// The derrick at 11,15 is west of the band: 7.52 cells from USA, 48.50 from Russia.
			Assert.That(PreCapturedOwnership.ResolveOnSide(0, new long[] { 49664, 7700 }, ScenarioSides),
				Is.EqualTo(1));
		}

		[Test]
		public void TheScenarioEastDerrickGoesToRussia()
		{
			// The derrick at 51,15 is east of the band: 8.51 cells from Russia, 47.50 from USA.
			Assert.That(PreCapturedOwnership.ResolveOnSide(1, new long[] { 8714, 48640 }, ScenarioSides),
				Is.EqualTo(0));
		}

		[Test]
		public void TheScenarioMiddleDerrickIsInTheBandAndStaysNeutral()
		{
			// 27.50 vs 28.50 cells -- a 3.6% margin, which the RATIO rule also calls neutral. The
			// scenario is nonetheless evidence about the border rule: with the band authored, the
			// verdict comes from the footprint being inside it, and the distances are not consulted.
			Assert.That(PreCapturedOwnership.ResolveOnSide(NoSide, new long[] { 29184, 28160 }, ScenarioSides),
				Is.EqualTo(-1));
		}
	}
}
