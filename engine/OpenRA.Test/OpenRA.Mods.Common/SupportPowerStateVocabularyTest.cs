#region Copyright & License Information
/*
 * WW3MOD — pins the vocabulary `Test.GetSupportPowerState` returns, and checks the Lua that
 * consumes it against that vocabulary.
 *
 * THE BUG THIS EXISTS FOR, because it cost a scenario run and nearly cost three more. The binding
 * originally suffixed every return with " (bin: k1,k2)", so it answered two questions in one
 * string. Three of its four callers then compared the result against a bare token:
 *
 *     if nukeState ~= "hidden" then ...        -- can never match "hidden (bin: KinzhalStrike)"
 *
 * The scenario failed reporting `state 'hidden (bin: KinzhalStrike)', where 'hidden' was required`
 * — the shipped game behaving EXACTLY as designed, and the test unable to say so. The other two
 * were worse in kind rather than better: their comparisons were always-true or never-true, so one
 * would have failed the same way and one became a guard that could not fire, silently.
 *
 * The fix was to the BINDING, not to four careful callers: `GetSupportPowerState` now returns one
 * bare token and `GetSupportPowerBin` returns the key list. This fixture keeps it that way from
 * both ends.
 *
 * WHICH TEST HERE WOULD HAVE CAUGHT THE ORIGINAL, and it is worth being precise because the other
 * one would not have: TokensAreBare. The Lua-side check validates that callers compare against
 * tokens the binding can produce, and `"hidden"` always WAS producible — it was the return value
 * that carried the decoration. So TokensAreBare is the load-bearing one; the Lua scan covers the
 * neighbouring mistake of a caller inventing a token that does not exist.
 *
 * WHAT NEITHER CHECKS: whether the binding returns the RIGHT token for a given world state. That
 * needs a World and a Player, so it lives in the scenarios (test-tacnuke-delivers asserts the
 * enabled reading, test-tacnuke-lobby-gated-off the gated one, on the same power).
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using OpenRA.Mods.Common.Scripting.Global;

namespace OpenRA.Test
{
	[TestFixture]
	public class SupportPowerStateVocabularyTest
	{
		[Test]
		public void TokensAreBare()
		{
			// THE ONE THAT WOULD HAVE CAUGHT IT. A token carrying a space, a bracket or a comma is
			// not a token a scenario can compare against with `==`, which is the only way any of
			// them is used.
			foreach (var token in TestGlobal.SupportPowerState.Fixed)
			{
				Assert.That(token, Is.Not.Null.And.Not.Empty);
				Assert.That(token, Does.Not.Contain(" "),
					$"'{token}' carries a space. Every caller compares these with `==` from Lua, so " +
					"anything appended to a token makes that comparison unsatisfiable — which is " +
					"exactly how the bin listing broke three of four call sites. Put extra " +
					"information in GetSupportPowerBin, or in a new binding of its own.");
				Assert.That(token, Does.Not.Contain("("), $"'{token}' carries a bracket");
				Assert.That(token, Does.Not.Contain(","), $"'{token}' carries a comma");
			}

			Assert.That(TestGlobal.SupportPowerState.ChargingPrefix, Does.EndWith(":"),
				"the one value-carrying token must stay prefix-matchable, since callers cannot " +
				"predict the tick count");
			Assert.That(TestGlobal.SupportPowerState.ChargingPrefix, Does.Not.Contain(" "));
		}

		[Test]
		public void TheTokenSetIsWhatTheScenariosExpect()
		{
			// Spelled out rather than derived, so that RENAMING a token is a deliberate act that
			// shows up here and points at the Lua that has to change with it.
			Assert.That(TestGlobal.SupportPowerState.Fixed,
				Is.EquivalentTo(new[] { "ready", "hidden", "absent", "no-manager" }));
		}

		[Test]
		public void IsValidAcceptsExactlyTheProducibleTokens()
		{
			foreach (var token in TestGlobal.SupportPowerState.Fixed)
				Assert.That(TestGlobal.SupportPowerState.IsValid(token), Is.True, token);

			Assert.That(TestGlobal.SupportPowerState.IsValid("charging:0"), Is.True);
			Assert.That(TestGlobal.SupportPowerState.IsValid("charging:2998"), Is.True);

			Assert.That(TestGlobal.SupportPowerState.IsValid("charging:"), Is.False, "no ticks");
			Assert.That(TestGlobal.SupportPowerState.IsValid("charging:x"), Is.False, "not a number");
			Assert.That(TestGlobal.SupportPowerState.IsValid("hidden (bin: KinzhalStrike)"), Is.False,
				"the decorated form the binding used to return must not validate — this is the " +
				"shape of the original defect");
			Assert.That(TestGlobal.SupportPowerState.IsValid("not-ready:5"), Is.False,
				"that is ActivateSupportPower's vocabulary, not this one; the two are easy to confuse");
			Assert.That(TestGlobal.SupportPowerState.IsValid(null), Is.False);
		}

		[Test]
		public void IsDrawnMatchesWhatTheBinActuallyDraws()
		{
			// SupportPowersWidget.cs:136 draws every power that is not Disabled, so a CHARGING power
			// has an icon. Getting this backwards would make the gated-off scenario pass on a power
			// that was merely slow.
			Assert.That(TestGlobal.SupportPowerState.IsDrawn("ready"), Is.True);
			Assert.That(TestGlobal.SupportPowerState.IsDrawn("charging:2998"), Is.True,
				"a charging power IS drawn — it shows a clock, not nothing");
			Assert.That(TestGlobal.SupportPowerState.IsDrawn("hidden"), Is.False);
			Assert.That(TestGlobal.SupportPowerState.IsDrawn("absent"), Is.False);
			Assert.That(TestGlobal.SupportPowerState.IsDrawn("no-manager"), Is.False);
		}

		[Test]
		public void EveryScenarioComparesAgainstAProducibleToken()
		{
			// The Lua-side half. For each scenario variable assigned from Test.GetSupportPowerState,
			// every string literal that variable is compared against must be something the binding
			// can actually return. Catches a caller inventing or misspelling a token — the failure
			// mode next door to the one that bit, and equally silent, because a comparison that can
			// never be true reads as "the condition simply did not happen".
			var offenders = new List<string>();

			foreach (var path in ScenarioScripts())
			{
				var text = File.ReadAllText(path);
				var stateVars = Regex.Matches(text, @"(\w+)\s*=\s*Test\.GetSupportPowerState\(")
					.Select(m => m.Groups[1].Value)
					.ToHashSet();

				if (stateVars.Count == 0)
					continue;

				foreach (var v in stateVars)
					foreach (Match m in Regex.Matches(text, $@"\b{Regex.Escape(v)}\s*[=~]=\s*""([^""]*)"""))
					{
						var literal = m.Groups[1].Value;

						// Scenarios seed these variables with a sentinel before the first read and
						// compare against it to mean "never sampled". That is deliberate and is not
						// a token claim.
						if (literal == "never-read")
							continue;

						if (!TestGlobal.SupportPowerState.IsValid(literal))
							offenders.Add($"{Path.GetFileName(path)}: {v} compared against \"{literal}\"");
					}
			}

			Assert.That(offenders, Is.Empty,
				"these comparisons can never be true, because Test.GetSupportPowerState cannot " +
				"return those strings:\n  " + string.Join("\n  ", offenders));
		}

		[Test]
		public void NoScenarioStillExpectsTheBinToBeAppendedToAState()
		{
			// The specific regression, named. If the suffix ever comes back, a scenario written
			// against it would carry this substring — and so would a stale comparison left behind
			// after the split.
			var offenders = ScenarioScripts()
				.Where(p => File.ReadAllLines(p)
					.Any(l => !l.TrimStart().StartsWith("--", StringComparison.Ordinal) && l.Contains("(bin: ")))
				.Select(Path.GetFileName)
				.ToArray();

			Assert.That(offenders, Is.Empty,
				"scenario code (not comments) still references the \" (bin: ...)\" suffix that " +
				$"GetSupportPowerState no longer returns: {string.Join(", ", offenders)}. The key " +
				"list comes from Test.GetSupportPowerBin now.");
		}

		static string[] ScenarioScripts()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null)
			{
				var candidate = Path.Combine(dir.FullName, "tools", "autotest", "scenarios");
				if (Directory.Exists(candidate))
					return Directory.GetFiles(candidate, "*.lua", SearchOption.AllDirectories);

				dir = dir.Parent;
			}

			throw new DirectoryNotFoundException("could not locate tools/autotest/scenarios");
		}
	}
}
