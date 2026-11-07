// SPDX-License-Identifier: AGPL-3.0-or-later

// The point-cloud worker, run for real on one cloud: the built image is started the way the
// overlay starts it (no network, read-only root, no capabilities, one hand-off directory), the
// three steps are handed to it one after another exactly as the application hands them — a
// request renamed into a job directory, the answer read back from result.json — and what came
// back is checked against the contract the application relies on. It prints a verdict: which
// steps ran, points in and out, tiles and bytes, timings. Never a point.
//
// This is the check the unit tests cannot make. `deploy/scans-worker.test.mjs` pins the files to
// each other and drives the loop with the system Python, where the steps' tools are absent; the
// application's tests answer requests with a canned worker. Neither proves that the image as built
// tiles a cloud, that its answers match the figures it reports, or that the tiles it writes carry
// no coordinates. This does, against a real file, and it is run by hand after a change to the
// image, the loop or the application's reading of its answers — not in hosted CI, which has no
// container engine with the image built and no cloud to tile.
//
// Usage:
//   node scripts/scans-worker-check.mjs <cloud.las|cloud.laz> [options]
//     --image <name>      the worker image (default silexgis-scans:tiler)
//     --build             build it first from deploy/scans (target tiler)
//     --work <dir>        where the hand-off directory goes (default: a fresh directory under
//                         the system temporary directory); it is removed afterwards unless --keep
//     --keep              leave the hand-off directory for inspection
//     --voxel-cm <n>      downsample with a voxel of n cm before tiling (default 2);
//                         --no-downsample tiles the original instead
//     --cpus <n>          processors for the container and jobs for the tiler (default 4)
//     --memory <size>     the container's memory limit (default 16g)
//     --timeout <s>       the most one step may take (default 3600)
//     --json              print the verdict as JSON instead of text
// Exit code 0 when every step answered ok and every check held, 1 otherwise, 2 for a bad call.

import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import {
  chmodSync, copyFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, renameSync, rmSync,
  statSync, writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, dirname, extname, join, resolve } from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';
import { inflateRawSync } from 'node:zlib';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');

// The worker runs as the application's own runtime user, which is not the user running this
// script, so the directories it must write into are opened to everyone for the duration of the
// check. Only the check's own scratch directory is ever opened this way.
const WORKER_UID = 1654;
const OPEN_DIR = 0o777;

// ---------------------------------------------------------------------------------------------
// Pure parts, exported for the test.

/** Parses the command line into options; throws on a bad call. */
export function parseArgs(argv) {
  const options = {
    cloud: null, image: 'silexgis-scans:tiler', build: false, work: null, keep: false,
    voxelCm: 2, downsample: true, cpus: 4, memory: '16g', timeout: 3600, json: false,
  };
  const takes = { '--image': 'image', '--work': 'work', '--voxel-cm': 'voxelCm', '--cpus': 'cpus', '--memory': 'memory', '--timeout': 'timeout' };
  const flags = { '--build': 'build', '--keep': 'keep', '--json': 'json' };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg in takes) {
      const value = argv[++i];
      if (value === undefined) throw new Error(`${arg} needs a value`);
      options[takes[arg]] = value;
    } else if (arg in flags) {
      options[flags[arg]] = true;
    } else if (arg === '--no-downsample') {
      options.downsample = false;
    } else if (arg.startsWith('--')) {
      throw new Error(`unknown option ${arg}`);
    } else if (options.cloud === null) {
      options.cloud = arg;
    } else {
      throw new Error(`one cloud at a time; '${arg}' is a second one`);
    }
  }
  if (options.cloud === null) throw new Error('name the cloud to run: node scripts/scans-worker-check.mjs <cloud.las|cloud.laz>');
  for (const name of ['voxelCm', 'cpus', 'timeout']) {
    const n = Number(options[name]);
    if (!Number.isFinite(n) || n <= 0) throw new Error(`${name} must be a positive number, not '${options[name]}'`);
    options[name] = n;
  }
  if (!['.las', '.laz'].includes(extname(options.cloud).toLowerCase())) throw new Error('the cloud must be a .las or .laz file');
  return options;
}

/**
 * The entries of a zip archive, from its central directory: name, compressed and stored sizes,
 * method and where the local header sits. Node has no zip reader of its own, and the only
 * questions asked of the archive are how many files it holds, how big they are and what one small
 * JSON file inside it says, so reading the directory by hand is less than a dependency.
 */
export function zipEntries(buffer) {
  // The end-of-central-directory record is the last thing in the file; its comment is at most
  // 65535 bytes, so it begins within that of the end.
  const EOCD = 0x06054b50;
  let eocd = -1;
  for (let i = buffer.length - 22; i >= Math.max(0, buffer.length - 22 - 65535); i--) {
    if (buffer.readUInt32LE(i) === EOCD) { eocd = i; break; }
  }
  if (eocd < 0) throw new Error('not a zip archive: no end-of-central-directory record');
  const count = buffer.readUInt16LE(eocd + 10);
  let at = buffer.readUInt32LE(eocd + 16);
  const entries = [];
  for (let n = 0; n < count; n++) {
    if (buffer.readUInt32LE(at) !== 0x02014b50) throw new Error(`zip central directory entry ${n} is damaged`);
    const method = buffer.readUInt16LE(at + 10);
    const compressedSize = buffer.readUInt32LE(at + 20);
    const size = buffer.readUInt32LE(at + 24);
    const nameLength = buffer.readUInt16LE(at + 28);
    const extraLength = buffer.readUInt16LE(at + 30);
    const commentLength = buffer.readUInt16LE(at + 32);
    const localHeader = buffer.readUInt32LE(at + 42);
    const name = buffer.toString('utf8', at + 46, at + 46 + nameLength);
    entries.push({ name, method, compressedSize, size, localHeader });
    at += 46 + nameLength + extraLength + commentLength;
  }
  return entries;
}

/** The bytes of one entry (stored or deflated). */
export function zipRead(buffer, entry) {
  const at = entry.localHeader;
  if (buffer.readUInt32LE(at) !== 0x04034b50) throw new Error(`zip local header of ${entry.name} is damaged`);
  const nameLength = buffer.readUInt16LE(at + 26);
  const extraLength = buffer.readUInt16LE(at + 28);
  const start = at + 30 + nameLength + extraLength;
  const raw = buffer.subarray(start, start + entry.compressedSize);
  if (entry.method === 0) return raw;
  if (entry.method === 8) return inflateRawSync(raw);
  throw new Error(`zip entry ${entry.name} uses compression method ${entry.method}, which this reader does not open`);
}

/**
 * What the application relies on in one step's answer, checked. `outputs` maps an output's file
 * name to {sizeBytes, sha256} as measured on disk; `archive` is the parsed tiles archive for the
 * tile step ({entries, tileset}) and absent otherwise. Returns the failed checks, in words.
 */
export function checkAnswer(step, result, files, outputs, archive) {
  const failed = [];
  const expect = (ok, text) => { if (!ok) failed.push(text); };
  expect(files.started, 'the worker wrote no `started` file when it took the job');
  expect(files.progressFraction === 1, `progress.json did not reach 1.0 (it says ${files.progressFraction})`);
  expect(result.status === 'ok', `the answer is ${result.status}: ${result.reason || '(no reason)'}`);
  expect(result.step === step, `the answer names step ${result.step}, not ${step}`);
  if (result.status !== 'ok') return failed;
  expect(typeof result.tool === 'string' && result.tool !== '', 'the answer names no tool');
  expect(typeof result.toolVersion === 'string' && result.toolVersion !== '', 'the answer names no tool version');
  expect(typeof result.seconds === 'number' && result.seconds >= 0, 'the answer carries no duration');
  expect(Array.isArray(result.outputs) && result.outputs.length > 0, 'the answer lists no outputs');
  for (const output of result.outputs ?? []) {
    const measured = outputs[output.file];
    expect(measured !== undefined, `output ${output.file} is listed but not in the job directory`);
    if (!measured) continue;
    expect(measured.sizeBytes === output.sizeBytes, `output ${output.file} is ${measured.sizeBytes} bytes on disk, ${output.sizeBytes} in the answer`);
    expect(measured.sha256 === output.sha256, `output ${output.file}'s digest on disk differs from the answer's`);
  }
  const data = result.data ?? {};
  const kinds = (result.outputs ?? []).map((o) => o.kind);
  switch (step) {
    case 'pointcloud.estimate':
      expect(kinds.includes('estimate'), 'no estimate output');
      expect(Number.isInteger(data.pointCount) && data.pointCount > 0, 'the estimate carries no point count');
      expect(Array.isArray(data.voxels) && data.voxels.length > 0, 'the estimate has no voxel rows');
      for (const row of data.voxels ?? []) {
        if (row.points === null) continue;
        expect(row.points <= data.pointCount, `a ${row.voxelCm} cm voxel keeps ${row.points} of ${data.pointCount} points`);
        expect(row.estimatedTileBytes > 0 && row.estimatedSeconds > 0, `the ${row.voxelCm} cm row has no tile estimate`);
      }
      expect(data.full && data.full.points === data.pointCount, 'the "full" row does not carry the whole cloud');
      break;
    case 'pointcloud.downsample':
      expect(kinds.includes('pointCloudLazDisplay'), 'no display copy output');
      expect(Number.isInteger(data.pointsIn) && Number.isInteger(data.pointsOut), 'the downsample reports no point counts');
      expect(data.pointsOut > 0 && data.pointsOut <= data.pointsIn, `the display copy keeps ${data.pointsOut} of ${data.pointsIn} points`);
      break;
    case 'py3dtiles.tile': {
      expect(kinds.includes('pointCloudTiles'), 'no tiles output');
      expect(Array.isArray(data.originShift) && data.originShift.length === 3, 'the answer reports no origin shift');
      expect(Number.isInteger(data.tileCount) && data.tileCount > 0, 'the answer counts no tiles');
      if (!archive) { failed.push('the tiles archive could not be read'); break; }
      const { entries, tileset } = archive;
      expect(entries.length === data.tileCount, `the archive holds ${entries.length} files, the answer says ${data.tileCount}`);
      const bytes = entries.reduce((sum, e) => sum + e.size, 0);
      expect(bytes === data.tileBytes, `the archive's files total ${bytes} bytes, the answer says ${data.tileBytes}`);
      expect(entries.some((e) => e.name === 'tileset.json'), 'the archive has no tileset.json at its root');
      expect(entries.every((e) => !e.name.startsWith('/') && !e.name.split('/').includes('..')), 'an archive entry has a path outside the archive');
      if (tileset) {
        // Tiles carry no coordinates: nothing in the tileset places it anywhere on Earth. Every
        // file is shifted to its own centre before tiling, so where the tileset puts its root —
        // the translation of the root transform (py3dtiles always writes one, carrying the
        // cloud's centroid) plus the centre of the root box — must lie within the local extent
        // the answer reports. A georeferenced cloud whose coordinates leaked through would put it
        // hundreds of thousands of metres out.
        const transform = tileset.root?.transform;
        expect(transform === undefined || (Array.isArray(transform) && transform.length === 16), 'the tileset root transform is not a 4×4 matrix');
        const box = tileset.root?.boundingVolume?.box;
        expect(Array.isArray(box) && box.length === 12, 'the tileset root has no box bounding volume');
        if (Array.isArray(box) && Array.isArray(data.localMin) && Array.isArray(data.localMax)) {
          const span = Math.max(...[0, 1, 2].map((i) => Math.abs(data.localMax[i] - data.localMin[i])));
          const shift = Array.isArray(transform) && transform.length === 16 ? transform.slice(12, 15) : [0, 0, 0];
          const off = Math.max(...[0, 1, 2].map((i) => Math.abs(shift[i] + box[i])));
          expect(off <= span + 1, `the tileset's root sits ${off.toFixed(1)} m from the origin for a cloud ${span.toFixed(1)} m across`);
        }
        expect(!tileset.root?.boundingVolume?.region, 'the tileset root uses a geographic region');
      } else {
        failed.push('tileset.json could not be parsed');
      }
      break;
    }
    default:
      failed.push(`no checks are known for step ${step}`);
  }
  return failed;
}

/** The verdict as text. */
export function formatVerdict(verdict) {
  const lines = [];
  const mb = (n) => `${(n / 1048576).toFixed(1)} MB`;
  lines.push(`Scans worker check: ${verdict.ok ? 'OK' : 'FAILED'}`);
  lines.push(`  image     ${verdict.image}${verdict.worker ? ` (worker ${verdict.worker})` : ''}`);
  if (verdict.tools) lines.push(`  tools     ${Object.entries(verdict.tools).map(([k, v]) => `${k} ${v}`).join(', ')}`);
  lines.push(`  cloud     ${verdict.cloud.name}, ${mb(verdict.cloud.bytes)}`);
  for (const step of verdict.steps) {
    const head = `  ${step.id.padEnd(22)} ${step.status.padEnd(9)} ${step.seconds.toFixed(1)} s worker, ${step.wallSeconds.toFixed(1)} s wall`;
    lines.push(head + (step.peakRssBytes ? `, peak ${mb(step.peakRssBytes)}` : ''));
    const f = step.figures;
    if (step.id === 'pointcloud.estimate' && f.pointCount !== undefined) {
      lines.push(`      points ${f.pointCount.toLocaleString('en-US')}, rgb ${f.hasRgb}, intensity ${f.hasIntensity}`);
      lines.push(`      full: ${mb(f.full.estimatedTileBytes)} in ${f.full.estimatedSeconds.toFixed(0)} s (estimated)`);
      for (const row of f.voxels ?? []) {
        lines.push(row.points === null
          ? `      ${String(row.voxelCm).padStart(3)} cm: not countable`
          : `      ${String(row.voxelCm).padStart(3)} cm: ${row.points.toLocaleString('en-US')} points, ${mb(row.estimatedTileBytes)} in ${row.estimatedSeconds.toFixed(0)} s (estimated)`);
      }
    } else if (step.id === 'pointcloud.downsample' && f.pointsIn !== undefined) {
      lines.push(`      ${f.method} ${f.voxelCm ?? f.everyNth}: ${f.pointsIn.toLocaleString('en-US')} → ${f.pointsOut.toLocaleString('en-US')} points, display copy ${mb(f.displayBytes)}`);
    } else if (step.id === 'py3dtiles.tile' && f.tileCount !== undefined) {
      lines.push(`      ${f.pointCount.toLocaleString('en-US')} points → ${f.tileCount} files, ${mb(f.tileBytes)} of tiles, archive ${mb(f.archiveBytes)}; spec ${f.specVersion}, rgb ${f.rgb}, intensity ${f.intensity}`);
      lines.push(`      origin shift ${f.originShift.map((v) => v.toFixed(1)).join(', ')}; local extent ${f.extent.map((v) => v.toFixed(1)).join(' × ')} m`);
    }
    for (const failure of step.failed) lines.push(`      FAILED: ${failure}`);
  }
  if (verdict.aborted) lines.push(`  aborted   ${verdict.aborted}`);
  return lines.join('\n');
}

// ---------------------------------------------------------------------------------------------
// Running the container and handing it jobs.

function sha256(path) {
  return createHash('sha256').update(readFileSync(path)).digest('hex');
}

function docker(args, options = {}) {
  const run = spawnSync('docker', args, { encoding: 'utf8', ...options });
  if (run.error) throw new Error(`docker could not be run: ${run.error.message}`);
  return run;
}

function openDir(path) {
  mkdirSync(path, { recursive: true });
  chmodSync(path, OPEN_DIR);
}

/** Writes a request the way the application does: a temporary name, then renamed into place. */
function handOver(jobDir, request) {
  const part = join(jobDir, 'request.json.part');
  writeFileSync(part, JSON.stringify(request, null, 1) + '\n');
  chmodSync(part, 0o644);
  renameSync(part, join(jobDir, 'request.json'));
}

const sleep = (ms) => new Promise((done) => setTimeout(done, ms));

async function waitForAnswer(jobDir, timeoutSeconds, containerName) {
  const deadline = Date.now() + (timeoutSeconds + 60) * 1000;
  while (Date.now() < deadline) {
    if (existsSync(join(jobDir, 'result.json'))) return JSON.parse(readFileSync(join(jobDir, 'result.json'), 'utf8'));
    const alive = docker(['inspect', '-f', '{{.State.Running}}', containerName]);
    if (alive.status !== 0 || alive.stdout.trim() !== 'true') throw new Error('the worker container stopped before answering');
    await sleep(250);
  }
  throw new Error(`no answer in ${jobDir} within ${timeoutSeconds + 60} s`);
}

function readProgress(jobDir) {
  try { return JSON.parse(readFileSync(join(jobDir, 'progress.json'), 'utf8')).fraction; } catch { return null; }
}

function measureOutputs(jobDir, result) {
  const measured = {};
  for (const output of result.outputs ?? []) {
    const path = join(jobDir, output.file);
    if (!existsSync(path)) continue;
    measured[output.file] = { sizeBytes: statSync(path).size, sha256: sha256(path) };
  }
  return measured;
}

function readArchive(path) {
  try {
    const buffer = readFileSync(path);
    const entries = zipEntries(buffer);
    const index = entries.find((e) => e.name === 'tileset.json');
    const tileset = index ? JSON.parse(zipRead(buffer, index).toString('utf8')) : null;
    return { entries, tileset };
  } catch {
    return null;
  }
}

async function main(argv) {
  let options;
  try {
    options = parseArgs(argv);
  } catch (error) {
    console.error(error.message);
    return 2;
  }
  const cloud = resolve(options.cloud);
  if (!existsSync(cloud)) { console.error(`${cloud} does not exist`); return 2; }

  if (options.build) {
    const build = docker(['build', '--target', 'tiler', '-t', options.image, join(repoRoot, 'deploy', 'scans')], { stdio: 'inherit', encoding: undefined });
    if (build.status !== 0) { console.error('the image did not build'); return 1; }
  }
  if (docker(['image', 'inspect', options.image]).status !== 0) {
    console.error(`image ${options.image} is not built; pass --build or build it from deploy/scans with --target tiler`);
    return 2;
  }

  const work = options.work ? resolve(options.work) : mkdtempSync(join(tmpdir(), 'silexgis-scans-check-'));
  const root = join(work, 'handoff');
  const jobs = join(root, 'jobs');
  if (existsSync(root)) rmSync(root, { recursive: true, force: true });
  openDir(root);
  openDir(jobs);

  const containerName = `silexgis-scans-check-${process.pid}`;
  const verdict = {
    ok: false, image: options.image, worker: null, tools: null,
    cloud: { name: basename(cloud), bytes: statSync(cloud).size },
    steps: [], aborted: null, handoff: root,
  };

  // The same isolation the overlay gives the worker, so what is measured is what an installation
  // runs: no network, read-only root with a tmpfs /tmp, no capabilities, bounded memory,
  // processors and processes. Only the hand-off directory is writable.
  const started = docker([
    'run', '-d', '--rm', '--name', containerName,
    '--network', 'none', '--read-only', '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges:true',
    '--pids-limit', '256', '--tmpfs', '/tmp:size=256m',
    '--memory', String(options.memory), '--cpus', String(options.cpus),
    '-e', 'SILEXGIS_SCANS_ROOT=/data/scans', '-e', 'SILEXGIS_SCANS_POLL_SECONDS=1',
    '-e', `SILEXGIS_SCANS_JOBS=${options.cpus}`, '-e', 'SILEXGIS_SCANS_CACHE_MB=2048',
    '-e', `SILEXGIS_SCANS_JOB_TIMEOUT_SECONDS=${options.timeout}`,
    '-v', `${root}:/data/scans`,
    options.image,
  ]);
  if (started.status !== 0) { console.error(started.stderr); return 1; }

  try {
    // The worker announces itself before it takes anything.
    for (let i = 0; i < 120 && !existsSync(join(root, 'capabilities.json')); i++) await sleep(250);
    if (!existsSync(join(root, 'capabilities.json'))) throw new Error('the worker wrote no capabilities.json within 30 s');
    const capabilities = JSON.parse(readFileSync(join(root, 'capabilities.json'), 'utf8'));
    verdict.worker = capabilities.worker;
    verdict.tools = capabilities.tools;
    const offered = new Set((capabilities.steps ?? []).map((s) => s.id));

    const extension = extname(cloud).toLowerCase();
    const inputName = `cloud${extension}`;
    const steps = [
      { id: 'pointcloud.estimate', parameters: {}, inputs: { pointCloudLaz: inputName } },
      ...(options.downsample ? [{ id: 'pointcloud.downsample', parameters: { method: 'voxel', voxelCm: options.voxelCm }, inputs: { pointCloudLaz: inputName } }] : []),
      { id: 'py3dtiles.tile', parameters: { specVersion: '1.0', rgb: true, intensity: true }, inputs: { pointCloudLaz: inputName } },
    ];
    let displayCopy = null;
    for (let n = 0; n < steps.length; n++) {
      const step = steps[n];
      if (!offered.has(step.id)) throw new Error(`the worker does not offer step ${step.id} (it offers ${[...offered].join(', ')})`);
      const jobDir = join(jobs, `${String(n + 1).padStart(2, '0')}-${step.id.replace(/\./g, '-')}`);
      openDir(jobDir);
      // The application copies the stored cloud into the job directory and, after a downsample,
      // the display copy it took back; the tile step then tiles that copy.
      copyFileSync(cloud, join(jobDir, inputName));
      chmodSync(join(jobDir, inputName), 0o644);
      const inputs = { ...step.inputs };
      if (step.id === 'py3dtiles.tile' && displayCopy) {
        copyFileSync(displayCopy, join(jobDir, 'display.laz'));
        chmodSync(join(jobDir, 'display.laz'), 0o644);
        inputs.pointCloudLazDisplay = 'display.laz';
      }
      const clock = process.hrtime.bigint();
      handOver(jobDir, { step: step.id, parameters: step.parameters, inputs, timeoutSeconds: options.timeout });
      const result = await waitForAnswer(jobDir, options.timeout, containerName);
      const wallSeconds = Number(process.hrtime.bigint() - clock) / 1e9;

      const outputs = measureOutputs(jobDir, result);
      const archive = step.id === 'py3dtiles.tile' ? readArchive(join(jobDir, 'tiles.zip')) : null;
      const files = { started: existsSync(join(jobDir, 'started')), progressFraction: readProgress(jobDir) };
      const failed = checkAnswer(step.id, result, files, outputs, archive);
      const data = result.data ?? {};
      const figures = step.id === 'pointcloud.downsample'
        ? { ...data, displayBytes: outputs['display.laz']?.sizeBytes ?? 0 }
        : step.id === 'py3dtiles.tile'
          ? { ...data, extent: [0, 1, 2].map((i) => (data.localMax?.[i] ?? 0) - (data.localMin?.[i] ?? 0)) }
          : data;
      verdict.steps.push({
        id: step.id, status: result.status, reason: result.reason, seconds: result.seconds ?? 0, wallSeconds,
        peakRssBytes: result.peakRssBytes, tool: result.tool, toolVersion: result.toolVersion, figures, failed,
      });
      if (result.status !== 'ok') break;
      if (step.id === 'pointcloud.downsample') displayCopy = join(jobDir, 'display.laz');
    }
    verdict.ok = verdict.steps.length === steps.length && verdict.steps.every((s) => s.failed.length === 0);
  } catch (error) {
    verdict.aborted = error.message;
    const log = docker(['logs', '--tail', '20', containerName]);
    if (log.stdout || log.stderr) verdict.workerLog = (log.stdout + log.stderr).trim().split('\n');
  } finally {
    docker(['stop', '-t', '15', containerName]);
  }

  if (options.json) console.log(JSON.stringify(verdict, null, 2));
  else {
    console.log(formatVerdict(verdict));
    if (verdict.workerLog) console.log('  worker said:\n    ' + verdict.workerLog.join('\n    '));
    if (options.keep) console.log(`  hand-off directory kept at ${root}`);
  }
  // What the worker wrote belongs to its user; the directories were opened, so the files inside
  // them can still be removed by whoever ran this — unless a step was cut off and left a
  // directory of its own behind, which is reported rather than fought.
  if (!options.keep) {
    try {
      rmSync(options.work ? root : work, { recursive: true, force: true });
    } catch (error) {
      console.error(`the hand-off directory ${root} could not be removed entirely (${error.code}); it holds files of uid ${WORKER_UID}`);
    }
  }
  return verdict.ok ? 0 : 1;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main(process.argv.slice(2)).then((code) => { process.exitCode = code; });
}
