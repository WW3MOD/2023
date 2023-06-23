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
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.Sound
{
	public enum PingType { Default, Soldier, SupportInfantry, Vehicle, Artillery, SupportVehicle }

	[Desc("Players will be notified when this actor becomes visible to them.",
		"Requires the 'EnemyWatcher' trait on the player actor.")]
	public class AnnounceOnSeenInfo : TraitInfo
	{
		[Desc("Should there be a radar ping on enemies' radar at the actor's location when they see him")]
		public readonly bool PingRadar = true;
		public readonly PingType Type = PingType.Default;
		public readonly Color? Color = null;
		public readonly int? LineWidth = null;
		public readonly int? Alpha = null;
		public readonly int? FromRadius = null;
		public readonly int? ToRadius = null;
		public readonly int? ResizeSpeed = null;
		public readonly float? RotationSpeed = null;
		public readonly int? Duration = null;

		[NotificationReference("Speech")]
		[Desc("Speech notification to play.")]
		public readonly string Notification = null;

		[Desc("Text notification to display.")]
		public readonly string TextNotification = null;

		public readonly bool AnnounceNeutrals = false;

		public override object Create(ActorInitializer init) { return new AnnounceOnSeen(init.Self, this); }
	}

	public class AnnounceOnSeen : INotifyDiscovered
	{
		public readonly AnnounceOnSeenInfo Info;

		readonly Lazy<RadarPings> radarPings;

		public AnnounceOnSeen(Actor self, AnnounceOnSeenInfo info)
		{
			Info = info;
			radarPings = Exts.Lazy(() => self.World.WorldActor.Trait<RadarPings>());
		}

		public void OnDiscovered(Actor self, Player discoverer, bool playNotification)
		{
			if (!playNotification || discoverer != self.World.RenderPlayer)
				return;

			// Hack to disable notifications for neutral actors so some custom maps don't need fixing
			// At this point it's either neutral or an enemy
			if (!Info.AnnounceNeutrals && !self.AppearsHostileTo(discoverer.PlayerActor))
				return;

			// Audio notification
			if (discoverer != null && !string.IsNullOrEmpty(Info.Notification))
				Game.Sound.PlayNotification(self.World.Map.Rules, discoverer, "Speech", Info.Notification, discoverer.Faction.InternalName);

			if (discoverer != null)
				TextNotificationsManager.AddTransientLine(Info.TextNotification, discoverer);

			// Radar notification
			if (Info.PingRadar)
			{
				var color = Color.Gray;
				var alpha = 100;
				var lineWidth = 1;
				var duration = 300;
				var fromRadius = 25;
				var toRadius = 5;
				var resizeSpeed = 1;
				var rotationSpeed = 0.02f;

				switch (Info.Type)
				{
					case PingType.Soldier:
						color = Color.Red;
						alpha = 70;
						fromRadius = 1;
						toRadius = 10;
						break;
					case PingType.SupportInfantry:
						fromRadius = 15;
						toRadius = 5;
						break;
					case PingType.Vehicle:
						color = Color.Red;
						lineWidth = 2;
						fromRadius = 20;
						toRadius = 10;
						break;
					case PingType.Artillery:
						color = Color.Red;
						fromRadius = 35;
						toRadius = 15;
						break;
					case PingType.SupportVehicle:
						fromRadius = 40;
						toRadius = 10;
						break;
				}

				color = Color.FromArgb(Info.Alpha ?? alpha, color.R, color.G, color.B);

				radarPings.Value?.Add(
					new RadarPing(() => true,
						self.CenterPosition, color, Info.LineWidth ?? lineWidth, Info.Duration ?? duration, Info.FromRadius ?? fromRadius,
						Info.ToRadius ?? toRadius, Info.ResizeSpeed ?? resizeSpeed, Info.RotationSpeed ?? rotationSpeed));
			}
		}
	}
}
