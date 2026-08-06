// SPDX-License-Identifier: AGPL-3.0-or-later

// Tests for the terrain pre-bake script: which elevation cells cover a box, where each one is
// downloaded from, what a downloaded cell is allowed to leave behind, how the pre-baker is run,
// what a baked pyramid publishes for itself, and how a tile is told apart from a fragment.
//
// Run from the repository root with `node --test "deploy/**/*.test.mjs"` — the pattern is quoted
// because Node expands it itself. It is the built-in runner, so this needs no toolchain
// of its own and no dependency. Importing the script does not run it: it only acts when it is the
// entry point, which is what keeps a test from starting a download of a country's worth of
// elevation. The one test that transfers anything serves it from a loopback socket it opened
// itself, so nothing here reaches a network.

import { strict as assert } from 'node:assert';
import { createServer } from 'node:http';
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, it } from 'node:test';

import {
  bakeDockerArgs,
  classifyTile,
  copernicusCells,
  copernicusUrl,
  downloadCell,
  missingLevels,
  pyramidVersion,
} from './terrain.mjs';

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
    const dir = scratch();
    const target = join(dir, 'N46_00_E022_00.tif');
    const site = await serving((_request, response) => {
      // A length that overstates the body. Some intermediaries end the transfer cleanly here.
      response.writeHead(200, { 'content-length': String(8 << 20), connection: 'close' });
      response.end(Buffer.alloc(1 << 20, 7));
    });
    try {
      await assert.rejects(downloadCell(site.url, target));
      assert.equal(existsSync(target), false);
      assert.equal(existsSync(`${target}.part`), false);
    } finally {
      await site.close();
    }
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
