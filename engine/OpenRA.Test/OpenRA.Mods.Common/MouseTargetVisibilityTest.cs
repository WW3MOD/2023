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
using OpenRA.Mods.Common;

namespace OpenRA.Test
{
	/// <summary>
	/// MouseTargetVisibility.IsRevealed decides whether a right-click or a selection box may pick an
	/// actor up. It is the reason a radar-only helicopter rendered on screen and then refused the
	/// attack order: IDefaultVisibility said "yes, radar holds it", and a second, narrower cell-fog
	/// check said "no, that cell is fogged" — which it always is for a radar contact, because radar
	/// increments its own MapLayers counter and contributes nothing to ResolvedVisibility.
	///
	/// The cases below pin both halves. The positive rungs are not the load-bearing ones: making
	/// everything clickable passes every one of them. The NEGATIVE rungs are what constrain the fix —
	/// an actor nothing has revealed must stay unclickable, and radar cover must not rescue an actor
	/// that IDefaultVisibility has already refused.
	/// </summary>
	[TestFixture]
	public class MouseTargetVisibilityTest
	{
		[Test]
		public void RadarOnlyContactIsClickable()
		{
			// The reported bug: helicopter held on radar, over ground with no vision on it.
			Assert.That(
				MouseTargetVisibility.IsRevealed(
					actorIsVisible: true, isFrozenUnderFog: false, positionIsUnfogged: false, isRadarDetected: true),
				Is.True,
				"An actor the player holds on radar must be targetable and selectable. Radar contributes " +
				"nothing to ResolvedVisibility, so its cell is always fogged and the cell test alone " +
				"refuses every radar contact — which renders the unit but will not let you order an " +
				"attack on it.");
		}

		[Test]
		public void UnrevealedActorIsNotClickable()
		{
			// The rung that stops "make everything clickable" from passing this fixture.
			Assert.That(
				MouseTargetVisibility.IsRevealed(
					actorIsVisible: false, isFrozenUnderFog: false, positionIsUnfogged: false, isRadarDetected: false),
				Is.False,
				"An actor the player has not revealed by any means must not be clickable.");
		}

		[Test]
		public void RadarCoverDoesNotOverrideDefaultVisibility()
		{
			// IDefaultVisibility stays the authority. Radar cover over a cell is not by itself knowledge
			// of what stands on it: a unit with Detectable.Radar = 0, or an aircraft that has landed and
			// lost its `airborne` condition, is refused here and must stay refused.
			Assert.That(
				MouseTargetVisibility.IsRevealed(
					actorIsVisible: false, isFrozenUnderFog: false, positionIsUnfogged: false, isRadarDetected: true),
				Is.False,
				"Radar cover must not make an actor clickable that IDefaultVisibility has already refused. " +
				"If this fails, radar coverage has become a wallhack over everything standing under it.");
		}

		[Test]
		public void PositionFogStillVetoesAnActorWithNoOtherChannel()
		{
			// 8db9da9e's defence-in-depth check, whose original bug was never reproduced. It keeps its
			// veto over everything that has not earned a named exemption.
			Assert.That(
				MouseTargetVisibility.IsRevealed(
					actorIsVisible: true, isFrozenUnderFog: false, positionIsUnfogged: false, isRadarDetected: false),
				Is.False,
				"The cell-fog veto must still apply to an actor with no exemption. If this fails the veto " +
				"has been deleted rather than narrowed, and the through-fog targeting bug 8db9da9e was " +
				"added for is unguarded again.");
		}

		[Test]
		public void FrozenUnderFogActorIsClickable()
		{
			// 22a1ec34: buildings in fog, so the TECN capture cursor appears.
			Assert.That(
				MouseTargetVisibility.IsRevealed(
					actorIsVisible: true, isFrozenUnderFog: true, positionIsUnfogged: false, isRadarDetected: false),
				Is.True,
				"A FrozenUnderFog actor must stay clickable in fog — this is what makes the capture cursor " +
				"appear on a building the player has scouted.");
		}

		[Test]
		public void PlainlyVisibleActorIsClickable()
		{
			Assert.That(
				MouseTargetVisibility.IsRevealed(
					actorIsVisible: true, isFrozenUnderFog: false, positionIsUnfogged: true, isRadarDetected: false),
				Is.True,
				"An actor in plain sight must be clickable.");
		}
	}
}
