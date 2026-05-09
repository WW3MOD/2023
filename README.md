# WW3MOD

A modern total conversion of [OpenRA](https://www.openra.net/), set in an alternate near-future where the Russo-Ukrainian war has escalated into a global conflict. Realism-focused real-time strategy with NATO and BRICS factions, suppression mechanics, garrisoned buildings, helicopter operations, and a supply-route reinforcement economy.

> **Status:** Public alpha — playable, working toward 1.0. See [Roadmap](#roadmap).

## Features

- **Two playable factions** — NATO/America and BRICS/Russia. A third (Ukraine) is planned.
- **Reinforcement economy** — no factories. Units are called in from off-map reserves via the **Supply Route** and arrive at the map edge.
- **Suppression system** — 10-tier infantry / 5-tier vehicle suppression. Suppressed infantry go prone; vehicles lose turret traverse and accuracy.
- **Garrisoned buildings** — capture shelters and fortified positions; soldiers fire from directional ports, gain damage protection, and duck under suppression.
- **Helicopter crew system** — pilots, copilots, and gunners. Heavy damage triggers controlled autorotation; critical damage causes uncontrolled crashes. Capture downed helicopters by walking your own pilot in.
- **Vehicle crew** — Driver / Gunner / Commander slots. Crew eject one-by-one on critical damage and can re-enter repaired vehicles.
- **Stance system** — fire discipline, engagement, cohesion, resupply behavior. Per-unit and per-type defaults persist across games.
- **Three-mode movement** — Move (smart self-defense only), Attack-Move (fire at everything), Force-Move (pure travel, never fire).
- **Scenario system** — scripted map variants in Lua, including a "Frontline" co-op mode with garrisons and waves.
- **13 maps** across snow, temperate, and urban tilesets.

## Screenshots

> _Drop 3–6 in-game screenshots in this section. Suggested mix: a wide tactical shot, a close-up firefight, a garrisoned building, and a minimap/strategic view._

## Download & play

Stable installers for Windows, macOS, and Linux are published on the [Releases](../../releases) page.

For the latest development build, clone the repo and run the launcher — it builds the engine on first run and launches the game directly.

| Windows               | Linux / macOS         |
| --------------------- | --------------------- |
| `launch-game.cmd`     | `./launch-game.sh`    |

.NET 8 or later is required.

## Build from source

```bash
# Linux / macOS
make all

# Windows (PowerShell)
./make.ps1 all
```

The solution file is `WW3MOD.sln`. The OpenRA engine lives in-repo under `engine/` (forked from `release-20230225`); there is no submodule and `AUTOMATIC_ENGINE_MANAGEMENT` is disabled.

## Roadmap

Active v1 scope and status are tracked in [`WORKSPACE/RELEASE_V1.md`](WORKSPACE/RELEASE_V1.md). Backlog and design notes live in [`DOCS/`](DOCS/).

## Contributing

Bug reports, balance feedback, and code contributions are all welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) and [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) before opening an issue or PR.

## Credits

Built on [OpenRA](https://github.com/OpenRA/OpenRA) (`release-20230225`). Inherited and adapted assets from other community OpenRA mods are documented in [CREDITS.md](CREDITS.md).

**Authors:** FreadyFish (lead), CmdrBambi.

## License

Released under the **GNU General Public License v3.0**. See [COPYING](COPYING) for the full text. All contributions are accepted under the same license.
