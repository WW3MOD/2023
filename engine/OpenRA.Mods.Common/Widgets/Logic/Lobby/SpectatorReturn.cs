#region Copyright & License Information
/*
 * WW3MOD spectator return — which slot, if any, a spectator can move back into.
 *
 * "Move to Spectator" hides the whole SPECTATE_AREA the moment it succeeds (its IsVisible requires
 * Slot != null), so the control that got you out is the first thing to vanish. A route back does
 * exist — the per-row Join/Play button issues "slot <key>", and the server's Slot handler is NOT
 * admin-gated — but it only exists while some slot is open, and nothing on screen connects it to
 * getting out of spectator mode.
 *
 * Extracted as a pure static so the predicate can be pinned without a live widget: reaching the
 * spectating state at all needs a click, and the trapped case needs a lobby whose every slot is
 * taken. Same move as ForceStartConfirm.
 *
 * The load-bearing property is that this must agree with the SERVER about what "available" means.
 * The client cannot grant itself a slot; it can only ask, and LobbyCommands.Slot rejects the ask
 * for a closed or occupied slot. A predicate looser than the server's produces a button that looks
 * live and silently does nothing — strictly worse than the missing button it replaces. Pinned in
 * SpectatorReturnTest.
 */
#endregion

using OpenRA.Network;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public static class SpectatorReturn
	{
		/// <summary>
		/// The slot a spectator could move into, or null when none is available.
		/// Mirrors the server's accept rule in LobbyCommands.Slot: not closed, and nobody
		/// (human or bot) already in it. Enumerates in <see cref="Session.Slots"/> order, which
		/// is the order LobbyLogic draws the roster rows — so the slot taken is the topmost one
		/// the player can see is free.
		/// </summary>
		public static string FirstAvailableSlot(Session session)
		{
			if (session == null)
				return null;

			foreach (var kv in session.Slots)
				if (!kv.Value.Closed && session.ClientInSlot(kv.Key) == null)
					return kv.Key;

			return null;
		}

		/// <summary>
		/// Whether <paramref name="client"/> is in the state the return control is for: connected
		/// and not holding a slot. Independent of whether a slot is actually free — a spectator
		/// with nowhere to go still needs to be told that, which is why this is separate from
		/// <see cref="FirstAvailableSlot"/>.
		/// </summary>
		public static bool IsSpectating(Session.Client client)
		{
			return client != null && client.Slot == null;
		}

		/// <summary>
		/// Whether the return would be accepted right now. A ready client is refused by
		/// ValidateCommand before the Slot handler ever runs, so readiness disables the control
		/// exactly as it disables every other lobby action.
		/// </summary>
		public static bool CanReturn(Session session, Session.Client client)
		{
			return IsSpectating(client)
				&& !client.IsReady
				&& FirstAvailableSlot(session) != null;
		}
	}
}
