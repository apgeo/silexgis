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

  // The sidebar offers this one by key, and a menu key with no route behind it lands whoever
  // clicked it on the router's error screen rather than failing to navigate.
  it('include the list of camps the sidebar sends people to', () => {
    expect(matchRoutes(routes, '/expeditions')).toBeTruthy();
  });

  // The header offers this one, and it is registered in two places — the router and the sidebar's
  // selected-key list — so it is exactly the kind of address that goes missing from one of them.
  it('include the inbox the header sends people to', () => {
    expect(matchRoutes(routes, '/notifications')).toBeTruthy();
  });

  // Registered in the same two places, and the sidebar is the only way in — a page reachable
  // from no menu and matched by no route is a page nobody finds either way.
  it('include the delivery health page the sidebar offers', () => {
    expect(matchRoutes(routes, '/admin/notification-health')).toBeTruthy();
  });

  // Same reason, for the record of everything dated: the sidebar offers it by key.
  it('include the calendar the sidebar sends people to', () => {
    expect(matchRoutes(routes, '/calendar')).toBeTruthy();
  });

  // Three addresses the sidebar offers by key, under a group that exists for them alone: a menu
  // key with no route behind it lands whoever clicked it on the router's error screen.
  it('include the registry statistics screens the sidebar offers', () => {
    for (const route of [
      '/statistics/distribution',
      '/statistics/correlation',
      '/statistics/regions',
    ]) {
      const matched = matchRoutes(routes, route);
      expect(matched, `${route} is offered in the sidebar and answered by no page`).toBeTruthy();
      expect(matched?.at(-1)?.route.path).toBe(route);
    }
  });

  // A button above the trip listing sends people here carrying their filter, and the address is
  // a static word standing where a trip's identifier otherwise stands — so it is exactly the kind
  // that can be swallowed by the route beside it instead of failing to be registered at all.
  it('include the trip insights page the listing sends people to', () => {
    const matched = matchRoutes(routes, '/trip-logs/stats');
    expect(matched).toBeTruthy();
    expect(matched?.at(-1)?.route.path).toBe('/trip-logs/stats');
  });
});
