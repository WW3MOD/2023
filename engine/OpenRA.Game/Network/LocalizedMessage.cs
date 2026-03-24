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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpenRA.Network
{
	public class LocalizedMessage
	{
		public const int ProtocolVersion = 1;

		public readonly string Key = string.Empty;

		[FieldLoader.LoadUsing(nameof(LoadArguments))]
		public readonly FluentArgument[] Arguments = Array.Empty<FluentArgument>();

		public string TranslatedText { get; }

		static object LoadArguments(MiniYaml yaml)
		{
			var arguments = new List<FluentArgument>();
			var argumentsNode = yaml.Nodes.FirstOrDefault(n => n.Key == "Arguments");
			if (argumentsNode != null)
			{
				var regex = new Regex(@"Argument@\d+");
				foreach (var argument in argumentsNode.Value.Nodes)
					if (regex.IsMatch(argument.Key))
						arguments.Add(FieldLoader.Load<FluentArgument>(argument.Value));
			}

			return arguments.ToArray();
		}

		public LocalizedMessage(ModData modData, MiniYaml yaml)
		{
			// Let the FieldLoader do the dirty work of loading the public fields.
			FieldLoader.Load(this, yaml);

			var args = new List<object>();
			foreach (var argument in Arguments)
			{
				args.Add(argument.Key);
				if (argument.Type == FluentArgument.FluentArgumentType.Number)
				{
					if (!double.TryParse(argument.Value, out var number))
						Log.Write("debug", $"Failed to parse {argument.Value}");

					args.Add(number);
				}
				else
					args.Add(argument.Value);
			}

			TranslatedText = FluentProvider.GetMessage(Key, args.ToArray());
		}

		public static string Serialize(string key, Dictionary<string, object> arguments = null)
		{
			var root = new List<MiniYamlNode>
			{
				new MiniYamlNode("Protocol", ProtocolVersion.ToString()),
				new MiniYamlNode("Key", key)
			};

			if (arguments != null)
			{
				var argumentsNode = new MiniYaml("");
				var i = 0;
				foreach (var argument in arguments.Select(a => new FluentArgument(a.Key, a.Value)))
					argumentsNode.Nodes.Add(new MiniYamlNode("Argument@" + i++, FieldSaver.Save(argument)));

				root.Add(new MiniYamlNode("Arguments", argumentsNode));
			}

			return new MiniYaml("", root)
				.ToLines("LocalizedMessage")
				.JoinWith("\n");
		}
	}
}
