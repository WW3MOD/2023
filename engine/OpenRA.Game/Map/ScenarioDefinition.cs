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

namespace OpenRA
{
	public class ScenarioDefinition
	{
		public readonly string Name;
		public readonly List<MiniYamlNode> Players;
		public readonly List<MiniYamlNode> Actors;
		public readonly List<MiniYamlNode> Rules;

		public ScenarioDefinition(string name, MiniYaml yaml)
		{
			Name = name;
			Players = new List<MiniYamlNode>();
			Actors = new List<MiniYamlNode>();
			Rules = new List<MiniYamlNode>();

			foreach (var node in yaml.Nodes)
			{
				switch (node.Key)
				{
					case "Players":
						Players = node.Value.Nodes.ToList();
						break;
					case "Actors":
						Actors = node.Value.Nodes.ToList();
						break;
					case "Rules":
						Rules = node.Value.Nodes.ToList();
						break;
				}
			}
		}
	}
}
