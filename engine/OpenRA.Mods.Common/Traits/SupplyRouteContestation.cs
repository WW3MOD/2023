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
using OpenRA.Mods.Common.Effects;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Graduated contestation system for Supply Routes.",
		"Tracks enemy vs friendly unit values in range to fill/deplete a control bar.",
		"Production speed scales with bar level below the slowdown threshold.")]
	public class SupplyRouteContestationInfo : TraitInfo
	{
		[Desc("Range to detect enemy and friendly forces.")]
		public readonly WDist Range = WDist.FromCells(10);

		[Desc("Allowed ProximityCaptor types that count as enemy contesters.")]
		public readonly BitSet<CaptureType> CaptorTypes = new BitSet<CaptureType>("Player", "Vehicle", "Tank", "Infantry");

		[Desc("Internal bar resolution (higher = smoother, avoids integer rounding).")]
		public readonly int BarMax = 100000;

		[Desc("Reference net enemy surplus value. This surplus depletes the bar in BaseTicks.")]
		public readonly int ReferenceValue = 2500;

		[Desc("Ticks to deplete bar from full at ReferenceValue net enemy surplus. (60s at 25 tps)")]
		public readonly int BaseTicks = 1500;

		[Desc("Minimum ticks to deplete bar from full, regardless of enemy surplus. (20s at 25 tps)")]
		public readonly int MinTicks = 500;

		[Desc("Ticks to recover bar from zero to full with no friendlies present. (120s at 25 tps)")]
		public readonly int BaseRecoveryTicks = 3000;

		[Desc("Recovery speed multiplier when friendly units are in range.")]
		public readonly int FriendlyRecoveryMultiplier = 3;

		[Desc("Bar percentage (0-100) below which production starts slowing.")]
		public readonly int SlowdownThreshold = 50;

		[Desc("How often to recalculate force values (in ticks). Performance optimization.")]
		public readonly int ScanInterval = 7;

		[NotificationReference("Speech")]
		[Desc("Speech notification when contestation begins.")]
		public readonly string ContestationNotification = "BaseAttack";

		[Desc("Text notification when contestation begins.")]
		public readonly string ContestationTextNotification = "Supply Route contested!";

		[Desc("Minimum duration (in milliseconds) between notifications.")]
		public readonly int NotifyInterval = 30000;

		[Desc("Ticks between building flashes while bar is below slowdown threshold.")]
		public readonly int FlashInterval = 100;

		[Desc("Minimap ping duration (ticks).")]
		public readonly int MiniMapPingDuration = 250;

		[Desc("Minimap ping color.")]
		public readonly Color MiniMapPingColor = Color.Orange;

		public override object Create(ActorInitializer init) { return new SupplyRouteContestation(init.Self, this); }
	}

	public class SupplyRouteContestation : ITick, ISelectionBar, IProductionSpeedModifier,
		INotifyAddedToWorld, INotifyRemovedFromWorld
	{
		readonly SupplyRouteContestationInfo info;
		readonly Actor self;
		readonly List<Actor> actorsInRange = new List<Actor>();

		int proximityTrigger;
		int controlBar;
		int cachedNetEnemySurplus;
		int cachedNetFriendlySurplus;
		int scanTick;
		long lastNotifyTime;
		bool wasContested;
		MiniMapPings radarPings;

		public SupplyRouteContestation(Actor self, SupplyRouteContestationInfo info)
		{
			this.info = info;
			this.self = self;
			controlBar = info.BarMax;
			lastNotifyTime = -info.NotifyInterval;
		}

		void INotifyAddedToWorld.AddedToWorld(Actor self)
		{
			radarPings = self.World.WorldActor.TraitOrDefault<MiniMapPings>();
			proximityTrigger = self.World.ActorMap.AddProximityTrigger(
				self.CenterPosition, info.Range, WDist.Zero, ActorEntered, ActorLeft);
		}

		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self)
		{
			self.World.ActorMap.RemoveProximityTrigger(proximityTrigger);
			actorsInRange.Clear();
		}

		bool IsRelevantActor(Actor a)
		{
			if (a == self || a.Disposed || !a.IsInWorld)
				return false;

			var rel = self.Owner.RelationshipWith(a.Owner);

			if (rel == PlayerRelationship.Enemy)
			{
				// Enemies must have ProximityCaptor with matching types
				var pc = a.Info.TraitInfoOrDefault<ProximityCaptorInfo>();
				return pc != null && pc.Types.Overlaps(info.CaptorTypes);
			}

			if (rel == PlayerRelationship.Ally)
			{
				// Friendlies just need a cost value
				var valued = a.Info.TraitInfoOrDefault<ValuedInfo>();
				return valued != null && valued.Cost > 0;
			}

			return false;
		}

		void ActorEntered(Actor other)
		{
			if (!IsRelevantActor(other))
				return;

			actorsInRange.Add(other);
		}

		void ActorLeft(Actor other)
		{
			actorsInRange.Remove(other);
		}

		void RecalculateForces()
		{
			actorsInRange.RemoveAll(a => a.Disposed || !a.IsInWorld);

			var enemyValue = 0;
			var friendlyValue = 0;

			foreach (var a in actorsInRange)
			{
				var valued = a.Info.TraitInfoOrDefault<ValuedInfo>();
				if (valued == null || valued.Cost <= 0)
					continue;

				var rel = self.Owner.RelationshipWith(a.Owner);
				if (rel == PlayerRelationship.Enemy)
					enemyValue += valued.Cost;
				else if (rel == PlayerRelationship.Ally)
					friendlyValue += valued.Cost;
			}

			cachedNetEnemySurplus = Math.Max(0, enemyValue - friendlyValue);
			cachedNetFriendlySurplus = Math.Max(0, friendlyValue - enemyValue);
		}

		void ITick.Tick(Actor self)
		{
			if (++scanTick >= info.ScanInterval)
			{
				scanTick = 0;
				RecalculateForces();
			}

			if (cachedNetEnemySurplus > 0)
			{
				// Depletion
				var ticksToDeplete = Math.Max(info.MinTicks,
					(long)info.BaseTicks * info.ReferenceValue / cachedNetEnemySurplus);
				var depletion = Math.Max(1, info.BarMax / (int)Math.Max(1, ticksToDeplete));
				controlBar = Math.Max(0, controlBar - depletion);

				// Trigger notification on transition to contested
				if (!wasContested)
				{
					wasContested = true;
					OnContestationStarted();
				}

				// Periodic flash while bar is below slowdown threshold
				var barPercent = controlBar * 100 / info.BarMax;
				if (barPercent < info.SlowdownThreshold && self.World.WorldTick % info.FlashInterval == 0)
				{
					self.World.AddFrameEndTask(w =>
						w.Add(new FlashTarget(self, Color.Red, 0.5f, 3, 4, 0)));
				}
			}
			else if (controlBar < info.BarMax)
			{
				// Recovery
				var baseRecovery = Math.Max(1, info.BarMax / info.BaseRecoveryTicks);
				var friendlyBoost = cachedNetFriendlySurplus > 0
					? info.FriendlyRecoveryMultiplier : 1;
				controlBar = Math.Min(info.BarMax, controlBar + baseRecovery * friendlyBoost);

				// Clear contested state when bar is full
				if (controlBar >= info.BarMax)
					wasContested = false;
			}
		}

		void OnContestationStarted()
		{
			if (Game.RunTime <= lastNotifyTime + info.NotifyInterval)
				return;

			lastNotifyTime = Game.RunTime;

			var localPlayer = self.World.LocalPlayer;
			if (localPlayer == null || localPlayer.Spectating)
				return;

			if (self.Owner == localPlayer || localPlayer.IsAlliedWith(self.Owner))
			{
				var rules = self.World.Map.Rules;
				Game.Sound.PlayNotification(rules, self.Owner, "Speech",
					info.ContestationNotification, self.Owner.Faction.InternalName);
				TextNotificationsManager.AddTransientLine(info.ContestationTextNotification, self.Owner);
			}

			radarPings?.Add(() => self.Owner.IsAlliedWith(self.World.RenderPlayer),
				self.CenterPosition, info.MiniMapPingColor, info.MiniMapPingDuration);

			// Flash the building
			self.World.AddFrameEndTask(w =>
				w.Add(new FlashTarget(self, Color.Red, 0.5f, 5, 4, 0)));
		}

		// IProductionSpeedModifier: 100 = normal, 0 = halted
		int IProductionSpeedModifier.GetProductionSpeedModifier()
		{
			var barPercent = controlBar * 100 / info.BarMax;
			if (barPercent >= info.SlowdownThreshold)
				return 100;

			// Linear scale: at threshold% = 100% speed, at 0% = 0% speed
			return barPercent * 100 / info.SlowdownThreshold;
		}

		// ISelectionBar: visible to all players
		float ISelectionBar.GetValue()
		{
			return (float)controlBar / info.BarMax;
		}

		Color ISelectionBar.GetColor()
		{
			return self.Owner.Color;
		}

		bool ISelectionBar.DisplayWhenEmpty => true;
	}
}
