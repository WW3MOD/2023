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
		// "standard 40 ms tick". That is an RA-era assumption, and whether it was WRONG depends
		// entirely on a key most readers of that line never looked at.
		//
		// ==== IT SPLITS THE SHIPPED CONFIGS IN TWO, 11 AGAINST 41 (counted 2026-09-19) ====
		// run-tournament.sh:148 reads `GameSpeed:` out of the config and passes it as Test.GameSpeed;
		// Game.LoadMap:1205 turns that into the lobby `option gamespeed` order, and World.cs:217-220
		// resolves world.GameSpeed from it. So the config key really does decide the timestep.
		//
		//   * The 41 configs that set `GameSpeed: fastest` run at Timestep 40, where 1000/40 = 25
		//     exactly. `* 25` was CORRECT there and TicksForSeconds(n, 40) returns the identical
		//     number. Nothing about smoke/sanity/quick/eco/combat-12min runs has moved.
		//   * The 11 plain tournament.yaml files set no GameSpeed and run at the 60 ms default =
		//     16.667 ticks/s. There `* 25` was wrong: `TimeLimitSeconds: 720` was 18000 ticks =
		//     1080 REAL seconds, and each file's own prose ("720s = 12 in-game minutes") described
		//     a match that had never been played.
		//
		// A WW3MOD bug entry dated 2026-09-19 states "no shipped tournament*.yaml sets a GameSpeed
		// key at all". That is true of the 11 and false of the other 41; it generalised from the
		// files it happened to open. Check the key before reasoning about any tournament's duration.
		//
		// CONSEQUENCE, STATED PLAINLY: the 11 were restated 720 -> 1080 in the same change, so their
		// tick count is unchanged at 18000 and every recorded baseline still compares. What changed
		// is that the number now means what it says. run-tournament.sh's wall budget is 4x
		// TimeLimitSeconds and therefore rose with it, so no run can start timing out because of this.
		//
		// THE NEW FAILURE MODE THIS INTRODUCES, because it did not exist while the 25 was hardcoded:
		// the deadline is now a function of world.GameSpeed. If the `option gamespeed` order ever
		// fails to apply -- Game.cs:1200-1204 warns that an unknown key falls back to default
		// SILENTLY -- a `fastest` config would quietly run 33 % short instead of being immune. The
		// timestep actually used is logged at WorldLoaded by BotVsBotMatchWatcher for exactly that
		// reason; read it before trusting a tournament duration.
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
