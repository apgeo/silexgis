// SPDX-License-Identifier: AGPL-3.0-or-later
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../i18n';
import AppLayout from './AppLayout.tsx';

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

function renderShell(at = '/map') {
  return render(
    <MemoryRouter initialEntries={[at]}>
      <Routes>
        <Route path="/" element={<AppLayout />}>
          <Route path="map" element={<div>map page</div>} />
          <Route path="checklists" element={<div>checklists page</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

/** The label of whatever the sider is showing as the page you are on. */
const selectedItem = () =>
  document.querySelector('.ant-menu-item-selected')?.textContent?.trim();

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

describe('AppLayout selected destination', () => {
  /**
   * Every destination in the rail has to be recognised from the path, or opening it lights up
   * the map instead — which reads as "you are on the map" while you are plainly not. The failure
   * is silent: navigation still works, so only the highlight is wrong.
   */
  it('lights up the destination the path is under', () => {
    capabilities = { checklists: 'read' };
    renderShell('/checklists');

    expect(screen.getByText('checklists page')).toBeInTheDocument();
    expect(selectedItem()).toBe('Checklists');
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
