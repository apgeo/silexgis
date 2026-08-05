// SPDX-License-Identifier: AGPL-3.0-or-later

// Build the elevation model the 3D view can draw its ground from.
// Cross-platform (Node 18+, no shell-isms). Three commands, run in this order:
//
//   node deploy/terrain.mjs fetch --bbox 22,45,26,48 --out ./dem
//       Downloads the Copernicus GLO-30 tiles covering a longitude/latitude box from the AWS
//       open-data bucket. No account, no key, no signature. One 1°x1° tile is about 44 MB.
//
//   node deploy/terrain.mjs bake --in ./dem --out /srv/silexgis/terrain [--max-depth 13]
//       Turns them into a static tile pyramid, using the mago-3d-terrainer Docker image
//       (MPL-2.0, carries its own Java). Prints the exact .env lines for what it produced.
//
//   node deploy/terrain.mjs check --dir /srv/silexgis/terrain
//       Verifies the pyramid before it goes anywhere near a web server: that it is complete,
//       that its tiles are what they claim to be, and what encoding it must be served under.
//
// WHY THE LAST ONE EXISTS. Terrain tiles are a binary mesh format, and a web server that
// describes their encoding wrongly produces no error anywhere: every request answers 200, the
// browser reports nothing, the console stays empty, and the only symptom is a globe with no
// ground on it. `check` prints the rule that applies to the bytes it actually found, so the
// serving configuration is derived from the files rather than assumed.

import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import {
  createWriteStream,
  existsSync,
  mkdirSync,
  readdirSync,
  readFileSync,
  renameSync,
  rmSync,
  statSync,
  writeFileSync,
} from 'node:fs';
import { open } from 'node:fs/promises';
import { join, relative, resolve, sep } from 'node:path';
import { Readable } from 'node:stream';
import { pipeline } from 'node:stream/promises';
import { pathToFileURL } from 'node:url';

/**
 * The pre-baker. Pinned to an exact release rather than a moving tag: what a generator writes —
 * in particular whether its tiles are compressed — is a property of its version, and this whole
 * pipeline's serving rule is derived from bytes that a different version might not produce.
 */
const TERRAINER_IMAGE = 'gaia3d/mago-3d-terrainer:1.14.2-release';

/**
 * Copernicus GLO-30, from the AWS open-data registry (https://registry.opendata.aws/copernicus-dem/).
 * One cloud-optimised GeoTIFF per 1°x1° cell, named for the south-west corner of the cell.
 * Free for any use including commercial, with attribution.
 */
const COPERNICUS_BUCKET = 'https://copernicus-dem-30m.s3.amazonaws.com';

/**
 * The credit the Copernicus licence requires, which is written into the pyramid so the scene has
 * something true to show. The pre-baker writes a placeholder here instead, and a placeholder is
 * worse than nothing: it is displayed.
 */
const COPERNICUS_ATTRIBUTION =
  'Copernicus DEM GLO-30 — © DLR e.V. 2010-2014 and © Airbus Defence and Space GmbH 2014-2018 '
  + 'provided under COPERNICUS by the European Union and ESA';

/** The two bytes every gzip stream begins with. */
const GZIP_MAGIC = [0x1f, 0x8b];

/**
 * Where a cell is written while it is still arriving.
 *
 * A download that stops half way — a closed terminal, a dropped link, a full disk — leaves bytes
 * behind, and bytes at the name the finished file will have are indistinguishable from a finished
 * file. The next run then skips the cell, the bake is fed a truncated raster, and whatever goes
 * wrong goes wrong hours later and somewhere else. Writing under a different name and moving it
 * into place only once the transfer has been checked makes an interrupted run cost nothing, which
 * is what the guide promises it costs.
 */
const PARTIAL_SUFFIX = '.part';

function fail(message, hint) {
  console.error(`\n${message}`);
  if (hint) {
    console.error(hint);
  }
  process.exit(1);
}

/** `--name value` and `--flag` out of the command line, with no dependency to do it. */
function parseArgs(argv) {
  const options = {};
  for (let index = 0; index < argv.length; index += 1) {
    const token = argv[index];
    if (!token.startsWith('--')) {
      continue;
    }
    const name = token.slice(2);
    const next = argv[index + 1];
    if (next === undefined || next.startsWith('--')) {
      options[name] = true;
    } else {
      options[name] = next;
      index += 1;
    }
  }
  return options;
}

/** The 1°x1° cells covering a west,south,east,north box, as Copernicus names them. */
export function copernicusCells(west, south, east, north) {
  const cells = [];
  for (let lat = Math.floor(south); lat < Math.ceil(north); lat += 1) {
    for (let lon = Math.floor(west); lon < Math.ceil(east); lon += 1) {
      // Named for its south-west corner — latitude two digits, longitude three — so the cell
      // spanning 46°N to 47°N is N46 and the one spanning 1°S to the equator is S01. Verified
      // against the bucket: S01_00_E015_00 exists and S00_00_W000_00 does not, because a corner
      // at exactly zero belongs to the northern and eastern names.
      const ns = lat < 0 ? 'S' : 'N';
      const ew = lon < 0 ? 'W' : 'E';
      const latDegrees = String(Math.abs(lat)).padStart(2, '0');
      const lonDegrees = String(Math.abs(lon)).padStart(3, '0');
      cells.push(`${ns}${latDegrees}_00_${ew}${lonDegrees}_00`);
    }
  }
  return cells;
}

/** The download URL for one named cell. */
export function copernicusUrl(cell) {
  const name = `Copernicus_DSM_COG_10_${cell}_DEM`;
  return `${COPERNICUS_BUCKET}/${name}/${name}.tif`;
}

function parseBbox(value) {
  const parts = String(value).split(',').map((part) => Number(part.trim()));
  if (parts.length !== 4 || parts.some((part) => !Number.isFinite(part))) {
    fail('--bbox must be west,south,east,north in degrees, e.g. --bbox 22,45,26,48');
  }
  const [west, south, east, north] = parts;
  if (west >= east || south >= north) {
    fail('--bbox must have west < east and south < north');
  }
  return { west, south, east, north };
}

async function fetchCommand(options) {
  if (!options.bbox || !options.out) {
    fail(
      'Usage: node deploy/terrain.mjs fetch --bbox west,south,east,north --out <directory>',
      'Example (the Apuseni): node deploy/terrain.mjs fetch --bbox 22,46,23,47 --out ./dem',
    );
  }
  const { west, south, east, north } = parseBbox(options.bbox);
  const outDir = resolve(process.cwd(), String(options.out));
  mkdirSync(outDir, { recursive: true });

  const cells = copernicusCells(west, south, east, north);
  console.log(
    `${cells.length} tile(s) of 1°x1° cover that box — roughly ${Math.round(cells.length * 44.5)} MB.`,
  );

  let downloaded = 0;
  let skipped = 0;
  let missing = 0;
  for (const cell of cells) {
    const url = copernicusUrl(cell);
    const target = join(outDir, `${cell}.tif`);
    if (existsSync(target) && statSync(target).size > 0) {
      // Downloading a country's worth of elevation twice because a run was interrupted is an
      // hour nobody has to spend. Only a file at the final name counts: an unfinished transfer
      // never reaches that name, so there is nothing here that could be a fragment.
      skipped += 1;
      continue;
    }
    process.stdout.write(`  ${cell} … `);
    let bytes;
    try {
      bytes = await downloadCell(url, target);
    } catch (error) {
      // A dropped link is the ordinary way this ends over a country's worth of data, and it is
      // worth a sentence rather than a stack trace — particularly the part about what re-running
      // will do, which is the whole reason the transfer is written where it is.
      fail(
        `\n  ${cell} failed: ${String(error)}`,
        '  Nothing incomplete was kept. Re-run the same command and it will carry on from here.',
      );
    }
    if (bytes === undefined) {
      // Cells that are entirely ocean are simply not published. That is an ordinary answer for a
      // box drawn around a coast, not a failure.
      console.log('not published (all sea)');
      missing += 1;
      continue;
    }
    console.log(`${(bytes / 1e6).toFixed(1)} MB`);
    downloaded += 1;
  }

  console.log(
    `\nDownloaded ${downloaded}, already present ${skipped}, not published ${missing}. → ${outDir}`,
  );
  console.log(`Next: node deploy/terrain.mjs bake --in ${options.out} --out <terrain directory>`);
}

/**
 * Fetches one elevation cell to `target`, or answers `undefined` when the cell is not published.
 *
 * The transfer lands under a temporary name and is moved onto the final one only after it has been
 * checked against the length the server declared, and anything that goes wrong takes the temporary
 * file with it. That is the whole difference between "re-running costs nothing" and "re-running
 * silently accepts whatever was on disk": a fragment can never occupy the name a finished cell has.
 *
 * `fetchImpl` is a parameter so this can be exercised against a local server; callers pass nothing.
 */
export async function downloadCell(url, target, fetchImpl = fetch) {
  const partial = `${target}${PARTIAL_SUFFIX}`;
  const response = await fetchImpl(url);
  if (response.status === 404) {
    return undefined;
  }
  if (!response.ok || !response.body) {
    fail(`  failed: ${url} answered ${response.status}`);
  }

  const declared = Number(response.headers.get('content-length'));
  try {
    await pipeline(Readable.fromWeb(response.body), createWriteStream(partial));
    const written = statSync(partial).size;
    // A body that ends early usually rejects above, but not always — a proxy can close a
    // connection cleanly at a byte boundary — and the length is right there in the headers.
    if (Number.isFinite(declared) && declared > 0 && written !== declared) {
      throw new Error(`got ${written} bytes of ${declared}`);
    }
    renameSync(partial, target);
    return written;
  } catch (error) {
    rmSync(partial, { force: true });
    throw error;
  }
}

async function bakeCommand(options) {
  if (!options.in || !options.out) {
    fail(
      'Usage: node deploy/terrain.mjs bake --in <dem directory> --out <terrain directory>'
      + ' [--max-depth 13] [--datum ellipsoidal]',
      'The input directory is what `fetch` wrote. The output is what a web server will serve.',
    );
  }
  const inDir = resolve(process.cwd(), String(options.in));
  const outDir = resolve(process.cwd(), String(options.out));
  if (!existsSync(inDir)) {
    fail(`No such input directory: ${inDir}`);
  }
  mkdirSync(outDir, { recursive: true });

  // 13 is about 10 m of ground per screen pixel at the equator, which is finer than the 30 m
  // source and is where more levels stop adding anything but tiles. Lower it for a first run:
  // every level roughly quadruples both the tile count and the time.
  const maxDepth = String(options.maxDepth ?? options['max-depth'] ?? 13);
  // Copernicus heights are measured from sea level (EGM2008), which is the same kind of number a
  // cave survey carries — so by default nothing is converted and the two already agree. Passing
  // --datum ellipsoidal converts them, which is geodetically truer and then requires the survey
  // to be corrected by the local geoid undulation; the .env lines printed below say so either way.
  const ellipsoidal = String(options.datum ?? '').toLowerCase() === 'ellipsoidal';

  const args = bakeDockerArgs({
    inDir,
    outDir,
    maxDepth,
    ellipsoidal,
    platform: process.platform,
    // Only some platforms have these at all, and only on those does the answer mean anything.
    uid: typeof process.getuid === 'function' ? process.getuid() : undefined,
    gid: typeof process.getgid === 'function' ? process.getgid() : undefined,
  });

  console.log(`\n$ docker ${args.join(' ')}\n`);
  try {
    execFileSync('docker', args, { stdio: 'inherit' });
  } catch {
    fail(
      'The pre-baker failed.',
      'Docker must be running, and the image is pulled on first use:\n'
      + `  docker pull ${TERRAINER_IMAGE}`,
    );
  }

  // Temporary rasters, if the run left any. They are as large as the pyramid itself, and an
  // operator baking a country cannot afford a second copy of it sitting in the served directory.
  const temp = join(outDir, 'temp');
  if (existsSync(temp)) {
    console.log("Removing the pre-baker's temporary rasters …");
    try {
      rmSync(temp, { recursive: true, force: true });
    } catch {
      console.warn(`  could not remove ${temp}; delete it by hand.`);
    }
  }

  try {
    finishLayerJson(outDir);
  } catch (error) {
    // Not swallowed. An unstamped pyramid still carries the pre-baker's constant version, which
    // puts the same string on the end of every tile URL it has ever produced — so a browser that
    // saw any earlier pyramid at this address keeps drawing that one out of its own cache, for a
    // week, without a request reaching the server. Serving that is worse than stopping here.
    fail(
      `The pyramid was built, but this script could not finish it: ${String(error)}`,
      '  The tiles are there; what failed was writing layer.json back with the attribution the\n'
      + '  data\'s licence requires and the version every tile URL is built from. Almost always\n'
      + '  that is ownership — a pyramid produced by running the pre-baker by hand belongs to the\n'
      + '  container\'s own user. Bake into an empty directory you own, through this script.',
    );
  }
  await checkCommand({ dir: outDir });
  printEnvironment(outDir, ellipsoidal);
}

/**
 * The command line the pre-baker is run with.
 *
 * Its own arguments are the dull part. What matters is `--user`, and it matters on exactly one
 * family of hosts. A Linux container engine runs an image as the user the image declares — this
 * one declares root — and a directory bind-mounted into it therefore comes back owned by root,
 * inside a directory belonging to whoever ran the command. Everything this script does after the
 * container exits then fails on permissions: it cannot stamp the pyramid with the version that
 * every tile URL is built from, and it cannot replace the pre-baker's placeholder credit with the
 * one the elevation data's licence requires. Both of those are silent months later — a re-bake
 * nobody's browser ever asks for, and a placeholder string displayed to viewers — and both are
 * avoided by handing the container the invoking user.
 *
 * Passing the same thing on macOS or Windows would be wrong rather than merely unnecessary: those
 * engines map ownership through a virtual machine, files already come back owned by the invoking
 * user, and a uid from the host means nothing inside that mapping. The pre-baker's own insistence
 * on a writable input directory is satisfied either way, because the operator owns the directory
 * they created.
 *
 * Exported for its own test; `bake` is what an operator runs.
 */
export function bakeDockerArgs({ inDir, outDir, maxDepth, ellipsoidal, platform, uid, gid }) {
  const args = ['run', '--rm'];
  if (platform === 'linux' && typeof uid === 'number' && typeof gid === 'number') {
    args.push('--user', `${uid}:${gid}`);
  }
  args.push(
    // Not mounted read-only, which would be the safer choice: the pre-baker refuses to start at
    // all against an input directory it cannot write to ("path is not writable"), checked before
    // it looks at anything else. It does not in fact write there, but it insists on being able to.
    '-v', `${inDir}:/data/input`,
    '-v', `${outDir}:/data/output`,
    TERRAINER_IMAGE,
    '-i', '/data/input',
    '-o', '/data/output',
    '-max', String(maxDepth),
  );
  if (ellipsoidal) {
    args.push('-g', 'EGM2008');
  }
  return args;
}

/**
 * The version a pyramid publishes for itself, derived from the tiles it actually contains.
 *
 * This is a cache key, and it has to be one. A tile client builds every tile's URL from the
 * template in `layer.json`, which ends `?v={version}` — so this string is on the end of every
 * request a browser makes for elevation. The pre-baker writes a constant there, the same one for
 * every pyramid it has ever produced, which makes the query useless: two different bakes are
 * requested at byte-identical URLs.
 *
 * That matters because re-baking is a normal thing to do — extending coverage, or converting the
 * heights to a different vertical datum — and elevation tiles are worth caching for a long time.
 * With a constant version, a viewer who looked at the scene last week keeps being served last
 * week's pyramid out of their own browser cache, with no request reaching the server at all: the
 * ground is drawn from heights the installation no longer has, no error appears, and if the datum
 * was what changed then every cave sits about forty metres off its hillside until the cache
 * happens to expire. Measured, not theorised — a re-bake that changed every height was served
 * entirely from cache, zero bytes over the network, for exactly this reason.
 *
 * Derived from the tile contents rather than from the clock so that re-running a bake that
 * produces the same tiles leaves caches warm, while any change to any tile changes every URL.
 * Kept semver-shaped because that is what the field is nominally for; a tile client substitutes
 * it into the URL and does not otherwise interpret it.
 */
export function pyramidVersion(digestHex) {
  return `1.1.0-${digestHex.slice(0, 12)}`;
}

/** A digest over every tile in a pyramid: its path and its bytes, in a fixed order. */
function pyramidDigest(dir) {
  const hash = createHash('sha256');
  // Sorted, and with the path folded in, so the answer depends only on what the pyramid contains
  // and not on the order a directory happened to be read in or the platform's separator.
  for (const tile of terrainFiles(dir).sort()) {
    hash.update(relative(dir, tile).split(sep).join('/'));
    hash.update(readFileSync(tile));
  }
  return hash.digest('hex');
}

/**
 * Replaces the pre-baker's placeholder metadata with something true, and stamps the pyramid with
 * a version derived from its own tiles.
 *
 * The pre-baker writes `"attribution": "insert attribution here"` into every pyramid, and a scene
 * that shows its terrain's credit would show exactly that. The Copernicus licence requires a
 * credit, so that part is both a correctness fix and a licence obligation.
 */
function finishLayerJson(outDir) {
  const layerPath = join(outDir, 'layer.json');
  if (!existsSync(layerPath)) {
    return;
  }
  const layer = JSON.parse(readFileSync(layerPath, 'utf8'));
  if (typeof layer.attribution !== 'string' || layer.attribution.startsWith('insert ')) {
    layer.attribution = COPERNICUS_ATTRIBUTION;
  }
  if (typeof layer.name !== 'string' || layer.name.startsWith('insert ')) {
    layer.name = 'Copernicus DEM GLO-30';
  }
  if (typeof layer.description !== 'string' || layer.description.startsWith('insert ')) {
    layer.description = 'Elevation model baked for SilexGIS.';
  }
  layer.version = pyramidVersion(pyramidDigest(outDir));
  delete layer.legend;
  writeFileSync(layerPath, `${JSON.stringify(layer)}\n`, 'utf8');
}

/** Every `.terrain` file under a directory. */
function terrainFiles(dir) {
  const found = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) {
      found.push(...terrainFiles(path));
    } else if (entry.name.endsWith('.terrain')) {
      found.push(path);
    }
  }
  return found;
}

/** The opening bytes of a file — enough of them to identify what it is. */
async function firstBytes(path, count) {
  const handle = await open(path, 'r');
  try {
    const buffer = Buffer.alloc(count);
    const { bytesRead } = await handle.read(buffer, 0, count, 0);
    return buffer.subarray(0, bytesRead);
  } finally {
    await handle.close();
  }
}

/**
 * Bytes of a tile that have to be read before it can be identified, and the smallest a quantized
 * mesh can possibly be: an 88-byte fixed header and the four-byte vertex count after it.
 */
const TILE_HEAD_BYTES = 92;

/** Three coordinates per vertex, two bytes each, immediately after the count. */
const BYTES_PER_VERTEX = 6;

/** More vertices than any terrain tile carries; a fragment read as a count usually exceeds it. */
const VERTEX_COUNT_BOUND = 1 << 22;

/** Comfortably outside the earth, in metres, for a coordinate measured from its centre. */
const EARTH_RADIUS_BOUND_METERS = 6.6e6;

/** Generously outside the range of the earth's own surface, in metres. */
const ELEVATION_BOUND_METERS = 15_000;

/**
 * What a tile is, decided from its opening bytes and its size: a mesh, a gzip stream, or neither.
 *
 * The obvious test — "does it start with the two bytes a gzip stream starts with" — is not good
 * enough, and the reason is worth writing down because it produces a failure that looks like a
 * bug in the pyramid rather than in the test. A quantized mesh has no magic number at all: it
 * begins with the tile's centre as a little-endian double, whose low two bytes are essentially
 * random. So roughly one tile in every sixty-five thousand starts with those two bytes by
 * coincidence, which over a country's worth of tiles is a near-certainty — and the answer would
 * be to declare that pyramid mixed and refuse it, or worse to tell the operator to serve a raw
 * pyramid as compressed. Re-baking cannot help either: the bytes are a function of where the tile
 * is, so the same coincidence comes back every time.
 *
 * A mesh is therefore identified positively instead, from the numbers in its header that have to
 * be what they are: a point within the earth, two elevations in the range the earth's surface
 * occupies with the lower not above the higher, and a vertex count the file is actually long
 * enough to hold. A gzip stream reinterpreted that way is a point somewhere in the region of
 * 10^300 metres from anywhere; a run of zeros left by a disk that filled declares no vertices at
 * all; and a tile cut off part way through declares more than are there. The gzip test is only
 * reached once all of that has failed, and asks for the whole fixed member header — magic, deflate
 * as the compression method, and no reserved flag bits — rather than for two bytes.
 *
 * Exported for its own test; `check` is what an operator runs.
 */
export function classifyTile(head, sizeBytes) {
  if (sizeBytes < TILE_HEAD_BYTES || head.length < TILE_HEAD_BYTES) {
    return 'damaged';
  }
  if (looksLikeQuantizedMesh(head, sizeBytes)) {
    return 'mesh';
  }
  if (looksGzipped(head)) {
    return 'gzip';
  }
  return 'damaged';
}

function looksLikeQuantizedMesh(head, sizeBytes) {
  const view = new DataView(head.buffer, head.byteOffset, head.byteLength);
  for (let offset = 0; offset < 24; offset += 8) {
    const ordinate = view.getFloat64(offset, true);
    if (!Number.isFinite(ordinate) || Math.abs(ordinate) > EARTH_RADIUS_BOUND_METERS) {
      return false;
    }
  }
  const lowest = view.getFloat32(24, true);
  const highest = view.getFloat32(28, true);
  if (
    !Number.isFinite(lowest)
    || !Number.isFinite(highest)
    || lowest < -ELEVATION_BOUND_METERS
    || highest > ELEVATION_BOUND_METERS
    || lowest > highest
  ) {
    return false;
  }
  const vertices = view.getUint32(88, true);
  return (
    vertices > 0
    && vertices <= VERTEX_COUNT_BOUND
    && sizeBytes >= TILE_HEAD_BYTES + vertices * BYTES_PER_VERTEX
  );
}

function looksGzipped(head) {
  return (
    head[0] === GZIP_MAGIC[0]
    && head[1] === GZIP_MAGIC[1]
    // Deflate is the only compression method the format has ever defined, and the top three flag
    // bits are reserved and must be clear.
    && head[2] === 0x08
    && (head[3] & 0xe0) === 0
  );
}

/**
 * The tile levels a pyramid says it holds but has none of on disk.
 *
 * `available` is indexed by level, so a level that publishes ranges and produced no files is a
 * bake that stopped part way — the shape a run that ran out of disk leaves behind. The rest of
 * the pyramid is perfectly readable and every tile in it is valid, so nothing else notices: the
 * renderer asks for a tile that is not there, gets a 404, and quietly draws the coarser one above
 * it instead. Ground at the wrong resolution, presented as ground.
 *
 * Only "advertised and entirely absent" is reported. Counting how many tiles each range implies
 * and comparing would be stricter and would also condemn a pyramid whose generator publishes its
 * ranges optimistically, which is not this command's call to make.
 *
 * Exported for its own test; `check` is what an operator runs.
 */
export function missingLevels(available, levelCounts) {
  if (!Array.isArray(available)) {
    return [];
  }
  const missing = [];
  for (let level = 0; level < available.length; level += 1) {
    const ranges = available[level];
    if (Array.isArray(ranges) && ranges.length > 0 && !(levelCounts.get(level) > 0)) {
      missing.push(level);
    }
  }
  return missing;
}

/** The zoom level a tile sits at, from where it is under the pyramid's root. */
function tileLevel(dir, path) {
  const level = Number(relative(dir, path).split(sep)[0]);
  return Number.isInteger(level) ? level : undefined;
}

async function checkCommand(options) {
  const dir = resolve(process.cwd(), String(options.dir ?? options.out ?? ''));
  if (!options.dir && !options.out) {
    fail('Usage: node deploy/terrain.mjs check --dir <terrain directory>');
  }
  const layerPath = join(dir, 'layer.json');
  if (!existsSync(layerPath)) {
    fail(
      `No layer.json in ${dir}.`,
      'A pyramid without it cannot be read at all. Bake into this directory first.',
    );
  }

  const layer = JSON.parse(readFileSync(layerPath, 'utf8'));
  const tiles = terrainFiles(dir);
  if (tiles.length === 0) {
    fail(`No .terrain tiles under ${dir}.`);
  }

  let bytes = 0;
  let gzipped = 0;
  const damaged = [];
  const levelCounts = new Map();
  for (const tile of tiles) {
    const size = statSync(tile).size;
    bytes += size;
    const kind = classifyTile(await firstBytes(tile, TILE_HEAD_BYTES), size);
    if (kind === 'damaged') {
      damaged.push(tile);
      continue;
    }
    if (kind === 'gzip') {
      gzipped += 1;
    }
    const level = tileLevel(dir, tile);
    if (level !== undefined) {
      levelCounts.set(level, (levelCounts.get(level) ?? 0) + 1);
    }
  }

  console.log('\n--- pyramid ---------------------------------------------------------------');
  console.log(`  directory   ${dir}`);
  console.log(`  format      ${layer.format}`);
  console.log(`  tiles       ${tiles.length}, ${(bytes / 1048576).toFixed(2)} MiB`);
  console.log(`  version     ${layer.version}`);
  console.log(`  attribution ${layer.attribution}`);
  if (typeof layer.attribution === 'string' && layer.attribution.startsWith('insert ')) {
    console.log('  WARNING: that is the pre-baker\'s placeholder, and a scene would display it.');
  }
  if (!String(layer.version ?? '').includes('-')) {
    console.log(
      '  WARNING: that version is the pre-baker\'s constant rather than one derived from these\n'
      + '  tiles. It goes on the end of every tile URL, so a browser that has seen ANY earlier\n'
      + '  pyramid at this address will keep drawing that one from its own cache instead, without\n'
      + '  a single request reaching this server. Re-run the bake through this script.',
    );
  }

  // Reported before the serving rule, because a serving rule for a pyramid with holes in it is
  // advice about the wrong problem.
  if (damaged.length > 0) {
    const shown = damaged.slice(0, 5).map((tile) => `    ${relative(dir, tile)}`).join('\n');
    fail(
      `  ${damaged.length} of ${tiles.length} tiles are neither a mesh nor a gzip stream:\n${shown}`
      + (damaged.length > 5 ? `\n    … and ${damaged.length - 5} more` : ''),
      '  Empty or cut short — the shape a bake that ran out of disk, or a copy that was'
      + '\n  interrupted, leaves behind. Bake again into an empty directory.',
    );
  }

  const gaps = missingLevels(layer.available, levelCounts);
  if (gaps.length > 0) {
    fail(
      gaps.length === 1
        ? `  This pyramid says it holds level ${gaps[0]} and has no tiles at it.`
        : `  This pyramid says it holds levels ${gaps.join(', ')} and has no tiles at any of them.`,
      '  It is incomplete. Nothing would report that while it was being looked at: the missing'
      + '\n  tiles answer 404, and the ground is quietly drawn from the coarser level above them'
      + '\n  — the wrong heights, presented as the right ones. Bake again into an empty directory.',
    );
  }

  const counted = [...levelCounts.keys()].sort((a, b) => a - b);
  console.log(
    `  levels      ${counted.map((level) => `${level}:${levelCounts.get(level)}`).join(' ')}`,
  );

  console.log('\n--- how these tiles must be served ----------------------------------------');
  if (gzipped === 0) {
    console.log(`  All ${tiles.length} tiles are UNCOMPRESSED.`);
    console.log('  → The web server must NOT declare Content-Encoding for them.');
  } else if (gzipped === tiles.length) {
    console.log(`  All ${tiles.length} tiles are gzip streams, under names that do not say so.`);
    console.log('  → Either the web server declares Content-Encoding: gzip for every one of');
    console.log('    them, or they are put into the pair the shipped nginx configuration');
    console.log('    expects: rename each tile to <name>.terrain.gz and decompress a copy back');
    console.log('    beside it under the original name (`gzip -dk`), so both files exist and');
    console.log('    `gzip_static on` picks whichever the browser can take and labels it itself.');
    console.log('    Renaming alone is not enough — that block answers 404 for a name it cannot');
    console.log('    find on disk, which would be every tile in the pyramid.');
  } else {
    fail(
      `  MIXED: ${gzipped} of ${tiles.length} tiles are gzip streams and the rest are not.`,
      '  No single Content-Encoding can describe this directory, so some tiles would always be'
      + '\n  served wrongly. Re-bake into an empty directory.',
    );
  }
  console.log('\n  The rule is that the declared encoding must describe the bytes. Get it wrong');
  console.log('  and every tile still answers 200, no error appears anywhere, and the globe is');
  console.log('  simply drawn with no ground on it.');
}

function printEnvironment(outDir, ellipsoidal) {
  console.log('\n--- add to deploy/.env ----------------------------------------------------');
  console.log(`SILEXGIS_TERRAIN_DIR=${outDir}`);
  console.log('SILEXGIS__Terrain__Url=/terrain/');
  if (ellipsoidal) {
    console.log('SILEXGIS__Terrain__HeightDatum=Ellipsoidal');
    console.log('# The local geoid undulation. Measured EGM2008 values over Romanian karst run');
    console.log('# +39 m to +45 m; the Apuseni is about +43. Leaving this at 0 with an');
    console.log('# ellipsoidal bake puts every cave that far below its hillside.');
    console.log('SILEXGIS__Terrain__GeoidHeightM=43.0');
  } else {
    console.log('# These tiles hold heights above sea level, which is the same kind of number a');
    console.log('# cave survey carries, so no correction is applied to surveyed altitudes.');
    console.log('SILEXGIS__Terrain__HeightDatum=Orthometric');
  }
  console.log(`SILEXGIS__Terrain__Attribution=${COPERNICUS_ATTRIBUTION}`);
  console.log('\nThen:');
  console.log('  docker compose -f docker-compose.yml -f docker-compose.terrain.yml up -d');
}

// Only when run, not when imported. The cell naming, the download URL and the pyramid version are
// exported and covered by terrain.test.mjs beside this file, and a test importing this module must
// not start downloading a country's worth of elevation as a side effect.
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const [command, ...rest] = process.argv.slice(2);
  const options = parseArgs(rest);

  switch (command) {
    case 'fetch':
      await fetchCommand(options);
      break;
    case 'bake':
      await bakeCommand(options);
      break;
    case 'check':
      await checkCommand(options);
      break;
    default:
      console.log('SilexGIS terrain pre-bake.\n');
      console.log('  node deploy/terrain.mjs fetch --bbox west,south,east,north --out <dem dir>');
      console.log(
        '  node deploy/terrain.mjs bake  --in <dem dir> --out <terrain dir> [--max-depth 13] [--datum ellipsoidal]',
      );
      console.log('  node deploy/terrain.mjs check --dir <terrain dir>');
      console.log('\nSee "Terrain" in docs/INSTALL.md.');
      process.exit(command ? 1 : 0);
  }
}
