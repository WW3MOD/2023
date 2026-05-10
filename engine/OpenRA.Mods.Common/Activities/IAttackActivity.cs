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

using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	// Marker for attack activities so order-restoring logic (e.g. GroupScatterHotkeyLogic) can read
	// the target without depending on TargetLineNodes/GetTargets, which the activity classes don't
	// reliably override. Source distinguishes user-issued attacks (Default) from automatic
	// engagements (AutoTarget, AttackMove) so spread/redistribute logic can ignore the latter.
	public interface IAttackActivity
	{
		Target Target { get; }
		bool ForceAttack { get; }
		AttackSource Source { get; }
	}
}
