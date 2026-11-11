// SPDX-License-Identifier: AGPL-3.0-or-later
//
// The scans worker is four files that have to agree with each other: the base compose file, the
// overlay that adds the worker, the worker's image and the loop inside it. Nothing fails loudly
// when they drift — a hand-off directory named differently on the two sides is an upload that
// waits forever for an answer nobody is writing, and a volume whose owner the two images disagree
// about is a permission error on the first write of the first job. Both are silences, so what they
// depend on is pinned here, and the loop's own answers are exercised with the system Python, which
// needs none of the tools (they are imported only by the steps).
//
// Run with the rest: `node --test "deploy/**/*.test.mjs"`.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

const deployDir = dirname(fileURLToPath(import.meta.url));
const read = (...parts) => readFileSync(join(deployDir, ...parts), 'utf8');

const base = read('docker-compose.yml');
const overlay = read('docker-compose.scans.yml');
const image = read('scans', 'Dockerfile');
const worker = read('scans', 'scan-worker.py');
const apiImage = readFileSync(join(deployDir, '..', 'server', 'Dockerfile'), 'utf8');

describe('the handover between the application and the scans worker', () => {
  it('names one directory on both sides of it', () => {
    assert.match(base, /SILEXGIS__Scans__HandoffRoot: \/data\/scans$/m);
    assert.match(overlay, /SILEXGIS_SCANS_ROOT: \/data\/scans$/m);
    assert.match(image, /ENV SILEXGIS_SCANS_ROOT=\/data\/scans/);
    assert.match(worker, /os\.environ\.get\("SILEXGIS_SCANS_ROOT", "\/data\/scans"\)/);
  });

  it('writes its answers under a temporary name and renames them into place', () => {
    // A reader that looks while an ordinary write is half finished sees a truncated file. The
    // loop writes every file that crosses the boundary through one function that renames.
    assert.match(worker, /part = path\.with_name\(path\.name \+ "\.part"\)/);
    assert.match(worker, /os\.replace\(part, path\)/);
    assert.match(worker, /write_json\(job\.directory \/ "result\.json"/);
    assert.match(worker, /write_json\(ROOT \/ "capabilities\.json"/);
  });
});

describe('the worker image', () => {
  it('pins every tool, at versions that can read LAZ and write 3D Tiles 1.0', () => {
    // py3dtiles below 12 cannot read LAZ and has no --spec-version; laspy's lazrs backend is what
    // makes a .laz readable at all. The application records these versions per run.
    assert.match(image, /"py3dtiles\[las\]==12\.1\.1"/);
    assert.match(image, /"laspy\[lazrs\]==2\.7\.0"/);
    assert.match(image, /"lazrs==0\.8\.2"/);
    assert.match(image, /"numpy==2\.3\.5"/);
  });

  it('adds Open3D for surfaces as its CPU-only build, pinned, without its viewers\' dependencies', () => {
    // The default open3d wheel carries CUDA and is four times the size; no machine here has a GPU.
    // Its declared dependencies (dash, flask, notebooks) serve viewers the worker never starts.
    assert.match(image, /pip install --no-cache-dir --no-deps "open3d-cpu==0\.20\.0"/);
    // Without these the module fails to load, though nothing draws.
    assert.match(image, /python3-venv libgomp1 libegl1 libgl1 libx11-6 libusb-1\.0-0/);
    const tiler = image.slice(image.indexOf('AS tiler'), image.indexOf('AS hdmapping'));
    assert.ok(tiler.includes('open3d-cpu'), 'the surface is made by the tiler');
  });

  it('has a tiler target that the overlay builds, and keeps HDMapping behind another', () => {
    assert.match(image, /^FROM ubuntu:24\.04 AS tiler$/m);
    assert.match(image, /^FROM tiler AS hdmapping$/m);
    assert.match(overlay, /^      target: tiler$/m);
    // The tiler must not depend on the compiled stage, or every overlay build compiles HDMapping.
    const tiler = image.slice(image.indexOf('AS tiler'), image.indexOf('AS hdmapping'));
    assert.ok(!tiler.includes('--from=build'), 'the tiler copies nothing from the HDMapping build');
  });

  it('owns the shared directory before it drops privileges, at the same id as the api', () => {
    const owns = /RUN mkdir -p [^\n]*\/data\/scans\/jobs[^\n]*chown/;
    assert.match(image, owns);
    assert.match(image, /ARG APP_UID=1654/);
    const tiler = image.slice(image.indexOf('AS tiler'));
    assert.ok(tiler.indexOf('RUN mkdir -p') < tiler.indexOf('USER '), 'the worker image must own the directory before dropping privileges');
    assert.match(apiImage, /RUN mkdir -p [^\n]*\/data\/scans\/jobs[^\n]*chown -R app:app/);
  });

  it('runs the loop as an isolated interpreter', () => {
    // -I ignores PYTHONPATH and the current directory, so nothing dropped into a job directory can
    // be imported by the worker.
    assert.match(image, /ENTRYPOINT \["\/opt\/py\/bin\/python3", "-I", "\/usr\/local\/bin\/scan-worker"\]/);
  });
});

describe('what the loop puts on the tiler command line', () => {
  it('states the spec version and takes its jobs and cache from the environment', () => {
    // Inside a container limited to a few processors and a slice of memory, the tool would
    // otherwise size itself from the host's processor count and free memory.
    assert.match(worker, /"--jobs", str\(int\(TILE_JOBS\)\), "--cache_size", str\(int\(TILE_CACHE_MB\)\)/);
    assert.match(worker, /"--spec-version", spec/);
    assert.match(overlay, /SILEXGIS_SCANS_JOBS: \$\{SILEXGIS_SCANS_WORKER_CPUS:-4\}/);
    assert.match(overlay, /SILEXGIS_SCANS_CACHE_MB: \$\{SILEXGIS_SCANS_WORKER_CACHE_MB:-2048\}/);
  });

  it('shifts every cloud to its own centre before tiling, so that no tile carries a coordinate', () => {
    assert.match(worker, /header\.offsets = np\.asarray\(reader\.header\.offsets, dtype=np\.float64\) - np\.asarray\(centre\)/);
    // Handed scale-aware records, laspy keeps their real coordinates by rescaling, undoing the shift.
    assert.match(worker, /writer\.write_points\(laspy\.PackedPointRecord\(chunk\.array, chunk\.point_format\)\)/);
    assert.match(worker, /"originShift": centre/);
  });

  it('draws the plan and profile pictures in the tiler, writing PNG with the standard library', () => {
    assert.match(worker, /^STEPS = \([^)]*"pointcloud\.raster"[^)]*\)$/m);
    assert.match(worker, /"pointcloud\.raster": step_raster,/);
    // No imaging package: the image already has numpy, and a PNG is zlib and a few chunk headers.
    assert.doesNotMatch(worker, /^\s*(import PIL|from PIL|import matplotlib|import imageio)/m);
    assert.match(worker, /zlib\.compress\(rows\.tobytes\(\), 6\)/);
  });

  it('makes surfaces in the tiler, announced only where Open3D is installed', () => {
    assert.match(worker, /^STEPS = \([^)]*"mesh\.surface"[^)]*\)$/m);
    assert.match(worker, /"mesh\.surface": step_surface,/);
    assert.match(worker, /if "open3d" in versions and "laspy" in versions and "numpy" in versions:\n\s+steps\.append\(\{"id": SURFACE_STEP, "tool": "open3d"/);
    // The surface is written about the tiles' shift, so it lines up with them and carries no
    // coordinate; the binary STL is written with numpy, not by a mesh library's own writer.
    assert.match(worker, /shift = params\["originShift"\]/);
    assert.match(worker, /def write_binary_stl\(/);
    // The package imports its plotly viewer, and with it dash and plotly, which are not installed:
    // a stand-in for that one module is put in place before the import.
    assert.match(worker, /sys\.modules\[name\] = stand_in/);
    assert.match(worker, /job\.produced\("meshStl", out\)/);
  });

  it('gives the tiler a place for its compiled-function cache on the read-only filesystem', () => {
    assert.match(image, /NUMBA_CACHE_DIR=\/tmp\/numba-cache/);
    assert.match(overlay, /- \/tmp:size=256m/);
  });
});

describe('the overlay', () => {
  it('carries the worker and nothing else, leaving the volume in the base file', () => {
    assert.match(base, /- silexgis-scans:\/data\/scans$/m);
    assert.match(base, /^  silexgis-scans:$/m);
    assert.ok(!/^volumes:/m.test(overlay), 'the overlay declares no volume of its own');
    assert.ok(!/\bweb:/.test(overlay), 'the overlay does not touch the web service');
  });

  it('publishes no port and has no network', () => {
    assert.ok(!/ports:/.test(overlay));
    assert.match(overlay, /^    network_mode: none$/m);
  });

  it('is isolated the way the survey compiler is', () => {
    assert.match(overlay, /^    read_only: true$/m);
    assert.match(overlay, /^    cap_drop:\n      - ALL$/m);
    assert.match(overlay, /^      - no-new-privileges:true$/m);
    assert.match(overlay, /^    pids_limit: 256$/m);
    assert.match(overlay, /mem_limit: \$\{SILEXGIS_SCANS_WORKER_MEMORY:-16g\}/);
    assert.match(overlay, /cpus: \$\{SILEXGIS_SCANS_WORKER_CPUS:-4\}/);
    assert.match(overlay, /- \/tmp:size=256m/);
  });

  it('stops `up` with a sentence when the application has not been told it exists', () => {
    assert.match(overlay, /\$\{SILEXGIS__Scans__ProcessingEnabled:\?set SILEXGIS__Scans__ProcessingEnabled=true[^}]*\}/);
  });
});

describe('the loop, run against a hand-off directory with the system Python', () => {
  const python = spawnSync('python3', ['--version'], { encoding: 'utf8' });
  const available = python.status === 0 && process.platform !== 'win32';
  const script = join(deployDir, 'scans', 'scan-worker.py');

  function sweep(root) {
    return spawnSync('python3', ['-I', script, '--once'], {
      encoding: 'utf8',
      env: { ...process.env, SILEXGIS_SCANS_ROOT: root, SILEXGIS_SCANS_POLL_SECONDS: '1' },
    });
  }

  function withRoot(run) {
    const root = mkdtempSync(join(tmpdir(), 'sx-scans-'));
    try {
      mkdirSync(join(root, 'jobs'));
      return run(root);
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  }

  const result = (root, name) => JSON.parse(readFileSync(join(root, 'jobs', name, 'result.json'), 'utf8'));

  it('announces itself and refuses a step it does not run', { skip: available ? false : 'python3 is not available' }, () => {
    withRoot((root) => {
      mkdirSync(join(root, 'jobs', 'a'));
      writeFileSync(join(root, 'jobs', 'a', 'request.json'), JSON.stringify({ step: 'nothing.at.all', parameters: {}, inputs: {} }));
      const run = sweep(root);
      assert.equal(run.status, 0, run.stdout + run.stderr);
      const capabilities = JSON.parse(readFileSync(join(root, 'capabilities.json'), 'utf8'));
      assert.ok(Array.isArray(capabilities.steps));
      assert.equal(result(root, 'a').status, 'refused');
      assert.ok(existsSync(join(root, 'jobs', 'a', 'started')));
    });
  });

  it('refuses an input that is not a plain file name, without reading it', { skip: available ? false : 'python3 is not available' }, () => {
    withRoot((root) => {
      mkdirSync(join(root, 'jobs', 'b'));
      writeFileSync(join(root, 'jobs', 'b', 'request.json'),
        JSON.stringify({ step: 'pointcloud.estimate', parameters: {}, inputs: { pointCloudLaz: '../../etc/passwd' } }));
      sweep(root);
      const answer = result(root, 'b');
      assert.equal(answer.status, 'refused');
      assert.match(answer.reason, /plain file name/);
    });
  });

  it('answers a withdrawn job as cancelled and an abandoned one as interrupted, running neither', { skip: available ? false : 'python3 is not available' }, () => {
    withRoot((root) => {
      for (const name of ['withdrawn', 'abandoned']) {
        mkdirSync(join(root, 'jobs', name));
        writeFileSync(join(root, 'jobs', name, 'request.json'), JSON.stringify({ step: 'py3dtiles.tile', parameters: {}, inputs: {} }));
      }
      writeFileSync(join(root, 'jobs', 'withdrawn', 'cancel'), '');
      writeFileSync(join(root, 'jobs', 'abandoned', 'started'), '2026-01-01T00:00:00Z\n');
      sweep(root);
      assert.equal(result(root, 'withdrawn').status, 'cancelled');
      assert.equal(result(root, 'abandoned').status, 'interrupted');
      assert.ok(!existsSync(join(root, 'jobs', 'abandoned', 'worker.log')), 'nothing ran');
    });
  });

  it('refuses a surface whose parameters are out of bounds before it loads any tool', { skip: available ? false : 'python3 is not available' }, () => {
    withRoot((root) => {
      const cases = {
        deep: { parameters: { poissonDepth: 40 }, reason: /poissonDepth must be between 5 and 12/ },
        fraction: { parameters: { targetTriangles: 1500.5 }, reason: /targetTriangles must be a whole number/ },
        word: { parameters: { voxelCm: 'two' }, reason: /voxelCm must be a number/ },
        shift: { parameters: { originShift: [1, 2] }, reason: /originShift must be three finite numbers/ },
      };
      for (const [name, { parameters }] of Object.entries(cases)) {
        mkdirSync(join(root, 'jobs', name));
        writeFileSync(join(root, 'jobs', name, 'cloud.laz'), '');
        writeFileSync(join(root, 'jobs', name, 'request.json'),
          JSON.stringify({ step: 'mesh.surface', parameters, inputs: { pointCloudLaz: 'cloud.laz' } }));
      }
      mkdirSync(join(root, 'jobs', 'nocloud'));
      writeFileSync(join(root, 'jobs', 'nocloud', 'request.json'), JSON.stringify({ step: 'mesh.surface', parameters: {}, inputs: {} }));
      sweep(root);
      for (const [name, { reason }] of Object.entries(cases)) {
        const answer = result(root, name);
        assert.equal(answer.status, 'refused', name);
        assert.match(answer.reason, reason);
      }
      assert.equal(result(root, 'nocloud').status, 'refused');
      assert.match(result(root, 'nocloud').reason, /no input of kind pointCloudLaz/);
    });
  });

  it('leaves an answered job alone', { skip: available ? false : 'python3 is not available' }, () => {
    withRoot((root) => {
      mkdirSync(join(root, 'jobs', 'done'));
      writeFileSync(join(root, 'jobs', 'done', 'request.json'), JSON.stringify({ step: 'nothing.at.all' }));
      writeFileSync(join(root, 'jobs', 'done', 'result.json'), JSON.stringify({ status: 'ok' }));
      sweep(root);
      assert.deepEqual(result(root, 'done'), { status: 'ok' });
    });
  });
});
