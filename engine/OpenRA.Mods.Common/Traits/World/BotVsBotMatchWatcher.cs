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
 *     "verdict_version": 5,
 *     "scenario": "<test-name>",
 *     "seed": <int>,
 *     "git_sha": "<run-tournament.sh stamps this>",
 *     "duration_ticks": <int>,
 *     "winner_client_index": <int>,
 *     "win_reason": "sr_capture"|"time_limit"|...,
 *     "players": [
 *       { "name": "...", "client_index": 0,
 *         "score_total": <long>,
 *         "score_components": { "army_value": ..., "capture_income": ..., ... },
 *         "stats": { "units_killed": ..., "units_dead": ..., "buildings_killed": ...,
 *                    "buildings_dead": ..., "kills_cost": ..., "deaths_cost": ...,
 *                    "army_value": ..., "assets_value": ..., "order_count": ...,
 *                    "experience": ..., "resources_earned": ... }
 *       },
 *       ...
 *     ]
 *   }
 *
 * v2 (additive, schema-stable): added the per-player "stats" object carrying the
 * full PlayerStatistics observer menu + cumulative PlayerResources.Earned.
 * v3 (additive, schema-stable): added stats."capture_income_gross" — cumulative
 * GROSS building income (pre-upkeep) granted to the player by income structures
 * they own/capture, integrated read-only from PlayerResources.TotalBuildingIncome.
 * This is the S1 economy metric; resources_earned (net) stays byte-compatible for
 * context. No scorer/win-rule input changed (see GrossIncomeIntegrator).
 * v4 (semantics, schema-stable): the scorer's economy term now derives from the
 * GROSS integral (capture_income_gross) instead of net resources_earned, so the
 * emitted score_components.capture_income value — and therefore the WinRule outcome
 * — counts held-derrick income in the SR-budget economy (LADDER S1 follow-up 1a).
 * No JSON field was added or removed; resources_earned and capture_income_gross are
 * both unchanged. The version bump flags the changed *meaning* of the emitted
 * capture_income score component.
 * v5 (additive, schema-stable): emit the top-level "seed" field (the lobby RandomSeed
 * actually used — from Test.RandomSeed or the DateTime.Now fallback). Pairs with the
 * World.cs LocalRandom seeding fix so a fixed seed now reproduces a whole match; the
 * verdict is self-describing (traceable to its seed without index arithmetic). The
 * bot-decision stream is a documented pure function of this seed:
 * localSeed = (int)(seed * 6364136223846793005 + 1442695040888963407).
 *
 * See:
 *   WORKSPACE/plans/260511_ai_tournament_harness.md
 *   WORKSPACE/ai/tournament_swap_guide.md
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
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

		// The authoritative match seed (lobby RandomSeed — from Test.RandomSeed or the
		// DateTime.Now fallback), captured at load so SerializeVerdict can stamp the verdict.
		int capturedSeed;

		// SR discovery is deferred to first tick because IWorldLoaded fires
		// BEFORE SpawnMapActors creates the actor instances. See PITFALLS.md §11.
		bool srDiscoveryDone;
		Action<string> diag;

		public BotVsBotMatchWatcher(BotVsBotMatchWatcherInfo info)
		{
			this.info = info;
		}

		void IWorldLoaded.WorldLoaded(World world, OpenRA.Graphics.WorldRenderer wr)
		{
			if (!TestMode.IsActive || string.IsNullOrEmpty(TestMode.TournamentConfigPath))
				return;

			capturedSeed = world.LobbyInfo.GlobalSettings.RandomSeed;

			// Diagnostic log next to the verdict file. Explicit flush — Log.Write is
			// buffered with a 5s timer that doesn't reliably fire before Game.Exit.
			var diagPath = !string.IsNullOrEmpty(TestMode.ResultPath)
				? Path.ChangeExtension(TestMode.ResultPath, ".watcher.log")
				: null;

			diag = msg =>
			{
				try
				{
					if (diagPath != null)
						File.AppendAllText(diagPath, msg + "\n");
				}
				catch { /* best-effort */ }
				Log.Write("debug", "[Tournament] " + msg);
			};

			try
			{
				config = TournamentConfig.LoadFromFile(TestMode.TournamentConfigPath);
				scorer = MatchHarness.CreateScorer(config.Scorer, config);
				winRule = MatchHarness.CreateWinRule(config.WinRule, config);
				state = new MatchTrackingState();

				// Apply speed multiplier by lowering world.Timestep (same mechanism the
				// in-game SpeedControlButton uses). Caps at 16× via TestMode arg
				// validation. PITFALL: Test.GameSpeed=fastest is only 2× and applied
				// via lobby setup order that may race — use Test.SpeedMultiplier
				// instead for reliable acceleration up to 8×.
				var effectiveMultiplier = config.SpeedMultiplier > 0 ? config.SpeedMultiplier : TestMode.SpeedMultiplier;
				if (effectiveMultiplier > 1)
				{
					var oldTimestep = world.Timestep;
					var newTimestep = System.Math.Max(1, oldTimestep / effectiveMultiplier);
					world.Timestep = newTimestep;
					diag($"WorldLoaded: speed multiplier {effectiveMultiplier}x — Timestep {oldTimestep} → {newTimestep} ms/tick");
				}

				// Light load-time logging only. SR discovery is deferred to first
				// Tick because IWorldLoaded fires BEFORE SpawnMapActors instantiates
				// the actors — at this point world.Actors doesn't yet include them.
				diag($"WorldLoaded: scorer={config.Scorer} winrule={config.WinRule} timeLimit={config.TimeLimitTicks} ticks ({config.TimeLimitSeconds}s)");
				diag($"WorldLoaded: world has {world.Players.Length} players, {world.Actors.Count()} actors (pre-SpawnMapActors)");

				active = true;
				countdown = info.EvaluationInterval;
			}
			catch (Exception e)
			{
				diag?.Invoke($"init failed: {e.Message}");
				TestMode.WriteResult("fail", $"tournament init failed: {e.Message}");
				active = false;
			}
		}

		void DiscoverSrsOnFirstTick(World world)
		{
			srDiscoveryDone = true;

			diag($"FirstTick: world has {world.Actors.Count()} actors total");
			foreach (var a in world.Actors.Where(a => a.Info.Name == info.SupplyRouteActorType))
				diag($"  {info.SupplyRouteActorType} #{a.ActorID} owned by {a.Owner?.InternalName ?? "<null>"} at {a.Location} (IsInWorld={a.IsInWorld} IsDead={a.IsDead})");

			// Filter to actual bot combatants. The Observer player (local human's
			// spectator slot) is Playable but spectating in intent — its PlayerReference
			// has Spectating: True but the lobby-slot path in Player.cs ignores that
			// for playable slots, so the runtime Spectating flag stays false. Use
			// IsBot as the discriminator: tournament scenarios place bot combatants
			// only, never humans.
			foreach (var player in world.Players.Where(p => !p.NonCombatant && p.IsBot))
			{
				var srActor = world.Actors.FirstOrDefault(a =>
					a.Owner == player
					&& a.Info.Name == info.SupplyRouteActorType
					&& !a.IsDead
					&& a.IsInWorld);

				if (srActor != null)
				{
					state.OriginalSrOwner[player] = srActor;
					diag($"  → {player.InternalName} owns SR at {srActor.Location}");
				}
				else
				{
					var owned = string.Join(", ", world.Actors.Where(a => a.Owner == player).Select(a => a.Info.Name));
					diag($"  → {player.InternalName} has NO {info.SupplyRouteActorType} (their actors: {owned})");
				}
			}

			if (state.OriginalSrOwner.Count < 2)
			{
				diag($"Skipping — found {state.OriginalSrOwner.Count} eligible players with SRs (need 2+).");
				TestMode.WriteResult("skip", "tournament: fewer than 2 SR-owning players found");
				active = false;
				finished = true;
				Game.Exit();
			}
			else
			{
				diag($"Match active with {state.OriginalSrOwner.Count} SR-owning players");
			}
		}

		void ITick.Tick(Actor self)
		{
			if (!active || finished)
				return;

			if (!srDiscoveryDone)
			{
				DiscoverSrsOnFirstTick(self.World);
				if (!active || finished)
					return;
			}

			// Integrate GROSS building income every tick (read-only observation — see
			// GrossIncomeIntegrator). Sampled before the per-second countdown gate so the
			// integral covers every tick, not just evaluation intervals.
			AccumulateGrossIncome();

			if (--countdown > 0)
				return;
			countdown = info.EvaluationInterval;

			var world = self.World;

			// Score every tracked player.
			var scores = new Dictionary<Player, MatchScoreSnapshot>();
			foreach (var p in state.OriginalSrOwner.Keys)
				scores[p] = scorer.ComputeScore(p, world, state);

			// Periodic diagnostic — every 5 evaluations (5 sec real-time at 25/s).
			if (world.WorldTick % (info.EvaluationInterval * 5) < info.EvaluationInterval)
			{
				var scoreStr = string.Join(" / ", scores.Select(s => $"{s.Key.InternalName}={s.Value.Total}"));
				diag?.Invoke($"tick={world.WorldTick} scores: {scoreStr}");
			}

			// Evaluate win rule.
			var verdict = winRule.EvaluateEndState(world, state, scores, world.WorldTick, config.TimeLimitTicks);
			if (verdict == null)
				return;

			finished = true;
			WriteVerdictAndExit(verdict);
		}

		// Read-only: accumulates each tracked player's gross building income by observing
		// PlayerResources.TotalBuildingIncome. Never mutates actor/player/trait state.
		void AccumulateGrossIncome()
		{
			foreach (var p in state.OriginalSrOwner.Keys)
			{
				var pr = p.PlayerActor?.TraitOrDefault<PlayerResources>();
				if (pr == null)
					continue;

				if (!state.GrossCaptureIncome.TryGetValue(p, out var integrator))
				{
					integrator = new Tournament.GrossIncomeIntegrator();
					state.GrossCaptureIncome[p] = integrator;
				}

				integrator.Tick(pr.TotalBuildingIncome, pr.Info.PassiveIncomeInterval);
			}
		}

		void WriteVerdictAndExit(MatchVerdict verdict)
		{
			var json = SerializeVerdict(verdict, state, capturedSeed);
			TestMode.WriteResult("pass", json);

			Log.Write("debug", $"[Tournament] match ended at tick {verdict.EndTick}: winner={verdict.Winner?.PlayerName ?? "<none>"} reason={verdict.Reason}");
			Game.Exit();
		}

		static string SerializeVerdict(MatchVerdict verdict, MatchTrackingState state, int seed)
		{
			// Manual JSON build (no System.Text.Json dependency on the engine project).
			// Embedded inside TestMode's `notes` string field — escaping done by TestMode.WriteResult.
			var sb = new StringBuilder();
			sb.Append("{");
			sb.Append("\"verdict_version\":5,");
			sb.Append($"\"seed\":{seed},");
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
				sb.Append($"\"faction\":\"{Escape(player.Faction?.InternalName ?? "")}\",");
				sb.Append($"\"score_total\":{snap.Total},");
				sb.Append("\"score_components\":{");

				var compFirst = true;
				foreach (var comp in snap.Components)
				{
					if (!compFirst) sb.Append(",");
					compFirst = false;
					sb.Append($"\"{Escape(comp.Key)}\":{comp.Value}");
				}

				sb.Append("},");

				// Full PlayerStatistics (the observer "menu" stats) plus cumulative earned
				// resources. Additive to the schema — existing fields above are unchanged.
				// PITFALL §14 (WORKSPACE/ai/archive/PITFALLS.md): PlayerStatistics.Income is a
				// rolling/sampled figure, NOT lifetime income — use PlayerResources.Earned for
				// cumulative cash earned.
				var stats = player.PlayerActor?.TraitOrDefault<PlayerStatistics>();
				var playerResources = player.PlayerActor?.TraitOrDefault<PlayerResources>();
				sb.Append("\"stats\":{");
				sb.Append($"\"units_killed\":{stats?.UnitsKilled ?? 0},");
				sb.Append($"\"units_dead\":{stats?.UnitsDead ?? 0},");
				sb.Append($"\"buildings_killed\":{stats?.BuildingsKilled ?? 0},");
				sb.Append($"\"buildings_dead\":{stats?.BuildingsDead ?? 0},");
				sb.Append($"\"kills_cost\":{stats?.KillsCost ?? 0},");
				sb.Append($"\"deaths_cost\":{stats?.DeathsCost ?? 0},");
				sb.Append($"\"army_value\":{stats?.ArmyValue ?? 0},");
				sb.Append($"\"assets_value\":{stats?.AssetsValue ?? 0},");
				sb.Append($"\"order_count\":{stats?.OrderCount ?? 0},");
				sb.Append($"\"experience\":{stats?.Experience ?? 0},");
				sb.Append($"\"resources_earned\":{playerResources?.Earned ?? 0},");
				// v3: cumulative GROSS building income (pre-upkeep) — the S1 economy metric.
				// resources_earned above (net) is unchanged and kept for context.
				sb.Append($"\"capture_income_gross\":{state.GrossCaptureIncomeFor(player)}");
				sb.Append("}");

				sb.Append("}");
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
