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

using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Attached to infantry that can occupy garrison ports. " +
		"When deployed at a port (condition active), replaces the normal Targetable " +
		"and only allows targeting by enemies within the port's firing arc. " +
		"The regular Targetable trait should have RequiresCondition: !garrisoned-at-port.")]
	public class GarrisonPortOccupantInfo : ConditionalTraitInfo, ITargetableInfo
	{
		[Desc("Target types (should match the regular Targetable).")]
		public readonly BitSet<TargetableType> TargetTypes;

		[Desc("Condition that activates this trait (should match GarrisonManager.GarrisonedCondition).")]
		public readonly string ActiveCondition = "garrisoned-at-port";

		public BitSet<TargetableType> GetTargetTypes() { return TargetTypes; }

		public override object Create(ActorInitializer init) { return new GarrisonPortOccupant(this); }
	}

	public class GarrisonPortOccupant : ConditionalTrait<GarrisonPortOccupantInfo>, ITargetable, IResolveOrder
	{
		// Set by GarrisonManager when deploying to a port, cleared on recall
		public Actor GarrisonBuilding { get; private set; }
		public int PortIndex { get; private set; } = -1;

		public GarrisonPortOccupant(GarrisonPortOccupantInfo info)
			: base(info) { }

		/// <summary>
		/// Force-move (Ctrl) issued to a soldier standing at a port takes him, and only him, out of the
		/// building — the direct-manipulation twin of the unload menu's per-man eject, so that choosing
		/// who leaves no longer means emptying the whole garrison.
		/// <para>Without this the order is not refused, it is swallowed: Mobile carries
		/// PauseOnCondition: garrisoned-at-port (infantry.yaml:53) while MoveOrderTargeter accepts the
		/// click regardless of pause (Mobile.cs:1174), so the player gets a move cursor, a real ForceMove
		/// order and a Move activity that is queued and then never advances. Revoking the condition is
		/// what releases it — Mobile's own contract is that a move queued while paused runs once unpaused,
		/// so this works whichever of the two traits resolves the order first.</para>
		/// <para>Scoped to ForceMove on purpose. Ctrl is the deliberate "yes, really leave the building"
		/// gesture; a plain right-click near a garrison stays inert rather than emptying a port by accident.</para>
		/// </summary>
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			// A ConditionalTrait still receives orders while disabled, so this is what scopes the
			// behaviour to a man actually standing at a port rather than any infantryman on the map.
			if (IsTraitDisabled || order.OrderString != "ForceMove")
				return;

			if (GarrisonBuilding == null || GarrisonBuilding.IsDead || !GarrisonBuilding.IsInWorld)
				return;

			var manager = GarrisonBuilding.TraitOrDefault<GarrisonManager>();
			if (manager == null)
				return;

			// Each selected soldier resolves his own ForceMove — the generator passes them through
			// individually rather than grouping them (UnitOrderGenerator.cs:101-105), so force-moving a
			// multi-soldier selection releases exactly those men and leaves the rest of the garrison in place.
			manager.EjectPassenger(GarrisonBuilding, self, self.World.Map.CellContaining(order.Target.CenterPosition));
		}

		public void SetPort(Actor building, int portIndex)
		{
			GarrisonBuilding = building;
			PortIndex = portIndex;
		}

		public void ClearPort()
		{
			GarrisonBuilding = null;
			PortIndex = -1;
		}

		public BitSet<TargetableType> TargetTypes => Info.TargetTypes;
		public bool RequiresForceFire => false;

		public bool TargetableBy(Actor self, Actor byActor)
		{
			if (IsTraitDisabled)
				return false;

			if (GarrisonBuilding == null || GarrisonBuilding.IsDead || PortIndex < 0)
				return true; // Fallback: targetable if port info missing

			var gm = GarrisonBuilding.TraitOrDefault<GarrisonManager>();
			if (gm == null || PortIndex >= gm.PortStates.Length)
				return true;

			var port = gm.PortStates[PortIndex].Port;

			// Calculate angle from building center to the attacker
			var buildingPos = GarrisonBuilding.CenterPosition;
			var viewerPos = byActor.CenterPosition;
			var delta = viewerPos - buildingPos;

			if (delta.HorizontalLengthSquared == 0)
				return true; // Attacker on top of building, allow targeting

			var angleToViewer = delta.Yaw;

			// Check if viewer is within port's Yaw ± Cone
			var diff = (angleToViewer - port.Yaw).Angle;

			// Normalize to [-512, 512) range (WAngle 1024 = full circle)
			if (diff > 512)
				diff -= 1024;

			// Within cone = targetable, outside = hidden from this attacker
			return diff >= -port.Cone.Angle && diff <= port.Cone.Angle;
		}
	}
}
