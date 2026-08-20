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

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// Concealment and observer vision strength are composed independently but resolved against each other
	/// on one shared 1..VisionLayers-1 ladder. While concealment could reach the top of that ladder, a unit
	/// there was beyond every observer in the game at every range — a Sniper at rank 3, stopped, reaches it
	/// from base 5 plus prone, dug-in and veterancy alone. These cases pin the gap that prevents it.
	/// </summary>
	[TestFixture]
	public class DetectableCeilingTest
	{
		static readonly int TopVisionBand = MapLayers.VisionLayers - 1;

		[Test]
		public void ConcealmentCannotReachTheTopVisionBand()
		{
			Assert.That(Detectable.ClampConcealment(int.MaxValue), Is.LessThan(TopVisionBand),
				"The top vision level is reserved for observers. If concealment can reach it, the strongest " +
				"observer in the game can at best match the most concealed unit, and nothing guarantees a reveal.");
		}

		[Test]
		public void TopBandObserverDetectsTheMostConcealedUnitPossible()
		{
			// The invariant that actually matters, and it must not depend on the reveal comparison staying
			// non-strict: standard vision has to win SOMEWHERE against the best concealment obtainable.
			var mostConcealed = Detectable.ClampConcealment(int.MaxValue);

			Assert.That(MapLayers.IsDetected(TopVisionBand, mostConcealed), Is.True,
				$"An observer at the top vision band must detect a unit concealed at {mostConcealed}.");
			Assert.That(TopVisionBand > mostConcealed, Is.True,
				"The top band must STRICTLY exceed maximum concealment, so this holds even if reveal is " +
				"reverted to a strict comparison.");
		}

		[TestCase(0)]
		[TestCase(-7)]
		[TestCase(int.MinValue)]
		public void ConcealmentFloorIsPreserved(int composed)
		{
			// Pre-existing and deliberate: 0 is shroud's level and must never be a concealment value.
			Assert.That(Detectable.ClampConcealment(composed), Is.EqualTo(1));
		}

		[Test]
		public void ClampNeverProducesZero()
		{
			// Diagnostic, and it earned its place: a scenario read Detectable.CurrentVisibility before
			// the actor's first tick, got the uninitialised 0, and the failure text invited the reading
			// that the clamp had produced it. It cannot. 0 from GetVisibilityLevel always means "has not
			// ticked yet"; -1 means "no Detectable trait". Neither is a statement about the ceiling.
			foreach (var composed in new[] { int.MinValue, -100, -1, 0, 1, 5, 9, 10, 11, 100, int.MaxValue })
				Assert.That(Detectable.ClampConcealment(composed), Is.Not.Zero,
					$"ClampConcealment({composed}) returned 0, which would make an uninitialised " +
					"CurrentVisibility indistinguishable from a computed one.");
		}

		[Test]
		public void ConcealmentIsUntouchedBelowTheCeilingAndCappedAbove()
		{
			// The loop must run PAST the ceiling or the capped branch is unreachable and this asserts
			// nothing about the ceiling at all — which is what the first version of it did.
			var ceiling = MapLayers.VisionLayers - 2;

			for (var i = 1; i <= MapLayers.VisionLayers + 2; i++)
				Assert.That(Detectable.ClampConcealment(i), Is.EqualTo(i <= ceiling ? i : ceiling),
					$"ClampConcealment({i}) with ceiling {ceiling}");
		}
	}
}
