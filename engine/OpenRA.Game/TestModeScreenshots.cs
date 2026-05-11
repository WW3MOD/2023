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
				return path;
			}
			catch (Exception e)
			{
				Log.Write("debug", $"[TestMode] screenshot capture failed: {e.Message}");
				return null;
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
