// SPDX-License-Identifier: AGPL-3.0-or-later
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../i18n';
import AppLayout from './AppLayout.tsx';

let mobile = false;

vi.mock('../hooks/useIsMobile.ts', () => ({ useIsMobile: () => mobile }));
vi.mock('../api/hooks.ts', () => ({ useMe: () => ({ data: { roles: [] } }) }));
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
