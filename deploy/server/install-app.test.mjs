// SPDX-License-Identifier: AGPL-3.0-or-later
//
// install-app.sh never overwrites an existing .env, because it holds generated passwords. A re-run
// with SILEXGIS_DOMAIN therefore cannot apply the domain, and it used to drop it without a word: the
// operator believed the installation had moved onto HTTPS while it stayed on plain HTTP, where
// nobody can sign in. It now says so, and names the command that does apply it.
//
// The real script runs against a scratch checkout, with stand-ins for `git` and `docker`, so
// nothing is cloned and no container engine is needed.

import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { chmodSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));

const SKIP =
  (process.platform === 'win32' && 'install-app.sh is a bash script for an Ubuntu host') ||
  (process.getuid?.() === 0 && 'install-app.sh refuses to run as root, by design');

const GIT = `#!/usr/bin/env bash
echo "git $*" >> "$STANDIN_LOG"
case "$*" in *rev-parse*) echo abc1234 ;; esac
exit 0
`;
const DOCKER = `#!/usr/bin/env bash
exit 0
`;

function rerun(envText, env) {
  const root = mkdtempSync(join(tmpdir(), 'silexgis-install-app-'));
  try {
    const bin = join(root, 'bin');
    const app = join(root, 'app');
    mkdirSync(bin);
    mkdirSync(join(app, '.git'), { recursive: true });
    mkdirSync(join(app, 'deploy'));
    writeFileSync(join(bin, 'git'), GIT);
    writeFileSync(join(bin, 'docker'), DOCKER);
    chmodSync(join(bin, 'git'), 0o755);
    chmodSync(join(bin, 'docker'), 0o755);
    const envFile = join(app, 'deploy', '.env');
    writeFileSync(envFile, envText);
    const r = spawnSync('bash', [join(here, 'install-app.sh')], {
      encoding: 'utf8',
      env: { ...process.env, PATH: `${bin}:${process.env.PATH}`, SILEXGIS_APP_DIR: app, STANDIN_LOG: join(root, 'git.log'), ...env },
    });
    return { ...r, envAfter: readFileSync(envFile, 'utf8') };
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

const PLAIN = 'COMPOSE_FILE=docker-compose.yml:docker-compose.terrain-worker.yml\nSILEXGIS_PUBLIC_URL=http://203.0.113.10\n';

describe('install-app.sh re-run over an existing .env', { skip: SKIP }, () => {
  it('says that a domain given now was not applied, and how to apply it', () => {
    const r = rerun(PLAIN, { SILEXGIS_DOMAIN: 'caves.example.invalid' });
    assert.equal(r.status, 0, r.stderr);
    assert.match(r.stderr, /SILEXGIS_DOMAIN=caves\.example\.invalid was NOT applied/);
    assert.match(r.stderr, /set-domain\.sh --primary caves\.example\.invalid --email admin@caves\.example\.invalid/);
    assert.equal(r.envAfter, PLAIN, 'the .env is left as it was');
  });

  it('says nothing when the .env already serves a TLS proxy', () => {
    const tls = 'COMPOSE_FILE=docker-compose.yml:docker-compose.tls.yml\nSILEXGIS_DOMAIN=caves.example.invalid\n';
    const r = rerun(tls, { SILEXGIS_DOMAIN: 'caves.example.invalid' });
    assert.equal(r.status, 0, r.stderr);
    assert.doesNotMatch(r.stderr, /NOT applied/);
  });

  it('says nothing when no domain was given', () => {
    const r = rerun(PLAIN, { SILEXGIS_HOST: '203.0.113.10' });
    assert.equal(r.status, 0, r.stderr);
    assert.doesNotMatch(r.stderr, /NOT applied/);
  });
});
