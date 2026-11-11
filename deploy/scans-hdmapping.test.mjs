// SPDX-License-Identifier: AGPL-3.0-or-later
//
// The HDMapping worker is the scan worker's loop run as a second worker beside the tiler, taking a
// raw scanner recording through odometry and export. HDMapping itself is compiled only into the
// worker's large image, so here its two programs are stand-ins that behave as the real ones were
// measured to: step 1 writes a cache inside the folder it reads from and a session folder of
// relative names; step 2 writes one cloud and a trajectory and prints the session's extent. What is
// proven is everything around them — the recording's pairing, the parameters resolved over the
// dumped defaults, the writable folder of links, progress, the stage markers, the session archive,
// the two workers' shared announcement — with the system Python and nothing installed.
//
// Run with the rest: `node --test "deploy/**/*.test.mjs"`.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { chmodSync, existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

const deployDir = dirname(fileURLToPath(import.meta.url));
const script = join(deployDir, 'scans', 'scan-worker.py');
const overlay = readFileSync(join(deployDir, 'docker-compose.scans.yml'), 'utf8');
const image = readFileSync(join(deployDir, 'scans', 'Dockerfile'), 'utf8');

const python = spawnSync('python3', ['--version'], { encoding: 'utf8' });
const available = python.status === 0 && process.platform !== 'win32';
const skip = available ? false : 'python3 is not available';

const DEFAULTS = `[version_info]
software_version = "0.106.0"
config_version = "1.0"

[paths]
current_output_dir = ""

[lidar_odometry_control]
max_iterations = 1000
use_motion_from_previous_step = true
resolution = 0.3

[misc]
clear_color = [0.45, 0.55, 0.6]
`;

const FAKE_ODOMETRY = `#!/usr/bin/env python3
import json, os, shutil, sys
from pathlib import Path
args = sys.argv[1:]
if args[0] == "--dump-default-params":
    Path(args[1]).write_text(${JSON.stringify(DEFAULTS)})
    print("Default parameters saved to: " + args[1])
    sys.exit(0)
source, params, out = (Path(a) for a in args)
(source / "cache").mkdir()
(source / "cache" / "chunk.cache").write_bytes(b"0" * 64)
assert (source / "lidar0000.laz").is_file()
print("Loading point clouds (1 files)..", flush=True)
for i in range(1, 4):
    print(f"[2026-01-01 00:00:00.000] [info] finished optimizing (since start : 0.1 [s]) - {i} / 3 at iteration 1 / 10", flush=True)
if os.environ.get("FAKE_HDM_FAIL"):
    sys.exit(3)
out.mkdir()
(out / "session.mjs").write_text(json.dumps({"Session Settings": {"lidar_odometry_version": "0.106.0", "folder_name": "."},
                                             "laz_file_names": [{"file_name": "scan_lio_0.laz"}]}))
(out / "scan_lio_0.laz").write_bytes(b"LASF" + bytes(400))
(out / "params_used.toml").write_text(params.read_text())
print("[2026-01-01 00:00:01.000] [info] total_length_of_calculated_trajectory: 12.5 [m]")
shutil.rmtree(source / "cache")
print("Cache: removed; peak 1.5 MiB")
print("Status: completed")
`;

const FAKE_EXPORT = `#!/usr/bin/env python3
import json, sys
from pathlib import Path
session, flag, out = sys.argv[1:]
assert flag == "--export"
assert json.loads(Path(session).read_text())["laz_file_names"]
out = Path(out)
out.mkdir()
(out / "all_step_2.laz").write_bytes(b"LASF" + bytes(400))
(out / "trajectories.csv").write_text("1.0 0 0 0 0 0 0 0 1\\n2.0 0 1 0 0 0 0 0 1\\n")
print("X (length): 512345.1 / 512445.1 / 100.0")
print("successfully written 1234 points")
`;

// A LAS 1.2 public header declaring this many points, and nothing after it.
function lasHeader(points) {
  const head = Buffer.alloc(227);
  head.write('LASF', 0, 'latin1');
  head[24] = 1;
  head[25] = 2;
  head.writeUInt16LE(227, 94);
  head.writeUInt32LE(points, 107);
  return head;
}

function withRoot(run) {
  const root = mkdtempSync(join(tmpdir(), 'sx-hdm-'));
  try {
    mkdirSync(join(root, 'jobs'));
    const bin = join(root, 'bin');
    mkdirSync(bin);
    writeFileSync(join(bin, 'odometry'), FAKE_ODOMETRY);
    writeFileSync(join(bin, 'export'), FAKE_EXPORT);
    chmodSync(join(bin, 'odometry'), 0o755);
    chmodSync(join(bin, 'export'), 0o755);
    return run(root);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

function sweep(root, role = 'hdmapping', extra = {}) {
  const run = spawnSync('python3', ['-I', script, '--once'], {
    encoding: 'utf8',
    env: {
      ...process.env,
      SILEXGIS_SCANS_ROOT: root,
      SILEXGIS_SCANS_POLL_SECONDS: '1',
      SILEXGIS_SCANS_WORKER_ROLE: role,
      SILEXGIS_SCANS_HDM_ODOMETRY: join(root, 'bin', 'odometry'),
      SILEXGIS_SCANS_HDM_EXPORT: join(root, 'bin', 'export'),
      HDMAPPING_COMMIT: 'abc123',
      ...extra,
    },
  });
  assert.equal(run.status, 0, run.stdout + run.stderr);
  return run;
}

function job(root, name, request, files = {}) {
  const directory = join(root, 'jobs', name);
  mkdirSync(directory);
  for (const [file, content] of Object.entries(files)) {
    writeFileSync(join(directory, file), content);
  }
  writeFileSync(join(directory, 'request.json'), JSON.stringify(request));
  return directory;
}

const recording = () => ({
  'lidar0000.laz': lasHeader(1000),
  'imu0000.csv': 'timestamp gyroX gyroY gyroZ accX accY accZ\n1 0 0 0 0 0 1\n',
  'lidar0000.sn': 'serial',
  'recording.json': JSON.stringify({ files: ['lidar0000.laz', 'imu0000.csv', 'lidar0000.sn'] }),
});

const result = (directory) => JSON.parse(readFileSync(join(directory, 'result.json'), 'utf8'));
const capabilities = (root) => JSON.parse(readFileSync(join(root, 'capabilities.json'), 'utf8'));

// Lists a zip's member names with the system Python, which every test here already needs.
function zipNames(archive) {
  const run = spawnSync('python3', ['-I', '-c', 'import json,sys,zipfile;print(json.dumps(zipfile.ZipFile(sys.argv[1]).namelist()))', archive], { encoding: 'utf8' });
  return JSON.parse(run.stdout);
}

// Loads the worker as a module (its loop runs only as a program) and prints what a call returns.
function call(expression) {
  const harness = `
import importlib.util, json, sys
spec = importlib.util.spec_from_file_location("scan_worker", sys.argv[1])
w = importlib.util.module_from_spec(spec)
spec.loader.exec_module(w)
try:
    value = ${expression}
    print(json.dumps({"value": value}))
except Exception as error:
    print(json.dumps({"error": type(error).__name__, "message": str(error)}))
`;
  const run = spawnSync('python3', ['-I', '-c', harness, script], { encoding: 'utf8' });
  assert.equal(run.status, 0, run.stderr);
  return JSON.parse(run.stdout);
}

describe('the HDMapping worker in the overlay', () => {
  it('is a second service on the same volume, isolated as the tiler is, behind a profile', () => {
    const service = overlay.slice(overlay.indexOf('  scans-hdmapping:'));
    assert.match(service, /^    profiles: \["hdmapping"\]$/m);
    assert.match(service, /^      target: hdmapping$/m);
    assert.match(service, /^    network_mode: none$/m);
    assert.match(service, /^      - silexgis-scans:\/data\/scans$/m);
    assert.match(service, /^    read_only: true$/m);
    assert.match(service, /^    cap_drop:\n      - ALL$/m);
    assert.match(service, /^      - no-new-privileges:true$/m);
    assert.match(service, /^    pids_limit: 256$/m);
    assert.match(service, /mem_limit: \$\{SILEXGIS_SCANS_HDM_MEMORY:-16g\}/);
    assert.match(service, /cpus: \$\{SILEXGIS_SCANS_HDM_CPUS:-8\}/);
    assert.match(service, /- \/tmp:size=256m/);
    assert.match(service, /SILEXGIS_SCANS_WORKER_ROLE: hdmapping/);
    assert.ok(!/ports:/.test(service));
  });

  it('runs the same loop in the image, in the HDMapping role', () => {
    const target = image.slice(image.indexOf('FROM tiler AS hdmapping'));
    assert.match(target, /SILEXGIS_SCANS_WORKER_ROLE=hdmapping/);
  });
});

describe('the worker\'s pure parts', { skip }, () => {
  it('reads step 1\'s progress from the poses it optimises', () => {
    const line = '[2026-11-07 08:13:45.059] [info] finished optimizing (since start : 0.270 [s]) - 1311 / 2622 at iteration 172 / 1000 in (load vector 0.000 [s])';
    const [fraction, message] = call(`w.odometry_progress(${JSON.stringify(line)})`).value;
    assert.ok(Math.abs(fraction - (0.05 + 0.85 * 0.5)) < 1e-9);
    assert.match(message, /1,311 of 2,622 poses/);
    assert.equal(call('w.odometry_progress("running iterations: 51/2622")').value[0] > 0.05, true);
    assert.equal(call('w.odometry_progress("header->number_of_point_records: 1593696")').value, null);
    assert.equal(call('w.odometry_progress("Status: completed")').value[0], 0.98);
  });

  it('lays a run\'s settings over the defaults, and refuses what the defaults do not hold', () => {
    const defaults = `{"version_info": {"software_version": "0.106.0"}, "paths": {"current_output_dir": ""}, "c": {"n": 1000, "on": True, "r": 0.3, "l": [0.5, 0.5]}}`;
    const ok = call(`w.resolve_parameters(${defaults}, {"version_info": {"software_version": "0.1"}, "c": {"n": 500, "r": 1, "on": True}})`).value;
    assert.deepEqual(ok[0].c, { n: 500, on: true, r: 1.0, l: [0.5, 0.5] });
    assert.equal(ok[0].version_info.software_version, '0.106.0', 'the program\'s own version always wins');
    assert.deepEqual(ok[1], ['c.n', 'c.r']);
    const refused = (overrides) => call(`w.resolve_parameters(${defaults}, ${overrides})`);
    assert.match(refused('{"c": {"nope": 1}}').message, /c\.nope is not one of/);
    assert.match(refused('{"x": {"n": 1}}').message, /\[x\] is not a section/);
    assert.match(refused('{"c": {"n": 1.5}}').message, /c\.n must be a whole number/);
    assert.match(refused('{"c": {"on": 1}}').message, /c\.on must be true or false/);
    assert.match(refused('{"paths": {"current_output_dir": "/etc"}}').message, /cannot be set/);
    assert.equal(refused('{"c": {"n": 1}}').error, undefined);
  });

  it('writes HDMapping\'s parameter file so that it reads back as the same values', () => {
    const values = `{"version_info": {"software_version": "0.106.0"}, "s": {"f": 0.10000000000000001, "big": 1e-12, "i": 7, "b": False, "t": "a \\"q\\"", "l": [0.45, 0.55]}}`;
    const same = call(`__import__("tomllib").loads(w.toml_text(${values})) == ${values}`).value;
    assert.equal(same, true);
  });

  it('pairs a recording\'s chunks and refuses a file no recording holds', () => {
    const paired = call('w.pair_recording(["lidar0001.laz", "IMU1.csv", "gnss0001.gnss", "calibration.json", "x.nmea"])').value;
    assert.deepEqual(paired.lidar, { 1: 'lidar0001.laz' });
    assert.equal(paired.gnss, true);
    assert.equal(call('w.pair_recording(["lidar0001.laz", "imu0002.csv"])').error, 'CheckFailed');
    assert.equal(call('w.pair_recording(["lidar0001.laz", "imu0001.csv", "notes.txt"])').error, 'Refused');
    assert.equal(call('w.pair_recording(["lidar0001.laz", "imu0001.csv", "../imu0001.csv"])').error, 'Refused');
  });

  it('withholds the extent HDMapping prints from a result\'s log tail', () => {
    assert.equal(call('w.withheld("X (length): 512345.1 / 512445.1 / 100.0")').value, 'X (length): [withheld]');
    assert.equal(call('w.withheld("successfully written 12 points")').value, 'successfully written 12 points');
  });

  it('merges a worker\'s announcement with the other worker\'s entries', () => {
    const merged = call(`w.merge_capabilities({"worker": "1", "steps": [{"id": "hdmapping.odometry", "owner": "hdmapping"}]},
      {"steps": [{"id": "py3dtiles.tile", "owner": "tiler"}, {"id": "hdmapping.odometry", "owner": "hdmapping", "version": "old"}, {"id": "legacy"}],
       "workers": {"tiler": {"worker": "1"}}}, "hdmapping")`).value;
    assert.deepEqual(merged.steps.map((s) => s.id).sort(), ['hdmapping.odometry', 'py3dtiles.tile']);
    assert.deepEqual(Object.keys(merged.workers).sort(), ['hdmapping', 'tiler']);
  });
});

describe('the HDMapping worker, run against a hand-off directory with stand-in programs', { skip }, () => {
  it('dumps the defaults beside its announcement and keeps the tiler\'s entries', () => {
    withRoot((root) => {
      writeFileSync(join(root, 'capabilities.json'), JSON.stringify({ steps: [{ id: 'py3dtiles.tile', tool: 'py3dtiles', version: '12.1.1', owner: 'tiler' }] }));
      sweep(root);
      const announced = capabilities(root);
      const ids = announced.steps.map((s) => s.id);
      for (const id of ['py3dtiles.tile', 'hdmapping.preflight', 'hdmapping.odometry', 'hdmapping.export']) {
        assert.ok(ids.includes(id), `${id} is announced`);
      }
      const odometry = announced.steps.find((s) => s.id === 'hdmapping.odometry');
      assert.equal(odometry.version, '0.106.0@abc123');
      assert.equal(odometry.owner, 'hdmapping');
      const defaults = JSON.parse(readFileSync(join(root, odometry.defaults), 'utf8'));
      assert.equal(defaults.values.lidar_odometry_control.max_iterations, 1000);
      assert.equal(defaults.softwareVersion, '0.106.0');
      assert.ok(existsSync(join(root, 'defaults', 'hdmapping.odometry.toml')));

      // The tiler restarting keeps the HDMapping worker's entries.
      sweep(root, 'tiler');
      assert.ok(capabilities(root).steps.some((s) => s.id === 'hdmapping.odometry'));
    });
  });

  it('announces no HDMapping step when the program cannot run', () => {
    withRoot((root) => {
      sweep(root, 'hdmapping', { SILEXGIS_SCANS_HDM_ODOMETRY: join(root, 'bin', 'missing') });
      assert.ok(!capabilities(root).steps.some((s) => s.id.startsWith('hdmapping.')));
    });
  });

  it('leaves the tiler\'s jobs alone, abandoned ones included', () => {
    withRoot((root) => {
      writeFileSync(join(root, 'capabilities.json'), JSON.stringify({ steps: [{ id: 'py3dtiles.tile', owner: 'tiler' }] }));
      const abandoned = job(root, 'tile', { step: 'py3dtiles.tile', inputs: {} }, { started: 'x' });
      sweep(root);
      assert.ok(!existsSync(join(abandoned, 'result.json')));
    });
  });

  it('runs odometry in a folder of links and hands back the session without its cache', () => {
    withRoot((root) => {
      const directory = job(root, 'odometry', {
        step: 'hdmapping.odometry',
        parameters: { values: { lidar_odometry_control: { max_iterations: 500 } } },
        inputs: { rawRecording: 'recording.json' },
      }, recording());
      sweep(root);
      const answer = result(directory);
      assert.equal(answer.status, 'ok', answer.reason + '\n' + answer.logTail.join('\n'));
      assert.equal(answer.tool, 'hdmapping');
      assert.equal(answer.toolVersion, '0.106.0@abc123');
      assert.deepEqual(answer.outputs.map((o) => [o.kind, o.file]), [['hdmSession', 'session.zip']]);
      assert.deepEqual(answer.data.parameters.changed, ['lidar_odometry_control.max_iterations']);
      assert.equal(answer.data.odometry.completed, true);
      assert.equal(answer.data.odometry.trajectoryMetres, 12.5);
      assert.equal(answer.data.odometry.scans, 1);
      assert.equal(answer.data.preflight.points, 1000);
      const names = zipNames(join(directory, 'session.zip'));
      assert.ok(names.includes('session.mjs') && names.includes('scan_lio_0.laz'));
      assert.ok(!names.some((n) => n.startsWith('cache')));
      assert.ok(!existsSync(join(directory, 'in')) && !existsSync(join(directory, 'lio')), 'the working folders are gone');
      assert.ok(existsSync(join(directory, 'lidar0000.laz')), 'the recording itself is untouched');
      assert.match(readFileSync(join(directory, 'params.toml'), 'utf8'), /^max_iterations = 500$/m);
      const stages = JSON.parse(readFileSync(join(directory, 'stages.json'), 'utf8'));
      assert.deepEqual(stages.done, ['preflight', 'parameters', 'links', 'odometry', 'session']);
      const progress = JSON.parse(readFileSync(join(directory, 'progress.json'), 'utf8'));
      assert.equal(progress.fraction, 1);
    });
  });

  it('answers a failed odometry as failed, and a setting the defaults do not hold as refused', () => {
    withRoot((root) => {
      const failed = job(root, 'failed', { step: 'hdmapping.odometry', inputs: { rawRecording: 'recording.json' } }, recording());
      const wrong = job(root, 'wrong', {
        step: 'hdmapping.odometry',
        parameters: { values: { lidar_odometry_control: { max_iteration: 5 } } },
        inputs: { rawRecording: 'recording.json' },
      }, recording());
      sweep(root, 'hdmapping', { FAKE_HDM_FAIL: '1' });
      assert.equal(result(failed).status, 'failed');
      assert.match(result(failed).reason, /exited 3/);
      assert.equal(result(wrong).status, 'refused');
      assert.match(result(wrong).reason, /max_iteration is not one of/);
    });
  });

  it('refuses odometry before it starts when a chunk has no pair', () => {
    withRoot((root) => {
      const files = recording();
      files['recording.json'] = JSON.stringify({ files: ['lidar0000.laz', 'lidar0000.sn'] });
      const directory = job(root, 'preflight', { step: 'hdmapping.preflight', inputs: { rawRecording: 'recording.json' } }, files);
      sweep(root);
      assert.equal(result(directory).status, 'failed');
      assert.match(result(directory).reason, /pair every lidar chunk/);
    });
  });

  it('checks a recording in preflight and reports its figures', () => {
    withRoot((root) => {
      const directory = job(root, 'preflight', { step: 'hdmapping.preflight', inputs: { rawRecording: 'recording.json' } }, recording());
      sweep(root);
      const answer = result(directory);
      assert.equal(answer.status, 'ok', answer.reason);
      assert.equal(answer.data.chunks, 1);
      assert.equal(answer.data.points, 1000);
      assert.equal(answer.data.neededBytes, answer.data.rawBytes * 22);
      assert.deepEqual(answer.outputs.map((o) => o.kind), ['report']);
    });
  });

  it('exports a session into a cloud and a trajectory, and keeps the extent out of the result', () => {
    withRoot((root) => {
      const odometry = job(root, 'odometry', { step: 'hdmapping.odometry', inputs: { rawRecording: 'recording.json' } }, recording());
      sweep(root);
      const session = readFileSync(join(odometry, 'session.zip'));
      const directory = job(root, 'export', { step: 'hdmapping.export', inputs: { hdmSession: 'hdmSession.zip' } }, { 'hdmSession.zip': session });
      sweep(root);
      const answer = result(directory);
      assert.equal(answer.status, 'ok', answer.reason);
      assert.deepEqual(answer.outputs.map((o) => [o.kind, o.file]), [['pointCloudLaz', 'export.laz'], ['trajectory', 'trajectory.csv']]);
      assert.equal(answer.data.export.pointsWritten, 1234);
      assert.ok(answer.logTail.some((l) => l.includes('[withheld]')));
      assert.ok(!answer.logTail.some((l) => l.includes('512345')));
      assert.ok(!existsSync(join(directory, 'session')) && !existsSync(join(directory, 'export')));
    });
  });

  it('refuses a session archive that names a file outside itself', () => {
    withRoot((root) => {
      const directory = job(root, 'export', { step: 'hdmapping.export', inputs: { hdmSession: 'hdmSession.zip' } });
      spawnSync('python3', ['-I', '-c', 'import sys,zipfile;z=zipfile.ZipFile(sys.argv[1],"w");z.writestr("../escape.txt","x");z.close()', join(directory, 'hdmSession.zip')]);
      sweep(root);
      assert.equal(result(directory).status, 'failed');
      assert.match(result(directory).reason, /outside itself/);
      assert.ok(!existsSync(join(root, 'jobs', 'escape.txt')));
    });
  });

  it('resumes a job stopped between stages, and calls one stopped inside a stage interrupted', () => {
    withRoot((root) => {
      const between = job(root, 'between', { step: 'hdmapping.odometry', inputs: { rawRecording: 'recording.json' } }, {
        ...recording(),
        started: 'x',
        'stages.json': JSON.stringify({ done: ['preflight', 'parameters', 'links', 'odometry'], running: null, data: { parameters: { softwareVersion: '0.106.0' } } }),
      });
      mkdirSync(join(between, 'lio'));
      writeFileSync(join(between, 'lio', 'session.mjs'), '{}');
      const inside = job(root, 'inside', { step: 'hdmapping.odometry', inputs: { rawRecording: 'recording.json' } }, {
        ...recording(),
        started: 'x',
        'stages.json': JSON.stringify({ done: ['preflight', 'parameters', 'links'], running: 'odometry', data: {} }),
      });
      sweep(root);
      const resumed = result(between);
      assert.equal(resumed.status, 'ok', resumed.reason);
      assert.deepEqual(zipNames(join(between, 'session.zip')), ['session.mjs']);
      const interrupted = result(inside);
      assert.equal(interrupted.status, 'interrupted');
      assert.equal(interrupted.data.interruptedIn, 'odometry');
      assert.ok(!existsSync(join(inside, 'worker.log')), 'nothing ran');
      assert.deepEqual(readdirSync(join(root, 'jobs')).sort(), ['between', 'inside']);
    });
  });
});
