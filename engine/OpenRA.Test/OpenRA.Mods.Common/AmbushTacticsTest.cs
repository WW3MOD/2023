#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
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
	/// Pins the pure Stage-2 halt-before-contact decision (PIPELINE item 8). The world-touching parts
	/// (GetConditionCount gate read + the group-detection scan) live in AttackMoveActivity; the
	/// combinator that turns those into "halt vs engage" is <see cref="AmbushTactics.ShouldHaltBeforeContact"/>
	/// so it can be exercised here with no simulation harness.
	///
	/// The load-bearing invariant for the ship's default-off / byte-identity contract is the FIRST test:
	/// with the gate OFF the decision is ALWAYS "engage" (false) regardless of the other inputs — that is
	/// exactly why @stable / control bots, and every un-opted-in unit, keep the stock attack-move path.
	///
	/// Source of truth: engine/OpenRA.Mods.Common/Traits/AmbushTactics.cs
	/// </summary>
	[TestFixture]
	public class AmbushTacticsTest
	{
		[Test]
		public void GateOffNeverHalts()
		{
			// The byte-identity guarantee: tacticsEnabled == false ⇒ false for EVERY combination of the
			// remaining inputs, so the original engage path always runs when the gate is not granted.
			foreach (var stance in new[] { UnitStance.HoldFire, UnitStance.Ambush, UnitStance.FireAtWill })
				foreach (var hasTarget in new[] { false, true })
					foreach (var detected in new[] { false, true })
						Assert.That(
							AmbushTactics.ShouldHaltBeforeContact(false, stance, hasTarget, detected),
							Is.False,
							$"gate off must never halt (stance={stance}, hasTarget={hasTarget}, detected={detected})");
		}

		[Test]
		public void OnlyAmbushStanceHalts()
		{
			// FireAtWill / HoldFire units never halt even with the gate on, a target present and unseen.
			Assert.That(AmbushTactics.ShouldHaltBeforeContact(true, UnitStance.FireAtWill, true, false), Is.False);
			Assert.That(AmbushTactics.ShouldHaltBeforeContact(true, UnitStance.HoldFire, true, false), Is.False);

			// Ambush + gate on + valid target + still unseen ⇒ the one combination that halts.
			Assert.That(AmbushTactics.ShouldHaltBeforeContact(true, UnitStance.Ambush, true, false), Is.True);
		}

		[Test]
		public void NoTargetNeverHalts()
		{
			Assert.That(AmbushTactics.ShouldHaltBeforeContact(true, UnitStance.Ambush, false, false), Is.False);
		}

		[Test]
		public void DetectedGroupEngagesInsteadOfHalting()
		{
			// Once the ambush is blown (a group member is visible to the enemy) the unit must NOT hold
			// fire from an exposed position — it falls through to the immediate engage path.
			Assert.That(AmbushTactics.ShouldHaltBeforeContact(true, UnitStance.Ambush, true, true), Is.False);
		}

		[Test]
		public void UndetectedAmbushWithTargetHalts()
		{
			// The positive case, stated on its own for clarity: gate on, Ambush, target present, group
			// still unseen ⇒ halt into the idle ambush.
			Assert.That(AmbushTactics.ShouldHaltBeforeContact(true, UnitStance.Ambush, true, false), Is.True);
		}
	}
}
