#region Copyright & License Information
/*
 * WW3MOD developer test harness.
 * Activated by `Test.Mode=true` launch arg. Without the arg, every member here
 * is `false`/`null` and no UI, no file writes, no behavior change occurs.
 */
#endregion

using System;
using System.IO;
using System.Text;

namespace OpenRA
{
	public static class TestMode
	{
		public static bool IsActive { get; private set; }
		public static string Name { get; private set; }
		public static string Description { get; set; }
		public static string ResultPath { get; private set; }

		// Per-run screenshot output dir. Routed into TestModeScreenshots.Initialize
		// during TestMode.Initialize. Set via Test.ScreenshotDir launch arg.
		public static string ScreenshotDir { get; private set; }

		// Path to the file-watcher command channel used by Phase 2 external
		// triggers (menu/lobby screenshots). When set, the engine polls this
		// path for commands like "screenshot <label>". Null = watcher dormant.
		public static string ScreenshotCmdFile { get; private set; }

		// AI tournament harness — path to tournament.yaml. Activates BotVsBotMatchWatcher.
		// Null/empty when not running a tournament match. See:
		//   engine/OpenRA.Mods.Common/Traits/World/BotVsBotMatchWatcher.cs
		//   WORKSPACE/plans/260511_ai_tournament_harness.md
		public static string TournamentConfigPath { get; private set; }

		// Override for the initial gamespeed setup order in Game.LoadMap. When set,
		// replaces the hardcoded "default" speed. Valid values are the keys in the
		// mod's GameSpeeds dictionary (slowest, slower, default, fast, faster, fastest).
		// Used by the tournament harness to crank speed without bothering with
		// settings.yaml. Null = use mod default.
		public static string GameSpeedOverride { get; private set; }

		// Override for the local server's random seed. When set, the server uses
		// this exact int instead of DateTime.Now.ToBinary(). Same seed + same code
		// + same map = same match (OpenRA simulation is deterministic). Used by
		// the tournament harness to give each seed-index a reproducible match.
		// Null = non-deterministic (DateTime.Now-based).
		public static int? RandomSeedOverride { get; private set; }

		// World.Timestep multiplier applied at WorldLoaded by the tournament
		// watcher. Works the same way the in-game SpeedControlButton works:
		//   world.Timestep = max(1, baseTimestep / multiplier)
		// Supports up to ~8× (matches the SpeedControlButton's range). This is
		// FASTER and MORE RELIABLE than Test.GameSpeed=fastest, which is capped
		// at 2× and applied via a lobby setup order that races state-Ready.
		public static int SpeedMultiplier { get; private set; } = 1;

		public static void Initialize(Arguments args)
		{
			var modeArg = args.GetValue("Test.Mode", null);
			if (string.IsNullOrEmpty(modeArg) || modeArg.ToLowerInvariant() != "true")
				return;

			IsActive = true;
			Name = args.GetValue("Test.Name", "unnamed");
			Description = args.GetValue("Test.Description", "");
			ResultPath = args.GetValue("Test.ResultPath",
				Path.Combine(Platform.SupportDir, "ww3mod-test-result.json"));
			TournamentConfigPath = args.GetValue("Test.TournamentConfig", null);
			GameSpeedOverride = args.GetValue("Test.GameSpeed", null);

			var seedArg = args.GetValue("Test.RandomSeed", null);
			if (!string.IsNullOrEmpty(seedArg) && int.TryParse(seedArg, out var seed))
				RandomSeedOverride = seed;

			var multArg = args.GetValue("Test.SpeedMultiplier", null);
			if (!string.IsNullOrEmpty(multArg) && int.TryParse(multArg, out var mult) && mult >= 1 && mult <= 16)
				SpeedMultiplier = mult;

			ScreenshotDir = args.GetValue("Test.ScreenshotDir", null);
			ScreenshotCmdFile = args.GetValue("Test.ScreenshotCmdFile", null);
			TestModeScreenshots.Initialize(ScreenshotDir);

			Console.WriteLine($"[TestMode] active — name={Name} result={ResultPath}");
			if (!string.IsNullOrEmpty(TournamentConfigPath))
				Log.Write("debug", $"[TestMode] tournament config: {TournamentConfigPath}");
			if (!string.IsNullOrEmpty(GameSpeedOverride))
				Log.Write("debug", $"[TestMode] gamespeed override: {GameSpeedOverride}");
			if (RandomSeedOverride.HasValue)
				Log.Write("debug", $"[TestMode] random seed override: {RandomSeedOverride.Value}");
			if (SpeedMultiplier > 1)
				Log.Write("debug", $"[TestMode] speed multiplier: {SpeedMultiplier}x");
		}

		public static void WriteResult(string status, string notes)
		{
			if (!IsActive || string.IsNullOrEmpty(ResultPath))
				return;

			var json = new StringBuilder();
			json.Append('{');
			json.Append($"\"name\":\"{JsonEscape(Name ?? "unnamed")}\",");
			json.Append($"\"status\":\"{JsonEscape(status ?? "")}\",");
			json.Append($"\"notes\":\"{JsonEscape(notes ?? "")}\",");
			json.Append($"\"timestamp\":\"{DateTime.UtcNow:o}\"");

			// PITFALL: Game.TakeScreenshot is async (Renderer.SaveScreenshot via
			// ThreadPool). When this verdict is written from Test.Pass/Fail, the
			// PNG files referenced below may still be flushing. The runner waits
			// ~250ms after exit before listing — see tools/autotest/run-test.sh.
			var caps = TestModeScreenshots.Captured;
			if (caps != null && caps.Count > 0)
			{
				json.Append(",\"screenshots\":[");
				for (var i = 0; i < caps.Count; i++)
				{
					if (i > 0) json.Append(',');
					var c = caps[i];
					json.Append('{');
					json.Append($"\"label\":\"{JsonEscape(c.Label)}\",");
					json.Append($"\"path\":\"{JsonEscape(c.Path)}\",");
					json.Append($"\"tick\":{c.Tick},");
					json.Append($"\"note\":\"{JsonEscape(c.Note)}\",");
					json.Append($"\"captured_at\":\"{c.CapturedAt:o}\"");
					json.Append('}');
				}
				json.Append(']');
			}

			json.Append('}');

			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(ResultPath));
				File.WriteAllText(ResultPath, json.ToString());
				Console.WriteLine($"[TestMode] result written: {status}"
					+ (caps != null && caps.Count > 0 ? $" ({caps.Count} screenshot(s))" : ""));
			}
			catch (Exception e)
			{
				Console.WriteLine($"[TestMode] failed to write result: {e.Message}");
			}
		}

		static string JsonEscape(string s)
		{
			if (string.IsNullOrEmpty(s))
				return "";
			return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
				.Replace("\r", "").Replace("\n", "\\n").Replace("\t", "\\t");
		}
	}
}
