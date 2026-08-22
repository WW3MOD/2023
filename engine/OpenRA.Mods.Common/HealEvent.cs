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

using OpenRA.Traits;

namespace OpenRA.Mods.Common
{
	/// <summary>
	/// The one place that answers "was this <see cref="INotifyDamage"/> event healing?".
	/// </summary>
	/// <remarks>
	/// Healing in this mod is negative damage: the medic's <c>Heal</c> weapon and the engineer's
	/// <c>Repair</c> weapon are ordinary <c>SpreadDamage</c> warheads with a negative
	/// <c>DamagePercent</c> (mods/ww3mod/rules/weapons/weapons-other.yaml:339 and :352), so treatment
	/// arrives through the same <see cref="INotifyDamage.Damaged"/> channel as being shot.
	///
	/// <para>Deliberately NOT keyed on <c>DamageTypes</c>. Both heal warheads leave that field empty,
	/// which is what makes the engine's own <c>GrantConditionOnHealingReceived</c> unusable here — its
	/// <c>DamageTypes</c> is <c>[FieldLoader.Require]</c> and can therefore never overlap an empty set.
	/// Testing the sign instead also picks up every non-weapon healer for free: the service-depot
	/// <c>RepairsUnits</c> tick has no warhead at all.</para>
	///
	/// <para>Extracted rather than inlined because two traits ask this question and must not drift:
	/// a copy that forgets healing is a NEGATIVE number reads as "damaged" and inverts silently.</para>
	/// </remarks>
	public static class HealEvent
	{
		public static bool IsHealing(AttackInfo e)
		{
			return e.Damage.Value < 0;
		}
	}
}
