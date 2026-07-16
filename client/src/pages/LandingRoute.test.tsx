// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it } from 'vitest';
import LandingRoute from './LandingRoute.tsx';
import { useUiPrefsStore } from '../stores/uiPrefsStore.ts';

function renderAt() {
  return render(
    <MemoryRouter initialEntries={['/']}>
      <Routes>
        <Route index element={<LandingRoute map={<div>the map</div>} />} />
        <Route path="/dashboard" element={<div>the dashboard</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

afterEach(() => {
  // The suite does not enable RTL's automatic cleanup, so unmount explicitly —
  // otherwise each render's DOM would stack up and the text queries would double-match.
  cleanup();
  useUiPrefsStore.setState({ landingPage: 'map' });
  localStorage.removeItem('silexgis.uiPrefs');
  window.location.hash = '';
});

describe('LandingRoute', () => {
  it('renders the map by default, keeping the workspace as the app default', () => {
    renderAt();
    expect(screen.getByText('the map')).toBeInTheDocument();
  });

  it('dispatches to the dashboard once the user opts in', () => {
    useUiPrefsStore.setState({ landingPage: 'dashboard' });
    renderAt();
    expect(screen.getByText('the dashboard')).toBeInTheDocument();
  });

  it('still shows the map for a shared map link, whatever the preference says', () => {
    // A hash carries a map position: the URL itself is the request, so it outranks the
    // landing preference — otherwise shared links would bounce to the dashboard.
    useUiPrefsStore.setState({ landingPage: 'dashboard' });
    window.location.hash = '#14/45.50000/25.40000';
    renderAt();
    expect(screen.getByText('the map')).toBeInTheDocument();
  });

  it('ignores a hash that is not a map position', () => {
    useUiPrefsStore.setState({ landingPage: 'dashboard' });
    window.location.hash = '#something-else';
    renderAt();
    expect(screen.getByText('the dashboard')).toBeInTheDocument();
  });
});
