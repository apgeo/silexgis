// SPDX-License-Identifier: AGPL-3.0-or-later
//
// The pure parts of the worker check: the command line, the hand-written zip reader and the
// judgement of an answer. The container run itself is exercised by hand on a real cloud.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { deflateRawSync } from 'node:zlib';

import { checkAnswer, formatVerdict, parseArgs, zipEntries, zipRead } from './scans-worker-check.mjs';

/** A zip archive built by hand: stored or deflated entries, local headers, a central directory. */
function zip(files) {
  const locals = [];
  const centrals = [];
  let offset = 0;
  for (const [name, text, method] of files) {
    const data = Buffer.from(text, 'utf8');
    const stored = method === 8 ? deflateRawSync(data) : data;
    const nameBytes = Buffer.from(name, 'utf8');
    const local = Buffer.alloc(30 + nameBytes.length);
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt16LE(method, 8);
    local.writeUInt32LE(stored.length, 18);
    local.writeUInt32LE(data.length, 22);
    local.writeUInt16LE(nameBytes.length, 26);
    nameBytes.copy(local, 30);
    const central = Buffer.alloc(46 + nameBytes.length);
    central.writeUInt32LE(0x02014b50, 0);
    central.writeUInt16LE(method, 10);
    central.writeUInt32LE(stored.length, 20);
    central.writeUInt32LE(data.length, 24);
    central.writeUInt16LE(nameBytes.length, 28);
    central.writeUInt32LE(offset, 42);
    nameBytes.copy(central, 46);
    locals.push(local, stored);
    centrals.push(central);
    offset += local.length + stored.length;
  }
  const directory = Buffer.concat(centrals);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(files.length, 10);
  end.writeUInt32LE(directory.length, 12);
  end.writeUInt32LE(offset, 16);
  return Buffer.concat([...locals, directory, end]);
}

describe('the command line', () => {
  it('needs a cloud and takes the options', () => {
    const o = parseArgs(['a.laz', '--voxel-cm', '5', '--cpus', '2', '--keep', '--no-downsample']);
    assert.equal(o.cloud, 'a.laz');
    assert.equal(o.voxelCm, 5);
    assert.equal(o.cpus, 2);
    assert.equal(o.keep, true);
    assert.equal(o.downsample, false);
    assert.equal(o.image, 'silexgis-scans:tiler');
  });

  it('refuses what it cannot run', () => {
    assert.throws(() => parseArgs([]), /name the cloud/);
    assert.throws(() => parseArgs(['a.txt']), /\.las or \.laz/);
    assert.throws(() => parseArgs(['a.laz', '--voxel-cm', 'x']), /voxelCm must be a positive number/);
    assert.throws(() => parseArgs(['a.laz', '--what']), /unknown option/);
    assert.throws(() => parseArgs(['a.laz', 'b.laz']), /one cloud at a time/);
  });
});

describe('the zip reader', () => {
  it('lists the entries and opens stored and deflated ones', () => {
    const archive = zip([['tileset.json', '{"root":{}}', 8], ['r.pnts', 'points', 0], ['sub/r0.pnts', 'more', 8]]);
    const entries = zipEntries(archive);
    assert.deepEqual(entries.map((e) => [e.name, e.size]), [['tileset.json', 11], ['r.pnts', 6], ['sub/r0.pnts', 4]]);
    assert.equal(zipRead(archive, entries[0]).toString(), '{"root":{}}');
    assert.equal(zipRead(archive, entries[1]).toString(), 'points');
    assert.equal(zipRead(archive, entries[2]).toString(), 'more');
  });

  it('says when it is given something else', () => {
    assert.throws(() => zipEntries(Buffer.from('not a zip at all, really not')), /not a zip archive/);
  });
});

describe('judging an answer', () => {
  const files = { started: true, progressFraction: 1 };
  const output = (kind, file) => ({ kind, file, sizeBytes: 10, sha256: 'abc' });
  const measured = { sizeBytes: 10, sha256: 'abc' };

  it('accepts an estimate whose rows fit under the cloud', () => {
    const result = {
      status: 'ok', step: 'pointcloud.estimate', tool: 'laspy', toolVersion: '2.7.0', seconds: 1.5,
      outputs: [output('estimate', 'estimate.json')],
      data: {
        pointCount: 100, full: { points: 100 },
        voxels: [{ voxelCm: 1, points: 90, estimatedTileBytes: 1, estimatedSeconds: 1 }, { voxelCm: 10, points: null }],
      },
    };
    assert.deepEqual(checkAnswer('pointcloud.estimate', result, files, { 'estimate.json': measured }), []);
    result.data.voxels[0].points = 101;
    assert.match(checkAnswer('pointcloud.estimate', result, files, { 'estimate.json': measured })[0], /keeps 101 of 100/);
  });

  it('names a failed step and stops there', () => {
    const failed = checkAnswer('py3dtiles.tile', { status: 'failed', step: 'py3dtiles.tile', reason: 'py3dtiles exited 1' }, files, {}, null);
    assert.deepEqual(failed, ['the answer is failed: py3dtiles exited 1']);
  });

  it('holds the answer to what is on disk', () => {
    const result = {
      status: 'ok', step: 'pointcloud.downsample', tool: 'laspy', toolVersion: '2.7.0', seconds: 1,
      outputs: [output('pointCloudLazDisplay', 'display.laz')], data: { pointsIn: 100, pointsOut: 40 },
    };
    assert.deepEqual(checkAnswer('pointcloud.downsample', result, files, { 'display.laz': measured }), []);
    const wrong = checkAnswer('pointcloud.downsample', result, files, { 'display.laz': { sizeBytes: 11, sha256: 'zzz' } });
    assert.equal(wrong.length, 2);
    assert.match(wrong[0], /11 bytes on disk, 10 in the answer/);
    assert.match(checkAnswer('pointcloud.downsample', result, { started: false, progressFraction: 0.5 }, { 'display.laz': measured }).join('\n'), /no `started` file/);
  });

  it('wants tiles about the origin with no transform, as many as the answer counts', () => {
    const result = {
      status: 'ok', step: 'py3dtiles.tile', tool: 'py3dtiles', toolVersion: '12.1.1', seconds: 20,
      outputs: [output('pointCloudTiles', 'tiles.zip')],
      data: { originShift: [500000, 4000000, 300], tileCount: 2, tileBytes: 17, localMin: [-10, -10, -2], localMax: [10, 10, 2] },
    };
    const entries = [{ name: 'tileset.json', size: 11 }, { name: 'r.pnts', size: 6 }];
    // py3dtiles writes a root transform carrying the centroid and a box about it; the two
    // together must sit near the origin.
    const identity = (t) => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, ...t, 1];
    const good = { entries, tileset: { root: { transform: identity([0.9, -0.2, 0.1]), boundingVolume: { box: [0, 0, 0, 10, 0, 0, 0, 10, 0, 0, 0, 2] } } } };
    assert.deepEqual(checkAnswer('py3dtiles.tile', result, files, { 'tiles.zip': measured }, good), []);
    const bare = { entries, tileset: { root: { boundingVolume: { box: [0.5, -0.2, 0, 10, 0, 0, 0, 10, 0, 0, 0, 2] } } } };
    assert.deepEqual(checkAnswer('py3dtiles.tile', result, files, { 'tiles.zip': measured }, bare), []);

    const leaked = { entries, tileset: { root: { transform: identity([500000, 4000000, 300]), boundingVolume: { box: [0, 0, 0, 10, 0, 0, 0, 10, 0, 0, 0, 2] } } } };
    const failed = checkAnswer('py3dtiles.tile', result, files, { 'tiles.zip': measured }, leaked);
    assert.match(failed.join('\n'), /root sits 4000000\.0 m from the origin/);
    const odd = { entries, tileset: { root: { transform: [1], boundingVolume: { box: [0, 0, 0, 10, 0, 0, 0, 10, 0, 0, 0, 2] } } } };
    assert.match(checkAnswer('py3dtiles.tile', result, files, { 'tiles.zip': measured }, odd).join('\n'), /not a 4×4 matrix/);

    const short = { entries: [entries[0]], tileset: good.tileset };
    assert.match(checkAnswer('py3dtiles.tile', result, files, { 'tiles.zip': measured }, short).join('\n'), /holds 1 files, the answer says 2/);
  });
});

describe('the verdict', () => {
  it('reads as figures, step by step', () => {
    const text = formatVerdict({
      ok: true, image: 'silexgis-scans:tiler', worker: '1', tools: { py3dtiles: '12.1.1' },
      cloud: { name: 'a.laz', bytes: 1048576 },
      steps: [{
        id: 'py3dtiles.tile', status: 'ok', seconds: 20.5, wallSeconds: 21.7, peakRssBytes: 2097152, failed: [],
        figures: { pointCount: 1000, tileCount: 3, tileBytes: 2048, archiveBytes: 1024, specVersion: '1.0', rgb: true, intensity: false, originShift: [1, 2, 3], extent: [4, 5, 6] },
      }],
    });
    assert.match(text, /^Scans worker check: OK/);
    assert.match(text, /py3dtiles\.tile +ok +20\.5 s worker, 21\.7 s wall, peak 2\.0 MB/);
    assert.match(text, /1,000 points → 3 files/);
    assert.match(text, /origin shift 1\.0, 2\.0, 3\.0/);
  });
});
