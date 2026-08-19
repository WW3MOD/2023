#region Copyright & License Information
/*
 * WW3MOD default bot-type selection tests.
 *
 * Every one-click "add a bot" path used to roll Game.CosmeticRandom across the shipped bot types
 * (upstream OpenRA #18914), so a player could not tell which opponent they had just added — and the
 * lobby buttons disagreed with SkirmishLogic. Four call sites now share AIUtils.SelectDefaultBotType.
 *
 * The load-bearing property is that the choice is DETERMINISTIC and lands on AIUtils.DefaultBotType
 * whenever that type ships. These tests exist because the four call sites are UI/server paths that
 * cannot be exercised without launching the game, so the shared predicate is the only thing that can
 * be pinned directly.
 *
 * USER RULING 2026-08-19: the default is "experimental", not "stable". The user tests this mod to
 * exercise the bot being actively improved. Benchmarks do not go through the lobby — they assign by
 * Type via map.yaml PlayerReferences and tournament.yaml — so this does not touch the control.
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
		public void PrefersTheUserChosenDefaultRegardlessOfDeclarationOrder()
		{
			Assert.That(AIUtils.SelectDefaultBotType(new[] { "experimental", "stable" }), Is.EqualTo("experimental"),
				"a one-click add must land on the user-chosen default bot type");

			// Order must not decide the default, otherwise reordering two YAML blocks silently
			// changes who the player faces. This is the load-bearing assertion in this fixture:
			// it fails for BOTH a wrong default and an order-dependent one.
			Assert.That(AIUtils.SelectDefaultBotType(new[] { "stable", "experimental" }), Is.EqualTo("experimental"),
				"declaration order in the rules file must not decide which bot a one-click add gives");
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
			//
			// The list here must NOT contain AIUtils.DefaultBotType, or this test stops exercising
			// the `?? botTypes.FirstOrDefault()` branch it is named for and passes vacuously.
			// That is exactly what happened on 2026-08-19: the list was ["experimental"] while the
			// default was flipped TO "experimental", and deleting the fallback failed nothing.
			Assert.That(AIUtils.DefaultBotType, Is.Not.EqualTo("stable"),
				"this test's list must exclude the default; update it if DefaultBotType changes");
			Assert.That(AIUtils.SelectDefaultBotType(new[] { "stable" }), Is.EqualTo("stable"));
		}

		[Test]
		public void ReturnsNullWhenNoBotsShip()
		{
			// Callers guard on null / empty before issuing slot_bot; this pins that contract.
			Assert.That(AIUtils.SelectDefaultBotType(new string[0]), Is.Null);
		}
	}
}
