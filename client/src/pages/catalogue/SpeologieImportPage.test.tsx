// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
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

vi.mock('../../api/hooks.ts', () => ({
  useSpeologieStatus: () => ({
    data: { configured: true, maxPageSize: 50, maxSelection: 100, portalUrl: 'https://www.speologie.org' },
  }),
  useSpeologieCaves: (requested: readonly number[]) =>
    requested.map((id) => ({
      data: [fresh, known].find((c) => c.id === id),
      isPending: false,
      error: undefined,
    })),
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
  beforeEach(() => {
    ids = '63,64';
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

    await waitFor(() => expect(screen.getByText(/You may not change that cave/)).toBeTruthy());
  });
});
