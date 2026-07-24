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

using System.Numerics;
using NUnit.Framework;
using OpenRA.Support;

namespace OpenRA.Test
{
	// Pins the pure LocalRandom seed-derivation used by World (bot-decision RNG
	// reproducibility, PIPELINE item 15). LocalRandom is seeded from the lobby
	// RandomSeed via a fixed PCG64 LCG transform so a fixed Test.RandomSeed
	// reproduces a whole match; these guard the transform's invariants so a
	// well-meaning "cleanup" of the magic constants can't silently reshuffle every
	// seeded stream and invalidate the seeded benchmark history.
	[TestFixture]
	public class SeedDerivationTest
	{
		// Representative seeds: the tournament harness's i*1000+17 family, the
		// unset sentinel, extremes, and a negative (the DateTime.Now fallback cast
		// to int can be negative).
		static readonly int[] Seeds = { 1, 17, 1017, 2017, 42, -42, int.MaxValue, int.MinValue };

		// Independent wide-integer reference for the same transform: full-precision
		// BigInteger multiply, then the low 32 bits reinterpreted as int32. This
		// pins the exact constants AND the two's-complement truncation semantics
		// without relying on C# unchecked long-overflow behaviour.
		static int ReferenceDerive(int seed)
		{
			var wide = (BigInteger)seed * 6364136223846793005 + 1442695040888963407;
			var low = (uint)(wide & 0xFFFFFFFF);
			return unchecked((int)low);
		}

		[TestCase(TestName = "DeriveLocalSeed matches the wide-integer reference (constants + truncation frozen)")]
		public void MatchesReference()
		{
			foreach (var s in Seeds)
				Assert.That(World.DeriveLocalSeed(s), Is.EqualTo(ReferenceDerive(s)),
					$"seed {s}");
		}

		[TestCase(TestName = "DeriveLocalSeed is deterministic (same input, same output)")]
		public void Deterministic()
		{
			foreach (var s in Seeds)
				Assert.That(World.DeriveLocalSeed(s), Is.EqualTo(World.DeriveLocalSeed(s)),
					$"seed {s}");
		}

		[TestCase(TestName = "DeriveLocalSeed has no fixed point (LocalRandom decorrelates from SharedRandom's seed)")]
		public void HasNoFixedPoint()
		{
			// x = (a*x + c) mod 2^32 requires x*(1-a) = c; a is odd so (1-a) is even
			// and c is odd, so no solution exists — the derived seed always differs
			// from the lobby seed, keeping the two MersenneTwister streams distinct.
			foreach (var s in Seeds)
				Assert.That(World.DeriveLocalSeed(s), Is.Not.EqualTo(s), $"seed {s}");
		}

		[TestCase(TestName = "DeriveLocalSeed is injective over distinct seeds (odd multiplier => bijection)")]
		public void DistinctSeedsDeriveDistinctSeeds()
		{
			for (var i = 0; i < Seeds.Length; i++)
				for (var j = i + 1; j < Seeds.Length; j++)
					Assert.That(World.DeriveLocalSeed(Seeds[i]), Is.Not.EqualTo(World.DeriveLocalSeed(Seeds[j])),
						$"seeds {Seeds[i]} and {Seeds[j]} collided");
		}

		[TestCase(TestName = "MersenneTwister replays identically for a fixed seed (reproducibility foundation)")]
		public void MersenneTwisterIsSeedReproducible()
		{
			const int Seed = 1017;
			var a = new MersenneTwister(Seed);
			var b = new MersenneTwister(Seed);
			for (var i = 0; i < 64; i++)
				Assert.That(a.Next(), Is.EqualTo(b.Next()), $"draw {i}");
		}

		[TestCase(TestName = "SharedRandom and LocalRandom streams diverge under the same lobby seed")]
		public void DerivedStreamDiffersFromRawSeedStream()
		{
			// SharedRandom is seeded with the raw lobby seed; LocalRandom with the
			// derived seed. Feeding both into MersenneTwister must yield different
			// streams, or bot decisions would be correlated with combat rolls.
			const int LobbySeed = 1017;
			var shared = new MersenneTwister(LobbySeed);
			var local = new MersenneTwister(World.DeriveLocalSeed(LobbySeed));

			var diverged = false;
			for (var i = 0; i < 64 && !diverged; i++)
				if (shared.Next() != local.Next())
					diverged = true;

			Assert.That(diverged, Is.True, "derived LocalRandom stream never diverged from the raw-seed stream");
		}
	}
}
