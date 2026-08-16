// SPDX-License-Identifier: AGPL-3.0-or-later
//
// The bake worker is four files that have to agree with each other: the base compose file, the
// overlay that adds the worker, the worker's own image and the supervisor inside it. Nothing in a
// build fails loudly when they drift — a spool directory named differently on the two sides is a
// build that waits forever for an answer nobody is writing, and a volume whose owner the two
// images disagree about is a permission error on the first write of the first bake. Both are
// silences, so what they depend on is pinned here.
//
// Run with the rest: `node --test "deploy/**/*.test.mjs"`.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

const deployDir = dirname(fileURLToPath(import.meta.url));
const read = (...parts) => readFileSync(join(deployDir, ...parts), 'utf8');

const base = read('docker-compose.yml');
const overlay = read('docker-compose.terrain-worker.yml');
const image = read('terrain-worker', 'Dockerfile');
const supervisor = read('terrain-worker', 'bake-worker.sh');
const apiImage = readFileSync(join(deployDir, '..', 'server', 'Dockerfile'), 'utf8');

describe('the handover between the application and the bake worker', () => {
  it('names one directory on both sides of it', () => {
    // The application writes a request here and waits for a result to appear beside it. Named
    // differently at either end, nothing errors: the worker watches an empty directory and the
    // build waits for an answer that is being written somewhere else.
    assert.match(base, /SILEXGIS__Terrain__SpoolRoot: \/data\/terrain\/spool/);
    assert.match(overlay, /SILEXGIS_TERRAIN_ROOT: \/data\/terrain/);
    assert.match(image, /ENV SILEXGIS_TERRAIN_ROOT=\/data\/terrain/);
    assert.match(supervisor, /SPOOL="\$\{SILEXGIS_TERRAIN_SPOOL:-\$ROOT\/spool\}"/);
  });

  it('writes both halves under a temporary name and renames them into place', () => {
    // A reader that looks while an ordinary write is half finished sees a truncated file, and a
    // truncated request is a bake of the wrong thing. A rename is the only way either side can
    // be sure of what it is reading.
    assert.match(supervisor, /result\.part/);
    assert.match(supervisor, /mv "\$directory\/result\.part" "\$directory\/result"/);
  });
});

describe('the worker image', () => {
  it('is built on the pinned pre-baker rather than on a tag that moves', () => {
    // Whether the tiles come out compressed is a property of this exact version, and the rule
    // they are served under is derived from those bytes.
    assert.match(image, /^FROM gaia3d\/mago-3d-terrainer:1\.14\.2-release$/m);
  });

  it('owns the shared directories before it drops privileges, at the same id as the api', () => {
    // Docker seeds a fresh named volume from the image directory beneath it, ownership included,
    // so a path missing from either image comes up root-owned and the first write of the first
    // bake fails. Both images therefore create both directories, and both must run as the same
    // user or each finds the other's files unwritable.
    const owns = /RUN mkdir -p [^\n]*\/data\/terrain\/spool[^\n]*chown/;
    assert.match(image, owns);
    assert.match(image, /ARG APP_UID=1654/);
    assert.ok(
      image.indexOf('RUN mkdir -p') < image.indexOf('USER '),
      'the worker image must own the directories before dropping privileges',
    );
    assert.match(apiImage, /RUN mkdir -p [^\n]*\/data\/terrain\/spool[^\n]*chown -R app:app/);
    assert.ok(
      apiImage.indexOf('RUN mkdir -p /data') < apiImage.indexOf('USER app'),
      'the api image must own the directories before dropping privileges',
    );
  });

  it('keeps the heap flag the base image carried in the entrypoint it replaces', () => {
    // The pre-baker ships no -Xmx and takes its heap as a percentage of the container's memory
    // limit, so losing this flag halves the heap — and short of heap the tool does not fail, it
    // stops refining and writes coarser tiles than were asked for.
    assert.match(supervisor, /-XX:MaxRAMPercentage=50\.0/);
    assert.match(overlay, /mem_limit: \$\{SILEXGIS_TERRAIN_WORKER_MEMORY:-8g\}/);
  });
});

describe('what the supervisor puts on the pre-baker command line', () => {
  it('always states the depth, and refuses a request that does not', () => {
    // Left to work it out, the pre-baker derives the depth from the finest raster it was given,
    // which for a patch of fine survey inside a coarse fill is several levels deeper than anyone
    // wanted — and the price of a bake tracks the number of tiles, not the area.
    assert.match(supervisor, /set -- -i "\$input" -o "\$output" -max "\$depth"/);
    assert.match(supervisor, /refuse "\$directory" "maxDepth is not a number"/);
  });

  it('tells it no void value, leaving the one the rasters were prepared with', () => {
    // The rasters arrive with the pre-baker's own default written into their voids, and nothing
    // on this command line tells it a different one. The two halves are one agreement.
    assert.ok(!supervisor.includes('-nv'), 'the pre-baker is told no void value');
    assert.ok(!supervisor.includes('--nodataValue'), 'the pre-baker is told no void value');
  });

  it('converts the heights only when the request asks for it', () => {
    assert.match(supervisor, /ellipsoidal\) set -- "\$@" -g EGM2008 ;;/);
  });
});

describe('the overlay', () => {
  it('carries the worker and nothing else, leaving the volume in the base file', () => {
    // A pyramid built once has to keep being served after the builder is switched off, and the
    // command-line path has to keep working with no overlay at all — so the mounts are
    // unconditional and live in the base file.
    assert.match(base, /- silexgis-terrain:\/data\/terrain$/m);
    assert.match(base, /- silexgis-terrain:\/srv\/terrain:ro$/m);
    assert.ok(!/^volumes:/m.test(overlay), 'the overlay declares no volume of its own');
    assert.ok(!/\bweb:/.test(overlay), 'the overlay does not touch the web service');
  });

  it('publishes no port', () => {
    assert.ok(!/ports:/.test(overlay));
  });

  it('stops `up` with a sentence when the application has not been told it exists', () => {
    // A worker running beside an application that never hands it anything looks exactly like no
    // worker at all: every build waits, and nothing anywhere says why.
    assert.match(
      overlay,
      /\$\{SILEXGIS__Terrain__BakeEnabled:\?set SILEXGIS__Terrain__BakeEnabled=true[^}]*\}/,
    );
  });
});
