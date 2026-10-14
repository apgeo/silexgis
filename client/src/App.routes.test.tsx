// SPDX-License-Identifier: AGPL-3.0-or-later
import { matchRoutes } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { routes } from './App.tsx';
import { RESLINK_TARGET_TYPES, targetTypeEntry } from './components/reslinks/registry.ts';

const SOME_ID = '00000000-0000-0000-0000-000000000001';

/**
 * The routes that answer an address, or null when only the catch-all does. The catch-all matches
 * everything, so asking `matchRoutes` alone would pass every check below whatever the table held.
 */
function pageFor(address: string) {
  const matched = matchRoutes(routes, address);
  return matched === null || matched.at(-1)?.route.path === '*' ? null : matched;
}

describe('an address nobody answers', () => {
  it('lands on the not-found page inside the signed-in frame, not on the router', () => {
    const matched = matchRoutes(routes, '/no/such/page');
    expect(matched?.at(-1)?.route.path).toBe('*');
    expect(pageFor('/no/such/page')).toBeNull();
    // Under the frame, so the navigation stays: the frame is the route above the catch-all.
    expect(matched?.some((m) => m.route.path === undefined && m.route.children?.some((c) => c.path === '*')))
      .toBe(true);
  });

  it('includes a mistyped page under a section that exists', () => {
    expect(matchRoutes(routes, '/settings/no-such-tab')?.at(-1)?.route.path).toBe('*');
  });

  it('is never the answer for an address the application does answer', () => {
    expect(pageFor('/caves')).not.toBeNull();
    expect(pageFor(`/trip-logs/${SOME_ID}`)?.at(-1)?.route.path).toBe('/trip-logs/:id');
  });

  it('meets the application\'s own error screen on every top-level route', () => {
    for (const route of routes) {
      expect(route.errorElement, `${route.path ?? '(layout)'} has no error screen`).toBeTruthy();
    }
  });
});

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
      expect(pageFor(route), `${type} routes to ${route}, which no page answers`)
        .toBeTruthy();
    }
  });

  it('include the page for a camp, which a chip and a grant notification both name', () => {
    expect(pageFor(`/expeditions/${SOME_ID}`)).toBeTruthy();
  });

  // The camp page offers its write-up by a link it builds itself, so the address is registered
  // in two places and is exactly the kind that goes missing from one of them.
  it('include the write-up the camp page sends people to', () => {
    expect(pageFor(`/expeditions/${SOME_ID}/report`)).toBeTruthy();
  });

  // The sidebar offers this one by key, and a menu key with no route behind it lands whoever
  // clicked it on the router's error screen rather than failing to navigate.
  it('include the list of camps the sidebar sends people to', () => {
    expect(pageFor('/expeditions')).toBeTruthy();
  });

  // The header offers this one, and it is registered in two places — the router and the sidebar's
  // selected-key list — so it is exactly the kind of address that goes missing from one of them.
  it('include the inbox the header sends people to', () => {
    expect(pageFor('/notifications')).toBeTruthy();
  });

  // Registered in the same two places, and the sidebar is the only way in — a page reachable
  // from no menu and matched by no route is a page nobody finds either way.
  it('include the delivery health page the sidebar offers', () => {
    expect(pageFor('/admin/notification-health')).toBeTruthy();
  });

  // The sidebar is the only way in to this one too, and only for full administrators — so a
  // missing route would be seen by the handful of people who are also the only ones who can use it.
  it('include the page of everything published that the sidebar offers', () => {
    expect(pageFor('/admin/published-trips')).toBeTruthy();
  });

  // The camp-roster vocabulary's page: offered by key from the configuration group and from the
  // camps list, and the one of the three activity vocabularies that had routes and no page.
  it('include the camp roles page the sidebar and the camps list offer', () => {
    expect(pageFor('/admin/camp-roster-roles')).toBeTruthy();
  });

  // Same reason, for the record of everything dated: the sidebar offers it by key.
  it('include the calendar the sidebar sends people to', () => {
    expect(pageFor('/calendar')).toBeTruthy();
  });

  // Three addresses the sidebar offers by key, under a group that exists for them alone: a menu
  // key with no route behind it lands whoever clicked it on the router's error screen.
  it('include the registry statistics screens the sidebar offers', () => {
    for (const route of [
      '/statistics/distribution',
      '/statistics/correlation',
      '/statistics/regions',
    ]) {
      const matched = pageFor(route);
      expect(matched, `${route} is offered in the sidebar and answered by no page`).toBeTruthy();
      expect(matched?.at(-1)?.route.path).toBe(route);
    }
  });

  // A button above the trip listing sends people here carrying their filter, and the address is
  // a static word standing where a trip's identifier otherwise stands — so it is exactly the kind
  // that can be swallowed by the route beside it instead of failing to be registered at all.
  it('include the trip insights page the listing sends people to', () => {
    const matched = pageFor('/trip-logs/stats');
    expect(matched).toBeTruthy();
    expect(matched?.at(-1)?.route.path).toBe('/trip-logs/stats');
  });

  // The same shape of address, and the same risk: swallowed by the route for one trip, the list
  // of deleted trips would open as a page saying no trip is called "deleted".
  it('include the deleted trips page the listing sends people to', () => {
    const matched = pageFor('/trip-logs/deleted');
    expect(matched).toBeTruthy();
    expect(matched?.at(-1)?.route.path).toBe('/trip-logs/deleted');
  });

  // And once more for features: swallowed by the route for one feature, the list of deleted
  // caves and features would open as a page saying no feature is called "deleted".
  it('include the deleted caves and features page the listings send people to', () => {
    const matched = pageFor('/features/deleted');
    expect(matched).toBeTruthy();
    expect(matched?.at(-1)?.route.path).toBe('/features/deleted');
  });
});
