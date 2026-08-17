#region Copyright & License Information
/*
 * WW3MOD lint-baseline tests — the floor that lets `--check-yaml` fail on NEW errors while a large
 * pre-existing tally stays red.
 *
 * The whole value of a baseline is that it cannot be quietly widened, so that is what these pin: the
 * prune path may only REMOVE entries from the enforced section, and a signature that is not recorded
 * must fail even when the totals happen to match. A baseline that can be regenerated in one step is a
 * baseline that gets regenerated to bury a regression; if a future change makes Prune capable of adding
 * a line, or makes an unrecorded error pass, these tests are what should stop it.
 */
#endregion

using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.UtilityCommands;

namespace OpenRA.Test
{
	[TestFixture]
	public class LintBaselineTest
	{
		static readonly string[] Sample =
		{
			"# a comment",
			"[repo]",
			"# since 2026-08-17",
			"map-a | Something is wrong.",
			"map-b | Something is wrong.",
			"",
			"[accepted]",
			"# this map has to breach the rule to be the test it is",
			"map-d | Something is wrong.",
			"",
			"[requires-ra-content]",
			"mod | :0: sprite file `x.shp` not found."
		};

		static readonly string[] AllSeen =
		{
			"map-a | Something is wrong.",
			"map-b | Something is wrong.",
			"map-d | Something is wrong.",
			"mod | :0: sprite file `x.shp` not found."
		};

		[Test]
		public void SignatureKeysOnTheFirstLineOfAMultiLineMessage()
		{
			Assert.That(
				LintBaseline.Signature("map-a", "This map does not define a valid cordon.\nA one cell border is required."),
				Is.EqualTo("map-a | This map does not define a valid cordon."));
		}

		[Test]
		public void SignatureScopesByMapSoASecondBadMapIsNotHiddenByTheFirst()
		{
			var a = LintBaseline.Signature("map-a", "Something is wrong.");
			var b = LintBaseline.Signature("map-b", "Something is wrong.");
			Assert.That(a, Is.Not.EqualTo(b));
		}

		[Test]
		public void ParseAssignsEntriesToSectionsAndDropsCommentsAndBlanks()
		{
			var entries = LintBaseline.Parse(Sample);
			Assert.That(entries.Count, Is.EqualTo(4));
			Assert.That(entries.Count(e => e.Section == LintBaseline.EnforcedSection), Is.EqualTo(2));
			Assert.That(entries.Count(e => e.Section == LintBaseline.AcceptedSection), Is.EqualTo(1));
			Assert.That(entries.Count(e => e.Section == "requires-ra-content"), Is.EqualTo(1));
		}

		[Test]
		public void ParseCarriesTheAmnestyDateForwardWithinItsSection()
		{
			// The date is what lets a run say how long the debt has stood; a section without one must not
			// inherit the previous section's.
			var entries = LintBaseline.Parse(Sample);
			Assert.That(entries.First(e => e.Text.StartsWith("map-a", System.StringComparison.Ordinal)).Since,
				Is.EqualTo("2026-08-17"));
			Assert.That(entries.First(e => e.Text.StartsWith("mod |", System.StringComparison.Ordinal)).Since,
				Is.Null);
		}

		[Test]
		public void AnUnrecordedErrorIsReportedAsNew()
		{
			var seen = AllSeen.Append("map-c | Something is wrong.").ToArray();
			var (added, fixedUp, _) = LintBaseline.Compare(LintBaseline.Parse(Sample), seen);
			Assert.That(added, Is.EqualTo(new[] { "map-c | Something is wrong." }));
			Assert.That(fixedUp, Is.Empty);
		}

		[Test]
		public void TheSameErrorCountWithADifferentErrorStillFails()
		{
			// The trap a count-based floor walks into: one map fixed, another broken, total unchanged.
			var seen = AllSeen.Where(s => !s.StartsWith("map-b", System.StringComparison.Ordinal))
				.Append("map-c | Something is wrong.").ToArray();

			var (added, fixedUp, _) = LintBaseline.Compare(LintBaseline.Parse(Sample), seen);
			Assert.That(added, Is.EqualTo(new[] { "map-c | Something is wrong." }));
			Assert.That(fixedUp, Is.EqualTo(new[] { "map-b | Something is wrong." }));
		}

		[Test]
		public void AnEnforcedEntryThatStopsOccurringIsReportedAsFixed()
		{
			var seen = AllSeen.Where(s => !s.StartsWith("map-b", System.StringComparison.Ordinal)).ToArray();
			var (added, fixedUp, absent) = LintBaseline.Compare(LintBaseline.Parse(Sample), seen);
			Assert.That(added, Is.Empty);
			Assert.That(fixedUp, Is.EqualTo(new[] { "map-b | Something is wrong." }));
			Assert.That(absent, Is.Empty);
		}

		[Test]
		public void AnAcceptedExceptionThatStopsOccurringMustAlsoBeRemoved()
		{
			// A deliberate exception is not a licence to leave a dead line behind: the map it describes may
			// have been changed, and then the exception is a claim about nothing.
			var seen = AllSeen.Where(s => !s.StartsWith("map-d", System.StringComparison.Ordinal)).ToArray();
			var (_, fixedUp, _) = LintBaseline.Compare(LintBaseline.Parse(Sample), seen);
			Assert.That(fixedUp, Is.EqualTo(new[] { "map-d | Something is wrong." }));
		}

		[Test]
		public void AnEnvironmentDependentEntryThatDoesNotOccurIsNotTreatedAsFixed()
		{
			// A developer machine with RA content installed sees none of that section. That must not
			// look like progress, and must not fail the run either.
			var seen = AllSeen.Where(s => !s.StartsWith("mod |", System.StringComparison.Ordinal)).ToArray();
			var (added, fixedUp, absent) = LintBaseline.Compare(LintBaseline.Parse(Sample), seen);
			Assert.That(added, Is.Empty);
			Assert.That(fixedUp, Is.Empty);
			Assert.That(absent, Is.EqualTo(new[] { "mod | :0: sprite file `x.shp` not found." }));
		}

		[Test]
		public void ClassOfGroupsTheSameRuleFailingInDifferentPlaces()
		{
			// 62 maps failing one rule is one fix, and the run should be able to say so.
			Assert.That(
				LintBaseline.ClassOf("map-a | The player `Multi0` must specify LockFaction: True."),
				Is.EqualTo(LintBaseline.ClassOf("map-b | The player `Multi5` must specify LockFaction: True.")));

			Assert.That(
				LintBaseline.ClassOf("map-a | This map does not define a valid cordon."),
				Is.Not.EqualTo(LintBaseline.ClassOf("map-a | The player `Multi0` must specify LockFaction: True.")));
		}

		[Test]
		public void PruneRemovesOnlyTheNamedEnforcedEntriesAndKeepsEverythingElseInPlace()
		{
			var pruned = LintBaseline.Prune(Sample, new[] { "map-b | Something is wrong." });
			Assert.That(pruned, Is.EqualTo(Sample.Where(l => l != "map-b | Something is wrong.")));
		}

		[Test]
		public void PruneCannotRaiseTheFloor()
		{
			// Pruning is the one automated edit, so it must be incapable of adding an entry — including
			// when handed the very signature a caller might want silenced.
			var pruned = LintBaseline.Prune(Sample, new[] { "map-c | A brand new error." });
			Assert.That(pruned, Is.EqualTo(Sample));

			var entriesBefore = LintBaseline.Parse(Sample).Count;
			Assert.That(LintBaseline.Parse(pruned).Count, Is.LessThanOrEqualTo(entriesBefore));
		}

		[Test]
		public void PruneWillNotTouchAnEnvironmentDependentEntry()
		{
			var pruned = LintBaseline.Prune(Sample, new[] { "mod | :0: sprite file `x.shp` not found." });
			Assert.That(pruned, Is.EqualTo(Sample));
		}
	}
}
