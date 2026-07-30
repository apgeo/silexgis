// SPDX-License-Identifier: AGPL-3.0-or-later
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import SettingsLayout from './SettingsLayout.tsx';

let mobile = false;

vi.mock('../../hooks/useIsMobile.ts', () => ({ useIsMobile: () => mobile }));

function LocationProbe() {
  return <div data-testid="location">{useLocation().pathname}</div>;
}

function renderShell(at = '/settings/emails') {
  return render(
    <MemoryRouter initialEntries={[at]}>
      <Routes>
        <Route path="/settings" element={<SettingsLayout />}>
          <Route path="profile" element={<div>profile section</div>} />
          <Route path="emails" element={<div>emails section</div>} />
        </Route>
      </Routes>
      <LocationProbe />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  mobile = false;
});

afterEach(cleanup);

describe('SettingsLayout', () => {
  it('marks the section the URL is showing', () => {
    renderShell();

    expect(document.querySelector('.ant-menu-item-selected')).toHaveTextContent('Emails');
    expect(screen.getByText('emails section')).toBeInTheDocument();
  });

  it('navigates to the section that was clicked', () => {
    renderShell();

    fireEvent.click(screen.getByText('Profile'));

    // Each section is its own route, which is what makes a section linkable — and what lets
    // the address-confirmation link land straight on the emails section.
    expect(screen.getByTestId('location')).toHaveTextContent('/settings/profile');
  });

  it('swaps the vertical nav for a picker on a phone', () => {
    mobile = true;
    renderShell();

    // A second off-canvas drawer would sit on the same screen edge as the shell's own.
    expect(document.querySelector('.ant-menu')).toBeNull();
    expect(document.querySelector('.ant-select')).not.toBeNull();
    expect(screen.getByText('emails section')).toBeInTheDocument();
  });
});
