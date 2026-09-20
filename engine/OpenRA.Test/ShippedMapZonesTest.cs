#region Copyright & License Information
/*
 * WW3MOD shipped-map DMZ zone tests.
 *
 * The nine hand-authored DEFCON borders were moved out of each map's rules.yaml
 * (DefconWallInfo.RegionCells, a flat X,Y list) and into its map.yaml (`Zones: DMZ`, row-ranges)
 * by tools/migrate-region-cells-to-zones.py. Three separate encoders now have to agree on that
 * format -- the C# codec here, the migration script, and tools/nav-guard/defcon_border_designer's
 * emit_zone -- and only ONE of them is the one the game actually reads.
 *
 * SO THIS FIXTURE READS THE SHIPPED FILES WITH THE ENGINE'S OWN CODEC. The Python side was checked
 * against itself and against the audit tool; this is what checks the side that matters. It is
 * pinned three ways:
 *
 *   PARSES AT ALL. MapZones.FromYaml throws on anything it cannot read, so a row the migration
 *   wrote wrongly fails here rather than at mod load on the user's machine.
 *
 *   CANONICAL ON DISK. Re-encoding what was parsed must be byte-identical to the file. If it is
 *   not, the first time anyone opens one of these maps in the editor and saves it, the whole
 *   Zones block rewrites itself and the diff buries the actual edit -- and the map UID moves for
 *   a change nobody made.
 *
 *   BIG ENOUGH TO BE A BORDER. A three-cell-wide band across a 128-cell map is hundreds of cells.
 *   A zone that parsed to a handful would be a migration that silently dropped rows, which every
 *   other check here would pass.
 *
 * WHAT THIS DOES NOT CHECK is whether each band still SEPARATES its map -- that needs terrain, so
 * it lives in tools/nav-guard/defcon_wall_audit.py --region-from-map, which was run before and
 * after the migration and produced byte-identical output.
 */
#endregion

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace OpenRA.Test
{
	[TestFixture]
	public class ShippedMapZonesTest
	{
		/// <summary>Every shipped map that authors a DMZ. shellmap-open-field deliberately has none.</summary>
		static readonly string[] MapsWithDmz =
		{
			"arena-tank-duel", "nuclear-winter-ww3", "polar-disorder-ww3", "river-zeta-ww3",
			"seventh-woods-ww3", "siberian-pass-ww3", "twin-rivers-ww3", "woodland-warfare-ww3",
			"x-lake-ww3",
		};

		static DirectoryInfo MapsDir()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = new DirectoryInfo(Path.Combine(dir.FullName, "mods", "ww3mod", "maps"));
				if (candidate.Exists)
					return candidate;
			}

			throw new DirectoryNotFoundException("could not locate mods/ww3mod/maps");
		}

		static MiniYaml ZonesNode(string mapName)
		{
			var path = Path.Combine(MapsDir().FullName, mapName, "map.yaml");
			Assert.That(File.Exists(path), Is.True, $"{mapName}: no map.yaml");

			var yaml = new MiniYaml(null, MiniYaml.FromFile(path));
			return yaml.NodeWithKeyOrDefault("Zones")?.Value;
		}

		[Test]
		public void EveryShippedBorderParsesWithTheEnginesOwnCodec()
		{
			foreach (var mapName in MapsWithDmz)
			{
				var node = ZonesNode(mapName);
				Assert.That(node, Is.Not.Null, $"{mapName}: map.yaml has no Zones node");

				var zones = MapZones.FromYaml(node);
				Assert.That(zones[MapZones.Dmz], Is.Not.Empty, $"{mapName}: the DMZ zone decoded to nothing");
			}
		}

		[Test]
		public void EveryShippedBorderIsAlreadyInCanonicalForm()
		{
			foreach (var mapName in MapsWithDmz)
			{
				var zones = MapZones.FromYaml(ZonesNode(mapName));
				var onDisk = ZonesNode(mapName).Nodes.WriteToString();
				var reEncoded = zones.ToYaml().Nodes.WriteToString();

				Assert.That(reEncoded, Is.EqualTo(onDisk),
					$"{mapName}: saving this map in the editor would rewrite its whole Zones block");
			}
		}

		[Test]
		public void EveryShippedBorderIsLargeEnoughToBeABand()
		{
			// The smallest shipped border is arena-tank-duel's 96 cells on a small map; the largest is
			// seventh-woods' 606. Anything under 50 is a migration that lost rows.
			foreach (var mapName in MapsWithDmz)
			{
				var cells = MapZones.FromYaml(ZonesNode(mapName))[MapZones.Dmz];
				Assert.That(cells.Length, Is.GreaterThan(50), $"{mapName}: only {cells.Length} DMZ cell(s)");
				Assert.That(cells.Distinct().Count(), Is.EqualTo(cells.Length), $"{mapName}: duplicate DMZ cells");
			}
		}

		[Test]
		public void TheCellsAreNoLongerDuplicatedInRulesYaml()
		{
			// The migration MOVED the cells rather than copying them. A RegionCells line left behind
			// would still load and still work -- unioned with the zone, identically -- which is
			// exactly why it would never be noticed, and why the next edit to the border would fix
			// one copy and not the other.
			foreach (var mapName in MapsWithDmz)
			{
				var path = Path.Combine(MapsDir().FullName, mapName, "rules.yaml");
				var lines = File.ReadAllLines(path);

				Assert.That(lines.Any(l => l.TrimStart().StartsWith("RegionCells:", StringComparison.Ordinal)), Is.False,
					$"{mapName}: rules.yaml still authors RegionCells after the migration");
			}
		}

		[Test]
		public void RiverZetaKeepsItsTerrainTypes()
		{
			// The one map whose border is terrain PLUS hand-drawn cells. The migration must not have
			// taken the terrain half with it -- DefconWall unions the two, and without the types this
			// map's border is 210 cells of ford and abutment with no river between them.
			var path = Path.Combine(MapsDir().FullName, "river-zeta-ww3", "rules.yaml");
			var text = File.ReadAllText(path);

			Assert.That(text, Does.Contain("RegionTerrainTypes: Water, River, Bridge"));
		}

		[Test]
		public void ShellmapAuthorsNoZoneAtAll()
		{
			// Its border is deliberately absent, and a Zones node appearing here would mean the
			// migration invented one.
			Assert.That(ZonesNode("shellmap-open-field"), Is.Null);
		}
	}
}
