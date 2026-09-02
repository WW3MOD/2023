### 75. Infantry give no selection feedback at all

`[BLOCKED ON A SCREENSHOT PASS — the change is two lines; the judgement is the entire item]`

**Perceived:** you box-select six riflemen and nothing on screen changes. No bracket, no highlight,
no outline. The only way to know what you have selected is to read the command bar.

**Source:** `WORKSPACE/proposals/260902-safe-wins-and-swings.md` §Tier 1, safe win 9. Filed
2026-09-02.

---

#### What blocks it, exactly

**This cannot be settled by reading, and no screenshots could be taken on the day it was filed.**
It is a `DOCS/recipes/SCREENSHOT.md` task and it needs a capture pass plus a multimodal look at the
result — ideally two shots, brackets on and brackets off, over a dense infantry blob.

**Do not dispatch this as "delete the two lines."** `ShowNever` was almost certainly set on purpose,
and brackets on a dense infantry blob may read as noise. That is a visual judgement, and the
supporting geometry is not encouraging: `Selectable.Bounds` on the same actor
(`infantry.yaml:58-60`) is `500,700,65,-128`, which is not an obviously bracket-friendly box.

**Treat this as "show the user two screenshots", not "turn it on."**

#### Mechanism and citation

`^Infantry` sets `SelectionDecorations: ShowNever: true` (`infantry.yaml:55-56`), and
`SelectionDecorationsBase.cs:109` is literally `if (selected && !Info.ShowNever)`.

**`ShowNever` occurs exactly once anywhere under `mods/`** — re-verified 2026-09-02 in this
worktree, `grep -rn 'ShowNever' mods/` returns the single hit `infantry.yaml:56`. Its engine default
is `false` (`SelectionDecorationsBase.cs:24`), so removing the two lines turns brackets on.

#### Size

Two lines to change; minutes. **The cost is entirely in judging the result**, and that cost is not
optional — shipping it unjudged is how a legibility change becomes a visual regression nobody
reviewed.

#### Related

- `DOCS/recipes/SCREENSHOT.md` — the capture + multimodal evaluation loop this needs.
- Safe win 5 / the half-health readout work notes that the space **above** a unit is already crowded
  (spotted `!`, two stance glyphs, the holding-fire pip — `defaults.yaml:888-904`). Selection
  brackets sit around the unit rather than above it, so the two do not collide directly, but anyone
  judging one should look at the other in the same screenshot.
