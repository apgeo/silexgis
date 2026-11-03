// SPDX-License-Identifier: AGPL-3.0-or-later
//
// An update that did not happen must never be reported as one.
//
// update.sh fast-forwards the checkout, builds the images, starts the containers (the API applies
// its database migrations as it starts) and waits for the public health address. Each of those can
// fail, and each failure has to stop the update, put the previous version back, check that it really
// is back, and say which of those things is true. The failure this suite was written for: the build
// and the start were two commands inside one function called as an `if` condition, where bash ignores
// `set -e`, so a failed build fell through to `up -d`, which happily restarted the OLD images; the old
// API answered healthy and the script printed "Updated old -> new" with exit 0.
//
// These tests run the real script with stand-ins first on PATH: a `docker` that keeps a small state
// (which commit's images were last built, which are running) and fails on request at a given commit,
// and a `curl` that answers the health address healthy or not depending on which commit is running.
// So what is checked is what the script does, not what its text says.

import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { chmodSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));

// update.sh is a bash script for the Linux server and the stand-ins are extensionless shell
// scripts found through a `:`-separated PATH; a Windows runner has neither, so this suite is
// skipped there and only there.
const SKIP_ON_WINDOWS =
  process.platform === 'win32' && 'update.sh is a bash script and needs a POSIX shell, which a Windows runner does not provide';

/**
 * The stand-in `docker`. State lives in $STANDIN_STATE: `built` is the commit whose images the
 * tags point at, `running` the commit whose containers run. Failures are chosen through the
 * environment by naming the commit at which a step fails:
 *   STANDIN_DB            running | stopped   (whether `compose ps` lists db)
 *   STANDIN_BUILD_FAIL    space-separated commits at which `compose build` fails
 *   STANDIN_UP_FAIL       commits at which `compose up -d` fails after starting the new API
 *                         (a migration that throws leaves the API unhealthy and its dependants
 *                         refuse to start, which is how compose reports it)
 * Every call is appended to $STANDIN_STATE/calls so a test can see what the script asked for.
 */
const DOCKER = `#!/usr/bin/env bash
state="$STANDIN_STATE"
echo "$*" >> "$state/calls"
head="$(git -C "$SILEXGIS_APP_DIR" rev-parse HEAD)"
fails_at() { case " $1 " in *" $head "*) return 0 ;; esac; return 1; }
case "$1 $2" in
"compose ps")
	if [ "$STANDIN_DB" = stopped ]; then printf 'api\\nweb\\n'; else printf 'db\\napi\\nweb\\n'; fi
	exit 0 ;;
"compose build")
	if fails_at "$STANDIN_BUILD_FAIL"; then echo "stand-in docker: build failed at $head" >&2; exit 17; fi
	echo "$head" > "$state/built"; exit 0 ;;
"compose up")
	# The containers are recreated from whatever the tags point at -- which, after a failed
	# build, is still the previous version.
	if [ "$3" = "-d" ] && [ "$4" = "--wait" ] && [ "$5" = db ]; then echo db-started >> "$state/events"; exit 0; fi
	cp "$state/built" "$state/running"
	if fails_at "$STANDIN_UP_FAIL"; then echo "stand-in docker: dependency failed to start: container api is unhealthy" >&2; exit 1; fi
	exit 0 ;;
"compose logs") echo "stand-in api log line"; exit 0 ;;
"image prune"|"builder prune") exit 0 ;;
esac
echo "stand-in docker: unexpected call: $*" >&2
exit 99
`;

/** The stand-in `curl`: healthy unless the running commit is listed in STANDIN_UNHEALTHY. */
const CURL = `#!/usr/bin/env bash
running="$(cat "$STANDIN_STATE/running")"
case " $STANDIN_UNHEALTHY " in *" $running "*) exit 22 ;; esac
echo Healthy
`;

/** Stands in for the real backup script, which has its own suite: it records that it ran. */
const BACKUP = `#!/bin/sh
set -eu
mkdir -p "$1"
echo dump > "$1/db.sql.gz"
`;

function git(cwd, ...args) {
  const r = spawnSync('git', ['-c', 'user.name=t', '-c', 'user.email=t@t', ...args], { cwd, encoding: 'utf8' });
  assert.equal(r.status, 0, r.stderr);
  return r.stdout.trim();
}

/** A checkout one commit behind its origin, a deploy folder, the stand-ins, and their state. */
function sandbox() {
  const root = mkdtempSync(join(tmpdir(), 'silexgis-update-'));
  const origin = join(root, 'origin');
  const app = join(root, 'app');
  const bin = join(root, 'bin');
  const state = join(root, 'state');
  for (const d of [origin, bin, state]) mkdirSync(d);
  git(origin, 'init', '-q', '-b', 'master');
  writeFileSync(join(origin, 'a'), '1');
  git(origin, 'add', 'a');
  git(origin, 'commit', '-q', '-m', 'one');
  git(root, 'clone', '-q', origin, app);
  writeFileSync(join(origin, 'a'), '2');
  git(origin, 'commit', '-q', '-am', 'two');
  const old = git(app, 'rev-parse', 'HEAD');
  const next = git(origin, 'rev-parse', 'HEAD');
  mkdirSync(join(app, 'deploy', 'scripts'), { recursive: true });
  writeFileSync(join(app, 'deploy', '.env'), 'SILEXGIS_PUBLIC_URL=http://127.0.0.1:9\n');
  writeFileSync(join(app, 'deploy', 'scripts', 'backup.sh'), BACKUP);
  writeFileSync(join(bin, 'docker'), DOCKER);
  writeFileSync(join(bin, 'curl'), CURL);
  chmodSync(join(bin, 'docker'), 0o755);
  chmodSync(join(bin, 'curl'), 0o755);
  // Before the update the old version is built and running.
  writeFileSync(join(state, 'built'), `${old}\n`);
  writeFileSync(join(state, 'running'), `${old}\n`);
  return { root, app, bin, state, old, next };
}

function run(box, env = {}) {
  const result = spawnSync('bash', [join(here, 'update.sh')], {
    encoding: 'utf8',
    env: {
      ...process.env,
      PATH: `${box.bin}:${process.env.PATH}`,
      SILEXGIS_APP_DIR: box.app,
      SILEXGIS_BACKUP_DIR: join(box.root, 'backups'),
      SILEXGIS_UPDATE_LOCK: join(box.root, 'update.lock'),
      // One health probe per wait: the stand-in answers at once, so there is nothing to wait for.
      SILEXGIS_HEALTH_TIMEOUT: '0',
      STANDIN_STATE: box.state,
      STANDIN_DB: 'running',
      STANDIN_BUILD_FAIL: '',
      STANDIN_UP_FAIL: '',
      STANDIN_UNHEALTHY: '',
      ...env,
    },
  });
  const read = (f) => (existsSync(join(box.state, f)) ? readFileSync(join(box.state, f), 'utf8') : '');
  return {
    ...result,
    out: result.stdout + result.stderr,
    head: git(box.app, 'rev-parse', 'HEAD'),
    running: read('running').trim(),
    calls: read('calls'),
    events: read('events'),
  };
}

function withBox(fn) {
  const box = sandbox();
  try {
    fn(box);
  } finally {
    rmSync(box.root, { recursive: true, force: true });
  }
}

describe('update.sh', { skip: SKIP_ON_WINDOWS }, () => {
  it('updates, and says so, when the build, the start and the health check all succeed', () => {
    withBox((box) => {
      const r = run(box);
      assert.equal(r.status, 0, r.out);
      assert.equal(r.head, box.next);
      assert.equal(r.running, box.next);
      assert.match(r.stdout, /Updated [0-9a-f]+ -> [0-9a-f]+/);
      assert.ok(existsSync(join(box.root, 'backups')), 'a backup was taken first');
    });
  });

  it('stops, rolls back and says the build failed when the new images do not build', () => {
    withBox((box) => {
      const r = run(box, { STANDIN_BUILD_FAIL: box.next });
      assert.notEqual(r.status, 0, r.out);
      assert.doesNotMatch(r.out, /Updated /);
      assert.doesNotMatch(r.out, /Healthy at/);
      assert.match(r.stderr, /build failed at/, 'the build’s own error stays visible');
      assert.match(r.stderr, /FAILED: the new version did not build/);
      assert.match(r.stderr, /previous version \([0-9a-f]+\) is running again/);
      assert.equal(r.head, box.old, 'the checkout is back on the previous commit');
      assert.equal(r.running, box.old);
      // The rollback rebuilds from the local cache: an unreachable registry may be why it failed.
      assert.match(r.calls, /^compose build$/m);
    });
  });

  it('says a migration may have run, and where the backup is, when the new version does not start', () => {
    withBox((box) => {
      const r = run(box, { STANDIN_UP_FAIL: box.next, STANDIN_UNHEALTHY: box.next });
      assert.notEqual(r.status, 0, r.out);
      assert.doesNotMatch(r.out, /Updated /);
      assert.match(r.stderr, /FAILED: the new version did not start/);
      assert.match(r.stderr, /previous version \([0-9a-f]+\) is running again/);
      assert.match(r.stderr, /database migration/);
      assert.match(r.stderr, new RegExp(`restore the backup at ${join(box.root, 'backups')}/\\d{8}T\\d{6}Z`));
      assert.equal(r.head, box.old);
      assert.equal(r.running, box.old);
    });
  });

  it('rolls back and says so when the new version starts but never answers healthy', () => {
    withBox((box) => {
      const r = run(box, { STANDIN_UNHEALTHY: box.next });
      assert.notEqual(r.status, 0, r.out);
      assert.doesNotMatch(r.out, /Updated /);
      assert.match(r.stderr, /FAILED: the new version did not become healthy/);
      assert.match(r.stderr, /previous version \([0-9a-f]+\) is running again/);
      assert.match(r.out, /stand-in api log line/, 'the API’s log is shown');
      assert.equal(r.head, box.old);
      assert.equal(r.running, box.old);
    });
  });

  it('never claims the previous version is back when rebuilding it fails too', () => {
    withBox((box) => {
      const r = run(box, { STANDIN_BUILD_FAIL: `${box.old} ${box.next}` });
      assert.notEqual(r.status, 0, r.out);
      assert.doesNotMatch(r.out, /Updated /);
      assert.doesNotMatch(r.out, /is running again|previous version restored/);
      assert.match(r.stderr, /the previous version could not be put back/);
    });
  });

  it('never claims the previous version is back when it does not answer healthy', () => {
    withBox((box) => {
      const r = run(box, { STANDIN_UNHEALTHY: `${box.old} ${box.next}` });
      assert.notEqual(r.status, 0, r.out);
      assert.doesNotMatch(r.out, /Updated /);
      assert.doesNotMatch(r.out, /is running again|previous version restored/);
      assert.match(r.stderr, /the previous version did not come back up/);
      assert.match(r.stderr, /restore\.sh/);
    });
  });

  it('starts a stopped database and backs it up before updating, rather than skipping the backup', () => {
    withBox((box) => {
      const r = run(box, { STANDIN_DB: 'stopped' });
      assert.equal(r.status, 0, r.out);
      assert.doesNotMatch(r.out, /nothing to back up/);
      assert.match(r.events, /db-started/);
      const backups = join(box.root, 'backups');
      const taken = spawnSync('find', [backups, '-name', 'db.sql.gz'], { encoding: 'utf8' }).stdout.trim();
      assert.ok(taken, 'a backup was taken');
    });
  });
});
