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
	public class ImageWidget : Widget
	{
		public readonly string TooltipTemplate;
		public readonly string TooltipContainer;

		public string ImageCollection = "";
		public string ImageName = "";
		public bool ClickThrough = true;

		// WW3MOD: opt-in. Widget Width/Height are layout-only for images — the sprite
		// draws at native size unless this is set, which scales it uniformly
		// (aspect preserved) to fit the widget bounds, centered.
		public bool ScaleToBounds = false;
		public Func<string> GetImageName;
		public Func<string> GetImageCollection;
		public Func<Sprite> GetSprite;

		[FluentReference]
		public string TooltipText;

		readonly Lazy<TooltipContainerWidget> tooltipContainer;
		public Func<string> GetTooltipText;

		readonly CachedTransform<(string, string), Sprite> getImageCache = new(
			((string Collection, string Image) args) => ChromeProvider.GetImage(args.Collection, args.Image));

		public ImageWidget()
		{
			GetImageName = () => ImageName;
			GetImageCollection = () => ImageCollection;
			var tooltipCache = new CachedTransform<string, string>(s => !string.IsNullOrEmpty(s) ? FluentProvider.GetMessage(s) : "");
			GetTooltipText = () => tooltipCache.Update(TooltipText);
			tooltipContainer = Exts.Lazy(() =>
				Ui.Root.Get<TooltipContainerWidget>(TooltipContainer));

			GetSprite = () => getImageCache.Update((GetImageCollection(), GetImageName()));
		}

		protected ImageWidget(ImageWidget other)
			: base(other)
		{
			ClickThrough = other.ClickThrough;
			ScaleToBounds = other.ScaleToBounds;
			ImageName = other.ImageName;
			GetImageName = other.GetImageName;
			ImageCollection = other.ImageCollection;
			GetImageCollection = other.GetImageCollection;

			TooltipTemplate = other.TooltipTemplate;
			TooltipContainer = other.TooltipContainer;
			GetTooltipText = other.GetTooltipText;
			tooltipContainer = Exts.Lazy(() =>
				Ui.Root.Get<TooltipContainerWidget>(TooltipContainer));

			GetSprite = () => getImageCache.Update((GetImageCollection(), GetImageName()));
		}

		public override Widget Clone() { return new ImageWidget(this); }

		public override void Draw()
		{
			var sprite = GetSprite();
			if (ScaleToBounds)
			{
				var rb = RenderBounds;
				var scale = Math.Min(rb.Width / sprite.Size.X, rb.Height / sprite.Size.Y);
				var size = new float2(sprite.Size.X * scale, sprite.Size.Y * scale);
				var pos = new float2(rb.X + (rb.Width - size.X) / 2, rb.Y + (rb.Height - size.Y) / 2);
				WidgetUtils.DrawSprite(sprite, pos, size);
			}
			else
				WidgetUtils.DrawSprite(sprite, RenderOrigin);
		}

		public override bool HandleMouseInput(MouseInput mi)
		{
			return !ClickThrough && RenderBounds.Contains(mi.Location);
		}

		public override void MouseEntered()
		{
			if (TooltipContainer == null || GetTooltipText == null)
				return;

			tooltipContainer.Value.SetTooltip(TooltipTemplate, new WidgetArgs() { { "getText", GetTooltipText } });
		}

		public override void MouseExited()
		{
			if (TooltipContainer == null)
				return;

			tooltipContainer.Value.RemoveTooltip();
		}
	}
}
