#region Copyright & License Information
/*
 * WW3MOD FrontlineOverlay — Stage A.3 of the doctrine roadmap.
 *
 * In-game debug overlay that renders InfluenceMap.GetFrontline() as
 * coloured circles, one per contested grid cell. Toggled by chat
 * command (default "/frontline") — F-key hotkey wiring is Stage A.4.
 *
 * Renders ABOVE world units (annotation pass), so the user can see the
 * contested zone over their armies.
 */
#endregion

using System.Collections.Generic;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Commands;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("WW3MOD: in-game debug overlay for InfluenceMap frontline cells.",
		"Chat command toggles render. Reads from InfluenceMap (must also be on the World).")]
	public class FrontlineOverlayInfo : TraitInfo
	{
		[Desc("Chat command to toggle the overlay.")]
		public readonly string CommandName = "frontline";

		[Desc("Colour of the frontline marker (ARGB).")]
		public readonly Color Color = Color.FromArgb(160, 255, 140, 0);

		[Desc("Radius (in WDist units, 1024 = 1 cell) of the filled marker per grid cell.")]
		public readonly int MarkerRadius = 768;

		public override object Create(ActorInitializer init) { return new FrontlineOverlay(this); }
	}

	public sealed class FrontlineOverlay : IWorldLoaded, IChatCommand, IRenderAnnotations
	{
		readonly FrontlineOverlayInfo info;
		InfluenceMap influenceMap;
		World world;

		bool enabled;

		public FrontlineOverlay(FrontlineOverlayInfo info)
		{
			this.info = info;
		}

		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			world = w;
			influenceMap = w.WorldActor.TraitOrDefault<InfluenceMap>();

			var console = w.WorldActor.TraitOrDefault<ChatCommands>();
			console?.RegisterCommand(info.CommandName, this);
		}

		void IChatCommand.InvokeCommand(string name, string arg)
		{
			if (name == info.CommandName)
				enabled = !enabled;
		}

		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (!enabled || influenceMap == null)
				yield break;

			// Perspective rule: render from the observing player's POV when there is one
			// (1v1 normal play). Spectator slots use the all-perspective view so the
			// observer sees every contested zone regardless of point of view.
			var perspective = world.LocalPlayer;
			bool[,] frontline;
			if (perspective == null || perspective.Spectating)
				frontline = influenceMap.GetFrontlineAnyPerspective();
			else
				frontline = influenceMap.GetFrontline(perspective);

			var radius = new WDist(info.MarkerRadius);
			for (var x = 0; x < influenceMap.GridWidth; x++)
			{
				for (var y = 0; y < influenceMap.GridHeight; y++)
				{
					if (!frontline[x, y])
						continue;

					var centreCell = influenceMap.GridCellToMapCell(x, y);
					if (!world.Map.Contains(centreCell))
						continue;

					var centreWorld = world.Map.CenterOfCell(centreCell);
					yield return new CircleAnnotationRenderable(centreWorld, radius, 1, info.Color, filled: true);
				}
			}
		}

		bool IRenderAnnotations.SpatiallyPartitionable => false;
	}
}
