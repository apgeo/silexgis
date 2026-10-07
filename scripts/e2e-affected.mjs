// SPDX-License-Identifier: AGPL-3.0-or-later

// Names the browser specs that answer for a change. The rule and its reasons are in
// e2e-affected.map.mjs; this is the reading of it, kept pure so it can be tested without git.
//
// Usage, from the repository root (the runner calls it for `--affected <base>`):
//   node scripts/e2e-affected.mjs [--base <ref>]     prints the selection and why
//
// Exit code: 0 with a selection, 10 when the change selects the whole suite, 1 on error.

import { readdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import process from 'node:process';

import * as browser from './e2e-affected.map.mjs';
import * as server from './gate-affected.map.mjs';
import { changedFiles } from './gate-affected.mjs';

/** The longest of `prefixes` that `path` starts with, or null. */
function longest(path, prefixes) {
  let best = null;
  for (const prefix of prefixes) {
    if (path.startsWith(prefix) && (best === null || prefix.length > best.length)) best = prefix;
  }
  return best;
}

/**
 * Changed paths in, selection out: `{ whole, specs, reasons }`. `whole` is true when the change
 * is wider than any list here; `specs` then still holds what was selected before that was found,
 * for the reader, and must not be run as if it were the answer.
 */
export function select(changedPaths, specsOnDisk, maps = { browser, server }) {
  const { always, specs: byArea, clientAreas, everything, outside } = maps.browser;
  const chosen = new Set(always);
  const reasons = [];
  let whole = false;
  const everythingBecause = (path, why) => {
    whole = true;
    reasons.push(`${path}: ${why} -> the whole suite`);
  };
  const take = (path, areas) => {
    for (const area of [areas].flat()) {
      const list = byArea[area];
      if (!list) everythingBecause(path, `no line for the area "${area}"`);
      else if (list.length === 0) everythingBecause(path, `no spec is narrower than everything for "${area}"`);
      else list.forEach((name) => chosen.add(name));
    }
  };

  for (const raw of changedPaths) {
    const path = raw.replaceAll('\\', '/');
    if (outside.some((prefix) => path.startsWith(prefix))) continue;

    if (path.startsWith('client/e2e/')) {
      const name = path.slice('client/e2e/'.length);
      if (/^[^/]+\.spec\.ts$/.test(name)) chosen.add(name);
      else everythingBecause(path, 'shared by every spec');
      continue;
    }

    if (path.startsWith('client/')) {
      // A unit test proves itself in the unit run; no browser spec answers for it.
      if (/\.test\.tsx?$/.test(path)) continue;
      const claimed = longest(path, [...Object.keys(clientAreas), ...everything]);
      if (claimed && clientAreas[claimed]) take(path, clientAreas[claimed]);
      else everythingBecause(path, claimed ? 'every page leans on it' : 'claimed by no area');
      continue;
    }

    if (path.startsWith('server/src/')) {
      if (maps.server.full.some((prefix) => path.startsWith(prefix))) {
        everythingBecause(path, 'whole-API blast radius');
        continue;
      }
      const claimed = longest(path, Object.keys(maps.server.areas));
      const area = claimed ? maps.server.areas[claimed] : null;
      if (typeof area === 'string') take(path, area);
      else everythingBecause(path, claimed ? 'its area names no group' : 'claimed by no area');
      continue;
    }

    everythingBecause(path, 'not a path this map knows');
  }

  return {
    whole,
    specs: [...chosen].filter((name) => specsOnDisk.includes(name)).sort(),
    reasons,
  };
}

/** The selection for the working tree against `base`. */
export function selectionFor(base, e2eDir) {
  const specsOnDisk = readdirSync(e2eDir).filter((name) => name.endsWith('.spec.ts'));
  const changed = changedFiles(base);
  return { base, changed: changed.length, ...select(changed, specsOnDisk) };
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  const at = process.argv.indexOf('--base');
  const base = at === -1 ? 'master' : process.argv[at + 1];
  const here = dirname(fileURLToPath(import.meta.url));
  try {
    const selection = selectionFor(base, join(here, '..', 'client', 'e2e'));
    console.log(`base: ${selection.base}   changed files: ${selection.changed}`);
    for (const reason of selection.reasons) console.log(`  - ${reason}`);
    if (selection.whole) {
      console.log('\nthe change selects the whole browser suite:\n  node scripts/e2e.mjs');
      process.exit(10);
    }
    console.log(`\n${selection.specs.length} specs:`);
    for (const name of selection.specs) console.log(`  ${name}`);
    console.log(`\nrun them with:\n  node scripts/e2e.mjs --affected ${selection.base}`);
  } catch (error) {
    console.error(error.message);
    process.exit(1);
  }
}
