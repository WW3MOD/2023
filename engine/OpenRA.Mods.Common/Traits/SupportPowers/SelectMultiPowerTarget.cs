#region Copyright & License Information
/*
 * WW3MOD — the order generator behind a support power the player aims at SEVERAL points.
 *
 * THE USER'S ASK, verbatim: "The MIRV lands all nukes really close together. I think we need to be
 * able to target various points with it ourselves, so when we activate it we can click multiple
 * times, and an overlay shows us how many more we can target, and only when we place the last one
 * it is issued."
 *
 * Every clause of that is load-bearing and each maps to one piece below:
 *
 *   "click multiple times"          -> OrderInner accumulates a cell per left click and yields
 *                                      NOTHING until the list is full.
 *   "an overlay shows us how many"  -> RenderAnnotations, drawn from annotation renderables that
 *                                      already exist. No new widget, no new sprite, no new cursor.
 *   "only when we place the last
 *    one it is issued"              -> the single Order is yielded on click N and on no other, so
 *                                      SupportPowerInstance.Activate — the only place a shot is
 *                                      consumed (SupportPowerManager.cs, bank.Consume) — cannot run
 *                                      until the player has finished placing.
 *
 * WHY ABANDONING IT IS SAFE, which is the property worth stating plainly: this class holds a
 * List(CPos) and nothing else. It queues no activity, touches no actor, and mutates no world state.
 * Right-click, Escape (which reaches World.CancelInputMode via the ingame menu button) and losing
 * the power mid-placement all end the same way — World.OrderGenerator is replaced, this object is
 * dropped, and no Order was ever issued. Nothing launches and the magazine is untouched, because
 * the magazine is only ever read on the order-resolution path this never reached.
 *
 * DERIVES FROM OrderGenerator, not IOrderGenerator directly, to inherit the standard click
 * dispatch: the base forwards to OrderInner on left-DOWN and right-UP only (OrderGenerator.cs:12),
 * so one physical click is exactly one aim point and a click-drag cannot place two.
 */
#endregion

using System.Collections.Generic;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>
	/// Collects <c>aimPoints</c> cells for a support power and issues one order carrying all of
	/// them. The counterpart to <see cref="SelectGenericPowerTarget"/>, which issues on first click.
	/// </summary>
	public class SelectMultiPowerTarget : OrderGenerator
	{
		readonly SupportPowerManager manager;
		readonly SupportPowerInfo info;
		readonly int aimPoints;
		readonly WDist aimPointRadius;
		readonly WDist maxSpread;
		readonly string rejectedSpeechNotification;
		readonly string rejectedTextNotification;
		readonly List<CPos> placed = new();

		// CLIENT-LOCAL AND DELIBERATELY SO. The hovered cell exists to position the remaining-count
		// readout and never leaves this class — it is not read on any path that produces an Order,
		// so it cannot reach the simulation. GetCursor is called once per frame while the pointer is
		// over the world (WorldInteractionControllerWidget.cs:225), which is what keeps it current.
		CPos hoveredCell;
		bool hovering;

		public string OrderKey { get; }

		public SelectMultiPowerTarget(string order, SupportPowerManager manager, SupportPowerInfo info,
			int aimPoints, WDist aimPointRadius, WDist maxSpread = default,
			string rejectedSpeechNotification = null, string rejectedTextNotification = null)
		{
			// Same opening move as SelectGenericPowerTarget: with left-click orders the current
			// selection would otherwise eat the placement clicks.
			if (Game.Settings.Game.UseClassicMouseStyle)
				manager.Self.World.Selection.Clear();

			this.manager = manager;
			this.info = info;
			this.aimPoints = aimPoints;
			this.aimPointRadius = aimPointRadius;
			this.maxSpread = maxSpread;
			this.rejectedSpeechNotification = rejectedSpeechNotification;
			this.rejectedTextNotification = rejectedTextNotification;
			OrderKey = order;
		}

		public int Remaining => aimPoints - placed.Count;

		/// <summary>
		/// The centre of the permitted footprint: the FIRST aim point placed. Null before any point
		/// exists, which is the state in which every cell on the map is legal.
		/// </summary>
		/// <remarks>
		/// FIRST CLICK, not a running centroid. It is the only anchor that can be drawn before the
		/// next click and the only one that never revokes a cell the player was already allowed to
		/// use; see MissileStrikePowerInfo.MaxAimPointSpread for the full argument.
		/// </remarks>
		WPos? Anchor(World world) =>
			placed.Count > 0 ? world.Map.CenterOfCell(placed[0]) : null;

		/// <summary>
		/// Whether a cell may be placed: inside the map, and inside the footprint once the anchor
		/// exists. Asks <see cref="MultiAimPointOrder.IsWithinSpread"/>, which is the same predicate
		/// <see cref="MissileStrikePower"/> clamps against, so the cursor cannot promise a placement
		/// the simulation would move.
		/// </summary>
		bool CanPlace(World world, CPos cell)
		{
			if (!world.Map.Contains(cell))
				return false;

			var anchor = Anchor(world);
			return anchor == null
				|| MultiAimPointOrder.IsWithinSpread(anchor.Value, world.Map.CenterOfCell(cell), maxSpread);
		}

		protected override IEnumerable<Order> OrderInner(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			if (mi.Button == MouseButton.Right)
			{
				// CANCELS THE WHOLE PLACEMENT, not just the last point. Backing a single point out
				// would need a second gesture to abandon the strike entirely, and right-click
				// already means "stop what you are doing" everywhere else in the game.
				world.CancelInputMode();
				yield break;
			}

			if (mi.Button != MouseButton.Left || !world.Map.Contains(cell))
				yield break;

			// TOLD, NOT IGNORED. A click that does nothing and says nothing reads as the game being
			// broken rather than as a rule being enforced, so an out-of-footprint click gets the
			// same treatment PlaceBuildingOrderGenerator gives a building that will not fit
			// (PlaceBuildingOrderGenerator.cs:184-185): speech plus a transient line. The player has
			// already seen the blocked cursor and the footprint circle by this point -- this is the
			// third and loudest layer, not the only one.
			//
			// The placement is NOT cancelled and no point is consumed: the player simply clicks
			// again somewhere legal. Cancelling would punish a misclick by throwing away five
			// correct aim points.
			if (!CanPlace(world, cell))
			{
				var owner = manager.Self.Owner;
				Game.Sound.PlayNotification(world.Map.Rules, owner, "Speech", rejectedSpeechNotification,
					owner.Faction.InternalName);
				TextNotificationsManager.AddTransientLine(owner, rejectedTextNotification);

				yield break;
			}

			// NO MINIMUM SEPARATION, and no rejection of a repeated cell. The user asked for
			// control — "we need to be able to target various points with it ourselves" — and a
			// forced spacing rule is the same designer-over-player choice that produced the
			// complaint in the first place. Two warheads deliberately stacked on one hard target is
			// a real tactic. The overlay answers the spacing question instead of a rule doing it:
			// the ring drawn at each placed point is the warhead's own lethal radius, so an overlap
			// the player did not intend is visible BEFORE the last click rather than after launch.
			placed.Add(cell);

			if (placed.Count < aimPoints)
				yield break;

			// THE LAST CLICK, AND ONLY THE LAST CLICK, ISSUES.
			//
			// Target is the first aim point, carried for the target line, the minimap ping and
			// SupportPowerInstance's own snap. The AUTHORITATIVE list is TargetString: a receiver
			// that reads Target alone gets a valid single-point strike rather than a malformed one.
			var order = new Order(OrderKey, manager.Self, Target.FromCell(world, placed[0]), false)
			{
				SuppressVisualFeedback = true,
				TargetString = MultiAimPointOrder.Serialize(placed)
			};

			world.CancelInputMode();
			yield return order;
		}

		protected override void Tick(World world)
		{
			// Identical guard to SelectGenericPowerTarget: if the power stops being available
			// mid-placement — the launching actor dies, the lobby option flips, the last banked
			// shot is spent by something else — the placement is abandoned. No order was issued, so
			// nothing launches and nothing is consumed.
			if (!manager.Powers.TryGetValue(OrderKey, out var p) || !p.Active || !p.Ready)
				world.CancelInputMode();
		}

		protected override IEnumerable<IRenderable> Render(WorldRenderer wr, World world) { yield break; }
		protected override IEnumerable<IRenderable> RenderAboveShroud(WorldRenderer wr, World world) { yield break; }

		protected override IEnumerable<IRenderable> RenderAnnotations(WorldRenderer wr, World world)
		{
			var map = world.Map;
			var color = manager.Self.Owner.Color;
			var ringColor = Color.FromArgb(90, color);
			var font = Font;

			// THE FOOTPRINT, drawn first so everything else sits on top of it. This is the rule made
			// visible: every remaining aim point must land inside this circle. It is fixed at the
			// first click and never moves, which is the property that makes it readable -- a circle
			// that drifted between clicks would be worse than no circle at all.
			//
			// White rather than the player colour, and dashed-thin rather than heavy, so it reads as
			// a boundary rather than as another aim point's lethal ring.
			var footprint = Anchor(world);
			if (footprint != null && maxSpread.Length > 0)
				yield return new CircleAnnotationRenderable(footprint.Value, maxSpread, 1,
					Color.FromArgb(140, Color.White));

			for (var i = 0; i < placed.Count; i++)
			{
				var pos = map.CenterOfCell(placed[i]);

				// The lethal ring first, under everything else. This is the answer to "how do I
				// avoid stacking them" — drawn rather than enforced.
				if (aimPointRadius.Length > 0)
					yield return new CircleAnnotationRenderable(pos, aimPointRadius, 1, ringColor);

				// A small ring on the cell itself, so a point placed inside another's lethal ring
				// is still individually visible.
				yield return new CircleAnnotationRenderable(pos, new WDist(320), 2, color);

				if (font != null)
					yield return new TextAnnotationRenderable(font, pos + new WVec(0, -768, 0), 0, color,
						(i + 1).ToStringInvariant());
			}

			// THE COUNT THE USER ASKED FOR. Pinned to the pointer rather than to a screen corner so
			// it is read without looking away from where the next warhead is going.
			if (font != null && hovering && Remaining > 0 && CanPlace(world, hoveredCell))
			{
				var text = Remaining > 1
					? Remaining.ToStringInvariant() + " MORE"
					: "LAST - CLICK TO LAUNCH";

				yield return new TextAnnotationRenderable(font, map.CenterOfCell(hoveredCell) + new WVec(0, 1024, 0),
					0, Color.White, text);
			}
		}

		protected override string GetCursor(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			hoveredCell = cell;
			hovering = true;

			// The bound BEFORE the click, not after it. CanPlace is the same predicate OrderInner
			// refuses on, so the cursor is a promise the click keeps.
			return CanPlace(world, cell) ? info.Cursor : info.BlockedCursor;
		}

		/// <summary>
		/// The readout font, or null when the mod does not define it.
		/// </summary>
		/// <remarks>
		/// Looked up rather than indexed, and every caller null-checks the result. A missing font
		/// must cost the mod its aim-point NUMBERS, not the ability to fire the weapon — indexing
		/// Renderer.Fonts would throw inside a render loop and take the frame with it.
		/// </remarks>
		static SpriteFont Font =>
			Game.Renderer.Fonts != null && Game.Renderer.Fonts.TryGetValue("MediumBold", out var f) ? f : null;
	}
}
