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
using OpenRA.Graphics;
using OpenRA.Mods.Common.Lint;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic.Ingame
{
	[ChromeLogicArgsHotkeys("OrderResupplyKey")]
	public class OrderResupplyHotkeyLogic : SingleHotkeyBaseLogic
	{
		readonly World world;
		readonly ISelection selection;

		public readonly string ClickSound = ChromeMetrics.Get<string>("ClickSound");
		public readonly string ClickDisabledSound = ChromeMetrics.Get<string>("ClickDisabledSound");

		[ObjectCreator.UseCtor]
		public OrderResupplyHotkeyLogic(Widget widget, ModData modData, WorldRenderer worldRenderer, World world, Dictionary<string, MiniYaml> logicArgs)
			: base(widget, modData, "OrderResupplyKey", "WORLD_KEYHANDLER", logicArgs)
		{
			this.world = world;
			selection = world.Selection;
		}

		protected override bool OnHotkeyActivated(KeyInput e)
		{
			Game.Sound.PlayNotification(world.Map.Rules, world.LocalPlayer, "Sounds", ClickSound, null);

			if (world.IsGameOver)
				return false;

			var selectionToOrder = selection.Actors;

			foreach (var actor in selectionToOrder)
			{
				var ammoPools = actor.TraitsImplementing<AmmoPool>();
				if (ammoPools != null)
					// foreach (var ammoPool in ammoPools)
					for (int i = 0; i < ammoPools.Length; i++)
					{
						// ammoPool.CheckAndAutoRearm(actor);
						OpenRA.Mods.Common.Traits.AmmoPool.AutoRearm(actor);
					}
			}

			return true;
		}
	}
}
