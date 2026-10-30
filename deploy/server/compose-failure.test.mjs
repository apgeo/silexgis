// SPDX-License-Identifier: AGPL-3.0-or-later
//
// A `docker compose` that cannot run at all must be reported as that — never read as "the database
// is not running".
//
// Both server scripts ask compose which services are running before they touch the database. They
// used to throw compose's error away and look for `db` in whatever came out, so a compose that
// failed outright (it did, for every root caller, because the wrappers switched user but kept
// HOME=/root) produced an empty answer that read as a stopped database:
//   - reset.sh said "FAILED: the db service is not running" while the database was up and healthy;
//   - update.sh said "database is not running; nothing to back up yet" and went on to update
//     WITHOUT a backup — on an installation holding real data, the one step that must not be
//     skipped.
//
// These tests run the real scripts with a stand-in `docker` first on PATH, so what is checked is
// what the scripts do, not what their text says.

import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { chmodSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));

// The scripts under test are bash scripts for the Linux server, and the stand-in `docker` is an
// extensionless bash script found through a `:`-separated PATH. A Windows runner has no POSIX
// shell it can rely on (the bash on its PATH may be the WSL launcher, and a drive-letter path
// breaks a `:`-separated list), so these two describes are skipped there and only there. The rest
// of the deployment-script suite still runs on Windows.
const SKIP_ON_WINDOWS =
  process.platform === 'win32' && 'the server scripts are bash and need a POSIX shell, which a Windows runner does not provide';

/** An installation directory and a `docker` that answers `compose ps` the given way. */
function sandbox(composePs) {
  const root = mkdtempSync(join(tmpdir(), 'silexgis-ops-'));
  const app = join(root, 'app');
  mkdirSync(join(app, 'deploy'), { recursive: true });
  const bin = join(root, 'bin');
  mkdirSync(bin);
  writeFileSync(
    join(bin, 'docker'),
    `#!/usr/bin/env bash
if [ "$1 $2" = "compose ps" ]; then
${composePs}
fi
echo "stand-in docker: unexpected call: $*" >&2
exit 99
`,
  );
  chmodSync(join(bin, 'docker'), 0o755);
  return { root, app, bin };
}

function run(script, args, box) {
  return spawnSync('bash', [join(here, script), ...args], {
    encoding: 'utf8',
    env: {
      ...process.env,
      PATH: `${box.bin}:${process.env.PATH}`,
      SILEXGIS_APP_DIR: box.app,
      SILEXGIS_BACKUP_DIR: join(box.root, 'backups'),
      SILEXGIS_BASELINE_DIR: join(box.root, 'baseline'),
    },
  });
}

const BROKEN = `echo "unknown flag: --status" >&2; exit 125`;
const NO_DB = `printf 'api\\nweb\\n'; exit 0`;

describe('reset.sh', { skip: SKIP_ON_WINDOWS }, () => {
  it('says compose could not run, with compose’s own error, rather than blaming the database', () => {
    const box = sandbox(BROKEN);
    try {
      const result = run('reset.sh', ['reset'], box);
      assert.notEqual(result.status, 0);
      assert.match(result.stderr, /unknown flag: --status/, 'compose’s own error stays visible');
      assert.match(result.stderr, /docker compose could not list the services/);
      assert.doesNotMatch(result.stderr, /the db service is not running/);
    } finally {
      rmSync(box.root, { recursive: true, force: true });
    }
  });

  it('still says the database is not running when compose answers without it', () => {
    const box = sandbox(NO_DB);
    try {
      const result = run('reset.sh', ['reset'], box);
      assert.notEqual(result.status, 0);
      assert.match(result.stderr, /the db service is not running/);
    } finally {
      rmSync(box.root, { recursive: true, force: true });
    }
  });
});

/** Makes the sandbox's app directory a checkout one commit behind its origin, as before an update. */
function behindOrigin(box) {
  const git = (cwd, ...args) => {
    const r = spawnSync('git', ['-c', 'user.name=t', '-c', 'user.email=t@t', ...args], { cwd, encoding: 'utf8' });
    assert.equal(r.status, 0, r.stderr);
  };
  const origin = join(box.root, 'origin');
  mkdirSync(origin);
  git(origin, 'init', '-q', '-b', 'master');
  writeFileSync(join(origin, 'a'), '1');
  git(origin, 'add', 'a');
  git(origin, 'commit', '-q', '-m', 'one');
  rmSync(box.app, { recursive: true, force: true });
  git(box.root, 'clone', '-q', origin, box.app);
  writeFileSync(join(origin, 'a'), '2');
  git(origin, 'commit', '-q', '-am', 'two');
  mkdirSync(join(box.app, 'deploy'), { recursive: true });
  writeFileSync(join(box.app, 'deploy', '.env'), 'SILEXGIS_PUBLIC_URL=http://127.0.0.1:9\n');
}

describe('update.sh', { skip: SKIP_ON_WINDOWS }, () => {
  it('refuses to update without a backup when compose cannot run', () => {
    const box = sandbox(BROKEN);
    try {
      behindOrigin(box);
      const result = run('update.sh', [], box);
      assert.notEqual(result.status, 0);
      assert.match(result.stderr, /refusing to update without a backup/);
      assert.doesNotMatch(result.stdout, /nothing to back up/);
    } finally {
      rmSync(box.root, { recursive: true, force: true });
    }
  });
});

