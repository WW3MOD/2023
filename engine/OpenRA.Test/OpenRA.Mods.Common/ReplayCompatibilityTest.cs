#region Copyright & License Information
/*
 * WW3MOD replay build-compatibility tests.
 *
 * Replays are version-gated, but the value gated on is Metadata.Version, a literal in
 * mods/ww3mod/mod.yaml that only the manual `make version` target rewrites — `all: engine` never
 * does, and the line has been touched exactly once since it was introduced in 2023. So every build
 * ever made from this repo reported the identical string, the comparison was equal by construction,
 * and a replay from any older build was accepted without comment. The case below that pins this is
 * OldBuildReplayIsFlagged, which FAILS on the code as it shipped, returning Compatible.
 *
 * What that acceptance costs is smaller than it first looks, and the tests are shaped by the
 * correction. A diverging replay is NOT silent: the recorded sync hashes are fed back through
 * OrderManager.ReceiveSync alongside the locally recomputed ones (ReplayConnection.cs:101-109,
 * :117-118) and a mismatch raises OutOfSync. So a build difference is worth SAYING and not worth
 * refusing over — hence IsAdvisory, and hence AdvisoryResultsNeverMaskARealBlocker, which is the
 * case that would otherwise send someone hunting a build mismatch when their actual problem is a
 * map they do not have.
 *
 * Over-refusing would be a regression of its own, so the same-build cases are as load-bearing as
 * the mismatch ones: a replay recorded by the same build must resolve Compatible, INCLUDING when it
 * is watched on a different computer.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Widgets.Logic;
using OpenRA.Network;

namespace OpenRA.Test
{
	[TestFixture]
	public class ReplayCompatibilityTest
	{
		// The literal every build reports, frozen in mod.yaml since 2023.
		const string FrozenVersion = "release-20230225";

		const string Mod = "ww3mod";

		static ReplayCompatibility Resolve(string replayFingerprint, string currentFingerprint)
		{
			return ReplayCompatibilityCheck.Resolve(
				Mod, FrozenVersion, replayFingerprint,
				true, FrozenVersion, currentFingerprint,
				true);
		}

		// The bug, stated directly. Both sides carry the same version string because that string
		// cannot move, so the version comparison passes and nothing downstream noticed.
		[Test]
		public void OldBuildReplayIsFlagged()
		{
			var result = Resolve("aaaaaaaaaa/11111111/cccccccc", "bbbbbbbbbb/22222222/cccccccc");

			Assert.That(result, Is.EqualTo(ReplayCompatibility.IncompatibleBuild),
				"a replay from an older build must be flagged; before this check it returned Compatible");
			Assert.That(ReplayCompatibilityCheck.IsAdvisory(result), Is.True,
				"a build difference is a warning the player can override, not a blocker");
		}

		// Every replay recorded before the stamp existed. Unverifiable is not the same as matching.
		[Test]
		public void ReplayWithNoBuildStampIsFlagged()
		{
			Assert.That(Resolve(null, "bbbbbbbbbb/22222222/cccccccc"),
				Is.EqualTo(ReplayCompatibility.UnverifiableBuild));

			Assert.That(Resolve("", "bbbbbbbbbb/22222222/cccccccc"),
				Is.EqualTo(ReplayCompatibility.UnverifiableBuild));

			Assert.That(ReplayCompatibilityCheck.IsAdvisory(ReplayCompatibility.UnverifiableBuild), Is.True);
		}

		// The common case, and the one a heavy-handed check would break.
		[Test]
		public void SameBuildReplayStillLoads()
		{
			Assert.That(Resolve("aaaaaaaaaa/11111111/cccccccc", "aaaaaaaaaa/11111111/cccccccc"),
				Is.EqualTo(ReplayCompatibility.Compatible));
		}

		// Same build, different content set: only the asset digest moves. Not weighed — two installs
		// can legitimately differ there (a different Red Alert release, an optional package one side
		// mounted), and the digest cannot tell that from real divergence. Note the digest is NOT
		// machine-specific: Folder.Contents hashes leaf names only, so identical extractions on two
		// computers agree.
		[Test]
		public void ADifferentContentSetIsNotWeighed()
		{
			Assert.That(Resolve("aaaaaaaaaa/11111111/cccccccc", "aaaaaaaaaa/11111111/dddddddd"),
				Is.EqualTo(ReplayCompatibility.Compatible));
		}

		// A local hash that threw must not flag every replay; ContentHashes returns a sentinel rather
		// than propagating, for the same reason.
		[Test]
		public void AFailedLocalHashDoesNotFlagAReplay()
		{
			Assert.That(Resolve("aaaaaaaaaa/11111111/cccccccc", "aaaaaaaaaa/error/error"),
				Is.EqualTo(ReplayCompatibility.Compatible));
		}

		// But the sentinel must not be a skeleton key. A stored fingerprint reading "error" would
		// otherwise match every build in existence.
		[Test]
		public void ASentinelInTheRECORDEDFingerprintIsNotAFreePass()
		{
			Assert.That(Resolve("error/error/error", "aaaaaaaaaa/11111111/cccccccc"),
				Is.EqualTo(ReplayCompatibility.IncompatibleBuild));
		}

		// The ordering case. A missing map stops playback outright; a build difference does not.
		// Reporting the build first would name the smaller problem and hide the one to act on.
		[Test]
		public void AdvisoryResultsNeverMaskARealBlocker()
		{
			Assert.That(
				ReplayCompatibilityCheck.Resolve(Mod, FrozenVersion, null, true, FrozenVersion, "a/b/c", false),
				Is.EqualTo(ReplayCompatibility.UnavailableMap),
				"a pre-stamp replay whose map is missing must report the map, not the stamp");

			Assert.That(
				ReplayCompatibilityCheck.Resolve(Mod, FrozenVersion, "x/y/z", true, FrozenVersion, "a/b/c", false),
				Is.EqualTo(ReplayCompatibility.UnavailableMap),
				"a mismatched build whose map is missing must report the map");
		}

		// A replay belonging to a different installed mod: the caller cannot compute that mod's
		// fingerprint. Defence in depth only — such a replay is already stopped by the map check,
		// because a foreign mod's map is not in this mod's cache.
		[Test]
		public void AReplayWeCannotFingerprintIsNotBlamedForIt()
		{
			Assert.That(
				ReplayCompatibilityCheck.Resolve(Mod, FrozenVersion, null, true, FrozenVersion, null, true),
				Is.EqualTo(ReplayCompatibility.Compatible));
		}

		// The checks that already existed must keep firing, and must keep taking precedence — a
		// replay from another mod should say so rather than blaming the build.
		[Test]
		public void ExistingChecksAreUnchanged()
		{
			Assert.That(
				ReplayCompatibilityCheck.Resolve(Mod, null, "a/b/c", true, FrozenVersion, "a/b/c", true),
				Is.EqualTo(ReplayCompatibility.UnknownVersion));

			Assert.That(
				ReplayCompatibilityCheck.Resolve(null, FrozenVersion, "a/b/c", true, FrozenVersion, "a/b/c", true),
				Is.EqualTo(ReplayCompatibility.UnknownMod));

			Assert.That(
				ReplayCompatibilityCheck.Resolve("othermod", FrozenVersion, null, false, null, "a/b/c", true),
				Is.EqualTo(ReplayCompatibility.UnavailableMod),
				"an uninstalled mod must be reported as such, not as an unverifiable build");

			Assert.That(
				ReplayCompatibilityCheck.Resolve(Mod, "v2", null, true, FrozenVersion, "a/b/c", true),
				Is.EqualTo(ReplayCompatibility.IncompatibleVersion),
				"a packaged release DOES carry a real version, so this check still has work to do");
		}

		[Test]
		public void OnlyBuildResultsAreAdvisory()
		{
			Assert.That(ReplayCompatibilityCheck.IsAdvisory(ReplayCompatibility.Compatible), Is.False);
			Assert.That(ReplayCompatibilityCheck.IsAdvisory(ReplayCompatibility.UnknownVersion), Is.False);
			Assert.That(ReplayCompatibilityCheck.IsAdvisory(ReplayCompatibility.UnknownMod), Is.False);
			Assert.That(ReplayCompatibilityCheck.IsAdvisory(ReplayCompatibility.UnavailableMod), Is.False);
			Assert.That(ReplayCompatibilityCheck.IsAdvisory(ReplayCompatibility.IncompatibleVersion), Is.False);
			Assert.That(ReplayCompatibilityCheck.IsAdvisory(ReplayCompatibility.UnavailableMap), Is.False);
		}

		// "Replay failed to load" would leave the player suspecting a corrupt file. The warning has
		// to name the thing that actually moved.
		[TestCase("aaaaaaaaaa/11111111/cccccccc", "bbbbbbbbbb/11111111/cccccccc", "engine build")]
		[TestCase("aaaaaaaaaa/11111111/cccccccc", "aaaaaaaaaa/22222222/cccccccc", "mod rules")]
		[TestCase("aaaaaaaaaa/11111111/cccccccc", "bbbbbbbbbb/22222222/cccccccc", "engine build and mod rules")]
		public void WarningNamesWhatChanged(string mine, string theirs, string expected)
		{
			Assert.That(BuildFingerprint.DescribeReplayDifference(mine, theirs), Is.EqualTo(expected));
		}

		// The asset digest is not weighed, so it must never be named as the reason either — telling
		// someone to re-extract Red Alert over a difference that did not flag them is worse than
		// saying nothing.
		[Test]
		public void WarningNeverBlamesGameContent()
		{
			Assert.That(
				BuildFingerprint.DescribeReplayDifference("aaaaaaaaaa/11111111/cccccccc", "aaaaaaaaaa/11111111/dddddddd"),
				Does.Not.Contain("Red Alert"));
		}

		[Test]
		public void UnstampedReplayIsDescribedAsPredatingTheCheck()
		{
			Assert.That(BuildFingerprint.DescribeReplayDifference("aaaaaaaaaa/11111111/cccccccc", null),
				Is.EqualTo("an older build that predates this check"));
		}

		// Runs on a warning path, so it must degrade rather than throw. DescribeDifference guards
		// only its second argument.
		[Test]
		public void DescribingAgainstAnAbsentLocalFingerprintDoesNotThrow()
		{
			Assert.That(BuildFingerprint.DescribeReplayDifference(null, "aaaaaaaaaa/11111111/cccccccc"),
				Is.EqualTo("a different build"));

			Assert.That(BuildFingerprint.DescribeReplayDifference(null, null),
				Is.EqualTo("a different build"));
		}

		[Test]
		public void SegmentsMatchRejectsAnAbsentFingerprint()
		{
			Assert.That(BuildFingerprint.ReplaySegmentsMatch("a/b/c", null), Is.False);
			Assert.That(BuildFingerprint.ReplaySegmentsMatch(null, "a/b/c"), Is.False);
			Assert.That(BuildFingerprint.ReplaySegmentsMatch("a/b/c", ""), Is.False);
		}

		// A fingerprint truncated to fewer segments than we compare cannot be shown to agree.
		[Test]
		public void SegmentsMatchRejectsATruncatedFingerprint()
		{
			Assert.That(BuildFingerprint.ReplaySegmentsMatch("aaaaaaaaaa", "aaaaaaaaaa/11111111/cccccccc"), Is.False);
		}
	}
}
