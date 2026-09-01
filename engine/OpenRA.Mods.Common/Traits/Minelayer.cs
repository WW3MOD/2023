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
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Orders;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public class MinelayerInfo : TraitInfo
	{
		[ActorReference]
		public readonly string Mine = "minv";

		public readonly string AmmoPoolName = "primary";

		public readonly WDist MinefieldDepth = new(1536);

		[VoiceReference]
		[Desc("Voice to use when ordered to lay a minefield.")]
		public readonly string Voice = "Action";

		[Desc("Color to use for the target line when laying mines.")]
		public readonly Color TargetLineColor = Color.Crimson;

		[Desc("Sprite overlay to use for valid minefield cells.")]
		public readonly string TileValidName = "build-valid";

		[Desc("Sprite overlay to use for invalid minefield cells.")]
		public readonly string TileInvalidName = "build-invalid";

		[Desc("Sprite overlay to use for minefield cells hidden behind fog or shroud.")]
		public readonly string TileUnknownName = "build-unknown";

		[Desc("Only allow laying mines on listed terrain types. Leave empty to allow all terrain types.")]
		public readonly HashSet<string> TerrainTypes = new();

		[CursorReference]
		[Desc("Cursor to display when able to lay a mine.")]
		public readonly string DeployCursor = "deploy";

		[CursorReference]
		[Desc("Cursor to display when unable to lay a mine.")]
		public readonly string DeployBlockedCursor = "deploy-blocked";

		[CursorReference]
		[Desc("Cursor to display when able to lay a mine.")]
		public readonly string AbilityCursor = "ability";

		[Desc("Ammo the minelayer consumes per mine.")]
		public readonly int AmmoUsage = 1;

		[Desc("Number of ticks it takes to lay a mine.")]
		public readonly int PreLayDelay = 0;

		[Desc("Number of ticks for the minelayer to wait after laying a mine. The wait can be interrupted by a player order.")]
		public readonly int AfterLayingDelay = 20;

		public override object Create(ActorInitializer init) { return new Minelayer(init.Self, this); }
	}

	public class Minelayer : IIssueOrder, IResolveOrder, ISync, IIssueDeployOrder, IOrderVoice, ITick
	{
		public readonly MinelayerInfo Info;
		public readonly Sprite Tile;

		readonly Actor self;

		[Sync]
		CPos minefieldStart;

		public Minelayer(Actor self, MinelayerInfo info)
		{
			Info = info;
			this.self = self;

			var tileset = self.World.Map.Tileset.ToLowerInvariant();
			var sequences = self.World.Map.Sequences;
			if (sequences.HasSequence("overlay", $"{Info.TileValidName}-{tileset}"))
				Tile = sequences.GetSequence("overlay", $"{Info.TileValidName}-{tileset}").GetSprite(0);
			else
				Tile = sequences.GetSequence("overlay", Info.TileValidName).GetSprite(0);
		}

		IEnumerable<IOrderTargeter> IIssueOrder.Orders
		{
			get
			{
				yield return new BeginMinefieldOrderTargeter(Info.AbilityCursor);
				yield return new DeployOrderTargeter("PlaceMine", 5,
					() => NoOneCanLayHere() ? Info.DeployBlockedCursor : Info.DeployCursor);
			}
		}

		/// <summary>
		/// True when no minelayer in the selection is standing somewhere it could lay, so the deploy
		/// cursor should read blocked. Same polarity as attack-move and guard, same combinator.
		/// </summary>
		/// <remarks>
		/// <para>ONLY the terrain term. There is deliberately NO ammo term here, and adding one is a
		/// false refusal — this was tried on this branch and removed after review. ResolveOrder
		/// (below) has no ammo test either, and a dry minelayer given PlaceMine does NOT fail:
		/// LayMines.cs:94 detects the empty pool and, when a rearm host exists, queues
		/// MoveAdjacentTo + MoveTo + Resupply and carries on laying (`:96-112`). It gives up only at
		/// `:103-104`, when rearmTarget is null. MNLY carries `Rearmable: RearmActors:
		/// logisticscenter` (vehicles.yaml:506-508) and the infantry minelayer self-reloads via
		/// ReloadAmmoPool, so in this mod the order is accepted and completes either way. Painting
		/// it blocked would cost the player the automatic rearm-and-lay the engine already provides.
		/// LayMines.cs:94 is the REARM TRIGGER — it is the proof the order is honoured, not a
		/// refusal.</para>
		///
		/// <para>Resolved over the selection rather than per-actor for the same reason as
		/// AttackMove: UnitOrderGenerator.CursorForOrders keeps the FIRST result on a priority tie,
		/// so one minelayer parked on a bad cell at the head of a healthy selection would otherwise
		/// paint the whole click blocked.</para>
		/// </remarks>
		bool NoOneCanLayHere()
		{
			var candidates = self.World.Selection.Contains(self)
				? self.World.Selection.Actors
				: new[] { self };

			// Each minelayer is judged on ITS OWN cell: DeployOrderTargeter issues a per-actor
			// PlaceMine at that actor's location, and ResolveOrder re-tests the same cell.
			// The Disposed/IsDead guard runs FIRST: Selection prunes per world tick but this is
			// evaluated from the render thread, so a just-killed selection member can still be
			// present, and the cannotAct lambda's Trait<Minelayer>() throws on a disposed actor
			// (same crash class as AttackMove.CanBeOrderedToAttackMove — see its remarks).
			// ReadsAsBlocked checks canReceiveOrder before cannotAct, so guarding here covers both.
			return Memo.ReadsAsBlocked(
				self.World,
				candidates,
				false,
				a => !a.Disposed && !a.IsDead && a.Info.HasTraitInfo<MinelayerInfo>(),
				a => !a.Trait<Minelayer>().IsCellAcceptable(a, a.Location));
		}

		/// <summary>Shared across every actor's targeter — see SelectionMemo.</summary>
		static readonly SelectionMemo Memo = new();

		Order IIssueOrder.IssueOrder(Actor self, IOrderTargeter order, in Target target, bool queued)
		{
			switch (order.OrderID)
			{
				case "BeginMinefield":
					return BeginMinefield(self, target, queued);
				case "PlaceMine":
					return new Order("PlaceMine", self, Target.FromCell(self.World, self.Location), queued);
				default:
					return null;
			}
		}

		public static Order BeginMinefield(Actor self, Target target, bool queued)
		{
			var start = self.World.Map.CellContaining(target.CenterPosition);
			if (self.World.OrderGenerator is MinefieldOrderGenerator generator)
				generator.AddMinelayer(self);
			else
				self.World.OrderGenerator = new MinefieldOrderGenerator(self, start, queued);

			return new Order("BeginMinefield", self, Target.FromCell(self.World, start), queued);
		}

		Order IIssueDeployOrder.IssueDeployOrder(Actor self, bool queued)
		{
			return new Order("PlaceMine", self, Target.FromCell(self.World, self.Location), queued);
		}

		bool IIssueDeployOrder.CanIssueDeployOrder(Actor self, bool queued)
		{
			return IsCellAcceptable(self, self.Location);
		}

		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString != "BeginMinefield" && order.OrderString != "PlaceMinefield" && order.OrderString != "PlaceMine")
				return;

			if (!order.Target.IsValidFor(self))
				return;

			var cell = self.World.Map.CellContaining(order.Target.CenterPosition);
			if (order.OrderString == "BeginMinefield")
				minefieldStart = cell;
			else if (order.OrderString == "PlaceMine")
			{
				if (IsCellAcceptable(self, cell))
					self.QueueActivity(order.Queued, new LayMines(self));
			}
			else if (order.OrderString == "PlaceMinefield")
			{
				// A different minelayer might have started laying the field without this minelayer knowing the start
				minefieldStart = order.ExtraLocation;

				var movement = self.Trait<IPositionable>();

				var minefield = GetMinefieldCells(minefieldStart, cell, Info.MinefieldDepth)
					.Where(c => IsCellAcceptable(self, c) && self.Owner.MapLayers.IsExplored(c)
						&& movement.CanEnterCell(c, null, BlockedByActor.Immovable) && movement is Mobile mobile && mobile.CanStayInCell(c))
					.OrderBy(c => (c - minefieldStart).LengthSquared).ToList();

				self.QueueActivity(order.Queued, new LayMines(self, minefield));
				self.ShowTargetLines();
			}
		}

		void ITick.Tick(Actor self)
		{
			if (self.CurrentActivity != null)
				foreach (var field in self.CurrentActivity.ActivitiesImplementing<LayMines>())
					field.CleanMineField(self);
		}

		string IOrderVoice.VoicePhraseForOrder(Actor self, Order order)
		{
			if (order.OrderString == "PlaceMine" || order.OrderString == "PlaceMinefield")
				return Info.Voice;

			return null;
		}

		static IEnumerable<CPos> GetMinefieldCells(CPos start, CPos end, WDist depth)
		{
			var mins = new CPos(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y));
			var maxs = new CPos(Math.Max(start.X, end.X), Math.Max(start.Y, end.Y));

			// TODO: proper endcaps, if anyone cares (which won't happen unless depth is large)
			var p = end - start;
			var q = new float2(p.Y, -p.X);
			q = (start != end) ? 1 / q.Length * q : new float2(1, 0);
			var c = -float2.Dot(q, new float2(start.X, start.Y));

			// return all points such that |ax + by + c| < depth
			// HACK: This will return the wrong results for isometric cells
			for (var i = mins.X; i <= maxs.X; i++)
				for (var j = mins.Y; j <= maxs.Y; j++)
					if (Math.Abs(q.X * i + q.Y * j + c) * 1024 < depth.Length)
						yield return new CPos(i, j);
		}

		public bool IsCellAcceptable(Actor self, CPos cell)
		{
			if (!self.World.Map.Contains(cell))
				return false;

			if (Info.TerrainTypes.Count == 0)
				return true;

			var terrainType = self.World.Map.GetTerrainInfo(cell).Type;
			return Info.TerrainTypes.Contains(terrainType);
		}

		sealed class MinefieldOrderGenerator : OrderGenerator
		{
			readonly List<Actor> minelayers;
			readonly Minelayer minelayer;
			readonly Sprite validTile, unknownTile, blockedTile;
			readonly float validAlpha, unknownAlpha, blockedAlpha;
			readonly CPos minefieldStart;
			readonly bool queued;
			readonly string cursor;

			public MinefieldOrderGenerator(Actor a, CPos xy, bool queued)
			{
				minelayers = new List<Actor>() { a };
				minefieldStart = xy;
				this.queued = queued;

				minelayer = a.Trait<Minelayer>();
				var tileset = a.World.Map.Tileset.ToLowerInvariant();
				var sequences = a.World.Map.Sequences;
				if (sequences.HasSequence("overlay", $"{minelayer.Info.TileValidName}-{tileset}"))
				{
					var validSequence = sequences.GetSequence("overlay", $"{minelayer.Info.TileValidName}-{tileset}");
					validTile = validSequence.GetSprite(0);
					validAlpha = validSequence.GetAlpha(0);
				}
				else
				{
					var validSequence = sequences.GetSequence("overlay", minelayer.Info.TileValidName);
					validTile = validSequence.GetSprite(0);
					validAlpha = validSequence.GetAlpha(0);
				}

				if (sequences.HasSequence("overlay", $"{minelayer.Info.TileUnknownName}-{tileset}"))
				{
					var unknownSequence = sequences.GetSequence("overlay", $"{minelayer.Info.TileUnknownName}-{tileset}");
					unknownTile = unknownSequence.GetSprite(0);
					unknownAlpha = unknownSequence.GetAlpha(0);
				}
				else
				{
					var unknownSequence = sequences.GetSequence("overlay", minelayer.Info.TileUnknownName);
					unknownTile = unknownSequence.GetSprite(0);
					unknownAlpha = unknownSequence.GetAlpha(0);
				}

				if (sequences.HasSequence("overlay", $"{minelayer.Info.TileInvalidName}-{tileset}"))
				{
					var blockedSequence = sequences.GetSequence("overlay", $"{minelayer.Info.TileInvalidName}-{tileset}");
					blockedTile = blockedSequence.GetSprite(0);
					blockedAlpha = blockedSequence.GetAlpha(0);
				}
				else
				{
					var blockedSequence = sequences.GetSequence("overlay", minelayer.Info.TileInvalidName);
					blockedTile = blockedSequence.GetSprite(0);
					blockedAlpha = blockedSequence.GetAlpha(0);
				}

				cursor = minelayer.Info.AbilityCursor;
			}

			public void AddMinelayer(Actor a)
			{
				minelayers.Add(a);
			}

			protected override IEnumerable<Order> OrderInner(World world, CPos cell, int2 worldPixel, MouseInput mi)
			{
				if (mi.Button == Game.Settings.Game.MouseButtonPreference.Cancel)
				{
					world.CancelInputMode();
					yield break;
				}

				if (mi.Button == Game.Settings.Game.MouseButtonPreference.Action)
				{
					minelayers[0].World.CancelInputMode();
					foreach (var minelayer in minelayers)
						yield return new Order("PlaceMinefield", minelayer, Target.FromCell(world, cell), queued) { ExtraLocation = minefieldStart };
				}
			}

			protected override void SelectionChanged(World world, IEnumerable<Actor> selected)
			{
				minelayers.Clear();
				minelayers.AddRange(selected.Where(s => !s.IsDead && s.Info.HasTraitInfo<MinelayerInfo>()));
				if (minelayers.Count == 0)
					world.CancelInputMode();
			}

			protected override IEnumerable<IRenderable> Render(WorldRenderer wr, World world) { yield break; }
			protected override IEnumerable<IRenderable> RenderAboveShroud(WorldRenderer wr, World world)
			{
				var minelayer = minelayers.FirstOrDefault(m => m.IsInWorld && !m.IsDead);
				if (minelayer == null)
					yield break;

				// We get the biggest depth so we cover all cells that mines could be placed on.
				var lastMousePos = wr.Viewport.ViewToWorld(Viewport.LastMousePos);
				var minefield = GetMinefieldCells(minefieldStart, lastMousePos,
					minelayers.Max(m => m.Info.TraitInfo<MinelayerInfo>().MinefieldDepth));

				var movement = minelayer.Trait<IPositionable>();
				var mobile = movement as Mobile;
				var pal = wr.Palette(TileSet.TerrainPaletteInternalName);
				foreach (var c in minefield)
				{
					var tile = validTile;
					var alpha = validAlpha;
					if (!world.Map.Contains(c))
					{
						tile = blockedTile;
						alpha = blockedAlpha;
					}
					else if (world.ShroudObscures(c))
					{
						tile = blockedTile;
						alpha = blockedAlpha;
					}
					else if (world.FogObscures(c))
					{
						tile = unknownTile;
						alpha = unknownAlpha;
					}
					else if (!this.minelayer.IsCellAcceptable(minelayer, c)
						|| !movement.CanEnterCell(c, null, BlockedByActor.Immovable) || (mobile != null && !mobile.CanStayInCell(c)))
					{
						tile = blockedTile;
						alpha = blockedAlpha;
					}

					yield return new SpriteRenderable(tile, world.Map.CenterOfCell(c), WVec.Zero, -511, pal, 1f, alpha, float3.Ones, TintModifiers.IgnoreWorldTint, true);
				}
			}

			protected override IEnumerable<IRenderable> RenderAnnotations(WorldRenderer wr, World world) { yield break; }

			protected override string GetCursor(World world, CPos cell, int2 worldPixel, MouseInput mi)
			{
				return cursor;
			}
		}

		sealed class BeginMinefieldOrderTargeter : IOrderTargeter
		{
			public string OrderID => "BeginMinefield";
			public int OrderPriority => 7;

			readonly string cursor;

			public BeginMinefieldOrderTargeter(string cursor)
			{
				this.cursor = cursor;
			}

			public bool TargetOverridesSelection(Actor self, in Target target, List<Actor> actorsAt, CPos xy, TargetModifiers modifiers) { return true; }

			public bool CanTarget(Actor self, in Target target, List<Actor> othersAtTarget, CPos xy, TargetModifiers modifiers, ref string cursor)
			{
				if (target.Type != TargetType.Terrain)
					return false;

				var location = self.World.Map.CellContaining(target.CenterPosition);
				if (!self.World.Map.Contains(location))
					return false;

				cursor = this.cursor;

				IsQueued = modifiers.HasModifier(TargetModifiers.ForceQueue);

				return modifiers.HasModifier(TargetModifiers.ForceAttack);
			}

			public bool IsQueued { get; private set; }
		}
	}
}
