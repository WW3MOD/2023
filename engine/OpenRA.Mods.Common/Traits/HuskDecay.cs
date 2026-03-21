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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Gradually sinks and fades a husk before removing it. On water terrain, sinks immediately.")]
	public class HuskDecayInfo : TraitInfo
	{
		[Desc("Ticks to wait before decay begins on land.")]
		public readonly int Delay = 2240;

		[Desc("Ticks for the sinking phase on land.")]
		public readonly int SinkDuration = 250;

		[Desc("Distance to sink on land.")]
		public readonly WDist SinkDistance = new WDist(1024);

		[Desc("Ticks for the fade-out phase on land (after sinking).")]
		public readonly int FadeDuration = 250;

		[Desc("Ticks to wait before sinking on water.")]
		public readonly int WaterDelay = 25;

		[Desc("Ticks for the sinking phase on water.")]
		public readonly int WaterSinkDuration = 200;

		[Desc("Distance to sink on water.")]
		public readonly WDist WaterSinkDistance = new WDist(3072);

		[Desc("Terrain types considered water.")]
		public readonly HashSet<string> WaterTerrainTypes = new HashSet<string> { "Water" };

		[GrantedConditionReference]
		[Desc("Condition granted when decay (sink/fade) begins.")]
		public readonly string DecayCondition = "husk-decaying";

		public override object Create(ActorInitializer init) { return new HuskDecay(this); }
	}

	public class HuskDecay : ITick, IRenderModifier, INotifyAddedToWorld
	{
		readonly HuskDecayInfo info;

		int ticks;
		int delay;
		int sinkDuration;
		int sinkDistance;
		int fadeDuration;
		bool onWater;
		bool decayStarted;
		int conditionToken = Actor.InvalidConditionToken;

		enum Phase { Waiting, Sinking, Fading, Done }
		Phase currentPhase = Phase.Waiting;

		public HuskDecay(HuskDecayInfo info)
		{
			this.info = info;
		}

		void INotifyAddedToWorld.AddedToWorld(Actor self)
		{
			var terrainType = self.World.Map.GetTerrainInfo(self.Location).Type;
			onWater = info.WaterTerrainTypes.Contains(terrainType);

			if (onWater)
			{
				delay = info.WaterDelay;
				sinkDuration = info.WaterSinkDuration;
				sinkDistance = info.WaterSinkDistance.Length;
				fadeDuration = 0;
			}
			else
			{
				delay = info.Delay;
				sinkDuration = info.SinkDuration;
				sinkDistance = info.SinkDistance.Length;
				fadeDuration = info.FadeDuration;
			}

			ticks = 0;
			currentPhase = Phase.Waiting;
		}

		void ITick.Tick(Actor self)
		{
			if (!self.IsInWorld || self.IsDead)
				return;

			ticks++;

			if (currentPhase == Phase.Waiting && ticks >= delay)
			{
				currentPhase = Phase.Sinking;
				ticks = 0;

				if (!decayStarted && !string.IsNullOrEmpty(info.DecayCondition))
				{
					conditionToken = self.GrantCondition(info.DecayCondition);
					decayStarted = true;
				}
			}
			else if (currentPhase == Phase.Sinking && ticks >= sinkDuration)
			{
				if (fadeDuration > 0)
				{
					currentPhase = Phase.Fading;
					ticks = 0;
				}
				else
				{
					currentPhase = Phase.Done;
				}
			}
			else if (currentPhase == Phase.Fading && ticks >= fadeDuration)
			{
				currentPhase = Phase.Done;
			}

			if (currentPhase == Phase.Done)
				self.World.AddFrameEndTask(w => { if (!self.IsDead) self.Dispose(); });
		}

		float CurrentSinkOffset
		{
			get
			{
				if (currentPhase == Phase.Sinking)
					return (float)ticks / sinkDuration * sinkDistance;

				if (currentPhase == Phase.Fading || currentPhase == Phase.Done)
					return sinkDistance;

				return 0;
			}
		}

		float CurrentAlpha
		{
			get
			{
				if (currentPhase == Phase.Fading)
					return 1f - (float)ticks / fadeDuration;

				if (currentPhase == Phase.Done)
					return 0f;

				return 1f;
			}
		}

		IEnumerable<IRenderable> IRenderModifier.ModifyRender(Actor self, WorldRenderer wr, IEnumerable<IRenderable> r)
		{
			if (currentPhase == Phase.Waiting)
				return r;

			return ModifiedRender(r);
		}

		IEnumerable<IRenderable> ModifiedRender(IEnumerable<IRenderable> r)
		{
			var sinkOffset = (int)CurrentSinkOffset;
			var alpha = CurrentAlpha;

			foreach (var renderable in r)
			{
				if (renderable is IModifyableRenderable mr)
				{
					var modified = mr.OffsetBy(new WVec(0, 0, -sinkOffset));
					if (alpha < 1f)
						modified = (IModifyableRenderable)((IModifyableRenderable)modified).WithAlpha(mr.Alpha * alpha);

					yield return (IRenderable)modified;
				}
				else
					yield return renderable;
			}
		}

		IEnumerable<Rectangle> IRenderModifier.ModifyScreenBounds(Actor self, WorldRenderer wr, IEnumerable<Rectangle> bounds)
		{
			return bounds;
		}
	}
}
