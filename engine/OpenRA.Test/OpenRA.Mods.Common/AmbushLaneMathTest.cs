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

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// Pins the pure lane geometry for the Stage-4 bot lane-ambush consumer (PIPELINE item 8). The
	/// world-touching parts (finding the friendly/enemy anchors, snapping to a passable cell, claiming
	/// units, granting the gate) live in <see cref="LaneAmbushBotModule"/>; the geometry + viability
	/// decisions are <see cref="AmbushLaneMath"/> so they can be exercised here with no simulation harness.
	///
	/// Source of truth: engine/OpenRA.Mods.Common/Traits/BotModules/LaneAmbushBotModule.cs
	/// </summary>
	[TestFixture]
	public class AmbushLaneMathTest
	{
		// ── PostPosition: interpolate friendly -> enemy by percent ──

		[Test]
		public void PostAtZeroPercentIsTheFriendlyAnchor()
		{
			var f = new WPos(1024, 2048, 0);
			var e = new WPos(9024, 2048, 0);
			Assert.That(AmbushLaneMath.PostPosition(f, e, 0), Is.EqualTo(f));
		}

		[Test]
		public void PostAtHundredPercentIsTheEnemyAnchor()
		{
			var f = new WPos(1024, 2048, 0);
			var e = new WPos(9024, 6048, 0);
			Assert.That(AmbushLaneMath.PostPosition(f, e, 100), Is.EqualTo(e));
		}

		[Test]
		public void PostAtFiftyPercentIsTheMidpoint()
		{
			var f = new WPos(0, 0, 0);
			var e = new WPos(8000, 4000, 0);
			Assert.That(AmbushLaneMath.PostPosition(f, e, 50), Is.EqualTo(new WPos(4000, 2000, 0)));
		}

		[Test]
		public void DefaultFortyPercentStaysOnOurSideOfTheMidline()
		{
			// The shipped default (PostFractionPct = 40): the post sits nearer our beachhead than the
			// enemy's — concealed in our own territory, on the corridor attackers commit down.
			var f = new WPos(0, 0, 0);
			var e = new WPos(10000, 0, 0);
			var p = AmbushLaneMath.PostPosition(f, e, 40);
			Assert.That(p, Is.EqualTo(new WPos(4000, 0, 0)));

			var distToFriendly = (p - f).HorizontalLength;
			var distToEnemy = (p - e).HorizontalLength;
			Assert.That(distToFriendly, Is.LessThan(distToEnemy), "post must be closer to our own beachhead");
		}

		[Test]
		public void PostInterpolatesAllThreeAxes()
		{
			var f = new WPos(100, 200, 300);
			var e = new WPos(1100, 1200, 1300);
			Assert.That(AmbushLaneMath.PostPosition(f, e, 50), Is.EqualTo(new WPos(600, 700, 800)));
		}

		[Test]
		public void PostFractionIsClampedToRange()
		{
			var f = new WPos(0, 0, 0);
			var e = new WPos(1000, 0, 0);

			// Negative clamps to 0 (the friendly anchor); over 100 clamps to 100 (the enemy anchor).
			Assert.That(AmbushLaneMath.PostPosition(f, e, -30), Is.EqualTo(f));
			Assert.That(AmbushLaneMath.PostPosition(f, e, 250), Is.EqualTo(e));
		}

		[Test]
		public void PostHandlesNegativeDeltaDirection()
		{
			// Enemy is "behind" us on an axis (negative delta): interpolation is still linear + deterministic.
			var f = new WPos(10000, 0, 0);
			var e = new WPos(0, 0, 0);
			Assert.That(AmbushLaneMath.PostPosition(f, e, 40), Is.EqualTo(new WPos(6000, 0, 0)));
		}

		// ── LaneIsViable: reject degenerate near-adjacent beachheads ──

		[Test]
		public void LaneViableAtOrAboveMinSeparation()
		{
			Assert.That(AmbushLaneMath.LaneIsViable(12, 12), Is.True);   // exactly at the floor
			Assert.That(AmbushLaneMath.LaneIsViable(40, 12), Is.True);   // well separated
		}

		[Test]
		public void LaneNotViableBelowMinSeparation()
		{
			// Beachheads basically adjacent ⇒ the interpolated post would sit on our own base ⇒ reject.
			Assert.That(AmbushLaneMath.LaneIsViable(5, 12), Is.False);
			Assert.That(AmbushLaneMath.LaneIsViable(0, 12), Is.False);
		}

		[Test]
		public void LaneViabilityFloorsNegativeThresholdAtZero()
		{
			// A negative threshold is floored to 0, so any non-negative separation is viable.
			Assert.That(AmbushLaneMath.LaneIsViable(0, -5), Is.True);
			Assert.That(AmbushLaneMath.LaneIsViable(3, -5), Is.True);
		}
	}
}
