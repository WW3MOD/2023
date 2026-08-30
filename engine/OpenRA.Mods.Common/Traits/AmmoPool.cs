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
using OpenRA.Mods.Common.Activities;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Actor has a limited amount of ammo, after using it all the actor must reload in some way.")]
	public class AmmoPoolInfo : TraitInfo, IProvideTooltipDescription, IRulesetLoaded
	{
		[Desc("Name of this ammo pool, used to link reload traits to this pool.")]
		public readonly string Name = "primary";

		[Desc("Name(s) of armament(s) that use this pool.")]
		public readonly string[] Armaments = { "primary", "secondary" };

		[Desc("Time in ticks to fully reload ammopool from empty.")]
		public readonly int FullReloadTicks = 0;

		[Desc("How many reloads should take place before unit is fully reloaded (based on reloading from empty).")]
		public readonly int FullReloadSteps = 0;

		[Desc("How much ammo does this pool contain when fully loaded.")]
		public readonly int Ammo = 1;

		[Desc("Initial ammo the actor is created with. Defaults to Ammo.")]
		public readonly int InitialAmmo = -1;

		[Desc("How much ammo is reloaded after a certain period.")]
		public readonly int ReloadCount = 1;

		[Desc("Time to reload per ReloadCount on airfield etc.")]
		public readonly int ReloadDelay = 50;

		[Desc("Should actor automatically move to rearm when out of ammo.")]
		public readonly bool AutoRearm = true;

		[Desc("Marks this pool as one the actor cannot meaningfully fight without. Once EVERY essential",
			"pool on an actor is empty it counts as dry for RESUPPLY-SEEKING purposes even if other",
			"pools still hold rounds — a rifleman out of bullets with one AT round left is, in the",
			"user's words, 'basically out of ammo'. See AmmoPool.OutOfEssentialAmmo.",
			"",
			"DEFAULTS TO FALSE, and the default is the whole safety of this feature: with nothing",
			"flagged anywhere, OutOfEssentialAmmo is byte-identical to AllPoolsEmpty and every dispatch",
			"path behaves exactly as it did before this field existed (pinned by",
			"EssentialAmmoTest.NoEssentialPoolsAuthored_MatchesAllPoolsEmpty). Turning the feature on is",
			"an authoring act, per pool, and it ships inert until someone does it.",
			"",
			"WHY THERE IS NO NAME-BASED DEFAULT, since 'primary-ammo is essential' is the obvious guess",
			"and 40 pools in this mod carry that name: it is WRONG on the first unit anyone reaches for.",
			"The tunguska's primary-ammo is its cannon and its secondary-ammo is its missiles, and a",
			"tunguska out of missiles is in far more trouble than one out of bullets. A rule that",
			"encodes the wrong answer on the motivating example is not a default, it is a coin flip",
			"applied to 40 pools at once. Author it per pool or leave it off.",
			"",
			"This is a SEEKING predicate only. It deliberately does not reach AmmoPool.CannotFight,",
			"which gates combat at seven call sites — an AT specialist down to his last round must",
			"still be allowed to fire it.")]
		public readonly bool Essential = false;

		[Desc("Furthest a rearm host can be, in CHESSBOARD cells, and still be worth self-dispatching to",
			"when this actor is ESSENTIAL-dry but not wholly dry — i.e. it can still shoot something,",
			"just not the thing that matters. Deliberately shorter than DryRearmLeashCells so the tier",
			"reads as the weaker impulse it is: 'there is a truck right here', not 'go find one'. A unit",
			"that can still fight should not abandon its position to cross the map.",
			"",
			"Same semantics as DryRearmLeashCells in every other respect — chessboard metric via",
			"SupplyHuntMath.WithinCellBudget, boundary-inclusive, 0 or less admits nothing (the unit",
			"stays put and raises NeedsResupply so a Hunt-stance truck can come to it). Do NOT infer the",
			"zero-semantics from PoiOffensiveBotModule.OutOfAmmoRearmSeekRadiusCells, whose 0 means",
			"UNLIMITED.",
			"",
			"Lives HERE rather than on AutoSeekSuppliesInfo beside ReturnWhenEmptyLeashCells, and both",
			"dry paths read it from the pools, because AmmoPoolInfo is present on every actor that can",
			"reach either path while AutoSeekSupplies is declared on ^Soldier alone. That asymmetry is",
			"why DryRearmLeashCells is its own field, and it points the same way here: reading down from",
			"AmmoPool is safe, reading up into AutoSeekSuppliesInfo is what would leave vehicles",
			"unleashed while looking complete.")]
		public readonly int EssentialDryLeashCells = 15;

		[Desc("Furthest a rearm host can be, in CHESSBOARD cells, and still be worth self-dispatching to",
			"when this actor has run DRY (every pool empty). Beyond it the unit stays put and raises",
			"NeedsResupply so a Hunt-stance truck can come to it instead — the same disposition",
			"AutoSeekSupplies adopts beyond its own budget. Boundary-inclusive; 0 or less admits nothing",
			"(i.e. a dry unit never self-dispatches, only flags), matching",
			"AutoSeekSuppliesInfo.ReturnWhenEmptyLeashCells rather than",
			"PoiOffensiveBotModule.OutOfAmmoRearmSeekRadiusCells, whose 0 means UNLIMITED — there are",
			"already two opposite zero-semantics in this codebase for this one idea, so state which you",
			"mean and never infer it.",
			"",
			"WHY THIS IS ITS OWN FIELD instead of reading AutoSeekSuppliesInfo.ReturnWhenEmptyLeashCells,",
			"which carries the same 30 and was the obvious thing to reuse: AutoSeekSupplies is declared on",
			"^Soldier ALONE (infantry.yaml), while the path this bounds — AutoRearmIfDry, reached from",
			"INotifyBecomingIdle and from firing the last round — runs on every non-aircraft actor with an",
			"AmmoPool, vehicles included. Reading the other trait's Info would have left every vehicle",
			"unleashed while looking complete at the only site anyone reads. The DEFAULTS are pinned equal",
			"by DryRearmLeashTest, so the two move together deliberately or not at all; the distance MATH",
			"is genuinely shared (SupplyHuntMath.WithinCellBudget), which is the part that must not drift.",
			"",
			"The two bound different things and that is why they are allowed to diverge: this one caps a",
			"unit dispatching ITSELF having run out, that one caps interrupting an order the player gave.")]
		public readonly int DryRearmLeashCells = 30;

		[ConsumedConditionReference]
		[Desc("Should actor automatically move to rearm when out of ammo.")]
		public readonly string AutoRearmCondition = null;

		[Desc("Cost per batch of ReloadCount rounds. Charged when a SupplyProvider rearms",
			"this pool, and deducted from sell/evac refund per missing batch.")]
		public readonly int SupplyValue = 1;

		[Desc("Sound to play for each reloaded ammo magazine.")]
		public readonly string RearmSound = null;

		[GrantedConditionReference]
		[Desc("The condition to grant to self for each ammo point in this pool.")]
		public readonly string AmmoCondition = null;

		// Ammo is dispensed in chunks of ReloadCount rounds, each chunk costing SupplyValue.
		// Shared rather than inlined so the per-pool tooltip line and the cross-pool grand
		// total in ProductionTooltipLogic cannot drift apart — they did, by up to 100x.
		public int BatchSize => System.Math.Max(1, ReloadCount);
		public int BatchCount => (Ammo + BatchSize - 1) / BatchSize;
		public int PoolBudget => BatchCount * SupplyValue;

		public override object Create(ActorInitializer init) { return new AmmoPool(this); }

		/// <summary>
		/// <para>An Essential pool that no host can refill is a permanent errand, so it is a load-time
		/// error rather than something to discover in a match.</para>
		///
		/// <para>The failure it prevents: dispatch fires on OutOfEssentialAmmo and the errand's exit is
		/// the negation of the same predicate, so the walk ends when the essential pool gains a round.
		/// If that pool is absent from Rearmable.AmmoPools, no host will ever put a round in it —
		/// Rearmable.RearmTick only touches RearmableAmmoPools — and the unit stands at the truck
		/// forever, combat-inert, with every bot module withholding it because IsSeekingRearm stays
		/// true. That is one YAML line away on ^E6, whose Rearmable lists secondary-ammo alone while he
		/// also carries an SMG pool; marking the SMG Essential would delete him from the game.</para>
		///
		/// <para>Only checked when Essential is actually set, so an unauthored mod cannot trip it. An
		/// actor with no Rearmable at all is exempt: nothing rearms it by any route, so this rule has
		/// nothing to say — the pool simply never causes a dispatch worth bounding.</para>
		/// </summary>
		void IRulesetLoaded<ActorInfo>.RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			if (!Essential)
				return;

			var rearmable = ai.TraitInfoOrDefault<RearmableInfo>();
			if (rearmable == null || rearmable.AmmoPools.Contains(Name))
				return;

			// Ruleset.cs:59 already prefixes "Actor type {name}: " when it catches this.
			throw new YamlException(
				$"AmmoPool '{Name}' is marked Essential but is not listed in Rearmable.AmmoPools " +
				$"({string.Join(", ", rearmable.AmmoPools)}). A unit dispatched because this pool is empty " +
				"can never have it refilled, so the resupply errand would never end and the unit would " +
				"stand at the host permanently. Either add it to Rearmable.AmmoPools or drop Essential.");
		}

		IEnumerable<TooltipElement> IProvideTooltipDescription.ProvideTooltipDescription(ActorInfo ai, Ruleset rules, out int priority)
		{
			priority = 100;

			if (Ammo <= 0 || SupplyValue <= 0)
				return null;

			// Walk the actor's armaments and pick out the ones that draw from this pool.
			// Multiple armaments can share one pool (e.g. dual-barrel burst weapons), so
			// list all the weapon names, joined with '+'. Falls back to the pool name if
			// no armaments link here (defensive — should not happen in well-formed YAML).
			var armaments = ai.TraitInfos<ArmamentInfo>()
				.Where(arm => Armaments.Contains(arm.Name))
				.ToArray();

			string label;
			if (armaments.Length == 0)
				label = FormatWeaponLabel(Name);
			else
				label = string.Join(" + ", armaments
					.Select(arm => FormatWeaponLabel(arm.Weapon))
					.Distinct());

			// ONE notation for one quantity. This rendered two different shapes depending on whether
			// BatchSize was 1 -- "Ammo: 1 × 50 supply = 50" against
			// "Ammo: 900 (9 batches × 100 rounds × 5 supply = 45)" -- so a rifleman, who carries one
			// pool of each kind, stated the same fact two ways four lines apart. The batch form is
			// the one economy.md documents (§"Tooltip format"); the short form was undocumented, so
			// it is the one that goes. Singular/plural is handled rather than reading "1 round".
			//
			// The round count moved OUT of the refill expression and into its own row: it is a
			// capacity, not a term of a price, and the two were only ever adjacent because both
			// had to fit on one line of one label. "8 × 30 = 240" is now the whole of the arithmetic.
			var rounds = Ammo == 1 ? "1 round" : $"{Ammo} rounds";
			return new[]
			{
				TooltipElement.Subhead(label),
				TooltipElement.Stat("Ammo", rounds),
				TooltipElement.Cost("Refill", $"{BatchCount} × {SupplyValue} = {PoolBudget} supply"),
			};
		}

		/// <summary>
		/// Turns a ruleset weapon key into something a player can read. The key itself is never
		/// changed — only its rendering. Players were seeing raw identifiers: `5.56mm.DMR`,
		/// `TankRound.Abrams`, `HIMARSTargeter`.
		/// </summary>
		static string FormatWeaponLabel(string raw)
		{
			if (string.IsNullOrEmpty(raw))
				return "Weapon";

			var trimmed = raw.TrimStart('^').Replace('-', ' ').Replace('_', ' ');

			var sb = new System.Text.StringBuilder(trimmed.Length + 8);
			for (var i = 0; i < trimmed.Length; i++)
			{
				var c = trimmed[i];

				// A dot BETWEEN TWO DIGITS is a decimal point and must survive — `5.56mm` and
				// `12.7mm` are calibres. Any other dot is a namespace separator (`TankRound.Abrams`)
				// and reads as a word break. Replacing all dots would render "5 56mm".
				if (c == '.')
				{
					var isDecimalPoint = i > 0 && i + 1 < trimmed.Length
						&& char.IsDigit(trimmed[i - 1]) && char.IsDigit(trimmed[i + 1]);
					sb.Append(isDecimalPoint ? '.' : ' ');
					continue;
				}

				if (i > 0 && char.IsUpper(c))
				{
					var prev = trimmed[i - 1];

					// `TankRound` -> `Tank Round`.
					var lowerToUpper = char.IsLower(prev);

					// End of an acronym run that starts a new word: `HIMARSTargeter` -> `HIMARS Targeter`.
					var acronymEnd = char.IsUpper(prev)
						&& i + 1 < trimmed.Length && char.IsLower(trimmed[i + 1]);

					// Deliberately NOT split on digit->upper. That boundary is ambiguous:
					// `M270Rockets` wants a break and `9M311` (a real missile designation) does not,
					// and nothing in the key distinguishes them. Leaving both joined is the safe half
					// of the trade — it never invents a word break inside a designation.
					if (lowerToUpper || acronymEnd)
						sb.Append(' ');
				}

				sb.Append(c);
			}

			return string.Join(" ", sb.ToString().Split(' ', System.StringSplitOptions.RemoveEmptyEntries));
		}
	}

	public class AmmoPool : INotifyCreated, INotifyAttack, INotifyBecomingIdle, IResolveOrder, ISync
	{
		public readonly AmmoPoolInfo Info;
		readonly Stack<int> tokens = new Stack<int>();
		IReloadAmmoModifier[] modifiers;

		/// <summary>
		/// Set when unit is out of ammo and ResupplyBehavior is Hold.
		/// Supply trucks with Hunt stance should seek out these units.
		/// Also set externally by activities (e.g. SeekSupplyProvider) when no supply
		/// source is reachable, so a Hunt-stance truck can come to us.
		/// </summary>
		public bool NeedsResupply { get; set; }

		[Sync]
		public int RemainingTicks;

		[Sync]
		public int CurrentAmmoCount { get; private set; }

		public bool HasAmmo => CurrentAmmoCount > 0;
		public bool HasHalfAmmo { get { return CurrentAmmoCount > Info.Ammo / 2; } }
		public bool HasFullAmmo => CurrentAmmoCount == Info.Ammo;

		public AmmoPool(AmmoPoolInfo info)
		{
			Info = info;
			CurrentAmmoCount = Info.InitialAmmo < Info.Ammo && Info.InitialAmmo >= 0 ? Info.InitialAmmo : Info.Ammo;
		}

		public bool GiveAmmo(Actor self, int count)
		{
			if (CurrentAmmoCount >= Info.Ammo || count < 0)
				return false;

			CurrentAmmoCount = (CurrentAmmoCount + count).Clamp(0, Info.Ammo);
			if (CurrentAmmoCount > 0)
				NeedsResupply = false;

			UpdateCondition(self);
			return true;
		}

		public bool TakeAmmo(Actor self, int count)
		{
			if (CurrentAmmoCount <= 0 || count < 0)
				return false;

			CurrentAmmoCount = (CurrentAmmoCount - count).Clamp(0, Info.Ammo);
			UpdateCondition(self);

			/* if (CurrentAmmoCount == 0)
			{
				AutoRearmIfDry(self);
			} */

			return true;
		}

		/// <summary>
		/// <para>"This actor cannot shoot anything" — it has at least one pool and every pool is empty.</para>
		///
		/// <para>EVERY pool, deliberately, and this is the one definition of the phrase in the codebase. The
		/// tempting narrower set is <see cref="Rearmable.RearmableAmmoPools"/>, which is filtered to
		/// Rearmable.AmmoPools — but that field answers a DIFFERENT question ("which pools can a host
		/// refill for me"), and the two sets are not the same actor-for-actor. The combat engineer
		/// declares only his C4 charges as rearmable while also carrying an SMG pool, so a
		/// rearmable-only test calls him out of ammo with a full magazine (infantry.yaml, ^E6).</para>
		/// </summary>
		public static bool AllPoolsEmpty(Actor self)
		{
			return AllPoolsEmpty(self.TraitsImplementing<AmmoPool>());
		}

		/// <summary>
		/// <para>"This actor cannot fight with the thing that matters" — the SEEKING predicate, and the
		/// one every resupply dispatcher and every self-assigned errand's exit test now shares.</para>
		///
		/// <para>If any pool on the actor is marked <see cref="AmmoPoolInfo.Essential"/>, this is "every
		/// ESSENTIAL pool is empty" and non-essential pools are ignored entirely. If none is marked — the
		/// shipped default everywhere — it falls back to <see cref="AllPoolsEmpty(IEnumerable{AmmoPool})"/>
		/// exactly, so a mod that authors nothing sees no behavioural change at all. That equivalence is
		/// the feature's whole safety story and is pinned by
		/// <c>EssentialAmmoTest.NoEssentialPoolsAuthored_MatchesAllPoolsEmpty</c>.</para>
		///
		/// <para>DO NOT substitute this into <see cref="CannotFight"/>. That predicate gates COMBAT at
		/// seven sites (Attack, AttackFollow, AttackBase ×2, AttackMove, SmartMoveActivity,
		/// AttackMoveActivity) where it means "stop trying to shoot" — feeding it this test would stop a
		/// rifleman attacking while he still holds the AT round he is supposed to fire. The two
		/// predicates coincide today only because nothing is authored Essential yet; they answer
		/// different questions and must not be merged on the strength of that coincidence.</para>
		///
		/// <para>THE OTHER HALF OF THE CONTRACT: this same function is the errand's EXIT test
		/// (<see cref="Activities.SeekSupplyProvider"/>, <see cref="Activities.Resupply"/>). Dispatch and
		/// exit must be literally this one function, not two tests that agree today. Widening dispatch
		/// alone while the exit still read AllPoolsEmpty would make a partially-dry unit's errand
		/// pointless on its FIRST tick — AllPoolsEmpty is already false at the moment such a unit is
		/// dispatched — and the unit would take one step and stop.</para>
		///
		/// <para>Single pass, no allocation and no LINQ: reached from ITick on every armed actor.</para>
		/// </summary>
		public static bool OutOfEssentialAmmo(IEnumerable<AmmoPool> pools)
		{
			var any = false;
			var allEmpty = true;
			var anyEssential = false;
			var allEssentialEmpty = true;

			foreach (var p in pools)
			{
				any = true;
				var hasAmmo = p.HasAmmo;
				if (hasAmmo)
					allEmpty = false;

				if (p.Info.Essential)
				{
					anyEssential = true;
					if (hasAmmo)
						allEssentialEmpty = false;
				}
			}

			// An actor with no pools is not dry — it simply has no ammunition model. Matches
			// AllPoolsEmpty, whose `any` flag exists for the same reason.
			if (!any)
				return false;

			return anyEssential ? allEssentialEmpty : allEmpty;
		}

		/// <summary>Actor overload, for the notification paths that have not cached their pools.</summary>
		public static bool OutOfEssentialAmmo(Actor self)
		{
			return OutOfEssentialAmmo(self.TraitsImplementing<AmmoPool>());
		}

		/// <summary>
		/// <para>Has a SELF-ASSIGNED resupply errand lost its reason? An errand nobody ordered lasts
		/// exactly as long as the dryness that prompted it; a player's explicit Resupply order
		/// (<paramref name="dispatchedBecauseDry"/> false) is a destination order and never expires here.</para>
		///
		/// <para>THIS IS A FUNCTION RATHER THAN A LINE IN EACH ACTIVITY on purpose. Both walking branches
		/// need it — <see cref="Activities.SeekSupplyProvider"/> and <see cref="Activities.Resupply"/> —
		/// and the rule that matters is not the expression but its IDENTITY with the dispatch predicate
		/// above. Two sites each writing <c>!AllPoolsEmpty(pools)</c> agreed with the dispatcher right up
		/// until the dispatcher widened, at which point they would have ended every partially-dry errand
		/// on its first tick. Naming one function is what makes that class of drift impossible instead of
		/// merely unlikely, and it is what lets a test pin the real rule rather than a copy of it
		/// (<c>EssentialAmmoTest.TheErrandExitTestCannotBeSatisfiedAtDispatch</c>).</para>
		/// </summary>
		public static bool SelfAssignedErrandIsOver(bool dispatchedBecauseDry, IEnumerable<AmmoPool> pools)
		{
			return dispatchedBecauseDry && !OutOfEssentialAmmo(pools);
		}

		/// <summary>
		/// <para>THE ONE PLACE A BATCH IS PAID FOR. Both delivery models fund a rearm through here — the
		/// proximity push in <c>SupplyProvider.Tick</c> and the docking pull in
		/// <see cref="Rearmable.RearmTick"/> — because the alternative is two implementations of the same
		/// arithmetic, and this codebase has already paid for that shape more than once.</para>
		///
		/// <para>A batch is <c>ReloadCount</c> rounds for <c>SupplyValue</c> supply. The FULL SupplyValue is
		/// charged even when fewer rounds are handed over because the pool is nearly full. That is the
		/// pre-existing convention on the push path and it is kept deliberately rather than tidied, so the
		/// two paths cannot disagree about a price — but it becomes PLAYER-VISIBLE under a charged economy:
		/// topping up two rounds of an abrams' forty-round pool costs the same 30 supply as filling five.</para>
		///
		/// <para><paramref name="provider"/> null means a host that charges nothing — a pure
		/// <c>RearmsUnits</c> depot, of which this mod has none. It is not a licence for free ammunition at
		/// a SupplyProvider host; every such host passes itself in.</para>
		///
		/// <para>Returns false without changing anything when the pool is full or the provider cannot afford
		/// the batch. Callers must treat "cannot afford" as ENDING the errand rather than as a reason to
		/// wait: a client parked at a depot that cannot pay keeps <see cref="IsSeekingRearm"/> true, which
		/// makes StarvingRecruitGate withhold it from every bot module, and the Logistics Centre restocks
		/// only via AbsorbsSupplyCache — an unbounded wait that deletes the unit from the game in all but
		/// name. Partial refill, then leave; that mirrors SupplyProvider.cs:968, which has always skipped a
		/// pool it cannot pay for rather than holding its target.</para>
		/// </summary>
		public static bool TryServeBatch(Actor client, AmmoPool pool, SupplyProvider provider)
		{
			if (pool.HasFullAmmo)
				return false;

			var cost = pool.Info.SupplyValue;
			if (provider != null && provider.CurrentSupply < cost)
				return false;

			var batch = System.Math.Max(1, pool.Info.ReloadCount);
			var missing = pool.Info.Ammo - pool.CurrentAmmoCount;
			if (!pool.GiveAmmo(client, System.Math.Min(batch, missing)))
				return false;

			// Charged AFTER the ammunition lands, so a GiveAmmo that declines cannot bill the depot.
			provider?.DeductSupply(cost);

			if (!string.IsNullOrEmpty(pool.Info.RearmSound))
				Game.Sound.PlayToPlayer(SoundType.World, client.Owner, pool.Info.RearmSound, client.CenterPosition);

			return true;
		}

		/// <summary>
		/// <para>Could this host actually pay for a batch of something we are short of? The one dispatch
		/// gate that is not about distance. Reached through <see cref="ChooseAffordableResupplier"/>,
		/// which asks it of every candidate BEFORE picking — the Auto arm, the Evacuate detour and
		/// AutoSeekSupplies' break-off arm all choose that way, and none re-asks it afterwards because
		/// the answer is guaranteed by construction.</para>
		///
		/// <para>PUBLIC because <see cref="Activities.SeekSupplyProvider"/> has to ask the SAME question
		/// while the errand is already running: it revalidates its target and re-picks on a 25-tick
		/// cadence, and a looser test there quietly undoes an affordable pick one layer down — the unit
		/// is dispatched to a host that can pay and retargets onto a nearer one that cannot. Sharing this
		/// predicate rather than restating it there is the house rule (see the three-copy note on
		/// SupplyProvider.AcceptClient): prose is not the countermeasure.</para>
		///
		/// <para>Everywhere else a wasted walk costs a walk. On the evacuate detour it costs the unit's
		/// exit: an evacuating actor that detours to a host which cannot serve it does not resume
		/// leaving — it parks in <see cref="Activities.SeekSupplyProvider"/>'s in-range branch, which
		/// stands still waiting for a push that never comes and has no stall guard of its own
		/// (AutoSeekSupplies' guard covers only errands that trait dispatched, on infantry).
		/// <see cref="ChooseResupplier"/> filters on CurrentSupply &gt; 0 only, which is not the same
		/// question when a pool costs 50 a batch and the truck is holding 3.</para>
		///
		/// <para>This paragraph USED TO NAME the m270/grad/tos — which ship with
		/// <c>InitialResupplyBehavior: Evacuate</c> — as the casualty. They cannot be: they are vehicles,
		/// and no vehicle can enter that activity. <see cref="AutoRearm"/> is its only construction site
		/// and takes the <see cref="Activities.SeekSupplyProvider"/> branch only for a host whose
		/// <c>SupplyProvider</c> has an empty <c>DockedCondition</c>; all 15 vehicles name
		/// <c>logisticscenter</c> and nothing else in <c>RearmActors</c>, and that is the one provider in
		/// the mod that sets <c>DockedCondition</c>, so vehicles route to <c>Resupply</c> instead. The
		/// live clientele of this gate is INFANTRY. Note that is a property of the RULESET, not an engine
		/// invariant: adding <c>supplycache</c> or <c>truk</c> to a vehicle's RearmActors puts vehicles
		/// into this activity for the first time.</para>
		///
		/// <para>The same test AutoSeekSupplies.CanServe applies from the other side — a host we would
		/// refuse to walk to must also be one we refuse to abandon an exit for — and since 2026-08-30 it
		/// is literally this method rather than a restatement of it. Two caveats on "same". The POOL SET
		/// is the caller's: CanServe passes the Rearmable subset, the dispatch sites pass every pool, and
		/// they coincide across the shipped ruleset without being the same expression. And this is not the
		/// provider's whole answer: SupplyProvider.AcceptClient can also return BelowThreshold on
		/// MinNeedThreshold, which no caller here models — irrelevant on the dry path that reaches this,
		/// where need is maximal, but it means "the provider would accept us" is the stronger claim.
		/// A host with no SupplyProvider charges nothing (the pure RearmsUnits depot), so it always
		/// passes.</para>
		/// </summary>
		public static bool HostCanAffordSomethingWeNeed(Actor host, IEnumerable<AmmoPool> pools)
		{
			var provider = host.TraitOrDefault<SupplyProvider>();
			if (provider == null)
				return true;

			foreach (var p in pools)
				if (!p.HasFullAmmo && provider.CurrentSupply >= p.Info.SupplyValue)
					return true;

			return false;
		}

		/// <summary>
		/// <para>Which leash applies to a self-dispatch right now: the full
		/// <see cref="AmmoPoolInfo.DryRearmLeashCells"/> when the actor cannot shoot at all, the shorter
		/// <see cref="AmmoPoolInfo.EssentialDryLeashCells"/> when it has merely lost the pool that
		/// matters and can still fire something.</para>
		///
		/// <para>Callers must already have established <see cref="OutOfEssentialAmmo(IEnumerable{AmmoPool})"/>;
		/// this only picks which of the two budgets that dryness earns. Resolved across all pools by
		/// <see cref="ResolveDryRearmLeash"/> (tightest wins) for the reasons given there.</para>
		/// </summary>
		public static int ResolveSeekLeash(IEnumerable<AmmoPool> pools)
		{
			var fullyDry = AllPoolsEmpty(pools);

			var leash = int.MaxValue;
			foreach (var p in pools)
			{
				var l = fullyDry ? p.Info.DryRearmLeashCells : p.Info.EssentialDryLeashCells;
				if (l < leash)
					leash = l;
			}

			return leash == int.MaxValue ? 0 : leash;
		}

		/// <summary>
		/// The dry-rearm leash for an ACTOR, from the pools it carries. Tightest wins.
		///
		/// <para>Needed because <see cref="DryRearmLeashCells"/> is declared per POOL while the decision it
		/// governs is per ACTOR — <see cref="AutoRearmIfDry"/> is an instance method that acts on the
		/// whole actor, and on a two-pool actor (several infantry carry primary + secondary)
		/// <c>INotifyBecomingIdle</c> delivers to EACH pool in turn. Reading <c>Info</c> off whichever
		/// instance happened to be notified would make the bound depend on trait ordering, so the answer
		/// is resolved across all of them and is the same whoever asks.</para>
		///
		/// <para>Minimum rather than maximum: this is a bound, and two pools disagreeing about it is a
		/// configuration mistake in which the safer reading is the shorter walk. An actor with no pools
		/// cannot reach the caller (AllPoolsEmpty is false for an empty set), so the 0 fallback is
		/// unreachable rather than meaningful — it inherits the "admits nothing" semantics either way.</para>
		/// </summary>
		/// <para>Takes the raw values rather than the pools so the tests can call THIS method instead of
		/// restating its four lines beside it — a test that reimplements the rule it checks agrees with
		/// itself no matter what the shipped code does.</para>
		public static int ResolveDryRearmLeash(IEnumerable<int> poolLeashes)
		{
			var leash = int.MaxValue;
			foreach (var l in poolLeashes)
				if (l < leash)
					leash = l;

			return leash == int.MaxValue ? 0 : leash;
		}

		/// <summary>Array overload for the per-tick callers, which cache their pools in Created.</summary>
		public static bool AllPoolsEmpty(IEnumerable<AmmoPool> pools)
		{
			var any = false;
			foreach (var p in pools)
			{
				if (p.HasAmmo)
					return false;

				any = true;
			}

			return any;
		}

		/// <summary>
		/// <para>"This actor must not be holding an attack order" — every pool empty
		/// (<see cref="AllPoolsEmpty(Actor)"/>) on an actor that is not an aircraft.</para>
		///
		/// <para>Aircraft are carved out deliberately, matching <see cref="AutoRearmIfDry"/> and
		/// <see cref="AutoRearmIfAnyNotFull"/> directly below. A dry aircraft rearms through its own
		/// idle ReturnToBase flow (Aircraft.cs), which is reached by the attack activity ending on the
		/// aircraft's terms; tearing that activity down from the outside fights that flow instead of
		/// helping it. Ground units have no such self-recovery, which is why they need this.</para>
		///
		/// <para>Note what this is NOT keyed on, because each alternative has already been wrong once:
		/// not <see cref="Rearmable.RearmableAmmoPools"/> (answers a different question — see
		/// AllPoolsEmpty), not "every armament paused" (armament PauseOnCondition also carries
		/// suppressed >= 10, empdisable, heavy-damage-attained and inwater, any of which would call a
		/// suppressed or EMP'd man with a full magazine dry), and not the red-ammo-pip YAML condition
		/// (implied by this, but not equal to it).</para>
		///
		/// <para>PITFALL corrected 2026-08-12: this note previously cited garrisoned-at-port as the example.
		/// That is wrong — no Armament in this mod carries it; it appears on Mobile and AttackFrontal
		/// only. The caveat stands on the terms above. Note also that it attaches to the QUESTION, not
		/// to the predicate: pause-for-any-reason is the RIGHT test for "should I stop moving to aim at
		/// THIS target?", and is wrong only as a stand-in for "send this man to resupply".</para>
		/// </summary>
		public static bool CannotFight(Actor self)
		{
			return AllPoolsEmpty(self) && !self.Info.HasTraitInfo<AircraftInfo>();
		}

		/// <summary>
		/// <para>Self-dispatch to a rearm host, or adopt whatever disposition the actor's ResupplyBehavior
		/// stance calls for, once it has run dry.</para>
		///
		/// <para>"Dry" is <see cref="OutOfEssentialAmmo(IEnumerable{AmmoPool})"/>, NOT AllPoolsEmpty —
		/// renamed from AutoRearmIfDry on 2026-08-21 because the old name became a lie the moment
		/// the predicate widened, and a method whose name states the wrong trigger is exactly the kind of
		/// thing that costs an afternoon. With nothing authored Essential the two are identical, so the
		/// rename is the only thing that changes for an unmodified mod.</para>
		/// </summary>
		public void AutoRearmIfDry(Actor self)
		{
			var ammoPools = self.TraitsImplementing<AmmoPool>();
			if (!OutOfEssentialAmmo(ammoPools) || self.Info.HasTraitInfo<AircraftInfo>())
				return;

			// Check resupply behavior stance
			var autoTarget = self.TraitOrDefault<AutoTarget>();
			var behavior = autoTarget?.ResupplyBehaviorValue ?? ResupplyBehavior.Auto;

			switch (behavior)
			{
				case ResupplyBehavior.Auto:
					// LEASHED since 2026-08-21 (user ruling). This dispatch used to apply no distance
					// test whatever, which was tolerable while the only hosts were trucks and Logistics
					// Centers — both of which sit near the army or the base. Infantry now seek dropped
					// SUPPLYCACHEs, which are wherever a truck happened to unload, so the set of things
					// a dry unit will walk any distance to grew and the absent bound started to matter.
					//
					// FALLS BACK TO EVACUATION since 2026-08-27 (user ruling: "'Auto' should mean that
					// they evacuate if no rearm actor exists"), immediately and with no grace period.
					// Every no-host path here used to end at "raise NeedsResupply and stand still",
					// which is not a decision to hold — it is a unit stuck with its hand up, because
					// that flag's only reader is a Hunt-stance provider that has to DRIVE to it. The
					// reported case was an iskander, which names `RearmActors: logisticscenter` alone;
					// with no Logistics Centre nothing in the ruleset could ever answer the flag.
					//
					// The judgement itself is SupplyHuntMath.DecideAutoDisposition — pure, NUnit-pinned
					// in ResupplyAutoFallbackTest, and deliberately NOT AmmoEvacMath.Decide, whose
					// budget parameter reads 0 as UNLIMITED where this one reads 0 as "admits nothing".
					//
					// DRAINED IS NOT ABSENT. ChooseResupplier filters on CurrentSupply > 0, and with
					// RearmsUnits absent from this mod that filter applies to EVERY host — so a null
					// answer here means "no depot with stock", not "no depot". The evacuation gate must
					// never be computed from it: an empty Logistics Centre is refilled by
					// AbsorbsSupplyCache, and with SupplyValue 1500 against TotalSupply 2250 an emptied
					// LC is where an Iskander NORMALLY leaves it. So the hopelessness inputs below ask
					// AnyRearmHostWithinLeash / AnyMobileRearmHost, which ignore stock, while only the
					// seek trigger reads it.
					var leash = ResolveSeekLeash(ammoPools);

					// The nearest AFFORDABLE host, not the nearest stocked one. A depot holding less
					// than one batch of anything we need cannot serve us on arrival — the LC left with
					// 750 after one 1500 batch is exactly that — so dispatching to it produces a shuttle.
					// Choosing on affordability rather than filtering the already-chosen nearest is what
					// keeps a unit from being stranded beside a poor depot while a stocked one sits just
					// past it; see SupplyHuntMath.SelectNearestAffordable for the worked case.
					var host = ChooseAffordableResupplier(self, ammoPools);
					var suppliedHostWithinLeash = host != null
						&& SupplyHuntMath.WithinCellBudget(
							host.Location.X - self.Location.X,
							host.Location.Y - self.Location.Y,
							leash);

					// The two hope scans are read by exactly one branch of the decision — the evacuation
					// conjunction — so they are computed only when that branch is reachable. Guarding on
					// whollyDry matters most: essential-dry-but-still-armed is the common case and must
					// not pay for two world scans whose answer is discarded.
					var whollyDry = AllPoolsEmpty(ammoPools);
					var namesRearmActors = NamesRearmActors(self);
					var mayEvacuate = whollyDry && namesRearmActors && leash > 0 && !suppliedHostWithinLeash;
					var anyHostWithinLeash = mayEvacuate && AnyRearmHostWithinLeash(self, leash);
					var anyHostCanReachUs = mayEvacuate && !anyHostWithinLeash && AnyMobileRearmHost(self);

					switch (SupplyHuntMath.DecideAutoDisposition(
						self.TraitOrDefault<IMove>() != null, whollyDry,
						namesRearmActors, leash > 0, suppliedHostWithinLeash,
						anyHostWithinLeash, anyHostCanReachUs))
					{
						case SupplyHuntMath.DryAutoDisposition.SeekRearm:
							foreach (var ap in ammoPools)
								ap.NeedsResupply = false;

							// Self-assigned because the unit has lost the weapon that defines it — which
							// since the Essential predicate landed is NOT the same as "cannot fight at
							// all". The errand is bounded by that reason and ends the moment it lapses.
							// See SeekSupplyProvider. `host` is passed rather than re-picked: it is the
							// nearest AFFORDABLE depot, and ChooseResupplier would return the nearest
							// merely-stocked one.
							AutoRearm(self, true, host);
							break;

						case SupplyHuntMath.DryAutoDisposition.HoldAndFlag:
							foreach (var ap in ammoPools)
								ap.NeedsResupply = true;

							break;

						case SupplyHuntMath.DryAutoDisposition.Evacuate:
							EvacuateForRefund(self, ammoPools);
							break;
					}

					break;

				case ResupplyBehavior.Hold:
					// Stay put, flag for supply truck pickup
					foreach (var ap in ammoPools)
						ap.NeedsResupply = true;

					break;

				case ResupplyBehavior.Evacuate:
					// DETOUR FIRST, if and only if the ammunition is nearer than the way out (user ruling
					// 2026-08-21: "they should still go to any nearby resupply first if it is available,
					// otherwise they evacuate"). A unit told to leave must never travel BACKWARDS, deeper
					// into the fight it was pulled from, to rearm — so this is a strict-nearer test rather
					// than a plain "is there a truck about", and ties go to leaving
					// (SupplyHuntMath.ResupplyBeatsExit).
					//
					// PROXY, stated plainly: the distance compared against is to the evacuation ANCHOR
					// (the SpawnArea the exit is chosen around), not to the literal edge cell the unit
					// will drive to. Resolving that cell means Map.ChooseClosestMatchingEdgeCell, which
					// sorts the whole perimeter and pathfinds per candidate — acceptable once when an
					// evacuation begins, not on every idle-while-dry decision. The anchor sits at the map
					// edge the unit is heading for, so the two agree to within the width of the spawn
					// area; where they disagree, it is by a handful of cells at the destination end, and
					// the error cannot flip a host that is genuinely behind the unit into looking ahead
					// of it. Sharing RotateToEdge's own helper rather than guessing beside it is what
					// keeps even that much honest.
					//
					// No anchor (no SpawnArea on the map) means there is no exit to be nearer than, and
					// RotateToEdge would fall back to the closest edge cell to the unit itself. The leash
					// alone then decides, which is the user's primary criterion anyway — "any NEARBY
					// resupply".
					//
					// CHOSEN ON AFFORDABILITY, not filtered after the fact — the same correction the Auto
					// arm took at :647, and it matters MORE here. Asking ChooseResupplier first returns the
					// nearest host holding ANY stock, so a near-but-poor depot SHADOWS a farther affordable
					// one: the affordability question is then put to the wrong actor, it answers no, and the
					// unit falls through to EvacuateForRefund. On the Auto arm that mistake costs a stall;
					// on this one the unit leaves the map permanently, with a depot that would have served
					// it sitting inside the leash. That is precisely the outcome the user ruling this arm
					// implements forbids — "they should still go to any nearby resupply first if it is
					// available".
					var evacHost = ChooseAffordableResupplier(self, ammoPools);
					if (evacHost != null && SupplyHuntMath.WithinCellBudget(
							evacHost.Location.X - self.Location.X,
							evacHost.Location.Y - self.Location.Y,
							ResolveSeekLeash(ammoPools)))
					{
						var anchor = RotateToEdge.FindClosestSpawnAreaForOwner(self);
						var beatsExit = anchor == null || SupplyHuntMath.ResupplyBeatsExit(
							evacHost.Location.X - self.Location.X,
							evacHost.Location.Y - self.Location.Y,
							anchor.Value.X - self.Location.X,
							anchor.Value.Y - self.Location.Y);

						if (beatsExit)
						{
							foreach (var ap in ammoPools)
								ap.NeedsResupply = false;

							// dispatchedBecauseDry: this is the unit's own errand, bounded by the dryness
							// that prompted it, and it walks home afterwards. If the rearm does not take
							// (host drained, unreachable), the unit goes idle still dry and arrives back
							// here — where the host is now filtered out by CurrentSupply and it leaves.
							//
							// `evacHost` is passed rather than re-picked, exactly as the Auto arm does at
							// :679. Every gate above — affordability, the leash, and beatsExit — was decided
							// ABOUT this actor, so letting AutoRearm fall back to ChooseResupplier would
							// dispatch the unit to a different one: the nearest merely-stocked depot, which
							// is the very host affordability just rejected. Note this argument only became
							// load-bearing when the line above started choosing on affordability; while both
							// calls were ChooseResupplier the re-pick returned the same actor and the
							// omission was inert. That is why the two changes are one change.
							AutoRearm(self, true, evacHost);
							self.ShowTargetLines();
							break;
						}
					}

					// Leave the battlefield via Supply Route
					EvacuateForRefund(self, ammoPools);
					break;
			}
		}

		/// <summary>
		/// <para>Leave the map via the Supply Route and bank the refund. Shared by the standing
		/// Evacuate disposition and by Auto's no-host fallback, which must be the SAME departure down
		/// to the refund — a unit that leaves because nothing can rearm it is not owed less than one
		/// that was told to leave.</para>
		///
		/// <para>GetEvacuationRefund, not GetSellValue: this is the disposition the Evacuate ORDER also
		/// reaches, so it must pay the same handicap-adjusted amount. m270/grad/tos ship
		/// <c>InitialResupplyBehavior: Evacuate</c>, so for them this is the normal way they leave the
		/// map rather than an edge case.</para>
		/// </summary>
		static void EvacuateForRefund(Actor self, IEnumerable<AmmoPool> ammoPools)
		{
			foreach (var ap in ammoPools)
				ap.NeedsResupply = false;

			var amount = self.GetEvacuationRefund();
			self.QueueActivity(false, new RotateToEdge(self, true, amount));
			self.ShowTargetLines();
		}

		public void AutoRearmIfAnyNotFull(Actor self)
		{
			var ammoPools = self.TraitsImplementing<AmmoPool>();
			if (ammoPools.Any() && ammoPools.Any(a => !a.HasFullAmmo) && !self.Info.HasTraitInfo<AircraftInfo>())
				AutoRearm(self, false);
		}

		void INotifyCreated.Created(Actor self)
		{
			UpdateCondition(self);
			modifiers = self.TraitsImplementing<IReloadAmmoModifier>().ToArray();

			self.World.AddFrameEndTask(w =>
			{
				/* RemainingTicks = Util.ApplyPercentageModifiers(Info.ReloadDelay, modifiers.Select(m => m.GetReloadAmmoModifier())); */

				if (Info.FullReloadTicks > 0)
				{
					var reloadCount = Info.ReloadCount;
					if (Info.FullReloadSteps > 0)
					{
						double a = Info.Ammo / Info.FullReloadSteps;
						reloadCount = (int)System.Math.Ceiling(a);
					}

					RemainingTicks = Util.ApplyPercentageModifiers(Info.FullReloadTicks * reloadCount / Info.Ammo, modifiers.Select(m => m.GetReloadAmmoModifier()));
				}
				else
					RemainingTicks = Util.ApplyPercentageModifiers(Info.ReloadDelay, modifiers.Select(m => m.GetReloadAmmoModifier()));
			});
		}

		void INotifyAttack.Attacking(Actor self, in Target target, Armament a, Barrel barrel)
		{
			if (a != null && Info.Armaments.Contains(a.Info.Name))
			{
				TakeAmmo(self, a.Info.AmmoUsage);

				if (!HasAmmo && self.TraitOrDefault<IMove>() != null && !self.Info.HasTraitInfo<AircraftInfo>())
					AutoRearmIfDry(self);
			}
		}

		void INotifyBecomingIdle.OnBecomingIdle(Actor self)
		{
			AutoRearmIfDry(self);
		}

		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "Resupply")
			{
				if (self.World.IsGameOver)
					return;

				var ammoPools = self.TraitsImplementing<AmmoPool>();
				if (ammoPools != null)
				{
					foreach (var ammoPool in ammoPools)
					{
						// OpenRA.Mods.Common.Traits.AmmoPool.AutoRearm(self);
						// Desyncs, orders needs to be synced, some kind of handshake involved.
						ammoPool.AutoRearmIfAnyNotFull(self);
					}
				}
			}
		}

		/// <summary>
		/// <para>Send this actor to the nearest rearm source.</para>
		///
		/// <para><paramref name="dispatchedBecauseDry"/> records WHY, and has no default on purpose: every
		/// caller must say whether this errand is the unit's own answer to being unable to fight
		/// (<see cref="AllPoolsEmpty(Actor)"/>) or a destination the player asked for. Both walking
		/// branches now honour it — <see cref="Activities.SeekSupplyProvider"/> for a provider with
		/// no docking gate, <see cref="Activities.Resupply"/> for one that has (the Logistics
		/// Centre). The <see cref="Activities.RideTransport"/> branch does not, and no shipped rearm
		/// host is boardable, so it is unreachable rather than merely unhandled.</para>
		/// </summary>
		/// <param name="host">The destination, when the caller has already chosen one. Null re-picks via
		/// <see cref="ChooseResupplier"/>, which is every pre-existing caller's behaviour. It exists
		/// because the Auto arm picks the nearest AFFORDABLE host
		/// (<see cref="ChooseAffordableResupplier"/>) and letting this method re-pick would send the unit
		/// to the nearest merely-stocked one instead — re-introducing, one call deeper, exactly the
		/// shuttle that choosing on affordability was meant to prevent.</param>
		public static void AutoRearm(Actor self, bool dispatchedBecauseDry, Actor host = null)
		{
			var nearestResupplier = host ?? ChooseResupplier(self);

			if (nearestResupplier != null)
			{
				// SupplyProvider host without a docking gate (TRUK, SUPPLYCACHE) —
				// passive rearm model. Use SeekSupplyProvider so the unit re-picks
				// if its target runs out of supply mid-route, and shows a target line.
				var supplyProvider = nearestResupplier.TraitOrDefault<SupplyProvider>();
				if (supplyProvider != null && string.IsNullOrEmpty(supplyProvider.Info.DockedCondition))
				{
					if (self.TraitOrDefault<IMove>() != null)
						self.QueueActivity(false, new SeekSupplyProvider(self, nearestResupplier, dispatchedBecauseDry));

					return;
				}

				// RearmsUnits host (logisticscenter, etc.) — existing dock/rearm behavior
				var cargo = nearestResupplier.TraitOrDefault<Cargo>();
				if (cargo != null && self.Info.HasTraitInfo<PassengerInfo>())
				{
					var passenger = self.TraitOrDefault<Passenger>();
					if (passenger != null && cargo.HasSpace(self.Info.TraitInfo<PassengerInfo>().Weight))
					{
						self.QueueActivity(false, new RideTransport(self, Target.FromActor(nearestResupplier), null));
						return;
					}
				}

				// PITFALL: LOGISTICSCENTER is a SupplyProvider with DockedCondition (no RearmsUnits) — falls through here.
				// Trait<RearmsUnits>() would crash; use the trait's CloseEnough if present, else dock-tight WDist.Zero.
				var rearmsUnits = nearestResupplier.TraitOrDefault<RearmsUnits>();
				var closeEnough = rearmsUnits != null ? rearmsUnits.Info.CloseEnough : WDist.Zero;
				self.QueueActivity(false, new Resupply(self, nearestResupplier, closeEnough, dispatchedBecauseDry: dispatchedBecauseDry));
			}
			else
			{
				// No resupplier found — flag for supply truck pickup instead of evacuating.
				// Evacuation only happens when ResupplyBehavior is explicitly set to Evacuate.
				var ammoPools = self.TraitsImplementing<AmmoPool>();
				foreach (var ap in ammoPools)
					ap.NeedsResupply = true;
			}
		}

		/// <summary>
		/// <para>Is this actor already on its way to (or sitting at) a rearm source? Covers every activity
		/// AutoRearm can queue, plus the infantry-only proximity errand.</para>
		///
		/// <para>AutoRearm queues with QueueActivity(false, …), which CANCELS the current activity — so any
		/// caller that re-dispatches on a cadence must ask this first, or a unit whose scan interval
		/// beats its travel time tears down and re-plans the same run forever without ever arriving.</para>
		///
		/// <para>WALKS THE WHOLE QUEUE, not just the head, and that is load-bearing. CancelActivity only calls
		/// Cancel on the current activity (Actor.cs:400-403), which raises IsCanceling — the cancelled
		/// activity stays HEAD until it winds down, and the resupply we just queued sits BEHIND it. A
		/// foot soldier takes ~41 ticks to finish the cell he is crossing, which is longer than the
		/// scan intervals asking this question, so a head-only test answers "no" during exactly the
		/// window it exists to cover: the errand is issued twice, and any bot module gating on this is
		/// free to re-task the unit and destroy it.</para>
		/// </summary>
		public static bool IsSeekingRearm(Actor self)
		{
			for (var a = self.CurrentActivity; a != null; a = a.NextActivity)
				if (a is SeekSupplyProvider || a is Resupply || a is RideTransport || a is SeekSuppliesAndReturn)
					return true;

			return false;
		}

		public static Actor ChooseResupplier(Actor self)
		{
			return RearmCandidates(self, true).ClosestToIgnoringPath(self);
		}

		/// <summary>
		/// <para>The nearest host that can afford at least one batch of something we are short of, or
		/// null. Distinct from <see cref="ChooseResupplier"/>, which returns the nearest host holding ANY
		/// supply at all — a depot down to 750 against a 1500 batch is a destination that cannot serve us
		/// on arrival, and dispatching to it produces a shuttle rather than a rearm.</para>
		///
		/// <para>Filters BEFORE picking, via <see cref="SupplyHuntMath.SelectNearestAffordable"/>. Doing
		/// it the other way round — asking <see cref="HostCanAffordSomethingWeNeed"/> about whatever
		/// <see cref="ChooseResupplier"/> already returned — silently strands a unit whenever a nearer
		/// depot is too poor and a farther one is not; see that helper for the worked case.</para>
		/// </summary>
		public static Actor ChooseAffordableResupplier(Actor self, IEnumerable<AmmoPool> pools)
		{
			var candidates = new List<SupplyHuntMath.Candidate>();
			var actors = new List<Actor>();
			var affordable = new List<bool>();

			foreach (var a in RearmCandidates(self, true))
			{
				candidates.Add(new SupplyHuntMath.Candidate(
					(a.CenterPosition - self.CenterPosition).HorizontalLengthSquared, a.ActorID));
				actors.Add(a);
				affordable.Add(HostCanAffordSomethingWeNeed(a, pools));
			}

			var best = SupplyHuntMath.SelectNearestAffordable(candidates, affordable);
			return best < 0 ? null : actors[best];
		}

		/// <summary>
		/// Whether this actor declares any rearm actors at all. False for anything without a
		/// <c>Rearmable</c> — most visibly <c>^CrewMember</c>, whose whole inheritance chain
		/// (^CamoSoldier → ^Soldier → ^Infantry) declares none. Such a unit has no depot to be missing,
		/// so <see cref="SupplyHuntMath.DecideAutoDisposition"/> excludes it from the fallback rather
		/// than reading its empty candidate set as "your supply line is gone".
		/// </summary>
		public static bool NamesRearmActors(Actor self)
		{
			var rearmInfo = self.Info.TraitInfoOrDefault<RearmableInfo>();
			return rearmInfo != null && rearmInfo.RearmActors.Count > 0;
		}

		/// <summary>
		/// <para>Whether ANY rearm host that EXISTS is able to travel to this actor. Asked when nothing
		/// can serve us right now, to tell "someone may still drive to me" apart from "nothing ever
		/// will" — see <see cref="SupplyHuntMath.DecideAutoDisposition"/>, where the consequence lives.</para>
		///
		/// <para>Mobility is the test because NeedsResupply's ONLY reader engine-wide,
		/// <c>SupplyProvider.FindNeedsResupplyTarget</c> (SupplyProvider.cs:622), is swept by a
		/// Hunt-stance provider that then DRIVES to the flagged unit. A building cannot answer the flag
		/// however much supply it holds.</para>
		///
		/// <para>IGNORES CURRENT SUPPLY on purpose. A drained truck is not a truck that has ceased to
		/// exist: it restocks and can still come. Filtering on stock here is precisely the conflation of
		/// "drained" with "absent" that made the first cut of this feature evacuate units against a
		/// recoverable condition.</para>
		///
		/// <para>Asks the whole candidate set rather than just the nearest, because the nearest may be a
		/// dropped SUPPLYCACHE (static) while a truck the unit could genuinely wait for sits further
		/// out. Reading only <see cref="ChooseResupplier"/>'s single answer would evacuate that unit.</para>
		///
		/// <para>KNOWN NARROWING, stated rather than hidden: this asks whether a host can MOVE, not
		/// whether it would be WILLING (a truck outside Hunt stance never sweeps) nor whether it could
		/// serve this actor on arrival (a provider only pushes to units carrying its RearmCondition).
		/// The second is exact in the shipped corpus — no vehicle names a mobile provider in RearmActors
		/// at all, and truk/supplycache serve replenish-soldiers only — so a mobile candidate here is
		/// always one that could really serve. Re-check that if a vehicle is ever given
		/// <c>RearmActors: truk</c>.</para>
		/// </summary>
		public static bool AnyMobileRearmHost(Actor self)
		{
			foreach (var candidate in RearmCandidates(self, false))
				if (candidate.TraitOrDefault<IMove>() != null)
					return true;

			return false;
		}

		/// <summary>
		/// <para>Whether any rearm host that EXISTS — drained or not — sits inside
		/// <paramref name="leashCells"/>. This is the "worth waiting beside" test: an empty Logistics
		/// Centre we are parked next to is one <c>AbsorbsSupplyCache</c> transfer away from serving
		/// again, so its emptiness is not a reason to leave the map.</para>
		///
		/// <para>Chessboard metric, matching the leash rather than <see cref="ChooseResupplier"/>'s
		/// Euclidean nearest-pick. That mismatch is pre-existing and still costs a stall in one shape
		/// (nearest-by-Euclid outside the leash while a farther host is inside it) — but because THIS
		/// test sweeps every host with the leash's own metric, that shape can no longer be mistaken for
		/// hopelessness and evacuated.</para>
		/// </summary>
		public static bool AnyRearmHostWithinLeash(Actor self, int leashCells)
		{
			foreach (var candidate in RearmCandidates(self, false))
				if (SupplyHuntMath.WithinCellBudget(
					candidate.Location.X - self.Location.X,
					candidate.Location.Y - self.Location.Y,
					leashCells))
					return true;

			return false;
		}

		/// <summary>
		/// <para>Every host this actor is entitled to rearm at, unordered. Factored out of
		/// <see cref="ChooseResupplier"/> so the "does one exist", "is one near" and "can one come to
		/// us" questions cannot drift apart from the filters that define the set.</para>
		///
		/// <para><paramref name="requireSupply"/> is the ONE axis on which callers differ, and it is a
		/// parameter rather than a second query because the difference between a host that is drained
		/// and one that is absent is the whole subject of
		/// <see cref="SupplyHuntMath.DecideAutoDisposition"/>. Note that with <c>RearmsUnits</c> absent
		/// from mods/ww3mod entirely, the first branch below is dead in this mod and EVERY host is
		/// supply-gated — which is why passing true here answers a materially narrower question than
		/// "is there a depot".</para>
		/// </summary>
		static IEnumerable<Actor> RearmCandidates(Actor self, bool requireSupply)
		{
			var rearmInfo = self.Info.TraitInfoOrDefault<RearmableInfo>();

			if (rearmInfo == null)
				return Enumerable.Empty<Actor>();

			// Traditional RearmsUnits hosts (logisticscenter, etc.)
			var rearmsUnitsActors = self.World.ActorsHavingTrait<RearmsUnits>()
				.Where(a => !a.IsDead
					&& a.Owner == self.Owner
					&& rearmInfo.RearmActors.Contains(a.Info.Name));

			// SupplyProvider hosts (TRUK, SUPPLYCACHE) with supply remaining.
			//
			// The parenthetical is CORPUS-DEPENDENT, not a property of this method: the set is whatever
			// the recipient's RearmActors names. It was false for SUPPLYCACHE until 2026-08-21 — no
			// RearmActors list anywhere contained it, so the cache branch of this query was unreachable
			// and crates were push-only. Infantry now name it, so a soldier walks to his own crate.
			// Re-read the YAML before trusting the names here; do not infer them from this comment.
			//
			// `a.Owner == self.Owner` is STRICT EQUALITY, and that is what keeps "seek ammo" from also
			// meaning "steal loot": an enemy or merely-allied crate is never a destination, however
			// close or however full. Taking an enemy crate is a separate mechanism (ProximityCapturable
			// on contact), and one a unit sent here can still trigger incidentally by passing an enemy
			// crate en route — that is pre-existing and unrelated to this filter.
			var supplyProviderActors = self.World.ActorsHavingTrait<SupplyProvider>()
				.Where(a => !a.IsDead
					&& a.Owner == self.Owner
					&& rearmInfo.RearmActors.Contains(a.Info.Name)
					&& (!requireSupply || a.Trait<SupplyProvider>().CurrentSupply > 0));

			return rearmsUnitsActors.Concat(supplyProviderActors);
		}

		void INotifyAttack.PreparingAttack(Actor self, in Target target, Armament a, Barrel barrel) { }

		void UpdateCondition(Actor self)
		{
			if (string.IsNullOrEmpty(Info.AmmoCondition))
				return;

			while (CurrentAmmoCount > tokens.Count && tokens.Count < Info.Ammo)
				tokens.Push(self.GrantCondition(Info.AmmoCondition));

			while (CurrentAmmoCount < tokens.Count && tokens.Count > 0)
				self.RevokeCondition(tokens.Pop());
		}

		public void Reload(Actor self, int reloadDelay = 0, int reloadCount = 0)
		{
			if (reloadDelay == 0) reloadDelay = Info.ReloadDelay;
			if (reloadCount == 0) reloadCount = Info.ReloadCount;

			if (!HasFullAmmo && --RemainingTicks == 0)
			{
				if (Info.FullReloadSteps > 0)
				{
					double a = Info.Ammo / Info.FullReloadSteps;
					reloadCount = (int)System.Math.Ceiling(a);
				}

				if (Info.FullReloadTicks > 0)
					RemainingTicks = Util.ApplyPercentageModifiers(Info.FullReloadTicks * reloadCount / Info.Ammo, modifiers.Select(m => m.GetReloadAmmoModifier()));
				else
					RemainingTicks = Util.ApplyPercentageModifiers(reloadDelay, modifiers.Select(m => m.GetReloadAmmoModifier()));

				GiveAmmo(self, reloadCount);

				if (!string.IsNullOrEmpty(Info.RearmSound))
					Game.Sound.PlayToPlayer(SoundType.World, self.Owner, Info.RearmSound, self.CenterPosition);
			}
		}
	}
}
