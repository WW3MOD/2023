### 88. In-tree yaml line citations in engine/, mods/, tools/ comments have drifted (≥112 sites)

`[LOW VALUE PER SITE — batch with the next worker touching each area; never a standalone pass]`

**Source:** the DOCS/ citation sweep 7a6d30a4 (2026-09-05) found 112 `structures/aircraft/vehicles.yaml:<line>` citations outside its scope: 26 under `engine/`, 26 under `mods/`, 60 under `tools/`; 5/5 spot-checked wrong (e.g. `vehicles.yaml:608` cites its own file's `PauseOnCondition` as `:38`, now `:43`; `engine/OpenRA.Test/.../TransitOnlyServiceHostTest.cs:108` cites Reservables at `:513/:588`, now `:713/:788`, in a comment whose reasoning is superseded by `ResupplyDock`'s explicit vacate). Filed at `main @ e8e57ada` from backlog item 5509c41b.

Comment-only edits. Engine comment changes need a build check; YAML comment edits go through the gate. Prefer citing the KEY over the line where the comment allows it (the convention the 2026-09-06 scenario work adopted).
