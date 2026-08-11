#region Copyright & License Information
/*
 * WW3MOD select-by-type — the scope decision behind Ctrl+Alt+LMB on a build-menu icon.
 *
 * Extracted from ProductionPaletteWidget as a pure static because the gesture cannot be reached
 * without a live mouse event on a real sidebar icon, and the interesting behaviour is entirely in
 * the branch choice: which of {nothing, on-screen, whole map} a click resolves to. Pinned in
 * SelectByTypeScopeTest.
 *
 * The escalation shape (screen first, map on the repeat click) is copied from
 * SelectUnitsByTypeHotkeyLogic and SelectAllUnitsHotkeyLogic rather than invented here, so all
 * three select-same-type paths in the mod widen in the same way.
 */
#endregion

namespace OpenRA.Mods.Common.Widgets
{
	public enum SelectByTypeScope
	{
		/// <summary>Player owns none of this type. Leave the current selection alone.</summary>
		None,

		/// <summary>Select the matching units currently visible on screen.</summary>
		Screen,

		/// <summary>Select every matching unit on the map.</summary>
		World,
	}

	public static class SelectByTypeScopeMath
	{
		/// <summary>
		/// Resolves one Ctrl+Alt+LMB on a build-menu icon.
		/// <paramref name="selectionIsExactlyOnScreenSet"/> means the player already holds precisely the
		/// on-screen matches — i.e. this is a repeat click — which is what escalates to the whole map.
		/// </summary>
		public static SelectByTypeScope Resolve(int onScreenCount, int worldCount, bool selectionIsExactlyOnScreenSet)
		{
			// Owning none of the type anywhere is a no-op rather than an empty selection: a mis-click on
			// the sidebar must not throw away whatever the player currently has selected.
			if (worldCount == 0)
				return SelectByTypeScope.None;

			// Nothing of the type is visible, so the screen step would select nothing and the player
			// would have to click twice to reach units they cannot see. Skip straight to the map.
			if (onScreenCount == 0)
				return SelectByTypeScope.World;

			// Repeat click, and there is genuinely something more to find off-screen.
			if (selectionIsExactlyOnScreenSet && worldCount > onScreenCount)
				return SelectByTypeScope.World;

			return SelectByTypeScope.Screen;
		}

		/// <summary>
		/// The selection class a build-menu icon maps to. Mirrors Selectable's constructor, which falls
		/// back to the actor name when SelectableInfo.Class is unset — the case for every ww3mod unit.
		/// </summary>
		public static string ResolveSelectionClass(string actorName, string configuredClass)
		{
			return string.IsNullOrEmpty(configuredClass) ? actorName : configuredClass;
		}
	}
}
