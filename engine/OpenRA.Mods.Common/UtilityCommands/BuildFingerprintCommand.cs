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
using System.Diagnostics;
using System.Linq;
using OpenRA.Network;

namespace OpenRA.Mods.Common.UtilityCommands
{
	/// <summary>
	/// Prints the build fingerprint the join handshake compares, so two players can check
	/// whether they match without starting a game and waiting for a desync.
	/// </summary>
	sealed class BuildFingerprintCommand : IUtilityCommand
	{
		string IUtilityCommand.Name => "--build-fingerprint";

		bool IUtilityCommand.ValidateArguments(string[] args) { return true; }

		[Desc("Print the build fingerprint used to reject mismatched clients at join time.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			var modData = Game.ModData = utility.ModData;

			var stopwatch = Stopwatch.StartNew();
			var fingerprint = BuildFingerprint.ForMod(modData);
			stopwatch.Stop();

			Console.WriteLine(fingerprint);
			Console.WriteLine($"  engine revision: {BuildFingerprint.EngineRevision}");
			Console.WriteLine($"  mod rules:       {modData.Manifest.Id} rules/weapons/sequences/tilesets + mod.yaml");
			Console.WriteLine($"  mounted content: {modData.ModFiles.MountedPackages.Count()} packages");
			Console.WriteLine($"  computed in:     {stopwatch.ElapsedMilliseconds} ms (once per session, on first use)");
		}
	}
}
