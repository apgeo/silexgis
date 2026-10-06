// SPDX-License-Identifier: AGPL-3.0-or-later
import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { after, test } from 'node:test';
import { fileURLToPath } from 'node:url';

import { pruneDependencyCaches } from './dependency-caches.mjs';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const scratch = mkdtempSync(join(tmpdir(), 'dependency-caches-'));
after(() => rmSync(scratch, { recursive: true, force: true }));

/** A cache directory holding one bundled file, saying whose it is unless told not to. */
function cache(viteDir, name, checkout) {
  mkdirSync(join(viteDir, name, 'deps'), { recursive: true });
  writeFileSync(join(viteDir, name, 'deps', 'react.js'), '// bundled');
  if (checkout !== undefined) writeFileSync(join(viteDir, name, 'checkout'), `${checkout}\n`);
}

test('the cache of a checkout that is gone is removed, and of one that is there is kept', () => {
  const viteDir = join(scratch, 'one');
  cache(viteDir, 'aaaaaaaa-5173', '/work/gone/client');
  cache(viteDir, 'aaaaaaaa-5175', '/work/gone/client');
  cache(viteDir, 'bbbbbbbb-5173', '/work/here/client');

  const removed = pruneDependencyCaches(viteDir, (path) => path === '/work/here/client');

  assert.deepEqual(removed.sort(), ['/work/gone/client', '/work/gone/client']);
  assert.equal(existsSync(join(viteDir, 'aaaaaaaa-5173')), false);
  assert.equal(existsSync(join(viteDir, 'aaaaaaaa-5175')), false);
  assert.equal(existsSync(join(viteDir, 'bbbbbbbb-5173', 'deps', 'react.js')), true);
});

test('a cache that does not say whose it is is left alone', () => {
  const viteDir = join(scratch, 'two');
  // What an older build left, the test runner's own directory, and a marker with nothing in it.
  cache(viteDir, 'deps');
  cache(viteDir, 'vitest');
  cache(viteDir, 'cccccccc-5173', '');

  assert.deepEqual(pruneDependencyCaches(viteDir, () => false), []);
  for (const name of ['deps', 'vitest', 'cccccccc-5173']) {
    assert.equal(existsSync(join(viteDir, name)), true, `${name} was removed`);
  }
});

test('a client that has never served anything has nothing to remove', () => {
  assert.deepEqual(pruneDependencyCaches(join(scratch, 'never-made')), []);
});

test('the dev server writes the file this reads, under the name this reads it by', () => {
  // Two files in two languages agreeing on one file name; if they part, every cache "does not
  // say" and nothing is ever removed, which no other test would notice.
  const config = readFileSync(join(repoRoot, 'client', 'vite.config.ts'), 'utf8');
  assert.match(config, /writeFileSync\(path\.join\(checkoutDir, dependencyCacheDir, 'checkout'\), `\$\{checkoutDir\}\\n`\)/);
});
