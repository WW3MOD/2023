#region Copyright & License Information
/*
 * WW3MOD critical-wound fire gate — corpus pin.
 *
 * Vehicles stop shooting when they are wrecked: every vehicle Armament carries `heavy-damage-attained`
 * in its PauseOnCondition, pinned in-game by test-arty-no-fire-at-critical. Infantry had the entire
 * degradation ladder for the same states — speed 0, vision 10%, burst 10%, burst-wait 400%, inaccuracy
 * 400% at critical (infantry.yaml ^EffectsWhenDamagedInfantry) — and no cutoff at the end of it, so a
 * man at 1% HP still put rounds downrange, slowly. Reported from live play 2026-08-20.
 *
 * The gate is one line on ^Soldier's AttackFrontal, which every soldier template reaches through
 * ^CamoSoldier. That makes it cheap to lose: MiniYaml OVERRIDES a scalar rather than merging it, so any
 * descendant that re-states PauseOnCondition for its own reasons silently drops `critical-damage` for
 * that unit, with no lint error and no visible symptom beyond a wounded man who keeps shooting. This
 * scans for exactly that.
 *
 * Reads the shipped YAML rather than a fixture: the thing being protected is the corpus.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace OpenRA.Test
{
	[TestFixture]
	public class CriticalWoundFireGateTest
	{
		const string Condition = "critical-damage";

		// ^MEDI is excluded BY DESIGN and this is the only exclusion. The medic's sole Armament is the
		// Heal weapon — he carries nothing offensive — so gating him would not stop a single bullet; it
		// would only stop a wounded medic treating his squad, which is a balance change nobody asked
		// for. If a medic is ever given a weapon, delete this entry rather than widening it.
		static readonly string[] ExcludedTemplates = { "^MEDI" };

		static string FindInfantryRules()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(dir.FullName, "mods", "ww3mod", "rules", "ingame", "infantry.yaml");
				if (File.Exists(candidate))
					return candidate;
			}

			throw new FileNotFoundException("could not locate mods/ww3mod/rules/ingame/infantry.yaml");
		}

		static List<MiniYamlNode> Templates()
		{
			return MiniYaml.FromFile(FindInfantryRules());
		}

		static string PauseOnCondition(MiniYamlNode template)
		{
			return template.Value.Nodes
				.FirstOrDefault(n => n.Key == "AttackFrontal")?.Value.Nodes
				.FirstOrDefault(n => n.Key == "PauseOnCondition")?.Value.Value;
		}

		[Test]
		public void SoldiersCannotFireAtCriticalDamage()
		{
			var soldier = Templates().FirstOrDefault(n => n.Key == "^Soldier");
			Assert.That(soldier, Is.Not.Null, "^Soldier template not found — this test is scanning nothing");

			var pause = PauseOnCondition(soldier);
			Assert.That(pause, Is.Not.Null,
				"^Soldier's AttackFrontal has no PauseOnCondition at all, so nothing stops a wounded soldier firing");

			Assert.That(pause, Does.Contain(Condition),
				$"^Soldier's AttackFrontal must pause on `{Condition}` — this is the infantry half of the " +
				"rule that already stops a wrecked vehicle shooting, and without it a man at 1% HP keeps firing");
		}

		[Test]
		public void NoDescendantSilentlyDropsTheGate()
		{
			var offenders = new List<string>();
			var scanned = 0;

			foreach (var template in Templates())
			{
				// Actor definitions are lowercased at ruleset load; the abstract templates this gate
				// travels through are the ^-prefixed ones.
				if (!template.Key.StartsWith("^", StringComparison.Ordinal) || template.Key == "^Soldier")
					continue;

				var pause = PauseOnCondition(template);
				if (pause == null)
					continue;

				scanned++;

				if (ExcludedTemplates.Contains(template.Key) || pause.Contains(Condition))
					continue;

				offenders.Add($"{template.Key} (PauseOnCondition: {pause})");
			}

			// Guard the guard: a rename that emptied the scan would otherwise report a clean result.
			Assert.That(scanned, Is.GreaterThan(0),
				"scanned no AttackFrontal PauseOnCondition overrides at all — the scan itself is broken");

			Assert.That(offenders, Is.Empty,
				"these templates re-state AttackFrontal's PauseOnCondition and so OVERRIDE ^Soldier's, " +
				$"dropping the `{Condition}` gate for every unit that inherits them: " +
				string.Join(", ", offenders));
		}
	}
}
