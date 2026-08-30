#region Copyright & License Information
/*
 * WW3MOD — pins that AttackMaxDistanceCells covers every LIVE target-commit site.
 *
 * WHY THIS EXISTS. The first version of the penetration bound guarded ONE site and its comment claimed it
 * covered "the single point where a target is committed". That was wrong, and wrong in the way that matters:
 * HelicopterWithdrawState re-picks a target with a bare omniscient FindClosestEnemy and hands straight back
 * to Approach, and HelicopterIdleState only re-picks when TargetActor is NULL. So an unbounded withdraw
 * re-pick did not merely leak once — it reset the squad to an unbounded objective permanently, and
 * retreating was the way to escape the cap. The batch this shipped in makes withdraws MORE frequent.
 *
 * A comment cannot hold that invariant, because the failure is a site that was never added to. This scan
 * fails when a fresh-scan commit site appears without the bound.
 *
 * SCOPE. Three sites are LIVE and must all be guarded: the Idle pick, the Approach soft-target divert, and
 * the Withdraw re-pick. The re-target inside HelicopterAttackRunState is deliberately NOT counted — that
 * state is unreachable on both shipped profiles (HeliAttackRunReachabilityTest), and guarding dead code
 * would blur which sites actually carry the guarantee.
 *
 * STATED GAP: this proves the guard is CALLED at each site. It does not prove the bound is the right
 * distance, nor that AttackObjectiveWithinReach measures from the right origin — both need a match.
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
	public class HeliAttackBoundTest
	{
		const int ExpectedLiveGuardedSites = 3;

		static readonly Regex GuardCall = new(@"AttackObjectiveWithinReach\(", RegexOptions.Compiled);
		static readonly Regex GuardDeclaration = new(@"\bbool\s+AttackObjectiveWithinReach\(", RegexOptions.Compiled);
		static readonly Regex StateHeader = new(@"^\s*class\s+(Helicopter\w+State)\b", RegexOptions.Compiled);

		static string FindStatesSource()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(dir.FullName, "OpenRA.Mods.Common", "Traits", "BotModules",
					"Squads", "States", "HelicopterStates.cs");
				if (File.Exists(candidate))
					return candidate;
			}

			return null;
		}

		/// <summary>The enclosing state class for a line, by walking back to the nearest class header.
		/// The guard's own declaration lives in the abstract base, above every state, so it walks back to
		/// no header and is reported as "(base)".</summary>
		static string EnclosingState(string[] lines, int index)
		{
			for (var i = index; i >= 0; i--)
			{
				var m = StateHeader.Match(lines[i]);
				if (m.Success)
					return m.Groups[1].Value;
			}

			return "(base)";
		}

		[Test]
		public void EveryLiveTargetCommitSiteIsBounded()
		{
			var file = FindStatesSource();
			if (file == null)
				Assert.Ignore("source tree not reachable from the test assembly — scan skipped, not passed");

			var lines = File.ReadAllLines(file);
			var guarded = new List<string>();
			for (var i = 0; i < lines.Length; i++)
			{
				if (!GuardCall.IsMatch(lines[i]) || GuardDeclaration.IsMatch(lines[i]))
					continue;

				guarded.Add(EnclosingState(lines, i));
			}

			Assert.Multiple(() =>
			{
				Assert.That(guarded, Has.Exactly(1).EqualTo("HelicopterIdleState"),
					"The Idle pick must be bounded — it is where a squad acquires its objective, covering both "
					+ "the threat-map cluster candidate and the FindClosestEnemy fallback. Guarded sites: "
					+ string.Join(", ", guarded));

				Assert.That(guarded, Has.Exactly(1).EqualTo("HelicopterApproachState"),
					"The Approach soft-target divert must be bounded, or a too-hot objective walks the squad "
					+ "past the cap 20 cells at a time — and each divert re-enters this state, so it chains. "
					+ "Guarded sites: " + string.Join(", ", guarded));

				Assert.That(guarded, Has.Exactly(1).EqualTo("HelicopterWithdrawState"),
					"The Withdraw re-pick MUST be bounded. It is a bare omniscient FindClosestEnemy that sets "
					+ "TargetActor and transitions straight back to Approach, and Idle only re-picks when "
					+ "TargetActor is null — so leaving it unbounded does not leak once, it resets the squad "
					+ "to an unbounded objective for the rest of the match and makes RETREATING the way to "
					+ "escape the cap. Guarded sites: " + string.Join(", ", guarded));

				Assert.That(guarded.Count, Is.EqualTo(ExpectedLiveGuardedSites),
					$"Expected exactly {ExpectedLiveGuardedSites} guarded sites, one per LIVE state that "
					+ "commits a target from a fresh scan. A fourth would most likely be "
					+ "HelicopterAttackRunState, which is deliberately left unguarded because it is "
					+ "unreachable on both shipped profiles — if you made that state live, this test and "
					+ "HeliAttackRunReachabilityTest both need revisiting together. Guarded sites: "
					+ string.Join(", ", guarded));
			});
		}
	}
}
