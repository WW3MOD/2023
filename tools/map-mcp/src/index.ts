#!/usr/bin/env node
/**
 * WW3MOD Map Creation MCP Server
 *
 * Provides tools for creating and editing OpenRA maps for WW3MOD.
 * Handles binary map.bin format, MiniYaml map.yaml, tileset parsing,
 * actor placement, player configuration, rules overrides, and Lua scripts.
 */

import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { z } from 'zod';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { PNG } from 'pngjs';

import { readMapBin, writeMapBin, createEmptyMapBin, type MapBinData } from './map-bin.js';
import { readMapYaml, writeMapYaml, writeRulesYaml, type MapYamlData, type PlayerReference, type MapActor } from './map-yaml.js';
import { parseTileset, findTemplateForTerrainType, hexToRgb, type TilesetData, type TilesetTemplate } from './tileset.js';

// ── Paths ──────────────────────────────────────────────────────────────────

// Resolve mod root — the server expects to be run from the WW3MOD project root
// or to find it via the tools/map-mcp location
const SCRIPT_DIR = path.dirname(new URL(import.meta.url).pathname);
// Handle Windows paths: /C:/... → C:/...
const fixedScriptDir = process.platform === 'win32' && SCRIPT_DIR.startsWith('/')
	? SCRIPT_DIR.substring(1)
	: SCRIPT_DIR;

const MOD_ROOT = path.resolve(fixedScriptDir, '..', '..', '..');
const MAPS_DIR = path.join(MOD_ROOT, 'mods', 'ww3mod', 'maps');
const TILESETS_DIR = path.join(MOD_ROOT, 'mods', 'ww3mod', 'tilesets');
const RULES_DIR = path.join(MOD_ROOT, 'mods', 'ww3mod', 'rules', 'ingame');

// ── Tileset Cache ──────────────────────────────────────────────────────────

const tilesetCache = new Map<string, TilesetData>();

function loadTileset(tilesetId: string): TilesetData {
	const cached = tilesetCache.get(tilesetId);
	if (cached) return cached;

	const filePath = path.join(TILESETS_DIR, `${tilesetId.toLowerCase()}.yaml`);
	if (!fs.existsSync(filePath)) {
		throw new Error(`Tileset file not found: ${filePath}`);
	}

	const data = parseTileset(filePath);
	tilesetCache.set(tilesetId, data);
	return data;
}

// ── Actor Types Cache ──────────────────────────────────────────────────────

interface ActorTypeInfo {
	name: string;
	category: string; // derived from filename
}

let actorTypesCache: ActorTypeInfo[] | null = null;

function loadActorTypes(): ActorTypeInfo[] {
	if (actorTypesCache) return actorTypesCache;

	actorTypesCache = [];
	if (!fs.existsSync(RULES_DIR)) return actorTypesCache;

	const files = fs.readdirSync(RULES_DIR).filter(f => f.endsWith('.yaml'));
	for (const file of files) {
		const category = file.replace('.yaml', '');
		const content = fs.readFileSync(path.join(RULES_DIR, file), 'utf-8');
		const lines = content.split(/\r?\n/);

		for (const line of lines) {
			// Actor definitions are at indent level 0, don't start with ^, #, or whitespace
			if (line.length === 0 || line[0] === '\t' || line[0] === ' ' || line[0] === '#' || line[0] === '^') continue;
			const colonIdx = line.indexOf(':');
			if (colonIdx > 0) {
				const name = line.substring(0, colonIdx).trim();
				// Skip template inherits and special entries
				if (name.startsWith('-') || name.includes('@')) continue;
				// Actor names are typically uppercase or mixed case identifiers
				if (/^[A-Z0-9][A-Za-z0-9_.]*$/.test(name)) {
					actorTypesCache.push({ name, category });
				}
			}
		}
	}

	return actorTypesCache;
}

// ── Helper Functions ───────────────────────────────────────────────────────

function getMapPath(mapName: string): string {
	return path.join(MAPS_DIR, mapName);
}

function ensureMapExists(mapName: string): string {
	const mapPath = getMapPath(mapName);
	if (!fs.existsSync(mapPath)) {
		throw new Error(`Map not found: ${mapName}. Available maps: ${listMapNames().join(', ')}`);
	}
	return mapPath;
}

function listMapNames(): string[] {
	if (!fs.existsSync(MAPS_DIR)) return [];
	return fs.readdirSync(MAPS_DIR).filter(f =>
		fs.statSync(path.join(MAPS_DIR, f)).isDirectory() &&
		fs.existsSync(path.join(MAPS_DIR, f, 'map.yaml'))
	);
}

function loadMap(mapName: string): { yaml: MapYamlData; bin: MapBinData } {
	const mapPath = ensureMapExists(mapName);
	const yaml = readMapYaml(path.join(mapPath, 'map.yaml'));
	const binBuf = fs.readFileSync(path.join(mapPath, 'map.bin'));
	const bin = readMapBin(binBuf);
	return { yaml, bin };
}

function saveMap(mapName: string, yaml: MapYamlData, bin: MapBinData): void {
	const mapPath = getMapPath(mapName);
	fs.writeFileSync(path.join(mapPath, 'map.yaml'), writeMapYaml(yaml));
	fs.writeFileSync(path.join(mapPath, 'map.bin'), writeMapBin(bin));
}

function getNextActorId(actors: MapActor[]): number {
	let maxId = -1;
	for (const a of actors) {
		const match = a.id.match(/^Actor(\d+)$/);
		if (match) maxId = Math.max(maxId, parseInt(match[1]));
	}
	return maxId + 1;
}

/** Get terrain type name for a tile from the tileset. */
function getTerrainTypeName(tileset: TilesetData, tileType: number, tileIndex: number): string {
	const tmpl = tileset.templates.get(tileType);
	if (!tmpl) return 'Unknown';
	return tmpl.tiles.get(tileIndex) ?? tmpl.tiles.get(0) ?? 'Unknown';
}

/** Generate a simple PNG preview from tile data. */
function generatePreviewPng(bin: MapBinData, tileset: TilesetData): Buffer {
	// Use playable bounds (skip 1-cell border)
	const w = bin.width;
	const h = bin.height;
	const png = new PNG({ width: w, height: h });

	for (let y = 0; y < h; y++) {
		for (let x = 0; x < w; x++) {
			const tile = bin.tiles[x]?.[y];
			let r = 40, g = 68, b = 40; // default green

			if (tile) {
				const typeName = getTerrainTypeName(tileset, tile.type, tile.index);
				const terrainType = tileset.terrainTypes.get(typeName);
				if (terrainType) {
					[r, g, b] = hexToRgb(terrainType.color);
				}
			}

			const idx = (y * w + x) * 4;
			png.data[idx] = r;
			png.data[idx + 1] = g;
			png.data[idx + 2] = b;
			png.data[idx + 3] = 255;
		}
	}

	return PNG.sync.write(png);
}

// ── MCP Server Setup ───────────────────────────────────────────────────────

const server = new McpServer({
	name: 'ww3mod-map',
	version: '1.0.0',
});

// ── Tool: create_map ───────────────────────────────────────────────────────

server.tool(
	'create_map',
	'Create a new empty WW3MOD map with all required files',
	{
		name: z.string().describe('Map directory name (e.g. "my-new-map")'),
		width: z.number().int().min(16).max(256).describe('Map width in cells (playable area)'),
		height: z.number().int().min(16).max(256).describe('Map height in cells (playable area)'),
		tileset: z.enum(['TEMPERAT', 'SNOW', 'DESERT']).default('TEMPERAT').describe('Tileset to use'),
		numPlayers: z.number().int().min(2).max(8).default(2).describe('Number of playable slots'),
		title: z.string().describe('Human-readable map title'),
		author: z.string().default('WW3MOD').describe('Map author name'),
		visibility: z.enum(['Lobby', 'Shellmap', 'MissionSelector']).default('Lobby').describe('Map visibility'),
		categories: z.string().default('Conquest').describe('Map categories'),
	},
	async (params) => {
		const mapPath = getMapPath(params.name);
		if (fs.existsSync(mapPath)) {
			return { content: [{ type: 'text', text: `Error: Map "${params.name}" already exists at ${mapPath}` }] };
		}

		// Map size includes 1-cell border on each side
		const totalW = params.width + 2;
		const totalH = params.height + 2;

		// Create map directory
		fs.mkdirSync(mapPath, { recursive: true });

		// Build players
		const players: PlayerReference[] = [
			{ name: 'Neutral', ownsWorld: true, nonCombatant: true, faction: 'Random' },
			{ name: 'Creeps', nonCombatant: true, faction: 'Random',
			  enemies: Array.from({ length: params.numPlayers }, (_, i) => `Multi${i}`).join(', ') },
		];
		for (let i = 0; i < params.numPlayers; i++) {
			players.push({
				name: `Multi${i}`,
				playable: true,
				faction: 'Random',
				enemies: 'Creeps',
			});
		}

		// Place default spawn points around the map edges
		const actors: MapActor[] = [];
		const spawnPositions = calculateSpawnPositions(params.width, params.height, params.numPlayers);
		for (let i = 0; i < spawnPositions.length; i++) {
			actors.push({
				id: `Actor${i}`,
				type: 'mpspawn',
				owner: 'Neutral',
				location: `${spawnPositions[i][0]},${spawnPositions[i][1]}`,
			});
		}

		// Build YAML
		const yaml: MapYamlData = {
			mapFormat: '12',
			requiresMod: 'ww3mod',
			title: params.title,
			author: params.author,
			tileset: params.tileset,
			mapSize: `${totalW},${totalH}`,
			bounds: `1,1,${params.width},${params.height}`,
			visibility: params.visibility,
			categories: params.categories,
			players,
			actors,
			extra: new Map(),
		};

		// Build binary (all clear tiles)
		const bin = createEmptyMapBin(totalW, totalH, 255);

		// Write files
		fs.writeFileSync(path.join(mapPath, 'map.yaml'), writeMapYaml(yaml));
		fs.writeFileSync(path.join(mapPath, 'map.bin'), writeMapBin(bin));

		// Generate preview
		const tileset = loadTileset(params.tileset);
		const pngBuf = generatePreviewPng(bin, tileset);
		fs.writeFileSync(path.join(mapPath, 'map.png'), pngBuf);

		return {
			content: [{
				type: 'text',
				text: `Created map "${params.name}" (${params.width}x${params.height}, ${params.tileset}, ${params.numPlayers} players)\n` +
					`Path: ${mapPath}\n` +
					`Files: map.yaml, map.bin, map.png\n` +
					`Spawn points: ${spawnPositions.map(p => `(${p[0]},${p[1]})`).join(', ')}`,
			}],
		};
	}
);

function calculateSpawnPositions(w: number, h: number, count: number): [number, number][] {
	const positions: [number, number][] = [];
	const margin = Math.max(8, Math.floor(Math.min(w, h) * 0.1));

	if (count === 2) {
		// Opposite corners
		positions.push([margin, margin]);
		positions.push([w - margin, h - margin]);
	} else if (count <= 4) {
		// Four corners
		const corners: [number, number][] = [
			[margin, margin],
			[w - margin, margin],
			[w - margin, h - margin],
			[margin, h - margin],
		];
		for (let i = 0; i < count; i++) positions.push(corners[i]);
	} else {
		// Distribute around edges
		for (let i = 0; i < count; i++) {
			const angle = (2 * Math.PI * i) / count - Math.PI / 2;
			const cx = Math.floor(w / 2);
			const cy = Math.floor(h / 2);
			const rx = cx - margin;
			const ry = cy - margin;
			const x = Math.round(cx + rx * Math.cos(angle));
			const y = Math.round(cy + ry * Math.sin(angle));
			positions.push([Math.max(1, Math.min(w, x)), Math.max(1, Math.min(h, y))]);
		}
	}

	return positions;
}

// ── Tool: read_map ─────────────────────────────────────────────────────────

server.tool(
	'read_map',
	'Load and return full map state as JSON',
	{
		mapName: z.string().describe('Map directory name'),
	},
	async ({ mapName }) => {
		const { yaml, bin } = loadMap(mapName);
		const tileset = loadTileset(yaml.tileset);

		// Build terrain distribution summary
		const terrainCounts = new Map<string, number>();
		for (let x = 0; x < bin.width; x++) {
			for (let y = 0; y < bin.height; y++) {
				const tile = bin.tiles[x]?.[y];
				if (tile) {
					const name = getTerrainTypeName(tileset, tile.type, tile.index);
					terrainCounts.set(name, (terrainCounts.get(name) ?? 0) + 1);
				}
			}
		}

		const result = {
			metadata: {
				title: yaml.title,
				author: yaml.author,
				tileset: yaml.tileset,
				mapSize: yaml.mapSize,
				bounds: yaml.bounds,
				visibility: yaml.visibility,
				categories: yaml.categories,
			},
			players: yaml.players,
			actors: yaml.actors,
			terrainDistribution: Object.fromEntries(terrainCounts),
			totalCells: bin.width * bin.height,
		};

		return { content: [{ type: 'text', text: JSON.stringify(result, null, 2) }] };
	}
);

// ── Tool: list_maps ────────────────────────────────────────────────────────

server.tool(
	'list_maps',
	'List all maps in the WW3MOD maps directory',
	{},
	async () => {
		const maps = listMapNames().map(name => {
			try {
				const yaml = readMapYaml(path.join(MAPS_DIR, name, 'map.yaml'));
				return {
					name,
					title: yaml.title,
					tileset: yaml.tileset,
					mapSize: yaml.mapSize,
					visibility: yaml.visibility,
					playerCount: yaml.players.filter(p => p.playable).length,
					actorCount: yaml.actors.length,
				};
			} catch {
				return { name, title: '(error reading)', tileset: '', mapSize: '', visibility: '', playerCount: 0, actorCount: 0 };
			}
		});

		return { content: [{ type: 'text', text: JSON.stringify(maps, null, 2) }] };
	}
);

// ── Tool: fill_terrain ─────────────────────────────────────────────────────

server.tool(
	'fill_terrain',
	'Fill a rectangular region with a terrain type',
	{
		mapName: z.string().describe('Map directory name'),
		x: z.number().int().min(0).describe('Start X coordinate'),
		y: z.number().int().min(0).describe('Start Y coordinate'),
		width: z.number().int().min(1).describe('Region width'),
		height: z.number().int().min(1).describe('Region height'),
		terrainType: z.string().describe('Terrain type name (e.g. "Clear", "Water", "Road", "Rock")'),
		tileIndex: z.number().int().min(0).optional().describe('Specific tile index (random if not set for PickAny tiles)'),
	},
	async (params) => {
		const { yaml, bin } = loadMap(params.mapName);
		const tileset = loadTileset(yaml.tileset);

		// Find the template for this terrain type
		const tmpl = findTemplateForTerrainType(tileset, params.terrainType);
		if (!tmpl) {
			const available = [...tileset.terrainTypes.values()].map(t => t.type).join(', ');
			return { content: [{ type: 'text', text: `Error: Unknown terrain type "${params.terrainType}". Available: ${available}` }] };
		}

		let filled = 0;
		const maxTileIndex = Math.max(...tmpl.tiles.keys());

		for (let dx = 0; dx < params.width; dx++) {
			for (let dy = 0; dy < params.height; dy++) {
				const tx = params.x + dx;
				const ty = params.y + dy;
				if (tx < 0 || tx >= bin.width || ty < 0 || ty >= bin.height) continue;

				const index = params.tileIndex ?? (tmpl.pickAny ? Math.floor(Math.random() * (maxTileIndex + 1)) : 0);
				bin.tiles[tx][ty] = { type: tmpl.id, index };
				filled++;
			}
		}

		saveMap(params.mapName, yaml, bin);
		return { content: [{ type: 'text', text: `Filled ${filled} cells with ${params.terrainType} (template ${tmpl.id}) in region (${params.x},${params.y})→(${params.x + params.width - 1},${params.y + params.height - 1})` }] };
	}
);

// ── Tool: paint_terrain ────────────────────────────────────────────────────

server.tool(
	'paint_terrain',
	'Set individual tiles for precision terrain editing',
	{
		mapName: z.string().describe('Map directory name'),
		tiles: z.array(z.object({
			x: z.number().int(),
			y: z.number().int(),
			terrainType: z.string().describe('Terrain type name'),
			tileIndex: z.number().int().optional(),
		})).describe('Array of tiles to paint'),
	},
	async ({ mapName, tiles }) => {
		const { yaml, bin } = loadMap(mapName);
		const tileset = loadTileset(yaml.tileset);

		let painted = 0;
		const errors: string[] = [];

		for (const t of tiles) {
			if (t.x < 0 || t.x >= bin.width || t.y < 0 || t.y >= bin.height) {
				errors.push(`(${t.x},${t.y}): out of bounds`);
				continue;
			}
			const tmpl = findTemplateForTerrainType(tileset, t.terrainType);
			if (!tmpl) {
				errors.push(`(${t.x},${t.y}): unknown terrain "${t.terrainType}"`);
				continue;
			}
			const maxIdx = Math.max(...tmpl.tiles.keys());
			const index = t.tileIndex ?? (tmpl.pickAny ? Math.floor(Math.random() * (maxIdx + 1)) : 0);
			bin.tiles[t.x][t.y] = { type: tmpl.id, index };
			painted++;
		}

		saveMap(mapName, yaml, bin);

		let msg = `Painted ${painted} tiles`;
		if (errors.length > 0) msg += `\nErrors: ${errors.join('; ')}`;
		return { content: [{ type: 'text', text: msg }] };
	}
);

// ── Tool: get_tileset_info ─────────────────────────────────────────────────

server.tool(
	'get_tileset_info',
	'List available terrain types and templates for a tileset',
	{
		tileset: z.enum(['TEMPERAT', 'SNOW', 'DESERT']).default('TEMPERAT'),
	},
	async ({ tileset: tilesetId }) => {
		const tileset = loadTileset(tilesetId);

		const terrainTypes = [...tileset.terrainTypes.values()].map(t => ({
			type: t.type,
			color: `#${t.color}`,
			targetTypes: t.targetTypes,
		}));

		// Group templates by category
		const byCategory = new Map<string, { id: number; size: string; pickAny: boolean }[]>();
		for (const tmpl of tileset.templates.values()) {
			for (const cat of tmpl.categories) {
				if (!byCategory.has(cat)) byCategory.set(cat, []);
				byCategory.get(cat)!.push({
					id: tmpl.id,
					size: `${tmpl.size[0]}x${tmpl.size[1]}`,
					pickAny: tmpl.pickAny,
				});
			}
		}

		const result = {
			name: tileset.name,
			id: tileset.id,
			terrainTypes,
			templatesByCategory: Object.fromEntries(byCategory),
			totalTemplates: tileset.templates.size,
		};

		return { content: [{ type: 'text', text: JSON.stringify(result, null, 2) }] };
	}
);

// ── Tool: place_actors ─────────────────────────────────────────────────────

server.tool(
	'place_actors',
	'Add actors (units, structures, props) to the map',
	{
		mapName: z.string().describe('Map directory name'),
		actors: z.array(z.object({
			type: z.string().describe('Actor type (e.g. "mpspawn", "t03", "PROC")'),
			owner: z.string().default('Neutral').describe('Owner player name'),
			x: z.number().int().describe('X cell coordinate'),
			y: z.number().int().describe('Y cell coordinate'),
			id: z.string().optional().describe('Custom actor ID (auto-generated if not set)'),
			facing: z.string().optional().describe('Actor facing direction'),
		})).describe('Array of actors to place'),
	},
	async ({ mapName, actors: newActors }) => {
		const mapPath = ensureMapExists(mapName);
		const yaml = readMapYaml(path.join(mapPath, 'map.yaml'));

		let nextId = getNextActorId(yaml.actors);
		const placed: string[] = [];

		for (const a of newActors) {
			const actorId = a.id ?? `Actor${nextId++}`;
			const actor: MapActor = {
				id: actorId,
				type: a.type,
				owner: a.owner,
				location: `${a.x},${a.y}`,
			};
			if (a.facing) actor.facing = a.facing;
			yaml.actors.push(actor);
			placed.push(`${actorId}: ${a.type} at (${a.x},${a.y})`);
		}

		fs.writeFileSync(path.join(mapPath, 'map.yaml'), writeMapYaml(yaml));

		return { content: [{ type: 'text', text: `Placed ${placed.length} actors:\n${placed.join('\n')}` }] };
	}
);

// ── Tool: remove_actors ────────────────────────────────────────────────────

server.tool(
	'remove_actors',
	'Remove actors by ID or by region',
	{
		mapName: z.string().describe('Map directory name'),
		actorIds: z.array(z.string()).optional().describe('Actor IDs to remove'),
		region: z.object({
			x: z.number().int(),
			y: z.number().int(),
			width: z.number().int(),
			height: z.number().int(),
		}).optional().describe('Region to clear actors from'),
	},
	async ({ mapName, actorIds, region }) => {
		const mapPath = ensureMapExists(mapName);
		const yaml = readMapYaml(path.join(mapPath, 'map.yaml'));

		const before = yaml.actors.length;

		if (actorIds && actorIds.length > 0) {
			const idSet = new Set(actorIds);
			yaml.actors = yaml.actors.filter(a => !idSet.has(a.id));
		}

		if (region) {
			yaml.actors = yaml.actors.filter(a => {
				const [ax, ay] = a.location.split(',').map(Number);
				return !(ax >= region.x && ax < region.x + region.width &&
				         ay >= region.y && ay < region.y + region.height);
			});
		}

		const removed = before - yaml.actors.length;
		fs.writeFileSync(path.join(mapPath, 'map.yaml'), writeMapYaml(yaml));

		return { content: [{ type: 'text', text: `Removed ${removed} actors (${yaml.actors.length} remaining)` }] };
	}
);

// ── Tool: list_actor_types ─────────────────────────────────────────────────

server.tool(
	'list_actor_types',
	'List available actor types from WW3MOD rules files',
	{
		category: z.string().optional().describe('Filter by category (e.g. "infantry", "vehicles-america", "structures")'),
	},
	async ({ category }) => {
		const allTypes = loadActorTypes();

		let filtered = allTypes;
		if (category) {
			filtered = allTypes.filter(t => t.category.includes(category));
		}

		// Group by category
		const byCategory = new Map<string, string[]>();
		for (const t of filtered) {
			if (!byCategory.has(t.category)) byCategory.set(t.category, []);
			byCategory.get(t.category)!.push(t.name);
		}

		return {
			content: [{
				type: 'text',
				text: JSON.stringify({
					totalTypes: filtered.length,
					categories: Object.fromEntries(byCategory),
				}, null, 2),
			}],
		};
	}
);

// ── Tool: set_players ──────────────────────────────────────────────────────

server.tool(
	'set_players',
	'Configure player slots for the map',
	{
		mapName: z.string().describe('Map directory name'),
		players: z.array(z.object({
			name: z.string(),
			faction: z.string().default('Random'),
			playable: z.boolean().default(false),
			ownsWorld: z.boolean().default(false),
			nonCombatant: z.boolean().default(false),
			color: z.string().optional(),
			enemies: z.string().optional(),
		})).describe('Player definitions'),
	},
	async ({ mapName, players }) => {
		const mapPath = ensureMapExists(mapName);
		const yaml = readMapYaml(path.join(mapPath, 'map.yaml'));

		yaml.players = players.map(p => ({
			name: p.name,
			faction: p.faction,
			playable: p.playable || undefined,
			ownsWorld: p.ownsWorld || undefined,
			nonCombatant: p.nonCombatant || undefined,
			color: p.color,
			enemies: p.enemies,
		}));

		fs.writeFileSync(path.join(mapPath, 'map.yaml'), writeMapYaml(yaml));

		return { content: [{ type: 'text', text: `Set ${players.length} players: ${players.map(p => p.name).join(', ')}` }] };
	}
);

// ── Tool: set_spawn_points ─────────────────────────────────────────────────

server.tool(
	'set_spawn_points',
	'Place mpspawn actors at specific positions or with automatic symmetry',
	{
		mapName: z.string().describe('Map directory name'),
		positions: z.array(z.object({
			x: z.number().int(),
			y: z.number().int(),
		})).optional().describe('Explicit spawn positions'),
		count: z.number().int().min(2).max(8).optional().describe('Number of spawns (for auto-placement)'),
		symmetry: z.enum(['mirror-x', 'mirror-y', 'rotational', 'corners']).optional().describe('Symmetry type for auto-placement'),
	},
	async ({ mapName, positions, count, symmetry }) => {
		const mapPath = ensureMapExists(mapName);
		const yaml = readMapYaml(path.join(mapPath, 'map.yaml'));

		// Remove existing mpspawn actors
		yaml.actors = yaml.actors.filter(a => a.type !== 'mpspawn');

		let spawnPos: [number, number][];

		if (positions) {
			spawnPos = positions.map(p => [p.x, p.y] as [number, number]);
		} else if (count) {
			const [totalW, totalH] = yaml.mapSize.split(',').map(Number);
			const w = totalW - 2;
			const h = totalH - 2;
			const margin = Math.max(8, Math.floor(Math.min(w, h) * 0.15));

			if (symmetry === 'mirror-x') {
				const half = Math.ceil(count / 2);
				spawnPos = [];
				for (let i = 0; i < half; i++) {
					const y = Math.floor(margin + (h - 2 * margin) * i / (half - 1 || 1));
					spawnPos.push([margin, y]);
					if (spawnPos.length < count) spawnPos.push([w - margin, y]);
				}
			} else if (symmetry === 'mirror-y') {
				const half = Math.ceil(count / 2);
				spawnPos = [];
				for (let i = 0; i < half; i++) {
					const x = Math.floor(margin + (w - 2 * margin) * i / (half - 1 || 1));
					spawnPos.push([x, margin]);
					if (spawnPos.length < count) spawnPos.push([x, h - margin]);
				}
			} else if (symmetry === 'corners') {
				spawnPos = calculateSpawnPositions(w, h, count);
			} else {
				// Rotational (default)
				spawnPos = [];
				const cx = Math.floor(w / 2);
				const cy = Math.floor(h / 2);
				const r = Math.min(cx, cy) - margin;
				for (let i = 0; i < count; i++) {
					const angle = (2 * Math.PI * i) / count - Math.PI / 2;
					spawnPos.push([
						Math.max(1, Math.min(w, Math.round(cx + r * Math.cos(angle)))),
						Math.max(1, Math.min(h, Math.round(cy + r * Math.sin(angle)))),
					]);
				}
			}
		} else {
			return { content: [{ type: 'text', text: 'Error: Provide either positions or count' }] };
		}

		let nextId = getNextActorId(yaml.actors);
		for (const [x, y] of spawnPos) {
			yaml.actors.push({
				id: `Actor${nextId++}`,
				type: 'mpspawn',
				owner: 'Neutral',
				location: `${x},${y}`,
			});
		}

		fs.writeFileSync(path.join(mapPath, 'map.yaml'), writeMapYaml(yaml));

		return {
			content: [{
				type: 'text',
				text: `Placed ${spawnPos.length} spawn points: ${spawnPos.map(p => `(${p[0]},${p[1]})`).join(', ')}`,
			}],
		};
	}
);

// ── Tool: set_map_rules ────────────────────────────────────────────────────

server.tool(
	'set_map_rules',
	'Write rules.yaml for map-specific rule overrides (lighting, weather, disabled traits, etc.)',
	{
		mapName: z.string().describe('Map directory name'),
		rulesContent: z.string().describe('Raw YAML content for rules.yaml (OpenRA MiniYaml format)'),
	},
	async ({ mapName, rulesContent }) => {
		const mapPath = ensureMapExists(mapName);
		fs.writeFileSync(path.join(mapPath, 'rules.yaml'), rulesContent);

		return { content: [{ type: 'text', text: `Wrote rules.yaml for map "${mapName}" (${rulesContent.length} bytes)` }] };
	}
);

// ── Tool: write_lua_script ─────────────────────────────────────────────────

server.tool(
	'write_lua_script',
	'Write a Lua script file and update rules.yaml to reference it',
	{
		mapName: z.string().describe('Map directory name'),
		scriptName: z.string().describe('Lua script filename (e.g. "mission.lua")'),
		content: z.string().describe('Lua script content'),
	},
	async ({ mapName, scriptName, content: scriptContent }) => {
		const mapPath = ensureMapExists(mapName);

		// Write the Lua file
		const luaPath = path.join(mapPath, scriptName);
		fs.writeFileSync(luaPath, scriptContent);

		// Update or create rules.yaml to reference the script
		const rulesPath = path.join(mapPath, 'rules.yaml');
		let rulesContent = '';
		if (fs.existsSync(rulesPath)) {
			rulesContent = fs.readFileSync(rulesPath, 'utf-8');
		}

		// Check if LuaScript is already referenced
		if (!rulesContent.includes('LuaScript:')) {
			// Add LuaScript section to World
			if (rulesContent.includes('World:')) {
				// Append under World
				const worldIdx = rulesContent.indexOf('World:');
				const insertPos = rulesContent.indexOf('\n', worldIdx);
				rulesContent = rulesContent.substring(0, insertPos + 1) +
					`\tLuaScript:\n\t\tScripts: ${scriptName}\n` +
					rulesContent.substring(insertPos + 1);
			} else {
				// Create World section
				rulesContent += `\nWorld:\n\tLuaScript:\n\t\tScripts: ${scriptName}\n`;
			}
			fs.writeFileSync(rulesPath, rulesContent);
		} else if (!rulesContent.includes(scriptName)) {
			// LuaScript exists but doesn't reference this script — update the Scripts line
			rulesContent = rulesContent.replace(
				/(Scripts:\s*)(.+)/,
				`$1$2, ${scriptName}`
			);
			fs.writeFileSync(rulesPath, rulesContent);
		}

		return { content: [{ type: 'text', text: `Wrote ${scriptName} (${scriptContent.length} bytes) and updated rules.yaml` }] };
	}
);

// ── Tool: generate_preview ─────────────────────────────────────────────────

server.tool(
	'generate_preview',
	'Generate map.png preview from terrain tile data',
	{
		mapName: z.string().describe('Map directory name'),
	},
	async ({ mapName }) => {
		const { yaml, bin } = loadMap(mapName);
		const tileset = loadTileset(yaml.tileset);
		const pngBuf = generatePreviewPng(bin, tileset);
		const mapPath = getMapPath(mapName);
		fs.writeFileSync(path.join(mapPath, 'map.png'), pngBuf);

		return { content: [{ type: 'text', text: `Generated map.png for "${mapName}" (${bin.width}x${bin.height}, ${pngBuf.length} bytes)` }] };
	}
);

// ── Tool: place_template ───────────────────────────────────────────────

server.tool(
	'place_template',
	'Place a specific tileset template by ID at a position. Multi-cell templates write multiple cells.',
	{
		mapName: z.string().describe('Map directory name'),
		templateId: z.number().int().describe('Template ID from the tileset'),
		x: z.number().int().describe('X coordinate for top-left corner of the template'),
		y: z.number().int().describe('Y coordinate for top-left corner of the template'),
	},
	async ({ mapName, templateId, x, y }) => {
		const { yaml, bin } = loadMap(mapName);
		const tileset = loadTileset(yaml.tileset);

		const tmpl = tileset.templates.get(templateId);
		if (!tmpl) {
			return { content: [{ type: 'text', text: `Error: Template ${templateId} not found in tileset ${yaml.tileset}` }] };
		}

		const [tw, th] = tmpl.size;
		let placed = 0;
		let skipped = 0;

		for (const [tileIndex, _terrainType] of tmpl.tiles) {
			const cellX = x + (tileIndex % tw);
			const cellY = y + Math.floor(tileIndex / tw);

			if (cellX < 0 || cellX >= bin.width || cellY < 0 || cellY >= bin.height) {
				skipped++;
				continue;
			}

			bin.tiles[cellX][cellY] = { type: templateId, index: tileIndex };
			placed++;
		}

		saveMap(mapName, yaml, bin);

		return {
			content: [{
				type: 'text',
				text: `Placed template ${templateId} (${tw}x${th}) at (${x},${y}): ${placed} cells written, ${skipped} skipped (out of bounds)`,
			}],
		};
	}
);

// ── Tool: draw_road ────────────────────────────────────────────────────

/** Compute cells along a line between two points using Bresenham's algorithm. */
function bresenhamLine(x0: number, y0: number, x1: number, y1: number): [number, number][] {
	const points: [number, number][] = [];
	let dx = Math.abs(x1 - x0);
	let dy = Math.abs(y1 - y0);
	const sx = x0 < x1 ? 1 : -1;
	const sy = y0 < y1 ? 1 : -1;
	let err = dx - dy;

	let cx = x0;
	let cy = y0;

	while (true) {
		points.push([cx, cy]);
		if (cx === x1 && cy === y1) break;
		const e2 = 2 * err;
		if (e2 > -dy) { err -= dy; cx += sx; }
		if (e2 < dx) { err += dx; cy += sy; }
	}

	return points;
}

/** Expand a set of path cells to the given width by adding neighboring cells. */
function expandPath(cells: Set<string>, width: number): Set<string> {
	if (width <= 1) return cells;
	const expanded = new Set(cells);
	const halfW = Math.floor((width - 1) / 2);

	for (const key of cells) {
		const [cx, cy] = key.split(',').map(Number);
		for (let dx = -halfW; dx <= halfW; dx++) {
			for (let dy = -halfW; dy <= halfW; dy++) {
				expanded.add(`${cx + dx},${cy + dy}`);
			}
		}
	}

	return expanded;
}

server.tool(
	'draw_road',
	'Draw a road path between waypoints using road templates. Alternates between available 1x1 road templates for variety, and upgrades 2x2 regions where possible.',
	{
		mapName: z.string().describe('Map directory name'),
		waypoints: z.array(z.object({
			x: z.number().int(),
			y: z.number().int(),
		})).min(2).describe('Array of waypoint positions defining the road path'),
		width: z.number().int().min(1).max(6).default(2).describe('Road width in cells (default 2)'),
	},
	async ({ mapName, waypoints, width }) => {
		const { yaml, bin } = loadMap(mapName);
		const tileset = loadTileset(yaml.tileset);

		// Collect all 1x1 road templates for variety
		const road1x1Templates: TilesetTemplate[] = [];
		for (const tmpl of tileset.templates.values()) {
			if (tmpl.size[0] === 1 && tmpl.size[1] === 1) {
				for (const [, terrainType] of tmpl.tiles) {
					if (terrainType === 'Road') {
						road1x1Templates.push(tmpl);
						break;
					}
				}
			}
		}

		if (road1x1Templates.length === 0) {
			return { content: [{ type: 'text', text: `Error: No 1x1 Road templates found in tileset ${yaml.tileset}` }] };
		}

		// Collect 2x2 all-road templates for variety upgrades
		const road2x2Templates: TilesetTemplate[] = [];
		for (const tmpl of tileset.templates.values()) {
			if (tmpl.size[0] === 2 && tmpl.size[1] === 2) {
				let allRoad = true;
				let roadCount = 0;
				for (const [, terrainType] of tmpl.tiles) {
					roadCount++;
					if (terrainType !== 'Road') { allRoad = false; break; }
				}
				if (allRoad && roadCount === 4) {
					road2x2Templates.push(tmpl);
				}
			}
		}

		// 1. Compute path cells between consecutive waypoints
		const pathCells = new Set<string>();
		for (let i = 0; i < waypoints.length - 1; i++) {
			const line = bresenhamLine(waypoints[i].x, waypoints[i].y, waypoints[i + 1].x, waypoints[i + 1].y);
			for (const [px, py] of line) {
				pathCells.add(`${px},${py}`);
			}
		}

		// 2. Expand to desired width
		const roadCells = expandPath(pathCells, width);

		// 3. Place 1x1 road tiles, alternating templates for variety
		let placed = 0;
		const roadCellList: [number, number][] = [];

		for (const key of roadCells) {
			const [cx, cy] = key.split(',').map(Number);
			if (cx < 0 || cx >= bin.width || cy < 0 || cy >= bin.height) continue;

			const tmpl = road1x1Templates[Math.floor(Math.random() * road1x1Templates.length)];
			const maxIdx = Math.max(...tmpl.tiles.keys());
			const tileIdx = tmpl.pickAny ? Math.floor(Math.random() * (maxIdx + 1)) : 0;
			bin.tiles[cx][cy] = { type: tmpl.id, index: tileIdx };
			placed++;
			roadCellList.push([cx, cy]);
		}

		// 4. Upgrade 2x2 blocks for variety where possible
		let upgraded = 0;
		if (road2x2Templates.length > 0) {
			const usedFor2x2 = new Set<string>();
			for (const [cx, cy] of roadCellList) {
				// Check if this cell can be the top-left of a 2x2 block
				if (usedFor2x2.has(`${cx},${cy}`)) continue;
				const neighbors = [
					`${cx},${cy}`, `${cx + 1},${cy}`,
					`${cx},${cy + 1}`, `${cx + 1},${cy + 1}`,
				];
				if (neighbors.every(n => roadCells.has(n) && !usedFor2x2.has(n))) {
					if (cx + 1 >= bin.width || cy + 1 >= bin.height) continue;
					// ~30% chance to upgrade to 2x2
					if (Math.random() < 0.3) {
						const tmpl2 = road2x2Templates[Math.floor(Math.random() * road2x2Templates.length)];
						for (const [tileIndex] of tmpl2.tiles) {
							const tx = cx + (tileIndex % 2);
							const ty = cy + Math.floor(tileIndex / 2);
							bin.tiles[tx][ty] = { type: tmpl2.id, index: tileIndex };
						}
						for (const n of neighbors) usedFor2x2.add(n);
						upgraded++;
					}
				}
			}
		}

		saveMap(mapName, yaml, bin);

		return {
			content: [{
				type: 'text',
				text: `Drew road with ${waypoints.length} waypoints, width ${width}:\n` +
					`  ${placed} cells painted with road tiles (${road1x1Templates.length} template variants)\n` +
					`  ${upgraded} regions upgraded to 2x2 templates (${road2x2Templates.length} variants available)\n` +
					`  Path: ${waypoints.map(w => `(${w.x},${w.y})`).join(' → ')}`,
			}],
		};
	}
);

// ── Tool: auto_shore ───────────────────────────────────────────────────

server.tool(
	'auto_shore',
	'Automatically add shore/beach transitions around water tiles. Replaces land cells adjacent to water with Rough terrain as a transition band.',
	{
		mapName: z.string().describe('Map directory name'),
	},
	async ({ mapName }) => {
		const { yaml, bin } = loadMap(mapName);
		const tileset = loadTileset(yaml.tileset);

		// Find a 1x1 Rough template for shore transitions
		let roughTemplate: TilesetTemplate | undefined;
		for (const tmpl of tileset.templates.values()) {
			if (tmpl.size[0] === 1 && tmpl.size[1] === 1) {
				for (const [, terrainType] of tmpl.tiles) {
					if (terrainType === 'Rough') {
						roughTemplate = tmpl;
						break;
					}
				}
				if (roughTemplate) break;
			}
		}

		if (!roughTemplate) {
			return { content: [{ type: 'text', text: `Error: No 1x1 Rough template found in tileset ${yaml.tileset} for shore transitions` }] };
		}

		// Build water mask
		const waterMask: boolean[][] = [];
		for (let x = 0; x < bin.width; x++) {
			waterMask[x] = [];
			for (let y = 0; y < bin.height; y++) {
				const tile = bin.tiles[x]?.[y];
				if (tile) {
					const typeName = getTerrainTypeName(tileset, tile.type, tile.index);
					waterMask[x][y] = typeName === 'Water';
				} else {
					waterMask[x][y] = false;
				}
			}
		}

		// Find shore cells: non-water cells adjacent to at least one water cell
		const shoreCells: [number, number][] = [];
		const cardinals: [number, number][] = [[0, -1], [1, 0], [0, 1], [-1, 0]];
		const diagonals: [number, number][] = [[-1, -1], [1, -1], [-1, 1], [1, 1]];
		const allNeighbors = [...cardinals, ...diagonals];

		for (let x = 0; x < bin.width; x++) {
			for (let y = 0; y < bin.height; y++) {
				if (waterMask[x][y]) continue; // Skip water cells

				let adjacentToWater = false;
				for (const [dx, dy] of allNeighbors) {
					const nx = x + dx;
					const ny = y + dy;
					if (nx >= 0 && nx < bin.width && ny >= 0 && ny < bin.height && waterMask[nx][ny]) {
						adjacentToWater = true;
						break;
					}
				}

				if (adjacentToWater) {
					shoreCells.push([x, y]);
				}
			}
		}

		// Place rough terrain on shore cells
		const maxIdx = Math.max(...roughTemplate.tiles.keys());
		let placed = 0;

		for (const [sx, sy] of shoreCells) {
			const tileIdx = roughTemplate.pickAny ? Math.floor(Math.random() * (maxIdx + 1)) : 0;
			bin.tiles[sx][sy] = { type: roughTemplate.id, index: tileIdx };
			placed++;
		}

		saveMap(mapName, yaml, bin);

		return {
			content: [{
				type: 'text',
				text: `Auto-shore complete for "${mapName}":\n` +
					`  ${placed} shore transition cells placed (Rough, template ${roughTemplate.id})\n` +
					`  Scanned ${bin.width * bin.height} total cells`,
			}],
		};
	}
);

// ── Start Server ───────────────────────────────────────────────────────────

async function main() {
	const transport = new StdioServerTransport();
	await server.connect(transport);
}

main().catch((err) => {
	process.stderr.write(`MCP server error: ${err}\n`);
	process.exit(1);
});
