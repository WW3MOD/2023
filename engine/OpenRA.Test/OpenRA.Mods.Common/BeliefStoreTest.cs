#region Copyright & License Information
/*
 * WW3MOD influence stack — Stage A belief-store lifecycle test.
 *
 * Drives the pure PlayerBeliefContacts table through scripted recompute passes to
 * pin the commander's-view contact lifecycle (design §2A): sight, lose-visual,
 * persist-with-decay, re-sight, verified-clear, and static-no-decay. No world is
 * mounted — the engine plumbing (fog reads) is exercised separately in-game.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class BeliefStoreTest
	{
		// Mirrors BeliefStoreInfo defaults.
		const int Fresh = 100;
		const int Frozen = 60;
		const int DecayPercent = 75;
		const int MinConfidence = 15;

		static readonly CPos CellA = new(10, 10);
		static readonly CPos CellB = new(14, 12);

		static void LoseVisual(PlayerBeliefContacts s)
		{
			// A recompute pass in which nothing is observed (target under fog).
			s.BeginPass();
			s.DecayUnrefreshed(DecayPercent, MinConfidence);
		}

		[Test]
		public void SightRecordsFreshContact()
		{
			var s = new PlayerBeliefContacts();
			s.BeginPass();
			s.Observe(1, CellA, "t90", isStatic: false, Fresh, tick: 0);

			Assert.That(s.Count, Is.EqualTo(1));
			Assert.That(s.TryGet(1, out var c), Is.True);
			Assert.That(c.Confidence, Is.EqualTo(100));
			Assert.That(c.Cell, Is.EqualTo(CellA));
			Assert.That(c.IsStatic, Is.False);
		}

		[Test]
		public void RefreshedContactDoesNotDecay()
		{
			var s = new PlayerBeliefContacts();
			s.BeginPass();
			s.Observe(1, CellA, "t90", isStatic: false, Fresh, tick: 0);
			// Same pass it was observed in: decay must skip it.
			s.DecayUnrefreshed(DecayPercent, MinConfidence);

			Assert.That(s.TryGet(1, out var c), Is.True);
			Assert.That(c.Confidence, Is.EqualTo(100));
		}

		[Test]
		public void MobileContactPersistsThenDecaysAway()
		{
			var s = new PlayerBeliefContacts();
			s.BeginPass();
			s.Observe(1, CellA, "t90", isStatic: false, Fresh, tick: 0);

			// Lose visual: the contact must PERSIST at its last-seen cell, fading.
			LoseVisual(s);
			Assert.That(s.TryGet(1, out var c), Is.True, "mobile contact should persist on first lost pass");
			Assert.That(c.Confidence, Is.EqualTo(75));
			Assert.That(c.Cell, Is.EqualTo(CellA), "persists at last-seen cell");

			// Keep losing visual; confidence monotonically falls and is eventually culled.
			var lastConfidence = c.Confidence;
			var passes = 1;
			while (s.TryGet(1, out c))
			{
				Assert.That(c.Confidence, Is.LessThanOrEqualTo(lastConfidence), "confidence must not rise while unobserved");
				lastConfidence = c.Confidence;
				LoseVisual(s);
				if (++passes > 50)
					Assert.Fail("mobile contact never decayed away");
			}

			Assert.That(s.Count, Is.EqualTo(0), "mobile contact is culled once below MinConfidence");
		}

		[Test]
		public void ReSightResetsToFresh()
		{
			var s = new PlayerBeliefContacts();
			s.BeginPass();
			s.Observe(1, CellA, "t90", isStatic: false, Fresh, tick: 0);
			LoseVisual(s);
			LoseVisual(s);
			Assert.That(s.TryGet(1, out var decayed), Is.True);
			Assert.That(decayed.Confidence, Is.LessThan(100));

			// Re-acquire at a new cell: confidence back to Fresh, position updated.
			s.BeginPass();
			s.Observe(1, CellB, "t90", isStatic: false, Fresh, tick: 10);
			Assert.That(s.TryGet(1, out var c), Is.True);
			Assert.That(c.Confidence, Is.EqualTo(100));
			Assert.That(c.Cell, Is.EqualTo(CellB), "seen-moving updates the contact to the new cell");
		}

		[Test]
		public void VerifiedClearRemovesImmediately()
		{
			// Models the engine's verified-clear: cell observed empty ⇒ Remove.
			var s = new PlayerBeliefContacts();
			s.BeginPass();
			s.Observe(1, CellA, "t90", isStatic: false, Fresh, tick: 0);

			s.BeginPass();
			s.Remove(1); // cell came into vision, nothing there.
			Assert.That(s.Count, Is.EqualTo(0));
			Assert.That(s.TryGet(1, out _), Is.False);
		}

		[Test]
		public void StaticContactNeverDecays()
		{
			var s = new PlayerBeliefContacts();
			s.BeginPass();
			s.Observe(2, CellA, "bunker", isStatic: true, Frozen, tick: 0);

			// Many unobserved passes: a static contact holds its confidence and survives.
			for (var i = 0; i < 30; i++)
				LoseVisual(s);

			Assert.That(s.TryGet(2, out var c), Is.True, "static contact must persist until verified gone");
			Assert.That(c.Confidence, Is.EqualTo(Frozen), "static contact does not decay");
		}

		[Test]
		public void StaticContactStillRemovableWhenVerifiedGone()
		{
			// No decay, but explicit removal (structure observed destroyed) still works.
			var s = new PlayerBeliefContacts();
			s.BeginPass();
			s.Observe(2, CellA, "bunker", isStatic: true, Frozen, tick: 0);
			s.BeginPass();
			s.Remove(2);
			Assert.That(s.Count, Is.EqualTo(0));
		}

		[Test]
		public void IndependentContactsDecayIndependently()
		{
			var s = new PlayerBeliefContacts();
			s.BeginPass();
			s.Observe(1, CellA, "t90", isStatic: false, Fresh, tick: 0);
			s.Observe(2, CellB, "bunker", isStatic: true, Frozen, tick: 0);

			for (var i = 0; i < 5; i++)
				LoseVisual(s);

			// Static survives untouched; mobile has faded.
			Assert.That(s.TryGet(2, out var stat), Is.True);
			Assert.That(stat.Confidence, Is.EqualTo(Frozen));
			Assert.That(s.TryGet(1, out var mob), Is.True);
			Assert.That(mob.Confidence, Is.LessThan(Fresh));
		}
	}
}
