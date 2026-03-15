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
	class NextBurstBarInfo : TraitInfo
	{
		[Desc("Armament names")]
		public readonly string[] Armaments = { "primary", "secondary" };

		public readonly Color Color = Color.Red;

		public override object Create(ActorInitializer init) { return new NextBurstBar(init.Self, this); }
	}

	class NextBurstBar : ISelectionBar, INotifyCreated
	{
		readonly NextBurstBarInfo info;
		readonly Actor self;
		IEnumerable<Armament> armaments;

		public NextBurstBar(Actor self, NextBurstBarInfo info)
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

			if (armaments.Any(a => !a.AmmoPool.HasAmmo))
				return 0;

			return armaments.Min(a =>
				a.Weapon.ReloadDelay > 0 && a.ReloadDelay > a.BurstWait
				? a.ReloadDelay / (float)a.Weapon.ReloadDelay
				: a.BurstWait / (a.IsBurstWait
					? a.Weapon.BurstWait
					: a.Weapon.BurstDelays.Length == 1
						? a.BurstWait / a.Weapon.BurstDelays[0]
						: a.BurstWait / (float)a.Weapon.BurstDelays[a.Weapon.Burst - (a.Weapon.Burst + 1)]));
		}

		Color ISelectionBar.GetColor() { return info.Color; }
		bool ISelectionBar.DisplayWhenEmpty => false;
	}
}
