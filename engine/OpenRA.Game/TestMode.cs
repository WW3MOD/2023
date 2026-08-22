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

		// When true, MainMenuLogic clicks straight through to the Skirmish lobby
		// after the menu loads, so external screenshot drivers don't need to
		// simulate input. Set via Test.OpenSkirmishLobby=true launch arg.
		public static bool OpenSkirmishLobby { get; private set; }

		// Investigation scaffolding: keep the autotest's real RenderPlayer instead of the
		// full-map world view, so a capture shows what a fogged player actually sees.
		public static bool KeepRenderPlayer { get; private set; }

		// Tab to switch to once the lobby is open. "Match" (default) | "Advanced" | "Music".
		// LobbyLogic checks this once after constructing the panel. Set via
		// Test.OpenLobbyTab=Advanced launch arg.
		public static string OpenLobbyTab { get; private set; }

		// Map UID/filename to seed into the skirmish lobby when OpenSkirmishLobby
		// is set. When null/empty, the lobby uses the default ChooseInitialMap
		// pick — usually fine, but unstable across machines (last-played map can
		// drift). Set via Test.LaunchLobbyMap=<map-id> for deterministic captures.
		public static string LaunchLobbyMap { get; private set; }

		// Lobby option id whose checkbox should be hovered once the options panel is
		// built, so a capture can show its tooltip. ButtonTooltipLogic lays tooltips
		// out with no word wrapping, so the only way to judge the width one produces
		// is to look at it. Set via Test.HoverLobbyOption=<option-id>.
		public static string HoverLobbyOption { get; private set; }

		// Ingame info panel to open automatically once a match is running, so
		// screenshot drivers can capture the ingame menu's tabs without simulating
		// input. "Debug" opens the debug/cheats panel (needs the cheats lobby
		// option); anything else non-empty opens the default Options view. Set via
		// Test.OpenIngameInfoPanel=Debug launch arg.
		public static string OpenIngameInfoPanel { get; private set; }

		// Path to a marker file LobbyLogic touches once MapIsPlayable. External
		// drivers (tools/autotest/screenshot-lobby.sh) poll this to know when
		// it's safe to fire a "screenshot" command — without this signal they
		// have to blind-sleep and risk capturing the loading state. Set via
		// Test.LobbyReadyFile=<path> launch arg.
		public static string LobbyReadyFile { get; private set; }

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

		// The authoritative lobby seed the match actually used — captured by World
		// once the seed is resolved (from RandomSeedOverride or the DateTime.Now
		// fallback). Distinct from RandomSeedOverride, which is the *requested*
		// override and stays null when the harness passes no seed. Stamped into the
		// verdict so any single-test run is reproducible by re-passing it via
		// run-test.sh --seed. Set by World; null until a world with Test.Mode loads.
		public static int? ResolvedSeed { get; set; }

		// World.Timestep multiplier applied at WorldLoaded by the tournament
		// watcher. Works the same way the in-game SpeedControlButton works:
		//   world.Timestep = max(1, baseTimestep / multiplier)
		// Supports up to ~8× (matches the SpeedControlButton's range). This is
		// FASTER and MORE RELIABLE than Test.GameSpeed=fastest, which is capped
		// at 2× and applied via a lobby setup order that races state-Ready.
		public static int SpeedMultiplier { get; private set; } = 1;

		// Arms sync reporting even with a single human client, and makes the GameSaved
		// acknowledgement dump the recording side's sync state. Diagnostic scaffolding for
		// saved-game restore desyncs, which are single-client by construction and therefore
		// invisible to the normal humanClients > 1 gate. Expensive (per-net-frame reflection
		// over every synced trait), so it stays OFF unless explicitly asked for.
		// Set via Test.ForceSyncReports=true.
		public static bool ForceSyncReports { get; private set; }

		// Resolved output path for the per-net-frame sync-hash trace (OrderManager). Null/empty
		// = no file and no work. Set via Test.SyncHashLog, same shape as Test.MissileTraceLog.
		// Exists because the sync-report ring holds 32 frames and only dumps on a game-save
		// acknowledgement, and reports are hard-disabled under a ReplayConnection — so nothing
		// shipped can answer "at which net frame did these two runs first disagree?" across a
		// whole match. Records exactly the hash SendSync already computes, so the trace is the
		// network's own view of the simulation and not a second, differently-derived one.
		public static string SyncHashLogPath { get; private set; }

		// Resolved output path for the UnitLifecycleLogger world trait's JSONL
		// event stream. Null/empty = the logger is inert (no file, no per-tick
		// work). Set via the Test.UnitLifecycleLog launch arg:
		//   Test.UnitLifecycleLog=true  → <ResultPath> with a .lifecycle.jsonl
		//                                  extension (sibling of the verdict, like
		//                                  BotVsBotMatchWatcher's .watcher.log).
		//   Test.UnitLifecycleLog=<path> → that explicit path.
		// Absent when the arg is not passed, so the trait no-ops in normal play.
		public static string UnitLifecycleLogPath { get; private set; }

		// Resolved output path for the Phase-0 missile trace (MissileTrace.cs).
		// Null/empty = the trace is inert (no file, no per-tick work, no records).
		// Set via the Test.MissileTraceLog launch arg, same shape as
		// Test.UnitLifecycleLog: `true` derives a `.missiles.jsonl` sibling of the
		// verdict, anything else is an explicit path.
		public static string MissileTraceLogPath { get; private set; }

		// false suppresses the per-tick lines and keeps only the per-missile summary
		// records. Set via Test.MissileTraceMode=summary. The engagement-distance
		// sweep produces thousands of missiles and only needs the summaries.
		public static bool MissileTraceTicks { get; private set; } = true;

		// Number of CreateEffectWarhead impacts that PASSED THE IMPACT VALIDITY GATES — i.e. were not
		// discarded as landing on an invalid actor or invalid terrain. Counted at that decision, which
		// is upstream of both the sprite (skipped when the warhead defines no Image/Explosions) and the
		// sound (skipped by ImpactSoundChance), so this is NOT a count of sprites drawn or sounds
		// played and must not be asserted on as one. Exposed as Test.GetImpactEffectCount() because a
		// swallowed impact and a normal one are otherwise indistinguishable from Lua.
		public static int ImpactEffectCount { get; private set; }

		public static void CountImpactEffect()
		{
			if (IsActive)
				ImpactEffectCount++;
		}

		public static void Initialize(Arguments args)
		{
			var modeArg = args.GetValue("Test.Mode", null);
			if (string.IsNullOrEmpty(modeArg) || !string.Equals(modeArg, "true", StringComparison.OrdinalIgnoreCase))
				return;

			IsActive = true;
			ImpactEffectCount = 0;
			Name = args.GetValue("Test.Name", "unnamed");
			Description = args.GetValue("Test.Description", "");
			ResultPath = args.GetValue("Test.ResultPath",
				Path.Combine(Platform.SupportDir, "ww3mod-test-result.json"));
			KeepRenderPlayer = string.Equals(args.GetValue("Test.KeepRenderPlayer", null), "true", StringComparison.OrdinalIgnoreCase);
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
			OpenSkirmishLobby = string.Equals(args.GetValue("Test.OpenSkirmishLobby", ""), "true", StringComparison.OrdinalIgnoreCase);
			OpenLobbyTab = args.GetValue("Test.OpenLobbyTab", null);
			LaunchLobbyMap = args.GetValue("Test.LaunchLobbyMap", null);
			HoverLobbyOption = args.GetValue("Test.HoverLobbyOption", null);
			LobbyReadyFile = args.GetValue("Test.LobbyReadyFile", null);
			OpenIngameInfoPanel = args.GetValue("Test.OpenIngameInfoPanel", null);
			ForceSyncReports = string.Equals(args.GetValue("Test.ForceSyncReports", ""), "true", StringComparison.OrdinalIgnoreCase);

			// UnitLifecycleLogger gate. "true"/"1" derives a sibling of the verdict
			// file; anything else is an explicit output path. Left null (inert) when
			// the arg is absent, matching the off-by-default discipline of the harness.
			var lifecycleArg = args.GetValue("Test.UnitLifecycleLog", null);
			if (!string.IsNullOrEmpty(lifecycleArg))
			{
				var lower = lifecycleArg.ToLowerInvariant();
				if (lower == "true" || lower == "1")
					UnitLifecycleLogPath = string.IsNullOrEmpty(ResultPath)
						? null
						: Path.ChangeExtension(ResultPath, ".lifecycle.jsonl");
				else
					UnitLifecycleLogPath = lifecycleArg;
			}

			var missileArg = args.GetValue("Test.MissileTraceLog", null);
			if (!string.IsNullOrEmpty(missileArg))
			{
				var lower = missileArg.ToLowerInvariant();
				if (lower == "true" || lower == "1")
					MissileTraceLogPath = string.IsNullOrEmpty(ResultPath)
						? null
						: Path.ChangeExtension(ResultPath, ".missiles.jsonl");
				else
					MissileTraceLogPath = missileArg;
			}

			var syncHashArg = args.GetValue("Test.SyncHashLog", null);
			if (!string.IsNullOrEmpty(syncHashArg))
			{
				var lower = syncHashArg.ToLowerInvariant();
				if (lower == "true" || lower == "1")
					SyncHashLogPath = string.IsNullOrEmpty(ResultPath)
						? null
						: Path.ChangeExtension(ResultPath, ".synchash.tsv");
				else
					SyncHashLogPath = syncHashArg;
			}

			MissileTraceTicks = !string.Equals(args.GetValue("Test.MissileTraceMode", "full"), "summary", StringComparison.OrdinalIgnoreCase);

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
			if (!string.IsNullOrEmpty(UnitLifecycleLogPath))
				Log.Write("debug", $"[TestMode] unit lifecycle log: {UnitLifecycleLogPath}");
			if (!string.IsNullOrEmpty(MissileTraceLogPath))
				Log.Write("debug", $"[TestMode] missile trace log: {MissileTraceLogPath} (ticks={MissileTraceTicks})");
			if (!string.IsNullOrEmpty(SyncHashLogPath))
				Log.Write("debug", $"[TestMode] sync hash log: {SyncHashLogPath}");
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

			// Additive (schema-stable): the authoritative match seed, so a single-test
			// verdict is traceable to the run that produced it. Absent only when the
			// world never loaded (e.g. an init-failure verdict). Reproduce via --seed.
			if (ResolvedSeed.HasValue)
				json.Append($",\"seed\":{ResolvedSeed.Value}");

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
