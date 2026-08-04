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
	/// Contract for SupplyProvider.DecideServe — the per-tick rule for what a provider may do with
	/// the target it is holding. Two live paths read it and must not disagree: ResupplyTarget (may
	/// I hand over a batch?) and SyncTargetCondition (should the RearmCondition be granted?).
	///
	/// Both halves matter. Delivery is the obvious channel; the condition is the quiet one, because
	/// it enables the target's own ReloadAmmoPool trickle, which carries no range check of its own —
	/// so a condition left granted on an out-of-aura target is a free refill at unlimited range that
	/// costs the provider nothing. Deleting either half of the rule breaks a test here.
	/// </summary>
	[TestFixture]
	public class SupplyServeDecisionTest
	{
		[Test]
		public void InAuraTargetIsServedAndHoldsTheCondition()
		{
			var d = SupplyProvider.DecideServe(targetInWorld: true, inAura: true);

			Assert.That(d.Deliver, Is.True);
			Assert.That(d.HoldCondition, Is.True);
			Assert.That(d.KeepTarget, Is.True);
		}

		[Test]
		public void OutOfAuraTargetGetsNeitherAmmoNorCondition()
		{
			// The whole point of the fix. Closing only the ammo channel leaves the condition open,
			// and the target self-reloads from across the map for free.
			var d = SupplyProvider.DecideServe(targetInWorld: true, inAura: false);

			Assert.That(d.Deliver, Is.False);
			Assert.That(d.HoldCondition, Is.False);
		}

		[Test]
		public void OutOfAuraTargetIsNONETHELESSKept()
		{
			// An approaching provider must still serve on arrival, so refusing to serve must not
			// drop the target — otherwise the Hunt drive-toward re-picks every scan and thrashes.
			var d = SupplyProvider.DecideServe(targetInWorld: true, inAura: false);

			Assert.That(d.KeepTarget, Is.True);
		}

		[Test]
		public void ReEntryReGrantsTheCondition()
		{
			// The trap the fix exists to avoid: SetTarget early-returns on an unchanged target, so
			// a target that leaves and re-enters the aura would never be re-evaluated if the grant
			// lived on the target-change edge. The decision is a pure function of current position,
			// so leaving and returning restores the condition.
			var away = SupplyProvider.DecideServe(targetInWorld: true, inAura: false);
			var back = SupplyProvider.DecideServe(targetInWorld: true, inAura: true);

			Assert.That(away.HoldCondition, Is.False);
			Assert.That(back.HoldCondition, Is.True);
		}

		[Test]
		public void ShelteredGarrisonPassengerIsServedWithoutTheCondition()
		{
			// Removed from the world with a stale CenterPosition, so the aura test is meaningless
			// for them — their building was in range when they were picked. They get ammo, but never
			// the condition: it would be invisible, and would leak if the soldier later deployed out.
			// inAura is passed both ways to show it does not enter into it.
			foreach (var inAura in new[] { true, false })
			{
				var d = SupplyProvider.DecideServe(targetInWorld: false, inAura: inAura);

				Assert.That(d.Deliver, Is.True, $"inAura={inAura}");
				Assert.That(d.HoldCondition, Is.False, $"inAura={inAura}");
				Assert.That(d.KeepTarget, Is.True, $"inAura={inAura}");
			}
		}

		[Test]
		public void ConditionIsNeverHeldWithoutDelivery()
		{
			// The invariant tying the two channels together: the condition is a free refill, so it
			// must never outlive the right to deliver. Any future case that grants one without the
			// other breaks here.
			foreach (var inWorld in new[] { true, false })
			{
				foreach (var inAura in new[] { true, false })
				{
					var d = SupplyProvider.DecideServe(inWorld, inAura);
					if (d.HoldCondition)
						Assert.That(d.Deliver, Is.True, $"inWorld={inWorld} inAura={inAura}");
				}
			}
		}
	}
}
