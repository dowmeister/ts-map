#!/usr/bin/env node
// Stage the minimal set of files the routing service needs at runtime into
// routing-service/data/, gzipping the large routing graphs. This staged folder
// is what gets baked into the production image (see Dockerfile.routing).
//
// Source layout (map_data/<game>/...) -> staged layout (data/<game>/...):
//   routing|geojson/routing-graph.json -> routing/routing-graph.json.gz
//   VectorTileMapInfo.json             -> VectorTileMapInfo.json (copied)
//   Cities.json                        -> Cities.json (copied)
//   geojson/companies.geojson          -> geojson/companies.geojson (copied)
//
// Usage:
//   node scripts/prepare-routing-data.mjs                # all default games
//   node scripts/prepare-routing-data.mjs ets2 promods   # subset

import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, '..', '..');
const srcRoot = process.env.MAP_DATA_PATH ?? path.join(repoRoot, 'map_data');
const outRoot = path.resolve(__dirname, '..', 'data');

const DEFAULT_GAMES = ['ets2', 'ats', 'promods', 'gu'];
const games = process.argv.slice(2).length ? process.argv.slice(2) : DEFAULT_GAMES;

// Small files copied verbatim (relative to map_data/<game>/).
const COPY_FILES = ['VectorTileMapInfo.json', 'Cities.json', path.join('geojson', 'companies.geojson')];

function findGraph(game) {
  for (const rel of [path.join('routing', 'routing-graph.json'), path.join('geojson', 'routing-graph.json')]) {
    const p = path.join(srcRoot, game, rel);
    if (fs.existsSync(p)) return p;
  }
  return null;
}

function mb(bytes) {
  return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
}

let totalRaw = 0;
let totalOut = 0;

for (const game of games) {
  const gameSrc = path.join(srcRoot, game);
  if (!fs.existsSync(gameSrc)) {
    console.warn(`[skip] ${game}: not found in ${srcRoot}`);
    continue;
  }

  const graphSrc = findGraph(game);
  if (!graphSrc) {
    console.warn(`[skip] ${game}: no routing-graph.json`);
    continue;
  }

  const gameOut = path.join(outRoot, game);
  fs.mkdirSync(path.join(gameOut, 'routing'), { recursive: true });

  // Gzip the routing graph.
  const raw = fs.readFileSync(graphSrc);
  const gz = zlib.gzipSync(raw, { level: 9 });
  const graphOut = path.join(gameOut, 'routing', 'routing-graph.json.gz');
  fs.writeFileSync(graphOut, gz);
  totalRaw += raw.length;
  totalOut += gz.length;
  console.log(`[${game}] graph ${mb(raw.length)} -> ${mb(gz.length)} gz`);

  // Copy small support files verbatim.
  for (const rel of COPY_FILES) {
    const from = path.join(gameSrc, rel);
    if (!fs.existsSync(from)) {
      console.warn(`  [warn] ${game}/${rel} missing`);
      continue;
    }
    const to = path.join(gameOut, rel);
    fs.mkdirSync(path.dirname(to), { recursive: true });
    fs.copyFileSync(from, to);
    totalOut += fs.statSync(to).size;
  }
}

console.log(`\nStaged into ${path.relative(repoRoot, outRoot)}`);
console.log(`Graphs: ${mb(totalRaw)} raw -> total baked payload ${mb(totalOut)}`);
