#region Copyright & License Information
/*
 * WW3MOD: pre-warm the precomputed line-of-sight cache for a map without
 * going through the in-game editor, so the first real load does not pay
 * generation. Narrower than --refresh-map (which rewrites map.yaml and
 * map.png) — this writes nothing into the map package at all.
 *
 * It no longer writes shadows.bin into the package. That file could not be
 * validated on read, so a stale copy silently overrode a correctly-keyed
 * cache; the support-dir cache is now the only source. Note that the cache
 * invalidates itself on a map, rules or ShadowCache.AlgoVersion change, so
 * this command is an optimisation and never a correctness requirement.
 */
#endregion

using System;
using OpenRA.FileSystem;

namespace OpenRA.Mods.Common.UtilityCommands
{
	sealed class RegenShadowsCommand : IUtilityCommand
	{
		string IUtilityCommand.Name => "--regen-shadows";

		bool IUtilityCommand.ValidateArguments(string[] args)
		{
			return args.Length >= 2;
		}

		[Desc("MAP", "Pre-warm the LOS cache for a map. Writes nothing into the map package.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			// HACK: The engine code assumes Game.ModData is set.
			var modData = Game.ModData = utility.ModData;
			using (var package = new Folder(Platform.EngineDir).OpenPackage(args[1], modData.ModFiles))
			{
				// Constructing the map is the whole job: it loads the cache or generates and stores
				// it. Regenerating here as well would only duplicate that work.
				var map = new Map(modData, package);
				Console.WriteLine($"Shadow cache ready for {args[1]} (map {map.Uid})");
			}
		}
	}
}
