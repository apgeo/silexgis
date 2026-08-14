// SPDX-License-Identifier: AGPL-3.0-or-later

// Tests for the terrain pre-bake script: which elevation cells cover a box, where each one is
// downloaded from, what a downloaded cell is allowed to leave behind, which of an operator's own
// files are elevation and what each becomes once prepared, how GDAL and the pre-baker are run,
// whose data a pyramid says it holds, what a baked pyramid publishes for itself, and how a tile
// is told apart from a fragment.
//
// Run from the repository root with `node --test "deploy/**/*.test.mjs"` — the pattern is quoted
// because Node expands it itself. It is the built-in runner, so this needs no toolchain
// of its own and no dependency. Importing the script does not run it: it only acts when it is the
// entry point, which is what keeps a test from starting a download of a country's worth of
// elevation. The one test that transfers anything serves it from a loopback socket it opened
// itself, so nothing here reaches a network.

import { strict as assert } from 'node:assert';
import { createServer } from 'node:http';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, it } from 'node:test';

import {
  bakeCredit,
  bakeDockerArgs,
  classifyTile,
  copernicusCells,
  copernicusUrl,
  describedLayer,
  directoriesOverlap,
  downloadCell,
  markSourceUnknown,
  missingLevels,
  preparedName,
  prepareDockerArgs,
  pixelSizeDegrees,
  pyramidVersion,
  rasterFiles,
  readSourceCredit,
  recordSource,
} from './terrain.mjs';

/** A scratch directory tree, removed when the run ends. */
const scratchDirectories = [];
after(() => {
  for (const dir of scratchDirectories) {
    rmSync(dir, { recursive: true, force: true });
  }
});

function scratchTree(files) {
  const root = mkdtempSync(join(tmpdir(), 'silexgis-terrain-'));
  scratchDirectories.push(root);
  for (const file of files) {
    const path = join(root, ...file.split('/'));
    mkdirSync(join(path, '..'), { recursive: true });
    writeFileSync(path, 'x');
  }
  return root;
}

describe('copernicusCells', () => {
  it('covers a one-degree box with the single cell containing it', () => {
    assert.deepEqual(copernicusCells(22, 46, 23, 47), ['N46_00_E022_00']);
  });

  it('names a cell for its south-west corner, not the corner nearest the equator', () => {
    // The cell spanning 1°S up to the equator is S01, and the one spanning the equator up to 1°N
    // is N00. Getting this backwards asks the bucket for a name that does not exist and reads as
    // a missing tile rather than as a bug, which is why it is pinned here.
    assert.deepEqual(copernicusCells(15, -1, 16, 0), ['S01_00_E015_00']);
    assert.deepEqual(copernicusCells(15, 0, 16, 1), ['N00_00_E015_00']);
  });

  it('names western cells for their west edge the same way', () => {
    assert.deepEqual(copernicusCells(-1, 46, 0, 47), ['W001']
      .map(() => 'N46_00_W001_00'));
    assert.deepEqual(copernicusCells(0, 46, 1, 47), ['N46_00_E000_00']);
  });

  it('pads latitude to two digits and longitude to three', () => {
    assert.deepEqual(copernicusCells(5, 7, 6, 8), ['N07_00_E005_00']);
  });

  it('fans a larger box out over every whole cell it touches, in row order', () => {
    assert.deepEqual(copernicusCells(22, 46, 24, 48), [
      'N46_00_E022_00',
      'N46_00_E023_00',
      'N47_00_E022_00',
      'N47_00_E023_00',
    ]);
  });

  it('includes the cell a fractional edge falls inside', () => {
    // A box from 22.4 to 23.6 needs both the cell starting at 22 and the one starting at 23,
    // because part of the box lies in each. Rounding the box inwards would silently leave a strip
    // of the requested area with no elevation under it.
    assert.deepEqual(copernicusCells(22.4, 46.4, 23.6, 46.6), ['N46_00_E022_00', 'N46_00_E023_00']);
  });

  it('does not include a cell the box only just stops short of', () => {
    assert.deepEqual(copernicusCells(22, 46, 23, 47), ['N46_00_E022_00']);
    assert.equal(copernicusCells(22, 46, 23.000001, 47).length, 2);
  });
});

describe('copernicusUrl', () => {
  it('builds the open-data path, where the directory repeats the file name', () => {
    assert.equal(
      copernicusUrl('N46_00_E022_00'),
      'https://copernicus-dem-30m.s3.amazonaws.com/Copernicus_DSM_COG_10_N46_00_E022_00_DEM'
      + '/Copernicus_DSM_COG_10_N46_00_E022_00_DEM.tif',
    );
  });
});

describe('pyramidVersion', () => {
  it('carries the digest, so two different pyramids never share a tile URL', () => {
    const a = pyramidVersion('a'.repeat(64));
    const b = pyramidVersion('b'.repeat(64));
    assert.notEqual(a, b);
    assert.match(a, /^1\.1\.0-[0-9a-f]{12}$/);
  });

  it('is stable for the same digest, so re-baking identical tiles leaves caches warm', () => {
    assert.equal(pyramidVersion('c'.repeat(64)), pyramidVersion('c'.repeat(64)));
  });

  it('is distinguishable from the pre-baker\'s constant', () => {
    // The check command tells an operator their pyramid is unstamped by looking for the hyphen,
    // because an unstamped one puts the same string on the end of every tile URL it has ever
    // produced and a browser then keeps drawing whichever pyramid it saw first.
    assert.ok(!'1.1.0'.includes('-'));
    assert.ok(pyramidVersion('d'.repeat(64)).includes('-'));
  });

  it('is safe to put in a URL query', () => {
    assert.equal(encodeURIComponent(pyramidVersion('e'.repeat(64))), pyramidVersion('e'.repeat(64)));
  });
});

describe('pixelSizeDegrees', () => {
  it('reads metres, because nobody holds a LiDAR set measured in degrees', () => {
    // The target projection is geographic, so the command line wants an angle, and an operator who
    // types 30 into it without noticing has asked for a pixel thirty degrees across — one raster
    // covering a third of the planet. Accepting the unit is what makes that unwritable.
    const thirtyMetres = pixelSizeDegrees('30m');
    assert.ok(Math.abs(thirtyMetres - 30 / 111_320) < 1e-12);
    assert.equal(pixelSizeDegrees('30 m'), thirtyMetres);
    assert.equal(pixelSizeDegrees('0.5m'), 0.5 / 111_320);
  });

  it('takes a bare number as degrees it was already given in', () => {
    assert.equal(pixelSizeDegrees('0.000277778'), 0.000277778);
    assert.equal(pixelSizeDegrees(0.001), 0.001);
  });

  it('answers with nothing for anything that is not a size', () => {
    // `true` is what the argument parser produces for a flag whose value was left off, and it must
    // not arrive at a command line as the string "true" or as NaN.
    for (const value of [true, '', 'coarse', '0', '-30', '-30m', 'm', '30km', undefined]) {
      assert.equal(pixelSizeDegrees(value), undefined, String(value));
    }
  });
});

describe('preparedName', () => {
  it('folds the path into the name, so two tiles with one name both survive', () => {
    // A national tile set arrives as one directory per survey year, with the same tile names in
    // each — and the later year is usually the reason for having it. Flattening on the base name
    // alone prepares one over the other, and half the coverage is gone with nothing said.
    assert.equal(preparedName('2019/tile_0421.tif'), '2019_tile_0421.tif');
    assert.equal(preparedName('2021/tile_0421.tif'), '2021_tile_0421.tif');
    assert.notEqual(preparedName('2019/tile_0421.tif'), preparedName('2021/tile_0421.tif'));
  });

  it('reads a Windows path the same way it reads a POSIX one', () => {
    assert.equal(preparedName('2019\\tile_0421.tif'), '2019_tile_0421.tif');
  });

  it('always ends .tif, whatever went in', () => {
    assert.equal(preparedName('grid.asc'), 'grid.tif');
    assert.equal(preparedName('N46E023.HGT'), 'N46E023.tif');
    assert.equal(preparedName('lidar/zona 1 (final).img'), 'lidar_zona_1_final_.tif');
  });
});

describe('rasterFiles', () => {
  it('finds elevation at any depth and leaves everything else alone', () => {
    // An operator's delivery directory holds the readme, the checksums and the index shapefile
    // beside the rasters. Handing one of those to a warp fails with a message about the file
    // being unreadable, which reads as though the elevation data itself were broken.
    const root = scratchTree([
      'readme.txt',
      'MD5SUMS',
      'index.shp',
      'index.dbf',
      'top.tif',
      '2019/tile_0421.tif',
      '2019/nested/deeper/grid.asc',
      '2021/N46E023.HGT',
    ]);
    const found = rasterFiles(root).map((path) => path.slice(root.length + 1).split('\\').join('/'));
    assert.deepEqual(found.sort(), [
      '2019/nested/deeper/grid.asc',
      '2019/tile_0421.tif',
      '2021/N46E023.HGT',
      'top.tif',
    ]);
  });

  it('says so plainly when a directory holds no elevation at all', () => {
    assert.deepEqual(rasterFiles(scratchTree(['readme.txt'])), []);
  });
});

describe('directoriesOverlap', () => {
  // `prepare` searches its input at any depth, so an output directory below the input is invisible
  // on the first run and read as input on the second: a full second copy of the coverage under a
  // derived name, a third on the run after, and a bake that mosaics the duplicates. Nothing
  // reports it — the guard is the only thing between an operator and a directory that grows
  // without bound on a machine where free space is a named risk.
  it('refuses a directory inside the other, in either direction, as well as the same one', () => {
    for (const platform of ['linux', 'win32']) {
      const root = platform === 'win32' ? 'C:\\data' : '/data';
      const sep = platform === 'win32' ? '\\' : '/';
      assert.ok(directoriesOverlap(root, root, platform), platform);
      assert.ok(directoriesOverlap(root, `${root}${sep}dem`, platform), platform);
      assert.ok(directoriesOverlap(root, `${root}${sep}a${sep}b${sep}dem`, platform), platform);
      assert.ok(directoriesOverlap(`${root}${sep}rasters`, root, platform), platform);
    }
  });

  it('allows the ordinary pair of siblings, and a name that merely starts the same', () => {
    // `/data/rasters-old` is not inside `/data/rasters`, and refusing it would be a refusal an
    // operator cannot argue with and cannot work around.
    assert.ok(!directoriesOverlap('/data/rasters', '/data/dem', 'linux'));
    assert.ok(!directoriesOverlap('/data/rasters', '/data/rasters-old', 'linux'));
    assert.ok(!directoriesOverlap('C:\\data\\rasters', 'D:\\dem', 'win32'));
  });

  it('folds case only where two spellings are one directory', () => {
    assert.ok(directoriesOverlap('C:\\Data', 'c:\\data\\dem', 'win32'));
    assert.ok(!directoriesOverlap('/Data', '/data/dem', 'linux'));
  });
});

describe('prepareDockerArgs', () => {
  const common = {
    inDir: '/rasters',
    outDir: '/dem',
    source: '2019/tile_0421.tif',
    target: '2019_tile_0421.tif.part',
    resampling: 'bilinear',
  };

  it('hands the container the invoking user on Linux, where ownership passes straight through', () => {
    // Without this the prepared rasters come back owned by root inside the operator's own
    // directory, and the very next command is the pre-baker, which refuses to start against an
    // input directory it cannot write to. The complaint then names a different command and a
    // directory the operator created themselves, and nothing that caused it.
    const args = prepareDockerArgs({ ...common, platform: 'linux', uid: 1000, gid: 1000 });
    assert.equal(args.indexOf('--user'), 2);
    assert.equal(args[3], '1000:1000');
  });

  it('does not on the platforms that map ownership through a virtual machine', () => {
    for (const platform of ['win32', 'darwin']) {
      const args = prepareDockerArgs({ ...common, platform, uid: undefined, gid: undefined });
      assert.ok(!args.includes('--user'), platform);
    }
    // A Linux host whose runtime does not publish a uid is left alone too, rather than guessed at.
    assert.ok(
      !prepareDockerArgs({ ...common, platform: 'linux', uid: undefined, gid: undefined })
        .includes('--user'),
    );
  });

  it('reprojects to EPSG:4326 and declares the void value the pre-baker looks for', () => {
    assert.deepEqual(prepareDockerArgs({ ...common, platform: 'win32' }), [
      'run', '--rm',
      '-e', 'GDAL_PAM_ENABLED=NO',
      '-v', '/rasters:/data/input:ro',
      '-v', '/dem:/data/output',
      'ghcr.io/osgeo/gdal:alpine-small-3.11.4',
      'gdalwarp',
      '-overwrite',
      '-t_srs', 'EPSG:4326',
      '-r', 'bilinear',
      '-dstnodata', '-9999',
      '-of', 'COG',
      '-co', 'COMPRESS=DEFLATE',
      '-co', 'BIGTIFF=IF_SAFER',
      '-multi',
      '-wo', 'NUM_THREADS=ALL_CPUS',
      '-wm', '512',
      '/data/input/2019/tile_0421.tif',
      '/data/output/2019_tile_0421.tif.part',
    ]);
  });

  it('writes the one void value the pre-baker is ever run on, which nothing can change', () => {
    // These two command lines are two halves of one agreement. The warp declares -9999 as the
    // void value; the pre-baker is never told a void value at all, so it uses its own default,
    // which is that one. Harmonise to anything else without also telling the pre-baker and every
    // void — a lake, the edge of a survey, a gap between flight lines — is meshed as real ground
    // thousands of metres below the hillside around it, with nothing reporting a thing. That is
    // why there is no option for it, and this pins both sides of the agreement together.
    const prepared = prepareDockerArgs({ ...common, platform: 'win32' });
    assert.equal(prepared[prepared.indexOf('-dstnodata') + 1], '-9999');
    const baked = bakeDockerArgs({
      inDir: '/dem', outDir: '/out', maxDepth: 13, ellipsoidal: false, platform: 'win32',
    });
    assert.ok(!baked.includes('-nv'), 'the pre-baker is told no void value');
    assert.ok(!baked.includes('--nodataValue'), 'the pre-baker is told no void value');
  });

  it('mounts the operator\'s own rasters read-only, and only the output writable', () => {
    // The pre-baker's input cannot be read-only because it insists on being able to write there.
    // Nothing in a reprojection does, and an operator's only copy of a national LiDAR delivery is
    // not a thing to leave writable by a container.
    const args = prepareDockerArgs({ ...common, platform: 'win32' });
    assert.ok(args.includes('/rasters:/data/input:ro'));
    assert.ok(args.includes('/dem:/data/output'));
    assert.ok(!args.includes('/dem:/data/output:ro'));
  });

  it('asks for a resolution only when one was given, and snaps it to a shared grid', () => {
    // Left out, every raster keeps what it has — which is the point: the pre-baker goes deep only
    // where the data is fine, and forcing one resolution across a mixed set destroys exactly that.
    assert.ok(!prepareDockerArgs({ ...common, platform: 'win32' }).includes('-tr'));
    const args = prepareDockerArgs({ ...common, platform: 'win32', pixelSize: 0.000277778 });
    assert.deepEqual(
      args.slice(args.indexOf('-tr'), args.indexOf('-tr') + 4),
      ['-tr', '0.000277778', '0.000277778', '-tap'],
    );
  });

  it('passes a stated source projection and sentinel through, and nothing when unstated', () => {
    const plain = prepareDockerArgs({ ...common, platform: 'win32' });
    assert.ok(!plain.includes('-s_srs'));
    assert.ok(!plain.includes('-srcnodata'));
    const stated = prepareDockerArgs({
      ...common, platform: 'win32', sourceSrs: 'EPSG:3844', sourceNodata: -32768,
    });
    assert.equal(stated[stated.indexOf('-s_srs') + 1], 'EPSG:3844');
    assert.equal(stated[stated.indexOf('-srcnodata') + 1], '-32768');
  });
});

describe('bakeDockerArgs', () => {
  const common = { inDir: '/dem', outDir: '/out', maxDepth: 13, ellipsoidal: false };

  it('hands the container the invoking user on Linux, where ownership passes straight through', () => {
    // Without this the pyramid comes back owned by root inside the operator's own directory, and
    // this script can then neither stamp it with the version every tile URL is built from nor
    // replace the pre-baker's placeholder credit with the one the data's licence requires. Both
    // failures show up months later and somewhere else, which is why the flag is pinned here.
    const args = bakeDockerArgs({ ...common, platform: 'linux', uid: 1000, gid: 1000 });
    assert.equal(args.indexOf('--user'), 2);
    assert.equal(args[3], '1000:1000');
  });

  it('does not on the platforms that map ownership through a virtual machine', () => {
    for (const platform of ['win32', 'darwin']) {
      const args = bakeDockerArgs({ ...common, platform, uid: undefined, gid: undefined });
      assert.ok(!args.includes('--user'), platform);
    }
    // A Linux host whose runtime does not publish a uid is left alone too, rather than guessed at.
    assert.ok(
      !bakeDockerArgs({ ...common, platform: 'linux', uid: undefined, gid: undefined })
        .includes('--user'),
    );
  });

  it('mounts both directories and passes the depth and the datum through', () => {
    const args = bakeDockerArgs({ ...common, platform: 'win32', maxDepth: 9, ellipsoidal: true });
    assert.deepEqual(args, [
      'run', '--rm',
      '-v', '/dem:/data/input',
      '-v', '/out:/data/output',
      'gaia3d/mago-3d-terrainer:1.14.2-release',
      '-i', '/data/input',
      '-o', '/data/output',
      '-max', '9',
      '-g', 'EGM2008',
    ]);
  });
});

/**
 * The fixed header of a quantized mesh, as a generator writes it: the tile's centre as three
 * little-endian doubles, its lowest and highest elevation as two little-endian floats, a bounding
 * sphere and an occlusion point that nothing here reads, and then the number of vertices.
 */
function meshHead({ centerX = 4_200_000.5, lowest = 210.5, highest = 1840.25, vertices = 1200 } = {}) {
  const head = Buffer.alloc(92);
  head.writeDoubleLE(centerX, 0);
  head.writeDoubleLE(1_700_000.25, 8);
  head.writeDoubleLE(4_600_000.75, 16);
  head.writeFloatLE(lowest, 24);
  head.writeFloatLE(highest, 28);
  head.writeUInt32LE(vertices, 88);
  return head;
}

describe('bakeCredit', () => {
  const copernicus = {
    attribution: 'Copernicus DEM GLO-30 — © DLR e.V. 2010-2014 and © Airbus Defence and Space '
      + 'GmbH 2014-2018 provided under COPERNICUS by the European Union and ESA',
    name: 'Copernicus DEM GLO-30',
  };
  const fetched = { sources: [copernicus], unknown: false };

  it('takes the operator\'s word over anything recorded beside the rasters', () => {
    // The recorded credit belongs to whatever a downloader last left in that directory, and an
    // operator who says what they are baking knows better than the directory does. The other way
    // round stamps a pyramid with a licence statement for data it does not hold — a false claim,
    // displayed on the scene, that only somebody who knows what was baked can see is false.
    const { credit } = bakeCredit({
      attribution: 'Elevation © National Agency', recorded: fetched,
    });
    assert.equal(credit.attribution, 'Elevation © National Agency');
    assert.ok(!credit.attribution.includes('Copernicus'));
    assert.equal(credit.name, undefined);
  });

  it('falls back to the credit a download recorded, which is why it is recorded', () => {
    assert.deepEqual(bakeCredit({ attribution: undefined, recorded: fetched }).credit, copernicus);
  });

  it('answers nothing when there is nothing, not the one source it can download', () => {
    // Rasters an operator brought themselves have no credit this script could have known. The
    // Copernicus text is the default only where Copernicus data was actually downloaded.
    for (const attribution of [undefined, true, '', '   ']) {
      assert.deepEqual(
        bakeCredit({ attribution, recorded: undefined }), { credit: undefined }, String(attribution),
      );
    }
    // Same answer for a directory `prepare` wrote and nothing else: rasters are there, nothing
    // recorded a credit for them, and none is invented.
    assert.deepEqual(
      bakeCredit({ attribution: undefined, recorded: { sources: [], unknown: true } }),
      { credit: undefined },
    );
  });

  it('refuses to credit a whole directory to one source when it holds more than one', () => {
    // THIS is the mixed directory the install guide recommends building: a coarse national fill
    // downloaded by `fetch` with fine local rasters prepared into the same place. Taking the one
    // recorded credit would stamp the pyramid — LiDAR and all — as Copernicus data, print that as
    // the environment line, and display it on the scene as the only statement about whose data it
    // is. Nothing downstream could tell it was wrong, so the refusal has to be here.
    const answer = bakeCredit({
      attribution: undefined,
      recorded: { sources: [copernicus], unknown: true },
    });
    assert.equal(answer.credit, undefined);
    assert.deepEqual(answer.partial, [copernicus]);
  });

  it('is satisfied by an operator who says what covers all of it', () => {
    const { credit, partial } = bakeCredit({
      attribution: `${copernicus.attribution} · Elevation © National Agency`,
      recorded: { sources: [copernicus], unknown: true },
    });
    assert.equal(partial, undefined);
    assert.ok(credit.attribution.includes('Copernicus'));
    assert.ok(credit.attribution.includes('National Agency'));
  });
});

describe('describedLayer', () => {
  const placeholders = {
    name: 'insert name here',
    description: 'insert description here',
    attribution: 'insert attribution here',
    legend: 'insert legend here',
    format: 'quantized-mesh-1.0',
  };

  it('never leaves the pre-baker\'s placeholder behind, with a credit or without one', () => {
    // The placeholder is not inert: a scene showing its terrain's credit shows that sentence to
    // everybody looking at it. Removing it with nothing to put in its place is still better,
    // because no credit is at least not a claim about somebody else's data.
    const credited = describedLayer(placeholders, { attribution: 'Elevation © National Agency' });
    assert.equal(credited.attribution, 'Elevation © National Agency');
    assert.ok(!credited.attribution.includes('Copernicus'));

    const uncredited = describedLayer(placeholders, undefined);
    assert.equal(uncredited.attribution, undefined);
    assert.equal(uncredited.name, undefined);
    for (const layer of [credited, uncredited]) {
      assert.equal(layer.legend, undefined);
      assert.ok(!JSON.stringify(layer).includes('insert '));
      assert.equal(layer.format, 'quantized-mesh-1.0');
    }
  });

  it('replaces a real credit left by an earlier bake into the same directory', () => {
    // Only overwriting placeholders would mean a directory re-baked from different data kept the
    // first data's licence statement, which is the same false claim by a slower route.
    const previous = { attribution: 'Copernicus DEM GLO-30 — © DLR e.V.', name: 'Copernicus' };
    const rebaked = describedLayer(previous, { attribution: 'Elevation © National Agency' });
    assert.equal(rebaked.attribution, 'Elevation © National Agency');
    assert.equal(rebaked.name, undefined);
  });

  it('leaves the layer it was given alone', () => {
    const original = { ...placeholders };
    describedLayer(original, { attribution: 'Elevation © National Agency' });
    assert.deepEqual(original, placeholders);
  });
});

describe('readSourceCredit', () => {
  const written = (contents) => {
    const dir = mkdtempSync(join(tmpdir(), 'silexgis-terrain-'));
    scratchDirectories.push(dir);
    if (contents !== undefined) {
      writeFileSync(join(dir, 'source-credit.json'), contents, 'utf8');
    }
    return dir;
  };

  it('reads back what a download recorded beside the cells it downloaded', () => {
    const dir = written(
      '{"sources":[{"attribution":"Elevation © National Agency","name":"National DEM"}],'
      + '"unknown":false}',
    );
    assert.deepEqual(readSourceCredit(dir), {
      sources: [{ attribution: 'Elevation © National Agency', name: 'National DEM' }],
      unknown: false,
    });
  });

  it('reads back a directory holding both a recorded source and rasters it cannot speak for', () => {
    // What `fetch` into a directory that `prepare` also wrote into leaves behind. Both halves
    // matter: dropping the mark would credit the whole bake to the recorded source, and dropping
    // the source would lose a credit its licence requires.
    const dir = written('{"sources":[{"attribution":"Copernicus DEM GLO-30"}],"unknown":true}');
    assert.deepEqual(readSourceCredit(dir), {
      sources: [{ attribution: 'Copernicus DEM GLO-30', name: undefined }],
      unknown: true,
    });
  });

  it('answers nothing for rasters nobody recorded a credit for', () => {
    // A directory of an operator's own rasters, never passed through any of these commands, is
    // the ordinary case here rather than a failure.
    assert.equal(readSourceCredit(written(undefined)), undefined);
    assert.equal(readSourceCredit(written('{"sources":[],"unknown":false}')), undefined);
    assert.equal(readSourceCredit(written('{"sources":[{"name":"National DEM"}]}')), undefined);
    assert.equal(readSourceCredit(written('{"sources":[{"attribution":"   "}]}')), undefined);
  });

  it('survives a directory being filled from both directions, in either order', () => {
    // The recommended way to work: a coarse national fill from `fetch` and fine local rasters
    // from `prepare`, in one directory. Whichever runs second must not erase what the first
    // recorded — one order would lose the downloaded data's required credit, and the other would
    // hand the operator's own rasters a credit belonging to data that never covered them.
    const copernicus = { attribution: 'Copernicus DEM GLO-30 — © DLR e.V.', name: 'Copernicus' };
    for (const order of ['fetch first', 'prepare first']) {
      const dir = written(undefined);
      if (order === 'fetch first') {
        recordSource(dir, copernicus);
        markSourceUnknown(dir);
      } else {
        markSourceUnknown(dir);
        recordSource(dir, copernicus);
      }
      assert.deepEqual(readSourceCredit(dir), { sources: [copernicus], unknown: true }, order);
      // And that is exactly the state a bake refuses to guess a credit for.
      assert.deepEqual(
        bakeCredit({ attribution: undefined, recorded: readSourceCredit(dir) }).partial,
        [copernicus],
        order,
      );
    }
  });

  it('records a source fetched twice into one directory once', () => {
    const dir = written(undefined);
    const copernicus = { attribution: 'Copernicus DEM GLO-30 — © DLR e.V.', name: 'Copernicus' };
    recordSource(dir, copernicus);
    recordSource(dir, copernicus);
    assert.deepEqual(readSourceCredit(dir), { sources: [copernicus], unknown: false });
    // Still the ordinary one-source directory, which needs nothing typed.
    assert.deepEqual(
      bakeCredit({ attribution: undefined, recorded: readSourceCredit(dir) }).credit, copernicus,
    );
  });

  it('treats a damaged record as data it cannot speak for rather than as no data', () => {
    // A file this script wrote and something else damaged. What it said is gone; that something
    // was worth recording here is not. Reading it as an empty directory would let the bake
    // proceed with no credit at all for data that may well require one.
    assert.deepEqual(readSourceCredit(written('{ not json')), { sources: [], unknown: true });
  });
});

/** A gzip member header: magic, deflate, no flags, and a plausible timestamp. */
function gzipHead() {
  const head = Buffer.alloc(92);
  head.set([0x1f, 0x8b, 0x08, 0x00, 0x11, 0x22, 0x33, 0x44, 0x00, 0x03], 0);
  return head;
}

describe('classifyTile', () => {
  it('reads a mesh as a mesh even when its first two bytes are the gzip magic', () => {
    // THIS is the fixture the detector exists for. A quantized mesh has no magic number: it starts
    // with a double, whose low two bytes are effectively random, so about one tile in sixty-five
    // thousand opens with 1f 8b by pure coincidence — a near-certainty over a country's worth of
    // them, and one that comes back identically on every re-bake because the bytes are a function
    // of where the tile is. Deciding on those two bytes alone declares such a pyramid mixed and
    // refuses it, or tells the operator to serve a raw pyramid as compressed.
    const head = meshHead();
    head[0] = 0x1f;
    head[1] = 0x8b;
    assert.ok(head.readDoubleLE(0) > 4_000_000);
    assert.equal(classifyTile(head, 40_000), 'mesh');
  });

  it('reads a gzip stream as one', () => {
    assert.equal(classifyTile(gzipHead(), 40_000), 'gzip');
  });

  it('refuses two bytes of coincidence that are not actually a gzip member', () => {
    const head = Buffer.alloc(92);
    head.set([0x1f, 0x8b, 0x99, 0xff], 0); // not deflate, reserved flag bits set
    assert.equal(classifyTile(head, 40_000), 'damaged');
  });

  it('calls an empty, zero-filled or cut-short tile damaged rather than a valid one', () => {
    // A bake that filled the disk, or a copy that was interrupted, leaves exactly these. Counted
    // as ordinary uncompressed tiles they make an incomplete pyramid pass, and the ground over
    // that part of the coverage is then quietly drawn from the level above with no error anywhere.
    assert.equal(classifyTile(Buffer.alloc(0), 0), 'damaged');
    assert.equal(classifyTile(meshHead().subarray(0, 12), 12), 'damaged');
    // A run of zeros satisfies every plausibility test there is and declares no vertices.
    assert.equal(classifyTile(Buffer.alloc(92), 40_000), 'damaged');
    // A whole header on a file far too short to hold the vertices it says it has.
    assert.equal(classifyTile(meshHead({ vertices: 1200 }), 5_000), 'damaged');
    assert.equal(classifyTile(meshHead({ vertices: 1200 }), 92 + 1200 * 6), 'mesh');
    assert.equal(classifyTile(meshHead(), 91), 'damaged');
  });

  it('refuses a header whose numbers cannot describe anywhere on earth', () => {
    assert.equal(classifyTile(meshHead({ centerX: 9e9 }), 40_000), 'damaged');
    assert.equal(classifyTile(meshHead({ lowest: 900, highest: 100 }), 40_000), 'damaged');
    assert.equal(classifyTile(meshHead({ highest: 99_000 }), 40_000), 'damaged');
  });
});

describe('missingLevels', () => {
  it('names a level the pyramid advertises and has nothing at', () => {
    const available = [[{ startX: 0, endX: 1 }], [{ startX: 0, endX: 3 }], [{ startX: 0, endX: 7 }]];
    assert.deepEqual(missingLevels(available, new Map([[0, 2], [1, 8]])), [2]);
  });

  it('says nothing about a complete pyramid, or about a level advertising no ranges', () => {
    const available = [[{ startX: 0 }], []];
    assert.deepEqual(missingLevels(available, new Map([[0, 2]])), []);
    assert.deepEqual(missingLevels(undefined, new Map()), []);
  });
});

describe('downloadCell', () => {
  const directories = [];
  const scratch = () => {
    const dir = mkdtempSync(join(tmpdir(), 'silexgis-terrain-'));
    directories.push(dir);
    return dir;
  };
  after(() => {
    for (const dir of directories) {
      rmSync(dir, { recursive: true, force: true });
    }
  });

  /** A loopback server that answers exactly as told, closed with the test that opened it. */
  async function serving(handler) {
    const server = createServer(handler);
    await new Promise((done) => server.listen(0, '127.0.0.1', done));
    const { port } = server.address();
    return {
      url: `http://127.0.0.1:${port}/cell.tif`,
      close: () => new Promise((done) => server.close(done)),
    };
  }

  it('moves a complete transfer onto the final name', async () => {
    const dir = scratch();
    const target = join(dir, 'N46_00_E022_00.tif');
    const payload = Buffer.alloc(4096, 7);
    const site = await serving((_request, response) => {
      response.writeHead(200, { 'content-length': String(payload.length) });
      response.end(payload);
    });
    try {
      assert.equal(await downloadCell(site.url, target), payload.length);
      assert.equal(readFileSync(target).length, payload.length);
      assert.equal(existsSync(`${target}.part`), false);
    } finally {
      await site.close();
    }
  });

  it('leaves nothing at the final name when the transfer is cut short', async () => {
    // The failure this whole shape exists to prevent: with the body written straight to its final
    // name, a fragment of a cell is what the next run finds — and existence plus a non-zero size
    // is indistinguishable from a finished download, so it is skipped for ever and the bake is
    // handed a truncated raster whose symptom appears hours later somewhere else. Enough bytes
    // are sent, and enough time given, that they genuinely reach the disk before the cut.
    const dir = scratch();
    const target = join(dir, 'N46_00_E022_00.tif');
    const site = await serving((_request, response) => {
      response.writeHead(200, { 'content-length': String(8 << 20) });
      response.write(Buffer.alloc(1 << 20, 7), () => {
        // Ends the connection without the rest, the way a dropped link does.
        response.destroy();
      });
    });
    try {
      await assert.rejects(downloadCell(site.url, target));
      assert.equal(existsSync(target), false);
      assert.equal(existsSync(`${target}.part`), false);
    } finally {
      await site.close();
    }
  });

  it('rejects a body that ends cleanly but short of what was declared', async () => {
    // A length that overstates the body. Some intermediaries end the transfer cleanly here, and
    // the declared-versus-written check is the only thing that catches it — the stream itself
    // reports a perfectly ordinary end. That check is what this test exists for.
    //
    // Driven through the injected fetch rather than a loopback socket on purpose. Served over a
    // real connection, the short body is a race inside the HTTP client: it either surfaces as a
    // rejection or trips an internal assertion in the client's parser, which arrives as an
    // uncaught exception rather than a failed promise. That aborts the test before its cleanup
    // runs, so the listening socket stays open and the whole run hangs with no summary. A body
    // that ends cleanly is fully expressible without a socket, so it is expressed that way and
    // the outcome is the same every run. The tests around this one keep their real server, where
    // what is under test is genuinely connection-level behaviour.
    const dir = scratch();
    const target = join(dir, 'N46_00_E022_00.tif');
    const declared = 8 << 20;
    const shortBody = async () =>
      new Response(
        new ReadableStream({
          start(controller) {
            controller.enqueue(new Uint8Array(1 << 20).fill(7));
            controller.close();
          },
        }),
        { status: 200, headers: { 'content-length': String(declared) } },
      );

    await assert.rejects(downloadCell('http://example.invalid/cell.tif', target, shortBody), {
      message: `got ${1 << 20} bytes of ${declared}`,
    });
    assert.equal(existsSync(target), false);
    assert.equal(existsSync(`${target}.part`), false);
  });

  it('overwrites a fragment a hard kill left behind rather than trusting it', async () => {
    const dir = scratch();
    const target = join(dir, 'N46_00_E022_00.tif');
    writeFileSync(`${target}.part`, Buffer.alloc(512, 1));
    const payload = Buffer.alloc(4096, 7);
    const site = await serving((_request, response) => {
      response.writeHead(200, { 'content-length': String(payload.length) });
      response.end(payload);
    });
    try {
      await downloadCell(site.url, target);
      assert.deepEqual(readFileSync(target), payload);
    } finally {
      await site.close();
    }
  });

  it('answers with nothing for a cell that was never published, leaving no file', async () => {
    const dir = scratch();
    const target = join(dir, 'N46_00_E022_00.tif');
    const site = await serving((_request, response) => {
      response.writeHead(404).end();
    });
    try {
      assert.equal(await downloadCell(site.url, target), undefined);
      assert.equal(existsSync(target), false);
      assert.equal(existsSync(`${target}.part`), false);
    } finally {
      await site.close();
    }
  });
});
