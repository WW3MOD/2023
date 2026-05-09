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
using OpenRA.Mods.Common.Effects;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	// Order types a rally-point waypoint can carry. Each produced unit replays the
	// type per waypoint, mirroring the modifier-key semantics used for normal unit
	// orders: default = Move, Alt = AttackMove, Ctrl = ForceMove. Reserved values
	// allow extending to Capture / EnterTransport / etc. without re-encoding.
	public enum RallyOrderType : byte
	{
		Move = 0,
		AttackMove = 1,
		ForceMove = 2,

		// 3..7 reserved for future per-waypoint orders. Bit width in Order.ExtraData
		// is 3 bits (see Pack/Unpack below) so values up to 7 are safe today.
	}

	public readonly struct RallyPointWaypoint : IEquatable<RallyPointWaypoint>
	{
		public readonly CPos Cell;
		public readonly RallyOrderType OrderType;

		public RallyPointWaypoint(CPos cell, RallyOrderType orderType)
		{
			Cell = cell;
			OrderType = orderType;
		}

		public bool Equals(RallyPointWaypoint other) => Cell == other.Cell && OrderType == other.OrderType;
		public override bool Equals(object obj) => obj is RallyPointWaypoint w && Equals(w);
		public override int GetHashCode() => Cell.GetHashCode() ^ ((int)OrderType << 24);
	}

	[Desc("Used to waypoint units after production or repair is finished.")]
	public class RallyPointInfo : TraitInfo
	{
		public readonly string Image = "rallypoint";

		[Desc("Width (in pixels) of the rallypoint line.")]
		public readonly int LineWidth = 1;

		[SequenceReference(nameof(Image), allowNullImage: true)]
		public readonly string FlagSequence = "flag";

		[SequenceReference(nameof(Image), allowNullImage: true)]
		public readonly string CirclesSequence = "circles";

		[CursorReference]
		[Desc("Cursor to display when rally point can be set (default Move).")]
		public readonly string Cursor = "ability";

		[CursorReference]
		[Desc("Cursor for Alt-modified rally point clicks — produced units attack-move to the waypoint.")]
		public readonly string AttackMoveCursor = "attackmove";

		[CursorReference]
		[Desc("Cursor for Ctrl-modified rally point clicks — produced units force-move to the waypoint.")]
		public readonly string ForceMoveCursor = "ability";

		[PaletteReference(nameof(IsPlayerPalette))]
		[Desc("Custom indicator palette name")]
		public readonly string Palette = "player";

		[Desc("Custom palette is a player palette BaseName")]
		public readonly bool IsPlayerPalette = true;

		[Desc("A list of 0 or more offsets defining the initial rally point path.")]
		public readonly CVec[] Path = Array.Empty<CVec>();

		[NotificationReference("Speech")]
		[Desc("Speech notification to play when setting a new rallypoint.")]
		public readonly string Notification = null;

		[FluentReference(optional: true)]
		[Desc("Text notification to display when setting a new rallypoint.")]
		public readonly string TextNotification = null;

		[Desc("Used to group equivalent actors to allow force-setting a rallypoint (e.g. for Primary production).")]
		public readonly string ForceSetType = null;

		public override object Create(ActorInitializer init) { return new RallyPoint(init.Self, this); }
	}

	public class RallyPoint : IIssueOrder, IResolveOrder, INotifyOwnerChanged, INotifyCreated, INotifyAddedToWorld, INotifyRemovedFromWorld
	{
		const string OrderID = "SetRallyPoint";

		// Order.ExtraData layout:
		//   bit 0     = ForceSet (existing — primary-production override)
		//   bits 1..3 = RallyOrderType (3 bits, values 0..7)
		const uint ForceSetBit = 1u << 0;
		const int OrderTypeShift = 1;
		const uint OrderTypeMask = 0x7u << OrderTypeShift;

		// Per-waypoint queue. Default-set waypoints (from Info.Path) start as Move.
		public List<RallyPointWaypoint> Path;

		public RallyPointInfo Info;
		public string PaletteName { get; private set; }
		RallyPointIndicator effect;

		// Convenience for code that only cares about cells (indicator path render,
		// indicator change-detection). Order-type info stays on Path.
		public IEnumerable<CPos> Cells => Path.Select(w => w.Cell);

		public void ResetPath(Actor self)
		{
			Path = Info.Path.Select(p => new RallyPointWaypoint(self.Location + p, RallyOrderType.Move)).ToList();
		}

		public RallyPoint(Actor self, RallyPointInfo info)
		{
			Info = info;
			ResetPath(self);
			PaletteName = info.IsPlayerPalette ? info.Palette + self.Owner.InternalName : info.Palette;
		}

		void INotifyCreated.Created(Actor self)
		{
			effect = new RallyPointIndicator(self, this);
		}

		public void OnOwnerChanged(Actor self, Player oldOwner, Player newOwner)
		{
			if (Info.IsPlayerPalette)
				PaletteName = Info.Palette + newOwner.InternalName;

			ResetPath(self);
		}

		public IEnumerable<IOrderTargeter> Orders
		{
			get { yield return new RallyPointOrderTargeter(Info); }
		}

		public Order IssueOrder(Actor self, IOrderTargeter order, in Target target, bool queued)
		{
			if (order.OrderID == OrderID)
			{
				Game.Sound.PlayNotification(self.World.Map.Rules, self.Owner, "Speech", Info.Notification, self.Owner.Faction.InternalName);
				TextNotificationsManager.AddTransientLine(self.Owner, Info.TextNotification);

				var targeter = (RallyPointOrderTargeter)order;
				var extra = 0u;
				if (targeter.ForceSet)
					extra |= ForceSetBit;
				extra |= ((uint)targeter.OrderType << OrderTypeShift) & OrderTypeMask;

				return new Order(order.OrderID, self, target, queued)
				{
					SuppressVisualFeedback = true,
					ExtraData = extra,
				};
			}

			return null;
		}

		public void ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "Stop")
			{
				Path.Clear();
				return;
			}

			if (order.OrderString != OrderID)
				return;

			if (!order.Target.IsValidFor(self))
				return;

			if (!order.Queued)
				Path.Clear();

			var orderType = (RallyOrderType)((order.ExtraData & OrderTypeMask) >> OrderTypeShift);
			var cell = self.World.Map.CellContaining(order.Target.CenterPosition);
			Path.Add(new RallyPointWaypoint(cell, orderType));
		}

		public static bool IsForceSet(Order order)
		{
			return order.OrderString == OrderID && (order.ExtraData & ForceSetBit) == ForceSetBit;
		}

		void INotifyAddedToWorld.AddedToWorld(Actor self)
		{
			self.World.AddFrameEndTask(w => w.Add(effect));
		}

		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self)
		{
			self.World.AddFrameEndTask(w => w.Remove(effect));
		}

		sealed class RallyPointOrderTargeter : IOrderTargeter
		{
			readonly RallyPointInfo info;

			public RallyPointOrderTargeter(RallyPointInfo info)
			{
				this.info = info;
			}

			public string OrderID => "SetRallyPoint";
			public int OrderPriority => 0;
			public bool TargetOverridesSelection(Actor self, in Target target, List<Actor> actorsAt, CPos xy, TargetModifiers modifiers) { return true; }
			public bool ForceSet { get; private set; }
			public bool IsQueued { get; private set; }
			public RallyOrderType OrderType { get; private set; }

			public bool CanTarget(Actor self, in Target target, List<Actor> othersAtTarget, CPos xy, TargetModifiers modifiers, ref string cursor)
			{
				if (target.Type != TargetType.Terrain)
					return false;

				IsQueued = modifiers.HasModifier(TargetModifiers.ForceQueue);

				var location = self.World.Map.CellContaining(target.CenterPosition);
				if (!self.World.Map.Contains(location))
					return false;

				// Resolve per-click order type from the same modifiers used by unit orders.
				// AttackMove (Alt) and ForceMove (Ctrl) win over plain Move; Ctrl+Alt
				// (ForceAttack) is reserved for the legacy primary-production override
				// when the structure opts in via ForceSetType.
				ForceSet = false;
				if (modifiers.HasModifier(TargetModifiers.AttackMove))
				{
					OrderType = RallyOrderType.AttackMove;
					cursor = info.AttackMoveCursor;
				}
				else if (modifiers.HasModifier(TargetModifiers.ForceMove))
				{
					OrderType = RallyOrderType.ForceMove;
					cursor = info.ForceMoveCursor;
				}
				else
				{
					OrderType = RallyOrderType.Move;
					cursor = info.Cursor;
				}

				if (modifiers.HasModifier(TargetModifiers.ForceAttack) && !string.IsNullOrEmpty(info.ForceSetType))
				{
					var closest = self.World.Selection.Actors
						.Where(a => !a.IsDead && a.IsInWorld && a.TraitOrDefault<RallyPoint>()?.Info.ForceSetType == info.ForceSetType)
						.ClosestToIgnoringPath(target.CenterPosition);

					ForceSet = closest == self;
				}

				return true;
			}
		}
	}
}
