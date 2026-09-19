#region Copyright & License Information
/*
 * WW3MOD news-feed format test — pins web/news.yaml against the parser that reads it.
 *
 * web/news.yaml is fetched at runtime from raw.githubusercontent.com and is linted by NOTHING:
 * it lives outside mods/, so neither `--check-yaml` nor `make test` ever opens it, and the engine
 * only discovers a defect in it on a player's machine, after the download, as a one-line status in
 * a panel most players never open. A malformed entry does not crash and does not fail any gate --
 * it just quietly leaves whatever was cached last on screen forever.
 *
 * The rules below are not this test's invention; they are what MainMenuLogic.ParseNews does
 * (engine/OpenRA.Mods.Common/Widgets/Logic/MainMenuLogic.cs, the ParseNews method):
 * MiniYaml.FromFile, one node per item, the node KEY read as a DateTime through FieldLoader, and
 * Title / Author / Content pulled out of the node's children by exact name. The two engine calls
 * are made here directly rather than reimplemented, so a parse this test accepts is a parse the
 * menu accepts.
 *
 * KNOWN LIMIT: ParseNews is a private instance method and cannot be called, so the SHAPE of the
 * read is mirrored rather than shared. If that method starts reading a fourth field, this test
 * will not notice on its own.
 */
#endregion

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace OpenRA.Test
{
	[TestFixture]
	public class NewsFeedFormatTest
	{
		// Exactly the keys ParseNews indexes. A missing one throws a KeyNotFoundException inside
		// its try, which surfaces to the player as "Failed to parse news" and nothing else.
		static readonly string[] RequiredFields = { "Title", "Author", "Content" };

		static string NewsPath()
		{
			return Path.Combine(ModVersionCompareTest.RepoRoot(), "web", "news.yaml");
		}

		[Test]
		public void TheHostedNewsFileParsesAsTheMenuWouldParseIt()
		{
			var path = NewsPath();
			Assert.That(File.Exists(path), Is.True, "web/news.yaml is the file WebServices.GameNews points at");

			var nodes = MiniYaml.FromFile(path);
			Assert.That(nodes.Count, Is.GreaterThan(0), "an empty feed renders an empty panel");

			foreach (var node in nodes)
			{
				// The timestamp is the node key, and FieldLoader wants "yyyy-MM-dd HH-mm-ss" --
				// dashes in the time, because a colon would end the MiniYaml key instead.
				Assert.DoesNotThrow(() => FieldLoader.GetValue<DateTime>("DateTime", node.Key),
					$"news item key '{node.Key}' is not a date in the yyyy-MM-dd HH-mm-ss form ParseNews requires");

				var fields = node.Value.ToDictionary();
				foreach (var required in RequiredFields)
					Assert.That(fields.ContainsKey(required), Is.True,
						$"news item '{node.Key}' has no {required}; ParseNews indexes it unconditionally");

				foreach (var required in RequiredFields)
					Assert.That(string.IsNullOrWhiteSpace(fields[required].Value), Is.False,
						$"news item '{node.Key}' has an empty {required}, which renders as a blank line");
			}
		}

		// Duplicate keys are the silent one: MiniYaml merges same-named top-level nodes, so two
		// items stamped with the same timestamp become one item and the other simply vanishes.
		[Test]
		public void EveryNewsItemHasADistinctTimestamp()
		{
			var nodes = MiniYaml.FromFile(NewsPath());
			var keys = nodes.Select(n => n.Key).ToArray();

			Assert.That(keys.Distinct().Count(), Is.EqualTo(keys.Length),
				"two items sharing a timestamp merge into one and the second is lost without an error");
		}

		// The panel auto-opens when a fetched item carries a DateTime absent from the cached copy,
		// so a timestamp in the future would pop the news panel open on every launch until the
		// clock catches up. FieldLoader parses with AssumeUniversal and no AdjustToUniversal, so the
		// value comes back in LOCAL time -- compare it against DateTime.Now, not UtcNow.
		[Test]
		public void NoNewsItemIsDatedInTheFuture()
		{
			var nodes = MiniYaml.FromFile(NewsPath());

			foreach (var node in nodes)
			{
				var dateTime = FieldLoader.GetValue<DateTime>("DateTime", node.Key);
				Assert.That(dateTime, Is.LessThanOrEqualTo(DateTime.Now.AddDays(1)),
					$"news item '{node.Key}' is dated in the future");
			}
		}
	}
}
