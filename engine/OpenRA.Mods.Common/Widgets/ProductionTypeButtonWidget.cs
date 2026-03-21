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

using System;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets
{
	public class ProductionTypeButtonWidget : WorldButtonWidget
	{
		public readonly string ProductionGroup;

		public Action OnRightClick = () => { };
		public Action OnMiddleClick = () => { };

		[ObjectCreator.UseCtor]
		public ProductionTypeButtonWidget(ModData modData, World world)
			: base(modData, world) { }

		protected ProductionTypeButtonWidget(ProductionTypeButtonWidget other)
			: base(other)
		{
			ProductionGroup = other.ProductionGroup;
			OnRightClick = other.OnRightClick;
			OnMiddleClick = other.OnMiddleClick;
		}

		public override bool HandleMouseInput(MouseInput mi)
		{
			if (mi.Button == MouseButton.Left)
				return base.HandleMouseInput(mi);

			if (IsDisabled())
				return false;

			if (mi.Button == MouseButton.Right && mi.Event == MouseInputEvent.Up)
			{
				OnRightClick();
				return true;
			}

			if (mi.Button == MouseButton.Middle && mi.Event == MouseInputEvent.Up)
			{
				OnMiddleClick();
				return true;
			}

			// Consume right/middle down events so they don't pass through
			if ((mi.Button == MouseButton.Right || mi.Button == MouseButton.Middle) && mi.Event == MouseInputEvent.Down)
				return true;

			return false;
		}
	}
}
