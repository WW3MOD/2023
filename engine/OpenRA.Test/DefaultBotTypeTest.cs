#region Copyright & License Information
/*
 * WW3MOD default bot-type selection tests.
 *
 * Every one-click "add a bot" path used to roll Game.CosmeticRandom across the shipped bot types
 * (upstream OpenRA #18914), so a player could not tell which opponent they had just added — and the
 * lobby buttons disagreed with SkirmishLogic, which had always deliberately seeded the frozen,
 * benchmark-validated profile. Four call sites now share AIUtils.SelectDefaultBotType.
 *
 * The load-bearing property is that the choice is DETERMINISTIC and lands on the benchmark-validated
 * profile whenever it ships. These tests exist because the four call sites are UI/server paths that
 * cannot be exercised without launching the game, so the shared predicate is the only thing that can
 * be pinned directly.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common;

namespace OpenRA.Test
{
	[TestFixture]
	public class DefaultBotTypeTest
	{
		[Test]
		public void PrefersTheBenchmarkValidatedProfileRegardlessOfDeclarationOrder()
		{
			Assert.That(AIUtils.SelectDefaultBotType(new[] { "experimental", "stable" }), Is.EqualTo("stable"));

			// The rules file declares @experimental first. Order must not decide the default,
			// otherwise reordering two YAML blocks silently changes who the player faces.
			Assert.That(AIUtils.SelectDefaultBotType(new[] { "stable", "experimental" }), Is.EqualTo("stable"));
		}

		[Test]
		public void RepeatedCallsAgree()
		{
			var types = new[] { "experimental", "stable" };
			var first = AIUtils.SelectDefaultBotType(types);

			// The bug was that filling four slots could yield four different opponents.
			for (var i = 0; i < 32; i++)
				Assert.That(AIUtils.SelectDefaultBotType(types), Is.EqualTo(first),
					"a one-click add must not vary between slots or between clicks");
		}

		[Test]
		public void FallsBackToTheFirstAvailableWhenTheDefaultIsAbsent()
		{
			// A map or mod may ship without the default profile; picking nothing would leave the
			// slot empty and make the button look broken.
			Assert.That(AIUtils.SelectDefaultBotType(new[] { "experimental" }), Is.EqualTo("experimental"));
		}

		[Test]
		public void ReturnsNullWhenNoBotsShip()
		{
			// Callers guard on null / empty before issuing slot_bot; this pins that contract.
			Assert.That(AIUtils.SelectDefaultBotType(new string[0]), Is.Null);
		}
	}
}
