// SPDX-License-Identifier: AGPL-3.0-or-later
import { isValidElement } from 'react';
import type { RouteObject } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { routes } from './App.tsx';
import RequireAuth from './auth/RequireAuth.tsx';

/**
 * Every address a visitor without an account can reach, and nothing else. Each one is opened by a
 * token, a mail link or a deliberate act of publication, or is part of signing in. Adding an
 * address here is a decision that something becomes reachable without an account; moving a page
 * above the sign-in guard by mistake makes it reachable too, and fails here instead of shipping a
 * page whose every request is refused.
 */
const ANONYMOUS = [
  '/login',
  '/auth/callback',
  '/confirm-email',
  '/reset-password',
  '/forgot-password',
  '/register',
  '/unsubscribe',
  '/shared/view/:token',
  '/shared/features/:token',
  '/shared/albums/:token',
  '/gallery/public',
  '/shared/trips/:token',
  '/shared/trips/:token/embed',
  '/q/:code',
  '/p/survey/:token',
  '/p/survey/:token/view',
].sort();

function isGuard(route: RouteObject): boolean {
  return isValidElement(route.element) && route.element.type === RequireAuth;
}

/** Paths reached without passing through the guard, and paths reached through it. */
function walk(table: RouteObject[], guarded: boolean, out: { open: string[]; closed: string[] }) {
  for (const route of table) {
    const underGuard = guarded || isGuard(route);
    if (route.path !== undefined) {
      (underGuard ? out.closed : out.open).push(route.path);
    }
    if (route.children) {
      walk(route.children, underGuard, out);
    }
  }
  return out;
}

describe('which addresses need an account', () => {
  const { open, closed } = walk(routes, false, { open: [], closed: [] });

  it('reaches exactly the agreed list without the sign-in guard', () => {
    expect([...open].sort()).toEqual(ANONYMOUS);
  });

  it('puts every other page, the catch-all included, behind the guard', () => {
    // The positive half: the guard is really in the table and holds the application's pages.
    expect(closed).toEqual(expect.arrayContaining(['/map', '/caves', '/admin/users', '*']));
    expect(closed.filter((path) => ANONYMOUS.includes(path))).toEqual([]);
  });
});
