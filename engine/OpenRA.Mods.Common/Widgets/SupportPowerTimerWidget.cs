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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets
{
	public class SupportPowerTimerWidget : Widget
	{
		[FluentReference("player", "support-power", "time")]
		const string Format = "support-power-timer";

		public readonly string Font = "Bold";
		public readonly TextAlign Align = TextAlign.Left;
		public readonly TimerOrder Order = TimerOrder.Descending;

		readonly SpriteFont font;
		readonly IEnumerable<SupportPowerInstance> powers;
		readonly Color bgDark, bgLight;
		(string Text, Color Color)[] texts;

		/// <summary>
		/// The powers this widget is willing to draw a timer line for, before the per-viewer
		/// relationship filter in <see cref="VisibleTo"/> is applied. Lazy: it is re-enumerated
		/// every Tick so a power that is enabled or disabled mid-match is picked up.
		/// </summary>
		public static IEnumerable<SupportPowerInstance> Candidates(World world)
		{
			return world.ActorsWithTrait<SupportPowerManager>()
				.Where(p => !p.Actor.IsDead && !p.Actor.Owner.NonCombatant)
				.SelectMany(s => s.Trait.Powers.Values)
				.Where(p => p.Instances.Count > 0 && p.Info.DisplayTimerRelationships != PlayerRelationship.None && !p.Disabled);
		}

		/// <summary>
		/// Whether <paramref name="power"/> draws a timer line for <paramref name="viewer"/>.
		/// Extracted so Test.GetSupportPowerTimerLines asks the widget's own question rather than
		/// keeping a second copy of it in step by hand — a DisplayTimerRelationships change is
		/// otherwise unobservable from a scenario, since the widget itself renders no state the
		/// Lua API can read.
		/// </summary>
		public static bool VisibleTo(SupportPowerInstance power, Player viewer)
		{
			var owner = power.Instances[0].Self.Owner;
			return viewer == null || power.Info.DisplayTimerRelationships.HasRelationship(owner.RelationshipWith(viewer));
		}

		[ObjectCreator.UseCtor]
		public SupportPowerTimerWidget(World world)
		{
			powers = Candidates(world);

			bgDark = ChromeMetrics.Get<Color>("TextContrastColorDark");
			bgLight = ChromeMetrics.Get<Color>("TextContrastColorLight");
			font = Game.Renderer.Fonts[Font];
		}

		public override void Tick()
		{
			var displayedPowers = powers.Where(p =>
			{
				var world = p.Instances[0].Self.World;
				return VisibleTo(p, world.RenderPlayer ?? world.LocalPlayer);
			});

			texts = displayedPowers.Select(p =>
			{
				var self = p.Instances[0].Self;
				var time = WidgetUtils.FormatTime(p.RemainingTicks, false, self.World.Timestep);
				var text = FluentProvider.GetMessage(Format,
					"player", self.Owner.ResolvedPlayerName,
					"support-power", p.Name,
					"time", time);

				var color = !p.Ready || Game.LocalTick % 50 < 25 ? self.OwnerColor() : Color.White;

				return (text, color);
			}).ToArray();
		}

		public override void Draw()
		{
			if (!IsVisible() || texts == null)
				return;

			var y = 0;
			foreach (var t in texts)
			{
				var textSize = font.Measure(t.Text);
				var location = new float2(Bounds.X, Bounds.Y + y);

				if (Align == TextAlign.Center)
					location += new int2((Bounds.Width - textSize.X) / 2, 0);

				if (Align == TextAlign.Right)
					location += new int2(Bounds.Width - textSize.X, 0);

				font.DrawTextWithShadow(t.Text, location, t.Color, bgDark, bgLight, 1);
				y += (font.Measure(t.Text).Y + 5) * (int)Order;
			}
		}

		public enum TimerOrder { Ascending = -1, Descending = 1 }
	}
}
