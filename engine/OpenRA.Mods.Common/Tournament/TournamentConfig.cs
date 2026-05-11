#region Copyright & License Information
/*
 * WW3MOD AI tournament harness — config loaded from tournament.yaml.
 *
 * Schema:
 *
 *   Matchup:
 *       P1Bot: normal             # informational; bot assignment is in map.yaml
 *       P2Bot: v2                 # informational
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
			public string P1Bot = "normal";
			public string P2Bot = "normal";
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

		/// <summary>Convert time limit to ticks at standard 40 ms tick (25 ticks/second).</summary>
		public int TimeLimitTicks => TimeLimitSeconds * 25;
	}
}
