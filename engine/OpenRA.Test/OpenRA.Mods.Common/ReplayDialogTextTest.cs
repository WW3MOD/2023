#region Copyright & License Information
/*
 * WW3MOD replay dialog text tests.
 *
 * The confirmation prompt does not wrap. ConfirmationDialogs splits the message on '\n' and clones
 * one fixed-height label per line; PROMPT_TEXT in confirmation-dialogs.yaml is 340px wide with
 * Height: 20 and no WordWrap (LabelWidget defaults it false). A long single-line string is therefore
 * not shrunk or wrapped - it is drawn past the edge of the panel and clipped.
 *
 * The first version of the two build-warning strings was 312 and 340 characters on one line, against
 * a group where nothing else exceeded ~60. Nobody caught it because nobody could open the dialog:
 * these paths need a replay recorded by a different build, and this session could not launch the
 * game at all. So the property is pinned here instead, mechanically and against the real shipped
 * file rather than a copy of it.
 *
 * The bound is not a magic number - it is measured from the strings in the SAME dialog that are
 * known to render today. That way the test says what we actually mean ("no worse than what already
 * works") and cannot rot when the panel is resized.
 */
#endregion

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace OpenRA.Test
{
	[TestFixture]
	public class ReplayDialogTextTest
	{
		const string Group = "dialog-incompatible-replay";

		// Pre-existing strings in the same dialog, with representative arguments. These render today.
		static readonly (string Key, object[] Args)[] KnownGood =
		{
			($"{Group}.prompt", null),
			($"{Group}.prompt-unknown-version", null),
			($"{Group}.prompt-unknown-mod", null),
			($"{Group}.prompt-unavailable-mod", new object[] { "mod", "ww3mod" }),
			($"{Group}.prompt-incompatible-version", new object[] { "version", "release-20230225" }),
			($"{Group}.prompt-unavailable-map", new object[] { "map", "0123456789abcdef" })
		};

		// The strings added for the build warning, with the longest argument DescribeReplayDifference
		// can actually produce.
		static readonly (string Key, object[] Args)[] UnderTest =
		{
			($"{Group}.prompt-incompatible-build", new object[] { "difference", "engine build and mod rules" }),
			($"{Group}.prompt-unverifiable-build", null)
		};

		static FluentBundle Bundle()
		{
			var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "mods", "common", "fluent", "common.ftl"));
			Assert.That(File.Exists(path), Is.True, $"could not find the shipped fluent file at {path}");

			return new FluentBundle("en", File.ReadAllText(path), e => Assert.Fail(e.Message));
		}

		static int LongestLine(FluentBundle bundle, string key, object[] args)
		{
			Assert.That(bundle.TryGetMessage(key, out var message, args), Is.True, $"`{key}` is missing from common.ftl");

			// ConfirmationDialogs.cs splits on '\n' and on nothing else.
			return message.Split('\n').Max(l => l.Length);
		}

		[Test]
		public void BuildWarningsFitTheDialogThatAlreadyRenders()
		{
			var bundle = Bundle();
			var budget = KnownGood.Max(k => LongestLine(bundle, k.Key, k.Args));

			foreach (var (key, args) in UnderTest)
			{
				var longest = LongestLine(bundle, key, args);
				Assert.That(longest, Is.LessThanOrEqualTo(budget),
					$"`{key}` has a {longest}-character line; the widest line in this dialog that is known to " +
					$"render is {budget}. The prompt does not wrap, so the excess is drawn off the panel and " +
					"clipped. Break the string across more lines rather than raising this bound.");
			}
		}

		// The mechanism the fix relies on. If Fluent ever joined continuation lines with a space
		// instead of a newline, the test above would still pass on a string that renders as one long
		// clipped line, so assert the newlines are really there.
		[Test]
		public void BuildWarningsActuallyContainLineBreaks()
		{
			var bundle = Bundle();

			foreach (var (key, args) in UnderTest)
			{
				Assert.That(bundle.TryGetMessage(key, out var message, args), Is.True);
				Assert.That(message, Does.Contain("\n"),
					$"`{key}` must be broken across lines; the dialog grows per line and never wraps");
			}
		}

		// The warning names what moved, so the argument has to survive into the rendered string.
		[Test]
		public void TheBuildWarningNamesTheDifference()
		{
			var bundle = Bundle();
			Assert.That(bundle.TryGetMessage(
				$"{Group}.prompt-incompatible-build", out var message, new object[] { "difference", "engine build" }), Is.True);

			Assert.That(message, Does.Contain("engine build"));
		}
	}
}
