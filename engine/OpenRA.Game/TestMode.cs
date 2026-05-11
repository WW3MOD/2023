#region Copyright & License Information
/*
 * WW3MOD developer test harness.
 * Activated by `Test.Mode=true` launch arg. Without the arg, every member here
 * is `false`/`null` and no UI, no file writes, no behavior change occurs.
 */
#endregion

using System;
using System.IO;

namespace OpenRA
{
	public static class TestMode
	{
		public static bool IsActive { get; private set; }
		public static string Name { get; private set; }
		public static string Description { get; set; }
		public static string ResultPath { get; private set; }

		// AI tournament harness — path to tournament.yaml. Activates BotVsBotMatchWatcher.
		// Null/empty when not running a tournament match. See:
		//   engine/OpenRA.Mods.Common/Traits/World/BotVsBotMatchWatcher.cs
		//   WORKSPACE/plans/260511_ai_tournament_harness.md
		public static string TournamentConfigPath { get; private set; }

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

			Console.WriteLine($"[TestMode] active — name={Name} result={ResultPath}");
			if (!string.IsNullOrEmpty(TournamentConfigPath))
				Log.Write("debug", $"[TestMode] tournament config: {TournamentConfigPath}");
		}

		public static void WriteResult(string status, string notes)
		{
			if (!IsActive || string.IsNullOrEmpty(ResultPath))
				return;

			var safeName = (Name ?? "unnamed").Replace("\"", "\\\"");
			var safeStatus = (status ?? "").Replace("\"", "\\\"");
			var safeNotes = (notes ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")
				.Replace("\r", "").Replace("\n", "\\n");
			var json = "{"
				+ $"\"name\":\"{safeName}\","
				+ $"\"status\":\"{safeStatus}\","
				+ $"\"notes\":\"{safeNotes}\","
				+ $"\"timestamp\":\"{DateTime.UtcNow:o}\""
				+ "}";

			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(ResultPath));
				File.WriteAllText(ResultPath, json);
				Console.WriteLine($"[TestMode] result written: {status}");
			}
			catch (Exception e)
			{
				Console.WriteLine($"[TestMode] failed to write result: {e.Message}");
			}
		}
	}
}
