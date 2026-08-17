#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenRA.Mods.Common.UtilityCommands
{
	// Records which --check-yaml errors are already known, so that a run fails on NEW errors only.
	// The mechanism is deliberately one-directional: it can REMOVE entries that no longer occur, and it can
	// never add one. Raising the floor means hand-editing the file, which shows up as added lines in review.
	public static class LintBaseline
	{
		public const string FileName = "lint-baseline.txt";

		// Errors the repository owns and has not fixed. This is DEBT, it is reported as a standing amnesty on
		// every run, and it is the only number here that is supposed to fall.
		public const string EnforcedSection = "repo";

		// Errors that are correct and must keep occurring — a map that has to breach a rule to be the test it
		// is. Enforced identically (an entry that stops occurring still has to be removed), but never counted
		// as debt, because counting a deliberate decision as debt is how a debt figure stops meaning anything.
		public const string AcceptedSection = "accepted";

		// Any other section is environment-dependent: matched so it cannot fail a run, never required to
		// occur, and never pruned — the environment that does not produce it has nothing to say about it.
		static readonly string[] Enforced = { EnforcedSection, AcceptedSection };

		public const string PruneEnvironmentVariable = "LINT_BASELINE_PRUNE";

		public const string SinceMarker = "# since ";

		public static string Signature(string scope, string message)
		{
			// Several lint errors carry a multi-line explanation under a one-line summary; key on the summary.
			var end = message.IndexOfAny(new[] { '\r', '\n' });
			var firstLine = (end < 0 ? message : message.Substring(0, end)).Trim();
			return $"{scope} | {firstLine}";
		}

		public static List<(string Section, string Text, string Since)> Parse(IEnumerable<string> lines)
		{
			var entries = new List<(string Section, string Text, string Since)>();
			var section = EnforcedSection;
			string since = null;
			foreach (var raw in lines)
			{
				var line = raw.Trim();
				if (line.StartsWith(SinceMarker, StringComparison.Ordinal))
				{
					since = line.Substring(SinceMarker.Length).Trim();
					continue;
				}

				if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
					continue;

				if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
				{
					section = line.Substring(1, line.Length - 2).Trim();
					since = null;
					continue;
				}

				entries.Add((section, line, since));
			}

			return entries;
		}

		public static (List<string> New, List<string> Fixed, List<string> AbsentElsewhere) Compare(
			IReadOnlyList<(string Section, string Text, string Since)> baseline, IReadOnlyCollection<string> seen)
		{
			var known = baseline.Select(e => e.Text).ToHashSet(StringComparer.Ordinal);
			var seenSet = seen.ToHashSet(StringComparer.Ordinal);

			var added = seenSet.Where(s => !known.Contains(s)).OrderBy(s => s, StringComparer.Ordinal).ToList();
			var fixedUp = baseline.Where(e => Enforced.Contains(e.Section) && !seenSet.Contains(e.Text))
				.Select(e => e.Text).OrderBy(s => s, StringComparer.Ordinal).ToList();
			var absent = baseline.Where(e => !Enforced.Contains(e.Section) && !seenSet.Contains(e.Text))
				.Select(e => e.Text).OrderBy(s => s, StringComparer.Ordinal).ToList();

			return (added, fixedUp, absent);
		}

		// The message with its quoted specifics blanked, so that 62 maps failing the same rule read as one
		// class of 62 rather than 62 unrelated lines. What a plan gets made out of.
		public static string ClassOf(string signature)
		{
			var split = signature.IndexOf(" | ", StringComparison.Ordinal);
			var message = split < 0 ? signature : signature.Substring(split + 3);
			var parts = message.Split('`');
			for (var i = 1; i < parts.Length; i += 2)
				parts[i] = "X";

			return string.Join("`", parts);
		}

		// Removes the given entries from the enforced sections. Never adds, never touches an
		// environment-dependent section, and leaves every comment and blank line where it was.
		public static List<string> Prune(IEnumerable<string> lines, IReadOnlyCollection<string> remove)
		{
			var kept = new List<string>();
			var section = EnforcedSection;
			foreach (var raw in lines)
			{
				var line = raw.Trim();
				if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
					section = line.Substring(1, line.Length - 2).Trim();
				else if (Enforced.Contains(section) && line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal)
					&& remove.Contains(line))
					continue;

				kept.Add(raw);
			}

			return kept;
		}

		public static bool Judge(ModData modData, IReadOnlyCollection<string> seen, int errorCount, Action<string> output)
		{
			var path = Path.Combine(modData.Manifest.Package.Name, FileName);
			if (!File.Exists(path))
			{
				if (errorCount > 0)
					output($"No lint baseline at {path}; every error counts as new.");

				return errorCount == 0;
			}

			var lines = File.ReadAllLines(path);
			var baseline = Parse(lines);
			var (added, fixedUp, absent) = Compare(baseline, seen);
			var pruning = Environment.GetEnvironmentVariable(PruneEnvironmentVariable)?
				.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;

			output("");
			output($"Lint baseline: {path}");
			output($"  {errorCount} errors emitted, {seen.Count} distinct signatures.");

			foreach (var e in added)
				output($"  NEW: {e}");

			if (added.Count > 0)
			{
				output($"  {added.Count} lint error(s) are not recorded. Fix them, or record them deliberately: " +
					$"[{EnforcedSection}] if it is debt someone will pay, [{AcceptedSection}] with a reason if the " +
					"error is correct and must keep occurring.");
				return false;
			}

			if (fixedUp.Count > 0)
			{
				foreach (var e in fixedUp)
					output($"  FIXED: {e}");

				if (!pruning)
				{
					output($"  {fixedUp.Count} recorded entr(y/ies) no longer occur. The floor drops with the fix: " +
						$"re-run with {PruneEnvironmentVariable}=true, then commit {FileName}.");
					return false;
				}

				File.WriteAllText(path, string.Join("\n", Prune(lines, fixedUp.ToHashSet(StringComparer.Ordinal))) + "\n");
				output($"  Pruned {fixedUp.Count} entr(y/ies). Commit {FileName} with the fix.");
			}

			ReportAmnesty(baseline, fixedUp, absent, output);
			return true;
		}

		// Printed last, and phrased as what it is. A baseline's failure mode is not that it lets a regression
		// through — the signature check handles that — it is that the recorded list turns into furniture that
		// everyone stops reading. So the run says out loud how much is being forgiven, for how long it has been
		// forgiven, and which single fix would remove the most of it.
		static void ReportAmnesty(
			IReadOnlyList<(string Section, string Text, string Since)> baseline,
			IReadOnlyCollection<string> justFixed,
			IReadOnlyCollection<string> absent,
			Action<string> output)
		{
			var debt = baseline
				.Where(e => e.Section == EnforcedSection && !justFixed.Contains(e.Text))
				.ToList();

			var accepted = baseline.Count(e => e.Section == AcceptedSection);
			var environmental = baseline.Count(e => !Enforced.Contains(e.Section));

			if (debt.Count == 0)
			{
				output($"  No lint debt. {accepted} accepted exception(s), {environmental} environment-dependent.");
				return;
			}

			var oldest = debt.Where(e => e.Since != null).Select(e => e.Since).OrderBy(s => s, StringComparer.Ordinal)
				.FirstOrDefault();
			var age = "";
			if (oldest != null && DateTime.TryParse(oldest, out var since))
				age = $", oldest amnestied {oldest} ({(int)(DateTime.UtcNow - since).TotalDays} days ago)";

			var largest = debt.GroupBy(e => ClassOf(e.Text)).OrderByDescending(g => g.Count()).First();

			output($"  STANDING AMNESTY: {debt.Count} lint error(s) are recorded as known-bad and did not fail " +
				$"this run{age}.");
			output($"  Largest class: {largest.Count()} x \"{largest.Key}\"");
			if (accepted > 0)
				output($"  ({accepted} further entr(y/ies) are accepted on purpose and are not debt.)");

			if (environmental > 0)
				output($"  ({environmental} environment-dependent, {absent.Count} of which did not occur here.)");
		}
	}
}
