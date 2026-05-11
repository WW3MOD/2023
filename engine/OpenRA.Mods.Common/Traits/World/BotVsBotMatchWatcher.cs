#region Copyright & License Information
/*
 * WW3MOD AI tournament harness — match watcher (world trait).
 *
 * Activated only when:
 *   (a) TestMode.IsActive   (the game was launched with Test.Mode=true), AND
 *   (b) Test.TournamentConfig=<path> launch arg was provided.
 *
 * Without both, the trait is a no-op — every code path returns immediately, so
 * the existing test/demo flow is unaffected by its presence in world.yaml.
 *
 * The trait reads tournament.yaml, instantiates a scorer and a win-rule via
 * MatchHarness (swap point: replace either independently), tracks per-player
 * state each tick, and writes a JSON verdict to TestMode.ResultPath when the
 * win rule decides the match is over.
 *
 * Verdict shape (serialized into TestMode.WriteResult's `notes` field):
 *   {
 *     "verdict_version": 1,
 *     "scenario": "<test-name>",
 *     "seed": <int>,
 *     "git_sha": "<run-tournament.sh stamps this>",
 *     "duration_ticks": <int>,
 *     "winner_client_index": <int>,
 *     "win_reason": "sr_capture"|"time_limit"|...,
 *     "players": [
 *       { "name": "...", "client_index": 0,
 *         "score_total": <long>,
 *         "score_components": { "army_value": ..., "capture_income": ..., ... }
 *       },
 *       ...
 *     ]
 *   }
 *
 * See:
 *   WORKSPACE/plans/260511_ai_tournament_harness.md
 *   WORKSPACE/ai/tournament_swap_guide.md
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenRA.Mods.Common.Tournament;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("AI-vs-AI tournament match watcher. Active only when both Test.Mode=true and Test.TournamentConfig=<path> launch args are present.",
		"Reads tournament.yaml, scores players each tick via the configured IMatchScorer, and writes a JSON verdict when the configured IWinRuleEvaluator signals the match is over.")]
	public class BotVsBotMatchWatcherInfo : TraitInfo
	{
		[Desc("Name of the SR actor used to detect SR-capture wins.")]
		public readonly string SupplyRouteActorType = "supplyroute";

		[Desc("Tick interval between win-rule evaluations. Default 25 = once per second.")]
		public readonly int EvaluationInterval = 25;

		public override object Create(ActorInitializer init) { return new BotVsBotMatchWatcher(this); }
	}

	public class BotVsBotMatchWatcher : ITick, IWorldLoaded
	{
		readonly BotVsBotMatchWatcherInfo info;

		TournamentConfig config;
		IMatchScorer scorer;
		IWinRuleEvaluator winRule;
		MatchTrackingState state;
		bool active;
		bool finished;
		int countdown;

		public BotVsBotMatchWatcher(BotVsBotMatchWatcherInfo info)
		{
			this.info = info;
		}

		void IWorldLoaded.WorldLoaded(World world, OpenRA.Graphics.WorldRenderer wr)
		{
			if (!TestMode.IsActive || string.IsNullOrEmpty(TestMode.TournamentConfigPath))
				return;

			try
			{
				config = TournamentConfig.LoadFromFile(TestMode.TournamentConfigPath);
				scorer = MatchHarness.CreateScorer(config.Scorer, config);
				winRule = MatchHarness.CreateWinRule(config.WinRule, config);
				state = new MatchTrackingState();

				// Snapshot starting SR ownership for every playable, non-spectator,
				// non-neutral player. The win rule checks these references against the
				// current owner each tick.
				foreach (var player in world.Players.Where(p => p.Playable && !p.NonCombatant && !p.Spectating))
				{
					var srActor = world.Actors.FirstOrDefault(a =>
						a.Owner == player
						&& a.Info.Name == info.SupplyRouteActorType
						&& !a.IsDead
						&& a.IsInWorld);

					if (srActor != null)
						state.OriginalSrOwner[player] = srActor;
				}

				active = state.OriginalSrOwner.Count >= 2;
				if (!active)
				{
					Log.Write("debug", $"[Tournament] Skipping — found {state.OriginalSrOwner.Count} eligible players with SRs (need 2+).");
					TestMode.WriteResult("skip", "tournament: fewer than 2 SR-owning players found");
					return;
				}

				countdown = info.EvaluationInterval;
				Log.Write("debug", $"[Tournament] active — scorer={config.Scorer} winrule={config.WinRule} timeLimit={config.TimeLimitTicks} ticks ({config.TimeLimitSeconds}s) players={state.OriginalSrOwner.Count}");
			}
			catch (Exception e)
			{
				Log.Write("debug", $"[Tournament] init failed: {e.Message}");
				TestMode.WriteResult("fail", $"tournament init failed: {e.Message}");
				active = false;
			}
		}

		void ITick.Tick(Actor self)
		{
			if (!active || finished)
				return;

			if (--countdown > 0)
				return;
			countdown = info.EvaluationInterval;

			var world = self.World;

			// Score every tracked player.
			var scores = new Dictionary<Player, MatchScoreSnapshot>();
			foreach (var p in state.OriginalSrOwner.Keys)
				scores[p] = scorer.ComputeScore(p, world, state);

			// Evaluate win rule.
			var verdict = winRule.EvaluateEndState(world, state, scores, world.WorldTick, config.TimeLimitTicks);
			if (verdict == null)
				return;

			finished = true;
			WriteVerdictAndExit(verdict);
		}

		void WriteVerdictAndExit(MatchVerdict verdict)
		{
			var json = SerializeVerdict(verdict);
			TestMode.WriteResult("pass", json);

			Log.Write("debug", $"[Tournament] match ended at tick {verdict.EndTick}: winner={verdict.Winner?.PlayerName ?? "<none>"} reason={verdict.Reason}");
			Game.Exit();
		}

		static string SerializeVerdict(MatchVerdict verdict)
		{
			// Manual JSON build (no System.Text.Json dependency on the engine project).
			// Embedded inside TestMode's `notes` string field — escaping done by TestMode.WriteResult.
			var sb = new StringBuilder();
			sb.Append("{");
			sb.Append("\"verdict_version\":1,");
			sb.Append($"\"duration_ticks\":{verdict.EndTick},");
			sb.Append($"\"winner_client_index\":{verdict.Winner?.ClientIndex ?? -1},");
			sb.Append($"\"winner_name\":\"{Escape(verdict.Winner?.PlayerName ?? "")}\",");
			sb.Append($"\"win_reason\":\"{Escape(verdict.Reason)}\",");
			sb.Append("\"players\":[");

			var first = true;
			foreach (var kv in verdict.Scores)
			{
				if (!first) sb.Append(",");
				first = false;

				var player = kv.Key;
				var snap = kv.Value;

				sb.Append("{");
				sb.Append($"\"name\":\"{Escape(player.PlayerName)}\",");
				sb.Append($"\"client_index\":{player.ClientIndex},");
				sb.Append($"\"bot_type\":\"{Escape(player.BotType ?? "")}\",");
				sb.Append($"\"score_total\":{snap.Total},");
				sb.Append("\"score_components\":{");

				var compFirst = true;
				foreach (var comp in snap.Components)
				{
					if (!compFirst) sb.Append(",");
					compFirst = false;
					sb.Append($"\"{Escape(comp.Key)}\":{comp.Value}");
				}

				sb.Append("}}");
			}

			sb.Append("]}");
			return sb.ToString();
		}

		static string Escape(string s)
		{
			if (string.IsNullOrEmpty(s))
				return "";
			return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
		}
	}
}
