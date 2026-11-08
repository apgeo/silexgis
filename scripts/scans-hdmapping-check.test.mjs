// SPDX-License-Identifier: AGPL-3.0-or-later
//
// The pure parts of the HDMapping worker check: the command line, choosing a recording's files,
// reading a LAS header's point count, the announcement both workers write, the judgement of the
// HDMapping steps' answers and the verdict. The container run itself is exercised by hand on a real
// recording.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
  checkAnnouncement, formatVerdict, lasPointCount, parseArgs, plannedSteps, selectRecording,
} from './scans-hdmapping-check.mjs';
import { checkAnswer } from './scans-worker-check.mjs';

describe('the command line', () => {
  it('takes a recording folder and a working directory, with the overlay defaults', () => {
    const options = parseArgs(['rec', '--work', '/srv/w']);
    assert.equal(options.recording, 'rec');
    assert.equal(options.image, 'silexgis-scans:hdmapping');
    assert.equal(options.tilerImage, 'silexgis-scans:hdmapping');
    assert.equal(options.cpus, 8);
    assert.equal(options.memory, '16g');
    assert.equal(options.voxelCm, null);
  });

  it('refuses to run without a working directory, which must have room for the odometry cache', () => {
    assert.throws(() => parseArgs(['rec']), /--work <dir> is required/);
  });

  it('refuses a second recording, an unknown option and a non-positive number', () => {
    assert.throws(() => parseArgs(['a', 'b', '--work', 'w']), /one recording at a time/);
    assert.throws(() => parseArgs(['a', '--work', 'w', '--fast']), /unknown option/);
    assert.throws(() => parseArgs(['a', '--work', 'w', '--voxel-cm', '0']), /voxelCm must be a positive number/);
  });
});

describe('choosing a recording\'s files', () => {
  const entry = (name, bytes = 10) => ({ name, bytes });

  it('takes the recording\'s files, pairs the chunks and notes GNSS', () => {
    const chosen = selectRecording([
      entry('lidar0000.laz', 100), entry('imu0000.csv', 20), entry('lidar0000.sn', 1), entry('gnss0000.gnss', 5),
      entry('lidar0001.laz', 100), entry('imu0001.csv', 20), entry('notes.txt', 3),
    ]);
    assert.equal(chosen.paired, true);
    assert.equal(chosen.chunks, 2);
    assert.equal(chosen.gnss, true);
    assert.equal(chosen.bytes, 246);
    assert.deepEqual(chosen.ignored, ['notes.txt']);
    assert.equal(chosen.files.length, 6);
  });

  it('says a recording with a chunk missing its IMU log is not paired', () => {
    const chosen = selectRecording([entry('lidar0000.laz'), entry('imu0000.csv'), entry('lidar0001.laz')]);
    assert.equal(chosen.paired, false);
    assert.equal(chosen.gnss, false);
  });
});

describe('a LAS header\'s point count', () => {
  const header = (minor, legacy, wide) => {
    const head = Buffer.alloc(375);
    head.write('LASF', 0, 'latin1');
    head[24] = 1;
    head[25] = minor;
    head.writeUInt16LE(minor >= 4 ? 375 : 227, 94);
    head.writeUInt32LE(legacy, 107);
    if (wide !== undefined) head.writeBigUInt64LE(BigInt(wide), 247);
    return head;
  };

  it('reads the legacy count of a LAS 1.2 header and the wide count of a 1.4 one', () => {
    assert.equal(lasPointCount(header(2, 1234)), 1234);
    assert.equal(lasPointCount(header(4, 0, 5_000_000_000)), 5_000_000_000);
    assert.equal(lasPointCount(header(4, 77, 0)), 77);
  });

  it('answers null for bytes that are not a LAS header', () => {
    assert.equal(lasPointCount(Buffer.from('PK\u0003\u0004 not a cloud')), null);
  });
});

describe('the announcement both workers write', () => {
  const steps = plannedSteps(null);
  const announced = (drop) => ({
    workers: { hdmapping: {}, tiler: {} },
    steps: steps.filter((s) => s.id !== drop).map((s) => ({ id: s.id, owner: s.worker })),
  });

  it('runs the application\'s order for a recording, with a downsample only when asked', () => {
    assert.deepEqual(steps.map((s) => s.id), [
      'hdmapping.preflight', 'hdmapping.odometry', 'hdmapping.export', 'pointcloud.check', 'pointcloud.estimate', 'py3dtiles.tile',
    ]);
    assert.ok(plannedSteps(2).some((s) => s.id === 'pointcloud.downsample'));
  });

  it('accepts every step offered by the worker that takes it', () => {
    assert.deepEqual(checkAnnouncement(announced(null), steps), []);
  });

  it('names a step one worker dropped and a step given to the wrong worker', () => {
    assert.match(checkAnnouncement(announced('py3dtiles.tile'), steps)[0], /does not offer py3dtiles.tile/);
    const wrong = announced(null);
    wrong.steps.find((s) => s.id === 'pointcloud.check').owner = 'tiler';
    assert.match(checkAnnouncement(wrong, steps)[0], /gives pointcloud.check to tiler/);
    assert.match(checkAnnouncement({ ...announced(null), workers: { hdmapping: {} } }, steps)[0], /no tiler worker/);
  });
});

describe('judging the HDMapping steps\' answers', () => {
  const files = { started: true, progressFraction: 1 };
  const output = (kind, file) => ({ kind, file, sizeBytes: 10, sha256: 'abc' });
  const measured = { sizeBytes: 10, sha256: 'abc' };
  const answer = (step, tool, outputs, data) => ({ status: 'ok', step, tool, toolVersion: '0.106.0', seconds: 3, outputs, data });

  it('checks the preflight against the recording as the caller counted it', () => {
    const result = answer('hdmapping.preflight', 'scan-worker', [output('report', 'preflight.json')],
      { chunks: 2, files: 4, rawBytes: 100, points: 50, gnss: true, freeBytes: 5000, neededBytes: 2200 });
    const recording = { chunks: 2, bytes: 100, gnss: true };
    assert.deepEqual(checkAnswer('hdmapping.preflight', result, files, { 'preflight.json': measured }, null, { recording }), []);
    assert.match(checkAnswer('hdmapping.preflight', result, files, { 'preflight.json': measured }, null, { recording: { ...recording, chunks: 3 } })[0], /counts 2 chunks/);
  });

  it('checks the odometry\'s session archive holds the session and not the cache', () => {
    const sha = 'a'.repeat(64);
    const result = answer('hdmapping.odometry', 'hdmapping', [output('hdmSession', 'session.zip')],
      { parameters: { softwareVersion: '0.106.0', sha256: sha }, odometry: { completed: true, scans: 52 } });
    const outputs = { 'session.zip': measured };
    const archive = { entries: [{ name: 'session.mjs' }, { name: 'scan_lio_0.laz' }] };
    assert.deepEqual(checkAnswer('hdmapping.odometry', result, files, outputs, archive), []);
    const withCache = { entries: [...archive.entries, { name: 'cache/part.bin' }] };
    assert.match(checkAnswer('hdmapping.odometry', result, files, outputs, withCache)[0], /cache or preview/);
    result.data.odometry.completed = false;
    assert.match(checkAnswer('hdmapping.odometry', result, files, outputs, archive)[0], /did not say the odometry completed/);
  });

  it('checks the export\'s point count against the exported cloud\'s header', () => {
    const result = answer('hdmapping.export', 'hdmapping', [output('pointCloudLaz', 'export.laz'), output('trajectory', 'trajectory.csv')],
      { export: { pointsWritten: 1000, missingTimestamps: false } });
    const outputs = { 'export.laz': measured, 'trajectory.csv': measured };
    assert.deepEqual(checkAnswer('hdmapping.export', result, files, outputs, null, { exportedPoints: 1000 }), []);
    assert.match(checkAnswer('hdmapping.export', result, files, outputs, null, { exportedPoints: 999 })[0], /declares 999 points/);
  });

  it('checks the export check found nothing and counted the whole cloud', () => {
    const report = {
      pointCount: 1000, declaredPoints: 1000, extentMetres: [10, 5, 3], nonFinitePoints: 0, headerBoundsHold: true,
      trajectory: { rows: 40, missingTimestamps: 0, timeGoesBack: 0, lengthMetres: 12 }, problems: [],
    };
    const result = answer('pointcloud.check', 'laspy', [output('report', 'check.json')], report);
    const outputs = { 'check.json': measured };
    assert.deepEqual(checkAnswer('pointcloud.check', result, files, outputs, null, { exportedPoints: 1000 }), []);
    result.data = { ...report, trajectory: undefined };
    assert.match(checkAnswer('pointcloud.check', result, files, outputs, null, { exportedPoints: 1000 })[0], /read no trajectory/);
  });
});

describe('the verdict', () => {
  it('prints timings, counts and memory, and never the tiles\' origin shift', () => {
    const text = formatVerdict({
      ok: true, image: 'silexgis-scans:hdmapping', tilerImage: 'silexgis-scans:hdmapping', hdmapping: 'abc123',
      recording: { name: 'mandeye', files: 100, chunks: 25, bytes: 263_000_000, gnss: true, ignored: 0 },
      announcement: { steps: 7, workers: ['hdmapping', 'tiler'] }, announcementFailed: [],
      steps: [
        { id: 'hdmapping.odometry', status: 'ok', seconds: 380, wallSeconds: 382, containerAnonPeakBytes: 6e9, failed: [],
          figures: { parameters: { softwareVersion: '0.106.0' }, odometry: { scans: 52, elapsedSeconds: 370, residentPeakMiB: 5400, cachePeakMiB: 4900 }, sessionBytes: 2e8, sessionFiles: 110 } },
        { id: 'py3dtiles.tile', status: 'ok', seconds: 48, wallSeconds: 49, failed: [],
          figures: { pointCount: 34_200_000, tileCount: 5819, tileBytes: 6e8, archiveBytes: 5.9e8, rgb: false, intensity: true } },
      ],
      totalSeconds: 600,
    });
    assert.match(text, /HDMapping worker check: OK/);
    assert.match(text, /hdmapping\.odometry +ok +380\.0 s worker, 382\.0 s wall, container peak 5\.59 GB/);
    assert.match(text, /34,200,000 points → 5,819 files/);
    assert.match(text, /total +10\.0 min wall/);
    assert.doesNotMatch(text, /origin shift/);
  });
});
