#region Copyright & License Information
/*
 * WW3MOD chrome substitution-symbol tests.
 *
 * VariableExpression.ParseSymbol resolves an unknown symbol to 0 and says nothing, so a plausible-looking
 * name in a widget's X/Y/Width/Height is not a crash, a warning or a visual glitch — it is a widget quietly
 * placed somewhere nobody can see it. That cost months of an invisible cargo panel (`X: WINDOW_RIGHT - 240`,
 * fixed at c9fdf334). These pin the two halves of the rule that replaces that silence: the symbols the
 * engine actually registers, and the two that exist for X and Y but not for Width and Height.
 */
#endregion

using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Lint;

namespace OpenRA.Test
{
	[TestFixture]
	public class ChromeSubstitutionSymbolTest
	{
		[TestCase("WINDOW_WIDTH")]
		[TestCase("WINDOW_HEIGHT")]
		[TestCase("PARENT_WIDTH")]
		[TestCase("PARENT_HEIGHT")]
		public void TheSymbolsWidgetInitializeAddsAreAcceptedEverywhere(string symbol)
		{
			Assert.That(CheckChromeIntegerExpressions.IsRegistered(symbol, true), Is.True);
			Assert.That(CheckChromeIntegerExpressions.IsRegistered(symbol, false), Is.True);
		}

		[TestCase("WIDTH")]
		[TestCase("HEIGHT")]
		public void TheWidgetsOwnSizeIsReadableFromXAndYButNotFromWidthAndHeight(string symbol)
		{
			// Widget.Initialize evaluates Width and Height BEFORE adding these, so using one inside a Width
			// or Height expression is the same silent zero as a misspelt name.
			Assert.That(CheckChromeIntegerExpressions.IsRegistered(symbol, true), Is.True);
			Assert.That(CheckChromeIntegerExpressions.IsRegistered(symbol, false), Is.False);
		}

		[TestCase("WINDOW_RIGHT")]
		[TestCase("WINDOW_BOTTOM")]
		[TestCase("PARENT_TOP")]
		[TestCase("PARENT_LEFT")]
		[TestCase("PARENT_RIGHT")]
		[TestCase("PARENT_BOTTOM")]
		public void PlausibleNamesThatNobodyRegistersAreRejected(string symbol)
		{
			// Every one of these reads as obviously correct in YAML. WINDOW_RIGHT/WINDOW_BOTTOM are the pair
			// that hid the cargo panel; PARENT_TOP is live in the HPF debug overlay today.
			Assert.That(CheckChromeIntegerExpressions.IsRegistered(symbol, true), Is.False);
			Assert.That(CheckChromeIntegerExpressions.IsRegistered(symbol, false), Is.False);
		}

		[Test]
		public void ADropDownPanelTemplateMaySeeTheButtonWidth()
		{
			Assert.That(CheckChromeIntegerExpressions.IsRegistered("DROPDOWN_WIDTH", false), Is.True);
		}

		[Test]
		public void TheErrorMessageCanNameTheSymbolsThatWouldHaveWorked()
		{
			Assert.That(CheckChromeIntegerExpressions.RegisteredSymbols(false), Does.Not.Contain("WIDTH"));
			Assert.That(CheckChromeIntegerExpressions.RegisteredSymbols(true).Contains("WIDTH"), Is.True);
		}
	}
}
