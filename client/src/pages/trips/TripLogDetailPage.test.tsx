// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation, useNavigate } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { TripLogInfo } from '../../api/hooks.ts';
import TripLogDetailPage from './TripLogDetailPage.tsx';

const TRIP = '33333333-4444-5555-6666-777777777777';

const { tripSpy, canSpy, importTrack, configSpy } = vi.hoisted(() => ({
  tripSpy: vi.fn(),
  canSpy: vi.fn(),
  importTrack: vi.fn(),
  configSpy: vi.fn(),
}));

vi.mock('../../api/hooks.ts', () => ({
  useTripLog: () => tripSpy(),
  useCavingGroups: () => ({ data: [] }),
  useTripTypes: () => ({ data: [] }),
  useTripParticipantRoles: () => ({ data: [] }),
  useDeleteTripLog: () => ({ mutateAsync: vi.fn() }),
  useStandDownTripCallout: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useArrangeTripCallout: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useUpdateTripLog: () => ({ mutateAsync: vi.fn() }),
  useImportTripTrack: () => ({ mutateAsync: importTrack, isPending: false }),
  useEffectiveAccess: () => ({ data: undefined }),
  useTripLogConfig: () => configSpy(),
  useCan: () => canSpy(),
  parseAccessActions: (actions: string) => new Set(actions.split(',')),
}));

// The panes are mounted by name here, not exercised: each has its own tests, and each asks the
// server for something of its own that this page knows nothing about.
vi.mock('./TripSections.tsx', () => ({ default: () => <div>what the trip recorded</div> }));
vi.mock('../../components/trips/TripGeometryField.tsx', () => ({
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
vi.mock('./TripChecklistTab.tsx', () => ({
  default: () => <div>what the party settles before it sets off</div>,
}));
vi.mock('./TripTrackingTab.tsx', () => ({
  default: () => <div>where the party is</div>,
}));
vi.mock('./TripFormModal.tsx', () => ({ default: () => <div /> }));
vi.mock('./TripStateControl.tsx', () => ({ default: () => <div /> }));
vi.mock('../../components/trips/TripCover.tsx', () => ({ default: () => <div /> }));
vi.mock('../../components/trips/TripGallerySection.tsx', () => ({
  default: () => <div>the photographs filed against the trip</div>,
}));
vi.mock('../../components/photolibrary/TripLibraryPhotoPanel.tsx', () => ({
  default: () => <div>what a neighbouring library holds from those days</div>,
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
  importTrack.mockReset();
  configSpy.mockReset().mockReturnValue({ data: { deletedRetentionDays: 30 } });
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

    // Both of these panes were drawn but unreachable by address: the key was in the strip and not
    // in the list the page reads the address against, so a link to either one landed on the
    // report instead — with the right tab visibly selected in the strip above it.
    cleanup();
    renderPage(`/trip-logs/${TRIP}?tab=checklist`);
    expect(screen.getByText('what the party settles before it sets off')).toBeTruthy();

    cleanup();
    renderPage(`/trip-logs/${TRIP}?tab=tracking`);
    expect(screen.getByText('where the party is')).toBeTruthy();
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

    // And beside it, what a neighbouring library holds from the days the trip was out — a reading
    // of somebody else's archive, so it is there for any reader for the same reason the gallery
    // is: nothing about it writes anything.
    expect(screen.getByText('what a neighbouring library holds from those days')).toBeTruthy();

    cleanup();
    canSpy.mockReturnValue(true);
    renderPage(`/trip-logs/${TRIP}?tab=photos`);
    expect(screen.getByTestId('trip-photo-import')).toBeTruthy();
  });

  it('says "no such trip" for a trip the server refused, instead of loading forever', () => {
    // The server answers 404 both for a trip that does not exist and for one the caller may not
    // read, by design. The read then settles as an error and is not retried, so a refusal that
    // left the page on its spinner would hold it there for good — which a reader reports as the
    // application having hung, not as a permission. The page names the ambiguity in words rather
    // than guessing which of the two it is.
    tripSpy.mockReturnValue({ data: undefined, isPending: false, isError: true });
    renderPage();

    expect(screen.getByText('No such trip')).toBeInTheDocument();
    expect(
      screen.getByText(/Either it is not there, or it is not yours to read/),
    ).toBeInTheDocument();
    expect(document.querySelector('.ant-spin')).toBeNull();
    expect(screen.queryByTestId('trip-open-report')).toBeNull();
    // A reader who could not have deleted a trip is not pointed at a list they have no use for.
    expect(screen.queryByTestId('trip-not-found-deleted')).toBeNull();
  });

  /**
   * A deleted trip answers exactly as one that was never there, so the page cannot say which it
   * is looking at. What it can do is say where a deleted one would be found, to somebody who
   * could have deleted it — the person who followed an old link to a trip they removed last week.
   */
  it('points somebody who could have deleted the trip at the deleted trips', () => {
    canSpy.mockReturnValue(true);
    tripSpy.mockReturnValue({ data: undefined, isPending: false, isError: true });
    renderPage();

    expect(screen.getByText(/it may still be among the deleted trips/)).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('trip-not-found-deleted'));
    expect(screen.getByTestId('address').textContent).toBe('/trip-logs/deleted');
  });

  /**
   * The confirmation says what the delete is about to do, in the installation's own number. The
   * number is the server's: one written into the page would go on promising thirty days on an
   * installation that keeps seven, in the one sentence somebody reads before pressing the button.
   */
  describe('deleting', () => {
    const confirmation = async () => {
      fireEvent.click(screen.getByTestId('trip-delete'));
      return (await screen.findByRole('tooltip')).textContent ?? '';
    };

    beforeEach(() => canSpy.mockReturnValue(true));

    it('says the trip can be restored and for how long, as the server reported it', async () => {
      configSpy.mockReturnValue({ data: { deletedRetentionDays: 7 } });
      renderPage();

      const text = await confirmation();
      expect(text).toContain('Delete this trip log?');
      expect(text).toContain('It can be restored from Deleted trips for 7 days.');
    });

    it('names no deadline where the installation keeps deleted trips', async () => {
      configSpy.mockReturnValue({ data: { deletedRetentionDays: null } });
      renderPage();

      const text = await confirmation();
      expect(text).toContain('It can be restored from Deleted trips.');
      expect(text).not.toMatch(/\bfor \d/);
    });

    it('promises nothing it has not been told yet', async () => {
      configSpy.mockReturnValue({ data: undefined });
      renderPage();

      const text = await confirmation();
      expect(text).toContain('Delete this trip log?');
      expect(text).not.toContain('restored');
    });
  });

  it('lets the row of page actions wrap rather than pushing the whole page sideways', () => {
    // Five buttons in a row come to 641px and a phone is 412px across. Unwrapped, this row was the
    // largest single reason the trip page scrolled sideways — larger than the tables under it —
    // and a page that scrolls sideways takes every other control off the screen with it: measured
    // on a 412px screen, sliding far enough right to read a caver's position put the tracking
    // card's Save button 297px off the left edge. Nothing moves where the row already fits.
    canSpy.mockReturnValue(true);
    renderPage();

    const actions = screen.getByTestId('trip-open-report').closest('.ant-flex');
    expect(actions).toHaveStyle({ flexWrap: 'wrap' });
  });


  /**
   * A recorded track becomes the sketch from the page, by choosing a file: the picker hands the
   * file to the trip's own geometry route and uploads nothing itself. Offered where editing is,
   * and to nobody else — a reader has no sketch to replace.
   */
  describe('importing a track', () => {
    it('posts the chosen GPX file to the trip', async () => {
      canSpy.mockReturnValue(true);
      importTrack.mockResolvedValue(trip());
      const { container } = renderPage();

      const file = new File(['<gpx/>'], 'walk.gpx', { type: 'application/gpx+xml' });
      const picker = container.querySelector('input[type="file"]') as HTMLInputElement;
      fireEvent.change(picker, { target: { files: [file] } });

      await waitFor(() => expect(importTrack).toHaveBeenCalledWith({ id: TRIP, file }));
    });

    it('offers no import to a reader', () => {
      canSpy.mockReturnValue(false);
      renderPage();

      expect(screen.queryByTestId('trip-import-track')).toBeNull();
    });
  });
});
