// SPDX-License-Identifier: AGPL-3.0-or-later

// Build the elevation model the 3D view can draw its ground from.
// Cross-platform (Node 18+, no shell-isms). Four commands; `fetch` and `prepare` are two ways of
// arriving at the same place, and one of them is enough:
//
//   node deploy/terrain.mjs fetch --bbox 22,45,26,48 --out ./dem
//       Downloads the Copernicus GLO-30 tiles covering a longitude/latitude box from the AWS
//       open-data bucket. No account, no key, no signature. One 1°x1° tile is about 44 MB.
//       It knows this one source and nothing else.
//
//   node deploy/terrain.mjs prepare --in ./rasters --out ./dem [--pixel-size 30m]
//       Turns elevation rasters you already have — a national LiDAR set, an aerial survey, a
//       regional model, in whatever projection they came in — into the form the pre-baker reads:
//       EPSG:4326, one nodata value, optionally coarser. This is the path for every source that
//       cannot be downloaded by drawing a box, which is most of the good ones.
//
//   node deploy/terrain.mjs bake --in ./dem --out /srv/silexgis/terrain [--max-depth 13]
//       Turns them into a static tile pyramid, using the mago-3d-terrainer Docker image
//       (MPL-2.0, carries its own Java). Prints the exact .env lines for what it produced.
//       `fetch` leaves the credit its data requires beside the rasters and this picks it up;
//       for anything else, including a directory holding more than one source, --attribution "…"
//       says whose data the scene is drawing.
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
 * GDAL, for the raster work `prepare` does. Run from a container for the same reason the pre-baker
 * is: this script must add no dependency to the machine it runs on, and the install guide promises
 * in writing that no system GDAL is required on any path.
 *
 * Pinned to an exact release, and the reason is sharper here than it looks. The tag that is easy to
 * reach for, `alpine-small-latest`, is a nightly build of an unreleased development version — it
 * moves under you, and a reprojection is exactly the kind of operation whose defaults and warnings
 * shift between versions. An operator who prepares half a country today and the other half next
 * month must get the same arithmetic both times.
 */
const GDAL_IMAGE = 'ghcr.io/osgeo/gdal:alpine-small-3.11.4';

/**
 * What `prepare` writes into the voids. It is not a setting, and deliberately is not offered as
 * one: it is the value the pre-baker reads as "no data", and nothing on the bake command line
 * tells it a different one. Harmonising to any other sentinel would leave every void — a lake a
 * sensor could not see into, the edge of a survey, a gap between flight lines — meshed as ground
 * thousands of metres below the surrounding hillside, drawn as confidently as the real terrain,
 * with nothing anywhere reporting it. An option that can only be set wrongly is not an option.
 */
const BAKER_NODATA = -9999;

/**
 * Degrees of longitude per metre at the equator, for reading a pixel size given in metres.
 *
 * The target projection is geographic, so a pixel size is an angle; operators think in metres. The
 * conversion is exact only on the equator and on every meridian — away from the equator a degree of
 * longitude covers less ground, so at 45° a pixel asked for as 30 m is about 30 m north-south and
 * about 21 m east-west. That is the right way round: the pixel is never coarser than what was
 * asked for, and this is a request for a working resolution rather than a measurement.
 */
const METERS_PER_DEGREE = 111_320;

/**
 * What `prepare` will pick up as elevation to work on. Deliberately a list rather than "every file":
 * an operator's directory holds readme files, checksums and shapefiles alongside the rasters, and
 * handing those to a warp produces a failure that reads as though the elevation data were broken.
 */
const RASTER_EXTENSIONS = [
  '.tif', '.tiff', '.vrt', '.img', '.asc', '.hgt', '.dem', '.bil', '.dt0', '.dt1', '.dt2',
];

/** How gdalwarp is allowed to fill a pixel it has to invent. */
const RESAMPLING_METHODS = [
  'near', 'bilinear', 'cubic', 'cubicspline', 'lanczos', 'average', 'rms', 'min', 'max', 'med',
];

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
 *
 * It belongs to the one source `fetch` knows, and to nothing else. Anything else baked through
 * this script carries the credit its own data requires, given with `bake --attribution`.
 */
const COPERNICUS_ATTRIBUTION =
  'Copernicus DEM GLO-30 — © DLR e.V. 2010-2014 and © Airbus Defence and Space GmbH 2014-2018 '
  + 'provided under COPERNICUS by the European Union and ESA';

/** What `fetch` calls the data it downloaded, written into the pyramid beside the credit. */
const COPERNICUS_NAME = 'Copernicus DEM GLO-30';

/**
 * Where the commands that put rasters in a directory record whose data they are.
 *
 * The credit has to survive the gap between assembling elevation and baking it, which may be
 * days and is usually two separate commands typed by hand. Carrying it in the operator's head
 * is exactly how a pyramid ends up stamped with a credit belonging to data it does not contain
 * — a false licence statement, displayed on screen to everyone who looks at the scene, with
 * nothing anywhere to say it is wrong. So the credit travels with the data instead.
 *
 * It records a *list* of sources, and separately whether the directory also holds rasters whose
 * provenance is not recorded, because one directory holding more than one source is the
 * recommended way to work: a coarse national fill from `fetch` with fine local data from
 * `prepare` over the parts that have it. Recording a single credit per directory would make that
 * combination stamp the whole pyramid — somebody else's LiDAR included — with the credit of
 * whichever command wrote the file last. So `fetch` adds the one source it knows to whatever is
 * already recorded, `prepare` marks the directory as holding rasters it cannot speak for, and
 * `bake` either finds one recorded source covering all of it or refuses to guess and asks for
 * `--attribution`.
 */
const SOURCE_CREDIT_FILE = 'source-credit.json';

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
  // The credit is left with the data, so a bake days later still stamps the pyramid with the
  // licence statement belonging to what is actually in it. Added to whatever is already recorded
  // rather than written over it: this directory may already hold prepared rasters of somebody
  // else's, and overwriting would retro-credit them to a source that never covered them.
  recordSource(outDir, { attribution: COPERNICUS_ATTRIBUTION, name: COPERNICUS_NAME });
  console.log(`Recorded the credit this data's licence requires in ${SOURCE_CREDIT_FILE}.`);
  console.log(`Next: node deploy/terrain.mjs bake --in ${options.out} --out <terrain directory>`);
}

function writeCreditRecord(dir, record) {
  writeFileSync(
    join(dir, SOURCE_CREDIT_FILE),
    `${JSON.stringify(record, undefined, 2)}\n`,
    'utf8',
  );
}

/**
 * Adds one source to what a directory records, keeping everything already recorded there.
 *
 * Added rather than written over, which is the whole point: a directory that already holds
 * prepared rasters of somebody else's carries a mark saying so, and overwriting the record would
 * retro-credit those rasters to a source that never covered them.
 *
 * Exported for its own test; `fetch` is what an operator runs.
 */
export function recordSource(dir, source) {
  const record = readSourceCredit(dir) ?? { sources: [], unknown: false };
  const already = record.sources.some((held) => held.attribution === source.attribution);
  writeCreditRecord(dir, {
    sources: already ? record.sources : [...record.sources, source],
    unknown: record.unknown,
  });
}

/**
 * Marks a directory as holding rasters whose provenance this script does not know.
 *
 * `prepare` cannot know where an operator's own rasters came from, and the directory it writes
 * into is the very one `fetch` is recommended to write into as well. Leaving the mark off would
 * let a bake of that mixed directory inherit the downloaded data's credit and stamp it across
 * somebody else's survey — the exact false statement the record exists to prevent.
 *
 * Exported for its own test; `prepare` is what an operator runs.
 */
export function markSourceUnknown(dir) {
  const record = readSourceCredit(dir) ?? { sources: [], unknown: false };
  writeCreditRecord(dir, { sources: record.sources, unknown: true });
}

/**
 * What a directory records about whose data is in it, or nothing when it records nothing.
 *
 * Answers `{ sources, unknown }`: the credits some command left there, and whether the directory
 * also holds rasters nothing recorded a credit for. Nothing at all is an ordinary answer — a
 * directory of an operator's own rasters, never passed through `prepare` — and it is never
 * guessed at. A pyramid with no credit shows none; one with the wrong credit states something
 * false to everybody who looks at the scene.
 *
 * Exported for its own test; `bake` is what an operator runs.
 */
export function readSourceCredit(dir) {
  const path = join(dir, SOURCE_CREDIT_FILE);
  if (!existsSync(path)) {
    return undefined;
  }
  let record;
  try {
    record = JSON.parse(readFileSync(path, 'utf8'));
  } catch {
    // A file this script wrote and something else damaged. What it said is lost, but the fact
    // that something put data here worth recording is not — so the directory is treated as
    // holding rasters nothing can speak for, and the bake asks instead of inventing a credit.
    return { sources: [], unknown: true };
  }
  const sources = (Array.isArray(record?.sources) ? record.sources : [])
    .filter((source) => typeof source?.attribution === 'string' && source.attribution.trim() !== '')
    .map((source) => ({
      attribution: source.attribution,
      name: typeof source.name === 'string' ? source.name : undefined,
    }));
  const unknown = record?.unknown === true;
  return sources.length === 0 && !unknown ? undefined : { sources, unknown };
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

/**
 * A pixel size in degrees, from what an operator typed: a bare number is already degrees, a number
 * with an `m` after it is metres. Answers `undefined` for anything that is not a usable size, so
 * the caller can say so in its own words rather than passing NaN into a command line.
 */
export function pixelSizeDegrees(value) {
  const text = String(value).trim().toLowerCase();
  const metres = /^(\d*\.?\d+)\s*m$/.exec(text);
  if (metres) {
    const size = Number(metres[1]);
    return size > 0 ? size / METERS_PER_DEGREE : undefined;
  }
  const degrees = Number(text);
  return Number.isFinite(degrees) && degrees > 0 ? degrees : undefined;
}

/**
 * What one input raster is called once it has been prepared.
 *
 * The path it had under the input directory is folded into the name rather than reproduced as
 * directories, because a national tile set arrives as `2019/tile_0421.tif` and `2021/tile_0421.tif`
 * — the same base name twice, in a set where the second one is the point of having it. Flattening
 * without folding the path in would prepare one of them over the other and lose half the coverage
 * with nothing to say so. Everything outside the safe set becomes an underscore so the name works
 * on both platforms this script runs on.
 */
export function preparedName(relativePath) {
  const withoutExtension = relativePath.split(sep).join('/').replace(/\.[^./]+$/, '');
  return `${withoutExtension.replace(/[^A-Za-z0-9._-]+/g, '_')}.tif`;
}

function isRasterName(name) {
  const lower = name.toLowerCase();
  return RASTER_EXTENSIONS.some((extension) => lower.endsWith(extension));
}

/** Every elevation raster under a directory, at any depth. Exported for its own test. */
export function rasterFiles(dir) {
  return filesUnder(dir, isRasterName);
}

/**
 * Brings rasters an operator already has into the one shape the pre-baker can read.
 *
 * `fetch` knows exactly one source, by design. This is the general path, and it is the whole
 * distance between "I downloaded the national LiDAR tile set" and "I have terrain": those files
 * are in a national projection the pre-baker does not reproject, carry whatever nodata sentinel
 * their producer chose, and are often far finer than anything worth baking over a whole region.
 *
 * Three things happen to each raster, and each one is a silent failure if it does not:
 *
 *   - **Reprojected to EPSG:4326.** The pre-baker reads a raster's own georeferencing and does not
 *     transform it, so a raster in a national grid is placed as though its coordinates were
 *     degrees, which puts the terrain in the sea off west Africa rather than reporting an error.
 *   - **One nodata value, the one the pre-baker already looks for.** Otherwise a void is read as a
 *     real height and drawn as a pit thousands of metres deep.
 *   - **Optionally coarser.** The pre-baker derives its depth from the finest raster it is given,
 *     so half a metre of LiDAR asks for a depth that multiplies the tile count many times over.
 *
 * Each raster is reprojected on its own rather than merged into one mosaic first, which is
 * deliberate and is the opposite of what "prepare a mosaic" sounds like. The pre-baker takes a
 * directory and mosaics it itself, resolving overlaps in favour of the finer raster; it computes
 * the depth it can honestly serve per raster and advertises that coverage sparsely. Merging first
 * would resample everything onto one grid, so either the fine data is thrown away or the coarse
 * data is inflated to pretend to a detail it does not have — and the patchwork of a modest depth
 * over a region with real detail only where real detail exists, which is the entire point, would
 * be flattened away before the pre-baker ever saw it. Merging also produces exactly the one giant
 * input file the pre-baker is least happy with.
 */
async function prepareCommand(options) {
  if (!options.in || !options.out) {
    fail(
      'Usage: node deploy/terrain.mjs prepare --in <raster directory> --out <dem directory>'
      + '\n       [--pixel-size 30m] [--resampling bilinear]'
      + ' [--source-srs EPSG:3844] [--source-nodata -32768] [--force]',
      'The input is elevation rasters you already have, in any projection. The output is a\n'
      + 'directory of EPSG:4326 GeoTIFFs, which is what `bake` reads — the same place `fetch`\n'
      + 'writes to, so the two are interchangeable and can even be pointed at one directory.\n'
      + 'A directory holding data from more than one source is baked with --attribution, in\n'
      + 'words that cover all of it.',
    );
  }
  const inDir = resolve(process.cwd(), String(options.in));
  const outDir = resolve(process.cwd(), String(options.out));
  if (!existsSync(inDir)) {
    fail(`No such input directory: ${inDir}`);
  }
  if (directoriesOverlap(inDir, outDir)) {
    fail(
      '--in and --out must be separate directories, neither inside the other.',
      'Prepared rasters are written under names derived from the originals, and the input is\n'
      + 'searched at any depth — so an output directory anywhere below the input would be read\n'
      + 'as input on the next run, preparing a second copy of everything under a new name, and\n'
      + 'another on the run after that. The bake then mosaics the duplicates.',
    );
  }

  const rasters = rasterFiles(inDir);
  if (rasters.length === 0) {
    fail(
      `No elevation rasters under ${inDir}.`,
      `  Looked for ${RASTER_EXTENSIONS.join(' ')} at any depth below it.`,
    );
  }

  const pixelSizeOption = options['pixel-size'] ?? options.pixelSize;
  let pixelSize;
  if (pixelSizeOption !== undefined) {
    pixelSize = pixelSizeDegrees(pixelSizeOption);
    if (pixelSize === undefined) {
      fail(
        `--pixel-size must be a size, not ${JSON.stringify(pixelSizeOption)}.`,
        '  Either metres — --pixel-size 30m — or degrees, --pixel-size 0.000277778.\n'
        + '  Left out entirely, each raster keeps the resolution it already has, which is\n'
        + '  usually what you want: the pre-baker goes deep only where the data is fine.',
      );
    }
  }

  const resampling = String(options.resampling ?? 'bilinear').toLowerCase();
  if (!RESAMPLING_METHODS.includes(resampling)) {
    fail(
      `--resampling must be one of: ${RESAMPLING_METHODS.join(' ')}`,
      '  bilinear is the default and is right for elevation. Use average when coarsening a long\n'
      + '  way, which keeps a summit from being sampled away; never near, which aliases ridges.',
    );
  }

  mkdirSync(outDir, { recursive: true });
  // Recorded before anything is converted, not after: a run that stops half way has already put
  // rasters of unrecorded provenance in this directory, and a bake of it must not inherit some
  // other command's credit for them.
  markSourceUnknown(outDir);
  console.log(`${rasters.length} raster(s) under ${inDir}.`);
  if (pixelSize !== undefined) {
    console.log(`Resampling every one of them to ${pixelSize.toPrecision(6)}° per pixel.`);
  }

  let prepared = 0;
  let skipped = 0;
  for (const raster of rasters) {
    const relativePath = relative(inDir, raster);
    // Container paths are POSIX whatever the host separator is.
    const source = relativePath.split(sep).join('/');
    const target = preparedName(relativePath);
    const targetPath = join(outDir, target);
    if (!options.force && existsSync(targetPath) && statSync(targetPath).size > 0) {
      // Same bargain `fetch` makes: re-running after an interruption costs only what was left.
      // Only a file at the final name counts, and nothing incomplete ever reaches that name.
      console.log(`  ${source} → ${target} (already prepared)`);
      skipped += 1;
      continue;
    }

    const partial = `${target}${PARTIAL_SUFFIX}`;
    const args = prepareDockerArgs({
      inDir,
      outDir,
      source,
      target: partial,
      pixelSize,
      sourceSrs: options['source-srs'] ?? options.sourceSrs,
      sourceNodata: options['source-nodata'] ?? options.sourceNodata,
      resampling,
      platform: process.platform,
      // Only some platforms have these at all, and only on those does the answer mean anything.
      uid: typeof process.getuid === 'function' ? process.getuid() : undefined,
      gid: typeof process.getgid === 'function' ? process.getgid() : undefined,
    });

    console.log(`\n$ docker ${args.join(' ')}\n`);
    try {
      execFileSync('docker', args, { stdio: 'inherit' });
    } catch {
      // A warp that fails part way has already created its output file, and a half-written raster
      // at the final name is worse than none: the next run skips it and the bake is fed a hole.
      rmSync(join(outDir, partial), { force: true });
      fail(
        `Preparing ${source} failed.`,
        'Docker must be running, and the image is pulled on first use:\n'
        + `  docker pull ${GDAL_IMAGE}\n`
        + '  If the message above is about the source projection, the raster does not carry one.\n'
        + '  Say what it is: --source-srs EPSG:3844 for the Romanian national grid, for instance.',
      );
    }
    renameSync(join(outDir, partial), targetPath);
    prepared += 1;
  }

  console.log(`\nPrepared ${prepared}, already present ${skipped}. → ${outDir}`);
  console.log(
    `Next: node deploy/terrain.mjs bake --in ${options.out} --out <terrain directory>`
    + '\n        --attribution "the credit this data\'s licence requires"',
  );
}

/**
 * Whether two directories are the same one, or one is inside the other.
 *
 * `prepare` searches its input at any depth, so an output directory below the input is invisible
 * on the first run and is read as input on the second — a second full copy of the coverage under
 * a derived name, a third on the run after that, and a bake that mosaics all of them. Comparing
 * for equality alone catches only the case an operator was least likely to type.
 *
 * Case is folded on Windows, where two spellings of one path are one directory, and not on the
 * platforms where they are two.
 *
 * Exported for its own test; `prepare` is what an operator runs.
 */
export function directoriesOverlap(inDir, outDir, platform = process.platform) {
  const windows = platform === 'win32';
  // The separator of the platform being asked about rather than the one this process runs on, so
  // the rule can be exercised for both from either.
  const boundary = windows ? '\\' : '/';
  const fold = (path) => (windows ? path.toLowerCase() : path);
  const from = fold(inDir);
  const to = fold(outDir);
  return from === to || to.startsWith(from + boundary) || from.startsWith(to + boundary);
}

/**
 * The command line one raster is reprojected with.
 *
 * `--user` is here for the same reason it is on the pre-baker's command line, and with a sharper
 * consequence. On Linux a container writes into a bind mount as the user the image declares — root
 * — so without this the prepared rasters come back owned by root inside the operator's own
 * directory. The very next command is `bake`, and the pre-baker refuses to start at all against an
 * input directory it cannot write to. What the operator sees is a complaint about writability from
 * a different command, about a directory they created themselves, naming nothing that caused it.
 * On macOS and Windows ownership is mapped through a virtual machine and a host uid means nothing
 * inside that mapping, so passing it there would be wrong rather than merely unnecessary.
 *
 * The input is mounted read-only, which the pre-baker's cannot be: nothing here writes to it, and
 * an operator's only copy of a national LiDAR set is not a thing to leave writable by a container.
 * GDAL's habit of dropping `.aux.xml` sidecars beside anything it reads is turned off rather than
 * merely blocked, so a read-only source produces no warnings about files it did not need to write.
 *
 * Warp memory is bounded explicitly. Left alone, a warp over a large raster will take as much as
 * it can get, and this runs on the same machine as everything else.
 *
 * Exported for its own test; `prepare` is what an operator runs.
 */
export function prepareDockerArgs({
  inDir, outDir, source, target, pixelSize, sourceSrs, sourceNodata, resampling,
  platform, uid, gid,
}) {
  const args = ['run', '--rm'];
  if (platform === 'linux' && typeof uid === 'number' && typeof gid === 'number') {
    args.push('--user', `${uid}:${gid}`);
  }
  args.push(
    '-e', 'GDAL_PAM_ENABLED=NO',
    '-v', `${inDir}:/data/input:ro`,
    '-v', `${outDir}:/data/output`,
    GDAL_IMAGE,
    'gdalwarp',
    '-overwrite',
    '-t_srs', 'EPSG:4326',
    '-r', String(resampling),
    // Written into the voids, and declared, so the pre-baker recognises them as voids rather than
    // as ground far below the surrounding hillside. Fixed rather than settable: nothing on the
    // bake command line can tell the pre-baker to look for a different value, so any other one
    // would be meshed as real ground.
    '-dstnodata', String(BAKER_NODATA),
  );
  if (sourceSrs) {
    // For rasters that carry no projection of their own — plain ASCII grids usually do not.
    args.push('-s_srs', String(sourceSrs));
  }
  if (sourceNodata !== undefined) {
    // For rasters that use a sentinel without declaring it, which would otherwise warp through as
    // an ordinary height.
    args.push('-srcnodata', String(sourceNodata));
  }
  if (pixelSize !== undefined) {
    // -tap snaps every output to one global grid of this size, so rasters prepared in separate
    // runs still line up pixel for pixel where they meet.
    args.push('-tr', String(pixelSize), String(pixelSize), '-tap');
  }
  args.push(
    '-of', 'COG',
    '-co', 'COMPRESS=DEFLATE',
    '-co', 'BIGTIFF=IF_SAFER',
    '-multi',
    '-wo', 'NUM_THREADS=ALL_CPUS',
    '-wm', '512',
    `/data/input/${source}`,
    `/data/output/${target}`,
  );
  return args;
}

async function bakeCommand(options) {
  if (!options.in || !options.out) {
    fail(
      'Usage: node deploy/terrain.mjs bake --in <dem directory> --out <terrain directory>'
      + '\n       [--max-depth 13] [--datum ellipsoidal] [--attribution "credit …"]',
      'The input directory is what `fetch` or `prepare` wrote. The output is what a web server\n'
      + 'will serve. Anything but a plain `fetch` directory needs --attribution: the credit is\n'
      + 'written into the pyramid and shown on the 3D scene, so for a directory holding more\n'
      + 'than one source it has to be words that cover all of them.',
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
  // What the scene will display as the credit for these heights. An operator's own word beats
  // anything recorded beside the rasters, which in turn beats nothing — and nothing is left as
  // nothing rather than filled in with the one source this script happens to know how to
  // download, because a credit naming data the pyramid does not contain is a false statement
  // that only the operator can see is false.
  if (options.attribution === true) {
    fail(
      '--attribution needs the credit itself.',
      '  For example: --attribution "Elevation data © National Mapping Agency, 2024".',
    );
  }
  const recorded = readSourceCredit(inDir);
  const { credit, partial } = bakeCredit({ attribution: options.attribution, recorded });
  if (partial) {
    // Refused before the bake rather than after it. The pyramid would take hours and would then
    // carry, as its one displayed licence statement, a credit true of only part of what is in it
    // — and nothing downstream could tell that it was wrong. A credit covering everything is
    // something only the operator can write, so this is where it is asked for.
    fail(
      'This directory holds data from more than one source, and no single recorded credit\n'
      + 'covers all of it. Bake it with one that does.',
      `  Recorded beside these rasters:\n${
        partial.map((source) => `    ${source.attribution}`).join('\n')}\n`
      + '  and rasters prepared from elsewhere, whose credit only you know.\n\n'
      + '  Re-run with a credit naming every source, for example:\n'
      + `    --attribution "${partial[0].attribution} · <the credit your own data requires>"`,
    );
  }
  if (credit && recorded && recorded.sources.length > 0 && options.attribution) {
    // Given by hand, which replaces what was recorded rather than joining it. Worth saying out
    // loud, because the recorded source is in this bake too and its licence still applies.
    console.log('\nWhat you passed replaces the credit recorded beside these rasters:');
    for (const source of recorded.sources) {
      console.log(`  ${source.attribution}`);
    }
    console.log('That data is in this bake as well, so make sure your words cover it.');
  }
  if (!credit) {
    console.log(
      '\nNo credit for this data: none was recorded beside the rasters and --attribution was not\n'
      + 'given, so the pyramid will carry no attribution at all and the scene will show none.\n'
      + 'If the data you are baking requires one, stop now and pass --attribution.',
    );
  }

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
    finishLayerJson(outDir, credit);
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
  printEnvironment(outDir, ellipsoidal, credit);
}

/**
 * The credit a bake will stamp its pyramid with, from what the operator said and what the input
 * directory records about itself.
 *
 * Answers `{ credit }` — the credit to stamp, or nothing to stamp at all — except in the one case
 * where the recorded credits are real but do not account for everything in the directory, which
 * answers `{ partial }` instead: the credits that were recorded, for the caller to show while it
 * refuses. That case is the whole reason this is not a one-liner. Mixing a coarse regional fill
 * with fine local rasters in one directory is the recommended way to work, and the naive answer —
 * take whatever credit is recorded there — stamps a pyramid substantially made of somebody's
 * survey with the licence statement of the data that happened to be downloaded into it. That is a
 * false statement, displayed on the scene, which nobody looking at it can tell is false.
 *
 * Exported for its own test; `bake` is what an operator runs.
 */
export function bakeCredit({ attribution, recorded }) {
  const given = typeof attribution === 'string' && attribution.trim() !== ''
    ? attribution.trim()
    : undefined;
  const sources = recorded?.sources ?? [];
  if (given) {
    // Given by hand, so it names the data in front of the operator rather than the data some
    // earlier command left in that directory. It carries no source name: a name and a credit
    // that disagreed would be worse than one credit on its own.
    return { credit: { attribution: given, name: undefined } };
  }
  if (sources.length === 1 && recorded?.unknown !== true) {
    // One recorded source and nothing else in the directory — a plain `fetch` output. This is the
    // case the record was written for, and the one where nothing needs to be typed.
    return { credit: { attribution: sources[0].attribution, name: sources[0].name } };
  }
  if (sources.length === 0) {
    // Rasters an operator brought themselves, whose credit this script could never have known.
    // Left as nothing rather than filled in with the one source it happens to know how to
    // download; the caller says so plainly.
    return { credit: undefined };
  }
  return { credit: undefined, partial: sources };
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
 */
function finishLayerJson(outDir, credit) {
  const layerPath = join(outDir, 'layer.json');
  if (!existsSync(layerPath)) {
    return;
  }
  const layer = describedLayer(JSON.parse(readFileSync(layerPath, 'utf8')), credit);
  layer.version = pyramidVersion(pyramidDigest(outDir));
  writeFileSync(layerPath, `${JSON.stringify(layer)}\n`, 'utf8');
}

/**
 * The pyramid's own description of itself, with the credit for the data it holds.
 *
 * The pre-baker writes `"attribution": "insert attribution here"` into every pyramid it produces,
 * and a scene that shows its terrain's credit shows exactly that — a placeholder is worse than
 * nothing, because it is displayed. So the placeholders go whether or not there is anything to
 * put in their place: an empty credit shows nothing, which is at least not a claim.
 *
 * A credit given for this bake replaces whatever is there, including a real-looking credit left
 * by an earlier bake into the same directory. Only overwriting placeholders would mean a
 * directory re-baked with different data kept the first data's licence statement.
 *
 * Exported for its own test; `bake` is what an operator runs.
 */
export function describedLayer(layer, credit) {
  const described = { ...layer };
  const placeholder = (value) => typeof value !== 'string' || value.startsWith('insert ');

  // The name and the credit are one statement about one set of data, so they move together. A
  // name kept from an earlier bake beside a credit given for this one would have the pyramid
  // naming one source and crediting another.
  if (credit?.attribution) {
    described.attribution = credit.attribution;
    if (credit.name) {
      described.name = credit.name;
    } else {
      delete described.name;
    }
  } else {
    if (placeholder(described.attribution)) {
      delete described.attribution;
    }
    if (placeholder(described.name)) {
      delete described.name;
    }
  }
  if (placeholder(described.description)) {
    described.description = 'Elevation model baked for SilexGIS.';
  }
  delete described.legend;
  return described;
}

/** Every file under a directory, at any depth, whose name the caller accepts. */
function filesUnder(dir, matches) {
  const found = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) {
      found.push(...filesUnder(path, matches));
    } else if (matches(entry.name)) {
      found.push(path);
    }
  }
  return found;
}

/** Every `.terrain` file under a directory. */
function terrainFiles(dir) {
  return filesUnder(dir, (name) => name.endsWith('.terrain'));
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
  console.log(`  attribution ${layer.attribution ?? '(none)'}`);
  if (typeof layer.attribution === 'string' && layer.attribution.startsWith('insert ')) {
    console.log('  WARNING: that is the pre-baker\'s placeholder, and a scene would display it.');
  } else if (layer.attribution === undefined) {
    console.log('  None is shown on the scene. If this data\'s licence requires a credit, re-bake');
    console.log('  with --attribution "…"; nothing else writes it.');
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

function printEnvironment(outDir, ellipsoidal, credit) {
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
  if (credit?.attribution) {
    console.log(`SILEXGIS__Terrain__Attribution=${credit.attribution}`);
  } else {
    console.log('# Nothing was baked with a credit, so there is none to show. If this data');
    console.log('# requires one, re-bake with --attribution "…" and paste the line it prints.');
    console.log('# SILEXGIS__Terrain__Attribution=');
  }
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
    case 'prepare':
      await prepareCommand(options);
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
        '  node deploy/terrain.mjs prepare --in <raster dir> --out <dem dir> [--pixel-size 30m]',
      );
      console.log(
        '  node deploy/terrain.mjs bake  --in <dem dir> --out <terrain dir> [--max-depth 13]'
        + ' [--datum ellipsoidal] [--attribution "credit …"]',
      );
      console.log('  node deploy/terrain.mjs check --dir <terrain dir>');
      console.log('\nSee "Terrain" in docs/INSTALL.md.');
      process.exit(command ? 1 : 0);
  }
}
