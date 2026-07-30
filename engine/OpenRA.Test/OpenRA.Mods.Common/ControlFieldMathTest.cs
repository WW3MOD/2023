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
	}
}
