# Command bar icon map — which buttons share art, and how many icons are missing

**Ref: `main @ 1160a531` (worktree `wt/identity-panel`), 2026-09-21.** Derived mechanically by
resolving every `Button@` in `mods/ww3mod/chrome/ingame-player.yaml` that draws from the command-bar
icon family through `mods/ww3mod/chrome.yaml`'s collection inheritance to its **sheet rectangle on
`uibits/glyphs.png`**. No build, no launch, no screenshot. The script is reproduced in §5 so the
count can be retaken in one command rather than re-argued.

---

## 0. Why this file exists

`WORKSPACE/PIPELINE.md:95` names "the duplicate-map table (19 of 25 buttons share art across 11
sprites; **14 new icons needed**)" as U4's deliverable. The 2026-09-21 release-readiness audit
(§1.4, `## Watch`) could not find it and flagged that it might never have been written.

**It was written.** It is [`audit/260816-command-bar-research.md` §3](260816-command-bar-research.md),
not `260816-content-completeness.md` — which is the file the audit grepped, and the only reason the
deliverable read as missing. That document is still the better read for **format and capacity**
(`glyphs.png` is 256×256 straight RGBA, no palette, no SHP step — §3 "Where the art actually lives").

**But its counts have drifted, and this file supersedes them.** The command bar has changed since
2026-08-16: a button was removed and the resupply bar was rehoused in its own collection. The
headline figures PIPELINE.md quotes are no longer what the tree contains.

| | 2026-08-16 | **2026-09-21** |
|---|---|---|
| Buttons in the command-bar icon family | 25 | **24** |
| Distinct sprite rectangles behind them | 11 | **11** |
| Buttons sharing art with at least one other | 19 | **20** |
| Unique-art buttons | 6 | **4** |
| **New icons to make all buttons distinct** | **14** | **13** |

**Do not quote either row as standing fact.** Both are dated readings of a file that is edited; §5
retakes them in one command.

---

## 1. What changed since 2026-08-16, and why the shape is the same

Two edits moved the numbers, and **neither reduced the duplication** — one removed a button, the
other moved three buttons from one shared rectangle to another:

1. **`@TAKE_COVER` no longer exists.** Zero hits for `TAKE_COVER` under `mods/ww3mod/chrome/` at this
   ref. It drew `command-icons/deploy`, so `deploy`'s user count went 3 → 2 by deletion.
2. **The resupply bar got its own collection, `resupply-icons` (`chrome.yaml:319-326`).** Its three
   buttons used to draw 16×16 `stance-icons` art; they now draw 24×24 `command-icons` art under new
   names. That took each of the three `stance-icons` rectangles from 4 users down to 3 — and added a
   second user to `deploy`, `stop` and `force-move`. **Net zero.** `deploy` is back to 3.

The reason to record this rather than just print the new table: **the new collection names look like
de-duplication and are not.** `command-mode-icons` (`chrome.yaml:298`) and `resupply-icons` both
`Inherits:` art that already existed, and `resupply-icons`' three regions point at `command-icons`
coordinates. A reader who counts *collections* or *`ImageName:` values* will conclude the bar has
more distinct art than it does. **The only thing that decides whether two buttons look alike is the
sheet rectangle**, which is what this table resolves to.

**One genuine improvement did land, and it is worth not undoing.** Both new collections exist to give
*active* states distinct art: `command-mode-icons-highlighted` (`:303-310`) and
`resupply-icons-highlighted` (`:328-332`) point at **8 real amber recolours** in the previously-empty
band at `y=182` and at the right-hand end of the command rows. So eight of these buttons already
differ from their twins **when engaged**. The duplication below is of the **resting** art, which is
what a player sees for almost the whole match.

---

## 2. The duplicate map

Ordered by how many buttons share the rectangle. `x,y` are pixel coordinates on
`mods/ww3mod/uibits/glyphs.png` (256×256; `-2x`/`-3x` are the same layout at 512/1024).

| Sprite rectangle | x,y | Size | Buttons drawing it | Count |
|---|---|---|---|---|
| `stance-icons/defend` | 17,119 | 16² | `EVACUATE`, `STANCE_AMBUSH`, `ENGAGEMENT_DEFENSIVE`, `COHESION_LOOSE` | **4** |
| `stance-icons/attack-anything` | 0,119 | 16² | `STANCE_FIREATWILL`, `ENGAGEMENT_HUNT`, `COHESION_TIGHT` | **3** |
| `stance-icons/hold-fire` | 51,119 | 16² | `STANCE_HOLDFIRE`, `ENGAGEMENT_HOLDPOSITION`, `COHESION_SPREAD` | **3** |
| `command-*-icons/guard` | 75,207 | 24² | `GUARD`, `PATROL`, `AUTO_ENTER` | **3** |
| `command-icons/deploy` | 100,207 | 24² | `DEPLOY`, `RESUPPLY`, `RESUPPLY_AUTO` | **3** |
| `command-*-icons/force-move` | 25,207 | 24² | `FORCE_MOVE`, `RESUPPLY_EVACUATE` | **2** |
| `command-icons/stop` | 150,207 | 24² | `STOP`, `RESUPPLY_HOLD` | **2** |
| `command-mode-icons/attack-move` | 0,207 | 24² | `ATTACK_MOVE` | 1 ✓ |
| `command-mode-icons/force-attack` | 50,207 | 24² | `FORCE_ATTACK` | 1 ✓ |
| `command-icons/scatter` | 125,207 | 24² | `SCATTER` | 1 ✓ |
| `command-mode-icons/queue-orders` | 175,207 | 24² | `QUEUE_ORDERS` | 1 ✓ |

**24 buttons, 11 rectangles. 4 buttons have art of their own; 20 share. 13 new icons make all 24
distinct** (24 − 11 = 13).

### Per-button, with line cites

| Button | `ingame-player.yaml` | Collection / name | Rectangle |
|---|---|---|---|
| `ATTACK_MOVE` | `:313` | `command-mode-icons/attack-move` | 0,207 24² |
| `FORCE_MOVE` | `:332` | `command-mode-icons/force-move` | 25,207 24² |
| `FORCE_ATTACK` | `:353` | `command-mode-icons/force-attack` | 50,207 24² |
| `GUARD` | `:374` | `command-mode-icons/guard` | 75,207 24² |
| `DEPLOY` | `:393` | `command-icons/deploy` | 100,207 24² |
| `RESUPPLY` | `:413` | `command-icons/resupply` | 100,207 24² |
| `PATROL` | `:433` | `command-mode-icons/guard` | 75,207 24² |
| `AUTO_ENTER` | `:453` | `command-icons/guard` | 75,207 24² |
| `SCATTER` | `:473` | `command-icons/scatter` | 125,207 24² |
| `STOP` | `:493` | `command-icons/stop` | 150,207 24² |
| `QUEUE_ORDERS` | `:513` | `command-mode-icons/queue-orders` | 175,207 24² |
| `EVACUATE` | `:534` | `stance-icons/defend` | 17,119 **16²** |
| `STANCE_FIREATWILL` | `:561` | `stance-icons/attack-anything` | 0,119 16² |
| `STANCE_AMBUSH` | `:581` | `stance-icons/defend` | 17,119 16² |
| `STANCE_HOLDFIRE` | `:602` | `stance-icons/hold-fire` | 51,119 16² |
| `ENGAGEMENT_HUNT` | `:630` | `stance-icons/attack-anything` | 0,119 16² |
| `ENGAGEMENT_DEFENSIVE` | `:650` | `stance-icons/defend` | 17,119 16² |
| `ENGAGEMENT_HOLDPOSITION` | `:671` | `stance-icons/hold-fire` | 51,119 16² |
| `COHESION_TIGHT` | `:699` | `stance-icons/attack-anything` | 0,119 16² |
| `COHESION_LOOSE` | `:719` | `stance-icons/defend` | 17,119 16² |
| `COHESION_SPREAD` | `:740` | `stance-icons/hold-fire` | 51,119 16² |
| `RESUPPLY_HOLD` | `:768` | `resupply-icons/hold` | 150,207 24² |
| `RESUPPLY_AUTO` | `:788` | `resupply-icons/auto` | 100,207 24² |
| `RESUPPLY_EVACUATE` | `:809` | `resupply-icons/evacuate` | 25,207 24² |

---

## 3. The 13 icons, as a shopping list

Each row is one button that currently borrows another's art. **Only the first column needs drawing** —
the rest of the bar keeps what it has. All at **24 × 24 RGBA** (see §4 on why `EVACUATE` too).

| # | Icon to draw | For | Currently borrows | Meaning to convey |
|---|---|---|---|---|
| 1 | `patrol` | `PATROL` | `guard` | move a repeating circuit between waypoints |
| 2 | `auto-enter` | `AUTO_ENTER` | `guard` | board the nearest transport / garrison by itself |
| 3 | `resupply` | `RESUPPLY` | `deploy` | go and take on ammo and fuel (already `# TODO` at `chrome.yaml:276`) |
| 4 | `evacuate` | `EVACUATE` | `stance-icons/defend` | leave the field — the command-bar action |
| 5 | `stance-ambush` | `STANCE_AMBUSH` | `defend` | hold fire until something comes close |
| 6 | `engage-hunt` | `ENGAGEMENT_HUNT` | `attack-anything` | seek targets out |
| 7 | `engage-defensive` | `ENGAGEMENT_DEFENSIVE` | `defend` | fire back, do not pursue |
| 8 | `engage-hold` | `ENGAGEMENT_HOLDPOSITION` | `hold-fire` | do not move to engage |
| 9 | `cohesion-tight` | `COHESION_TIGHT` | `attack-anything` | keep the group close together |
| 10 | `cohesion-loose` | `COHESION_LOOSE` | `defend` | keep the group at medium spacing |
| 11 | `cohesion-spread` | `COHESION_SPREAD` | `hold-fire` | scatter the group wide |
| 12 | `resupply-hold` | `RESUPPLY_HOLD` | `stop` | never leave to resupply |
| 13 | `resupply-evacuate` | `RESUPPLY_EVACUATE` | `force-move` | withdraw when out of supply |

**Cheapest single win, and it needs no art at all:** `stance-icons/return-fire` (34,119, 16²) is
declared with all three states (`chrome.yaml:248-250`) and **used by no button** — verified at this
ref. Pointing one of rows 5–11 at it de-duplicates a button for one line of YAML.

---

## 4. Two things to get right when the art is drawn

1. **`EVACUATE` is off-grid, independent of duplication.** It is the only `COMMAND_BAR` button that
   pulls from `stance-icons`, so it draws **16×16 at offset (9,5)** while its eleven neighbours draw
   **24×24 at (5,1)**. Even with unique art it will read as visibly smaller and misaligned until it
   moves to a 24×24 `command-icons` region and its `Image@ICON` gets `X: 5`, `Y: 1`.
2. **`glyphs.png` is effectively full — draw onto a new sheet.** The 08-16 pass measured room for
   about two more 24² cells in the command band; the resupply recolours have since taken most of it,
   and `chrome.yaml:318` records that **one** 24² cell is now free, at `225,232`. 13 icons plus their
   `-disabled` twins do not fit. The clean route is unchanged and is additive: a **new collection with
   its own `Image:`** (e.g. `ww3-command-icons.png`), which touches none of the 152 existing regions.
   Format is friendly — straight RGBA PNG, no palette, no SHP step.

---

## 5. Retaking the count

No build. Resolves collection inheritance, so it sees through `command-mode-icons` and
`resupply-icons` to the real rectangles.

```bash
python3 - <<'PY'
import io, re
from collections import defaultdict
lines = io.open("mods/ww3mod/chrome/ingame-player.yaml", encoding="utf-8").read().split("\n")
buttons=[]; cur=None
for i,l in enumerate(lines):
    m=re.match(r'^\t+Button@([A-Z_0-9]+):', l)
    if m: cur={"name":m.group(1),"line":i+1,"coll":None,"img":None}; buttons.append(cur)
    if cur is not None:
        c=re.match(r'^\t+ImageCollection: (\S+)',l)
        if c and cur["coll"] is None: cur["coll"]=c.group(1)
        n=re.match(r'^\t+ImageName: (\S+)',l)
        if n and cur["img"] is None: cur["img"]=n.group(1)
ch=io.open("mods/ww3mod/chrome.yaml",encoding="utf-8").read().split("\n")
coll=None; regions={}; inherits={}
for l in ch:
    m=re.match(r'^([a-zA-Z^][\w^-]*):\s*$',l)
    if m: coll=m.group(1); regions.setdefault(coll,{}); continue
    m=re.match(r'^\tInherits: (\S+)',l)
    if m and coll: inherits[coll]=m.group(1); continue
    m=re.match(r'^\t\t([\w-]+): (\d+), (\d+), (\d+), (\d+)',l)
    if m and coll: regions[coll][m.group(1)]=tuple(int(x) for x in m.groups()[1:])
def rect(c,n):
    seen=set()
    while c and c not in seen:
        seen.add(c)
        if n in regions.get(c,{}): return regions[c][n]
        c=inherits.get(c)
FAM={"command-icons","command-mode-icons","stance-icons","resupply-icons"}
rows=[(b["name"],b["coll"],b["img"],rect(b["coll"],b["img"]),b["line"]) for b in buttons
      if b["coll"] in FAM and b["img"]]
byrect=defaultdict(list)
for n,c,i,r,ln in rows: byrect[r].append(n)
uniq=sum(1 for v in byrect.values() if len(v)==1)
print(f"BUTTONS={len(rows)} RECTS={len(byrect)} UNIQUE={uniq} SHARING={len(rows)-uniq} NEEDED={len(rows)-len(byrect)}")
for r,v in sorted(byrect.items(), key=lambda kv:-len(kv[1])): print(len(v), r, sorted(v))
PY
```

At `1160a531` this prints `BUTTONS=24 RECTS=11 UNIQUE=4 SHARING=20 NEEDED=13`.

---

## Watch

- **Nothing here was looked at.** Every claim is a coordinate resolved out of YAML. Two buttons
  pointing at the same rectangle certainly render the same pixels, so the duplication is sound — but
  whether a given icon *reads* as its command is a judgement no parser makes, and the "meaning to
  convey" column in §3 is my reading of the button names, not a design ruling.
- **The 8 amber `-highlighted` recolours are asserted from `chrome.yaml` coordinates, not compared.**
  They point at distinct rectangles in the `y=182` band and at `200,207`/`225,207`/`200,232`. I did
  not decode the PNG to confirm those rectangles contain anything other than transparency. If the
  band is empty, eight buttons lose their glyph when engaged — a different and worse defect than
  duplication, and it would be invisible to this analysis.
- **Scope is the command-bar icon family only.** Buttons drawing `order-icons`, `production-icons`,
  `sidebar-bits` and `music` are excluded by construction, and the 11 garrison/cargo `EJECT_*`
  buttons (R6) carry no icon at all. None of those is counted above.
- **`command-icons/resupply` still carries the previous author's `# TODO`** at `chrome.yaml:276-277`
  — byte-identical coordinates to `deploy`. That marker is the only in-tree acknowledgement of any of
  this and should not be removed until row 3 of §3 is drawn.
