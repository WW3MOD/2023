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
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	public class MiniMapPingsInfo : TraitInfo
	{
		public readonly int FromRadius = 200;
		public readonly int ToRadius = 15;
		public readonly int ResizeSpeed = 4;
		public readonly float RotationSpeed = 0.12f;

		public override object Create(ActorInitializer init) { return new MiniMapPings(this); }
	}

	public class MiniMapPings : ITick
	{
		public readonly List<MiniMapPing> Pings = new List<MiniMapPing>();
		readonly MiniMapPingsInfo info;

		public WPos? LastPingPosition;

		public MiniMapPings(MiniMapPingsInfo info)
		{
			this.info = info;
		}

		void ITick.Tick(Actor self)
		{
			foreach (var ping in Pings.ToArray())
				if (!ping.Tick())
					Pings.Remove(ping);
		}

		public MiniMapPing Add(MiniMapPing radarPing)
		{
			if (radarPing.IsVisible())
				LastPingPosition = radarPing.Position;

			Pings.Add(radarPing);

			return radarPing;
		}

		public MiniMapPing Add(Func<bool> isVisible, WPos position, Color color, int duration)
		{
			var ping = new MiniMapPing(isVisible, position, color, 1, duration,
				info.FromRadius, info.ToRadius, info.ResizeSpeed, info.RotationSpeed);

			if (ping.IsVisible())
				LastPingPosition = ping.Position;

			Pings.Add(ping);

			return ping;
		}

		public void Remove(MiniMapPing ping)
		{
			Pings.Remove(ping);
		}
	}

	public class MiniMapPing
	{
		public Func<bool> IsVisible;
		public WPos Position;
		public Color Color;
		public int LineWidth;
		public int Duration;
		public int FromRadius;
		public int ToRadius;
		public int ResizeSpeed;
		public float RotationSpeed;

		int radius;
		float angle;
		int tick;

		public MiniMapPing(Func<bool> isVisible, WPos position, Color color, int lineWidth, int duration,
			int fromRadius, int toRadius, int resizeSpeed, float rotationSpeed)
		{
			IsVisible = isVisible;
			Position = position;
			Color = color;
			LineWidth = lineWidth;
			Duration = duration;
			FromRadius = fromRadius;
			ToRadius = toRadius;
			ResizeSpeed = resizeSpeed;
			RotationSpeed = rotationSpeed;

			radius = fromRadius;
		}

		public bool Tick()
		{
			if (++tick == Duration)
				return false;

			if (ToRadius > FromRadius)
				radius = Math.Min(radius + ResizeSpeed, ToRadius);
			else
				radius = Math.Max(radius - ResizeSpeed, ToRadius);
			angle -= RotationSpeed;
			return true;
		}

		public IEnumerable<float2> Points(float2 center)
		{
			yield return center + radius * float2.FromAngle(angle);
			yield return center + radius * float2.FromAngle((float)(angle + 2 * Math.PI / 3));
			yield return center + radius * float2.FromAngle((float)(angle + 4 * Math.PI / 3));
		}
	}
}
