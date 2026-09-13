# Scenarios split in two: a deployment dropdown that works everywhere, and scripted maps that are just maps

_Recorded 2026-09-09T16:59:26.279Z by ffb08fdc_

**Decided by the user, 2026-09-09, after two rounds of design questions.** This supersedes the "finish the lobby scenario system and make scenarios first-class" answer given earlier the same day — the user reached a better split once the machinery was on the table.

## What was decided

The one feature called "Scenario" is actually two unrelated things that were fused because they happened to share a file. They separate cleanly:

**1. Pre-placed units become a plain lobby dropdown, sibling to "Starting units".** It controls how many units spawn and where; placement is automatic; it works on ANY map with no per-map data. This is the "you are a commander called into an ongoing war, your troops are already in position" intent, and it needs no scenario machinery at all.

**2. A "Scenario" becomes a purpose-built MAP with scripts attached.** Not a layer selected in the lobby over an existing map — the scenario IS the map. This is the campaign-mission idea the user actually had: scripted behaviour, garrison units the player does NOT control that are simply there, the player commanding only their own force, and whatever else a mission wants. The user's words: "it could get really creative."

**3. The shellmap becomes a purpose-built map too**, with the scripted behaviour authored directly, rather than a scenario variant of a playable map.

## The consequence that matters

**This is mostly a DELETION.** The per-map `scenarios.yaml` layer, `ScenarioLobbyDropdown`, `map.ScenarioNames` / `ShellmapScenario`, and the three `!= "scenario"` filters all exist to select a scenario layer over a map in the lobby. Under this decision nothing selects a layer over a map, so that whole path goes. The five-month team bug goes with it — `river-zeta-frontline.lua:312-314` sorting humans by `p.Team` stops existing rather than being repaired.

## The DEFCON coupling, which replaces a posture control

Asked how much Frontline is beyond placement (posture? contact already underway?), the user picked nothing and said **"This depends on the DEFCON level, of course"**, and separately that forward deployment "pairs well with DEFCON 3 mode where there is some time to reposition if there are some bad placements."

Read that as: **there is no posture setting to build.** Starting DEFCON level already determines whether shooting is permitted (see decision 05 — hold-fire at DEFCON 2 is a world-level flag read at six sites). Deployment says where your units are; DEFCON says whether the war has started. The two compose, and the grace period at DEFCON 3 is also the mitigation for a derived placement rule putting someone somewhere awkward — which removes the main objection to deriving placement at all.

## Alternatives considered and rejected

- **Finish the lobby scenario system as designed** (the user's own earlier answer). Rejected by the user on reflection: it was "hastily added without too much thought", and finishing it would have meant restoring lobby team-locking to serve a script that should not have been asking about teams.
- **Frontline as a match mode picked per map** (the agent's recommendation, and the user's first-round pick). Superseded: the deployment half needs no mode, and the scripted half needs a map, so "a mode picked per map" was the wrong middle.
- **Authored front-line positions per map.** Rejected implicitly — the user asked for automatic placement controlled by a count. Authored positions survive only as what a purpose-built scenario map does anyway, by being a map.

## Sequencing, which is load-bearing

**Do not delete first.** The main menu background works TODAY through the scenario machinery. Order: build the purpose-built shellmap → build the deployment dropdown → only then remove the lobby scenario layer. Removing the layer while the menu still depends on it breaks the first thing anyone sees.
