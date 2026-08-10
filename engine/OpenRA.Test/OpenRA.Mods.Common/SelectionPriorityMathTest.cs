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
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// Contract for the SelectionPriorityModifier override key. The rule has to match
	/// SelectableExts.BaseSelectionPriority exactly: that method reads Ctrl and Alt EXCLUSIVELY (each
	/// branch requires the other key to be up), so a modifier whose suppression rule read them
	/// inclusively would re-include a deprioritised unit on a two-key press that raises nobody's base
	/// priority — the unit would be the only thing the box picked up.
	/// </summary>
	[TestFixture]
	public class SelectionPriorityMathTest
	{
		[Test]
		public void NoOverrideKeyMeansNeverSuppressed()
		{
			// The shipped default. Existing deprioritisations (evacuating units) keep behaving exactly
			// as before this field existed.
			Assert.That(SelectionPriorityMath.Suppressed(SelectionPriorityModifiers.None, Modifiers.Ctrl), Is.False);
			Assert.That(SelectionPriorityMath.Suppressed(SelectionPriorityModifiers.None, Modifiers.Alt), Is.False);
			Assert.That(SelectionPriorityMath.Suppressed(SelectionPriorityModifiers.None, Modifiers.None), Is.False);
		}

		[Test]
		public void HoldingTheNamedKeySuppresses()
		{
			Assert.That(SelectionPriorityMath.Suppressed(SelectionPriorityModifiers.Ctrl, Modifiers.Ctrl), Is.True);
			Assert.That(SelectionPriorityMath.Suppressed(SelectionPriorityModifiers.Alt, Modifiers.Alt), Is.True);
		}

		[Test]
		public void HoldingTheOtherKeyDoesNotSuppress()
		{
			Assert.That(SelectionPriorityMath.Suppressed(SelectionPriorityModifiers.Ctrl, Modifiers.Alt), Is.False);
			Assert.That(SelectionPriorityMath.Suppressed(SelectionPriorityModifiers.Alt, Modifiers.Ctrl), Is.False);
		}

		[Test]
		public void HoldingNothingDoesNotSuppress()
		{
			// The whole point of the feature: an ordinary box-drag leaves the dry men behind.
			Assert.That(SelectionPriorityMath.Suppressed(SelectionPriorityModifiers.Ctrl, Modifiers.None), Is.False);
			Assert.That(SelectionPriorityMath.Suppressed(SelectionPriorityModifiers.Ctrl, Modifiers.Shift), Is.False);
		}

		[Test]
		public void CtrlAndAltTogetherIsNeither()
		{
			// Mirrors BaseSelectionPriority's exclusive reading. Both keys down raises nobody to
			// int.MaxValue, so suppressing here would leave the dry unit as the HIGHEST priority actor
			// in the box and select it alone.
			Assert.That(SelectionPriorityMath.Suppressed(SelectionPriorityModifiers.Ctrl, Modifiers.Ctrl | Modifiers.Alt), Is.False);
			Assert.That(SelectionPriorityMath.Suppressed(SelectionPriorityModifiers.Alt, Modifiers.Ctrl | Modifiers.Alt), Is.False);
		}

		[Test]
		public void EitherKeySuppressesWhenBothAreNamed()
		{
			const SelectionPriorityModifiers Both = SelectionPriorityModifiers.Ctrl | SelectionPriorityModifiers.Alt;
			Assert.That(SelectionPriorityMath.Suppressed(Both, Modifiers.Ctrl), Is.True);
			Assert.That(SelectionPriorityMath.Suppressed(Both, Modifiers.Alt), Is.True);
			Assert.That(SelectionPriorityMath.Suppressed(Both, Modifiers.None), Is.False);
		}

		[Test]
		public void UnrelatedModifiersRideAlong()
		{
			// Shift is the add-to-selection modifier and is routinely held at the same time; it must not
			// break the Ctrl override.
			Assert.That(SelectionPriorityMath.Suppressed(SelectionPriorityModifiers.Ctrl, Modifiers.Ctrl | Modifiers.Shift), Is.True);
		}
	}
}
