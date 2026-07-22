#region Copyright & License Information
/*
 * WW3MOD influence stack — Stage B: per-domain danger fields.
 *
 * Two per-player threat-projection channels, stamped from the Stage-A belief store:
 *   - ANTI-GROUND: where the enemy can hurt ground units.
 *   - ANTI-AIR:    where the enemy can hurt helicopters/aircraft.
 * A believed contact contributes to a channel only if it carries a weapon whose
 * ValidTargets can hit that domain, and each contact stamps a radial kernel whose
 *   - RADIUS  ← the max range of its weapons vs that domain (+ a small buffer), and
 *   - INTENSITY ← damage throughput × a durability/cost weight × confidence.
 * That yields exactly the sniper-vs-humvee shape the design calls for: range sets
 * the width of the aura, lethality sets its density.
 *
 * AIR CHANNEL DISCRIMINATOR (design §2B PITFALL): WW3MOD separates the `Air` and
 * `Helicopter` target types. The anti-AIR DANGER channel keys off "can hit an
 * airborne target" — `Helicopter` OR `Air` (an airborne heli is targetable as both
 * via Targetable@Airborne) — because a ground MG that lists `Helicopter` genuinely
 * threatens our helicopters even though it is not an air-defence asset. (This is
 * broader than UnitRoleResolver's ShortRangeAD, which keys on `Air` alone to find
 * dedicated SAM carriers. Danger = "what can shoot me down"; AD role = "what is a
 * SAM".) Fixed-wing gets its own third channel later if needed.
 *
 * KERNELS ARE RADIAL v1. Terrain-aware flow (a river splitting the front) is a
 * declared v2 upgrade. Ranges/damage are read from ACTUAL armament data at map
 * load — never hard-coded per unit (mandate).
 *
 * STAGE-C SEAM: the believed-territory baseline projection (a generic low-intensity
 * danger out to the longest plausible enemy weapon envelope, wherever the enemy is
 * believed to hold ground) needs the control field, which lands in Stage C. The hook
 * ProjectTerritoryBaseline is present and called but intentionally empty here.
 *
 * DETERMINISM / FOG: contacts already come fog-legal and synced from the belief store;
 * kernel math is pure integer; stamping is additive so order does not matter. No
 * LocalRandom, never reads RenderPlayer. SharedRandom only staggers the first tick.
 *
 * INERT IN STAGE B: pure data. NOTHING consumes these fields for behaviour — the
 * @experimental strategy layer and the human overlay (Stages C+) will. Control bots
 * (Normal/Rush/Turtle) and @stable never read this code path, so registering the
 * trait is behaviour-inert for every profile.
 */
#endregion

using System.Collections.Generic;
using OpenRA.Mods.Common.Warheads;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public enum DangerChannel { Ground, Air }

	// The ruleset-derived facts a contact's kernel is built from — extracted once per
	// actor type at map load. Ranges are in WDist units; throughput is damage per
	// ThroughputWindow ticks. Split from the trait (mirroring UnitRoleResolver) so the
	// kernel math is NUnit-testable without mounting a world.
	public readonly struct DangerKernelFacts
	{
		public readonly int GroundRange;      // max range of ground-capable weapons.
		public readonly int AirRange;         // max range of air-capable (can-hit-Helicopter) weapons.
		public readonly int GroundThroughput; // summed damage/window of ground-capable weapons.
		public readonly int AirThroughput;    // summed damage/window of air-capable weapons.
		public readonly int Health;           // durability proxy (HealthInfo.HP).
		public readonly int Cost;             // value proxy (ValuedInfo.Cost).

		public DangerKernelFacts(int groundRange, int airRange, int groundThroughput,
			int airThroughput, int health, int cost)
		{
			GroundRange = groundRange;
			AirRange = airRange;
			GroundThroughput = groundThroughput;
			AirThroughput = airThroughput;
			Health = health;
			Cost = cost;
		}

		public bool ThreatensGround => GroundThroughput > 0 && GroundRange > 0;
		public bool ThreatensAir => AirThroughput > 0 && AirRange > 0;
	}

	// Tunables snapshotted from the Info so the pure kernel math needs no trait handle.
	public readonly struct DangerKernelParams
	{
		public readonly int RangeBufferCells;
		public readonly int MaxRadiusCells;
		public readonly int DurabilityBase;
		public readonly int HealthDivisor;
		public readonly int CostDivisor;

		public DangerKernelParams(int rangeBufferCells, int maxRadiusCells,
			int durabilityBase, int healthDivisor, int costDivisor)
		{
			RangeBufferCells = rangeBufferCells;
			MaxRadiusCells = maxRadiusCells;
			DurabilityBase = durabilityBase;
			HealthDivisor = healthDivisor;
			CostDivisor = costDivisor;
		}
	}

	// A computed kernel: how far the aura reaches (cells) and how dense it is at the core.
	public readonly struct DangerKernel
	{
		public readonly int RadiusCells;
		public readonly int Intensity;

		public DangerKernel(int radiusCells, int intensity)
		{
			RadiusCells = radiusCells;
			Intensity = intensity;
		}

		public bool Contributes => RadiusCells > 0 && Intensity > 0;
	}

	public static class DangerKernelMath
	{
		// Air-domain target types. A weapon that lists any of these can engage airborne
		// targets; a weapon that lists ANYTHING ELSE is a ground threat.
		//   - Helicopter / Air: an airborne heli is targetable as both (Targetable@Airborne),
		//     so a weapon that can shoot our helis lists Helicopter and/or Air.
		//   - ICBM: the interceptor/anti-missile marker. Pure anti-air weapons carry
		//     "Air, ICBM" (20mm_CRAM, AACannon, SurfaceToAirMissile, AirToAirMissile). It is an
		//     air-domain type, NOT a ground target — excluding it stops every SAM/CRAM/interceptor
		//     stamping a spurious anti-ground aura at full AA range.
		public const string AirType = "Air";
		public const string HelicopterType = "Helicopter";
		public const string IcbmType = "ICBM";

		static readonly string[] AirDomainTypes = { AirType, HelicopterType, IcbmType };

		// The anti-air DANGER channel: "can this weapon hit an airborne target?" — includes
		// dedicated SAMs (Air) and ground autocannons/MGs that list Helicopter. Danger is
		// about what can shoot our helis down, NOT about what is an air-defence asset.
		public static bool WeaponThreatensAir(BitSet<TargetableType> validTargets)
		{
			return validTargets.Contains(HelicopterType) || validTargets.Contains(AirType);
		}

		// The anti-ground channel: any valid target that is not an air-domain type. A weapon
		// whose ValidTargets are ALL air-domain (e.g. "Air, ICBM") threatens no ground.
		public static bool WeaponThreatensGround(BitSet<TargetableType> validTargets)
		{
			foreach (var t in validTargets)
				if (System.Array.IndexOf(AirDomainTypes, t) < 0)
					return true;

			return false;
		}

		// Radius from range, intensity from throughput × durability weight × confidence.
		// Returns a non-contributing kernel when the contact has no weapon for this domain.
		public static DangerKernel Compute(in DangerKernelFacts f, DangerChannel channel,
			int confidencePercent, in DangerKernelParams p)
		{
			var range = channel == DangerChannel.Air ? f.AirRange : f.GroundRange;
			var throughput = channel == DangerChannel.Air ? f.AirThroughput : f.GroundThroughput;
			if (range <= 0 || throughput <= 0 || confidencePercent <= 0)
				return default;

			var radius = range / 1024 + p.RangeBufferCells;
			if (radius > p.MaxRadiusCells)
				radius = p.MaxRadiusCells;
			if (radius < 1)
				radius = 1;

			// Durability/cost weight: ~1.0x (DurabilityBase) for a fragile, cheap unit, rising
			// with health and cost. A tank's aura is denser than a rifleman's at equal throughput.
			var durabilityWeight = p.DurabilityBase + f.Health / p.HealthDivisor + f.Cost / p.CostDivisor;
			var intensity = throughput * durabilityWeight / p.DurabilityBase * confidencePercent / 100;
			if (intensity < 1)
				intensity = 1;

			return new DangerKernel(radius, intensity);
		}
	}

	[TraitLocation(SystemActors.World)]
	[Desc("WW3MOD influence stack Stage B: per-player anti-ground + anti-air danger fields.",
		"Stamped from the belief store; kernels read from actual armament data. Pure data —",
		"no behaviour consumer (Stage C overlay + @experimental strategy will read it).")]
	public class DangerFieldLayerInfo : TraitInfo
	{
		[Desc("Ticks between recomputes. Staggered against the other world grids.")]
		public readonly int UpdateInterval = 25;

		[Desc("Small buffer (cells) added to a contact's weapon-range radius — a spotter/drone",
			"could extend effective reach slightly beyond nominal max range.")]
		public readonly int RangeBufferCells = 2;

		[Desc("Hard cap on a single kernel's radius (cells). Bounds worst-case stamp cost (§6).")]
		public readonly int MaxRadiusCells = 32;

		[Desc("Ticks the throughput window normalises damage over (damage per this many ticks).",
			"Uniform scale — affects absolute intensity, not the relative kernel shapes.")]
		public readonly int ThroughputWindow = 100;

		[Desc("Baseline durability weight (=100 ⇒ 1.0x). Health and cost add to it.")]
		public readonly int DurabilityBase = 100;

		[Desc("HP per +1 weight point above the baseline.")]
		public readonly int HealthDivisor = 10;

		[Desc("Cost per +1 weight point above the baseline.")]
		public readonly int CostDivisor = 50;

		[Desc("Stage-C territory baseline: low intensity added to every cell within the believed",
			"enemy weapon envelope of believed-enemy-held ground (the 'a drone could arrive' danger).",
			"0 disables the baseline entirely.")]
		public readonly int BaselineIntensity = 5;

		[Desc("Fallback envelope radius (cells) for the territory baseline when NO believed enemy",
			"contact carries a ground weapon to derive it from. Default 0 = OFF — the envelope is",
			"data-driven (longest believed enemy ground-weapon range) whenever contacts exist, so",
			"there is deliberately no hard-coded 'arty reaches N cells' constant.")]
		public readonly int BaselineFallbackEnvelopeCells = 0;

		[Desc("Hard cap (cells) on the territory-baseline projection radius, bounding its stamp cost",
			"even when a very-long-range enemy weapon is believed.")]
		public readonly int BaselineMaxProjectionCells = 24;

		public override object Create(ActorInitializer init) { return new DangerFieldLayer(init.Self, this); }
	}

	public class DangerFieldLayer : ITick, IWorldLoaded
	{
		public struct DangerCell
		{
			public int Ground;
			public int Air;
		}

		sealed class PlayerField
		{
			public readonly CellLayer<DangerCell> Cells;
			public readonly List<CPos> ActiveCells = new();
			public readonly HashSet<CPos> ActiveSet = new();

			public PlayerField(Map map) { Cells = new CellLayer<DangerCell>(map); }

			public void MarkActive(CPos cell)
			{
				if (ActiveSet.Add(cell))
					ActiveCells.Add(cell);
			}
		}

		public readonly DangerFieldLayerInfo Info;
		readonly World world;
		readonly DangerKernelParams kernelParams;
		readonly Dictionary<string, DangerKernelFacts> factsByType = new();
		readonly Dictionary<Player, PlayerField> fields = new();

		BeliefStore beliefStore;
		ControlField controlField;
		readonly List<Player> participants = new();
		int subCountdown;
		int cursor = -1;

		public DangerFieldLayer(Actor self, DangerFieldLayerInfo info)
		{
			Info = info;
			world = self.World;
			kernelParams = new DangerKernelParams(info.RangeBufferCells, info.MaxRadiusCells,
				info.DurabilityBase, info.HealthDivisor, info.CostDivisor);
		}

		void IWorldLoaded.WorldLoaded(World w, OpenRA.Graphics.WorldRenderer wr)
		{
			beliefStore = w.WorldActor.TraitOrDefault<BeliefStore>();
			controlField = w.WorldActor.TraitOrDefault<ControlField>();

			// Cache kernel facts per actor type once — weapon references are resolved by now
			// (ArmamentInfo.WeaponInfo populated at RulesetLoaded). Per-world (map overrides apply).
			foreach (var ai in w.Map.Rules.Actors.Values)
			{
				if (ai.Name.StartsWith("^", System.StringComparison.Ordinal))
					continue;

				factsByType[ai.Name] = ExtractKernelFacts(ai, Info.ThroughputWindow);
			}

			subCountdown = w.SharedRandom.Next(0, Info.UpdateInterval);
		}

		void ITick.Tick(Actor self)
		{
			if (beliefStore == null || --subCountdown > 0)
				return;

			// §6 narrow + stagger: only @experimental bots + human combatants get a field, and
			// exactly one participant is rebuilt per sub-slot (round-robin), so the two-channel
			// per-player cost never lands on a single tick.
			InfluenceStack.GatherParticipants(world, participants);
			subCountdown = InfluenceStack.SubInterval(Info.UpdateInterval, participants.Count);
			if (participants.Count == 0)
				return;

			cursor = (cursor + 1) % participants.Count;
			RecomputePlayer(participants[cursor]);
		}

		void RecomputePlayer(Player player)
		{
			if (!fields.TryGetValue(player, out var field))
				fields[player] = field = new PlayerField(world.Map);

			ClearField(field);

			foreach (var contact in beliefStore.Contacts(player))
				StampContact(field, contact);

			ProjectTerritoryBaseline(player, field);
		}

		void ClearField(PlayerField field)
		{
			foreach (var cell in field.ActiveCells)
				field.Cells[cell] = default;

			field.ActiveCells.Clear();
			field.ActiveSet.Clear();
		}

		void StampContact(PlayerField field, BeliefContact contact)
		{
			if (!factsByType.TryGetValue(contact.TypeName, out var facts))
				return;

			var ground = DangerKernelMath.Compute(facts, DangerChannel.Ground, contact.Confidence, kernelParams);
			if (ground.Contributes)
				Stamp(field, contact.Cell, ground, DangerChannel.Ground);

			var air = DangerKernelMath.Compute(facts, DangerChannel.Air, contact.Confidence, kernelParams);
			if (air.Contributes)
				Stamp(field, contact.Cell, air, DangerChannel.Air);
		}

		// Radial kernel with linear falloff: full intensity at the contact, tapering to a
		// thin ring at the edge. Additive across contacts (danger accumulates).
		void Stamp(PlayerField field, CPos origin, DangerKernel kernel, DangerChannel channel)
		{
			var r = kernel.RadiusCells;
			for (var dy = -r; dy <= r; dy++)
			{
				for (var dx = -r; dx <= r; dx++)
				{
					var d = Exts.ISqrt(dx * dx + dy * dy);
					if (d > r)
						continue;

					var cell = new CPos(origin.X + dx, origin.Y + dy);
					if (!field.Cells.Contains(cell))
						continue;

					var contribution = kernel.Intensity * (r - d + 1) / (r + 1);
					if (contribution <= 0)
						continue;

					var data = field.Cells[cell];
					if (channel == DangerChannel.Air)
						data.Air += contribution;
					else
						data.Ground += contribution;

					field.Cells[cell] = data;
					field.MarkActive(cell);
				}
			}
		}

		// STAGE-C territory baseline: wherever the player believes the enemy HOLDS ground (control
		// field), project a generic low-intensity danger outward to the believed enemy weapon
		// envelope — the design's "a spotter/drone could arrive at any time, so everything within
		// arty reach of enemy territory is slightly dangerous" clause. Conservative + data-driven:
		//   - the envelope is the longest GROUND range among current believed enemy contacts (never
		//     a hard-coded 'arty = 40 cells'); with no such contact it falls back to a knob that
		//     DEFAULTS OFF, so the baseline simply does not fire until there is evidence to size it.
		//   - projected only from the FRONTIER of believed-enemy territory (enemy cells touching
		//     non-enemy ground), at coarse grid stride, so the stamp stays cheap.
		void ProjectTerritoryBaseline(Player player, PlayerField field)
		{
			if (Info.BaselineIntensity <= 0 || controlField == null || !controlField.HasField(player))
				return;

			var envelopeMapCells = BelievedEnemyGroundEnvelopeCells(player);
			if (envelopeMapCells <= 0)
				return;

			if (envelopeMapCells > Info.BaselineMaxProjectionCells)
				envelopeMapCells = Info.BaselineMaxProjectionCells;

			var cellSize = controlField.Info.CellSize;
			var envGrid = envelopeMapCells / cellSize;
			if (envGrid < 1)
				envGrid = 1;

			for (var gx = 0; gx < controlField.GridWidth; gx++)
			{
				for (var gy = 0; gy < controlField.GridHeight; gy++)
				{
					if (controlField.OwnerAt(player, gx, gy) != ControlOwner.Enemy)
						continue;

					if (!IsBelievedEnemyFrontier(player, gx, gy))
						continue;

					StampBaseline(field, gx, gy, envGrid);
				}
			}
		}

		// True when a believed-enemy grid cell borders non-enemy ground — the perimeter danger
		// radiates from, so we do not restamp the interior of a large enemy-held region.
		bool IsBelievedEnemyFrontier(Player player, int gx, int gy)
		{
			return controlField.OwnerAt(player, gx - 1, gy) != ControlOwner.Enemy
				|| controlField.OwnerAt(player, gx + 1, gy) != ControlOwner.Enemy
				|| controlField.OwnerAt(player, gx, gy - 1) != ControlOwner.Enemy
				|| controlField.OwnerAt(player, gx, gy + 1) != ControlOwner.Enemy;
		}

		// Low-intensity radial stamp (grid stride) into BOTH channels around a frontier grid cell.
		void StampBaseline(PlayerField field, int cx, int cy, int envGrid)
		{
			for (var dgy = -envGrid; dgy <= envGrid; dgy++)
			{
				for (var dgx = -envGrid; dgx <= envGrid; dgx++)
				{
					var d = Exts.ISqrt(dgx * dgx + dgy * dgy);
					if (d > envGrid)
						continue;

					var cell = controlField.GridCellToMapCell(cx + dgx, cy + dgy);
					if (!field.Cells.Contains(cell))
						continue;

					var contribution = Info.BaselineIntensity * (envGrid - d + 1) / (envGrid + 1);
					if (contribution <= 0)
						continue;

					var data = field.Cells[cell];
					data.Ground += contribution;
					data.Air += contribution;
					field.Cells[cell] = data;
					field.MarkActive(cell);
				}
			}
		}

		// Longest believed enemy GROUND-weapon range (cells), the "assumed arty envelope". Read from
		// actual armament data via the cached kernel facts — no hard-coded distance. Falls back to a
		// default-OFF knob when no believed contact carries a ground weapon.
		int BelievedEnemyGroundEnvelopeCells(Player player)
		{
			var maxRange = 0;
			foreach (var contact in beliefStore.Contacts(player))
				if (factsByType.TryGetValue(contact.TypeName, out var facts) && facts.GroundRange > maxRange)
					maxRange = facts.GroundRange;

			if (maxRange <= 0)
				return Info.BaselineFallbackEnvelopeCells;

			return maxRange / 1024 + Info.RangeBufferCells;
		}

		// Reads armament data for one actor type: per-domain max range + summed throughput,
		// plus durability/value proxies. Pure ruleset inspection.
		public static DangerKernelFacts ExtractKernelFacts(ActorInfo info, int throughputWindow)
		{
			int groundRange = 0, airRange = 0, groundThroughput = 0, airThroughput = 0;

			foreach (var arm in info.TraitInfos<ArmamentInfo>())
			{
				var weapon = arm.WeaponInfo;
				if (weapon == null)
					continue;

				var range = weapon.Range.Length;
				if (range <= 0)
					continue;

				var throughput = WeaponThroughput(weapon.Warheads, weapon.Burst, weapon.ReloadDelay, throughputWindow);

				if (DangerKernelMath.WeaponThreatensGround(weapon.ValidTargets))
				{
					if (range > groundRange)
						groundRange = range;
					groundThroughput += throughput;
				}

				if (DangerKernelMath.WeaponThreatensAir(weapon.ValidTargets))
				{
					if (range > airRange)
						airRange = range;
					airThroughput += throughput;
				}
			}

			var health = info.TraitInfoOrDefault<HealthInfo>()?.HP ?? 0;
			var valued = info.TraitInfoOrDefault<ValuedInfo>();
			var cost = valued?.Cost ?? 0;

			return new DangerKernelFacts(groundRange, airRange, groundThroughput, airThroughput, health, cost);
		}

		// Summed absolute warhead damage per burst, scaled to damage per throughputWindow ticks.
		static int WeaponThroughput(List<IWarhead> warheads, int burst, int reloadDelay, int throughputWindow)
		{
			var burstDamage = 0;
			foreach (var wh in warheads)
				if (wh is DamageWarhead dw && dw.Damage > 0)
					burstDamage += dw.Damage;

			if (burstDamage <= 0)
				return 0;

			burstDamage *= burst > 0 ? burst : 1;
			var reload = reloadDelay > 0 ? reloadDelay : 1;
			return burstDamage * throughputWindow / reload;
		}

		// ---------- Public query API (Stage-C overlay / consumer seam) ----------

		PlayerField FieldOrNull(Player player)
		{
			return player != null && fields.TryGetValue(player, out var f) ? f : null;
		}

		/// <summary>Anti-ground danger at a cell for a player (0 when none / no field).</summary>
		public int GroundDanger(Player player, CPos cell)
		{
			var field = FieldOrNull(player);
			return field != null && field.Cells.Contains(cell) ? field.Cells[cell].Ground : 0;
		}

		/// <summary>Anti-air danger at a cell for a player (0 when none / no field).</summary>
		public int AirDanger(Player player, CPos cell)
		{
			var field = FieldOrNull(player);
			return field != null && field.Cells.Contains(cell) ? field.Cells[cell].Air : 0;
		}

		/// <summary>Danger in the requested channel at a cell.</summary>
		public int Danger(Player player, CPos cell, DangerChannel channel)
		{
			return channel == DangerChannel.Air ? AirDanger(player, cell) : GroundDanger(player, cell);
		}

		public bool HasData(Player player, CPos cell)
		{
			var field = FieldOrNull(player);
			if (field == null || !field.Cells.Contains(cell))
				return false;

			var data = field.Cells[cell];
			return data.Ground > 0 || data.Air > 0;
		}

		/// <summary>Cells carrying any danger for this player. Empty when no field yet —
		/// avoids a full-map walk for the overlay / consumers.</summary>
		public IReadOnlyList<CPos> ActiveCells(Player player)
		{
			var field = FieldOrNull(player);
			return field != null ? field.ActiveCells : System.Array.Empty<CPos>();
		}
	}
}
