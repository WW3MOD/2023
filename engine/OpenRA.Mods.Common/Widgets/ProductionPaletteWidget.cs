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

		[FluentReference("units")]
		const string SelectedUnitsAcrossScreen = "selected-units-across-screen";

		[FluentReference("units")]
		const string SelectedUnitsAcrossMap = "selected-units-across-map";

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

		// The split queue badge, top-right, right-aligned: the manual count in white with the
		// auto-build count in lime beside it, so "3+2" says three of these were ordered and two more
		// keep coming. Manual and auto-build entries are the SAME kind of object in one FIFO list -
		// Infinite is just a flag on an entry (ProductionQueue.cs:645-649) - and this is the only
		// place the mix is stated. It replaces both the old "now"/"total" stack, whose top number
		// answered a question nobody asks, and the lime stripe that used to run down the left edge
		// carrying the same single bit as the badge's colour.
		public readonly Color CountManualColor = Color.White;
		public readonly Color CountAutoColor = Color.LimeGreen;
		public readonly string CountAutoPrefix = "+";
		public readonly int CountRightMargin = 3;
		public readonly int CountTopMargin = 1;

		// The held-rank chevron, top-left: ONE sprite for the highest tier banked, because that is
		// the only tier a purchase can spend (RankAccrual.HighestHeldTier, RankAccumulation.cs:141-152).
		// Drawn from the shipped iconchevrons art rather than from the polyline strip it replaces,
		// which ran x 4-54 and y 33-45 at full 3/2/1 and collided with the caption baked into the
		// bottom 8 rows of every cameo. The per-tier breakdown it gives up lives in the production
		// tooltip now. ProductionIconOverlayManager is deliberately NOT used: it is inert in this mod
		// and its one-sprite-per-actor shape cannot express a tier.
		//
		// Sequences are named <prefix><tier>: rank1, rank2, rank3. Not a [SequenceReference] - the
		// prefix alone is not a sequence, and the widget's own ClockSequence carries no attribute
		// either. A prefix naming sequences that do not exist fails loudly when the first chevron is
		// drawn, which is the good failure; the silent one is art, and that is guarded by the sprite
		// being a real SHP frame rather than a glyph.
		public readonly string RankSequencePrefix = "rank";
		public readonly string RankAnimation = "iconchevrons";
		public readonly string RankPalette = "chrome";
		public readonly Color RankColor = Color.FromArgb(255, 240, 210, 122);

		// Top-left inset of the chevron sprite, in cell coordinates.
		public readonly int2 RankChevronOffset = new int2(1, 1);

		// Top-left of the count, in cell coordinates: 1 px right of the chevron's ink, which is 14
		// wide at every tier. NOT derived from the sprite's drawn width - ShpTDLoader trims a frame
		// to its used rect and then pads that back out with a 1 px transparent border and an even-
		// size fudge (ShpTDLoader.cs:113-134), so Sprite.Size reports 16 for 14 columns of ink and
		// deriving from it would sit the count 2 px right of where the approved mockup puts it.
		public readonly int2 RankCountOffset = new int2(16, 2);

		// Blank pixels the count keeps clear of the queue badge before it gives way entirely.
		public readonly int RankCountBadgeGap = 1;

		// The count is suppressed at 1: a lone chevron already says "one banked", and a digit that
		// appears and disappears makes the mark's width jump as stock changes. Note this is the
		// OPPOSITE ruling to the strip this replaces, which drew the digit always - there the digit
		// was needed to tell three side-by-side tier entries apart, and here there is only ever one.
		public readonly int RankCountMinimum = 2;

		// U+00D7 MULTIPLICATION SIGN, glyph 153 in FreeSansBold with a 52-byte outline - verified by
		// parsing the font's cmap and loca, not assumed. A glyph the font lacks renders as NOTHING at
		// all, silently, with the widget working perfectly, which is the trap this feature has now
		// sprung three times. Do not swap this for a Geometric Shapes or arrow character.
		public readonly string RankCountPrefix = "\u00D7";

		Player cachedQueueOwner;
		IProductionIconOverlay[] pios;
		RankAccumulation rankAccumulation;
		PaletteReference rankPalette;

		// One sprite per purchasable tier, built on first use rather than in Initialize: mods with no
		// RankAccumulation trait (ra, cnc, d2k all share this widget) never reach the load, so they
		// never need the sequences this names.
		Sprite[] rankChevrons;

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

			// Split queue badge in the TOP-RIGHT corner, on ONE line. Right-aligned, so we keep an
			// x-anchor here and subtract measured text width per draw - twice now, once per half.
			countRightAnchor = IconSize.X - CountRightMargin;
			countTopY = CountTopMargin;

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

			ApplyTestHover();
		}

		string appliedTestHover;

		/// <summary>
		/// Opens the production tooltip for the actor named by <c>Test.HoverProductionIcon</c>,
		/// doing by hand what a mouse hover does: seat <see cref="TooltipIcon"/>, park the cursor
		/// inside the icon's rect, and raise MouseEntered so the tooltip container builds.
		/// </summary>
		/// <remarks>
		/// Deferred to Tick for the same reason LobbyOptionsLogic defers its hover: the icon
		/// rectangles only exist after RefreshIcons has run a layout pass. Without this hook the
		/// production tooltip cannot be photographed at all — it is built solely from
		/// <see cref="MouseEntered"/>, so the only alternative is driving the OS cursor.
		/// </remarks>
		void ApplyTestHover()
		{
			if (!TestMode.IsActive)
				return;

			var requested = TestMode.HoverProductionIcon;
			if (string.IsNullOrEmpty(requested) || requested == appliedTestHover)
				return;

			var match = icons.FirstOrDefault(i =>
				string.Equals(i.Value.Actor.Name, requested, StringComparison.OrdinalIgnoreCase));

			// The queue this palette shows may simply not contain the actor — the sidebar has one
			// palette per queue. Leave it unapplied rather than claiming the hover, so the palette
			// that DOES own the icon can take it on its own Tick.
			if (match.Value == null)
				return;

			appliedTestHover = requested;
			TooltipIcon = match.Value;

			var iconRect = match.Key;
			Viewport.LastMousePos = new int2(
				iconRect.X + iconRect.Width / 2,
				iconRect.Y + iconRect.Height / 2);

			if (TooltipContainer != null)
			{
				var sidebar = Parent?.RenderBounds ?? RenderBounds;
				tooltipContainer.Value.AnchorBounds = new Rectangle(
					sidebar.X, iconRect.Y, sidebar.Width, iconRect.Height);
				tooltipContainer.Value.AnchorAbove = false;
			}

			MouseEntered();
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

		/// <summary>
		/// Ctrl+Alt+LMB on an icon selects the player's units of that type instead of queueing one.
		/// Escalates screen -> map on a repeat click, matching SelectUnitsByTypeHotkeyLogic.
		/// </summary>
		bool HandleSelectByType(ProductionIcon icon)
		{
			var eligiblePlayers = SelectionUtils.GetPlayersToIncludeInSelection(World);
			var selectableInfo = World.Map.Rules.Actors[icon.Name].TraitInfoOrDefault<SelectableInfo>();
			var selectionClasses = new HashSet<string>
			{
				SelectByTypeScopeMath.ResolveSelectionClass(icon.Name, selectableInfo?.Class)
			};

			var onScreen = SelectionUtils.SelectActorsOnScreen(World, worldRenderer, selectionClasses, eligiblePlayers).ToList();
			var inWorld = SelectionUtils.SelectActorsInWorld(World, selectionClasses, eligiblePlayers).ToList();

			var selected = World.Selection.Actors;
			var selectionIsExactlyOnScreenSet = onScreen.Count > 0
				&& selected.Count == onScreen.Count
				&& onScreen.All(selected.Contains);

			var scope = SelectByTypeScopeMath.Resolve(onScreen.Count, inWorld.Count, selectionIsExactlyOnScreenSet);
			if (scope == SelectByTypeScope.None)
			{
				// Owns none of this type: deliberately leave the existing selection intact.
				Game.Sound.PlayNotification(World.Map.Rules, World.LocalPlayer, "Sounds", ClickDisabledSound, null);
				return true;
			}

			var newSelection = scope == SelectByTypeScope.World ? inWorld : onScreen;
			World.Selection.Combine(World, newSelection, false, false);

			TextNotificationsManager.AddFeedbackLine(
				scope == SelectByTypeScope.World ? SelectedUnitsAcrossMap : SelectedUnitsAcrossScreen,
				"units", newSelection.Count);

			Game.Sound.PlayNotification(World.Map.Rules, World.LocalPlayer, "Sounds", ClickSound, null);

			return true;
		}

		/// <summary>
		/// Test hook: drives a sidebar click on the icon for <paramref name="name"/>, switching to the
		/// queue that offers it first. Routes through the same HandleEvent path as a real click so the
		/// modifier tiers are exercised; only SDL modifier decode and icon hit-testing are bypassed.
		/// Returns false when no enabled queue offers the type.
		/// </summary>
		public bool SimulateIconClick(string name, MouseButton btn, Modifiers modifiers)
		{
			if (!icons.Values.Any(i => i.Name == name))
			{
				var queue = World.LocalPlayer?.PlayerActor.TraitsImplementing<ProductionQueue>()
					.FirstOrDefault(q => q.Enabled && q.BuildableItems().Any(a => a.Name == name));

				if (queue == null)
					return false;

				CurrentQueue = queue;
			}

			var icon = icons.Values.FirstOrDefault(i => i.Name == name);
			return icon != null && HandleEvent(icon, btn, modifiers);
		}

		bool HandleEvent(ProductionIcon icon, MouseButton btn, Modifiers modifiers)
		{
			// Ctrl+Alt+LMB is a selection gesture, not a production one, so it has to be intercepted
			// ahead of HandleLeftClick — which would otherwise pick up a completed building or queue.
			// This displaces the old "priority-insert + auto-build" combination; both flags remain
			// reachable on their own as Ctrl+click and Alt+click.
			if (btn == MouseButton.Left && modifiers.HasModifier(Modifiers.Ctrl) && modifiers.HasModifier(Modifiers.Alt))
				return HandleSelectByType(icon);

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
			rankAccumulation = cachedQueueOwner.PlayerActor.TraitOrDefault<RankAccumulation>();
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

			// Resolved here rather than in Initialize for the same reason the clock's is: the world's
			// palettes are guaranteed present by the time a queue has been handed to us.
			rankPalette = worldRenderer.Palette(RankPalette);

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

			// Icons
			Game.Renderer.EnableAntialiasingFilter();
			foreach (var icon in icons.Values)
			{
				WidgetUtils.DrawSpriteCentered(icon.Sprite, icon.Palette, icon.Pos + iconOffset);

				// Draw the ProductionIconOverlay's sprites
				foreach (var pio in pios.Where(p => p.IsOverlayActive(icon.Actor)))
					WidgetUtils.DrawSpriteCentered(pio.Sprite, worldRenderer.Palette(pio.Palette), icon.Pos + iconOffset + pio.Offset(IconSize));

				// Build progress — only show the clock on the icon that's actually being produced.
				// Showing empty clocks on every queued icon was misleading; a queued type that is not
				// at the head of the FIFO is signalled by carrying a badge and no countdown.
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
				var showRank = true;

				// Left edge of whatever the queue badge occupies on line 1, so the rank count knows
				// how much room it has. The whole cell width when there is no badge at all.
				var badgeLeft = icon.Pos.X + IconSize.X;

				if (total > 0)
				{
					var first = icon.Queued[0];
					var auto = 0;
					for (var i = 0; i < icon.Queued.Count; i++)
						if (icon.Queued[i].Infinite)
							auto++;

					var manual = total - auto;
					var waiting = !CurrentQueue.IsProducing(first) && !first.Done;

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

					badgeLeft = DrawQueueBadge(icon, manual, auto);

					showRank = ProductionIconMarks.ShowRankMark(true, first.Done, first.Paused, waiting, DrawTime);
				}

				if (showRank)
					DrawHeldRank(icon, badgeLeft);
			}
		}

		/// <summary>
		/// The split queue badge, top-right: the manual count in white and the recycling count in
		/// lime, laid right to left from the same anchor so the pair reads as one number. Returns the
		/// x it grew leftwards to, which is the room the rank count on the same line has left.
		/// </summary>
		float DrawQueueBadge(ProductionIcon icon, int manual, int auto)
		{
			var anchor = icon.Pos.X + countRightAnchor;
			var y = icon.Pos.Y + countTopY;

			var autoText = ProductionIconMarks.AutoBadgeText(manual, auto, CountAutoPrefix);
			if (autoText != null)
			{
				anchor -= overlayFont.Measure(autoText).X;
				overlayFont.DrawTextWithContrast(autoText, new float2(anchor, y), CountAutoColor, Color.Black, 1);
			}

			var manualText = ProductionIconMarks.ManualBadgeText(manual);
			if (manualText != null)
			{
				anchor -= overlayFont.Measure(manualText).X;
				overlayFont.DrawTextWithContrast(manualText, new float2(anchor, y), CountManualColor, Color.Black, 1);
			}

			return anchor;
		}

		/// <summary>
		/// One chevron sprite in the top-left for the highest tier banked against this type, with the
		/// count beside it once there is more than one. Nothing is drawn for a type holding nothing,
		/// which is most of them most of the time.
		/// <para>Top-left is the only corner this can live in. Bottom-left and bottom-right are 8 of
		/// their 12 rows caption ink baked into the cameo art, and top-right is the queue badge - so a
		/// tier-3 sprite, the tallest at 18 rows, only clears the caption by starting from the top.
		/// Its ink runs to cell row 18 against a caption starting at row 38.</para>
		/// </summary>
		void DrawHeldRank(ProductionIcon icon, float badgeLeft)
		{
			if (rankAccumulation == null || rankPalette == null)
				return;

			var tier = rankAccumulation.PeekRank(icon.Name);
			if (tier < 1)
				return;

			// The sprite's own Offset is subtracted back out so the position given is the top-left of
			// the frame's INK: SHP frames are trimmed to their used rect and carry a re-centring
			// offset, which SpriteRenderer.DrawSprite adds back (SpriteRenderer.cs:148-160).
			var sprite = RankChevron(tier);
			Game.Renderer.SpriteRenderer.DrawSprite(sprite, rankPalette,
				icon.Pos + RankChevronOffset - sprite.Offset);

			var held = rankAccumulation.StockOf(icon.Name, tier);
			var text = ProductionIconMarks.RankCountText(held, RankCountMinimum, RankCountPrefix);
			if (text == null)
				return;

			var pos = icon.Pos + RankCountOffset;
			if (!ProductionIconMarks.RankCountFits(pos.X, overlayFont.Measure(text).X, badgeLeft, RankCountBadgeGap))
				return;

			overlayFont.DrawTextWithContrast(text, pos, RankColor, Color.Black, 1);
		}

		/// <summary>
		/// The chevron sprite for a tier, resolved once. Deferred out of Initialize so that a mod
		/// sharing this widget without a RankAccumulation trait never asks for sequences it does not
		/// declare - only DrawHeldRank reaches here, and it returns early without the trait.
		/// </summary>
		Sprite RankChevron(int tier)
		{
			if (rankChevrons == null)
			{
				rankChevrons = new Sprite[RankAccrual.MaxPurchasableRank];
				var anim = new Animation(World, RankAnimation);
				for (var t = 1; t <= RankAccrual.MaxPurchasableRank; t++)
				{
					anim.Play(ProductionIconMarks.ChevronSequence(RankSequencePrefix, t));
					rankChevrons[t - 1] = anim.Image;
				}
			}

			return rankChevrons[tier - 1];
		}

		public override string GetCursor(int2 pos)
		{
			var icon = icons.Where(i => i.Key.Contains(pos))
				.Select(i => i.Value).FirstOrDefault();

			return icon != null ? base.GetCursor(pos) : null;
		}
	}
}
