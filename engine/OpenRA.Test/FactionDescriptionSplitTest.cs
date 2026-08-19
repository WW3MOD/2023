#region Copyright & License Information
/*
 * WW3MOD faction tooltip description tests.
 *
 * These pin the fix for faction tooltips that showed their whole description as the title and an
 * empty body — every faction, every time. Commit 75ac6941 wrote good description bodies but could
 * not make them visible, because the defect is on the read side, not in the copy: MiniYaml stores
 * "Title\nBody" with a LITERAL backslash-n (it does not unescape, which is why eight other engine
 * sites hand-roll Replace("\\n", "\n")), while SplitOnFirstToken splits on a REAL newline. The
 * search misses, so the whole string becomes the title and the body comes back null.
 *
 * The load-bearing property is that a description authored as "Title\nBody" in MiniYaml reaches the
 * tooltip as TWO NON-EMPTY PARTS. That cannot be caught without a mouse click on a lobby dropdown,
 * so it is pinned here instead of by screenshot.
 *
 * The first two tests are the falsifier for the whole diagnosis: if either MiniYaml or the Fluent
 * layer unescaped the separator, there would be no bug and the fix below would be wrong.
 */
#endregion

using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Widgets.Logic;

namespace OpenRA.Test
{
	[TestFixture]
	public class FactionDescriptionSplitTest
	{
		// Copied verbatim from mods/ww3mod/rules/world.yaml (Faction@0), including the escape.
		const string FactionYaml =
			"Faction@0:\n" +
			"\tName: America\n" +
			"\tInternalName: america\n" +
			"\tDescription: America\\nNATO's lead power. Precision airpower, networked armour and air " +
			"cavalry: fewer units, costlier, striking first and at range.\n";

		static string DescriptionFromYaml()
		{
			return MiniYaml.FromString(FactionYaml, "")
				.Single(n => n.Key == "Faction@0").Value.Nodes
				.Single(n => n.Key == "Description").Value.Value;
		}

		[Test]
		public void MiniYamlLeavesTheDescriptionSeparatorEscaped()
		{
			var description = DescriptionFromYaml();

			// If this ever fails, MiniYaml learned to unescape and the fix below is redundant.
			Assert.That(description, Does.Contain("\\n"),
				"MiniYaml must hand back the literal two-character escape — the fix depends on it");
			Assert.That(description, Does.Not.Contain("\n"),
				"a real newline here would mean the split already worked and there was never a bug");
		}

		[Test]
		public void TheFluentLayerHandsBackANonKeyStringUnchanged()
		{
			// Descriptions are prose, not Fluent keys, so GetMessage falls through and returns the
			// input verbatim. This is the other half of the falsifier: Fluent does not unescape either.
			var bundle = new FluentBundle("en", "", _ => { });
			var description = DescriptionFromYaml();

			Assert.That(bundle.GetMessage(description), Is.EqualTo(description),
				"an unescape hiding in the Fluent layer would invalidate this whole diagnosis");
		}

		[Test]
		public void SplitDescriptionSeparatesTheTitleFromTheBody()
		{
			var (title, body) = LobbyUtils.SplitDescription(DescriptionFromYaml());

			Assert.That(title, Is.EqualTo("America"),
				"the tooltip title must be just the faction name, not the entire description string");
			Assert.That(body, Is.Not.Null.And.Not.Empty,
				"the tooltip body must carry the description text — an empty body is the reported bug");
			Assert.That(body, Does.StartWith("NATO's lead power."));
			Assert.That(body, Does.Not.Contain("\\n"), "no escape may survive into displayed text");
		}

		[Test]
		public void SplitDescriptionStillSplitsARealNewline()
		{
			// Fluent-sourced translations arrive with real newlines. Unescaping must not break them.
			var (title, body) = LobbyUtils.SplitDescription("Russia\nThe BRICS bloc's spearhead.");

			Assert.That(title, Is.EqualTo("Russia"));
			Assert.That(body, Is.EqualTo("The BRICS bloc's spearhead."));
		}

		[Test]
		public void SplitDescriptionOfNullIsEmpty()
		{
			// Faction.Description is optional; the call sites pass null straight through.
			var (title, body) = LobbyUtils.SplitDescription(null);

			Assert.That(title, Is.Null);
			Assert.That(body, Is.Null);
		}
	}
}
