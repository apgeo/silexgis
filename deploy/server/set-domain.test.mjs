// SPDX-License-Identifier: AGPL-3.0-or-later
//
// set-domain.sh is the way to move an installation set up on plain HTTP onto a name of its own.
// It used to write the domain and the https public URL but not the TLS overlay into COMPOSE_FILE,
// so the proxy that serves the name was never started: the installation stayed on plain HTTP,
// where nobody can sign in, and the restart failed on a caddy service that did not exist.
//
// The test runs the real script against a scratch .env, with stand-ins for `docker` (which records
// the restart) and `curl` (which answers the health check and fails the public-address lookup), and
// with the DNS check skipped, so nothing leaves the machine.

import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { chmodSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));

const SKIP_ON_WINDOWS =
  process.platform === 'win32' && 'set-domain.sh is a bash script and needs a POSIX shell, which a Windows runner does not provide';

const DOCKER = `#!/usr/bin/env bash
echo "docker $*" >> "$STANDIN_LOG"
exit 0
`;
const CURL = `#!/usr/bin/env bash
case "$*" in
*/health/ready*) exit 0 ;;
*) exit 6 ;;
esac
`;

function sandbox(env) {
  const root = mkdtempSync(join(tmpdir(), 'silexgis-set-domain-'));
  const bin = join(root, 'bin');
  mkdirSync(bin);
  mkdirSync(join(root, 'app', 'deploy'), { recursive: true });
  writeFileSync(join(bin, 'docker'), DOCKER);
  writeFileSync(join(bin, 'curl'), CURL);
  chmodSync(join(bin, 'docker'), 0o755);
  chmodSync(join(bin, 'curl'), 0o755);
  writeFileSync(join(root, 'app', 'deploy', '.env'), env);
  return { root, bin, env: join(root, 'app', 'deploy', '.env'), log: join(root, 'docker.log') };
}

function setDomain(box, ...args) {
  return spawnSync('bash', [join(here, 'set-domain.sh'), '--primary', 'caves.example.invalid', '--skip-dns-check', ...args], {
    encoding: 'utf8',
    env: { ...process.env, PATH: `${box.bin}:${process.env.PATH}`, SILEXGIS_APP_DIR: join(box.root, 'app'), STANDIN_LOG: box.log },
  });
}

const envValue = (box, key) =>
  readFileSync(box.env, 'utf8')
    .split('\n')
    .filter((l) => l.startsWith(`${key}=`))
    .map((l) => l.slice(key.length + 1));

describe('set-domain.sh', { skip: SKIP_ON_WINDOWS }, () => {
  it('moves a plain-HTTP installation onto the TLS proxy, once', () => {
    const box = sandbox('COMPOSE_FILE=docker-compose.yml:docker-compose.terrain-worker.yml\nSILEXGIS_PUBLIC_URL=http://203.0.113.10\n');
    try {
      const first = setDomain(box);
      assert.equal(first.status, 0, first.stderr);
      assert.deepEqual(envValue(box, 'COMPOSE_FILE'), ['docker-compose.yml:docker-compose.terrain-worker.yml:docker-compose.tls.yml']);
      assert.deepEqual(envValue(box, 'SILEXGIS_PUBLIC_URL'), ['https://caves.example.invalid']);
      assert.deepEqual(envValue(box, 'SILEXGIS_DOMAIN'), ['caves.example.invalid']);
      assert.deepEqual(envValue(box, 'SILEXGIS_TLS_EMAIL'), ['admin@caves.example.invalid']);
      assert.match(readFileSync(box.log, 'utf8'), /^docker compose up -d --remove-orphans$/m, 'the whole stack is recreated');

      const again = setDomain(box, '--email', 'ops@example.invalid', '--no-restart');
      assert.equal(again.status, 0, again.stderr);
      assert.deepEqual(envValue(box, 'COMPOSE_FILE'), ['docker-compose.yml:docker-compose.terrain-worker.yml:docker-compose.tls.yml'], 'the overlay is not added twice');
      assert.deepEqual(envValue(box, 'SILEXGIS_TLS_EMAIL'), ['ops@example.invalid']);
    } finally {
      rmSync(box.root, { recursive: true, force: true });
    }
  });

  it('adds the overlay to the default file list when the .env names none', () => {
    const box = sandbox('SILEXGIS_PUBLIC_URL=http://203.0.113.10\n');
    try {
      const r = setDomain(box, '--no-restart');
      assert.equal(r.status, 0, r.stderr);
      assert.deepEqual(envValue(box, 'COMPOSE_FILE'), ['docker-compose.yml:docker-compose.tls.yml']);
    } finally {
      rmSync(box.root, { recursive: true, force: true });
    }
  });
});
