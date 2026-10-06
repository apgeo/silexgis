// SPDX-License-Identifier: AGPL-3.0-or-later

// Removes the dev server's dependency caches that belong to checkouts which no longer exist.
//
// client/vite.config.ts gives every dev server a cache of its own, under the `node_modules` it
// resolves: one directory per checkout and port, about eighty megabytes each. Checkouts of this
// project usually share one `node_modules` through a link, so a checkout that is removed leaves
// its caches behind in a directory that goes on being used — twenty-odd of them had gathered,
// most for checkouts long gone. Each cache says whose it is in a file named `checkout`; a cache
// whose checkout is no longer on disk can be of no use to anybody, because the directory's name
// is derived from that path. A cache that does not say is left alone.
//
// Usage: node scripts/dependency-caches.mjs [<client directory>]

import { existsSync, readdirSync, readFileSync, rmSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import process from 'node:process';

/**
 * Removes every cache under `viteDir` whose checkout is gone, and returns the checkouts they
 * named. `stillThere` is how a path is asked after; it is a parameter so a test can answer.
 */
export function pruneDependencyCaches(viteDir, stillThere = existsSync) {
  const removed = [];
  let names;
  try {
    names = readdirSync(viteDir);
  } catch {
    return removed; // No cache directory at all: nothing has ever been served from here.
  }
  for (const name of names) {
    const marker = join(viteDir, name, 'checkout');
    let checkout;
    try {
      checkout = readFileSync(marker, 'utf8').trim();
    } catch {
      continue; // Says nothing about whose it is.
    }
    if (checkout !== '' && !stillThere(checkout)) {
      rmSync(join(viteDir, name), { recursive: true, force: true });
      removed.push(checkout);
    }
  }
  return removed;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  const clientDir =
    process.argv[2] ?? join(dirname(fileURLToPath(import.meta.url)), '..', 'client');
  const removed = pruneDependencyCaches(join(clientDir, 'node_modules', '.vite'));
  console.log(
    removed.length === 0
      ? 'No dependency cache of a removed checkout.'
      : `Removed ${removed.length} dependency cache(s) of checkouts that are gone:\n  ${removed.join('\n  ')}`,
  );
}
