// SPDX-License-Identifier: AGPL-3.0-or-later

// The shards must add up to the whole suite: every class in exactly one shard, the same deal for
// the same tree, and no test hiding in a file the class convention would never deal out.

import { strict as assert } from 'node:assert';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { describe, it } from 'node:test';

import { listClasses, shard, testDir } from './gate-shard.mjs';

describe('dealing the suite into shards', () => {
  it('every class lands in exactly one shard, whatever the count', () => {
    const all = listClasses();
    assert.ok(all.length > 100, `expected the real suite, saw ${all.length} classes`);
    for (const count of [1, 3, 8, 12, 50]) {
      const dealt = [];
      for (let i = 0; i < count; i++) dealt.push(...shard(all, i, count));
      assert.deepEqual(dealt.sort(), [...all].sort(), `count ${count} loses or doubles a class`);
    }
  });

  it('is deterministic and balanced to within one class', () => {
    const all = listClasses();
    const sizes = Array.from({ length: 8 }, (_, i) => shard(all, i, 8).length);
    assert.ok(Math.max(...sizes) - Math.min(...sizes) <= 1, `uneven: ${sizes}`);
    assert.deepEqual(shard(all, 3, 8), shard(all, 3, 8));
  });

  it('refuses an index outside the count', () => {
    assert.throws(() => shard(['A'], 1, 1));
    assert.throws(() => shard(['A'], -1, 2));
    assert.throws(() => shard(['A'], 0, 0));
  });

  // The convention is "a top-level *Tests.cs file is a class". A [Fact] or [Theory] anywhere else
  // — a subfolder, a file named differently — would be in no shard and run in no CI job, and
  // nothing would say so. Nested test classes inside a *Tests.cs file are fine: the filter pins
  // the outer class name, which selects everything within it.
  it('no test attribute lives in a file the convention does not deal', () => {
    const offenders = [];
    const walk = (dir) => {
      for (const entry of readdirSync(dir)) {
        if (entry === 'bin' || entry === 'obj') continue;
        const full = join(dir, entry);
        if (statSync(full).isDirectory()) walk(full);
        else if (entry.endsWith('.cs')) {
          const text = readFileSync(full, 'utf8');
          if (!/\[(Fact|Theory)\b/.test(text)) continue;
          const topLevel = dir === testDir && /^[A-Za-z0-9]+Tests\.cs$/.test(entry);
          if (!topLevel) offenders.push(relative(testDir, full));
        }
      }
    };
    walk(testDir);
    assert.deepEqual(offenders, [], `tests outside the class convention would run in no shard: ${offenders.join(', ')}`);
  });
});
