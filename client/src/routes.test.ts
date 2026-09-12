// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';

/**
 * Every in-app navigation target has to be a route this application declares.
 *
 * The failure this exists for is silent in every other check: a button navigating to
 * `/trips/{id}` while the route is `/trip-logs/:id` compiles, type-checks, lints, renders,
 * and takes the reader to the router's bare "404 Not Found" developer page. It shipped on
 * the map's trip selection panel, where no test touched the button at all — and a test
 * written for that one button would only have pinned whatever string the button already
 * had. So the assertion is over the whole application instead: read the route table and
 * every navigation literal out of the source, and require the second to be covered by the
 * first.
 *
 * Read through the bundler rather than the filesystem, as `i18n.test.ts` does, so this
 * stays inside the app's own module world.
 */
const sourceText = import.meta.glob('./**/*.{ts,tsx}', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

/** The first path segment of every route the router declares, e.g. `trip-logs`. */
function declaredRoots(): Set<string> {
  const app = sourceText['./App.tsx'];
  expect(app, 'App.tsx should be readable through the glob').toBeTruthy();
  const roots = new Set<string>();
  for (const [, path] of app.matchAll(/path:\s*'(\/[^']*)'/g)) {
    const root = path.split('/')[1];
    if (root) roots.add(root);
  }
  return roots;
}

/**
 * The first path segment of every absolute path handed to `navigate(...)` as a literal.
 *
 * Only literals: a path assembled from a variable cannot be checked here, and pretending
 * otherwise would make this test look stronger than it is.
 */
function navigatedRoots(): Map<string, string[]> {
  const found = new Map<string, string[]>();
  for (const [file, text] of Object.entries(sourceText)) {
    if (file.includes('.test.')) continue;
    for (const [, root] of text.matchAll(/navigate\(\s*[`'"]\/([a-z0-9-]+)/g)) {
      found.set(root, [...(found.get(root) ?? []), file]);
    }
  }
  return found;
}

describe('in-app navigation', () => {
  it('never sends the reader to a path no route declares', () => {
    const declared = declaredRoots();
    // The route table is what this is measured against, so an empty read would pass
    // everything; assert it was really found before trusting a comparison with it.
    expect(declared.size).toBeGreaterThan(10);
    expect(declared.has('trip-logs')).toBe(true);

    const navigated = navigatedRoots();
    expect(navigated.size).toBeGreaterThan(5);

    const unrouted = [...navigated.entries()]
      .filter(([root]) => !declared.has(root))
      .map(([root, files]) => `/${root} (from ${files.join(', ')})`);

    expect(unrouted).toEqual([]);
  });
});
