# AI-Assisted Development Capabilities for WW3MOD

What an AI assistant (Claude Code) can and cannot do for this project, ordered by feasibility and impact. Each capability is rated for automation potential, effort to set up, and whether it's recommended.

---

## Tier 1: Works Right Now (No Setup Needed)

These capabilities are available today with no additional tooling.

---

### 1.1 C# Engine Development

**What:** Write, modify, debug, and refactor C# engine code. Design new traits, fix bugs, implement features.

**Current state:** Fully operational. This is the primary workflow today.

**Automation level:** High — AI writes code, builds, runs tests. Human verifies in-game.

**Recommendation:** Already the core workflow. Keep doing this.

---

### 1.2 YAML Unit/Weapon/Rules Authoring

**What:** Create and balance units, weapons, warheads, AI configs, suppression values — all YAML-based.

**Current state:** Fully operational. AI can read existing YAML, understand templates/inheritance, and produce correct YAML.

**Automation level:** High — AI writes YAML, human playtests balance.

**Downsides:** Balance tuning requires human feel. AI can propose numbers but can't judge "does this feel fun."

**Recommendation:** Already working well. AI is especially good at bulk operations (e.g., "add vehicle suppression to all vehicles" or "rebalance all infantry costs proportionally").

---

### 1.3 Lua Map Scripting

**What:** Write campaign mission scripts, shellmap animations, trigger logic for custom maps.

**Current state:** Fully operational. OpenRA uses a well-documented Lua API (`Trigger`, `Reinforcements`, `Camera`, `Media`, `Actor` methods). The project already has examples (`shellmap-ww3-ivanivske.lua`, `campaign.lua`).

**Automation level:** High — AI can write complete Lua scripts from a description like "NATO reinforcements arrive from the east at minute 3, Russia counterattacks from north at minute 5."

**Downsides:** Testing requires launching the specific map. No unit tests for Lua scripts.

**Recommendation:** Strongly recommended for campaign development. AI can produce mission scripts much faster than manual authoring.

**Example prompt:** "Write a Lua script for a mission where the player defends a bridge for 10 minutes against waves of increasing difficulty, with allied reinforcements arriving at minutes 3 and 7."

---

### 1.4 YAML Sequence Definitions

**What:** Define sprite animation sequences (facings, frame ranges, offsets, ticks) in the `sequences/*.yaml` files.

**Current state:** Fully operational. AI can read existing sequence files and produce new ones following the same patterns.

**Automation level:** High — given a sprite sheet specification (frame count, facings, layout), AI can write the sequence YAML.

**Downsides:** AI cannot see the actual sprites, so it needs to be told the frame layout (or reference an existing similar unit). If a sprite has 8 facings with 6 walk frames starting at frame 16, the human needs to provide that info (or AI can copy from a similar unit).

**Recommendation:** Good for bulk work. If you add new infantry sprites that follow the same layout as e1, AI can instantly produce the sequence file.

---

### 1.5 Documentation & Planning

**What:** Write docs, architecture guides, TODO lists, assess project state, plan features.

**Current state:** Fully operational. See CLAUDE.md, COLLABORATION.md, PROJECT_ASSESSMENT.md.

**Automation level:** Complete.

**Recommendation:** Let AI maintain all documentation. It's already doing this.

---

### 1.6 AI Configuration (Bot YAML)

**What:** Design and tune AI opponents — build orders, squad composition, attack timing, capture priorities.

**Current state:** Fully operational. AI config is pure YAML in `rules/ai/`.

**Automation level:** High — AI can design new AI personalities, adjust build priorities, create faction-specific strategies.

**Downsides:** Like balance tuning, needs playtesting. But AI can produce many variants quickly for A/B testing.

**Recommendation:** Recommended. "Create an aggressive rush AI for Russia that prioritizes early infantry pressure" is a single-prompt task.

---

### 1.7 Unit Tests & Regression Tests

**What:** Write NUnit tests for engine math, formulas, and logic.

**Current state:** Just set up (136 tests passing). New WW3MOD-specific tests for AmmoPool, SupplyProvider, and Suppression.

**Automation level:** High — AI writes tests after each feature or bugfix.

**Recommendation:** Growing the test suite with each session prevents regressions. AI should write a test for every bug it fixes.

---

## Tier 2: Feasible with Moderate Setup (Days of Work)

These require building some tooling but are achievable and high-value.

---

### 2.1 Map Creation via MCP Server

**What:** Create and edit OpenRA maps programmatically — terrain tiles, actor placement, spawn points, map metadata.

**How it works:** Build a custom MCP (Model Context Protocol) server that exposes map editing tools to Claude. MCP servers run as subprocesses and give Claude new capabilities beyond file editing.

**Architecture:**
```
Claude Code
    ↓ (stdio / MCP protocol)
ww3mod-map-server (Python or Node.js)
    ↓
Reads/writes map.bin (binary tile data)
Reads/writes map.yaml (metadata, actors, players)
Reads/writes map.png (preview thumbnail)
Generates rules.yaml (map-specific rule overrides)
Writes .lua scripts (map triggers)
```

**MCP tools the server would expose:**
```
create_map(name, width, height, tileset)     — scaffold a new map directory
get_tileset_info(tileset)                     — list available tiles for SNOW/TEMPERAT/etc.
set_terrain_region(x, y, w, h, tile, variation) — paint terrain
place_actor(x, y, type, owner, facing)       — place units/structures
add_player(name, faction, playable, enemies) — configure player slots
set_bounds(x1, y1, x2, y2)                  — set playable area
generate_preview()                           — render map.png
validate_map()                               — check for common errors
export_map()                                 — write all files
read_map(path)                               — load existing map for editing
```

**Effort:** 3-5 sessions to build. The hardest part is parsing/writing `map.bin` (binary tile format). The YAML and Lua parts are trivial.

**Partial automation (easier):** Skip binary tile editing. Just generate `map.yaml` (actors, players, metadata) and `rules.yaml` (weather, lighting). Human uses the in-game map editor for terrain, AI handles everything else.

**Full automation:** Parse the binary tile format (documented in OpenRA source: `Map.cs`, `MapGrid.cs`). Then Claude can design complete maps from a text description.

**Setup steps:**
1. Create a Node.js or Python project implementing the MCP server spec
2. Reverse-engineer `map.bin` format from OpenRA's `Map.cs` load/save code
3. Implement the tool handlers
4. Add `.mcp.json` to the project root:
```json
{
  "mcpServers": {
    "ww3mod-maps": {
      "command": "node",
      "args": ["tools/map-server/index.js"],
      "env": {}
    }
  }
}
```

**Recommendation:** HIGH VALUE. Map creation is one of the biggest bottlenecks in mod development. Even partial automation (metadata + actors + Lua, human does terrain) would save huge amounts of time. For campaign development this becomes essential.

**Downsides:** Binary format parsing requires careful implementation. Maps need playtesting for balance (spawn symmetry, resource placement, chokepoints). AI can't judge "does this map look good" without seeing it.

---

### 2.2 Campaign System (Maps + Scripting + Briefings)

**What:** Design and build a full single-player campaign — mission briefings, scripted events, difficulty scaling, story progression.

**Current state:** The Lua scripting infrastructure exists. `campaign.lua` has basic scaffolding. No missions exist yet.

**What AI can do today (no extra tooling):**
- Write complete Lua mission scripts with triggers, reinforcements, objectives
- Design mission flow (briefing text, objective sequences, win/lose conditions)
- Write map-specific `rules.yaml` for each mission (disable certain units, set starting resources, etc.)
- Create AI opponent behavior per mission

**What needs the MCP map server (Tier 2.1):**
- Creating the actual map terrain for each mission
- Placing pre-built units and structures

**Effort:** Lua scripts + mission design: 1-2 sessions per mission. Map creation adds 1-2 more if using MCP tools.

**Recommendation:** Start with scripting missions on existing maps (repurpose skirmish maps as campaign maps with Lua triggers). This requires zero extra tooling. Build the MCP map server later when you want custom campaign maps.

**Example workflow:**
```
Human: "Mission 1: Defend Ivanivske. Player starts with a base in the south.
        Russia attacks in 3 waves. Allied reinforcements arrive by helicopter
        after wave 2. Win by surviving 15 minutes."

AI: Writes complete Lua script, map rules.yaml, briefing text, difficulty
    scaling (Easy: 2 waves, Normal: 3, Hard: 4 with stronger units).
```

---

### 2.3 Chrome/UI Layout Authoring

**What:** Design and modify UI layouts — sidebar panels, selection boxes, lobby screens. OpenRA UI is defined in `chrome/*.yaml` files.

**Current state:** AI can already edit chrome YAML. But UI layout is trial-and-error without visual feedback.

**MCP enhancement:** Build a simple MCP tool that renders a chrome layout to a PNG preview (using OpenRA's rendering code or a simplified mock). Then AI could iterate on layouts with visual feedback.

**Effort:** Chrome YAML editing works now (0 effort). Visual preview MCP: 2-3 sessions.

**Recommendation:** Low priority. The chrome YAML is editable now. Visual preview would be nice but isn't blocking.

---

### 2.4 Balance Spreadsheet / Simulation

**What:** Build a tool that simulates combat outcomes (unit A vs unit B at distance X) without launching the game.

**Architecture:** A standalone C# program or MCP server that loads YAML rules and simulates:
- DPS calculations (weapon damage × burst ÷ reload time, with armor modifiers)
- Time-to-kill matrices
- Cost-effectiveness ratios
- Suppression buildup/decay curves

**Effort:** 3-5 sessions for a useful simulator. Doesn't need to be pixel-perfect — approximate DPS/TTK is enough for balance work.

**Recommendation:** Medium priority. Very useful once you're in the balance-tuning phase. Currently balance is done by feel + playtesting. A simulator would let AI propose and validate balance changes mathematically before you even launch the game.

**Example prompt:** "The T-72 kills infantry too fast. Show me the TTK matrix for all MBTs vs infantry, then adjust the T-72's burst and damage so its TTK matches the M1 Abrams."

---

## Tier 3: Feasible with Significant Effort (Weeks of Work)

These are technically possible but require substantial tooling investment.

---

### 3.1 Sprite/Art Generation with AI Image Models

**What:** Generate unit sprites, building graphics, explosion effects, terrain tiles using AI image generation (DALL-E, Midjourney, Stable Diffusion).

**The challenge:** OpenRA sprites are very specific:
- **Format:** Westwood `.shp` (palette-indexed, 8-bit, specific palette)
- **Layout:** Precise frame grids (8/16/32 facings × N animation frames)
- **Style:** Isometric perspective, consistent scale, palette-matched
- **Size:** Tiny (often 30-60px per frame)

**What's realistic today:**
1. AI generates concept art / reference images for new units
2. Human (or specialized pixel art AI) creates actual sprites from the concept
3. AI writes the sequence YAML to integrate the sprites

**What would need tooling:**
- A pipeline that takes AI-generated images and converts them to `.shp` format
- Palette quantization (reduce to the OpenRA palette)
- Frame sheet splitting (cut a generated sprite sheet into individual frames)
- An MCP server wrapping this pipeline so Claude can invoke it

**The `.shp` conversion problem:** OpenRA uses Westwood's proprietary sprite format. You'd need:
1. Generate PNG sprite sheet (AI image model)
2. Quantize to OpenRA palette (ImageMagick or custom code)
3. Pack into `.shp` format (OpenRA has export tools, or write a custom packer)

**Effort:** 5-10 sessions for a basic pipeline. Quality will be inconsistent — AI image models struggle with pixel art, isometric perspective, and consistent style across frames.

**Recommendation:** LOW priority for now. The art pipeline is the hardest part to automate. Better to:
- Source sprites from other OpenRA mods (see `DOCS/SPRITE_REFERENCES.md`)
- Commission pixel artists for critical units
- Use AI for concept art / reference only

**Partial win (low effort):** AI can generate map preview thumbnails (`map.png`), UI icons, and briefing screen backgrounds. These are standard PNG files with no format constraints.

---

### 3.2 Sound Design / Audio Generation

**What:** Generate unit voice lines, weapon sounds, ambient audio using AI audio models.

**Format requirements:**
- `.wav` or `.aud` (Westwood audio format)
- Short clips (0.5-3 seconds for weapon sounds, 1-5 seconds for voice lines)
- Consistent style/quality

**What's realistic:**
- AI text-to-speech for unit voice responses ("Moving out", "Target acquired")
- AI sound generation for weapon effects (experimental, quality varies)
- AI music generation for menu/mission background music

**Effort:** Low for voice lines (TTS APIs exist). Medium for sound effects. The `.aud` format would need a converter (or just use `.wav`).

**Recommendation:** LOW priority but easy win for voice lines. A TTS MCP server that generates `.wav` files from text prompts could produce all unit voices in a few sessions.

---

### 3.3 Automated Playtesting via Headless Game

**What:** Run the game headlessly (no graphics) with AI-controlled players to test balance, find crashes, and validate changes without human interaction.

**How:** OpenRA supports headless dedicated servers. You could:
1. Launch a headless game with two AI players
2. Log combat statistics (kills, deaths, resource income, unit composition over time)
3. Detect crashes automatically
4. Run many games in parallel to find balance issues statistically

**Effort:** 5-8 sessions. Requires understanding OpenRA's dedicated server mode, adding statistical logging, and building a harness to run and analyze games.

**Recommendation:** HIGH VALUE but high effort. This is the holy grail of game balance — automated A/B testing. Would let you test balance changes across hundreds of games overnight. But it's a large investment.

**Partial win:** Just use headless mode for crash detection. Run 10 AI-vs-AI games after each engine change. If any crash, investigate. Much easier than full statistical analysis.

---

### 3.4 Tileset / Terrain Art

**What:** Create new terrain tilesets (e.g., urban, jungle, desert variants).

**The challenge:** Tilesets are complex:
- Each tileset has hundreds of tile variations
- Tiles must seamlessly connect (edges match)
- Isometric perspective must be consistent
- Format is `.tem`/`.sno`/`.des` (tileset-specific extensions of the `.tmp` format)

**Recommendation:** NOT recommended for AI automation. Tileset creation is extremely specialized pixel art work. Use existing tilesets or source from other mods.

---

## Tier 4: Research / Experimental

These are forward-looking ideas that may become practical as AI tooling improves.

---

### 4.1 Live Game State Observation

**What:** Connect Claude to a running game instance to observe state in real-time — see unit positions, health, ammo, combat events. Like a "coach" watching over your shoulder.

**How:** An MCP server that reads OpenRA's debug/replay data or connects to the game's network protocol.

**Use case:** "Why did my infantry get wiped?" → AI analyzes the game log/replay and explains what happened tactically.

**Effort:** High (8-15 sessions). Would need to tap into OpenRA's replay/observation system.

**Recommendation:** Cool but not practical yet. Better to just describe what happened and let AI investigate the code.

---

### 4.2 Replay Analysis

**What:** Parse `.orarep` replay files and analyze player strategy, unit effectiveness, combat outcomes.

**How:** OpenRA replays contain all player orders. A parser could reconstruct the game and produce statistics.

**Effort:** Medium (3-5 sessions to parse orders, 5-10 for full analysis).

**Recommendation:** Useful for competitive balance but not a priority for WW3MOD's current state.

---

### 4.3 Procedural Map Generation

**What:** Generate maps algorithmically — Perlin noise for terrain, constraint-based placement for resources and spawn points, symmetry enforcement.

**How:** Build on top of the MCP map server (Tier 2.1) with procedural generation algorithms.

**Use case:** "Generate 5 balanced 2-player maps with snow terrain, each having 2 bridges as chokepoints."

**Effort:** 3-5 sessions on top of the map MCP server.

**Recommendation:** Fun and useful for variety, but quality will lag behind hand-crafted maps. Good for generating rough drafts that humans refine.

---

## Priority Recommendation

If you want to maximize AI leverage, here's the order I'd invest in:

| Priority | Capability | Effort | Impact |
|----------|-----------|--------|--------|
| 1 | Campaign Lua scripting (on existing maps) | 0 sessions | High — enables campaign mode |
| 2 | Balance simulation spreadsheet | 3-5 sessions | High — data-driven balance |
| 3 | MCP map server (partial: YAML + actors) | 2-3 sessions | High — faster map iteration |
| 4 | MCP map server (full: binary tiles) | 3-5 sessions | Very high — full map creation |
| 5 | TTS voice line generation | 1-2 sessions | Medium — fills major audio gap |
| 6 | Headless crash testing | 2-3 sessions | Medium — automated QA |
| 7 | AI art concept generation pipeline | 5-10 sessions | Low — inconsistent quality |
| 8 | Full headless balance testing | 5-8 sessions | High but expensive |

The biggest bang-for-buck is **campaign scripting** (free, just Lua) and the **MCP map server** (moderate effort, unlocks map creation and campaign maps).

---

## How to Get Started

**Today (zero setup):**
- "Write a Lua mission script for [description]"
- "Design an AI personality that [behavior]"
- "Rebalance all infantry costs so [criteria]"

**This week (MCP map server):**
1. AI creates the MCP server scaffolding (Node.js + MCP SDK)
2. Implement YAML/actor tools first (skip binary tiles)
3. Add `.mcp.json` to project
4. Claude can now place actors, configure players, write map metadata
5. Human does terrain in the in-game editor, AI does everything else

**Later (full automation):**
- Add binary tile parsing to the MCP server
- Build the balance simulator
- Add TTS voice generation
