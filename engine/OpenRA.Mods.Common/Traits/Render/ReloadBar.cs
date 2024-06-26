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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.Render
{
	[Desc("Visualizes the minimum remaining time for reloading the armaments.")]
	class ReloadBarInfo : TraitInfo
	{
		[Desc("Armament names")]
		public readonly string[] Armaments = { "primary", "secondary" };

		public readonly Color Color = Color.Red;

		public override object Create(ActorInitializer init) { return new ReloadBar(init.Self, this); }
	}

	class ReloadBar : ISelectionBar, INotifyCreated
	{
		readonly ReloadBarInfo info;
		readonly Actor self;
		IEnumerable<Armament> armaments;

		public ReloadBar(Actor self, ReloadBarInfo info)
		{
			this.self = self;
			this.info = info;
		}

		void INotifyCreated.Created(Actor self)
		{
			// Name check can be cached but enabled check can't.
			armaments = self.TraitsImplementing<Armament>().Where(a => info.Armaments.Contains(a.Info.Name)).ToArray().Where(t => !t.IsTraitDisabled);
		}

		float ISelectionBar.GetValue()
		{
			if (!self.Owner.IsAlliedWith(self.World.RenderPlayer))
				return 0;

			return armaments.Min(a => a.ReloadDelay / (float)a.Weapon.ReloadDelay);
		}

		Color ISelectionBar.GetColor() { return info.Color; }
		bool ISelectionBar.DisplayWhenEmpty => false;
	}
}
