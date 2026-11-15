// SPDX-License-Identifier: AGPL-3.0-or-later
//
// An import replaces the database and the uploads, so a bundle that is not what was exported must
// be refused before anything is touched. A stand-in `docker` first on PATH records any call, and
// the tests assert there was none.

import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { chmodSync, existsSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const SKIP = process.platform === 'win32' && 'import-bundle.sh is a POSIX sh script';

function run(args, bundle) {
  const root = mkdtempSync(join(tmpdir(), 'import-bundle-'));
  const bin = join(root, 'bin');
  mkdirSync(bin);
  const calls = join(root, 'docker-calls');
  writeFileSync(join(bin, 'docker'), `#!/bin/sh\necho "$@" >> "${calls}"\n`);
  chmodSync(join(bin, 'docker'), 0o755);
  const dir = join(root, 'bundle');
  mkdirSync(dir);
  if (bundle) {
    writeFileSync(join(dir, 'db.sql.gz'), 'db');
    writeFileSync(join(dir, 'files.tar.gz'), 'files');
    const sum = (s) => createHash('sha256').update(s).digest('hex');
    writeFileSync(join(dir, 'SHA256SUMS'), `${sum('db')}  db.sql.gz\n${sum(bundle.filesContent)}  files.tar.gz\n`);
  }
  const r = spawnSync('sh', [join(here, 'import-bundle.sh'), ...args(dir)], {
    env: { ...process.env, PATH: `${bin}:${process.env.PATH}` },
    encoding: 'utf8',
    cwd: root,
  });
  const result = { ...r, dockerCalled: existsSync(calls) };
  rmSync(root, { recursive: true, force: true });
  return result;
}

describe('import-bundle.sh', { skip: SKIP }, () => {
  it('refuses to run without --yes', () => {
    const r = run((d) => [d], { filesContent: 'files' });
    assert.notEqual(r.status, 0);
    assert.equal(r.dockerCalled, false);
  });

  it('refuses a bundle whose checksum does not match, before touching docker', () => {
    const r = run((d) => [d, '--yes'], { filesContent: 'something else' });
    assert.notEqual(r.status, 0);
    assert.match(r.stderr, /checksum mismatch/);
    assert.equal(r.dockerCalled, false);
  });
});
