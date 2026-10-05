// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { CaveDetail, Entrance } from '../../api/hooks.ts';

/**
 * The entrances table, and specifically what it says about a position it is not allowed to state
 * precisely.
 *
 * The table prints five decimals — about a metre — for every row alike, including the rows the
 * server snapped to a 5 km grid before sending. That was already true before the map button was
 * added; the button made it matter, because a control that frames a coordinate is a much stronger
 * claim about it than a number in a cell. So what is asserted here is not "the button works" but
 * the pair: that an approximate row is marked as one, and that framing it goes to a zoom at which
 * the screen does not present a snapped point as a surveyed one.
 */

const navigate = vi.fn();
const fitGeoJsonGeometry = vi.fn();
const setSelection = vi.fn();
// What the permissions dialog was last handed; the dialog itself is somebody else's to test.
const permissionsModal = vi.fn();

let entrances: Entrance[] = [];
// What the domain capability answers, which is what the page falls back on with no summary.
let can = false;

vi.mock('react-router-dom', async () => ({
  ...(await vi.importActual<typeof import('react-router-dom')>('react-router-dom')),
  useNavigate: () => navigate,
  useParams: () => ({ id: 'cave-1' }),
}));

vi.mock('../../map/mapContext.ts', () => ({
  APPROXIMATE_MAX_ZOOM: 12,
  fitGeoJsonGeometry: (geometry: object, maxZoom?: number) => fitGeoJsonGeometry(geometry, maxZoom),
}));

vi.mock('../../stores/workspaceStore.ts', () => ({
  useWorkspaceStore: (selector: (s: { setSelection: typeof setSelection }) => unknown) =>
    selector({ setSelection }),
}));

vi.mock('../../api/hooks.ts', () => ({
  useCave: () => ({ data: cave(), isPending: false }),
  useCaveSummary: () => ({ data: undefined }),
  // The declared-depths card is drawn on this page; nothing declared, so it says so.
  useCaveDepthPlaces: () => ({ data: [] }),
  useWriteCaveDepthPlace: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useDeleteCaveDepthPlace: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useEntrances: () => ({ data: entrances }),
  useCaveTypes: () => ({ data: [] }),
  useRockTypes: () => ({ data: [] }),
  useEntranceTypes: () => ({ data: [{ id: 1, code: 'shaft', name: 'Shaft' }] }),
  useDeleteCave: () => ({ mutateAsync: vi.fn() }),
  useDeleteEntrance: () => ({ mutateAsync: vi.fn() }),
  useUpdateCave: () => ({ mutateAsync: vi.fn() }),
  useUpdateEntrance: () => ({ mutateAsync: vi.fn() }),
  useCan: () => can,
}));

// The page is a shell around a dozen independent sections, none of which this is about. Each is
// stubbed to nothing so a failure here can only be the table's.
// (each factory is written out because vi.mock calls are hoisted above any const they would share)
vi.mock('../../components/attachments/AttachmentSection.tsx', () => ({ default: () => null }));
vi.mock('../../components/history/HistoryPanel.tsx', () => ({ default: () => null }));
vi.mock('../../components/permissions/PermissionsModal.tsx', () => ({
  default: (props: { open: boolean; onClose: () => void }) => {
    permissionsModal(props);
    return null;
  },
}));
vi.mock('../../components/reslinks/LinksSection.tsx', () => ({ default: () => null }));
vi.mock('../../components/statistics/CaveCrossSectionPanel.tsx', () => ({ default: () => null }));
vi.mock('../../components/statistics/CavePatternPanel.tsx', () => ({ default: () => null }));
vi.mock('../../components/statistics/CaveHypsometryPanel.tsx', () => ({ default: () => null }));
vi.mock('../../components/statistics/CaveOverburdenPanel.tsx', () => ({ default: () => null }));
vi.mock('../../components/statistics/CaveOrientationPanel.tsx', () => ({ default: () => null }));
vi.mock('../../components/statistics/CaveStatisticsPanel.tsx', () => ({ default: () => null }));
vi.mock('../../components/statistics/CaveStructurePanel.tsx', () => ({ default: () => null }));
vi.mock('../../components/statistics/CaveTopologyPanel.tsx', () => ({ default: () => null }));
vi.mock('../../components/statistics/TripStatisticsPanel.tsx', () => ({ default: () => null }));
vi.mock('../../components/shares/ShareLinksModal.tsx', () => ({ default: () => null }));
vi.mock('../../components/qr/QrPublicationModal.tsx', () => ({ default: () => null }));
vi.mock('../../components/tags/TagChips.tsx', () => ({ default: () => null }));
vi.mock('../../components/caves/EntranceEditorModal.tsx', () => ({ default: () => null }));
vi.mock('./CaveClosestApproachSection.tsx', () => ({ default: () => null }));
vi.mock('./CaveTripsSection.tsx', () => ({ default: () => null }));
vi.mock('./CenterlineSection.tsx', () => ({ default: () => null }));
vi.mock('./SurveyModelSection.tsx', () => ({ default: () => null }));
vi.mock('./SurveySourceSection.tsx', () => ({ default: () => null }));
vi.mock('./SurveyQualityPanel.tsx', () => ({ default: () => null }));
vi.mock('./CaveExternalIdsSection.tsx', () => ({ default: () => null }));

const { default: CaveDetailPage } = await import('./CaveDetailPage.tsx');

function cave(): CaveDetail {
  return {
    id: 'cave-1',
    name: 'Test cave',
    parents: [],
    visibility: 'internal',
    approximateLocation: false,
    properties: {},
  } as unknown as CaveDetail;
}

function entrance(id: string, lon: number, lat: number, approximate: boolean): Entrance {
  return {
    id,
    caveId: 'cave-1',
    name: `Entrance ${id}`,
    entranceTypeId: 1,
    isMain: false,
    approximateLocation: approximate,
    positionQuality: 'gps',
    geom: { type: 'Point', coordinates: [lon, lat] },
  } as unknown as Entrance;
}

function show() {
  return render(
    <MemoryRouter>
      <CaveDetailPage />
    </MemoryRouter>,
  );
}

/** The address the page is at, as the router sees it, so a test can watch the page change it. */
function LocationProbe() {
  return <span data-testid="location-search">{useLocation().search}</span>;
}

function showAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <CaveDetailPage />
      <LocationProbe />
    </MemoryRouter>,
  );
}

/** The permissions dialog's props as of its latest render. */
function permissionsDialog(): { open: boolean; onClose: () => void } {
  return permissionsModal.mock.lastCall![0] as { open: boolean; onClose: () => void };
}

/** The row for one entrance, found by the name in its first cell. */
function row(name: string) {
  return screen.getByText(name).closest('tr') as HTMLElement;
}

beforeEach(() => {
  navigate.mockClear();
  fitGeoJsonGeometry.mockClear();
  setSelection.mockClear();
  permissionsModal.mockClear();
  can = false;
});

afterEach(cleanup);

describe('CaveDetailPage entrances table', () => {
  it('frames a surveyed entrance at the ordinary zoom and goes to the map', () => {
    entrances = [entrance('a', 22.85, 46.55, false)];
    show();

    fireEvent.click(within(row('Entrance a')).getByRole('button', { name: 'Show on map' }));

    expect(setSelection).toHaveBeenCalledWith({
      kind: 'entrance',
      entranceId: 'a',
      caveId: 'cave-1',
    });
    // undefined, not a number: the caller declines to choose, so the framing helper's own
    // close-up default applies. Asserting the value here would restate that default in a second
    // place and let the two drift.
    expect(fitGeoJsonGeometry).toHaveBeenCalledWith(
      { type: 'Point', coordinates: [22.85, 46.55] },
      undefined,
    );
    expect(navigate).toHaveBeenCalledWith('/map');
  });

  it('frames an approximate entrance loosely instead', () => {
    entrances = [entrance('b', 22.85, 46.55, true)];
    show();

    fireEvent.click(within(row('Entrance b')).getByRole('button', { name: 'Show on map' }));

    expect(fitGeoJsonGeometry).toHaveBeenCalledWith(expect.anything(), 12);
    expect(navigate).toHaveBeenCalledWith('/map');
  });

  it('marks the approximate row and leaves the surveyed one unmarked', () => {
    entrances = [entrance('a', 22.85, 46.55, false), entrance('b', 23.6, 46.77, true)];
    show();

    expect(within(row('Entrance b')).getByText('approx.')).toBeInTheDocument();
    expect(within(row('Entrance a')).queryByText('approx.')).toBeNull();
  });

  it('offers the map button on an approximate row rather than withholding it', () => {
    // Hiding it would protect nothing: the coordinate is printed in the same cell the button sits
    // in. The honest control is one that goes there and says how well the place is known.
    entrances = [entrance('b', 22.85, 46.55, true)];
    show();

    expect(within(row('Entrance b')).getByRole('button', { name: 'Show on map' })).toBeEnabled();
  });
});

/**
 * The permissions dialog has an address, so a message about granting access to this cave can
 * link to where the grant is made. What is asserted is the pair that makes an address safe: it
 * opens the dialog only for somebody who may use it, and closing hands back a clean address.
 */
describe('CaveDetailPage permissions address', () => {
  it('opens the permissions dialog from its address for a caller who may manage them', () => {
    can = true;
    showAt('/caves/cave-1?permissions=1');

    expect(permissionsDialog().open).toBe(true);
  });

  it('leaves the address inert for a caller who may not', () => {
    can = false;
    showAt('/caves/cave-1?permissions=1');

    expect(permissionsModal.mock.calls.every(([props]) => props.open === false)).toBe(true);
    expect(screen.queryByRole('button', { name: /Permissions/ })).toBeNull();
  });

  it('clears the address when the dialog closes, and stays shut', () => {
    can = true;
    showAt('/caves/cave-1?permissions=1');
    expect(screen.getByTestId('location-search').textContent).toBe('?permissions=1');

    act(() => permissionsDialog().onClose());

    expect(screen.getByTestId('location-search').textContent).toBe('');
    expect(permissionsDialog().open).toBe(false);
  });

  it('opens from the lock button without touching the address', () => {
    can = true;
    showAt('/caves/cave-1');

    fireEvent.click(screen.getByRole('button', { name: /Permissions/ }));

    expect(permissionsDialog().open).toBe(true);
    expect(screen.getByTestId('location-search').textContent).toBe('');
  });
});
