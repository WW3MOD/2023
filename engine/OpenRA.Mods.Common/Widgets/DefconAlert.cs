#region Copyright & License Information
/*
 * WW3MOD addition. Not upstream OpenRA.
 */
#endregion

/*
 * PLAYING A NOTIFICATION FROM A WIDGET, SAFELY -- added 2026-09-13 with the escalation alerts.
 *
 * The DEFCON banner had been on screen for days playing NOTHING. Three of the mode's four turning
 * points -- 3 -> 2, 2 -> 1 and the nuclear release -- moved a strip in a 341-pixel panel in the
 * bottom-left corner and made no sound at all, so a player looking at their own units missed the
 * moment the rules changed underneath them.
 *
 * ==== WHY THE SOUND IS PLAYED FROM THE WIDGET AND NOT FROM THE TRAIT ====
 * The widgets ALREADY detect these edges, exactly once each, per client, and each already carries
 * the guard that stops the opening NoLevel -> level edge announcing a transition that never
 * happened. Raising the same notification from DefconEscalation.Tick would mean a SECOND edge
 * detector over the same state, in synced code, that has to agree with the first one forever.
 * There is nothing to gain from it: Sound.PlayPredefined already filters on
 * `player == player.World.LocalPlayer` (Sound.cs:445), so a notification raised in synced code is
 * a per-client effect anyway -- the sim side buys no correctness, only a second thing to keep in
 * step.
 *
 * ==== WHY THE POOL IS CHECKED BEFORE PLAYING, WHICH LOOKS LIKE PARANOIA AND IS NOT ====
 * Sound.PlayPredefined THROWS on a name it cannot find -- `InvalidOperationException("Can't find
 * {definition} in notification pool")`, Sound.cs:429 -- and NOTHING LINTS A WIDGET'S NOTIFICATION
 * NAME. CheckNotifications walks TraitInfo fields carrying [NotificationReference]
 * (Lint/CheckNotifications.cs:39) and no widget in this engine has ever used that attribute, so a
 * typo in a chrome yaml override is invisible to `make test` and reaches the player as a crash at
 * the exact moment the banner fires. Checking the pool turns that into a log line and a silent
 * banner, which is the right failure for a cosmetic layer.
 *
 * ==== WHO HEARS IT ====
 * world.LocalPlayer, which is NULL for an observer -- and null is what makes PlayPredefined's own
 * filter pass, so an observer hears the alerts for the match they are watching. That is deliberate
 * rather than incidental: the observer HUD carries the same readout and the same banners, and a
 * silent copy of an alerting UI would be the observer wondering why their screen just changed.
 */

using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets
{
	public static class DefconAlert
	{
		/// <summary>The notification pool holding the mod's UI bleeps and buzzers.</summary>
		public const string SoundsPool = "Sounds";

		/// <summary>The notification pool holding recorded speech.</summary>
		public const string SpeechPool = "Speech";

		/// <summary>
		/// Play a notification on THIS client, or log and do nothing if the mod does not define it.
		/// </summary>
		public static void Play(World world, string pool, string notification)
		{
			if (world == null || string.IsNullOrEmpty(pool) || string.IsNullOrEmpty(notification))
				return;

			var rules = world.Map.Rules;
			if (rules?.Notifications == null)
				return;

			if (!rules.Notifications.TryGetValue(pool.ToLowerInvariant(), out var sound)
				|| !sound.NotificationsPools.Value.ContainsKey(notification))
			{
				Log.Write("debug", $"DEFCON ALERT: no '{notification}' in the '{pool}' notification pool; nothing played.");
				return;
			}

			var listener = world.LocalPlayer;
			Game.Sound.PlayNotification(rules, listener, pool, notification, listener?.Faction.InternalName);
		}

		/// <summary>The system line that goes with an alert. Fluent key, with optional arguments.</summary>
		// TextNotificationsManager, not a chat message: this is the Transients pool, which is the
		// same one DefconWall's refused-crossing line uses and is what the mod already means by "the
		// game telling you something". Null-keyed lines are dropped there rather than here, so a
		// chrome file that leaves one unset simply gets the sound.
		public static void Line(World world, string key, params object[] args)
		{
			if (world == null || string.IsNullOrEmpty(key))
				return;

			TextNotificationsManager.AddTransientLine(world.LocalPlayer, key, args);
		}
	}
}
