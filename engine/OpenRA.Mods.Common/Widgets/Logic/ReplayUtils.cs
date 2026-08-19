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

		static readonly Action DoNothing = () => { };

		public static bool PromptConfirmReplayCompatibility(ReplayMetadata replayMeta, ModData modData, Action onCancel = null)
		{
			onCancel ??= DoNothing;

			if (replayMeta == null)
				return IncompatibleReplayDialog(modData, onCancel, IncompatibleReplayPrompt);

			var info = replayMeta.GameInfo;
			var mod = info.Mod;
			var modInstalled = mod != null && Game.Mods.ContainsKey(mod);

			// Only meaningful against the mod that is actually loaded. A replay from a DIFFERENT
			// installed mod reaches here before BlankLoadScreen.cs:94-95 switches to it, and
			// fingerprinting the mod we happen to be running would compare two unrelated builds and
			// refuse it with a reason that names the wrong thing.
			var currentFingerprint = mod == modData.Manifest.Id ? BuildFingerprint.ForMod(modData) : null;

			// MapPreview indexes MapCache by uid, and a null uid would throw where the checks above
			// used to return first. Truncated metadata should still reach a dialog, not an exception.
			var mapAvailable = info.MapUid != null && info.MapPreview.Status == MapStatus.Available;

			var result = ReplayCompatibilityCheck.Resolve(
				mod, info.Version, info.BuildFingerprint,
				modInstalled, modInstalled ? Game.Mods[mod].Metadata.Version : null, currentFingerprint,
				mapAvailable);

			switch (result)
			{
				case ReplayCompatibility.Compatible:
					return true;

				case ReplayCompatibility.UnknownVersion:
					return IncompatibleReplayDialog(modData, onCancel, UnknownVersion);

				case ReplayCompatibility.UnknownMod:
					return IncompatibleReplayDialog(modData, onCancel, UnknownMod);

				case ReplayCompatibility.UnavailableMod:
					return IncompatibleReplayDialog(modData, onCancel, UnvailableMod, "mod", mod);

				case ReplayCompatibility.IncompatibleVersion:
					return IncompatibleReplayDialog(modData, onCancel, IncompatibleVersion, "version", info.Version);

				case ReplayCompatibility.UnverifiableBuild:
					return IncompatibleReplayDialog(modData, onCancel, UnverifiableBuild);

				case ReplayCompatibility.IncompatibleBuild:
					return IncompatibleReplayDialog(modData, onCancel, IncompatibleBuild,
						"difference", BuildFingerprint.DescribeReplayDifference(currentFingerprint, info.BuildFingerprint));

				case ReplayCompatibility.UnavailableMap:
					return IncompatibleReplayDialog(modData, onCancel, UnvailableMap, "map", info.MapUid);

				default:
					return IncompatibleReplayDialog(modData, onCancel, IncompatibleReplayPrompt);
			}
		}

		static bool IncompatibleReplayDialog(ModData modData, Action onCancel, string text, params object[] args)
		{
			ConfirmationDialogs.ButtonPrompt(
				modData, IncompatibleReplayTitle, text, textArguments: args, onCancel: onCancel, cancelText: IncompatibleReplayAccept);
			return false;
		}
	}
}
