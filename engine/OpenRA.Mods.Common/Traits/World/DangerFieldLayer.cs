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
 * kernel math is pure integer; stamping is additive so order does not matter. No LocalRandom,
 * never reads RenderPlayer, and ZERO RNG — the first-tick stagger is a fixed UpdateInterval/3.
 *
 * INERT IN STAGE B: pure data. NOTHING consumes these fields for behaviour — the
 * @experimental strategy layer and the human overlay (Stages C+) will. Control bots
 * (Normal/Rush/Turtle) and @stable never read this code path, so registering the
 * trait is behaviour-inert for every profile.
 */
#endregion

using System;
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

			// Durability/cost weight: 1.0x (DurabilityBase) for a fragile, cheap unit, rising with health
			// and cost. A tank's aura is denser than a rifleman's at equal throughput. Over WW3MOD's 92
			// ground-contributing types the band is 1.00x (a rifleman, and anything under 1,000 HP) to 1.28x
			// (abrams, 28,000 HP / $2,500), median 1.00x — a TIE-BREAK between comparable threats, which is
			// the whole design intent. It is deliberately NOT a second lethality term: ground throughput
			// already spans 20 to 245,000 (four orders), so a weight with real dynamic range would be
			// re-ranking the classes throughput just ranked. `--danger-reference` prints the live band.
			// See HealthDivisor/CostDivisor for the RA-scale trap this replaces.
			var durabilityWeight = p.DurabilityBase + f.Health / p.HealthDivisor + f.Cost / p.CostDivisor;

			// COMPUTED IN long, BUT THE OVERFLOW THIS ONCE GUARDED WAS A SYMPTOM, NOT THE DISEASE. With the
			// old WeaponThroughput a real Abrams read 2,300,000 against a durability weight of 2,950 — a first
			// multiply of 6.79e9, which wrapped negative, fell through the `< 1` guard and was clamped to the
			// FLOOR OF 1, so every heavy vehicle stamped an aura of 1 while a rifleman stamped ~1,626 and the
			// field ranked a rifle squad above an armoured company. But 2,300,000 was never a real throughput:
			// it was a cycle-length error (see WeaponThroughput). With the cadence corrected AND the weight
			// rescaled to its documented band, that Abrams reads 17,846 x 128 = 2.28e6, and the worst first
			// multiply across BOTH channels — the hind, 245,000 x 100 — is 2.45e7, 87x below int.MaxValue.
			// That last figure is not an estimate: `OpenRA.Utility ww3mod --danger-reference` prints it as
			// `worst-first-multiply`, so the headroom claim can be re-checked against the ruleset it is a
			// claim about, which is the only form of it that survives a rebalance.
			// The long is KEPT, and deliberately not sold as the fix: both inputs are data-driven, so a
			// rebalance can enlarge them again, and this is the cheapest place to be certain. If it ever
			// saturates, that is a signal the weapon data has drifted — not something to widen further.
			var weighted = (long)throughput * durabilityWeight / p.DurabilityBase * confidencePercent / 100;
			if (weighted < 1)
				weighted = 1;

			return new DangerKernel(radius, weighted > int.MaxValue ? int.MaxValue : (int)weighted);
		}

		// Territory-baseline contribution is GROUND-ONLY. The envelope is derived from believed enemy
		// GROUND weapon reach, so it must not paint the ANTI-AIR channel: an AA-free rear area must
		// keep reading air-safe, or the Stage-D helicopter consumer (which leashes to the AA-safe
		// envelope) would refuse safe ground. An air baseline, if ever wanted, must derive from
		// believed ANTI-AIR envelopes instead — a Stage-D concern, not this ground projection.
		public static (int Ground, int Air) BaselineChannels(int contribution) => (contribution, 0);

		/// <summary>THE REFERENCE CONTACT: the median core intensity, at full confidence, over every actor
		/// type in the ruleset that threatens <paramref name="channel"/>. This is the denominator that makes
		/// a danger threshold expressible as a SCALE-FREE number — see <see cref="DangerUnitsToField"/>.
		///
		/// <para>WHY THIS EXISTS. Intensity is `sustained damage × durability × confidence`, so its magnitude
		/// is whatever the MOD's damage numbers happen to be. WW3MOD's are 10^3–10^5 where Red Alert's were
		/// ~50, so a single believed tank stamps a core intensity around 5e5 — three to four orders above any
		/// constant inherited from RA. Every hand-tuned constant written against this field before 2026-08-09
		/// was therefore an RA-scale number sitting under a field rescaled by orders of magnitude, and every
		/// one of them fired unconditionally. Deriving the
		/// unit from the same armament data the kernels are built from is what stops that recurring: rebalance
		/// a tank's damage and the reference moves WITH it, so the thresholds keep meaning what they said.</para>
		///
		/// <para>MEDIAN, not mean: the damage table is bimodal (a legacy small-arms cluster at 10^2 and the
		/// real body at 10^3–10^4, plus superweapon outliers at 2×10^5), and a mean would be dragged into the
		/// outliers. Median over TYPES — not over live contacts — on purpose: a runtime distribution collapses
		/// when the field is quiet, and a threshold relative to a collapsing distribution re-fires at ambient,
		/// which is the very bug this replaces. Type facts are map-static, so the unit is stable all match.</para>
		///
		/// <para>Determinism: the intensities are SORTED before the median is taken, so the result does not
		/// depend on the caller's iteration order over the ruleset. Zero RNG. Returns 0 when no type threatens
		/// the channel, which callers must read as "no reference" — see <see cref="DangerUnitsToField"/>.</para></summary>
		public static int ReferenceIntensity(IEnumerable<DangerKernelFacts> facts, DangerChannel channel,
			in DangerKernelParams p)
		{
			var intensities = new List<int>();
			foreach (var f in facts)
			{
				var kernel = Compute(f, channel, 100, p);
				if (kernel.Contributes)
					intensities.Add(kernel.Intensity);
			}

			if (intensities.Count == 0)
				return 0;

			intensities.Sort();
			return intensities[intensities.Count / 2];
		}

		/// <summary>Convert a threshold expressed in DANGER UNITS to raw field units, where
		/// <c>100 units = one reference contact at point-blank</c> (<see cref="ReferenceIntensity"/>).
		/// So 50 means "half as dangerous as a typical enemy unit standing on this cell", and — because the
		/// kernel taper is linear over `range/1024 + 2` cells — a contact whose envelope merely TOUCHES the
		/// cell contributes only a few units, which is the ambient flicker a level test must sit above.
		///
		/// <para>0 units maps to exactly 0 field units, so a literal-zero test ("outside every believed
		/// envelope") converts losslessly and keeps its meaning at any scale. A NEGATIVE threshold passes
		/// through UNCHANGED, because several consumers use a negative value to mean "guard disabled" — the
		/// sentinel is preserved here, in the one function every caller goes through, rather than re-guarded
		/// at each call site where one of them would eventually forget.</para>
		///
		/// <para>A reference of 0 (no type threatens the channel — e.g. a test ruleset) yields
		/// <see cref="int.MaxValue"/> for any positive threshold rather than 0: with no scale to calibrate
		/// against, a level test must fail CLOSED (never "everywhere is dangerous"), matching the direction
		/// every consumer's own fallback already takes when its field is absent. The product is computed in
		/// long and clamped: reference intensities are ~10^5–10^6 today, so a threshold of a few hundred units
		/// has ample headroom, but both terms are data-driven and the clamp costs nothing.</para>
		/// Pure integer, zero RNG.</summary>
		public static int DangerUnitsToField(int units, int referenceIntensity)
		{
			if (units <= 0)
				return units;

			if (referenceIntensity <= 0)
				return int.MaxValue;

			var scaled = (long)units * referenceIntensity / 100;
			return scaled >= int.MaxValue ? int.MaxValue : (int)scaled;
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

		[Desc("HP per +1 weight point above the baseline. WW3MOD hulls run 200–28,000 HP, so this is",
			"1000x the RA-era 10 the field shipped with: at 10 an Abrams alone drew +2,800 points",
			"(a 29x multiplier) and the weight stopped being a tie-break between comparable threats.")]
		public readonly int HealthDivisor = 1000;

		[Desc("Cost per +1 weight point above the baseline. Scaled with HealthDivisor for the same",
			"reason — WW3MOD costs run 25–6,000 against RA's ~50–2,000.")]
		public readonly int CostDivisor = 5000;

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

		[Desc("How many recomputes per player emit a `[danger] dist` distribution line (min/median/max over",
			"stamped cells, in raw field units AND in danger units). UNCONDITIONAL — not behind DebugLogging —",
			"because nothing recorded what this field actually holds, which is exactly how an RA-scale constant",
			"(the old EvacDangerThreshold: 60) survived the total conversion unnoticed through three rounds of",
			"'trucks are fixed'. The one distribution ever measured — median 66,834 at evac entry, from the",
			"2026-08-09 play log — was itself recorded while the weapon-cadence bug was live, so it described a",
			"field where every heavy contact stamped a clamped 1; treat it as evidence that SOMETHING was wrong,",
			"not as a calibration. A threshold set in danger units is only as trustworthy as the distribution it",
			"was derived against, so the distribution has to be readable from an ORDINARY play session, with no",
			"test harness and no batch run.",
			"Bounded by episode, not by rate: the first N recomputes per player, then one every",
			"DistributionLogEveryNth. 0 disables.")]
		public readonly int DistributionLogEpisodes = 3;

		[Desc("After the opening DistributionLogEpisodes, emit one distribution line every Nth recompute per",
			"player — enough to see the field grow as contact is made, without per-scan spam. 0 = opening",
			"episodes only.")]
		public readonly int DistributionLogEveryNth = 40;

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

			// Median of Ground over ActiveCells, or -1 when it has not been computed for the CURRENT
			// field. Reset on every recompute, so a consumer can never read a median belonging to an
			// older belief state. Instrumentation-free: it is a cache of a pure function of the field.
			public int GroundMedianCache = -1;

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

		/// <summary>Core intensity of the median ground-threatening actor type at full confidence — the
		/// denominator for every GROUND threshold expressed in danger units. Map-static (ruleset-derived),
		/// so it is stable for the whole match. See <see cref="DangerKernelMath.ReferenceIntensity"/>.</summary>
		public int ReferenceGroundIntensity { get; private set; }

		/// <summary>The AIR-channel reference. Separate from the ground one and NOT interchangeable: the two
		/// channels are stamped from different weapon sets with wildly different throughputs, so converting an
		/// air threshold against the ground reference would be exactly the cross-scale mix this unit exists to
		/// prevent.</summary>
		public int ReferenceAirIntensity { get; private set; }

		readonly Dictionary<Player, int> recomputeCount = new();

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

			// The armor population a Versus table must zero before its warhead counts as harmless. Derived
			// from the SAME ruleset the kernels are, so a map that adds an armor class re-opens the test
			// for that map rather than silently keeping a stale verdict.
			var (armorTypes, anyUnprovableArmor) = RulesetArmorTypes(w.Map.Rules.Actors.Values);

			// Cache kernel facts per actor type once — weapon references are resolved by now
			// (ArmamentInfo.WeaponInfo populated at RulesetLoaded). Per-world (map overrides apply).
			foreach (var ai in w.Map.Rules.Actors.Values)
			{
				if (ai.Name.StartsWith("^", System.StringComparison.Ordinal))
					continue;

				factsByType[ai.Name] = ExtractKernelFacts(ai, Info.ThroughputWindow, armorTypes, anyUnprovableArmor);
			}

			// The reference contact, from the SAME facts the kernels are stamped from, so a balance change to
			// the mod's damage table moves the unit with it instead of silently re-scaling every threshold.
			ReferenceGroundIntensity = DangerKernelMath.ReferenceIntensity(factsByType.Values, DangerChannel.Ground, kernelParams);
			ReferenceAirIntensity = DangerKernelMath.ReferenceIntensity(factsByType.Values, DangerChannel.Air, kernelParams);

			// UNCONDITIONAL, once per world. Two jobs: without it a reader cannot convert the danger units in
			// ai.yaml back into the raw numbers the [danger] dist and evac lines report — and, because every
			// threshold on this branch is a FRACTION of the reference, the SPREAD is what says whether the
			// median is representative or whether one weapon class dominates the ruleset. The thresholds were
			// derived, not measured (the only play log predates the cadence fix, so its distribution no longer
			// exists), and this line plus [danger] dist is what a single ordinary session needs to confirm or
			// refute them. If min and max straddle the median by more than ~2 orders, the median is a weak
			// reference and the unit wants revisiting — not the individual thresholds.
			var groundSpread = ContributingIntensitySpread(DangerChannel.Ground);
			Log.Write("debug",
				$"[danger] reference ground={ReferenceGroundIntensity} air={ReferenceAirIntensity} "
				+ $"(100 danger units = one reference contact at point-blank) "
				+ $"ground-types={groundSpread.Count}/{factsByType.Count} "
				+ $"min={groundSpread.Min} max={groundSpread.Max}");

			// DETERMINISTIC stagger — NOT a SharedRandom draw (see BeliefStore for the byte-identity
			// rationale). Distinct offset from the other two grids to keep them off the same tick.
			subCountdown = Info.UpdateInterval / 3;
		}

		void ITick.Tick(Actor self)
		{
			if (beliefStore == null || --subCountdown > 0)
				return;

			// §6 narrow + stagger: only the fog-respecting bot profiles (@experimental AND @stable since the
			// 2026-08-02 parity promotion, InfluenceStack.cs:47-48) plus human combatants get a field, and
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
			field.GroundMedianCache = -1;

			foreach (var contact in beliefStore.Contacts(player))
				StampContact(field, contact);

			ProjectTerritoryBaseline(player, field);

			LogDistribution(player, field);
		}

		/// <summary>Bounded, unconditional record of what this field ACTUALLY holds — the measurement whose
		/// absence let an RA-scale threshold survive the total conversion. Reports both raw field units and
		/// danger units, so the numbers in ai.yaml can be checked against the field they gate without a test
		/// harness: a level threshold should sit above the median and below the peak, and a threshold the
		/// median already exceeds is firing unconditionally.
		///
		/// <para>Bounded by EPISODE, not deduped: the opening recomputes (when the field is nearly empty and
		/// a too-low threshold is most visible) plus a periodic sample as contact builds. The sort is O(n log n)
		/// over stamped cells, which is why it runs only on the scans that log — never per recompute.</para></summary>
		void LogDistribution(Player player, PlayerField field)
		{
			if (Info.DistributionLogEpisodes <= 0)
				return;

			recomputeCount.TryGetValue(player, out var n);
			recomputeCount[player] = ++n;

			var opening = n <= Info.DistributionLogEpisodes;
			var periodic = Info.DistributionLogEveryNth > 0 && n % Info.DistributionLogEveryNth == 0;
			if (!opening && !periodic)
				return;

			if (field.ActiveCells.Count == 0)
			{
				Log.Write("debug", $"[danger] dist player={player.PlayerName} n={n} cells=0 — no believed contact");
				return;
			}

			var ground = new List<int>(field.ActiveCells.Count);
			var air = new List<int>(field.ActiveCells.Count);
			foreach (var cell in field.ActiveCells)
			{
				var data = field.Cells[cell];
				ground.Add(data.Ground);
				air.Add(data.Air);
			}

			ground.Sort();
			var min = ground[0];
			var median = ground[ground.Count / 2];
			var max = ground[ground.Count - 1];

			Log.Write("debug",
				$"[danger] dist player={player.PlayerName} n={n} cells={ground.Count} "
				+ $"ground min={min} median={median} max={max} "
				+ $"| in units median={ToDangerUnits(median, ReferenceGroundIntensity)} "
				+ $"max={ToDangerUnits(max, ReferenceGroundIntensity)} ref={ReferenceGroundIntensity}");

			air.Sort();
			LogPercentiles(player, n, "ground", ground, ReferenceGroundIntensity);
			LogPercentiles(player, n, "air", air, ReferenceAirIntensity);
		}

		/// <summary>The percentile CURVE of one channel over the same stamped-cell population
		/// <see cref="LogDistribution"/> already reports min/median/max for — emitted as its OWN line, leaving
		/// `[danger] dist` byte-identical. Nothing automated parses that line today (checked across tools/),
		/// so that is for human greps and for comparing against already-captured logs, not a machine contract.
		///
		/// <para>WHY THIS EXISTS: min/median/max is three points, and no percentile can be honestly derived
		/// from three points. A danger unit defined as a percentile of the LIVE field (the successor to the
		/// median-of-types <see cref="DangerKernelMath.ReferenceIntensity"/>) has to be chosen against the
		/// distribution's actual shape, and the 2026-08-10 measurement — two players 3.4x apart on the same
		/// map in the same match — is exactly the evidence that the shape, not the tuning, is what the
		/// current constant gets wrong. Deriving the successor unit from three points would reproduce that
		/// error with a percentile's name on it.</para>
		///
		/// <para>DECILES PLUS p95/p99, and the tail is not decoration. Deciles fix the shape at 10% steps,
		/// which is what distinguishes "the field is flat with a few peaks" from "the field is uniformly
		/// hot" — the question a level threshold turns on. But the field is heavy-tailed (the load-time
		/// spread already straddles the median by ~4 orders), and every threshold in the HOSTILE direction
		/// is fitted to the top of that tail, where deciles have no resolution at all: p90 and max can sit
		/// two orders apart with nothing between them. p95/p99 are the cheapest points that make the tail
		/// legible.</para>
		///
		/// <para>Diagnostics ONLY. Reads the field, writes nothing, draws no random number, and is called
		/// from the same already-bounded episode gate — the list is sorted for the median regardless, so a
		/// percentile is an O(1) index off work that was already done. The AIR curve is taken over the SAME
		/// cell population as the ground one (every cell either channel stamped), NOT over air-stamped cells
		/// only: that keeps the two curves index-comparable, but it means a mostly-ground field reports air
		/// deciles of 0 up to a high percentile. That is the honest reading — the air channel carries no
		/// territory baseline by design — not a truncated sample.</para></summary>
		void LogPercentiles(Player player, int n, string channel, List<int> sorted, int reference)
		{
			var raw = new System.Text.StringBuilder();
			var units = new System.Text.StringBuilder();
			foreach (var p in PercentilePoints)
			{
				var v = Percentile(sorted, p);
				raw.Append($" p{p}={v}");
				units.Append($" p{p}={ToDangerUnits(v, reference)}");
			}

			Log.Write("debug",
				$"[danger] pct player={player.PlayerName} n={n} chan={channel} cells={sorted.Count} "
				+ $"raw{raw} | units{units} ref={reference}");
		}

		static readonly int[] PercentilePoints = { 10, 20, 30, 40, 50, 60, 70, 80, 90, 95, 99 };

		/// <summary>Nearest-rank percentile over an ASCENDING-sorted list. Integer, deterministic, no RNG.
		/// Chosen over an interpolating definition because the field is integer-valued and a reader has to be
		/// able to say "this exact cell reading is the p90" — an interpolated p90 names a value no cell holds.
		/// At p=50 this reduces to `sorted[count / 2]`, the same element <see cref="LogDistribution"/> already
		/// calls the median, for every count — so the two lines can never disagree about the middle.</summary>
		static int Percentile(List<int> sorted, int percent)
		{
			if (sorted.Count == 0)
				return 0;

			var index = (int)((long)percent * sorted.Count / 100);
			if (index >= sorted.Count)
				index = sorted.Count - 1;

			return sorted[index];
		}

		// How many ruleset types actually stamp this channel, and the range of their core intensities — the
		// context that says whether ReferenceIntensity's median is a fair middle or an artefact of a lopsided
		// population. Load-time only.
		(int Count, int Min, int Max) ContributingIntensitySpread(DangerChannel channel)
		{
			int count = 0, min = int.MaxValue, max = 0;
			foreach (var f in factsByType.Values)
			{
				var k = DangerKernelMath.Compute(f, channel, 100, kernelParams);
				if (!k.Contributes)
					continue;

				count++;
				if (k.Intensity < min)
					min = k.Intensity;
				if (k.Intensity > max)
					max = k.Intensity;
			}

			return (count, count > 0 ? min : 0, max);
		}

		// Raw field units back to danger units, for the log only (the consumer direction is
		// DangerKernelMath.DangerUnitsToField). Guards the no-reference case rather than dividing by zero.
		static int ToDangerUnits(int fieldValue, int referenceIntensity)
		{
			return referenceIntensity > 0 ? (int)((long)fieldValue * 100 / referenceIntensity) : -1;
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

					// long for the same reason Compute is, and with the same honest headroom: a real core
					// intensity is ~5.2e5 (a believed Abrams), and times a taper numerator of up to
					// MaxRadiusCells+1 = 33 that is ~1.7e7 — about 125x below int.MaxValue. The width is a
					// bound on data-driven inputs, NOT a fix for a number that is currently large.
					var contribution = (int)Math.Min(int.MaxValue,
						(long)kernel.Intensity * (r - d + 1) / (r + 1));
					if (contribution <= 0)
						continue;

					// SATURATING accumulation — the one place here with a real, if remote, path to overflow.
					// Kernels are additive across contacts by design, and unlike the two products above the
					// sum has no per-contact bound: at ~5.2e5 per believed armoured contact it would take
					// roughly 4,000 co-located auras to saturate. That is not reachable in a normal match,
					// but it is the term that scales with BELIEF COUNT rather than with the weapon table, so
					// it is the one worth making unconditionally safe. Wrapping negative here would make the
					// hottest ground on the map read as the safest — the worst possible direction for a field
					// every consumer uses as a safety gate.
					var data = field.Cells[cell];
					if (channel == DangerChannel.Air)
						data.Air = (int)Math.Min(int.MaxValue, (long)data.Air + contribution);
					else
						data.Ground = (int)Math.Min(int.MaxValue, (long)data.Ground + contribution);

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

					// GROUND-ONLY: the envelope is a ground-weapon reach, so it feeds only the ground
					// channel (see DangerKernelMath.BaselineChannels for why the air channel is spared).
					var (ground, air) = DangerKernelMath.BaselineChannels(contribution);
					var data = field.Cells[cell];
					data.Ground += ground;
					data.Air += air;
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

		/// <summary>Every armor type any DAMAGEABLE actor in the ruleset carries, plus a flag saying whether
		/// "this warhead harms nothing" is provable from the ruleset at all. See
		/// <see cref="WarheadIsHarmless"/> for why the whole population, and not just a warhead's own table,
		/// is what the test needs.
		///
		/// <para>Three ruleset shapes make the proof impossible, and each one SETS THE FLAG rather than being
		/// worked around, because all three end with <c>DamageVersus</c>'s armor filter matching nothing and
		/// returning the unmodified 100% — a full-damage hit that no reading of the <c>Versus</c> table
		/// predicts:
		/// <list type="bullet">
		/// <item>A null <see cref="ArmorInfo.Type"/>: the filter skips null-typed armor outright
		/// (DamageWarhead.cs:105).</item>
		/// <item>A CONDITIONAL armor — <see cref="ArmorInfo"/> derives from <c>ConditionalTraitInfo</c> and the
		/// filter takes only `!a.IsTraitDisabled` (:105), so while the condition is unheld the actor carries no
		/// armor class at all. Static ruleset inspection cannot know when that is.</item>
		/// <item>A <see cref="HitShapeInfo.ArmorTypes"/> restriction: a non-empty set makes the match depend on
		/// WHICH shape the projectile hit (:106), a runtime geometry fact.</item>
		/// </list>
		/// None of the three exists in WW3MOD today, so the flag reads false and every weapon is judged on its
		/// own table. They are handled anyway because the guarantee this pair is claimed to have is "fails open
		/// in every uncertain direction", and a guarantee that holds only because nobody has written that YAML
		/// yet is not one.</para></summary>
		public static (HashSet<string> Types, bool AnyUnprovableArmor) RulesetArmorTypes(IEnumerable<ActorInfo> actors)
		{
			var types = new HashSet<string>(StringComparer.Ordinal);
			var unprovable = false;

			foreach (var ai in actors)
			{
				if (ai.Name.StartsWith("^", StringComparison.Ordinal))
					continue;

				// Only a damageable actor can be a victim: DamageWarhead.IsValidAgainst rejects
				// anything without IHealthInfo before Versus is ever consulted (:59-60).
				// IHealthInfo, not the concrete HealthInfo: IsValidAgainst tests the INTERFACE, so a future
				// non-HealthInfo implementer would otherwise hide its actors' armor classes from this
				// population — and a weapon damaging only such a class would then be judged harmless and
				// silently dropped from the danger field. That is the one direction this method must never
				// fail in, and filtering on the concrete type was the one path here that failed CLOSED.
				if (ai.TraitInfoOrDefault<IHealthInfo>() == null)
					continue;

				foreach (var shape in ai.TraitInfos<HitShapeInfo>())
					if (!shape.ArmorTypes.IsEmpty)
						unprovable = true;

				foreach (var armor in ai.TraitInfos<ArmorInfo>())
				{
					if (armor.Type == null || armor.RequiresCondition != null)
						unprovable = true;

					if (armor.Type != null)
						types.Add(armor.Type);
				}
			}

			return (types, unprovable);
		}

		/// <summary>Can this warhead damage ANY armor class in the ruleset? A warhead that cannot is a
		/// spotter/marker, not a threat, and must not enter the danger field — nor pad the population
		/// <see cref="DangerKernelMath.ReferenceIntensity"/> takes its median over.
		///
		/// <para>THE ASYMMETRY THAT MAKES THIS SUBTLE, read off <c>DamageWarhead.DamageVersus</c>: an EMPTY
		/// <c>Versus</c> means "full damage to everything" (:101-102, an early return), but a NON-empty one
		/// only modifies the classes it LISTS — an unlisted armor type matches nothing in the
		/// <c>ContainsKey</c> filter (:105), so <c>ApplyPercentageModifiers</c> runs over an empty sequence
		/// and returns the unmodified 100%. Omission is therefore the OPPOSITE of a zero. A table that zeroes
		/// six classes and omits a seventh is a full-damage weapon against the seventh, not a weak one.</para>
		///
		/// <para>Hence the test is against the ruleset's armor population, not against the table alone, and it
		/// fails OPEN in every uncertain direction: an empty table, a missing class, any armor the ruleset
		/// cannot prove will be matched (see <see cref="RulesetArmorTypes"/>), or any listed value above 0 all
		/// read as "can harm". The only warhead this drops is one whose author explicitly wrote 0 against every
		/// class that exists.</para>
		///
		/// <para>AS OF 2026-08-11 THAT IS NO WARHEAD AT ALL, and the near-miss is the reason to keep reading.
		/// `IskanderTargeter`/`HIMARSTargeter` (weapons-missiles.yaml:284-306) are force-fire spotter weapons
		/// that look harmless — `Damage: 50` with every listed class at 0 — and were reported as phantom
		/// contributors to the field. They are not. Their table zeroes `None, Wood, Concrete, Light, Medium,
		/// Heavy, Brick`, of which `Brick` is not an armor class in this ruleset at all, while `Kevlar` (the 14
		/// combat-infantry types inheriting `^Soldier`), `Unarmored` and `Indestructable` are and go UNLISTED —
		/// so by the omission rule above those targeters deal their full 50 to those infantry.
		/// `--danger-reference` prints the per-warhead verdict with the unlisted classes named, which is how
		/// that was settled instead of assumed.</para></summary>
		public static bool WarheadIsHarmless(DamageWarhead warhead, HashSet<string> rulesetArmorTypes,
			bool anyUnprovableArmor)
		{
			if (anyUnprovableArmor || warhead.Versus.Count == 0 || rulesetArmorTypes == null || rulesetArmorTypes.Count == 0)
				return false;

			foreach (var armorType in rulesetArmorTypes)
				if (!warhead.Versus.TryGetValue(armorType, out var percent) || percent > 0)
					return false;

			return true;
		}

		// Reads armament data for one actor type: per-domain max range + summed throughput,
		// plus durability/value proxies. Pure ruleset inspection.
		public static DangerKernelFacts ExtractKernelFacts(ActorInfo info, int throughputWindow,
			HashSet<string> rulesetArmorTypes = null, bool anyUnprovableArmor = false)
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

				var throughput = WeaponThroughput(weapon.Warheads, weapon.Burst, weapon.BurstDelays,
					weapon.BurstWait, weapon.Magazine, weapon.ReloadDelay, throughputWindow,
					rulesetArmorTypes, anyUnprovableArmor);

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

		/// <summary>SUSTAINED damage per <paramref name="throughputWindow"/> ticks, derived from the weapon's
		/// REAL fire cycle. Public so the cadence model itself is NUnit-pinnable: it is the single input the
		/// whole danger field is built from, so an error here mis-scales every consumer at once.
		///
		/// <para>THE CYCLE, read off Armament rather than assumed. `CanFire` refuses while
		/// `IsReloading || IsWaitingBurst` (Armament.cs:327), and `ReloadDelay` and `BurstWait` are decremented
		/// INDEPENDENTLY in the same tick handler (:283-287) — so when both are armed the weapon is blocked for
		/// the LONGER of the two, never their sum. `UpdateBurst` (:624-648) sets `BurstWait` to the full
		/// `Weapon.BurstWait` only when a burst is EXHAUSTED, and to `BurstDelays[k]` between shots inside a
		/// burst. `UpdateMagazine` (:608-622) arms `ReloadDelay` only when the MAGAZINE empties — and it runs
		/// once per SHOT (:380), so `Magazine` counts shots, not bursts.</para>
		///
		/// <para>THIS REPLACES `burstDamage x Burst x window / ReloadDelay`, which was wrong in BOTH directions
		/// and by different factors per weapon class — which is exactly why it could not be corrected by
		/// rescaling anything downstream:
		/// <list type="bullet">
		/// <item>It divided by `ReloadDelay`, which ~90% of WW3MOD weapons never set: the mod paces fire with
		/// `BurstWait`, which `ArmamentInfo.RulesetLoaded` (:128-129) makes MANDATORY by throwing without it.
		/// Those weapons hit the `reload = 1` fallback and were OVER-stated by their entire cycle length —
		/// `TankRound.Abrams` (20,000+3,000 damage, `BurstWait: 130`) read 2,300,000 against a true 17,692.</item>
		/// <item>For the ~14 weapons that DO set `ReloadDelay`, it divided by a magazine-swap delay as if it
		/// were the shot interval and ignored `Magazine` entirely, UNDER-stating them. `5.56mm.AR`
		/// (`Magazine: 100`, `ReloadDelay: 150`, `Burst: 10`, `BurstDelays: 1`, `BurstWait: 8`) read 1,333
		/// against a true ~6,410 — ten 10-shot bursts of 17 ticks plus one 150-tick swap is ~312 ticks per
		/// 20,000 damage.</item>
		/// </list>
		/// A UNIFORM error would have cancelled in the reference ratio thresholds are expressed against. A
		/// ~130x over-statement on one weapon class against a ~5x under-statement on another does not: it
		/// re-ranks the classes relative to each other, which is the one thing a threat field must get right.
		/// This is also why the old formula's headline symptom was an int overflow — 2,300,000 was never a
		/// throughput, it was a cycle-length error wearing one.</para>
		///
		/// <para>Approximation, stated: where `Magazine` is not a whole multiple of `Burst` the real weapon
		/// empties mid-burst; this counts whole bursts so the damage and the time stay consistent with each
		/// other. Computed in long because the inputs are data-driven, not because the result is large.</para></summary>
		public static int WeaponThroughput(List<IWarhead> warheads, int burst, int[] burstDelays,
			int burstWait, int magazine, int reloadDelay, int throughputWindow,
			HashSet<string> rulesetArmorTypes = null, bool anyUnprovableArmor = false)
		{
			var damagePerShot = 0;
			foreach (var wh in warheads)
				if (wh is DamageWarhead dw && dw.Damage > 0
					&& !WarheadIsHarmless(dw, rulesetArmorTypes, anyUnprovableArmor))
					damagePerShot += dw.Damage;

			return SustainedThroughput(damagePerShot, burst, burstDelays, burstWait, magazine, reloadDelay, throughputWindow);
		}

		/// <summary>The cadence half of <see cref="WeaponThroughput"/>, split out so the fire-cycle model can
		/// be pinned directly from real weapon numbers: `DamageWarhead.Damage` is a readonly YAML-loaded field
		/// and a warhead list cannot be built in a unit test, which is precisely how the previous cadence went
		/// unexamined. Everything above about the cycle applies here; this is the arithmetic.</summary>
		public static int SustainedThroughput(int damagePerShot, int burst, int[] burstDelays,
			int burstWait, int magazine, int reloadDelay, int throughputWindow)
		{
			if (damagePerShot <= 0)
				return 0;

			var shotsPerBurst = burst > 0 ? burst : 1;

			// Intra-burst spacing: the gap after shot k is BurstDelays[k-1] for k in 1..burst-1, and a
			// single-entry array applies to every gap — UpdateBurst's :643-645 indexing, restated forwards.
			var intraBurst = 0;
			if (shotsPerBurst > 1 && burstDelays != null && burstDelays.Length > 0)
				for (var i = 0; i < shotsPerBurst - 1; i++)
					intraBurst += burstDelays[Math.Min(i, burstDelays.Length - 1)];

			var wait = burstWait > 0 ? burstWait : 0;

			var burstsPerMagazine = (magazine > 0 ? magazine : 1) / shotsPerBurst;
			if (burstsPerMagazine < 1)
				burstsPerMagazine = 1;

			var ticks = (long)burstsPerMagazine * (intraBurst + wait);

			// The magazine swap OVERLAPS the final burst's wait rather than following it, because both
			// counters are armed on the same shot and tick down together.
			if (reloadDelay > wait)
				ticks += reloadDelay - wait;

			// A weapon declaring neither a wait nor a reload has no modelled cycle: read it as fast rather
			// than dividing by zero. Unreachable in WW3MOD (BurstWait is mandatory), but this class is shared
			// with mods where it is not.
			if (ticks < 1)
				ticks = 1;

			var damage = (long)burstsPerMagazine * shotsPerBurst * damagePerShot;
			var throughput = damage * throughputWindow / ticks;
			return throughput > int.MaxValue ? int.MaxValue : (int)throughput;
		}

		// ---------- Public query API (Stage-C overlay / consumer seam) ----------

		PlayerField FieldOrNull(Player player)
		{
			return player != null && fields.TryGetValue(player, out var f) ? f : null;
		}

		/// <summary>THE CONVERSION EVERY LEVEL THRESHOLD MUST GO THROUGH. Turns a threshold in danger units
		/// (100 = one reference contact at point-blank) into the raw field units a <see cref="GroundDanger"/>
		/// read is measured in.
		///
		/// <para>Consumers convert AT THE CALL SITE and pass the raw number down, so the pure math helpers
		/// (GroundDangerNav, ForwardStagingMath, EscortSizingMath, the PoiOffense buckets) stay scale-agnostic
		/// and keep their existing int signatures — they compare two numbers in the same units and do not care
		/// which units those are. Only the binding between a configured constant and the field needs the
		/// unit, and that binding is exactly here.</para>
		///
		/// <para>Do NOT use this for a PRESENCE test. `GroundDanger(...) >= 1` — "any believed weapon envelope
		/// reaches this cell at all" — is already scale-free and correct at any magnitude; putting it through
		/// the conversion would turn it into a level test at 1% of a reference contact and silently raise the
		/// bar by orders of magnitude. GarrisonBotModule.MinBelievedDanger is that case, deliberately left on
		/// the raw scale.</para></summary>
		public int GroundDangerUnitsToField(int units)
		{
			return DangerKernelMath.DangerUnitsToField(units, ReferenceGroundIntensity);
		}

		/// <summary>The AIR-channel conversion. Uses the air reference — see <see cref="ReferenceAirIntensity"/>
		/// for why the two are not interchangeable. Note the air channel carries NO territory baseline
		/// (<see cref="DangerKernelMath.BaselineChannels"/>), so a literal 0 here really does mean "outside
		/// every believed AA envelope" and converts losslessly.</summary>
		public int AirDangerUnitsToField(int units)
		{
			return DangerKernelMath.DangerUnitsToField(units, ReferenceAirIntensity);
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
		/// <summary>The MEDIAN ground-danger reading over this player's stamped cells — the per-player scale a
		/// RELATIVE danger test compares against. 0 when nothing is believed dangerous anywhere.
		///
		/// <para>WHY A CONSUMER NEEDS THIS RATHER THAN A CONSTANT. Measured 2026-08-10: the live median cell
		/// differs between the two players of the SAME match on the SAME map by 3.4x (818 vs 2,755 danger
		/// units), and 17 of 18 configured ground thresholds sit 8x-459x BELOW it. No global constant can sit
		/// at the same percentile for both sides, so any absolute threshold is miscalibrated for at least one
		/// player by construction. A statistic taken from the player's own field is the only comparison that
		/// travels.</para>
		///
		/// <para>Cached per recompute and computed lazily, so the O(n log n) sort runs at most once per field
		/// rebuild and only if somebody asks — the same reason LogDistribution does not sort every recompute.
		/// Deterministic: an integer sort over a multiset, so the result does not depend on stamping order,
		/// and zero RNG.</para>
		///
		/// <para>Returns 0 for "no believed contact anywhere", which callers must NOT read as "safe by a
		/// small margin" — it means the scale is undefined, and a relative test has nothing to stand on. That
		/// is exactly the case a floor exists to cover.</para></summary>
		public int GroundDangerMedian(Player player)
		{
			if (!fields.TryGetValue(player, out var field) || field.ActiveCells.Count == 0)
				return 0;

			if (field.GroundMedianCache >= 0)
				return field.GroundMedianCache;

			var ground = new List<int>(field.ActiveCells.Count);
			foreach (var cell in field.ActiveCells)
				ground.Add(field.Cells[cell].Ground);

			ground.Sort();
			field.GroundMedianCache = ground[ground.Count / 2];
			return field.GroundMedianCache;
		}

		public IReadOnlyList<CPos> ActiveCells(Player player)
		{
			var field = FieldOrNull(player);
			return field != null ? field.ActiveCells : System.Array.Empty<CPos>();
		}
	}
}
