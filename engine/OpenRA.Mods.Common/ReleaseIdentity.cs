#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using OpenRA.Network;

namespace OpenRA.Mods.Common
{
	// WW3MOD: the four lines behind the main menu's "v" button. This is the first and often the only
	// thing a stranger reads about what build they are running, and until 2026-09-21 three of the four
	// were false on a packaged release:
	//
	//   1. The version was the literal "WW3MOD - Pre-Alpha" on a tree that had shipped v0.1.0/1/2.
	//   2. "Built: " was DateTime.Now evaluated when the menu opened, so it rendered the PLAYER'S
	//      current date, every day, forever. Not stale -- false, and confidently so.
	//   3. "Fork: " rendered mod.yaml's Version:, which is the OpenRA release this forked from in a
	//      source tree but which packaging OVERWRITES with the WW3MOD git tag
	//      (mod.config PACKAGING_OVERWRITE_MOD_VERSION="True" -> packaging/functions.sh
	//      set_mod_version), so on every install the line describing the fork point read "v0.1.2".
	//
	// Kept pure and free of Game/widget state so OpenRA.Test can pin it: a panel is only opened by a
	// human clicking a button, so nothing else in the build would ever notice these going wrong again.
	public static class ReleaseIdentity
	{
		public const string ModName = "WW3MOD";

		// What the version line says when packaging did not stamp a tag. Deliberately not a marketing
		// word: WORKSPACE/AWAITING-USER.md section 1 asks the user to pick one and is unanswered, and a
		// dev tree is not a release to name anyway. The packaged line shows the tag itself, which is a
		// fact rather than a choice, so the unanswered question does not block either line.
		public const string DevMarker = "dev build";

		public const string UnknownBuildDate = "unknown";

		// Git short-SHA convention. BuildRevision is stamped at --abbrev=10 for handshake identity
		// (Directory.Build.targets StampBuildRevision); eight is enough to paste into git log and keeps
		// the Bold line inside the 275px label that mainmenu.yaml gives it.
		const int RevisionChars = 8;

		/// <summary>The bold first line: the packaged release tag, or a development marker.</summary>
		// The discriminator is ModVersion.TryParse rather than a compare against engine/VERSION, because
		// the question is semantic: "is this a release version number?" A stamped tag (v0.1.2) parses;
		// the checked-in fork marker (release-20230225) does not, and neither does anything else a
		// source tree can be carrying. ModVersion's own comments already assign that reading to an
		// unparseable local version, so the two uses cannot drift apart.
		public static string VersionLabel(string modVersion, string buildRevision)
		{
			if (ModVersion.TryParse(modVersion, out _))
				return ModName + " " + modVersion.Trim();

			var revision = ShortRevision(buildRevision);
			return revision == null
				? ModName + " — " + DevMarker
				: ModName + " — " + DevMarker + " (" + revision + ")";
		}

		/// <summary>The engine release this mod forked from. Never the mod's own version.</summary>
		// Sourced from Game.EngineVersion (engine/VERSION), which packaging rewrites with mod.config's
		// ENGINE_VERSION -- the same value it already holds in a source tree. So this line reads the
		// same on a dev tree and on an install, which is the property the old one lacked.
		public static string ForkLabel(string engineVersion)
		{
			return string.IsNullOrWhiteSpace(engineVersion)
				? "Fork: OpenRA"
				: "Fork: OpenRA " + engineVersion.Trim();
		}

		/// <summary>When this build was produced -- not when the panel was opened.</summary>
		public static string BuildLabel(DateTime? buildTime)
		{
			return "Built: " + (buildTime == null
				? UnknownBuildDate
				: buildTime.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
		}

		/// <summary>Last-write time of a built assembly, or null when it cannot be read.</summary>
		// Why the file timestamp and not an [AssemblyMetadata("BuildDate", ...)] stamp next to
		// BuildRevision: that attribute is a Compile input, so a value that moves every build would
		// recompile OpenRA.Game on EVERY build. Directory.Build.targets' own comment records why that
		// matters here -- it measured 6.3s vs 3.1s and it breaks the Windows edit-yaml-while-playing
		// loop, because the build fails fast on DLLs the running game holds. A date is not worth that.
		//
		// The file time is honest in both trees and costs nothing: a Release/Debug build rewrites the
		// assembly (on macOS/Linux Directory.Build.targets even unlinks it first), and packaging
		// publishes a fresh one into the install. It is not spoof-proof -- copying a tree with a tool
		// that does not preserve mtimes moves it -- but it can only ever be wrong by the age of a file
		// copy, where DateTime.Now was wrong by the entire age of the release.
		public static DateTime? ResolveBuildTime(Assembly assembly)
		{
			try
			{
				var location = assembly?.Location;
				if (string.IsNullOrEmpty(location) || !File.Exists(location))
					return null;

				return File.GetLastWriteTime(location);
			}
			catch (IOException)
			{
				return null;
			}
			catch (UnauthorizedAccessException)
			{
				return null;
			}
		}

		/// <summary>The assembly whose build time and revision describe this engine build.</summary>
		// OpenRA.Game is the one carrying the BuildRevision attribute, and StampBuildRevision makes the
		// revision a Compile input of that project -- so any commit touching engine/ or mods/ recompiles
		// it. Reading its timestamp keeps "Built:" and the revision describing the same compile.
		public static Assembly StampedAssembly => typeof(BuildFingerprint).Assembly;

		// "abc123def0+1a2b3c4d" -> "abc123de+". The trailing marker is BuildRevision's own notation for
		// a tree that had uncommitted engine changes at build time; dropping it would report a modified
		// build as a clean commit, which is the one thing a revision must not do.
		static string ShortRevision(string buildRevision)
		{
			if (string.IsNullOrWhiteSpace(buildRevision))
				return null;

			var revision = buildRevision.Trim();
			if (string.Equals(revision, BuildFingerprint.UnknownRevision, StringComparison.Ordinal))
				return null;

			var dirty = revision.IndexOf('+');
			var commit = dirty < 0 ? revision : revision[..dirty];
			if (commit.Length == 0)
				return null;

			if (commit.Length > RevisionChars)
				commit = commit[..RevisionChars];

			return dirty < 0 ? commit : commit + "+";
		}
	}
}
