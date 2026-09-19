#region Copyright & License Information
/*
 * WW3MOD WebServices wiring test — pins mod.yaml's WebServices block to the code and files it names.
 *
 * Everything this block configures fails SILENTLY when it is wrong. A mistyped field name is
 * ignored and the default master.openra.net endpoint is used; a URL whose path is not actually
 * committed returns a 404 body that ModVersion.Compare correctly reads as Unknown and ParseNews
 * correctly refuses to parse. In all three cases the menu comes up looking perfectly normal and the
 * feature is simply gone -- which is the state this whole branch exists to get out of, and is
 * indistinguishable from it on any screenshot.
 *
 * No gate covers this. `--check-yaml` lints rules and maps, not the manifest's IGlobalModData
 * blocks, and no test loads the manifest. So the two links are checked here by hand: key -> field,
 * and URL -> committed file.
 */
#endregion

using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OpenRA.Mods.Common;

namespace OpenRA.Test
{
	[TestFixture]
	public class WebServicesConfigTest
	{
		// The repository is public, so raw.githubusercontent serves any committed file with no
		// hosting at all. That is the whole reason this approach works, and it is also the reason a
		// URL here is only as good as the file being on main under exactly this path.
		const string RawPrefix = "https://raw.githubusercontent.com/WW3MOD/2023/main/";

		static MiniYaml WebServicesNode()
		{
			var path = Path.Combine(ModVersionCompareTest.RepoRoot(), "mods", "ww3mod", "mod.yaml");
			var node = MiniYaml.FromFile(path).FirstOrDefault(n => n.Key == "WebServices");

			Assert.That(node, Is.Not.Null,
				"mods/ww3mod/mod.yaml has no WebServices block, so the mod is back on master.openra.net");

			return node.Value;
		}

		// A key that is not a field is not an error anywhere -- FieldLoader ignores it and the
		// default endpoint stands.
		[Test]
		public void EveryConfiguredKeyIsAFieldOnWebServices()
		{
			var fields = typeof(WebServices)
				.GetFields(BindingFlags.Public | BindingFlags.Instance)
				.Select(f => f.Name)
				.ToArray();

			foreach (var node in WebServicesNode().Nodes)
				Assert.That(fields, Contains.Item(node.Key),
					$"WebServices has no field '{node.Key}'; FieldLoader would ignore it silently");
		}

		[TestCase("LatestVersionUrl", "web/latest.txt")]
		[TestCase("GameNews", "web/news.yaml")]
		public void EachHostedUrlPointsAtAFileThatIsActuallyCommitted(string key, string expectedPath)
		{
			var value = WebServicesNode().ToDictionary()[key].Value;

			Assert.That(value, Does.StartWith(RawPrefix),
				$"{key} must be served from this repository, or there is a host to keep alive");

			var relative = value[RawPrefix.Length..];
			Assert.That(relative, Is.EqualTo(expectedPath));

			var onDisk = Path.Combine(ModVersionCompareTest.RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));
			Assert.That(File.Exists(onDisk), Is.True,
				$"{key} points at {relative}, which is not in the repository -- the fetch would 404");
		}

		// The update notice's button is the only reason the notice is worth showing; an empty URL
		// hides it and leaves the labels telling the player to go and look for the download.
		[Test]
		public void TheUpdateNoticeHasADownloadPageToOpen()
		{
			var value = WebServicesNode().ToDictionary()["LatestVersionDownloadUrl"].Value;

			Assert.That(string.IsNullOrWhiteSpace(value), Is.False);
			Assert.That(value, Does.StartWith("https://"), "the button hands this straight to the desktop browser");
			Assert.That(value, Is.EqualTo("https://github.com/WW3MOD/2023/releases"));
		}

		// The sysinfo payload MainMenuLogic appends to the news URL is only meaningful to the OpenRA
		// master server. Sending it to a static file host would put an opted-in player's system
		// profile in a third party's request logs for no purpose whatsoever.
		[Test]
		public void StaticNewsHostingDoesNotCarryTheSystemInfoPayload()
		{
			var fields = WebServicesNode().ToDictionary();

			Assert.That(fields.ContainsKey("GameNewsSendClientInfo"), Is.True,
				"news moved off master.openra.net, so the payload must be explicitly switched off");
			Assert.That(fields["GameNewsSendClientInfo"].Value, Is.EqualTo("false"));
		}

		// Default-path guard: the engine change must be invisible to any mod that does not opt in,
		// which is every other mod shipped in engine/mods.
		[Test]
		public void TheNewFieldsDefaultToTheUnchangedMasterServerBehaviour()
		{
			var defaults = new WebServices();

			Assert.That(defaults.LatestVersionUrl, Is.Empty, "an empty LatestVersionUrl keeps CheckModVersion on the master-server path");
			Assert.That(defaults.LatestVersionDownloadUrl, Is.Empty);
			Assert.That(defaults.GameNewsSendClientInfo, Is.True);
			Assert.That(defaults.GameNews, Is.EqualTo("https://master.openra.net/gamenews"));
			Assert.That(defaults.VersionCheck, Is.EqualTo("https://master.openra.net/versioncheck"));
		}
	}
}
