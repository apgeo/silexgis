// SPDX-License-Identifier: AGPL-3.0-or-later

// Deals the API integration suite into shards by test class, so hosted CI runners — four cores
// and a six-hour ceiling each — can take a slice apiece of a suite measured in hours and still
// return one verdict for the whole of it.
//
// A shard is a `dotnet test --filter` expression naming its classes. Classes are the top-level
// `*Tests.cs` files of the test project, the convention scripts/gate-affected.mjs already relies
// on, sorted and dealt round-robin: the same tree always deals the same way, every class lands
// in exactly one shard, and a class added tomorrow is in some shard the day it is added. The
// test beside this file proves the first two and that no test attribute hides in a file the
// convention would miss.
//
// Usage, from the repository root:
//   node scripts/gate-shard.mjs --index <0-based> --count <shards> [--filter | --list | --json]
//
// --filter (the default) prints the expression for `dotnet test --filter`; --list one class per
// line; --json both plus the counts.

import { readdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import process from 'node:process';

import { filterExpression } from './gate-affected.mjs';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
export const testDir = join(repoRoot, 'server', 'tests', 'SilexGis.Api.Tests');

/** The integration test classes, by the file-name convention, sorted. */
export function listClasses(dir = testDir) {
  return readdirSync(dir)
    .filter((f) => /^[A-Za-z0-9]+Tests\.cs$/.test(f))
    .map((f) => f.slice(0, -'.cs'.length))
    .sort();
}

/** The classes of shard `index` out of `count`, dealt round-robin over the sorted list. */
export function shard(classes, index, count) {
  if (!Number.isInteger(index) || !Number.isInteger(count) || count < 1 || index < 0 || index >= count) {
    throw new Error(`shard index must be in [0, count): got index ${index} of ${count}`);
  }
  return [...classes].sort().filter((_, i) => i % count === index);
}

function main() {
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
  const index = Number(take('--index'));
  const count = Number(take('--count'));
  const asJson = has('--json');
  const asList = has('--list');
  has('--filter');
  if (args.length) {
    console.error(`unknown arguments: ${args.join(' ')}`);
    process.exit(1);
  }
  const all = listClasses();
  const mine = shard(all, index, count);
  if (asJson) {
    console.log(JSON.stringify({ index, count, total: all.length, classes: mine, filter: filterExpression(mine) }, null, 2));
  } else if (asList) {
    console.log(mine.join('\n'));
  } else {
    console.log(filterExpression(mine));
  }
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  try {
    main();
  } catch (e) {
    console.error(e.message);
    process.exit(1);
  }
}
