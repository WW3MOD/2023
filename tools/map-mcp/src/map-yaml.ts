/**
 * OpenRA MiniYaml reader/writer for map.yaml files.
 *
 * MiniYaml uses tab indentation with specific formatting:
 * - Top-level keys have no indentation
 * - Blank lines separate top-level sections
 * - Child nodes use tab indentation
 * - Values after colon with space: "Key: Value"
 * - Actor format: "ActorName: actortype" with child "Owner:" and "Location:" etc.
 */

import * as fs from 'node:fs';
import * as path from 'node:path';

export interface PlayerReference {
	name: string;
	ownsWorld?: boolean;
	nonCombatant?: boolean;
	playable?: boolean;
	faction?: string;
	color?: string;
	enemies?: string;
}

export interface MapActor {
	id: string;       // e.g. "Actor0" or "WestRoad2"
	type: string;     // e.g. "mpspawn", "waypoint", "tc05"
	owner: string;
	location: string; // "x,y"
	facing?: string;
	[key: string]: string | undefined;
}

export interface MapYamlData {
	mapFormat: string;
	requiresMod: string;
	title: string;
	author: string;
	tileset: string;
	mapSize: string;    // "W,H"
	bounds: string;     // "x,y,w,h"
	visibility: string;
	categories: string;
	players: PlayerReference[];
	actors: MapActor[];
	/** Any extra top-level keys we don't specifically parse */
	extra: Map<string, string>;
}

export function readMapYaml(filePath: string): MapYamlData {
	const content = fs.readFileSync(filePath, 'utf-8');
	const lines = content.split(/\r?\n/);

	const data: MapYamlData = {
		mapFormat: '12',
		requiresMod: 'ww3mod',
		title: '',
		author: '',
		tileset: 'TEMPERAT',
		mapSize: '',
		bounds: '',
		visibility: 'Lobby',
		categories: 'Conquest',
		players: [],
		actors: [],
		extra: new Map(),
	};

	let section = '';
	let currentPlayer: PlayerReference | null = null;
	let currentActor: MapActor | null = null;

	for (const line of lines) {
		const trimmed = line.trimEnd();
		if (trimmed === '') {
			// Flush current objects on blank lines
			if (currentPlayer) { data.players.push(currentPlayer); currentPlayer = null; }
			if (currentActor) { data.actors.push(currentActor); currentActor = null; }
			continue;
		}

		// Count leading tabs
		const tabs = line.length - line.replace(/^\t+/, '').length;

		if (tabs === 0) {
			// Top-level key
			if (currentPlayer) { data.players.push(currentPlayer); currentPlayer = null; }
			if (currentActor) { data.actors.push(currentActor); currentActor = null; }

			const colonIdx = trimmed.indexOf(':');
			if (colonIdx < 0) continue;
			const key = trimmed.substring(0, colonIdx).trim();
			const value = trimmed.substring(colonIdx + 1).trim();

			switch (key) {
				case 'MapFormat': data.mapFormat = value; break;
				case 'RequiresMod': data.requiresMod = value; break;
				case 'Title': data.title = value; break;
				case 'Author': data.author = value; break;
				case 'Tileset': data.tileset = value; break;
				case 'MapSize': data.mapSize = value; break;
				case 'Bounds': data.bounds = value; break;
				case 'Visibility': data.visibility = value; break;
				case 'Categories': data.categories = value; break;
				case 'Players': section = 'players'; break;
				case 'Actors': section = 'actors'; break;
				default:
					if (value) data.extra.set(key, value);
					else section = key.toLowerCase();
					break;
			}
		} else if (tabs === 1 && section === 'players') {
			// Player definition: "\tPlayerReference@Name:"
			if (currentPlayer) data.players.push(currentPlayer);
			const match = trimmed.match(/PlayerReference@(\w+):/);
			if (match) {
				currentPlayer = { name: match[1] };
			}
		} else if (tabs === 2 && section === 'players' && currentPlayer) {
			// Player property: "\t\tKey: Value"
			const colonIdx = trimmed.indexOf(':');
			if (colonIdx >= 0) {
				const key = trimmed.substring(0, colonIdx).trim();
				const value = trimmed.substring(colonIdx + 1).trim();
				switch (key) {
					case 'Name': currentPlayer.name = value; break;
					case 'OwnsWorld': currentPlayer.ownsWorld = value === 'True'; break;
					case 'NonCombatant': currentPlayer.nonCombatant = value === 'True'; break;
					case 'Playable': currentPlayer.playable = value === 'True'; break;
					case 'Faction': currentPlayer.faction = value; break;
					case 'Color': currentPlayer.color = value; break;
					case 'Enemies': currentPlayer.enemies = value; break;
				}
			}
		} else if (tabs === 1 && section === 'actors') {
			// Actor definition: "\tActorName: actortype"
			if (currentActor) data.actors.push(currentActor);
			const colonIdx = trimmed.indexOf(':');
			if (colonIdx >= 0) {
				const id = trimmed.substring(0, colonIdx).trim();
				const type = trimmed.substring(colonIdx + 1).trim();
				currentActor = { id, type, owner: 'Neutral', location: '0,0' };
			}
		} else if (tabs === 2 && section === 'actors' && currentActor) {
			// Actor property
			const colonIdx = trimmed.indexOf(':');
			if (colonIdx >= 0) {
				const key = trimmed.substring(0, colonIdx).trim();
				const value = trimmed.substring(colonIdx + 1).trim();
				switch (key) {
					case 'Owner': currentActor.owner = value; break;
					case 'Location': currentActor.location = value; break;
					default: currentActor[key.charAt(0).toLowerCase() + key.slice(1)] = value; break;
				}
			}
		}
	}

	// Flush remaining
	if (currentPlayer) data.players.push(currentPlayer);
	if (currentActor) data.actors.push(currentActor);

	return data;
}

export function writeMapYaml(data: MapYamlData): string {
	const lines: string[] = [];

	lines.push(`MapFormat: ${data.mapFormat}`);
	lines.push('');
	lines.push(`RequiresMod: ${data.requiresMod}`);
	lines.push('');
	lines.push(`Title: ${data.title}`);
	lines.push('');
	lines.push(`Author: ${data.author}`);
	lines.push('');
	lines.push(`Tileset: ${data.tileset}`);
	lines.push('');
	lines.push(`MapSize: ${data.mapSize}`);
	lines.push('');
	lines.push(`Bounds: ${data.bounds}`);
	lines.push('');
	lines.push(`Visibility: ${data.visibility}`);
	lines.push('');
	lines.push(`Categories: ${data.categories}`);
	lines.push('');

	// Players
	lines.push('Players:');
	for (const p of data.players) {
		lines.push(`\tPlayerReference@${p.name}:`);
		lines.push(`\t\tName: ${p.name}`);
		if (p.ownsWorld) lines.push('\t\tOwnsWorld: True');
		if (p.nonCombatant) lines.push('\t\tNonCombatant: True');
		if (p.playable) lines.push('\t\tPlayable: True');
		if (p.faction) lines.push(`\t\tFaction: ${p.faction}`);
		if (p.color) lines.push(`\t\tColor: ${p.color}`);
		if (p.enemies) lines.push(`\t\tEnemies: ${p.enemies}`);
	}
	lines.push('');

	// Actors
	lines.push('Actors:');
	for (const a of data.actors) {
		lines.push(`\t${a.id}: ${a.type}`);
		lines.push(`\t\tOwner: ${a.owner}`);
		lines.push(`\t\tLocation: ${a.location}`);
		// Write any extra properties
		for (const [key, value] of Object.entries(a)) {
			if (['id', 'type', 'owner', 'location'].includes(key)) continue;
			if (value !== undefined) {
				const yamlKey = key.charAt(0).toUpperCase() + key.slice(1);
				lines.push(`\t\t${yamlKey}: ${value}`);
			}
		}
	}
	lines.push('');

	// Write extra top-level keys (e.g. Rules: rules.yaml)
	for (const [key, value] of data.extra) {
		lines.push(`${key}: ${value}`);
	}

	return lines.join('\n');
}

export function writeRulesYaml(rules: Record<string, unknown>): string {
	const lines: string[] = [];
	writeYamlNode(lines, rules, 0);
	return lines.join('\n') + '\n';
}

function writeYamlNode(lines: string[], obj: Record<string, unknown>, depth: number): void {
	const indent = '\t'.repeat(depth);
	for (const [key, value] of Object.entries(obj)) {
		if (value === null || value === undefined) {
			lines.push(`${indent}${key}:`);
		} else if (typeof value === 'object' && !Array.isArray(value)) {
			lines.push(`${indent}${key}:`);
			writeYamlNode(lines, value as Record<string, unknown>, depth + 1);
		} else {
			lines.push(`${indent}${key}: ${value}`);
		}
	}
}
