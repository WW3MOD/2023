#region Copyright & License Information
/*
 * WW3MOD ModifierOrderGeneratorMath tests — the gate on CommandBarLogic's held-modifier handler.
 *
 * The behaviour under test: entering an explicit input mode with a modifier still held must not have
 * that mode immediately taken away again. The reported case is the engineer's minefield selector,
 * which is opened BY a Ctrl+Alt click and was therefore destroyed by the next key event, before the
 * player had let go of Ctrl+Alt.
 *
 * These pins exist because the decision sits inside a widget key handler that needs a live Widget,
 * World and chrome tree to construct, so it cannot be exercised from a scripted path. They pin the
 * decision table ONLY. Whether CommandBarLogic is correctly wired to it is NOT covered here and is
 * not covered by an asserting scenario either: the trigger is a keyboard event arriving while
 * Ctrl+Alt are physically held, and Lua cannot synthesise one. That half is a human verdict —
 * tools/autotest/scenarios/test-minelayer-mode-survives-modifiers stages the gesture for it.
 *
 * THE SECOND HALF OF THIS FILE FAILS THE OBVIOUS WRONG FIX. "Don't clobber a mode that is already
 * up" reads like "only proceed when the generator is exactly the default UnitOrderGenerator", and
 * that is wrong: ForceModifiersOrderGenerator, AttackMoveOrderGenerator and GuardOrderGenerator all
 * DERIVE from UnitOrderGenerator and are the handler's own output. Protecting those from the handler
 * freezes whichever modifier mode you entered first — press Ctrl+Alt then release to Alt and
 * attack-move would never arm. Each has a pin below and each goes red under that fix.
 */
#endregion

using System;
using System.Reflection;
using NUnit.Framework;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class ModifierOrderGeneratorMathTest
	{
		// The instruction, in one assertion, against the REAL type from the bug report rather than a
		// stand-in. MinefieldOrderGenerator is a private nested class of Minelayer, so it is resolved
		// by reflection — and the lookup is asserted non-null first, because a rename would otherwise
		// turn this into a test that passes by checking nothing.
		[Test]
		public void TheMinefieldSelectorSurvivesTheHeldModifiers()
		{
			var minefieldOrderGenerator = typeof(Minelayer)
				.GetNestedType("MinefieldOrderGenerator", BindingFlags.NonPublic);

			Assert.That(minefieldOrderGenerator, Is.Not.Null,
				"Minelayer.MinefieldOrderGenerator was not found — this test pins nothing until the name is fixed");

			Assert.That(ModifierOrderGeneratorMath.AllowsModifierOverride(minefieldOrderGenerator), Is.False,
				"the minefield selector is opened by a Ctrl+Alt click, so it must outlive the held modifiers");
		}

		// Same rule, stated over the whole category rather than the one reported instance: a mode with
		// its own entry and exit is never the modifier handler's to take.
		[Test]
		public void ExplicitInputModesAreNeverReplaced()
		{
			Assert.Multiple(() =>
			{
				Assert.That(ModifierOrderGeneratorMath.AllowsModifierOverride(typeof(PatrolOrderGenerator)), Is.False);
				Assert.That(ModifierOrderGeneratorMath.AllowsModifierOverride(typeof(PlaceBuildingOrderGenerator)), Is.False);
				Assert.That(ModifierOrderGeneratorMath.AllowsModifierOverride(typeof(RepairOrderGenerator)), Is.False);
				Assert.That(ModifierOrderGeneratorMath.AllowsModifierOverride(typeof(BeaconOrderGenerator)), Is.False);
			});
		}

		// ---------- the cases the naive "only the default generator" fix would break ----------

		// The default click handler is what the modifier modes are a variation of, and the state the
		// player is in the overwhelming majority of the time.
		[Test]
		public void TheDefaultGeneratorIsAlwaysReplaceable()
		{
			Assert.That(ModifierOrderGeneratorMath.AllowsModifierOverride(typeof(UnitOrderGenerator)), Is.True);
		}

		// The handler's own output. Ctrl+Alt installs one of these on every key event while held, so
		// refusing to replace it would make the handler unable to refresh its own mode.
		[Test]
		public void TheHandlerMayReplaceItsOwnForceModifiersGenerator()
		{
			Assert.That(ModifierOrderGeneratorMath.AllowsModifierOverride(typeof(ForceModifiersOrderGenerator)), Is.True,
				"ForceModifiersOrderGenerator derives from UnitOrderGenerator and is this handler's own output");
		}

		// Releasing Ctrl while keeping Alt is a live transition from force-attack to attack-move; both
		// directions have to be allowed or the second gesture never arms.
		[Test]
		public void ModifierModesMaySwapBetweenEachOther()
		{
			Assert.Multiple(() =>
			{
				Assert.That(ModifierOrderGeneratorMath.AllowsModifierOverride(typeof(AttackMoveOrderGenerator)), Is.True);
				Assert.That(ModifierOrderGeneratorMath.AllowsModifierOverride(typeof(GuardOrderGenerator)), Is.True,
					"GuardOrderGenerator also derives from UnitOrderGenerator — it is ordinary click handling, not a mode");
			});
		}

		// There is no generator during teardown; the gate must be transparent rather than throw.
		[Test]
		public void NoGeneratorAtAllIsReplaceable()
		{
			Assert.That(ModifierOrderGeneratorMath.AllowsModifierOverride(null), Is.True);
		}
	}
}
