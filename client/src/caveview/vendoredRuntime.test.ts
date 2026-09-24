// SPDX-License-Identifier: AGPL-3.0-or-later
import { existsSync, readdirSync, statSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { CAVEVIEW_HOME } from './loadCaveView.ts';

/**
 * That the viewer this application says it loads is the one actually sitting on disk.
 *
 * <b>The failure this exists for.</b> The viewer is not an npm package; it is a prebuilt bundle
 * vendored under a directory named by its distribution version, and `CAVEVIEW_HOME` is a string
 * naming that directory. Upgrading means copying a new build in **and** changing the string, and
 * nothing connected the two: a build copied without the bump goes on serving the old viewer, and a
 * bump without the copy 404s every asset. Neither shows up in a type check, a lint, or any test —
 * the first sign of either is a reader opening the 3D view and getting nothing, which is the point
 * at which nobody is looking at this file.
 *
 * It asserts the relationship rather than a version, so an upgrade never has to edit it.
 */

const publicDir = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'public');

/** The paths the loader actually fetches, derived the same way the loader derives them. */
const fetched = ['js/CaveView2.min.js', 'css/caveview.css'];

describe('the vendored viewer runtime', () => {
  it('is where the loader says it is, and carries what the loader asks for', () => {
    expect(CAVEVIEW_HOME.startsWith('/caveview/'), CAVEVIEW_HOME).toBe(true);
    expect(CAVEVIEW_HOME.endsWith('/'), 'the home is a directory, joined to by string').toBe(true);

    const home = join(publicDir, CAVEVIEW_HOME);
    expect(existsSync(home), `${CAVEVIEW_HOME} is vendored`).toBe(true);

    for (const rel of fetched) {
      const file = join(home, rel);
      expect(existsSync(file), `${CAVEVIEW_HOME}${rel} exists`).toBe(true);
      // Present-but-empty is the shape a half-finished copy leaves behind, and it loads as a
      // viewer that does nothing rather than as a missing file.
      expect(statSync(file).size, `${CAVEVIEW_HOME}${rel} is not empty`).toBeGreaterThan(1024);
    }

    // The workers are resolved by the bundle at runtime against the same home, so they are not
    // named in the source anywhere and a copy that missed them would break only on a model load.
    const workers = join(home, 'js', 'workers');
    expect(existsSync(workers), `${CAVEVIEW_HOME}js/workers/ exists`).toBe(true);
    expect(readdirSync(workers).filter((f) => f.endsWith('.js')).length).toBeGreaterThan(0);
  });

  it('keeps at most one superseded version beside it', () => {
    // The vendoring rule is to keep the previous directory for one release — a browser tab opened
    // before an upgrade still asks for the old paths — and to delete it in the release after. Left
    // unchecked they accumulate silently, and each one is most of a megabyte of dead bundle shipped
    // in every image. Two is the rule's maximum: the current one and the one it replaced.
    const versions = readdirSync(join(publicDir, 'caveview'))
      .filter((entry) => entry.startsWith('v'))
      .filter((entry) => statSync(join(publicDir, 'caveview', entry)).isDirectory());

    expect(versions).toContain(CAVEVIEW_HOME.replace('/caveview/', '').replace('/', ''));
    expect(versions.length, `vendored versions: ${versions.join(', ')}`).toBeLessThanOrEqual(2);
  });
});
