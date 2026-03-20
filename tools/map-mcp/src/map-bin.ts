/**
 * OpenRA map.bin binary format reader/writer.
 *
 * Format 2 layout:
 *   Offset 0:   uint8   format (= 2)
 *   Offset 1:   uint16  width  (LE)
 *   Offset 3:   uint16  height (LE)
 *   Offset 5:   uint32  tilesOffset   (= 17)
 *   Offset 9:   uint32  heightsOffset (0 if none)
 *   Offset 13:  uint32  resourcesOffset
 *   Offset 17:  tiles   (3 bytes/cell: uint16 type + uint8 index)
 *   Then:       resources (2 bytes/cell: uint8 type + uint8 density)
 *   Optional:   heights (1 byte/cell)
 *
 * Iteration order: column-major — for x in 0..width, for y in 0..height.
 */

export interface TerrainTile {
	type: number;   // uint16 template ID
	index: number;  // uint8 tile index within template
}

export interface ResourceTile {
	type: number;   // uint8
	density: number; // uint8
}

export interface MapBinData {
	width: number;
	height: number;
	tiles: TerrainTile[][];    // [x][y]
	resources: ResourceTile[][]; // [x][y]
	heights: number[][] | null;  // [x][y], null if no height data
}

export function readMapBin(buf: Buffer): MapBinData {
	const format = buf.readUInt8(0);
	if (format !== 1 && format !== 2)
		throw new Error(`Unknown binary map format: ${format}`);

	const width = buf.readUInt16LE(1);
	const height = buf.readUInt16LE(3);

	let tilesOffset: number;
	let heightsOffset: number;
	let resourcesOffset: number;

	if (format === 1) {
		tilesOffset = 5;
		heightsOffset = 0;
		resourcesOffset = 3 * width * height + 5;
	} else {
		tilesOffset = buf.readUInt32LE(5);
		heightsOffset = buf.readUInt32LE(9);
		resourcesOffset = buf.readUInt32LE(13);
	}

	// Read tiles (column-major)
	const tiles: TerrainTile[][] = [];
	if (tilesOffset > 0) {
		let pos = tilesOffset;
		for (let x = 0; x < width; x++) {
			tiles[x] = [];
			for (let y = 0; y < height; y++) {
				const type = buf.readUInt16LE(pos);
				const index = buf.readUInt8(pos + 2);
				tiles[x][y] = { type, index };
				pos += 3;
			}
		}
	}

	// Read resources
	const resources: ResourceTile[][] = [];
	if (resourcesOffset > 0) {
		let pos = resourcesOffset;
		for (let x = 0; x < width; x++) {
			resources[x] = [];
			for (let y = 0; y < height; y++) {
				const type = buf.readUInt8(pos);
				const density = buf.readUInt8(pos + 1);
				resources[x][y] = { type, density };
				pos += 2;
			}
		}
	}

	// Read heights
	let heights: number[][] | null = null;
	if (heightsOffset > 0) {
		heights = [];
		let pos = heightsOffset;
		for (let x = 0; x < width; x++) {
			heights[x] = [];
			for (let y = 0; y < height; y++) {
				heights[x][y] = buf.readUInt8(pos);
				pos += 1;
			}
		}
	}

	return { width, height, tiles, resources, heights };
}

export function writeMapBin(data: MapBinData): Buffer {
	const { width, height, tiles, resources, heights } = data;

	const hasHeights = heights !== null;
	const tilesOffset = 17;
	const heightsOffset = hasHeights ? 3 * width * height + 17 : 0;
	const resourcesOffset = (hasHeights ? 4 : 3) * width * height + 17;

	const totalSize = resourcesOffset + 2 * width * height;
	const buf = Buffer.alloc(totalSize);

	// Header
	buf.writeUInt8(2, 0);            // format
	buf.writeUInt16LE(width, 1);
	buf.writeUInt16LE(height, 3);
	buf.writeUInt32LE(tilesOffset, 5);
	buf.writeUInt32LE(heightsOffset, 9);
	buf.writeUInt32LE(resourcesOffset, 13);

	// Tiles (column-major)
	let pos = tilesOffset;
	for (let x = 0; x < width; x++) {
		for (let y = 0; y < height; y++) {
			const tile = tiles[x]?.[y] ?? { type: 255, index: 0 };
			buf.writeUInt16LE(tile.type, pos);
			buf.writeUInt8(tile.index, pos + 2);
			pos += 3;
		}
	}

	// Heights
	if (hasHeights && heights) {
		pos = heightsOffset;
		for (let x = 0; x < width; x++) {
			for (let y = 0; y < height; y++) {
				buf.writeUInt8(heights[x]?.[y] ?? 0, pos);
				pos += 1;
			}
		}
	}

	// Resources
	pos = resourcesOffset;
	for (let x = 0; x < width; x++) {
		for (let y = 0; y < height; y++) {
			const res = resources[x]?.[y] ?? { type: 0, density: 0 };
			buf.writeUInt8(res.type, pos);
			buf.writeUInt8(res.density, pos + 1);
			pos += 2;
		}
	}

	return buf;
}

/** Create an empty MapBinData filled with a given tile type. */
export function createEmptyMapBin(width: number, height: number, tileType: number = 255): MapBinData {
	const tiles: TerrainTile[][] = [];
	const resources: ResourceTile[][] = [];

	for (let x = 0; x < width; x++) {
		tiles[x] = [];
		resources[x] = [];
		for (let y = 0; y < height; y++) {
			// Use random index for PickAny clear tiles (0-15)
			const index = tileType === 255 ? Math.floor(Math.random() * 16) : 0;
			tiles[x][y] = { type: tileType, index };
			resources[x][y] = { type: 0, density: 0 };
		}
	}

	return { width, height, tiles, resources, heights: null };
}
