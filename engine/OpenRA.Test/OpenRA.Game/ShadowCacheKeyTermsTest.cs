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

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenRA.GameRules;
using OpenRA.Traits;

namespace OpenRA.Test
{
	sealed class MockDensityInfo : TraitInfo, IDensityInfo
	{
		readonly Dictionary<CVec, byte> density;

		public MockDensityInfo(Dictionary<CVec, byte> density) { this.density = density; }

		Dictionary<CVec, byte> IDensityInfo.Density() { return density; }

		public override object Create(ActorInitializer init) { return null; }
	}

	/// <summary>
	/// The two cache-key terms that cannot be exercised through ShadowCacheTest's string-in/string-out
	/// surface, and that are each the only thing standing between a rules or algorithm change and a
	/// silently stale concealment layer.
	/// </summary>
	[TestFixture]
	public class ShadowCacheKeyTermsTest
	{
		static Ruleset RulesWith(params (string Name, Dictionary<CVec, byte> Density)[] actors)
		{
			var infos = actors.ToDictionary(
				a => a.Name,
				a => a.Density == null
					? new ActorInfo(a.Name)
					: new ActorInfo(a.Name, new MockDensityInfo(a.Density)));

			return new Ruleset(infos,
				new Dictionary<string, WeaponInfo>(),
				new Dictionary<string, SoundInfo>(),
				new Dictionary<string, SoundInfo>(),
				new Dictionary<string, MusicInfo>(),
				null,
				new Dictionary<string, MiniYamlNode>());
		}

		static Dictionary<CVec, byte> Footprint(params (int X, int Y, byte D)[] cells)
		{
			return cells.ToDictionary(c => new CVec(c.X, c.Y), c => c.D);
		}

		[Test(Description = "The hash does not depend on the order actors happen to enumerate in.")]
		public void DensityHashIsStableAcrossEnumerationOrder()
		{
			var forward = RulesWith(
				("aaa", Footprint((0, 0, 10), (1, 0, 5))),
				("bbb", Footprint((0, 0, 3))),
				("ccc", Footprint((0, 1, 7))));

			var reversed = RulesWith(
				("ccc", Footprint((0, 1, 7))),
				("bbb", Footprint((0, 0, 3))),
				("aaa", Footprint((1, 0, 5), (0, 0, 10))));

			Assert.That(
				ShadowCache.ComputeDensityRulesHash(reversed),
				Is.EqualTo(ShadowCache.ComputeDensityRulesHash(forward)));
		}

		[Test(Description = "Changing a density VALUE changes the hash — the rules-only staleness route.")]
		public void ChangingADensityValueChangesTheHash()
		{
			var before = RulesWith(("tree", Footprint((0, 0, 10))));
			var after = RulesWith(("tree", Footprint((0, 0, 11))));

			Assert.That(
				ShadowCache.ComputeDensityRulesHash(after),
				Is.Not.EqualTo(ShadowCache.ComputeDensityRulesHash(before)));
		}

		[Test(Description = "Changing which cells carry density changes the hash.")]
		public void ChangingADensityFootprintChangesTheHash()
		{
			var before = RulesWith(("tree", Footprint((0, 0, 10))));
			var after = RulesWith(("tree", Footprint((0, 0, 10), (1, 0, 10))));

			Assert.That(
				ShadowCache.ComputeDensityRulesHash(after),
				Is.Not.EqualTo(ShadowCache.ComputeDensityRulesHash(before)));
		}

		[Test(Description = "Moving density between actors changes the hash, so the actor name is part of it.")]
		public void MovingDensityBetweenActorsChangesTheHash()
		{
			var before = RulesWith(("tree", Footprint((0, 0, 10))), ("rock", null));
			var after = RulesWith(("tree", null), ("rock", Footprint((0, 0, 10))));

			Assert.That(
				ShadowCache.ComputeDensityRulesHash(after),
				Is.Not.EqualTo(ShadowCache.ComputeDensityRulesHash(before)));
		}

		[Test(Description = "An actor with no density does not affect the hash, so unrelated rules edits do not force a regen.")]
		public void ActorsWithoutDensityDoNotAffectTheHash()
		{
			var lean = RulesWith(("tree", Footprint((0, 0, 10))));
			var padded = RulesWith(("tree", Footprint((0, 0, 10))), ("infantry", null), ("tank", null));

			Assert.That(
				ShadowCache.ComputeDensityRulesHash(padded),
				Is.EqualTo(ShadowCache.ComputeDensityRulesHash(lean)));
		}

		/// <summary>
		/// A forgotten AlgoVersion bump is otherwise undetectable: the payload length is purely
		/// geometric and invariant under every curve change, so a cached entry generated with the old
		/// curve passes every check the cache makes and serves the wrong concealment forever.
		/// Nothing in the language links Map.ForestGroundShadow to ShadowCache.AlgoVersion, so this
		/// test is the link.
		///
		/// <para>If this fails you changed the shadow curve. That is fine — bump
		/// ShadowCache.AlgoVersion so every existing cache entry is rebuilt, then update the constant
		/// below to the reported actual. Do NOT just update the constant.</para>
		///
		/// <para>Covers the ground curve and the annulus geometry. It does NOT cover the airborne
		/// channel's /5f, the 512 obstacle height or the 2048 eye height, which are literals inside
		/// RecomputeShadowFrom with no reachable accessor — changing one of those still silently
		/// requires a manual bump.</para>
		/// </summary>
		[Test(Description = "The shadow curve has not changed without a matching AlgoVersion bump.")]
		public void ShadowCurveMatchesTheRecordedAlgoVersion()
		{
			var checksum = 17;
			for (var density = 0; density <= 255; density++)
				checksum = (checksum * 31) + Map.ForestGroundShadow(density);

			checksum = (checksum * 31) + Map.ForestShadowKneeDensity;
			checksum = (checksum * 31) + MapShadowLayer.MinRange;
			checksum = (checksum * 31) + MapShadowLayer.MaxRange;

			Assert.That(checksum, Is.EqualTo(-1804186489),
				"The shadow generation algorithm changed. Bump ShadowCache.AlgoVersion (currently " +
				ShadowCache.AlgoVersion + ") so stale cache entries are rebuilt, then record the new checksum.");
		}
	}
}
