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
using OpenRA.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.Player)]
	[Desc("Attach this to the player actor.")]
	public class SupportPowerManagerInfo : TraitInfo, Requires<DeveloperModeInfo>, Requires<TechTreeInfo>
	{
		public override object Create(ActorInitializer init) { return new SupportPowerManager(init); }
	}

	public class SupportPowerManager : ITick, IResolveOrder, ITechTreeElement
	{
		public readonly Actor Self;
		public readonly Dictionary<string, SupportPowerInstance> Powers = new();

		public readonly DeveloperMode DevMode;
		public readonly TechTree TechTree;
		public readonly Lazy<MiniMapPings> MiniMapPings;

		public SupportPowerManager(ActorInitializer init)
		{
			Self = init.Self;
			DevMode = Self.Trait<DeveloperMode>();
			TechTree = Self.Trait<TechTree>();
			MiniMapPings = Exts.Lazy(() => Self.World.WorldActor.TraitOrDefault<MiniMapPings>());

			init.World.ActorAdded += ActorAdded;
			init.World.ActorRemoved += ActorRemoved;
		}

		static string MakeKey(SupportPower sp)
		{
			return sp.Info.AllowMultiple ? sp.Info.OrderName + "_" + sp.Self.ActorID : sp.Info.OrderName;
		}

		void ActorAdded(Actor a)
		{
			if (a.Owner != Self.Owner)
				return;

			foreach (var t in a.TraitsImplementing<SupportPower>())
			{
				var key = MakeKey(t);

				if (!Powers.TryGetValue(key, out var spi))
				{
					Powers.Add(key, spi = t.CreateInstance(key, this));

					if (t.Info.Prerequisites.Length > 0)
					{
						TechTree.Add(key, t.Info.Prerequisites, 0, this);
						TechTree.Update();
					}
				}

				spi.Instances.Add(t);
			}
		}

		void ActorRemoved(Actor a)
		{
			if (a.Owner != Self.Owner || !a.Info.HasTraitInfo<SupportPowerInfo>())
				return;

			foreach (var t in a.TraitsImplementing<SupportPower>())
			{
				var key = MakeKey(t);
				Powers[key].Instances.Remove(t);

				if (Powers[key].Instances.Count == 0 && !Powers[key].Disabled)
				{
					Powers.Remove(key);
					TechTree.Remove(key);
					TechTree.Update();
				}
			}
		}

		void ITick.Tick(Actor self)
		{
			foreach (var power in Powers.Values)
				power.Tick();
		}

		public void ResolveOrder(Actor self, Order order)
		{
			// order.OrderString is the key of the support power
			if (Powers.TryGetValue(order.OrderString, out var sp))
				sp.Activate(order);
		}

		static readonly SupportPowerInstance[] NoInstances = Array.Empty<SupportPowerInstance>();

		public IEnumerable<SupportPowerInstance> GetPowersForActor(Actor a)
		{
			if (Powers.Count == 0 || a.Owner != Self.Owner || !a.Info.HasTraitInfo<SupportPowerInfo>())
				return NoInstances;

			return a.TraitsImplementing<SupportPower>()
				.Select(t => Powers[MakeKey(t)])
				.Where(p => p.Instances.Any(i => !i.IsTraitDisabled && i.Self == a));
		}

		public void PrerequisitesAvailable(string key)
		{
			if (!Powers.TryGetValue(key, out var sp))
				return;

			sp.PrerequisitesAvailable(true);
		}

		public void PrerequisitesUnavailable(string key)
		{
			if (!Powers.TryGetValue(key, out var sp))
				return;

			sp.PrerequisitesAvailable(false);
		}

		public void PrerequisitesItemHidden(string key) { }
		public void PrerequisitesItemVisible(string key) { }
	}

	public class SupportPowerInstance
	{
		protected readonly SupportPowerManager Manager;

		public readonly string Key;

		public readonly List<SupportPower> Instances = new();

		/// <summary>
		/// <para>The charge interval this power is counting against, in ticks. 0 for a purchased power,
		/// which has no timer at all.</para>
		///
		/// <para>ANYTHING THAT WRITES <see cref="remainingSubTicks"/> FROM THIS VALUE IS COUPLED TO
		/// WHOEVER SET IT LAST. <see cref="ResetTimer"/> assigns `TotalTicks * 100`, so for a power
		/// carrying a nuclear SIDE cooldown it re-arms the full lockout rather than the side's
		/// remaining ticks, and the cameo then disagrees with the ledger. Nothing in ww3mod reaches
		/// that today -- `InfiltrateForSupportPowerReset` is the one caller and no actor in the mod
		/// carries it -- but a mod that wires spy infiltration against a nuclear power gets exactly
		/// that desynchronisation, and the fix would be to route it through
		/// NuclearExchange rather than to touch the timer directly.</para>
		///
		/// <para>SETTABLE SINCE THE NUCLEAR EXCHANGE v2, AND ONLY FROM <see cref="SetCooldown"/>. It was
		/// readonly while every timer was a property of the POWER; Escalation's side cooldown is a
		/// property of the SIDE and of the band that was fired, so the same 1 kt warhead is on a five
		/// minute clock after its own shot and a twelve minute one after its team's 100 kt. That cannot
		/// be expressed by writing <see cref="remainingSubTicks"/> alone: <see cref="Tick"/> clamps the
		/// countdown to <c>TotalTicks * 100</c> on the very next tick, so a longer value is silently
		/// truncated -- and the cameo's clock wipe is drawn as a fraction of this, so a countdown that
		/// did not move it would start the arc part-drawn.</para>
		/// </summary>
		public int TotalTicks { get; private set; }

		protected int remainingSubTicks;
		public int RemainingTicks => remainingSubTicks / 100;
		public bool Active { get; private set; }

		/// <summary>
		/// "May this player have this power at all?" -- the host's lobby tick, the faction
		/// prerequisite, still being alive. SEPARATE from whether a shot is banked, and that
		/// separation is the whole of the purchase model: the build menu keys off THIS, so a power
		/// is buyable precisely while it is permitted, whether or not one is already loaded.
		/// </summary>
		public bool Permitted =>
			PermittedIgnoringPrerequisites && (prereqsAvailable || Manager.DevMode.AllTech);

		/// <summary>
		/// <para>Everything <see cref="Permitted"/> asks EXCEPT the tech tree's answer: alive, enabled by
		/// its own RequiresCondition, and not a spent one-shot.</para>
		///
		/// <para>THIS IS NOT A WEAKER `Permitted` AND IS NOT FOR GENERAL USE. It exists for the two paths
		/// that are LICENSED to override a prerequisite — the final exchange and the nuclear exchange's
		/// TOP RUNG at <see cref="NuclearRung.GameEnder"/>, both of which hand out powers gated on
		/// `powers.event`, a
		/// name no faction provides. Those paths need to know whether everything else about the power is
		/// in order before they decide to override the one thing that is not; asking `Permitted` gives
		/// them a flat no and asking nothing at all would force readiness onto a power whose own
		/// condition is unsatisfied, which <see cref="Tick"/> would undo on the same tick anyway.</para>
		///
		/// <para>THE CALLER STILL OWES THE OWNERSHIP CHECK. Overriding the tier is sanctioned; overriding
		/// the faction is not. <see cref="NuclearGameEnders.ArmableBy"/> is where that line is drawn,
		/// and a caller that reads this property without also asking that one hands an America player
		/// Russia's warhead.</para>
		/// </summary>
		public bool PermittedIgnoringPrerequisites =>
			Manager.Self.Owner.WinState != WinState.Lost &&
			instancesEnabled &&
			!oneShotFired;

		/// <summary>
		/// "Should the cameo be ABSENT from the support bin?" -- SupportPowersWidget filters on this
		/// (SupportPowersWidget.cs:136). For a timer power this is exactly !Permitted, byte for byte
		/// what it was before the purchase model existed, because an unpurchasable power's bank
		/// reports HidesIcon false. For a purchased power it additionally hides an empty magazine.
		/// </summary>
		public bool Disabled => !bank.IconVisible(Permitted);

		/// <summary>Can this power be BOUGHT right now? False for every timer-charged power.</summary>
		public bool Purchasable => bank.CanPurchase(Permitted);

		/// <summary>Shots paid for and not yet fired. Always 0 for a timer-charged power.</summary>
		public int Charges => bank.Charges;

		public SupportPowerInfo Info { get { return Instances.Select(i => i.Info).FirstOrDefault(); } }
		public readonly string Name;
		public readonly string Description;
		public bool Ready => Active && RemainingTicks == 0;

		readonly SupportPowerChargeBank bank;

		bool instancesEnabled;
		bool prereqsAvailable = true;
		bool oneShotFired;
		protected bool notifiedCharging;
		bool notifiedReady;

		public void ResetTimer()
		{
			remainingSubTicks = TotalTicks * 100;
		}

		/// <summary>
		/// <para>Put this power on a cooldown of exactly <paramref name="ticks"/>, whatever interval it
		/// was built with. 0 makes it ready on this tick.</para>
		///
		/// <para>THE ONE CALLER IS <see cref="NuclearExchange"/>, in DEFCON Escalation, where a nuclear
		/// power has no interval of its own: what it waits for is its SIDE's cooldown, set by the band
		/// somebody on that side last fired. Both numbers are written together so the cameo cannot lie
		/// in either direction -- the countdown under the icon is <see cref="RemainingTicks"/> and the
		/// clock wipe over it is the ratio to <see cref="TotalTicks"/>, and a caller that moved only one
		/// of them would draw an arc that disagrees with its own number.</para>
		///
		/// <para>IT IS INERT OUTSIDE ESCALATION because nothing else calls it. Skirmish, Sandbox and
		/// every other mod keep the interval their ChargeInterval gave them, byte for byte.</para>
		/// </summary>
		public void SetCooldown(int ticks)
		{
			// ZERO MEANS "READY NOW", NOT "NO TIMER AT ALL", and the difference is a whole economy.
			// TotalTicks == 0 is the shape a PURCHASED power has: Tick clamps the countdown to [0, 0],
			// the cameo draws a full clock forever, and SupportPowerChargeBar divides by it. A caller
			// meaning "this is available again" must not be able to write that by accident -- which a
			// game-ender's zero cooldown very nearly did (review, 2026-09-15). Leave the interval
			// alone and just empty the countdown.
			if (ticks <= 0)
			{
				remainingSubTicks = 0;
				return;
			}

			TotalTicks = ticks;
			remainingSubTicks = ticks * 100;
		}

		/// <summary>
		/// A purchase completed: bank a shot. Called from SupportPowerProductionQueue.BuildUnit, on
		/// the synced production path, so every client banks it on the same tick.
		/// </summary>
		public void GrantCharge(int count = 1)
		{
			bank.Grant(count);

			// Belt and braces. TotalTicks is already 0 for any power that can reach here, so this
			// assignment is a no-op today; it is written down so that a future power which is both
			// purchased AND carries a live ChargeInterval still arrives ready rather than starting
			// a countdown the player has just paid to skip.
			remainingSubTicks = 0;
		}

		/// <summary>
		/// <para>FORCE THIS POWER FIRE-READY ON THIS TICK, whatever it was doing. The DOOMSDAY final
		/// exchange is the one caller: every surviving side is handed its game-enders for fifteen
		/// seconds, and fifteen seconds is far shorter than any charge interval in the mod.</para>
		///
		/// <para>IT HAS TO DO ALL THREE HALVES, because a power is Ready only when every one of them is
		/// satisfied and which is binding depends on the power:
		///   * THE TIER. Both shipped game-enders carry `Prerequisites: powers.event`, and powers.event
		///     is "provided by NO faction, ever -- Dead Hand / scripted / demo only" (player.yaml:144);
		///     outside Sandbox nothing provides it, so `Permitted` is false however many conditions are
		///     granted. THIS IS THE SHIPPED DEFAULT FOR BOTH OF THEM, not an edge case — grant the
		///     condition alone and the exchange hands out nothing at all. Overridden HERE rather than
		///     by a second ProvidesPrerequisite on `powers.event`, which would be the declarative way
		///     and is the wrong one: that name is shared with the TSAR BOMBA, and putting a 50 Mt
		///     warhead on the shop floor is precisely what decision 04 closes off. Setting the flag on
		///     the chosen instances leaks to nothing else.
		///
		///     It STICKS, and that is a property of TechTree rather than luck: Watcher.Update notifies
		///     only on an EDGE (TechTree.cs:166-190), and the real prerequisite state does not change,
		///     so nothing re-issues PrerequisitesUnavailable and undoes this.
		///   * THE MAGAZINE. A RequiresPurchase power (both shipped game-enders) is Ready only while a
		///     shot is banked, and nothing else here banks one. Granted only when the bank is EMPTY, so
		///     arming a side that already bought a game-ender does not quietly hand it a second.
		///   * THE TIMER. A timer-charged power that has been sitting disabled has remainingSubTicks
		///     pinned at full: Tick resets it on every tick instancesEnabled is false (:246-248) and
		///     then returns before the countdown, so a disabled power does not charge while it waits.
		///     Zeroing is the only way it can be ready inside a window this short. No-op for a purchased
		///     power, whose TotalTicks is 0 and whose remainingSubTicks is therefore already 0.</para>
		///
		/// <para>THE CONDITION IS THE CALLER'S PROBLEM, not this method's. A power whose RequiresCondition is
		/// unsatisfied stays disabled, Tick pins its timer back to full on the next tick, and this will
		/// have achieved nothing — so grant the condition FIRST. Actor.GrantCondition applies immediately
		/// (Actor.cs:725-733), which is what makes "first" mean "on the same line" rather than "a tick
		/// earlier".</para>
		/// </summary>
		/// <para>WHAT IT CANNOT DO: a OneShot power that has already fired stays unpermitted, because
		/// oneShotFired is a one-way latch and re-opening it would be a different feature. No power in
		/// the arsenal sets OneShot today, so this costs nothing; a game-ender that ever does will need
		/// deciding about rather than inheriting this silently.</para>
		public virtual void MakeReady()
		{
			prereqsAvailable = true;

			if (bank.Enabled && bank.Charges == 0)
				bank.Grant(1);

			remainingSubTicks = 0;
		}

		public SupportPowerInstance(string key, SupportPowerInfo info, SupportPowerManager manager)
		{
			Key = key;
			Manager = manager;

			// A purchased power has NO timer at all -- not a long one, none. TotalTicks 0 makes
			// remainingSubTicks permanently 0 through every path that touches it (Tick clamps to
			// [0, 0], PrerequisitesAvailable and Activate both assign TotalTicks * 100), so
			// `Ready => Active && RemainingTicks == 0` reduces to `Active`, and Active reduces to
			// Permitted-and-stocked. That is "arrives already fully loaded", expressed as an
			// invariant rather than as a value that has to be reset in the right places.
			//
			// The one consumer that would divide by it already guards zero: SupportPowersWidget
			// pins the cameo clock to its last frame when TotalTicks == 0 (SupportPowersWidget.cs:214),
			// which draws a full circle -- the correct picture for a shot sitting in the magazine.
			// ==== THE ESCALATION BYPASS, AND IT IS THE ONE PLACE THE ECONOMY IS DECIDED ====
			// Decision 02: in DEFCON Escalation nothing nuclear is purchasable -- a band is a free
			// power on a regeneration timer. EscalationCooldownTicks returns that timer, or -1 for the
			// ordinary purchase economy, and -1 is what EVERY power outside Escalation and every
			// non-nuclear power inside it gets. So Skirmish, Sandbox and every other mod are
			// byte-identical to before by construction rather than by care.
			//
			// TURNING THE BANK OFF IS WHAT EMPTIES THE BUY TAB, and it costs nothing extra:
			// SupportPowerProductionQueue filters BOTH AllItems and BuildableItems on
			// SupportPowerInstance.Purchasable (:109-116), which is bank.CanPurchase, which is
			// `Enabled && permitted`. A bank built disabled therefore removes the cameo from the shop
			// rather than greying it out -- which is the behaviour the ruling asks for, stated once
			// here instead of as a filter somewhere else that could fall out of step with this line.
			var escalationCooldown = NuclearExchange.EscalationCooldownTicks(manager.Self.World, info);
			var purchased = info.RequiresPurchase && escalationCooldown < 0;

			bank = new SupportPowerChargeBank(purchased);

			// THE ESCALATION VALUE IS A STARTING POINT, NOT THE INTERVAL THIS POWER WILL USE. In that
			// mode NuclearExchange rewrites both numbers through SetCooldown on the release edge, on
			// every level rise and on every launch by this side, because the wait is the SIDE's and not
			// the power's. This is its own band's cooldown, which is what the first shot at this band
			// would cost -- a sane value for the window between actor creation and the release grant,
			// during which the power's band condition is ungranted and Tick pins the countdown to full
			// anyway.
			TotalTicks = purchased ? 0 : (escalationCooldown >= 0 ? escalationCooldown : info.ChargeInterval);

			// A FREE NUCLEAR POWER STARTS COLD, not fully charged, and that is not a tax on the player:
			// its band condition is ungranted until release, and Tick pins remainingSubTicks back to
			// full on every tick a power is disabled (:246-248), so the countdown could not have run
			// anyway. NuclearExchange zeroes it on the grant edge through MakeReady, which is what
			// makes the band ready the instant it is released rather than one interval later.
			remainingSubTicks = info.StartFullyCharged || purchased ? 0 : TotalTicks * 100;
			Name = info.Name == null ? string.Empty : FluentProvider.GetMessage(info.Name);
			Description = info.Description == null ? string.Empty : FluentProvider.GetMessage(info.Description);
		}

		public virtual void PrerequisitesAvailable(bool available)
		{
			prereqsAvailable = available;

			if (!available)
				remainingSubTicks = TotalTicks * 100;
		}

		public virtual void Tick()
		{
			instancesEnabled = Instances.Any(i => !i.IsTraitDisabled);
			if (!instancesEnabled)
				remainingSubTicks = TotalTicks * 100;

			Active = !Disabled && Instances.Any(i => !i.IsTraitPaused);
			if (!Active)
				return;

			var power = Instances[0];
			if (Manager.DevMode.FastCharge && remainingSubTicks > 2500)
				remainingSubTicks = 2500;

			if (remainingSubTicks > 0)
				remainingSubTicks = (remainingSubTicks - 100).Clamp(0, TotalTicks * 100);

			if (!notifiedCharging)
			{
				power.Charging(power.Self, Key);
				notifiedCharging = true;
			}

			if (RemainingTicks == 0 && !notifiedReady)
			{
				power.Charged(power.Self, Key);
				notifiedReady = true;
			}
		}

		public virtual void Target()
		{
			if (!Ready)
				return;

			var power = Instances.FirstOrDefault(i => !i.IsTraitPaused);

			if (power == null)
				return;

			Game.Sound.PlayToPlayer(SoundType.UI, Manager.Self.Owner, Info.SelectTargetSound);
			Game.Sound.PlayNotification(power.Self.World.Map.Rules, power.Self.Owner, "Speech",
				Info.SelectTargetSpeechNotification, power.Self.Owner.Faction.InternalName);

			TextNotificationsManager.AddTransientLine(power.Self.Owner, Info.SelectTargetTextNotification);

			power.SelectTarget(power.Self, Key, Manager);
		}

		public virtual void Activate(Order order)
		{
			if (!Ready)
				return;

			// Resolved HERE rather than in SelectGenericPowerTarget because this is the one seam
			// every order source funnels through — the human order generator, a bot's QueueOrder
			// (SupportPowerBotModule.cs:114-115, which builds Target.FromCell), and a Lua binding
			// alike — and because it runs on the synced order-resolution path, so every client
			// resolves the same aim point from the same world rather than trusting the position one
			// client computed. The cost is that the order generator cannot draw the resolved point
			// without duplicating this; see the comment on SelectGenericPowerTarget.GetCursor.
			if (Info != null && Info.SnapToActorCenter)
				order = SupportPowerAimPoint.SnapToActorCenter(Manager.Self.World, order);

			var power = Instances.Where(i => !i.IsTraitPaused && !i.IsTraitDisabled)
				.MinByOrDefault(a =>
				{
					if (a.Self.OccupiesSpace == null || order.Target.Type == TargetType.Invalid)
						return 0;

					return (a.Self.CenterPosition - order.Target.CenterPosition).HorizontalLengthSquared;
				});

			if (power == null)
				return;

			// Note: order.Subject is the *player* actor
			power.Activate(power.Self, order, Manager);

			// One purchase, one shot. Consume BEFORE the timer reset below so a stacked bank leaves
			// the cameo up: at charges 2 -> 1 the power stays permitted and stocked, Disabled stays
			// false, and the icon simply stops reading "x2". At 1 -> 0 the bank empties and the
			// cameo leaves the bin until the next purchase lands. No-op for a timer power.
			bank.Consume();

			remainingSubTicks = TotalTicks * 100;
			notifiedCharging = notifiedReady = false;

			if (Info.OneShot)
			{
				PrerequisitesAvailable(false);
				oneShotFired = true;
			}
		}

		public virtual string IconOverlayTextOverride()
		{
			// "x3" for a stacked magazine, null at 0 or 1 shot so the widget falls through to its
			// own READY / ON HOLD / countdown logic exactly as before.
			return bank.OverlayText;
		}

		public virtual string TooltipTimeTextOverride()
		{
			return null;
		}
	}

	public class SelectGenericPowerTarget : OrderGenerator
	{
		readonly SupportPowerManager manager;
		readonly SupportPowerInfo info;
		readonly MouseButton expectedButton;

		public string OrderKey { get; }

		public SelectGenericPowerTarget(string order, SupportPowerManager manager, SupportPowerInfo info, MouseButton button)
		{
			// Clear selection if using Left-Click Orders
			if (Game.Settings.Game.UseClassicMouseStyle)
				manager.Self.World.Selection.Clear();

			this.manager = manager;
			OrderKey = order;
			this.info = info;
			expectedButton = button;
		}

		protected override IEnumerable<Order> OrderInner(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			world.CancelInputMode();
			if (mi.Button == expectedButton && world.Map.Contains(cell))
				yield return new Order(OrderKey, manager.Self, Target.FromCell(world, cell), false) { SuppressVisualFeedback = true };
		}

		protected override void Tick(World world)
		{
			// Cancel the OG if we can't use the power
			if (!manager.Powers.TryGetValue(OrderKey, out var p) || !p.Active || !p.Ready)
				world.CancelInputMode();
		}

		protected override IEnumerable<IRenderable> Render(WorldRenderer wr, World world) { yield break; }
		protected override IEnumerable<IRenderable> RenderAboveShroud(WorldRenderer wr, World world) { yield break; }
		protected override IEnumerable<IRenderable> RenderAnnotations(WorldRenderer wr, World world) { yield break; }
		// UNCHANGED, KNOWINGLY. With SupportPowerInfo.SnapToActorCenter on, a click over one
		// quadrant of a 2x2 building strikes the building's centre, so the cell-shaped cursor the
		// player sees is a small lie about where the blast lands. Drawing the truth needs the
		// resolved aim point, which is computed on the synced path in SupportPowerInstance.Activate
		// and is not available here without a second client-side copy of the same query — a copy
		// that would read actors inside the player's own fog. Left as a deliberate debt: the honest
		// fix is an aim-point marker rendered from a shared resolver, and it is a visual change that
		// wants a screenshot pass rather than a blind edit.
		protected override string GetCursor(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			return world.Map.Contains(cell) ? info.Cursor : info.BlockedCursor;
		}
	}
}
