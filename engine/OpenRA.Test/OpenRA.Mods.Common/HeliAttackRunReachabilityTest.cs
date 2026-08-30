#region Copyright & License Information
/*
 * WW3MOD — pins the reachability of HelicopterAttackRunState, which is currently ZERO.
 *
 * THE FINDING THIS EXISTS TO PRESERVE. WORKSPACE/recon-bot-helicopters.md §2.2(i) states that the squad
 * "abandons the entire standoff apparatus 8 cells from the target" and that the close-in attack run "is
 * reached on every successful approach". That is not what the code does. The handoff is:
 *
 *     if (!standoff)
 *     {
 *         var distToTarget = ...;
 *         if (distToTarget < WDist.FromCells(8).Length)
 *             owner.FuzzyStateMachine.ChangeState(owner, new HelicopterAttackRunState());
 *     }
 *
 * `standoff` is StandoffEngagementEnabled(owner), i.e. HelicopterSquadBotModuleInfo.StandoffEngagement for
 * the ENABLED module instance. Both shipped bot profiles — HelicopterSquadBotModule@stable and
 * @experimental — set StandoffEngagement: true. That construction is the ONLY one in the engine, so
 * HelicopterAttackRunState is dead code for every profile that ships a helicopter squad module.
 *
 * WHAT FOLLOWS FROM IT, since three separate proposals were aimed at this state:
 *   * P7 (withdraw on danger spike inside the attack run) cannot change match behaviour today. It is
 *     implemented anyway, defaulted off, so the state is not a trap for whoever disables standoff.
 *   * P6 (AttackRunHandoffCells, to gate the handoff) would have been a no-op.
 *   * §2.2(ii) — "the frontier standoff is dimensioned to exactly the distance that triggers the handoff,
 *     so it cancels itself" — is moot: the handoff cannot fire, so it cannot be cancelling anything.
 *
 * WHY A TEST AND NOT A COMMENT. A comment claiming "this is unreachable" rots silently the moment someone
 * adds a second construction site or flips StandoffEngagement off, and the next reader inherits a false
 * claim. This fails instead, and its message says what to do about it.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace OpenRA.Test
{
	[TestFixture]
	public class HeliAttackRunReachabilityTest
	{
		static readonly Regex Construction = new(@"new\s+HelicopterAttackRunState\s*\(", RegexOptions.Compiled);
		static readonly Regex StandoffGuard = new(@"if\s*\(\s*!\s*standoff\s*\)", RegexOptions.Compiled);
		static readonly Regex HeliModuleBlock = new(@"^\s*HelicopterSquadBotModule@", RegexOptions.Compiled);
		static readonly Regex StandoffSetting = new(@"^\s*StandoffEngagement\s*:\s*(\w+)", RegexOptions.Compiled);
		static readonly Regex TopLevelTrait = new(@"^\t\w", RegexOptions.Compiled);

		static string FindRepoFile(params string[] parts)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(dir.FullName, Path.Combine(parts));
				if (File.Exists(candidate))
					return candidate;
			}

			return null;
		}

		[Test]
		public void AttackRunStateIsConstructedOnlyBehindTheStandoffGuard()
		{
			var file = FindRepoFile("OpenRA.Mods.Common", "Traits", "BotModules", "Squads", "States",
				"HelicopterStates.cs");
			if (file == null)
				Assert.Ignore("source tree not reachable from the test assembly — scan skipped, not passed");

			var lines = File.ReadAllLines(file);
			var sites = new List<int>();
			for (var i = 0; i < lines.Length; i++)
				if (Construction.IsMatch(lines[i]))
					sites.Add(i);

			Assert.That(sites.Count, Is.EqualTo(1),
				"HelicopterAttackRunState is expected to have exactly ONE construction site. A second one is "
				+ "how this state quietly becomes live again: everything written about it — including the "
				+ "default-off WithdrawOnSpikeInAttackRun — assumes the single guarded entry below. Sites at "
				+ "lines: " + string.Join(", ", sites));

			// Walk back from the construction to the nearest enclosing `if (!standoff)`. Ten lines is generous;
			// the guard currently sits four above it.
			var guarded = false;
			for (var i = sites[0]; i >= 0 && i > sites[0] - 10; i--)
				if (StandoffGuard.IsMatch(lines[i]))
				{
					guarded = true;
					break;
				}

			Assert.That(guarded, Is.True,
				"The only entry into HelicopterAttackRunState must stay behind `if (!standoff)`. If this guard "
				+ "was removed, the close-in attack run is now LIVE for both shipped bot profiles — which is a "
				+ "real behavioural change to @stable as well as @experimental, and the state's danger withdraw "
				+ "(WithdrawOnSpikeInAttackRun) is still defaulted OFF in YAML. Turn it on deliberately or "
				+ "restore the guard.");
		}

		[Test]
		public void EveryShippedHeliProfileEnablesStandoffAndSoCannotReachTheAttackRun()
		{
			var file = FindRepoFile("mods", "ww3mod", "rules", "ai", "ai.yaml");
			if (file == null)
				Assert.Ignore("mod rules not reachable from the test assembly — scan skipped, not passed");

			var lines = File.ReadAllLines(file);
			var profiles = new Dictionary<string, string>();
			string current = null;
			foreach (var line in lines)
			{
				if (HeliModuleBlock.IsMatch(line))
				{
					current = line.Trim().TrimEnd(':');
					profiles[current] = "unset";
					continue;
				}

				if (current == null)
					continue;

				// A new top-level trait (one tab, non-comment) ends the block we are reading.
				if (TopLevelTrait.IsMatch(line) && !HeliModuleBlock.IsMatch(line))
				{
					current = null;
					continue;
				}

				var m = StandoffSetting.Match(line);
				if (m.Success)
					profiles[current] = m.Groups[1].Value;
			}

			Assert.That(profiles, Is.Not.Empty, "no HelicopterSquadBotModule blocks found — the scan lost its subject");

			var reachable = new List<string>();
			foreach (var kv in profiles)
				if (kv.Value != "true")
					reachable.Add(kv.Key + " (StandoffEngagement: " + kv.Value + ")");

			Assert.That(reachable, Is.Empty,
				"A shipped helicopter profile no longer sets StandoffEngagement: true, so HelicopterAttackRunState "
				+ "is now REACHABLE for it. That state issues bare Attack orders with no standoff, no danger "
				+ "leash, no frontier push and no detour, and its danger withdraw is opt-in via "
				+ "WithdrawOnSpikeInAttackRun — which these profiles do not need to set while standoff is on. If "
				+ "you disabled standoff deliberately, set WithdrawOnSpikeInAttackRun: true on the same profile "
				+ "and update this test. Profiles: " + string.Join(", ", reachable));
		}
	}
}
