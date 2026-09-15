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

/*
 * THE NUCLEAR EXCHANGE -- Escalation's arms-control model, v2, as the user ruled it on 2026-09-15
 * (manager-2b944571 decision 03, spec 02). TWO NUMBERS PER SIDE AND NOTHING ELSE:
 *
 *     Level         -- 0 (Hold), or 1..5. The highest band this side may fire. NEVER FALLS.
 *     CooldownTicks -- one countdown per SIDE. While it is above zero, NO nuclear power belonging
 *                      to ANY player on that side may fire, at any band.
 *
 * A plain class with no dependency on Actor, World or the lobby, for exactly the reason
 * DefconEscalationState, NuclearReleaseLadder and NuclearUnlockSchedule are plain classes and each
 * says so in its own header: nothing in OpenRA.Test can construct a World, so arithmetic living
 * inside a trait method is arithmetic verified by reading only.
 *
 * ==== WHAT V2 REPLACES, AND WHY THE OLD SHAPE IS GONE RATHER THAN TUNED ====
 * v1 (decision 01) gave a side a PERMANENT level plus a RETALIATION WINDOW one band above whatever
 * it was last hit with, and put each BAND on its own regeneration timer. The user played it and
 * ruled against it: "too many nukes in flight, no chance to build forces". The window was the main
 * source of that -- it granted a bigger weapon AND demanded it be used inside a few minutes, so
 * every exchange became a burst -- and N bands on N independent timers let a side fire several
 * warheads a minute underneath the burst.
 *
 * Lengthening the timers could not fix it: with per-band clocks a side still holds up to four
 * ready warheads at once, so the RATE is the number of bands, not the interval. The window could
 * not be kept either, because "reply within 1:00 or lose the grant" is the reply-or-lose pressure
 * the ruling rejects by name. So both are deleted rather than retuned, and what is left is the
 * smallest model that still says the one thing the design has always said: USING NUKES ESCALATES
 * THE ENEMY.
 *
 * ==== THE RULES, IN THE ORDER THEY FIRE ====
 *  1. RELEASE. Unchanged, and still owned by NuclearReleaseLadder's gate on DefconEscalation: a
 *     countdown runs from DEFCON 1 and on expiry EVERY side's Level becomes Kiloton with no
 *     cooldown. Before that every side is at Hold and no warhead of any yield is permitted, so
 *     rule 3 cannot fire.
 *  2. AVAILABILITY. A side may fire band b iff b <= Level AND CooldownTicks == 0. Both halves are
 *     side-wide: a team of two fires ONCE per cooldown between them, not once each.
 *  3. FIRING. Side A fires band b (one activation, however many warheads the salvo delivers):
 *         A.CooldownTicks = the cooldown for band b            -- the firer pays, alone
 *         for every ENEMY side B: B.Level = max(B.Level, min(b + 1, 5))   -- PERMANENT
 *     A's own Level is unchanged by its own fire, and A's allies share A's side, so they are not a
 *     separate case: they are the same row.
 *  4. END. At Level 5 the side's NATIONAL game-ender is fireable under the same cooldown rule.
 *     Firing one sets FinalExchange on the outcome and takes NO cooldown -- the match is over, and
 *     a lockout the match never outlives would be a number nobody reads.
 *  5. ESCALATION IS ASYMMETRIC BY DESIGN. The aggressor hands the defender a bigger weapon and
 *     never itself. A side that never fires keeps its enemy at Level 1 forever; being hit by a
 *     100 kt gives END. That is the deterrent, and it is accepted rather than tolerated.
 *
 * ==== WHY A DROPPED LAUNCH IS AN ALARM AND NOT A SHRUG ====
 * ReportLaunch refuses a launch above the firer's Level or inside its cooldown, and says WHICH.
 * Neither is reachable by clicking: the band condition is ungranted above the Level, and the
 * cooldown is on the support power's own timer, so a refused launch means the two layers have
 * disagreed -- which is a bug, and is the kind that otherwise shows up only as a nuke that
 * escalated nobody. The trait logs the reason; the refusal enum exists so it can.
 *
 * ==== NOTHING IS PURCHASABLE (decision 02, 2026-09-13, unchanged) ====
 * "In escalation it is only on the timer etc ... nothing is purchasable." Money never touches a
 * nuke in this mode. IsFreeTimerPower is that rule; SupportPowerInstance's constructor is where it
 * bites, building the charge bank DISABLED so the cameo leaves the shop entirely.
 *
 * WHERE THE COOLDOWN IS ACTUALLY COUNTED IS BOTH HERE AND ON THE POWER, deliberately. This class
 * owns the number the LEDGER draws and the number the launch gate tests; SupportPowerInstance owns
 * the copy that greys the cameo and draws its clock. They are set from the same value on the same
 * tick and decrement in step -- see NuclearExchange.SetSideCooldown for why a power whose band is
 * not granted cannot drift out of step, and why the grant path re-synchronises it.
 *
 * ==== DETERMINISM ====
 * Integer arithmetic throughout, no RNG, no wall-clock, no floating point. Side keys are supplied
 * by the caller and iterated in REGISTRATION order (sideKeys, a List) rather than in Dictionary
 * order, so every enumeration this class exposes is identical on every client.
 */

using System.Collections.Generic;

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>
	/// How hard this match's nuclear cooldowns bite. Named for the real doctrines, and the ORDER is
	/// slowest-to-fastest rather than alphabetical.
	/// </summary>
	public enum NuclearPosture
	{
		/// <summary>Limited War: the longest waits, so an exchange is a handful of deliberate shots.</summary>
		Limited = 0,

		/// <summary>Flexible Response: the shipped cooldowns, unscaled. The default.</summary>
		Flexible = 1,

		/// <summary>Massive Retaliation: the shortest waits, so a spiral runs to its end quickly.</summary>
		Massive = 2
	}

	/// <summary>The posture multiplier, as a percentage of a band's shipped cooldown.</summary>
	// UNTUNED PLACEHOLDERS. Nobody has played this mode; 150/100/60 are round numbers either side of
	// "as shipped" and are the user's brief rather than a measurement.
	//
	// THEY ARE LIVE SINCE decision 02 MADE ESCALATION'S NUKES FREE. While every nuclear power was
	// RequiresPurchase its TotalTicks was forced to 0 (SupportPowerManager.cs) and there was no
	// interval for a multiplier to multiply; the bypass gave them one. This is applied ONCE, where
	// the side cooldown table is built -- see NuclearExchange's constructor -- so a cooldown that
	// has reached this class is already scaled and must not be scaled again.
	public static class NuclearPostureScale
	{
		public const int LimitedPercent = 150;
		public const int FlexiblePercent = 100;
		public const int MassivePercent = 60;

		public static int Percent(NuclearPosture posture)
		{
			switch (posture)
			{
				case NuclearPosture.Limited: return LimitedPercent;
				case NuclearPosture.Massive: return MassivePercent;
				default: return FlexiblePercent;
			}
		}

		/// <summary>Scale a tick count by the posture. Multiplies BEFORE dividing, so 60 % is exact.</summary>
		// The idiom is NuclearUnlockSchedule.TicksForMinutes's, for its reason: `ticks * (pct/100)`
		// would evaluate the parenthesis in integer arithmetic and yield 0 or 1, which is not a
		// rounding error but a total loss of the value.
		public static int Apply(int ticks, NuclearPosture posture)
		{
			if (ticks <= 0)
				return 0;

			return ticks * Percent(posture) / 100;
		}
	}

	/// <summary>Why <see cref="NuclearExchangeState.ReportLaunch"/> refused a launch.</summary>
	// SPLIT INTO "not our business" AND "should have been impossible", because the trait logs the
	// two at different volumes. The first four are ordinary no-ops -- a Skirmish match, a Lua
	// scenario poking the trait before release, the Tsar Bomba, a non-nuclear power. The last two
	// mean a power was Ready that the rules say could not have been, which is a defect.
	public enum NuclearLaunchRefusal
	{
		/// <summary>Not refused.</summary>
		None = 0,

		/// <summary>Skirmish or Sandbox: there is no exchange at all.</summary>
		NotEscalation = 1,

		/// <summary>The release gate has not opened.</summary>
		NotReleased = 2,

		/// <summary>Above <see cref="NuclearReleaseLadder.SandboxOnlyAboveTons"/> -- the Tsar Bomba.</summary>
		AboveSandboxCeiling = 3,

		/// <summary>A yield that is on no rung at all.</summary>
		NotNuclear = 4,

		/// <summary>ALARM: the band fired is above this side's Level.</summary>
		AboveLevel = 5,

		/// <summary>ALARM: this side is inside its cooldown.</summary>
		OnCooldown = 6
	}

	/// <summary>What one call to <see cref="NuclearExchangeState.ReportLaunch"/> did.</summary>
	public readonly struct NuclearLaunchOutcome
	{
		/// <summary>False when the launch was refused; <see cref="Refusal"/> says why.</summary>
		public readonly bool Counted;

		/// <summary>The band that was fired, as a <see cref="NuclearRung"/> value.</summary>
		public readonly int Band;

		/// <summary>A game-ender was released: the match ends, and the trait is what begins it.</summary>
		public readonly bool FinalExchange;

		/// <summary>The cooldown this launch put the firing side on. 0 for a game-ender.</summary>
		public readonly int CooldownTicks;

		/// <summary>Why it was refused, or <see cref="NuclearLaunchRefusal.None"/>.</summary>
		public readonly NuclearLaunchRefusal Refusal;

		public NuclearLaunchOutcome(bool counted, int band, bool finalExchange, int cooldownTicks,
			NuclearLaunchRefusal refusal)
		{
			Counted = counted;
			Band = band;
			FinalExchange = finalExchange;
			CooldownTicks = cooldownTicks;
			Refusal = refusal;
		}

		public static NuclearLaunchOutcome Refused(NuclearLaunchRefusal refusal, int band)
		{
			return new NuclearLaunchOutcome(false, band, false, 0, refusal);
		}

		/// <summary>True for the two refusals that mean a power was Ready when it should not have been.</summary>
		public bool IsAlarming => Refusal == NuclearLaunchRefusal.AboveLevel || Refusal == NuclearLaunchRefusal.OnCooldown;
	}

	public class NuclearExchangeState
	{
		/// <summary>One side's whole position. A side is a TEAM, or a player with no team.</summary>
		public sealed class SideState
		{
			/// <summary>The highest band this side may fire. NEVER FALLS.</summary>
			public int Level = (int)NuclearRung.Hold;

			/// <summary>Ticks until this side may fire ANY band again. 0 is ready.</summary>
			public int CooldownTicks;

			/// <summary>
			/// Bumped every time <see cref="Level"/> RISES. A consumer that has to act once per rise
			/// -- a banner announcing it -- could watch the level itself, and this is here for the
			/// consumer that wants the edge without holding a copy of the value.
			/// </summary>
			public int LevelSerial;
		}

		readonly DefconGameMode mode;

		// ALREADY POSTURE-SCALED when it arrives; see NuclearPostureScale. Indexed from
		// NuclearRung.Kiloton, which is what CooldownTicksFor indexes against.
		readonly int[] cooldownTicksPerBand;

		// REGISTRATION ORDER, not Dictionary order. Every enumeration this class exposes walks this
		// list, so the order is identical on every client and in every test. See the header.
		readonly List<int> sideKeys = new List<int>();
		readonly Dictionary<int, SideState> sides = new Dictionary<int, SideState>();

		/// <summary>Whether the release gate has opened. Until it has, every side is at Hold.</summary>
		public bool Released { get; private set; }

		public NuclearExchangeState(DefconGameMode mode, IReadOnlyList<int> cooldownTicksPerBand)
		{
			this.mode = mode;

			// COPIED, NOT ALIASED, and clamped rather than trusted. The Info refuses a non-positive
			// value in RulesetLoaded, but this class is constructible from a test and from any future
			// caller, and a negative cooldown would read as "ready" the tick after firing.
			if (cooldownTicksPerBand == null || cooldownTicksPerBand.Count == 0)
				this.cooldownTicksPerBand = new[] { 0 };
			else
			{
				this.cooldownTicksPerBand = new int[cooldownTicksPerBand.Count];
				for (var i = 0; i < cooldownTicksPerBand.Count; i++)
					this.cooldownTicksPerBand[i] = cooldownTicksPerBand[i] < 0 ? 0 : cooldownTicksPerBand[i];
			}
		}

		/// <summary>Sides, in registration order.</summary>
		public IReadOnlyList<int> Sides => sideKeys;

		/// <summary>
		/// <para>Is this power FREE AND TIMER-CHARGED rather than bought? True for a nuclear power in
		/// Escalation and for nothing else, which is the whole of decision 02's "in escalation mode we
		/// only control the timing, nothing is purchasable".</para>
		///
		/// <para>THE MODE TEST IS THE LOAD-BEARING HALF. Skirmish and Sandbox keep the purchase economy
		/// byte-identical -- Skirmish is the shipped default game mode, the user tests from main, and
		/// every nuclear scenario under tools/autotest/scenarios buys its shot through the Powers
		/// queue. A predicate that answered true outside Escalation would empty the buy tab in all of
		/// them at once, silently, because an unpurchasable power is simply absent from it
		/// (SupportPowerProductionQueue.AllItems filters on SupportPowerInstance.Purchasable).</para>
		///
		/// <para>THE TSAR BOMBA IS NOT FREE EITHER, and that falls out of the mode test rather than
		/// needing its own: it is unreachable in Escalation at all (decision 04), so the only modes it
		/// exists in are the two this returns false for.</para>
		/// </summary>
		public static bool IsFreeTimerPower(DefconGameMode mode, int tons)
		{
			return mode == DefconGameMode.Escalation && tons > 0 && tons <= NuclearReleaseLadder.SandboxOnlyAboveTons;
		}

		/// <summary>
		/// <para>The side key for one player: its TEAM when it has one, and a unique NEGATIVE when it
		/// does not, so a teamless player is its own side and can never collide with a team number.</para>
		///
		/// <para>TWO SOURCES, IN THIS ORDER, AND THE ORDER IS NOT THE INTERESTING PART -- the caller
		/// resolving <paramref name="owningClientTeam"/> is. A lobby client's team wins because a
		/// human or bot in a slot may have been moved between teams in the lobby, which the map
		/// cannot know; <paramref name="referenceTeam"/> is the map's own `Team:`, which is the only
		/// statement of sides a map-authored player can make.</para>
		///
		/// <para>NON-POSITIVE MEANS ABSENT for both. Lobby team 0 is "no team" rather than team zero,
		/// and PlayerReference.Team defaults to 0 -- so neither can be distinguished from unset, and
		/// neither needs to be.</para>
		///
		/// <para>PURE, AND THAT IS WHY IT IS HERE. The defect this split exists to catch was in the
		/// LOOKUP that feeds <paramref name="owningClientTeam"/> and not in this arithmetic (see
		/// NuclearExchange.SideKeyFor), but a caller passing 0 for "no client owns this player" is
		/// exactly the case that was unreachable before and is now the one worth pinning.</para>
		/// </summary>
		public static int SideKeyFor(int owningClientTeam, int referenceTeam, int playerIndex)
		{
			var team = owningClientTeam > 0 ? owningClientTeam : referenceTeam;

			return team > 0 ? team : -(playerIndex + 1);
		}

		/// <summary>
		/// <para>The SIDE COOLDOWN a shot at this band costs, given the four per-band intervals in
		/// ascending band order. The list is <see cref="NuclearRung.Kiloton"/> through
		/// <see cref="NuclearRung.HundredKiloton"/>; anything outside that range takes the last
		/// entry.</para>
		///
		/// <para>RENAMED FROM `RegenTicksFor` WITH v2 AND THE MEANING CHANGED WITH IT. It used to be
		/// "how long until THIS BAND comes back for this side", one independent clock per band. It is
		/// now "how long until ANY band comes back for this side", one clock, chosen by the band that
		/// was fired. Same table, same indexing, a different quantity -- so the name had to move too.</para>
		///
		/// <para>A GAME-ENDER NEVER REACHES THIS. <see cref="ReportLaunch"/> sets no cooldown at the top
		/// rung because the match is ending; the top entry is still returned for a caller that asks,
		/// so the table has no hole in it.</para>
		/// </summary>
		public static int CooldownTicksFor(int band, IReadOnlyList<int> perBandTicks)
		{
			if (perBandTicks == null || perBandTicks.Count == 0)
				return 0;

			var index = band - (int)NuclearRung.Kiloton;
			if (index < 0)
				index = 0;

			if (index >= perBandTicks.Count)
				index = perBandTicks.Count - 1;

			var ticks = perBandTicks[index];
			return ticks < 0 ? 0 : ticks;
		}

		/// <summary>
		/// <para>Is a player one of the match's SIDES? Two booleans, and the one that is deliberately
		/// NOT here is the thing worth reading this for.</para>
		///
		/// <para>`Playable` IS NOT PART OF THE TEST, and a first version of this rule had it and was
		/// wrong. <c>PlayerReference.Playable</c> defaults to FALSE (`PlayerReference.cs:24`) and says
		/// only "is this a slot the lobby offers", so requiring it silently drops every map-authored
		/// combatant — a scripted enemy in a mission, or either side of an autotest scenario that did
		/// not happen to write the line. Such a player could then be nuked and arm nobody, and could
		/// never retaliate, because the exchange would not know they existed.</para>
		///
		/// <para>IT COST A SCENARIO RUN. test-nuclear-exchange authored `Playable: True` on USA and not
		/// on Russia; the run logged "NUCLEAR RELEASE: all 1 sides" and every Russian nuclear power
		/// stayed dark for the whole match, while <see cref="DefconWall"/> — which partitions the same
		/// players with the predicate below — logged "derived from 2 home(s) in 2 group(s)" on the very
		/// same tick. Two traits disagreeing about who is in the match is the bug; this is the shipped
		/// side of that disagreement (`DefconWall.cs:296`).</para>
		/// </summary>
		public static bool CountsAsASide(bool nonCombatant, bool spectating)
		{
			return !nonCombatant && !spectating;
		}

		/// <summary>Add a side. Idempotent, so a caller may register from a loop without checking.</summary>
		public void RegisterSide(int side)
		{
			if (sides.ContainsKey(side))
				return;

			sides.Add(side, new SideState());
			sideKeys.Add(side);
		}

		/// <summary>This side's state, or null if it was never registered.</summary>
		public SideState For(int side)
		{
			return sides.TryGetValue(side, out var state) ? state : null;
		}

		/// <summary>
		/// The highest band this side may fire. THE ONE NUMBER the condition layer reads -- see
		/// <see cref="GrantConditionOnNuclearRelease"/>.
		/// </summary>
		public int LevelFor(int side)
		{
			return For(side)?.Level ?? (int)NuclearRung.Hold;
		}

		/// <summary>Ticks until this side may fire again, at any band. 0 is ready.</summary>
		public int CooldownFor(int side)
		{
			return For(side)?.CooldownTicks ?? 0;
		}

		/// <summary>Bumped on every RISE of this side's level; see <see cref="SideState.LevelSerial"/>.</summary>
		public int LevelSerialFor(int side)
		{
			return For(side)?.LevelSerial ?? 0;
		}

		/// <summary>
		/// <para>Has a readiness request finished, or must it be retried? True when every power in the
		/// newly granted range has been armed, and when there was nothing in the range to arm.</para>
		///
		/// <para>IT IS NOT `armed &gt; 0`, AND THAT DISTINCTION COST THREE SCENARIOS ON 2026-09-15.
		/// Arming a granted band needs a retry budget at all because the band condition is granted by
		/// a PLAYER-actor trait while the grant is issued from the WORLD actor, so it lands one or two
		/// ticks late (see NuclearExchange's header). A predicate reading "at least one power was
		/// armed" reports the request finished on the FIRST tick whenever the range contains any power
		/// that was already permitted -- and the band that was actually just granted is then never
		/// looked at again. It keeps its own constructed interval and counts that down instead, so the
		/// player watches a cameo reload a weapon they never fired.</para>
		///
		/// <para>THE RANGE CAN CONTAIN MORE THAN ONE POWER EVEN WHEN IT IS SCOPED TO ONE BAND, which is
		/// why fixing the range alone would not have been enough: the top rung holds all three
		/// game-enders, and `powers-sandbox` puts the other faction's whole ladder beside a player's
		/// own. So "at least one" can be satisfied by a sibling at the same band.</para>
		///
		/// <para>THE COST OF BEING STRICT IS BOUNDED AND SMALL. A power in range that can NEVER be
		/// armed -- the wrong faction's game-ender, a weapon whose lobby checkbox is off -- keeps this
		/// false until the caller's retry budget runs out. That budget is 30 ticks
		/// (<see cref="NuclearExchangeInfo.GrantRetryTicks"/>), the work is a walk over at most a
		/// handful of powers, and every power that CAN be armed was armed on the first pass anyway.
		/// Failing slow beats failing silent.</para>
		/// </summary>
		public static bool GrantSatisfied(int powersInRange, int powersArmed)
		{
			return powersArmed >= powersInRange;
		}

		/// <summary>May this side fire band <paramref name="band"/> right now? Rule 2, stated once.</summary>
		// ONE PREDICATE, THREE CALLERS -- ReportLaunch's gate, the bot's policy input and the ledger.
		// Three hand-rolled copies of `band <= Level && Cooldown == 0` is how one of them ends up
		// disagreeing with the others about the boundary, and the boundary here is <=, not <.
		public bool MayFire(int side, int band)
		{
			var s = For(side);
			if (s == null || band <= (int)NuclearRung.Hold)
				return false;

			return band <= s.Level && s.CooldownTicks <= 0;
		}

		/// <summary>
		/// The release gate has opened: every side holds the lowest band, with no cooldown, from now
		/// on. Returns true the first time only, so a caller may poll it.
		/// </summary>
		public bool Release()
		{
			if (Released || mode != DefconGameMode.Escalation)
				return false;

			Released = true;

			foreach (var key in sideKeys)
			{
				var s = sides[key];
				if (s.Level < (int)NuclearRung.Kiloton)
				{
					s.Level = (int)NuclearRung.Kiloton;
					s.LevelSerial++;
				}

				// RELEASE CLEARS ANY COOLDOWN, which cannot happen in play -- nothing can have been
				// fired before the gate opened, because ReportLaunch refuses every launch until it
				// does. Written anyway so that "release is the same for everybody" is true of the
				// STATE and not only of the paths that reach it.
				s.CooldownTicks = 0;
			}

			return true;
		}

		/// <summary>
		/// One tick of every running cooldown. Returns the sides whose cooldown reached zero ON THIS
		/// TICK, in registration order, so a caller can announce it.
		/// </summary>
		// Allocates only when something actually expires, which is a handful of ticks in a whole
		// match: the null stays null on every other tick and the caller treats it as empty.
		public List<int> Tick()
		{
			List<int> expired = null;

			foreach (var key in sideKeys)
			{
				var s = sides[key];
				if (s.CooldownTicks <= 0)
					continue;

				// The decrement idiom DefconEscalationState.Tick and NuclearReleaseLadder.Tick both
				// use, so a cooldown of N ticks blocks for exactly N ticks rather than N +/- 1.
				if (--s.CooldownTicks > 0)
					continue;

				(expired ??= new List<int>()).Add(key);
			}

			return expired;
		}

		/// <summary>
		/// Side <paramref name="firerSide"/> has released a warhead of <paramref name="tons"/> tons of
		/// TNT. Rule 3: the firer takes the cooldown, every enemy side takes the level.
		/// </summary>
		public NuclearLaunchOutcome ReportLaunch(int firerSide, int tons)
		{
			// Skirmish and Sandbox have no exchange at all -- their release is NuclearUnlockClock's
			// schedule, which no detonation moves. Same strict-no-op rule the ladder carried.
			if (mode != DefconGameMode.Escalation)
				return NuclearLaunchOutcome.Refused(NuclearLaunchRefusal.NotEscalation, (int)NuclearRung.Hold);

			// A LAUNCH BEFORE RELEASE IS DROPPED WHOLE. Nothing a player can click reaches here while
			// the gate is shut, because every nuclear power is gated on a band condition that is not
			// granted -- but a Lua scenario or a bot calling the trait directly can, and escalating a
			// side off the back of one would let a match arrive at release already escalated.
			if (!Released)
				return NuclearLaunchOutcome.Refused(NuclearLaunchRefusal.NotReleased, (int)NuclearRung.Hold);

			// THE TSAR BOMBA GATE, checked before the band. Nothing a player can click reaches here
			// with a 50 Mt warhead -- MissileStrikePower@TsarBomba is gated on the unrestricted
			// condition, which is never granted inside Escalation -- but a Lua scenario calling the
			// trait directly can, and escalating the other side to END off the back of a weapon that
			// is not in play would end the match by a route decision 04 closed.
			if (tons > NuclearReleaseLadder.SandboxOnlyAboveTons)
				return NuclearLaunchOutcome.Refused(NuclearLaunchRefusal.AboveSandboxCeiling, (int)NuclearRung.Hold);

			var band = NuclearReleaseLadder.RungForYield(tons);
			if (band <= (int)NuclearRung.Hold)
				return NuclearLaunchOutcome.Refused(NuclearLaunchRefusal.NotNuclear, (int)NuclearRung.Hold);

			// ==== THE TWO ALARMING REFUSALS. See the header: neither is reachable by clicking. ====
			var firer = For(firerSide);
			if (firer == null || band > firer.Level)
				return NuclearLaunchOutcome.Refused(NuclearLaunchRefusal.AboveLevel, band);

			if (firer.CooldownTicks > 0)
				return NuclearLaunchOutcome.Refused(NuclearLaunchRefusal.OnCooldown, band);

			var finalExchange = band >= (int)NuclearRung.GameEnder;

			// THE FIRER PAYS, AND ONLY THE FIRER'S SIDE. A game-ender takes none: the match ends on
			// this launch, so a lockout would be a number nobody lives to read -- and zero here is not
			// "ready again next tick" for the same reason.
			var cooldown = finalExchange ? 0 : CooldownTicksFor(band, cooldownTicksPerBand);
			firer.CooldownTicks = cooldown;

			// ONE BAND ABOVE WHAT LANDED ON THEM, CAPPED AT THE TOP RUNG, AND PERMANENT. Wherever it
			// lands: there is no damage attribution and no demonstration shot, which is the rule
			// decision 01 chose over decision 14's 10 % and which v2 keeps unchanged.
			var granted = band + 1;
			if (granted > NuclearReleaseLadder.Highest)
				granted = NuclearReleaseLadder.Highest;

			foreach (var key in sideKeys)
			{
				if (key == firerSide)
					continue;

				var s = sides[key];

				// max(), not assignment: LEVELS NEVER FALL is the whole of the ratchet, and a side
				// already at 50 kt must not be cut back to 20 kt by a subsequent 1 kt shot. This is
				// also why a side cannot climb by firing small on purpose -- it climbs nobody.
				if (granted > s.Level)
				{
					s.Level = granted;
					s.LevelSerial++;
				}
			}

			return new NuclearLaunchOutcome(true, band, finalExchange, cooldown, NuclearLaunchRefusal.None);
		}
	}
}
