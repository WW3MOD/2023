#region Copyright & License Information
/*
 * WW3MOD: regenerate the shadows.bin precomputed line-of-sight cache for a
 * map without going through the in-game editor. Narrower than --refresh-map
 * (which also rewrites map.yaml and map.png) — touches only shadows.bin.
 * Use after a fix to the shadow compute pipeline that invalidates cached
 * data baked into existing maps.
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

		[Desc("MAP", "Rebuild and save the shadows.bin LOS cache for a map. Does not touch map.yaml, map.png, or map.bin.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			// HACK: The engine code assumes Game.ModData is set.
			// HACK: We know that maps can only be oramap or folders, which are ReadWrite.
			var modData = Game.ModData = utility.ModData;
			using (var package = new Folder(Platform.EngineDir).OpenPackage(args[1], modData.ModFiles))
			{
				var map = new Map(modData, package);
				((IReadWritePackage)package).Update("shadows.bin", map.SaveShadowsBinaryData());
				Console.WriteLine($"Wrote shadows.bin for {args[1]}");
			}
		}
	}
}
