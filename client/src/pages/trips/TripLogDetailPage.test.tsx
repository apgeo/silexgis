// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation, useNavigate } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { TripLogInfo } from '../../api/hooks.ts';
import TripLogDetailPage from './TripLogDetailPage.tsx';

const TRIP = '33333333-4444-5555-6666-777777777777';

const { tripSpy, canSpy } = vi.hoisted(() => ({ tripSpy: vi.fn(), canSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useTripLog: () => tripSpy(),
  useCavingGroups: () => ({ data: [] }),
  useTripTypes: () => ({ data: [] }),
  useTripParticipantRoles: () => ({ data: [] }),
  useDeleteTripLog: () => ({ mutateAsync: vi.fn() }),
  useUpdateTripLog: () => ({ mutateAsync: vi.fn() }),
  useEffectiveAccess: () => ({ data: undefined }),
  useCan: () => canSpy(),
  parseAccessActions: (actions: string) => new Set(actions.split(',')),
}));

// The panes are mounted by name here, not exercised: each has its own tests, and each asks the
// server for something of its own that this page knows nothing about.
vi.mock('./TripSections.tsx', () => ({ default: () => <div>what the trip recorded</div> }));
vi.mock('./TripGeometryField.tsx', () => ({
  default: ({ active }: { active?: boolean }) => (
    <div data-testid="sketch">the shape the trip drew, shown: {String(active)}</div>
  ),
}));
vi.mock('./TripRoleFields.tsx', () => ({
  default: () => <div>what the trip did to what it names</div>,
}));
vi.mock('./TripInvitationsTab.tsx', () => ({
  default: () => <div>who was asked and what each said</div>,
}));
vi.mock('./TripFormModal.tsx', () => ({ default: () => <div /> }));
vi.mock('./TripStateControl.tsx', () => ({ default: () => <div /> }));
vi.mock('../../components/trips/TripCover.tsx', () => ({ default: () => <div /> }));
vi.mock('../../components/trips/TripGallerySection.tsx', () => ({
  default: () => <div>the photographs filed against the trip</div>,
}));
vi.mock('../../components/reslinks/LinksSection.tsx', () => ({
  default: () => <div>everything else the trip is tied to</div>,
}));
vi.mock('../../components/attachments/AttachmentSection.tsx', () => ({
  default: () => <div>what is filed against the trip</div>,
}));
vi.mock('../../components/history/HistoryPanel.tsx', () => ({
  default: () => <div>what was changed and by whom</div>,
}));
vi.mock('../../components/tags/TagChips.tsx', () => ({ default: () => <span /> }));
vi.mock('../../components/permissions/PermissionsModal.tsx', () => ({ default: () => <div /> }));

function trip(overrides: Partial<TripLogInfo> = {}): TripLogInfo {
  return {
    id: TRIP,
    title: 'Digging weekend',
    tripTypeId: null,
    tripDate: '2026-03-14',
    tripDateEnd: null,
    entryTime: null,
    exitTime: null,
    description: null,
    results: null,
    weatherConditions: null,
    locationText: null,
    organizingCavingGroupId: null,
    geom: { type: 'Point', coordinates: [22.5, 46.5] },
    caveIds: [],
    cavesWithheld: 0,
    participants: [],
    proposers: [],
    cavingGroupId: null,
    visibility: 'private',
    state: 'published',
    publishedAt: null,
    depthReachedM: null,
    lengthSurveyedM: null,
    surveyStations: null,
    ropeMetres: null,
    hadIncident: false,
    fieldData: {},
    logistics: {},
    safety: {},
    maxParticipants: null,
    ...overrides,
  } as unknown as TripLogInfo;
}

/** One step back, so that leaving the trip can be told apart from walking its tabs. */
function Back() {
  const navigate = useNavigate();
  return (
    <button type="button" onClick={() => void navigate(-1)}>
      go back
    </button>
  );
}

/** Reads the address back out, which is the whole claim a tab in the URL makes. */
function Address() {
  const location = useLocation();
  return <span data-testid="address">{`${location.pathname}${location.search}`}</span>;
}

function renderPage(entry = `/trip-logs/${TRIP}`) {
  return render(
    <App>
      <MemoryRouter initialEntries={['/somewhere-else', entry]} initialIndex={1}>
        <Address />
        <Back />
        <Routes>
          <Route path="/somewhere-else" element={<div>somewhere else entirely</div>} />
          <Route path="/trip-logs/:id" element={<TripLogDetailPage />} />
        </Routes>
      </MemoryRouter>
    </App>,
  );
}

afterEach(cleanup);
beforeEach(() => {
  tripSpy.mockReturnValue({ data: trip(), isPending: false });
  canSpy.mockReturnValue(false);
});

describe('the trip page', () => {
  it('opens on its own tab, and reads the tab out of the address', () => {
    renderPage();
    expect(screen.getByText('what the trip recorded')).toBeTruthy();

    cleanup();
    renderPage(`/trip-logs/${TRIP}?tab=links`);
    expect(screen.getByText('what the trip did to what it names')).toBeTruthy();

    cleanup();
    renderPage(`/trip-logs/${TRIP}?tab=invitations`);
    expect(screen.getByText('who was asked and what each said')).toBeTruthy();

    cleanup();
    renderPage(`/trip-logs/${TRIP}?tab=files`);
    expect(screen.getByText('what is filed against the trip')).toBeTruthy();
  });

  it('falls back to its own tab when the address names one it does not have', () => {
    // antd draws nothing at all under the tab strip for an activeKey matching no pane, so an
    // address somebody edited by hand must not be able to produce a page with no content.
    renderPage(`/trip-logs/${TRIP}?tab=quantumTunnel`);

    expect(screen.getByText('what the trip recorded')).toBeTruthy();
  });

  it('writes the tab into the address, and leaves the trip on the way back rather than walking it', () => {
    renderPage();
    // The page's own tab is the bare address: it is where the page opens, and a parameter
    // saying so would make two addresses for one place.
    expect(screen.getByTestId('address').textContent).toBe(`/trip-logs/${TRIP}`);

    fireEvent.click(screen.getByRole('tab', { name: 'Photographs' }));
    expect(screen.getByTestId('address').textContent).toBe(`/trip-logs/${TRIP}?tab=photos`);
    expect(screen.getByText('the photographs filed against the trip')).toBeTruthy();

    fireEvent.click(screen.getByRole('tab', { name: 'History' }));
    expect(screen.getByTestId('address').textContent).toBe(`/trip-logs/${TRIP}?tab=history`);

    // Two tabs opened, one step back: switching replaces rather than pushes, so back leaves the
    // trip instead of retracing every tab the reader looked at on the way.
    fireEvent.click(screen.getByText('go back'));
    expect(screen.getByTestId('address').textContent).toBe('/somewhere-else');
  });

  it('tells the pane that owns a map when it is the one on screen, not merely when it is mounted', () => {
    // A map built against a container that is not being shown measures nothing and draws a blank
    // tile grid which never repairs itself, and the strip keeps a pane mounted once it has been
    // opened — so being mounted is not the same question as being visible.
    renderPage();
    expect(screen.getByTestId('sketch').textContent).toContain('shown: true');

    fireEvent.click(screen.getByRole('tab', { name: 'Files' }));
    expect(screen.getByTestId('sketch').textContent).toContain('shown: false');
  });

  it('offers filing photographs into the trip only to somebody who may write it', () => {
    canSpy.mockReturnValue(false);
    renderPage(`/trip-logs/${TRIP}?tab=photos`);
    // The gallery itself is there for any reader; putting photographs into the trip is not.
    expect(screen.getByText('the photographs filed against the trip')).toBeTruthy();
    expect(screen.queryByTestId('trip-photo-import')).toBeNull();

    cleanup();
    canSpy.mockReturnValue(true);
    renderPage(`/trip-logs/${TRIP}?tab=photos`);
    expect(screen.getByTestId('trip-photo-import')).toBeTruthy();
  });
});
