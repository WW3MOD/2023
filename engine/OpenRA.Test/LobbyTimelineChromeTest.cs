#region Copyright & License Information
/*
 * THE TIMELINE'S RESERVED SPACE, read out of the SHIPPED CHROME rather than asserted as numbers.
 *
 * The timeline is a sibling of LOBBY_OPTIONS inside COMMON_OPTIONS_PANEL, not a child of it --
 * LobbyOptionsLogic.RebuildOptions calls RemoveChildren() on that container, so anything parented
 * there is destroyed on the first map change. Being a sibling means the option grid below it has to
 * be pushed down BY HAND, in yaml, by exactly the widget's height. Nothing enforces that at
 * runtime: get it wrong and the bar either overlaps the first section header or leaves a dead gap,
 * and both look like a drawing bug rather than a configuration one.
 *
 * That is the same reason CameoCaptionBandTest exists and reads chrome instead of restating
 * arithmetic: the bug class here is a configuration bug, and only reading the configuration catches
 * it.
 *
 * WHAT THIS DOES NOT COVER, stated plainly: it does not catch the blank-bar deadlock this feature
 * actually shipped (see LobbyTimelineMathTest for that one), and it cannot check that the widget
 * draws anything. It checks one relationship between three numbers in one file.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace OpenRA.Test
{
	[TestFixture]
	public class LobbyTimelineChromeTest
	{
		const string TimelineNode = "Timeline@MATCH_TIMELINE";
		const string OptionsNode = "Container@LOBBY_OPTIONS";

		// The engine's chrome lives under engine/mods/common, not mods/ww3mod, so this walks for a
		// different anchor than CameoCaptionBandTest does. Both forms are tried because the test
		// binary's depth below the repo root is a build-layout detail.
		static string FindChrome(string file)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null)
			{
				foreach (var prefix in new[] { new[] { "mods", "common", "chrome" }, new[] { "engine", "mods", "common", "chrome" } })
				{
					var candidate = Path.Combine(new[] { dir.FullName }.Concat(prefix).Append(file).ToArray());
					if (File.Exists(candidate))
						return candidate;
				}

				dir = dir.Parent;
			}

			throw new FileNotFoundException("could not locate mods/common/chrome/" + file);
		}

		static MiniYamlNode Find(IEnumerable<MiniYamlNode> nodes, string key)
		{
			foreach (var node in nodes)
			{
				if (node.Key == key)
					return node;

				var children = node.Value.NodeWithKeyOrDefault("Children");
				if (children == null)
					continue;

				var found = Find(children.Value.Nodes, key);
				if (found != null)
					return found;
			}

			return null;
		}

		static string Field(MiniYamlNode node, string key)
		{
			var field = node.Value.NodeWithKeyOrDefault(key);
			Assert.That(field, Is.Not.Null, $"{node.Key} has no {key}");
			return field.Value.Value.Trim();
		}

		// Both Y values are engine expressions of the form "<shared prefix> + <constant>". Only the
		// trailing constant is compared; the prefix must match exactly, because two rows anchored to
		// DIFFERENT expressions have no fixed spacing between them at all and the delta below would
		// be meaningless.
		static (string Prefix, int Offset) SplitOffset(string expression, string what)
		{
			var plus = expression.LastIndexOf('+');
			Assert.That(plus, Is.GreaterThan(0), $"{what} is not of the form '<expression> + <constant>': {expression}");

			var prefix = expression[..plus].Trim();
			var tail = expression[(plus + 1)..].Trim();
			Assert.That(int.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset), Is.True,
				$"{what} does not end in an integer constant: {expression}");

			return (prefix, offset);
		}

		[Test]
		public void TheOptionGridIsPushedDownByExactlyTheTimelineHeight()
		{
			var chrome = MiniYaml.FromFile(FindChrome("lobby-players.yaml"));

			var timeline = Find(chrome, TimelineNode);
			Assert.That(timeline, Is.Not.Null, $"{TimelineNode} is missing from lobby-players.yaml");

			var options = Find(chrome, OptionsNode);
			Assert.That(options, Is.Not.Null, $"{OptionsNode} is missing from lobby-players.yaml");

			var height = int.Parse(Field(timeline, "Height"), CultureInfo.InvariantCulture);
			var (timelinePrefix, timelineOffset) = SplitOffset(Field(timeline, "Y"), $"{TimelineNode} Y");
			var (optionsPrefix, optionsOffset) = SplitOffset(Field(options, "Y"), $"{OptionsNode} Y");

			Assert.That(optionsPrefix, Is.EqualTo(timelinePrefix),
				"the timeline and the option grid are anchored to different expressions, so the gap between them is not fixed.");

			Assert.That(optionsOffset - timelineOffset, Is.EqualTo(height),
				$"the option grid sits {optionsOffset - timelineOffset}px below the timeline, which is {height}px tall. "
				+ "If the gap is larger the panel shows dead space; if smaller the bar is drawn over by the first section header. "
				+ "Change both numbers together.");
		}

		// The timeline must NOT be inside LOBBY_OPTIONS: that container is emptied by
		// LobbyOptionsLogic.RebuildOptions on every rebuild, so a timeline parented there would
		// vanish the first time the host changed the map — intermittently, and only after an action,
		// which is the worst shape of bug to find by hand.
		[Test]
		public void TheTimelineIsNotAChildOfTheContainerThatGetsEmptied()
		{
			var chrome = MiniYaml.FromFile(FindChrome("lobby-players.yaml"));
			var options = Find(chrome, OptionsNode);
			Assert.That(options, Is.Not.Null);

			var children = options.Value.NodeWithKeyOrDefault("Children");
			var nested = children == null ? null : Find(children.Value.Nodes, TimelineNode);

			Assert.That(nested, Is.Null,
				$"{TimelineNode} is inside {OptionsNode}, which LobbyOptionsLogic.RebuildOptions clears with RemoveChildren(). "
				+ "It must be a sibling.");
		}
	}
}
