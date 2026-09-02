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

namespace OpenRA.Test
{
	/// <summary>
	/// Calls the production helpers directly rather than mirroring them (contrast
	/// <see cref="CustomSellValueTest"/>, which reproduces the formula and therefore cannot notice it
	/// changing). What is pinned here is the DRIFT CONTRACT: the number the Evacuate button shows and the
	/// cash RotateToEdge.DoSell pays go through the same <see cref="EvacRefundPreviewMath.ScaleByHealth"/>,
	/// so the button cannot promise an amount the game does not honour.
	/// </summary>
	[TestFixture]
	public class EvacRefundPreviewMathTest
	{
		// --- The health term: the one factor read on arrival rather than frozen at order time ---

		[Test]
		public void UndamagedUnitIsPaidTheWholeFrozenRefund()
		{
			Assert.That(EvacRefundPreviewMath.ScaleByHealth(1500, 4000, 4000), Is.EqualTo(1500));
		}

		[Test]
		public void DamagedUnitIsWorthLessThanItsSellValue()
		{
			// THE REGRESSION THIS FILE EXISTS FOR. GetEvacuationRefund carries no health term at all, so a
			// preview built on it alone would show 1500 for a tank the game will pay 450 for. Not drift on
			// the walk home — wrong at the instant of the press.
			Assert.That(EvacRefundPreviewMath.ScaleByHealth(1500, 1200, 4000), Is.EqualTo(450));
			Assert.That(EvacRefundPreviewMath.ScaleByHealth(1500, 1200, 4000), Is.Not.EqualTo(1500));
		}

		[Test]
		public void ScalingFloorsAndNeverOverpromises()
		{
			// 1000 x 1/3 = 333.33 -> 333. Truncation must go DOWN: the shown figure may under-promise by a
			// credit, never over-promise by one.
			Assert.That(EvacRefundPreviewMath.ScaleByHealth(1000, 1, 3), Is.EqualTo(333));
		}

		[Test]
		public void AWreckStillReturnsAnAnswerRatherThanNothing()
		{
			// Rounds to zero; DoSell draws "+$0" unconditionally for exactly this case, so the preview must
			// agree that the answer is 0 rather than something non-zero.
			Assert.That(EvacRefundPreviewMath.ScaleByHealth(10, 1, 100), Is.EqualTo(0));
		}

		[Test]
		public void LargeRefundsDoNotOverflowIntoNonsense()
		{
			// Guards the long promotion DoSell had inline. Computed in int arithmetic, 1500000 x 2000
			// wraps past int.MaxValue and this comes out large and NEGATIVE.
			Assert.That(EvacRefundPreviewMath.ScaleByHealth(1_500_000, 2_000, 4_000), Is.EqualTo(750_000));
		}

		// --- Aggregation and mixed selection ---

		[Test]
		public void LoneUnitGetsABareFigure()
		{
			Assert.That(EvacRefundPreviewMath.FormatRefundLine(450, 1, 1),
				Is.EqualTo("Refund at current value: $450"));
		}

		[Test]
		public void HomogeneousSelectionSumsAndStatesTheCount()
		{
			Assert.That(EvacRefundPreviewMath.FormatRefundLine(4120, 3, 3),
				Is.EqualTo("Refund at current value: $4120 (3 units)"));
		}

		[Test]
		public void MixedSelectionSaysHowManyUnitsTheFigureCovers()
		{
			// The button enables on ANY evacuable actor in the selection and the order is broadcast to all
			// of them, so a bare total would silently cover fewer units than are highlighted.
			Assert.That(EvacRefundPreviewMath.FormatRefundLine(4120, 3, 5),
				Is.EqualTo("Refund at current value: $4120 (3 of 5 selected)"));
		}

		[Test]
		public void NothingEvacuableProducesNoLineAtAll()
		{
			Assert.That(EvacRefundPreviewMath.FormatRefundLine(0, 0, 4), Is.Null);
		}
	}
}
