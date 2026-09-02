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
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class TargetEqualityTest
	{
		// Target's Terrain branch compares terrainPositions — a WPos[] — with ==, i.e. by REFERENCE
		// (Target.cs:233), and every terrain Target allocates its own backing array (Target.cs:37, :50,
		// :86). So two independently constructed terrain Targets at the identical position are NEVER
		// equal, while a struct copy (which shares the array reference) always is.
		//
		// This is load-bearing, not academic: Armament.CheckFire resets AimingDelay whenever the target
		// "changed" (Armament.cs:412-419) and AimingDelay defaults to 15 (Armament.cs:101). Any caller
		// that rebuilds a terrain Target per tick therefore re-arms the delay every tick and can never
		// fire. These tests pin the current semantics so that widening the comparison to a sequence
		// compare is a deliberate, visible change rather than a silent one.

		[Test]
		public void TerrainTargetsAtTheSamePositionAreNotEqual()
		{
			var pos = new WPos(1024, 2048, 0);

			var a = Target.FromPos(pos);
			var b = Target.FromPos(pos);

			Assert.That(a.CenterPosition, Is.EqualTo(b.CenterPosition),
				"Precondition: both targets describe the same world position.");

			Assert.That(a == b, Is.False,
				"Terrain equality compares the backing WPos[] by reference, so separately " +
				"constructed terrain targets never compare equal.");
			Assert.That(a != b, Is.True);
			Assert.That(a.Equals(b), Is.False);
		}

		[Test]
		public void ACopyOfATerrainTargetIsEqualToItself()
		{
			// This is why most call sites survive: passing a Target around (including through an
			// `in` parameter, or storing it in a field) copies the struct but shares the array
			// reference, so the comparison still reports "unchanged".
			var a = Target.FromPos(new WPos(1024, 2048, 0));
			var copy = a;

			Assert.That(a == copy, Is.True);
			Assert.That(a.Equals(copy), Is.True);
		}

		[Test]
		public void TerrainTargetEqualityIgnoresPositionOnceReferencesDiffer()
		{
			// The position/cell/subCell comparisons in the Terrain branch are unreachable for
			// independently constructed targets: the reference check short-circuits first. Pinning
			// this makes it explicit that those terms currently carry no weight.
			var near = Target.FromPos(new WPos(1024, 2048, 0));
			var far = Target.FromPos(new WPos(999999, 999999, 0));

			Assert.That(near == far, Is.False);
			Assert.That(Target.FromPos(new WPos(1024, 2048, 0)) == near, Is.False,
				"Same position, different array — still unequal.");
		}

		[Test]
		public void InvalidTargetsAreNeverEqualEvenToThemselves()
		{
			// The Invalid branch returns false unconditionally (Target.cs:242-244), so an
			// `x == Target.Invalid` guard can never fire. Code must test Type instead.
			var invalid = Target.Invalid;

			Assert.That(invalid == Target.Invalid, Is.False);
#pragma warning disable CS1718 // Comparison made to same variable — that is exactly the point here.
			Assert.That(invalid == invalid, Is.False);
#pragma warning restore CS1718
			Assert.That(invalid.Type, Is.EqualTo(TargetType.Invalid),
				"Type is the reliable way to detect an invalid target.");
		}

		[Test]
		public void TerrainTargetHashCodeIsReferenceBasedAndMatchesEquality()
		{
			// GetHashCode folds in terrainPositions.GetHashCode() — also reference identity — so it
			// stays consistent with ==. That consistency is what keeps a hypothetical Target-keyed
			// dictionary well-defined; it just makes value lookups impossible. (There are no
			// Target-keyed collections in the tree today.)
			var pos = new WPos(1024, 2048, 0);
			var a = Target.FromPos(pos);
			var copy = a;

			Assert.That(a.GetHashCode(), Is.EqualTo(copy.GetHashCode()),
				"Equal targets must agree on hash code.");
		}
	}
}
