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
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
		public readonly string ProductionGroup;
		public readonly string RepeatSymbolsFont = "Symbols";

		public new Action OnRightClick = () => { };
		public Action OnMiddleClick = () => { };
		public Func<bool> RepeatModeActive = () => false;

		SpriteFont symbolFont;

		[ObjectCreator.UseCtor]
		public ProductionTypeButtonWidget(ModData modData, World world)
			: base(modData, world)
=======
		public readonly uint ColorFog = Color.FromArgb(128, Color.Black).ToArgb();
		public readonly uint ColorShroud = Color.Black.ToArgb();

		public string WorldInteractionController = null;
		public int AnimationLength = 5;
		public string RadarOnlineSound = null;
		public string RadarOfflineSound = null;
		public string SoundUp;
		public string SoundDown;
		public Func<bool> IsEnabled = () => true;
		public Action AfterOpen = () => { };
		public Action AfterClose = () => { };
		public Action<float> Animating = _ => { };

		readonly World world;
		readonly WorldRenderer worldRenderer;
		readonly RadarPings radarPings;
		readonly IRadarTerrainLayer[] radarTerrainLayers;
		readonly bool isRectangularIsometric;
		readonly int cellWidth;
		readonly int previewWidth;
		readonly int previewHeight;
		readonly string worldDefaultCursor = ChromeMetrics.Get<string>("WorldDefaultCursor");

		float radarMinimapHeight;
		int frame;
		bool hasRadar;
		bool cachedEnabled;

		float previewScale = 0;
		int2 previewOrigin = int2.Zero;
		Rectangle mapRect = Rectangle.Empty;

		Sheet radarSheet;
		byte[] radarData;

		Sprite terrainSprite;
		Sprite actorSprite;
		Sprite shroudSprite;
		Shroud shroud;
		PlayerRadarTerrain playerRadarTerrain;
		Player currentPlayer;

		[ObjectCreator.UseCtor]
		public RadarWidget(World world, WorldRenderer worldRenderer)
		{
			this.world = world;
			this.worldRenderer = worldRenderer;

			radarPings = world.WorldActor.TraitOrDefault<RadarPings>();
			radarTerrainLayers = world.WorldActor.TraitsImplementing<IRadarTerrainLayer>().ToArray();
			isRectangularIsometric = world.Map.Grid.Type == MapGridType.RectangularIsometric;
			cellWidth = isRectangularIsometric ? 2 : 1;
			previewWidth = world.Map.MapSize.X;
			previewHeight = world.Map.MapSize.Y;
			if (isRectangularIsometric)
				previewWidth = 2 * previewWidth - 1;
		}

		void CellTerrainColorChanged(MPos uv)
		{
			UpdateTerrainColor(uv);
		}

		void CellTerrainColorChanged(CPos cell)
		{
			UpdateTerrainColor(cell.ToMPos(world.Map));
		}

		public override void Initialize(WidgetArgs args)
		{
			base.Initialize(args);

			// The four layers are stored in a 2x2 grid within a single texture
			radarSheet = new Sheet(SheetType.BGRA, new Size(2 * previewWidth, 2 * previewHeight).NextPowerOf2());
			radarData = radarSheet.GetData();

			MapBoundsChanged();

			var player = world.Type == WorldType.Regular ? world.LocalPlayer ?? world.RenderPlayer : null;
			SetPlayer(player, true);

			if (player == null)
			{
				// Set initial terrain data
				foreach (var uv in world.Map.AllCells.MapCoords)
					UpdateTerrainColor(uv);
			}

			world.RenderPlayerChanged += WorldOnRenderPlayerChanged;
		}

		void WorldOnRenderPlayerChanged(Player player)
		{
			SetPlayer(player);

			// Set initial terrain data
			foreach (var uv in world.Map.AllCells.MapCoords)
				UpdateTerrainColor(uv);
		}

		void SetPlayer(Player player, bool forceUpdate = false)
		{
			currentPlayer = player;

			var newShroud = player?.Shroud;

			if (newShroud != shroud)
			{
				if (shroud != null)
					shroud.OnShroudChanged -= UpdateShroudCell;

				if (newShroud != null)
				{
					newShroud.OnShroudChanged += UpdateShroudCell;
					foreach (var puv in world.Map.ProjectedCells)
						UpdateShroudCell(puv);
				}

				shroud = newShroud;
			}

			var newPlayerRadarTerrain = currentPlayer?.PlayerActor.TraitOrDefault<PlayerRadarTerrain>();

			if (forceUpdate || newPlayerRadarTerrain != playerRadarTerrain)
			{
				if (playerRadarTerrain != null)
					playerRadarTerrain.CellTerrainColorChanged -= CellTerrainColorChanged;
				else
				{
					world.Map.Tiles.CellEntryChanged -= CellTerrainColorChanged;
					foreach (var rtl in radarTerrainLayers)
						rtl.CellEntryChanged -= CellTerrainColorChanged;
				}

				if (newPlayerRadarTerrain != null)
					newPlayerRadarTerrain.CellTerrainColorChanged += CellTerrainColorChanged;
				else
				{
					world.Map.Tiles.CellEntryChanged += CellTerrainColorChanged;
					foreach (var rtl in radarTerrainLayers)
						rtl.CellEntryChanged += CellTerrainColorChanged;
				}

				playerRadarTerrain = newPlayerRadarTerrain;
			}
		}

		void MapBoundsChanged()
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp
		{
			Game.Renderer.Fonts.TryGetValue(RepeatSymbolsFont, out symbolFont);
		}

		protected ProductionTypeButtonWidget(ProductionTypeButtonWidget other)
			: base(other)
		{
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
			ProductionGroup = other.ProductionGroup;
			OnRightClick = other.OnRightClick;
			OnMiddleClick = other.OnMiddleClick;
			RepeatModeActive = other.RepeatModeActive;
			symbolFont = other.symbolFont;
=======
			var (leftColor, rightColor) = playerRadarTerrain != null && playerRadarTerrain.IsInitialized ?
				playerRadarTerrain[uv] : PlayerRadarTerrain.GetColor(world.Map, radarTerrainLayers, uv);

			var stride = radarSheet.Size.Width;

			unsafe
			{
				fixed (byte* colorBytes = &radarData[0])
				{
					var colors = (uint*)colorBytes;
					if (isRectangularIsometric)
					{
						// Odd rows are shifted right by 1px
						var dx = uv.V & 1;
						if (uv.U + dx > 0)
							colors[uv.V * stride + 2 * uv.U + dx - 1] = leftColor;

						if (2 * uv.U + dx < stride)
							colors[uv.V * stride + 2 * uv.U + dx] = rightColor;
					}
					else
						colors[uv.V * stride + uv.U] = leftColor;
				}
			}
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp
		}

		public override void MouseEntered()
		{
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
			if (TooltipContainer == null || GetTooltipText() == null)
				return;
=======
			var color = 0u;
			var cv = currentPlayer.Shroud.GetVisibility(puv);
			if (!cv.HasFlag(Shroud.CellVisibility.Explored))
				color = ColorShroud;
			else if (!cv.HasFlag(Shroud.CellVisibility.Visible))
				color = ColorFog;

			var stride = radarSheet.Size.Width;
			unsafe
			{
				fixed (byte* colorBytes = &radarData[0])
				{
					var colors = (uint*)colorBytes;
					foreach (var iuv in world.Map.Unproject(puv))
					{
						if (isRectangularIsometric)
						{
							// Odd rows are shifted right by 1px
							var dx = iuv.V & 1;
							if (iuv.U + dx > 0)
								colors[iuv.V * stride + 2 * iuv.U + dx - 1 + previewWidth] = color;

							if (2 * iuv.U + dx < stride)
								colors[iuv.V * stride + 2 * iuv.U + dx + previewWidth] = color;
						}
						else
							colors[iuv.V * stride + iuv.U + previewWidth] = color;
					}
				}
			}
		}

		public override string GetCursor(int2 pos)
		{
			if (world == null || !hasRadar)
				return null;

			var cell = MinimapPixelToCell(pos);
			var worldPixel = worldRenderer.ScreenPxPosition(world.Map.CenterOfCell(cell));
			var location = worldRenderer.Viewport.WorldToViewPx(worldPixel);

			var mi = new MouseInput
			{
				Location = location,
				Button = Game.Settings.Game.MouseButtonPreference.Action,
				Modifiers = Game.GetModifierKeys()
			};

			var cursor = world.OrderGenerator.GetCursor(world, cell, worldPixel, mi);
			if (cursor == null)
				return worldDefaultCursor;
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp

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

			if (symbolFont != null && RepeatModeActive())
			{
				var rb = RenderBounds;
				var symbol = "\u221E"; // ∞
				var size = symbolFont.Measure(symbol);
				var pos = new float2(rb.X + (rb.Width - size.X) / 2, rb.Y + (rb.Height - size.Y) / 2);
				symbolFont.DrawTextWithContrast(symbol, pos, Color.LimeGreen, Color.Black, 1);
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
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
				OnRightClick();
				return true;
=======
				// The actor layer is updated every tick
				var stride = radarSheet.Size.Width;
				Array.Clear(radarData, 4 * actorSprite.Bounds.Top * stride, 4 * actorSprite.Bounds.Height * stride);

				var cells = new List<(CPos Cell, Color Color)>();

				unsafe
				{
					fixed (byte* colorBytes = &radarData[0])
					{
						var colors = (uint*)colorBytes;

						foreach (var t in world.ActorsWithTrait<IRadarSignature>())
						{
							if (!t.Actor.IsInWorld || world.FogObscures(t.Actor))
								continue;

							cells.Clear();
							t.Trait.PopulateRadarSignatureCells(t.Actor, cells);
							foreach (var cell in cells)
							{
								if (!world.Map.Contains(cell.Cell))
									continue;

								var uv = cell.Cell.ToMPos(world.Map.Grid.Type);
								var color = cell.Color.ToArgb();
								if (isRectangularIsometric)
								{
									// Odd rows are shifted right by 1px
									var dx = uv.V & 1;
									if (uv.U + dx > 0)
										colors[(uv.V + previewHeight) * stride + 2 * uv.U + dx - 1] = color;

									if (2 * uv.U + dx < stride)
										colors[(uv.V + previewHeight) * stride + 2 * uv.U + dx] = color;
								}
								else
									colors[(uv.V + previewHeight) * stride + uv.U] = color;
							}
						}
					}
				}
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp
			}

			if (mi.Button == MouseButton.Middle && mi.Event == MouseInputEvent.Up)
			{
				OnMiddleClick();
				return true;
			}
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
=======
		}

		int2 CellToMinimapPixel(CPos p)
		{
			var uv = p.ToMPos(world.Map);
			var dx = (int)(previewScale * cellWidth * (uv.U - world.Map.Bounds.Left));
			var dy = (int)(previewScale * (uv.V - world.Map.Bounds.Top));

			// Odd rows are shifted right by 1px
			if (isRectangularIsometric && (uv.V & 1) == 1)
				dx++;

			return new int2(mapRect.X + dx, mapRect.Y + dy);
		}

		CPos MinimapPixelToCell(int2 p)
		{
			var u = (int)((p.X - mapRect.X) / (previewScale * cellWidth)) + world.Map.Bounds.Left;
			var v = (int)((p.Y - mapRect.Y) / previewScale) + world.Map.Bounds.Top;
			return new MPos(u, v).ToCPos(world.Map);
		}

		public override void Removed()
		{
			base.Removed();
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp

			// Consume right/middle down events so they don't pass through
			if ((mi.Button == MouseButton.Right || mi.Button == MouseButton.Middle) && mi.Event == MouseInputEvent.Down)
				return true;

			return false;
		}

		public override Widget Clone() { return new ProductionTypeButtonWidget(this); }
	}
}
