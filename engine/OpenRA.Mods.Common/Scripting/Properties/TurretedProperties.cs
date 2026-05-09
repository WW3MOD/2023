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

using System.Linq;
using Eluant;
using OpenRA.Mods.Common.Traits;
using OpenRA.Scripting;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Scripting
{
	[ScriptPropertyGroup("Turret")]
	public class TurretedProperties : ScriptActorProperties, Requires<TurretedInfo>
	{
		readonly Turreted[] turrets;

		public TurretedProperties(ScriptContext context, Actor self)
			: base(context, self)
		{
			turrets = self.TraitsImplementing<Turreted>().ToArray();
		}

		[Desc("Returns the local turret facing in WAngle units (0–1023, 0 = aligned with body forward).")]
		public int TurretFacing(string turretName = "primary")
		{
			var t = turrets.FirstOrDefault(x => x.Info.Turret == turretName);
			if (t == null)
				throw new LuaException($"Invalid turret name {turretName} on {Self}.");

			return t.LocalOrientation.Yaw.Angle;
		}
	}
}
