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

using OpenRA.Network;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public enum ReplayCompatibility
	{
		Compatible,

		// Blockers. The replay cannot be played at all, so these refuse.
		UnknownVersion,
		UnknownMod,
		UnavailableMod,
		IncompatibleVersion,
		UnavailableMap,

		/// <summary>Recorded before replays carried a build stamp, so it cannot be checked. Advisory.</summary>
		UnverifiableBuild,

		/// <summary>Recorded by a build whose engine or rules differ from the one now running. Advisory.</summary>
		IncompatibleBuild
	}

	/// <summary>
	/// Decides whether a recorded replay can be played back by the build now running.
	/// </summary>
	/// <remarks>
	/// Split out of <see cref="ReplayUtils"/> so the decision can be tested without a game: the
	/// wrapper there reaches into <see cref="Game.Mods"/> and puts a dialog on screen, and neither
	/// exists under NUnit.
	/// <para/>
	/// The version comparison alone is not a check. <c>Metadata.Version</c> comes from a literal in
	/// mod.yaml that only the manual <c>make version</c> target rewrites, so every build made from
	/// this repo reports the same string and the comparison is equal by construction. The build
	/// fingerprint is what actually dates a replay; the version comparison is kept because a
	/// packaged release DOES carry a real version (packaging/*/buildpackage.sh stamps the staged
	/// copy), so it still catches something.
	/// <para/>
	/// The build result is ADVISORY, and is returned last so a real blocker is never masked by it.
	/// A replay that diverges does not do so silently: the recorded sync hashes are replayed back
	/// through <see cref="OrderManager.ReceiveSync"/> alongside the locally recomputed ones
	/// (ReplayConnection.cs:101-109 and :117-118), and a mismatch raises OutOfSync. So a build
	/// difference is a reason to WARN, not to refuse - refusing would block playback that the engine
	/// is already equipped to catch and report, and the same argument was settled the same way for
	/// the join path (WORKSPACE/closeout/54ab3880.md, "a guard that blocks play is worse than the
	/// bug it diagnoses").
	/// </remarks>
	public static class ReplayCompatibilityCheck
	{
		/// <summary>
		/// Whether a result is a warning the player may override rather than a blocker. Blockers
		/// mean the replay cannot run at all; advisory results mean it will run and may desync.
		/// </summary>
		public static bool IsAdvisory(ReplayCompatibility result)
		{
			return result == ReplayCompatibility.UnverifiableBuild || result == ReplayCompatibility.IncompatibleBuild;
		}

		/// <summary>
		/// Resolves compatibility from already-extracted values. <paramref name="installedVersion"/>
		/// and <paramref name="currentFingerprint"/> describe the running build.
		/// </summary>
		public static ReplayCompatibility Resolve(
			string replayMod, string replayVersion, string replayFingerprint,
			bool modInstalled, string installedVersion, string currentFingerprint,
			bool mapAvailable)
		{
			if (replayVersion == null)
				return ReplayCompatibility.UnknownVersion;

			if (replayMod == null)
				return ReplayCompatibility.UnknownMod;

			if (!modInstalled)
				return ReplayCompatibility.UnavailableMod;

			if (installedVersion != replayVersion)
				return ReplayCompatibility.IncompatibleVersion;

			// Before the build checks, deliberately. A missing map stops playback outright, so
			// reporting a build warning instead would name the smaller problem and hide the one the
			// player has to act on.
			if (!mapAvailable)
				return ReplayCompatibility.UnavailableMap;

			// An empty current fingerprint means the caller could not establish the running build's
			// own identity, which happens when the replay belongs to a different mod than the one
			// loaded. Defence in depth only: such a replay is already stopped by the map check above,
			// because a foreign mod's map is not in this mod's cache.
			if (string.IsNullOrEmpty(currentFingerprint))
				return ReplayCompatibility.Compatible;

			if (string.IsNullOrEmpty(replayFingerprint))
				return ReplayCompatibility.UnverifiableBuild;

			if (!BuildFingerprint.ReplaySegmentsMatch(currentFingerprint, replayFingerprint))
				return ReplayCompatibility.IncompatibleBuild;

			return ReplayCompatibility.Compatible;
		}
	}
}
