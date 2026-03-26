#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Adds a lobby dropdown to select a map scenario, if the map has scenarios.yaml.")]
	[TraitLocation(SystemActors.World)]
	public class ScenarioLobbyDropdownInfo : TraitInfo, ILobbyOptions
	{
		[Desc("Display order for the scenario dropdown in the lobby.")]
		public readonly int DisplayOrder = -100;

		IEnumerable<LobbyOption> ILobbyOptions.LobbyOptions(MapPreview map)
		{
			var scenarioNames = map.ScenarioNames;
			if (scenarioNames == null || scenarioNames.Length == 0)
				yield break;

			var values = new Dictionary<string, string> { { "none", "None" } };
			foreach (var name in scenarioNames)
				values[name] = name;

			yield return new LobbyOption("scenario", "Scenario", "Select a scripted scenario for this map", true, DisplayOrder,
				values, "none", false);
		}

		public override object Create(ActorInitializer init) { return new ScenarioLobbyDropdown(); }
	}

	public class ScenarioLobbyDropdown { }
}
