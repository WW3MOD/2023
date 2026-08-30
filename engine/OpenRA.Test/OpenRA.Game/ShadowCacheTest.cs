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

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace OpenRA.Test
{
	/// <summary>
	/// ShadowCache persists generated shadow/density layers outside the map package. Two failure
	/// modes matter and they fail in opposite directions:
	///
	/// <para>A cache that always misses is indistinguishable from no cache at all, only slower — so
	/// these tests prove a HIT actually reads the stored bytes, not merely that nothing threw.</para>
	///
	/// <para>A cache that wrongly hits serves stale concealment values, and because shadow feeds
	/// vision attenuation and firing LOS, two players disagreeing about it is a desync. So each of
	/// the three key terms is shown to invalidate independently.</para>
	/// </summary>
	[TestFixture]
	public class ShadowCacheTest
	{
		string dir;

		[SetUp]
		public void SetUp()
		{
			dir = Path.Combine(Path.GetTempPath(), "ww3-shadowcache-test-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);
		}

		[TearDown]
		public void TearDown()
		{
			if (Directory.Exists(dir))
				Directory.Delete(dir, true);
		}

		static byte[] Payload(int length, byte seed)
		{
			var bytes = new byte[length];
			for (var i = 0; i < length; i++)
				bytes[i] = (byte)((i * 31) + seed);

			return bytes;
		}

		byte[] LoadOrNull(string key)
		{
			byte[] got = null;
			var hit = ShadowCache.TryLoad(dir, key, s =>
			{
				using (var ms = new MemoryStream())
				{
					s.CopyTo(ms);
					got = ms.ToArray();
				}
			});

			return hit ? got : null;
		}

		[Test(Description = "A stored entry is served back byte-for-byte — the cache genuinely hits.")]
		public void StoredEntryIsServedBack()
		{
			var payload = Payload(4096, 7);
			ShadowCache.TrySave(dir, "key-a", payload, ShadowCache.MaxCacheBytes);

			// Not just "did not throw": the exact bytes must come back, and they must have come off
			// the disk rather than out of the generator.
			Assert.That(LoadOrNull("key-a"), Is.EqualTo(payload));
			Assert.That(Directory.GetFiles(dir, "*.bin").Length, Is.EqualTo(1));
		}

		[Test(Description = "An absent entry is a miss, not an exception.")]
		public void AbsentEntryIsAMiss()
		{
			Assert.That(LoadOrNull("never-written"), Is.Null);
		}

		[Test(Description = "Identical inputs produce the same key — the no-change case is a hit.")]
		public void UnchangedInputsHit()
		{
			var a = ShadowCache.ComputeKey("uid-1", "density-1");
			var b = ShadowCache.ComputeKey("uid-1", "density-1");

			Assert.That(a, Is.EqualTo(b));

			ShadowCache.TrySave(dir, a, Payload(512, 3), ShadowCache.MaxCacheBytes);
			Assert.That(LoadOrNull(b), Is.Not.Null);
		}

		[Test(Description = "A map edit changes the UID and therefore misses.")]
		public void ChangedMapUidMisses()
		{
			var before = ShadowCache.ComputeKey("uid-1", "density-1");
			var after = ShadowCache.ComputeKey("uid-2", "density-1");

			Assert.That(after, Is.Not.EqualTo(before));

			ShadowCache.TrySave(dir, before, Payload(512, 3), ShadowCache.MaxCacheBytes);
			Assert.That(LoadOrNull(after), Is.Null);
		}

		[Test(Description = "A rules-only density edit misses, even though no map byte changed.")]
		public void ChangedDensityRulesMisses()
		{
			var before = ShadowCache.ComputeKey("uid-1", "density-1");
			var after = ShadowCache.ComputeKey("uid-1", "density-2");

			Assert.That(after, Is.Not.EqualTo(before));

			ShadowCache.TrySave(dir, before, Payload(512, 3), ShadowCache.MaxCacheBytes);
			Assert.That(LoadOrNull(after), Is.Null);
		}

		[Test(Description = "Bumping AlgoVersion misses — this is what makes editing the shadow curve safe.")]
		public void BumpedAlgoVersionMisses()
		{
			var before = ShadowCache.ComputeKey("uid-1", "density-1", 1);
			var after = ShadowCache.ComputeKey("uid-1", "density-1", 2);

			Assert.That(after, Is.Not.EqualTo(before));

			ShadowCache.TrySave(dir, before, Payload(512, 3), ShadowCache.MaxCacheBytes);
			Assert.That(LoadOrNull(after), Is.Null);
		}

		[Test(Description = "The shipped const routes through the same derivation the version test exercises.")]
		public void PublicKeyMatchesExplicitVersionAtTheShippedConst()
		{
			Assert.That(
				ShadowCache.ComputeKey("uid-1", "density-1"),
				Is.EqualTo(ShadowCache.ComputeKey("uid-1", "density-1", ShadowCache.AlgoVersion)));
		}

		[Test(Description = "A truncated file is a miss, not a half-filled layer.")]
		public void TruncatedEntryIsAMiss()
		{
			ShadowCache.TrySave(dir, "key-a", Payload(4096, 7), ShadowCache.MaxCacheBytes);

			var path = Directory.GetFiles(dir, "*.bin").Single();
			using (var f = new FileStream(path, FileMode.Open, FileAccess.Write))
				f.SetLength(f.Length - 1024);

			Assert.That(LoadOrNull("key-a"), Is.Null);
		}

		[Test(Description = "A reader that stops short of the payload end is a miss, not a prefix accepted as a whole layer.")]
		public void APartiallyConsumedPayloadIsAMiss()
		{
			ShadowCache.TrySave(dir, "key-a", Payload(4096, 7), ShadowCache.MaxCacheBytes);

			// Stands in for the annulus geometry shrinking: same uid, same rules, same AlgoVersion,
			// but the reader now wants fewer bytes than the stored entry holds. The header length
			// check cannot see this, because the file really does contain what it claims.
			var hit = ShadowCache.TryLoad(dir, "key-a", s => s.ReadByte());

			Assert.That(hit, Is.False, "A short read was accepted, serving a prefix as a complete layer.");
		}

		[Test(Description = "A file that is not a cache entry at all is a miss, not an exception.")]
		public void GarbageEntryIsAMiss()
		{
			File.WriteAllBytes(Path.Combine(dir, "key-a.bin"), new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
			Assert.That(LoadOrNull("key-a"), Is.Null);
		}

		[Test(Description = "An entry for a different key, renamed into place, is rejected by the header.")]
		public void ForeignEntryIsAMiss()
		{
			ShadowCache.TrySave(dir, "key-a", Payload(4096, 7), ShadowCache.MaxCacheBytes);

			var path = Directory.GetFiles(dir, "*.bin").Single();
			File.Move(path, Path.Combine(dir, "key-b.bin"));

			// The filename now says key-b, but the header still says key-a.
			Assert.That(LoadOrNull("key-b"), Is.Null);
		}

		[Test(Description = "Eviction holds the directory under the cap, oldest-used first.")]
		public void EvictionHoldsTheCapAndDropsLeastRecentlyUsed()
		{
			const int Size = 4096;
			var cap = (Size * 3) + 512;

			foreach (var k in new[] { "old", "middle", "recent" })
			{
				ShadowCache.TrySave(dir, k, Payload(Size, 1), cap);

				// Distinct write times so the LRU order is unambiguous rather than resolution-dependent.
				File.SetLastWriteTimeUtc(Path.Combine(dir, k + ".bin"),
					DateTime.UtcNow - TimeSpan.FromHours(k == "old" ? 3 : k == "middle" ? 2 : 1));
			}

			Assert.That(Directory.GetFiles(dir, "*.bin").Length, Is.EqualTo(3));

			// A fourth entry cannot fit, so the least recently used one must go.
			ShadowCache.TrySave(dir, "newest", Payload(Size, 2), cap);

			var remaining = Directory.GetFiles(dir, "*.bin").Select(Path.GetFileNameWithoutExtension).ToArray();
			Assert.That(remaining, Does.Not.Contain("old"));
			Assert.That(remaining, Does.Contain("newest"));
			Assert.That(Directory.GetFiles(dir, "*.bin").Sum(f => new FileInfo(f).Length), Is.LessThanOrEqualTo(cap));
		}

		/// <remarks>
		/// This does NOT prove the write is atomic — a writer going straight to the final path also
		/// leaves no temp, and this test passes when it does. Atomicity rests on File.Move within a
		/// directory being atomic, which is a platform guarantee rather than something a unit test
		/// can show without racing a reader against a writer. What this pins is that the temp file
		/// is always cleaned up on the success path, so temps in the directory always mean a crash.
		/// </remarks>
		[Test(Description = "A successful write leaves no temp litter behind.")]
		public void WriteLeavesNoTempLitter()
		{
			ShadowCache.TrySave(dir, "key-a", Payload(4096, 7), ShadowCache.MaxCacheBytes);

			Assert.That(Directory.GetFiles(dir, "*" + ".tmp"), Is.Empty);
			Assert.That(Directory.GetFiles(dir), Has.Length.EqualTo(1));
		}

		[Test(Description = "Rewriting a key replaces the entry in place rather than leaving two.")]
		public void RewritingAKeyReplacesTheEntry()
		{
			var first = Payload(4096, 7);
			var second = Payload(2048, 11);

			ShadowCache.TrySave(dir, "key-a", first, ShadowCache.MaxCacheBytes);
			Assert.That(LoadOrNull("key-a"), Is.EqualTo(first));

			ShadowCache.TrySave(dir, "key-a", second, ShadowCache.MaxCacheBytes);

			Assert.That(Directory.GetFiles(dir), Has.Length.EqualTo(1));
			Assert.That(LoadOrNull("key-a"), Is.EqualTo(second));
		}

		[Test(Description = "A temp orphaned by a killed process is reaped once stale, so it cannot eat the cap.")]
		public void StaleTempFilesAreReaped()
		{
			var orphan = Path.Combine(dir, "key-z.bin.deadbeef.tmp");
			File.WriteAllBytes(orphan, Payload(4096, 9));
			File.SetLastWriteTimeUtc(orphan, DateTime.UtcNow - TimeSpan.FromHours(6));

			var inFlight = Path.Combine(dir, "key-y.bin.cafebabe.tmp");
			File.WriteAllBytes(inFlight, Payload(4096, 9));

			ShadowCache.TrySave(dir, "key-a", Payload(512, 1), ShadowCache.MaxCacheBytes);

			Assert.That(File.Exists(orphan), Is.False, "A stale temp should have been reaped.");

			// A temp that could still belong to a write in flight must survive.
			Assert.That(File.Exists(inFlight), Is.True, "A fresh temp must not be reaped.");
		}

		[Test(Description = "A write that fails reclaims its temp instead of stranding the payload on disk.")]
		public void AFailedWriteLeavesNoTemp()
		{
			// A directory where the entry should go makes File.Move throw after the temp is fully
			// written — the same shape as a deferred ENOSPC surfacing at fsync, which is the most
			// likely real cause and the one that matters, since it strands up to 87 MB.
			Directory.CreateDirectory(Path.Combine(dir, "key-a.bin"));

			ShadowCache.TrySave(dir, "key-a", Payload(4096, 7), ShadowCache.MaxCacheBytes);

			Assert.That(Directory.GetFiles(dir, "*.tmp"), Is.Empty, "A failed write stranded its temp file.");
		}

		[Test(Description = "An entry larger than the cap does not wipe every other entry to make room it cannot use.")]
		public void AnOversizedEntryDoesNotWipeTheCache()
		{
			const int Cap = 8192;
			ShadowCache.TrySave(dir, "keep-me", Payload(1024, 1), Cap);
			Assert.That(LoadOrNull("keep-me"), Is.Not.Null);

			// Bigger than the whole cap. Evicting for it can never make it fit.
			ShadowCache.TrySave(dir, "oversized", Payload(Cap * 2, 2), Cap);

			Assert.That(LoadOrNull("keep-me"), Is.Not.Null,
				"An entry that cannot fit under the cap evicted the entries that could.");
			Assert.That(LoadOrNull("oversized"), Is.Null,
				"An entry larger than the cap was stored, putting the directory permanently over it.");
			Assert.That(Directory.GetFiles(dir).Sum(f => new FileInfo(f).Length), Is.LessThanOrEqualTo(Cap));
		}

		[Test(Description = "A hit restamps the entry, so the cache evicts by use rather than by age.")]
		public void AHitRefreshesTheLruStamp()
		{
			ShadowCache.TrySave(dir, "key-a", Payload(512, 1), ShadowCache.MaxCacheBytes);

			var path = Path.Combine(dir, "key-a.bin");
			var stale = DateTime.UtcNow - TimeSpan.FromHours(5);
			File.SetLastWriteTimeUtc(path, stale);

			Assert.That(LoadOrNull("key-a"), Is.Not.Null);
			Assert.That(File.GetLastWriteTimeUtc(path), Is.GreaterThan(stale));
		}
	}
}
