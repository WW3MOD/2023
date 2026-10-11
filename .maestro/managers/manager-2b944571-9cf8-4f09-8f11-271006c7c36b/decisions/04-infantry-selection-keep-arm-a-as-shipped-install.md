# Infantry selection: keep arm A as shipped; install chain de-OpenRA approved

_Recorded 2026-09-30T01:39:42.656Z by 7a1529e6_

2026-09-30. Two user forks resolved in one turn.

**Infantry selection bracket (audit pkg 4, question 8RXIKGFDjS1mzOQhgaJg0).** Options: ship arm B brackets (delete `SelectionDecorations: ShowNever: true`, infantry.yaml:56-57); keep A and move the selected-pip margin off the class pictogram (infantry.yaml:237); keep A as shipped. **User picked: keep A as shipped, close the item** — note: "I need to do this hands on later but not now." Consequence: no shipped-rules change; branch wt/infantry-selection (86f86980 demo rig + 11b65270 Test.SelectActors one-actor fix) merges as documentation/test-hygiene only. The demo scenario stays as the capture rig for the later hands-on session. Not to be re-raised by an agent; the user owns the timing.

**Windows install chain (audit pkg 2, approval MwjTnTK5wUtW3e7MiM7uF).** User APPROVED the one-way door: new install dir, registry key, Start Menu folder, shortcut name, Product name; existing v0.1.x installs orphaned, accepted pre-release. Scope deliberately excludes the `$APPDATA\OpenRA` support dir (engine Platform.cs:174-184 — user-data migration, not approved), the `openra-<mod>` URL scheme, and binary names. Worker cf5fb977 on wt/installer-identity from main @ 986e9f2e. No makensis on this host, so the installer itself cannot be built here — verification is string readback + build/check/NUnit.
