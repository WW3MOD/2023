# 001 — Tunguska duplicate `Health:` key

Status: PROPOSED
Source: `260802-parity-audit.md` §2 (strykershorad ↔ tunguska)

## Evidence

The `tunguska` actor node in
`mods/ww3mod/rules/ingame/vehicles-russia.yaml` declares `Health:` **twice**
inside the same actor:

- `vehicles-russia.yaml:786-787` — `Health: HP: 14000`
- `vehicles-russia.yaml:799-800` — `Health: HP: 8000` (13 lines later, after
  `Targetable`/`HitShape`, immediately before `Mobile`)

Verified first-hand (not agent-reported). This is an authoring bug regardless
of which value the engine's MiniYaml merge resolves to: one of the two keys is
dead text that misleads every future reader/tuner. If the later key wins,
Tunguska fields **8000 HP vs its US counterpart strykershorad's 14000**
(`vehicles-america.yaml:841-842`) — a 43% HP deficit on the RU mobile AA
platform — while also costing less (1700, `vehicles-russia.yaml:785` vs 2500,
`vehicles-america.yaml:838`), which the audit flags separately as suspicious
pricing.

Static analysis cannot determine the engine's duplicate-key resolution with
certainty; a combat-sim rules dump (once utility runs are granted) confirms
the effective value in one command.

## Proposed change

`mods/ww3mod/rules/ingame/vehicles-russia.yaml` — delete the second block
(lines 799-800):

```yaml
	Health:
		HP: 8000
```

keeping the first declaration `HP: 14000` (lines 786-787), which matches the
strykershorad counterpart. If the user's intent was actually 8000, delete the
*first* block instead — but then the pricing asymmetry vs strykershorad
(1700/8000 vs 2500/14000) should be re-examined as its own proposal.

## Expected effect

- Zero gameplay change if the engine already resolves duplicates
  first-key-wins; otherwise Tunguska HP rises 8000 → 14000, matching its US
  counterpart.
- `tournament-parity-cross-usru` (+ swapped, 20 seeds each) is the detection
  metric: RU `faction_winrate_pct` should move toward 50% if the effective HP
  was 8000, since mobile AA survivability gates the RU answer to the US
  attack-heli fleet.

## Risk

- Low. Single-key deletion; no other actor references Tunguska HP.
- If the effective value was 8000 and current cross-faction balance has
  silently adapted around a fragile Tunguska, raising it to 14000 buffs RU AA
  — watch the mirror/cross parity runs before and after.
- Regression detection: `test-heli-vs-heli-missile` and the parity tournament
  set; any Tunguska TTK change shows up in cross-run army-value curves.
