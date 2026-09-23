# PBOX unmanned-vision gate: blast-radius audit across every scenario that touches a pbox

**Audited 2026-09-22 against `wt/pbox-sight-audit` branched from `origin/main` @ `1d7ea03b`.**
Read-only audit plus comment-only corrections. No scenario was launched; no engine code touched.

## The change under audit, re-verified rather than taken on trust

`70e63582` (wt/neutral-entry, 2026-09-21) added one line to PBOX:

```
mods/ww3mod/rules/ingame/structures-defenses.yaml:215
	Inherits@DetectionWhenLoaded: ^StandardVisionWhenLoaded
```

**The line is at `:215`, not `:214`.** Every live citation in the tree said `:214`
(`test-frozen-tooltip-owner-hidden/rules.yaml:47`, that scenario's `.lua:141`, and
`WORKSPACE/DISCOVERIES.md:101`), and the brief that commissioned this audit repeated it. The
file is byte-identical from `70e63582` to `1d7ea03b` — `git log 70e63582..HEAD --
mods/ww3mod/rules/ingame/structures-defenses.yaml` is empty — so `:215` is correct at every ref
at which the line has ever existed and `:214` was never right. Corrected in all three places.

`^StandardVisionWhenLoaded` (`mods/ww3mod/rules/defaults.yaml:156-177`) inherits
`^StandardVision` (`:115-154`) and re-states all ten `Vision@N` keys carrying nothing but
`RequiresCondition: loaded`, so MiniYaml key collision gates the whole ladder `^Defense`
supplies at `structures-defenses.yaml:5`.

**"Reveals nothing, not even its own cell" is exact, and this was checked rather than assumed.**
The only other vision ladder in PBOX's ancestry is `^BasicBuilding`'s `Vision@3/@2/@1`
(`structures.yaml:14-23`, strength 3 out to 1c0) — all three keys are re-declared by
`^StandardVision` and therefore all three are inside the gated set. Grepping PBOX's own block
(`:204-334`) and `^Defense` (`:2-23`) for `reveal|vision|detect|radar|sight` turns up no second
reveal trait. There is no surviving ungated `Vision@N` and no `RevealsShroud`.

`loaded` is granted by `Cargo.LoadedCondition` (`structures-defenses.yaml:272`), i.e. by a
passenger being in the hold — "manned" below means exactly that.

## The three structural facts that decide almost every row

1. **`Vision` is Ally-only.** `Vision.cs:24` defaults `ValidRelationships` to
   `PlayerRelationship.Ally` and `AddCellsToPlayerMapLayer` enforces it at `:48-50`. Nothing in
   `mods/ww3mod` overrides it. So a pbox owned by a player *other than the viewer* never
   contributed a cell to the viewer's layer, before or after the gate. **20 of the 23 scenarios
   are decided by this line alone.**
2. **Fog off ⇒ vision cannot matter.** With `FogCheckboxEnabled: false` and Explored on,
   `MapLayers.GetVisibility` takes the "explored ⇒ 10" branch and every cell reads 10.
3. **An unmanned pbox cannot shoot, so no AutoTarget premise can rest on its sight.** PBOX's
   only armament is `AttackGarrisoned` (declared in its own block, `:204-334`; `^Defense` carries
   no `Attack*`). With an empty hold there is no firer, so the "AutoTarget needs vision" hazard
   named in the brief is unreachable for an unmanned box. Every test scenario additionally pins
   PBOX to `HoldFire` on both stance fields.

## The table

23 scenarios reference a pbox (the earlier count of 18 was low). `count` is pbox-family actors
placed; the three clone actors (`fogmarker`, `shademarker`, `probebox`) all `Inherits: PBOX` and
so inherited the gate too.

| # | Scenario | Manned in the verdict window? | Depends on pbox sight? | Verdict | Action |
|---|---|---|---|---|---|
| 1 | `test-frozen-tooltip-owner-hidden` | No — USA-owned, ungarrisoned | **YES** — phase 1 waits on USA seeing 8,16 through its own Box | **BROKEN (already fixed)** | none — fixed at `cb8ce077`; only its `:214` cite corrected here |
| 2 | `test-building-visible-at-spawn` | No — `NearBox`/`FarBox` Russia-owned (`map.yaml:59-64`) | No — every read is `Test.GetVisibility(usa,…)` / `Test.IsDetectedBy(…,usa)` (`.lua:105-120`); USA's sight is the Scout at 5,16 | UNAFFECTED | none |
| 3 | `test-unscouted-building-hidden` | No — Russia-owned (`map.yaml:58-64`) | No — same shape, Explored OFF; asserts on an ENEMY pbox | UNAFFECTED | none (its 260922 `fail` is the unrelated `FrozenUnderFog.IsVisible` short-circuit) |
| 4 | `test-supplyroute-exempt-from-fog` | No — `EnemyBox` Russia-owned (`map.yaml:62-64`) | No — USA-side reads only (`.lua:103-108`) | UNAFFECTED | none (260922 `fail` is the same unrelated cause) |
| 5 | `test-frozen-owner-snapshot` | No — `Box` Russia-owned (`map.yaml:96`), reassigned to **Neutral** at `.lua:179`, never to USA | No — USA sees via the Scout then loses it; the Box never served USA's layer at any phase | UNAFFECTED | none |
| 6 | `test-garrison-kill-still-kills` | **Yes** — three riflemen loaded; `Box` starts Neutral (`map.yaml:62`) | No — `FogCheckboxEnabled: false` (`rules.yaml:5`); verdict is `Kill`/`IsDead` (`.lua:65-108`) | UNAFFECTED | none |
| 7 | `test-fog-darkness-ruler` | No — 7 USA-owned `fogmarker`s | No — **all ten `Vision@N` removed** (`rules.yaml:55-64`) | UNAFFECTED | comment: the "load-bearing" justification is now false |
| 8 | `test-minimap-stance-shades` | No — 193 `shademarker`s (Viewer/Enemy1-5/Neutral) | No — all ten removed (`rules.yaml:72-81`); fog off | UNAFFECTED | comment: the cost justification is now historical |
| 9 | `test-evac-refund-indicator` | No — `probebox`, Russia-owned | No — all ten removed; and Russia-owned, so Ally-only applies twice over | UNAFFECTED | comment: premise restated |
| 10 | `test-capture-rules` | n/a — **no pbox actor**; `count=0` | No — two `.lua` comment mentions of `^Defense`'s `-Capturable` | UNAFFECTED | none |
| 11 | `test-garrisoned-emplacement-under-nuke` | n/a — **no pbox actor**; subject is a `gtwr` (`map.yaml:72`) | No — fog off and locked (`rules.yaml:28-29`) | UNAFFECTED | none |
| 12-23 | the 12 demos: `demo-garrison-lineup`, `demo-garrison-panel-full`, `demo-heat-haze`, `demo-highyield-nuke`, `demo-light-events`, `demo-nuke-arsenal`, `demo-nuke-edge-band`, `demo-nuke-fog-seam`, `demo-nuke-perf`, `demo-nuke-perf-single`, `demo-nuke-shroud-still-hides`, `demo-nuke-yield` | mixed (lineup/panel-full garrison a USA box; the rest are Russia/Neutral city filler) | **No — zero visibility reads between them.** `grep GetVisibility\|IsDetectedBy\|IsMouseTargetable\|FrozenActor` over all twelve `.lua` returns nothing | UNAFFECTED (and verdict-free by construction — every `Test.Pass`/`Test.Fail` hit in those files is inside a comment saying there is none) | none |

### On the three fog-enabled demos specifically

`demo-nuke-fog-seam`, `demo-nuke-edge-band` and `demo-nuke-shroud-still-hides` run with fog ON,
so they were the plausible candidates for a changed *picture* even without a verdict. They are
not affected: the viewer is USA in all three, every pbox in them is Russia-owned, and the two
that need lit ground carry a dedicated USA `camera` actor for it (`FogCam` at 58,64 and 39,64).
Russia's pillboxes never lit a USA pixel.

## The enumeration itself is a finding: 18 + 3 + 2 = 23

`grep -lE "^\s+\w+: pbox$" tools/autotest/scenarios/*/map.yaml | wc -l` returns **18** — exactly
the number `cb8ce077` cited, and it is correct as far as it goes. The audit's 23 is the same set
plus two kinds of scenario that grep cannot see:

- **3 scenarios place no literal `pbox` at all and still inherit the gate**, through a
  scenario-local clone declared in their own `rules.yaml`: `fogmarker`
  (`test-fog-darkness-ruler`), `shademarker` (`test-minimap-stance-shades`), `probebox`
  (`test-evac-refund-indicator`), each `Inherits: PBOX`. All three strip the ladder, so all
  three are immune — but **the recommended countermeasure grep would not have told anyone
  that**, and a fourth clone written tomorrow without the removals would be silently gated.
  The rule in `DISCOVERIES.md` should be *grep the map.yamls for the actor AND the rules.yamls
  for `Inherits: <ACTOR>`*.
- **2 scenarios mention a pbox only in prose** (`test-capture-rules`,
  `test-garrisoned-emplacement-under-nuke`) and place none.

## Verdict counts

**UNAFFECTED 22 · VACUOUS-NOW 0 · WRONG-NOW 0 · BROKEN 1 (already fixed at `cb8ce077`).**

No scenario was found passing for the wrong reason, and no scenario was found proving less than
its header claims. `cb8ce077`'s blast-radius claim — that this is one scenario — **holds**, and
now holds against the passing set as well as the failing 39 that worker checked.

Cross-check against the 260922 suite (`~/.ww3mod-tests/screenshots/260922_*/result.json`): the
eleven test scenarios run as `pass ×6` (`building-visible-at-spawn`, `capture-rules`,
`frozen-owner-snapshot`, `garrison-kill-still-kills`, `garrisoned-emplacement-under-nuke`),
`skip ×3` (the three capture instruments — `evac-refund-indicator`, `fog-darkness-ruler`,
`minimap-stance-shades`, all Skip-terminal by design) and `fail ×3`
(`frozen-tooltip-owner-hidden` — this gate; `supplyroute-exempt-from-fog` and
`unscouted-building-hidden` — the unrelated `FrozenUnderFog.IsVisible` cause). Demos are not in
the suite, as expected for verdict-free scenarios. Nothing in that ledger contradicts a row above.

## GTWR and HBOX, as asked

Both were **already gated before** `70e63582` — `git show 70e63582^:…` puts
`Inherits@DetectionWhenLoaded` at `:79` (GTWR) and `:329` (HBOX), so the merge added the third,
PBOX, and changed nothing about the other two. 31 scenarios place a `gtwr` or an `hbox`
(16 demos, 8 tests, 7 tournaments). **Not one of them contains a single `GetVisibility` or
`IsDetectedBy` call**, so none can be assuming an unmanned GTWR/HBOX sees. The two USA-owned
GTWR scenarios (`test-garrison-force-move-eject`, `test-garrison-suppression-readout`) run fog
off; the rest are Russia- or Neutral-owned, i.e. Ally-only again. No action.

## Changes made by this audit

Comment-only. No YAML key, actor, geometry or Lua statement was altered, so no scenario's
behaviour can have changed and no re-run is required to trust any verdict above.

- `:214` → `:215` in the three live citations of the gate line.
- `test-fog-darkness-ruler/rules.yaml`, `test-minimap-stance-shades/rules.yaml`,
  `test-evac-refund-indicator/rules.yaml`: each carried a justification for stripping all ten
  `Vision@N` that rests on "a stock pillbox lights its own 32-cell circle". Since the gate that
  claim is **false for an unmanned box**, which is what all three place. The removals are kept —
  they keep each instrument independent of a gate it does not control — but they are now
  belt-and-braces rather than load-bearing, and each comment now says so. Corrected rather than
  left alone because a reader would otherwise take any of the three as standing evidence for a
  property PBOX no longer has, and carry it somewhere it does damage.

## Noted, NOT fixed — out of this audit's scope

Five files still cite `^StandardVision` as `defaults.yaml:95-133`; it is at `:115-154`
(`cb8ce077` corrected the same drift in the tooltip scenario only):
`test-frozen-owner-snapshot/{map.yaml:50,.lua:147,description.txt:1}`,
`test-fog-darkness-ruler/{map.yaml:49,description.txt:1}`. `test-unscouted-building-hidden/map.yaml:44`
cites `defaults.yaml:80` for the same ladder. This is ordinary line drift, not gate fallout, and
fixing six more files would widen the diff past the audit's remit.
