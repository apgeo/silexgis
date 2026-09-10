// SPDX-License-Identifier: AGPL-3.0-or-later

// The browser suite cannot quietly shrink: every spec file on disk must be claimed by at least
// one Playwright project, and no spec may be claimed by two.
//
// Why this exists. Each project used to pin its spec files by name in a regex alternation, and a
// spec that nobody remembered to add ran nowhere — it passed the gate by never executing.
// `photo-library-smoke.spec.ts` was in exactly that state: written, committed, and claimed by no
// project. The desktop project now takes everything and the phone projects subtract their three,
// so a new spec is covered by writing it; this test is what keeps that true, and what catches a
// spec accidentally added to the ignore list or a phone spec renamed out of its project.
//
// It reads the config as text rather than importing it, because importing pulls in Playwright and
// a whole toolchain to answer a question about file names. The parse is deliberately narrow: it
// finds the regex literals belonging to `testMatch` and `testIgnore`, and fails loudly if the
// config's shape stops matching what it expects, rather than silently checking nothing.
//
// Run from the repository root with `node --test "scripts/**/*.test.mjs"`.

import { strict as assert } from 'node:assert';
import { readFileSync, readdirSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, it } from 'node:test';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const e2eDir = join(repoRoot, 'client', 'e2e');
const configPath = join(repoRoot, 'client', 'playwright.config.ts');
const config = readFileSync(configPath, 'utf8');

/** Every `*.spec.ts` in the suite directory, as bare file names. */
function specsOnDisk() {
  return readdirSync(e2eDir)
    .filter((f) => f.endsWith('.spec.ts'))
    .sort();
}

/** The regex literals held in the shared phone-only list. */
function phoneOnlyPatterns() {
  const block = /const PHONE_ONLY_SPECS = \[([\s\S]*?)\];/.exec(config);
  assert.ok(block, 'playwright.config.ts no longer declares PHONE_ONLY_SPECS as an array literal');
  return [...block[1].matchAll(/\/((?:[^/\\\n]|\\.)+)\/[gimsuy]*/g)].map((m) => new RegExp(m[1]));
}

/**
 * One entry per project: its name, the patterns it selects, and the patterns it subtracts.
 *
 * `testIgnore: PHONE_ONLY_SPECS` is resolved to the shared list; a project that ignores something
 * else is not understood and fails the parse rather than being read as ignoring nothing.
 */
function projects() {
  const found = [];
  const projectBlock = /\{\s*(?:\/\/[^\n]*\n\s*|\/\*[\s\S]*?\*\/\s*)*name: '([^']+)',([\s\S]*?)\n    \}/g;
  for (const [, name, body] of config.matchAll(projectBlock)) {
    const match = /testMatch:\s*(?:\n\s*)?\/((?:[^/\\\n]|\\.)+)\/[gimsuy]*/.exec(body);
    if (!match) continue;

    let ignore = [];
    if (/testIgnore:/.test(body)) {
      assert.match(
        body,
        /testIgnore:\s*PHONE_ONLY_SPECS/,
        `project "${name}" ignores something this test does not understand; teach it or the check is blind`,
      );
      ignore = phoneOnlyPatterns();
    }

    found.push({ name, match: new RegExp(match[1]), ignore });
  }
  return found;
}

/** Which projects would run a given spec file. */
function claimants(spec, all) {
  return all
    .filter((p) => p.match.test(spec) && !p.ignore.some((i) => i.test(spec)))
    .map((p) => p.name);
}

describe('the browser suite cannot shrink unnoticed', () => {
  it('the config is still shaped the way this check reads it', () => {
    const all = projects();
    assert.ok(all.length >= 2, 'no Playwright projects were parsed out of the config');
    assert.ok(
      all.some((p) => p.ignore.length > 0),
      'no project subtracts the phone-only specs; the desktop project is meant to',
    );
    assert.ok(specsOnDisk().length > 0, 'no spec files were found to check');
  });

  it('every spec on disk is claimed by at least one project', () => {
    const all = projects();
    const orphans = specsOnDisk().filter((s) => claimants(s, all).length === 0);
    assert.deepEqual(
      orphans,
      [],
      `these specs would run in no project, so they pass by never executing: ${orphans.join(', ')}`,
    );
  });

  // Deliberately not "no spec runs twice". One spec is meant to: the landscape project exists to
  // run scene3d-mobile sideways as well as upright, and asserting uniqueness would forbid the
  // thing that project is for. What must never happen is a phone spec also running on desktop —
  // a viewport it was not written for, and the way an over-broad desktop rule would show up.
  it('no spec runs on desktop as well as on a phone', () => {
    const all = projects();
    const phone = all.filter((p) => p.name !== 'desktop').map((p) => p.name);
    const both = specsOnDisk()
      .map((s) => [s, claimants(s, all)])
      .filter(([, owners]) => owners.includes('desktop') && owners.some((o) => phone.includes(o)));
    assert.deepEqual(
      both.map(([s, owners]) => `${s} -> ${owners.join(' + ')}`),
      [],
      'a phone spec is also running on desktop, in a viewport it was not written for',
    );
  });

  it('every phone-only pattern names a spec that exists', () => {
    const disk = specsOnDisk();
    const dangling = phoneOnlyPatterns()
      .filter((p) => !disk.some((s) => p.test(s)))
      .map((p) => p.source);
    assert.deepEqual(
      dangling,
      [],
      'a phone-only pattern matches no file, so a rename has left the desktop project subtracting nothing',
    );
  });
});
