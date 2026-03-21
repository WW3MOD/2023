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
using OpenRA.Graphics;
using OpenRA.Mods.Common.Effects;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Fades out a husk before removing it. On water terrain, plays a splash and disposes immediately.")]
	public class HuskDecayInfo : TraitInfo
	{
		[Desc("Ticks to wait before fade begins on land.")]
		public readonly int Delay = 2240;

		[Desc("Ticks for the fade-out phase on land.")]
		public readonly int FadeDuration = 250;

		[Desc("Terrain types considered water. Husks on water get a splash effect instead of fading.")]
		public readonly HashSet<string> WaterTerrainTypes = new HashSet<string> { "Water" };

		[Desc("Image to use for the water splash effect.")]
		public readonly string WaterSplashImage = "explosion";

		[Desc("Sequence to play for the water splash effect.")]
		public readonly string WaterSplashSequence = "splash_large";

		[Desc("Palette for the water splash effect.")]
		public readonly string WaterSplashPalette = "effect";

		[Desc("Ticks to wait before disposing on water (splash plays first).")]
		public readonly int WaterDelay = 25;

		[GrantedConditionReference]
		[Desc("Condition granted when fade begins.")]
		public readonly string DecayCondition = "husk-decaying";

		public override object Create(ActorInitializer init) { return new HuskDecay(this); }
	}

	public class HuskDecay : ITick, IRenderModifier, INotifyAddedToWorld
	{
		readonly HuskDecayInfo info;

		int ticks;
		bool onWater;
		bool decayStarted;
		bool splashSpawned;

		enum Phase { Waiting, Fading, Done }
		Phase currentPhase = Phase.Waiting;

		public HuskDecay(HuskDecayInfo info)
		{
			this.info = info;
		}

		void INotifyAddedToWorld.AddedToWorld(Actor self)
		{
			var terrainType = self.World.Map.GetTerrainInfo(self.Location).Type;
			onWater = info.WaterTerrainTypes.Contains(terrainType);
			ticks = 0;
			currentPhase = Phase.Waiting;
		}

		void ITick.Tick(Actor self)
		{
			if (!self.IsInWorld || self.IsDead)
				return;

			ticks++;

			if (onWater)
			{
				if (!splashSpawned)
				{
					self.World.AddFrameEndTask(w => w.Add(new SpriteEffect(
						self.CenterPosition, self.World,
						info.WaterSplashImage, info.WaterSplashSequence, info.WaterSplashPalette)));
					splashSpawned = true;
				}

				if (ticks >= info.WaterDelay)
					self.World.AddFrameEndTask(w => { if (!self.IsDead) self.Dispose(); });

				return;
			}

			if (currentPhase == Phase.Waiting && ticks >= info.Delay)
			{
				currentPhase = Phase.Fading;
				ticks = 0;

				if (!decayStarted && !string.IsNullOrEmpty(info.DecayCondition))
				{
					self.GrantCondition(info.DecayCondition);
					decayStarted = true;
				}
			}
			else if (currentPhase == Phase.Fading && ticks >= info.FadeDuration)
			{
				currentPhase = Phase.Done;
			}

			if (currentPhase == Phase.Done)
				self.World.AddFrameEndTask(w => { if (!self.IsDead) self.Dispose(); });
		}

		float CurrentAlpha
		{
			get
			{
				if (currentPhase == Phase.Fading)
					return 1f - (float)ticks / info.FadeDuration;

				if (currentPhase == Phase.Done)
					return 0f;

				return 1f;
			}
		}

		IEnumerable<IRenderable> IRenderModifier.ModifyRender(Actor self, WorldRenderer wr, IEnumerable<IRenderable> r)
		{
			if (currentPhase != Phase.Fading && currentPhase != Phase.Done)
				return r;

			return ModifiedRender(r);
		}

		IEnumerable<IRenderable> ModifiedRender(IEnumerable<IRenderable> r)
		{
			var alpha = CurrentAlpha;

			foreach (var renderable in r)
			{
				if (renderable is IModifyableRenderable mr)
					yield return (IRenderable)mr.WithAlpha(mr.Alpha * alpha);
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
