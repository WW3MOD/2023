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
using NUnit.Framework;
using OpenRA.Primitives;

namespace OpenRA.Test
{
	[TestFixture]
	public class RelationshipShadeTest
	{
		// The ww3mod band bases (mods/ww3mod/metrics.yaml). Enemies is the interesting one:
		// it is the band that most often holds several players.
		static readonly Color Enemies = Color.FromArgb(0xFF, 0x00, 0x00);
		static readonly Color Allies = Color.FromArgb(0x32, 0xCD, 0x32);
		static readonly Color Self = Color.FromArgb(0x1E, 0x90, 0xFF);

		static float Lightness(Color c)
		{
			var (_, _, s, v) = c.ToAhsv();
			return v * (1 - s / 2);
		}

		static float Hue(Color c) => c.ToAhsv().H;

		[TestCase(1)]
		[TestCase(2)]
		[TestCase(4)]
		[TestCase(8)]
		public void ShadingNeverLeavesTheHueBand(int count)
		{
			// The whole scheme rests on this: an enemy must never drift far enough to read as an ally.
			foreach (var (band, name) in new[] { (Enemies, "enemies"), (Allies, "allies"), (Self, "self") })
			{
				var baseHue = Hue(band);
				for (var i = 0; i < count; i++)
				{
					var hue = Hue(RelationshipShade.Shade(band, i, count));
					Assert.That(hue, Is.EqualTo(baseHue).Within(1.0f),
						$"{name} shade {i}/{count} moved hue off its band");
				}
			}
		}

		[Test]
		public void ASingleOccupantGetsTheTunedColorExactly()
		{
			// The 1v1 case, and the reason a lone ally still looks like the colour the mod tuned.
			Assert.That(RelationshipShade.Shade(Enemies, 0, 1), Is.EqualTo(Enemies));
			Assert.That(RelationshipShade.Shade(Allies, 0, 1), Is.EqualTo(Allies));
			Assert.That(RelationshipShade.Shade(Self, 0, 1), Is.EqualTo(Self));
		}

		[TestCase(2)]
		[TestCase(3)]
		[TestCase(4)]
		[TestCase(5)]
		[TestCase(6)]
		[TestCase(7)]
		[TestCase(8)]
		public void DistinctPlayersGetDistinctColors(int count)
		{
			var seen = new List<Color>();
			for (var i = 0; i < count; i++)
				seen.Add(RelationshipShade.Shade(Enemies, i, count));

			Assert.That(seen.Distinct().Count(), Is.EqualTo(count),
				$"two of {count} enemies collided to the same 8-bit colour");
		}

		[TestCase(2)]
		[TestCase(3)]
		[TestCase(4)]
		[TestCase(5)]
		[TestCase(6)]
		[TestCase(7)]
		[TestCase(8)]
		public void IndexOrderRunsLightToDark(int count)
		{
			// "a slightly darker red for enemy nr 2" — the ordering is part of the spec, not incidental.
			for (var i = 1; i < count; i++)
			{
				var lighter = Lightness(RelationshipShade.Shade(Enemies, i - 1, count));
				var darker = Lightness(RelationshipShade.Shade(Enemies, i, count));
				Assert.That(darker, Is.LessThan(lighter),
					$"enemy {i} was not darker than enemy {i - 1} at count {count}");
			}
		}

		[TestCase(2, 0.12f)]
		[TestCase(3, 0.12f)]
		[TestCase(4, 0.12f)]
		[TestCase(5, 0.11f)]
		[TestCase(6, 0.088f)]
		[TestCase(7, 0.073f)]
		[TestCase(8, 0.063f)]
		public void AdjacentShadesKeepTheExpectedSeparation(int count, float expectedStep)
		{
			// Pins the compression schedule: a comfortable fixed 0.12 while the band is small, then an
			// even squeeze once MaxSpan can no longer hold that. If someone retunes the constants, this
			// is the test that says by how much legibility moved.
			for (var i = 1; i < count; i++)
			{
				var gap = Lightness(RelationshipShade.Shade(Enemies, i - 1, count))
					- Lightness(RelationshipShade.Shade(Enemies, i, count));
				Assert.That(gap, Is.EqualTo(expectedStep).Within(0.01f),
					$"step between enemy {i - 1} and {i} at count {count}");
			}
		}

		[TestCase(2)]
		[TestCase(5)]
		[TestCase(8)]
		public void RampStaysWithinLegibleLightnessBounds(int count)
		{
			// No shade may bottom out at near-black or wash out to near-white on the minimap.
			for (var i = 0; i < count; i++)
			{
				var l = Lightness(RelationshipShade.Shade(Enemies, i, count));
				Assert.That(l, Is.InRange(RelationshipShade.MinLightness - 0.01f, RelationshipShade.MaxLightness + 0.01f));
			}
		}

		[Test]
		public void ADarkBaseSlidesTheWindowRatherThanClippingIt()
		{
			// A band base near the edge of the range must keep full separation, not collapse against it.
			var darkBase = Color.FromArgb(0x30, 0x00, 0x00);
			var lightBase = Color.FromArgb(0xFF, 0xD0, 0xD0);
			foreach (var band in new[] { darkBase, lightBase })
			{
				for (var i = 1; i < 4; i++)
				{
					var gap = Lightness(RelationshipShade.Shade(band, i - 1, 4))
						- Lightness(RelationshipShade.Shade(band, i, 4));
					Assert.That(gap, Is.EqualTo(RelationshipShade.PreferredStep).Within(0.01f));
				}
			}
		}

		[Test]
		public void ShadeIsAPureFunctionOfIndexAndCount()
		{
			// Stability: the same player in the same band gets the same colour on every call, on every
			// client, for the whole match. Player.BandRank supplies the index from World.Players order.
			for (var count = 1; count <= 8; count++)
			{
				for (var i = 0; i < count; i++)
				{
					var a = RelationshipShade.Shade(Enemies, i, count);
					var b = RelationshipShade.Shade(Enemies, i, count);
					Assert.That(a, Is.EqualTo(b));
				}
			}
		}

		[TestCase(-1, 4)]
		[TestCase(4, 4)]
		[TestCase(0, 0)]
		public void OutOfRangeInputsFallBackToTheBaseColor(int index, int count)
		{
			Assert.That(RelationshipShade.Shade(Enemies, index, count), Is.EqualTo(Enemies));
		}

		[Test]
		public void CompressionIsMonotonicInBandSize()
		{
			// Degradation must be gradual: a bigger band is never MORE separated than a smaller one.
			var previous = float.MaxValue;
			for (var count = 2; count <= 16; count++)
			{
				var gap = Lightness(RelationshipShade.Shade(Enemies, 0, count))
					- Lightness(RelationshipShade.Shade(Enemies, 1, count));
				Assert.That(gap, Is.LessThanOrEqualTo(previous + 0.0001f),
					$"separation grew going from {count - 1} to {count} players");
				Assert.That(gap, Is.GreaterThan(0f));
				previous = gap;
			}
		}
	}
}
