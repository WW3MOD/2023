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
using OpenRA.Effects;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Graphics
{
	public sealed class WorldRenderer : IDisposable
	{
		// PITFALL (2026-05): -Pos.X / 16 is the X tiebreaker — gives every renderable a small "west-on-top" bias
		// at equal Y, so a row of sprites with baked-in right-edge shadows can't render east-of-west and reveal
		// the seam. One cell of X = 64 sort units, one cell of Y = 1024, so south-on-top still wins overall.
		// Actors that need east-on-top (e.g. fields whose baked shadow is on the right) flip this via
		// RenderSprites.XRenderOrder. Removing this term resurrects non-deterministic dark-seam bugs in dense
		// field/tree groups after copy-paste in the map editor.
		public static readonly Func<IRenderable, int> RenderableZPositionComparisonKey =
			r => r.Pos.Y + r.Pos.Z + r.ZOffset - r.Pos.X / 16;

		/// <summary>When true, renders order lines for all friendly units, not just selected ones.</summary>
		public bool ShowAllOrders { get; set; }

		public readonly Size TileSize;
		public readonly int TileScale;
		public readonly World World;
		public Viewport Viewport { get; }
		public readonly ITerrainLighting TerrainLighting;

		public event Action PaletteInvalidated = null;

		readonly HashSet<Actor> onScreenActors = new();
		readonly HardwarePalette palette = new();
		readonly Dictionary<string, PaletteReference> palettes = new();
		readonly IRenderTerrain terrainRenderer;
		readonly Lazy<DebugVisualizations> debugVis;
		readonly Func<string, PaletteReference> createPaletteReference;
		readonly bool enableDepthBuffer;

		readonly List<IFinalizedRenderable> preparedRenderables = new();
		readonly List<IFinalizedRenderable> preparedOverlayRenderables = new();
		readonly List<IFinalizedRenderable> preparedAnnotationRenderables = new();

		readonly List<IRenderable> renderablesBuffer = new();
		readonly IRenderer[] renderers;
		readonly IRenderPostProcessPass[] postProcessPasses;

		internal WorldRenderer(ModData modData, World world)
		{
			World = world;
			TileSize = World.Map.Grid.TileSize;
			TileScale = World.Map.Grid.TileScale;
			Viewport = new Viewport(this, world.Map);

			createPaletteReference = CreatePaletteReference;

			var mapGrid = modData.Manifest.Get<MapGrid>();
			enableDepthBuffer = mapGrid.EnableDepthBuffer;

			foreach (var pal in world.TraitDict.ActorsWithTrait<ILoadsPalettes>())
				pal.Trait.LoadPalettes(this);

			Player.SetupRelationshipColors(world.Players, world.LocalPlayer, this, true);

			palette.Initialize();

			TerrainLighting = world.WorldActor.TraitOrDefault<ITerrainLighting>();
			renderers = world.WorldActor.TraitsImplementing<IRenderer>().ToArray();
			terrainRenderer = world.WorldActor.TraitOrDefault<IRenderTerrain>();

			debugVis = Exts.Lazy(() => world.WorldActor.TraitOrDefault<DebugVisualizations>());

			postProcessPasses = world.WorldActor.TraitsImplementing<IRenderPostProcessPass>().ToArray();
		}

		public void BeginFrame()
		{
			foreach (var r in renderers)
				r.BeginFrame();
		}

		public void EndFrame()
		{
			foreach (var r in renderers)
				r.EndFrame();
		}

		public void UpdatePalettesForPlayer(string internalName, Color color, bool replaceExisting)
		{
			foreach (var pal in World.WorldActor.TraitsImplementing<ILoadsPlayerPalettes>())
				pal.LoadPlayerPalettes(this, internalName, color, replaceExisting);
		}

		PaletteReference CreatePaletteReference(string name)
		{
			var pal = palette.GetPalette(name);
			return new PaletteReference(name, palette.GetPaletteIndex(name), pal, palette);
		}

		public PaletteReference Palette(string name)
		{
			// HACK: This is working around the fact that palettes are defined on traits rather than sequences
			// and can be removed once this has been fixed.
			return string.IsNullOrEmpty(name) ? null : palettes.GetOrAdd(name, createPaletteReference);
		}

		public void AddPalette(string name, ImmutablePalette pal, bool allowModifiers = false, bool allowOverwrite = false)
		{
			if (allowOverwrite && palette.Contains(name))
				ReplacePalette(name, pal);
			else
			{
				var oldHeight = palette.Height;
				palette.AddPalette(name, pal, allowModifiers);

				if (oldHeight != palette.Height)
					PaletteInvalidated?.Invoke();
			}
		}

		public void ReplacePalette(string name, IPalette pal)
		{
			palette.ReplacePalette(name, pal);

			// Update cached PlayerReference if one exists
			if (palettes.TryGetValue(name, out var paletteReference))
				paletteReference.Palette = pal;
		}

		public void SetPaletteColorShift(string name, float hueOffset, float satOffset, float valueModifier, float minHue, float maxHue)
		{
			palette.SetColorShift(name, hueOffset, satOffset, valueModifier, minHue, maxHue);
		}

		// PERF: Avoid LINQ.
		void GenerateRenderables()
		{
			foreach (var actor in onScreenActors)
				renderablesBuffer.AddRange(actor.Render(this));

			renderablesBuffer.AddRange(World.WorldActor.Render(this));

			if (World.RenderPlayer != null)
				renderablesBuffer.AddRange(World.RenderPlayer.PlayerActor.Render(this));

			if (World.OrderGenerator != null)
				renderablesBuffer.AddRange(World.OrderGenerator.Render(this, World));

			// Unpartitioned effects
			foreach (var e in World.UnpartitionedEffects)
				renderablesBuffer.AddRange(e.Render(this));

			// Partitioned, currently on-screen effects
			foreach (var e in World.ScreenMap.RenderableEffectsInBox(Viewport.TopLeft, Viewport.BottomRight))
				renderablesBuffer.AddRange(e.Render(this));

			// Renderables must be ordered using a stable sorting algorithm to avoid flickering artefacts
			foreach (var renderable in renderablesBuffer.OrderBy(RenderableZPositionComparisonKey))
				preparedRenderables.Add(renderable.PrepareRender(this));

			// PERF: Reuse collection to avoid allocations.
			renderablesBuffer.Clear();
		}

		// PERF: Avoid LINQ.
		void GenerateOverlayRenderables()
		{
			World.ApplyToActorsWithTrait<IRenderAboveShroud>((actor, trait) =>
			{
				if (!actor.IsInWorld || actor.Disposed || (trait.SpatiallyPartitionable && !onScreenActors.Contains(actor)))
					return;

				foreach (var renderable in trait.RenderAboveShroud(actor, this))
					preparedOverlayRenderables.Add(renderable.PrepareRender(this));
			});

			if (ShowAllOrders)
			{
				// When showing all orders, render WhenSelected overlays for all friendly actors
				World.ApplyToActorsWithTrait<IRenderAboveShroudWhenSelected>((actor, trait) =>
				{
					if (!actor.IsInWorld || actor.Disposed || World.Selection.Contains(actor))
						return;

					if (!actor.Owner.IsAlliedWith(World.LocalPlayer))
						return;

					if (trait.SpatiallyPartitionable && !onScreenActors.Contains(actor))
						return;

					foreach (var renderable in trait.RenderAboveShroud(actor, this))
						preparedOverlayRenderables.Add(renderable.PrepareRender(this));
				});
			}

			foreach (var a in World.Selection.Actors)
			{
				if (!a.IsInWorld || a.Disposed)
					continue;

				foreach (var t in a.TraitsImplementing<IRenderAboveShroudWhenSelected>())
				{
					if (t.SpatiallyPartitionable && !onScreenActors.Contains(a))
						continue;

					foreach (var renderable in t.RenderAboveShroud(a, this))
						preparedOverlayRenderables.Add(renderable.PrepareRender(this));
				}
			}

			foreach (var e in World.Effects)
			{
				if (e is not IEffectAboveShroud ea)
					continue;

				foreach (var renderable in ea.RenderAboveShroud(this))
					preparedOverlayRenderables.Add(renderable.PrepareRender(this));
			}

			if (World.OrderGenerator != null)
				foreach (var renderable in World.OrderGenerator.RenderAboveShroud(this, World))
					preparedOverlayRenderables.Add(renderable.PrepareRender(this));
		}

		// PERF: Avoid LINQ.
		void GenerateAnnotationRenderables()
		{
			World.ApplyToActorsWithTrait<IRenderAnnotations>((actor, trait) =>
			{
				if (!actor.IsInWorld || actor.Disposed || (trait.SpatiallyPartitionable && !onScreenActors.Contains(actor)))
					return;

				foreach (var renderAnnotation in trait.RenderAnnotations(actor, this))
					preparedAnnotationRenderables.Add(renderAnnotation.PrepareRender(this));
			});

			if (ShowAllOrders)
			{
				// When showing all orders, render WhenSelected annotations for all friendly actors
				World.ApplyToActorsWithTrait<IRenderAnnotationsWhenSelected>((actor, trait) =>
				{
					if (!actor.IsInWorld || actor.Disposed || World.Selection.Contains(actor))
						return;

					if (!actor.Owner.IsAlliedWith(World.LocalPlayer))
						return;

					if (trait.SpatiallyPartitionable && !onScreenActors.Contains(actor))
						return;

					foreach (var renderAnnotation in trait.RenderAnnotations(actor, this))
						preparedAnnotationRenderables.Add(renderAnnotation.PrepareRender(this));
				});
			}

			foreach (var a in World.Selection.Actors)
			{
				if (!a.IsInWorld || a.Disposed)
					continue;

				foreach (var t in a.TraitsImplementing<IRenderAnnotationsWhenSelected>())
				{
					if (t.SpatiallyPartitionable && !onScreenActors.Contains(a))
						continue;

					foreach (var renderAnnotation in t.RenderAnnotations(a, this))
						preparedAnnotationRenderables.Add(renderAnnotation.PrepareRender(this));
				}
			}

			foreach (var e in World.Effects)
			{
				if (e is not IEffectAnnotation ea)
					continue;

				foreach (var renderAnnotation in ea.RenderAnnotation(this))
					preparedAnnotationRenderables.Add(renderAnnotation.PrepareRender(this));
			}

			if (World.OrderGenerator != null)
				foreach (var renderAnnotation in World.OrderGenerator.RenderAnnotations(this, World))
					preparedAnnotationRenderables.Add(renderAnnotation.PrepareRender(this));
		}

		public void PrepareRenderables()
		{
			if (World.WorldActor.Disposed)
				return;

			RefreshPalette();

			// PERF: Reuse collection to avoid allocations.
			onScreenActors.UnionWith(World.ScreenMap.RenderableActorsInBox(Viewport.TopLeft, Viewport.BottomRight));

			GenerateRenderables();
			GenerateOverlayRenderables();
			GenerateAnnotationRenderables();

			onScreenActors.Clear();
		}

		public void Draw()
		{
			if (World.WorldActor.Disposed)
				return;

			debugVis.Value?.UpdateDepthBuffer();

			// WW3MOD: Use full viewport bounds (not clamped to map edges) so that
			// actors, projectiles, and effects near/beyond map edges still render
			// (e.g. missiles at altitude, nuke mushroom clouds, aircraft flying off-map).
			var bounds = Viewport.GetScissorBounds(false);
			Game.Renderer.EnableScissor(bounds);

			if (enableDepthBuffer)
				Game.Renderer.Context.EnableDepthBuffer();

			terrainRenderer?.RenderTerrain(this, Viewport);

			// WW3MOD: Black out beyond-map area AFTER terrain but BEFORE actors.
			// Hides terrain tile overflow at edges, but actors/trees/projectiles
			// render on top so tall sprites near edges remain visible.
			if (World.Type != WorldType.Editor)
				DrawBeyondMapFog();

			Game.Renderer.Flush();

			for (var i = 0; i < preparedRenderables.Count; i++)
				preparedRenderables[i].Render(this);

			if (enableDepthBuffer)
				Game.Renderer.ClearDepthBuffer();

			ApplyPostProcessing(PostProcessPassType.AfterActors);

			World.ApplyToActorsWithTrait<IRenderAboveWorld>((actor, trait) =>
			{
				if (actor.IsInWorld && !actor.Disposed)
					trait.RenderAboveWorld(actor, this);
			});

			if (enableDepthBuffer)
				Game.Renderer.ClearDepthBuffer();

			ApplyPostProcessing(PostProcessPassType.AfterWorld);

			World.ApplyToActorsWithTrait<IRenderShroud>((actor, trait) => trait.RenderShroud(this));

			// WW3MOD: Extend fog overlay into the beyond-map area so actor sprites
			// that extend past the map boundary get the same fog as border cells.
			if (World.Type != WorldType.Editor)
				DrawBeyondMapActorFog();

			if (enableDepthBuffer)
				Game.Renderer.Context.DisableDepthBuffer();

			Game.Renderer.DisableScissor();

			// HACK: Keep old grouping behaviour
			var groupedOverlayRenderables = preparedOverlayRenderables.GroupBy(prs => prs.GetType());
			foreach (var g in groupedOverlayRenderables)
				foreach (var r in g)
					r.Render(this);

			ApplyPostProcessing(PostProcessPassType.AfterShroud);

			Game.Renderer.Flush();
		}

		void ApplyPostProcessing(PostProcessPassType type)
		{
			foreach (var pass in postProcessPasses)
			{
				if (pass.Type != type || !pass.Enabled)
					continue;

				Game.Renderer.Flush();
				pass.Draw(this);
			}
		}

		void DrawBeyondMapFog()
		{
			var map = World.Map;

			// Calculate map boundaries from Bounds directly.
			// ProjectedBottomRight.X has an off-by-one (uses br.U*scale-1 instead
			// of (br.U+1)*scale-1), excluding the last column. We compute correct
			// boundaries here to avoid modifying the shared Map formula.
			var bounds = map.Bounds;
			var tl = new WPos(bounds.Left * TileScale, bounds.Top * TileScale, 0);
			var br = new WPos(bounds.Right * TileScale, bounds.Bottom * TileScale, 0);
			var mapTL = ScreenPxPosition(tl);
			var mapBR = ScreenPxPosition(br);
			var vpTL = Viewport.TopLeft;
			var vpBR = Viewport.BottomRight;

			// Fully opaque black overlay — hides sprite overflow from edge cells
			// (trees, buildings whose visuals extend beyond their cell boundary)
			var fogColor = Color.FromArgb(255, 0, 0, 0);
			var cr = Game.Renderer.WorldRgbaColorRenderer;

			// Top strip (full width)
			if (vpTL.Y < mapTL.Y)
				cr.FillRect(new float3(vpTL.X, vpTL.Y, 0), new float3(vpBR.X, mapTL.Y, 0), fogColor);

			// Bottom strip (full width)
			if (vpBR.Y > mapBR.Y)
				cr.FillRect(new float3(vpTL.X, mapBR.Y, 0), new float3(vpBR.X, vpBR.Y, 0), fogColor);

			// Left strip (between map top and bottom only)
			if (vpTL.X < mapTL.X)
				cr.FillRect(new float3(vpTL.X, Math.Max(vpTL.Y, mapTL.Y), 0),
					new float3(mapTL.X, Math.Min(vpBR.Y, mapBR.Y), 0), fogColor);

			// Right strip (between map top and bottom only)
			if (vpBR.X > mapBR.X)
				cr.FillRect(new float3(mapBR.X, Math.Max(vpTL.Y, mapTL.Y), 0),
					new float3(vpBR.X, Math.Min(vpBR.Y, mapBR.Y), 0), fogColor);

			Game.Renderer.Flush();
		}

		// WW3MOD: Draw fog overlay in the beyond-map area AFTER actors and shroud,
		// so that actor sprite pixels extending beyond the map boundary get fogged
		// to match their border cell's visibility level.
		void DrawBeyondMapActorFog()
		{
			var renderPlayer = World.RenderPlayer;
			if (renderPlayer == null)
				return;

			var mapLayers = renderPlayer.MapLayers;
			if (mapLayers == null)
				return;

			var map = World.Map;
			var bounds = map.Bounds;
			var cr = Game.Renderer.WorldRgbaColorRenderer;
			var vpTL = Viewport.TopLeft;
			var vpBR = Viewport.BottomRight;

			// Precompute combined fog alpha for each visibility level (0-10).
			// For visibility V, fog layers V through 9 are drawn by ShroudRenderer.
			// Combined alpha = 1 - product of (1 - layerAlpha) for each layer.
			var fogAlphas = new float[MapLayers.VisionLayers];
			fogAlphas[0] = 1f;
			fogAlphas[10] = 0f;
			for (var v = 1; v < 10; v++)
			{
				var transparency = 1f;
				for (var layer = v; layer <= 9; layer++)
				{
					var a = 1f;
					if (layer > 1)
						a -= (layer - 1) / 12f;
					a /= 3f;
					transparency *= 1f - a;
				}

				fogAlphas[v] = 1f - transparency;
			}

			// Map boundary in screen coordinates
			var tl = new WPos(bounds.Left * TileScale, bounds.Top * TileScale, 0);
			var br = new WPos(bounds.Right * TileScale, bounds.Bottom * TileScale, 0);
			var mapTL = ScreenPxPosition(tl);
			var mapBR = ScreenPxPosition(br);

			// Cell dimensions in screen pixels
			var mapWidth = bounds.Right - bounds.Left;
			var mapHeight = bounds.Bottom - bounds.Top;
			if (mapWidth <= 0 || mapHeight <= 0)
				return;

			var cellW = (mapBR.X - mapTL.X) / mapWidth;
			var cellH = (mapBR.Y - mapTL.Y) / mapHeight;

			// Draw fog strips extending beyond each map edge, one per border cell.
			// Adjacent cells with the same visibility are batched into a single rect.

			// Top edge
			if (vpTL.Y < mapTL.Y)
			{
				var x = bounds.Left;
				while (x < bounds.Right)
				{
					var puv = (PPos)new CPos(x, bounds.Top).ToMPos(map);
					var vis = mapLayers.GetVisibility(puv);

					// Batch adjacent cells with same visibility
					var xEnd = x + 1;
					while (xEnd < bounds.Right)
					{
						var nextPuv = (PPos)new CPos(xEnd, bounds.Top).ToMPos(map);
						if (mapLayers.GetVisibility(nextPuv) != vis)
							break;
						xEnd++;
					}

					var alpha = fogAlphas[vis];
					if (alpha > 0.01f)
					{
						var fogColor = Color.FromArgb((int)(alpha * 255), 0, 0, 0);
						var left = mapTL.X + (x - bounds.Left) * cellW;
						var right = mapTL.X + (xEnd - bounds.Left) * cellW;
						cr.FillRect(new float3(left, vpTL.Y, 0), new float3(right, mapTL.Y, 0), fogColor);
					}

					x = xEnd;
				}
			}

			// Bottom edge
			if (vpBR.Y > mapBR.Y)
			{
				var x = bounds.Left;
				while (x < bounds.Right)
				{
					var puv = (PPos)new CPos(x, bounds.Bottom - 1).ToMPos(map);
					var vis = mapLayers.GetVisibility(puv);

					var xEnd = x + 1;
					while (xEnd < bounds.Right)
					{
						var nextPuv = (PPos)new CPos(xEnd, bounds.Bottom - 1).ToMPos(map);
						if (mapLayers.GetVisibility(nextPuv) != vis)
							break;
						xEnd++;
					}

					var alpha = fogAlphas[vis];
					if (alpha > 0.01f)
					{
						var fogColor = Color.FromArgb((int)(alpha * 255), 0, 0, 0);
						var left = mapTL.X + (x - bounds.Left) * cellW;
						var right = mapTL.X + (xEnd - bounds.Left) * cellW;
						cr.FillRect(new float3(left, mapBR.Y, 0), new float3(right, vpBR.Y, 0), fogColor);
					}

					x = xEnd;
				}
			}

			// Left edge (between map top and bottom)
			if (vpTL.X < mapTL.X)
			{
				var y = bounds.Top;
				while (y < bounds.Bottom)
				{
					var puv = (PPos)new CPos(bounds.Left, y).ToMPos(map);
					var vis = mapLayers.GetVisibility(puv);

					var yEnd = y + 1;
					while (yEnd < bounds.Bottom)
					{
						var nextPuv = (PPos)new CPos(bounds.Left, yEnd).ToMPos(map);
						if (mapLayers.GetVisibility(nextPuv) != vis)
							break;
						yEnd++;
					}

					var alpha = fogAlphas[vis];
					if (alpha > 0.01f)
					{
						var fogColor = Color.FromArgb((int)(alpha * 255), 0, 0, 0);
						var top = mapTL.Y + (y - bounds.Top) * cellH;
						var bottom = mapTL.Y + (yEnd - bounds.Top) * cellH;
						cr.FillRect(new float3(vpTL.X, top, 0), new float3(mapTL.X, bottom, 0), fogColor);
					}

					y = yEnd;
				}
			}

			// Right edge (between map top and bottom)
			if (vpBR.X > mapBR.X)
			{
				var y = bounds.Top;
				while (y < bounds.Bottom)
				{
					var puv = (PPos)new CPos(bounds.Right - 1, y).ToMPos(map);
					var vis = mapLayers.GetVisibility(puv);

					var yEnd = y + 1;
					while (yEnd < bounds.Bottom)
					{
						var nextPuv = (PPos)new CPos(bounds.Right - 1, yEnd).ToMPos(map);
						if (mapLayers.GetVisibility(nextPuv) != vis)
							break;
						yEnd++;
					}

					var alpha = fogAlphas[vis];
					if (alpha > 0.01f)
					{
						var fogColor = Color.FromArgb((int)(alpha * 255), 0, 0, 0);
						var top = mapTL.Y + (y - bounds.Top) * cellH;
						var bottom = mapTL.Y + (yEnd - bounds.Top) * cellH;
						cr.FillRect(new float3(mapBR.X, top, 0), new float3(vpBR.X, bottom, 0), fogColor);
					}

					y = yEnd;
				}
			}

			// Corners: use nearest corner cell's visibility
			// Top-left
			if (vpTL.Y < mapTL.Y && vpTL.X < mapTL.X)
			{
				var vis = mapLayers.GetVisibility((PPos)new CPos(bounds.Left, bounds.Top).ToMPos(map));
				var alpha = fogAlphas[vis];
				if (alpha > 0.01f)
					cr.FillRect(new float3(vpTL.X, vpTL.Y, 0), new float3(mapTL.X, mapTL.Y, 0),
						Color.FromArgb((int)(alpha * 255), 0, 0, 0));
			}

			// Top-right
			if (vpTL.Y < mapTL.Y && vpBR.X > mapBR.X)
			{
				var vis = mapLayers.GetVisibility((PPos)new CPos(bounds.Right - 1, bounds.Top).ToMPos(map));
				var alpha = fogAlphas[vis];
				if (alpha > 0.01f)
					cr.FillRect(new float3(mapBR.X, vpTL.Y, 0), new float3(vpBR.X, mapTL.Y, 0),
						Color.FromArgb((int)(alpha * 255), 0, 0, 0));
			}

			// Bottom-left
			if (vpBR.Y > mapBR.Y && vpTL.X < mapTL.X)
			{
				var vis = mapLayers.GetVisibility((PPos)new CPos(bounds.Left, bounds.Bottom - 1).ToMPos(map));
				var alpha = fogAlphas[vis];
				if (alpha > 0.01f)
					cr.FillRect(new float3(vpTL.X, mapBR.Y, 0), new float3(mapTL.X, vpBR.Y, 0),
						Color.FromArgb((int)(alpha * 255), 0, 0, 0));
			}

			// Bottom-right
			if (vpBR.Y > mapBR.Y && vpBR.X > mapBR.X)
			{
				var vis = mapLayers.GetVisibility((PPos)new CPos(bounds.Right - 1, bounds.Bottom - 1).ToMPos(map));
				var alpha = fogAlphas[vis];
				if (alpha > 0.01f)
					cr.FillRect(new float3(mapBR.X, mapBR.Y, 0), new float3(vpBR.X, vpBR.Y, 0),
						Color.FromArgb((int)(alpha * 255), 0, 0, 0));
			}

			Game.Renderer.Flush();
		}

		public void DrawAnnotations()
		{
			Game.Renderer.EnableAntialiasingFilter();
			for (var i = 0; i < preparedAnnotationRenderables.Count; i++)
				preparedAnnotationRenderables[i].Render(this);
			Game.Renderer.DisableAntialiasingFilter();

			// Engine debugging overlays
			if (debugVis.Value != null && debugVis.Value.RenderGeometry)
			{
				for (var i = 0; i < preparedRenderables.Count; i++)
					preparedRenderables[i].RenderDebugGeometry(this);

				for (var i = 0; i < preparedOverlayRenderables.Count; i++)
					preparedOverlayRenderables[i].RenderDebugGeometry(this);

				for (var i = 0; i < preparedAnnotationRenderables.Count; i++)
					preparedAnnotationRenderables[i].RenderDebugGeometry(this);
			}

			if (debugVis.Value != null && debugVis.Value.ScreenMap)
			{
				foreach (var r in World.ScreenMap.RenderBounds(World.RenderPlayer))
				{
					var tl = Viewport.WorldToViewPx(new float2(r.Left, r.Top));
					var br = Viewport.WorldToViewPx(new float2(r.Right, r.Bottom));
					Game.Renderer.RgbaColorRenderer.DrawRect(tl, br, 1, Color.MediumSpringGreen);
				}

				foreach (var b in World.ScreenMap.MouseBounds(World.RenderPlayer))
				{
					var points = new float2[b.Vertices.Length];
					for (var index = 0; index < b.Vertices.Length; index++)
					{
						var vertex = b.Vertices[index];
						points[index] = Viewport.WorldToViewPx(vertex).ToFloat2();
					}

					Game.Renderer.RgbaColorRenderer.DrawPolygon(points, 1, Color.OrangeRed);
				}
			}

			Game.Renderer.Flush();

			preparedRenderables.Clear();
			preparedOverlayRenderables.Clear();
			preparedAnnotationRenderables.Clear();
		}

		public void RefreshPalette()
		{
			palette.ApplyModifiers(World.WorldActor.TraitsImplementing<IPaletteModifier>());
			Game.Renderer.SetPalette(palette);
		}

		// Conversion between world and screen coordinates
		public float2 ScreenPosition(WPos pos)
		{
			return new float2((float)TileSize.Width * pos.X / TileScale, (float)TileSize.Height * (pos.Y - pos.Z) / TileScale);
		}

		public float3 Screen3DPosition(WPos pos)
		{
			// The projection from world coordinates to screen coordinates has
			// a non-obvious relationship between the y and z coordinates:
			// * A flat surface with constant y (e.g. a vertical wall) in world coordinates
			//   transforms into a flat surface with constant z (depth) in screen coordinates.
			// * Increasing the world y coordinate increases screen y and z coordinates equally.
			// * Increases the world z coordinate decreases screen y but doesn't change screen z.
			var z = pos.Y * (float)TileSize.Height / TileScale;
			return new float3((float)TileSize.Width * pos.X / TileScale, (float)TileSize.Height * (pos.Y - pos.Z) / TileScale, z);
		}

		public int2 ScreenPxPosition(WPos pos)
		{
			// Round to nearest pixel
			var px = ScreenPosition(pos);
			return new int2((int)Math.Round(px.X), (int)Math.Round(px.Y));
		}

		public float3 Screen3DPxPosition(WPos pos)
		{
			// Round to nearest pixel
			var px = Screen3DPosition(pos);
			return new float3((float)Math.Round(px.X), (float)Math.Round(px.Y), px.Z);
		}

		// For scaling vectors to pixel sizes in the model renderer
		public float3 ScreenVectorComponents(in WVec vec)
		{
			return new float3(
				(float)TileSize.Width * vec.X / TileScale,
				(float)TileSize.Height * (vec.Y - vec.Z) / TileScale,
				(float)TileSize.Height * vec.Z / TileScale);
		}

		// For scaling vectors to pixel sizes in the model renderer
		public float[] ScreenVector(in WVec vec)
		{
			var xyz = ScreenVectorComponents(vec);
			return new[] { xyz.X, xyz.Y, xyz.Z, 1f };
		}

		public int2 ScreenPxOffset(in WVec vec)
		{
			// Round to nearest pixel
			var xyz = ScreenVectorComponents(vec);
			return new int2((int)Math.Round(xyz.X), (int)Math.Round(xyz.Y));
		}

		/// <summary>
		/// Returns a position in the world that is projected to the given screen position.
		/// There are many possible world positions, and the returned value chooses the value with no elevation.
		/// </summary>
		public WPos ProjectedPosition(int2 screenPx)
		{
			return new WPos(TileScale * screenPx.X / TileSize.Width, TileScale * screenPx.Y / TileSize.Height, 0);
		}

		public void Dispose()
		{
			// HACK: Disposing the world from here violates ownership
			// but the WorldRenderer lifetime matches the disposal
			// behavior we want for the world, and the root object setup
			// is so horrible that doing it properly would be a giant mess.
			World.Dispose();

			palette.Dispose();
		}
	}
}
