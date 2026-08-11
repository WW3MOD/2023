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
using System.Text.RegularExpressions;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// Two pins around the executor's FIRE-stance opt-out (PIPELINE item 8 Stage 1, commit 174075e9):
	/// StancePositioningExecutor relinquishes management of any unit whose fire stance is below
	/// FireAtWill, so an Ambush/HoldFire placement is never walked off its cell (the un-ambush bug).
	///
	/// 1. <see cref="OptOutAppliesToEveryStanceBelowFireAtWill"/> and friends pin the predicate itself,
	///    following the project idiom (StancePositioningLeashTest, StanceCoverPositioningTest) of
	///    extracting the decision as a pure static and pinning it without a live actor.
	///
	/// 2. <see cref="ExecutorScenariosDoNotSilenceTheUnitUnderTestByFireStance"/> is the guard that would
	///    actually have caught the 2026-08-10 regression cluster. That gate landed as a C#-only change
	///    with no scenario updates, but three autotest scenarios were putting their own unit-under-test
	///    on HoldFire purely to silence combat — a setup convenience that the new gate turned into a
	///    switch that disabled the trait under test. All three went red at the unit's spawn cell and
	///    stayed red for weeks, because nothing cross-checked the scenario corpus against the engine
	///    gate. This test is that cross-check: a scenario that expects the executor to MOVE a unit must
	///    not put that unit in a stance the executor opts out of.
	///
	/// Source of truth: engine/OpenRA.Mods.Common/Traits/StancePositioningExecutor.cs
	/// </summary>
	[TestFixture]
	public class StancePositioningFireStanceTest
	{
		// ── 1. The predicate ──

		[Test]
		public void OptOutAppliesToEveryStanceBelowFireAtWill()
		{
			Assert.That(StancePositioningExecutor.FireStanceAllowsRepositioning(UnitStance.HoldFire), Is.False,
				"HoldFire expresses a hold intent stronger than a reposition");
			Assert.That(StancePositioningExecutor.FireStanceAllowsRepositioning(UnitStance.Ambush), Is.False,
				"Ambush placement must never be walked off its cell (the un-ambush bug)");
		}

		[Test]
		public void FireAtWillIsTheOnlyStanceThatPermitsRepositioning()
		{
			Assert.That(StancePositioningExecutor.FireStanceAllowsRepositioning(UnitStance.FireAtWill), Is.True,
				"FireAtWill is the AI default and every @stable/control bot — it must never trip the opt-out");

			var permitted = Enum.GetValues(typeof(UnitStance)).Cast<UnitStance>()
				.Where(StancePositioningExecutor.FireStanceAllowsRepositioning)
				.ToArray();

			// Pinned as an exact set, not just "FireAtWill is in it": a new stance added above FireAtWill
			// would silently inherit repositioning, which is a decision that must be made deliberately.
			Assert.That(permitted, Is.EqualTo(new[] { UnitStance.FireAtWill }),
				"exactly one stance may permit repositioning; adding another is a deliberate design change");
		}

		// ── 2. The scenario-corpus cross-check ──

		// test-stance-optout is excluded BY DESIGN: it is the only stance scenario that asserts the
		// executor issues NO move, so a sub-FireAtWill unit in it is not self-defeating the way it is in
		// the positive scenarios. NOTE (2026-08-11): that exclusion also records a real weakness — the
		// opt-out scenario currently sets its HoldPosition/deployed units to HoldFire as well, so it
		// would pass even if the stance and deploy opt-outs it claims to test were both broken. Filed in
		// WORKSPACE/bugs/discovered.md rather than fixed here, because verifying a change to it needs an
		// autotest run this branch was not scoped for.
		static readonly string[] ExcludedScenarios = { "test-stance-optout" };

		static readonly Regex AliasAssign = new(@"^\s*local\s+(\w+)\s*=\s*(\w+)\s*$", RegexOptions.Compiled);
		static readonly Regex IpairsLoop = new(@"for\s+_\s*,\s*(\w+)\s+in\s+ipairs\(\{([^}]*)\}\)", RegexOptions.Compiled);
		static readonly Regex StanceAssign = new(@"(\w+)\.Stance\s*=\s*""(\w+)""", RegexOptions.Compiled);
		static readonly Regex ActorDecl = new(@"^\t(\w+):\s*(\S+)\s*$", RegexOptions.Compiled);
		static readonly Regex OwnerDecl = new(@"^\t\tOwner:\s*(\w+)\s*$", RegexOptions.Compiled);

		static string FindScenariosDir()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(dir.FullName, "tools", "autotest", "scenarios");
				if (Directory.Exists(candidate))
					return candidate;
			}

			return null;
		}

		/// <summary>actor name -> owning player name, read from the scenario's map.yaml Actors block.</summary>
		static Dictionary<string, string> ActorOwners(string mapYaml)
		{
			var owners = new Dictionary<string, string>();
			var lines = File.ReadAllLines(mapYaml);
			string pending = null;

			foreach (var line in lines)
			{
				var actor = ActorDecl.Match(line);
				if (actor.Success)
				{
					pending = actor.Groups[1].Value;
					continue;
				}

				var owner = OwnerDecl.Match(line);
				if (owner.Success && pending != null)
				{
					owners[pending] = owner.Groups[1].Value;
					pending = null;
				}
			}

			return owners;
		}

		/// <summary>The scenario's human player: the single PlayerReference marked Playable: True.</summary>
		static string HumanPlayer(string mapYaml)
		{
			string current = null;
			foreach (var line in File.ReadAllLines(mapYaml))
			{
				var trimmed = line.Trim();
				if (trimmed.StartsWith("Name:", StringComparison.Ordinal))
					current = trimmed.Substring("Name:".Length).Trim();
				else if (trimmed.StartsWith("Playable:", StringComparison.Ordinal)
					&& trimmed.EndsWith("True", StringComparison.OrdinalIgnoreCase))
					return current;
			}

			return null;
		}

		/// <summary>
		/// Resolve every `X.Stance = "V"` in the Lua back to the ACTOR names it can affect. Handles the
		/// three forms the stance scenarios use: a direct actor name, a `local u = Actor` alias, and a
		/// `for _, u in ipairs({ A, B })` loop variable. Anything it cannot resolve to a declared actor is
		/// skipped rather than guessed at — a guard that silently invents bindings is worse than one with
		/// a known blind spot.
		/// </summary>
		static List<(string Actor, string Stance)> ResolvedStanceAssignments(string luaPath, ICollection<string> knownActors)
		{
			var lua = File.ReadAllLines(luaPath);
			var bindings = new Dictionary<string, List<string>>();

			foreach (var name in knownActors)
				bindings[name] = new List<string> { name };

			foreach (var line in lua)
			{
				var alias = AliasAssign.Match(line);
				if (alias.Success && bindings.TryGetValue(alias.Groups[2].Value, out var target))
				{
					bindings[alias.Groups[1].Value] = new List<string>(target);
					continue;
				}

				var loop = IpairsLoop.Match(line);
				if (loop.Success)
				{
					var members = loop.Groups[2].Value
						.Split(',')
						.Select(s => s.Trim())
						.Where(knownActors.Contains)
						.ToList();

					if (members.Count > 0)
						bindings[loop.Groups[1].Value] = members;
				}
			}

			var result = new List<(string, string)>();
			foreach (var line in lua)
			{
				foreach (Match m in StanceAssign.Matches(line))
				{
					if (!bindings.TryGetValue(m.Groups[1].Value, out var actors))
						continue;

					foreach (var a in actors)
						result.Add((a, m.Groups[2].Value));
				}
			}

			return result;
		}

		[Test]
		public void ExecutorScenariosDoNotSilenceTheUnitUnderTestByFireStance()
		{
			var root = FindScenariosDir();
			if (root == null)
				Assert.Ignore("autotest scenarios not reachable from the test assembly — scan skipped, not passed");

			var scenarios = Directory.EnumerateDirectories(root, "test-stance-*")
				.Where(d => !ExcludedScenarios.Contains(Path.GetFileName(d)))
				.OrderBy(d => d, StringComparer.Ordinal)
				.ToArray();

			Assert.That(scenarios, Is.Not.Empty, "expected the test-stance-* scenarios to exist");

			var violations = new List<string>();
			var checkedAssignments = 0;

			foreach (var dir in scenarios)
			{
				var name = Path.GetFileName(dir);
				var mapYaml = Path.Combine(dir, "map.yaml");
				var lua = Path.Combine(dir, name + ".lua");
				if (!File.Exists(mapYaml) || !File.Exists(lua))
					continue;

				var owners = ActorOwners(mapYaml);
				var human = HumanPlayer(mapYaml);
				if (human == null)
					continue;

				foreach (var (actor, stance) in ResolvedStanceAssignments(lua, owners.Keys))
				{
					// Only the human-owned units are under the executor's management in these scenarios;
					// the enemies exist to give the threat layer a bearing and SHOULD stay silenced.
					if (!owners.TryGetValue(actor, out var owner) || owner != human)
						continue;

					checkedAssignments++;

					if (!Enum.TryParse<UnitStance>(stance, out var parsed))
					{
						violations.Add($"{name}: {actor} set to unknown stance \"{stance}\"");
						continue;
					}

					if (!StancePositioningExecutor.FireStanceAllowsRepositioning(parsed))
						violations.Add(
							$"{name}: {actor} (owned by the human {human}) is set to {parsed}, which the " +
							"executor opts out of — the scenario would silently disable the trait it tests");
				}
			}

			Assert.That(checkedAssignments, Is.GreaterThan(0),
				"resolved no human stance assignments at all — the Lua/map parsing has drifted and this " +
				"guard is no longer measuring anything");

			Assert.That(violations, Is.Empty,
				"scenario(s) disable StancePositioningExecutor on their own unit-under-test:" +
				Environment.NewLine + string.Join(Environment.NewLine, violations));
		}
	}
}
