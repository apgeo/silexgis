// SPDX-License-Identifier: AGPL-3.0-or-later
//
// The `silexgis-update` command install-ops.sh writes must start the updater with the same
// backup rotation the nightly unit keeps. The updater prunes the same folder, and with its own,
// smaller default it cut the nightly rotation down to its size on every update.
//
// install-ops.sh itself needs root and writes into /usr/local/sbin and /etc/systemd, so the test
// renders only the wrapper: it takes the wrapper's here-document out of the script, expands it with
// the script's own variables pointed at a scratch folder, and runs the result with a stand-in
// update.sh that reports the environment it was started with.

import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { chmodSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir, userInfo } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));

const SKIP_ON_WINDOWS =
  process.platform === 'win32' && 'install-ops.sh is a bash script and needs a POSIX shell, which a Windows runner does not provide';

/** The wrapper's here-document, exactly as install-ops.sh writes it, with its target redirected. */
function wrapperBlock(target) {
  const lines = readFileSync(join(here, 'install-ops.sh'), 'utf8').split('\n');
  const start = lines.findIndex((l) => l === 'cat > /usr/local/sbin/silexgis-update <<EOF');
  assert.ok(start >= 0, 'install-ops.sh writes the silexgis-update wrapper with a here-document');
  const end = lines.findIndex((l, i) => i > start && l === 'EOF');
  return [`cat > "${target}" <<EOF`, ...lines.slice(start + 1, end + 1)].join('\n');
}

describe('install-ops.sh silexgis-update wrapper', { skip: SKIP_ON_WINDOWS }, () => {
  it('passes the rotation size it was installed with on to the updater', () => {
    const root = mkdtempSync(join(tmpdir(), 'silexgis-install-ops-'));
    try {
      const wrapper = join(root, 'silexgis-update');
      writeFileSync(join(root, 'update.sh'), 'echo "keep=${SILEXGIS_BACKUP_KEEP:-unset} dir=$SILEXGIS_BACKUP_DIR"\n');
      const render = [
        `DEPLOY_USER='${userInfo().username}'`,
        `APP_DIR='${join(root, 'app')}'`,
        `OPS_DIR='${root}'`,
        `BACKUP_DIR='${join(root, 'backups')}'`,
        'BACKUP_KEEP=30',
        wrapperBlock(wrapper),
      ].join('\n');
      const rendered = spawnSync('bash', ['-c', render], { encoding: 'utf8' });
      assert.equal(rendered.status, 0, rendered.stderr);
      chmodSync(wrapper, 0o755);

      const run = spawnSync('bash', [wrapper], { encoding: 'utf8', env: { ...process.env, SILEXGIS_BACKUP_KEEP: '' } });
      assert.equal(run.status, 0, run.stderr);
      assert.equal(run.stdout.trim(), `keep=30 dir=${join(root, 'backups')}`);
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });
});
