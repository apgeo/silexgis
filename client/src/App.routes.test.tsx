// SPDX-License-Identifier: AGPL-3.0-or-later
import { matchRoutes } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { routes } from './App.tsx';
import { RESLINK_TARGET_TYPES, targetTypeEntry } from './components/reslinks/registry.ts';

const SOME_ID = '00000000-0000-0000-0000-000000000001';

/**
 * The two halves of a navigable chip, checked against each other.
 *
 * A kind of thing gets a route in the link registry the day its page ships, and the page's route
 * goes into the router in the same change — but nothing makes that simultaneous, and getting it
 * wrong does not fail to navigate: it lands the reader on the router's own error screen, which is
 * strictly worse than a chip that stays put. This asks the question the other way round, so a
 * route promised to a reader without a page behind it fails here instead of in front of them.
 */
describe('the addresses this application hands out', () => {
  it('are all addresses it answers', () => {
    for (const type of RESLINK_TARGET_TYPES) {
      const route = targetTypeEntry(type).route?.(SOME_ID);
      if (route === undefined || route === null) {
        // A kind with no page routes to null on purpose; the chip renders and does not move.
        continue;
      }
      expect(matchRoutes(routes, route), `${type} routes to ${route}, which no page answers`)
        .toBeTruthy();
    }
  });

  it('include the page for a camp, which a chip and a grant notification both name', () => {
    expect(matchRoutes(routes, `/expeditions/${SOME_ID}`)).toBeTruthy();
  });
});
