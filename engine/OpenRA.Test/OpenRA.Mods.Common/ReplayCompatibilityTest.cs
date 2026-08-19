#region Copyright & License Information
/*
 * WW3MOD replay build-compatibility tests.
 *
 * These pin a bug that was invisible rather than noisy. Replays are version-gated, but the value
 * gated on is Metadata.Version, a literal in mods/ww3mod/mod.yaml that only the manual `make
 * version` target rewrites — `all: engine` never does, and the line has been touched exactly once
 * since it was introduced in 2023. So every build ever made from this repo reported the identical
 * string, the comparison was equal by construction, and a replay from any older build was neither
 * refused nor warned about: it loaded and diverged silently during playback. That is the worst of
 * the three available behaviours, and it cannot be caught by a test that only checks the new gate
 * fires — the case below that matters most is OldBuildReplayIsRefused, which FAILS on the code as
 * it shipped, returning Compatible.
 *
 * The other half is just as load-bearing: over-refusing would be a regression of its own. A replay
 * recorded by the same build must still open silently, INCLUDING when it is watched on a different
 * computer — the fingerprint's third segment digests the Red Alert installation under ^SupportDir,
 * which differs between machines by construction, so gating on it would refuse the ordinary
 * record-here-watch-there case.
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
		// cannot move, so the version comparison passes and everything downstream believed the
		// replay was current.
		[Test]
		public void OldBuildReplayIsRefused()
		{
			var result = Resolve("aaaaaaaaaa/11111111/cccccccc", "bbbbbbbbbb/22222222/cccccccc");

			Assert.That(result, Is.EqualTo(ReplayCompatibility.IncompatibleBuild),
				"a replay from an older build must be refused; before this gate it returned Compatible and diverged silently");
		}

		// Every replay recorded before the stamp existed. Unverifiable is not the same as matching,
		// and treating it as matching is what produced the silent divergence in the first place.
		[Test]
		public void ReplayWithNoBuildStampIsRefused()
		{
			Assert.That(Resolve(null, "bbbbbbbbbb/22222222/cccccccc"),
				Is.EqualTo(ReplayCompatibility.UnverifiableBuild));

			Assert.That(Resolve("", "bbbbbbbbbb/22222222/cccccccc"),
				Is.EqualTo(ReplayCompatibility.UnverifiableBuild));
		}

		// The common case, and the one a heavy-handed gate would break.
		[Test]
		public void SameBuildReplayStillLoads()
		{
			Assert.That(Resolve("aaaaaaaaaa/11111111/cccccccc", "aaaaaaaaaa/11111111/cccccccc"),
				Is.EqualTo(ReplayCompatibility.Compatible));
		}

		// Same build, different computer: only the asset digest moves. Refusing this would make the
		// gate worse than the bug for anyone who records on one machine and watches on another.
		[Test]
		public void SameBuildOnAnotherMachineStillLoads()
		{
			Assert.That(Resolve("aaaaaaaaaa/11111111/cccccccc", "aaaaaaaaaa/11111111/dddddddd"),
				Is.EqualTo(ReplayCompatibility.Compatible));
		}

		// A hash that threw must not lock the player out of their replays; ContentHashes deliberately
		// returns a sentinel rather than propagating, for the same reason.
		[Test]
		public void AFailedHashDoesNotRefuseAReplay()
		{
			Assert.That(Resolve("aaaaaaaaaa/11111111/cccccccc", "aaaaaaaaaa/error/error"),
				Is.EqualTo(ReplayCompatibility.Compatible));
		}

		// A replay belonging to a different installed mod: the caller cannot compute that mod's
		// fingerprint, because the mod is not the one loaded. Refusing here would reject a replay
		// the engine is about to switch mods to play (BlankLoadScreen.cs:94-95), and would name the
		// running mod's build as the culprit — a reason that is simply not true.
		[Test]
		public void AReplayWeCannotFingerprintIsNotBlamedForIt()
		{
			Assert.That(
				ReplayCompatibilityCheck.Resolve(Mod, FrozenVersion, null, true, FrozenVersion, null, true),
				Is.EqualTo(ReplayCompatibility.Compatible));

			Assert.That(
				ReplayCompatibilityCheck.Resolve(Mod, FrozenVersion, "a/b/c", true, FrozenVersion, null, false),
				Is.EqualTo(ReplayCompatibility.UnavailableMap),
				"the checks we CAN still make must keep running");
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

			Assert.That(
				ReplayCompatibilityCheck.Resolve(Mod, FrozenVersion, "a/b/c", true, FrozenVersion, "a/b/c", false),
				Is.EqualTo(ReplayCompatibility.UnavailableMap));
		}

		// "Replay failed to load" would leave the player suspecting a corrupt file. The refusal has
		// to name the thing that actually moved.
		[TestCase("aaaaaaaaaa/11111111/cccccccc", "bbbbbbbbbb/11111111/cccccccc", "engine build")]
		[TestCase("aaaaaaaaaa/11111111/cccccccc", "aaaaaaaaaa/22222222/cccccccc", "mod rules")]
		[TestCase("aaaaaaaaaa/11111111/cccccccc", "bbbbbbbbbb/22222222/cccccccc", "engine build and mod rules")]
		public void RefusalNamesWhatChanged(string mine, string theirs, string expected)
		{
			Assert.That(BuildFingerprint.DescribeReplayDifference(mine, theirs), Is.EqualTo(expected));
		}

		// The asset digest is not weighed, so it must never be named as the reason either — telling
		// someone to re-extract Red Alert over a difference that did not refuse them is worse than
		// saying nothing.
		[Test]
		public void RefusalNeverBlamesGameContent()
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
