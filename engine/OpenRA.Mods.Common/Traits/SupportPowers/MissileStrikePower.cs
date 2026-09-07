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
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Effects;
using OpenRA.Mods.Common.Orders;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Support power that flies a BallisticMissile actor in from off-map and detonates it on the",
		"target cell. Unlike " + nameof(NukePower) + ", whose NukeLaunch projectile builds a Z-only",
		"spawn-to-target offset and therefore always descends vertically onto the target",
		"(NukeLaunch.cs:73-78), this arrives laterally.",
		"",
		"ONE APPROACH VECTOR PER SALVO. Every warhead of a multi-aim-point strike is born on the same",
		"azimuth and flies a parallel track, so the salvo arrives as one launch rather than fanning",
		"out of a single point. See " + nameof(MissileStrikeApproach) + " for the geometry and for why",
		"the bearing is taken from the salvo CENTROID exactly once.")]
	public class MissileStrikePowerInfo : SupportPowerInfo
	{
		[ActorReference(typeof(BallisticMissileInfo))]
		[FieldLoader.Require]
		[Desc("Actor to spawn off-map on the approach vector. Must carry the " + nameof(BallisticMissile) + " trait.")]
		public readonly string MissileActor = null;

		[Desc("Height above the terrain at which the missile is spawned. Note this is the altitude at",
			"the OFF-MAP spawn, not at the map boundary: the missile descends linearly from here to",
			"the aim point across the whole standoff, so it crosses the boundary already part-way",
			"down. SpawnAltitude minus " + nameof(DetonationAltitude) + " is still the total descent.")]
		public readonly WDist SpawnAltitude = new(6144);

		[Desc("Offset applied to the spawn position, before the altitude. X is along the flight",
			"direction (negative pushes the spawn further off-map), Y is lateral, Z is added to " + nameof(SpawnAltitude) + ".")]
		public readonly WVec SpawnOffset = WVec.Zero;

		[Desc("Delay (in ticks) after the order until the missile is added to the world.",
			"The launch sounds play immediately; this is the gap before anything is visible.")]
		public readonly int MissileDelay = 0;

		[Desc("How many independently-aimed warheads this strike delivers. ONE (the default) is the",
			"behaviour every instance had before this field existed: one click, one missile, and",
			"the order generator is the shared " + nameof(SelectGenericPowerTarget) + ".",
			"",
			"Above one the power becomes MULTI-TARGET. Activating it enters a placement mode",
			"(" + nameof(SelectMultiPowerTarget) + ") that takes this many clicks; the order is",
			"issued on the LAST click and not before, and one missile is spawned per aim point.",
			"Right-click or Escape during placement abandons it without firing or consuming a shot.",
			"",
			"THE WARHEAD IS PER-MISSILE, so a multi-point power's missile actor must carry the",
			"single-warhead payload rather than a cluster: N missiles each firing an N-way cluster",
			"is N-squared detonations. RS-28 Sarmat is the shipped example — its SarmatMissile fires",
			"NukeSarmatRV (one 750 kt RV), not the NukeSarmatMIRV bus it used when the whole salvo",
			"rode a single missile.")]
		public readonly int AimPoints = 1;

		[Desc("Extra ticks added to " + nameof(MissileDelay) + " for each aim point after the first,",
			"so a salvo leaves the map edge as a stream rather than as one stack of sprites.",
			"Deterministic: the offset is index * this value, with no randomness anywhere.",
			"Ignored when " + nameof(AimPoints) + " is 1.")]
		public readonly int AimPointInterval = 0;

		[Desc("Radius of the ring drawn around each placed aim point during placement. PURELY",
			"COSMETIC — it is read by the order generator and by nothing on the simulation path.",
			"Set it to the warhead's lethal radius so the player can see two aim points overlapping",
			"before committing; zero (the default) draws no ring.")]
		public readonly WDist AimPointRadius = WDist.Zero;

		[Desc("Ring radius used to spread the aim points of a SINGLE-TARGET order for a power whose",
			nameof(AimPoints) + " is above one — i.e. an order from a bot or a Lua binding, which",
			"picks one location and knows nothing about placement mode. The first warhead lands on",
			"that location and the rest are laid on a ring of this radius at even angles, so a bot's",
			"MIRV is still a MIRV instead of a single warhead.",
			"",
			"Zero (the default) stacks every warhead on the one point. That is the correct default",
			"for a power with AimPoints 1, where this field is never read at all.")]
		public readonly WDist AimPointFallbackSpread = WDist.Zero;

		[Desc("Extra horizontal distance BEYOND the map's own diagonal at which the salvo is spawned.",
			"",
			"The standoff itself is NOT this number: it is the map diagonal plus this. The diagonal is",
			"the largest distance between any two points inside the map, so walking that far back up",
			"the approach from ANY aim point lands outside the map by at least this margin, on every",
			"map size, with no per-map tuning. That is what stops a strategic weapon being seen to pop",
			"into existence next to the launching player's Supply Route.",
			"",
			"IT IS ALSO THE FLIGHT LENGTH, and therefore the warning time: the standoff is the whole",
			"horizontal distance flown, identical for every aim point, so a strike on your own doorstep",
			"now takes exactly as long to arrive as one on the far corner. Raising this lengthens every",
			"strike in the mod and flattens every approach; it is not a cosmetic knob.")]
		public readonly WDist ApproachMargin = new(16 * 1024);

		[Desc("Altitude above the aim point at which the missile detonates. Zero (the default) is a",
			"ground burst, which is what every instance did before this field existed.",
			"",
			"PITFALL, and it is silent: every warhead carries an AirThreshold (Warhead.cs:41-45,",
			"default 128) above which a terrain-affecting warhead stops seeing terrain target types",
			"and is evaluated against `Air` instead. The `Atomic` warhead set answers this by",
			"writing AirThreshold: 10c0 on every damage row and listing Air on its CreateEffect —",
			"so a detonation at 6c256 is fine and one above 10c0 would silently do NOTHING to the",
			"ground, with no error and no lint. Raising this past a weapon's own AirThreshold is",
			"the failure mode to check for first if an airburst stops hurting anything.",
			"",
			"Note this only moves the DETONATION. The beacon, the minimap ping and the reveal",
			"camera stay on the ground aim point below it, which is what the player pointed at.")]
		public readonly WDist DetonationAltitude = WDist.Zero;

		[Desc("Range of cells the camera should reveal around the target cell.")]
		public readonly WDist CameraRange = WDist.Zero;

		[Desc("Can the camera reveal shroud generated by the `" + nameof(CreatesShroud) + "` trait?")]
		public readonly bool RevealGeneratedShroud = false;

		[Desc("Reveal cells to players with these relationships only.")]
		public readonly PlayerRelationship CameraRelationships = PlayerRelationship.Ally;

		[Desc("Amount of time before impact to spawn the camera.")]
		public readonly int CameraSpawnAdvance = 25;

		[Desc("Amount of time after impact to remove the camera.")]
		public readonly int CameraRemoveDelay = 25;

		[Desc("Amount of time before impact to remove the beacon.")]
		public readonly int BeaconRemoveAdvance = 5;

		public override object Create(ActorInitializer init) { return new MissileStrikePower(init.Self, this); }
	}

	public class MissileStrikePower : SupportPower
	{
		readonly MissileStrikePowerInfo info;

		public MissileStrikePower(Actor self, MissileStrikePowerInfo info)
			: base(self, info)
		{
			this.info = info;
		}

		public override void SelectTarget(Actor self, string order, SupportPowerManager manager)
		{
			if (info.AimPoints <= 1)
			{
				base.SelectTarget(self, order, manager);
				return;
			}

			self.World.OrderGenerator = new SelectMultiPowerTarget(order, manager, info, info.AimPoints, info.AimPointRadius);
		}

		public override void Activate(Actor self, Order order, SupportPowerManager manager)
		{
			base.Activate(self, order, manager);

			// ONCE for the salvo, not once per warhead. Six overlapping copies of the same launch
			// notification is a bug the player hears rather than a bigger event.
			PlayLaunchSounds();

			var aimPoints = ResolveAimPoints(self.World, order);

			// ONCE for the salvo, and this is the whole fix. Taking the bearing per warhead -- which
			// is what spawning each at the owner's nearest map-edge cell and facing it at its own aim
			// point amounted to -- fans a MIRV outward from one point. One azimuth off the CENTROID
			// gives parallel tracks, lateral spacing equal to the spacing the player clicked, and one
			// flight time for the whole salvo.
			var approach = ApproachFor(self, aimPoints);

			for (var i = 0; i < aimPoints.Length; i++)
			{
				Activate(self, aimPoints[i], i * info.AimPointInterval, approach);

				// SupportPower.Activate already pinged aim point 0. Ping the rest so an enemy
				// watching the minimap is warned about the whole salvo and not just a sixth of it.
				if (i > 0 && Info.DisplayMiniMapPing && manager.MiniMapPings?.Value != null)
				{
					var pos = aimPoints[i];
					manager.MiniMapPings.Value.Add(
						() => order.Player.IsAlliedWith(self.World.RenderPlayer),
						pos,
						order.Player.Color,
						Info.MiniMapPingDuration);
				}
			}
		}

		/// <summary>
		/// The world positions this strike's warheads are flown to, in launch order.
		/// </summary>
		/// <remarks>
		/// <para>RUNS ON THE SYNCED ORDER-RESOLUTION PATH — SupportPowerManager.ResolveOrder to
		/// SupportPowerInstance.Activate to here — and reads nothing that is not either in the order
		/// or in the shared world. That is the whole desync argument: the order carries integer
		/// cells encoded and decoded culture-invariantly
		/// (<see cref="MultiAimPointOrder"/>), the snap consults the ActorMap and
		/// BuildingInfluence indices which every client agrees on, and the fallback ring is integer
		/// WAngle rotation with no randomness and no floating point. The order generator that
		/// COLLECTED the points is client-local by construction and contributes nothing here beyond
		/// the cell list it put on the wire.</para>
		///
		/// <para>BOUNDED against a malformed or hostile order: the decoded list is clamped to the
		/// map and truncated to <see cref="MissileStrikePowerInfo.AimPoints"/>, so no order can
		/// spawn more missiles than the power is rated for or aim one off the map.</para>
		/// </remarks>
		WPos[] ResolveAimPoints(World world, Order order)
		{
			var count = Math.Max(1, info.AimPoints);
			var placed = MultiAimPointOrder.Deserialize(order.TargetString);

			if (placed != null && placed.Length > 0)
			{
				var used = Math.Min(placed.Length, count);
				var points = new WPos[used];
				for (var i = 0; i < used; i++)
					points[i] = ResolveCell(world, world.Map.Clamp(placed[i]));

				return points;
			}

			// SINGLE-TARGET ORDER for a multi-warhead power: a bot, a Lua binding, or a replay from
			// before this feature existed. order.Target has already been snapped to an actor centre
			// by SupportPowerInstance.Activate, so it is used as-is for the first warhead.
			var center = order.Target.CenterPosition;
			var offsets = MultiAimPointOrder.FallbackRingOffsets(count, info.AimPointFallbackSpread);
			var fallback = new WPos[offsets.Length];

			// Index 0 is a zero offset by construction, so the sender's own point is used verbatim
			// rather than round-tripped through a cell — it has already been snapped to an actor
			// centre by SupportPowerInstance.Activate, and a WPos that is deliberately NOT on a cell
			// centre is exactly what that snap produces (SupportPowerAimPoint).
			fallback[0] = center;
			for (var i = 1; i < offsets.Length; i++)
			{
				var cell = world.Map.Clamp(world.Map.CellContaining(center + offsets[i]));
				fallback[i] = world.Map.CenterOfCell(cell);
			}

			return fallback;
		}

		/// <summary>
		/// A placed cell resolved to the point the warhead is flown to, honouring
		/// <see cref="SupportPowerInfo.SnapToActorCenter"/> exactly as the single-target path does.
		/// </summary>
		WPos ResolveCell(World world, CPos cell)
		{
			var target = Target.FromCell(world, cell);
			if (!Info.SnapToActorCenter)
				return target.CenterPosition;

			return SupportPowerAimPoint.Resolve(world, target) ?? target.CenterPosition;
		}

		/// <summary>
		/// The one approach vector this salvo flies. Read once per ORDER, never per warhead.
		/// </summary>
		/// <remarks>
		/// Everything it reads is shared world state -- the owner's HomeLocation, the map size, and
		/// the aim points that came off the wire -- so every client computes the same azimuth on the
		/// same tick. See <see cref="MissileStrikeApproach"/> for the determinism and bounding
		/// argument.
		/// </remarks>
		public MissileStrikeApproach ApproachFor(Actor self, WPos[] aimPoints)
		{
			var map = self.World.Map;

			return MissileStrikeApproach.For(
				map.CenterOfCell(self.Owner.HomeLocation),
				map.CenterOfCell(new CPos(map.MapSize.X / 2, map.MapSize.Y / 2)),
				map.MapSize.X,
				map.MapSize.Y,
				info.ApproachMargin.Length,
				aimPoints);
		}

		public Actor Activate(Actor self, WPos targetPosition)
		{
			return Activate(self, targetPosition, 0);
		}

		/// <summary>
		/// A ONE-WARHEAD strike, for a caller outside the order path that has no salvo to average.
		/// The centroid of a single aim point is that aim point, so this is the same geometry with
		/// the salvo size set to one.
		/// </summary>
		public Actor Activate(Actor self, WPos targetPosition, int extraDelay)
		{
			return Activate(self, targetPosition, extraDelay, ApproachFor(self, new[] { targetPosition }));
		}

		public Actor Activate(Actor self, WPos targetPosition, int extraDelay, MissileStrikeApproach approach)
		{
			// Every use of MissileDelay below goes through this local so that one warhead of a
			// salvo is late by exactly its index, and every OTHER power — extraDelay 0 — computes
			// the identical numbers it did before this parameter existed.
			var missileDelay = info.MissileDelay + extraDelay;

			var world = self.World;

			// Same rule as the airstrike -- the strike comes in over the player's own back line, not
			// from a bearing the player picks (AirstrikePower.cs:79, established by a20c8a82) -- but
			// NOT the same construction, and the difference is why this no longer calls
			// ChooseClosestEdgeCell. That call spawned every warhead of a salvo at ONE cell and let
			// each face its own aim point, which fanned a six-warhead MIRV outward from a single
			// point on the owner's edge. Reentry vehicles from one launch arrive parallel.
			//
			// The bearing is now the salvo's, taken once from its centroid, and the spawn is that
			// bearing walked back past the map boundary -- so the warheads are parallel, the player
			// still cannot pick the direction, and a strategic weapon is no longer seen being born
			// next to the Supply Route it is supposedly launched from a continent away.
			var facing = approach.Facing;
			var spawnPos = approach.SpawnPosition(targetPosition);

			var offset = info.SpawnOffset;
			if (offset != WVec.Zero)
				spawnPos += new WVec(offset.Y, -offset.X, 0).Rotate(WRot.FromYaw(facing)) + new WVec(0, 0, offset.Z);

			spawnPos += new WVec(0, 0, info.SpawnAltitude.Length);

			var missile = world.CreateActor(false, info.MissileActor, new TypeDictionary
			{
				new CenterPositionInit(spawnPos),
				new OwnerInit(self.Owner),
				new FacingInit(facing),
			});

			// ORDERING IS LOAD-BEARING. BallisticMissile.AddedToWorld queues BallisticMissileFly
			// (BallisticMissile.cs:218), whose constructor reads Target.CenterPosition
			// unconditionally — so an unset Target throws the InvalidOperationException documented
			// at MissileSpawnerMaster.cs:85-87. CreateActor(false, ...) builds the actor without
			// adding it, the add happens below, and the assignment goes in between: exactly the
			// handshake MissileSpawnerMaster.cs:112,116 performs.
			var bm = missile.Trait<BallisticMissile>();

			// The missile flies to the DETONATION point, not to the aim point. BallisticMissileFly
			// takes its whole trajectory from Target.CenterPosition (BallisticMissileFly.cs:45),
			// terminates with SetPosition(self, targetPos) and only then queues the kill
			// (BallisticMissileFly.cs:216-221) — so the actor really is sitting at this position
			// when Explodes fires the payload at self.CenterPosition (Explodes.cs:133). Nothing
			// downstream needs telling: CreateEffectWarhead spawns its sprite at the impact
			// position unless ForceDisplayAtGroundLevel is set (CreateEffectWarhead.cs:140-150),
			// so the explosion animation follows the burst height on its own.
			bm.Target = Target.FromPos(targetPosition + new WVec(0, 0, info.DetonationAltitude.Length));

			// At MissileDelay 0 this is byte-for-byte MissileSpawnerMaster's own add
			// (BaseSpawnerMaster.cs:228-250). SpawnActorEffect is only reached when a delay is
			// asked for; it holds the already-created actor and adds it N ticks later, which is the
			// only deferred-add form in the codebase that preserves the Target-before-Add ordering.
			if (missileDelay <= 0)
				world.AddFrameEndTask(w => w.Add(missile));
			else
				world.AddFrameEndTask(w => w.Add(new SpawnActorEffect(missile, missileDelay)));

			// Ticks from the order to the detonation. BallisticMissileFly.EstimateArcTicks is the
			// activity's own arithmetic, so this is the flight the missile will actually fly rather
			// than a second number that has to be kept in step by hand.
			//
			// hDist IS NOW THE STANDOFF, and therefore the same for every aim point and every warhead
			// in the salvo (SpawnOffset aside, and nothing ships one). It used to be the distance from
			// the owner's nearest map-edge cell to wherever they clicked, which made the warning time
			// SHORTEST for a strike next to your own base -- exactly backwards. It is still read off
			// the real spawn rather than from approach.Standoff so that the beacon stays honest if a
			// SpawnOffset ever moves the birth point.
			var hDist = (targetPosition - spawnPos).HorizontalLength;
			var impactDelay = missileDelay + bm.Info.PreLaunchTicks
				+ BallisticMissileFly.EstimateArcTicks(bm.Info, hDist);

			if (info.CameraRange != WDist.Zero)
			{
				var type = info.RevealGeneratedShroud ? MapLayers.Type.Vision : MapLayers.Type.PassiveVisibility;
				var cameraDelay = Math.Max(0, impactDelay - info.CameraSpawnAdvance);

				world.AddFrameEndTask(w => w.Add(new RevealShroudEffect(targetPosition, info.CameraRange, type,
					self.Owner, info.CameraRelationships, cameraDelay, info.CameraSpawnAdvance + info.CameraRemoveDelay)));
			}

			if (Info.DisplayBeacon)
			{
				// The clock is driven off the missile's LIVE POSITION once it is flying, the way the
				// airstrike beacon is (AirstrikePower.cs:168-170), so it stays honest if the flight
				// overruns the estimate above.
				//
				// It cannot be position-driven before that, and MissileDelay is what makes this
				// matter: for those ticks the actor exists but is not in the world, sitting at its
				// spawn position, so the position formula reads a constant 0 and the beacon shows a
				// clock frozen at the top of its sweep for the whole wait — which reads to the
				// player as "the order did not take". Ticks are the only clock available then, and
				// they are exact during the wait for the same reason the flight is not: nothing can
				// make SpawnActorEffect take longer than the count it was handed.
				var orderTick = world.WorldTick;
				var delayFraction = impactDelay > 0
					? Math.Clamp(missileDelay * 1f / impactDelay, 0f, 1f)
					: 0f;

				float FractionComplete()
				{
					if (missile.IsDead || missile.Disposed)
						return 1f;

					if (!missile.IsInWorld)
						return impactDelay > 0
							? Math.Clamp((world.WorldTick - orderTick) * 1f / impactDelay, 0f, 1f)
							: 1f;

					var flown = hDist <= 0
						? 1f
						: 1f - ((missile.CenterPosition - targetPosition).HorizontalLength * 1f / hDist);

					return delayFraction + (Math.Clamp(flown, 0f, 1f) * (1f - delayFraction));
				}

				var beacon = new Beacon(
					self.Owner,
					targetPosition,
					Info.BeaconPaletteIsPlayerPalette,
					Info.BeaconPalette,
					Info.BeaconImage,
					Info.BeaconPoster,
					Info.BeaconPosterPalette,
					Info.BeaconSequence,
					Info.ArrowSequence,
					Info.CircleSequence,
					Info.ClockSequence,
					FractionComplete,
					Info.BeaconDelay,
					Math.Max(1, impactDelay - info.BeaconRemoveAdvance));

				world.AddFrameEndTask(w => w.Add(beacon));
			}

			return missile;
		}
	}
}
