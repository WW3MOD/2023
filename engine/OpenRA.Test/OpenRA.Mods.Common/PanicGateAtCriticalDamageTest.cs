#region Copyright & License Information
/*
 * WW3MOD critical-damage panic gate — corpus pin.
 *
 * A critically damaged man is prone and immobile. He does not panic and he does not wander.
 * Reported from live play 2026-08-21 against the technician, but the rule is universal.
 *
 * ^Soldier-family infantry reach this through InfantryStates' `PanicCondition: onfire && !critical-damage`.
 * The civilian family (^CivInfantry -> ^ArmedCivilian -> ^TECN) has no InfantryStates at all; it panics
 * through the SEPARATE ScaredyCat trait and moves through Wanders, and both shipped ungated. The speed
 * ladder in ^EffectsWhenDamagedInfantry zeroes displacement at critical, which is why this looked fixed
 * and was not: SpeedMultiplier governs how fast a unit crosses a cell, not whether it decides to move.
 * A speed-0 panicking man still enters the panic state, still renders the `panic-run` sequence on the
 * spot, and still claims the next cell (Move.cs `SetLocation` runs before speed is consulted).
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
	public class PanicGateAtCriticalDamageTest
	{
		const string Gate = "!critical-damage";

		// Traits that move an actor with no player order behind them.
		static readonly string[] PanicTraits = { "ScaredyCat", "Wanders" };

		static readonly string[] RuleFiles = { "infantry.yaml", "infantry-neutral.yaml" };

		static string FindRules(string file)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(dir.FullName, "mods", "ww3mod", "rules", "ingame", file);
				if (File.Exists(candidate))
					return candidate;
			}

			throw new FileNotFoundException($"could not locate mods/ww3mod/rules/ingame/{file}");
		}

		static List<MiniYamlNode> Templates(string file)
		{
			return MiniYaml.FromFile(FindRules(file));
		}

		static string RequiresCondition(MiniYamlNode template, string trait)
		{
			return template.Value.Nodes
				.FirstOrDefault(n => n.Key == trait)?.Value.Nodes
				.FirstOrDefault(n => n.Key == "RequiresCondition")?.Value.Value;
		}

		static bool Gated(string condition)
		{
			return condition != null && condition.Replace(" ", "").Contains(Gate, StringComparison.Ordinal);
		}

		[Test]
		public void CivilianInfantryCannotPanicOrWanderAtCriticalDamage()
		{
			var civ = Templates("infantry.yaml").FirstOrDefault(n => n.Key == "^CivInfantry");
			Assert.That(civ, Is.Not.Null, "^CivInfantry template not found — this test is scanning nothing");

			foreach (var trait in PanicTraits)
			{
				var declared = civ.Value.Nodes.Any(n => n.Key == trait);
				Assert.That(declared, Is.True,
					$"^CivInfantry no longer declares {trait} — if it was removed on purpose, delete it from " +
					"PanicTraits here; if it was renamed, this gate is now scanning nothing");

				var condition = RequiresCondition(civ, trait);
				Assert.That(Gated(condition), Is.True,
					$"^CivInfantry's {trait} must carry `RequiresCondition: {Gate}` (found: " +
					$"{condition ?? "no RequiresCondition at all"}). Without it a critically damaged " +
					"civilian, armed civilian or technician still panics and still moves off its cell — " +
					"SpeedMultiplier@CriticalDamage only zeroes the speed of a move it has already decided to make.");
			}
		}

		[Test]
		public void NoDescendantSilentlyDropsTheGate()
		{
			var offenders = new List<string>();
			var scanned = 0;

			foreach (var file in RuleFiles)
			{
				foreach (var template in Templates(file))
				{
					foreach (var trait in PanicTraits)
					{
						if (!template.Value.Nodes.Any(n => n.Key == trait))
							continue;

						// Count every DECLARATION, not just the gated ones — counting overrides would leave
						// this scan vacuous the moment the corpus is correct, which is precisely when the
						// guard needs to still be measuring something.
						scanned++;

						// A descendant that re-states the trait with no RequiresCondition of its own keeps
						// the inherited one — MiniYaml merges child nodes. Only a re-stated RequiresCondition
						// OVERRIDES, and that is the silent way to lose this.
						var condition = RequiresCondition(template, trait);
						if (condition == null || Gated(condition))
							continue;

						offenders.Add($"{file}:{template.Key} ({trait} RequiresCondition: {condition})");
					}
				}
			}

			// Guard the guard: a rename that emptied the scan would otherwise report a clean result.
			Assert.That(scanned, Is.GreaterThan(0),
				"scanned no ScaredyCat/Wanders declarations at all — the scan itself is broken");

			Assert.That(offenders, Is.Empty,
				"these templates re-state a panic trait's RequiresCondition and so OVERRIDE ^CivInfantry's, " +
				$"dropping the `{Gate}` gate for every unit that inherits them: " + string.Join(", ", offenders));
		}

		[Test]
		public void CriticallyDamagedInfantryHaveZeroSpeed()
		{
			var effects = Templates("infantry.yaml").FirstOrDefault(n => n.Key == "^EffectsWhenDamagedInfantry");
			Assert.That(effects, Is.Not.Null, "^EffectsWhenDamagedInfantry not found — this test is scanning nothing");

			var speed = effects.Value.Nodes.FirstOrDefault(n => n.Key == "SpeedMultiplier@CriticalDamage");
			Assert.That(speed, Is.Not.Null, "^EffectsWhenDamagedInfantry has no SpeedMultiplier@CriticalDamage");

			var modifier = speed.Value.Nodes.FirstOrDefault(n => n.Key == "Modifier")?.Value.Value;
			Assert.That(modifier, Is.EqualTo("0"),
				"a critically damaged man must not be able to cross a cell at any speed");

			var condition = speed.Value.Nodes.FirstOrDefault(n => n.Key == "RequiresCondition")?.Value.Value;
			Assert.That(condition, Is.EqualTo("critical-damage"));
		}

		[Test]
		public void ProneIsGrantedByTheDamageStateNotMerelyPermitted()
		{
			var effects = Templates("infantry.yaml").FirstOrDefault(n => n.Key == "^EffectsWhenDamagedInfantry");
			Assert.That(effects, Is.Not.Null, "^EffectsWhenDamagedInfantry not found — this test is scanning nothing");

			// ^Infantry inherits this, so it reaches the civilian family too — which carries no
			// InfantryStates and therefore has no other route to the prone condition.
			var grant = effects.Value.Nodes.FirstOrDefault(n => n.Key == "GrantCondition@HeavyDamageProne");
			Assert.That(grant, Is.Not.Null,
				"prone must be GRANTED by the damage state, not merely made available by InfantryStates' " +
				"`!moving` clause — a civilian-family actor has no InfantryStates to grant it");

			Assert.That(grant.Value.Nodes.FirstOrDefault(n => n.Key == "Condition")?.Value.Value, Is.EqualTo("prone"));
			Assert.That(grant.Value.Nodes.FirstOrDefault(n => n.Key == "RequiresCondition")?.Value.Value,
				Is.EqualTo("heavy-damage-attained"),
				"heavy-damage-attained covers Heavy AND Critical; a bare `heavy-damage` would leave a " +
				"critically damaged man standing up");
		}
	}
}
