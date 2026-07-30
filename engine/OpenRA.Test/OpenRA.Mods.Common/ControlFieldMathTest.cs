#region Copyright & License Information
/*
 * WW3MOD control-field math test — influence stack, Stage C.
 *
 * Pins the pure ownership lifecycle the trait relies on: Voronoi seeding, presence painting,
 * capture-flip, verified-clear grayzone, persistence lingering, contest erosion, and anchors.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class ControlFieldMathTest
	{
		// Mirrors the ControlFieldInfo defaults:
		// (SeedStrength, MaxScore, PresenceGain, ContestErode%, VerifiedClearErode%, PersistDecay%, GrayBand)
		static readonly ControlParams Defaults = new(500, 1000, 250, 40, 100, 8, 150);

		const int NoSeed = int.MaxValue;

		static ControlEvidence Self => new(selfPresent: true, enemyPresent: false, verifiedClear: false);
		static ControlEvidence Enemy => new(selfPresent: false, enemyPresent: true, verifiedClear: false);
		static ControlEvidence Contested => new(selfPresent: true, enemyPresent: true, verifiedClear: false);
		static ControlEvidence Clear => new(selfPresent: false, enemyPresent: false, verifiedClear: true);
		static ControlEvidence Fog => new(selfPresent: false, enemyPresent: false, verifiedClear: false);

		[Test]
		public void SeedPartitionByProximity()
		{
			Assert.Multiple(() =>
			{
				// Nearer home owns the cell.
				Assert.That(ControlFieldMath.SeedScore(4, 25, 500), Is.EqualTo(500), "self nearer → ours");
				Assert.That(ControlFieldMath.SeedScore(25, 4, 500), Is.EqualTo(-500), "enemy nearer → theirs");

				// Equidistant is the contested midline.
				Assert.That(ControlFieldMath.SeedScore(9, 9, 500), Is.EqualTo(0), "tie → contested");

				// Missing a side's seeds: the present side owns; no seeds at all → contested.
				Assert.That(ControlFieldMath.SeedScore(NoSeed, 16, 500), Is.EqualTo(-500), "only enemy seeded");
				Assert.That(ControlFieldMath.SeedScore(16, NoSeed, 500), Is.EqualTo(500), "only self seeded");
				Assert.That(ControlFieldMath.SeedScore(NoSeed, NoSeed, 500), Is.EqualTo(0), "no seeds → contested");
			});
		}

		[Test]
		public void PresencePaintsAndSaturates()
		{
			// Own units in empty ground claim it, and saturate at MaxScore.
			var s = ControlFieldMath.UpdateScore(0, Self, Defaults);
			Assert.That(s, Is.EqualTo(250));

			// Repeated presence climbs to and clamps at MaxScore.
			for (var i = 0; i < 10; i++)
				s = ControlFieldMath.UpdateScore(s, Self, Defaults);
			Assert.That(s, Is.EqualTo(1000), "clamped at MaxScore");
			Assert.That(ControlFieldMath.Classify(s, Defaults.GrayBand), Is.EqualTo(ControlOwner.Own));
		}

		[Test]
		public void CaptureFlipsOwnership()
		{
			// A firmly-held own cell (seeded +500) under sustained believed-enemy presence erodes
			// through contested and flips to enemy — the capture semantic.
			var s = 500;
			Assert.That(ControlFieldMath.Classify(s, Defaults.GrayBand), Is.EqualTo(ControlOwner.Own));

			s = ControlFieldMath.UpdateScore(s, Enemy, Defaults); // 250
			Assert.That(ControlFieldMath.Classify(s, Defaults.GrayBand), Is.EqualTo(ControlOwner.Own));

			s = ControlFieldMath.UpdateScore(s, Enemy, Defaults); // 0
			Assert.That(ControlFieldMath.Classify(s, Defaults.GrayBand), Is.EqualTo(ControlOwner.Contested));

			s = ControlFieldMath.UpdateScore(s, Enemy, Defaults); // -250
			Assert.That(ControlFieldMath.Classify(s, Defaults.GrayBand), Is.EqualTo(ControlOwner.Enemy),
				"sustained enemy presence flipped ownership");
		}

		[Test]
		public void VerifiedClearGraysImmediately()
		{
			// Observed empty ⇒ ownership relaxes to the grayzone at once (default erode 100%),
			// for both an own-held and an enemy-held cell.
			Assert.Multiple(() =>
			{
				Assert.That(ControlFieldMath.UpdateScore(1000, Clear, Defaults), Is.EqualTo(0));
				Assert.That(ControlFieldMath.UpdateScore(-1000, Clear, Defaults), Is.EqualTo(0));
				Assert.That(ControlFieldMath.Classify(ControlFieldMath.UpdateScore(800, Clear, Defaults),
					Defaults.GrayBand), Is.EqualTo(ControlOwner.Contested));
			});
		}

		[Test]
		public void PersistenceLingersUnderFog()
		{
			// No evidence (units left into fog): ownership fades slowly and stays ours for many
			// cycles — no flicker. 8%/cycle from 500 is still well above the gray band after 5.
			var s = 500;
			for (var i = 0; i < 5; i++)
				s = ControlFieldMath.UpdateScore(s, Fog, Defaults);

			Assert.That(s, Is.GreaterThan(Defaults.GrayBand), "still believed ours after 5 fogged cycles");
			Assert.That(ControlFieldMath.Classify(s, Defaults.GrayBand), Is.EqualTo(ControlOwner.Own));
		}

		[Test]
		public void ContestErodesTowardGray()
		{
			// A cell with both sides present bleeds toward the grayzone faster than fog decay.
			var contest = ControlFieldMath.UpdateScore(500, Contested, Defaults); // 300
			var fog = ControlFieldMath.UpdateScore(500, Fog, Defaults);           // 460
			Assert.That(contest, Is.LessThan(fog), "contest erodes faster than persistence");

			var s = 500;
			for (var i = 0; i < 4; i++)
				s = ControlFieldMath.UpdateScore(s, Contested, Defaults);
			Assert.That(ControlFieldMath.Classify(s, Defaults.GrayBand), Is.EqualTo(ControlOwner.Contested));
		}

		[Test]
		public void AnchorsPinOwnership()
		{
			Assert.Multiple(() =>
			{
				// A self anchor re-asserts a firm ownership floor even over enemy-eroded ground.
				var pinned = ControlFieldMath.ApplyAnchor(-500, 800, self: true);
				Assert.That(pinned, Is.EqualTo(800));
				Assert.That(ControlFieldMath.Classify(pinned, Defaults.GrayBand), Is.EqualTo(ControlOwner.Own));

				// An enemy anchor caps ownership negative — believed-enemy beachhead ground stays theirs.
				var enemyPinned = ControlFieldMath.ApplyAnchor(500, 800, self: false);
				Assert.That(enemyPinned, Is.EqualTo(-800));
				Assert.That(ControlFieldMath.Classify(enemyPinned, Defaults.GrayBand), Is.EqualTo(ControlOwner.Enemy));

				// An anchor never LOWERS a stronger existing claim, and a zero-strength anchor is a no-op.
				Assert.That(ControlFieldMath.ApplyAnchor(900, 800, self: true), Is.EqualTo(900));
				Assert.That(ControlFieldMath.ApplyAnchor(300, 0, self: true), Is.EqualTo(300));
			});
		}

		[Test]
		public void ClassifyBoundariesAreGrayInclusive()
		{
			Assert.Multiple(() =>
			{
				Assert.That(ControlFieldMath.Classify(150, 150), Is.EqualTo(ControlOwner.Contested), "== band is gray");
				Assert.That(ControlFieldMath.Classify(151, 150), Is.EqualTo(ControlOwner.Own));
				Assert.That(ControlFieldMath.Classify(-150, 150), Is.EqualTo(ControlOwner.Contested));
				Assert.That(ControlFieldMath.Classify(-151, 150), Is.EqualTo(ControlOwner.Enemy));
				Assert.That(ControlFieldMath.Classify(0, 150), Is.EqualTo(ControlOwner.Contested));
			});
		}

		[Test]
		public void FrontlineIsTheEnemyRegionBoundary()
		{
			const int band = 150;
			Assert.Multiple(() =>
			{
				// Enemy (score < −band) meeting anything not-enemy ⇒ frontline edge (order-independent).
				Assert.That(ControlFieldMath.IsFrontlineEdge(-500, 500, band), Is.True, "enemy | ours");
				Assert.That(ControlFieldMath.IsFrontlineEdge(500, -500, band), Is.True, "ours | enemy");

				// THE load-bearing case: the verified-clear rule relaxes observed-empty ground to 0
				// (contested no-man's-land). Enemy | neutral MUST still draw the front — a strict sign
				// flip would miss it and the contour would vanish in the buffer.
				Assert.That(ControlFieldMath.IsFrontlineEdge(-500, 0, band), Is.True, "enemy | neutral buffer");
				Assert.That(ControlFieldMath.IsFrontlineEdge(0, -500, band), Is.True, "neutral buffer | enemy");

				// The boundary sits exactly at the red-wash edge (Classify's Enemy threshold): a weak
				// enemy lean still inside the gray band (−100, classifies Contested) is "our side".
				Assert.That(ControlFieldMath.IsFrontlineEdge(-100, -500, band), Is.True, "gray-band lean | firm enemy");
				Assert.That(ControlFieldMath.IsFrontlineEdge(-150, -500, band), Is.True, "== −band is our side | enemy");

				// Same side ⇒ no edge. Both enemy, both ours, both neutral, ours | neutral all quiet —
				// only the enemy frontier draws, so held pockets aren't boxed and the buffer isn't split.
				Assert.That(ControlFieldMath.IsFrontlineEdge(-500, -250, band), Is.False, "both firm enemy");
				Assert.That(ControlFieldMath.IsFrontlineEdge(500, 250, band), Is.False, "both ours");
				Assert.That(ControlFieldMath.IsFrontlineEdge(0, 0, band), Is.False, "both neutral");
				Assert.That(ControlFieldMath.IsFrontlineEdge(500, 0, band), Is.False, "ours | neutral is not the front");
			});
		}

		[Test]
		public void FrontierDistanceIsBfsFromTheEnemyRegion()
		{
			const int band = 150;
			const int far = 64;

			// A 5x1 strip: one believed-enemy cell (score < -band) at gx=0, the rest neutral/ours.
			// Distance should be the coarse-cell hop count from each cell to that enemy cell.
			var score = new int[5, 1];
			score[0, 0] = -500; // enemy
			score[1, 0] = 0;    // neutral no-man's-land (still "our side" of the front)
			score[2, 0] = 0;
			score[3, 0] = 200;  // ours
			score[4, 0] = 500;  // ours

			var dist = new int[5, 1];
			ControlFieldMath.ComputeFrontierDistance(score, dist, 5, 1, band, far);

			Assert.Multiple(() =>
			{
				Assert.That(dist[0, 0], Is.EqualTo(0), "the enemy cell is on the front (distance 0)");
				Assert.That(dist[1, 0], Is.EqualTo(1), "neutral buffer one hop behind the front");
				Assert.That(dist[2, 0], Is.EqualTo(2));
				Assert.That(dist[3, 0], Is.EqualTo(3), "friendly ground three hops behind — this is what standoff reads");
				Assert.That(dist[4, 0], Is.EqualTo(4));
			});
		}

		[Test]
		public void FrontierDistanceIsMultiSourceAndBounded()
		{
			const int band = 150;

			// Enemy cells at both ends of a 5x1 strip: BFS takes the nearer of the two sources.
			var score = new int[5, 1];
			score[0, 0] = -300;
			score[4, 0] = -300;

			var dist = new int[5, 1];
			ControlFieldMath.ComputeFrontierDistance(score, dist, 5, 1, band, 64);
			Assert.Multiple(() =>
			{
				Assert.That(dist[0, 0], Is.EqualTo(0));
				Assert.That(dist[1, 0], Is.EqualTo(1));
				Assert.That(dist[2, 0], Is.EqualTo(2), "middle cell is 2 from the nearer of the two enemy sources");
				Assert.That(dist[3, 0], Is.EqualTo(1));
				Assert.That(dist[4, 0], Is.EqualTo(0));
			});

			// No believed enemy region anywhere ⇒ every cell reads the 'far' sentinel (the cap), never 0.
			var empty = new int[3, 1];
			var emptyDist = new int[3, 1];
			ControlFieldMath.ComputeFrontierDistance(empty, emptyDist, 3, 1, band, 64);
			Assert.That(emptyDist[0, 0], Is.EqualTo(64), "no enemy region ⇒ far sentinel");
			Assert.That(emptyDist[2, 0], Is.EqualTo(64));

			// The cap bounds the reading: a lone enemy source with a tiny cap leaves distant cells at the cap.
			var one = new int[6, 1];
			one[0, 0] = -300;
			var capped = new int[6, 1];
			ControlFieldMath.ComputeFrontierDistance(one, capped, 6, 1, band, 3);
			Assert.Multiple(() =>
			{
				Assert.That(capped[0, 0], Is.EqualTo(0));
				Assert.That(capped[2, 0], Is.EqualTo(2), "within the cap the true distance stands");
				Assert.That(capped[3, 0], Is.EqualTo(3), "beyond the cap reads the sentinel, not the true distance");
				Assert.That(capped[5, 0], Is.EqualTo(3), "far cell clamped to the cap sentinel");
			});
		}
	}
}
