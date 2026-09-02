#region Copyright & License Information
/*
 * A held command-bar MODE marks itself with an amber glyph as well as a lighter panel; a MOMENTARY
 * action (Stop, Deploy, Resupply, Scatter, Auto-Enter) keeps its grey glyph and flashes the panel
 * only. Both halves of that live entirely in YAML, and NOTHING else checks either one: no lint rule
 * in OpenRA.Mods.Common/Lint references ChromeProvider or ImageCollection, so `--check-yaml` cannot
 * see a collection that has quietly become a bare alias of its base. Before this fixture the only
 * instrument that could tell the two apart was a human looking at a screenshot.
 *
 * The failure mode being gated is specifically silent. WidgetUtils.GetCachedStatefulImage
 * (WidgetUtils.cs:44-54) resolves the highlighted glyph as
 * TryGetImage(collection + "-highlighted", name) ?? GetImage(collection + "-highlighted", name),
 * and a `-highlighted` collection that only says `Inherits:` still answers the first call — with the
 * BASE rectangle. So the override and the fallback agree, the glyph never changes, and everything
 * looks correctly wired in YAML, in logic and in review. That is what command-icons-highlighted did
 * for every command-bar mode until 2026-09-01.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace OpenRA.Test
{
	[TestFixture]
	public class CommandBarHighlightTest
	{
		const string ModeCollection = "command-mode-icons";
		const string MomentaryCollection = "command-icons";

		static string FindMod(params string[] relative)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var parts = new[] { dir.FullName, "mods", "ww3mod" }.Concat(relative).ToArray();
				var candidate = Path.Combine(parts);
				if (File.Exists(candidate))
					return candidate;
			}

			throw new FileNotFoundException("could not locate mods/ww3mod/" + string.Join("/", relative));
		}

		/// <summary>
		/// Region rectangles of a chrome collection, keyed by image name. Goes through MiniYaml.Merge
		/// rather than FromFile so `Inherits:` is resolved the way ChromeProvider sees it — a collection
		/// that inherits its base really does carry the base's rectangles here, which is the whole
		/// reason the bare-alias bug is invisible to a naive read of the file.
		/// </summary>
		static Dictionary<string, string> Regions(string collection)
		{
			var chrome = MiniYaml.Merge(new[] { MiniYaml.FromFile(FindMod("chrome.yaml")) });
			var node = chrome.FirstOrDefault(n => n.Key == collection)
				?? throw new AssertionException($"chrome.yaml defines no collection `{collection}`");

			var regions = node.Value.Nodes.FirstOrDefault(n => n.Key == "Regions");
			if (regions == null)
				return new Dictionary<string, string>();

			return regions.Value.Nodes.ToDictionary(
				n => n.Key,
				n => string.Join(",", n.Value.Value.Split(',').Select(p => p.Trim())));
		}

		/// <summary>
		/// Every (collection, image) pair drawn by an Image@ICON anywhere in ingame-player.yaml.
		/// Enumerated from the file rather than listed here so a button added later is covered without
		/// anyone remembering to update this fixture.
		/// </summary>
		static List<(string Collection, string Image)> Icons()
		{
			var found = new List<(string, string)>();

			void Walk(IEnumerable<MiniYamlNode> nodes)
			{
				foreach (var n in nodes)
				{
					if (n.Key != null && n.Key.StartsWith("Image@", StringComparison.Ordinal))
					{
						var collection = n.Value.Nodes.FirstOrDefault(c => c.Key == "ImageCollection")?.Value.Value;
						var image = n.Value.Nodes.FirstOrDefault(c => c.Key == "ImageName")?.Value.Value;
						if (!string.IsNullOrEmpty(collection) && !string.IsNullOrEmpty(image))
							found.Add((collection.Trim(), image.Trim()));
					}

					Walk(n.Value.Nodes);
				}
			}

			Walk(MiniYaml.FromFile(FindMod("chrome", "ingame-player.yaml")));
			return found;
		}

		/// <summary>
		/// Guards every other test in this fixture against passing vacuously. Both of the assertions
		/// below are "for each icon drawn from collection X ..." and would hold trivially if the walk
		/// above returned nothing — a renamed widget key or a MiniYaml change would turn this fixture
		/// green while checking nothing at all.
		/// </summary>
		[Test]
		public void TheCommandBarIconWalkFindsBothKindsOfButton()
		{
			var icons = Icons();

			Assert.That(icons.Count(i => i.Collection == ModeCollection), Is.GreaterThanOrEqualTo(5),
				$"expected the held-mode buttons to draw `{ModeCollection}` — if this dropped to zero the " +
				"Image@ICON walk stopped matching and the rest of this fixture is checking nothing");

			Assert.That(icons.Count(i => i.Collection == MomentaryCollection), Is.GreaterThanOrEqualTo(4),
				$"expected the momentary buttons to still draw `{MomentaryCollection}`");
		}

		/// <summary>
		/// The bug this fixture exists for: a held mode whose highlighted glyph is the same rectangle as
		/// its unhighlighted one signals its state with the panel shade alone.
		/// </summary>
		[Test]
		public void EveryHeldModeGlyphChangesWhenHighlighted()
		{
			var normal = Regions(ModeCollection);
			var highlighted = Regions(ModeCollection + "-highlighted");

			foreach (var image in Icons().Where(i => i.Collection == ModeCollection).Select(i => i.Image).Distinct())
			{
				Assert.That(normal.ContainsKey(image), Is.True,
					$"`{ModeCollection}` cannot draw `{image}` at all");
				Assert.That(highlighted.ContainsKey(image), Is.True,
					$"`{ModeCollection}-highlighted` cannot draw `{image}` at all");

				Assert.That(highlighted[image], Is.Not.EqualTo(normal[image]),
					$"held mode `{image}` resolves to the same rectangle ({normal[image]}) highlighted as " +
					$"unhighlighted, so engaging the mode changes the panel but not the glyph. Add an amber " +
					$"recolour and point `{ModeCollection}-highlighted` at it.");
			}
		}

		/// <summary>
		/// The other half of the 2026-09-01 decision, and the reason the modes were given their own
		/// collection instead of filling in command-icons-highlighted: `guard` is drawn by Guard and
		/// Patrol (held modes) AND by Auto-Enter (momentary). Recolouring it inside the shared collection
		/// would make an amber guard glyph mean either "Guard is engaged" or "Auto-Enter just fired" on
		/// two adjacent buttons carrying identical art. command-icons-highlighted is therefore a bare
		/// alias ON PURPOSE, and this asserts it stays one.
		/// </summary>
		[Test]
		public void MomentaryFlashGlyphsAreNotRecoloured()
		{
			var normal = Regions(MomentaryCollection);
			var highlighted = Regions(MomentaryCollection + "-highlighted");

			foreach (var image in Icons().Where(i => i.Collection == MomentaryCollection).Select(i => i.Image).Distinct())
			{
				Assert.That(normal.ContainsKey(image), Is.True,
					$"`{MomentaryCollection}` cannot draw `{image}` at all");

				var effective = highlighted.TryGetValue(image, out var over) ? over : normal[image];
				Assert.That(effective, Is.EqualTo(normal[image]),
					$"momentary action `{image}` now recolours when it flashes. That is a held-mode cue; if " +
					"it is wanted here, move the button to a collection of its own rather than recolouring " +
					$"`{MomentaryCollection}`, which Guard and Patrol also read through their own twin.");
			}
		}
	}
}
