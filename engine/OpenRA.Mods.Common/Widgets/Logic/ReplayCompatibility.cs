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
		UnknownVersion,
		UnknownMod,
		UnavailableMod,
		IncompatibleVersion,

		/// <summary>Recorded before replays carried a build stamp, so it cannot be checked at all.</summary>
		UnverifiableBuild,

		/// <summary>Recorded by a build whose engine or rules differ from the one now running.</summary>
		IncompatibleBuild,

		UnavailableMap
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
	/// this repo reports the same string and the comparison is equal by construction — a replay from
	/// any earlier build passed straight through and then diverged silently during playback, which
	/// is worse than either refusing it or warning about it. The build fingerprint is what actually
	/// dates a replay; the version comparison is kept because a packaged release DOES carry a real
	/// version (packaging/*/buildpackage.sh stamps the staged copy), so it still catches something.
	/// </remarks>
	public static class ReplayCompatibilityCheck
	{
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

			// An empty current fingerprint means the caller could not establish the running build's
			// own identity — it happens when the replay belongs to a different mod than the one
			// loaded. Skipped rather than refused: blaming the replay for our own missing half would
			// name a culprit that is not the reason.
			if (!string.IsNullOrEmpty(currentFingerprint))
			{
				// Refused rather than waved through, matching how an absent version is already
				// treated above: a replay whose build cannot be established is exactly the one that
				// diverges without saying anything. Everything recorded from here on carries a stamp.
				if (string.IsNullOrEmpty(replayFingerprint))
					return ReplayCompatibility.UnverifiableBuild;

				if (!BuildFingerprint.ReplaySegmentsMatch(currentFingerprint, replayFingerprint))
					return ReplayCompatibility.IncompatibleBuild;
			}

			if (!mapAvailable)
				return ReplayCompatibility.UnavailableMap;

			return ReplayCompatibility.Compatible;
		}
	}
}
