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
using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Lint;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets
{
	public class SupportPowersWidget : Widget
	{
		[FluentReference]
		public string ReadyText = "";

		[FluentReference]
		public string HoldText = "";

		public readonly string OverlayFont = "TinyBold";

		[Desc("Font for the persistent bottom-of-cameo caption. Only ever used by a power that sets",
			"SupportPowerInfo.CameoCaption, so leaving this at the overlay font costs nothing.")]
		public readonly string CaptionFont = "TinyBold";

		[Desc("Pixels between the bottom of the caption and the bottom of the icon slot.")]
		public readonly int CaptionBottomMargin = 2;

		[Desc("Pixels reserved at each side. A caption wider than what is left is shortened, not clipped.")]
		public readonly int CaptionSideMargin = 1;

		public readonly Color CaptionColor = Color.White;
		public readonly Color CaptionContrastColor = Color.Black;

		[Desc("Solid band drawn behind the caption. Default fully transparent, which draws nothing.",
			"",
			"Set this when captioning a cameo that ALREADY has a caption baked into its art - which",
			"is every cameo the mod currently ships. The baked lettering occupies the same pixels the",
			"runtime caption wants, so without a band the two words overlap and neither is readable.",
			"With one, the runtime caption REPLACES the baked one and the wording becomes editable.")]
		public readonly Color CaptionBackgroundColor = Color.Transparent;

		[Desc("Pixels the caption background extends above and below the text's line box.")]
		public readonly int CaptionBackgroundPadding = 1;

		public readonly int2 IconSize = new(64, 48);
		public readonly int IconMargin = 10;
		public readonly int2 IconSpriteOffset = int2.Zero;

		public readonly string TooltipContainer;
		public readonly string TooltipTemplate = "SUPPORT_POWER_TOOLTIP";

		// Note: LinterHotkeyNames assumes that these are disabled by default
		public readonly string HotkeyPrefix = null;
		public readonly int HotkeyCount = 0;

		public readonly string ClockAnimation = "clock";
		public readonly string ClockSequence = "idle";
		public readonly string ClockPalette = "chrome";

		public readonly bool Horizontal = false;

		public int IconCount { get; private set; }
		public event Action<int, int> OnIconCountChanged = (a, b) => { };

		readonly ModData modData;
		readonly WorldRenderer worldRenderer;
		readonly SupportPowerManager spm;

		Animation icon;
		Animation clock;
		Dictionary<Rectangle, SupportPowerIcon> icons = new();

		public SupportPowerIcon TooltipIcon { get; private set; }
		public Func<SupportPowerIcon> GetTooltipIcon;
		readonly Lazy<TooltipContainerWidget> tooltipContainer;
		HotkeyReference[] hotkeys;

		Rectangle eventBounds;
		public override Rectangle EventBounds => eventBounds;
		SpriteFont overlayFont, captionFont;
		CameoCaptionCache captions;
		float2 iconOffset, holdOffset, readyOffset, timeOffset;

		[CustomLintableHotkeyNames]
		public static IEnumerable<string> LinterHotkeyNames(MiniYamlNode widgetNode, Action<string> emitError)
		{
			var prefix = "";
			var prefixNode = widgetNode.Value.NodeWithKeyOrDefault("HotkeyPrefix");
			if (prefixNode != null)
				prefix = prefixNode.Value.Value;

			var count = 0;
			var countNode = widgetNode.Value.NodeWithKeyOrDefault("HotkeyCount");
			if (countNode != null)
				count = FieldLoader.GetValue<int>("HotkeyCount", countNode.Value.Value);

			if (count == 0)
				return Array.Empty<string>();

			if (string.IsNullOrEmpty(prefix))
				emitError($"{widgetNode.Location} must define HotkeyPrefix if HotkeyCount > 0.");

			return Exts.MakeArray(count, i => prefix + (i + 1).ToStringInvariant("D2"));
		}

		[ObjectCreator.UseCtor]
		public SupportPowersWidget(ModData modData, World world, WorldRenderer worldRenderer)
		{
			this.modData = modData;
			this.worldRenderer = worldRenderer;
			GetTooltipIcon = () => TooltipIcon;
			spm = world.LocalPlayer.PlayerActor.Trait<SupportPowerManager>();
			tooltipContainer = Exts.Lazy(() =>
				Ui.Root.Get<TooltipContainerWidget>(TooltipContainer));
		}

		public override void Initialize(WidgetArgs args)
		{
			base.Initialize(args);

			hotkeys = Exts.MakeArray(HotkeyCount,
				i => modData.Hotkeys[HotkeyPrefix + (i + 1).ToStringInvariant("D2")]);

			overlayFont = Game.Renderer.Fonts[OverlayFont];
			captionFont = Game.Renderer.Fonts[CaptionFont];
			captions = new CameoCaptionCache(captionFont.Measure, ResolveCaption,
				IconSize.X, IconSize.Y, CaptionSideMargin, CaptionBottomMargin, CaptionBackgroundPadding);

			iconOffset = 0.5f * IconSize.ToFloat2() + IconSpriteOffset;

			HoldText = FluentProvider.GetMessage(HoldText);
			holdOffset = iconOffset - overlayFont.Measure(HoldText) / 2;
			ReadyText = FluentProvider.GetMessage(ReadyText);
			readyOffset = iconOffset - overlayFont.Measure(ReadyText) / 2;

			clock = new Animation(worldRenderer.World, ClockAnimation);
		}

		public class SupportPowerIcon
		{
			public SupportPowerInstance Power;
			public float2 Pos;
			public Sprite Sprite;
			public PaletteReference Palette;
			public PaletteReference IconClockPalette;
			public HotkeyReference Hotkey;
		}

		/// <summary>
		/// A caption may be a Fluent key or a literal. TryGetMessage rather than GetMessage because
		/// GetMessage delegates to the map bundle when one is loaded, and a caption is authored in
		/// mod rules where a plain "50 KT" must survive verbatim.
		/// </summary>
		static string ResolveCaption(string raw)
		{
			return FluentProvider.TryGetMessage(raw, out var message) ? message : raw;
		}

		public void RefreshIcons()
		{
			icons = new Dictionary<Rectangle, SupportPowerIcon>();
			var powers = spm.Powers.Values.Where(p => !p.Disabled)
				.OrderBy(p => p.Info.SupportPowerPaletteOrder);

			var oldIconCount = IconCount;
			IconCount = 0;

			var rb = RenderBounds;
			foreach (var p in powers)
			{
				Rectangle rect;
				if (Horizontal)
					rect = new Rectangle(rb.X + IconCount * (IconSize.X + IconMargin), rb.Y, IconSize.X, IconSize.Y);
				else
					rect = new Rectangle(rb.X, rb.Y + IconCount * (IconSize.Y + IconMargin), IconSize.X, IconSize.Y);

				icon = new Animation(worldRenderer.World, p.Info.IconImage);
				icon.Play(p.Info.Icon);

				var power = new SupportPowerIcon()
				{
					Power = p,
					Pos = new float2(rect.Location),
					Sprite = icon.Image,
					Palette = worldRenderer.Palette(p.Info.IconPalette),
					IconClockPalette = worldRenderer.Palette(ClockPalette),
					Hotkey = IconCount < HotkeyCount ? hotkeys[IconCount] : null,
				};

				icons.Add(rect, power);
				IconCount++;
			}

			eventBounds = icons.Keys.Union();

			if (oldIconCount != IconCount)
				OnIconCountChanged(oldIconCount, IconCount);
		}

		protected void ClickIcon(SupportPowerIcon clicked)
		{
			if (!clicked.Power.Active)
			{
				Game.Sound.PlayToPlayer(SoundType.UI, spm.Self.Owner, clicked.Power.Info.InsufficientPowerSound);
				Game.Sound.PlayNotification(spm.Self.World.Map.Rules, spm.Self.Owner, "Speech",
					clicked.Power.Info.InsufficientPowerSpeechNotification, spm.Self.Owner.Faction.InternalName);

				TextNotificationsManager.AddTransientLine(spm.Self.Owner, clicked.Power.Info.InsufficientPowerTextNotification);
			}
			else
				clicked.Power.Target();
		}

		public override bool HandleKeyPress(KeyInput e)
		{
			if (e.Event == KeyInputEvent.Down)
			{
				var a = icons.Values.FirstOrDefault(i => i.Hotkey != null && i.Hotkey.IsActivatedBy(e));

				if (a != null)
				{
					ClickIcon(a);
					return true;
				}
			}

			return false;
		}

		public override void Draw()
		{
			timeOffset = iconOffset - overlayFont.Measure(WidgetUtils.FormatTime(0, worldRenderer.World.Timestep)) / 2;

			// Icons
			Game.Renderer.EnableAntialiasingFilter();
			foreach (var p in icons.Values)
			{
				WidgetUtils.DrawSpriteCentered(p.Sprite, p.Palette, p.Pos + iconOffset);

				// Charge progress
				var sp = p.Power;
				clock.PlayFetchIndex(ClockSequence,
					() => sp.TotalTicks == 0 ? clock.CurrentSequence.Length - 1 : (sp.TotalTicks - sp.RemainingTicks)
					* (clock.CurrentSequence.Length - 1) / sp.TotalTicks);

				clock.Tick();
				WidgetUtils.DrawSpriteCentered(clock.Image, p.IconClockPalette, p.Pos + iconOffset);
			}

			Game.Renderer.DisableAntialiasingFilter();

			// Overlay
			foreach (var p in icons.Values)
			{
				// Persistent caption along the bottom edge, independent of the transient centre text
				// below it. This is the slot that lets three powers sharing one sprite name themselves.
				var caption = captions.Get(p.Power.Info.CameoCaption);
				if (caption != null)
				{
					if (CaptionBackgroundColor.A != 0)
						WidgetUtils.FillRectWithColor(
							new Rectangle(
								(int)p.Pos.X + caption.Background.X,
								(int)p.Pos.Y + caption.Background.Y,
								caption.Background.Width,
								caption.Background.Height),
							CaptionBackgroundColor);

					captionFont.DrawTextWithContrast(caption.Text,
						p.Pos + new float2(caption.Offset.X, caption.Offset.Y),
						CaptionColor, CaptionContrastColor, 1);
				}

				var customText = p.Power.IconOverlayTextOverride();
				if (customText != null)
				{
					var customOffset = iconOffset - overlayFont.Measure(customText) / 2;
					overlayFont.DrawTextWithContrast(customText,
						p.Pos + customOffset,
						Color.White, Color.Black, 1);
				}
				else if (p.Power.Ready)
					overlayFont.DrawTextWithContrast(ReadyText,
						p.Pos + readyOffset,
						Color.White, Color.Black, 1);
				else if (!p.Power.Active)
					overlayFont.DrawTextWithContrast(HoldText,
						p.Pos + holdOffset,
						Color.White, Color.Black, 1);
				else
					overlayFont.DrawTextWithContrast(WidgetUtils.FormatTime(p.Power.RemainingTicks, worldRenderer.World.Timestep),
						p.Pos + timeOffset,
						Color.White, Color.Black, 1);
			}
		}

		public override void Tick()
		{
			// TODO: Only do this when the powers have changed
			RefreshIcons();
		}

		public override void MouseEntered()
		{
			if (TooltipContainer == null)
				return;

			tooltipContainer.Value.SetTooltip(TooltipTemplate,
				new WidgetArgs() { { "world", worldRenderer.World }, { "player", spm.Self.Owner }, { "getTooltipIcon", GetTooltipIcon } });
		}

		public override void MouseExited()
		{
			if (TooltipContainer == null)
				return;

			tooltipContainer.Value.RemoveTooltip();
		}

		public override bool HandleMouseInput(MouseInput mi)
		{
			if (mi.Event == MouseInputEvent.Move)
			{
				var icon = icons.Where(i => i.Key.Contains(mi.Location))
					.Select(i => i.Value).FirstOrDefault();

				TooltipIcon = icon;
				return false;
			}

			if (mi.Event != MouseInputEvent.Down)
				return false;

			var clicked = icons.Where(i => i.Key.Contains(mi.Location))
				.Select(i => i.Value).FirstOrDefault();

			if (clicked != null)
				ClickIcon(clicked);

			return true;
		}
	}
}
