// SPDX-License-Identifier: AGPL-3.0-or-later

// The test projects may not scratch under the system temporary directory.
//
// On the machine this suite is developed on, that directory is a 16 GB tmpfs — memory. Eighty
// classes writing uploads, rasters and key rings into it took that memory from every other
// process on the box, and when it filled, the shell's own output capture broke and a running
// suite failed wholesale at login while the root disk reported tens of gigabytes free. The
// scratch root for tests is `TestScratch.Root`, under the build output, which a `dotnet clean`
// reclaims. This check keeps the system directory out of the test sources, because a rule that
// lives only in a comment is re-broken by the next class that copies an older one.
//
// Run from the repository root with `node --test "scripts/**/*.test.mjs"`.

import { strict as assert } from 'node:assert';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, it } from 'node:test';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const testsRoot = join(repoRoot, 'server', 'tests');

/** Every C# source under the test projects, build output excluded. */
function* sources(dir) {
  for (const entry of readdirSync(dir)) {
    if (entry === 'bin' || entry === 'obj') continue;
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) yield* sources(full);
    else if (entry.endsWith('.cs')) yield full;
  }
}

describe('test scratch stays off the system temporary directory', () => {
  it('no test source calls Path.GetTempPath() outside a comment', () => {
    const offenders = [];
    for (const file of sources(testsRoot)) {
      readFileSync(file, 'utf8').split('\n').forEach((line, i) => {
        // A line comment may name the method to explain why it is avoided; code may not call it.
        const code = line.replace(/\/\/.*$/, '');
        if (code.includes('GetTempPath()')) offenders.push(`${relative(repoRoot, file)}:${i + 1}`);
      });
    }
    assert.deepEqual(
      offenders,
      [],
      `use TestScratch.Root (under the build output) instead of the system temp dir:\n  ${offenders.join('\n  ')}`,
    );
  });

  it('TestScratch.Root is under the build output', () => {
    const helper = readFileSync(
      join(testsRoot, 'SilexGis.Api.Tests', 'Support', 'TestScratch.cs'),
      'utf8',
    );
    assert.ok(helper.includes('AppContext.BaseDirectory'), 'TestScratch.Root must derive from the assembly output');
  });
});
