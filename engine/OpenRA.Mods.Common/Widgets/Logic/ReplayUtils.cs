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
using OpenRA.FileFormats;
using OpenRA.Network;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public static class ReplayUtils
	{
		[FluentReference]
		const string IncompatibleReplayTitle = "dialog-incompatible-replay.title";

		[FluentReference]
		const string IncompatibleReplayPrompt = "dialog-incompatible-replay.prompt";

		[FluentReference]
		const string IncompatibleReplayAccept = "dialog-incompatible-replay.confirm";

		[FluentReference]
		const string UnknownVersion = "dialog-incompatible-replay.prompt-unknown-version";

		[FluentReference]
		const string UnknownMod = "dialog-incompatible-replay.prompt-unknown-mod";

		[FluentReference("mod")]
		const string UnvailableMod = "dialog-incompatible-replay.prompt-unavailable-mod";

		[FluentReference("version")]
		const string IncompatibleVersion = "dialog-incompatible-replay.prompt-incompatible-version";

		[FluentReference("map")]
		const string UnvailableMap = "dialog-incompatible-replay.prompt-unavailable-map";

		[FluentReference("difference")]
		const string IncompatibleBuild = "dialog-incompatible-replay.prompt-incompatible-build";

		[FluentReference]
		const string UnverifiableBuild = "dialog-incompatible-replay.prompt-unverifiable-build";

		[FluentReference]
		const string DesyncRiskTitle = "dialog-replay-desync-risk.title";

		[FluentReference]
		const string WatchAnyway = "dialog-replay-desync-risk.watch-anyway";

		[FluentReference]
		const string DoNotWatch = "dialog-replay-desync-risk.cancel";

		static readonly Action DoNothing = () => { };

		/// <summary>
		/// Runs <paramref name="onWatch"/> if the replay can be played, and otherwise puts a dialog
		/// on screen. A build mismatch is a WARNING: the dialog offers to watch anyway, and
		/// <paramref name="onWatch"/> runs if the player takes it. Everything else is a blocker and
		/// only offers to dismiss.
		/// </summary>
		/// <remarks>
		/// Callback-shaped rather than returning a bool because the watch-anyway path is decided
		/// after this returns - ButtonPrompt opens a window and does not block.
		/// </remarks>
		public static void PromptReplayCompatibility(ReplayMetadata replayMeta, ModData modData, Action onWatch, Action onCancel = null)
		{
			onCancel ??= DoNothing;

			if (replayMeta == null)
			{
				IncompatibleReplayDialog(modData, onCancel, IncompatibleReplayPrompt);
				return;
			}

			var info = replayMeta.GameInfo;
			var mod = info.Mod;
			var modInstalled = mod != null && Game.Mods.ContainsKey(mod);

			// Only meaningful against the mod that is actually loaded; we cannot compute another
			// mod's fingerprint from here. Defence in depth rather than the thing that saves the
			// case - a foreign-mod replay is already stopped by the map check, since its map is not
			// in this mod's cache.
			var currentFingerprint = mod == modData.Manifest.Id ? BuildFingerprint.ForMod(modData) : null;

			// MapPreview indexes MapCache by uid, and a null uid would throw where the checks above
			// used to return first. Truncated metadata should still reach a dialog, not an exception.
			var mapAvailable = info.MapUid != null && info.MapPreview.Status == MapStatus.Available;

			var result = ReplayCompatibilityCheck.Resolve(
				mod, info.Version, info.BuildFingerprint,
				modInstalled, modInstalled ? Game.Mods[mod].Metadata.Version : null, currentFingerprint,
				mapAvailable);

			if (result == ReplayCompatibility.Compatible)
			{
				onWatch();
				return;
			}

			if (ReplayCompatibilityCheck.IsAdvisory(result))
			{
				var text = result == ReplayCompatibility.UnverifiableBuild ? UnverifiableBuild : IncompatibleBuild;
				var args = result == ReplayCompatibility.UnverifiableBuild
					? Array.Empty<object>()
					: new object[] { "difference", BuildFingerprint.DescribeReplayDifference(currentFingerprint, info.BuildFingerprint) };

				ConfirmationDialogs.ButtonPrompt(
					modData, DesyncRiskTitle, text, textArguments: args,
					onConfirm: onWatch, confirmText: WatchAnyway,
					onCancel: onCancel, cancelText: DoNotWatch);
				return;
			}

			switch (result)
			{
				case ReplayCompatibility.UnknownVersion:
					IncompatibleReplayDialog(modData, onCancel, UnknownVersion);
					break;

				case ReplayCompatibility.UnknownMod:
					IncompatibleReplayDialog(modData, onCancel, UnknownMod);
					break;

				case ReplayCompatibility.UnavailableMod:
					IncompatibleReplayDialog(modData, onCancel, UnvailableMod, "mod", mod);
					break;

				case ReplayCompatibility.IncompatibleVersion:
					IncompatibleReplayDialog(modData, onCancel, IncompatibleVersion, "version", info.Version);
					break;

				case ReplayCompatibility.UnavailableMap:
					IncompatibleReplayDialog(modData, onCancel, UnvailableMap, "map", info.MapUid);
					break;

				default:
					IncompatibleReplayDialog(modData, onCancel, IncompatibleReplayPrompt);
					break;
			}
		}

		static void IncompatibleReplayDialog(ModData modData, Action onCancel, string text, params object[] args)
		{
			ConfirmationDialogs.ButtonPrompt(
				modData, IncompatibleReplayTitle, text, textArguments: args, onCancel: onCancel, cancelText: IncompatibleReplayAccept);
		}
	}
}
