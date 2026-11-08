// SPDX-License-Identifier: AGPL-3.0-or-later

// The HDMapping worker, run for real on one raw MANDEYE recording: the built `hdmapping` image is
// started twice the way the overlay starts the two scans workers — once as the HDMapping worker,
// once as the tiler — both isolated as the overlay isolates them and sharing one hand-off
// directory. The recording is then handed through the steps in the order the application hands
// them (preflight, odometry, export, the export check, the estimate, optionally a downsample, and
// tiling), each as a request renamed into a job directory with the answer read back from
// result.json, and every answer is checked against the contract the application relies on.
//
// It prints a verdict — the two workers' announcement, each step's timing, the points, the session
// and tile sizes, and memory — and never a point, a coordinate or a position. What it checks that
// nothing else can: that HDMapping as compiled into the image runs headless inside the overlay's
// limits (no network, read-only root, no capabilities, bounded processes and memory), that the
// worker's answers match the files it wrote, that the two workers announce into one
// capabilities.json without dropping each other's steps, and that the tiles made from the export
// sit in a local frame. It is run by hand after a change to the image, the worker loop or the
// application's reading of the HDMapping answers — never in hosted CI, which has neither the image
// (its build compiles HDMapping) nor a recording.
//
// Usage:
//   node scripts/scans-hdmapping-check.mjs <recording folder> --work <dir> [options]
//     --work <dir>        where the hand-off directory goes. Required: the odometry's cache peaks at
//                         about twenty times the recording inside the job directory, so it belongs
//                         on a disk with room, never a memory-backed temporary directory. Removed
//                         afterwards unless --keep.
//     --image <name>      the HDMapping worker image (default silexgis-scans:hdmapping)
//     --tiler-image <n>   the tiler image (default: the HDMapping image, run as the tiler, so both
//                         workers carry the same loop)
//     --build             build the HDMapping image first from deploy/scans (target hdmapping;
//                         compiling HDMapping takes most of an hour without a build cache)
//     --voxel-cm <n>      downsample the export with a voxel of n cm before tiling (default: tile
//                         the export as it is)
//     --cpus <n>          processors for the HDMapping container (default 8, the overlay's)
//     --memory <size>     the HDMapping container's memory limit (default 16g, the overlay's)
//     --timeout <s>       the most one step may take (default 14400)
//     --keep              leave the hand-off directory for inspection
//     --json              print the verdict as JSON instead of text
// Exit code 0 when every step answered ok and every check held, 1 otherwise, 2 for a bad call.

import {
  chmodSync, closeSync, copyFileSync, existsSync, mkdtempSync, openSync, readFileSync, readSync, readdirSync,
  rmSync, statSync, writeFileSync,
} from 'node:fs';
import { basename, dirname, join, resolve } from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

import {
  WORKER_UID, checkAnswer, docker, handOver, measureOutputs, openDir, readArchive, readProgress, sleep,
  waitForAnswer,
} from './scans-worker-check.mjs';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');

// The overlay's isolation lines for both scans workers; the check runs under the same ones so that
// what it measures is what an installation runs.
const PIDS_LIMIT = 256;
const TMPFS = '/tmp:size=256m';
const TILER_CPUS = 4;
const TILER_MEMORY = '16g';

const MANIFEST = 'recording.json';

// ---------------------------------------------------------------------------------------------
// Pure parts, exported for the test.

/** Parses the command line into options; throws on a bad call. */
export function parseArgs(argv) {
  const options = {
    recording: null, work: null, image: 'silexgis-scans:hdmapping', tilerImage: null, build: false,
    voxelCm: null, cpus: 8, memory: '16g', timeout: 14400, keep: false, json: false,
  };
  const takes = {
    '--work': 'work', '--image': 'image', '--tiler-image': 'tilerImage', '--voxel-cm': 'voxelCm',
    '--cpus': 'cpus', '--memory': 'memory', '--timeout': 'timeout',
  };
  const flags = { '--build': 'build', '--keep': 'keep', '--json': 'json' };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg in takes) {
      const value = argv[++i];
      if (value === undefined) throw new Error(`${arg} needs a value`);
      options[takes[arg]] = value;
    } else if (arg in flags) {
      options[flags[arg]] = true;
    } else if (arg.startsWith('--')) {
      throw new Error(`unknown option ${arg}`);
    } else if (options.recording === null) {
      options.recording = arg;
    } else {
      throw new Error(`one recording at a time; '${arg}' is a second one`);
    }
  }
  if (options.recording === null) throw new Error('name the recording folder: node scripts/scans-hdmapping-check.mjs <folder> --work <dir>');
  if (options.work === null) {
    throw new Error('--work <dir> is required: the odometry cache peaks at about twenty times the recording, on a disk with room, not in a memory-backed temporary directory');
  }
  for (const name of ['cpus', 'timeout', ...(options.voxelCm === null ? [] : ['voxelCm'])]) {
    const n = Number(options[name]);
    if (!Number.isFinite(n) || n <= 0) throw new Error(`${name} must be a positive number, not '${options[name]}'`);
    options[name] = n;
  }
  options.tilerImage ??= options.image;
  return options;
}

// The names a MANDEYE recording holds, as the worker and the application classify them. Anything
// else in the folder (camera pictures, notes) is not part of what is processed and is left out.
const RECORDING_FILES = [
  [/^lidar(\d{1,6})\.laz$/i, 'lidar'],
  [/^imu(\d{1,6})\.csv$/i, 'imu'],
  [/^lidar(\d{1,6})\.sn$/i, 'serial'],
  [/^gnss(\d{1,6})\.gnss$/i, 'gnss'],
  [/\.nmea$/i, 'nmea'],
  [/^calibration\.json$/i, 'calibration'],
];

/**
 * The recording a folder holds, from its file names and sizes: the files a recording is made of,
 * the chunks paired, whether it carries GNSS, and what was left out. `entries` is
 * [{name, bytes}] for the folder's plain files.
 */
export function selectRecording(entries) {
  const files = [];
  const ignored = [];
  const lidar = new Set();
  const imu = new Set();
  let gnss = false;
  for (const entry of entries) {
    const match = RECORDING_FILES.find(([pattern]) => pattern.test(entry.name));
    if (!match) { ignored.push(entry.name); continue; }
    files.push(entry);
    const [pattern, kind] = match;
    const chunk = pattern.exec(entry.name)[1];
    if (kind === 'lidar') lidar.add(Number(chunk));
    else if (kind === 'imu') imu.add(Number(chunk));
    else if (kind === 'gnss' || kind === 'nmea') gnss = true;
  }
  files.sort((a, b) => a.name.localeCompare(b.name));
  const paired = lidar.size > 0 && lidar.size === imu.size && [...lidar].every((n) => imu.has(n));
  return {
    files, ignored, chunks: lidar.size, paired, gnss,
    bytes: files.reduce((sum, f) => sum + f.bytes, 0),
  };
}

/**
 * The point count a LAS or LAZ file's public header declares (the header is uncompressed even in
 * a LAZ file): the 64-bit count of a LAS 1.4 header when it is set, the legacy 32-bit one
 * otherwise. Null for bytes that are not a LAS header.
 */
export function lasPointCount(head) {
  if (head.length < 227 || head.toString('latin1', 0, 4) !== 'LASF') return null;
  const minor = head[25];
  const headerSize = head.readUInt16LE(94);
  const legacy = head.readUInt32LE(107);
  if (minor >= 4 && headerSize >= 375 && head.length >= 255) {
    const wide = Number(head.readBigUInt64LE(247));
    return wide || legacy;
  }
  return legacy;
}

/** The step list the application runs for a raw recording, with each step's inputs named. */
export function plannedSteps(voxelCm) {
  return [
    { id: 'hdmapping.preflight', worker: 'hdmapping', parameters: {} },
    { id: 'hdmapping.odometry', worker: 'hdmapping', parameters: {} },
    { id: 'hdmapping.export', worker: 'hdmapping', parameters: {} },
    { id: 'pointcloud.check', worker: 'hdmapping', parameters: {} },
    { id: 'pointcloud.estimate', worker: 'tiler', parameters: {} },
    ...(voxelCm === null ? [] : [{ id: 'pointcloud.downsample', worker: 'tiler', parameters: { method: 'voxel', voxelCm } }]),
    { id: 'py3dtiles.tile', worker: 'tiler', parameters: { specVersion: '1.0', rgb: true, intensity: true } },
  ];
}

/**
 * What the shared capabilities.json must say once both workers have announced: every step of the
 * run offered, each by the worker that takes it. Returns the failed checks, in words.
 */
export function checkAnnouncement(capabilities, steps) {
  const failed = [];
  const offered = new Map((capabilities?.steps ?? []).map((s) => [s.id, s.owner]));
  for (const step of steps) {
    if (!offered.has(step.id)) failed.push(`capabilities.json does not offer ${step.id}`);
    else if (offered.get(step.id) !== step.worker) failed.push(`capabilities.json gives ${step.id} to ${offered.get(step.id)}, not ${step.worker}`);
  }
  const workers = Object.keys(capabilities?.workers ?? {});
  for (const role of ['hdmapping', 'tiler']) {
    if (!workers.includes(role)) failed.push(`capabilities.json names no ${role} worker`);
  }
  return failed;
}

/** The verdict as text. */
export function formatVerdict(verdict) {
  const lines = [];
  const mb = (n) => `${(n / 1048576).toFixed(1)} MB`;
  const gb = (n) => `${(n / 1073741824).toFixed(2)} GB`;
  const count = (n) => (Number.isFinite(n) ? n.toLocaleString('en-US') : '?');
  lines.push(`HDMapping worker check: ${verdict.ok ? 'OK' : 'FAILED'}`);
  lines.push(`  image     ${verdict.image}${verdict.tilerImage !== verdict.image ? `, tiler ${verdict.tilerImage}` : ''}`);
  if (verdict.hdmapping) lines.push(`  hdmapping ${verdict.hdmapping}`);
  const r = verdict.recording;
  lines.push(`  recording ${r.name}: ${r.files} files, ${r.chunks} chunks, ${mb(r.bytes)}, GNSS ${r.gnss ? 'yes' : 'no'}${r.ignored ? `, ${r.ignored} other files left out` : ''}`);
  if (verdict.announcement) {
    lines.push(`  announced ${verdict.announcement.steps} steps by ${verdict.announcement.workers.join(' + ')}`);
  }
  for (const failure of verdict.announcementFailed ?? []) lines.push(`      FAILED: ${failure}`);
  for (const step of verdict.steps) {
    const memory = step.containerAnonPeakBytes ? `, container peak ${gb(step.containerAnonPeakBytes)}` : '';
    lines.push(`  ${step.id.padEnd(22)} ${step.status.padEnd(9)} ${step.seconds.toFixed(1)} s worker, ${step.wallSeconds.toFixed(1)} s wall${memory}`);
    const f = step.figures ?? {};
    if (step.id === 'hdmapping.preflight' && f.points !== undefined) {
      lines.push(`      ${count(f.chunks)} chunks, ${count(f.points)} raw points; room ${gb(f.freeBytes)} free for ${gb(f.neededBytes)} needed`);
    } else if (step.id === 'hdmapping.odometry' && f.odometry) {
      const o = f.odometry;
      lines.push(`      HDMapping ${f.parameters?.softwareVersion ?? '?'}: ${count(o.scans)} scans, ${o.elapsedSeconds ?? '?'} s computing, trajectory ${Number.isFinite(o.trajectoryMetres) ? o.trajectoryMetres.toFixed(1) : '?'} m`);
      lines.push(`      its own peaks: resident ${o.residentPeakMiB ?? '?'} MiB, cache ${o.cachePeakMiB ?? '?'} MiB; session archive ${mb(f.sessionBytes ?? 0)} in ${count(f.sessionFiles)} files`);
    } else if (step.id === 'hdmapping.export' && f.export) {
      lines.push(`      ${count(f.export.pointsWritten)} points, cloud ${mb(f.cloudBytes ?? 0)}, trajectory ${mb(f.trajectoryBytes ?? 0)}${f.export.missingTimestamps ? '; some poses carry no time' : ''}`);
    } else if (step.id === 'pointcloud.check' && f.pointCount !== undefined) {
      lines.push(`      ${count(f.pointCount)} points checked, extent ${(f.extentMetres ?? []).map((v) => v.toFixed(1)).join(' × ')} m; trajectory ${count(f.trajectory?.rows)} poses, ${f.trajectory?.lengthMetres ?? '?'} m, ${count(f.trajectory?.missingTimestamps)} without time`);
    } else if (step.id === 'pointcloud.estimate' && f.pointCount !== undefined) {
      lines.push(`      points ${count(f.pointCount)}, rgb ${f.hasRgb}, intensity ${f.hasIntensity}; full: ${mb(f.full?.estimatedTileBytes ?? 0)} in ${(f.full?.estimatedSeconds ?? 0).toFixed(0)} s (estimated)`);
    } else if (step.id === 'pointcloud.downsample' && f.pointsIn !== undefined) {
      lines.push(`      ${f.method} ${f.voxelCm ?? f.everyNth}: ${count(f.pointsIn)} → ${count(f.pointsOut)} points`);
    } else if (step.id === 'py3dtiles.tile' && f.tileCount !== undefined) {
      lines.push(`      ${count(f.pointCount)} points → ${count(f.tileCount)} files, ${mb(f.tileBytes)} of tiles, archive ${mb(f.archiveBytes)}; rgb ${f.rgb}, intensity ${f.intensity}`);
    }
    for (const failure of step.failed) lines.push(`      FAILED: ${failure}`);
  }
  if (verdict.totalSeconds !== undefined) lines.push(`  total     ${(verdict.totalSeconds / 60).toFixed(1)} min wall`);
  if (verdict.aborted) lines.push(`  aborted   ${verdict.aborted}`);
  return lines.join('\n');
}

// ---------------------------------------------------------------------------------------------
// Running the containers.

/**
 * The anonymous memory a container's processes hold now, from its control group: the memory its
 * programs allocated, without the page cache that writing gigabytes of files fills and the kernel
 * gives back on demand. Null when the control group cannot be read (another cgroup layout).
 */
function containerAnonBytes(id) {
  for (const path of [
    `/sys/fs/cgroup/system.slice/docker-${id}.scope/memory.stat`,
    `/sys/fs/cgroup/docker/${id}/memory.stat`,
  ]) {
    try {
      const stat = readFileSync(path, 'utf8');
      const found = /^anon (\d+)$/m.exec(stat);
      if (found) return Number(found[1]);
    } catch {
      // try the next layout
    }
  }
  return null;
}

function headerPoints(path) {
  const head = Buffer.alloc(375);
  const fd = openSync(path, 'r');
  try {
    const read = readSync(fd, head, 0, head.length, 0);
    return lasPointCount(head.subarray(0, read));
  } finally {
    closeSync(fd);
  }
}

function copyIn(from, jobDir, name) {
  copyFileSync(from, join(jobDir, name));
  chmodSync(join(jobDir, name), 0o644);
}

function startWorker(name, image, role, root, cpus, memory, timeout) {
  // The overlay's isolation for both workers: no network, read-only root with a small tmpfs /tmp,
  // no capabilities, bounded memory, processors and processes. Only the hand-off directory is
  // writable.
  const run = docker([
    'run', '-d', '--rm', '--name', name,
    '--network', 'none', '--read-only', '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges:true',
    '--pids-limit', String(PIDS_LIMIT), '--tmpfs', TMPFS,
    '--memory', String(memory), '--cpus', String(cpus),
    '-e', 'SILEXGIS_SCANS_ROOT=/data/scans', '-e', `SILEXGIS_SCANS_WORKER_ROLE=${role}`,
    '-e', 'SILEXGIS_SCANS_POLL_SECONDS=1', '-e', `SILEXGIS_SCANS_JOBS=${Math.max(1, Math.floor(cpus))}`,
    '-e', `SILEXGIS_SCANS_JOB_TIMEOUT_SECONDS=${timeout}`,
    '-v', `${root}:/data/scans`,
    image,
  ]);
  if (run.status !== 0) throw new Error(`the ${role} worker did not start: ${run.stderr.trim()}`);
  return run.stdout.trim();
}

async function waitForAnnouncement(root, roles, seconds) {
  const path = join(root, 'capabilities.json');
  for (let i = 0; i < seconds * 4; i++) {
    try {
      const value = JSON.parse(readFileSync(path, 'utf8'));
      const workers = Object.keys(value.workers ?? {});
      if (roles.every((r) => workers.includes(r))) return value;
    } catch {
      // not written yet, or being replaced
    }
    await sleep(250);
  }
  return existsSync(path) ? JSON.parse(readFileSync(path, 'utf8')) : null;
}

async function main(argv) {
  let options;
  try {
    options = parseArgs(argv);
  } catch (error) {
    console.error(error.message);
    return 2;
  }
  const folder = resolve(options.recording);
  if (!existsSync(folder) || !statSync(folder).isDirectory()) { console.error(`${folder} is not a folder`); return 2; }
  const recording = selectRecording(readdirSync(folder, { withFileTypes: true })
    .filter((e) => e.isFile())
    .map((e) => ({ name: e.name, bytes: statSync(join(folder, e.name)).size })));
  if (!recording.paired) {
    console.error(`${folder} is not a MANDEYE recording: it needs lidarNNNN.laz files each paired with an imuNNNN.csv of the same number`);
    return 2;
  }

  if (options.build) {
    const build = docker(['build', '--target', 'hdmapping', '-t', options.image, join(repoRoot, 'deploy', 'scans')], { stdio: 'inherit', encoding: undefined });
    if (build.status !== 0) { console.error('the image did not build'); return 1; }
  }
  for (const image of new Set([options.image, options.tilerImage])) {
    if (docker(['image', 'inspect', image]).status !== 0) {
      console.error(`image ${image} is not built; pass --build or build it from deploy/scans with --target hdmapping`);
      return 2;
    }
  }

  const work = resolve(options.work);
  openDir(work);
  const root = mkdtempSync(join(work, 'hdmapping-check-'));
  chmodSync(root, 0o777);
  const jobs = join(root, 'jobs');
  openDir(jobs);

  const steps = plannedSteps(options.voxelCm);
  const verdict = {
    ok: false, image: options.image, tilerImage: options.tilerImage, hdmapping: null,
    recording: {
      name: basename(folder), files: recording.files.length, chunks: recording.chunks, bytes: recording.bytes,
      gnss: recording.gnss, ignored: recording.ignored.length,
    },
    announcement: null, announcementFailed: [], steps: [], aborted: null, handoff: root,
  };
  const clock = process.hrtime.bigint();
  const containers = {};
  try {
    containers.hdmapping = { name: `silexgis-hdm-check-${process.pid}` };
    containers.hdmapping.id = startWorker(containers.hdmapping.name, options.image, 'hdmapping', root, options.cpus, options.memory, options.timeout);
    containers.tiler = { name: `silexgis-tiler-check-${process.pid}` };
    containers.tiler.id = startWorker(containers.tiler.name, options.tilerImage, 'tiler', root, TILER_CPUS, TILER_MEMORY, options.timeout);

    // The HDMapping worker dumps its default parameters before it announces anything.
    const capabilities = await waitForAnnouncement(root, ['hdmapping', 'tiler'], 120);
    if (!capabilities) throw new Error('neither worker wrote capabilities.json within 120 s');
    verdict.hdmapping = capabilities.workers?.hdmapping?.hdmapping ?? capabilities.hdmapping ?? null;
    verdict.announcement = { steps: (capabilities.steps ?? []).length, workers: Object.keys(capabilities.workers ?? {}) };
    verdict.announcementFailed = checkAnnouncement(capabilities, steps);
    const defaults = join(root, 'defaults', 'hdmapping.odometry.json');
    if (!existsSync(defaults)) verdict.announcementFailed.push('the HDMapping worker published no default parameters');
    if (verdict.announcementFailed.length) throw new Error('the workers did not announce the run\'s steps');

    let session = null;
    let cloud = null;
    let trajectory = null;
    let display = null;
    let exportedPoints;
    for (let n = 0; n < steps.length; n++) {
      const step = steps[n];
      const jobDir = join(jobs, `${String(n + 1).padStart(2, '0')}-${step.id.replace(/\./g, '-')}`);
      openDir(jobDir);
      // The application copies each input artefact into the job directory under a plain name;
      // a recording arrives as its files under their own names and a manifest naming them.
      const inputs = {};
      if (step.id.startsWith('hdmapping.') && step.id !== 'hdmapping.export') {
        for (const file of recording.files) copyIn(join(folder, file.name), jobDir, file.name);
        handOverManifest(jobDir, recording.files.map((f) => f.name));
        inputs.rawRecording = MANIFEST;
      } else if (step.id === 'hdmapping.export') {
        copyIn(session, jobDir, 'hdmSession.zip');
        inputs.hdmSession = 'hdmSession.zip';
      } else {
        copyIn(cloud, jobDir, 'pointCloudLaz.laz');
        inputs.pointCloudLaz = 'pointCloudLaz.laz';
        if (step.id === 'pointcloud.check') {
          copyIn(trajectory, jobDir, 'trajectory.csv');
          inputs.trajectory = 'trajectory.csv';
        }
        if (step.id === 'py3dtiles.tile' && display) {
          copyIn(display, jobDir, 'pointCloudLazDisplay.laz');
          inputs.pointCloudLazDisplay = 'pointCloudLazDisplay.laz';
        }
      }

      const container = containers[step.worker];
      const started = process.hrtime.bigint();
      handOver(jobDir, { step: step.id, parameters: step.parameters, inputs, timeoutSeconds: options.timeout });
      let anonPeak = 0;
      const sampler = setInterval(() => {
        const anon = containerAnonBytes(container.id);
        if (anon !== null && anon > anonPeak) anonPeak = anon;
      }, 500);
      let result;
      try {
        result = await waitForAnswer(jobDir, options.timeout, container.name);
      } finally {
        clearInterval(sampler);
      }
      const wallSeconds = Number(process.hrtime.bigint() - started) / 1e9;

      const outputs = measureOutputs(jobDir, result);
      const fileOf = (kind) => (result.outputs ?? []).find((o) => o.kind === kind)?.file;
      let archive = null;
      const figures = { ...(result.data ?? {}) };
      if (step.id === 'hdmapping.odometry' && fileOf('hdmSession')) {
        archive = readArchive(join(jobDir, fileOf('hdmSession')), null);
        figures.sessionBytes = outputs[fileOf('hdmSession')]?.sizeBytes;
        figures.sessionFiles = archive?.entries.length;
      } else if (step.id === 'hdmapping.export' && fileOf('pointCloudLaz')) {
        exportedPoints = headerPoints(join(jobDir, fileOf('pointCloudLaz')));
        figures.cloudBytes = outputs[fileOf('pointCloudLaz')]?.sizeBytes;
        figures.trajectoryBytes = outputs[fileOf('trajectory')]?.sizeBytes;
      } else if (step.id === 'py3dtiles.tile' && fileOf('pointCloudTiles')) {
        archive = readArchive(join(jobDir, fileOf('pointCloudTiles')));
      }
      const files = { started: existsSync(join(jobDir, 'started')), progressFraction: readProgress(jobDir) };
      const extra = step.id === 'hdmapping.preflight'
        ? { recording: { chunks: recording.chunks, bytes: recording.bytes, gnss: recording.gnss } }
        : exportedPoints === undefined ? {} : { exportedPoints };
      const failed = checkAnswer(step.id, result, files, outputs, archive, extra);
      verdict.steps.push({
        id: step.id, worker: step.worker, status: result.status, reason: result.reason, seconds: result.seconds ?? 0,
        wallSeconds, containerAnonPeakBytes: anonPeak || null, tool: result.tool, toolVersion: result.toolVersion,
        figures: withoutPositions(step.id, figures), failed,
      });
      if (result.status !== 'ok') break;
      if (step.id === 'hdmapping.odometry') session = join(jobDir, fileOf('hdmSession'));
      if (step.id === 'hdmapping.export') {
        cloud = join(jobDir, fileOf('pointCloudLaz'));
        trajectory = join(jobDir, fileOf('trajectory'));
      }
      if (step.id === 'pointcloud.downsample') display = join(jobDir, fileOf('pointCloudLazDisplay'));
    }
    verdict.ok = verdict.steps.length === steps.length && verdict.steps.every((s) => s.failed.length === 0);
  } catch (error) {
    verdict.aborted = error.message;
    for (const [role, container] of Object.entries(containers)) {
      const log = docker(['logs', '--tail', '15', container.name]);
      const text = `${log.stdout ?? ''}${log.stderr ?? ''}`.trim();
      if (text) (verdict.workerLog ??= []).push(`[${role}]`, ...text.split('\n'));
    }
  } finally {
    for (const container of Object.values(containers)) docker(['stop', '-t', '15', container.name]);
  }
  verdict.totalSeconds = Number(process.hrtime.bigint() - clock) / 1e9;

  if (options.json) console.log(JSON.stringify(verdict, null, 2));
  else {
    console.log(formatVerdict(verdict));
    if (verdict.workerLog) console.log('  workers said:\n    ' + verdict.workerLog.join('\n    '));
    if (options.keep) console.log(`  hand-off directory kept at ${root}`);
  }
  if (!options.keep) {
    try {
      rmSync(root, { recursive: true, force: true });
    } catch {
      // A folder the worker made (its published defaults, HDMapping's home inside a job) belongs to
      // the worker's user, and what is inside it can only be removed as that user: once more, from
      // inside the image.
      docker(['run', '--rm', '--network', 'none', '--user', String(WORKER_UID), '--entrypoint', 'rm',
        '-v', `${root}:/data/scans`, options.image, '-rf', '/data/scans/jobs', '/data/scans/defaults']);
      try {
        rmSync(root, { recursive: true, force: true });
      } catch (error) {
        console.error(`the hand-off directory ${root} could not be removed entirely (${error.code}); it holds files of uid ${WORKER_UID}`);
      }
    }
  }
  return verdict.ok ? 0 : 1;
}

function handOverManifest(jobDir, names) {
  // Written the way the application writes it: one JSON object naming the recording's files.
  const path = join(jobDir, MANIFEST);
  writeFileSync(path, JSON.stringify({ files: names }));
  chmodSync(path, 0o644);
}

/**
 * The figures a step reported, without anything that could be a position. The worker's answers
 * already carry sizes and counts only; the tile step's origin shift and local extent are the
 * exceptions, and the verdict keeps neither (a georeferenced export would make the shift a real
 * coordinate).
 */
function withoutPositions(stepId, figures) {
  if (stepId !== 'py3dtiles.tile') return figures;
  const { originShift: _shift, localMin: _min, localMax: _max, ...rest } = figures;
  return rest;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main(process.argv.slice(2)).then((code) => { process.exitCode = code; });
}
