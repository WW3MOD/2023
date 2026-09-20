#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

/*
 * NAMED CELL REGIONS STORED IN THE MAP PACKAGE -- map.yaml's `Zones:` node.
 *
 * WHY THIS EXISTS. WW3MOD's DEFCON 3 dividing border was authored as DefconWallInfo.RegionCells, a
 * flat `X,Y, X,Y, ...` CPos[] living in the map's own rules.yaml. That works and the engine still
 * reads it, but it is not editable: the map editor has no way to write rules.yaml, so the nine
 * shipped borders were drawn by a Python script (tools/nav-guard/defcon_border_designer.py) and
 * pasted in by hand. A border is a piece of MAP GEOMETRY, and map geometry belongs in the map
 * package where the editor can paint it.
 *
 * WHY A ROW-RANGE FORM AND NOT THE FLAT CPos[]. The flat form is one enormous line -- twin-rivers'
 * border is 399 cells on a single 4.5 kB line -- so every edit to it is a whole-line diff that no
 * reviewer can read. Ranges by row are the same data at roughly a tenth the size, they diff per row
 * rather than per file, and they are legible: a human can see "rows 12 to 103, three cells wide at
 * x59-61" in the file. The nine shipped borders are mostly long vertical bands, which is the exact
 * shape this form compresses best.
 *
 *     Zones:
 *         DMZ:
 *             12: 40-44
 *             13: 39-45, 60-61
 *
 * A row key is Y. Its value is a comma-separated list of INCLUSIVE X ranges, each either `lo-hi` or
 * a bare `x` for a single cell. Rows ascend, ranges within a row ascend, and runs are always
 * coalesced -- so Encode is canonical and two equal cell sets always produce byte-identical YAML.
 *
 * ZONES ARE NOT CLIPPED TO Bounds, and that is deliberate rather than an oversight. DefconWall adds
 * authored cells unconditionally, including cells outside Bounds, because that is how the border
 * ring gets closed where a river reaches the map edge but the playable Bounds stop one cell short
 * (DefconWall.BuildRegion). river-zeta-ww3 relies on it: 14 of its 844 authored cells are outside
 * Bounds. A codec that clipped would silently open a one-cell seam in a shipped map.
 *
 * LAYER IS NOT STORED. A zone is a set of ground cells; CPos.Layer addresses subterranean movement
 * layers and has no meaning for a painted region. Encode REFUSES a non-zero layer rather than
 * dropping it, because dropping it would make Decode(Encode(x)) quietly unequal to x.
 *
 * EVERYTHING HERE IS INTEGER-ONLY AND ALLOCATION-FREE AT RUNTIME. It runs at map load and at editor
 * save, never per tick, but the no-floating-point rule is the mod's and applies everywhere.
 */

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;

namespace OpenRA
{
	/// <summary>
	/// The named cell regions a map authors in map.yaml's <c>Zones:</c> node. Immutable; the editor
	/// builds a new instance with <see cref="WithZone"/> and assigns it to <see cref="Map.Zones"/>.
	/// </summary>
	public sealed class MapZones
	{
		/// <summary>
		/// The demilitarised zone: the DEFCON 3 dividing band, unioned into DefconWall's region path.
		/// The one zone id the engine itself gives meaning to; every other id is inert map metadata.
		/// </summary>
		public const string Dmz = "DMZ";

		/// <summary>A map that authors no zones at all. Every map without a <c>Zones:</c> node.</summary>
		public static readonly MapZones Empty = new(ImmutableSortedDictionary<string, CPos[]>.Empty);

		readonly ImmutableSortedDictionary<string, CPos[]> zones;

		MapZones(ImmutableSortedDictionary<string, CPos[]> zones)
		{
			this.zones = zones;
		}

		/// <summary>Zone ids present on this map, in the order they are written to map.yaml.</summary>
		public IEnumerable<string> ZoneIds => zones.Keys;

		/// <summary>How many zones this map authors. Zero on every map that has no <c>Zones:</c> node.</summary>
		public int Count => zones.Count;

		/// <summary>
		/// The cells of one zone, or an EMPTY ARRAY for a zone this map does not author. Never null:
		/// the caller's question is nearly always "which cells", and "no zone" and "an empty zone"
		/// are the same answer to it.
		/// </summary>
		public CPos[] this[string id] => zones.TryGetValue(id, out var cells) ? cells : Array.Empty<CPos>();

		/// <summary>
		/// This map's zones with <paramref name="id"/> replaced by <paramref name="cells"/>. An empty
		/// or null cell set REMOVES the zone, which is what keeps an emptied zone out of map.yaml
		/// rather than writing an empty block the next reader has to interpret.
		/// </summary>
		public MapZones WithZone(string id, IEnumerable<CPos> cells)
		{
			ValidateId(id);

			var distinct = cells == null ? Array.Empty<CPos>() : Canonical(cells);
			if (distinct.Length == 0)
				return zones.ContainsKey(id) ? new MapZones(zones.Remove(id)) : this;

			return new MapZones(zones.SetItem(id, distinct));
		}

		/// <summary>Sorted, de-duplicated, and rejected outright if any cell carries a movement layer.</summary>
		static CPos[] Canonical(IEnumerable<CPos> cells)
		{
			var seen = new HashSet<CPos>();
			foreach (var cell in cells)
			{
				if (cell.Layer != 0)
					throw new ArgumentException(
						$"A zone cell may not carry a movement layer; got {cell} on layer {cell.Layer}.", nameof(cells));

				seen.Add(cell);
			}

			return seen.OrderBy(c => c.Y).ThenBy(c => c.X).ToArray();
		}

		static void ValidateId(string id)
		{
			if (string.IsNullOrEmpty(id))
				throw new ArgumentException("A zone id may not be empty.", nameof(id));

			foreach (var c in id)
				if (!char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != '.')
					throw new ArgumentException(
						$"Invalid zone id `{id}`: a zone id is a MiniYaml key, so it may only contain " +
						"letters, digits, `-`, `_` and `.`.", nameof(id));
		}

		/// <summary>
		/// Read the <c>Zones:</c> node. Throws <see cref="YamlException"/> on anything it cannot
		/// read -- a malformed zone is a border that would silently not be there, and a border that
		/// claims to divide the map and does not is the one failure DefconWall's own design refuses
		/// to ship (see its BuildRegion note on a degenerate region).
		/// </summary>
		public static MapZones FromYaml(MiniYaml yaml)
		{
			if (yaml == null || yaml.Nodes.Length == 0)
				return Empty;

			var builder = ImmutableSortedDictionary.CreateBuilder<string, CPos[]>(StringComparer.Ordinal);
			foreach (var node in yaml.Nodes)
			{
				var id = node.Key;
				ValidateId(id);

				if (builder.ContainsKey(id))
					throw new YamlException($"Duplicate zone `{id}` in Zones.");

				var cells = DecodeRows(id, node.Value);
				if (cells.Length > 0)
					builder.Add(id, cells);
			}

			return builder.Count == 0 ? Empty : new MapZones(builder.ToImmutable());
		}

		/// <summary>
		/// The <c>Zones:</c> node, or a node with no children for a map that authors none -- which
		/// MapField.Serialize drops entirely, so an empty zone set adds nothing to map.yaml.
		/// </summary>
		public MiniYaml ToYaml()
		{
			if (zones.Count == 0)
				return new MiniYaml(null);

			var nodes = new List<MiniYamlNode>(zones.Count);
			foreach (var kv in zones)
				nodes.Add(new MiniYamlNode(kv.Key, EncodeRows(kv.Value)));

			return new MiniYaml(null, nodes);
		}

		/// <summary>
		/// One node per occupied row, `Y: lo-hi, lo-hi`. Canonical: rows ascend, ranges ascend, runs
		/// are coalesced, and a one-cell run is written bare.
		/// </summary>
		public static MiniYaml EncodeRows(IEnumerable<CPos> cells)
		{
			var byRow = new SortedDictionary<int, SortedSet<int>>();
			foreach (var cell in cells)
			{
				if (cell.Layer != 0)
					throw new ArgumentException(
						$"A zone cell may not carry a movement layer; got {cell} on layer {cell.Layer}.", nameof(cells));

				if (!byRow.TryGetValue(cell.Y, out var xs))
					byRow.Add(cell.Y, xs = new SortedSet<int>());

				xs.Add(cell.X);
			}

			var nodes = new List<MiniYamlNode>(byRow.Count);
			var sb = new StringBuilder();
			foreach (var row in byRow)
			{
				sb.Clear();
				var runStart = int.MinValue;
				var runEnd = int.MinValue;
				foreach (var x in row.Value)
				{
					if (runStart != int.MinValue && x == runEnd + 1)
					{
						runEnd = x;
						continue;
					}

					if (runStart != int.MinValue)
						AppendRun(sb, runStart, runEnd);

					runStart = runEnd = x;
				}

				if (runStart != int.MinValue)
					AppendRun(sb, runStart, runEnd);

				nodes.Add(new MiniYamlNode(row.Key.ToString(NumberFormatInfo.InvariantInfo), sb.ToString()));
			}

			return new MiniYaml(null, nodes);
		}

		static void AppendRun(StringBuilder sb, int lo, int hi)
		{
			if (sb.Length > 0)
				sb.Append(", ");

			sb.Append(lo.ToString(NumberFormatInfo.InvariantInfo));
			if (hi != lo)
			{
				sb.Append('-');
				sb.Append(hi.ToString(NumberFormatInfo.InvariantInfo));
			}
		}

		/// <summary>
		/// The inverse of <see cref="EncodeRows"/>. Lenient about whitespace and about a bare value
		/// where a range would do; loud about everything else.
		/// </summary>
		public static CPos[] DecodeRows(string zoneId, MiniYaml yaml)
		{
			if (yaml == null || yaml.Nodes.Length == 0)
				return Array.Empty<CPos>();

			var cells = new List<CPos>();
			var rowsSeen = new HashSet<int>();
			foreach (var node in yaml.Nodes)
			{
				if (!int.TryParse(node.Key.Trim(), NumberStyles.Integer, NumberFormatInfo.InvariantInfo, out var y))
					throw new YamlException($"Zone `{zoneId}`: `{node.Key}` is not a row number.");

				if (!rowsSeen.Add(y))
					throw new YamlException($"Zone `{zoneId}`: row {y} appears twice.");

				if (node.Value.Nodes.Length > 0)
					throw new YamlException($"Zone `{zoneId}`: row {y} has child nodes; a row is a list of X ranges.");

				foreach (var token in (node.Value.Value ?? "").Split(','))
				{
					var range = token.Trim();
					if (range.Length == 0)
						continue;

					var (lo, hi) = ParseRange(zoneId, y, range);
					for (var x = lo; x <= hi; x++)
						cells.Add(new CPos(x, y));
				}
			}

			return cells.ToArray();
		}

		/// <summary>
		/// `lo-hi` or a bare `x`. The separator is the first `-` at index > 0, which is what makes a
		/// negative bound unambiguous: `-3--1` splits at index 2 into `-3` and `-1`, and `-5` has no
		/// separator at all and is a single cell.
		/// </summary>
		static (int Lo, int Hi) ParseRange(string zoneId, int y, string range)
		{
			var split = range.IndexOf('-', 1);
			if (split < 0)
			{
				var single = ParseBound(zoneId, y, range);
				return (single, single);
			}

			var lo = ParseBound(zoneId, y, range[..split]);
			var hi = ParseBound(zoneId, y, range[(split + 1)..]);
			if (hi < lo)
				throw new YamlException($"Zone `{zoneId}`, row {y}: range `{range}` runs backwards.");

			return (lo, hi);
		}

		static int ParseBound(string zoneId, int y, string token)
		{
			if (!int.TryParse(token.Trim(), NumberStyles.Integer, NumberFormatInfo.InvariantInfo, out var value))
				throw new YamlException($"Zone `{zoneId}`, row {y}: `{token}` is not a number.");

			return value;
		}
	}
}
