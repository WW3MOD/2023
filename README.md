# WW3MOD

A modern total conversion of [OpenRA](https://www.openra.net/), set in an alternate near-future where the Russo-Ukrainian war has escalated into a global conflict. Realism-focused real-time strategy: two superpowers, no base building, suppression that pins infantry, buildings you garrison and fight from, and an economy of reinforcements called in from off-map rather than manufactured.

> **Status:** playable alpha. The first public build is **[v0.1.0](../../releases/latest)** — installers for Windows, macOS and Linux.

## Download & play

Grab an installer from the **[Releases page](../../releases/latest)** and run it.

| Platform | File |
|---|---|
| Windows | `WW3MOD-<version>-x64.exe` — or `-x86` on 32-bit Windows |
| macOS | `WW3MOD-<version>.dmg` |
| Linux | `WW3MOD-<version>-x86_64.AppImage` — `chmod +x`, then run |

The `-winportable.zip` files are the same build without an installer, for running in place.

**You need neither .NET nor a copy of Red Alert.** Each installer bundles its own runtime, and the game still loads artwork and audio from Red Alert's data files, so on first launch it offers a Quick Install that fetches them from a mirror of the 2008 freeware release. If you own the game — disc, Origin, or The First Decade — Advanced Install copies them from your installation instead, which also gets you the music and campaign videos.

The binaries are **not code-signed**. Windows shows a SmartScreen warning (*More info → Run anyway*); macOS refuses a plain double-click, so right-click the app → *Open*.

Multiplayer requires everyone to be on the same version.

## Features

- **Two playable factions** — America and Russia.
- **Reinforcement economy** — no factories, no tech tree. Units are called in from off-map reserves via the **Supply Route**, a fixed beachhead that arrives at the map edge. Enemies who hold ground near your Supply Route contest it: first your production slows, and if they keep it long enough you are out of the war.
- **Suppression system** — 10-tier infantry / 5-tier vehicle suppression. Suppressed infantry go prone; vehicles lose turret traverse and accuracy.
- **Garrisoned buildings** — capture shelters and fortified positions; soldiers fire from directional ports, gain damage protection, and duck under suppression.
- **Helicopter crew system** — pilots, copilots, and gunners. Heavy damage triggers controlled autorotation; critical damage causes uncontrolled crashes. Capture downed helicopters by walking your own pilot in.
- **Vehicle crew** — Driver / Gunner / Commander slots. Crew eject one-by-one on critical damage and can re-enter repaired vehicles.
- **Stance system** — fire discipline, engagement, cohesion, resupply behavior. Per-unit and per-type defaults persist across games.
- **Three-mode movement** — Move (smart self-defense only), Attack-Move (fire at everything), Force-Move (pure travel, never fire).
- **Nuclear weapons** — a graded arsenal from tactical yields up to a Dead Hand retaliatory salvo, with fireball light, fallout and blast scarring scaled to yield.
- **Scenario system** — scripted map variants in Lua, including a "Frontline" co-op mode with garrisons and waves.
- **8 skirmish maps** across snow and temperate tilesets.

## Reporting bugs

Open an [issue](../../issues). Logs live in `Documents\OpenRA\Logs` on Windows and `~/.openra/Logs` elsewhere; attaching them makes a crash far easier to chase.

## Build from source

```bash
# Linux / macOS
make all

# Windows (PowerShell)
./make.ps1 all
```

Then `launch-game.cmd` (Windows) or `./launch-game.sh` (Linux/macOS). Running a source build needs a .NET 8 or later **runtime** on the machine; the packaged installers above do not, because they ship one.

### Prerequisite: a 6.0.4xx .NET SDK

`global.json` pins the SDK to `6.0.428` with `rollForward: latestFeature`. **A newer SDK will not do**:
`latestFeature` cannot roll forward across a major version, so a machine carrying only 8.x or 10.x fails
to build every project with *"A compatible .NET SDK was not found"*. It must be a **6.0.4xx** band — a
6.0.1xx SDK is also rejected.

```powershell
winget install Microsoft.DotNet.SDK.6     # Windows
```

Or download from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/6.0). Installing
side by side is safe and leaves an existing 8.x or 10.x SDK untouched — the pin only decides which SDK
*compiles*, not which runtime executes.

.NET 6 is end-of-life, and depending on it is a deliberate tradeoff, not an oversight: the pin is what
makes CI and a local `make check` run the identical analyzer set, so the code-style gate cannot go red
for reasons unrelated to any commit. The full reasoning is in commit `e4453e6b` — read it before
proposing a bump.

The solution file is `WW3MOD.sln`. The OpenRA engine lives in-repo under `engine/` (forked from `release-20230225`); there is no submodule and `AUTOMATIC_ENGINE_MANAGEMENT` is disabled.

## Contributing

Bug reports, balance feedback, and code contributions are all welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) and [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) before opening an issue or PR.

Design references and engine notes for contributors are in [`DOCS/`](DOCS/); `WORKSPACE/` is the project's day-to-day working area and is written for the people already in it rather than for newcomers.

## Credits

Built on [OpenRA](https://github.com/OpenRA/OpenRA) (`release-20230225`). Inherited and adapted assets from other community OpenRA mods are documented in [CREDITS.md](CREDITS.md).

**Authors:** FreadyFish (lead), CmdrBambi.

## License

Released under the **GNU General Public License v3.0**. See [COPYING](COPYING) for the full text. All contributions are accepted under the same license.
