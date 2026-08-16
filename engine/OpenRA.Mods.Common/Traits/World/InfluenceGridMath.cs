#region Copyright & License Information
/*
 * WW3MOD influence stack — coarse-grid coordinate conversion (pure).
 *
 * The control/influence grid is COARSER than the map: one grid cell covers CellSize x CellSize map cells.
 * Converting between the two is lossy in one direction and that asymmetry has already cost one shipped bug,
 * so the two halves live here, together, with the loss stated:
 *
 *   MapToGrid       is a FLOOR      — every map cell in a block maps to the one grid cell containing it.
 *   GridToMapCentre is a REPRESENTATIVE — it returns the block's centre, not the cell you came from.
 *
 * THE ROUND TRIP IS NOT THE IDENTITY. GridToMapCentre(MapToGrid(v)) == v only when v happens to BE the centre
 * of its block: at CellSize 2 the centre is 2k+1, so it holds for odd v and fails for even v — one coordinate
 * in two, and for a 2-D cell one placement in FOUR.
 *
 * Consequence, learned the expensive way (WORKSPACE/recon/260817-combined-arms-rendezvous-postmortem.md):
 * NEVER test "did this grid-space computation move?" by round-tripping to map space and comparing there.
 * A zero-step result compares UNEQUAL to its own input for 3 of 4 inputs, so the test silently inverts.
 * Compare in grid space, then convert once at the end.
 *
 * v3-portable: engine-free integer math, no World, no CPos. Zero RNG.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	public static class InfluenceGridMath
	{
		/// <summary>Grid coordinate of the block containing this map coordinate (floor).</summary>
		public static int MapToGrid(int cellSize, int mapCoord)
		{
			return mapCoord / cellSize;
		}

		/// <summary>
		/// Map coordinate at the CENTRE of this grid block. Not an inverse of <see cref="MapToGrid"/> — see the
		/// note on this class before using the two together.
		/// </summary>
		public static int GridToMapCentre(int cellSize, int gridCoord)
		{
			return gridCoord * cellSize + cellSize / 2;
		}
	}
}
