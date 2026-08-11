#region Copyright & License Information
/*
 * WW3MOD SelectByTypeScope tests — Ctrl+Alt+LMB on a build-menu icon.
 *
 * These pins exist because the gesture needs a real mouse event on a real sidebar icon to reach, so
 * the branch choice cannot be exercised by hand without playing the game. Screenshots can show that
 * a selection happened; only these tests show it picked the right scope.
 *
 * Two properties are load-bearing. First, A CLICK MUST NEVER DESTROY A SELECTION IT CANNOT REPLACE:
 * clicking an icon for a type you own none of has to be inert, because silently deselecting the
 * army you had is worse than doing nothing. Second, THE GESTURE MUST FIND UNITS YOU CANNOT SEE —
 * that is the whole point of it — so a type with nothing on screen has to skip the screen step
 * rather than resolve to an empty selection and make the player click again.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Widgets;

namespace OpenRA.Test
{
	[TestFixture]
	public class SelectByTypeScopeTest
	{
		// The mis-click guard. Any regression that turns this into Screen or World hands back an
		// empty selection, which the widget would apply over whatever the player had selected.
		[Test]
		public void OwningNoneOfTheTypeIsANoOp()
		{
			Assert.That(SelectByTypeScopeMath.Resolve(0, 0, false), Is.EqualTo(SelectByTypeScope.None));
			Assert.That(SelectByTypeScopeMath.Resolve(0, 0, true), Is.EqualTo(SelectByTypeScope.None),
				"a repeat click on a type the player owns none of is still a no-op");
		}

		// The "locate" case: none visible, some out there. Escalating only on the repeat click would
		// make the first click select nothing at the exact moment the feature is most useful.
		[Test]
		public void NothingOnScreenGoesStraightToTheMap()
		{
			Assert.That(SelectByTypeScopeMath.Resolve(0, 5, false), Is.EqualTo(SelectByTypeScope.World));
		}

		[Test]
		public void FirstClickTakesWhatIsOnScreen()
		{
			Assert.That(SelectByTypeScopeMath.Resolve(3, 8, false), Is.EqualTo(SelectByTypeScope.Screen));
		}

		[Test]
		public void RepeatClickEscalatesToTheMap()
		{
			Assert.That(SelectByTypeScopeMath.Resolve(3, 8, true), Is.EqualTo(SelectByTypeScope.World));
		}

		// Holding the same count as the screen set by coincidence must not escalate — otherwise a
		// player who had an unrelated 3 units selected would skip the screen step entirely.
		[Test]
		public void ADifferentSelectionOfTheSameSizeDoesNotEscalate()
		{
			Assert.That(SelectByTypeScopeMath.Resolve(3, 8, false), Is.EqualTo(SelectByTypeScope.Screen));
		}

		// Everything the player owns is already visible, so there is nothing to widen to. Escalating
		// here would swap to an identical set and log a misleading "across the map" line.
		[Test]
		public void RepeatClickWithNothingOffScreenStaysOnScreen()
		{
			Assert.That(SelectByTypeScopeMath.Resolve(4, 4, true), Is.EqualTo(SelectByTypeScope.Screen));
		}

		[Test]
		public void SelectionClassFallsBackToTheActorName()
		{
			Assert.That(SelectByTypeScopeMath.ResolveSelectionClass("e1", null), Is.EqualTo("e1"));
			Assert.That(SelectByTypeScopeMath.ResolveSelectionClass("e1", ""), Is.EqualTo("e1"),
				"Selectable treats an unset Class as absent, and so must the icon lookup");
		}

		[Test]
		public void AConfiguredSelectionClassWins()
		{
			Assert.That(SelectByTypeScopeMath.ResolveSelectionClass("e1", "infantry"), Is.EqualTo("infantry"));
		}
	}
}
