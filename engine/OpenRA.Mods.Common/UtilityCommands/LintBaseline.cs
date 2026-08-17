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

		// Entries in this section are errors the repository itself owns; they are reproducible anywhere the
		// repo is checked out, so one of them no longer occurring means it was FIXED and the floor must drop.
		// Entries in any other section are environment-dependent (see the file's own header) and are matched,
		// but never required to occur.
		public const string EnforcedSection = "repo";

		public const string PruneEnvironmentVariable = "LINT_BASELINE_PRUNE";

		public static string Signature(string scope, string message)
		{
			// Several lint errors carry a multi-line explanation under a one-line summary; key on the summary.
			var end = message.IndexOfAny(new[] { '\r', '\n' });
			var firstLine = (end < 0 ? message : message.Substring(0, end)).Trim();
			return $"{scope} | {firstLine}";
		}

		public static List<(string Section, string Text)> Parse(IEnumerable<string> lines)
		{
			var entries = new List<(string Section, string Text)>();
			var section = EnforcedSection;
			foreach (var raw in lines)
			{
				var line = raw.Trim();
				if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
					continue;

				if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
				{
					section = line.Substring(1, line.Length - 2).Trim();
					continue;
				}

				entries.Add((section, line));
			}

			return entries;
		}

		public static (List<string> New, List<string> Fixed, List<string> AbsentElsewhere) Compare(
			IReadOnlyList<(string Section, string Text)> baseline, IReadOnlyCollection<string> seen)
		{
			var known = baseline.Select(e => e.Text).ToHashSet(StringComparer.Ordinal);
			var seenSet = seen.ToHashSet(StringComparer.Ordinal);

			var added = seenSet.Where(s => !known.Contains(s)).OrderBy(s => s, StringComparer.Ordinal).ToList();
			var fixedUp = baseline.Where(e => e.Section == EnforcedSection && !seenSet.Contains(e.Text))
				.Select(e => e.Text).OrderBy(s => s, StringComparer.Ordinal).ToList();
			var absent = baseline.Where(e => e.Section != EnforcedSection && !seenSet.Contains(e.Text))
				.Select(e => e.Text).OrderBy(s => s, StringComparer.Ordinal).ToList();

			return (added, fixedUp, absent);
		}

		// Removes the given entries from the enforced section. Never adds, never touches another section,
		// and leaves every comment and blank line where it was.
		public static List<string> Prune(IEnumerable<string> lines, IReadOnlyCollection<string> remove)
		{
			var kept = new List<string>();
			var section = EnforcedSection;
			foreach (var raw in lines)
			{
				var line = raw.Trim();
				if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
					section = line.Substring(1, line.Length - 2).Trim();
				else if (section == EnforcedSection && line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal)
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
			var enforced = baseline.Count(e => e.Section == EnforcedSection);
			var pruning = Environment.GetEnvironmentVariable(PruneEnvironmentVariable)?
				.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;

			output("");
			output($"Lint baseline: {path}");
			output($"  {errorCount} errors emitted, {seen.Count} distinct signatures.");
			output($"  repo-owned lint debt: {enforced - fixedUp.Count} of {enforced} recorded entries still occur.");
			if (baseline.Count > enforced)
				output($"  environment-dependent: {baseline.Count - enforced} recorded, {absent.Count} did not occur here.");

			foreach (var e in added)
				output($"  NEW: {e}");

			if (added.Count > 0)
			{
				output($"  {added.Count} lint error(s) are not in the baseline. Fix them, or add them to " +
					$"[{EnforcedSection}] in {FileName} with a note saying why they are acceptable.");
				return false;
			}

			if (fixedUp.Count == 0)
			{
				output("  0 new. Baseline holds.");
				return true;
			}

			foreach (var e in fixedUp)
				output($"  FIXED: {e}");

			if (!pruning)
			{
				output($"  {fixedUp.Count} baseline entr(y/ies) no longer occur. The floor must drop with the fix: " +
					$"re-run with {PruneEnvironmentVariable}=true to remove them.");
				return false;
			}

			File.WriteAllText(path, string.Join("\n", Prune(lines, fixedUp.ToHashSet(StringComparer.Ordinal))) + "\n");
			output($"  Pruned {fixedUp.Count} entr(y/ies) from [{EnforcedSection}]. Commit {FileName} with the fix.");
			return true;
		}
	}
}
