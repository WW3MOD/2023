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
	public class LabelWithTooltipWidget : LabelWidget
	{
		public readonly string TooltipTemplate;
		public readonly string TooltipContainer;
		public readonly bool AnchorTooltip;
		protected Lazy<TooltipContainerWidget> tooltipContainer;

		public Func<string> GetTooltipText = () => "";

		[ObjectCreator.UseCtor]
		public LabelWithTooltipWidget()
			: base()
		{
			tooltipContainer = Exts.Lazy(() =>
				Ui.Root.Get<TooltipContainerWidget>(TooltipContainer));
		}

		protected LabelWithTooltipWidget(LabelWithTooltipWidget other)
			: base(other)
		{
			TooltipTemplate = other.TooltipTemplate;
			TooltipContainer = other.TooltipContainer;
			AnchorTooltip = other.AnchorTooltip;

			tooltipContainer = Exts.Lazy(() =>
				Ui.Root.Get<TooltipContainerWidget>(TooltipContainer));

			GetTooltipText = other.GetTooltipText;
		}

		public override Widget Clone() { return new LabelWithTooltipWidget(this); }

		public override void MouseEntered()
		{
			if (TooltipContainer == null)
				return;

			if (GetTooltipText != null)
				tooltipContainer.Value.SetTooltip(TooltipTemplate, new WidgetArgs() { { "getText", GetTooltipText } });

			if (AnchorTooltip)
			{
				// Anchor to parent's bounds so the tooltip appears to the left of the
				// containing panel (e.g. sidebar) rather than overlapping it.
				var anchor = Parent != null ? Parent.RenderBounds : RenderBounds;
				tooltipContainer.Value.AnchorBounds = anchor;
			}
		}

		public override void MouseExited()
		{
			// Only try to remove the tooltip if we know it has been created
			// This avoids a crash if the widget (and the container it refers to) are being removed
			if (TooltipContainer != null && tooltipContainer.IsValueCreated)
			{
				tooltipContainer.Value.AnchorBounds = null;
				tooltipContainer.Value.RemoveTooltip();
			}
		}
	}
}
