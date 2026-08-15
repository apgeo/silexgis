// SPDX-License-Identifier: AGPL-3.0-or-later
import { matchRoutes, MemoryRouter, Route, Routes } from 'react-router-dom';
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../i18n';
import AppLayout from './AppLayout.tsx';
import { routes } from '../App.tsx';

let mobile = false;
let capabilities: Record<string, string> | undefined;
// The rank the relation vocabulary is gated on, which no capability answer expresses:
// membership of the protected group is what the server resolves it from.
let permissionGroups: { slug: string }[] = [];

vi.mock('../hooks/useIsMobile.ts', () => ({ useIsMobile: () => mobile }));
vi.mock('../api/hooks.ts', () => ({
  useMe: () => ({ data: { avatarUrl: null } }),
  useMyPermissionGroups: () => ({ data: permissionGroups }),
  useCapabilities: () => ({ data: capabilities ? { domains: capabilities } : undefined }),
  // The real helper, inlined: the mock replaces the module wholesale.
  hasAccessAction: (actions: string | undefined, flag: string) =>
    (actions ?? '').split(',').map((x) => x.trim()).includes(flag),
}));
vi.mock('../auth/auth.tsx', () => ({
  useAuth: () => ({ user: { profile: { preferred_username: 'tester' } }, signOut: vi.fn() }),
}));

function renderShell() {
  return render(
    <MemoryRouter initialEntries={['/map']}>
      <Routes>
        <Route path="/" element={<AppLayout />}>
          <Route path="map" element={<div>map page</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

const sider = () => document.querySelector('.ant-layout-sider');
const zeroWidthTrigger = () => document.querySelector('.ant-layout-sider-zero-width-trigger');

beforeEach(() => {
  mobile = false;
  capabilities = undefined;
  permissionGroups = [];
});

afterEach(cleanup);

describe('AppLayout sider', () => {
  it('keeps the collapsed icon rail on desktop', () => {
    renderShell();

    // 80px is antd's collapsed rail. Guards the reason this sider is controlled rather
    // than using antd's `breakpoint` prop: that prop drives `collapsed` in both
    // directions and would force the rail open here, to its full 200px.
    expect(sider()).toHaveStyle({ width: '80px' });
    expect(zeroWidthTrigger()).toBeNull();
    expect(screen.getByText('map page')).toBeInTheDocument();
  });

  it('takes the rail off-canvas with a trigger on mobile', () => {
    mobile = true;
    renderShell();

    expect(sider()).toHaveStyle({ width: '0px' });
    // antd's own edge trigger is what brings it back; without it the nav is unreachable.
    expect(zeroWidthTrigger()).not.toBeNull();
  });
});

describe('AppLayout nav gating', () => {
  it('offers no admin destinations before capabilities arrive', () => {
    renderShell();

    expect(screen.queryByText('Permission groups')).toBeNull();
    expect(screen.queryByText('Audit')).toBeNull();
  });

  it('shows exactly the destinations the capabilities carry', () => {
    capabilities = {
      permissionGroups: 'read, write',
      audit: 'read',
      // Held but without read — must not surface the page.
      messageTemplates: 'write',
    };
    renderShell();

    expect(screen.getByText('Permission groups')).toBeInTheDocument();
    expect(screen.getByText('Audit')).toBeInTheDocument();
    expect(screen.queryByText('Message texts')).toBeNull();
    expect(screen.queryByText('Messaging')).toBeNull();
    expect(screen.queryByText('Feature sets')).toBeNull();
  });

  it('offers the terrain builder only to a holder of the terrain right', () => {
    // Every other domain right there is, and still not the terrain page: the elevation
    // surface is its own installation-level domain, so no other right earns it. The holder
    // is asserted in the same test, because a gate that had quietly stopped offering the
    // page to anybody would otherwise read as a passing security assertion.
    capabilities = { permissionGroups: 'read, write', taxonomies: 'read, write', audit: 'read' };
    renderShell();
    expect(screen.queryByText('Terrain')).toBeNull();

    cleanup();
    capabilities = { terrain: 'read, execute, delete' };
    renderShell();
    expect(screen.getByText('Terrain')).toBeInTheDocument();
  });

  it('offers the relation vocabulary to the rank that can write it, not to a domain right', () => {
    // Every domain right there is, and still not the rank: the vocabulary is not a
    // resource domain, so no amount of domain access earns the page.
    capabilities = { permissionGroups: 'read, write', taxonomies: 'read, write', audit: 'read' };
    renderShell();
    expect(screen.queryByText('Link relations')).toBeNull();

    cleanup();
    permissionGroups = [{ slug: 'full-administrators' }];
    renderShell();
    expect(screen.getByText('Link relations')).toBeInTheDocument();
  });
});

describe('AppLayout nav destinations', () => {
  it('offers no destination the router cannot match', () => {
    // Selecting an item navigates to `/<key>`, and this application's router has no
    // fallback route and no error element: an unmatched path does not land on an empty
    // page, it replaces header, sider and content with the router's own error screen,
    // leaving no way back inside the application. So every key the rail can offer has to
    // resolve, and the rail is asked with everything granted so nothing is out of view.
    // Every domain answers with every action, whatever domains exist — a fixed list here
    // would quietly stop covering a domain added later, which is exactly the case this
    // guards.
    capabilities = new Proxy({}, { get: () => 'read, write, create, delete' }) as Record<string, string>;
    permissionGroups = [{ slug: 'full-administrators' }];
    renderShell();

    const rail = sider();
    expect(rail).not.toBeNull();
    const keys = Array.from(rail!.querySelectorAll('[data-menu-id]'))
      // rc-menu identifies an item as "<its own id>-<the key we gave it>", where its id is
      // "rc-menu-uuid" plus a counter that is left off under test. Both shapes are stripped
      // here; the count assertion below fails loudly if neither matched.
      .map((node) => /^rc-menu-uuid-(?:\d+-)?(.+)$/.exec(node.getAttribute('data-menu-id') ?? '')?.[1])
      .filter((key): key is string => key !== undefined);

    // The rail is long; a selector that had stopped matching would otherwise pass here
    // by finding nothing at all to check.
    expect(keys).toContain('admin/terrain');
    expect(keys.length).toBeGreaterThan(15);

    const unmatched = keys.filter((key) => matchRoutes(routes, `/${key}`) === null);
    expect(unmatched).toEqual([]);
  });
});
