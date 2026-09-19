#region Copyright & License Information
/*
 * WW3MOD pre-captured structure ownership tests — who owns a neutral derrick before a shot is fired.
 *
 * The "Pre-captured Structures" lobby option hands every neutral capturable structure to the nearest player at
 * world load, unless nobody is meaningfully nearer, in which case it stays neutral. "Meaningfully nearer" is a
 * RATIO against the nearest non-allied player, and the threshold was calibrated on the ten shipped maps rather
 * than chosen — so the thing worth pinning is not the arithmetic in the abstract but the specific verdicts the
 * calibration promised. Every woodland-warfare-ww3 fixture below is the real geometry: the map's own spawn
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
	}
}
