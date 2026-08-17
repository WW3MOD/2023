#region Copyright & License Information
/*
 * WW3MOD sync-hash folding characterisation.
 *
 * Sync.GenerateHashFunc folds a trait's [Sync] members with position-independent XOR (Ldfld; Xor per
 * member), and a bool contributes only 0 or 1. Two consequences that are easy to get wrong when
 * deciding what to annotate:
 *
 *  1. Two bools that change TOGETHER cancel — the trait hashes the same as if neither had changed.
 *  2. Folding the same booleans into ONE int at distinct bit positions removes the cancellation,
 *     because a single member is XORed exactly once.
 *
 * This is why VehicleCrew carries a packed SyncCrewState property in addition to its per-field
 * annotations: DamageStateChanged sets `ejecting` and `waitingForStop` in the same statement block,
 * so on the critical transition the two raw bools cancel each other out.
 *
 * NOTE: the 0xaaa/0x555 constants in Sync.EmitSyncOpcodes are dead code — the Brtrue tests the
 * constant it just pushed, so the bool falls through as raw 0/1. Restoring them would NOT fix the
 * cancellation (0x555^0x555 == 0xaaa^0xaaa == 0); only distinct bit positions do. Do not "fix" those
 * constants expecting better detection — it would change every sync hash for no gain.
 */
#endregion

using System;
using System.Reflection;
using NUnit.Framework;

namespace OpenRA.Test
{
	[TestFixture]
	public class SyncFoldingTest
	{
		sealed class TwoBools : ISync
		{
			[Sync]
			public bool A;

			[Sync]
			public bool B;
		}

		sealed class PackedBools : ISync
		{
			public bool A;
			public bool B;

			[Sync]
			int Packed => (A ? 1 : 0) | (B ? 1 << 1 : 0);
		}

		static int Hash(object o)
		{
			var generate = typeof(Sync).GetMethod("GenerateHashFunc", BindingFlags.NonPublic | BindingFlags.Static);
			Assert.That(generate, Is.Not.Null, "Sync.GenerateHashFunc not found — this test needs updating.");
			return ((Func<object, int>)generate.Invoke(null, new object[] { o.GetType() }))(o);
		}

		[Test]
		public void TwoBoolsChangingTogetherCancelInTheFold()
		{
			var neither = new TwoBools { A = false, B = false };
			var both = new TwoBools { A = true, B = true };
			var onlyA = new TwoBools { A = true, B = false };

			// Sanity: a single bool IS visible, so the collision below is not "nothing is hashed".
			Assert.That(Hash(onlyA), Is.Not.EqualTo(Hash(neither)),
				"A single [Sync] bool should change the trait hash.");

			Assert.That(Hash(both), Is.EqualTo(Hash(neither)),
				"Characterisation: two [Sync] bools set together are expected to cancel under XOR folding. " +
				"If this now FAILS, the hasher's folding changed for the better — re-read the packed " +
				"SyncCrewState comment in VehicleCrew, which exists only to work around this.");
		}

		[Test]
		public void PackingIntoDistinctBitsSurvivesASimultaneousChange()
		{
			var neither = new PackedBools { A = false, B = false };
			var both = new PackedBools { A = true, B = true };

			Assert.That(Hash(both), Is.Not.EqualTo(Hash(neither)),
				"Packing both flags into one int at distinct bit positions must survive a simultaneous " +
				"change — this is the property VehicleCrew.SyncCrewState relies on.");
		}
	}
}
