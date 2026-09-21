#region Copyright & License Information
/*
 * WW3MOD release-identity tests — the four lines behind the main menu's "v" button.
 *
 * This panel is opened by a human clicking a button and by nothing else, so no gate in the build
 * has ever looked at it: `all`, `check`, `dotnet test`, `make test`, lua-gate, nav-guard and the
 * smoke gate were all green for the entire life of a panel that was wrong on three of its four
 * lines on every packaged release (WORKSPACE/audit/260921-release-readiness.md section 2.2 D1).
 *
 * The two failures worth catching are both silent:
 *
 *   1. A VERSION LABEL THAT IS A CONSTANT. The old line was the literal "WW3MOD - Pre-Alpha" on a
 *      tree that had shipped v0.1.0/1/2. A constant cannot be caught by reading it once -- it is
 *      correct on the day it is written -- so it is caught here by varying the input and requiring
 *      the output to vary with it, plus a source scan that refuses to let the literal come back.
 *   2. A BUILD DATE THAT IS A CLOCK. The old line was DateTime.Now evaluated when the menu opened,
 *      which renders the PLAYER'S current date labelled as the build date, forever. Any date-valued
 *      label that ignores its input is this bug, so the label is required to render the timestamp
 *      it is handed.
 */
#endregion

using System;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using OpenRA.Mods.Common;

namespace OpenRA.Test
{
	[TestFixture]
	public class ReleaseIdentityTest
	{
		// What packaging stamps into mod.yaml Version: (mod.config PACKAGING_OVERWRITE_MOD_VERSION).
		const string PackagedTag = "v0.1.2";

		// What the checked-in mod.yaml carries, and what "Fork:" is supposed to be naming.
		const string ForkPoint = "release-20230225";

		// The string this whole fixture exists to keep out of the tree.
		const string BannedLiteral = "Pre-Alpha";

		[TestCase("v0.1.2", TestName = "The shipped tag")]
		[TestCase("v0.1.0", TestName = "An older tag")]
		[TestCase("v1.0.0", TestName = "A future tag")]
		[TestCase("0.2.0", TestName = "A tag without the leading v")]
		public void APackagedBuildShowsTheTagItWasStampedWith(string tag)
		{
			var label = ReleaseIdentity.VersionLabel(tag, "1160a531ab");
			Assert.That(label, Does.Contain(tag),
				"a packaged build must name the release it is, not a word chosen at authoring time");
			Assert.That(label, Does.Contain(ReleaseIdentity.ModName));
			Assert.That(label, Does.Not.Contain(ReleaseIdentity.DevMarker),
				"a stamped tag is a release; calling it a dev build would be the same lie in the other direction");
		}

		// The mechanical form of "the version label is not a string literal": a constant cannot
		// distinguish two different releases, so requiring the output to vary with the input is a
		// test no hardcoded string can pass.
		[Test]
		public void TheVersionLabelVariesWithTheVersion()
		{
			Assert.That(
				ReleaseIdentity.VersionLabel("v0.1.2", "1160a531ab"),
				Is.Not.EqualTo(ReleaseIdentity.VersionLabel("v0.2.0", "1160a531ab")),
				"two different releases must not render the same version line");

			Assert.That(
				ReleaseIdentity.VersionLabel(ForkPoint, "1160a531ab"),
				Is.Not.EqualTo(ReleaseIdentity.VersionLabel(ForkPoint, "aaaaaaaaaa")),
				"two different dev builds must not render the same version line");
		}

		// An unstamped source tree carries mod.yaml's frozen fork marker, which is not a release
		// number. That is the discriminator -- and it must not be mistaken for a shipped version.
		[TestCase(ForkPoint, TestName = "The checked-in fork marker")]
		[TestCase("develop", TestName = "A branch name")]
		[TestCase("", TestName = "Empty")]
		[TestCase(null, TestName = "Absent")]
		public void AnUnstampedBuildSaysSoAndNamesItsRevision(string modVersion)
		{
			var label = ReleaseIdentity.VersionLabel(modVersion, "1160a531ab+1a2b3c4d");
			Assert.That(label, Does.Contain(ReleaseIdentity.DevMarker),
				"a source build must not present itself as a release");
			Assert.That(label, Does.Contain("1160a531"), "a dev build should name the commit it was built from");
			Assert.That(label, Does.Contain("+"), "a build from a dirty tree must not read as a clean commit");
		}

		// Stated separately because it is the specific confusion the panel shipped with: the fork
		// marker is the ENGINE's release, and a source build has no version of its own to show.
		[Test]
		public void AnUnstampedBuildNeverShowsTheForkMarkerAsItsOwnVersion()
		{
			Assert.That(ReleaseIdentity.VersionLabel(ForkPoint, "1160a531ab"), Does.Not.Contain(ForkPoint));
		}

		[TestCase("unknown", TestName = "git was unavailable at build time")]
		[TestCase("", TestName = "No revision stamped")]
		[TestCase(null, TestName = "No attribute at all")]
		public void AnUnknownRevisionDegradesToTheBareMarker(string revision)
		{
			var label = ReleaseIdentity.VersionLabel(ForkPoint, revision);
			Assert.That(label, Does.Contain(ReleaseIdentity.DevMarker));
			Assert.That(label, Does.Not.Contain("("), "there is no revision to put in brackets");
			Assert.That(label, Does.Not.Contain("unknown"), "'unknown' is a sentinel, not something to show a player");
		}

		// The line is labelled as the OpenRA release this forked from, so it must be the ENGINE
		// version. Feeding it the mod version is exactly the bug that shipped: packaging overwrites
		// mod.yaml Version: with the WW3MOD tag, so on every install that line read "Fork: v0.1.2".
		[Test]
		public void TheForkLineNamesTheEngineAndNeverTheModVersion()
		{
			var label = ReleaseIdentity.ForkLabel(ForkPoint);
			Assert.That(label, Does.Contain(ForkPoint));
			Assert.That(label, Does.Contain("OpenRA"), "the fork point is an OpenRA release and should say so");
			Assert.That(label, Does.Not.Contain(PackagedTag));
			Assert.That(ReleaseIdentity.ForkLabel("release-20240101"), Does.Not.EqualTo(label),
				"the fork line must track engine/VERSION rather than being written down once");
		}

		// The regression test for DateTime.Now: a label that renders its argument cannot be reading
		// the wall clock. A date far from today is used so a clock-reading implementation cannot
		// pass by coincidence.
		[Test]
		public void TheBuildLineRendersTheTimestampItIsGivenAndNotToday()
		{
			var label = ReleaseIdentity.BuildLabel(new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc));
			Assert.That(label, Does.Contain("2001-02-03"),
				"the build line must show when the build happened, not when the panel was opened");
			Assert.That(label, Does.Not.Contain(DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
				"rendering today's date is the exact defect this replaced");
		}

		[Test]
		public void AnUnreadableBuildTimeSaysUnknownRatherThanGuessing()
		{
			Assert.That(ReleaseIdentity.BuildLabel(null), Does.Contain(ReleaseIdentity.UnknownBuildDate));
			Assert.That(ReleaseIdentity.ResolveBuildTime(null), Is.Null);
		}

		// The build this test is running inside was produced by a build, so its own assembly must
		// resolve. If this ever fails, "Built:" has silently gone to "unknown" on every install.
		[Test]
		public void TheStampedAssemblyResolvesToARealBuildTime()
		{
			var resolved = ReleaseIdentity.ResolveBuildTime(ReleaseIdentity.StampedAssembly);
			Assert.That(resolved, Is.Not.Null, "OpenRA.Game's assembly should be on disk while its own tests run");
			Assert.That(resolved.Value, Is.GreaterThan(new DateTime(2020, 1, 1)),
				"a build time before this project existed means the timestamp is not a build time");
		}

		// The source-level half. The two failures above are about behaviour; this one is about the
		// literal coming back -- someone hardcoding a nicer word into the panel would leave every
		// assertion above green, because the assertions test the helper and the panel would have
		// stopped calling it. Both panels are covered: info-panel.yaml's MOD_INFO_PANEL is opened by
		// nothing today, but it carried the identical bug and is the one a future grep finds first.
		[TestCase("Widgets/Logic/MainMenuLogic.cs", TestName = "The live v dropdown")]
		[TestCase("Widgets/Logic/ModInfoPanelLogic.cs", TestName = "The unreachable mod info panel")]
		public void NoPanelHardcodesAVersionString(string relativePath)
		{
			var path = Path.Combine(ModVersionCompareTest.RepoRoot(), "engine", "OpenRA.Mods.Common", relativePath);
			Assert.That(File.Exists(path), Is.True, $"{relativePath} should exist");

			var code = CodeOnly(File.ReadAllLines(path));
			Assert.That(code, Does.Not.Contain(BannedLiteral),
				$"{relativePath} must derive its version line; a literal is correct only on the day it is typed");
			Assert.That(code, Does.Not.Contain("DateTime.Now"),
				$"{relativePath} must not read the wall clock for a build date");
			Assert.That(code, Does.Contain(nameof(ReleaseIdentity)),
				$"{relativePath} should build its identity lines through {nameof(ReleaseIdentity)}");
		}

		// Comments are excluded deliberately, and the first run of this test is why: a comment in
		// ModInfoPanelLogic.cs naming the old literal so a future grep would land on the right panel
		// tripped a guard aimed at code. Recording what a string USED to be is the opposite of
		// hardcoding it, and a gate that punishes the note would get the note deleted instead.
		static string CodeOnly(string[] lines)
		{
			var code = new StringBuilder();
			foreach (var line in lines)
			{
				var trimmed = line.TrimStart();
				if (trimmed.StartsWith("//", StringComparison.Ordinal)
					|| trimmed.StartsWith("*", StringComparison.Ordinal)
					|| trimmed.StartsWith("/*", StringComparison.Ordinal))
					continue;

				code.AppendLine(line);
			}

			return code.ToString();
		}
	}
}
