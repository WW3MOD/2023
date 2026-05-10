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
using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Lint;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Network;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets
{
	public class ProductionIcon
	{
		public ActorInfo Actor;
		public string Name;
		public HotkeyReference Hotkey;
		public Sprite Sprite;
		public PaletteReference Palette;
		public PaletteReference IconClockPalette;
		public PaletteReference IconDarkenPalette;
		public float2 Pos;
		public List<ProductionItem> Queued;
		public ProductionQueue ProductionQueue;
	}

	public class ProductionPaletteWidget : Widget
	{
		public enum ReadyTextStyleOptions { Solid, AlternatingColor, Blinking }
		public readonly ReadyTextStyleOptions ReadyTextStyle = ReadyTextStyleOptions.AlternatingColor;
		public readonly Color ReadyTextAltColor = Color.Gold;
		public readonly int Columns = 3;
		public readonly int2 IconSize = new int2(64, 48);
		public readonly int2 IconMargin = int2.Zero;
		public readonly int2 IconSpriteOffset = int2.Zero;

		public readonly string ClickSound = ChromeMetrics.Get<string>("ClickSound");
		public readonly string ClickDisabledSound = ChromeMetrics.Get<string>("ClickDisabledSound");
		public readonly string TooltipContainer;
		public readonly string TooltipTemplate = "PRODUCTION_TOOLTIP";

		// Note: LinterHotkeyNames assumes that these are disabled by default
		public readonly string HotkeyPrefix = null;
		public readonly int HotkeyCount = 0;
		public readonly HotkeyReference SelectProductionBuildingHotkey = new HotkeyReference();

		public readonly string ClockAnimation = "clock";
		public readonly string ClockSequence = "idle";
		public readonly string ClockPalette = "chrome";

		public readonly string NotBuildableAnimation = "clock";
		public readonly string NotBuildableSequence = "idle";
		public readonly string NotBuildablePalette = "chrome";

		public readonly string OverlayFont = "TinyBold";
		public readonly string SymbolsFont = "Symbols";

		public readonly bool DrawTime = true;

		public readonly string ReadyText = "";

		public readonly string HoldText = "";

		public readonly string InfiniteSymbol = "\u221E";

		public int DisplayedIconCount { get; private set; }
		public int TotalIconCount { get; private set; }
		public event Action<int, int> OnIconCountChanged = (a, b) => { };

		public ProductionIcon TooltipIcon { get; private set; }
		public Func<ProductionIcon> GetTooltipIcon;
		public readonly World World;
		readonly ModData modData;
		readonly OrderManager orderManager;

		public int MinimumRows = 4;
		public int MaximumRows = int.MaxValue;

		public int IconRowOffset = 0;
		public int MaxIconRowOffset = int.MaxValue;

		readonly Lazy<TooltipContainerWidget> tooltipContainer;
		ProductionQueue currentQueue;
		HotkeyReference[] hotkeys;

		public ProductionQueue CurrentQueue
		{
			get => currentQueue;
			set
			{
				currentQueue = value;
				if (currentQueue != null)
					UpdateCachedProductionIconOverlays();

				RefreshIcons();
			}
		}

		public override Rectangle EventBounds => eventBounds;
		Dictionary<Rectangle, ProductionIcon> icons = new Dictionary<Rectangle, ProductionIcon>();
		Animation cantBuild;
		Animation clock;
		Rectangle eventBounds = Rectangle.Empty;

		readonly WorldRenderer worldRenderer;

		SpriteFont overlayFont, symbolFont;
		float2 iconOffset, holdOffset, readyOffset, timeOffset;
		float countRightAnchor;
		float countTopY;
		float countBottomY;

		// Visual states for the two-number queue badge
		public readonly Color CountNowColor = Color.White;          // top: "now" — items in a row at the head
		public readonly Color CountTotalActiveColor = Color.White;  // bottom when no gap and no auto
		public readonly Color CountTotalWaitingColor = Color.Gold;  // bottom when this type isn't at the head
		public readonly Color CountTotalAutoColor = Color.LimeGreen; // bottom when any copies are infinite

		// Lime stripe down the LEFT edge of the icon when any item of this type is in auto-build mode.
		// Drawn with the primitive renderer to avoid font-glyph problems (FreeSansBold doesn't carry
		// the ∞ glyph at TinyBold size — it rendered as a missing-glyph box).
		public readonly Color AutoStripeColor = Color.LimeGreen;
		public readonly int AutoStripeWidth = 3;

		Player cachedQueueOwner;
		IProductionIconOverlay[] pios;

		[CustomLintableHotkeyNames]
		public static IEnumerable<string> LinterHotkeyNames(MiniYamlNode widgetNode, Action<string> emitError)
		{
			var prefix = "";
			var prefixNode = widgetNode.Value.Nodes.FirstOrDefault(n => n.Key == "HotkeyPrefix");
			if (prefixNode != null)
				prefix = prefixNode.Value.Value;

			var count = 0;
			var countNode = widgetNode.Value.Nodes.FirstOrDefault(n => n.Key == "HotkeyCount");
			if (countNode != null)
				count = FieldLoader.GetValue<int>("HotkeyCount", countNode.Value.Value);

			if (count == 0)
				return Array.Empty<string>();

			if (string.IsNullOrEmpty(prefix))
				emitError($"{widgetNode.Location} must define HotkeyPrefix if HotkeyCount > 0.");

			return Exts.MakeArray(count, i => prefix + (i + 1).ToString("D2"));
		}

		[ObjectCreator.UseCtor]
		public ProductionPaletteWidget(ModData modData, OrderManager orderManager, World world, WorldRenderer worldRenderer)
		{
			this.modData = modData;
			this.orderManager = orderManager;
			World = world;
			this.worldRenderer = worldRenderer;
			GetTooltipIcon = () => TooltipIcon;
			tooltipContainer = Exts.Lazy(() =>
				Ui.Root.Get<TooltipContainerWidget>(TooltipContainer));
		}

		public override void Initialize(WidgetArgs args)
		{
			base.Initialize(args);

			clock = new Animation(World, ClockAnimation);
			cantBuild = new Animation(World, NotBuildableAnimation);
			cantBuild.PlayFetchIndex(NotBuildableSequence, () => 0);
			hotkeys = Exts.MakeArray(HotkeyCount,
				i => modData.Hotkeys[HotkeyPrefix + (i + 1).ToString("D2")]);

			overlayFont = Game.Renderer.Fonts[OverlayFont];
			Game.Renderer.Fonts.TryGetValue(SymbolsFont, out symbolFont);

			iconOffset = 0.5f * IconSize.ToFloat2() + IconSpriteOffset;

			// Two-number queue badge in the TOP-RIGHT corner, stacked vertically. Top row = "now"
			// (items in a row at queue head); bottom row = total of this type. Right-aligned, so we
			// keep an x-anchor here and subtract measured text width per draw.
			countRightAnchor = IconSize.X - 3;
			var lineHeight = overlayFont.Measure("0").Y;
			countTopY = 1;
			countBottomY = countTopY + lineHeight - 1;

			holdOffset = iconOffset - overlayFont.Measure(HoldText) / 2;
			readyOffset = iconOffset - overlayFont.Measure(ReadyText) / 2;
		}

		public void ScrollDown()
		{
			if (CanScrollDown)
				IconRowOffset++;
		}

		public bool CanScrollDown
		{
			get
			{
				var totalRows = (TotalIconCount + Columns - 1) / Columns;

				return IconRowOffset < totalRows - MaxIconRowOffset;
			}
		}

		public void ScrollUp()
		{
			if (CanScrollUp)
				IconRowOffset--;
		}

		public bool CanScrollUp => IconRowOffset > 0;

		public void ScrollToTop()
		{
			IconRowOffset = 0;
		}

		public IEnumerable<ActorInfo> AllBuildables
		{
			get
			{
				if (CurrentQueue == null)
					return Enumerable.Empty<ActorInfo>();

				return CurrentQueue.AllItems().OrderBy(a => a.TraitInfo<BuildableInfo>().BuildPaletteOrder);
			}
		}

		public override void Tick()
		{
			TotalIconCount = AllBuildables.Count();

			if (CurrentQueue != null && !CurrentQueue.Actor.IsInWorld)
				CurrentQueue = null;

			if (CurrentQueue != null)
			{
				if (CurrentQueue.Actor.Owner != cachedQueueOwner)
					UpdateCachedProductionIconOverlays();

				RefreshIcons();
			}
		}

		public override void MouseEntered()
		{
			if (TooltipContainer != null)
				tooltipContainer.Value.SetTooltip(TooltipTemplate,
					new WidgetArgs() { { "player", World.LocalPlayer }, { "getTooltipIcon", GetTooltipIcon }, { "world", World } });
		}

		public override void MouseExited()
		{
			if (TooltipContainer != null)
			{
				tooltipContainer.Value.AnchorBounds = null;
				tooltipContainer.Value.RemoveTooltip();
			}
		}

		public override bool HandleMouseInput(MouseInput mi)
		{
			var iconEntry = icons.Where(i => i.Key.Contains(mi.Location)).FirstOrDefault();
			var icon = iconEntry.Value;

			if (mi.Event == MouseInputEvent.Move)
			{
				TooltipIcon = icon;
				if (icon != null && TooltipContainer != null)
				{
					// Anchor tooltip to the left of the full sidebar, aligned with the hovered icon row
					var iconRect = iconEntry.Key;
					var sidebar = Parent?.RenderBounds ?? RenderBounds;
					tooltipContainer.Value.AnchorBounds = new Rectangle(
						sidebar.X, iconRect.Y,
						sidebar.Width, iconRect.Height);
					tooltipContainer.Value.AnchorAbove = false;
				}
			}

			if (mi.Event == MouseInputEvent.Scroll)
			{
				if (mi.Delta.Y < 0 && CanScrollDown)
				{
					ScrollDown();
					Ui.ResetTooltips();
					Game.Sound.PlayNotification(World.Map.Rules, World.LocalPlayer, "Sounds", ClickSound, null);
				}
				else if (mi.Delta.Y > 0 && CanScrollUp)
				{
					ScrollUp();
					Ui.ResetTooltips();
					Game.Sound.PlayNotification(World.Map.Rules, World.LocalPlayer, "Sounds", ClickSound, null);
				}
			}

			if (icon == null)
				return false;

			// Eat mouse-up events
			if (mi.Event != MouseInputEvent.Down)
				return true;

			return HandleEvent(icon, mi.Button, mi.Modifiers);
		}

		protected bool PickUpCompletedBuildingIcon(ProductionIcon icon, ProductionItem item)
		{
			var actor = World.Map.Rules.Actors[icon.Name];

			if (item != null && item.Done && actor.HasTraitInfo<BuildingInfo>())
			{
				World.OrderGenerator = new PlaceBuildingOrderGenerator(CurrentQueue, icon.Name, worldRenderer);
				return true;
			}

			return false;
		}

		public void PickUpCompletedBuilding()
		{
			foreach (var icon in icons.Values)
			{
				var item = icon.Queued.FirstOrDefault();
				if (PickUpCompletedBuildingIcon(icon, item))
					break;
			}
		}

		bool HandleLeftClick(ProductionItem item, ProductionIcon icon, int handleCount, Modifiers modifiers)
		{
			if (PickUpCompletedBuildingIcon(icon, item))
			{
				Game.Sound.PlayNotification(World.Map.Rules, World.LocalPlayer, "Sounds", ClickSound, null);
				return true;
			}

			if (item != null && item.Paused)
			{
				// Resume a paused item
				Game.Sound.PlayNotification(World.Map.Rules, World.LocalPlayer, "Sounds", ClickSound, null);
				Game.Sound.PlayNotification(World.Map.Rules, World.LocalPlayer, "Speech", CurrentQueue.Info.QueuedAudio, World.LocalPlayer.Faction.InternalName);
				TextNotificationsManager.AddTransientLine(World.LocalPlayer, CurrentQueue.Info.QueuedTextNotification);

				World.IssueOrder(Order.PauseProduction(CurrentQueue.Actor, icon.Name, false));
				return true;
			}

			var buildable = CurrentQueue.BuildableItems().FirstOrDefault(a => a.Name == icon.Name);

			if (buildable != null)
			{
				// Queue a new item
				Game.Sound.PlayNotification(World.Map.Rules, World.LocalPlayer, "Sounds", ClickSound, null);
				var canQueue = CurrentQueue.CanQueue(buildable, out var notification, out var textNotification);

				if (!CurrentQueue.AllQueued().Any())
				{
					Game.Sound.PlayNotification(World.Map.Rules, World.LocalPlayer, "Speech", notification, World.LocalPlayer.Faction.InternalName);
					TextNotificationsManager.AddTransientLine(World.LocalPlayer, textNotification);
				}

				if (canQueue)
				{
					var queued = !modifiers.HasModifier(Modifiers.Ctrl);
					var auto = modifiers.HasModifier(Modifiers.Alt);
					World.IssueOrder(Order.StartProduction(CurrentQueue.Actor, icon.Name, handleCount, queued, auto));
					return true;
				}
			}

			return false;
		}

		bool HandleRightClick(ProductionItem item, ProductionIcon icon, int handleCount)
		{
			if (item == null)
				return false;

			Game.Sound.PlayNotification(World.Map.Rules, World.LocalPlayer, "Sounds", ClickSound, null);

			// If ANY copy of this type is on auto-build, route through the cancel path. The queue
			// handler exits auto-mode atomically (strips Infinite from all of them, refunds queued
			// copies, leaves the in-flight one to finish). Shift gives handleCount=5 so the next
			// iteration also cancels the in-flight item.
			var anyInfinite = icon.Queued.Any(q => q.Infinite);
			if (anyInfinite || CurrentQueue.Info.DisallowPaused || item.Paused || item.Done || item.TotalCost == item.RemainingCost)
			{
				// Instantly cancel items that haven't started, have finished, or if the queue doesn't support pausing
				Game.Sound.PlayNotification(World.Map.Rules, World.LocalPlayer, "Speech", CurrentQueue.Info.CancelledAudio, World.LocalPlayer.Faction.InternalName);
				TextNotificationsManager.AddTransientLine(World.LocalPlayer, CurrentQueue.Info.CancelledTextNotification);

				World.IssueOrder(Order.CancelProduction(CurrentQueue.Actor, icon.Name, handleCount));
			}
			else
			{
				// Pause an existing item
				Game.Sound.PlayNotification(World.Map.Rules, World.LocalPlayer, "Speech", CurrentQueue.Info.OnHoldAudio, World.LocalPlayer.Faction.InternalName);
				TextNotificationsManager.AddTransientLine(World.LocalPlayer, CurrentQueue.Info.OnHoldTextNotification);

				World.IssueOrder(Order.PauseProduction(CurrentQueue.Actor, icon.Name, true));
			}

			return true;
		}

		bool HandleMiddleClick(ProductionItem item, ProductionIcon icon, int handleCount)
		{
			if (item == null)
				return false;

			// Directly cancel, skipping "on-hold"
			Game.Sound.PlayNotification(World.Map.Rules, World.LocalPlayer, "Sounds", ClickSound, null);
			Game.Sound.PlayNotification(World.Map.Rules, World.LocalPlayer, "Speech", CurrentQueue.Info.CancelledAudio, World.LocalPlayer.Faction.InternalName);
			TextNotificationsManager.AddTransientLine(World.LocalPlayer, CurrentQueue.Info.CancelledTextNotification);

			World.IssueOrder(Order.CancelProduction(CurrentQueue.Actor, icon.Name, handleCount));

			return true;
		}

		bool HandleEvent(ProductionIcon icon, MouseButton btn, Modifiers modifiers)
		{
			// Click = 1, Shift+click = 5. Alt is now the "auto-build" flag (Order.StartProductionAutoFlag),
			// not a count multiplier — Alt+click queues 1 auto, Shift+Alt+click queues 5 auto.
			var startCount = modifiers.HasModifier(Modifiers.Shift) ? 5 : 1;

			// PERF: avoid an unnecessary enumeration by casting back to its known type
			var cancelCount = modifiers.HasModifier(Modifiers.Ctrl) ? ((List<ProductionItem>)CurrentQueue.AllQueued()).Count : startCount;

			// Middle-click is the "nuke this icon" gesture: cancel every queued copy of this type,
			// including any in-flight item. The +1 covers the auto-mode case — the first
			// CancelProductionInner iteration exits auto and clears queued copies (leaving in-flight),
			// the next iteration cancels the in-flight one.
			var middleCancelCount = icon.Queued.Count + 1;

			var item = icon.Queued.FirstOrDefault();
			var handled = btn == MouseButton.Left ? HandleLeftClick(item, icon, startCount, modifiers)
				: btn == MouseButton.Right ? HandleRightClick(item, icon, cancelCount)
				: btn == MouseButton.Middle && HandleMiddleClick(item, icon, middleCancelCount);

			if (!handled)
				Game.Sound.PlayNotification(World.Map.Rules, World.LocalPlayer, "Sounds", ClickDisabledSound, null);

			return true;
		}

		public override bool HandleKeyPress(KeyInput e)
		{
			if (e.Event == KeyInputEvent.Up || CurrentQueue == null)
				return false;

			if (SelectProductionBuildingHotkey.IsActivatedBy(e))
				return SelectProductionBuilding();

			var batchModifiers = e.Modifiers.HasModifier(Modifiers.Shift) ? Modifiers.Shift : Modifiers.None;

			// HACK: enable production if the shift key is pressed
			e.Modifiers &= ~Modifiers.Shift;
			var toBuild = icons.Values.FirstOrDefault(i => i.Hotkey != null && i.Hotkey.IsActivatedBy(e));
			return toBuild != null && HandleEvent(toBuild, MouseButton.Left, batchModifiers);
		}

		bool SelectProductionBuilding()
		{
			var viewport = worldRenderer.Viewport;
			var selection = World.Selection;

			if (CurrentQueue == null)
				return true;

			var facility = CurrentQueue.MostLikelyProducer().Actor;

			if (facility == null || facility.OccupiesSpace == null)
				return true;

			if (selection.Actors.Count() == 1 && selection.Contains(facility))
				viewport.Center(selection.Actors);
			else
				selection.Combine(World, new[] { facility }, false, true);

			Game.Sound.PlayNotification(World.Map.Rules, null, "Sounds", ClickSound, null);
			return true;
		}

		void UpdateCachedProductionIconOverlays()
		{
			cachedQueueOwner = CurrentQueue.Actor.Owner;
			pios = cachedQueueOwner.PlayerActor.TraitsImplementing<IProductionIconOverlay>().ToArray();
		}

		public void RefreshIcons()
		{
			icons = new Dictionary<Rectangle, ProductionIcon>();
			var producer = CurrentQueue != null ? CurrentQueue.MostLikelyProducer() : default;
			if (CurrentQueue == null || producer.Trait == null)
			{
				if (DisplayedIconCount != 0)
				{
					OnIconCountChanged(DisplayedIconCount, 0);
					DisplayedIconCount = 0;
				}

				return;
			}

			var oldIconCount = DisplayedIconCount;
			DisplayedIconCount = 0;

			var rb = RenderBounds;
			var faction = producer.Trait.Faction;

			foreach (var item in AllBuildables.Skip(IconRowOffset * Columns).Take(MaxIconRowOffset * Columns))
			{
				var x = DisplayedIconCount % Columns;
				var y = DisplayedIconCount / Columns;
				var rect = new Rectangle(rb.X + x * (IconSize.X + IconMargin.X), rb.Y + y * (IconSize.Y + IconMargin.Y), IconSize.X, IconSize.Y);

				var rsi = item.TraitInfo<RenderSpritesInfo>();
				var icon = new Animation(World, rsi.GetImage(item, faction));
				var bi = item.TraitInfo<BuildableInfo>();
				icon.Play(bi.Icon);

				var palette = bi.IconPaletteIsPlayerPalette ? bi.IconPalette + producer.Actor.Owner.InternalName : bi.IconPalette;

				var pi = new ProductionIcon()
				{
					Actor = item,
					Name = item.Name,
					Hotkey = DisplayedIconCount < HotkeyCount ? hotkeys[DisplayedIconCount] : null,
					Sprite = icon.Image,
					Palette = worldRenderer.Palette(palette),
					IconClockPalette = worldRenderer.Palette(ClockPalette),
					IconDarkenPalette = worldRenderer.Palette(NotBuildablePalette),
					Pos = new float2(rect.Location),
					Queued = currentQueue.AllQueued().Where(a => a.Item == item.Name).ToList(),
					ProductionQueue = currentQueue
				};

				icons.Add(rect, pi);
				DisplayedIconCount++;
			}

			eventBounds = icons.Keys.Union();

			if (oldIconCount != DisplayedIconCount)
				OnIconCountChanged(oldIconCount, DisplayedIconCount);
		}

		public override void Draw()
		{
			timeOffset = iconOffset - overlayFont.Measure(WidgetUtils.FormatTime(0, World.Timestep)) / 2;

			if (CurrentQueue == null)
				return;

			var buildableItems = CurrentQueue.BuildableItems();

			// Walk the global queue once to get a positional view we can use to compute "now" counts.
			var allQueued = (IList<ProductionItem>)CurrentQueue.AllQueued();

			// Icons
			Game.Renderer.EnableAntialiasingFilter();
			foreach (var icon in icons.Values)
			{
				WidgetUtils.DrawSpriteCentered(icon.Sprite, icon.Palette, icon.Pos + iconOffset);

				// Draw the ProductionIconOverlay's sprites
				foreach (var pio in pios.Where(p => p.IsOverlayActive(icon.Actor)))
					WidgetUtils.DrawSpriteCentered(pio.Sprite, worldRenderer.Palette(pio.Palette), icon.Pos + iconOffset + pio.Offset(IconSize));

				// Build progress — only show the clock on the icon that's actually being produced.
				// Showing empty clocks on every queued icon was misleading; the count badge now
				// signals "queued, waiting" instead.
				if (icon.Queued.Count > 0)
				{
					var first = icon.Queued[0];
					var isActive = CurrentQueue.IsProducing(first);
					if (isActive)
					{
						clock.PlayFetchIndex(ClockSequence,
							() => (first.TotalTime - first.RemainingTime)
								* (clock.CurrentSequence.Length - 1) / first.TotalTime);
						clock.Tick();

						WidgetUtils.DrawSpriteCentered(clock.Image, icon.IconClockPalette, icon.Pos + iconOffset);
					}
				}
				else if (!buildableItems.Any(a => a.Name == icon.Name))
					WidgetUtils.DrawSpriteCentered(cantBuild.Image, icon.IconDarkenPalette, icon.Pos + iconOffset);
			}

			Game.Renderer.DisableAntialiasingFilter();

			// Overlays
			foreach (var icon in icons.Values)
			{
				var total = icon.Queued.Count;
				if (total == 0)
					continue;

				var first = icon.Queued[0];
				var anyInfinite = false;
				for (var i = 0; i < icon.Queued.Count; i++)
				{
					if (icon.Queued[i].Infinite)
					{
						anyInfinite = true;
						break;
					}
				}

				var waiting = !CurrentQueue.IsProducing(first) && !first.Done;

				// "Now" count = number of consecutive items of this type starting at the global
				// queue head. Reads as "how many of these will produce in a row right now".
				// Only meaningful (non-zero) for the icon whose type is at queue[0].
				var nowCount = 0;
				for (var i = 0; i < allQueued.Count; i++)
				{
					if (allQueued[i].Item == icon.Name)
						nowCount++;
					else
						break;
				}

				// Lime stripe down the left edge: this type has at least one auto-build copy. Drawn
				// as a primitive so it never depends on a missing font glyph.
				if (anyInfinite)
				{
					var stripe = new Rectangle(
						(int)icon.Pos.X,
						(int)icon.Pos.Y,
						AutoStripeWidth,
						IconSize.Y);
					WidgetUtils.FillRectWithColor(stripe, AutoStripeColor);
				}

				// Center text — READY / ON HOLD / time — unchanged.
				if (first.Done)
				{
					if (ReadyTextStyle == ReadyTextStyleOptions.Solid || orderManager.LocalFrameNumber * worldRenderer.World.Timestep / 360 % 2 == 0)
						overlayFont.DrawTextWithContrast(ReadyText, icon.Pos + readyOffset, Color.White, Color.Black, 1);
					else if (ReadyTextStyle == ReadyTextStyleOptions.AlternatingColor)
						overlayFont.DrawTextWithContrast(ReadyText, icon.Pos + readyOffset, ReadyTextAltColor, Color.Black, 1);
				}
				else if (first.Paused)
					overlayFont.DrawTextWithContrast(HoldText,
						icon.Pos + holdOffset,
						Color.White, Color.Black, 1);
				else if (!waiting && DrawTime)
					overlayFont.DrawTextWithContrast(WidgetUtils.FormatTime(first.Queue.RemainingTimeActual(first), World.Timestep),
						icon.Pos + timeOffset,
						Color.White, Color.Black, 1);

				// Two-number badge in the TOP-RIGHT corner, right-aligned.
				// Top row "now" only when there's a gap (now < total) AND this type is at the head.
				// Bottom row "total" when total > 1 OR the type is waiting (so a lone queued copy
				// still gets a "1" indicator).
				var showTop = nowCount > 0 && nowCount < total;
				var showBottom = total > 1 || waiting;

				if (showTop)
				{
					var nowText = nowCount.ToString();
					var nowWidth = overlayFont.Measure(nowText).X;
					var nowPos = new float2(icon.Pos.X + countRightAnchor - nowWidth, icon.Pos.Y + countTopY);
					overlayFont.DrawTextWithContrast(nowText, nowPos, CountNowColor, Color.Black, 1);
				}

				if (showBottom)
				{
					var totalText = total.ToString();
					var totalWidth = overlayFont.Measure(totalText).X;
					var totalY = showTop ? countBottomY : countTopY;
					var totalPos = new float2(icon.Pos.X + countRightAnchor - totalWidth, icon.Pos.Y + totalY);
					var totalColor = anyInfinite ? CountTotalAutoColor
						: waiting ? CountTotalWaitingColor
						: CountTotalActiveColor;
					overlayFont.DrawTextWithContrast(totalText, totalPos, totalColor, Color.Black, 1);
				}
			}
		}

		public override string GetCursor(int2 pos)
		{
			var icon = icons.Where(i => i.Key.Contains(pos))
				.Select(i => i.Value).FirstOrDefault();

			return icon != null ? base.GetCursor(pos) : null;
		}
	}
}
