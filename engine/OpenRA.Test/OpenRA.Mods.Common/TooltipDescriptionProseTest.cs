#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
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
using NUnit.Framework;

namespace OpenRA.Test
{
	/// <summary>
	/// <para>Buildable.Description is prose. The production tooltip generates a section per weapon,
	/// plus Carries, Health, Speed, Call-in, Upkeep and Full refill rows, so a description that also
	/// lists those facts states them twice — and, being hand-written, eventually states them
	/// differently. pbox and hbox said "Garrisons 2 soldiers" against a Cargo.MaxWeight of 4;
	/// bradley said "2x wire-guided ATGMs" against a pool of 8.</para>
	///
	/// <para>Reads the shipped rules rather than a fixture: the failure mode being guarded against is
	/// a new actor authored in the old style, which no fixture would ever contain.</para>
	/// </summary>
	[TestFixture]
	public class TooltipDescriptionProseTest
	{
		static string FindRulesDir()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(dir.FullName, "mods", "ww3mod", "rules");
				if (Directory.Exists(candidate))
					return candidate;
			}

			throw new DirectoryNotFoundException("could not locate mods/ww3mod/rules");
		}

		/// <summary>Every uncommented Description: line in the shipped rules, with its origin.</summary>
		static IEnumerable<(string File, int Line, string Text)> LiveDescriptions()
		{
			foreach (var path in Directory.EnumerateFiles(FindRulesDir(), "*.yaml", SearchOption.AllDirectories))
			{
				var lines = File.ReadAllLines(path);
				for (var i = 0; i < lines.Length; i++)
				{
					var trimmed = lines[i].TrimStart();

					// Commented-out actors are kept in these files in bulk (all of naval.yaml, most of
					// the support powers in player.yaml). They render nothing, so they are not held to
					// this rule — reformatting them would be churn against text no player can reach.
					if (trimmed.StartsWith("#", StringComparison.Ordinal))
						continue;

					if (!trimmed.StartsWith("Description:", StringComparison.Ordinal))
						continue;

					yield return (Path.GetFileName(path), i + 1, trimmed.Substring("Description:".Length).Trim());
				}
			}
		}

		[Test]
		public void NoLiveDescriptionIsAuthoredAsABulletList()
		{
			var offenders = LiveDescriptions()
				.Where(d => d.Text.Contains("\\n - ", StringComparison.Ordinal))
				.Select(d => $"{d.File}:{d.Line}")
				.ToList();

			Assert.That(offenders, Is.Empty,
				"These descriptions carry a ' - ' bullet list. The tooltip already renders a section " +
				"per weapon and a row per stat below the prose, so a list here restates them. Put the " +
				"fact in a sentence, or leave it to the row that generates it: " +
				string.Join(", ", offenders));
		}

		[Test]
		public void TheAuditFoundDescriptionsToLookAt()
		{
			// Without this the test above passes just as happily when FindRulesDir resolves to an
			// empty tree, which is exactly what a broken test-output layout looks like.
			Assert.That(LiveDescriptions().Count(), Is.GreaterThan(40),
				"Found almost no Description: lines in mods/ww3mod/rules. The scan is not reaching " +
				"the shipped rules and the assertion above is measuring nothing.");
		}
	}
}
