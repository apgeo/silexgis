// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { CaveDetail, CaveSummary, Entrance } from '../../api/hooks.ts';
import { takePendingReveal3d } from '../../scene3d/pendingReveal3d.ts';

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
// What this cave's own summary answers about the caller, or nothing while it is still on its way.
let summary: CaveSummary | undefined;

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
  useCaveSummary: () => ({ data: summary }),
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
  default: (props: PermissionsDialogProps) => {
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

/** What the page hands the permissions dialog. */
interface PermissionsDialogProps {
  open: boolean;
  onClose: () => void;
  grantTo?: string | null;
}

/** The cave's own answer about the caller: whether they may manage its permissions, and nothing else. */
function summaryAnswering(canManagePermissions: boolean): CaveSummary {
  return {
    entranceCount: 0,
    centerlineCount: 0,
    surveyModelCount: 0,
    attachmentCount: 0,
    tripLogCount: 0,
    permissions: {
      canWrite: false,
      canDelete: false,
      canShare: false,
      canManagePermissions,
    },
  } as unknown as CaveSummary;
}

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
function permissionsDialog(): PermissionsDialogProps {
  return permissionsModal.mock.lastCall![0] as PermissionsDialogProps;
}

/** Whether the dialog was drawn open at any point so far, however briefly. */
function dialogEverOpened(): boolean {
  return permissionsModal.mock.calls.some(([props]) => (props as PermissionsDialogProps).open);
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
  summary = undefined;
});

afterEach(cleanup);

describe('CaveDetailPage open in 3D', () => {
  it('selects the cave, leaves the scene a place to fly to, and goes there', () => {
    entrances = [];
    show();

    fireEvent.click(screen.getByRole('button', { name: /Open in 3D/ }));

    // The scene loads the walls of whatever is selected, so the selection is set before the page
    // is left; the camera's destination waits in the one-shot slot because the scene is not
    // mounted yet and nothing else could carry it there.
    expect(setSelection).toHaveBeenCalledWith({ kind: 'cave', caveId: 'cave-1' });
    expect(takePendingReveal3d()).toEqual({
      targetType: 'feature',
      targetId: 'cave-1',
      label: 'Test cave',
    });
    expect(navigate).toHaveBeenCalledWith('/map3d');
  });
});

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
  // An account's id, shaped as an address carries one.
  const ana = '0b6f2c1e-5a51-4c0e-9d1b-3f6a8a2d7c11';

  it('opens the permissions dialog from its address for a caller who may manage them', () => {
    summary = summaryAnswering(true);
    showAt('/caves/cave-1?permissions=1');

    expect(permissionsDialog().open).toBe(true);
  });

  it('leaves the address inert for a caller who may not', () => {
    summary = summaryAnswering(false);
    showAt('/caves/cave-1?permissions=1');

    expect(dialogEverOpened()).toBe(false);
    expect(screen.queryByRole('button', { name: /Permissions/ })).toBeNull();
  });

  it('leaves the address inert for a caller who holds the right over caves in general but not over this one', () => {
    // The coarse capability bridges the buttons until the cave's own answer arrives, and it is
    // the wrong question for an address: an editor of caves at large is not thereby somebody
    // who may hand out access to this one. Opening on it would draw the dialog, have its rules
    // refused, and shut it again a moment later.
    can = true;
    summary = summaryAnswering(false);
    showAt(`/caves/cave-1?permissions=1&grantTo=${ana}`);

    expect(dialogEverOpened()).toBe(false);
  });

  it('waits for the cave\'s own answer rather than opening on the coarse capability', () => {
    can = true;
    const view = showAt('/caves/cave-1?permissions=1');
    expect(dialogEverOpened()).toBe(false);

    summary = summaryAnswering(true);
    view.rerender(
      <MemoryRouter initialEntries={['/caves/cave-1?permissions=1']}>
        <CaveDetailPage />
        <LocationProbe />
      </MemoryRouter>,
    );

    expect(permissionsDialog().open).toBe(true);
  });

  it('clears the address when the dialog closes, and stays shut', () => {
    summary = summaryAnswering(true);
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

  it('hands the dialog the account the address was written about', () => {
    summary = summaryAnswering(true);
    showAt(`/caves/cave-1?permissions=1&grantTo=${ana}`);

    expect(permissionsDialog().open).toBe(true);
    expect(permissionsDialog().grantTo).toBe(ana);
  });

  it('clears the account from the address with the dialog, and leaves what is not its own alone', () => {
    // An address copied after the dialog has been dealt with should not still carry somebody's
    // account in it, and whatever else the address said was not this dialog's to remove.
    summary = summaryAnswering(true);
    showAt(`/caves/cave-1?from=inbox&permissions=1&grantTo=${ana}`);

    act(() => permissionsDialog().onClose());

    expect(screen.getByTestId('location-search').textContent).toBe('?from=inbox');
    expect(permissionsDialog().open).toBe(false);
    expect(permissionsDialog().grantTo).toBeNull();
  });

  it('reads the account only beside the dialog\'s own parameter', () => {
    // Alone it opens nothing and names nobody: the dialog reached from the lock button is the
    // ordinary one, and closing it tidies the stray parameter away.
    can = true;
    summary = summaryAnswering(true);
    showAt(`/caves/cave-1?grantTo=${ana}`);
    expect(dialogEverOpened()).toBe(false);

    fireEvent.click(screen.getByRole('button', { name: /Permissions/ }));
    expect(permissionsDialog().open).toBe(true);
    expect(permissionsDialog().grantTo).toBeNull();

    act(() => permissionsDialog().onClose());
    expect(screen.getByTestId('location-search').textContent).toBe('');
  });
});
