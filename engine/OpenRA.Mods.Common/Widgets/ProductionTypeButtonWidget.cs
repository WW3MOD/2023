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

using System;
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets
{
	public class ProductionTypeButtonWidget : WorldButtonWidget
	{
		public readonly string ProductionGroup;
		public readonly string RepeatSymbolsFont = "Symbols";
		public readonly string RepeatSymbolsFallbackFont = "TinyBold";

		public new Action OnRightClick = () => { };
		public Action OnMiddleClick = () => { };
		public Func<bool> RepeatModeActive = () => false;

		SpriteFont symbolFont;

		[ObjectCreator.UseCtor]
		public ProductionTypeButtonWidget(ModData modData, World world)
			: base(modData, world)
		{
			if (!Game.Renderer.Fonts.TryGetValue(RepeatSymbolsFont, out symbolFont))
				Game.Renderer.Fonts.TryGetValue(RepeatSymbolsFallbackFont, out symbolFont);
		}

		protected ProductionTypeButtonWidget(ProductionTypeButtonWidget other)
			: base(other)
		{
			ProductionGroup = other.ProductionGroup;
			OnRightClick = other.OnRightClick;
			OnMiddleClick = other.OnMiddleClick;
			RepeatModeActive = other.RepeatModeActive;
			symbolFont = other.symbolFont;
		}

		public override void MouseEntered()
		{
			if (TooltipContainer == null || GetTooltipText() == null)
				return;

			// Must set anchor AFTER base.MouseEntered because SetTooltip
			// calls RemoveTooltip which clears AnchorBounds.
			base.MouseEntered();
			tooltipContainer.Value.AnchorBounds = RenderBounds;
		}

		public override void MouseExited()
		{
			if (TooltipContainer != null && tooltipContainer.IsValueCreated)
				tooltipContainer.Value.AnchorBounds = null;

			base.MouseExited();
		}

		public override void Draw()
		{
			base.Draw();

			if (RepeatModeActive())
			{
				// Lime stripe down the LEFT edge of the tab — this category has queue-wide auto-build
				// engaged. Drawn as a primitive so it never depends on a missing font glyph.
				var rb = RenderBounds;
				var stripe = new Rectangle(rb.X, rb.Y, 3, rb.Height);
				WidgetUtils.FillRectWithColor(stripe, Color.LimeGreen);
			}
		}

		public override bool HandleMouseInput(MouseInput mi)
		{
			if (mi.Button == MouseButton.Left)
			{
				// Alt+left-click is handled by OnMouseUp for repeat mode toggle
				// Pass through to base which fires OnMouseUp with modifiers
				return base.HandleMouseInput(mi);
			}

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

		public override Widget Clone() { return new ProductionTypeButtonWidget(this); }
	}
}
