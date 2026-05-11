#region Copyright & License Information
/*
 * WW3MOD developer test harness — screenshot capture coordination.
 *
 * When Test.Mode=true and Test.ScreenshotDir is set (or defaulted), routes
 * Test.Screenshot(label) Lua calls and Phase 2 external commands into a
 * per-run directory with controlled filenames. The captured list is
 * serialized into the verdict JSON's "screenshots" array by TestMode.WriteResult.
 *
 * Dormant when Test.Mode=false — Initialize is never called, OutputDir is null,
 * every Capture call short-circuits. No threads, no files, no overhead.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OpenRA
{
	public static class TestModeScreenshots
	{
		public sealed class Entry
		{
			public string Label;
			public string Path;
			public string Note;
			public int Tick;
			public DateTime CapturedAt;
		}

		public static string OutputDir { get; private set; }
		public static IReadOnlyList<Entry> Captured => captured;

		static readonly List<Entry> captured = new List<Entry>();
		static int sequence;

		public static void Initialize(string outputDir)
		{
			if (string.IsNullOrEmpty(outputDir))
			{
				// Reasonable default if launch arg unset but TestMode is active.
				outputDir = Path.Combine(Platform.SupportDir, "Screenshots",
					"test-run-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
			}

			OutputDir = outputDir;
			try
			{
				Directory.CreateDirectory(OutputDir);
				Console.WriteLine($"[TestMode] screenshots dir: {OutputDir}");
			}
			catch (Exception e)
			{
				Log.Write("debug", $"[TestMode] failed to create screenshot dir {OutputDir}: {e.Message}");
				OutputDir = null;
			}
		}

		// Trigger a screenshot to a controlled per-run path. Returns the planned
		// path on success, null if disabled or write-prep failed. The file itself
		// appears on disk shortly after (Renderer.SaveScreenshot is async via
		// ThreadPool — see PITFALL at TestMode.WriteResult call sites).
		public static string Capture(string label, string note = "", int tick = -1)
		{
			if (!TestMode.IsActive || string.IsNullOrEmpty(OutputDir))
				return null;

			sequence++;
			var safeLabel = SanitizeLabel(label);
			var filename = $"{sequence:D3}_{safeLabel}.png";
			var path = Path.Combine(OutputDir, filename);

			try
			{
				Game.TakeScreenshot(path);
				captured.Add(new Entry
				{
					Label = label ?? "",
					Path = path,
					Note = note ?? "",
					Tick = tick,
					CapturedAt = DateTime.UtcNow,
				});

				// Keep manifest.json current for any external observer (Phase 2
				// CLI, agent watching for the label). Cheap — single-digit-kB write.
				SaveManifest();

				return path;
			}
			catch (Exception e)
			{
				Log.Write("debug", $"[TestMode] screenshot capture failed: {e.Message}");
				return null;
			}
		}

		// Manifest of every capture taken in this run, mirroring the verdict
		// JSON's screenshots[] format but always present (even outside autotest
		// scenarios — used by Phase 2 external screenshot mode where there's
		// no Lua / Test.Pass to write the verdict). Rewritten after each
		// Capture(). Agents reading the manifest see captures as they appear.
		public static void SaveManifest()
		{
			if (string.IsNullOrEmpty(OutputDir))
				return;

			var manifestPath = Path.Combine(OutputDir, "manifest.json");
			var sb = new StringBuilder();
			sb.Append('{');
			sb.Append($"\"output_dir\":\"{JsonEscape(OutputDir)}\",");
			sb.Append($"\"updated_at\":\"{DateTime.UtcNow:o}\",");
			sb.Append("\"screenshots\":[");
			for (var i = 0; i < captured.Count; i++)
			{
				if (i > 0) sb.Append(',');
				var c = captured[i];
				sb.Append('{');
				sb.Append($"\"label\":\"{JsonEscape(c.Label)}\",");
				sb.Append($"\"path\":\"{JsonEscape(c.Path)}\",");
				sb.Append($"\"tick\":{c.Tick},");
				sb.Append($"\"note\":\"{JsonEscape(c.Note)}\",");
				sb.Append($"\"captured_at\":\"{c.CapturedAt:o}\"");
				sb.Append('}');
			}
			sb.Append("]}");

			try
			{
				File.WriteAllText(manifestPath, sb.ToString());
			}
			catch (Exception e)
			{
				Log.Write("debug", $"[TestMode] manifest write failed: {e.Message}");
			}
		}

		static string JsonEscape(string s)
		{
			if (string.IsNullOrEmpty(s))
				return "";
			return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
				.Replace("\r", "").Replace("\n", "\\n").Replace("\t", "\\t");
		}

		// Phase 2 — external command-file watcher. When Test.ScreenshotCmdFile is
		// set, PollCommands() is called once per LogicTick. It checks the file's
		// mtime; if it changed since last poll, reads the file, parses one
		// command per line, executes each, then deletes the file. This lets a
		// CLI (tools/autotest/screenshot.sh) drive screenshots in any game
		// state — main menu, server lobby, in-match — without needing a Lua
		// scenario or a Launch.Map.
		//
		// Command grammar (minimal):
		//   screenshot <label>          — capture a screenshot tagged <label>
		//
		// Anything else is ignored (logged to debug). Phase 3 would extend to
		// click/key/text commands for full UI automation.

		static DateTime lastCmdFileMtime;

		public static void PollCommands()
		{
			if (!TestMode.IsActive || string.IsNullOrEmpty(TestMode.ScreenshotCmdFile))
				return;

			if (!File.Exists(TestMode.ScreenshotCmdFile))
				return;

			DateTime mtime;
			try { mtime = File.GetLastWriteTimeUtc(TestMode.ScreenshotCmdFile); }
			catch { return; }

			if (mtime == lastCmdFileMtime)
				return;

			lastCmdFileMtime = mtime;

			string[] lines;
			try { lines = File.ReadAllLines(TestMode.ScreenshotCmdFile); }
			catch (Exception e)
			{
				Log.Write("debug", $"[TestMode] cmd file read failed: {e.Message}");
				return;
			}

			// Delete the file so the next write triggers a fresh mtime change.
			// We've already captured the content; deletion makes mtime semantics
			// trivial (any future file is new).
			try { File.Delete(TestMode.ScreenshotCmdFile); }
			catch { /* ignore — rewrite from the agent side will overwrite */ }

			foreach (var raw in lines)
			{
				var line = raw?.Trim();
				if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
					continue;

				// "screenshot <label>"
				if (line.StartsWith("screenshot ", StringComparison.OrdinalIgnoreCase))
				{
					var label = line.Substring("screenshot ".Length).Trim();
					if (string.IsNullOrEmpty(label))
						label = "manual";

					var path = Capture(label, "phase 2 external trigger", -1);
					SaveManifest();
					Log.Write("debug", $"[TestMode] external screenshot: {label} → {path}");
				}
				else
				{
					Log.Write("debug", $"[TestMode] unknown cmd: {line}");
				}
			}
		}

		// True when every captured screenshot's path exists on disk. Used by
		// TestGlobal.Pass/Fail/Skip to defer Game.Exit until pending ThreadPool
		// PNG-writes have completed — otherwise process termination kills the
		// background workers and the files never appear.
		public static bool AllCapturesFlushed()
		{
			if (captured.Count == 0)
				return true;

			foreach (var c in captured)
				if (!File.Exists(c.Path))
					return false;

			return true;
		}

		// Lowercase, alnum + dash + underscore only. Spaces become dashes. Anything
		// else dropped. Keeps filenames safe across Windows/macOS/Linux and
		// predictable for the agent that later reads the verdict JSON.
		public static string SanitizeLabel(string label)
		{
			if (string.IsNullOrEmpty(label))
				return "unlabeled";

			var sb = new StringBuilder(label.Length);
			foreach (var ch in label.ToLowerInvariant())
			{
				if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '-' || ch == '_')
					sb.Append(ch);
				else if (ch == ' ')
					sb.Append('-');
			}
			return sb.Length > 0 ? sb.ToString() : "unlabeled";
		}
	}
}
