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

using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.MiniMap;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class IngameMiniMapDisplayLogic : ChromeLogic
	{
		[ObjectCreator.UseCtor]
		public IngameMiniMapDisplayLogic(Widget widget, World world)
		{
			var minimapEnabled = false;
			var cachedMiniMapEnabled = false;
			var blockColor = Color.Transparent;
			var minimap = widget.Get<MiniMapWidget>("MINIMAP");
			minimap.IsEnabled = () => minimapEnabled;
			var devMode = world.LocalPlayer.PlayerActor.Trait<DeveloperMode>();

			var ticker = widget.Get<LogicTickerWidget>("MINIMAP_TICKER");
			ticker.OnTick = () =>
			{
				minimapEnabled = devMode.DisableShroud || world.ActorsHavingTrait<ProvidesMiniMap>(r => !r.IsTraitDisabled)
					.Any(a => a.Owner == world.LocalPlayer);

				if (minimapEnabled != cachedMiniMapEnabled)
					Game.Sound.PlayNotification(world.Map.Rules, null, "Sounds", minimapEnabled ? minimap.SoundUp : minimap.SoundDown, null);
				cachedMiniMapEnabled = minimapEnabled;
			};

			var block = widget.GetOrNull<ColorBlockWidget>("MINIMAP_FADETOBLACK");
			if (block != null)
			{
				minimap.Animating = x => blockColor = Color.FromArgb((int)(255 * x), Color.Black);
				block.IsVisible = () => blockColor.A != 0;
				block.GetColor = () => blockColor;
			}
		}
	}
}
