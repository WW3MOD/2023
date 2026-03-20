/**
 * Tileset definition parser for OpenRA tilesets.
 * Reads terrain types and template definitions from tileset YAML files.
 */

import * as fs from 'node:fs';
import * as path from 'node:path';

export interface TerrainType {
	type: string;
	color: string; // hex color, e.g. "284428"
	targetTypes: string;
}

export interface TilesetTemplate {
	id: number;
	size: [number, number]; // [width, height]
	categories: string[];
	pickAny: boolean;
	tiles: Map<number, string>; // index → terrain type name
}

export interface TilesetData {
	name: string;
	id: string;
	terrainTypes: Map<string, TerrainType>;
	templates: Map<number, TilesetTemplate>;
}

/**
 * Parse a tileset YAML file.
 * This is a simplified parser that handles the specific format of OpenRA tileset files.
 */
export function parseTileset(filePath: string): TilesetData {
	const content = fs.readFileSync(filePath, 'utf-8');
	const lines = content.split(/\r?\n/);

	const data: TilesetData = {
		name: '',
		id: '',
		terrainTypes: new Map(),
		templates: new Map(),
	};

	let section = ''; // 'general', 'terrain', 'templates'
	let currentTerrainKey = '';
	let currentTerrain: TerrainType | null = null;
	let currentTemplate: TilesetTemplate | null = null;
	let inTiles = false;

	for (const line of lines) {
		const trimmed = line.trimEnd();
		if (trimmed === '' || trimmed.startsWith('#')) {
			if (trimmed === '') {
				// Blank line can reset context within templates
			}
			continue;
		}

		const tabs = line.length - line.replace(/^\t+/, '').length;

		if (tabs === 0) {
			// Top-level section
			const colonIdx = trimmed.indexOf(':');
			if (colonIdx >= 0) {
				const key = trimmed.substring(0, colonIdx).trim();
				if (key === 'General') section = 'general';
				else if (key === 'Terrain') section = 'terrain';
				else if (key === 'Templates') section = 'templates';
			}
		} else if (tabs === 1) {
			inTiles = false;

			if (section === 'general') {
				const colonIdx = trimmed.indexOf(':');
				if (colonIdx >= 0) {
					const key = trimmed.substring(0, colonIdx).trim();
					const value = trimmed.substring(colonIdx + 1).trim();
					if (key === 'Name') data.name = value;
					else if (key === 'Id') data.id = value;
				}
			} else if (section === 'terrain') {
				// TerrainType@TypeName:
				const match = trimmed.match(/TerrainType@(\w+):/);
				if (match) {
					if (currentTerrain) data.terrainTypes.set(currentTerrainKey, currentTerrain);
					currentTerrainKey = match[1];
					currentTerrain = { type: '', color: '808080', targetTypes: 'Ground' };
				}
			} else if (section === 'templates') {
				// Template@ID:
				const match = trimmed.match(/Template@(\d+):/);
				if (match) {
					if (currentTemplate) data.templates.set(currentTemplate.id, currentTemplate);
					currentTemplate = {
						id: parseInt(match[1]),
						size: [1, 1],
						categories: [],
						pickAny: false,
						tiles: new Map(),
					};
				}
			}
		} else if (tabs === 2) {
			const colonIdx = trimmed.indexOf(':');
			if (colonIdx < 0) continue;
			const key = trimmed.substring(0, colonIdx).trim();
			const value = trimmed.substring(colonIdx + 1).trim();

			if (section === 'terrain' && currentTerrain) {
				if (key === 'Type') currentTerrain.type = value;
				else if (key === 'Color') currentTerrain.color = value;
				else if (key === 'TargetTypes') currentTerrain.targetTypes = value;
			} else if (section === 'templates' && currentTemplate) {
				if (key === 'Id') currentTemplate.id = parseInt(value);
				else if (key === 'Size') {
					const parts = value.split(',').map(s => parseInt(s.trim()));
					currentTemplate.size = [parts[0], parts[1]];
				}
				else if (key === 'Categories') currentTemplate.categories = value.split(',').map(s => s.trim());
				else if (key === 'PickAny') currentTemplate.pickAny = value === 'True';
				else if (key === 'Tiles') inTiles = true;
			}
		} else if (tabs === 3 && inTiles && currentTemplate) {
			// Tile index: "0: Clear"
			const colonIdx = trimmed.indexOf(':');
			if (colonIdx >= 0) {
				const index = parseInt(trimmed.substring(0, colonIdx).trim());
				const terrainType = trimmed.substring(colonIdx + 1).trim();
				if (!isNaN(index)) currentTemplate.tiles.set(index, terrainType);
			}
		}
	}

	// Flush remaining
	if (currentTerrain) data.terrainTypes.set(currentTerrainKey, currentTerrain);
	if (currentTemplate) data.templates.set(currentTemplate.id, currentTemplate);

	return data;
}

/** Get all 1x1 templates for a given terrain category. */
export function getTemplatesForCategory(tileset: TilesetData, category: string): TilesetTemplate[] {
	const results: TilesetTemplate[] = [];
	for (const tmpl of tileset.templates.values()) {
		if (tmpl.size[0] === 1 && tmpl.size[1] === 1 && tmpl.categories.includes(category)) {
			results.push(tmpl);
		}
	}
	return results;
}

/**
 * Find the best template ID for a given terrain type name.
 * For painting individual tiles, we need 1x1 templates.
 */
export function findTemplateForTerrainType(tileset: TilesetData, terrainTypeName: string): TilesetTemplate | undefined {
	// First try exact category match
	for (const tmpl of tileset.templates.values()) {
		if (tmpl.size[0] === 1 && tmpl.size[1] === 1) {
			for (const [, type] of tmpl.tiles) {
				if (type === terrainTypeName) return tmpl;
			}
		}
	}
	return undefined;
}

/** Parse hex color string to RGB. */
export function hexToRgb(hex: string): [number, number, number] {
	const r = parseInt(hex.substring(0, 2), 16);
	const g = parseInt(hex.substring(2, 4), 16);
	const b = parseInt(hex.substring(4, 6), 16);
	return [r, g, b];
}
