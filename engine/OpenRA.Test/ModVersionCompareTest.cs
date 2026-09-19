#region Copyright & License Information
/*
 * WW3MOD update-notice tests — the client-side version comparison behind the main menu notice.
 *
 * The notice is shown by MainMenuLogic on exactly one status, Outdated. Everything that decides
 * that status lives in ModVersion.Compare, and the half of the feature around it is pure network:
 * it cannot be exercised without a live host, and on a developer machine it never runs at all.
 * So this is the only place the rules are checkable, and the two failures worth catching are both
 * silent rather than loud:
 *
 *   1. Telling a DEVELOPMENT BUILD it is outdated. Unstamped builds carry the checked-in
 *      mod.yaml Version (today "release-20230225"); packaging rewrites it to the tag. A rule that
 *      reads an unparseable local version as "behind" would put a permanent update nag on every
 *      source build, which is the state the whole feature exists to avoid.
 *   2. Reading a BROKEN REMOTE as "you are up to date". A 404 body, an HTML error page or an empty
 *      file must not resolve to Latest -- that is indistinguishable from a working check and would
 *      hide a real release indefinitely.
 *
 * Both are answered with Unknown, which is the status that shows nothing.
 */
#endregion

using System.IO;
using NUnit.Framework;
using OpenRA.Mods.Common;

namespace OpenRA.Test
{
	[TestFixture]
	public class ModVersionCompareTest
	{
		// What an unpackaged build actually carries: mods/ww3mod/mod.yaml's checked-in Version,
		// which packaging/functions.sh set_mod_version overwrites with the git tag at package time.
		const string DevVersion = "release-20230225";

		internal static string RepoRoot()
		{
			var dir = Directory.GetCurrentDirectory();
			while (dir != null && !Directory.Exists(Path.Combine(dir, "mods", "ww3mod")))
				dir = Directory.GetParent(dir)?.FullName;

			Assert.That(dir, Is.Not.Null, "could not locate mods/ww3mod above the test working directory");
			return dir;
		}

		[TestCase("v0.1.1", "v0.1.2", TestName = "One patch behind")]
		[TestCase("v0.1.2", "v0.2.0", TestName = "One minor behind")]
		[TestCase("v0.9.9", "v1.0.0", TestName = "Across a major bump")]
		[TestCase("v0.1", "v0.1.1", TestName = "Missing components count as zero, not as a wildcard")]
		[TestCase("0.1.1", "v0.1.2", TestName = "The leading v is optional on either side")]
		public void AnOlderLocalVersionIsOutdated(string local, string remote)
		{
			Assert.That(ModVersion.Compare(local, remote), Is.EqualTo(ModVersionStatus.Outdated));
		}

		[TestCase("v0.1.2", "v0.1.2", TestName = "Exactly the released version")]
		[TestCase("v0.1.2", "v0.1.2\n", TestName = "A trailing newline is not a difference")]
		[TestCase("v0.1.2", "  v0.1.2  \r\n", TestName = "Surrounding whitespace is not a difference")]
		[TestCase("v0.1", "v0.1.0", TestName = "v0.1 and v0.1.0 are the same version")]
		[TestCase("v0.1.2", "0.1.2", TestName = "The leading v is not part of the comparison")]
		public void TheReleasedVersionIsCurrent(string local, string remote)
		{
			Assert.That(ModVersion.Compare(local, remote), Is.EqualTo(ModVersionStatus.Latest));
		}

		// Ahead of the file rather than behind it: the normal state between tagging a release and
		// remembering to bump web/latest.txt. Nagging there would be worse than saying nothing.
		[TestCase("v0.2.0", "v0.1.2", TestName = "A newer local build is never told to downgrade")]
		[TestCase("v1.0.0", "v0.9.9", TestName = "Newer across a major bump")]
		[TestCase("v0.1.3", "v0.1.2", TestName = "Newer by one patch")]
		public void ANewerLocalVersionIsCurrent(string local, string remote)
		{
			Assert.That(ModVersion.Compare(local, remote), Is.EqualTo(ModVersionStatus.Latest));
		}

		// The load-bearing case: working from source must never show the notice, whatever the file
		// says -- including when the file is a long way ahead.
		[TestCase(DevVersion, "v0.1.2")]
		[TestCase(DevVersion, "v99.0.0")]
		[TestCase("{DEV_VERSION}", "v0.1.2")]
		[TestCase("develop", "v0.1.2")]
		[TestCase("", "v0.1.2")]
		[TestCase(null, "v0.1.2")]
		public void ADevelopmentBuildIsNeverOutdated(string local, string remote)
		{
			var status = ModVersion.Compare(local, remote);
			Assert.That(status, Is.Not.EqualTo(ModVersionStatus.Outdated),
				"an unstamped build has no version to compare, so it cannot be behind one");
			Assert.That(status, Is.EqualTo(ModVersionStatus.Unknown));
		}

		[TestCase("", TestName = "Empty file")]
		[TestCase("   \n\n  \n", TestName = "Whitespace only")]
		[TestCase(null, TestName = "No body at all")]
		[TestCase("404: Not Found", TestName = "What raw.githubusercontent.com serves for a bad path")]
		[TestCase("<!DOCTYPE html><html><body>Not Found</body></html>", TestName = "An HTML error page")]
		[TestCase("latest", TestName = "A word rather than a version")]
		[TestCase("v0.1.2.3.4", TestName = "Too many components")]
		[TestCase("v0.1.-2", TestName = "A negative component")]
		[TestCase("v0.1.2a", TestName = "A non-numeric component")]
		[TestCase("v", TestName = "The prefix and nothing else")]
		[TestCase("v0.1.99999999999999", TestName = "A component too large for an int")]
		public void AnUnusableRemoteFileIsUnknown(string remote)
		{
			Assert.That(ModVersion.Compare("v0.1.0", remote), Is.EqualTo(ModVersionStatus.Unknown),
				"a remote file that is not a version must not read as either current or outdated");
		}

		// A body long enough to be a web page is rejected without parsing, so a URL pointed at the
		// wrong thing cannot feed an arbitrary document through the version path.
		[Test]
		public void AnOverlongRemoteBodyIsUnknown()
		{
			var longLine = new string('1', 200);
			Assert.That(ModVersion.Compare("v0.1.0", longLine), Is.EqualTo(ModVersionStatus.Unknown));
		}

		// web/latest.txt is served to real installs and is linted by nothing -- it is outside
		// mods/, so neither --check-yaml nor make test ever looks at it. If it stops parsing, every
		// install silently goes back to never being told about a release.
		[Test]
		public void TheHostedLatestVersionFileParses()
		{
			var path = Path.Combine(RepoRoot(), "web", "latest.txt");
			Assert.That(File.Exists(path), Is.True, "web/latest.txt is the file WebServices.LatestVersionUrl points at");

			var contents = File.ReadAllText(path);
			Assert.That(ModVersion.TryParse(contents, out _), Is.True,
				$"web/latest.txt must be a version string; it reads {contents.Trim()}");

			// A stamped install running exactly this tag must be told it is current, which is the
			// one behaviour the file's content can break on its own.
			Assert.That(ModVersion.Compare(contents.Trim(), contents), Is.EqualTo(ModVersionStatus.Latest));
		}
	}
}
