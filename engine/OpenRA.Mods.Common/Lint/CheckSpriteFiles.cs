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
using System.Collections.Generic;
using OpenRA.Graphics;

namespace OpenRA.Mods.Common.Lint
{
	/// <summary>Reports sequence filenames that no mounted package provides.</summary>
	/// <remarks>
	/// Nothing else in the suite checks that a referenced ASSET exists - CheckSequences validates that a
	/// sequence NAME is defined, not that its file is on disk. That gap is what makes a dangling filename
	/// dangerous rather than merely untidy: WW3MOD replaced upstream's throw-on-missing-file with a blank
	/// frame (SpriteCache.ResolveSprites) and a clamp (DefaultSpriteSequence), so a missing sprite no
	/// longer fails at load. It degrades to nothing, silently, on the machines that lack it - which never
	/// includes the developer's. Without this pass the only place the mismatch surfaces is a stranger's
	/// install.
	/// </remarks>
	sealed class CheckSpriteFiles : ILintSequencesPass
	{
		// The runner invokes sequence passes once per tileset AND again for every map, all sharing the
		// mod-level sequence definitions, so one dangling filename would be reported ~13 times. Inflated
		// counts are exactly what taught this project to read lint totals as noise rather than signal, so
		// report each (location, file) once per process.
		static readonly HashSet<string> Reported = new();

		void ILintSequencesPass.Run(
			Action<string> emitError, Action<string> emitWarning, ModData modData, Ruleset rules, SequenceSet sequences)
		{
			foreach (var (filename, location) in sequences.SpriteCache.UnreadableReservedFiles)
			{
				// An error, not a warning: a sequence that names a file nobody ships is the one input that
				// can still reach the degraded render path, and every instance is a one-line YAML fix.
				if (Reported.Add($"{location}|{filename}"))
					emitError($"{location}: sprite file `{filename}` not found.");
			}
		}
	}
}
