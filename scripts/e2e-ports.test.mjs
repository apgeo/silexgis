// SPDX-License-Identifier: AGPL-3.0-or-later
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import net from 'node:net';
import { delimiter, dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

import { REFUSED_EXIT_CODE, isListening, portRefusal } from './e2e-ports.mjs';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');

/** A listener on a port the system chose; `close` frees it. */
function listen(host = '127.0.0.1') {
  return new Promise((resolve, reject) => {
    const server = net.createServer((socket) => socket.destroy());
    server.once('error', reject);
    server.listen(0, host, () => resolve({
      port: server.address().port,
      close: () => new Promise((done) => server.close(done)),
    }));
  });
}

/** A port nothing listens on: bound, read, and released again. */
async function freePort() {
  const holder = await listen();
  await holder.close();
  return holder.port;
}

test('a port with a listener is listening, whatever the listener speaks, and a port without one is not', async () => {
  const held = await listen();
  try {
    assert.equal(await isListening(held.port), true);
  } finally {
    await held.close();
  }
  assert.equal(await isListening(await freePort()), false);
});

test('three free distinct ports are not refused', async () => {
  const ports = { db: await freePort(), api: await freePort(), dev: await freePort() };
  assert.equal(await portRefusal(ports), null);
});

test('a listening port is refused with a code, the port, its role and the variable that moves it', async () => {
  const held = await listen();
  try {
    const free = { db: await freePort(), api: await freePort(), dev: await freePort() };
    for (const [key, variable, label] of [
      ['db', 'SILEXGIS_E2E_DB_PORT', 'database'],
      ['api', 'SILEXGIS_E2E_API_PORT', 'API'],
      ['dev', 'SILEXGIS_E2E_DEV_PORT', 'development server'],
    ]) {
      const refusal = await portRefusal({ ...free, [key]: String(held.port) });
      assert.equal(refusal.code, 'E2E_PORT_IN_USE', key);
      assert.ok(refusal.message.startsWith('E2E_PORT_IN_USE:'), key);
      assert.ok(refusal.message.includes(`port ${held.port} (the ${label})`), key);
      assert.ok(refusal.message.includes(variable), key);
      assert.ok(refusal.message.includes('SILEXGIS_E2E_REUSE=1'), key);
    }
  } finally {
    await held.close();
  }
});

test('every port that is taken is named, not only the first', async () => {
  const a = await listen();
  const b = await listen();
  try {
    const refusal = await portRefusal({ db: a.port, api: b.port, dev: await freePort() });
    assert.equal(refusal.message.split('\n').length, 2);
    assert.ok(refusal.message.includes(String(a.port)) && refusal.message.includes(String(b.port)));
  } finally {
    await a.close();
    await b.close();
  }
});

test('a port number that is not one, and two roles on one port, are refused before any probe', async () => {
  const never = () => assert.fail('nothing should be probed');
  const bad = await portRefusal({ db: 'abc', api: '5081', dev: '5174' }, never);
  assert.equal(bad.code, 'E2E_PORT_INVALID');
  assert.ok(bad.message.includes('SILEXGIS_E2E_DB_PORT'));
  assert.equal((await portRefusal({ db: '70000', api: '5081', dev: '5174' }, never)).code, 'E2E_PORT_INVALID');

  const same = await portRefusal({ db: '5000', api: '5000', dev: '5174' }, never);
  assert.equal(same.code, 'E2E_PORTS_NOT_DISTINCT');
});

test('the runner exits with the refusal code, and starts nothing, when a port is taken', async (t) => {
  if (!existsSync(join(repoRoot, 'client', 'node_modules'))) {
    t.skip('client/node_modules is not installed, so the runner stops earlier for another reason');
    return;
  }
  const held = await listen();
  try {
    const result = spawnSync(process.execPath, [join(repoRoot, 'scripts', 'e2e.mjs')], {
      encoding: 'utf8',
      env: {
        ...process.env,
        SILEXGIS_E2E_REUSE: '',
        SILEXGIS_E2E_DB_PORT: String(await freePort()),
        SILEXGIS_E2E_API_PORT: String(held.port),
        SILEXGIS_E2E_DEV_PORT: String(await freePort()),
      },
    });
    assert.equal(result.status, REFUSED_EXIT_CODE, result.stderr);
    assert.match(result.stderr, /E2E_PORT_IN_USE: port \d+ \(the API\)/);
    // The refusal comes before the database step.
    assert.doesNotMatch(result.stdout, /=== Database on/);
  } finally {
    await held.close();
  }
});

test('a refusal leaves a database container of the same name untouched', async (t) => {
  if (process.platform === 'win32' || !existsSync(join(repoRoot, 'client', 'node_modules'))) {
    t.skip('needs a POSIX shell for the stand-in docker, and an installed client');
    return;
  }
  const dir = mkdtempSync(join(tmpdir(), 'e2e-docker-stub-'));
  const record = join(dir, 'calls.txt');
  writeFileSync(join(dir, 'docker'), `#!/bin/sh\necho "$@" >> "${record}"\n`, { mode: 0o755 });
  const held = await listen();
  try {
    const result = spawnSync(process.execPath, [join(repoRoot, 'scripts', 'e2e.mjs')], {
      encoding: 'utf8',
      env: {
        ...process.env,
        PATH: `${dir}${delimiter}${process.env.PATH}`,
        SILEXGIS_E2E_REUSE: '',
        SILEXGIS_E2E_DB_PORT: String(await freePort()),
        SILEXGIS_E2E_API_PORT: String(held.port),
        SILEXGIS_E2E_DEV_PORT: String(await freePort()),
      },
    });
    assert.equal(result.status, REFUSED_EXIT_CODE, result.stderr);
    assert.equal(existsSync(record), false, 'docker was called before the refusal');
  } finally {
    await held.close();
    rmSync(dir, { recursive: true, force: true });
  }
});
