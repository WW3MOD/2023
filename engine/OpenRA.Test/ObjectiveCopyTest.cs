#region Copyright & License Information
/*
 * WW3MOD shipped-objective copy test — reads rules/player.yaml as it ships.
 *
 * ConquestVictoryConditions.Objective is the line the Objectives tab prints, and that tab is the
 * DEFAULT tab of the in-game info dialog (GameInfoLogic adds it first; AutoSelect resolves to it).
 * Until 2026-10-07 the field was never set, so every match told the player "Destroy all
 * opposition!" -- in a game whose enemy Supply Route no weapon can damage
 * (structures.yaml, Targetable NoAutoTarget). Nothing failed: the engine default is a valid string.
 * This fixture is the only thing that notices the field going missing again.
 *
 * It also pins the removal of ResourceStorageWarning ("Silos needed."). That trait fires only when
 * PlayerResources.Resources exceeds a share of ResourceCapacity, and capacity only ever grows through
 * StoresPlayerResources, which no WW3MOD actor carries -- every write to Resources is clamped to
 * capacity (PlayerResources.GiveResources, RemoveStorage, the Lua setter). It was inert Red Alert
 * residue; re-adding it would be harmless today and wrong the day anything gains storage.
 */
#endregion

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class ObjectiveCopyTest
	{
		/// <summary>STATS_CHECKBOX in chrome/ingame-infostats.yaml is one line of Bold in 482 px.</summary>
		const int MaxObjectiveLength = 60;

		static string FindMod(params string[] relative)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(new[] { dir.FullName, "mods", "ww3mod" }.Concat(relative).ToArray());
				if (File.Exists(candidate))
					return candidate;
			}

			throw new FileNotFoundException("could not locate mods/ww3mod/" + string.Join("/", relative));
		}

		static MiniYamlNode PlayerActor()
		{
			var nodes = MiniYaml.FromFile(FindMod("rules", "player.yaml"));
			var player = nodes.FirstOrDefault(n => n.Key == "Player");
			Assert.That(player, Is.Not.Null, "rules/player.yaml no longer defines a top-level `Player:` actor");
			return player;
		}

		[Test]
		public void ShippedObjectiveIsSetAndDescribesContestation()
		{
			var cvc = PlayerActor().Value.Nodes.FirstOrDefault(n => n.Key == "ConquestVictoryConditions");
			Assert.That(cvc, Is.Not.Null, "Player no longer carries ConquestVictoryConditions");

			var objective = cvc.Value.Nodes.FirstOrDefault(n => n.Key == "Objective")?.Value.Value?.Trim();
			Assert.That(objective, Is.Not.Null.And.Not.Empty,
				"ConquestVictoryConditions sets no Objective, so the Objectives tab falls back to the engine " +
				"default and tells the player to destroy a Supply Route nothing can damage.");

			Assert.That(objective, Is.Not.EqualTo(new ConquestVictoryConditionsInfo().Objective),
				"the shipped Objective is the engine default again");

			Assert.That(objective, Does.Contain("Supply Route"),
				"the objective should name the Supply Route -- contesting it is how matches are decided");

			Assert.That(objective.ToLowerInvariant(), Does.Not.Contain("captur"),
				"Supply Route ownership never transfers; contestation is not capture (CLAUDE.md)");

			Assert.That(objective.Length, Is.LessThanOrEqualTo(MaxObjectiveLength),
				$"\"{objective}\" is {objective.Length} characters; the Objectives checkbox is a single line");
		}

		[Test]
		public void SilosNeededWarningIsNotWired()
		{
			Assert.That(PlayerActor().Value.Nodes.Any(n => n.Key.Split('@')[0] == "ResourceStorageWarning"), Is.False,
				"ResourceStorageWarning is back on Player. WW3MOD has no resource storage, so it can never " +
				"fire today; if something now grants storage, \"Silos needed.\" is the wrong thing to say.");
		}
	}
}
