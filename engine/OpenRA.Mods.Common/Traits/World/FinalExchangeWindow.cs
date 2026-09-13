#region Copyright & License Information
/*
 * WW3MOD addition. Not upstream OpenRA.
 */
#endregion

using System.Collections.Generic;

namespace OpenRA.Mods.Common.Traits
{
	public enum FinalExchangePhase
	{
		/// <summary>Nothing has happened. The match is ordinary.</summary>
		Idle = 0,

		/// <summary>The fifteen seconds. Every surviving side holds its game-enders and may place them.</summary>
		Open = 1,

		/// <summary>The window has expired. Dead Hand has the remaining warheads and the salvo is running.</summary>
		Closed = 2,
	}

	/// <summary>
	/// <para>THE FIFTEEN SECONDS, as a state machine with no <see cref="World"/> in it.</para>
	///
	/// <para>Split out of <see cref="DoomsdayStrike"/> for the same reason
	/// <see cref="SupportPowerChargeBank"/> was split out of <see cref="SupportPowerInstance"/>:
	/// every question the feature turns on — has it started, has it expired, who placed their own
	/// warheads and who is getting them placed for them — is answerable from two ints and two lists,
	/// and answering them here means they are pinned by FinalExchangeWindowTest rather than only by
	/// launching the game. The trait owns everything that needs a world (granting the powers, the
	/// salvo, the announcement); this owns only the bookkeeping.</para>
	///
	/// <para>DETERMINISM. No RNG, no wall clock, no dictionary or set enumeration — the tick is passed in
	/// and the two collections are Lists appended in a caller-fixed order, so every client walks them
	/// identically. <see cref="Tick"/> reports the closing edge EXACTLY ONCE, which is what lets the
	/// caller hang a one-shot transition off it without a second flag of its own.</para>
	///
	/// <para>SIDES ARE IDENTIFIED BY STRING, the same convention <see cref="NuclearReleaseLadder"/> uses for
	/// a firer. The caller passes Player.InternalName; this file never interprets it.</para>
	/// </summary>
	public sealed class FinalExchangeWindow
	{
		// Seat order, as handed in. Never re-sorted: the caller's order IS world.Players order, which
		// is fixed at world creation and identical on every client.
		readonly List<string> sides = new();

		// Sides that put at least one game-ender of their own in the air during the window, in the
		// order they did it. Membership is tested with Contains rather than a HashSet so that the
		// enumeration order below is the order things actually happened in.
		readonly List<string> placed = new();

		public FinalExchangePhase Phase { get; private set; } = FinalExchangePhase.Idle;

		/// <summary>The tick the window opened on. Meaningless while <see cref="Phase"/> is Idle.</summary>
		public int OpenedTick { get; private set; }

		/// <summary>The tick the window closes on — the first tick at which Dead Hand places the rest.</summary>
		public int ClosesTick { get; private set; }

		/// <summary>
		/// The side that triggered the exchange by firing, or null for the time-limit path. Recorded
		/// rather than used: it is what the announcement names, and it is the side that has already
		/// placed by definition.
		/// </summary>
		public string Trigger { get; private set; }

		/// <summary>How many sides put their own game-enders up. [Sync]-able projection for the trait.</summary>
		public int PlacementCount => placed.Count;

		/// <summary>
		/// <para>Open the window. Returns false — and changes NOTHING — if it has already been opened,
		/// which is the whole of the idempotency requirement: a second trigger during the exchange
		/// must not restart the clock, re-arm anybody, or clear the placement record.</para>
		///
		/// <para>A duration of zero or less is rejected too, so a host or scenario that sets the window
		/// to 0 gets the pre-window behaviour (the salvo fires immediately) rather than a window that
		/// opens and closes on the same tick.</para>
		/// </summary>
		public bool Begin(int tick, int durationTicks, IEnumerable<string> survivingSides, string trigger)
		{
			if (Phase != FinalExchangePhase.Idle || durationTicks <= 0)
				return false;

			Phase = FinalExchangePhase.Open;
			OpenedTick = tick;
			ClosesTick = tick + durationTicks;
			Trigger = trigger;

			if (survivingSides != null)
				foreach (var s in survivingSides)
					if (!string.IsNullOrEmpty(s) && !sides.Contains(s))
						sides.Add(s);

			// THE TRIGGER HAS ALREADY PLACED, BY DEFINITION. Path (b) is "a side fires a game-ender it
			// was granted through the exchange" — that warhead is the first of the exchange, so the
			// firer is not a side Dead Hand places for. Recorded here rather than left to the launch
			// hook because the hook can only see a launch once the window is open, and on this path
			// the launch is what opened it.
			if (!string.IsNullOrEmpty(trigger))
				RecordPlacement(trigger);

			return true;
		}

		/// <summary>Ticks left to place, clamped at zero. What the countdown widget reads.</summary>
		public int TicksRemaining(int tick)
		{
			if (Phase != FinalExchangePhase.Open)
				return 0;

			var remaining = ClosesTick - tick;
			return remaining > 0 ? remaining : 0;
		}

		/// <summary>
		/// Advance. Returns true on the CLOSING TICK AND ONLY THE CLOSING TICK — the caller hangs the
		/// Dead Hand placement off that edge, and calling this again afterwards returns false forever.
		/// </summary>
		public bool Tick(int tick)
		{
			if (Phase != FinalExchangePhase.Open || tick < ClosesTick)
				return false;

			Phase = FinalExchangePhase.Closed;
			return true;
		}

		/// <summary>
		/// A side put one of its own game-enders in the air. Returns true the first time for that
		/// side. Ignored outside the window: a warhead fired after Dead Hand has placed is a warhead
		/// from the salvo, not a placement.
		/// </summary>
		public bool RecordPlacement(string side)
		{
			if (Phase != FinalExchangePhase.Open || string.IsNullOrEmpty(side) || placed.Contains(side))
				return false;

			placed.Add(side);

			// A side that fires without ever having been enumerated as surviving — a scenario firing
			// through Lua, a side that joined the record late — still counts as having placed, and is
			// appended so the split below stays a partition of everyone this window knows about.
			if (!sides.Contains(side))
				sides.Add(side);

			return true;
		}

		public bool HasPlaced(string side)
		{
			return side != null && placed.Contains(side);
		}

		/// <summary>Sides that placed their own, in seat order.</summary>
		public IEnumerable<string> SidesThatPlaced()
		{
			foreach (var s in sides)
				if (placed.Contains(s))
					yield return s;
		}

		/// <summary>
		/// Sides Dead Hand places for, in seat order — the other half of the same partition. A side
		/// whose game-ender was charging, unaffordable or simply never clicked is in here, which is
		/// the rule "either way the outcome is the same".
		/// </summary>
		public IEnumerable<string> SidesPlacedForByDeadHand()
		{
			foreach (var s in sides)
				if (!placed.Contains(s))
					yield return s;
		}
	}
}
