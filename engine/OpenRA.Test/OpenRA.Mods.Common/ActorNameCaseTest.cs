#region Copyright & License Information
/*
 * WW3MOD bot-module actor-name case-hardening — mechanism pins.
 *
 * Actor names are lowercased at ruleset load (Ruleset.cs:126), but bot-module actor-name config
 * collections load with the default ORDINAL (case-sensitive) comparer, so an uppercase YAML value
 * silently never matches. ActorNameCase.NormalizeInPlace / NormalizeKeysInPlace lowercase those
 * collections once in each Info's RulesetLoaded. These pin the two mechanisms:
 *   (1) HASHSET — an uppercase-valued set matches a lowercase actor-name lookup post-normalization.
 *   (2) DICTIONARY — an uppercase-keyed dict resolves under a lowercase actor-name key post-normalization,
 *       preserving values.
 *   (3) BEHAVIOR-IDENTITY — an all-lowercase collection (the state of every ww3mod field today) is
 *       left unchanged, so the hardening is a no-op for current config; it only changes what a future
 *       uppercase typo does (silent miss -> still matches). Idempotent under a second pass.
 * Pure over synthetic collections; no world mounted.
 */
#endregion

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class ActorNameCaseTest
	{
		[Test]
		public void HashSet_UppercaseValue_MatchesLowercaseActorNameAfterNormalize()
		{
			// The confirmed AirUnitsTypes-shaped defect: uppercase entry never matched the always-lowercase
			// actor name pre-fix.
			var set = new HashSet<string> { "A10", "F16" };
			Assert.That(set.Contains("a10"), Is.False, "precondition: ordinal set misses the lowercase actor name");

			ActorNameCase.NormalizeInPlace(set);

			Assert.That(set.Contains("a10"), Is.True, "post-normalize: lowercase actor-name lookup matches");
			Assert.That(set.Contains("f16"), Is.True);
			Assert.That(set.Count, Is.EqualTo(2), "no entries dropped");
		}

		[Test]
		public void Dictionary_UppercaseKey_ResolvesUnderLowercaseActorNameAfterNormalize()
		{
			// The confirmed UnitsToBuild-shaped defect: uppercase key => airframe UNBUILDABLE pre-fix.
			var dict = new Dictionary<string, int> { { "HELI", 80 }, { "TRAN", 15 } };
			Assert.That(dict.ContainsKey("heli"), Is.False, "precondition: ordinal dict misses the lowercase actor name");

			ActorNameCase.NormalizeKeysInPlace(dict);

			Assert.That(dict.ContainsKey("heli"), Is.True, "post-normalize: lowercase actor-name key resolves");
			Assert.That(dict["heli"], Is.EqualTo(80), "value preserved under the lowercased key");
			Assert.That(dict["tran"], Is.EqualTo(15));
			Assert.That(dict.Count, Is.EqualTo(2), "no entries dropped");
		}

		[Test]
		public void AllLowercase_IsUnchanged_AndIdempotent()
		{
			// Every ww3mod flagged field is all-lowercase today, so normalization must be a no-op there.
			var set = new HashSet<string> { "a10", "f16" };
			var dict = new Dictionary<string, int> { { "heli", 80 }, { "littlebird", 40 } };

			ActorNameCase.NormalizeInPlace(set);
			ActorNameCase.NormalizeKeysInPlace(dict);

			Assert.That(set.SetEquals(new HashSet<string> { "a10", "f16" }), Is.True, "lowercase set unchanged");
			Assert.That(dict["heli"], Is.EqualTo(80));
			Assert.That(dict["littlebird"], Is.EqualTo(40));
			Assert.That(dict.Count, Is.EqualTo(2));

			// A second pass is a no-op (idempotent) — safe even if RulesetLoaded runs more than once.
			ActorNameCase.NormalizeInPlace(set);
			ActorNameCase.NormalizeKeysInPlace(dict);
			Assert.That(set.SetEquals(new HashSet<string> { "a10", "f16" }), Is.True);
			Assert.That(dict.Count, Is.EqualTo(2));
		}

		[Test]
		public void NullAndEmpty_AreSafe()
		{
			// Unset dicts default to null (UnitsToBuild/BuildingLimits/...); empty sets are the inert-latent case.
			ActorNameCase.NormalizeInPlace(null);
			ActorNameCase.NormalizeKeysInPlace(null);
			var empty = new HashSet<string>();
			ActorNameCase.NormalizeInPlace(empty);
			Assert.That(empty.Count, Is.EqualTo(0));
		}
	}
}
