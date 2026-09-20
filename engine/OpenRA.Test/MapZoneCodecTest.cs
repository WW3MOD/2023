#region Copyright & License Information
/*
 * WW3MOD map-zone codec tests.
 *
 * MapZones is the storage half of the editor's Zones tool: the row-range form written to map.yaml's
 * `Zones:` node. It is a pure codec with no dependency on Map, World or the editor, which is the
 * whole reason it was written as a separate class -- nothing in OpenRA.Test can construct a World,
 * and a codec verified by reading is a codec that loses a shipped border the first time someone
 * touches it.
 *
 * WHAT ACTUALLY HAS TO HOLD, and it is narrower and harder than "it round-trips":
 *
 *   1. ROUND-TRIP EXACT, on cell SETS. Nine shipped DEFCON borders are about to be migrated into
 *      this format by a script. If decode(encode(x)) drops or invents one cell, a border silently
 *      stops separating the map and the wall never goes up at DEFCON 3 -- which DefconWall logs and
 *      nothing else reports. This is the test that migration is safe.
 *
 *   2. CANONICAL. encode is a FUNCTION of the cell set, not of the order it arrived in. Two mappers
 *      painting the same band in different directions must produce byte-identical map.yaml, or
 *      every save is a spurious diff and the map UID churns.
 *
 *   3. LOUD ON GARBAGE. A row that does not parse must throw, not yield an empty row. The failure
 *      this prevents is the quiet one: a typo'd border that loads fine, draws nothing, and divides
 *      nothing.
 *
 *   4. NOT CLIPPED. river-zeta-ww3 carries 14 border cells OUTSIDE Bounds, deliberately -- they are
 *      what closes the ring where the river reaches the map edge and Bounds stops one cell short.
 *      A codec that clipped to anything would open a one-cell seam in a shipped map.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace OpenRA.Test
{
	[TestFixture]
	public class MapZoneCodecTest
	{
		static MiniYaml Parse(string text)
		{
			return new MiniYaml(null, MiniYaml.FromString(text, "test"));
		}

		static string Render(MiniYaml yaml)
		{
			return yaml.Nodes.WriteToString();
		}

		/// <summary>A three-cell-wide vertical band with a kink, i.e. the shape every shipped border is.</summary>
		static List<CPos> Band()
		{
			var cells = new List<CPos>();
			for (var y = 0; y < 40; y++)
			{
				var x = y < 20 ? 59 : 62;
				for (var dx = 0; dx < 3; dx++)
					cells.Add(new CPos(x + dx, y));
			}

			return cells;
		}

		[Test]
		public void ARowIsWrittenAsInclusiveRanges()
		{
			var yaml = MapZones.EncodeRows(new[]
			{
				new CPos(40, 12), new CPos(41, 12), new CPos(42, 12), new CPos(43, 12), new CPos(44, 12),
				new CPos(39, 13), new CPos(60, 13), new CPos(61, 13),
			});

			Assert.That(Render(yaml).Trim().Replace("\r", ""), Is.EqualTo("12: 40-44\n13: 39, 60-61"));
		}

		[Test]
		public void EncodeThenDecodeReturnsTheSameCells()
		{
			var cells = Band();
			var decoded = MapZones.DecodeRows("DMZ", MapZones.EncodeRows(cells));

			Assert.That(decoded.OrderBy(c => c.Y).ThenBy(c => c.X).ToArray(),
				Is.EqualTo(cells.OrderBy(c => c.Y).ThenBy(c => c.X).ToArray()));
		}

		[Test]
		public void EncodeIsCanonicalRegardlessOfTheOrderCellsArriveIn()
		{
			var forwards = Band();
			var backwards = Enumerable.Reverse(forwards).ToArray();

			Assert.That(Render(MapZones.EncodeRows(backwards)), Is.EqualTo(Render(MapZones.EncodeRows(forwards))),
				"the same band painted in the opposite direction produced different YAML");
		}

		[Test]
		public void ReEncodingADecodedZoneIsByteIdentical()
		{
			var once = MapZones.EncodeRows(Band());
			var twice = MapZones.EncodeRows(MapZones.DecodeRows("DMZ", once));

			Assert.That(Render(twice), Is.EqualTo(Render(once)));
		}

		[Test]
		public void DuplicateCellsCollapseRatherThanDuplicating()
		{
			var cells = Band();
			var withDupes = cells.Concat(cells).ToArray();

			Assert.That(MapZones.DecodeRows("DMZ", MapZones.EncodeRows(withDupes)).Length, Is.EqualTo(cells.Count));
		}

		[Test]
		public void NegativeAndOutOfBoundsCoordinatesSurviveTheRoundTrip()
		{
			// river-zeta-ww3's out-of-Bounds ring cells are the shipped case; the negatives pin the
			// `-3--1` separator rule, which is the one place the format could be read two ways.
			var cells = new[] { new CPos(-3, -2), new CPos(-2, -2), new CPos(-1, -2), new CPos(0, 0), new CPos(500, 900) };
			var decoded = MapZones.DecodeRows("DMZ", MapZones.EncodeRows(cells));

			Assert.That(decoded.OrderBy(c => c.Y).ThenBy(c => c.X).ToArray(),
				Is.EqualTo(cells.OrderBy(c => c.Y).ThenBy(c => c.X).ToArray()));
		}

		[Test]
		public void ANegativeRunIsWrittenWithBothSigns()
		{
			var yaml = MapZones.EncodeRows(new[] { new CPos(-3, 7), new CPos(-2, 7), new CPos(-1, 7) });
			Assert.That(Render(yaml).Trim(), Is.EqualTo("7: -3--1"));
		}

		[Test]
		public void ABareValueIsAcceptedWhereARangeWouldDo()
		{
			var cells = MapZones.DecodeRows("DMZ", Parse("4: 7\n5: 1-2, 9"));
			Assert.That(cells, Is.EquivalentTo(new[] { new CPos(7, 4), new CPos(1, 5), new CPos(2, 5), new CPos(9, 5) }));
		}

		[Test]
		public void WhitespaceAndEmptyTokensAreTolerated()
		{
			var cells = MapZones.DecodeRows("DMZ", Parse("  4  :   7 ,  9-10 ,"));
			Assert.That(cells, Is.EquivalentTo(new[] { new CPos(7, 4), new CPos(9, 4), new CPos(10, 4) }));
		}

		[Test]
		public void ARowThatIsNotANumberThrows()
		{
			Assert.Throws<YamlException>(() => MapZones.DecodeRows("DMZ", Parse("twelve: 1-2")));
		}

		[Test]
		public void ARangeBoundThatIsNotANumberThrows()
		{
			Assert.Throws<YamlException>(() => MapZones.DecodeRows("DMZ", Parse("12: 1-two")));
		}

		[Test]
		public void ABackwardsRangeThrows()
		{
			Assert.Throws<YamlException>(() => MapZones.DecodeRows("DMZ", Parse("12: 9-4")));
		}

		[Test]
		public void ARowWithChildNodesThrows()
		{
			Assert.Throws<YamlException>(() => MapZones.DecodeRows("DMZ", Parse("12:\n\t13: 1-2")));
		}

		[Test]
		public void ACellOnAMovementLayerIsRefusedRatherThanFlattened()
		{
			Assert.Throws<ArgumentException>(() => MapZones.EncodeRows(new[] { new CPos(3, 4, 1) }));
		}

		[Test]
		public void AnEmptyZoneSetSerialisesToNothingAtAll()
		{
			// MapField.Serialize drops a MiniYaml with no value and no children, so this is what keeps
			// `Zones:` out of the 300-odd maps that have none.
			var yaml = MapZones.Empty.ToYaml();
			Assert.That(yaml.Value, Is.Null);
			Assert.That(yaml.Nodes.Length, Is.Zero);
		}

		[Test]
		public void AnEmptiedZoneIsRemovedRatherThanWrittenEmpty()
		{
			var zones = MapZones.Empty.WithZone(MapZones.Dmz, Band());
			Assert.That(zones.Count, Is.EqualTo(1));

			Assert.That(zones.WithZone(MapZones.Dmz, Array.Empty<CPos>()).Count, Is.Zero);
			Assert.That(zones.WithZone(MapZones.Dmz, null).Count, Is.Zero);
		}

		[Test]
		public void AZoneThisMapDoesNotAuthorReadsAsEmptyRatherThanNull()
		{
			Assert.That(MapZones.Empty[MapZones.Dmz], Is.Empty);
			Assert.That(MapZones.Empty["NOSUCHZONE"], Is.Empty);
		}

		[Test]
		public void TheWholeNodeRoundTripsThroughText()
		{
			// The real path: MapZones -> MiniYaml -> map.yaml text -> MiniYaml -> MapZones.
			var original = MapZones.Empty.WithZone(MapZones.Dmz, Band());
			var text = original.ToYaml().Nodes.WriteToString();
			var reloaded = MapZones.FromYaml(Parse(text));

			Assert.That(reloaded.Count, Is.EqualTo(1));
			Assert.That(reloaded[MapZones.Dmz], Is.EqualTo(original[MapZones.Dmz]));
		}

		[Test]
		public void TwoZonesRoundTripIndependently()
		{
			var zones = MapZones.Empty
				.WithZone(MapZones.Dmz, Band())
				.WithZone("STAGING", new[] { new CPos(1, 1), new CPos(2, 1) });

			var reloaded = MapZones.FromYaml(Parse(zones.ToYaml().Nodes.WriteToString()));

			Assert.That(reloaded.ZoneIds, Is.EquivalentTo(new[] { MapZones.Dmz, "STAGING" }));
			Assert.That(reloaded[MapZones.Dmz], Is.EqualTo(zones[MapZones.Dmz]));
			Assert.That(reloaded["STAGING"], Is.EqualTo(zones["STAGING"]));
		}

		[Test]
		public void AZoneIdThatIsNotAValidYamlKeyIsRefused()
		{
			Assert.Throws<ArgumentException>(() => MapZones.Empty.WithZone("has space", new[] { CPos.Zero }));
			Assert.Throws<ArgumentException>(() => MapZones.Empty.WithZone("", new[] { CPos.Zero }));
		}

		[Test]
		public void AMissingZonesNodeIsTheSameAsAnEmptyOne()
		{
			Assert.That(MapZones.FromYaml(null).Count, Is.Zero);
			Assert.That(MapZones.FromYaml(new MiniYaml(null)).Count, Is.Zero);
		}

		[Test]
		public void TheCompactFormIsDramaticallySmallerThanTheFlatCPosList()
		{
			// Not a style point: twin-rivers-ww3's border is 399 cells on ONE 4.5 kB line in
			// rules.yaml today, which is why nobody can review a change to it. Pinned so a future
			// "simplification" back to one cell per entry has to argue with a number.
			var cells = Band();
			var flat = string.Join(", ", cells.Select(c => $"{c.X},{c.Y}"));
			var compact = MapZones.EncodeRows(cells).Nodes.WriteToString();

			Assert.That(compact.Length, Is.LessThan(flat.Length / 2));
		}
	}
}
