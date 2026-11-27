// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { SpeologieCave } from '../../api/hooks.ts';

const fresh: SpeologieCave = {
  id: 63,
  title: 'Sistemul Vărășoaia',
  slug: 'V5',
  url: 'https://www.speologie.org/V5',
  county: 'BH',
  locality: 'Padiş',
  mountain: 'bihor',
  length: 28000,
  depth: 653,
  negativeDepth: 653,
  altitude: 1367,
  protectionClass: 'B',
  science: null,
  rockCode: '00',
  sump: true,
  vanished: false,
  protectedAreaCode: null,
  hydroNumber: '21',
  hydroBasinId: 605,
  hydroBasin: {
    id: 605,
    parentId: 604,
    name: '3440 - Bazinul Padiş',
    label: 'Bazinul Padiş',
    path: 'Munţii Apuseni › Munţii Bihorului › Bazinele închise şi platourile înalte › Bazinul Padiş',
    depth: 4,
  },
  description: 'A converted description.',
  alreadyImported: false,
  existingCaveId: null,
};

const known: SpeologieCave = {
  ...fresh,
  id: 64,
  title: 'Peștera Urșilor',
  slug: 'pestera-ursilor',
  url: 'https://www.speologie.org/pestera-ursilor',
  alreadyImported: true,
  existingCaveId: 'cave-uuid-1',
};

let ids = '63,64';
const commitMutate = vi.fn();
const navigate = vi.fn();
/** Set by the mocked map so a test can drive a placement without a real OpenLayers canvas. */
let placeHandler: ((id: number, position: [number, number]) => void) | null = null;

/** How the status read answers: configured, not configured, or failed. */
let statusAnswer: 'configured' | 'unconfigured' | 'failed' = 'configured';
const refetchStatus = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  useSpeologieStatus: () =>
    statusAnswer === 'failed'
      ? { data: undefined, isError: true, error: new TypeError('Failed to fetch'), refetch: refetchStatus }
      : {
          data: {
            configured: statusAnswer === 'configured',
            maxPageSize: 50,
            maxSelection: 100,
            portalUrl: 'https://www.speologie.org',
          },
          isError: false,
          error: null,
          refetch: refetchStatus,
        },
  useSpeologieCaves: (requested: readonly number[]) =>
    requested.map((id) =>
      statusAnswer === 'configured'
        ? { data: [fresh, known].find((c) => c.id === id), isPending: false, isLoading: false, error: undefined }
        : // A read that is never sent: pending for good, and never loading.
          { data: undefined, isPending: true, isLoading: false, error: undefined },
    ),
  useCaveTypes: () => ({ data: [{ id: 1, code: 'cave', name: 'Cave' }, { id: 2, code: 'pit', name: 'Pit / Aven' }] }),
  useCavingGroups: () => ({ data: [], isLoading: false }),
  useImportFromSpeologie: () => ({ mutateAsync: commitMutate, isPending: false }),
}));

// The placement map builds a real OpenLayers instance against a canvas jsdom does not have. The
// screen is a table beside a map and this file is about the table, so the map is replaced by a
// button that reports a click at a fixed position.
vi.mock('../../components/catalogue/CataloguePlacementMap.tsx', () => ({
  default: (props: { placingId: number | null; onPlace: (id: number, p: [number, number]) => void }) => {
    placeHandler = props.onPlace;
    return <div data-testid="catalogue-placement-map" data-placing={String(props.placingId)} />;
  },
}));

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return {
    ...actual,
    useNavigate: () => navigate,
    useSearchParams: () => [new URLSearchParams(ids ? `ids=${ids}` : ''), vi.fn()],
  };
});

const { default: SpeologieImportPage } = await import('./SpeologieImportPage.tsx');

function show() {
  return render(
    <MemoryRouter>
      <App>
        <SpeologieImportPage />
      </App>
    </MemoryRouter>,
  );
}

afterEach(cleanup);

describe('SpeologieImportPage', () => {
  it('keeps the page title and frame when nothing was chosen to import', () => {
    ids = '';
    show();
    expect(screen.getByRole('heading', { level: 3, name: 'Import from the catalogue' })).toBeInTheDocument();
    expect(
      screen.getByText('Nothing was chosen. Go back to the search and pick the caves to import.'),
    ).toBeInTheDocument();
    ids = '63,64';
  });

  beforeEach(() => {
    ids = '63,64';
    statusAnswer = 'configured';
    refetchStatus.mockReset();
    placeHandler = null;
    navigate.mockReset();
    commitMutate.mockReset();
    commitMutate.mockResolvedValue({
      batchId: 'batch-1',
      createdCount: 1,
      updatedCount: 1,
      skippedCount: 0,
      failures: [],
    });
  });

  it('says the installation has no catalogue key, rather than spinning on reads it never sends', () => {
    statusAnswer = 'unconfigured';
    show();

    expect(screen.getByTestId('speologie-not-configured')).toBeTruthy();
    expect(document.querySelector('.ant-table .ant-spin-spinning')).toBeNull();
  });

  it('says the status could not be read, with a way to ask again, rather than spinning', () => {
    statusAnswer = 'failed';
    show();

    expect(screen.getByTestId('speologie-status-failed')).toBeTruthy();
    expect(document.querySelector('.ant-table .ant-spin-spinning')).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(refetchStatus).toHaveBeenCalledTimes(1);
  });

  it('says plainly that the catalogue carries no coordinates', () => {
    show();
    expect(screen.getByTestId('speologie-no-coordinates')).toBeTruthy();
  });

  it('asks for nothing when the address names no caves', () => {
    ids = '';
    show();
    expect(screen.queryByTestId('speologie-candidates')).toBeNull();
    expect(screen.getByText(/Nothing was chosen/)).toBeTruthy();
  });

  it('proposes creating a new cave and refreshing one already here', () => {
    show();
    expect(screen.getByTestId('speologie-action-63').textContent).toContain('Create');
    expect(screen.getByTestId('speologie-action-64').textContent).toContain('Refresh');
  });

  it('imports every chosen cave, unplaced, with the decisions it was shown', async () => {
    show();
    fireEvent.click(screen.getByTestId('speologie-commit'));

    await waitFor(() => expect(commitMutate).toHaveBeenCalledTimes(1));
    const body = commitMutate.mock.calls[0][0];

    expect(body.selection).toEqual([63, 64]);
    expect(body.decisions['63'].action).toBe('create');
    expect(body.decisions['64'].action).toBe('update');
    // Nobody placed anything, so nothing claims a position it does not have.
    expect(body.decisions['63'].longitude).toBeNull();
    expect(body.decisions['63'].latitude).toBeNull();
    // Most restrictive unless somebody says otherwise.
    expect(body.visibility).toBe('private');
    expect(body.locationProtected).toBe(false);
  });

  it('sends a position only for the cave that was placed on the map', async () => {
    show();

    fireEvent.click(screen.getByTestId('speologie-place-63'));
    expect(screen.getByTestId('catalogue-placement-map').getAttribute('data-placing')).toBe('63');

    // The map is mocked, so the placement is delivered the way the real map delivers it.
    act(() => placeHandler?.(63, [22.6, 46.55]));

    fireEvent.click(screen.getByTestId('speologie-commit'));
    await waitFor(() => expect(commitMutate).toHaveBeenCalledTimes(1));

    const body = commitMutate.mock.calls[0][0];
    expect(body.decisions['63'].longitude).toBeCloseTo(22.6);
    expect(body.decisions['63'].latitude).toBeCloseTo(46.55);
    expect(body.decisions['64'].longitude).toBeNull();
  });

  it('keeps refreshing a cave already here when it is placed or given a cave type', async () => {
    show();

    // A placement on the already-imported row is the first change it gets.
    fireEvent.click(screen.getByTestId('speologie-place-64'));
    act(() => placeHandler?.(64, [22.7, 46.6]));

    // Then a cave type, picked from the row's own select.
    const typeSelect = screen.getByTestId('speologie-cave-type-64');
    fireEvent.mouseDown(typeSelect.querySelector('.ant-select-selector') ?? typeSelect);
    fireEvent.click(await screen.findByTitle('Pit / Aven'));

    // The row still says it refreshes the cave rather than offering a disabled "create".
    expect(screen.getByTestId('speologie-action-64').textContent).toContain('Refresh');

    fireEvent.click(screen.getByTestId('speologie-commit'));
    await waitFor(() => expect(commitMutate).toHaveBeenCalledTimes(1));

    const body = commitMutate.mock.calls[0][0];
    expect(body.decisions['64']).toEqual({
      action: 'update',
      caveTypeCode: 'pit',
      longitude: 22.7,
      latitude: 46.6,
    });
    // The untouched new row keeps its own proposal.
    expect(body.decisions['63'].action).toBe('create');
  });

  it('keeps refreshing a cave already here when its position is cleared again', async () => {
    show();

    act(() => placeHandler?.(64, [22.7, 46.6]));
    fireEvent.click(screen.getByText('Remove the position'));

    fireEvent.click(screen.getByTestId('speologie-commit'));
    await waitFor(() => expect(commitMutate).toHaveBeenCalledTimes(1));

    const body = commitMutate.mock.calls[0][0];
    expect(body.decisions['64'].action).toBe('update');
    expect(body.decisions['64'].longitude).toBeNull();
  });

  it('shows what the import did, and points at the caves that arrived without a position', async () => {
    show();
    fireEvent.click(screen.getByTestId('speologie-commit'));

    await waitFor(() => expect(screen.getByText('The import is done')).toBeTruthy());

    fireEvent.click(screen.getByText('See the caves'));
    expect(navigate).toHaveBeenCalledWith('/caves?unplaced=true');
  });

  it('lists what could not be imported rather than summarising it', async () => {
    commitMutate.mockResolvedValue({
      batchId: 'batch-1',
      createdCount: 1,
      updatedCount: 0,
      skippedCount: 0,
      failures: [
        {
          speologieId: 64,
          title: 'Peștera Urșilor',
          code: 'speologie.update_forbidden',
          reason: 'You may not change that cave.',
        },
      ],
    });
    show();
    fireEvent.click(screen.getByTestId('speologie-commit'));

    // The row's code is worded in the reader's language rather than repeating the server's sentence.
    await waitFor(() => expect(screen.getByText(/You may not change the cave this entry was imported as/)).toBeTruthy());
  });

  it('words a refused row from its code, and falls back to the server sentence for a code it does not know', async () => {
    commitMutate.mockResolvedValue({
      batchId: 'batch-1',
      createdCount: 0,
      updatedCount: 0,
      skippedCount: 0,
      failures: [
        { speologieId: 63, title: 'Sistemul Vărășoaia', code: 'speologie.position_invalid', reason: 'Server sentence A.' },
        { speologieId: 64, title: 'Peștera Urșilor', code: 'feature.something_new', reason: 'Server sentence B.' },
      ],
    });
    show();
    fireEvent.click(screen.getByTestId('speologie-commit'));

    await waitFor(() => expect(screen.getByText(/The position given for this cave is not on the earth/)).toBeTruthy());
    expect(screen.queryByText(/Server sentence A/)).toBeNull();
    expect(screen.getByText(/Server sentence B/)).toBeTruthy();
  });

  it('says an account may not create caves when the import is refused for that, not that the catalogue is down', async () => {
    commitMutate.mockRejectedValue(new ApiError(403, 'access.create_forbidden', 'An English sentence.'));
    show();
    fireEvent.click(screen.getByTestId('speologie-commit'));

    expect(await screen.findByText(/Your account may not create caves/)).toBeTruthy();
    expect(screen.queryByText(/The catalogue could not be reached/)).toBeNull();
  });

  it('reports an import that failed for no reason it can name as a failed import', async () => {
    commitMutate.mockRejectedValue(new ApiError(500));
    show();
    fireEvent.click(screen.getByTestId('speologie-commit'));

    expect(await screen.findByText('The import could not be completed.')).toBeTruthy();
    expect(screen.queryByText(/The catalogue could not be reached/)).toBeNull();
  });
});
