// SPDX-License-Identifier: AGPL-3.0-or-later
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../i18n';
import AppLayout from './AppLayout.tsx';

let mobile = false;
let capabilities: Record<string, string> | undefined;
// The rank the relation vocabulary is gated on, which no capability answer expresses:
// membership of the protected group is what the server resolves it from.
let permissionGroups: { slug: string }[] = [];

vi.mock('../hooks/useIsMobile.ts', () => ({ useIsMobile: () => mobile }));
const saveLocale = vi.fn();
let me: { avatarUrl: null; displayName?: string | null; email?: string } = { avatarUrl: null };
vi.mock('../api/hooks.ts', () => ({
  useMe: () => ({ data: me }),
  useUpdateLocale: () => ({ mutate: saveLocale }),
  useMyPermissionGroups: () => ({ data: permissionGroups }),
  useCapabilities: () => ({ data: capabilities ? { domains: capabilities } : undefined }),
  // The real helper, inlined: the mock replaces the module wholesale.
  hasAccessAction: (actions: string | undefined, flag: string) =>
    (actions ?? '').split(',').map((x) => x.trim()).includes(flag),
}));
// The user-name claim carries the protected label the server publishes to third parties: for an
// account that never set a display name it is a generated pseudonym, not a name or an address.
vi.mock('../auth/auth.tsx', () => ({
  useAuth: () => ({
    user: { profile: { preferred_username: 'user-4f2b8c1d', email: 'caver@example.org' } },
    signOut: vi.fn(),
  }),
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
  me = { avatarUrl: null, displayName: null, email: 'caver@example.org' };
  mobile = false;
  capabilities = undefined;
  permissionGroups = [];
  saveLocale.mockClear();
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

describe('AppLayout account name', () => {
  it('names an account with no display name by its address, never by the protected label', () => {
    renderShell();

    // Showing somebody their own address is not the leak the label exists to close; showing them
    // "user-4f2b8c1d" instead leaves no way to tell which account is signed in on a shared machine.
    expect(screen.getByText('caver@example.org')).toBeInTheDocument();
    expect(screen.queryByText('user-4f2b8c1d')).toBeNull();
  });

  it('prefers the display name once there is one', () => {
    me = { avatarUrl: null, displayName: 'Ana', email: 'caver@example.org' };
    renderShell();

    expect(screen.getByText('Ana')).toBeInTheDocument();
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

describe('AppLayout language switch', () => {
  it('tells the server which language was chosen, and the zone it was chosen in', async () => {
    renderShell();

    // By accessible name, not position: several comboboxes exist once a page is mounted below.
    const selector = screen.getByRole('combobox', { name: 'Language' });
    await act(async () => {
      fireEvent.mouseDown(selector);
    });
    await act(async () => {
      fireEvent.click(await screen.findByTitle('RO'));
    });

    // The language a person reads in is also the language every message to them is written in,
    // so it has to reach the account and not only this browser.
    expect(saveLocale).toHaveBeenCalledTimes(1);
    expect(saveLocale.mock.calls[0][0]).toMatchObject({ language: 'ro' });
    const { timeZone } = saveLocale.mock.calls[0][0] as { timeZone: string | null };
    expect(timeZone === null || typeof timeZone === 'string').toBe(true);
  });
});
