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
using OpenRA.GameRules;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Effects;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Projectiles
{
	[Desc("Projectile that travels in a straight line or arc.")]
	public class BulletInfo : IProjectileInfo
	{
		[Desc("Projectile speed in WDist / tick, two values indicate variable velocity.")]
		public readonly WDist[] Speed = { new WDist(17) };

		[Desc("The maximum/constant/incremental inaccuracy used in conjunction with the InaccuracyType property.")]
		public readonly WDist Inaccuracy = WDist.Zero;

		[Desc("The minimum inaccuracy regardless of distance to target.")]
		public readonly WDist MinInaccuracy = WDist.Zero;

		[Desc("The maximum inaccuracy used for each new following shot.")]
		public readonly WVec InaccuracyPerProjectile = WVec.Zero;

		[Desc("Controls the way inaccuracy is calculated. Possible values are 'Maximum' - scale from 0 to max with range, 'PerCellIncrement' - scale from 0 with range and 'Absolute' - use set value regardless of range.")]
		public readonly InaccuracyType InaccuracyType = InaccuracyType.Maximum;

		[Desc("Image to display.")]
		public readonly string Image = null;

		[SequenceReference(nameof(Image), allowNullImage: true)]
		[Desc("Loop a randomly chosen sequence of Image from this list while this projectile is moving.")]
		public readonly string[] Sequences = { "idle" };

		[PaletteReference(nameof(IsPlayerPalette))]
		[Desc("The palette used to draw this projectile.")]
		public readonly string Palette = "effect";

		[Desc("Palette is a player palette BaseName")]
		public readonly bool IsPlayerPalette = false;

		[Desc("Does this projectile have a shadow?")]
		public readonly bool Shadow = false;

		[Desc("Color to draw shadow if Shadow is true.")]
		public readonly Color ShadowColor = Color.FromArgb(140, 0, 0, 0);

		[Desc("Trail animation.")]
		public readonly string TrailImage = null;

		[SequenceReference(nameof(TrailImage), allowNullImage: true)]
		[Desc("Loop a randomly chosen sequence of TrailImage from this list while this projectile is moving.")]
		public readonly string[] TrailSequences = { "idle" };

		[Desc("Interval in ticks between each spawned Trail animation.")]
		public readonly int TrailInterval = 1;

		[Desc("Delay in ticks until trail animation is spawned (How far behind will it be relative projectile).")]
		public readonly int TrailDelay = 1;

		[Desc("Delay in ticks until trail animation is stopped.")]
		public readonly int TrailDeactivation = -1;

		[PaletteReference(nameof(TrailUsePlayerPalette))]
		[Desc("Palette used to render the trail sequence.")]
		public readonly string TrailPalette = "effect";

		[Desc("Use the Player Palette to render the trail sequence.")]
		public readonly bool TrailUsePlayerPalette = false;

		[Desc("Is this blocked by actors with BlocksProjectiles trait.")]
		public readonly bool Blockable = true;

		[Desc("Width of projectile (used for finding blocking actors).")]
		public readonly WDist Width = new WDist(1);

		[Desc("Arc in WAngles, two values indicate variable arc.")]
		public readonly WAngle[] LaunchAngle = { WAngle.Zero };

		[Desc("Up to how many times does this bullet bounce when touching ground without hitting a target.",
			"0 implies exploding on contact with the originally targeted position.")]
		public readonly int BounceCount = 0;

		[Desc("Modify distance of each bounce by this percentage of previous distance.")]
		public readonly int BounceRangeModifier = 60;

		[Desc("Sound to play when the projectile hits the ground, but not the target.")]
		public readonly string BounceSound = null;

		[Desc("Terrain where the projectile explodes instead of bouncing.")]
		public readonly HashSet<string> InvalidBounceTerrain = new HashSet<string>();

		[Desc("Trigger the explosion if the projectile touches an actor thats owner has these player relationships.")]
		public readonly PlayerRelationship ValidBounceBlockerRelationships = PlayerRelationship.Enemy | PlayerRelationship.Neutral;

		[Desc("Altitude above terrain below which to explode. Zero effectively deactivates airburst.")]
		public readonly WDist AirburstAltitude = WDist.Zero;

		[Desc("When set, display a line behind the actor. Length is measured in ticks after appearing.")]
		public readonly int ContrailLength = 0;

		[Desc("Time (in ticks) after which the line should appear. Controls the distance to the actor.")]
		public readonly int ContrailDelay = 1;

		[Desc("Delay in ticks until contrail animation is stopped.")]
		public readonly int ContrailDeactivation = -1;

		[Desc("Equivalent to sequence ZOffset. Controls Z sorting.")]
		public readonly int ContrailZOffset = 2047;

		[Desc("Thickness of the emitted line.")]
		public readonly WDist ContrailWidth = new WDist(1);

		[Desc("RGB color at the contrail start.")]
		public readonly Color ContrailStartColor = Color.DarkGray; // 169,169,169

		[Desc("Use player remap color instead of a custom color at the contrail the start.")]
		public readonly bool ContrailStartColorUsePlayerColor = false;

		[Desc("The alpha value [from 0 to 255] of color at the contrail the start.")]
		public readonly int ContrailStartColorAlpha = 150;

		[Desc("RGB color at the contrail end. Set to start color if undefined")]
		public readonly Color? ContrailEndColor = Color.Gray; // 128,128,128

		[Desc("Use player remap color instead of a custom color at the contrail end.")]
		public readonly bool ContrailEndColorUsePlayerColor = false;

		[Desc("The alpha value [from 0 to 255] of color at the contrail end.")]
		public readonly int ContrailEndColorAlpha = 60;

		public IProjectile Create(ProjectileArgs args) { return new Bullet(this, args); }
	}

	public class Bullet : IProjectile, ISync
	{
		readonly BulletInfo info;
		readonly ProjectileArgs args;
		readonly Animation anim;
		readonly WAngle facing;
		readonly WAngle angle;
		readonly WDist speed;
		readonly string trailPalette;

		readonly float3 shadowColor;
		readonly float shadowAlpha;

		readonly ContrailRenderable contrail;

		[Sync]
		WPos pos, lastPos, target, source;

		readonly bool lastPosIsSet = false;
		int length;
		int ticks, smokeTicks;
		int remainingBounces;

		public Bullet(BulletInfo info, ProjectileArgs args)
		{
			this.info = info;
			this.args = args;
			pos = args.CurrentSource();
			source = args.CurrentSource();

			var world = args.SourceActor.World;

			// If two LaunchAngle values are provided, the first is angle at MinRange, second at MaxRange
			if (info.LaunchAngle.Length > 1)
			{
				var targetRange = pos - args.PassiveTarget;
				var rangePercent = (float)(targetRange.Length - args.Weapon.MinRange.Length) / (float)(args.Weapon.Range.Length - args.Weapon.MinRange.Length);

				angle = new WAngle((int)(info.LaunchAngle[0].Angle + rangePercent * (info.LaunchAngle[1].Angle - info.LaunchAngle[0].Angle)));
			}
			else
				angle = info.LaunchAngle[0];

			if (info.Speed.Length > 1)
				speed = new WDist(world.SharedRandom.Next(info.Speed[0].Length, info.Speed[1].Length));
			else
				speed = info.Speed[0];

			target = args.PassiveTarget;
			if (info.Inaccuracy.Length > 0)
			{
				/* if (args.SourceActor) */

				var maxInaccuracyOffset = Util.GetProjectileInaccuracy(info.Inaccuracy.Length, info.InaccuracyType, args);
				if (info.MinInaccuracy != WDist.Zero)
				{
					maxInaccuracyOffset = info.MinInaccuracy.Length;
				}

				var wVecFromPDF = WVec.FromPDF(world.SharedRandom, 2);

				if (info.InaccuracyPerProjectile != WVec.Zero && lastPosIsSet)
				{
					target = lastPos;
					target += wVecFromPDF * maxInaccuracyOffset / 1024 - info.InaccuracyPerProjectile;
				}
				else
				{
					target += wVecFromPDF * maxInaccuracyOffset / 1024;
				}
			}

			if (info.AirburstAltitude > WDist.Zero)
				target += new WVec(WDist.Zero, WDist.Zero, info.AirburstAltitude);

			facing = (target - pos).Yaw;
			length = Math.Max((target - pos).Length / speed.Length, 1);

			if (!string.IsNullOrEmpty(info.Image))
			{
				anim = new Animation(world, info.Image, new Func<WAngle>(GetEffectiveFacing));
				anim.PlayRepeating(info.Sequences.Random(world.SharedRandom));
			}

			if (info.ContrailLength > 0)
			{
				var startcolor = info.ContrailStartColorUsePlayerColor ? Color.FromArgb(info.ContrailStartColorAlpha, args.SourceActor.Owner.Color) : Color.FromArgb(info.ContrailStartColorAlpha, info.ContrailStartColor);
				var endcolor = info.ContrailEndColorUsePlayerColor ? Color.FromArgb(info.ContrailEndColorAlpha, args.SourceActor.Owner.Color) : Color.FromArgb(info.ContrailEndColorAlpha, info.ContrailEndColor ?? startcolor);
				contrail = new ContrailRenderable(world, startcolor, endcolor, info.ContrailWidth, info.ContrailLength, info.ContrailDelay, info.ContrailZOffset);
			}

			trailPalette = info.TrailPalette;
			if (info.TrailUsePlayerPalette)
				trailPalette += args.SourceActor.Owner.InternalName;

			smokeTicks = 0;
			remainingBounces = info.BounceCount;

			shadowColor = new float3(info.ShadowColor.R, info.ShadowColor.G, info.ShadowColor.B) / 255f;
			shadowAlpha = info.ShadowColor.A / 255f;
		}

		WAngle GetEffectiveFacing()
		{
			var at = (float)ticks / (length - 1);
			var attitude = angle.Tan() * (1 - 2 * at) / (4 * 1024);

			var u = (facing.Angle % 512) / 512f;
			var scale = 2048 * u * (1 - u);

			var effective = (int)(facing.Angle < 512
				? facing.Angle - scale * attitude
				: facing.Angle + scale * attitude);

			return new WAngle(effective);
		}

		int activeTicksCount = 0;

		public void Tick(World world)
		{
			anim?.Tick();

			lastPos = pos;
			pos = WPos.LerpQuadratic(source, target, angle, ticks, length);

			if (!string.IsNullOrEmpty(info.TrailImage) && (info.TrailDeactivation == -1 || activeTicksCount < info.TrailDeactivation) && --smokeTicks < 0)
			{
				var delayedPos = WPos.LerpQuadratic(source, target, angle, ticks - info.TrailDelay, length);
				world.AddFrameEndTask(w => w.Add(new SpriteEffect(delayedPos, GetEffectiveFacing(), w,
					info.TrailImage, info.TrailSequences.Random(world.SharedRandom), trailPalette)));

				smokeTicks = info.TrailInterval;
			}

			if (info.ContrailLength > 0)
				contrail.Update(pos);

			if (ShouldExplode(world))
			{
				Explode(world);
			}

			activeTicksCount++;
		}

		bool ShouldExplode(World world)
		{
			// TODO
			var aaa = (args.SourceActor.CenterPosition - lastPos).Length;
			var bbb = (args.SourceActor.CenterPosition - lastPos);
			// Check for walls or other blocking obstacles
			if (info.Blockable && (args.SourceActor.CenterPosition - lastPos).Length > 2048 && BlocksProjectiles.AnyBlockingActorsBetween(world, args.SourceActor.Owner, lastPos, pos, info.Width, out var blockedPos, args.SourceActor, true, true))
			{
				pos = blockedPos;
				return true;
			}

			var flightLengthReached = ticks++ >= length;
			var shouldBounce = remainingBounces > 0;

			if (flightLengthReached && shouldBounce)
			{
				var cell = world.Map.CellContaining(pos);
				if (!world.Map.Contains(cell))
					return true;

				if (info.InvalidBounceTerrain.Contains(world.Map.GetTerrainInfo(cell).Type))
					return true;

				if (AnyValidTargetsInRadius(world, pos, info.Width, args.SourceActor, true))
					return true;

				target += (pos - source) * info.BounceRangeModifier / 100;
				var dat = world.Map.DistanceAboveTerrain(target);
				target += new WVec(0, 0, -dat.Length);
				length = Math.Max((target - pos).Length / speed.Length, 1);

				ticks = 0;
				source = pos;
				Game.Sound.Play(SoundType.World, info.BounceSound, source);
				remainingBounces--;
			}

			// Flight length reached / exceeded
			if (flightLengthReached && !shouldBounce)
				return true;

			// Driving into cell with higher height level
			if (world.Map.DistanceAboveTerrain(pos).Length < 0)
				return true;

			// After first bounce, check for targets each tick
			if (remainingBounces < info.BounceCount && AnyValidTargetsInRadius(world, pos, info.Width, args.SourceActor, true))
				return true;

			return false;
		}

		public IEnumerable<IRenderable> Render(WorldRenderer wr)
		{
			if (info.ContrailLength > 0)
				yield return contrail;

			if (anim == null || ticks >= length)
				yield break;

			var world = args.SourceActor.World;
			if (!world.FogObscures(pos))
			{
				var paletteName = info.Palette;
				if (paletteName != null && info.IsPlayerPalette)
					paletteName += args.SourceActor.Owner.InternalName;

				var palette = wr.Palette(paletteName);

				if (info.Shadow)
				{
					var dat = world.Map.DistanceAboveTerrain(pos);
					var shadowPos = pos - new WVec(0, 0, dat.Length);
					foreach (var r in anim.Render(shadowPos, palette))
						yield return ((IModifyableRenderable)r)
							.WithTint(shadowColor, ((IModifyableRenderable)r).TintModifiers | TintModifiers.ReplaceColor)
							.WithAlpha(shadowAlpha);
				}

				foreach (var r in anim.Render(pos, palette))
					yield return r;
			}
		}

		void Explode(World world)
		{
			if (info.ContrailLength > 0)
				world.AddFrameEndTask(w => w.Add(new ContrailFader(pos, contrail)));

			world.AddFrameEndTask(w => w.Remove(this));

			var warheadArgs = new WarheadArgs(args)
			{
				ImpactOrientation = new WRot(WAngle.Zero, Util.GetVerticalAngle(lastPos, pos), args.Facing),
				ImpactPosition = pos,
			};

			args.Weapon.Impact(Target.FromPos(pos), warheadArgs);
		}

		bool AnyValidTargetsInRadius(World world, WPos pos, WDist radius, Actor firedBy, bool checkTargetType)
		{
			foreach (var victim in world.FindActorsOnCircle(pos, radius))
			{
				if (checkTargetType && !Target.FromActor(victim).IsValidFor(firedBy))
					continue;

				if (!info.ValidBounceBlockerRelationships.HasRelationship(firedBy.Owner.RelationshipWith(victim.Owner)))
					continue;

				// If the impact position is within any actor's HitShape, we have a direct hit
				// PERF: Avoid using TraitsImplementing<HitShape> that needs to find the actor in the trait dictionary.
				foreach (var targetPos in victim.EnabledTargetablePositions)
					if (targetPos is HitShape h)
						if (h.DistanceFromEdge(victim, pos).Length <= 0)
							return true;
			}

			return false;
		}
	}
}
