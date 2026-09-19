#region Copyright & License Information
/*
 * WW3MOD AI tournament harness — config loaded from tournament.yaml.
 *
 * Schema:
 *
 *   Matchup:
 *       P1Bot: stable             # informational; bot assignment is in map.yaml
 *       P2Bot: experimental                 # informational
 *   TimeLimitSeconds: 720         # match deadline in real game seconds
 *   Scorer: weighted_components   # MatchHarness registry key
 *   WinRule: score_or_sr_capture  # MatchHarness registry key
 *   Score:
 *       ArmyValueWeight: 1.0
 *       CaptureIncomeWeight: 2.0
 *       KillsValueWeight: 1.0
 *       SrCaptureBonus: 100000
 *   ScoreMarginForDecisive: 0.20
 */
#endregion

namespace OpenRA.Mods.Common.Tournament
{
	public class TournamentConfig
	{
		public class MatchupConfig
		{
			public string P1Bot = "stable";
			public string P2Bot = "stable";
		}

		public class ScoreConfig
		{
			public float ArmyValueWeight = 1.0f;
			public float CaptureIncomeWeight = 2.0f;
			public float KillsValueWeight = 1.0f;
			public long SrCaptureBonus = 100000;
		}

		public MatchupConfig Matchup = new MatchupConfig();
		public int TimeLimitSeconds = 720;
		public string Scorer = "weighted_components";
		public string WinRule = "score_or_sr_capture";
		public ScoreConfig Score = new ScoreConfig();
		public float ScoreMarginForDecisive = 0.20f;

		// Game speed key (e.g. "default", "fastest"). Must match a key in the
		// mod's GameSpeeds dictionary (engine/mods/ra/mod.yaml). The launcher
		// passes this via Test.GameSpeed; Game.LoadMap applies it to the
		// initial "option gamespeed" setup order. CAPPED AT 2× by the mod's
		// GameSpeeds config — for higher speeds use SpeedMultiplier (below).
		public string GameSpeed = "default";

		// Speed multiplier applied at WorldLoaded by lowering world.Timestep.
		// Range 1..16; 8× is the limit of the in-game SpeedControlButton.
		// MUCH more effective than GameSpeed for tournament batches. Set 0 to
		// fall back to whatever Test.SpeedMultiplier launch arg provides
		// (default 1× = real-time).
		public int SpeedMultiplier = 0;

		public static TournamentConfig LoadFromFile(string path)
		{
			var yaml = MiniYaml.FromFile(path);
			var config = new TournamentConfig();

			foreach (var node in yaml)
			{
				switch (node.Key)
				{
					case "Matchup":
						FieldLoader.Load(config.Matchup, node.Value);
						break;
					case "TimeLimitSeconds":
						config.TimeLimitSeconds = FieldLoader.GetValue<int>("TimeLimitSeconds", node.Value.Value);
						break;
					case "Scorer":
						config.Scorer = node.Value.Value;
						break;
					case "WinRule":
						config.WinRule = node.Value.Value;
						break;
					case "Score":
						FieldLoader.Load(config.Score, node.Value);
						break;
					case "ScoreMarginForDecisive":
						config.ScoreMarginForDecisive = FieldLoader.GetValue<float>("ScoreMarginForDecisive", node.Value.Value);
						break;
					case "GameSpeed":
						config.GameSpeed = node.Value.Value;
						break;
					case "SpeedMultiplier":
						config.SpeedMultiplier = FieldLoader.GetValue<int>("SpeedMultiplier", node.Value.Value);
						break;
				}
			}

			return config;
		}

		/// <summary>
		/// Match deadline in TICKS at a given millisecond timestep. 720 s at the mod's 60 ms default
		/// is 12000 ticks, which is the identity to check any change here against.
		/// </summary>
		// WAS `public int TimeLimitTicks => TimeLimitSeconds * 25;`, with a doc comment asserting a
		// "standard 40 ms tick". That is an RA-era assumption and it is wrong for every tournament
		// this repo runs: no shipped tournament*.yaml sets a GameSpeed key and run-tournament.sh
		// defaults GAME_SPEED="default", whose Timestep is 60 ms = 16.667 ticks/s. So
		// `TimeLimitSeconds: 720` was 18000 ticks = 1080 real seconds, and every tournament.yaml's
		// own comment ("720s = 12 in-game minutes at standard 1x speed") was wrong by that 1.5x.
		//
		// CONSEQUENCE OF THE FIX, STATED PLAINLY: tournament matches get ~33 % SHORTER in ticks
		// (720 s: 18000 -> 12000) and now last exactly the seconds they are configured for. Any
		// benchmark baseline taken before 2026-09-19 was taken over a longer match and does not
		// compare. run-tournament.sh's wall-clock budget is 4x TimeLimitSeconds, which was absorbing
		// the 1.5x; it is now 4x a match that really does take TimeLimitSeconds, so it gets more
		// headroom rather than less and no run can start timing out because of this.
		//
		// PASS THE CONFIGURED TIMESTEP, NOT world.Timestep: BotVsBotMatchWatcher lowers the latter
		// to apply SpeedMultiplier, and feeding that back in here would multiply the deadline by the
		// speed-up instead of leaving the tick count alone.
		public int TimeLimitTicksAt(int timestepMilliseconds)
		{
			return TickTime.TicksForSeconds(TimeLimitSeconds, timestepMilliseconds);
		}
	}
}
