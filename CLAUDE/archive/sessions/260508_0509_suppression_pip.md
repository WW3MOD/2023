# Suppression pip visibility on regular soldiers

## Summary
User reports the suppression pip is visible on ground-vehicle crew (driver/gunner/commander/pilot) but NOT on regular infantry. Wants all soldiers to show the same suppression pip.

## Root cause (hypothesis)
`^SuppressionPips` (mods/ww3mod/rules/ingame/infantry.yaml:499) is at `Position: Top, Margin: 0,0` — i.e., centered horizontally, anchored to selectable bounds top.

`^Soldier`'s `WithDecoration@Class` is at `Position: Top, Margin: 0,6` — same X anchor, 6px below. Class sprites for regular infantry (e1_class, e3_class, etc.) are full pictograms that are tall enough to extend upward and visually cover the suppression pip's area. Crew use `empty_class` / small `pilot_class` so the suppression pip stays uncovered.

No `-WithDecoration@Suppression_X` removals or `-Inherits@Suppression*` exist. The chain `^Infantry → ^SuppressionEffects → ^SuppressionPips` is intact for everyone — they all *have* the trait, but the pip is occluded for regular soldiers.

## Files I'll touch
- mods/ww3mod/rules/ingame/infantry.yaml (only the `^SuppressionPips` Margin values, lines 499-579)

## Status
in progress
