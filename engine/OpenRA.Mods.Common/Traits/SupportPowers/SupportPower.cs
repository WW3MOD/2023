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
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public abstract class SupportPowerInfo : PausableConditionalTraitInfo
	{
		[Desc("Measured in ticks.")]
		public readonly int ChargeInterval = 0;

		[Desc("If set, overrides ChargeInterval with the value from this lobby option ID. " +
			"Expects values like '2min', '3min', etc. Parsed at 25 ticks/second.")]
		public readonly string LobbyChargeIntervalId = null;

		public readonly string IconImage = "icon";

		[SequenceReference(nameof(IconImage))]
		[Desc("Icon sprite displayed in the support power palette.")]
		public readonly string Icon = null;

		[PaletteReference]
		[Desc("Palette used for the icon.")]
		public readonly string IconPalette = "chrome";

		[FluentReference(optional: true)]
		public readonly string Name = null;

		[FluentReference(optional: true)]
		public readonly string Description = null;

		[Desc("Short all-caps label drawn along the bottom of this power's cameo at runtime, so the",
			"wording is data rather than baked pixels. Unset (the default) draws nothing at all.",
			"",
			"Its reason to exist is the shared sprite: several ww3mod nuclear powers draw the SAME",
			"icon (three B61-12 yields are all `paranuke`), so a baked caption physically cannot tell",
			"them apart and the bin shows identical cameos. A caption can, because it is per-power.",
			"",
			"This is NOT `Name` and should not repeat it - `Name` is a full designation with a yield",
			"in brackets and is many times too wide for a 62px slot. Write the discriminator only.",
			"A caption too wide to fit is shortened from the right rather than allowed to bleed into",
			"the neighbouring cameo.",
			"",
			"Accepts a Fluent key or, when no bundle defines it, the literal string.")]
		[FluentReference(optional: true)]
		public readonly string CameoCaption = null;

		[Desc("Sequence of a small badge sprite stamped over the bottom-right of this power's cameo",
			"at runtime, on top of whatever art the cameo already uses. Unset (the default) draws",
			"nothing. Resolved against the support power palette's BadgeAnimation image.",
			"",
			"This is what makes a marking mean something across an arsenal: `nuclear` on every",
			"warhead and on nothing else says at a glance which powers end a base and which do not,",
			"and it says it on cameos that do not exist yet as well as the ones that do. Its width is",
			"reserved out of the caption's before the caption is fitted, so the two never overlap.")]
		// Not a [SequenceReference]: the bare attribute resolves against the actor's own image, and
		// this sequence lives on the widget's BadgeAnimation instead - a chrome image no actor owns.
		public readonly string CameoBadge = null;

		[Desc("Allow multiple instances of the same support power.")]
		public readonly bool AllowMultiple = false;

		[Desc("Allow this to be used only once.")]
		public readonly bool OneShot = false;

		[CursorReference]
		[Desc("Cursor to display for using this support power.")]
		public readonly string Cursor = "ability";

		[CursorReference]
		[Desc("Cursor when unable to activate on this position. ")]
		public readonly string BlockedCursor = "generic-blocked";

		[Desc("Aim at the CENTRE of an actor occupying the targeted cell instead of at the cell.",
			"A multi-cell building's centre is a cell corner, so the resolved aim point is a WPos",
			"that need not lie on any cell centre — this is the point of the setting, not a rounding",
			"error. The point is frozen when the power activates: the strike does not follow a target",
			"that walks away. Set false for a power whose effect belongs on the cell the player",
			"clicked rather than on whatever is standing there (a paradrop, an actor spawn).")]
		public readonly bool SnapToActorCenter = true;

		[Desc("If set to true, the support power will be fully charged when it becomes available. " +
			"Normal rules apply for subsequent charges.")]
		public readonly bool StartFullyCharged = false;

		[Desc("BUY this power from a production queue instead of charging it on a timer.",
			"",
			"With this set, ChargeInterval is IGNORED (SupportPowerInstance forces TotalTicks to 0)",
			"and readiness comes from a bank of purchased shots instead: the cameo is ABSENT from the",
			"support bin until a purchase completes, appears fully charged the moment it does, and",
			"disappears again when the last banked shot is fired. One purchase is one shot; buying",
			"again while a shot is banked stacks, and the cameo then reads 'x2' rather than 'READY'.",
			"",
			"The buying end is SupportPowerProductionQueue plus a bodiless proxy actor carrying",
			"ProvidesSupportPowerCharge that names this power's OrderName. Setting this true WITHOUT",
			"a proxy actor makes the power permanently unreachable -- there is no timer left to fall",
			"back to -- and nothing lints for it.",
			"",
			"DEFAULT FALSE, and every timer-charged power in every mod keeps its exact current",
			"behaviour: SupportPowerChargeBank degenerates to a no-op when this is not set.",
			"",
			"NOTE for a power with a CUSTOM SupportPowerInstance subclass: if that subclass overrides",
			"IconOverlayTextOverride it replaces the stacked-charge 'x2' readout, because the override",
			"wins. Nothing else in the purchase model is affected.")]
		public readonly bool RequiresPurchase = false;

		public readonly string[] Prerequisites = Array.Empty<string>();

		public readonly string DetectedSound = null;

		[NotificationReference("Speech")]
		public readonly string DetectedSpeechNotification = null;

		[FluentReference(optional: true)]
		public readonly string DetectedTextNotification = null;

		public readonly string BeginChargeSound = null;

		[NotificationReference("Speech")]
		public readonly string BeginChargeSpeechNotification = null;

		[FluentReference(optional: true)]
		public readonly string BeginChargeTextNotification = null;

		public readonly string EndChargeSound = null;

		[NotificationReference("Speech")]
		public readonly string EndChargeSpeechNotification = null;

		[FluentReference(optional: true)]
		public readonly string EndChargeTextNotification = null;

		public readonly string SelectTargetSound = null;

		[NotificationReference("Speech")]
		public readonly string SelectTargetSpeechNotification = null;

		[FluentReference(optional: true)]
		public readonly string SelectTargetTextNotification = null;

		public readonly string InsufficientPowerSound = null;

		[NotificationReference("Speech")]
		public readonly string InsufficientPowerSpeechNotification = null;

		[FluentReference(optional: true)]
		public readonly string InsufficientPowerTextNotification = null;

		public readonly string LaunchSound = null;

		[NotificationReference("Speech")]
		public readonly string LaunchSpeechNotification = null;

		[FluentReference(optional: true)]
		public readonly string LaunchTextNotification = null;

		public readonly string IncomingSound = null;

		[NotificationReference("Speech")]
		public readonly string IncomingSpeechNotification = null;

		[FluentReference(optional: true)]
		public readonly string IncomingTextNotification = null;

		[Desc("Defines to which players the timer is shown.")]
		public readonly PlayerRelationship DisplayTimerRelationships = PlayerRelationship.None;

		[Desc("Beacons are only supported on the Airstrike, Paratroopers, and Nuke powers")]
		public readonly bool DisplayBeacon = false;

		public readonly bool BeaconPaletteIsPlayerPalette = true;

		[PaletteReference(nameof(BeaconPaletteIsPlayerPalette))]
		public readonly string BeaconPalette = "player";

		public readonly string BeaconImage = "beacon";

		[SequenceReference(nameof(BeaconImage))]
		public readonly string BeaconPoster = null;

		[PaletteReference]
		public readonly string BeaconPosterPalette = "chrome";

		[SequenceReference(nameof(BeaconImage))]
		public readonly string ClockSequence = null;

		[SequenceReference(nameof(BeaconImage))]
		public readonly string BeaconSequence = null;

		[SequenceReference(nameof(BeaconImage))]
		public readonly string ArrowSequence = null;

		[SequenceReference(nameof(BeaconImage))]
		public readonly string CircleSequence = null;

		[Desc("Delay after launch, measured in ticks.")]
		public readonly int BeaconDelay = 0;

		public readonly bool DisplayMiniMapPing = false;

		[Desc("Measured in ticks.")]
		public readonly int MiniMapPingDuration = 125;

		public readonly string OrderName;

		[Desc("Sort order for the support power palette. Smaller numbers are presented earlier.")]
		public readonly int SupportPowerPaletteOrder = 9999;

		protected SupportPowerInfo() { OrderName = GetType().Name + "Order"; }
	}

	public class SupportPower : PausableConditionalTrait<SupportPowerInfo>
	{
		public readonly Actor Self;
		readonly SupportPowerInfo info;
		protected MiniMapPing ping;

		public SupportPower(Actor self, SupportPowerInfo info)
			: base(info)
		{
			Self = self;
			this.info = info;
		}

		protected override void Created(Actor self)
		{
			base.Created(self);

			var player = self.World.LocalPlayer;
			if (player != null && player != self.Owner)
			{
				Game.Sound.Play(SoundType.UI, Info.DetectedSound);
				Game.Sound.PlayNotification(self.World.Map.Rules, player, "Speech", info.DetectedSpeechNotification, player.Faction.InternalName);
				TextNotificationsManager.AddTransientLine(player, info.DetectedTextNotification);
			}
		}

		public virtual SupportPowerInstance CreateInstance(string key, SupportPowerManager manager)
		{
			return new SupportPowerInstance(key, info, manager);
		}

		public virtual void Charging(Actor self, string key)
		{
			Game.Sound.PlayToPlayer(SoundType.UI, self.Owner, Info.BeginChargeSound);
			Game.Sound.PlayNotification(self.World.Map.Rules, self.Owner, "Speech",
				Info.BeginChargeSpeechNotification, self.Owner.Faction.InternalName);

			TextNotificationsManager.AddTransientLine(self.Owner, Info.BeginChargeTextNotification);
		}

		public virtual void Charged(Actor self, string key)
		{
			Game.Sound.PlayToPlayer(SoundType.UI, self.Owner, Info.EndChargeSound);
			Game.Sound.PlayNotification(self.World.Map.Rules, self.Owner, "Speech",
				Info.EndChargeSpeechNotification, self.Owner.Faction.InternalName);

			TextNotificationsManager.AddTransientLine(self.Owner, Info.EndChargeTextNotification);

			foreach (var notify in self.TraitsImplementing<INotifySupportPower>())
				notify.Charged(self);
		}

		public virtual void SelectTarget(Actor self, string order, SupportPowerManager manager)
		{
			self.World.OrderGenerator = new SelectGenericPowerTarget(order, manager, info, MouseButton.Left);
		}

		public virtual void Activate(Actor self, Order order, SupportPowerManager manager)
		{
			if (Info.DisplayMiniMapPing && manager.MiniMapPings != null)
			{
				ping = manager.MiniMapPings.Value.Add(
					() => order.Player.IsAlliedWith(self.World.RenderPlayer),
					order.Target.CenterPosition,
					order.Player.Color,
					Info.MiniMapPingDuration);
			}

			foreach (var notify in self.TraitsImplementing<INotifySupportPower>())
				notify.Activated(self);
		}

		public virtual void PlayLaunchSounds()
		{
			var localPlayer = Self.World.LocalPlayer;
			if (localPlayer == null || localPlayer.Spectating)
				return;

			var isAllied = Self.Owner.IsAlliedWith(localPlayer);
			Game.Sound.Play(SoundType.UI, isAllied ? Info.LaunchSound : Info.IncomingSound);

			var speech = isAllied ? Info.LaunchSpeechNotification : Info.IncomingSpeechNotification;
			Game.Sound.PlayNotification(Self.World.Map.Rules, localPlayer, "Speech", speech, localPlayer.Faction.InternalName);

			var text = isAllied ? Info.LaunchTextNotification : Info.IncomingTextNotification;
			TextNotificationsManager.AddTransientLine(localPlayer, text);
		}

		public IEnumerable<CPos> CellsMatching(CPos location, char[] footprint, CVec dimensions)
		{
			var index = 0;
			var x = location.X - (dimensions.X - 1) / 2;
			var y = location.Y - (dimensions.Y - 1) / 2;
			for (var j = 0; j < dimensions.Y; j++)
				for (var i = 0; i < dimensions.X; i++)
					if (footprint[index++] == 'x')
						yield return new CPos(x + i, y + j);
		}
	}
}
