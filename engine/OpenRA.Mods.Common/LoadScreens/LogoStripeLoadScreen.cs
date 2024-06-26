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
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;

namespace OpenRA.Mods.Common.LoadScreens
{
	public sealed class LogoStripeLoadScreen : SheetLoadScreen
	{
		Rectangle stripeRectLeft;
		Rectangle stripeRectRight;
		float2 logoPos;
		Sprite stripeLeft, stripeRight, logo;

		Sheet lastSheet;
		int lastDensity;
		Size lastResolution;

		string[] messages = { "Loading..." };

		public override void Init(ModData modData, Dictionary<string, string> info)
		{
			base.Init(modData, info);

			if (info.ContainsKey("Text"))
				messages = info["Text"].Split(',');
		}

		public override void DisplayInner(Renderer r, Sheet s, int density)
		{
			if (s != lastSheet || density != lastDensity)
			{
				lastSheet = s;
				lastDensity = density;
				logo = CreateSprite(s, density, new Rectangle(0, 0, 256, 256));
				stripeLeft = CreateSprite(s, density, new Rectangle(256, 0, 128, 256));
				stripeRight = CreateSprite(s, density, new Rectangle(512, 0, 128, 256));
			}

			if (r.Resolution != lastResolution)
			{
				lastResolution = r.Resolution;
				stripeRectLeft = new Rectangle(0, lastResolution.Height / 2 - 128, lastResolution.Width / 2, 256);
				stripeRectRight = new Rectangle(lastResolution.Width / 2, lastResolution.Height / 2 - 128, lastResolution.Width / 2, 256);
				logoPos = new float2(lastResolution.Width / 2 - 128, lastResolution.Height / 2 - 128);
			}

			WidgetUtils.FillRectWithSprite(stripeRectLeft, stripeLeft);
			WidgetUtils.FillRectWithSprite(stripeRectRight, stripeRight);

			if (logo != null)
				r.RgbaSpriteRenderer.DrawSprite(logo, logoPos);

			if (r.Fonts != null)
			{
				var text = messages.Random(Game.CosmeticRandom);
				var textSize = r.Fonts["Bold"].Measure(text);
				r.Fonts["Bold"].DrawText(text, new float2(r.Resolution.Width - textSize.X - 20, r.Resolution.Height - textSize.Y - 20), Color.White);
			}
		}
	}
}
