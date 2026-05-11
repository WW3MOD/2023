#region Copyright & License Information
/*
 * WW3MOD AI tournament harness — default win rule.
 *
 * Match ends when EITHER:
 *   (a) A player's original SR is no longer owned by them — instant win for the
 *       other side, "sr_capture" reason, plus SrCaptureBonus added to the
 *       winner's score.
 *   (b) The time limit (TimeLimitTicks) is reached — winner = highest total
 *       score, "time_limit" reason.
 *
 * Ties at time-out (equal score) resolve to the first-iterated player
 * deterministically — acceptable for Phase 1; tournament aggregation can
 * detect ties as 50/50 by comparing scores in the result JSON.
 *
 * To replace: implement IWinRuleEvaluator, register in MatchHarness, reference
 * by name from tournament.yaml's "WinRule:" field. See
 * WORKSPACE/ai/tournament_swap_guide.md.
 */
#endregion

using System.Collections.Generic;
using System.Linq;

namespace OpenRA.Mods.Common.Tournament.WinRules
{
	public class TimeOrSrCaptureWinRule : IWinRuleEvaluator
	{
		readonly TournamentConfig config;

		public TimeOrSrCaptureWinRule(TournamentConfig config)
		{
			this.config = config;
		}

		public MatchVerdict EvaluateEndState(
			World world,
			MatchTrackingState state,
			Dictionary<Player, MatchScoreSnapshot> scores,
			int currentTick,
			int timeLimitTicks)
		{
			// SR capture — instant decisive outcome.
			foreach (var kv in state.OriginalSrOwner)
			{
				var player = kv.Key;
				var sr = kv.Value;

				// SR is indestructible by design (Armor: Indestructable); the only way it
				// "leaves" a player is via capture (OwnerLostAction → Neutral) or by being
				// removed from the world. Either counts as the player losing their SR.
				var lost = sr == null || sr.IsDead || !sr.IsInWorld || sr.Owner != player;
				if (!lost)
					continue;

				// Winner = the highest-scoring other tracked player.
				var winner = scores
					.Where(s => s.Key != player)
					.OrderByDescending(s => s.Value.Total)
					.Select(s => s.Key)
					.FirstOrDefault();

				if (winner == null)
					continue;

				// Apply SR capture bonus to the winner's snapshot so the final JSON
				// shows where the points came from.
				if (scores.TryGetValue(winner, out var ws))
				{
					ws.Components["sr_capture_bonus"] = config.Score.SrCaptureBonus;
					ws.Total += config.Score.SrCaptureBonus;
				}

				return new MatchVerdict
				{
					Winner = winner,
					Reason = "sr_capture",
					EndTick = currentTick,
					Scores = scores,
				};
			}

			// Time-limit: highest total wins. Equal scores resolved by Player.ClientIndex
			// ordering for determinism (could surface as a "tie" flag if needed later).
			if (currentTick >= timeLimitTicks)
			{
				var winner = scores
					.OrderByDescending(s => s.Value.Total)
					.ThenBy(s => s.Key.ClientIndex)
					.Select(s => s.Key)
					.FirstOrDefault();

				return new MatchVerdict
				{
					Winner = winner,
					Reason = "time_limit",
					EndTick = currentTick,
					Scores = scores,
				};
			}

			return null;
		}
	}
}
