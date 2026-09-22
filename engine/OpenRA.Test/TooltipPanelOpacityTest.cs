#region Copyright & License Information
/*
 * WW3MOD production-tooltip opacity test — decodes the SHIPPED chrome sheet, because the bug this
 * exists to catch was a single alpha value in the art and nothing in the code could see it.
 *
 * WHAT SHIPPED. `Background@PRODUCTION_TOOLTIP` named `dialog4`, whose centre tile in
 * uibits/dialog.png (518,393 52x52) is black at ALPHA 159 of 255 — so 38% of whatever is behind
 * the panel came through it. The panel is anchored to the LEFT of the production sidebar
 * (TooltipContainerWidget.GetAnchoredPosition, :154, places it at
 * `anchor.X - tooltipWidth - AnchorGap` and clamps only vertically), so on a wide display a tall
 * tooltip lands across the sidebar's icon column and unit portraits are legible THROUGH the panel,
 * behind the tooltip's own text. Filed 2026-08-30 in WORKSPACE/bugs/discovered.md; re-measured and
 * fixed 2026-09-21. THE GEOMETRY WAS NEVER THE BUG — the alpha was.
 *
 * WHY IT DECODES THE PNG. Asserting that the yaml says `tooltip-panel` would only restate the
 * edit. The thing that has to stay true is a property of the ART: the tile the panel tiles across
 * its interior must be fully opaque. Repointing `background:` at another sheet region, or someone
 * editing dialog.png, both break the fix while leaving every string in the yaml correct.
 *
 * WHAT IT DOES NOT CHECK, DELIBERATELY: the 6px frame ring. Its inner pixels are the same
 * alpha-159 black and its outer row is the bevel highlight, and replacing those would flatten the
 * bevel that every other panel in the mod has. The tooltip's TEXT sits in the interior, which is
 * what this pins. A translucent ring around an opaque panel is chrome, not the reported defect.
 *
 * WHAT IT DOES NOT CHECK EITHER: the `ra` and `ts` mods. Both load the same
 * common/chrome/tooltips.yaml and therefore both declare `tooltip-panel`, but as a plain alias of
 * their own `dialog4` — they are unchanged on purpose, and ts's sheet has a fully TRANSPARENT tile
 * where ww3mod's and ra's have an opaque one, so the ww3mod definition is not portable to it.
 * The declaration-exists half IS checked below for both of them, because a Background: naming a
 * missing collection draws NOTHING and does it silently (WidgetUtils.cs:93-95). ww3mod's own
 * declaration is proven by the opacity test itself, which reads it.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.FileFormats;
using OpenRA.Graphics;

namespace OpenRA.Test
{
	[TestFixture]
	public class TooltipPanelOpacityTest
	{
		/// <summary>
		/// The widget whose background this fixture is about, and the file that declares it. Shared
		/// with `ra` and `ts`; cnc and d2k declare their own PRODUCTION_TOOLTIP and never read it.
		/// </summary>
		const string TooltipWidget = "Background@PRODUCTION_TOOLTIP";

		/// <summary>Mods that load common/chrome/tooltips.yaml and so must declare the collection.</summary>
		static readonly string[] ModsSharingTheWidget = { "ra", "ts" };

		static DirectoryInfo FindRepoRoot()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
				if (File.Exists(Path.Combine(dir.FullName, "WW3MOD.sln")))
					return dir;

			throw new DirectoryNotFoundException("could not locate the repository root (WW3MOD.sln)");
		}

		static string RepoFile(params string[] relative)
		{
			var path = Path.Combine(new[] { FindRepoRoot().FullName }.Concat(relative).ToArray());
			if (!File.Exists(path))
				throw new FileNotFoundException("could not locate " + string.Join("/", relative), path);

			return path;
		}

		static string Field(MiniYamlNode node, string key)
		{
			return node.Value.Nodes.FirstOrDefault(n => n.Key == key)?.Value.Value?.Trim();
		}

		static IEnumerable<MiniYamlNode> Descendants(IEnumerable<MiniYamlNode> nodes)
		{
			foreach (var n in nodes)
			{
				yield return n;
				foreach (var inner in Descendants(n.Value.Nodes))
					yield return inner;
			}
		}

		/// <summary>The collection name that the shipped PRODUCTION_TOOLTIP asks to be drawn with.</summary>
		static string BackgroundCollection()
		{
			var chrome = MiniYaml.FromFile(RepoFile("engine", "mods", "common", "chrome", "tooltips.yaml"));
			var widget = Descendants(chrome).FirstOrDefault(n => n.Key == TooltipWidget);

			Assert.That(widget, Is.Not.Null,
				$"{TooltipWidget} is gone from common/chrome/tooltips.yaml. If the production tooltip " +
				"moved to a mod-owned chrome file, re-point this fixture at it rather than deleting it.");

			var collection = Field(widget, "Background");
			Assert.That(collection, Is.Not.Null.And.Not.Empty,
				$"{TooltipWidget} declares no Background. A BackgroundWidget with no collection draws " +
				"no panel at all, which is the failure this fixture exists to notice.");

			return collection;
		}

		/// <summary>The four "x, y, width, height" numbers of one named region in one chrome collection.</summary>
		static (int X, int Y, int Width, int Height) Region(string chromeYaml, string collection, string region)
		{
			var node = MiniYaml.FromFile(chromeYaml).FirstOrDefault(n => n.Key == collection);
			Assert.That(node, Is.Not.Null,
				$"{chromeYaml} declares no '{collection}' collection, but common/chrome/tooltips.yaml " +
				$"asks for it by name. A Background: naming a missing collection draws NOTHING and does " +
				"it silently — ChromeProvider.TryGetPanelImages returns null and WidgetUtils.DrawPanel " +
				"(WidgetUtils.cs:93-95) skips without a word.");

			var regions = node.Value.Nodes.FirstOrDefault(n => n.Key == "Regions");
			Assert.That(regions, Is.Not.Null,
				$"'{collection}' in {chromeYaml} has no Regions block. This fixture reads the " +
				"interior tile from one; a PanelRegion cannot take its centre from a different part " +
				"of the sheet, which is the whole reason this collection exists.");

			var value = regions.Value.Nodes.FirstOrDefault(n => n.Key == region)?.Value.Value;
			Assert.That(value, Is.Not.Null, $"'{collection}' declares no '{region}' region");

			var parts = value.Split(',').Select(p => int.Parse(p.Trim(), CultureInfo.InvariantCulture)).ToArray();
			Assert.That(parts, Has.Length.EqualTo(4), $"'{collection}/{region}' is not an \"x, y, w, h\" rect");
			return (parts[0], parts[1], parts[2], parts[3]);
		}

		[Test]
		public void TheProductionTooltipInteriorIsFullyOpaque()
		{
			var collection = BackgroundCollection();

			Assert.That(collection, Is.Not.EqualTo("dialog4"),
				"the production tooltip is back on dialog4, whose interior tile is black at alpha 159 " +
				"of 255. The panel overlaps the production sidebar by design, so at that alpha the " +
				"sidebar's unit portraits read straight through it (bugs/discovered.md 2026-08-30).");

			var chromeYaml = RepoFile("mods", "ww3mod", "chrome.yaml");
			var rect = Region(chromeYaml, collection, "background");

			// WidgetUtils.DrawPanel tiles this sprite across the panel's interior rect, inset by the
			// border sprite sizes (WidgetUtils.cs:174-176). Every pixel of it is therefore visible
			// somewhere on a large enough panel, so every pixel has to be opaque — not just a corner.
			var png = ReadSheet(chromeYaml, collection);

			Assert.That(png.Type, Is.EqualTo(SpriteFrameType.Rgba32),
				"the chrome sheet is not RGBA, so this fixture cannot read an alpha channel from it");

			var translucent = 0;
			var minAlpha = 255;
			for (var y = rect.Y; y < rect.Y + rect.Height; y++)
			{
				for (var x = rect.X; x < rect.X + rect.Width; x++)
				{
					var alpha = png.Data[(y * png.Width + x) * png.PixelStride + 3];
					if (alpha != 255)
					{
						translucent++;
						minAlpha = Math.Min(minAlpha, alpha);
					}
				}
			}

			Assert.That(translucent, Is.Zero,
				$"'{collection}/background' is the tile the production tooltip's interior is filled " +
				$"with, and {translucent} of its {rect.Width * rect.Height} pixels are translucent " +
				$"(lowest alpha {minAlpha} of 255). Whatever is behind the tooltip — the production " +
				"sidebar it overlaps — shows through by that much.");
		}

		/// <summary>
		/// The nine names ChromeProvider looks a Regions-based panel up by, in the order
		/// TryGetPanelImages asks for them (ChromeProvider.cs:248-256).
		/// </summary>
		static readonly string[] PanelRegionNames =
		{
			"corner-tl", "border-t", "corner-tr",
			"border-l", "background", "border-r",
			"corner-bl", "border-b", "corner-br"
		};

		[Test]
		public void TheProductionTooltipPanelDeclaresAllNineRegions()
		{
			// A Regions-based collection is looked up BY NAME, one name per slot, and a miss returns
			// null rather than throwing — DrawPanel then just skips that piece (WidgetUtils.cs:179+).
			// So a single mistyped region name costs the panel a border or a corner, silently, and
			// the opacity check above would not notice because it only reads `background`.
			var collection = BackgroundCollection();
			var chromeYaml = RepoFile("mods", "ww3mod", "chrome.yaml");

			foreach (var region in PanelRegionNames)
			{
				var rect = Region(chromeYaml, collection, region);
				Assert.That(rect.Width, Is.GreaterThan(0), $"'{collection}/{region}' has zero width");
				Assert.That(rect.Height, Is.GreaterThan(0), $"'{collection}/{region}' has zero height");
			}
		}

		[Test]
		public void EveryModSharingTheTooltipDeclaresTheCollection()
		{
			var collection = BackgroundCollection();

			foreach (var mod in ModsSharingTheWidget)
			{
				var chromeYaml = RepoFile("engine", "mods", mod, "chrome.yaml");
				var declared = MiniYaml.FromFile(chromeYaml).Any(n => n.Key == collection);

				Assert.That(declared, Is.True,
					$"the '{mod}' mod loads common/chrome/tooltips.yaml, which asks for the " +
					$"'{collection}' collection, but engine/mods/{mod}/chrome.yaml does not declare " +
					"one. Its production tooltip would draw no panel at all, and would do it " +
					"silently: TryGetPanelImages returns null and the caller skips.");
			}
		}

		/// <summary>The sheet a chrome collection draws from, via the `Image:` its parent supplies.</summary>
		static Png ReadSheet(string chromeYaml, string collection)
		{
			var nodes = MiniYaml.FromFile(chromeYaml);
			var node = nodes.First(n => n.Key == collection);

			// The collection states Inherits: ^Dialog and the Image lives on the parent, which is how
			// every collection in this file is written. Resolved rather than hard-coded so that moving
			// the tooltip onto a different sheet fails here loudly instead of checking the wrong art.
			var image = Field(node, "Image");
			var parent = Field(node, "Inherits");
			while (image == null && parent != null)
			{
				var parentNode = nodes.FirstOrDefault(n => n.Key == parent);
				Assert.That(parentNode, Is.Not.Null, $"'{collection}' inherits '{parent}', which is not declared");
				image = Field(parentNode, "Image");
				parent = Field(parentNode, "Inherits");
			}

			Assert.That(image, Is.Not.Null,
				$"could not resolve an Image for the '{collection}' chrome collection");

			using (var stream = File.OpenRead(RepoFile("mods", "ww3mod", "uibits", image)))
				return new Png(stream);
		}
	}
}
