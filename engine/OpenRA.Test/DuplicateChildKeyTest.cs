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
using NUnit.Framework;

namespace OpenRA.Test
{
	/// <summary>
	/// A top-level node that repeats one of its own direct child keys is a LATENT mod-wide load
	/// failure, and the reason it is worth a standing fixture is that it does not fail while it is
	/// being written — it fails much later, in a file nobody has touched, when someone finally
	/// overrides that actor.
	///
	/// Inside a single source the duplicate is quietly folded together: MergeIntoResolved keys the
	/// node list with a HashSet and merges the later node over the earlier one in place
	/// (MiniYaml.cs:427-446), so the actor loads and plays with one trait whose fields are the union.
	/// Nothing is reported. The dictionary-ification that catches it only runs when a SECOND source
	/// defines the same top-level key: MergeNode then calls the MiniYaml-pair MergePartial (:587),
	/// whose first act is to run each side's own child list through IntoDictionaryWithConflictLog
	/// (:525-528) and throw `duplicate values found for the following keys` (Exts.cs:491).
	///
	/// That throw is not scoped to the actor or to the overriding file. Rules resolve once at load,
	/// so it takes down every map, the YAML lint (which then reports 0 maps) and the game at the
	/// menu. It happened on 2026-09-20: rules/cameo-captions.yaml became the first file ever to
	/// override A10:, and a duplicate ReloadAmmoPool@1 that had sat in aircraft-america.yaml for
	/// months stopped the whole mod loading — with an error naming neither the caption table nor
	/// the A10's real problem.
	///
	/// Scope is every file the manifest merges through MiniYaml.Merge, plus each shipped map's
	/// declared rules — NOT a directory walk of mods/ww3mod/rules, which both over- and
	/// under-counts: rules/ingame/old.yaml is on disk and never loaded, while sequences/ and
	/// rules/weapons/ are loaded and are not in the Rules: list at all.
	/// </summary>
	[TestFixture]
	public class DuplicateChildKeyTest
	{
		/// <summary>Manifest sections loaded through MiniYaml.Load -> MiniYaml.Merge
		/// (Ruleset.cs:125-141 for the first six, SequenceSet.cs:95 for Sequences).</summary>
		static readonly string[] MergedSections =
		{
			"Rules", "Weapons", "Voices", "Notifications", "Music", "ModelSequences", "Sequences"
		};

		static DirectoryInfo ModDir()
		{
			var dir = ModRulesYaml.RulesDir().Parent;
			Assert.That(dir, Is.Not.Null, "mods/ww3mod/rules has no parent directory");
			return dir;
		}

		/// <summary>Absolute paths of every merged manifest file, resolved from mod.yaml rather than
		/// from the directory tree.</summary>
		static List<string> ManifestFiles(DirectoryInfo mod)
		{
			var files = new List<string>();
			foreach (var section in MiniYaml.FromFile(Path.Combine(mod.FullName, "mod.yaml")))
			{
				if (!MergedSections.Contains(section.Key))
					continue;

				foreach (var entry in section.Value.Nodes)
				{
					// `prefix|path`; only this mod's own files are ours to police.
					var key = entry.Key.TrimStart('~');
					var split = key.IndexOf('|');
					if (split < 0 || key[..split] != "ww3mod")
						continue;

					var path = Path.Combine(mod.FullName, key[(split + 1)..].Replace('/', Path.DirectorySeparatorChar));
					Assert.That(File.Exists(path), Is.True,
						$"mod.yaml {section.Key}: lists {entry.Key} but {path} does not exist — the mod itself would not load.");
					files.Add(path);
				}
			}

			return files;
		}

		/// <summary>Each shipped map's declared rules/weapons/sequences files. A map's custom rules
		/// merge on top of the mod's, so the same duplicate in a map is the same load failure —
		/// except that Map.PostInit catches it and silently falls back to default rules, which is
		/// worse to diagnose, not better.</summary>
		static List<string> MapFiles(DirectoryInfo mod)
		{
			var files = new List<string>();
			var maps = new DirectoryInfo(Path.Combine(mod.FullName, "maps"));
			if (!maps.Exists)
				return files;

			foreach (var map in maps.GetDirectories())
			{
				var manifest = Path.Combine(map.FullName, "map.yaml");
				if (!File.Exists(manifest))
					continue;

				foreach (var section in MiniYaml.FromFile(manifest))
				{
					if (!MergedSections.Contains(section.Key) || string.IsNullOrWhiteSpace(section.Value.Value))
						continue;

					foreach (var name in section.Value.Value.Split(','))
					{
						var path = Path.Combine(map.FullName, name.Trim());
						if (File.Exists(path))
							files.Add(path);
					}
				}
			}

			return files;
		}

		/// <summary>One entry per (top-level node, repeated child key) in a single source.</summary>
		static IEnumerable<string> Offenders(string path)
		{
			foreach (var node in MiniYaml.FromFile(path))
			{
				if (node.Key == null)
					continue;

				var dupes = node.Value.Nodes
					.Where(n => n.Key != null)
					.GroupBy(n => n.Key, StringComparer.Ordinal)
					.Where(g => g.Count() > 1);

				foreach (var g in dupes)
					yield return $"{node.Key} (at {node.Location}) repeats `{g.Key}` at " +
						string.Join(", ", g.Select(n => n.Location.ToString()));
			}
		}

		[TestCase(TestName = "No loaded YAML node repeats one of its own direct child keys")]
		public void NoLoadedNodeRepeatsADirectChildKey()
		{
			var mod = ModDir();
			var files = ManifestFiles(mod);
			var mapFiles = MapFiles(mod);

			// A fixture that scans nothing passes. Both floors sit well under the shipped counts
			// (60 manifest files, 10 maps when this was written) so ordinary churn does not trip them.
			Assert.That(files.Count, Is.GreaterThan(40),
				$"only {files.Count} merged manifest files resolved from mod.yaml — this fixture is scanning nothing, not passing.");
			Assert.That(mapFiles.Count, Is.GreaterThan(5),
				$"only {mapFiles.Count} shipped map rules files resolved — this fixture is scanning nothing, not passing.");

			var offenders = files.Concat(mapFiles).SelectMany(Offenders).ToList();

			Assert.That(offenders, Is.Empty,
				"These nodes repeat a direct child key. MiniYaml folds the repeats together silently today, but the " +
				"moment a second file defines the same top-level key the merge throws `duplicate values found for the " +
				"following keys` and NOTHING loads — not just that actor, the whole mod. Resolve each to the single " +
				"node the engine already produces (union of the children, later value wins) rather than deleting one " +
				"at random:" + Environment.NewLine + "\t" + string.Join(Environment.NewLine + "\t", offenders));
		}

		[TestCase(TestName = "Repeated child keys in one source merge second-wins")]
		public void RepeatedChildKeysMergeSecondWins()
		{
			// The rule every resolution of a duplicate relies on, pinned against the engine itself
			// rather than against a note in a doc.
			var source = MiniYaml.FromString(
				"Actor:\n\tTrait: first\n\t\tShared: 1\n\t\tOnlyFirst: a\n\tTrait: second\n\t\tShared: 2\n\t\tOnlySecond: b\n",
				"second-wins");

			var merged = MiniYaml.Merge(new[] { source });
			var trait = merged.Single(n => n.Key == "Actor").Value.Nodes.Single(n => n.Key == "Trait");

			Assert.That(trait.Value.Value, Is.EqualTo("second"), "the node's own value should be the later one");
			Assert.That(ModRulesYaml.Child(trait.Value, "Shared"), Is.EqualTo("2"), "a field set in both should take the later value");
			Assert.That(ModRulesYaml.Child(trait.Value, "OnlyFirst"), Is.EqualTo("a"), "a field set only in the earlier node should survive");
			Assert.That(ModRulesYaml.Child(trait.Value, "OnlySecond"), Is.EqualTo("b"), "a field set only in the later node should survive");
			Assert.That(trait.Value.Nodes.Length, Is.EqualTo(3), "the result should be the union of both children");
		}

		[TestCase(TestName = "A repeated child key throws only once a second source defines the key")]
		public void ARepeatedChildKeyThrowsOnlyUnderAnOverride()
		{
			// Why the defect is latent, and why a game that starts proves nothing about it.
			var defining = MiniYaml.FromString("Actor:\n\tTrait:\n\t\tField: 1\n\tTrait:\n\t\tField: 2\n", "defining");
			var overriding = MiniYaml.FromString("Actor:\n\tOther:\n", "overriding");

			Assert.DoesNotThrow(() => MiniYaml.Merge(new[] { defining }),
				"a lone source with a repeated child key must still load — that is exactly what hides it.");

			var thrown = Assert.Throws<ArgumentException>(() => MiniYaml.Merge(new[] { defining, overriding }));
			Assert.That(thrown.Message, Does.Contain("duplicate values found for the following keys"));
		}
	}
}
