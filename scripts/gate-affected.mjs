// SPDX-License-Identifier: AGPL-3.0-or-later

// Names the integration test classes that must answer for a change, so a slice can get an
// early integration verdict in minutes instead of standing behind the whole suite.
//
// The verdict this produces is an EARLY WARNING TIER, not the proof: the full suite still
// runs for every batch — this tool only decides which classes run first, synchronously,
// before the rest is awaited in the background. Selection fails closed: any server path the
// map does not claim, and any touched area mapped to no owning classes, selects the full
// suite. Changes to the access or location-protection rules add every class that asserts a
// denial or an obfuscated position, whichever feature those classes nominally belong to.
//
// Usage, from the repository root:
//   node scripts/gate-affected.mjs [--base <ref>] [--json | --list | --filter] [--map <file>]
//
//   --base <ref>   diff the working tree (tracked + untracked) against <ref>; default: master
//   --json         machine-readable result (mode, classes, filter, reasons)
//   --list         one class per line
//   --filter       just the `dotnet test --filter` expression (empty when mode is full/none)
//   --rederive     rewrite the map's two cross-cutting lists from the test sources and say what
//                  changed; the map's own test fails until they match, and this is how they are
//                  made to — by a command somebody runs and commits, never behind their back
//
// Exit code: 0 with mode "targeted" or "none"; 10 with mode "full" (so a caller can branch
// on it without parsing anything); 1 on error.

import { execFileSync } from 'node:child_process';
import { readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';
import process from 'node:process';

const TEST_DIR = 'server/tests/SilexGis.Api.Tests/';
// Through fileURLToPath, not the URL's own pathname: on Windows that begins with a slash before
// the drive letter, and the path built from it names nothing.
const HERE = path.dirname(fileURLToPath(import.meta.url));
const DEFAULT_MAP = path.join(HERE, 'gate-affected.map.mjs');
const TEST_NAMESPACE = 'SilexGis.Api.Tests';

/**
 * Pure classification: changed paths + the map in, verdict out. Exported for the tests.
 * Returns { mode: 'full'|'targeted'|'none', classes: string[], reasons: string[] }.
 */
export function classify(changedPaths, map) {
  const classes = new Set();
  const reasons = [];
  let full = false;
  let crossCut = false;

  const areaEntries = Object.entries(map.areas);

  for (const raw of changedPaths) {
    const p = raw.replaceAll('\\', '/');

    if (map.crossCutting.triggers.some((t) => p.startsWith(t))) crossCut = true;

    if ((map.outside ?? []).some((t) => p.startsWith(t))) continue;

    if (map.full.some((t) => p.startsWith(t))) {
      full = true;
      reasons.push(`${p}: whole-API blast radius -> full suite`);
      continue;
    }

    if (p.startsWith(TEST_DIR)) {
      const m = /^([A-Za-z0-9]+Tests)\.cs$/.exec(path.posix.basename(p));
      if (m && !p.slice(TEST_DIR.length).includes('/')) {
        classes.add(m[1]);
      } else {
        // Support code, fixtures, the csproj: everything leans on it.
        full = true;
        reasons.push(`${p}: shared test infrastructure -> full suite`);
      }
      continue;
    }

    // Longest matching area prefix wins.
    let best = null;
    for (const [prefix, owner] of areaEntries) {
      if (p.startsWith(prefix) && (!best || prefix.length > best[0].length)) best = [prefix, owner];
    }
    if (best) {
      const owner = typeof best[1] === 'string' ? map.groups[best[1]] : best[1];
      if (owner === undefined) {
        full = true;
        reasons.push(`${p}: area names unknown group '${best[1]}' -> full suite`);
      } else if (owner.length === 0) {
        full = true;
        reasons.push(`${p}: area '${best[0]}' has no owning classes recorded yet -> full suite`);
      } else {
        for (const c of owner) classes.add(c);
      }
      continue;
    }

    if (p.startsWith('server/')) {
      // Fail closed: a server path nothing claims gets the whole suite.
      full = true;
      reasons.push(`${p}: not claimed by the map -> full suite`);
    }
    // Anything else (repo-root docs and metadata) selects nothing.
  }

  if (crossCut) {
    reasons.push('access/location rules touched -> every denial- and obfuscation-asserting class added');
    for (const c of map.crossCutting.permissionClasses) classes.add(c);
    for (const c of map.crossCutting.locationClasses) classes.add(c);
  }

  const list = [...classes].sort();
  const mode = full ? 'full' : list.length ? 'targeted' : 'none';
  return { mode, classes: mode === 'full' ? [] : list, reasons };
}

/**
 * The two cross-cutting lists as the test sources yield them today: every class that asserts a
 * denial, and every class that asserts an obfuscated or protected position, by the patterns the
 * map records. One copy of the rule — the map's own test compares the lists against this, and
 * `--rederive` writes this into the map.
 */
export function deriveCrossCutting(map, testDirectory) {
  const permission = new RegExp(
    map.crossCutting.derivation.permission.source,
    map.crossCutting.derivation.permission.flags,
  );
  const location = new RegExp(
    map.crossCutting.derivation.location.source,
    map.crossCutting.derivation.location.flags,
  );
  const permissionClasses = [];
  const locationClasses = [];
  for (const file of readdirSync(testDirectory).filter((f) => f.endsWith('Tests.cs')).sort()) {
    const text = readFileSync(path.join(testDirectory, file), 'utf8');
    const name = file.slice(0, -'.cs'.length);
    if (permission.test(text)) permissionClasses.push(name);
    if (location.test(text)) locationClasses.push(name);
  }
  return { permissionClasses, locationClasses };
}

/** The map's source with its two cross-cutting lists replaced. Exported for the tests. */
export function withCrossCutting(mapSource, lists) {
  const wrapped = (names) => {
    const lines = [];
    let line = '   ';
    for (const name of names) {
      const piece = ` '${name}',`;
      if (line.length + piece.length > 96) {
        lines.push(line);
        line = '   ';
      }
      line += piece;
    }
    if (line.trim()) lines.push(line);
    return lines.join('\n');
  };
  let out = mapSource;
  for (const key of ['permissionClasses', 'locationClasses']) {
    const list = new RegExp(`(\\n  ${key}: \\[\\n)[\\s\\S]*?(\\n  \\],)`);
    if (!list.test(out)) throw new Error(`the map has no ${key} list in the shape this rewrites`);
    out = out.replace(list, (_, open, close) => `${open}${wrapped(lists[key])}${close}`);
  }
  return out;
}

/** The `dotnet test --filter` expression selecting exactly these classes. */
export function filterExpression(classes) {
  // The trailing dot pins the match to the class: FullyQualifiedName is Namespace.Class.Method,
  // and `~` alone would let one class name select another that merely starts with it.
  return classes.map((c) => `FullyQualifiedName~${TEST_NAMESPACE}.${c}.`).join('|');
}

/**
 * What the working tree's changes against `base` select, for a caller that wants to run it
 * rather than read it: `{ base, changed, mode, classes, reasons }`, mode being this script's
 * `targeted`, `full` or `none`.
 */
export async function selectionFor(base = 'master', mapFile = DEFAULT_MAP) {
  const map = await import(pathToFileURL(mapFile).href);
  const files = changedFiles(base);
  return { base, changed: files.length, ...classify(files, map) };
}

/** The working tree's changes against `base`: tracked differences and untracked files. */
export function changedFiles(base) {
  const opts = { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 };
  const diff = execFileSync('git', ['diff', '--name-only', base, '--'], opts);
  const untracked = execFileSync('git', ['ls-files', '--others', '--exclude-standard'], opts);
  return [...new Set((diff + untracked).split('\n').map((l) => l.trim()).filter(Boolean))];
}

async function main() {
  const args = process.argv.slice(2);
  const take = (flag) => {
    const i = args.indexOf(flag);
    if (i === -1) return undefined;
    const v = args[i + 1];
    args.splice(i, 2);
    return v;
  };
  const has = (flag) => {
    const i = args.indexOf(flag);
    if (i === -1) return false;
    args.splice(i, 1);
    return true;
  };

  const base = take('--base') ?? 'master';
  const here = HERE;
  const mapFile = take('--map') ?? DEFAULT_MAP;
  const rederive = has('--rederive');
  const asJson = has('--json');
  const asList = has('--list');
  const asFilter = has('--filter');
  if (args.length) {
    console.error(`unknown arguments: ${args.join(' ')}`);
    process.exit(1);
  }

  const map = await import(pathToFileURL(mapFile).href);

  if (rederive) {
    const lists = deriveCrossCutting(map, path.join(here, '..', ...TEST_DIR.split('/').filter(Boolean)));
    for (const key of ['permissionClasses', 'locationClasses']) {
      const before = new Set(map.crossCutting[key]);
      const after = new Set(lists[key]);
      const added = lists[key].filter((c) => !before.has(c));
      const removed = map.crossCutting[key].filter((c) => !after.has(c));
      console.log(`${key}: ${lists[key].length} classes`);
      for (const c of added) console.log(`  + ${c}`);
      for (const c of removed) console.log(`  - ${c}`);
    }
    writeFileSync(mapFile, withCrossCutting(readFileSync(mapFile, 'utf8'), lists));
    return;
  }

  const files = changedFiles(base);
  const result = classify(files, map);
  const filter = filterExpression(result.classes);

  if (asJson) {
    console.log(JSON.stringify({ base, changed: files.length, ...result, filter }, null, 2));
  } else if (asList) {
    for (const c of result.classes) console.log(c);
  } else if (asFilter) {
    console.log(filter);
  } else {
    console.log(`base: ${base}   changed files: ${files.length}   mode: ${result.mode}`);
    for (const r of result.reasons) console.log(`  - ${r}`);
    if (result.mode === 'targeted') {
      console.log(`\n${result.classes.length} classes:`);
      for (const c of result.classes) console.log(`  ${c}`);
      console.log(
        `\nrun them with:\n  dotnet test tests/SilexGis.Api.Tests --no-build --filter "${filter}"`,
      );
      // One process builds applications on about two cores whatever its thread count, so a
      // long list is quicker dealt into several.
      console.log(
        `or, in several test processes, from the repository root:\n  node scripts/gate-sharded.mjs --affected ${base}`,
      );
    } else if (result.mode === 'full') {
      console.log('\nrun the full suite:\n  dotnet test tests/SilexGis.Api.Tests --no-build');
    } else {
      console.log('\nno integration classes selected — the change is outside the API.');
    }
  }
  process.exit(result.mode === 'full' ? 10 : 0);
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  main().catch((e) => {
    console.error(e.message);
    process.exit(1);
  });
}
