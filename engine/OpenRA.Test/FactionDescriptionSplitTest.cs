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

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Widgets.Logic;

namespace OpenRA.Test
{
	[TestFixture]
	public class FactionDescriptionSplitTest
	{
		// The two playable factions. Faction@randomside is deliberately excluded: its description
		// title ("Random Side") differs from its Name ("Any Side"), so it does not carry the
		// authoring convention asserted below.
		static readonly string[] PlayableFactions = { "america", "russia" };

		/// <summary>
		/// Reads the SHIPPED mods/ww3mod/rules/world.yaml, located by walking up from the test binary
		/// the way ScreenShakeModelTest.FindMod does.
		/// </summary>
		// This fixture used to hold a hardcoded literal commented "copied verbatim from world.yaml".
		// Nothing enforced that claim, so editing the shipped copy left this fixture green against a
		// stale quotation — a backlog item and a manager brief both cited the coupling as a gate that
		// did not exist. Reading the file is what makes an edit to the copy actually reach the
		// assertions below.
		static MiniYamlNode Faction(string internalName)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(dir.FullName, "mods", "ww3mod", "rules", "world.yaml");
				if (!File.Exists(candidate))
					continue;

				// The Faction@ blocks hang off ^BaseWorld, not off World.
				var faction = MiniYaml.FromFile(candidate)
					.Single(n => n.Key == "^BaseWorld").Value.Nodes
					.SingleOrDefault(n => n.Key.StartsWith("Faction@", StringComparison.Ordinal)
						&& n.Value.Nodes.Any(c => c.Key == "InternalName" && c.Value.Value == internalName));

				Assert.That(faction, Is.Not.Null,
					$"world.yaml no longer defines a Faction with InternalName: {internalName}");

				return faction;
			}

			throw new FileNotFoundException("could not locate mods/ww3mod/rules/world.yaml");
		}

		static string Field(MiniYamlNode faction, string key)
		{
			return faction.Value.Nodes.Single(n => n.Key == key).Value.Value;
		}

		[TestCaseSource(nameof(PlayableFactions))]
		public void MiniYamlLeavesTheDescriptionSeparatorEscaped(string internalName)
		{
			var description = Field(Faction(internalName), "Description");

			// If this ever fails, MiniYaml learned to unescape and the fix below is redundant.
			Assert.That(description, Does.Contain("\\n"),
				"MiniYaml must hand back the literal two-character escape — the fix depends on it");
			Assert.That(description, Does.Not.Contain("\n"),
				"a real newline here would mean the split already worked and there was never a bug");
		}

		[TestCaseSource(nameof(PlayableFactions))]
		public void TheFluentLayerHandsBackANonKeyStringUnchanged(string internalName)
		{
			// Descriptions are prose, not Fluent keys, so GetMessage falls through and returns the
			// input verbatim. This is the other half of the falsifier: Fluent does not unescape either.
			var bundle = new FluentBundle("en", "", _ => { });
			var description = Field(Faction(internalName), "Description");

			Assert.That(bundle.GetMessage(description), Is.EqualTo(description),
				"an unescape hiding in the Fluent layer would invalidate this whole diagnosis");
		}

		[TestCaseSource(nameof(PlayableFactions))]
		public void SplitDescriptionSeparatesTheTitleFromTheBody(string internalName)
		{
			var faction = Faction(internalName);
			var description = Field(faction, "Description");
			var (title, body) = LobbyUtils.SplitDescription(description);

			// Asserted against world.yaml's own Name field rather than against a literal, so renaming a
			// faction without updating the description head turns this red instead of passing on a copy
			// of the old name. The prose itself is NOT asserted — pinning it here is what went stale.
			Assert.That(title, Is.EqualTo(Field(faction, "Name")),
				"the tooltip title must be just the faction name, not the entire description string");
			Assert.That(body, Is.Not.Null.And.Not.Empty,
				"the tooltip body must carry the description text — an empty body is the reported bug");
			Assert.That(body.Length, Is.GreaterThan(20),
				"the body must be the description prose, not a stray fragment left by a bad split");
			Assert.That(title, Does.Not.Contain("\\n"), "no escape may survive into displayed text");
			Assert.That(body, Does.Not.Contain("\\n"), "no escape may survive into displayed text");

			// And nothing is dropped in the middle: the two parts rejoin to the whole description.
			Assert.That(title + "\n" + body, Is.EqualTo(description.Replace("\\n", "\n")));
		}

		[Test]
		public void SplitDescriptionStillSplitsARealNewline()
		{
			// Fluent-sourced translations arrive with real newlines. Unescaping must not break them.
			// This string is a SYNTHETIC stand-in, not a quotation of world.yaml — the shipped copy
			// carries the escape, and the whole point of this case is the real-newline path.
			var (title, body) = LobbyUtils.SplitDescription("Russia\nNATO's principal adversary.");

			Assert.That(title, Is.EqualTo("Russia"));
			Assert.That(body, Is.EqualTo("NATO's principal adversary."));
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
