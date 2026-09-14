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
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Activities;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("DOOMSDAY / \"Dead Hand\": the replacement for a plain time-limited game. When the clock reaches",
		"zero the map is annihilated by a staggered ICBM salvo instead of the match simply stopping.",
		"",
		"Named for the Soviet Perimeter system, which is what it is: an automatic retaliatory launch that",
		"nobody on the map ordered and nobody can stop.",
		"",
		"THE SEQUENCE, in order, and each stage is separately tunable:",
		"  1. THE FINAL EXCHANGE OPENS. Statistics freeze on that exact tick, before anything is",
		"     launched, the map comes out of the fog, and every surviving side is handed its",
		"     game-enders, fire-ready, for " + nameof(DoomsdayStrikeInfo.FinalExchangeWindowTicks) + ". Players place their own aim points with",
		"     the ordinary targeting UI; a side that places nothing loses nothing by it.",
		"  2. AT ZERO Dead Hand places the rest. Anything a side did not aim is aimed by the",
		"     machine, from the same target list the salvo has always used.",
		"  3. A tight wave of TACTICAL warheads on the point targets: oil derricks and Supply Routes.",
		"  4. A deliberate pause, long enough to read as 'it is over'.",
		"  5. A wave of STRATEGIC warheads on the population centres.",
		"  6. Everything still alive is destroyed, and the winner is resolved from the FROZEN score.",
		"",
		"TWO WAYS IN, ONE ENDING (user ruling, 2026-09-13): the Time Limit reaching zero with this",
		"checkbox ticked, or a side firing a game-ender it was granted through the nuclear exchange.",
		"Both call " + nameof(DoomsdayStrike.BeginFinalExchange) + ", which is idempotent — 'either way the outcome is the",
		"same, the nukes fly and the game ends'.",
		"",
		"RETUNED 2026-09-07 after the user played it: the salvo used to add coverage FILL warheads until",
		"every cell of the map was inside some lethal radius, and it fired the 6 Mt " + nameof(DoomsdayStrikeInfo.CityMissile) + " for",
		"each of them. On river-zeta that was 17 six-megaton detonations on top of the 2 aimed at cities,",
		"and it nearly took the game down. The fill pass is gone and the yields came down with it; see",
		"the class remarks for what that costs.",
		"",
		"Attach to the World actor. Requires " + nameof(TimeLimitManager) + ", which supplies the trigger.")]
	public class DoomsdayStrikeInfo : TraitInfo, ILobbyOptions, Requires<TimeLimitManagerInfo>
	{
		// THE PLAYER-FACING NAME IS "NUCLEAR ENDING"; THE SYMBOL NAMES DELIBERATELY DO NOT FOLLOW IT.
		//
		// This string has been "Doomsday", then "Dead Hand", now this, all on 2026-09-10. The last
		// move had a mechanical argument rather than a stylistic one: THE AUTO-LAUNCH IS NOT THE
		// WHOLE OF IT ANY MORE. At zero every surviving side is handed its game-enders and fifteen
		// seconds to choose targets, and only what nobody aimed is aimed by the machine.
		//
		// CORRECTED 2026-09-13. That paragraph used to end "THE AUTO-LAUNCH IS GONE ... the name
		// described a mechanism that had been removed", and it was PROSE ONLY: no code implemented
		// the window, and NotifyTimerExpired ran the fully automatic salvo. The window exists now
		// (see BeginFinalExchange), so the sentence is true for the first time — but note what it
		// does NOT say. Dead Hand still places every warhead nobody claimed, which is exactly the
		// machine the name describes, so the old name was never as wrong as this argued.
		//
		// The feature is TWO controls and they are now named separately: TimeLimitManager's
		// dropdown says HOW LONG ("Time Limit", world.yaml), and this checkbox says WHAT HAPPENS
		// at zero ("Nuclear ending"). Untick it and the match simply ends on score.
		//
		// The trait, its file, its fields and the `doomsday` option id keep the old name on
		// purpose: the id is wire-visible (saved skirmish settings, replays and any map that sets
		// it), and renaming a lobby option id silently discards the stored value. So this is not
		// drift waiting to be tidied up — and note it has now survived three renames of the copy,
		// which is the argument for leaving it alone rather than against. Change the strings,
		// never the symbols.
		[Desc("Label for the lobby checkbox.")]
		public readonly string DoomsdayLabel = "Nuclear ending";

		[Desc("Tooltip for the lobby checkbox.")]
		public readonly string DoomsdayDescription = "When the Time Limit expires, the map is destroyed by a nuclear salvo and the highest score at that moment wins. Turn it off to end on score alone, with no strike. It does nothing while the Time Limit reads No limit.";

		[Desc("Default state of the lobby checkbox.")]
		public readonly bool DoomsdayEnabled = true;

		[Desc("Prevent the checkbox from being changed in the lobby.")]
		public readonly bool DoomsdayLocked = false;

		[Desc("Whether to show the checkbox in the lobby.")]
		public readonly bool DoomsdayCheckboxVisible = true;

		[Desc("Display order for the lobby checkbox.")]
		public readonly int DoomsdayCheckboxDisplayOrder = 12;

		// Lobby option id, so consumers stop repeating the string literal. LobbyOptionsLogic
		// needs it to file this checkbox in the same section as the Doomsday Clock it modifies.
		public const string DoomsdayOptionId = "doomsday";

		[Desc("Run the salvo in TestMode sessions too. Defaults to false, which is what keeps the",
			"existing timed tournament and autotest configurations behaving exactly as they did:",
			"they set a time limit and expect the score comparison, not an apocalypse. The demo",
			"scenario sets this true.")]
		public readonly bool RunInTestMode = false;

		[ActorReference(typeof(BallisticMissileInfo))]
		[FieldLoader.Require]
		[Desc("Missile actor for the opening wave on outlier targets. Must carry " + nameof(BallisticMissile) + ".")]
		public readonly string OutlierMissile = null;

		[ActorReference(typeof(BallisticMissileInfo))]
		[FieldLoader.Require]
		[Desc("Missile actor for the city wave, and for the fill warheads.")]
		public readonly string CityMissile = null;

		[Desc("CONSERVATIVE lethal radius of the SMALLEST warhead in the salvo — the outlier one.",
			"",
			"THIS IS NO LONGER A COVERAGE GUARANTEE. It used to be: the fill pass laid warheads on a",
			"lattice derived from this number until every cell of the map was inside somebody's radius.",
			"That pass is gone (see the class remarks), so this now does exactly two things — it bounds",
			"the jitter, and it caps how far a city's warheads may be spread from its centroid.",
			"",
			"14c0 sits just inside the shipped outlier warhead's blast-wave reach — `Atomic`, whose",
			"ShockwaveDamage MaxRadius is 15c0. It was 20c0, read off a claim about ignition range",
			"rather than blast; the blast contour is the honest number now that nothing downstream",
			"generates warheads from it.")]
		public readonly WDist LethalRadius = new(14 * 1024);

		[Desc("Maximum distance an aim point is displaced by the synced RNG. STRICTLY SMALLER than",
			nameof(LethalRadius) + ": placement is done against (LethalRadius - JitterRadius), so no draw",
			"can move an impact far enough to uncover something the layout had covered.")]
		public readonly WDist JitterRadius = new(4 * 1024);

		[Desc("Minimum distance between two impacts. 'Never detonating too many too close' — this is",
			"what thins a dense line of derricks down to a spread-out salvo.",
			"",
			"MUST NOT EXCEED (" + nameof(DoomsdayStrikeInfo.LethalRadius) + " - " + nameof(DoomsdayStrikeInfo.JitterRadius) + "), and that is a NEW",
			"constraint as of the 2026-09-07 retune. The separation filter drops a candidate when a",
			"kept impact is closer than this, so a dropped asset is only still inside somebody's lethal",
			"radius if this distance fits inside the effective one. It used to hold by luck — 12 against",
			"an effective 16 — and dropping the lethal radius to 14 broke it: at 12 against an effective",
			"10, two of river-zeta's eighteen point targets came out uncovered. It did not matter before",
			"because the fill pass swept up anything the targeting missed; with the fill pass gone this",
			"is the only thing keeping a derrick from being dropped and then not shot.",
			"",
			"DoomsdayCoverageTest.MinSeparationFitsInsideTheEffectiveRadius pins it.")]
		public readonly WDist MinSeparation = new(10 * 1024);

		[Desc("Two buildings within this distance of each other belong to the same city. Applied",
			"transitively, so a ribbon development links into one city rather than several.")]
		public readonly WDist CityLinkDistance = new(8 * 1024);

		[Desc("A cluster with at least this many buildings is a CITY and gets " + nameof(DoomsdayStrikeInfo.WarheadsPerCity),
			"large warheads. Anything smaller is an OUTLIER and gets one small warhead in the opening wave.")]
		public readonly int CityMinBuildings = 4;

		[Desc("Large warheads aimed at each city, spread about its centre along its long axis.",
			"",
			"ONE, not two. The user's ceiling for river-zeta is \"one nuke per Derrick, plus the two city",
			"destroyers\" — and river-zeta clusters into exactly two cities, so one warhead each IS the",
			"two city destroyers. At two per city the same map produced four. The spread machinery in",
			nameof(DoomsdayMath.CityAimPoints) + " is retained and still tested; at a count of 1 it returns the centroid.")]
		public readonly int WarheadsPerCity = 1;

		[Desc("Actor types that are always outlier targets in their own right, regardless of what they",
			"cluster with — the high-value point targets. Oil derricks are the case the design names.")]
		public readonly HashSet<string> HighValueTypes = new() { "oilb", "supplyroute" };

		[Desc("Actor types excluded from target enumeration even though they are buildings. Walls and",
			"tank traps are structures to the engine and scenery to a targeteer.")]
		public readonly HashSet<string> ExcludeTypes = new() { "barb", "sbag", "fenc", "brik", "cycl", "tanktrap", "tanktrap2" };

		[Desc("Target type that marks an actor as a REAL STRUCTURE for the purposes of this mode. An actor",
			"is enumerated only if some " + nameof(Targetable) + " on it declares this, or its type is listed in",
			nameof(DoomsdayStrikeInfo.HighValueTypes) + ".",
			"",
			"THIS TEST REPLACED A TRAIT TEST THAT WAS WRONG, and the correction is the single biggest",
			"reason the salvo shrank. The enumeration used to be `HasTraitInfo<BuildingInfo>()`, on the",
			"stated grounds that river-zeta's 1713 v17 and 864 v16 crop tiles \"inherit ^CivField and have",
			"no Building trait at all\". They do have one — ^CivField carries `Building: Footprint: x,",
			"Dimensions: 1,1` (civilian.yaml), and so does ^Tree. The premise was false, so 4459 of",
			"river-zeta's 4544 actors were being clustered as buildings: the fields tile the map, single",
			"linkage joined them into two blobs of ~2200 members each, and the mode's idea of a \"city\"",
			"was the centroid of half a map of rice paddy.",
			"",
			"Target types are the right test because they are what a targeteer can see. ^CivField",
			"deliberately declares NO " + nameof(Targetable) + " at all (there is a PITFALL comment in civilian.yaml",
			"saying why), and ^Tree declares `Trees` — so both fall out, while ^BasicBuilding and",
			"^CivBuilding both declare Structure and stay in. SUPPLYROUTE declares only NoAutoTarget and",
			"is carried by the " + nameof(DoomsdayStrikeInfo.HighValueTypes) + " bypass instead.")]
		public readonly string StructureTargetType = "Structure";

		[Desc("Height above the aim point at which a warhead enters the map.")]
		public readonly WDist SpawnAltitude = new(38 * 1024);

		[Desc("Horizontal distance from the aim point at which a warhead enters the map. Together with",
			nameof(SpawnAltitude) + " this sets the TERMINAL ANGLE, which is the whole point: 38c0 over",
			"5c0 is a slope of 7.6, an 82.5-degree descent, against the ~10 degrees the shipped strike",
			"missiles fly. Re-entry, not an artillery arc.",
			"",
			"MUST BE NON-ZERO. BallisticMissileFly divides by the horizontal distance and completes",
			"immediately at zero (BallisticMissileFly.cs:281-283), so a purely vertical drop would",
			"teleport onto the target and detonate on its first tick.")]
		public readonly WDist ApproachDistance = new(5 * 1024);

		[Desc("Ticks between consecutive impacts inside one wave.")]
		public readonly int WithinWaveTicks = 2;

		[Desc("THE PAUSE: ticks between the last outlier impact and the first city impact. The single",
			"most important timing value in the sequence — long enough that the viewer has concluded it",
			"is over, short enough that the whole salvo stays inside a few seconds. 40 ticks is 2.4s at",
			"the mod's 60ms timestep.")]
		public readonly int OutlierToCityPauseTicks = 40;

		[Desc("Ticks after the LAST impact before anything still alive is destroyed outright. This is",
			"the backstop that makes 'nothing survives' unconditional rather than contingent on warhead",
			"tuning — see the class remarks.")]
		public readonly int AnnihilationDelayTicks = 90;

		[Desc("Ticks after the annihilation before the win/loss verdict is applied from the frozen score.")]
		public readonly int ResolutionDelayTicks = 30;

		[Desc("Ticks of lead-in between the clock expiring and the first impact.")]
		public readonly int LeadInTicks = 30;

		[Desc("THE FINAL EXCHANGE WINDOW: how long every surviving side holds its game-enders and may",
			"place them itself before Dead Hand places the rest. 250 ticks is 15.0 s at the mod's 60 ms",
			"timestep — NOT 25 tps, which would read this as 10 s (see conventions.md).",
			"",
			"ZERO OR LESS SKIPS THE WINDOW ENTIRELY and fires the salvo on the trigger tick, which is",
			"byte-for-byte the behaviour this mode had before the window existed. That is the escape",
			"hatch for a scenario that wants the old shape back without stripping the trait.")]
		public readonly int FinalExchangeWindowTicks = 250;

		// DELIBERATELY NOT [GrantedConditionReference]. That attribute states "this trait grants this
		// condition ON ITS OWN ACTOR", and CheckConditions is a strictly per-actor pass
		// (Lint/CheckConditions.cs:33-77): it collects granted and consumed names one actor at a time.
		// This trait sits on the WORLD actor and grants onto the PLAYER actors, a shape the lint cannot
		// model — annotating it would raise "Actor type `world` grants conditions that are not
		// consumed" on every run, which is a warning that is simply false rather than a floor worth
		// keeping. The consumer side is still checked where it matters: the two powers' own
		// RequiresCondition is consumed on the player actor, where GrantConditionOnNuclearRelease's
		// annotated grant satisfies it.
		[Desc("Condition granted to EVERY surviving player actor when the window opens, and held for",
			"the rest of the match. It is the game-ender rung of the nuclear ladder — the same name",
			nameof(GrantConditionOnNuclearReleaseInfo) + " grants at " + nameof(NuclearRung.GameEnder) + " — so a power already gated on it",
			"needs no second condition and no edit.",
			"",
			"GRANTED HERE RATHER THAN THROUGH A NEW TRAIT, for one reason: Actor.GrantCondition applies",
			"IMMEDIATELY (Actor.cs:725-733 calls UpdateConditionState, which notifies synchronously),",
			"so the powers are enabled before the very next line arms them. A polling trait would have",
			"landed the condition a tick later and left the arming to race it.",
			"",
			"It does NOT override the host's arsenal checkbox: the shipped game-enders are gated on",
			"`!nuke-arsenal-disabled && nuclear-release-gameender` (nuclear-arsenal.yaml:239,300), so a",
			"host who turned the arsenal off still gets no cameo, and Dead Hand places for that side.")]
		public readonly string FinalExchangeCondition = "nuclear-release-gameender";

		[Desc("Prerequisites the final exchange is licensed to IGNORE when it arms a game-ender.",
			"",
			"SupportPowerInstance.MakeReady overrides the tech tree wholesale -- it sets",
			"prereqsAvailable true on whatever instance it is handed -- and that is deliberate for",
			"`powers.event`, which no faction provides and which exists so a game-ender is never on",
			"the shop floor. It is NOT deliberate for the faction tier beside it: overriding that",
			"hands an America player Russia's Sarmat and a Russia player America's B83, which is the",
			"one place the arsenal's faction lock did not hold. Everything a power requires that is",
			"not named here must be genuinely held by the player before it is armed.",
			"",
			"EMPTY IS THE STRICT SETTING, not the permissive one: it makes the window respect every",
			"prerequisite, which for the shipped pair means arming nobody outside Sandbox. The",
			"default is the one name the override is actually for.")]
		public readonly string[] OverriddenPrerequisites = { "powers.event" };

		[NotificationReference("Speech")]
		[Desc("Speech notification played to every surviving player when the window opens.",
			"",
			"DELIBERATELY UNSET. There is no recorded line for this moment — rules/sound/notifications.yaml",
			"has AbombPrepping/AbombReady/AlertBuzzer and nothing that says 'place your warheads' — and a",
			"wrong line is worse than none, because this is the one moment in the match a player has",
			"fifteen seconds to act on. The system line and the on-screen banner carry it until a line",
			"is recorded; set this then, with no other edit.")]
		public readonly string FinalExchangeNotification = null;

		[Desc("Damage type applied by the final annihilation sweep.")]
		public readonly BitSet<DamageType> AnnihilationDamageTypes = default;

		IEnumerable<LobbyOption> ILobbyOptions.LobbyOptions(MapPreview map)
		{
			yield return new LobbyBooleanOption(DoomsdayOptionId, DoomsdayLabel, DoomsdayDescription,
				DoomsdayCheckboxVisible, DoomsdayCheckboxDisplayOrder, DoomsdayEnabled, DoomsdayLocked);
		}

		public override object Create(ActorInitializer init) { return new DoomsdayStrike(init.Self, this); }
	}

	/// <summary>
	/// <para>THE MODE. See <see cref="DoomsdayStrikeInfo"/> for the sequence; the interesting parts of the
	/// implementation are the three guarantees it has to keep.</para>
	///
	/// <para>COVERAGE IS NO LONGER TOTAL, AND THAT IS THE POINT OF THE 2026-09-07 RETUNE. The mode used to
	/// run <see cref="DoomsdayMath.UncoveredCells"/> over the whole playable rectangle and drop a fill
	/// warhead on every gap, so that every cell was provably inside some warhead's lethal radius. It
	/// worked, and it is gone, because the user played it and asked for far fewer warheads: on
	/// river-zeta the fill pass alone was 17 of the 19 six-megaton detonations. What the salvo now
	/// covers is what it AIMS at — the high-value point targets and the city clusters. Ground between
	/// them is uncovered on purpose, and a structure that is neither a derrick nor part of a city is
	/// not shot at.</para>
	///
	/// <para>SO "NOTHING SURVIVES" IS NOW THE BACKSTOP'S PROPERTY ALONE, not the salvo's. It used to be both:
	/// a proven geometric cover AND a sweep, deliberately belt-and-braces. Only the sweep is left.
	/// <see cref="Annihilate"/> still destroys everything standing after the last impact, so the mode's
	/// guarantee is unchanged from a player's point of view — what changed is that the warheads are now
	/// spectacle aimed at targets, and the guarantee is carried entirely by the sweep behind them. If
	/// that sweep is ever removed, the guarantee goes with it; there is no longer a second mechanism.</para>
	///
	/// <para>DETERMINISM. Every random draw goes through World.SharedRandom. Nothing in the pipeline iterates
	/// a Dictionary or a HashSet — the asset list is sorted by ActorID before it is used for anything,
	/// and the two Info HashSets are only ever membership-TESTED, never enumerated. This is simulation
	/// state and it must be byte-identical on every client; note this is the exact opposite of the rule
	/// that governs render-only effects like the screen shake, which must avoid SharedRandom.</para>
	/// </summary>
	public class DoomsdayStrike : ITick, INotifyTimeLimit, ISync
	{
		readonly DoomsdayStrikeInfo info;
		readonly World world;
		readonly bool enabled;

		// Cell-space radii, converted once at construction. The math layer is entirely in cells.
		readonly int lethalCells;
		readonly int jitterCells;
		readonly int effectiveCells;
		readonly int minSeparationCells;
		readonly int cityLinkCells;

		// The scheduled salvo, in ascending spawn tick. Consumed from the front by Tick.
		readonly List<(int SpawnTick, WPos Target, string Actor)> pending = new();
		int nextPending;

		bool triggered;
		int lastImpactTick;
		int annihilationTick;
		int resolutionTick;
		bool annihilated;
		bool resolved;
		bool salvoBuilt;

		/// <summary>The fifteen seconds, as bookkeeping. See <see cref="FinalExchangeWindow"/>.</summary>
		readonly FinalExchangeWindow window = new();

		// Sides already handed their game-enders, as "player|powerkey". MEMBERSHIP-TESTED ONLY, never
		// enumerated -- the same licence DoomsdayStrikeInfo's two HashSets have, and for the same
		// reason: a set's enumeration order is not a thing every client agrees about.
		//
		// It is what stops the window being a magazine. A purchased power's bank is emptied by
		// Activate (SupportPowerManager.cs:327), so re-arming an already-armed power every tick would
		// hand a player unlimited game-enders inside the window instead of the one the exchange grants.
		readonly HashSet<string> armed = new();

		// The latest tick at which a PLAYER-PLACED game-ender is due to detonate. The resolution is
		// held past it, so the verdict never lands while the player's own warhead is still in the air.
		int playerImpactTick;

		// A launch reported on a tick the exchange had not yet begun, kept for exactly that tick. See
		// NotifyExchangeLaunch for why one tick of lookback is the whole of what is needed.
		int pendingLaunchReportedTick = -1;
		int pendingLaunchImpactTick;

		/// <summary>True while every surviving side may still place its own game-enders.</summary>
		public bool FinalExchangeOpen => window.Phase == FinalExchangePhase.Open;

		/// <summary>Ticks left to place. What the countdown banner reads; zero outside the window.</summary>
		public int FinalExchangeTicksRemaining => window.TicksRemaining(world.WorldTick);

		// ==== SYNCED, BECAUSE ALL THREE DECIDE WHAT HAPPENS TO THE MATCH ====
		// Same argument as DefconEscalation's: ISync is load-bearing rather than decoration, since
		// Actor.cs:206 hashes a trait only when `trait is ISync`. The phase is an int projection
		// because the hasher is IL-emitted and cannot hash an enum. A client that disagreed about
		// whether the window was open would disagree about who may fire a 1.2 Mt warhead.
		[Sync]
		public int FinalExchangePhaseValue => (int)window.Phase;

		[Sync]
		public int FinalExchangeClosesTick => window.ClosesTick;

		[Sync]
		public int FinalExchangePlacements => window.PlacementCount;

		/// <summary>
		/// True from the trigger until the verdict is applied. While set, the ordinary victory checks
		/// stand down — see <see cref="VictoryChecksSuspended"/>.
		/// </summary>
		public bool SalvoInProgress { get; private set; }

		public DoomsdayStrike(Actor self, DoomsdayStrikeInfo info)
		{
			this.info = info;
			world = self.World;

			var option = world.LobbyInfo.GlobalSettings.OptionOrDefault(DoomsdayStrikeInfo.DoomsdayOptionId, info.DoomsdayEnabled.ToString());
			enabled = bool.TryParse(option, out var parsed) ? parsed : info.DoomsdayEnabled;

			lethalCells = info.LethalRadius.Length / 1024;
			jitterCells = info.JitterRadius.Length / 1024;
			effectiveCells = DoomsdayMath.EffectiveRadius(lethalCells, jitterCells);
			minSeparationCells = info.MinSeparation.Length / 1024;
			cityLinkCells = info.CityLinkDistance.Length / 1024;
		}

		/// <summary>
		/// <para>Whether the ordinary win/loss machinery should stand down for this world.</para>
		///
		/// <para>It has to, and the reason is not cosmetic. Once the warheads start landing, players lose their
		/// last units in whatever order the geometry happens to produce, and
		/// <see cref="ConquestVictoryConditions"/> would award the match to whoever survived a few ticks
		/// longer. The user's requirement is that the winner comes from the SCORE as it stood before the
		/// first warhead — so the checks are suspended for the duration and the verdict is applied at the
		/// end from a score that has been frozen the whole time.</para>
		/// </summary>
		public static bool VictoryChecksSuspended(World world)
		{
			var dd = world.WorldActor.TraitOrDefault<DoomsdayStrike>();
			return dd != null && dd.SalvoInProgress;
		}

		void INotifyTimeLimit.NotifyTimerExpired(Actor self)
		{
			// PATH (a): the clock. Everything this used to do inline is now the first half of
			// BeginFinalExchange, which path (b) reaches from the other direction.
			BeginFinalExchange(null);
		}

		/// <summary>
		/// <para>OPEN THE FINAL EXCHANGE. The single entry point to the ending, and the only thing the
		/// nuclear-exchange side has to know about this trait.</para>
		///
		/// <para>TWO CALLERS, ONE ENDING (user ruling, 2026-09-13: "Both players have the 15 seconds to
		/// choose targets, or the Dead Hand places them for them. Either way the outcome is the same,
		/// the nukes fly and the game ends"):
		///   (a) the Time Limit reaching zero with the Nuclear ending checkbox ticked — <paramref name="trigger"/> null;
		///   (b) a side firing a game-ender it was granted through the exchange — that side is the
		///       trigger, its warhead is the first of the exchange, and the window opens for everyone else.</para>
		///
		/// <para>IDEMPOTENT. A second call while the exchange is in progress is a no-op: it does not restart
		/// the clock, re-arm anybody, or clear the placement record. Both paths can and will fire on the
		/// same tick — the time limit expiring while a warhead is already in the air is not a rare case —
		/// and the first one in wins.</para>
		///
		/// <para>CALL THIS BEFORE ACTIVATING THE TRIGGERING POWER, on path (b). The launch hook below needs
		/// the exchange to exist when the warhead is reported so it can hold the resolution open for that
		/// impact; calling afterwards still works, because a report made on the same tick is absorbed
		/// here, but calling on a LATER tick would leave that one warhead unwaited-for.</para>
		/// </summary>
		public void BeginFinalExchange(Player trigger)
		{
			if (triggered || !enabled)
				return;

			if (TestMode.IsActive && !info.RunInTestMode)
				return;

			triggered = true;

			// ORDER IS LOAD-BEARING, and this is the requirement most easily done superficially.
			// TimeLimitManager notifies WORLD traits before PLAYER traits (TimeLimitManager.cs:150-157),
			// so this runs before ConquestVictoryConditions sees the same event. Freezing here means the
			// score every later reader sees — including the verdict at the end of the salvo — is the score
			// as it stood on the trigger tick, with nothing the exchange or the annihilation does able to
			// move it.
			//
			// THE FREEZE IS AT THE WINDOW'S START, NOT AT ITS END, and that is the user's requirement
			// rather than an implementation convenience: the outcome is decided the moment the exchange
			// opens, so the fifteen seconds must not be a last chance to farm kills for score. A player
			// spending them shooting instead of aiming gains nothing by it.
			FreezeStatistics();

			// Victory checks stand down from the START for the same reason. Without this a side that
			// loses its last unit during the window would be awarded a loss by ConquestVictoryConditions
			// before a single warhead had been placed.
			SalvoInProgress = true;

			// THE MAP COMES OUT OF THE FOG so the exchange can be aimed, and so it can be watched.
			//
			// IT NOW PRECEDES BuildSalvo RATHER THAN FOLLOWING IT, which retires a sequencing argument
			// this file used to make ("the aim points are provably not downstream of the reveal"). The
			// stronger half of that argument is untouched and is what the property actually rests on:
			// BuildSalvo enumerates world.Actors directly and never asks any player what it can see, so
			// it is independent of visibility by construction rather than by running first. See the
			// remarks on RevealMap.
			RevealMap();

			if (window.Begin(world.WorldTick, info.FinalExchangeWindowTicks, SurvivingSides(), trigger?.InternalName))
			{
				// A launch reported earlier on THIS tick is the trigger's own warhead arriving ahead of
				// the call. Absorb it so the resolution waits for it.
				if (pendingLaunchReportedTick == world.WorldTick && pendingLaunchImpactTick > playerImpactTick)
					playerImpactTick = pendingLaunchImpactTick;

				ArmGameEnders();
				AnnounceFinalExchange();
				return;
			}

			// FinalExchangeWindowTicks <= 0: no window, straight to the salvo. Byte-for-byte the mode's
			// behaviour before the window existed.
			PlaceDeadHandSalvo();
		}

		/// <summary>
		/// <para>A game-ender has been put in the air by a player, and is due to detonate at
		/// <paramref name="impactTick"/>. Called from <see cref="MissileStrikePower"/> on the SYNCED
		/// order-resolution path, once per warhead, with the impact tick the power itself computed —
		/// so this cannot drift from the flight the missile actually flies.</para>
		///
		/// <para>It does two separate things, and they are separate on purpose:
		///   * records that this side placed its own — but ONLY for a game-ender, since the window
		///     leaves the lower rungs granted too and a tactical shot is not a placement;
		///   * holds the resolution open past the impact, so the verdict never lands while a player's
		///     own warhead is still in the air. A B83 carries MissileDelay 700 plus its flight, which
		///     is far longer than the whole staged salvo — without this the match would be resolved and
		///     annihilated before the shot the player took landed.</para>
		///
		/// <para>THE ONE-TICK LOOKBACK. A launch reported before the exchange exists is kept for exactly the
		/// tick it was reported on, and <see cref="BeginFinalExchange"/> absorbs it. That covers the only
		/// ordering that can occur in practice — path (b) activating the power and opening the window in
		/// either order within one order's resolution — without keeping an unbounded history.</para>
		/// </summary>
		public static void NotifyExchangeLaunch(World world, Player firer, int impactTick, SupportPowerInfo powerInfo)
		{
			world.WorldActor.TraitOrDefault<DoomsdayStrike>()?.ReportExchangeLaunch(firer, impactTick, powerInfo);
		}

		void ReportExchangeLaunch(Player firer, int impactTick, SupportPowerInfo powerInfo)
		{
			if (!SalvoInProgress)
			{
				// Not (yet) an exchange. Keep it for this tick only; anything older is stale by
				// construction because BeginFinalExchange only ever looks at the current tick.
				if (pendingLaunchReportedTick != world.WorldTick)
				{
					pendingLaunchReportedTick = world.WorldTick;
					pendingLaunchImpactTick = impactTick;
				}
				else if (impactTick > pendingLaunchImpactTick)
					pendingLaunchImpactTick = impactTick;

				return;
			}

			// ONLY A GAME-ENDER IS A PLACEMENT, and the distinction is load-bearing rather than tidy.
			// A side still holding a 1 kt B61 can fire it inside the window — the exchange grants the
			// top rung, it does not revoke the lower ones — and counting that as "this side placed its
			// warheads" would take it out of the Dead Hand list on the strength of a tactical shot.
			//
			// THE SCHEDULE IS EXTENDED FOR THE SHOT EITHER WAY, below. Anything a player put in the air
			// before the verdict should land before the verdict, whatever its yield.
			if (IsGameEnder(powerInfo))
				window.RecordPlacement(firer?.InternalName);

			if (impactTick > playerImpactTick)
				playerImpactTick = impactTick;

			// Once the schedule exists it has to be moved, not just recorded against. Not after the
			// sweep has run: extending the annihilation at that point would not un-kill anything and
			// would only delay a verdict that is already decided.
			if (salvoBuilt && !annihilated)
				ExtendScheduleForImpact(impactTick);
		}

		/// <summary>
		/// Sides that are still in the match, in world.Players order — which is world-creation order and
		/// therefore identical on every client.
		/// </summary>
		IEnumerable<string> SurvivingSides()
		{
			foreach (var p in world.Players)
				if (p.Playable && !p.NonCombatant && p.WinState != WinState.Lost)
					yield return p.InternalName;
		}

		/// <summary>
		/// <para>Hand every surviving side its game-enders, fire-ready, for the duration of the window.</para>
		///
		/// <para>THREE THINGS GATE A GAME-ENDER AND ALL THREE HAVE TO GO, which is the part that is easy
		/// to do superficially — grant the condition, watch the cameo stay absent, and have no idea why:
		///   * THE TIER. Both shipped game-enders carry `Prerequisites: powers.event`, provided by NO
		///     faction and only by the Sandbox lobby option (player.yaml:144, :193). That is the
		///     SHIPPED DEFAULT for both of them, so a condition-only implementation of this feature
		///     hands out nothing in a normal match while looking entirely correct in the code.
		///     <see cref="SupportPowerInstance.MakeReady"/> overrides it per instance; see there for
		///     why not with a second ProvidesPrerequisite.
		///   * THE CONDITION. `RequiresCondition: !nuke-arsenal-disabled &amp;&amp; nuclear-release-gameender`
		///     (nuclear-arsenal.yaml:239,300). Granted here on the player actor, where the powers live.
		///     Actor.GrantCondition applies IMMEDIATELY (Actor.cs:725-733), so the trait is enabled
		///     before the next line runs. The token is deliberately never revoked: the match ends inside
		///     the salvo, and a revoke would only ever race it.
		///   * THE MAGAZINE. Both shipped game-enders are RequiresPurchase, so they are Ready only while
		///     a shot is banked (SupportPowerChargeBank.IconVisible), and nothing about the condition
		///     banks one. <see cref="SupportPowerInstance.MakeReady"/> is what does.</para>
		///
		/// <para>AND A DISABLED POWER DOES NOT CHARGE WHILE IT WAITS. SupportPowerInstance.Tick pins
		/// remainingSubTicks at TotalTicks * 100 on every tick it is disabled (SupportPowerManager.cs:246-248)
		/// and then returns before the countdown — so "grant the condition and let it charge" would hand a
		/// side a power that needed its whole charge interval, which no fifteen-second window can contain.
		/// MakeReady zeroes it. For the shipped pair this is a formality (RequiresPurchase forces
		/// TotalTicks to 0) and it is done anyway, so a game-ender added later on a timer still arrives.</para>
		///
		/// <para>ONCE PER SIDE PER POWER. <see cref="armed"/> is what makes the window a single shot rather
		/// than a magazine — see its declaration.</para>
		/// </summary>
		void ArmGameEnders()
		{
			foreach (var p in world.Players)
			{
				if (!p.Playable || p.NonCombatant || p.WinState == WinState.Lost)
					continue;

				p.PlayerActor.GrantCondition(info.FinalExchangeCondition);

				var manager = p.PlayerActor.TraitOrDefault<SupportPowerManager>();
				if (manager == null)
					continue;

				// SORTED BY KEY. Powers is a Dictionary and its enumeration order is not something
				// every client agrees about; the operations below are order-independent, but this file
				// does not iterate an unordered collection at all and that rule is worth keeping whole.
				var techTree = p.PlayerActor.TraitOrDefault<TechTree>();

				foreach (var key in manager.Powers.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList())
				{
					var instance = manager.Powers[key];
					if (!IsGameEnder(instance.Info))
						continue;

					if (!OwnedByFaction(techTree, instance.Info))
						continue;

					var id = p.InternalName + "|" + key;
					if (!armed.Add(id))
						continue;

					instance.MakeReady();
				}
			}
		}

		/// <summary>
		/// <para>Does this player's faction OWN this game-ender? Every prerequisite the power declares
		/// must be genuinely held, except the ones <see cref="DoomsdayStrikeInfo.OverriddenPrerequisites"/>
		/// licenses the window to ignore.</para>
		/// </summary>
		// MOVED TO NuclearGameEnders ON 2026-09-14 AND NOT COPIED THERE. The retaliation window at
		// NuclearRung.GameEnder hands out the same weapons on the same terms and had NO such check at
		// all -- it gated on SupportPowerInstance.Permitted, which folds in the `powers.event` this
		// override exists for, so its grant could never open and the END band drew no cameo in a
		// normal match. A rule whose two halves pull in opposite directions (override the tier, never
		// the faction) is exactly the thing not to have two copies of, so it is stated once and both
		// paths ask it. The reasoning that used to sit here -- why the tech tree rather than a faction
		// field, why it is not a no-op under Sandbox, why a missing TechTree arms nothing -- moved
		// with it and is unchanged.
		bool OwnedByFaction(TechTree techTree, SupportPowerInfo powerInfo)
		{
			return NuclearGameEnders.OwnedByFaction(techTree, powerInfo, info.OverriddenPrerequisites);
		}

		/// <summary>
		/// <para>Is this power a GAME-ENDER — one of the weapons the exchange hands out? See
		/// <see cref="NuclearGameEnders.Is"/> for why it is asked of the YIELD, and for why the Tsar
		/// Bomba is excluded by the ladder's own constant rather than by name.</para>
		/// </summary>
		// MOVED TO NuclearGameEnders.Is ON 2026-09-14, with its reasoning, for the reason given on
		// OwnedByFaction below: the retaliation window needs the identical question answered and two
		// copies of "which warheads are game-enders" is the second silent copy of the band table this
		// predicate was written to avoid in the first place.
		static bool IsGameEnder(SupportPowerInfo powerInfo)
		{
			return NuclearGameEnders.Is(powerInfo);
		}

		void AnnounceFinalExchange()
		{
			// PITFALL: GameSpeed.Timestep, not world.Timestep. The latter is mutated at runtime by the
			// debug speed button and by test-mode speed multipliers, so this line would announce a
			// different number of seconds for the same 250 ticks depending on what speed the session
			// happened to be running at -- and would then disagree with the countdown band, which
			// converts the same way. Constant per match, so reading it here stays deterministic.
			var seconds = (info.FinalExchangeWindowTicks * world.GameSpeed.Timestep) / 1000;
			TextNotificationsManager.AddSystemLine(
				$"FINAL EXCHANGE. Place your warheads — {seconds} seconds.");

			// Deliberately may be null; see DoomsdayStrikeInfo.FinalExchangeNotification. PlayNotification
			// with a null key is a documented no-op, so this costs nothing until a line is recorded.
			foreach (var p in world.Players)
				if (p.Playable && !p.NonCombatant && p.WinState != WinState.Lost)
					Game.Sound.PlayNotification(world.Map.Rules, p, "Speech",
						info.FinalExchangeNotification, p.Faction.InternalName);
		}

		/// <summary>
		/// The window has expired (or was never opened). Dead Hand places everything nobody claimed and
		/// the staged salvo runs.
		/// </summary>
		void PlaceDeadHandSalvo()
		{
			BuildSalvo();
			salvoBuilt = true;

			// THE RESOLUTION WAITS FOR THE PLAYERS' OWN WARHEADS. A game-ender placed at the top of the
			// window lands long after the staged salvo is done — this is what stops the verdict being
			// applied while it is still in the air.
			if (playerImpactTick > lastImpactTick)
				ExtendScheduleForImpact(playerImpactTick);

			AnnounceDeadHandPlacement();
		}

		void AnnounceDeadHandPlacement()
		{
			TextNotificationsManager.AddSystemLine("DEAD HAND ACTIVATED. Incoming.");

			// THE SPLIT, said out loud. The user's ruling is that placing and not placing reach the same
			// ending, so the only thing left to tell the player is which of the two they took — and a
			// side that never saw a cameo (arsenal off, power unaffordable, never clicked) reads its own
			// name here rather than being left to guess why nothing of theirs flew.
			var placedFor = window.SidesPlacedForByDeadHand().ToList();
			if (placedFor.Count > 0)
				TextNotificationsManager.AddSystemLine(
					"Dead Hand placed for: " + placedFor.JoinWith(", ") + ".");
		}

		/// <summary>
		/// Move the annihilation and the verdict out past an impact that lands later than anything
		/// already scheduled. Monotonic — it only ever pushes the schedule later, so no ordering of
		/// reports can bring the sweep forward onto a warhead still in the air.
		/// </summary>
		void ExtendScheduleForImpact(int impactTick)
		{
			if (impactTick <= lastImpactTick)
				return;

			lastImpactTick = impactTick;
			annihilationTick = lastImpactTick + info.AnnihilationDelayTicks;
			resolutionTick = annihilationTick + info.ResolutionDelayTicks;
		}

		/// <summary>
		/// <para>Stop every per-player accumulator, on the tick the clock expired.</para>
		///
		/// <para>This is a FREEZE OF THE ACCUMULATION, not a snapshot of the display. Two flags do it, and they
		/// were chosen because they are choke points rather than because they are convenient:
		///   * PlayerExperience.Frozen — GiveExperience is the single entry point through which every
		///     score-affecting event in the engine passes (kills via GivesExperience, captures, donations,
		///     infiltration, repairs, Lua). Guarding it there covers all of them at once, which is why the
		///     kills the nukes themselves cause credit nobody.
		///   * PlayerStatistics.Frozen — stops the income/army sampling in its Tick and every
		///     UpdatesPlayerStatistics lifecycle callback, so unit counts, asset values, kill/death tallies
		///     and the composition telemetry all stop where they were.</para>
		/// </summary>
		void FreezeStatistics()
		{
			foreach (var p in world.Players)
			{
				p.PlayerActor.TraitOrDefault<PlayerStatistics>()?.Freeze();
				p.PlayerActor.TraitOrDefault<PlayerExperience>()?.Freeze();
			}
		}

		/// <summary>
		/// Enumerate strategic assets, cluster them, assign warheads, enforce separation, jitter, then
		/// top up for total coverage — and turn the result into spawn ticks.
		/// </summary>
		void BuildSalvo()
		{
			var bounds = world.Map.Bounds;

			// ---- 1. Enumerate. Sorted by ActorID, which is assigned in world-creation order and is
			// therefore identical on every client. Everything downstream inherits this ordering.
			//
			// STRUCTURES, NOT SCENERY — and the test for that is target types, not BuildingInfo. See
			// DoomsdayStrikeInfo.StructureTargetType for why the trait test that used to be here was
			// wrong and what it cost. In short: crop fields and trees both carry a Building trait, so
			// HasTraitInfo<BuildingInfo>() matched 4459 of river-zeta's 4544 actors.
			//
			// THIS READS WORLD STATE ONLY. Nothing here consults any player's MapLayers, explored set or
			// visibility, which is what makes the fog reveal in NotifyTimerExpired safe: it cannot move
			// an aim point. The reveal is also sequenced after this runs, so the salvo is provably
			// computed from pre-reveal state as well as independent of it.
			//
			// Info.Name is lowercased defensively before every comparison, for the same reason
			// UpdatesPlayerStatistics does it (PlayerStatistics.cs): the Rules.Actors dictionary is
			// case-sensitive with lowercased keys, and a yaml-supplied type list that happens to be
			// capitalised would otherwise silently match nothing.
			var buildings = world.Actors
				.Where(a => a.IsInWorld && !a.Disposed
					&& !info.ExcludeTypes.Contains(a.Info.Name.ToLowerInvariant())
					&& (info.HighValueTypes.Contains(a.Info.Name.ToLowerInvariant()) || IsStructure(a.Info)))
				.OrderBy(a => a.ActorID)
				.ToList();

			var highValue = new List<CPos>();
			var cityCandidates = new List<CPos>();
			foreach (var a in buildings)
			{
				if (info.HighValueTypes.Contains(a.Info.Name.ToLowerInvariant()))
					highValue.Add(a.Location);
				else
					cityCandidates.Add(a.Location);
			}

			// ---- 2. Cluster the rest into cities. Small clusters are outliers, not cities.
			var clusters = DoomsdayMath.ClusterAssets(cityCandidates, cityLinkCells);

			// Candidate aim points, in PRIORITY order — the min-separation filter walks this order and
			// keeps the earlier entry when two collide, so cities outrank derricks outrank stragglers.
			var candidates = new List<CPos>();
			var candidateTiers = new List<DoomsdayTier>();

			foreach (var cluster in clusters)
			{
				if (cluster.Count < info.CityMinBuildings)
					continue;

				foreach (var p in DoomsdayMath.CityAimPoints(cityCandidates, cluster, info.WarheadsPerCity, minSeparationCells, effectiveCells))
				{
					candidates.Add(p);
					candidateTiers.Add(DoomsdayTier.City);
				}
			}

			foreach (var p in highValue)
			{
				candidates.Add(p);
				candidateTiers.Add(DoomsdayTier.Outlier);
			}

			// CLUSTERS BELOW CityMinBuildings GET NOTHING, and this is the second half of the count cut.
			// A lone farmhouse used to draw its own warhead as an "outlier"; on river-zeta that was nine
			// more impacts for nine pairs of huts. The user's rule is one warhead per derrick plus the
			// city destroyers, so the outlier wave is now exactly the high-value point targets above.
			// Those buildings still die — Annihilate sweeps them — they are just not aimed at.

			// ---- 3. Jitter, inside a disc strictly smaller than the coverage margin.
			//
			// JITTER RUNS BEFORE THE SEPARATION FILTER, and the order is the whole point. Filtering first
			// would enforce the minimum separation on the IDEAL layout and then let the RNG walk two kept
			// impacts back toward each other — up to 2*JitterRadius, which at the shipped 12-cell
			// separation and 4-cell jitter is a worst case of 4 cells apart. The user's constraint is
			// about where the warheads actually land, so it is enforced on where they actually land.
			//
			// Nothing is lost by doing it this way: the jitter is bounded by the coverage margin, so a
			// jittered point still covers everything its unjittered self did, and the fill pass below runs
			// on the final positions either way.
			for (var i = 0; i < candidates.Count; i++)
			{
				var j = DoomsdayMath.DiscJitter(world.SharedRandom.Next(), world.SharedRandom.Next(0, jitterCells + 1), jitterCells);
				var c = new CPos(candidates[i].X + j.X, candidates[i].Y + j.Y);
				candidates[i] = new CPos(
					Math.Clamp(c.X, bounds.Left, bounds.Right - 1),
					Math.Clamp(c.Y, bounds.Top, bounds.Bottom - 1));
			}

			// ---- 4. Separation, on the jittered positions. The candidate order is the priority order, so
			// a city aim point always survives a collision with a derrick rather than the other way round.
			var aimCells = new List<CPos>();
			var aimTiers = new List<DoomsdayTier>();
			foreach (var i in DoomsdayMath.MinSeparationFilter(candidates, minSeparationCells))
			{
				aimCells.Add(candidates[i]);
				aimTiers.Add(candidateTiers[i]);
			}

			// ---- 5. NO FILL PASS. This is where DoomsdayMath.UncoveredCells / GridSpacing / FillPoints
			// used to run, topping the salvo up until the whole playable rectangle was inside somebody's
			// lethal radius. It is deliberately not called. On river-zeta it emitted 17 aim points, every
			// one of them fired with the CityMissile, and it was the dominant cost in the salvo by a very
			// wide margin — see the class remarks and the retune note on DoomsdayStrikeInfo.
			//
			// The math is kept rather than deleted: it is pure, it is covered by DoomsdayCoverageTest,
			// and that test now uses UncoveredCells to STATE how much ground the salvo leaves alone
			// instead of asserting that it leaves none. Restoring the behaviour is re-adding this block.

			// ---- 6. Schedule. The fill pause is 0 because no impact is ever tagged Fill.
			var timings = new DoomsdayMath.ScheduleTimings(info.WithinWaveTicks, info.OutlierToCityPauseTicks, 0);
			var impacts = DoomsdayMath.BuildSchedule(aimCells, aimTiers, timings);

			var outlierFlight = FlightTicks(info.OutlierMissile);
			var cityFlight = FlightTicks(info.CityMissile);
			var maxFlight = Math.Max(outlierFlight, cityFlight);
			var firstImpactTick = world.WorldTick + info.LeadInTicks + maxFlight;

			// Seeded rather than left at zero so that a degenerate map with nothing to aim at — no
			// buildings AND a bounds so small the fill pass emits nothing — still runs the lead-in and
			// the delays instead of annihilating on the trigger tick, which is what a zero would mean
			// once it was compared against an already-larger WorldTick.
			lastImpactTick = firstImpactTick;

			foreach (var impact in impacts)
			{
				var actor = impact.Tier == DoomsdayTier.Outlier ? info.OutlierMissile : info.CityMissile;
				var flight = impact.Tier == DoomsdayTier.Outlier ? outlierFlight : cityFlight;
				var arrival = firstImpactTick + impact.ArrivalOffset;

				pending.Add((arrival - flight, world.Map.CenterOfCell(impact.Cell), actor));

				if (arrival > lastImpactTick)
					lastImpactTick = arrival;
			}

			// Spawn order, not arrival order: a slower missile aimed at a later arrival can still need to
			// launch before a faster one aimed at an earlier arrival. Sorted by spawn tick so Tick can
			// consume from the front. OrderBy is a stable sort, so equal spawn ticks keep schedule order.
			pending.Sort((a, b) => a.SpawnTick.CompareTo(b.SpawnTick));

			annihilationTick = lastImpactTick + info.AnnihilationDelayTicks;
			resolutionTick = annihilationTick + info.ResolutionDelayTicks;
		}

		/// <summary>
		/// <para>Whether an actor type is a REAL STRUCTURE rather than scenery, tested by target type.</para>
		///
		/// <para>Info-level rather than instance-level on purpose. A <see cref="Targetable"/> may be gated by
		/// RequiresCondition, and this question is "could this thing ever be a structure", not "is it
		/// one on this tick" — reading the Info answers the first, which is the one target enumeration
		/// wants. See <see cref="DoomsdayStrikeInfo.StructureTargetType"/> for why this is a target-type
		/// test and not the BuildingInfo test that used to be here.</para>
		/// </summary>
		bool IsStructure(ActorInfo actorInfo)
		{
			foreach (var t in actorInfo.TraitInfos<ITargetableInfo>())
				if (t.GetTargetTypes().Contains(info.StructureTargetType))
					return true;

			return false;
		}

		/// <summary>
		/// <para>Lift the shroud and the fog for every player, so the salvo is watched over the whole map.</para>
		///
		/// <para>WHY THIS IS NOT THE INTELLIGENCE LEAK IT LOOKS LIKE. Revealing the map normally hands a player
		/// free information, which is exactly why the nuclear-flash-over-fog work brightens the effect
		/// without lifting the shroud. That objection does not apply here and it is worth being explicit
		/// about why, because a future reader will otherwise see a map reveal in gameplay code and assume
		/// it is a bug: by the time this runs the statistics are frozen (see <see cref="FreezeStatistics"/>),
		/// the winner is already determined by the frozen score, the ordinary victory checks are suspended,
		/// and <see cref="Annihilate"/> is going to kill every actor on the map in a few seconds. There is
		/// no information advantage left to leak because there is no game left to play.</para>
		///
		/// <para>VISIBILITY ONLY, NOT TARGETING. MapLayers.Disabled short-circuits IsExplored and forces
		/// FogEnabled false (MapLayers.cs), so it changes what is DRAWN and what queries about visibility
		/// answer — it moves no actor and retargets nothing. The salvo itself cannot be affected in any
		/// case: BuildSalvo enumerates world.Actors directly and never asks a player what it can see, and
		/// it has already run by the time this is called.</para>
		///
		/// <para>DETERMINISM. Disabled is [Sync] simulation state, and this runs from INotifyTimeLimit on a tick
		/// every client agrees on, for every player in the same fixed world.Players order — so all clients
		/// make the same change on the same tick. Setting it on every player rather than only the local one
		/// is what keeps that true; a local-only reveal would desync the [Sync] hash.</para>
		/// </summary>
		void RevealMap()
		{
			// Player.MapLayers is a readonly field resolved with Trait<MapLayers>() at construction
			// (Player.cs:221), so it is never null here; the same direct access GpsWatcher and
			// RevealMapCrateAction use.
			foreach (var p in world.Players)
				p.MapLayers.Disabled = true;
		}

		/// <summary>
		/// Flight time of one warhead, read from the activity's own arithmetic rather than kept in step
		/// with it by hand. Constant across the salvo because every warhead flies the same horizontal
		/// distance — <see cref="DoomsdayStrikeInfo.ApproachDistance"/> — regardless of where it is aimed.
		/// Terrain height under the aim point does not enter into it: hDist is a HORIZONTAL length.
		/// </summary>
		int FlightTicks(string actorType)
		{
			var missileInfo = world.Map.Rules.Actors[actorType].TraitInfo<BallisticMissileInfo>();
			return BallisticMissileFly.EstimateArcTicks(missileInfo, info.ApproachDistance.Length);
		}

		void ITick.Tick(Actor self)
		{
			if (!triggered)
				return;

			// THE WINDOW. Tick reports the closing edge exactly once (FinalExchangeWindow.Tick), so the
			// Dead Hand placement hangs off it with no second flag here. While it is still open there is
			// nothing scheduled yet — the salvo does not exist until the window shuts.
			if (window.Phase == FinalExchangePhase.Open)
			{
				if (!window.Tick(world.WorldTick))
					return;

				PlaceDeadHandSalvo();
			}

			while (nextPending < pending.Count && pending[nextPending].SpawnTick <= world.WorldTick)
			{
				Launch(pending[nextPending].Target, pending[nextPending].Actor);
				nextPending++;
			}

			if (!annihilated && world.WorldTick >= annihilationTick)
			{
				annihilated = true;
				Annihilate();
			}

			if (!resolved && world.WorldTick >= resolutionTick)
			{
				resolved = true;
				Resolve();
			}
		}

		/// <summary>
		/// <para>Put one warhead in the air on a re-entry trajectory.</para>
		///
		/// <para>STEEPNESS COMES FROM THE GEOMETRY, NOT FROM LaunchAngle, and that distinction is the whole
		/// design. Raising LaunchAngle on a BallisticMissile scales the arc apex with shot length
		/// (BallisticMissileFly.cs:62-63), so the same setting produces a different trajectory on a big
		/// map than on a small one — which is exactly why the shipped strike missiles sit at a deliberately
		/// low 30 raw units. Here the missile is spawned high and CLOSE, so the descent is steep by
		/// construction and identical on every map: SpawnAltitude over ApproachDistance, 38c0 over 5c0,
		/// is a constant slope of 7.6 whatever the map is. The missile actors set LaunchAngle 0, which
		/// makes the arc term vanish entirely and leaves a dead-straight 82.5-degree descent.</para>
		/// </summary>
		void Launch(WPos target, string actorType)
		{
			// Bearing is drawn from SharedRandom so the salvo does not arrive in parade formation, all on
			// the same heading. It affects only the direction the missile comes IN from; the aim point and
			// therefore the coverage argument are already fixed.
			var bearing = new WAngle(world.SharedRandom.Next(0, 1024));
			var offset = new WVec(0, -info.ApproachDistance.Length, 0).Rotate(WRot.FromYaw(bearing));
			var spawnPos = target + offset + new WVec(0, 0, info.SpawnAltitude.Length);

			var missile = world.CreateActor(false, actorType, new TypeDictionary
			{
				new CenterPositionInit(spawnPos),
				// The world-owning player, NOT a lookup for a player literally named "Neutral": a map is
				// free to call its OwnsWorld player anything, and First() on a missing name throws.
				new OwnerInit(world.WorldActor.Owner),
				new FacingInit((target - spawnPos).Yaw),
			});

			// ORDERING IS LOAD-BEARING, and it is the same handshake MissileStrikePower performs
			// (MissileStrikePower.cs:118-141): BallisticMissile.AddedToWorld queues BallisticMissileFly,
			// whose constructor reads Target.CenterPosition unconditionally, so the Target must be set
			// between building the actor and adding it to the world.
			var bm = missile.Trait<BallisticMissile>();
			bm.Target = Target.FromPos(target);
			world.AddFrameEndTask(w => w.Add(missile));
		}

		/// <summary>
		/// <para>The backstop. Destroy everything still standing, so "nothing survives" is a property of the
		/// mode rather than a property of this week's warhead tuning. See the class remarks for why this
		/// exists alongside a proven geometric cover rather than instead of one.</para>
		///
		/// <para>Statistics are already frozen, so none of these deaths reach anybody's score.</para>
		/// </summary>
		void Annihilate()
		{
			// Snapshot first: Kill mutates the actor list through husks and death effects, and iterating
			// world.Actors lazily while that happens is how this would throw.
			var doomed = world.Actors
				.Where(a => a.IsInWorld && !a.Disposed && a.Info.HasTraitInfo<HealthInfo>())
				.OrderBy(a => a.ActorID)
				.ToList();

			foreach (var a in doomed)
				if (a.IsInWorld && !a.Disposed)
					a.Kill(a, info.AnnihilationDamageTypes);

			TextNotificationsManager.AddSystemLine("Total strategic annihilation.");
		}

		/// <summary>
		/// <para>Apply the verdict from the frozen score.</para>
		///
		/// <para>This deliberately REUSES the shipped time-limit resolution rather than reimplementing it: the
		/// suspension is lifted and <see cref="INotifyTimeLimit.NotifyTimerExpired"/> is re-raised on the
		/// player actors, which runs ConquestVictoryConditions' existing highest-Experience-wins
		/// comparison. Because PlayerExperience has been frozen since the expiry tick, that comparison
		/// reads exactly the numbers it would have read then — which is what makes the freeze
		/// load-bearing rather than decorative, and is the property DoomsdayStatsFreezeTest pins.</para>
		///
		/// <para>SIMULTANEOUS ELIMINATION IS NOT A CASE HERE. Every player was destroyed on the same tick by
		/// the sweep above, but no player has a WinState yet, because the victory checks were suspended
		/// for the whole salvo. So the tie-break never runs on "who lost their last unit last" — there is
		/// exactly one ordering decision, taken here, over frozen numbers. A genuine score TIE resolves
		/// through the existing OrderByDescending, which is a stable sort over world.Players in its fixed
		/// creation order: the earliest-seated tied player wins, identically on every client.</para>
		/// </summary>
		void Resolve()
		{
			SalvoInProgress = false;

			foreach (var p in world.Players)
				foreach (var ntl in p.PlayerActor.TraitsImplementing<INotifyTimeLimit>())
					ntl.NotifyTimerExpired(p.PlayerActor);
		}
	}
}
